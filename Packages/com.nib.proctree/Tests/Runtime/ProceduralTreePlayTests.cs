using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nib.ProcTree.PlayTests
{
    /// <summary>Play-loop behaviour: background generation, live growth sweep, cleanup (spec §8).</summary>
    public class ProceduralTreePlayTests
    {
        GameObject _go;
        TreeProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TreeProfile>();
            _profile.parameters.years = 25;          // smaller tree keeps the test quick
            _profile.parameters.markerCount = 8000;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            Object.Destroy(_profile);
        }

        IEnumerator SpawnAndWait()
        {
            _go = new GameObject("Test Oak");
            _go.SetActive(false);
            var tree = _go.AddComponent<ProceduralTree>();
            tree.profile = _profile;
            tree.growth = 1f;
            _go.SetActive(true);                     // OnEnable starts the background generation
            float timeout = Time.realtimeSinceStartup + 20f;
            while ((tree.IsGenerating || tree.Bake == null) && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.IsNotNull(tree.Bake, "background generation did not finish");
        }

        [UnityTest]
        public IEnumerator Generates_InBackground_AndDrawsMeshes()
        {
            yield return SpawnAndWait();
            Assert.IsNotNull(_go.transform.Find("Bark").GetComponent<MeshFilter>().sharedMesh);
            Assert.IsNotNull(_go.transform.Find("Leaves").GetComponent<MeshFilter>().sharedMesh);
        }

        [UnityTest]
        public IEnumerator GrowthSweep_ZeroToOne_NoErrors_LeavesFollow()
        {
            yield return SpawnAndWait();
            var tree = _go.GetComponent<ProceduralTree>();
            tree.growth = 0f;
            yield return null;
            Assert.AreEqual(0, tree.LiveLeafCount, "growth 0 = nothing");
            int maxLeaves = 0;
            for (float g = 0f; g <= 1f; g += 0.02f)
            {
                tree.growth = g;
                yield return null;
                maxLeaves = Mathf.Max(maxLeaves, tree.LiveLeafCount);
            }
            Assert.Greater(maxLeaves, 100);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Disable_ReleasesMeshes()
        {
            yield return SpawnAndWait();
            _go.SetActive(false);
            yield return null;
            Assert.IsNull(_go.transform.Find("Bark").GetComponent<MeshFilter>().sharedMesh);
            Assert.IsNull(_go.GetComponent<ProceduralTree>().Bake);
        }

        [UnityTest]
        public IEnumerator Regenerate_KeepsOldTreeUntilSwap()
        {
            yield return SpawnAndWait();
            var tree = _go.GetComponent<ProceduralTree>();
            var bark = _go.transform.Find("Bark").GetComponent<MeshFilter>();
            tree.seed = 77;
            tree.Regenerate();
            while (tree.IsGenerating)
            {
                Assert.IsNotNull(bark.sharedMesh, "never a frame with nothing drawn");
                yield return null;
            }
            Assert.IsNotNull(bark.sharedMesh);
        }

        [UnityTest]
        public IEnumerator WindDisabled_IsStillAir()
        {
            var wind = new GameObject("Wind").AddComponent<TreeWind>();
            wind.strength = 0.5f;
            yield return null;
            Assert.Greater(Shader.GetGlobalFloat("_TreeWindStrength"), 0f);
            wind.enabled = false;
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_TreeWindStrength"));
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_TreeLeafFlutter"));
            Object.Destroy(wind.gameObject);
        }
    }
}
