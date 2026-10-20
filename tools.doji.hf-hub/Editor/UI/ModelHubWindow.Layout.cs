using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    internal sealed partial class ModelHubWindow
    {
        private PaneLayout GetPaneLayout(Rect contentRect)
        {
            var maxListPaneWidth = Mathf.Max(MinListPaneWidth, contentRect.width - MinDetailsPaneWidth - SplitterWidth);
            _listPaneWidth = Mathf.Clamp(_listPaneWidth, MinListPaneWidth, maxListPaneWidth);

            var listRect = new Rect(contentRect.x, contentRect.y, _listPaneWidth, contentRect.height);
            var splitterRect = new Rect(listRect.xMax, contentRect.y, SplitterWidth, contentRect.height);
            var detailsRect = new Rect(splitterRect.xMax, contentRect.y, contentRect.xMax - splitterRect.xMax, contentRect.height);
            return new PaneLayout(listRect, splitterRect, detailsRect);
        }

        private void HandleSplitter(Rect contentRect, Rect splitterRect)
        {
            var currentEvent = Event.current;
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            switch (currentEvent.type)
            {
                case EventType.MouseDown when currentEvent.button == 0 && splitterRect.Contains(currentEvent.mousePosition):
                    _isDraggingSplitter = true;
                    currentEvent.Use();
                    break;
                case EventType.MouseDrag when _isDraggingSplitter:
                    // Manual splitter gives Package Manager-style pane resizing without relying on
                    // GUILayout widths that are hard to drag interactively in IMGUI.
                    var maxListPaneWidth = Mathf.Max(MinListPaneWidth, contentRect.width - MinDetailsPaneWidth - SplitterWidth);
                    _listPaneWidth = Mathf.Clamp(currentEvent.mousePosition.x - contentRect.x - (SplitterWidth * 0.5f), MinListPaneWidth, maxListPaneWidth);
                    Repaint();
                    currentEvent.Use();
                    break;
                case EventType.MouseUp when _isDraggingSplitter:
                    _isDraggingSplitter = false;
                    currentEvent.Use();
                    break;
            }
        }

        private static void DrawSplitter(Rect splitterRect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var lineRect = new Rect(splitterRect.x + ((splitterRect.width - 1f) * 0.5f), splitterRect.y, 1f, splitterRect.height);
            var color = EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.20f, 0.20f, 1f)
                : new Color(0.72f, 0.72f, 0.72f, 1f);
            EditorGUI.DrawRect(lineRect, color);
        }

        private static void DrawPaneBackgrounds(PaneLayout paneLayout)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            // Package Manager uses stronger pane contrast than default IMGUI layout. Fill backgrounds
            // explicitly so sidebar reads as separate navigation surface instead of one flat editor area.
            var listColor = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.17f, 0.17f, 1f)
                : new Color(0.82f, 0.82f, 0.82f, 1f);
            var detailsColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);

            EditorGUI.DrawRect(paneLayout.ListRect, listColor);
            EditorGUI.DrawRect(paneLayout.DetailsRect, detailsColor);
        }

        private readonly struct PaneLayout
        {
            public PaneLayout(Rect listRect, Rect splitterRect, Rect detailsRect)
            {
                ListRect = listRect;
                SplitterRect = splitterRect;
                DetailsRect = detailsRect;
            }

            public Rect ListRect { get; }
            public Rect SplitterRect { get; }
            public Rect DetailsRect { get; }
        }
    }
}
