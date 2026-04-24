using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    internal sealed partial class ModelHubWindow
    {
        private void DrawOptionalLinkRow(string label, string value, Action<string> action)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                // Long absolute paths force details pane to grow horizontally, which can clip header badge.
                // Show compact display values here and keep full path in tooltip.
                var displayValue = GetPathDisplayValue(label, value);
                var buttonContent = new GUIContent(displayValue, value);
                EditorGUILayout.PrefixLabel(label);
                if (GUILayout.Button(buttonContent, _pathLinkStyle))
                {
                    action(value);
                }
            }
        }

        private static string GetPathDisplayValue(string label, string value)
        {
            if (string.Equals(label, Content.ManifestLabel, StringComparison.Ordinal))
            {
                return GetManifestDisplayValue(value);
            }

            var fileName = Path.GetFileName(value);
            return string.IsNullOrWhiteSpace(fileName) ? value : fileName;
        }

        private static string GetManifestDisplayValue(string value)
        {
            var normalizedValue = value.Replace('\\', '/');

            var assetsIndex = normalizedValue.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                return $"../{normalizedValue.Substring(assetsIndex)}";
            }

            var packagesIndex = normalizedValue.IndexOf("Packages/", StringComparison.OrdinalIgnoreCase);
            if (packagesIndex >= 0)
            {
                return $"../{normalizedValue.Substring(packagesIndex)}";
            }

            var fileName = Path.GetFileName(normalizedValue);
            return string.IsNullOrWhiteSpace(fileName) ? normalizedValue : $"../{fileName}";
        }

        private static string FormatByteSize(long? bytes)
        {
            if (!bytes.HasValue || bytes.Value < 0)
            {
                return null;
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes.Value;
            var unitIndex = 0;
            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:0} {units[unitIndex]}"
                : $"{size:0.0} {units[unitIndex]}";
        }

        private static MessageType GetDiagnosticMessageType(ModelHubDiagnostic diagnostic)
        {
            return diagnostic?.Severity switch
            {
                "error" => MessageType.Error,
                "warning" => MessageType.Warning,
                _ => MessageType.Info
            };
        }
    }
}
