namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Base URL of the Hugging Face Hub for API calls and file downloads, in the Editor and in players.
    /// </summary>
    internal static class HubEndpoint
    {
        internal const string Default = "https://huggingface.co";

        /// <summary>
        /// Test hook that points every request at another server, for example a local fake Hub.
        /// </summary>
        internal static string Override { get; set; }

        /// <summary>
        /// The base URL without a trailing slash.
        /// </summary>
        public static string Url => string.IsNullOrWhiteSpace(Override) ? Default : Override.TrimEnd('/');
    }
}
