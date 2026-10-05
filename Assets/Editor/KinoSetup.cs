using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Wires up Kino (jp.keijiro.kino.post-processing): registers every Kino effect in HDRP's
/// custom post-process order (plus the project's own Scanlines effect) and gives the main camera a Volume whose profile holds all of them.
/// Every Kino effect is neutral by default (intensity/opacity 0) — tick a parameter's override to use it.
/// </summary>
public static class KinoSetup
{
    const string ProfilePath = "Assets/Settings/KinoCameraProfile.asset";

    [MenuItem("Tools/Kino/Setup Main Camera")]
    public static void SetupAll()
    {
        RegisterEffects();
        AddToMainCamera();
    }

    static Type[] KinoTypes() => TypeCache.GetTypesDerivedFrom<CustomPostProcessVolumeComponent>()
        .Where(t => (t.Namespace == "Kino.PostProcessing" || t == typeof(Scanlines)) && !t.IsAbstract)
        .OrderBy(t => t.Name).ToArray();

    [MenuItem("Tools/Kino/Register Effects In Post-Process Order")]
    public static void RegisterEffects()
    {
        // CustomPostProcessOrdersSettings and its list type are internal in HDRP 17 — reach them by reflection.
        var hdrp = typeof(HDRenderPipelineAsset).Assembly;
        var settingsType = hdrp.GetType("UnityEngine.Rendering.HighDefinition.CustomPostProcessOrdersSettings", true);
        var settings = typeof(GraphicsSettings).GetMethods()
            .First(m => m.Name == "GetRenderPipelineSettings" && m.IsGenericMethod && m.GetParameters().Length == 0)
            .MakeGenericMethod(settingsType).Invoke(null, null);

        int added = 0;
        foreach (var type in KinoTypes())
        {
            var probe = (CustomPostProcessVolumeComponent)ScriptableObject.CreateInstance(type);
            var point = probe.injectionPoint;
            UnityEngine.Object.DestroyImmediate(probe);

            var listProp = point switch
            {
                CustomPostProcessInjectionPoint.AfterOpaqueAndSky => "beforeTransparentCustomPostProcesses",
                CustomPostProcessInjectionPoint.BeforeTAA => "beforeTAACustomPostProcesses",
                CustomPostProcessInjectionPoint.BeforePostProcess => "beforePostProcessCustomPostProcesses",
                CustomPostProcessInjectionPoint.AfterPostProcessBlurs => "afterPostProcessBlursCustomPostProcesses",
                _ => "afterPostProcessCustomPostProcesses",
            };
            var list = settingsType.GetProperty(listProp).GetValue(settings);
            var ok = (bool)list.GetType().GetMethod("Add", new[] { typeof(string) })
                .Invoke(list, new object[] { type.AssemblyQualifiedName });
            if (ok) added++;
            Debug.Log($"[Kino] {type.Name} → {point}{(ok ? " (registered)" : " (already registered)")}");
        }

        // Overlay first: the sequence plays video through it, and every later effect (Glitch,
        // Slice, Utility, Scanlines…) must distort the video too. The list has no Insert, so rebuild it.
        var after = settingsType.GetProperty("afterPostProcessCustomPostProcesses").GetValue(settings);
        var listType = after.GetType();
        int count = (int)listType.GetProperty("Count").GetValue(after);
        var item = listType.GetProperties().First(p => p.GetIndexParameters().Length == 1);
        var order = Enumerable.Range(0, count).Select(i => (Type)item.GetValue(after, new object[] { i })).ToList();
        var overlay = order.FirstOrDefault(t => t != null && t.FullName == "Kino.PostProcessing.Overlay");
        if (overlay != null && order.IndexOf(overlay) != 0)
        {
            var remove = listType.GetMethod("Remove", new[] { typeof(string) });
            var add = listType.GetMethod("Add", new[] { typeof(string) });
            foreach (var t in order) remove.Invoke(after, new object[] { t.AssemblyQualifiedName });
            foreach (var t in order.OrderBy(t => t == overlay ? 0 : 1)) add.Invoke(after, new object[] { t.AssemblyQualifiedName });
            Debug.Log("[Kino] Moved Overlay to the front of the After Post Process order.");
        }

        var globalSettings = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
        EditorUtility.SetDirty(globalSettings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Kino] Registered {added} new effect(s) in {AssetDatabase.GetAssetPath(globalSettings)}.");
    }

    [MenuItem("Tools/Kino/Add Volume To Main Camera")]
    public static void AddToMainCamera()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[Kino] No camera tagged MainCamera in the open scene."); return; }

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        foreach (var type in KinoTypes())
        {
            if (profile.Has(type)) continue;
            var comp = profile.Add(type, false);
            comp.name = type.Name;
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        EditorUtility.SetDirty(profile);

        var vol = cam.GetComponent<Volume>();
        if (vol == null) vol = Undo.AddComponent<Volume>(cam.gameObject);
        vol.isGlobal = true;
        vol.priority = 10;
        vol.sharedProfile = profile;
        vol.profile = profile; // see HDRP gotcha: avoid an in-memory profile copy shadowing the asset
        EditorUtility.SetDirty(vol);

        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Kino] Volume with {profile.components.Count} Kino effect(s) on '{cam.name}' → {ProfilePath}");
    }
}
