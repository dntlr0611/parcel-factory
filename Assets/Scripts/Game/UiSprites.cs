using System.Collections.Generic;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 둥근 모서리 채움·테두리·그림자 9-slice 스프라이트를 런타임에 SDF(부호 거리)로 만들어 캐시한다(스펙 12.2).
    /// 텍스처는 디자인 px의 Supersample배로 그리고 pixelsPerUnit도 같은 배수로 올려, 캔버스에서는 반경이 디자인 px 그대로 보이면서 더 선명하다.
    /// 모든 스프라이트는 흰색이고 Image.color로 물들인다.
    /// </summary>
    public static class UiSprites
    {
        public const int Supersample = 2;
        private const float BasePpu = 100f; // Canvas.referencePixelsPerUnit 기본값
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Vector4> margins = new Dictionary<string, Vector4>();

        /// <summary>반경 radius(디자인 px)의 둥근 사각 채움. 알약은 radius = 높이/2.</summary>
        public static Sprite Rounded(int radius) => Get("fill" + radius, () => MakeRounded(radius, 0f));

        /// <summary>안쪽으로 width(디자인 px) 두께의 둥근 테두리.</summary>
        public static Sprite Ring(int radius, float width) => Get("ring" + radius + "_" + width, () => MakeRounded(radius, width));

        /// <summary>
        /// 그림자(또는 빛 번짐): 호스트와 같은 모양을 oy만큼 아래로 옮겨 CSS blur(σ = blur/2)로 흐리고, 호스트 안쪽은 비운다.
        /// margin = 호스트 바깥으로 나가는 (왼쪽, 아래, 오른쪽, 위) 디자인 px. fixedHeight &gt; 0이면 그 높이 전용(세로로 늘리지 않음).
        /// </summary>
        public static Sprite Shadow(int radius, int oy, int blur, int fixedHeight, out Vector4 margin)
        {
            string key = "shadow" + radius + "_" + oy + "_" + blur + "_" + fixedHeight;
            var s = Get(key, () => MakeShadow(radius, oy, blur, fixedHeight, key));
            margin = margins[key];
            return s;
        }

        /// <summary>가로 그라데이션(왼쪽 alpha a0 → 오른쪽 a1, 부드러운 곡선). 시작 화면 막에 쓴다.</summary>
        public static Sprite HorizontalFade(float a0, float a1) => Get("hfade" + a0 + "_" + a1, () =>
        {
            const int w = 256;
            var tex = NewTex(w, 4);
            var px = new Color32[w * 4];
            for (int x = 0; x < w; x++)
            {
                float t = x / (w - 1f);
                float a = Mathf.Lerp(a0, a1, t * t * (3f - 2f * t));
                for (int y = 0; y < 4; y++) px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, w, 4), new Vector2(0.5f, 0.5f), BasePpu, 0, SpriteMeshType.FullRect);
        });

        /// <summary>프롬프트 꼬리(아래를 가리키는 45° 마름모의 아래 절반). line=true면 아래 두 변만 1px 선.</summary>
        public static Sprite Tail(bool line) => Get("tail" + line, () =>
        {
            const int S = Supersample;
            const float half = 7.07f; // 10px 정사각형을 45° 돌린 대각선의 절반
            int w = Mathf.CeilToInt(half * 2f * S) + 2 * S, h = Mathf.CeilToInt((half + 3f) * S);
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            float cx = w * 0.5f, cy = h - 3f * S; // 마름모 중심(위에서 3px 아래), 위로 3px는 알약 테두리를 덮는다
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - cx), dy = cy - (y + 0.5f); // dy > 0: 중심 아래
                    float d = (dx + Mathf.Max(dy, 0f) - half * S) / 1.41421f; // 아래 두 변까지의 거리(위쪽은 띠 모양 그대로)
                    float fill = Mathf.Clamp01(0.5f - d);
                    float a = line ? (dy > -0.5f ? fill - Mathf.Clamp01(0.5f - (d + S)) : 0f) : fill;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), BasePpu * S, 0, SpriteMeshType.FullRect);
        });

        /// <summary>점선 한 칸(가로 3px 선 + 2px 빈칸). Image.Type.Tiled로 깔아 쓴다.</summary>
        public static Sprite Dash() => Get("dash", () =>
        {
            const int S = Supersample;
            int w = 5 * S, h = S;
            var tex = NewTex(w, h);
            tex.wrapMode = TextureWrapMode.Repeat;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)(x < 3 * S ? 255 : 0));
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), BasePpu * S, 0, SpriteMeshType.FullRect);
        });

        /// <summary>설정 아이콘(가로줄 3개 + 손잡이 원, 64px 흰색). 아이콘 묶음에 톱니가 없어 코드로 그린다.</summary>
        public static Sprite SlidersIcon() => Get("icon-sliders", () =>
        {
            const int n = 64;
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            float[] ys = { 46f, 32f, 18f }, ks = { 22f, 42f, 28f };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f, d = float.MaxValue;
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = Mathf.Clamp(px0, 10f, 54f);
                        float line = new Vector2(px0 - cx, py0 - ys[i]).magnitude - 2.5f;  // 굵기 5 캡슐
                        float ring = Mathf.Abs(new Vector2(px0 - ks[i], py0 - ys[i]).magnitude - 6f) - 2.5f; // 손잡이 링
                        float hole = 4f - new Vector2(px0 - ks[i], py0 - ys[i]).magnitude; // 링 안쪽은 선을 지운다
                        d = Mathf.Min(d, Mathf.Min(Mathf.Max(line, hole), ring));
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), BasePpu, 0, SpriteMeshType.FullRect);
        });

        /// <summary>운송장 부착대 아이콘(64px 흰색): 머리띠가 굵은 종이 한 장 + 바코드 줄 3개. 아이콘 묶음에 없어 코드로 그린다(스펙 15.1).</summary>
        public static Sprite WaybillIcon() => Get("icon-waybill", () =>
        {
            const int n = 64;
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f - 32f, y + 0.5f - 32f);
                    float outer = Sdf(p, new Vector2(22f, 26f), 5f);
                    float sheet = Mathf.Abs(outer + 2.5f) - 2.5f;                                  // 종이 테두리(두께 5)
                    float band = Sdf(p - new Vector2(0f, 15f), new Vector2(20f, 6f), 1f);          // 머리띠
                    float d = Mathf.Min(sheet, band);
                    for (int i = 0; i < 3; i++)                                                     // 바코드 줄
                        d = Mathf.Min(d, Sdf(p - new Vector2(-8f + i * 8f, -8f), new Vector2(i == 1 ? 1.5f : 2.5f, 9f), 0.5f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), BasePpu, 0, SpriteMeshType.FullRect);
        });

        private static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s.texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            cache[key] = s;
            return s;
        }

        private static Texture2D NewTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PF_UiSprite" };
            return tex;
        }

        /// <summary>둥근 사각의 부호 거리(안쪽 음수). p는 중심 기준, b는 반폭·반높이, r은 반경.</summary>
        private static float Sdf(Vector2 p, Vector2 b, float r)
        {
            float qx = Mathf.Abs(p.x) - (b.x - r), qy = Mathf.Abs(p.y) - (b.y - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Sprite MakeRounded(int radius, float ringWidth)
        {
            const int S = Supersample;
            int r = Mathf.Max(radius, 1) * S;
            int n = 2 * r + 2 * S; // 가운데 2px(디자인 1px)이 늘어나는 칸
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            var b = new Vector2(n * 0.5f, n * 0.5f);
            float w = ringWidth * S;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Sdf(new Vector2(x + 0.5f - b.x, y + 0.5f - b.y), b, radius > 0 ? r : 0.01f);
                    float a = Mathf.Clamp01(0.5f - d);
                    if (w > 0f) a -= Mathf.Clamp01(0.5f - (d + w));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), BasePpu * S, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        private static Sprite MakeShadow(int radius, int oy, int blur, int fixedHeight, string key)
        {
            const int S = Supersample;
            float sigma = Mathf.Max(blur * 0.5f, 0.5f);
            int ext = Mathf.CeilToInt(sigma * 2.6f);
            int R = radius;
            int W = 2 * R + 2, H = fixedHeight > 0 ? fixedHeight : 2 * R + oy + 2;
            int mL = ext, mR = ext, mT = Mathf.Max(0, ext - oy), mB = ext + oy;
            int tw = (mL + W + mR) * S, th = (mT + H + mB) * S;
            var tex = NewTex(tw, th);
            var px = new Color32[tw * th];
            var half = new Vector2(W * 0.5f * S, H * 0.5f * S);
            float pcx = (mL + W * 0.5f) * S, pcy = (mT + H * 0.5f) * S; // 위에서 잰 호스트 중심
            float rr = Mathf.Min(R, H * 0.5f) * S;
            float inv = 1f / (sigma * S * 1.41421356f);
            for (int y = 0; y < th; y++)
            {
                float top = th - (y + 0.5f); // 위에서 잰 좌표
                for (int x = 0; x < tw; x++)
                {
                    var p = new Vector2(x + 0.5f - pcx, top - pcy);
                    float dPanel = Sdf(p, half, rr);
                    float dShadow = Sdf(new Vector2(p.x, p.y - oy * S), half, rr);
                    float v = 0.5f * Erfc(dShadow * inv);
                    float a = v * (1f - Mathf.Clamp01(0.5f - dPanel));
                    px[y * tw + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px); tex.Apply(false, false);
            int left = (mL + R) * S, right = tw - left - 2 * S;
            int topB, bottomB;
            if (fixedHeight > 0) { topB = th / 2; bottomB = th - topB - 1; }
            else { topB = (mT + R + oy) * S; bottomB = th - topB - 2 * S; }
            margins[key] = new Vector4(mL, mB, mR, mT);
            // Sprite border 순서: 왼쪽, 아래, 오른쪽, 위
            return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), BasePpu * S, 0, SpriteMeshType.FullRect, new Vector4(left, bottomB, right, topB));
        }

        /// <summary>상보 오차 함수 근사(Numerical Recipes erfcc, 오차 1.2e-7).</summary>
        private static float Erfc(float x)
        {
            float z = Mathf.Abs(x);
            float t = 1f / (1f + 0.5f * z);
            float r = t * Mathf.Exp(-z * z - 1.26551223f + t * (1.00002368f + t * (0.37409196f + t * (0.09678418f + t * (-0.18628806f + t * (0.27886807f + t * (-1.13520398f + t * (1.48851587f + t * (-0.82215223f + t * 0.17087277f)))))))));
            return x >= 0f ? r : 2f - r;
        }
    }
}
