using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Reads environment variables in this Editor process and as newly started programs get them. Tests replace it
    /// with a fake.
    /// </summary>
    internal interface IEnvironmentVariables
    {
        /// <summary>
        /// Whether <see cref="GetForNewPrograms"/> can be read on this platform. Only Windows stores user variables
        /// where a running program can read them.
        /// </summary>
        bool CanReadForNewPrograms { get; }

        /// <summary>
        /// The value in this Editor process, fixed when the process started or set by the Editor itself.
        /// </summary>
        string GetInProcess(string name);

        /// <summary>
        /// The value a program started now gets: the user value, else the machine value. Null when unset.
        /// </summary>
        string GetForNewPrograms(string name);

        /// <summary>
        /// Stores a user variable for programs started from now on, and sets it in this Editor too. Windows only.
        /// </summary>
        void SetForUser(string name, string value);
    }

    /// <summary>
    /// Reads the real environment. On Windows, user and machine values come from the registry, as Explorer passes
    /// them to newly started programs.
    /// </summary>
    internal sealed class SystemEnvironmentVariables : IEnvironmentVariables
    {
        public bool CanReadForNewPrograms => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public string GetInProcess(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public string GetForNewPrograms(string name)
        {
            if (!CanReadForNewPrograms)
            {
                return null;
            }

            try
            {
                return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) ??
                       Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void SetForUser(string name, string value)
        {
            if (!CanReadForNewPrograms)
            {
                throw new PlatformNotSupportedException("Only Windows stores user environment variables for new programs.");
            }

            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// One folder variable whose value in this Editor differs from what new programs get: it changed after Unity
    /// Hub started, and Unity Hub passes its own old environment to every Editor it launches.
    /// </summary>
    internal sealed class StaleEnvironmentVariable
    {
        public StaleEnvironmentVariable(string name, string editorValue, string currentValue)
        {
            Name = name;
            EditorValue = editorValue;
            CurrentValue = currentValue;
        }

        public string Name { get; }

        /// <summary>
        /// The value this Editor uses, or null when unset here.
        /// </summary>
        public string EditorValue { get; }

        /// <summary>
        /// The value newly started programs get, or null when unset for them.
        /// </summary>
        public string CurrentValue { get; }
    }

    /// <summary>
    /// One folder AI Model Hub uses, why, and the space left on its drive.
    /// </summary>
    internal sealed class CacheFolderInfo
    {
        public CacheFolderInfo(string path, string source, DriveSpace drive)
        {
            Path = path;
            Source = source;
            Drive = drive;
        }

        public string Path { get; }

        /// <summary>
        /// <see cref="CachePaths.EditorSettingSource"/>, an environment variable name, or <see cref="CachePaths.DefaultSource"/>.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// The folder's drive, or null when it cannot be read.
        /// </summary>
        public DriveSpace Drive { get; }
    }

    /// <summary>
    /// The folders in use and the warnings about them.
    /// </summary>
    internal sealed class CacheFolderReport
    {
        public CacheFolderReport(CacheFolderInfo hubCache, CacheFolderInfo dataRoot, IReadOnlyList<StaleEnvironmentVariable> staleVariables)
        {
            HubCache = hubCache;
            DataRoot = dataRoot;
            StaleVariables = staleVariables;
        }

        /// <summary>
        /// The Hugging Face cache with the downloads, shared with Python.
        /// </summary>
        public CacheFolderInfo HubCache { get; }

        /// <summary>
        /// AI Model Hub's data folder with the index and the converted models.
        /// </summary>
        public CacheFolderInfo DataRoot { get; }

        public IReadOnlyList<StaleEnvironmentVariable> StaleVariables { get; }
    }

    /// <summary>
    /// Describes the folders AI Model Hub uses and checks the environment variables that decide them.
    /// </summary>
    internal static class CacheFolders
    {
        private static IEnvironmentVariables _environment = new SystemEnvironmentVariables();

        /// <summary>
        /// The environment in use. Tests set a fake and restore it.
        /// </summary>
        internal static IEnvironmentVariables Environment
        {
            get => _environment;
            set => _environment = value ?? new SystemEnvironmentVariables();
        }

        /// <summary>
        /// The folders in use now, their sources, free space, and stale variables.
        /// </summary>
        public static CacheFolderReport Describe(IEnvironmentVariables environment = null, IDiskSpace diskSpace = null)
        {
            var disk = diskSpace ?? DiskSpaceCheck.DiskSpace;
            var hubCache = CachePaths.GetCacheRoot();
            var dataRoot = CachePaths.GetDataRoot();
            return new CacheFolderReport(
                new CacheFolderInfo(hubCache, CachePaths.GetCacheRootSource(), disk.GetDrive(hubCache)),
                new CacheFolderInfo(dataRoot, CachePaths.GetDataRootSource(), disk.GetDrive(dataRoot)),
                FindStaleVariables(environment));
        }

        /// <summary>
        /// Folder variables whose value in this Editor differs from what newly started programs get. Empty where
        /// the platform cannot tell, which is every platform except Windows.
        /// </summary>
        public static StaleEnvironmentVariable[] FindStaleVariables(IEnvironmentVariables environment = null)
        {
            var env = environment ?? Environment;
            if (!env.CanReadForNewPrograms)
            {
                return Array.Empty<StaleEnvironmentVariable>();
            }

            return CachePaths.FolderEnvironmentVariables
                .Select(name => (name, editorValue: Normalize(env.GetInProcess(name)), currentValue: Normalize(env.GetForNewPrograms(name))))
                .Where(item => !SamePath(item.editorValue, item.currentValue))
                .Select(item => new StaleEnvironmentVariable(item.name, item.editorValue, item.currentValue))
                .ToArray();
        }

        /// <summary>
        /// Plain words for a folder source, for the window and the console.
        /// </summary>
        public static string DescribeSource(string source)
        {
            return source switch
            {
                CachePaths.EditorSettingSource => "Editor setting (Preferences > AI Model Hub)",
                CachePaths.DefaultSource => "default",
                null => "default",
                _ => $"{source} environment variable"
            };
        }

        /// <summary>
        /// What to do about stale variables, in one message.
        /// </summary>
        public static string BuildStaleMessage(IReadOnlyList<StaleEnvironmentVariable> staleVariables)
        {
            if (staleVariables == null || staleVariables.Count == 0)
            {
                return null;
            }

            var lines = staleVariables.Select(variable =>
                $"{variable.Name}: this Editor uses {DescribeValue(variable.EditorValue)}, new programs get {DescribeValue(variable.CurrentValue)}.");
            return "An environment variable changed after Unity started, so this Editor still uses the old value. " +
                   string.Join(" ", lines) +
                   " Quit Unity Hub (also from the system tray), then start Unity Hub and this Editor again. Or choose the folder in Preferences > AI Model Hub, which applies at once.";
        }

        private static string DescribeValue(string value)
        {
            return value == null ? "no value" : $"'{value}'";
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool SamePath(string left, string right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(
                left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison);
        }
    }
}
