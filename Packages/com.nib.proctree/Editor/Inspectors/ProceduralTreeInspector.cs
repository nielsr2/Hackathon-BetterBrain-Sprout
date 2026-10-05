using Nib.EditorTheme;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nib.ProcTree.Editor
{
    /// <summary>
    /// UI Toolkit inspector for <see cref="ProceduralTree"/>: a big growth slider with a live
    /// "year N of M" readout, seed dice, regenerate + progress, and bake stats.
    /// </summary>
    [CustomEditor(typeof(ProceduralTree))]
    public sealed class ProceduralTreeInspector : UnityEditor.Editor
    {
        const string Dir = "Packages/com.nib.proctree/Editor/Inspectors/";

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            NibTheme.Apply(root, NibFamily.Botanical);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Dir + "ProceduralTreeInspector.uxml");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Dir + "ProceduralTreeInspector.uss");
            if (tree == null) { root.Add(new HelpBox("ProceduralTreeInspector.uxml missing.", HelpBoxMessageType.Error)); return root; }
            tree.CloneTree(root);
            if (sheet != null) root.styleSheets.Add(sheet);

            var t = (ProceduralTree)target;
            var status = root.Q<StatusPill>("status");
            var readout = root.Q<Label>("year-readout");
            var stats = root.Q<Label>("stats");
            var progress = root.Q<ProgressBar>("progress");

            root.Q<Button>("regenerate").clicked += () => { foreach (var o in targets) ((ProceduralTree)o).Regenerate(); };
            root.Q<Button>("dice").clicked += () =>
            {
                foreach (var o in targets)
                {
                    var pt = (ProceduralTree)o;
                    Undo.RecordObject(pt, "Random Tree Seed");
                    pt.seed = Random.Range(1, 1_000_000);
                    EditorUtility.SetDirty(pt);
                    pt.Regenerate();
                }
            };

            root.schedule.Execute(() => Refresh(t, status, readout, stats, progress)).Every(100);
            Refresh(t, status, readout, stats, progress);
            return root;
        }

        static void Refresh(ProceduralTree t, StatusPill status, Label readout, Label stats, ProgressBar progress)
        {
            if (t == null) return;
            progress.style.display = t.IsGenerating ? DisplayStyle.Flex : DisplayStyle.None;
            progress.value = t.Progress;

            if (t.profile == null) { status.text = "no profile"; status.Level = StatusPillLevel.Error; }
            else if (t.IsGenerating) { status.text = "growing…"; status.Level = StatusPillLevel.Warn; }
            else if (t.Bake == null) { status.text = "empty"; status.Level = StatusPillLevel.Off; }
            else { status.text = "ready"; status.Level = StatusPillLevel.Ok; }

            var b = t.Bake;
            if (b == null) { readout.text = "—"; stats.text = "Nothing generated yet."; return; }
            readout.text = t.growth <= 0f ? "nothing yet (growth 0)" : $"year {t.Year:0.0} of {b.years}  ·  {t.LiveLeafCount:N0} leaves";
            stats.text = $"{b.segments.Length:N0} segments ever  ·  {b.leaves.Length:N0} leaves ever (max {b.maxLiveLeaves:N0} at once)\n" +
                         $"{b.bark.centerline.Length:N0} bark vertices  ·  generated in {t.LastGenerateMs} ms";
        }
    }
}
