using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the squarified treemap layout used by the Model Hub cache overview.
    /// </summary>
    internal sealed class TreemapLayoutTests
    {
        /// <summary>
        /// Verifies tile areas match their weights, tiles stay inside the bounds, and tiles do not overlap.
        /// </summary>
        [Test]
        public void Squarify_FillsBoundsWithProportionalNonOverlappingTiles()
        {
            var weights = new double[] { 6, 6, 4, 3, 2, 2, 1 };
            var bounds = new Rect(0f, 0f, 600f, 400f);

            var rects = TreemapLayout.Squarify(weights, bounds);

            var total = weights.Sum();
            for (var index = 0; index < weights.Length; index++)
            {
                var expectedArea = bounds.width * bounds.height * weights[index] / total;
                Assert.That(rects[index].width * rects[index].height, Is.EqualTo(expectedArea).Within(expectedArea * 0.01), $"tile {index}");
                Assert.That(rects[index].xMin, Is.GreaterThanOrEqualTo(-0.01f));
                Assert.That(rects[index].yMin, Is.GreaterThanOrEqualTo(-0.01f));
                Assert.That(rects[index].xMax, Is.LessThanOrEqualTo(bounds.xMax + 0.01f));
                Assert.That(rects[index].yMax, Is.LessThanOrEqualTo(bounds.yMax + 0.01f));
                for (var other = index + 1; other < weights.Length; other++)
                {
                    var overlapX = Mathf.Min(rects[index].xMax, rects[other].xMax) - Mathf.Max(rects[index].xMin, rects[other].xMin);
                    var overlapY = Mathf.Min(rects[index].yMax, rects[other].yMax) - Mathf.Max(rects[index].yMin, rects[other].yMin);
                    Assert.That(overlapX <= 0.01f || overlapY <= 0.01f, Is.True, $"tiles {index} and {other} overlap");
                }
            }
        }

        /// <summary>
        /// Verifies tiles keep the input order and zero weights get no area.
        /// </summary>
        [Test]
        public void Squarify_KeepsInputOrder_AndGivesZeroWeightsNoArea()
        {
            var rects = TreemapLayout.Squarify(new double[] { 1, 0, 3 }, new Rect(0f, 0f, 100f, 100f));

            Assert.That(rects, Has.Length.EqualTo(3));
            Assert.That(rects[1].width * rects[1].height, Is.Zero);
            Assert.That(rects[2].width * rects[2].height, Is.EqualTo(7500f).Within(1f));
            Assert.That(rects[0].width * rects[0].height, Is.EqualTo(2500f).Within(1f));
        }

        /// <summary>
        /// Verifies tiles stay close to square instead of becoming thin slices.
        /// </summary>
        [Test]
        public void Squarify_KeepsEqualTilesCloseToSquare()
        {
            var rects = TreemapLayout.Squarify(Enumerable.Repeat(1d, 16).ToArray(), new Rect(0f, 0f, 400f, 400f));

            foreach (var rect in rects)
            {
                var aspect = Mathf.Max(rect.width, rect.height) / Mathf.Min(rect.width, rect.height);
                Assert.That(aspect, Is.LessThan(2f));
            }
        }
    }
}
