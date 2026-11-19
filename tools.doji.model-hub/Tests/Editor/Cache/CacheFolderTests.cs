using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers which folders AI Model Hub uses: the per-user Editor setting, the Hugging Face and AI Model Hub
    /// environment variables, the stale variable check, and the folder report for tools.
    /// </summary>
    internal sealed class CacheFolderTests
    {
        private readonly Dictionary<string, string> _savedVariables = new(StringComparer.Ordinal);
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.CacheFolderTests", Guid.NewGuid().ToString("N"));
            foreach (var name in CachePaths.FolderEnvironmentVariables)
            {
                _savedVariables[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _savedVariables)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>
        /// A custom cache folder in the Editor setting is the cache, even when HF_HUB_CACHE is set, and the source
        /// says it comes from the setting.
        /// </summary>
        [Test]
        public void CustomHubCache_WinsOverEnvironmentVariable()
        {
            var custom = Path.Combine(_root, "custom-hub");
            Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, Path.Combine(_root, "env-hub"));

            using (CacheFolderSettings.PushOverride(custom, null))
            {
                Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo(custom));
                Assert.That(CachePaths.GetCacheRootSource(), Is.EqualTo(CachePaths.EditorSettingSource));
                Assert.That(CachePaths.GetRepositoryRoot("owner/model"), Is.EqualTo(Path.Combine(custom, "models--owner--model")));
            }
        }

        /// <summary>
        /// "Hugging Face default" follows HF_HUB_CACHE first, then HF_HOME/hub, like the huggingface_hub Python library.
        /// </summary>
        [Test]
        public void DefaultHubCache_FollowsHubCacheThenHfHome()
        {
            using (CacheFolderSettings.PushOverride(null, null))
            {
                var home = Path.Combine(_root, "hf-home");
                Environment.SetEnvironmentVariable(CachePaths.HomeEnvironmentVariable, home);
                Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo(Path.Combine(home, "hub")));
                Assert.That(CachePaths.GetCacheRootSource(), Is.EqualTo(CachePaths.HomeEnvironmentVariable));

                var hubCache = Path.Combine(_root, "hub-cache");
                Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, hubCache);
                Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo(hubCache));
                Assert.That(CachePaths.GetCacheRootSource(), Is.EqualTo(CachePaths.HubCacheEnvironmentVariable));
            }
        }

        /// <summary>
        /// Without a setting or variable, the cache is ~/.cache/huggingface/hub and the source is the default.
        /// </summary>
        [Test]
        public void DefaultHubCache_WithoutVariables_IsUserCacheFolder()
        {
            using (CacheFolderSettings.PushOverride(null, null))
            {
                Assert.That(
                    CachePaths.GetCacheRoot(),
                    Is.EqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface", "hub")));
                Assert.That(CachePaths.GetCacheRootSource(), Is.EqualTo(CachePaths.DefaultSource));
            }
        }

        /// <summary>
        /// A custom data folder holds the index and the converted models and wins over DOJI_MODEL_HUB_DATA_ROOT;
        /// without it, the variable decides.
        /// </summary>
        [Test]
        public void CustomDataRoot_WinsOverEnvironmentVariable()
        {
            var fromVariable = Path.Combine(_root, "env-data");
            var custom = Path.Combine(_root, "custom-data");
            Environment.SetEnvironmentVariable(CachePaths.DataRootEnvironmentVariable, fromVariable);

            using (CacheFolderSettings.PushOverride(null, custom))
            {
                Assert.That(CachePaths.GetDataRoot(), Is.EqualTo(custom));
                Assert.That(CachePaths.GetConvertedRoot(), Is.EqualTo(Path.Combine(custom, "converted")));
                Assert.That(CachePaths.GetDataRootSource(), Is.EqualTo(CachePaths.EditorSettingSource));
            }

            using (CacheFolderSettings.PushOverride(null, null))
            {
                Assert.That(CachePaths.GetDataRoot(), Is.EqualTo(fromVariable));
                Assert.That(CachePaths.GetDataRootSource(), Is.EqualTo(CachePaths.DataRootEnvironmentVariable));
            }
        }

        /// <summary>
        /// A saved choice applies at once and raises Changed. Inside a test override it never writes EditorPrefs.
        /// Saved paths are full paths without a trailing separator.
        /// </summary>
        [Test]
        public void Save_AppliesAtOnceWithoutTouchingEditorPrefs()
        {
            var before = EditorPrefs.GetString(CacheFolderSettings.EditorPrefsKey, "<unset>");
            var changed = 0;
            void OnChanged() => changed++;
            CacheFolderSettings.Changed += OnChanged;
            try
            {
                using (CacheFolderSettings.PushOverride(null, null))
                {
                    var custom = Path.Combine(_root, "saved") + Path.DirectorySeparatorChar;
                    CacheFolderSettings.Save(new CacheFolderChoice { HubCacheFolder = custom });

                    Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo(Path.Combine(_root, "saved")));
                    Assert.That(changed, Is.EqualTo(1));
                }
            }
            finally
            {
                CacheFolderSettings.Changed -= OnChanged;
            }

            Assert.That(EditorPrefs.GetString(CacheFolderSettings.EditorPrefsKey, "<unset>"), Is.EqualTo(before));
        }

        /// <summary>
        /// "Also set it for Python" sets HF_HOME to the parent of a folder named hub, and HF_HUB_CACHE otherwise or
        /// when HF_HUB_CACHE is already set and would win over HF_HOME.
        /// </summary>
        [Test]
        public void GetPythonVariable_PicksHfHomeForHubFolders()
        {
            var environment = new FakeEnvironmentVariables();
            var hub = Path.Combine(_root, "huggingface", "hub");
            var other = Path.Combine(_root, "models");

            Assert.That(CacheFolderChange.GetPythonVariable(hub, environment), Is.EqualTo((CachePaths.HomeEnvironmentVariable, Path.Combine(_root, "huggingface"))));
            Assert.That(CacheFolderChange.GetPythonVariable(other, environment), Is.EqualTo((CachePaths.HubCacheEnvironmentVariable, other)));

            environment.ForNewPrograms[CachePaths.HubCacheEnvironmentVariable] = @"C:\elsewhere";
            Assert.That(CacheFolderChange.GetPythonVariable(hub, environment), Is.EqualTo((CachePaths.HubCacheEnvironmentVariable, hub)));
        }

        /// <summary>
        /// A variable that new programs get with another value than this Editor has is stale: it changed after
        /// Unity Hub started. The message says to restart Unity Hub or use the Editor setting.
        /// </summary>
        [Test]
        public void FindStaleVariables_ReportsChangedValue()
        {
            var environment = new FakeEnvironmentVariables();
            environment.InProcess[CachePaths.HomeEnvironmentVariable] = @"C:\old\huggingface";
            environment.ForNewPrograms[CachePaths.HomeEnvironmentVariable] = @"G:\huggingface";
            environment.ForNewPrograms[CachePaths.DataRootEnvironmentVariable] = @"G:\Doji\ModelHub";

            var stale = CacheFolders.FindStaleVariables(environment);

            Assert.That(stale, Has.Length.EqualTo(2));
            Assert.That(stale[0].Name, Is.EqualTo(CachePaths.HomeEnvironmentVariable));
            Assert.That(stale[0].EditorValue, Is.EqualTo(@"C:\old\huggingface"));
            Assert.That(stale[0].CurrentValue, Is.EqualTo(@"G:\huggingface"));
            Assert.That(stale[1].Name, Is.EqualTo(CachePaths.DataRootEnvironmentVariable));
            Assert.That(stale[1].EditorValue, Is.Null);
            Assert.That(
                CacheFolders.BuildStaleMessage(stale),
                Is.EqualTo(
                    "An environment variable changed after Unity started, so this Editor still uses the old value. " +
                    @"HF_HOME: this Editor uses 'C:\old\huggingface', new programs get 'G:\huggingface'. " +
                    @"DOJI_MODEL_HUB_DATA_ROOT: this Editor uses no value, new programs get 'G:\Doji\ModelHub'. " +
                    "Quit Unity Hub (also from the system tray), then start Unity Hub and this Editor again. Or choose the folder in Preferences > AI Model Hub, which applies at once."));
        }

        /// <summary>
        /// Equal values are not stale, nor is anything where the platform cannot read the values new programs get.
        /// </summary>
        [Test]
        public void FindStaleVariables_IgnoresEqualValuesAndUnreadablePlatforms()
        {
            var environment = new FakeEnvironmentVariables();
            environment.InProcess[CachePaths.HubCacheEnvironmentVariable] = @"G:\huggingface\hub\";
            environment.ForNewPrograms[CachePaths.HubCacheEnvironmentVariable] = @"G:\huggingface\hub";
            Assert.That(CacheFolders.FindStaleVariables(environment), Is.Empty);

            environment.ForNewPrograms[CachePaths.HomeEnvironmentVariable] = @"G:\huggingface";
            environment.CanReadForNewPrograms = false;
            Assert.That(CacheFolders.FindStaleVariables(environment), Is.Empty);
        }

        /// <summary>
        /// The tool report lists both folders with their source and the free space of their drives, and the
        /// stale variables with a warning.
        /// </summary>
        [Test]
        public void GetCacheInfo_ListsFoldersSourcesFreeSpaceAndStaleVariables()
        {
            var hubCache = Path.Combine(_root, "g", "hub");
            var dataRoot = Path.Combine(_root, "c", "data");
            Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, hubCache);
            var disk = new FakeDiskSpace()
                .Add(Path.Combine(_root, "g"), "G:\\", 500, 1000)
                .Add(Path.Combine(_root, "c"), "C:\\", 9, 100);
            var environment = new FakeEnvironmentVariables();
            environment.ForNewPrograms[CachePaths.HomeEnvironmentVariable] = @"G:\huggingface";

            using (CacheFolderSettings.PushOverride(null, dataRoot))
            {
                var info = ModelHubAutomation.GetCacheInfo(environment, disk);

                Assert.That(info.HubCache.Path, Is.EqualTo(hubCache));
                Assert.That(info.HubCache.Source, Is.EqualTo("HF_HUB_CACHE"));
                Assert.That(info.HubCache.Drive, Is.EqualTo("G:\\"));
                Assert.That(info.HubCache.FreeBytes, Is.EqualTo(500));
                Assert.That(info.HubCache.TotalBytes, Is.EqualTo(1000));
                Assert.That(info.DataRoot.Path, Is.EqualTo(dataRoot));
                Assert.That(info.DataRoot.Source, Is.EqualTo("editor_setting"));
                Assert.That(info.DataRoot.FreeBytes, Is.EqualTo(9));
                Assert.That(info.StaleVariables, Has.Length.EqualTo(1));
                Assert.That(info.StaleVariables[0].Name, Is.EqualTo("HF_HOME"));
                Assert.That(info.Warning, Does.Contain("Quit Unity Hub"));
            }
        }
    }
}
