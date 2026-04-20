using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers entry text and enabled state for the custom Manage popup.
    /// </summary>
    internal sealed class HfHubManageMenuPopupTests
    {
        /// <summary>
        /// Verifies both actions are enabled when raw cache is still present.
        /// </summary>
        [Test]
        public void BuildEntries_EnablesCacheActionWhenCacheExists()
        {
            var entries = HfHubManageMenuPopup.BuildEntries(
                true,
                null,
                null,
                "Remove From Project",
                "Project tooltip",
                "Remove From Cache",
                "Cache tooltip");

            Assert.That(entries.Select(entry => entry.Label).ToArray(), Is.EqualTo(new[]
            {
                "Remove From Project",
                "Remove From Cache"
            }));
            Assert.That(entries.Select(entry => entry.IsEnabled).ToArray(), Is.EqualTo(new[]
            {
                true,
                true
            }));
            Assert.That(entries.Select(entry => entry.Tooltip).ToArray(), Is.EqualTo(new[]
            {
                "Project tooltip",
                "Cache tooltip"
            }));
        }

        /// <summary>
        /// Verifies cache action stays visible but disabled when raw cache is already gone.
        /// </summary>
        [Test]
        public void BuildEntries_DisablesCacheActionWhenCacheIsMissing()
        {
            var entries = HfHubManageMenuPopup.BuildEntries(
                false,
                null,
                null,
                "Remove From Project",
                "Project tooltip",
                "Remove From Cache",
                "Missing cache tooltip");

            Assert.That(entries[0].IsEnabled, Is.True);
            Assert.That(entries[1].IsEnabled, Is.False);
            Assert.That(entries[1].Tooltip, Is.EqualTo("Missing cache tooltip"));
        }
    }
}
