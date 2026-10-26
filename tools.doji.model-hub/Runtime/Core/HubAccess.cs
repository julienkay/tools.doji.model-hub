using System.IO;
using Doji.Downloads;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Turns the Hub's access errors into messages that say what is wrong. AI Model Hub sends no access token, so gated
    /// and private repositories answer 401 or 403. The Hub also answers 401 for a repository that does not exist.
    /// </summary>
    internal static class HubAccess
    {
        public static bool IsDenied(long responseCode)
        {
            return responseCode is 401 or 403;
        }

        /// <summary>
        /// For a file download: the repository exists, so it is gated or private.
        /// </summary>
        public static IOException FileDenied(string repository, string repoPath, WebRequestException inner = null)
        {
            return new IOException(
                $"Hugging Face denied the download of '{repoPath}' from '{repository}'. The model is gated or private. " +
                "AI Model Hub downloads public models only; access tokens are not supported yet.",
                inner);
        }

        /// <summary>
        /// For the repository lookup: the repository does not exist, or it is private.
        /// </summary>
        public static IOException RepositoryDenied(string repository, string revision, WebRequestException inner = null)
        {
            return new IOException(
                $"Hugging Face has no public model '{repository}' at revision '{revision}'. Check the id and revision. " +
                "Private models are not supported yet.",
                inner);
        }
    }
}
