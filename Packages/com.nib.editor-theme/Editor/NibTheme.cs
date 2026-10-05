using UnityEditor;
using UnityEngine.UIElements;

namespace Nib.EditorTheme
{
    /// <summary>Which Nib tool family a window belongs to; selects its single accent color.</summary>
    public enum NibFamily
    {
        /// <summary>Grown/scattered geometry tools (petals, ferns, teeth, fireflies, sun).</summary>
        Botanical,
        /// <summary>Look-shaping tools (light composer, lens flares, craquelure, palettes).</summary>
        Light,
        /// <summary>Hardware and stream tools (MIDI, OSC, tavle).</summary>
        Signal,
    }

    /// <summary>
    /// Entry point for the shared Nib editor theme. Call <see cref="Apply"/> on a window's
    /// root element; it attaches the token and control stylesheets and the family accent class.
    /// Loading is fail-soft: if the theme package is missing, the UI still works unthemed.
    /// </summary>
    public static class NibTheme
    {
        const string StylesPath = "Packages/com.nib.editor-theme/Editor/Styles/";

        /// <summary>Root class carrying the design tokens.</summary>
        public const string RootClass = "nib-root";

        /// <summary>Applies Nib tokens, shared control styles, and the family accent to a root element.</summary>
        public static void Apply(VisualElement root, NibFamily family)
        {
            if (root == null)
                return;

            root.AddToClassList(RootClass);
            if (!EditorGUIUtility.isProSkin)
                root.AddToClassList("nib-root--light");

            root.AddToClassList(family switch
            {
                NibFamily.Botanical => "nib-fam-botanical",
                NibFamily.Light => "nib-fam-light",
                _ => "nib-fam-signal",
            });

            AddSheet(root, StylesPath + "Variables.uss");
            AddSheet(root, StylesPath + "Controls.uss");
        }

        static void AddSheet(VisualElement root, string path)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null && !root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);
        }
    }
}
