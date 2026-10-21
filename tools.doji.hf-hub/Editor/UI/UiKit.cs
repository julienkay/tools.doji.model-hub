using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Visual tone shared by status pills, dots, and callouts.
    /// </summary>
    internal enum UiTone
    {
        Neutral,
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Small factory helpers that build HF Hub UI Toolkit elements with shared USS classes.
    /// </summary>
    internal static class UiKit
    {
        internal const string StylesFolder = "Packages/tools.doji.hf-hub/Editor/UI/Styles/";
        internal const string SharedStyleSheet = "HfHub.uss";

        /// <summary>
        /// Adds shared and surface-specific style sheets plus the current editor theme class.
        /// </summary>
        public static void ApplyTheme(VisualElement root, params string[] styleSheets)
        {
            AddStyleSheet(root, SharedStyleSheet);
            foreach (var styleSheet in styleSheets ?? Array.Empty<string>())
            {
                AddStyleSheet(root, styleSheet);
            }

            root.AddToClassList("hf-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "hf-dark" : "hf-light");
        }

        private static void AddStyleSheet(VisualElement root, string fileName)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesFolder + fileName);
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        public static VisualElement Element(params string[] classes)
        {
            var element = new VisualElement();
            AddClasses(element, classes);
            return element;
        }

        public static VisualElement Row(params string[] classes)
        {
            var row = Element(classes);
            row.AddToClassList("hf-row");
            return row;
        }

        public static VisualElement Spacer()
        {
            return Element("hf-spacer");
        }

        public static Label Text(string text, params string[] classes)
        {
            var label = new Label(text ?? string.Empty);
            AddClasses(label, classes);
            return label;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("hf-button");
            AddClasses(button, classes);
            return button;
        }

        public static Button PrimaryButton(string text, Action onClick, params string[] classes)
        {
            var button = Button(text, onClick, classes);
            button.AddToClassList("hf-button--primary");
            return button;
        }

        public static Button IconButton(string iconName, string tooltip, Action onClick)
        {
            var button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("hf-icon-button");
            button.Add(Icon(iconName));
            return button;
        }

        public static Button LinkButton(string text, Action onClick, string tooltip = null)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.AddToClassList("hf-link");
            return button;
        }

        /// <summary>
        /// Builds an icon element from one built-in editor icon name. Missing icons render as empty space.
        /// </summary>
        public static VisualElement Icon(string iconName, params string[] classes)
        {
            var icon = Element("hf-icon");
            AddClasses(icon, classes);
            var texture = LoadIcon(iconName);
            if (texture != null)
            {
                icon.style.backgroundImage = new StyleBackground(texture);
            }

            return icon;
        }

        public static Texture2D LoadIcon(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return null;
            }

            var previousLogging = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                var themedName = EditorGUIUtility.isProSkin && !iconName.StartsWith("d_", StringComparison.Ordinal)
                    ? "d_" + iconName
                    : iconName;
                var texture = EditorGUIUtility.IconContent(themedName)?.image as Texture2D;
                return texture != null ? texture : EditorGUIUtility.IconContent(iconName)?.image as Texture2D;
            }
            finally
            {
                Debug.unityLogger.logEnabled = previousLogging;
            }
        }

        public static VisualElement Pill(UiTone tone, string text, string tooltip = null)
        {
            var pill = Element("hf-pill", ToneClass(tone));
            pill.tooltip = tooltip;
            pill.Add(Element("hf-pill__dot"));
            pill.Add(Text(text, "hf-pill__label"));
            return pill;
        }

        public static VisualElement Dot(UiTone tone, string tooltip = null)
        {
            var dot = Element("hf-dot", ToneClass(tone));
            dot.tooltip = tooltip;
            return dot;
        }

        public static VisualElement Tag(string text, string tooltip = null)
        {
            var tag = Text(text, "hf-tag");
            tag.tooltip = tooltip;
            return tag;
        }

        public static VisualElement Callout(UiTone tone, string title, string message, params Button[] actions)
        {
            var callout = Element("hf-callout", ToneClass(tone));
            var body = Element("hf-callout__body");
            if (!string.IsNullOrWhiteSpace(title))
            {
                body.Add(Text(title, "hf-callout__title"));
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                body.Add(Text(message, "hf-callout__message"));
            }

            if (actions != null && actions.Length > 0)
            {
                var actionRow = Element("hf-callout__actions");
                for (var index = 0; index < actions.Length; index++)
                {
                    if (index == 0)
                    {
                        actions[index].AddToClassList("hf-button--first");
                    }

                    actionRow.Add(actions[index]);
                }

                body.Add(actionRow);
            }

            callout.Add(body);
            return callout;
        }

        /// <summary>
        /// Builds a titled section. Add content to the returned element.
        /// </summary>
        public static VisualElement Section(string title, string hint = null, VisualElement trailing = null)
        {
            var section = Element("hf-section");
            var header = Element("hf-section__header");
            header.Add(Text(title, "hf-heading"));
            header.Add(Spacer());
            if (trailing != null)
            {
                header.Add(trailing);
            }

            section.Add(header);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                section.Add(Text(hint, "hf-section__hint"));
            }

            return section;
        }

        public static VisualElement KeyValue(string key, string value, string tooltip = null)
        {
            var valueLabel = Text(value);
            valueLabel.tooltip = tooltip;
            return KeyValue(key, valueLabel);
        }

        public static VisualElement KeyValue(string key, VisualElement value)
        {
            var row = Element("hf-kv");
            row.Add(Text(key, "hf-kv__key"));
            var valueContainer = Element("hf-kv__value");
            valueContainer.Add(value);
            row.Add(valueContainer);
            return row;
        }

        public static VisualElement Divider()
        {
            return Element("hf-divider");
        }

        public static TextField SearchField(string placeholder, Action<string> onChanged)
        {
            var field = new TextField();
            field.AddToClassList("hf-search");
            field.textEdition.placeholder = placeholder;
            field.textEdition.hidePlaceholderOnFocus = false;
            field.Add(Icon("Search Icon", "hf-search__icon"));
            field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return field;
        }

        public static VisualElement EmptyState(string iconName, string title, string message, params Button[] actions)
        {
            var empty = Element("hf-empty");
            if (!string.IsNullOrWhiteSpace(iconName))
            {
                empty.Add(Icon(iconName, "hf-empty__icon"));
            }

            empty.Add(Text(title, "hf-empty__title"));
            if (!string.IsNullOrWhiteSpace(message))
            {
                empty.Add(Text(message, "hf-empty__message"));
            }

            if (actions != null && actions.Length > 0)
            {
                var row = Row();
                for (var index = 0; index < actions.Length; index++)
                {
                    if (index == 0)
                    {
                        actions[index].AddToClassList("hf-button--first");
                    }

                    row.Add(actions[index]);
                }

                empty.Add(row);
            }

            return empty;
        }

        public static string ToneClass(UiTone tone)
        {
            return tone switch
            {
                UiTone.Info => "hf-tone-info",
                UiTone.Success => "hf-tone-success",
                UiTone.Warning => "hf-tone-warning",
                UiTone.Error => "hf-tone-error",
                _ => "hf-tone-neutral"
            };
        }

        public static void SetVisible(VisualElement element, bool visible)
        {
            element?.EnableInClassList("hf-hidden", !visible);
        }

        /// <summary>
        /// Formats a byte count with binary units, for example <c>66.4 MB</c>.
        /// </summary>
        public static string FormatBytes(long? bytes)
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
                : $"{size:0.#} {units[unitIndex]}";
        }

        public static string RevealInFileBrowserLabel =>
            Application.platform switch
            {
                RuntimePlatform.OSXEditor => "Show in Finder",
                RuntimePlatform.WindowsEditor => "Show in Explorer",
                _ => "Open Folder"
            };

        private static void AddClasses(VisualElement element, string[] classes)
        {
            if (classes == null)
            {
                return;
            }

            foreach (var className in classes)
            {
                if (!string.IsNullOrWhiteSpace(className))
                {
                    element.AddToClassList(className);
                }
            }
        }
    }
}
