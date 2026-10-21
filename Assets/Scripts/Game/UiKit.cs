using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 글꼴 다섯 개(스펙 12.2). 본문 Noto Medium, 강조 Noto Bold, 숫자·영문 Sora. Sora에는 한글·₩이 없으므로 그런 문자열은 For()로 Noto를 고른다.
    /// Resources에 없으면 Noto Regular → 내장 LegacyRuntime 순으로 대신한다. 가짜 볼드(FontStyle.Bold)는 쓰지 않는다.
    /// </summary>
    public static class Fonts
    {
        public const string RegularPath = "Fonts/NotoSansKR-Regular", BodyPath = "Fonts/NotoSansKR-Medium", BoldPath = "Fonts/NotoSansKR-Bold",
            NumPath = "Fonts/Sora-SemiBold", NumBoldPath = "Fonts/Sora-Bold";
        private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        public static Font Regular => Load(RegularPath);
        public static Font Body => Load(BodyPath);
        public static Font Bold => Load(BoldPath);
        public static Font Num => Load(NumPath);
        public static Font NumBold => Load(NumBoldPath);

        private static Font Load(string path)
        {
            if (cache.TryGetValue(path, out var f) && f != null) return f;
            f = Resources.Load<Font>(path);
            if (f == null && path != RegularPath) f = Resources.Load<Font>(RegularPath);
            if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cache[path] = f;
            return f;
        }

        /// <summary>Sora로 그릴 수 있는 문자열인가(라틴·숫자·기본 기호, − · – —). 한글·₩·화살표가 섞이면 false.</summary>
        public static bool IsLatin(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            foreach (var ch in s)
                if (ch > 0x24F && ch != '−' && ch != '·' && ch != '–' && ch != '—') return false;
            return true;
        }

        /// <summary>숫자용 글꼴을 고른다: 라틴이면 Sora(SemiBold/Bold), 아니면 같은 굵기의 Noto(Medium/Bold).</summary>
        public static Font For(string s, bool bold) => IsLatin(s) ? (bold ? NumBold : Num) : (bold ? Bold : Body);
    }

    /// <summary>UGUI를 코드로 만든다. 모든 색·반경 토큰은 여기 상수에서 나온다(스펙 12.2, 시안 CSS :root).</summary>
    public static partial class UiKit
    {
        private static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        private static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>
        /// 어두운 반투명 면의 alpha 보정. 프로젝트는 Linear 색 공간이라 반투명 UI가 브라우저(sRGB에서 섞음)보다 옅게 섞인다.
        /// 거의 검은 면은 sRGB 결과가 (1−a)^(1/2.2)·뒤 배경이므로 a' = 1 − (1 − a)^2.2이면 시안 CSS와 같은 밝기가 된다.
        /// </summary>
        public static float DarkAlpha(float a) => Linear ? 1f - Mathf.Pow(1f - a, 2.2f) : a;

        /// <summary>흰색 반투명 선·면의 alpha 보정(어두운 패널 위라고 가정). Linear에서 흰색은 훨씬 진하게 섞이므로 낮춘다.</summary>
        public static float LightAlpha(float a)
        {
            if (!Linear) return a;
            const float bg = 0.1f; // 패널 위 sRGB 밝기 근사
            float bgLin = Mathf.Pow(bg, 2.2f), want = Mathf.Pow(a + (1f - a) * bg, 2.2f);
            return Mathf.Clamp01((want - bgLin) / (1f - bgLin));
        }

        /// <summary>흰색 alpha 오버레이(보정 포함).</summary>
        public static Color White(float a) => new Color(1f, 1f, 1f, LightAlpha(a));
        /// <summary>검은색 alpha 오버레이(보정 포함).</summary>
        public static Color Black(float a) => new Color(0f, 0f, 0f, DarkAlpha(a));

        // ── 색 토큰 (시안 CSS 그대로, 반투명 값은 색 공간 보정) ──
        public static readonly Color Panel = Rgb(15, 19, 22, DarkAlpha(0.66f));
        public static readonly Color Panel2 = Rgb(15, 19, 22, DarkAlpha(0.9f));
        public static readonly Color Line = White(0.12f);
        public static readonly Color Line2 = White(0.22f);
        public static readonly Color Fg = Rgb(0xf4, 0xf3, 0xee);
        public static readonly Color Muted = Rgb(0xa9, 0xb0, 0xa5);
        public static readonly Color Accent = Rgb(0xf2, 0xa3, 0x3a);
        public static readonly Color AccentFg = Rgb(0x1f, 0x14, 0x02);
        public static readonly Color Good = Rgb(0x6f, 0xd6, 0x8a);
        public static readonly Color Bad = Rgb(0xff, 0x6f, 0x62);
        public static readonly Color Info = Rgb(0x7c, 0xb5, 0xff);
        public static readonly Color Warn = Rgb(0xff, 0xd6, 0x38);
        /// <summary>EXP 강조색(#7FB7FF).</summary>
        public static readonly Color ExpColor = Rgb(0x7f, 0xb7, 0xff);
        public static readonly Color Backdrop = Rgb(5, 8, 10, DarkAlpha(0.45f));
        public static readonly Color Danger = Rgb(0xc9, 0x47, 0x2f);
        public static readonly Color OkFg = Rgb(0x0f, 0x2a, 0x16);
        public static readonly Color BadFg = Rgb(0x2a, 0x0a, 0x06);
        public static readonly Color Paper = Rgb(0xf6, 0xf2, 0xe8);
        public static readonly Color PaperLine = Rgb(0xe2, 0xd9, 0xc6);
        public static readonly Color PaperInk = Rgb(0x2a, 0x26, 0x20);
        public static readonly Color PaperMuted = Rgb(0x7a, 0x6e, 0x5c);
        public static readonly Color PaperDash = Rgb(0xcd, 0xbf, 0xa3);
        public static readonly Color SlotBg = Black(0.3f);
        /// <summary>키캡·체력 트랙 배경(흰색 .12), 상태창 행(흰색 .05), 버튼 hover(흰색 .08 쪽으로 당긴 패널).</summary>
        public static readonly Color KeyBg = White(0.16f), RowBg = White(0.05f), HoverFill = Rgb(35, 39, 42, DarkAlpha(0.72f));

        // ── 반경 토큰 ──
        public const int RPanel = 12, RButton = 10, RKey = 5, RTile = 7, RCard = 14, RBadge = 8, RSlot = 10;

        /// <summary>그림자 단계. 시안 --shadow(0 10px 30px .35), 카드(0 24px 60px .5), 버튼용 얕은 그림자.</summary>
        public enum Elevation { None, Low, Panel, Card }

        /// <summary>예전 이름. 본문 글꼴(Noto Medium).</summary>
        public static Font DefaultFont => Fonts.Body;

        // ── 아이콘 ──
        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        /// <summary>Resources/UI/Icons/{name}.png(흰색 64px). 스프라이트로 임포트되지 않았으면 텍스처에서 만든다. 없으면 null.</summary>
        public static Sprite Icon(string name)
        {
            if (icons.TryGetValue(name, out var s) && s != null) return s;
            if (name == "ui-settings") return icons[name] = UiSprites.SlidersIcon(); // 아이콘 묶음에 없는 설정 아이콘은 코드로 그린다
            if (name == HudController.LabelerIconName) return icons[name] = UiSprites.WaybillIcon(); // 운송장 부착대(스펙 15.1)
            s = Resources.Load<Sprite>("UI/Icons/" + name);
            if (s == null)
            {
                var tex = Resources.Load<Texture2D>("UI/Icons/" + name);
                if (tex != null) s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            icons[name] = s;
            return s;
        }

        public static Canvas MakeCanvas(string name = "HUD", int sortingOrder = 0)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 800f);
            scaler.matchWidthOrHeight = 0.5f;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                es.transform.SetParent(go.transform, false);
            }
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
            return rt;
        }

        /// <summary>부모를 꽉 채우는 RectTransform(오프셋으로 안쪽/바깥쪽 여백).</summary>
        public static RectTransform Fill(string name, Transform parent, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>둥근 모서리 채움 상자(테두리·그림자 없음). radius 0이면 직각(화면 전체 막처럼 모서리가 안 보이는 곳에만).</summary>
        public static Image Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color color, int radius)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (radius > 0) { img.sprite = UiSprites.Rounded(radius); img.type = Image.Type.Sliced; }
            img.raycastTarget = false;
            return img;
        }

        /// <summary>패널: 둥근 채움 + 1px 선 + 아래로 번지는 그림자(시안 .panel).</summary>
        public static Image PanelBox(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color? color = null,
            int radius = RPanel, Color? line = null, Elevation elevation = Elevation.Panel)
        {
            var img = Box(name, parent, anchorMin, anchorMax, pivot, pos, size, color ?? Panel, radius);
            img.raycastTarget = true;
            if (elevation != Elevation.None) AddShadow(img.rectTransform, radius, elevation);
            AddBorder(img.rectTransform, radius, line ?? Line);
            return img;
        }

        /// <summary>안쪽 1px(또는 width) 선. 채움 위, 내용 아래에 그린다.</summary>
        public static Image AddBorder(RectTransform host, int radius, Color color, float width = 1f, float outset = 0f, string name = "Border")
        {
            var rt = Fill(name, host, -outset, -outset, -outset, -outset);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Ring(radius + Mathf.RoundToInt(outset), width); img.type = Image.Type.Sliced;
            img.color = color; img.raycastTarget = false;
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return img;
        }

        /// <summary>그림자를 호스트의 첫 자식으로 붙인다. 스프라이트가 호스트 안쪽을 비워 두므로(CSS box-shadow처럼) 자식이어도 호스트를 가리지 않는다.</summary>
        public static Image AddShadow(RectTransform host, int radius, Elevation e)
        {
            int oy, blur; float alpha;
            switch (e)
            {
                case Elevation.Low: oy = 2; blur = 6; alpha = 0.28f; break;
                case Elevation.Card: oy = 24; blur = 60; alpha = 0.5f; break;
                default: oy = 10; blur = 30; alpha = 0.35f; break;
            }
            return AddGlow(host, radius, oy, blur, Black(alpha), "Shadow");
        }

        /// <summary>번짐(그림자·빛). oy만큼 아래로 옮긴 같은 모양을 blur(CSS px)로 흐린다. 호스트 높이가 작으면 그 높이 전용 스프라이트를 쓴다.</summary>
        public static Image AddGlow(RectTransform host, int radius, int oy, int blur, Color color, string name = "Glow")
        {
            float h = host.anchorMin.y == host.anchorMax.y ? host.sizeDelta.y : host.rect.height;
            int fixedH = h > 0f && h < 2 * radius + oy + 2 ? Mathf.RoundToInt(h) : 0;
            var sprite = UiSprites.Shadow(radius, oy, blur, fixedH, out var m);
            var rt = Fill(name, host, -m.x, -m.y, -m.z, -m.w);
            rt.SetAsFirstSibling();
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return img;
        }

        /// <summary>1px 구분선(시안 border-top: 1px solid var(--line)).</summary>
        public static Image Divider(string name, Transform parent, Color? color = null)
        {
            var rt = Rect(name, parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? Line; img.raycastTarget = false;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, Font font = null)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font != null ? font : Fonts.Body; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = FontStyle.Normal;
            t.text = text; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>글자 내용에 맞는 글꼴(Sora 또는 Noto)로 바꿔 넣는다.</summary>
        public static void SetNumText(Text t, string s, bool bold)
        {
            if (t.text != s) t.text = s;
            var f = Fonts.For(s, bold);
            if (t.font != f) t.font = f;
        }

        /// <summary>RectTransform을 부모의 한 점(anchor)에 고정 크기로 놓는다.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing, RectOffset padding)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing; g.padding = padding; g.childAlignment = TextAnchor.MiddleLeft;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false; g.childControlWidth = true; g.childControlHeight = true;
            return g;
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing, RectOffset padding)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing; g.padding = padding; g.childAlignment = TextAnchor.UpperLeft;
            g.childForceExpandWidth = true; g.childForceExpandHeight = false; g.childControlWidth = true; g.childControlHeight = true;
            return g;
        }

        public static LayoutElement Size(Component c, float w, float h)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
            return le;
        }
    }
}
