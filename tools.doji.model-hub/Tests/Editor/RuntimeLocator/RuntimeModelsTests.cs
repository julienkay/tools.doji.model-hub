using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the runtime API: finding models in the registry, loading bundled and Editor models, downloading into the
    /// device cache, and converting downloaded ONNX files once.
    /// </summary>
    internal sealed class RuntimeModelsTests
    {
        private const string Repo = "doji/runtime-test";
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";

        private TestCacheScope _cacheScope;
        private TestProjectStateScope _projectScope;
        private string _rootDirectory;
        private string _sentisPath;
        private int _downloadCount;
        private readonly List<string> _downloadedUrls = new();

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.RuntimeModelsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _projectScope = new TestProjectStateScope();
            _sentisPath = Path.Combine(_rootDirectory, "sample.sentis");
            ModelWriter.Save(_sentisPath, new EditorOnnxModelConverter().Convert(new OnnxConversionRequest(GetSampleOnnxPath())).Model);
            CacheFiles.ForceSymlinkSupport = false;
            _downloadCount = 0;
            _downloadedUrls.Clear();
            RuntimeDownloader.DownloadOverride = (url, target, progress, token) =>
            {
                _downloadCount++;
                _downloadedUrls.Add(url);
                var source = url.EndsWith(".onnx", StringComparison.Ordinal) ? GetSampleOnnxPath()
                    : url.EndsWith(".sentis", StringComparison.Ordinal) ? _sentisPath
                    : null;
                if (source != null)
                {
                    File.Copy(source, target, true);
                }
                else
                {
                    File.WriteAllBytes(target, new byte[4]);
                }

                return Task.CompletedTask;
            };
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeModels.RegistryOverride = null;
            RuntimeModels.ResetForTests();
            RuntimeDownloader.DownloadOverride = null;
            Hub.RegisterOnnxConverter(null, null);
            CacheFiles.ForceSymlinkSupport = null;
            _projectScope?.Dispose();
            _cacheScope?.Dispose();
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies a bundled model loads from StreamingAssets, where the build puts it.
        /// </summary>
        [Test]
        public async Task LoadModelAsync_LoadsABundledModelFromStreamingAssets()
        {
            var relativePath = Hub.GetRuntimeModelRelativePath(Repo, "onnx/model.onnx");
            var bundledPath = Hub.GetRuntimeAbsolutePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledPath));
            File.Copy(_sentisPath, bundledPath);
            UseRegistry(Model("bundle", new RuntimeModelFile { repoPath = "onnx/model.onnx", format = "onnx", relativePath = relativePath }));

            var model = await Hub.LoadModelAsync(Repo, "onnx/model.onnx");

            Assert.That(model, Is.Not.Null);
            Assert.That(Hub.LoadModel(Repo, "onnx/model.onnx"), Is.Not.Null);
            Assert.That(_downloadCount, Is.Zero);
        }

        /// <summary>
        /// Verifies the Editor loads the runtime model on this machine, and explains a missing import.
        /// </summary>
        [Test]
        public void LoadModel_UsesTheEditorLocalPath()
        {
            UseRegistry(Model("bundle", new RuntimeModelFile { repoPath = "a.onnx", format = "onnx", localPath = _sentisPath }));

            Assert.That(Hub.LoadModel(Repo, "a.onnx"), Is.Not.Null);

            UseRegistry(Model("bundle", new RuntimeModelFile { repoPath = "a.onnx", format = "onnx", localPath = Path.Combine(_rootDirectory, "missing.sentis") }));
            var exception = Assert.Throws<FileNotFoundException>(() => Hub.LoadModel(Repo, "a.onnx"));
            Assert.That(exception.Message, Does.Contain("not set up"));
        }

        /// <summary>
        /// Verifies a downloaded Sentis model goes into the cache at the pinned commit once, with its ref, and later
        /// loads need no download.
        /// </summary>
        [Test]
        public async Task LoadModelAsync_DownloadsASentisModelOnce()
        {
            UseRegistry(Model("download", new RuntimeModelFile
            {
                repoPath = "sentis/model.sentis",
                format = "sentis",
                source = new RuntimeSourceFile { repoPath = "sentis/model.sentis", etag = "\"etag-sentis\"", size = new FileInfo(_sentisPath).Length }
            }));

            var first = await Hub.LoadModelAsync(Repo, "sentis/model.sentis");
            var second = await Hub.LoadModelAsync(Repo, "sentis/model.sentis");

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(_downloadCount, Is.EqualTo(1));
            Assert.That(_downloadedUrls.Single(), Is.EqualTo($"https://huggingface.co/{Repo}/resolve/{Commit}/sentis/model.sentis"));
            Assert.That(File.Exists(CachePaths.GetSnapshotFilePath(Repo, Commit, "sentis/model.sentis")), Is.True);
            Assert.That(File.ReadAllText(CachePaths.GetRevisionRefPath(Repo, "main")), Is.EqualTo(Commit));
            Assert.That(Hub.LoadModel(Repo, "sentis/model.sentis"), Is.Not.Null, "Once downloaded, it loads without waiting.");
        }

        /// <summary>
        /// Verifies downloads go into the folder of the cache spelling of the id, while lookups use the package's id.
        /// </summary>
        [Test]
        public async Task LoadModelAsync_DownloadsIntoTheCacheSpellingOfTheId()
        {
            var entry = Model("download", new RuntimeModelFile
            {
                repoPath = "sentis/model.sentis",
                format = "sentis",
                source = new RuntimeSourceFile { repoPath = "sentis/model.sentis", etag = "etag-sentis" }
            });
            entry.cacheRepoId = "doji/Runtime-Test";
            UseRegistry(entry);

            await Hub.LoadModelAsync(Repo, "sentis/model.sentis");

            Assert.That(
                Directory.GetDirectories(_cacheScope.CacheRoot).Select(Path.GetFileName),
                Does.Contain("models--doji--Runtime-Test"));
            Assert.That(_downloadedUrls.Single(), Does.Contain("/doji/Runtime-Test/resolve/"));
        }

        /// <summary>
        /// Verifies a downloaded ONNX model brings its companion files, is converted once with the build's settings,
        /// and is found by its conversion key later.
        /// </summary>
        [Test]
        public async Task LoadModelAsync_DownloadsAndConvertsAnOnnxModelOnce()
        {
            var converter = new CountingConverter();
            Hub.RegisterOnnxConverter(converter, "test-converter@1");
            var input = new EditorOnnxModelConverter().Convert(new OnnxConversionRequest(GetSampleOnnxPath())).Model.inputs[0];
            var dimensions = Enumerable.Repeat(5, input.shape.rank).ToArray();
            UseRegistry(Model("download", new RuntimeModelFile
            {
                repoPath = "onnx/model.onnx",
                format = "onnx",
                source = new RuntimeSourceFile { repoPath = "onnx/model.onnx", etag = "etag-onnx", size = new FileInfo(GetSampleOnnxPath()).Length },
                companions = new[] { new RuntimeSourceFile { repoPath = "onnx/model.onnx_data", etag = "etag-data", size = 4 } },
                inputShapes = new[] { new RuntimeInputShape { inputName = input.name, dimensions = dimensions } },
                quantization = "none",
                recipeHash = "recipe-1"
            }));

            var first = await Hub.LoadModelAsync(Repo, "onnx/model.onnx");
            var second = await Hub.LoadModelAsync(Repo, "onnx/model.onnx");

            Assert.That(converter.Count, Is.EqualTo(1));
            Assert.That(_downloadCount, Is.EqualTo(2), "The model and its companion file, once each.");
            Assert.That(File.Exists(CachePaths.GetSnapshotFilePath(Repo, Commit, "onnx/model.onnx_data")), Is.True);
            var key = ConvertedModelStore.ComputeKey("etag-onnx", "test-converter@1", "recipe-1");
            Assert.That(File.Exists(ConvertedModelStore.GetModelPath(Repo, key)), Is.True);
            Assert.That(first.inputs[0].shape.ToTensorShape().ToArray(), Is.EqualTo(dimensions));
            Assert.That(second.inputs[0].shape.ToTensorShape().ToArray(), Is.EqualTo(dimensions));
        }

        /// <summary>
        /// Verifies a converter registered without an id uses the id the build recorded in the registry.
        /// </summary>
        [Test]
        public async Task LoadModelAsync_UsesTheRegistryConverterIdForTheBuiltInConverter()
        {
            RuntimeModels.OnnxConverter = new CountingConverter();
            var registry = UseRegistry(Model("download", new RuntimeModelFile
            {
                repoPath = "onnx/model.onnx",
                format = "onnx",
                source = new RuntimeSourceFile { repoPath = "onnx/model.onnx", etag = "etag-onnx" },
                recipeHash = "recipe-1"
            }));
            registry.onnxConverterId = "built-in@2";

            await Hub.LoadModelAsync(Repo, "onnx/model.onnx");

            Assert.That(File.Exists(ConvertedModelStore.GetModelPath(Repo, ConvertedModelStore.ComputeKey("etag-onnx", "built-in@2", "recipe-1"))), Is.True);
        }

        /// <summary>
        /// Verifies a downloaded ONNX model without a converter explains how to fix it.
        /// </summary>
        [Test]
        public void LoadModelAsync_ExplainsAMissingOnnxConverter()
        {
            UseRegistry(Model("download", new RuntimeModelFile { repoPath = "a.onnx", format = "onnx", source = new RuntimeSourceFile { repoPath = "a.onnx", etag = "e" } }));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(() => Hub.LoadModelAsync(Repo, "a.onnx"));

            Assert.That(exception.Message, Does.Contain("tools.doji.onnx-model-loader"));
            Assert.That(_downloadCount, Is.Zero, "Nothing is downloaded when it could not be converted.");
        }

        /// <summary>
        /// Verifies loading without waiting points to the async API when a model is not on the device yet.
        /// </summary>
        [Test]
        public void LoadModel_PointsToTheAsyncApiBeforeADownload()
        {
            UseRegistry(Model("download", new RuntimeModelFile { repoPath = "a.sentis", format = "sentis", source = new RuntimeSourceFile { repoPath = "a.sentis", etag = "e" } }));

            var exception = Assert.Throws<InvalidOperationException>(() => Hub.LoadModel(Repo, "a.sentis"));

            Assert.That(exception.Message, Does.Contain("LoadModelAsync"));
        }

        /// <summary>
        /// Verifies a loading screen can fetch every file of a model up front.
        /// </summary>
        [Test]
        public async Task EnsureAvailableAsync_DownloadsEveryFile()
        {
            UseRegistry(Model(
                "download",
                new RuntimeModelFile { repoPath = "a.sentis", format = "sentis", source = new RuntimeSourceFile { repoPath = "a.sentis", etag = "e1" } },
                new RuntimeModelFile { repoPath = "b.sentis", format = "sentis", source = new RuntimeSourceFile { repoPath = "b.sentis", etag = "e2" } }));

            await Hub.EnsureAvailableAsync(Repo);
            await Hub.EnsureAvailableAsync(Repo);

            Assert.That(_downloadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies lookups explain unknown models and files, and a package id picks that package's entry.
        /// </summary>
        [Test]
        public void Lookup_ExplainsUnknownModelsAndUsesThePackageId()
        {
            var registry = new RuntimeRegistry
            {
                packages = new[]
                {
                    Package("tools.doji.a", Model("bundle", new RuntimeModelFile { repoPath = "a.onnx", format = "onnx", localPath = _sentisPath })),
                    Package("tools.doji.b", Model("bundle", new RuntimeModelFile { repoPath = "b.onnx", format = "onnx", localPath = _sentisPath }))
                }
            };
            RuntimeModels.RegistryOverride = registry;

            Assert.That(Hub.FromPretrained("tools.doji.b", Repo).Files, Is.EqualTo(new[] { "b.onnx" }));
            Assert.That(Hub.LoadModel(Repo, "b.onnx"), Is.Not.Null, "Without a package id, the entry that has the file is used.");
            Assert.That(Assert.Throws<FileNotFoundException>(() => Hub.LoadModel(Repo, "c.onnx")).Message, Does.Contain("a.onnx, b.onnx"));
            Assert.That(Assert.Throws<FileNotFoundException>(() => Hub.LoadModel("doji/unknown", "a.onnx")).Message, Does.Contain("Add it to a manifest"));
            Assert.That(Assert.Throws<FileNotFoundException>(() => Hub.LoadModel("tools.doji.a", Repo, "b.onnx")).Message, Does.Contain("tools.doji.a"));
        }

        /// <summary>
        /// Verifies the registry keeps its fields through JSON, never writes Editor paths, and refuses a newer version.
        /// </summary>
        [Test]
        public void Registry_RoundTripsWithoutEditorPathsAndRefusesNewerVersions()
        {
            var registry = new RuntimeRegistry
            {
                onnxConverterId = "c@1",
                packages = new[] { Package("tools.doji.a", Model("download", new RuntimeModelFile
                {
                    repoPath = "a.onnx",
                    format = "onnx",
                    relativePath = "model-hub/models/a.sentis",
                    localPath = "C:/secret/path.sentis",
                    source = new RuntimeSourceFile { repoPath = "a.onnx", etag = "e", size = 3 },
                    inputShapes = new[] { new RuntimeInputShape { inputName = "x", dimensions = new[] { 1, -1 } } }
                })) }
            };

            var json = registry.ToJson();
            var copy = RuntimeRegistry.FromJson(json);

            Assert.That(json, Does.Not.Contain("secret"));
            var file = copy.packages.Single().models.Single().files.Single();
            Assert.That(copy.onnxConverterId, Is.EqualTo("c@1"));
            Assert.That(copy.packages.Single().models.Single().Delivery, Is.EqualTo(ModelDelivery.Download));
            Assert.That(file.source.etag, Is.EqualTo("e"));
            Assert.That(file.inputShapes.Single().dimensions, Is.EqualTo(new[] { 1, -1 }));
            Assert.Throws<InvalidOperationException>(() => RuntimeRegistry.FromJson("{\"version\": 99}"));
        }

        /// <summary>
        /// Verifies download URLs escape each path segment and keep the folders.
        /// </summary>
        [Test]
        public void BuildResolveUrl_EscapesSegments()
        {
            Assert.That(
                RuntimeDownloader.BuildResolveUrl("owner/name", "abc", "onnx/my model+1.onnx"),
                Is.EqualTo("https://huggingface.co/owner/name/resolve/abc/onnx/my%20model%2b1.onnx").IgnoreCase);
        }

        /// <summary>
        /// Verifies a package's model list has its included files with display names, and no other package's files.
        /// </summary>
        [Test]
        public void GetModelFiles_ListsTheFilesOfOnePackage()
        {
            var registry = UseRegistry(Model("download",
                new RuntimeModelFile { repoPath = "onnx/small.onnx", label = "Small (63 MB)" },
                new RuntimeModelFile { repoPath = "unet/model.onnx" }));
            registry.packages = registry.packages
                .Append(Package("tools.doji.other", new RuntimeModelEntry { repoId = "doji/other", files = new[] { new RuntimeModelFile { repoPath = "a.sentis" } } }))
                .ToArray();

            var files = Hub.GetModelFiles("tools.doji.test");

            Assert.That(files.Select(file => file.DisplayName), Is.EqualTo(new[] { "Small (63 MB)", "unet/model" }));
            Assert.That(files.Select(file => file.RepoId).Distinct(), Is.EqualTo(new[] { Repo }));
            Assert.That(files[0].Delivery, Is.EqualTo(ModelDelivery.Download));
            Assert.That(Hub.GetModelFiles(null).Count, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies the name a picker shows without a label: the path without its extension and its onnx/ or sentis/
        /// folder.
        /// </summary>
        [TestCase("onnx/midas_v21_small_256.onnx", null, "midas_v21_small_256")]
        [TestCase("sentis/model.sentis", null, "model")]
        [TestCase("unet/model.onnx", null, "unet/model")]
        [TestCase("model.onnx", " ", "model")]
        [TestCase("onnx/model.onnx", "Fast", "Fast")]
        public void DisplayName_FallsBackToThePath(string repoPath, string label, string expected)
        {
            Assert.That(HubModelFile.GetDisplayName(label, repoPath), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies a model reference loads a package's file through the registry, and that a model asset replaces the file.
        /// </summary>
        [Test]
        public async Task HubModelReference_LoadsAPackageFileOrAModelAsset()
        {
            UseRegistry(Model("bundle", new RuntimeModelFile { repoPath = "a.onnx", format = "onnx", localPath = _sentisPath }));
            var file = Hub.GetModelFiles("tools.doji.test").Single().ToReference();

            Assert.That(file.ToString(), Is.EqualTo($"{Repo}:a.onnx"));
            Assert.That(await file.LoadAsync(), Is.Not.Null);
            Assert.That(file.Load(), Is.Not.Null);

            var empty = new HubModelReference();
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(() => empty.Load(), Throws.InvalidOperationException.With.Message.Contains("names no model"));

            var asset = UnityEngine.ScriptableObject.CreateInstance<ModelAsset>();
            try
            {
                var own = new HubModelReference(asset);
                Assert.That(own.IsEmpty, Is.False);
                Assert.That(own.ModelAsset, Is.SameAs(asset));
                Assert.That(own.RepoId, Is.Null, "A model asset wins over a file.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// Verifies a file you got yourself loads: a Sentis file directly, an ONNX file through the Editor converter,
        /// and a missing file fails with its path.
        /// </summary>
        [Test]
        public void LoadModelFromFile_LoadsSentisAndOnnxFiles()
        {
            Assert.That(Hub.LoadModelFromFile(_sentisPath), Is.Not.Null);
            Assert.That(Hub.LoadModelFromFile(GetSampleOnnxPath()).inputs, Is.Not.Empty);

            var missing = Path.Combine(_rootDirectory, "missing.onnx");
            var exception = Assert.Throws<FileNotFoundException>(() => Hub.LoadModelFromFile(missing));
            Assert.That(exception.Message, Does.Contain(missing));
        }

        private RuntimeRegistry UseRegistry(RuntimeModelEntry model)
        {
            var registry = new RuntimeRegistry { packages = new[] { Package("tools.doji.test", model) } };
            RuntimeModels.RegistryOverride = registry;
            return registry;
        }

        private static RuntimePackageEntry Package(string packageId, RuntimeModelEntry model)
        {
            return new RuntimePackageEntry { packageId = packageId, packageVersion = "1.0.0", models = new[] { model } };
        }

        private static RuntimeModelEntry Model(string delivery, params RuntimeModelFile[] files)
        {
            return new RuntimeModelEntry { repoId = Repo, revision = "main", commit = Commit, delivery = delivery, files = files };
        }

        private static string GetSampleOnnxPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly);
            return Path.Combine(packageInfo.resolvedPath, "Samples~", "Run a model", "SingleInputSingleOuput.onnx");
        }

        private sealed class CountingConverter : IOnnxModelConverter
        {
            public int Count { get; private set; }

            public OnnxConversionResult Convert(OnnxConversionRequest request)
            {
                Count++;
                return new EditorOnnxModelConverter().Convert(request);
            }
        }
    }
}
