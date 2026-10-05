using NUnit.Framework;
using Nib.ProcTree.Core;
using Nib.ProcTree.Core.Meshing;
using Nib.ProcTree.Editor;
using Nib.ProcTree.Rendering;
using UnityEditor;
using UnityEngine;

namespace Nib.ProcTree.Tests
{
    /// <summary>Unity-side EditMode tests (need the editor; the pure Core suite lives in Core/).</summary>
    public class ProcTreeEditorTests
    {
        [Test]
        public void Profile_YearFor_ZeroIsNothing_OneIsMature_Monotonic()
        {
            var p = ScriptableObject.CreateInstance<TreeProfile>();
            Assert.AreEqual(0f, p.YearFor(0f, 80));
            Assert.AreEqual(80f, p.YearFor(1f, 80), 1e-3f);
            float prev = 0f;
            for (float g = 0f; g <= 1f; g += 0.01f)
            {
                float y = p.YearFor(g, 80);
                Assert.GreaterOrEqual(y + 1e-4f, prev);
                prev = y;
            }
            Assert.Less(p.YearFor(0.5f, 80), 40f, "front-loaded: half the slider is less than half the life");
            Object.DestroyImmediate(p);
        }

        [Test]
        public void Profile_ToParams_SnapshotsCurves()
        {
            var p = ScriptableObject.CreateInstance<TreeProfile>();
            p.heightByAge = AnimationCurve.Constant(0f, 1f, 0.42f);
            var snap = p.ToParams();
            Assert.AreEqual(0.42f, snap.heightByAge.Evaluate(0.3f), 1e-4f);
            Assert.AreNotEqual(p.ToParams().ComputeHash(), TreeParams.OakParkland().ComputeHash());
            Object.DestroyImmediate(p);
        }

        [Test]
        public void Menu_PlacesAWorkingOak()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ProcTreeSetup.OakPrefabPath) == null)
                Assert.Ignore("Oak prefab not generated yet — run Tools ▸ Proc Tree ▸ Setup Project.");
            var go = ProcTreeMenu.CreateOak();
            try
            {
                var tree = go.GetComponent<ProceduralTree>();
                Assert.IsNotNull(tree);
                Assert.IsNotNull(tree.profile, "placed oak must have its profile (PACKAGE_STANDARDS §4)");
                Assert.IsNotNull(tree.barkMaterial);
                Assert.IsNotNull(tree.leafMaterial);
                Assert.IsNotNull(go.transform.Find("Bark")?.GetComponent<MeshRenderer>());
                Assert.IsNotNull(go.transform.Find("Leaves")?.GetComponent<MeshRenderer>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Shaders_Exist()
        {
            Assert.IsNotNull(Shader.Find(TreeMaterialFactory.BarkShader), "ProcTree/Bark failed to compile or import");
            Assert.IsNotNull(Shader.Find(TreeMaterialFactory.LeafShader), "ProcTree/Leaf failed to compile or import");
        }
    }
}
