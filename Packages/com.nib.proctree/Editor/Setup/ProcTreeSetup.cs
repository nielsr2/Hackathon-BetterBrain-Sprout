using System.IO;
using Nib.ProcTree.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if PROCTREE_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace Nib.ProcTree.Editor
{
    /// <summary>
    /// Setup Project: from nothing, builds the Oak profile, textures, materials, the Oak prefab and
    /// the Growth Demo sample scene — all in code, batchmode-safe (<see cref="Run"/>). Mirrors
    /// ProcFoliage's ProjectSetup, including its lessons (reload assets after NewScene, relocate
    /// the sample scene into Samples~ because Unity cannot save into a '~' folder).
    /// </summary>
    public static class ProcTreeSetup
    {
        /// <summary>Package asset root.</summary>
        public const string PackageRoot = "Packages/com.nib.proctree";
        /// <summary>Generated presets (profile, materials).</summary>
        public const string PresetsFolder = PackageRoot + "/Presets";
        /// <summary>Generated textures.</summary>
        public const string TexturesFolder = PresetsFolder + "/Textures";
        /// <summary>Prefab folder.</summary>
        public const string PrefabFolder = PackageRoot + "/Runtime/Prefabs";
        /// <summary>The Oak prefab.</summary>
        public const string OakPrefabPath = PrefabFolder + "/Oak.prefab";
        /// <summary>The Oak profile asset.</summary>
        public const string OakProfilePath = PresetsFolder + "/Oak.asset";
        const string BarkMatPath = PresetsFolder + "/Oak Bark.mat";
        const string LeafMatPath = PresetsFolder + "/Oak Leaf.mat";
        const string DemoSceneName = "GrowthDemo";

        /// <summary>Runs the full setup.</summary>
        [MenuItem("Tools/Proc Tree/Setup Project")]
        public static void Run()
        {
            if (!IsHdrpActive(out var msg))
            {
                Debug.LogError("[ProcTree] Setup Project aborted — " + msg);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Proc Tree — HDRP required", msg, "OK");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            CreateAssets();
            CreatePrefab();
            CreateSampleScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProcTree] Setup Project complete — presets in " + PresetsFolder + ", prefab " + OakPrefabPath +
                      ", sample in Samples~/GrowthDemo.");
        }

        /// <summary>Textures, profile and materials (overwritten in place, GUIDs preserved).</summary>
        public static void CreateAssets()
        {
            EnsureFolder(PresetsFolder);
            EnsureFolder(TexturesFolder);
            var tex = TreeTextureGenerator.GenerateInto(TexturesFolder);

            var profile = AssetDatabase.LoadAssetAtPath<TreeProfile>(OakProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<TreeProfile>();
                AssetDatabase.CreateAsset(profile, OakProfilePath);
            }
            else
            {
                var fresh = ScriptableObject.CreateInstance<TreeProfile>();
                fresh.name = profile.name;
                EditorUtility.CopySerialized(fresh, profile);
                Object.DestroyImmediate(fresh);
            }
            EditorUtility.SetDirty(profile);

            SaveMaterial(BarkMatPath, TreeMaterialFactory.CreateBark(tex.barkAlbedo, tex.barkNormal));
            SaveMaterial(LeafMatPath, TreeMaterialFactory.CreateLeaf(tex.leafAlbedo, tex.leafNormal, tex.leafThickness));
            AssetDatabase.SaveAssets();
        }

        /// <summary>The Oak prefab: ProceduralTree + its Bark/Leaves renderers + TreeWind, no mesh (the seed is the asset).</summary>
        public static void CreatePrefab()
        {
            EnsureFolder(PrefabFolder);
            var go = new GameObject("Oak");
            // Renderers ship in the prefab so instantiating it adds no components (the Inspector
            // NRE lesson from ProcFoliage's c182f28).
            foreach (var child in new[] { "Bark", "Leaves" })
            {
                var c = new GameObject(child);
                c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>();
                var mr = c.AddComponent<MeshRenderer>();
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            }
            var tree = go.AddComponent<ProceduralTree>();          // OnEnable: no profile yet → builds nothing
            tree.profile = AssetDatabase.LoadAssetAtPath<TreeProfile>(OakProfilePath);
            tree.barkMaterial = AssetDatabase.LoadAssetAtPath<Material>(BarkMatPath);
            tree.leafMaterial = AssetDatabase.LoadAssetAtPath<Material>(LeafMatPath);
            tree.seed = 1;
            tree.growth = 1f;
            go.AddComponent<TreeWind>();
            PrefabUtility.SaveAsPrefabAsset(go, OakPrefabPath);
            Object.DestroyImmediate(go);
        }

        /// <summary>Builds Samples~/GrowthDemo/GrowthDemo.unity.</summary>
        public static void CreateSampleScene()
        {
            const string tmpDir = "Assets/ProcTree_TmpDemo";
            if (!AssetDatabase.IsValidFolder(tmpDir)) AssetDatabase.CreateFolder("Assets", "ProcTree_TmpDemo");
            string tmpScene = tmpDir + "/" + DemoSceneName + ".unity";

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PopulateDemoScene(scene);                               // loads assets fresh after NewScene
            EditorSceneManager.SaveScene(scene, tmpScene);

            var root = PackageRootOnDisk();
            if (root == null) Debug.LogError("[ProcTree] Could not resolve the package's on-disk path; the sample scene was not relocated into Samples~.");
            else Relocate(tmpScene, Path.Combine(root, "Samples~/GrowthDemo/" + DemoSceneName + ".unity"));
            AssetDatabase.Refresh();
            AssetDatabase.DeleteAsset(tmpDir);

            if (!Application.isBatchMode)
            {
                var view = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                PopulateDemoScene(view);
            }
        }

        /// <summary>One oak whose growth ping-pongs, a low backlighting sun, ground, camera, exposure.</summary>
        public static void PopulateDemoScene(Scene scene)
        {
            SceneManager.SetActiveScene(scene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(28f, 160f, 0f);     // low and behind: backlit leaves glow
            ConfigureSun(sun, 100000f);

            var camGo = new GameObject("Demo Camera");
            camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 5.5f, -26f);
            camGo.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
#if PROCTREE_HDRP
            camGo.AddComponent<HDAdditionalCameraData>();
#endif
            BuildExposureVolume();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OakPrefabPath);
            if (prefab == null) { Debug.LogError("[ProcTree] Oak prefab missing; run CreatePrefab first."); return; }
            var oak = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var anim = oak.AddComponent<GrowthAnimator>();
            anim.tree = oak.GetComponent<ProceduralTree>();
        }

        /// <summary>Reference-free HDRP-active check.</summary>
        public static bool IsHdrpActive(out string message)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) { message = "No render pipeline is active. Proc Tree requires HDRP (Project Settings > Graphics)."; return false; }
            var typeName = rp.GetType().FullName ?? "";
            if (!typeName.Contains("HighDefinition")) { message = $"Active render pipeline is '{typeName}', not HDRP."; return false; }
            message = "HDRP active.";
            return true;
        }

        static void ConfigureSun(Light l, float lux)
        {
            l.color = new Color(1f, 0.96f, 0.9f);
#if PROCTREE_HDRP
            if (l.GetComponent<HDAdditionalLightData>() == null) l.gameObject.AddComponent<HDAdditionalLightData>();
            l.lightUnit = LightUnit.Lux;
            l.intensity = lux;
#else
            l.intensity = 1f;
#endif
        }

        static void BuildExposureVolume()
        {
#if PROCTREE_HDRP
            var volGo = new GameObject("Post Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var exposure = profile.Add<Exposure>();
            exposure.mode.overrideState = true;
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.overrideState = true;
            exposure.fixedExposure.value = 13f;
            var tonemap = profile.Add<Tonemapping>();
            tonemap.mode.overrideState = true;
            tonemap.mode.value = TonemappingMode.ACES;
            vol.sharedProfile = profile;
#endif
        }

        static void SaveMaterial(string path, Material m)
        {
            if (m == null) return;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return; }
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
        }

        static string PackageRootOnDisk()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackageRoot + "/package.json");
            return info != null && !string.IsNullOrEmpty(info.resolvedPath) ? info.resolvedPath : null;
        }

        static void Relocate(string fromAssetPath, string absTo)
        {
            var absFrom = Path.GetFullPath(fromAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absTo));
            if (File.Exists(absTo)) File.Delete(absTo);
            File.Move(absFrom, absTo);
            if (File.Exists(absFrom + ".meta"))
            {
                if (File.Exists(absTo + ".meta")) File.Delete(absTo + ".meta");
                File.Move(absFrom + ".meta", absTo + ".meta");
            }
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
