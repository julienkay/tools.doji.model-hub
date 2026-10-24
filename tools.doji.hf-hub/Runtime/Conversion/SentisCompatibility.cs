using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Decides which Inference Engine versions can reuse each other's converted models. Most updates do not change
    /// the serialized format: an older <c>.sentis</c> file is upgraded on load, and a newer one loads with a warning. So
    /// HF Hub converts again only when the installed version crosses a <b>format generation</b> listed here, not on
    /// every update.
    /// </summary>
    public static class SentisCompatibility
    {
        /// <summary>
        /// One format generation: the first Inference Engine version it covers, the <c>ModelWriter.version</c> it
        /// started with, a revision for importer changes that keep the file format but must not reuse older
        /// conversions, and the newest writer version checked to still reuse this generation's files.
        /// </summary>
        internal readonly struct Generation
        {
            public Generation(string firstVersion, int modelWriterVersion, int revision = 0, int? checkedUpToWriterVersion = null)
            {
                FirstVersion = firstVersion;
                ModelWriterVersion = modelWriterVersion;
                Revision = revision;
                CheckedUpToWriterVersion = checkedUpToWriterVersion ?? modelWriterVersion;
            }

            public string FirstVersion { get; }

            /// <summary>
            /// The writer version this generation started with. It names the generation and never changes.
            /// </summary>
            public int ModelWriterVersion { get; }

            public int Revision { get; }

            /// <summary>
            /// The newest writer version known to keep using this generation's files. Raising it keeps the id.
            /// </summary>
            public int CheckedUpToWriterVersion { get; }

            /// <summary>
            /// For example <c>sentis-f8</c>, or <c>sentis-f8.1</c> after an importer-only break.
            /// </summary>
            public string Id => Revision == 0 ? $"sentis-f{ModelWriterVersion}" : $"sentis-f{ModelWriterVersion}.{Revision}";
        }

        /// <summary>
        /// Format generations, oldest first. Add one when an Inference Engine release changes the serialized format
        /// (<c>ModelWriter.version</c>) in a way older conversions must not be reused for, or raise the revision when
        /// the ONNX importer's output changes without a format change. A test fails when the installed
        /// <c>ModelWriter.version</c> is newer than the last entry checked, so a format change is never missed. When a
        /// newer format still reads older files well, raise <c>checkedUpToWriterVersion</c> of the last entry instead:
        /// the id stays, and existing conversions are reused.
        /// </summary>
        internal static readonly IReadOnlyList<Generation> Generations = new[]
        {
            new Generation("2.2.0", 8)
        };

        /// <summary>
        /// Gets the format generation of an Inference Engine version, for example <c>sentis-f8</c>. Versions newer than
        /// every entry belong to the last generation, so an update reuses existing conversions by default. A version
        /// that cannot be read also belongs to the last generation.
        /// </summary>
        public static string GetGeneration(string inferenceEngineVersion)
        {
            var version = TryParse(inferenceEngineVersion);
            if (version == null)
            {
                return Generations[Generations.Count - 1].Id;
            }

            var match = Generations.LastOrDefault(generation => TryParse(generation.FirstVersion) <= version);
            return string.IsNullOrEmpty(match.FirstVersion)
                ? "before-" + Generations[0].Id
                : match.Id;
        }

        /// <summary>
        /// Whether models converted with one Inference Engine version can be used with another.
        /// </summary>
        public static bool AreCompatible(string inferenceEngineVersion, string otherInferenceEngineVersion)
        {
            return string.Equals(GetGeneration(inferenceEngineVersion), GetGeneration(otherInferenceEngineVersion), StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads <c>major.minor.patch</c> and ignores a pre-release suffix such as <c>-pre.1</c>.
        /// </summary>
        private static Version TryParse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var core = value.Trim().Split('-', '+')[0];
            return Version.TryParse(core, out var version)
                ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
                : null;
        }
    }
}
