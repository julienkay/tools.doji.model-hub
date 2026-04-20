namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Provides HuggingFace-oriented naming helpers for manifest model definitions.
    /// </summary>
    internal static class HfHubModelDependencyDefinitionExtensions
    {
        /// <summary>
        /// Returns the canonical HuggingFace repository identifier for one manifest entry.
        /// </summary>
        public static string GetRepositoryId(this HfHubModelDependencyDefinition definition)
        {
            return definition?.Id;
        }
    }
}
