using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the Model card | Files | License links and the license fields read from the Hub's card data.
    /// </summary>
    internal sealed class ModelLinksTests
    {
        private const string Repo = "doji/test-model";
        private const string Commit = "abc123";

        /// <summary>
        /// Verifies the row links the model card, the files at the commit, and the license file at the commit, and
        /// names the declared license.
        /// </summary>
        [Test]
        public void Build_LinksModelCardFilesAndLicenseFile()
        {
            var links = ModelLinks.Build(Repo, Commit, new ModelCardMetadata { License = "mit" }, new[] { "onnx/model.onnx", "LICENSE.txt" });

            Assert.That(links.Select(link => link.Label), Is.EqualTo(new[] { "Model card", "Files", "License: mit" }));
            Assert.That(links[0].Url, Is.EqualTo("https://huggingface.co/doji/test-model"));
            Assert.That(links[1].Url, Is.EqualTo("https://huggingface.co/doji/test-model/tree/abc123"));
            Assert.That(links[2].Url, Is.EqualTo("https://huggingface.co/doji/test-model/blob/abc123/LICENSE.txt"));
        }

        /// <summary>
        /// Verifies a license file in a subfolder is not taken for the repository license.
        /// </summary>
        [Test]
        public void GetLicenseLink_IgnoresLicenseFilesInSubfolders()
        {
            var link = ModelLinks.GetLicenseLink(Repo, Commit, new ModelCardMetadata { License = "apache-2.0" }, new[] { "third_party/LICENSE" });

            Assert.That(link.Label, Is.EqualTo("License: apache-2.0"));
            Assert.That(link.Url, Is.EqualTo("https://huggingface.co/doji/test-model"));
        }

        /// <summary>
        /// Verifies the card's license_link wins over a license file: an absolute URL as given, a repository path at
        /// the commit with escaped segments.
        /// </summary>
        [TestCase("https://example.com/terms", "https://example.com/terms")]
        [TestCase("LICENSE.md", "https://huggingface.co/doji/test-model/blob/abc123/LICENSE.md")]
        [TestCase("./docs/model license.md", "https://huggingface.co/doji/test-model/blob/abc123/docs/model%20license.md")]
        public void GetLicenseLink_PrefersCardLicenseLink(string licenseLink, string expectedUrl)
        {
            var card = new ModelCardMetadata { License = "other", LicenseName = "doji-research", LicenseLink = licenseLink };

            var link = ModelLinks.GetLicenseLink(Repo, Commit, card, new[] { "LICENSE" });

            Assert.That(link.Label, Is.EqualTo("License: doji-research"));
            Assert.That(link.Url, Is.EqualTo(expectedUrl));
        }

        /// <summary>
        /// Verifies a link that is neither a web URL nor a repository path falls back to the license file.
        /// </summary>
        [Test]
        public void GetLicenseLink_IgnoresUnusableLicenseLink()
        {
            var card = new ModelCardMetadata { License = "mit", LicenseLink = "javascript:alert(1)" };

            var link = ModelLinks.GetLicenseLink(Repo, Commit, card, new[] { "LICENSE" });

            Assert.That(link.Url, Is.EqualTo("https://huggingface.co/doji/test-model/blob/abc123/LICENSE"));
        }

        /// <summary>
        /// Verifies a card without a license says so, and an unknown card only points at the model card.
        /// </summary>
        [Test]
        public void GetLicenseLink_DistinguishesNoLicenseFromUnknownCard()
        {
            var none = ModelLinks.GetLicenseLink(Repo, Commit, new ModelCardMetadata(), Array.Empty<string>());
            var unknown = ModelLinks.GetLicenseLink(Repo, Commit, null, null);

            Assert.That(none.Label, Is.EqualTo("No license"));
            Assert.That(none.Tooltip, Does.Contain("declares no license"));
            Assert.That(none.Url, Is.EqualTo("https://huggingface.co/doji/test-model"));
            Assert.That(unknown.Label, Is.EqualTo("License"));
            Assert.That(unknown.Url, Is.EqualTo("https://huggingface.co/doji/test-model"));
        }

        /// <summary>
        /// Verifies the links open the requested revision when the commit is not known yet, escaped.
        /// </summary>
        [Test]
        public void Build_UsesEscapedRevisionWithoutCommit()
        {
            var links = ModelLinks.Build(Repo, "refs/pr/1", null, null);

            Assert.That(links[1].Url, Is.EqualTo("https://huggingface.co/doji/test-model/tree/refs%2Fpr%2F1"));
        }

        /// <summary>
        /// Verifies license fields read from card data: one id, a list of ids, and a missing card.
        /// </summary>
        [Test]
        public void FromCardData_ReadsLicenseFields()
        {
            var single = ModelCardMetadata.FromCardData(new JObject { ["license"] = "mit", ["license_link"] = "LICENSE" });
            var list = ModelCardMetadata.FromCardData(new JObject { ["license"] = new JArray("mit", " apache-2.0 ", 3) });
            var missing = ModelCardMetadata.FromCardData(null);

            Assert.That(single.License, Is.EqualTo("mit"));
            Assert.That(single.LicenseLink, Is.EqualTo("LICENSE"));
            Assert.That(single.LicenseName, Is.Null);
            Assert.That(list.License, Is.EqualTo("mit, apache-2.0"));
            Assert.That(missing, Is.Not.Null);
            Assert.That(missing.License, Is.Null);
        }
    }
}
