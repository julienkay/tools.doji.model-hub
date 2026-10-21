using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Squarified treemap layout (Bruls, Huizing, van Wijk): splits a rectangle into tiles whose areas match their
    /// weights while keeping tiles close to square, so sizes stay comparable at a glance.
    /// </summary>
    internal static class TreemapLayout
    {
        /// <summary>
        /// Returns one rectangle per weight, in input order. Zero or negative weights get an empty rectangle.
        /// </summary>
        public static Rect[] Squarify(IReadOnlyList<double> weights, Rect bounds)
        {
            var result = new Rect[weights?.Count ?? 0];
            if (result.Length == 0 || bounds.width <= 0f || bounds.height <= 0f)
            {
                return result;
            }

            var order = Enumerable.Range(0, result.Length)
                .Where(index => weights[index] > 0d)
                .OrderByDescending(index => weights[index])
                .ToArray();
            var total = order.Sum(index => weights[index]);
            if (order.Length == 0 || total <= 0d)
            {
                return result;
            }

            var scale = bounds.width * (double)bounds.height / total;
            var remaining = bounds;
            var start = 0;
            while (start < order.Length)
            {
                var side = Math.Min(remaining.width, remaining.height);
                var end = start + 1;
                var rowArea = weights[order[start]] * scale;
                var worst = Worst(weights[order[start]] * scale, weights[order[start]] * scale, rowArea, side);
                while (end < order.Length)
                {
                    var area = weights[order[end]] * scale;
                    var candidate = Worst(weights[order[start]] * scale, area, rowArea + area, side);
                    if (candidate > worst)
                    {
                        break;
                    }

                    worst = candidate;
                    rowArea += area;
                    end++;
                }

                remaining = PlaceRow(order, start, end, weights, scale, rowArea, remaining, result);
                start = end;
            }

            return result;
        }

        /// <summary>
        /// Worst aspect ratio of a row whose largest tile is <paramref name="largest"/> and smallest is
        /// <paramref name="smallest"/>, laid along a side of length <paramref name="side"/>.
        /// </summary>
        private static double Worst(double largest, double smallest, double rowArea, double side)
        {
            var sideSquared = side * side;
            var areaSquared = rowArea * rowArea;
            return Math.Max(sideSquared * largest / areaSquared, areaSquared / (sideSquared * smallest));
        }

        private static Rect PlaceRow(
            int[] order,
            int start,
            int end,
            IReadOnlyList<double> weights,
            double scale,
            double rowArea,
            Rect remaining,
            Rect[] result)
        {
            if (remaining.width >= remaining.height)
            {
                // Column along the left edge.
                var columnWidth = (float)(rowArea / remaining.height);
                var y = remaining.y;
                for (var index = start; index < end; index++)
                {
                    var height = index == end - 1
                        ? remaining.yMax - y
                        : (float)(weights[order[index]] * scale / columnWidth);
                    result[order[index]] = new Rect(remaining.x, y, columnWidth, height);
                    y += height;
                }

                return new Rect(remaining.x + columnWidth, remaining.y, Math.Max(0f, remaining.width - columnWidth), remaining.height);
            }

            // Row along the top edge.
            var rowHeight = (float)(rowArea / remaining.width);
            var x = remaining.x;
            for (var index = start; index < end; index++)
            {
                var width = index == end - 1
                    ? remaining.xMax - x
                    : (float)(weights[order[index]] * scale / rowHeight);
                result[order[index]] = new Rect(x, remaining.y, width, rowHeight);
                x += width;
            }

            return new Rect(remaining.x, remaining.y + rowHeight, remaining.width, Math.Max(0f, remaining.height - rowHeight));
        }
    }
}
