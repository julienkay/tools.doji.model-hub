using System;
using System.IO;
using System.Linq;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Storage tab: disk usage in the shared download cache and in this project, per file.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private static void BuildStorageTab(VisualElement content, ModelHubItem item)
        {
            var stats = UiKit.Element("mh-stats");
            stats.Add(BuildStorageStat(
                "Download cache",
                "mh-stat__swatch--cache",
                item.CacheSizeOnDiskBytes,
                item.CacheSizeOnDiskBytes.HasValue ? "Shared by all projects on this machine" : "Not downloaded on this machine",
                item.RawSnapshotRootPath,
                UiKit.RevealInFileBrowserLabel,
                () => ProjectSettingsLinks.RevealPath(item.RawSnapshotRootPath),
                isFirst: true));
            stats.Add(BuildStorageStat(
                "Converted",
                "mh-stat__swatch--project",
                item.RuntimeSizeOnDiskBytes,
                item.RuntimeSizeOnDiskBytes.HasValue
                    ? "Sentis models converted from ONNX, shared by all projects"
                    : item.RuntimeFiles.Count > 0 ? "Uses the downloaded Sentis files as they are" : "Not imported yet",
                item.RuntimeModelFolder,
                UiKit.RevealInFileBrowserLabel,
                () => ProjectSettingsLinks.RevealPath(item.RuntimeModelFolder),
                isFirst: false));
            content.Add(stats);

            var files = item.Files
                .Where(file => file.RawSizeOnDiskBytes.GetValueOrDefault() > 0 || file.RuntimeSizeOnDiskBytes.GetValueOrDefault() > 0)
                .OrderByDescending(file => file.RawSizeOnDiskBytes.GetValueOrDefault() + file.RuntimeSizeOnDiskBytes.GetValueOrDefault())
                .ToArray();
            if (files.Length == 0)
            {
                return;
            }

            var maxBytes = Math.Max(
                1L,
                files.Max(file => Math.Max(file.RawSizeOnDiskBytes.GetValueOrDefault(), file.RuntimeSizeOnDiskBytes.GetValueOrDefault())));
            var section = UiKit.Section("By file");
            var head = UiKit.Element("mh-storage-head");
            head.Add(UiKit.Text("FILE", "mh-storage-row__name"));
            head.Add(UiKit.Text("CACHE", "mh-storage-row__bar"));
            head.Add(UiKit.Text("PROJECT", "mh-storage-row__bar"));
            section.Add(head);
            foreach (var file in files)
            {
                var row = UiKit.Element("mh-storage-row");
                var name = UiKit.Text(Path.GetFileName(file.RepoPath), "mh-storage-row__name");
                name.tooltip = file.RepoPath;
                row.Add(name);
                row.Add(BuildStorageBar(file.RawSizeOnDiskBytes, maxBytes, "mh-storage-row__fill--cache"));
                row.Add(BuildStorageBar(file.RuntimeSizeOnDiskBytes, maxBytes, "mh-storage-row__fill--project"));
                section.Add(row);
            }

            content.Add(section);
        }

        private static VisualElement BuildStorageStat(
            string title,
            string swatchClass,
            long? bytes,
            string detail,
            string path,
            string actionLabel,
            Action action,
            bool isFirst)
        {
            var card = UiKit.Element("hf-card", "mh-stat");
            card.EnableInClassList("mh-stat--first", isFirst);
            var header = UiKit.Element("mh-stat__header");
            header.Add(UiKit.Element("mh-stat__swatch", swatchClass));
            header.Add(UiKit.Text(title.ToUpperInvariant(), "hf-overline"));
            card.Add(header);
            card.Add(UiKit.Text(UiKit.FormatBytes(bytes) ?? "—", "mh-stat__value"));
            var detailLabel = UiKit.Text(detail, "mh-stat__detail");
            detailLabel.tooltip = path;
            card.Add(detailLabel);
            if (!string.IsNullOrWhiteSpace(path))
            {
                card.Add(UiKit.LinkButton(actionLabel, action, path));
            }

            return card;
        }

        private static VisualElement BuildStorageBar(long? bytes, long maxBytes, string fillClass)
        {
            var bar = UiKit.Element("mh-storage-row__bar");
            var track = UiKit.Element("mh-storage-row__track");
            var fill = UiKit.Element("mh-storage-row__fill", fillClass);
            fill.style.width = Length.Percent(Math.Min(100f, 100f * bytes.GetValueOrDefault() / maxBytes));
            track.Add(fill);
            bar.Add(track);
            bar.Add(UiKit.Text(UiKit.FormatBytes(bytes) ?? "—", "mh-storage-row__size"));
            return bar;
        }
    }
}
