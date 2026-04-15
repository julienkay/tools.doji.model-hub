namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Defines the package-level file and path conventions used by the HuggingFace Hub integration.
    /// </summary>
    public static class ModelHub
    {
        /// <summary>
        /// Package manifest filename that declares remote model dependencies for a Unity package.
        /// </summary>
        public const string ManifestFileName = "hf-hub-manifest.json";

        /// <summary>
        /// Copyable example manifest filename that package authors can use as a starting point.
        /// </summary>
        public const string ManifestTemplateFileName = "hf-hub-manifest.template.json";

        /// <summary>
        /// Root folder inside the Unity project where generated `.sentis` assets will be written.
        /// </summary>
        public const string GeneratedAssetsRoot = "Assets/Doji/HfHub/Generated";
    }
}
