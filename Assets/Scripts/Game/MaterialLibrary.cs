using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParcelFactory.Game
{
    /// <summary>URP Lit/Unlit 재질을 코드로 만든다. 같은 인자는 캐시해 재사용한다.</summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Lit(Color c, float smoothness = 0.1f)
        {
            string key = $"lit:{c}:{smoothness}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            return cache[key] = m;
        }

        public static Material Emissive(Color c, float strength = 1.5f)
        {
            string key = $"emi:{c}:{strength}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = Lit(c, 0.3f);
            m = new Material(m);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return cache[key] = m;
        }

        public static Material Transparent(Color c)
        {
            string key = $"tr:{c}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return cache[key] = m;
        }
    }
}
