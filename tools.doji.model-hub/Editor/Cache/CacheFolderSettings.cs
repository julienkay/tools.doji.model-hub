using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// The Editor's per-user choice of the two folders AI Model Hub uses: the Hugging Face cache and the data folder
    /// with the index and the converted models. Stored in <see cref="EditorPrefs"/>, so it applies to every project
    /// of this user on this machine and never lives inside the folders it points at. Players never read it: they use
    /// the environment variables or the default.
    /// </summary>
    internal static class CacheFolderSettings
    {
        internal const string EditorPrefsKey = "Doji.AI.ModelHub.CacheFolders";

        private static CacheFolderChoice _stored;
        private static CacheFolderChoice _override;
        private static bool _loaded;

        /// <summary>
        /// Raised on the main thread after a folder choice changed.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// The current choice. Safe to read from any thread after the Editor loaded the package.
        /// </summary>
        public static CacheFolderChoice Current
        {
            get
            {
                if (_override != null)
                {
                    return _override;
                }

                if (!_loaded)
                {
                    Load();
                }

                return _stored ?? CacheFolderChoice.Default;
            }
        }

        /// <summary>
        /// The custom Hugging Face cache folder, or null when the cache follows the Hugging Face default.
        /// </summary>
        public static string GetCustomHubCache()
        {
            return Normalize(Current.HubCacheFolder);
        }

        /// <summary>
        /// The custom data folder, or null when the data folder follows <c>DOJI_MODEL_HUB_DATA_ROOT</c> or the default.
        /// </summary>
        public static string GetCustomDataRoot()
        {
            return Normalize(Current.DataFolder);
        }

        /// <summary>
        /// Stores a new choice for this user. It applies at once: the next path lookup uses it.
        /// </summary>
        public static void Save(CacheFolderChoice choice)
        {
            var normalized = new CacheFolderChoice
            {
                HubCacheFolder = Normalize(choice?.HubCacheFolder),
                DataFolder = Normalize(choice?.DataFolder)
            };
            if (_override != null)
            {
                _override = normalized;
            }
            else
            {
                _stored = normalized;
                _loaded = true;
                EditorPrefs.SetString(EditorPrefsKey, JsonConvert.SerializeObject(normalized));
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Replaces the stored choice until the scope is disposed, without touching <see cref="EditorPrefs"/>.
        /// Tests use it.
        /// </summary>
        internal static IDisposable PushOverride(string hubCacheFolder, string dataFolder)
        {
            var previous = _override;
            _override = new CacheFolderChoice
            {
                HubCacheFolder = Normalize(hubCacheFolder),
                DataFolder = Normalize(dataFolder)
            };
            return new RestoreScope(() => _override = previous);
        }

        /// <summary>
        /// Reads the stored choice again, for example after another Editor changed it.
        /// </summary>
        internal static void Load()
        {
            _loaded = true;
            try
            {
                var json = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
                _stored = string.IsNullOrWhiteSpace(json)
                    ? CacheFolderChoice.Default
                    : JsonConvert.DeserializeObject<CacheFolderChoice>(json) ?? CacheFolderChoice.Default;
            }
            catch (JsonException)
            {
                _stored = CacheFolderChoice.Default;
            }
        }

        internal static string Normalize(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            var expanded = CachePaths.ExpandPathValue(folder);
            try
            {
                var full = Path.GetFullPath(expanded);
                return string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase)
                    ? full
                    : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return expanded;
            }
        }

        private sealed class RestoreScope : IDisposable
        {
            private Action _restore;

            public RestoreScope(Action restore)
            {
                _restore = restore;
            }

            public void Dispose()
            {
                _restore?.Invoke();
                _restore = null;
            }
        }
    }

    /// <summary>
    /// One user's folder choice. A null folder follows the environment variables or the default.
    /// </summary>
    [Serializable]
    internal sealed class CacheFolderChoice
    {
        public static CacheFolderChoice Default => new();

        /// <summary>
        /// Custom Hugging Face cache folder, or null for "Hugging Face default (shared with Python)".
        /// </summary>
        [JsonProperty("hubCache")] public string HubCacheFolder { get; set; }

        /// <summary>
        /// Custom data folder for the index and the converted models, or null for the default.
        /// </summary>
        [JsonProperty("dataRoot")] public string DataFolder { get; set; }
    }

    /// <summary>
    /// Connects the runtime cache paths to the per-user folder setting.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorCacheRoot
    {
        static EditorCacheRoot()
        {
            Register();
        }

        /// <summary>
        /// Makes <see cref="CachePaths"/> use the per-user folders. Safe to call more than once.
        /// </summary>
        public static void Register()
        {
            CacheFolderSettings.Load();
            CachePaths.CacheRootOverride ??= CacheFolderSettings.GetCustomHubCache;
            CachePaths.DataRootOverride ??= CacheFolderSettings.GetCustomDataRoot;
        }
    }
}
