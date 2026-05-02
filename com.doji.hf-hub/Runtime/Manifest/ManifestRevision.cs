using System;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Centralizes the schema default for manifest revision resolution without forcing editor surfaces to normalize authored text.
    /// </summary>
    internal static class ManifestRevision
    {
        public const string Default = "main";

        public static string NormalizeForResolution(string revision)
        {
            return string.IsNullOrWhiteSpace(revision)
                ? Default
                : revision.Trim();
        }
    }
}
