using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nib.ProcTree.Editor
{
    /// <summary>
    /// Deterministic, code-generated textures (no scans, no manual steps): a seamless tiling oak
    /// bark (vertical fissured ridges) and a lobed oak-leaf atlas (alpha = lobed outline, veins,
    /// thickness for backlight). Kept deliberately simple, as asked for the bark.
    /// </summary>
    public static class TreeTextureGenerator
    {
        const int BarkRes = 512;
        const int LeafRes = 256;

        /// <summary>Paths of the generated textures.</summary>
        public struct Set
        {
            /// <summary>Bark textures.</summary>
            public Texture2D barkAlbedo, barkNormal;
            /// <summary>Leaf textures.</summary>
            public Texture2D leafAlbedo, leafNormal, leafThickness;
        }

        /// <summary>Writes (or overwrites) all textures into <paramref name="folder"/> and imports them.</summary>
        public static Set GenerateInto(string folder)
        {
            Directory.CreateDirectory(folder);
            string bA = folder + "/Oak_bark_albedo.png", bN = folder + "/Oak_bark_normal.png";
            string lA = folder + "/Oak_leaf_albedo.png", lN = folder + "/Oak_leaf_normal.png", lT = folder + "/Oak_leaf_thickness.png";

            Write(bA, BarkRes, (u, v) =>
            {
                float h = BarkHeight(u, v);
                float speck = Hash(u * 913f, v * 377f) * 0.08f;
                float g = Mathf.Lerp(0.18f, 0.62f, h) + speck;            // dark fissures, grey ridges
                return new Color(g * 1.02f, g * 0.95f, g * 0.86f, 1f);
            });
            Write(bN, BarkRes, (u, v) => HeightNormal(BarkHeight, u, v, 1f / BarkRes, 2.5f));

            Write(lA, LeafRes, (u, v) =>
            {
                float a = LeafAlpha(u, v);
                float vein = LeafVein(u, v);
                float g = Mathf.Lerp(0.30f, 0.42f, v) * (1f - 0.25f * vein);
                return new Color(g * 0.52f, g, g * 0.24f, a);
            });
            Write(lN, LeafRes, (u, v) => HeightNormal((x, y) => -LeafVein(x, y), u, v, 1f / LeafRes, 1.2f));
            Write(lT, LeafRes, (u, v) =>
            {
                float t = Mathf.Lerp(0.2f, 0.45f, 1f - v) + 0.5f * LeafVein(u, v);   // veins thicker, tip thinner
                return new Color(t, t, t, 1f);
            });

            Import(bA, srgb: true, normal: false, wrap: TextureWrapMode.Repeat);
            Import(bN, srgb: false, normal: true, wrap: TextureWrapMode.Repeat);
            Import(lA, srgb: true, normal: false, wrap: TextureWrapMode.Clamp);
            Import(lN, srgb: false, normal: true, wrap: TextureWrapMode.Clamp);
            Import(lT, srgb: false, normal: false, wrap: TextureWrapMode.Clamp);

            return new Set
            {
                barkAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(bA),
                barkNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(bN),
                leafAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(lA),
                leafNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(lN),
                leafThickness = AssetDatabase.LoadAssetAtPath<Texture2D>(lT),
            };
        }

        // Seamless in u and v: integer frequencies only. Vertical ridges whose phase wanders with v.
        static float BarkHeight(float u, float v)
        {
            const float TwoPi = Mathf.PI * 2f;
            float warp = 0.10f * Mathf.Sin(TwoPi * v * 2f) + 0.05f * Mathf.Sin(TwoPi * (v * 5f + u)) + 0.03f * Mathf.Sin(TwoPi * (v * 11f + 0.3f));
            float ridge = Mathf.Abs(Mathf.Sin(TwoPi * (u * 7f + warp) * 0.5f * 2f));      // 7 ridges across
            float cross = 0.5f + 0.5f * Mathf.Sin(TwoPi * (v * 9f + 0.4f * Mathf.Sin(TwoPi * u * 3f)));
            float h = Mathf.Pow(ridge, 0.55f) * Mathf.Lerp(0.75f, 1f, cross);
            return Mathf.Clamp01(h);
        }

        // Lobed oak leaf: obovate envelope (widest past the middle) with 5 rounded lobes per side,
        // short petiole. u = across (0..1), v = along (0 base .. 1 tip).
        static float LeafAlpha(float u, float v)
        {
            if (v < 0.07f) return Mathf.Abs(u - 0.5f) < 0.025f ? 1f : 0f;               // petiole
            float t = (v - 0.07f) / 0.93f;
            float envelope = 0.5f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.75f));
            float lobes = 0.68f + 0.32f * Mathf.Abs(Mathf.Sin(Mathf.PI * 5f * Mathf.Pow(t, 0.9f)));
            float half = envelope * lobes * 0.96f;
            return Mathf.Abs(u - 0.5f) <= half ? 1f : 0f;
        }

        static float LeafVein(float u, float v)
        {
            float mid = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) / 0.02f);
            float lateral = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin(Mathf.PI * (v * 5f - Mathf.Abs(u - 0.5f) * 1.6f))) / 0.08f);
            return Mathf.Max(mid, lateral * 0.6f * (v > 0.08f ? 1f : 0f));
        }

        static Color HeightNormal(Func<float, float, float> h, float u, float v, float d, float strength)
        {
            float dx = (h(Frac(u + d), v) - h(Frac(u - d), v)) * strength;
            float dy = (h(u, Frac(v + d)) - h(u, Frac(v - d))) * strength;
            var n = new Vector3(-dx, -dy, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static float Hash(float x, float y)
        {
            float s = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        static void Write(string path, int res, Func<float, float, Color> shade)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    px[y * res + x] = shade((x + 0.5f) / res, (y + 0.5f) / res);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        static void Import(string path, bool srgb, bool normal, TextureWrapMode wrap)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.mipmapEnabled = true;
            imp.alphaIsTransparency = !normal && srgb;
            imp.wrapMode = wrap;
            imp.SaveAndReimport();
        }
    }
}
