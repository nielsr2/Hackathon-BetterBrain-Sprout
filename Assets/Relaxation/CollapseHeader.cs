using UnityEngine;

namespace Relaxation
{
    /// <summary>IMGUI panel header: "▾ Title  summary" row that toggles a collapsed flag when clicked.</summary>
    public static class CollapseHeader
    {
        static GUIStyle _style;

        /// <summary>Draws the header; returns true when the panel body should be drawn.</summary>
        /// <param name="summary">Shown only while collapsed, so the panel still says something useful.</param>
        public static bool Draw(ref bool collapsed, string title, string summary = null)
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            string text = (collapsed ? "▸ " : "▾ ") + title;
            if (collapsed && !string.IsNullOrEmpty(summary)) text += "   " + summary;
            if (GUILayout.Button(text, _style)) collapsed = !collapsed;
            return !collapsed;
        }
    }
}
