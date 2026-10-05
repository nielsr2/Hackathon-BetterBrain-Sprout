using UnityEngine;

namespace Nib.ProcTree.Rendering
{
    /// <summary>Builds fully configured bark and leaf materials in code (no manual wiring).</summary>
    public static class TreeMaterialFactory
    {
        /// <summary>Bark shader name.</summary>
        public const string BarkShader = "ProcTree/Bark";
        /// <summary>Leaf shader name.</summary>
        public const string LeafShader = "ProcTree/Leaf";

        /// <summary>A bark material; textures may be null (flat colour).</summary>
        public static Material CreateBark(Texture2D albedo, Texture2D normal)
        {
            var m = New(BarkShader, "Oak Bark");
            if (m == null) return null;
            if (albedo != null) m.SetTexture("_BaseMap", albedo);
            if (normal != null) m.SetTexture("_NormalMap", normal);
            m.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.52f, 1f));
            m.EnableKeyword("_WIND_ON");
            return m;
        }

        /// <summary>A leaf material; textures may be null (square leaves).</summary>
        public static Material CreateLeaf(Texture2D albedo, Texture2D normal, Texture2D thickness)
        {
            var m = New(LeafShader, "Oak Leaf");
            if (m == null) return null;
            if (albedo != null) m.SetTexture("_BaseMap", albedo);
            if (normal != null) m.SetTexture("_NormalMap", normal);
            if (thickness != null) m.SetTexture("_ThicknessMap", thickness);
            m.EnableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_DOUBLESIDED_ON");
            m.EnableKeyword("_WIND_ON");
            m.EnableKeyword("_TRANSMISSION_ON");
            m.EnableKeyword("_HUE_VARIATION_ON");
            return m;
        }

        static Material New(string shaderName, string name)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ProcTree] Shader '{shaderName}' not found — package not imported or the shader failed to compile.");
                return null;
            }
            return new Material(shader) { name = name };
        }
    }
}
