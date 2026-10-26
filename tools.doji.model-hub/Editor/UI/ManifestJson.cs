using System.Text.RegularExpressions;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Cleans manifest JSON written with <c>JsonUtility</c>, which writes every field.
    /// </summary>
    internal static class ManifestJson
    {
        private static readonly Regex EmptyLabel = new(@",\s*""label""\s*:\s*""""");

        /// <summary>
        /// Removes <c>"label": ""</c>, so files without a label keep the short form. The label must be the last field
        /// of a file object.
        /// </summary>
        public static string StripEmptyLabels(string json)
        {
            return json == null ? null : EmptyLabel.Replace(json, string.Empty);
        }
    }
}
