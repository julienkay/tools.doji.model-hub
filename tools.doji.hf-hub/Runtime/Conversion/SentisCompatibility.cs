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
        /// One format generation: the first Inference Engine version whose converted models older versions must not
        /// reuse, and the <c>ModelWriter.version</c> that generation was last checked against.
        /// </summary>
        internal readonly struct Generation
        {
            public Generation(string firstVersion, int modelWriterVersion)
            {
                FirstVersion = firstVersion;
                ModelWriterVersion = modelWriterVersion;
            }

            public string FirstVersion { get; }

            public int ModelWriterVersion { get; }

            public string Id => "ie-" + FirstVersion;
        }

        /// <summary>
        /// Format generations, oldest first. Add one when an Inference Engine release changes the serialized format
        /// (<c>ModelWriter.version</c>) or what the ONNX importer produces in a way older conversions must not be reused
        /// for. A test fails when the installed <c>ModelWriter.version</c> is newer than the last entry, so a format
        /// change is never missed.
        /// </summary>
        internal static readonly IReadOnlyList<Generation> Generations = new[]
        {
            new Generation("2.2.0", 8)
        };

        /// <summary>
        /// Gets the format generation of an Inference Engine version, for example <c>ie-2.2.0</c>. Versions newer than
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
