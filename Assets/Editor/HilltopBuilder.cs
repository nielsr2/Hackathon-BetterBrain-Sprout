using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Builds the oak-on-a-hill scene: sculpted terrain, painted layers, instanced grass details,
/// physically based sky with volumetric fog and a visible low sun. Re-runnable: everything it
/// generates lives in Assets/Hilltop and the "Hilltop Terrain" GameObject.
/// </summary>
public static class HilltopBuilder
{
    const string Dir = "Assets/Hilltop";
    const string TerrainName = "Hilltop Terrain";

    // Terrain dimensions (metres).
    const float Size = 200f, MaxHeight = 30f;
    const int HeightRes = 513, AlphaRes = 512, DetailRes = 1024;

    // Hill shape.
    const float Baseline = 1f;      // ground level away from the hill
    const float HillHeight = 8f;    // peak above baseline
    const float HillRadius = 30f;   // where the dome meets the plain
    const float DirtRadius = 3.5f;  // worn patch around the trunk

    // Sun: elevation above the horizon and yaw offset to the right of the camera's view axis.
    const float SunElevation = 11f, SunYawOffset = 25f;
    const float ExposureEV = 14f;

    [MenuItem("Tools/Hilltop/Build All")]
    public static void BuildAll()
    {
        EnsureFolder();
        var terrain = BuildTerrain();
        PlaceTree(terrain);
        BuildSky();
        ConfigureSun();
        PlaceCamera(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Hilltop] Build complete. Summit height {terrain.SampleHeight(Vector3.zero):0.00} m.");
    }

    /// <summary>Logs the resolved sky/exposure stack and terrain/detail state — for checking a build.</summary>
    /// <summary>Regenerates grass meshes/materials/prefabs (GUIDs kept) and repaints the detail layers; heights and splat untouched.</summary>
    [MenuItem("Tools/Hilltop/Rebuild Grass Only")]
    public static void RebuildGrass()
    {
        var go = GameObject.Find(TerrainName);
        var terrain = go != null ? go.GetComponent<Terrain>() : null;
        if (terrain == null) { Debug.LogError($"[Hilltop] No '{TerrainName}' in the open scene."); return; }
        var td = terrain.terrainData;
        td.detailPrototypes = BuildGrassPrototypes(FoliageProfile());
        PaintGrass(td);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssetIfDirty(td);
        terrain.Flush();
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        Debug.Log("[Hilltop] Grass rebuilt.");
    }

    /// <summary>
    /// Hides the sky's planet ground (the dark band with a hard horizon line past the 200 m terrain):
    /// a large flat skirt continues the ground out to the true horizon, where the height fog fades it
    /// into the sky haze. Also turns on TAA, which the thin grass blades need to stop shimmering.
    /// </summary>
    [MenuItem("Tools/Hilltop/Horizon Skirt + TAA (open scene)")]
    public static void PolishHorizon()
    {
        EnsureFolder();
        BuildHorizonSkirt();

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Dir + "/HilltopSky.asset");
        if (profile != null && profile.TryGet<Fog>(out var fog))
        {
            fog.maxFogDistance.Override(SkirtRadius); // fog must keep thickening all the way out to the skirt's edge
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.mipFogMaxMip.Override(0f); // sharp sky lookup: a blurred one mixes in the planet ground below the horizon
            // Volumetric fog out to the far skirt: the analytic fallback beyond it samples a low-res sky
            // cubemap whose horizon texels blend in the dark planet ground, giving a hard line.
            fog.depthExtent.Override(3000f);
            fog.sliceDistributionUniformity.Override(0.2f); // keep most slices near the camera
            // Height fog is measured from the planet surface, which PlanetSinkKm moves below world zero:
            // shift the layer by the same amount so it still hugs the ground (0–20 m above world zero).
            fog.baseHeight.Override(PlanetSinkKm * 1000f);
            fog.maximumHeight.Override(PlanetSinkKm * 1000f + 20f);
            fog.meanFreePath.Override(700f);
            fog.enableVolumetricFog.Override(true);
            EditorUtility.SetDirty(profile);
        }
        if (profile != null && profile.TryGet<VisualEnvironment>(out var env))
        {
            // Sink the sky's planet so its own horizon dips below everything seen over the skirt:
            // sky-coloured fog then always samples haze, never the dark planet ground.
            env.centerMode.Override(VisualEnvironment.PlanetMode.Manual);
            env.planetCenter.Override(new Vector3(0f, -env.planetRadius.value - PlanetSinkKm, 0f));
            EditorUtility.SetDirty(profile);
        }
        if (profile != null) AssetDatabase.SaveAssetIfDirty(profile);
        var volGo = GameObject.Find("Post Volume");
        var vol = volGo != null ? volGo.GetComponent<Volume>() : null;
        if (vol != null && profile != null)
        {
            Undo.RecordObject(vol, "Sky profile");
            vol.sharedProfile = profile;
            vol.profile = profile; // drop any in-memory copy, which would shadow the asset
        }

        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(cam, "Horizon far clip");
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, SkirtRadius * 1.2f);
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) continue;
            Undo.RecordObject(hd, "TAA");
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
            EditorUtility.SetDirty(hd);
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Hilltop] Horizon skirt built, fog distance extended, TAA on.");
    }

    const string SkirtName = "Horizon Skirt";
    const float SkirtRadius = 6000f;
    // Planet surface this far below the world origin: its horizon dips ~1°, far below the skirt's ~0.03°.
    const float PlanetSinkKm = 1f; // VisualEnvironment planet values are in kilometres
    // Under the terrain's rising rim (≥ ~2.5 m at r = 98 even with lumps), so it only shows beyond the edge.
    const float SkirtHeight = Baseline + 1f, SkirtInner = 98f;

    static void BuildHorizonSkirt()
    {
        const int Segments = 96, Rings = 24;
        var verts = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        for (int r = 0; r <= Rings; r++)
        {
            // Geometric ring spacing: dense near the terrain, sparse at the horizon.
            float radius = SkirtInner * Mathf.Pow(SkirtRadius / SkirtInner, r / (float)Rings);
            for (int s = 0; s <= Segments; s++)
            {
                float a = s / (float)Segments * Mathf.PI * 2f;
                var v = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                verts.Add(v);
                uvs.Add(new Vector2(v.x, v.z) / 8f);
            }
        }
        int row = Segments + 1;
        for (int r = 0; r < Rings; r++)
        for (int s = 0; s < Segments; s++)
        {
            int i = r * row + s;
            tris.AddRange(new[] { i, i + 1, i + row, i + 1, i + row + 1, i + row }); // clockwise from above
        }
        var mesh = new Mesh { name = "HorizonSkirt_Mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(SkirtRadius * 2f, 1f, SkirtRadius * 2f));
        mesh = Persist(mesh, $"{Dir}/HorizonSkirt_Mesh.asset");

        var grassLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{Dir}/TL_Grass.terrainlayer");
        var mat = new Material(Shader.Find("HDRP/Lit")) { name = "M_HorizonSkirt" };
        // Matches the terrain's grass layer as seen at the detail-distance edge; fog does the rest.
        mat.SetColor("_BaseColor", new Color(0.55f, 0.62f, 0.42f));
        if (grassLayer != null && grassLayer.diffuseTexture != null) mat.SetTexture("_BaseColorMap", grassLayer.diffuseTexture);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.1f);
        mat = Persist(mat, $"{Dir}/M_HorizonSkirt.mat");
        HDMaterial.ValidateMaterial(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mesh);

        var go = GameObject.Find(SkirtName);
        if (go == null)
        {
            go = new GameObject(SkirtName);
            Undo.RegisterCreatedObjectUndo(go, "Horizon skirt");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
        }
        go.isStatic = true;
        go.transform.SetPositionAndRotation(new Vector3(0f, SkirtHeight, 0f), Quaternion.identity);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    [MenuItem("Tools/Hilltop/Log Diagnostics")]
    static void LogDiagnostics()
    {
        var stack = VolumeManager.instance.CreateStack();
        var cam = GameObject.Find("Demo Camera");
        VolumeManager.instance.Update(stack, cam != null ? cam.transform : null, ~0);
        var env = stack.GetComponent<VisualEnvironment>();
        var pbs = stack.GetComponent<PhysicallyBasedSky>();
        var exp = stack.GetComponent<Exposure>();
        var hdCam = cam != null ? cam.GetComponent<HDAdditionalCameraData>() : null;
        Debug.Log($"[Hilltop] skyType={env.skyType.value} ambient={env.skyAmbientMode.value} " +
                  $"pbsType={pbs.type.value} pbsMaterial={pbs.material.value} " +
                  $"exposure={exp.mode.value}/{exp.fixedExposure.value} " +
                  $"clearMode={(hdCam != null ? hdCam.clearColorMode.ToString() : "none")} " +
                  $"pbrShader={Shader.Find("Hidden/HDRP/Sky/PbrSky") != null}");
        VolumeManager.instance.DestroyStack(stack);

        var t = Terrain.activeTerrain;
        if (t == null) { Debug.Log("[Hilltop] no active terrain"); return; }
        var td = t.terrainData;
        Debug.Log($"[Hilltop] terrain size={td.size} hRes={td.heightmapResolution} " +
                  $"h(0,0)={t.SampleHeight(Vector3.zero)} h(0,-42)={t.SampleHeight(new Vector3(0, 0, -42))} " +
                  $"layers={td.terrainLayers.Length} mat={(t.materialTemplate != null ? t.materialTemplate.shader.name : "null")} " +
                  $"details={td.detailPrototypes.Length} cam={(cam != null ? cam.transform.position.ToString() : "-")}");
        var w = td.GetAlphamaps(AlphaRes / 2 - 40, AlphaRes / 2 - 40, 1, 1);
        string tex = string.Join(",", System.Array.ConvertAll(td.terrainLayers, l => l == null ? "nullLayer" : (l.diffuseTexture == null ? "noTex" : l.diffuseTexture.name)));
        int detailSum = 0;
        foreach (var v in td.GetDetailLayer(DetailRes / 2 - 60, DetailRes / 2 - 60, 8, 8, 0)) detailSum += v;
        string protos = string.Join(",", System.Array.ConvertAll(td.detailPrototypes, p => { p.Validate(out string err); return $"{(p.prototype != null ? p.prototype.name : "null")}/err={err}"; }));
        Debug.Log($"[Hilltop] alphaTex={td.alphamapTextureCount} w=({w[0, 0, 0]:0.00},{w[0, 0, 1]:0.00},{w[0, 0, 2]:0.00}) layers={tex} " +
                  $"detailSum8x8={detailSum} protos={protos} drawFoliage={t.drawTreesAndFoliage} scatter={td.detailScatterMode}");
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Grass_Lush.prefab");
        var pmr = pf.GetComponent<MeshRenderer>();
        var pmf = pf.GetComponent<MeshFilter>();
        var fresh = new DetailPrototype { prototype = pf, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit };
        fresh.Validate(out string freshErr);
        Debug.Log($"[Hilltop] prefab mat={(pmr.sharedMaterial != null ? pmr.sharedMaterial.name : "null")} " +
                  $"mesh={(pmf.sharedMesh != null ? pmf.sharedMesh.name + " readable=" + pmf.sharedMesh.isReadable : "null")} freshErr={freshErr}");
    }

    // ---------------------------------------------------------------- terrain

    static Terrain BuildTerrain()
    {
        var old = GameObject.Find(TerrainName);
        if (old != null) Object.DestroyImmediate(old);

        // Create every supporting asset first: replacing an asset (delete + create) can reload
        // other assets from disk, which would wipe the TerrainData's unsaved edits.
        var layers = BuildLayers();
        var foliage = FoliageProfile();
        var grass = BuildGrassPrototypes(foliage);
        var material = TerrainMaterial();

        // The TerrainData must be an asset before painting so its splat textures become sub-assets.
        var td = new TerrainData();
        ReplaceAsset(td, Dir + "/HilltopTerrain.asset");
        td.heightmapResolution = HeightRes;
        td.size = new Vector3(Size, MaxHeight, Size);
        td.alphamapResolution = AlphaRes;
        td.SetDetailResolution(DetailRes, 32);
        td.SetDetailScatterMode(DetailScatterMode.CoverageMode);
        td.SetHeights(0, 0, SculptHeights());
        td.terrainLayers = layers;
        td.SetAlphamaps(0, 0, PaintSplat(td));
        td.detailPrototypes = grass;
        PaintGrass(td);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssetIfDirty(td);

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = TerrainName;
        go.transform.position = new Vector3(-Size / 2f, 0f, -Size / 2f);
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = material;
        terrain.drawInstanced = true;
        terrain.detailObjectDistance = 150f;
        terrain.detailObjectDensity = 1f;
        terrain.heightmapPixelError = 3f;

        var ground = GameObject.Find("Ground");
        if (ground != null) ground.SetActive(false);
        return terrain;
    }

    static float[,] SculptHeights()
    {
        var h = new float[HeightRes, HeightRes];
        for (int z = 0; z < HeightRes; z++)
        for (int x = 0; x < HeightRes; x++)
        {
            float wx = x / (HeightRes - 1f) * Size - Size / 2f;
            float wz = z / (HeightRes - 1f) * Size - Size / 2f;
            float r = Mathf.Sqrt(wx * wx + wz * wz);

            // Dome with a softly flattened top: (1 - t^2)^2.
            float t = Mathf.Clamp01(r / HillRadius);
            float dome = HillHeight * (1f - t * t) * (1f - t * t);

            // Lumps that fade out toward the summit so the tree stands on level ground.
            float lumpMask = Mathf.SmoothStep(0f, 1f, r / 12f);
            float lumps = (Mathf.PerlinNoise(wx * 0.035f + 13f, wz * 0.035f + 7f) - 0.5f) * 2.4f * lumpMask
                        + (Mathf.PerlinNoise(wx * 0.15f + 3f, wz * 0.15f + 91f) - 0.5f) * 0.35f * lumpMask;

            // Rolling rise toward the edges so the horizon isn't a flat line.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(55f, 100f, r))
                       * (3f + 5f * Mathf.PerlinNoise(wx * 0.02f + 50f, wz * 0.02f + 20f));

            h[z, x] = Mathf.Clamp01((Baseline + dome + lumps + rise) / MaxHeight);
        }
        return h;
    }

    static float[,,] PaintSplat(TerrainData td)
    {
        var a = new float[AlphaRes, AlphaRes, 4]; // 0 grass, 1 dirt, 2 rock, 3 dry grass
        for (int z = 0; z < AlphaRes; z++)
        for (int x = 0; x < AlphaRes; x++)
        {
            float u = (x + 0.5f) / AlphaRes, v = (z + 0.5f) / AlphaRes;
            float wx = u * Size - Size / 2f, wz = v * Size - Size / 2f;
            float r = Mathf.Sqrt(wx * wx + wz * wz);
            float steep = td.GetSteepness(u, v);
            float n = Mathf.PerlinNoise(wx * 0.3f + 5f, wz * 0.3f + 9f);

            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 30f, steep + (n - 0.5f) * 8f));
            float dirt = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DirtRadius - 1f, DirtRadius + 1.5f, r + (n - 0.5f) * 1.5f));
            dirt = Mathf.Max(dirt, PathMask(wx, wz) * (0.55f + 0.45f * n));
            dirt *= 1f - rock;
            float grass = Mathf.Max(0f, 1f - rock - dirt);
            // The ground under the grass follows the same lush/dry patches as the grass on top.
            float dry = grass * DryMask(wx, wz) * 0.85f;

            a[z, x, 0] = grass - dry;
            a[z, x, 1] = dirt;
            a[z, x, 2] = rock;
            a[z, x, 3] = dry;
        }
        return a;
    }

    /// <summary>Low-frequency patchiness shared by the ground splat and the grass details.</summary>
    static float Patch(float wx, float wz) => Mathf.PerlinNoise(wx * 0.08f + 40f, wz * 0.08f + 11f);

    static float DryMask(float wx, float wz) => Mathf.SmoothStep(0f, 1f, (Patch(wx, wz) - 0.45f) * 2.5f);

    /// <summary>A faint trail winding from the summit down toward the camera side (-Z).</summary>
    static float PathMask(float wx, float wz)
    {
        if (wz > -1f) return 0f;
        float centre = 2.5f * Mathf.Sin(wz * 0.07f) + 1.2f * Mathf.Sin(wz * 0.19f + 1f);
        float d = Mathf.Abs(wx - centre);
        float fadeOut = 1f - Mathf.InverseLerp(40f, 70f, -wz);
        return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1.2f, d))) * fadeOut;
    }

    // ---------------------------------------------------------------- layers & textures

    // CC0 scanned sets from ambientCG, unpacked to Assets/Hilltop/Ground/<set>/<set>_2K-JPG_<map>.jpg.
    const string GroundDir = Dir + "/Ground";

    static TerrainLayer[] BuildLayers()
    {
        // Tints are diffuse remaps: they pull the mossy scan toward the colour of the grass blade roots.
        return new[]
        {
            PbrLayer("Grass", "Ground037", 5f, new Color(0.45f, 0.55f, 0.32f), new Vector2(0f, 0.6f), 0.45f)
                ?? MakeLayer("Grass", new Color(0.17f, 0.27f, 0.07f), new Color(0.32f, 0.43f, 0.13f), 4f, 0.15f, 1, false),
            PbrLayer("Dirt", "Ground048", 3.5f, new Color(0.95f, 0.9f, 0.85f), new Vector2(0f, 0.5f), 0.4f)
                ?? MakeLayer("Dirt",  new Color(0.27f, 0.20f, 0.13f), new Color(0.42f, 0.33f, 0.23f), 3f, 0.2f, 2, false),
            PbrLayer("Rock", "Rock051", 8f, new Color(0.85f, 0.85f, 0.85f), new Vector2(0.1f, 1f), 0.5f)
                ?? MakeLayer("Rock",  new Color(0.33f, 0.32f, 0.30f), new Color(0.56f, 0.54f, 0.50f), 6f, 0.3f, 3, true),
            PbrLayer("GrassDry", "Ground037", 5f, new Color(0.75f, 0.65f, 0.35f), new Vector2(0f, 0.6f), 0.4f)
                ?? MakeLayer("GrassDry", new Color(0.30f, 0.29f, 0.12f), new Color(0.48f, 0.45f, 0.22f), 4f, 0.15f, 4, false),
        };
    }

    /// <summary>
    /// A terrain layer from a scanned PBR set: albedo, GL normal map and an HDRP mask map
    /// (R metallic, G AO, B height, A smoothness). Returns null when the set isn't on disk.
    /// </summary>
    static TerrainLayer PbrLayer(string name, string set, float tile, Color tint, Vector2 heightRemap, float maxSmoothness)
    {
        string Map(string m) => $"{GroundDir}/{set}/{set}_2K-JPG_{m}.jpg";
        if (!System.IO.File.Exists(Map("Color"))) return null;

        ConfigureImporter(Map("Color"), TextureImporterType.Default, true, false);
        ConfigureImporter(Map("NormalGL"), TextureImporterType.NormalMap, false, false);
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Map("Color"));
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Map("NormalGL"));
        var mask = PackMaskMap(set, Map("AmbientOcclusion"), Map("Displacement"), Map("Roughness"));

        var layer = new TerrainLayer
        {
            name = "TL_" + name,
            diffuseTexture = albedo,
            normalMapTexture = normal,
            normalScale = 1f,
            maskMapTexture = mask,
            tileSize = new Vector2(tile, tile),
            smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly, // ignored once a mask map is present
            metallic = 0f,
            diffuseRemapMin = Vector4.zero,
            diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f),
            maskMapRemapMin = new Vector4(0f, 0f, heightRemap.x, 0f),
            maskMapRemapMax = new Vector4(0f, 1f, heightRemap.y, maxSmoothness),
        };
        return Persist(layer, $"{Dir}/TL_{name}.terrainlayer");
    }

    static void ConfigureImporter(string path, TextureImporterType type, bool sRGB, bool readable)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp.textureType == type && imp.sRGBTexture == sRGB && imp.isReadable == readable && imp.maxTextureSize == 2048) return;
        imp.textureType = type;
        imp.sRGBTexture = sRGB;
        imp.isReadable = readable;
        imp.maxTextureSize = 2048;
        if (readable) imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }

    /// <summary>Packs AO, displacement and roughness into an HDRP mask map PNG next to the set (once).</summary>
    static Texture2D PackMaskMap(string set, string aoPath, string heightPath, string roughPath)
    {
        string path = $"{GroundDir}/{set}/{set}_Mask.png";
        if (!System.IO.File.Exists(path))
        {
            ConfigureImporter(aoPath, TextureImporterType.Default, false, true);
            ConfigureImporter(heightPath, TextureImporterType.Default, false, true);
            ConfigureImporter(roughPath, TextureImporterType.Default, false, true);
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath).GetPixels32();
            var h = AssetDatabase.LoadAssetAtPath<Texture2D>(heightPath).GetPixels32();
            var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
            var r = rough.GetPixels32();
            var px = new Color32[r.Length];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(0, ao[i].r, h[i].r, (byte)(255 - r[i].r));
            var tex = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            // The sources were only made readable for packing.
            foreach (var p in new[] { aoPath, heightPath, roughPath })
                ConfigureImporter(p, TextureImporterType.Default, false, false);
        }
        ConfigureImporter(path, TextureImporterType.Default, false, false);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static TerrainLayer MakeLayer(string name, Color dark, Color light, float tile, float smoothness, int seed, bool streaky)
    {
        var texPath = $"{Dir}/T_{name}.asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
        {
            tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true, false);
            AssetDatabase.CreateAsset(tex, texPath);
        }
        FillNoise(tex, dark, light, seed, streaky);

        var layer = new TerrainLayer
        {
            name = "TL_" + name,
            diffuseTexture = tex,
            tileSize = new Vector2(tile, tile),
            smoothness = smoothness,
            smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly, // texture alpha is 1 → mirror-like otherwise
            metallic = 0f,
            diffuseRemapMin = Vector4.zero,
            diffuseRemapMax = Vector4.one,
        };
        return Persist(layer, $"{Dir}/TL_{name}.terrainlayer");
    }

    const int TexSize = 512;

    static void FillNoise(Texture2D tex, Color dark, Color light, int seed, bool streaky)
    {
        const int N = TexSize;
        tex.Reinitialize(N, N, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = (float)x / N, v = (float)y / N;
            float coarse = Fbm(u, streaky ? v * 0.25f : v, 4, 5, seed);
            float speck = Hash(x, y, seed + 17);
            float t = Mathf.Clamp01(coarse * 0.85f + speck * 0.3f - 0.1f);
            if (streaky) t = Mathf.Clamp01(t + (Fbm(u, v, 16, 3, seed + 5) - 0.5f) * 0.5f);
            px[y * N + x] = Color.Lerp(dark, light, t);
        }
        tex.SetPixels(px);
        tex.Apply(true);
        EditorUtility.SetDirty(tex);
    }

    // Tileable value noise: lattice wraps at `period`, so u,v in [0,1) tile seamlessly.
    static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int period = basePeriod;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(u * period, v * period, period, seed + i * 31);
            norm += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return sum / norm;
    }

    static float ValueNoise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = Mod(xi, period), x1 = Mod(xi + 1, period);
        int y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
        float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
        float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }
    }

    static Material TerrainMaterial()
    {
        var source = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;
        if (source == null) { Debug.LogError("[Hilltop] Active pipeline has no default terrain material."); return null; }

        // A copy, so height blending doesn't leak into the pipeline's shared default.
        var mat = new Material(source) { name = "M_HilltopTerrain" };
        mat.SetFloat("_EnableHeightBlend", 1f);
        mat.SetFloat("_HeightTransition", 0.25f);
        mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
        HDMaterial.ValidateMaterial(mat);
        mat = Persist(mat, $"{Dir}/M_HilltopTerrain.mat");
        AssetDatabase.SaveAssetIfDirty(mat);
        return mat;
    }

    // ---------------------------------------------------------------- grass details

    /// <summary>Shape of one grass clump variant. Heights and widths are in metres before detail scaling.</summary>
    struct ClumpSpec
    {
        public int blades, seed;
        public float hMin, hMax, wMin, wMax, bendMin, bendMax, radius;
        public bool heads;      // seed heads / flowers on the blade tips
        public float headSize;
    }

    /// <summary>Colours sampled up the blade via UV.y: root → tip, then the head colour above <see cref="HeadStart"/>.</summary>
    struct GrassColors
    {
        public Color root, tip, head;
    }

    const float HeadStart = 0.9f; // blade UVs stop here when a clump has heads, heads sit above it

    // Detail layer order, shared by BuildGrassPrototypes and PaintGrass.
    const int FillLayer = 0, LushLayer = 1, TallLayer = 2, DryLayer = 3, FlowerLayer = 4;

    static DetailPrototype[] BuildGrassPrototypes(DiffusionProfileSettings foliage)
    {
        var fill = GrassPrefab("Grass_Fill", new ClumpSpec { blades = 12, seed = 3, hMin = 0.15f, hMax = 0.3f, wMin = 0.03f, wMax = 0.05f, bendMin = 0.03f, bendMax = 0.1f, radius = 0.16f },
            new GrassColors { root = new Color(0.13f, 0.19f, 0.05f), tip = new Color(0.42f, 0.55f, 0.17f) }, foliage);
        var lush = GrassPrefab("Grass_Lush", new ClumpSpec { blades = 10, seed = 7, hMin = 0.3f, hMax = 0.55f, wMin = 0.04f, wMax = 0.065f, bendMin = 0.08f, bendMax = 0.2f, radius = 0.12f },
            new GrassColors { root = new Color(0.12f, 0.18f, 0.04f), tip = new Color(0.46f, 0.58f, 0.16f) }, foliage);
        var tall = GrassPrefab("Grass_Tall", new ClumpSpec { blades = 6, seed = 11, hMin = 0.6f, hMax = 0.9f, wMin = 0.02f, wMax = 0.035f, bendMin = 0.1f, bendMax = 0.25f, radius = 0.1f, heads = true, headSize = 0.07f },
            new GrassColors { root = new Color(0.12f, 0.16f, 0.05f), tip = new Color(0.55f, 0.58f, 0.26f), head = new Color(0.62f, 0.54f, 0.32f) }, foliage);
        var dry  = GrassPrefab("Grass_Dry",  new ClumpSpec { blades = 9, seed = 5, hMin = 0.3f, hMax = 0.5f, wMin = 0.035f, wMax = 0.055f, bendMin = 0.2f, bendMax = 0.35f, radius = 0.14f },
            new GrassColors { root = new Color(0.25f, 0.23f, 0.10f), tip = new Color(0.72f, 0.64f, 0.36f) }, foliage);
        var flowers = GrassPrefab("Flowers_Sparse", new ClumpSpec { blades = 4, seed = 13, hMin = 0.25f, hMax = 0.4f, wMin = 0.006f, wMax = 0.009f, bendMin = 0.02f, bendMax = 0.08f, radius = 0.1f, heads = true, headSize = 0.035f },
            new GrassColors { root = new Color(0.10f, 0.15f, 0.04f), tip = new Color(0.32f, 0.44f, 0.12f), head = new Color(0.80f, 0.76f, 0.60f) }, foliage); // dimmer: bright heads read as distant sparkle

        var protos = new DetailPrototype[5];
        protos[FillLayer] = GrassPrototype(fill, 2.0f);
        protos[LushLayer] = GrassPrototype(lush, 1.0f);
        protos[TallLayer] = GrassPrototype(tall, 0.35f);
        protos[DryLayer] = GrassPrototype(dry, 0.6f);
        protos[FlowerLayer] = GrassPrototype(flowers, 0.15f);
        return protos;
    }

    static void PaintGrass(TerrainData td)
    {
        var maps = new int[5][,];
        for (int i = 0; i < maps.Length; i++) maps[i] = new int[DetailRes, DetailRes];
        var splat = td.GetAlphamaps(0, 0, AlphaRes, AlphaRes);
        for (int z = 0; z < DetailRes; z++)
        for (int x = 0; x < DetailRes; x++)
        {
            float u = (x + 0.5f) / DetailRes, v = (z + 0.5f) / DetailRes;
            float wx = u * Size - Size / 2f, wz = v * Size - Size / 2f;
            int sx = Mathf.Min(AlphaRes - 1, (int)(u * AlphaRes)), sz = Mathf.Min(AlphaRes - 1, (int)(v * AlphaRes));
            float grass = splat[sz, sx, 0] + splat[sz, sx, 3];
            float dirt = splat[sz, sx, 1];
            float r = Mathf.Sqrt(wx * wx + wz * wz);
            if (r < 1.2f) continue; // keep the trunk base clear

            // Grass thins out onto dirt instead of stopping at a hard edge.
            float cover = Mathf.Clamp01(grass + dirt * 0.25f);
            float patch = Patch(wx, wz), dryW = DryMask(wx, wz);
            float fine = Mathf.PerlinNoise(wx * 0.4f + 71f, wz * 0.4f + 3f);
            float path = PathMask(wx, wz);

            float fill = cover * Mathf.Lerp(0.7f, 1f, fine);
            float lush = cover * Mathf.Lerp(0.55f, 1f, patch) * (1f - dryW * 0.6f);
            float tall = cover * Mathf.SmoothStep(0f, 1f, (fine - 0.55f) * 4f) * (1f - path);
            float dry = cover * dryW;
            float bloom = Mathf.PerlinNoise(wx * 0.25f + 17f, wz * 0.25f + 59f);
            float flowers = grass * Mathf.SmoothStep(0f, 1f, (bloom - 0.68f) * 6f) * Mathf.InverseLerp(8f, 14f, r) * (1f - path);

            maps[FillLayer][z, x] = Mathf.RoundToInt(255f * fill);
            maps[LushLayer][z, x] = Mathf.RoundToInt(255f * lush);
            maps[TallLayer][z, x] = Mathf.RoundToInt(255f * tall);
            maps[DryLayer][z, x] = Mathf.RoundToInt(255f * dry);
            maps[FlowerLayer][z, x] = Mathf.RoundToInt(255f * flowers);
        }
        for (int i = 0; i < maps.Length; i++) td.SetDetailLayer(0, 0, i, maps[i]);
    }

    static DetailPrototype GrassPrototype(GameObject prefab, float density)
    {
        return new DetailPrototype
        {
            prototype = prefab,
            usePrototypeMesh = true,
            renderMode = DetailRenderMode.VertexLit,
            useInstancing = true,
            minWidth = 0.8f, maxWidth = 1.3f,
            minHeight = 0.7f, maxHeight = 1.4f,
            noiseSpread = 0.3f,
            density = density,
            alignToGround = 0.6f,
            positionJitter = 1f,
        };
    }

    /// <summary>Thin-object transmission so backlit blades glow; created once so its hash stays stable.</summary>
    static DiffusionProfileSettings FoliageProfile()
    {
        string path = $"{Dir}/DP_Foliage.asset";
        var dp = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(path);
        if (dp == null)
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<DiffusionProfileSettings>(), path);
            // Re-importing runs OnEnable with the asset's GUID available, which assigns the profile hash.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            dp = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(path);
        }
        dp.transmissionTint = new Color(0.9f, 1.0f, 0.45f);
        dp.scatteringDistance = new Color(0.35f, 0.45f, 0.15f);
        EditorUtility.SetDirty(dp);
        AssetDatabase.SaveAssetIfDirty(dp);
        return dp;
    }

    static GameObject GrassPrefab(string name, ClumpSpec spec, GrassColors colors, DiffusionProfileSettings foliage)
    {
        var clump = GrassClumpMesh(spec);
        clump.name = name + "_Mesh";
        var mesh = Persist(clump, $"{Dir}/{name}_Mesh.asset");
        var (baseMap, maskMap) = GrassTextures(name, colors, spec.heads);

        var mat = new Material(Shader.Find("HDRP/Lit")) { name = "M_" + name, enableInstancing = true };
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BaseColorMap", baseMap);
        mat.SetTexture("_MaskMap", maskMap);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_AORemapMin", 0f);
        mat.SetFloat("_AORemapMax", 1f);
        mat.SetFloat("_SmoothnessRemapMin", 0.1f);
        mat.SetFloat("_SmoothnessRemapMax", 0.2f); // glossy thin blades glint into speckle against a low sun
        mat.SetFloat("_Thickness", 0.25f);
        mat = Persist(mat, $"{Dir}/M_{name}.mat");
        // After persisting: the diffusion profile reference is stored as a sub-asset of the material.
        mat.SetMaterialType(MaterialId.LitTranslucent);
        HDMaterial.SetDiffusionProfile(mat, foliage);
        HDMaterial.ValidateMaterial(mat);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mesh);

        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        // Zero-thickness blades shadow their own front faces; contact shadows + SSAO ground them instead.
        mr.shadowCastingMode = ShadowCastingMode.Off;
        var path = $"{Dir}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        // Re-import so the prefab resolves its mesh/material against the saved assets;
        // otherwise a rebuild can leave it pointing at stale (null) objects.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>
    /// Vertical strips addressed by UV.y: a root-to-tip colour gradient and an HDRP mask map whose
    /// AO darkens the base of the clump (R metallic 0, G AO, B unused, A smoothness).
    /// </summary>
    static (Texture2D, Texture2D) GrassTextures(string name, GrassColors c, bool heads)
    {
        const int W = 4, H = 64;
        var basePx = new Color[W * H];
        var maskPx = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float t = (y + 0.5f) / H;
            Color col;
            if (heads && t >= HeadStart) col = c.head;
            else
            {
                float s = heads ? t / HeadStart : t;
                col = Color.Lerp(c.root, c.tip, Mathf.SmoothStep(0f, 1f, s));
            }
            float ao = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, t * 1.6f));
            for (int x = 0; x < W; x++)
            {
                basePx[y * W + x] = col;
                maskPx[y * W + x] = new Color(0f, ao, 0f, 1f);
            }
        }
        return (StripTexture($"{Dir}/T_{name}_Base.asset", basePx, W, H, false),
                StripTexture($"{Dir}/T_{name}_Mask.asset", maskPx, W, H, true));
    }

    static Texture2D StripTexture(string path, Color[] px, int w, int h, bool linear)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null)
        {
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            AssetDatabase.CreateAsset(tex, path);
        }
        tex.Reinitialize(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels(px);
        tex.Apply(false);
        EditorUtility.SetDirty(tex);
        return tex;
    }

    /// <summary>
    /// A clump of tapered, curved blades. Each blade is emitted twice (front and back) so no
    /// double-sided material is needed. Normals are rounded across the blade width with only a light
    /// upward bias, so blades shade like curved leaves rather than flat cards.
    /// </summary>
    static Mesh GrassClumpMesh(ClumpSpec spec)
    {
        const int Segs = 5;
        var rng = new System.Random(spec.seed);
        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        var verts = new System.Collections.Generic.List<Vector3>();
        var norms = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var tris = new System.Collections.Generic.List<int>();
        float uvTop = spec.heads ? HeadStart - 0.02f : 1f;

        for (int b = 0; b < spec.blades; b++)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float rad = Rand(0f, spec.radius);
            var root = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            float facing = Rand(0f, Mathf.PI * 2f);
            var side = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));
            // Lean mostly outward from the clump centre, so clumps splay open.
            var outward = rad > 1e-4f ? root.normalized : side;
            var lean = (outward * 0.7f + new Vector3(-side.z, 0f, side.x) * Rand(-0.5f, 0.5f)).normalized;
            float height = Rand(spec.hMin, spec.hMax), width = Rand(spec.wMin, spec.wMax), bend = Rand(spec.bendMin, spec.bendMax);
            Vector3 tip = root;

            for (int face = 0; face < 2; face++)
            {
                int start = verts.Count;
                // Matches the triangle winding below (face 0 front = Cross(up, side)).
                var faceNormal = Vector3.Cross(Vector3.up, side) * (face == 0 ? 1f : -1f);
                var nLeft = (faceNormal * 0.75f - side * 0.45f + Vector3.up * 0.25f).normalized;
                var nRight = (faceNormal * 0.75f + side * 0.45f + Vector3.up * 0.25f).normalized;
                for (int s = 0; s <= Segs; s++)
                {
                    float t = (float)s / Segs;
                    var spine = root + Vector3.up * (height * t) + lean * (bend * t * t);
                    if (s == Segs) tip = spine;
                    float w = width * (1f - t * 0.9f);
                    verts.Add(spine - side * w); norms.Add(nLeft);  uvs.Add(new Vector2(0f, t * uvTop));
                    verts.Add(spine + side * w); norms.Add(nRight); uvs.Add(new Vector2(1f, t * uvTop));
                }
                for (int s = 0; s < Segs; s++)
                {
                    int i = start + s * 2;
                    if (face == 0) tris.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                    else           tris.AddRange(new[] { i, i + 1, i + 2, i + 1, i + 3, i + 2 });
                }
            }

            if (spec.heads) AddHead(tip, lean, spec.headSize, verts, norms, uvs, tris);
        }

        var mesh = new Mesh { name = "GrassClump" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A seed head / flower: two crossed, double-sided diamond cards at the blade tip.</summary>
    static void AddHead(Vector3 tip, Vector3 lean, float size,
        System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<Vector3> norms,
        System.Collections.Generic.List<Vector2> uvs, System.Collections.Generic.List<int> tris)
    {
        var axis = (Vector3.up + lean * 0.3f).normalized;
        for (int card = 0; card < 2; card++)
        {
            var side = Quaternion.AngleAxis(card * 90f, axis) * Vector3.Cross(axis, Vector3.forward).normalized;
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            var faceN = Vector3.Cross(axis, side).normalized; // front of the face-0 winding
            for (int face = 0; face < 2; face++)
            {
                int i = verts.Count;
                var n = ((face == 0 ? faceN : -faceN) * 0.6f + Vector3.up * 0.4f).normalized;
                verts.Add(tip);                                  // bottom
                verts.Add(tip + axis * size * 0.5f - side * size * 0.22f);
                verts.Add(tip + axis * size * 0.5f + side * size * 0.22f);
                verts.Add(tip + axis * size);                    // top
                for (int k = 0; k < 4; k++) { norms.Add(n); uvs.Add(new Vector2(0.5f, 0.97f)); }
                if (face == 0) tris.AddRange(new[] { i, i + 1, i + 2, i + 1, i + 3, i + 2 });
                else           tris.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
            }
        }
    }

    // ---------------------------------------------------------------- tree, sky, sun, camera

    static void PlaceTree(Terrain terrain)
    {
        var oak = GameObject.Find("Oak");
        if (oak == null) { Debug.LogWarning("[Hilltop] No 'Oak' in scene; skipping tree placement."); return; }
        float y = terrain.SampleHeight(Vector3.zero) + terrain.transform.position.y - 0.15f;
        oak.transform.position = new Vector3(0f, y, 0f);
    }

    static void BuildSky()
    {
        var volGo = GameObject.Find("Post Volume");
        if (volGo == null) { volGo = new GameObject("Post Volume"); volGo.AddComponent<Volume>().isGlobal = true; }
        var vol = volGo.GetComponent<Volume>();

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        ReplaceAsset(profile, Dir + "/HilltopSky.asset");

        var exposure = profile.Add<Exposure>();
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(ExposureEV);

        profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);

        var env = profile.Add<VisualEnvironment>();
        env.skyType.Override((int)SkyType.PhysicallyBased);
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

        profile.Add<PhysicallyBasedSky>();

        var fog = profile.Add<Fog>();
        fog.enabled.Override(true);
        fog.meanFreePath.Override(700f);
        fog.baseHeight.Override(0f);
        fog.maximumHeight.Override(20f);
        fog.maxFogDistance.Override(2000f);
        fog.enableVolumetricFog.Override(true);
        fog.albedo.Override(new Color(1f, 0.97f, 0.92f));
        fog.anisotropy.Override(0.6f); // forward scattering: brighter haze toward the sun

        // Grounding for the grass: blade-on-ground contact shadows and darkened clump bases.
        var contact = profile.Add<ContactShadows>();
        contact.enable.Override(true);
        contact.length.Override(0.3f);
        var ssao = profile.Add<ScreenSpaceAmbientOcclusion>();
        ssao.intensity.Override(1.2f);
        ssao.radius.Override(1.0f);

        var foliage = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>($"{Dir}/DP_Foliage.asset");
        if (foliage != null) profile.Add<DiffusionProfileList>().diffusionProfiles.Override(new[] { foliage });

        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);
        vol.sharedProfile = profile;
        vol.profile = profile; // drop any in-memory instance copy, which would shadow sharedProfile
    }

    static void ConfigureSun()
    {
        var sunGo = GameObject.Find("Sun");
        if (sunGo == null) { Debug.LogWarning("[Hilltop] No 'Sun' in scene."); return; }
        // Light forward points away from the sun; yaw 180 puts the sun ahead of a +Z camera.
        sunGo.transform.rotation = Quaternion.Euler(SunElevation, 180f + SunYawOffset, 0f);
        var light = sunGo.GetComponent<Light>();
        light.useColorTemperature = true;
        light.colorTemperature = 5200f;
        light.color = Color.white;
        var hd = sunGo.GetComponent<HDAdditionalLightData>();
        if (hd != null)
        {
            hd.angularDiameter = 2f;
            hd.flareSize = 1.5f;
            hd.affectsVolumetric = true;
            hd.useContactShadow.useOverride = true;
            hd.useContactShadow.@override = true;
            hd.SetShadowResolutionOverride(false);
            hd.SetShadowResolutionLevel(2); // High
        }
    }

    static void PlaceCamera(Terrain terrain)
    {
        var cam = GameObject.Find("Demo Camera");
        if (cam == null) return;
        var pos = new Vector3(0f, 0f, -42f);
        pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + 2f;
        cam.transform.position = pos;
        var oak = GameObject.Find("Oak");
        var target = (oak != null ? oak.transform.position : Vector3.zero) + Vector3.up * 4.5f;
        cam.transform.rotation = Quaternion.LookRotation(target - pos);
        var c = cam.GetComponent<Camera>();
        if (c != null) c.farClipPlane = Mathf.Max(c.farClipPlane, 2000f);
    }

    // ---------------------------------------------------------------- asset helpers

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Hilltop");
    }

    /// <summary>
    /// Saves <paramref name="fresh"/> at <paramref name="path"/>, or copies it over the asset already
    /// there so its GUID (and every reference to it) survives a rebuild.
    /// </summary>
    static T Persist<T>(T fresh, string path) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null) { AssetDatabase.CreateAsset(fresh, path); return fresh; }
        EditorUtility.CopySerialized(fresh, existing);
        Object.DestroyImmediate(fresh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static void ReplaceAsset(Object obj, string path)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(obj, path);
    }
}
