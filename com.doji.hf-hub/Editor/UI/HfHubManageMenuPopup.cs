using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Draws the custom Manage popup so each action can expose tooltip text and enabled state.
    /// </summary>
    internal sealed class HfHubManageMenuPopup : PopupWindowContent
    {
        private const float PopupWidth = 220f;
        private const float RowHeight = 22f;
        private readonly Entry[] _entries;
        private GUIStyle _rowStyle;

        /// <summary>
        /// Carries one popup action row together with UI state needed for drawing and invocation.
        /// </summary>
        internal readonly struct Entry
        {
            public Entry(string label, string tooltip, bool isEnabled, Action action)
            {
                Label = label;
                Tooltip = tooltip;
                IsEnabled = isEnabled;
                Action = action;
            }

            public string Label { get; }

            public string Tooltip { get; }

            public bool IsEnabled { get; }

            public Action Action { get; }
        }

        public HfHubManageMenuPopup(IReadOnlyList<Entry> entries)
        {
            _entries = entries?.ToArray() ?? Array.Empty<Entry>();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(PopupWidth, Mathf.Max(RowHeight, _entries.Length * RowHeight));
        }

        public override void OnGUI(Rect rect)
        {
            InitializeStyles();
            using (new GUILayout.AreaScope(rect))
            {
                for (var index = 0; index < _entries.Length; index++)
                {
                    var entry = _entries[index];
                    using (new EditorGUI.DisabledScope(!entry.IsEnabled))
                    {
                        if (GUILayout.Button(
                                new GUIContent(entry.Label, entry.Tooltip),
                                _rowStyle,
                                GUILayout.ExpandWidth(true),
                                GUILayout.Height(RowHeight)))
                        {
                            editorWindow.Close();
                            entry.Action?.Invoke();
                        }
                    }
                }
            }
        }

        internal static IReadOnlyList<Entry> BuildEntries(
            bool hasCache,
            Action removeFromProject,
            Action removeFromCache,
            string removeFromProjectLabel,
            string removeFromProjectTooltip,
            string removeFromCacheLabel,
            string removeFromCacheTooltip)
        {
            return new[]
            {
                new Entry(removeFromProjectLabel, removeFromProjectTooltip, true, removeFromProject),
                new Entry(removeFromCacheLabel, removeFromCacheTooltip, hasCache, removeFromCache)
            };
        }

        private void InitializeStyles()
        {
            _rowStyle ??= new GUIStyle(EditorStyles.toolbarButton)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                stretchWidth = true
            };
            _rowStyle.fixedHeight = RowHeight;
        }
    }
}
