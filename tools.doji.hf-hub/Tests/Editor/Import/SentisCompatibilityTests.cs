using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers which Inference Engine versions reuse each other's converted models.
    /// </summary>
    internal sealed class SentisCompatibilityTests
    {
        /// <summary>
        /// Verifies versions map to the newest generation they reach, including pre-releases and future versions.
        /// </summary>
        [TestCase("2.2.0", "ie-2.2.0")]
        [TestCase("2.6.1", "ie-2.2.0")]
        [TestCase("2.7.0-pre.3", "ie-2.2.0")]
        [TestCase("9.0.0", "ie-2.2.0")]
        [TestCase("2.1.0", "before-ie-2.2.0")]
        [TestCase("1.6.0-pre.1", "before-ie-2.2.0")]
        public void GetGeneration_MapsVersionsToTheirFormatGeneration(string version, string generation)
        {
            Assert.That(SentisCompatibility.GetGeneration(version), Is.EqualTo(generation));
        }

        /// <summary>
        /// Verifies an unreadable version is treated as current, so it reuses conversions instead of converting all.
        /// </summary>
        [TestCase(null)]
        [TestCase("unknown")]
        public void GetGeneration_TreatsUnreadableVersionsAsTheLatestGeneration(string version)
        {
            Assert.That(SentisCompatibility.GetGeneration(version), Is.EqualTo(SentisCompatibility.Generations.Last().Id));
        }

        /// <summary>
        /// Verifies updates inside a generation are compatible and crossing a generation is not.
        /// </summary>
        [Test]
        public void AreCompatible_OnlyAcrossTheSameGeneration()
        {
            Assert.That(SentisCompatibility.AreCompatible("2.6.1", "2.7.0"), Is.True);
            Assert.That(SentisCompatibility.AreCompatible("2.1.0", "2.6.1"), Is.False);
        }

        /// <summary>
        /// Verifies generations are listed oldest first with non-decreasing writer versions.
        /// </summary>
        [Test]
        public void Generations_AreOrdered()
        {
            var generations = SentisCompatibility.Generations;
            for (var index = 1; index < generations.Count; index++)
            {
                Assert.That(System.Version.Parse(generations[index].FirstVersion), Is.GreaterThan(System.Version.Parse(generations[index - 1].FirstVersion)));
                Assert.That(generations[index].ModelWriterVersion, Is.GreaterThanOrEqualTo(generations[index - 1].ModelWriterVersion));
            }
        }

        /// <summary>
        /// Tripwire: fails when the installed Inference Engine writes a newer <c>.sentis</c> format than the last
        /// generation was checked against. Decide whether older conversions stay usable, then add a generation or
        /// raise the checked writer version in <see cref="SentisCompatibility.Generations"/>.
        /// </summary>
        [Test]
        public void Generations_CoverTheInstalledModelWriterVersion()
        {
            var field = typeof(Model).Assembly.GetType("Unity.InferenceEngine.ModelWriter")?
                .GetField("version", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                Assert.Ignore("This Inference Engine version does not expose ModelWriter.version.");
            }

            var installed = (int)field.GetValue(null);
            Assert.That(
                installed,
                Is.LessThanOrEqualTo(SentisCompatibility.Generations.Last().ModelWriterVersion),
                $"Inference Engine {ProjectImportService.GetSentisVersion()} writes .sentis format {installed}. Review SentisCompatibility.Generations.");
        }
    }
}
