using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParcelFactory.Game
{
    /// <summary>URP Lit/Unlit 재질을 코드로 만든다. 같은 인자는 캐시해 재사용한다.</summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        /// <summary>
        /// Resources/Materials의 템플릿(에디터 MaterialTemplates.Build로 생성)을 복제한다.
        /// 빌드는 에셋이 참조하는 셰이더 변형만 남기므로 Shader.Find만으로는 Unlit·투명·발광 변형이 빠진다. 템플릿이 없으면 Shader.Find로 대신한다.
        /// </summary>
        private static Material FromTemplate(string name, string shader)
        {
            var t = Resources.Load<Material>("Materials/" + name);
            return t != null ? new Material(t) : new Material(Shader.Find(shader));
        }

        public static Material Lit(Color c, float smoothness = 0.1f)
        {
            string key = $"lit:{c}:{smoothness}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = FromTemplate("PF_Lit", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            return cache[key] = m;
        }

        public static Material Emissive(Color c, float strength = 1.5f)
        {
            string key = $"emi:{c}:{strength}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = FromTemplate("PF_Emissive", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return cache[key] = m;
        }

        public static Material Transparent(Color c)
        {
            string key = $"tr:{c}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = FromTemplate("PF_Transparent", "Universal Render Pipeline/Unlit");
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

        /// <summary>
        /// 텍스처 데칼(알파 컷아웃, 스펙 14 항공 마크·표지판): 텍스처 색 그대로, 방향광과 무관하게 읽히도록 같은 텍스처로 glow배 발광한다.
        /// 템플릿 PF_Cutout(Lit + _ALPHATEST_ON + _EMISSION)을 복제한다.
        /// </summary>
        public static Material Decal(Texture tex, float glow = 0.6f)
        {
            string key = $"decal:{(tex != null ? tex.GetInstanceID() : 0)}:{glow}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = FromTemplate("PF_Cutout", "Universal Render Pipeline/Lit");
            m.name = "Decal-" + (tex != null ? tex.name : "none");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", Color.white * glow);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return cache[key] = m;
        }

        /// <summary>운송장 머리띠 먹색 #2A2F36(스펙 15.4). 운송장은 한 종류이고 반품 여부는 검사 도장이 나타낸다. 카드와 3D가 같은 색.</summary>
        public static readonly Color WaybillInk = new Color(0x2A / 255f, 0x2F / 255f, 0x36 / 255f);

        /// <summary>상자 윗면 운송장(스펙 15.3·15.4): 128×84 흰 종이, 위 22px 먹색 머리띠, 회색 글줄 2개, 아래 바코드 줄. 한 번 만들어 둔다.</summary>
        public static Material Waybill()
        {
            if (waybill != null) return waybill;
            Material m;
            const int w = 128, h = 84;
            var buf = new Color[w * h];
            var paper = new Color(0.97f, 0.96f, 0.93f, 1f);
            for (int i = 0; i < buf.Length; i++) buf[i] = paper;
            FillRect(buf, w, h, 0, h - 22, w, 22, WaybillInk); // y = 0이 아래 행
            var ink = new Color(0.35f, 0.35f, 0.38f, 1f);
            FillRect(buf, w, h, 10, h - 34, 70, 5, ink);
            FillRect(buf, w, h, 10, h - 44, 46, 4, ink);
            var bar = new Color(0.1f, 0.1f, 0.12f, 1f);
            var rng = new System.Random(9);
            for (int x = 10; x < w - 10;)
            {
                int bw = 2 + rng.Next(4);
                FillRect(buf, w, h, x, 8, bw, 22, bar);
                x += bw + 2 + rng.Next(3);
            }
            var tex = MakeTexture(w, h, buf, TextureWrapMode.Clamp, "Waybill");
            m = new Material(Lit(Color.white, 0.15f)) { name = tex.name };
            m.SetTexture("_BaseMap", tex);
            return waybill = m;
        }

        private static Material waybill;

        /// <summary>n×n 칸 창고 바닥: 기본색 tone + 미세 노이즈 + 칸마다 어두운 이음새(2px)와 밝은 하이라이트(1px). 칸당 64px.</summary>
        public static Material Concrete(int tiles, Color baseTone)
        {
            string key = $"concrete:{tiles}:{baseTone}";
            if (cache.TryGetValue(key, out var m)) return m;
            const int px = 64;
            int size = Mathf.Max(1, tiles) * px;
            var buf = new Color[size * size];
            for (int i = 0; i < buf.Length; i++) buf[i] = baseTone;
            var rng = new System.Random(5);
            var darkSpeck = new Color(170 / 255f, 164 / 255f, 154 / 255f, 0.35f);
            var lightSpeck = new Color(235 / 255f, 231 / 255f, 224 / 255f, 0.35f);
            for (int i = 0, count = tiles * tiles * 260; i < count; i++)
            {
                var c = rng.NextDouble() < 0.5 ? darkSpeck : lightSpeck;
                int x = rng.Next(size), y = rng.Next(size), w = 1 + rng.Next(2), h = 1 + rng.Next(2);
                FillRect(buf, size, size, x, y, w, h, c);
            }
            var seam = new Color(120 / 255f, 114 / 255f, 104 / 255f, 0.55f);
            var hi = new Color(1f, 1f, 1f, 0.25f);
            for (int i = 0; i <= tiles; i++)
            {
                int p = i * px;
                FillRect(buf, size, size, p - 1, 0, 2, size, seam);   // 세로 이음새
                FillRect(buf, size, size, 0, p - 1, size, 2, seam);   // 가로 이음새
                if (i < tiles)
                {
                    FillRect(buf, size, size, p + 2, 0, 1, size, hi);
                    FillRect(buf, size, size, 0, p + 2, size, 1, hi);
                }
            }
            var tex = MakeTexture(size, size, buf, TextureWrapMode.Clamp, "Concrete" + tiles);
            m = Lit(Color.white, 0.08f);
            m = new Material(m) { name = "Concrete" + tiles };
            m.SetTexture("_BaseMap", tex);
            return cache[key] = m;
        }

        /// <summary>바깥 야드 아스팔트: 512px 어두운 회색 + 노이즈, 16회 반복.</summary>
        public static Material Asphalt()
        {
            const string key = "asphalt";
            if (cache.TryGetValue(key, out var m)) return m;
            const int size = 512;
            var buf = new Color[size * size];
            var baseC = new Color(0x4a / 255f, 0x4b / 255f, 0x4e / 255f, 1f);
            for (int i = 0; i < buf.Length; i++) buf[i] = baseC;
            var rng = new System.Random(3);
            var dark = new Color(60 / 255f, 61 / 255f, 64 / 255f, 0.5f);
            var light = new Color(96 / 255f, 97 / 255f, 100 / 255f, 0.45f);
            for (int i = 0; i < 14000; i++)
            {
                var c = rng.NextDouble() < 0.5 ? dark : light;
                FillRect(buf, size, size, rng.Next(size), rng.Next(size), 1 + rng.Next(2), 1 + rng.Next(2), c, wrap: true);
            }
            var tex = MakeTexture(size, size, buf, TextureWrapMode.Repeat, "Asphalt");
            m = new Material(Lit(Color.white, 0f)) { name = "Asphalt" };
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", new Vector2(16f, 16f));
            return cache[key] = m;
        }

        /// <summary>노랑(#F2C230)/검정 사선 위험 테이프, 64×16, 반복. repeatU는 테이프 길이 방향 반복 횟수.</summary>
        public static Material Hazard(float repeatU = 1f)
        {
            string key = $"hazard:{repeatU}";
            if (cache.TryGetValue(key, out var m)) return m;
            if (!hazardTex)
            {
                const int w = 64, h = 16;
                var buf = new Color[w * h];
                var yellow = new Color(0xf2 / 255f, 0xc2 / 255f, 0x30 / 255f, 1f);
                var black = new Color(0x2a / 255f, 0x2a / 255f, 0x2a / 255f, 1f);
                for (int r = 0; r < h; r++)
                {
                    float yc = (h - 1 - r) + 0.5f; // 캔버스 좌표(위가 0)
                    for (int x = 0; x < w; x++)
                    {
                        float s = (x + 0.5f) - (h - yc);
                        float mod = ((s % 16f) + 16f) % 16f;
                        buf[r * w + x] = mod < 8f ? black : yellow;
                    }
                }
                hazardTex = MakeTexture(w, h, buf, TextureWrapMode.Repeat, "Hazard");
            }
            m = new Material(Lit(Color.white, 0.15f)) { name = "Hazard" };
            m.SetTexture("_BaseMap", hazardTex);
            m.SetTextureScale("_BaseMap", new Vector2(repeatU, 1f));
            return cache[key] = m;
        }

        private static Texture2D hazardTex;

        private static Texture2D MakeTexture(int w, int h, Color[] pixels, TextureWrapMode wrap, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) // sRGB
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8,
            };
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>알파 블렌딩으로 사각형을 칠한다. y = 0이 아래 행.</summary>
        private static void FillRect(Color[] buf, int w, int h, int x0, int y0, int rw, int rh, Color c, bool wrap = false)
        {
            for (int y = y0; y < y0 + rh; y++)
            {
                int yy = y;
                if (wrap) yy = ((y % h) + h) % h; else if (y < 0 || y >= h) continue;
                for (int x = x0; x < x0 + rw; x++)
                {
                    int xx = x;
                    if (wrap) xx = ((x % w) + w) % w; else if (x < 0 || x >= w) continue;
                    int i = yy * w + xx;
                    var d = buf[i];
                    buf[i] = new Color(d.r + (c.r - d.r) * c.a, d.g + (c.g - d.g) * c.a, d.b + (c.b - d.b) * c.a, 1f);
                }
            }
        }
    }
}
