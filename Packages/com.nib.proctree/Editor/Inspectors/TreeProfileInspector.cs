using Nib.EditorTheme;
using Nib.ProcTree.Core;
using UnityEditor;
using UnityEngine.UIElements;

namespace Nib.ProcTree.Editor
{
    /// <summary>
    /// Grouped UI Toolkit inspector for <see cref="TreeProfile"/>. It lists the scalar parameters
    /// and the editable AnimationCurves; the sampled-curve arrays inside
    /// <see cref="TreeParams"/> are internal snapshots and are deliberately not shown.
    /// </summary>
    [CustomEditor(typeof(TreeProfile))]
    public sealed class TreeProfileInspector : UnityEditor.Editor
    {
        const string Dir = "Packages/com.nib.proctree/Editor/Inspectors/";

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            NibTheme.Apply(root, NibFamily.Botanical);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Dir + "TreeProfileInspector.uxml");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Dir + "TreeProfileInspector.uss");
            if (tree == null) { root.Add(new HelpBox("TreeProfileInspector.uxml missing.", HelpBoxMessageType.Error)); return root; }
            tree.CloneTree(root);
            if (sheet != null) root.styleSheets.Add(sheet);

            root.Q<Button>("reset").clicked += () =>
            {
                var p = (TreeProfile)target;
                Undo.RecordObject(p, "Reset Tree Profile");
                var fresh = UnityEngine.ScriptableObject.CreateInstance<TreeProfile>();
                fresh.name = p.name;            // CopySerialized copies the name too
                EditorUtility.CopySerialized(fresh, p);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(p);
            };
            return root;
        }
    }
}
