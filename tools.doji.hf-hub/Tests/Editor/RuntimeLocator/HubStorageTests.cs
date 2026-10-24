using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the cleanup methods applications call themselves: listing and deleting converted models, and deleting
    /// Android copies of other app versions.
    /// </summary>
    internal sealed class HubStorageTests
    {
        private TestCacheScope _cacheScope;

        [SetUp]
        public void SetUp()
        {
            _cacheScope = new TestCacheScope(Path.Combine(Path.GetTempPath(), "hfhub-storage-tests", Guid.NewGuid().ToString("N")));
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeModels.RegistryOverride = null;
            _cacheScope.Dispose();
        }

        /// <summary>
        /// Verifies converted models are listed with what produced them, and a model without a record still shows.
        /// </summary>
        [Test]
        public void GetConvertedModels_ListsModelsWithTheirRecords()
        {
            var withRecord = WriteConverted("doji/a", "k1", "ie-2.2.0", "onnx/a.onnx");
            var withoutRecord = WriteConverted("doji/b", "k2", null, null, writeRecord: false);

            var models = HubStorage.GetConvertedModels();

            Assert.That(models.Select(model => model.ModelPath), Is.EquivalentTo(new[] { withRecord, withoutRecord }));
            var listed = models.Single(model => model.ModelPath == withRecord);
            Assert.That(listed.Repository, Is.EqualTo("doji/a"));
            Assert.That(listed.RepoPath, Is.EqualTo("onnx/a.onnx"));
            Assert.That(listed.FormatGeneration, Is.EqualTo("ie-2.2.0"));
            Assert.That(listed.SizeInBytes, Is.EqualTo(5));
            Assert.That(models.Single(model => model.ModelPath == withoutRecord).Repository, Is.Null);
        }

        /// <summary>
        /// Verifies deleting by generation removes only the matching models, their records, and empty folders.
        /// </summary>
        [Test]
        public void DeleteConvertedModels_DeletesOnlyMatchingModels()
        {
            var old = WriteConverted("doji/a", "old", "before-ie-2.2.0", "onnx/a.onnx");
            var current = WriteConverted("doji/b", "new", "ie-2.2.0", "onnx/b.onnx");
            RuntimeModels.RegistryOverride = new RuntimeRegistry { formatGeneration = "ie-2.2.0" };

            var deleted = HubStorage.DeleteConvertedModels(model => model.FormatGeneration != HubStorage.FormatGeneration);

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(File.Exists(old), Is.False);
            Assert.That(File.Exists(Path.ChangeExtension(old, ".json")), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(old)), Is.False);
            Assert.That(File.Exists(current), Is.True);
        }

        /// <summary>
        /// Verifies a model that is being written is skipped instead of deleted under the writer.
        /// </summary>
        [Test]
        public void DeleteConvertedModels_SkipsModelsBeingWritten()
        {
            var path = WriteConverted("doji/a", "busy", "ie-2.2.0", "onnx/a.onnx");
            Assert.That(BlobLock.TryAcquire(path + ".lock", out var writer), Is.True);
            using (writer)
            {
                Assert.That(HubStorage.DeleteConvertedModels(_ => true), Is.Zero);
            }

            Assert.That(File.Exists(path), Is.True);
        }

        /// <summary>
        /// Verifies only the copies of other app versions are deleted, and nothing happens without copies.
        /// </summary>
        [Test]
        public void DeleteBundledCopiesExcept_KeepsTheRunningVersion()
        {
            Assert.That(HubStorage.DeleteBundledCopiesExcept("1.1"), Is.Zero);

            var root = RuntimeModels.GetBundledCopiesRoot();
            foreach (var version in new[] { "1.0", "1.1", "0.9" })
            {
                Directory.CreateDirectory(Path.Combine(root, version, "hf-hub", "models"));
                File.WriteAllText(Path.Combine(root, version, "hf-hub", "models", "m.sentis"), version);
            }

            Assert.That(HubStorage.DeleteBundledCopiesExcept("1.1"), Is.EqualTo(2));
            Assert.That(Directory.GetDirectories(root).Select(Path.GetFileName), Is.EqualTo(new[] { "1.1" }));
        }

        private static string WriteConverted(string repository, string key, string generation, string repoPath, bool writeRecord = true)
        {
            var modelPath = ConvertedModelStore.GetModelPath(repository, key);
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath));
            File.WriteAllText(modelPath, "model");
            if (writeRecord)
            {
                File.WriteAllText(ConvertedModelStore.GetRecordPath(repository, key), JsonUtility.ToJson(new ConvertedModelRecord
                {
                    repository = repository,
                    repoPath = repoPath,
                    converterId = "test@" + generation,
                    formatGeneration = generation
                }));
            }

            return Path.GetFullPath(modelPath);
        }
    }
}
