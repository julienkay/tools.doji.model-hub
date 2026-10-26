using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Names the package and default model of a <see cref="PretrainedModel{T}"/> class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PretrainedModelAttribute : Attribute
    {
        /// <param name="packageId">Your package id, for example <c>tools.doji.midas</c>.</param>
        /// <param name="repoId">The repository of the default model, for example <c>julienkay/sentis-MiDaS</c>.</param>
        /// <param name="defaultRepoPath">The default model's path in that repository.</param>
        public PretrainedModelAttribute(string packageId, string repoId, string defaultRepoPath)
        {
            PackageId = packageId;
            RepoId = repoId;
            DefaultRepoPath = defaultRepoPath;
        }

        public string PackageId { get; }

        public string RepoId { get; }

        public string DefaultRepoPath { get; }

        internal static PretrainedModelAttribute Get(Type type)
        {
            return type?.GetCustomAttribute<PretrainedModelAttribute>(false);
        }
    }

    /// <summary>
    /// A base class for a package's model class. It adds the <c>FromPretrained</c> factory methods, the default model,
    /// and the <see cref="Backend"/> property, so the class only sets up and releases its own resources.
    /// </summary>
    /// <remarks>
    /// Mark the class with <see cref="PretrainedModelAttribute"/>, and give it a private parameterless constructor
    /// for the factory methods:
    /// <code>
    /// [PretrainedModel("tools.doji.midas", "julienkay/sentis-MiDaS", "onnx/midas_v21_small_256.onnx")]
    /// public sealed class Midas : PretrainedModel&lt;Midas&gt;
    /// {
    ///     private Midas() { }
    ///     public Midas(Model model, BackendType backend = BackendType.GPUCompute) : base(model, backend) { }
    ///
    ///     protected override void Initialize() { /* create a Worker for Model on Backend */ }
    ///     protected override void Release() { /* dispose of it */ }
    /// }
    /// </code>
    /// For a package that runs several models together, use <see cref="HubModelLoader"/> directly.
    /// </remarks>
    /// <typeparam name="T">The class that derives from this one.</typeparam>
    public abstract class PretrainedModel<T> : IDisposable where T : PretrainedModel<T>
    {
        private static HubModelLoader s_loader;

        private BackendType _backend = BackendType.GPUCompute;

        /// <summary>
        /// For the factory methods. Derived classes declare a private parameterless constructor that calls it.
        /// </summary>
        protected PretrainedModel() { }

        /// <summary>
        /// Uses a model that is already loaded, and calls <see cref="Initialize"/>. Initialize runs before the body
        /// of the derived constructor.
        /// </summary>
        /// <param name="name">A short name for logs and texture names. Defaults to the class name in lower case.</param>
        protected PretrainedModel(Model model, BackendType backend, string name = null)
        {
            Setup(model ?? throw new ArgumentNullException(nameof(model)), backend, name);
        }

        /// <summary>
        /// Uses a model asset of your own, and calls <see cref="Initialize"/>.
        /// </summary>
        protected PretrainedModel(ModelAsset modelAsset, BackendType backend)
        {
            if (modelAsset == null)
            {
                throw new ArgumentNullException(nameof(modelAsset));
            }

            Setup(ModelLoader.Load(modelAsset), backend, modelAsset.name);
        }

        /// <summary>
        /// Loads the models of this class's package. Its package and default model come from the
        /// <see cref="PretrainedModelAttribute"/> on <typeparamref name="T"/>.
        /// </summary>
        public static HubModelLoader Loader => s_loader ??= CreateLoader();

        /// <summary>
        /// The package id that AI Model Hub knows this package by.
        /// </summary>
        public static string PackageId => Loader.PackageId;

        /// <summary>
        /// The repository that a bare repository path refers to.
        /// </summary>
        public static string RepoId => Loader.RepoId;

        /// <summary>
        /// The model that loads when you name none.
        /// </summary>
        public static HubModelReference DefaultModel => Loader.DefaultModel;

        /// <summary>
        /// The loaded model.
        /// </summary>
        public Model Model { get; private set; }

        /// <summary>
        /// A short name of the model, for logs and texture names: its file name without the extension.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Which <see cref="BackendType"/> runs the model. Changing it calls <see cref="Release"/> and then
        /// <see cref="Initialize"/>.
        /// </summary>
        public BackendType Backend
        {
            get => _backend;
            set
            {
                if (_backend == value)
                {
                    return;
                }

                _backend = value;
                if (Model != null)
                {
                    Release();
                    Initialize();
                }
            }
        }

        /// <summary>
        /// Loads a model and creates an instance. Works on every platform; a model that the project downloads at
        /// runtime is fetched on first use.
        /// </summary>
        /// <param name="model">
        /// Null for <see cref="DefaultModel"/>. A path of a <c>.sentis</c> or <c>.onnx</c> file loads that file.
        /// Anything else is a file of the package's manifest, for example <c>onnx/model.onnx</c>; the project must
        /// include it in <c>Window &gt; AI Model Hub</c>.
        /// </param>
        /// <param name="backend">The backend to run the model with.</param>
        /// <param name="progress">Reports download progress when the model is downloaded at runtime.</param>
        /// <param name="cancellationToken">Cancels the download.</param>
        public static async Task<T> FromPretrainedAsync(
            string model = null,
            BackendType backend = BackendType.GPUCompute,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Create(await Loader.LoadAsync(model, progress, cancellationToken), backend);
        }

        /// <summary>
        /// Loads a model chosen with the <see cref="HubModelAttribute"/> picker or from <see cref="Hub.GetModelFiles"/>,
        /// and creates an instance. Null loads <see cref="DefaultModel"/>.
        /// </summary>
        public static async Task<T> FromPretrainedAsync(
            HubModelReference model,
            BackendType backend = BackendType.GPUCompute,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Create(await Loader.LoadAsync(model, progress, cancellationToken), backend);
        }

        /// <summary>
        /// Loads a model without waiting, like <see cref="FromPretrainedAsync(string,BackendType,IProgress{DownloadProgress},CancellationToken)"/>.
        /// Works in the Editor and for bundled models on Windows, macOS, Linux, and iOS. On Android and the Web, or
        /// when the model is downloaded at runtime, use <c>FromPretrainedAsync</c>.
        /// </summary>
        public static T FromPretrained(string model = null, BackendType backend = BackendType.GPUCompute)
        {
            return Create(Loader.Load(model), backend);
        }

        /// <summary>
        /// Loads a model without waiting, where <see cref="FromPretrained(string,BackendType)"/> can.
        /// </summary>
        public static T FromPretrained(HubModelReference model, BackendType backend = BackendType.GPUCompute)
        {
            return Create(Loader.Load(model), backend);
        }

        /// <summary>
        /// Sets up what running <see cref="Model"/> on <see cref="Backend"/> needs, for example a <see cref="Worker"/>.
        /// </summary>
        protected abstract void Initialize();

        /// <summary>
        /// Releases what <see cref="Initialize"/> created.
        /// </summary>
        protected abstract void Release();

        public void Dispose()
        {
            Release();
        }

        private void Setup(Model model, BackendType backend, string name)
        {
            Model = model;
            Name = string.IsNullOrWhiteSpace(name) ? typeof(T).Name.ToLowerInvariant() : name;
            _backend = backend;
            Initialize();
        }

        private static T Create(LoadedModel loaded, BackendType backend)
        {
            T instance;
            try
            {
                instance = (T)Activator.CreateInstance(typeof(T), nonPublic: true);
            }
            catch (MissingMethodException exception)
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name} needs a parameterless constructor, for example 'private {typeof(T).Name}() {{ }}', for FromPretrained.",
                    exception);
            }

            instance.Setup(loaded.Model, backend, loaded.Name);
            return instance;
        }

        private static HubModelLoader CreateLoader()
        {
            var attribute = PretrainedModelAttribute.Get(typeof(T))
                ?? throw new InvalidOperationException(
                    $"{typeof(T).Name} needs a [PretrainedModel(packageId, repoId, defaultRepoPath)] attribute.");
            return new HubModelLoader(attribute.PackageId, attribute.RepoId, attribute.DefaultRepoPath);
        }
    }
}
