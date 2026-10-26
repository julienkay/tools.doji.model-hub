using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Doji.Downloads;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Turns a model card (<c>README.md</c>) into plain text for a preview: no YAML metadata, HTML, images, or
    /// Markdown markup.
    /// </summary>
    internal static class ModelCardText
    {
        private static readonly Regex FrontMatter = new(@"\A﻿?---[ \t]*\r?\n.*?\r?\n---[ \t]*(\r?\n|\z)", RegexOptions.Singleline);
        private static readonly Regex HtmlComment = new(@"<!--.*?-->", RegexOptions.Singleline);
        private static readonly Regex HtmlTag = new(@"<[^>\n]+>");
        private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]*\)");
        private static readonly Regex Link = new(@"\[([^\]]*)\]\([^)]*\)");
        private static readonly Regex Heading = new(@"^[ \t]{0,3}#{1,6}[ \t]+", RegexOptions.Multiline);
        private static readonly Regex Emphasis = new(@"(\*\*|__)(.+?)\1");
        private static readonly Regex SingleEmphasis = new(@"(?<![\w*_])([*_])(?=\S)(.+?)(?<=\S)\1(?![\w*_])");
        private static readonly Regex Blockquote = new(@"^[ \t]*>[ \t]?", RegexOptions.Multiline);
        private static readonly Regex CodeFence = new(@"^[ \t]*```[^\n]*(\n|\z)", RegexOptions.Multiline);
        private static readonly Regex BlankLines = new(@"\n[ \t]*\n(?:[ \t]*\n)+");

        /// <summary>
        /// Returns the readable text of a model card, or an empty string when it has none.
        /// </summary>
        public static string ToPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var text = markdown.Replace("\r\n", "\n");
            text = FrontMatter.Replace(text, string.Empty, 1);
            text = HtmlComment.Replace(text, string.Empty);
            text = Image.Replace(text, string.Empty);
            text = HtmlTag.Replace(text, string.Empty);
            text = Link.Replace(text, "$1");
            text = Heading.Replace(text, string.Empty);
            text = Emphasis.Replace(text, "$2");
            text = SingleEmphasis.Replace(text, "$2");
            text = Blockquote.Replace(text, string.Empty);
            text = CodeFence.Replace(text, string.Empty);
            text = string.Join("\n", text.Split('\n').Select(line => line.TrimEnd()));
            text = BlankLines.Replace(text, "\n\n");
            return text.Trim();
        }

        /// <summary>
        /// Returns the start of the text, at most <paramref name="maxLines"/> lines and about
        /// <paramref name="maxCharacters"/> characters, cut at a word, and whether text was left out.
        /// </summary>
        public static string GetPreview(string text, int maxLines, int maxCharacters, out bool isTruncated)
        {
            text ??= string.Empty;
            var lines = text.Split('\n');
            var preview = lines.Length > maxLines ? string.Join("\n", lines.Take(maxLines)).TrimEnd() : text;
            if (preview.Length > maxCharacters)
            {
                var cut = preview.LastIndexOfAny(new[] { ' ', '\n' }, maxCharacters);
                preview = preview.Substring(0, cut > maxCharacters / 2 ? cut : maxCharacters).TrimEnd() + "…";
            }

            isTruncated = preview.Length < text.Length;
            return preview;
        }
    }

    /// <summary>
    /// Loads model cards for the Model Cache view: from the cached snapshot when it has <c>README.md</c>, else from
    /// Hugging Face once per session.
    /// </summary>
    internal static class ModelCardService
    {
        internal const string FileName = "README.md";

        private static readonly Dictionary<string, Task<string>> Requests = new(StringComparer.Ordinal);

        /// <summary>
        /// Reads the model card of a cached revision, or returns null when the snapshot does not have it.
        /// </summary>
        public static string ReadCached(string repositoryFolder, string revision)
        {
            var snapshot = CacheOverviewService.GetSnapshotFolder(repositoryFolder, revision);
            var path = snapshot == null ? null : Path.Combine(snapshot, FileName);
            try
            {
                return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads the model card once per repository and revision. The task returns null when the model has no
        /// model card, and fails when Hugging Face cannot be reached.
        /// </summary>
        public static Task<string> FetchAsync(string repository, string revision)
        {
            var key = $"{repository}@{revision}";
            if (!Requests.TryGetValue(key, out var request) || request.IsFaulted)
            {
                request = DownloadAsync(repository, revision);
                Requests[key] = request;
            }

            return request;
        }

        private static async Task<string> DownloadAsync(string repository, string revision)
        {
            using var request = UnityWebRequest.Get(HttpClient.BuildResolveUrl(repository, revision, FileName));
            try
            {
                await request.SendAsync();
            }
            catch (Exception) when (request.responseCode == 404)
            {
                return null;
            }

            return request.downloadHandler.text;
        }
    }
}
