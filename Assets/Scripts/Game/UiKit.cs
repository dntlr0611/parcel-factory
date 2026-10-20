using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>UGUI를 코드로 만든다. 모든 색은 여기 상수에서 나온다.</summary>
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.06f, 0.08f, 0.09f, 0.74f);
        public static readonly Color Panel2 = new Color(0.06f, 0.08f, 0.09f, 0.92f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color Fg = new Color(0.95f, 0.96f, 0.94f);
        public static readonly Color Muted = new Color(0.73f, 0.75f, 0.71f);
        public static readonly Color Accent = new Color(1f, 0.72f, 0.29f);
        public static readonly Color AccentFg = new Color(0.14f, 0.1f, 0.02f);
        public static readonly Color Good = new Color(0.37f, 0.83f, 0.48f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);
        public static readonly Color Warn = new Color(1f, 0.84f, 0.22f);
        /// <summary>EXP 강조색(#7FB7FF).</summary>
        public static readonly Color ExpColor = new Color(127f / 255f, 183f / 255f, 1f);

        /// <summary>Resources 아래 UI 폰트 경로(Noto Sans KR, SIL OFL). 파일이 없으면 Unity 내장 LegacyRuntime으로 대신한다.</summary>
        public const string FontResourcePath = "Fonts/NotoSansKR-Regular";
        private static Font font;

        public static Font DefaultFont
        {
            get
            {
                if (font == null) font = Resources.Load<Font>(FontResourcePath);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Canvas MakeCanvas()
        {
            var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

        public static Image PanelBox(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color? color = null)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? Panel;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style;
            t.text = text; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string text, Vector2 size, System.Action onClick, Color? bg = null, Color? fg = null, int fontSize = 14, Vector2? anchor = null)
        {
            // anchor: 부모 rect 안의 기준점(0~1). 생략하면 중앙. 피벗은 항상 버튼 중심이라 anchoredPosition은 기준점에서 버튼 중심까지의 거리다.
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var rt = Rect(name, parent, a, a, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg ?? new Color(0.15f, 0.17f, 0.2f, 0.9f);
            var b = rt.gameObject.AddComponent<Button>();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = b.colors; colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f); colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); b.colors = colors;
            b.onClick.AddListener(() => onClick?.Invoke());
            var label = Label("Label", rt, text, fontSize, fg ?? Fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.offsetMin = new Vector2(6f, 2f); label.rectTransform.offsetMax = new Vector2(-6f, -2f);
            return b;
        }

        public static void SetButtonLook(Button b, bool on)
        {
            b.GetComponent<Image>().color = on ? Accent : new Color(0.15f, 0.17f, 0.2f, 0.9f);
            b.GetComponentInChildren<Text>().color = on ? AccentFg : Fg;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing, RectOffset padding)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing; g.padding = padding; g.childAlignment = TextAnchor.MiddleLeft;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false; g.childControlWidth = false; g.childControlHeight = false;
            return g;
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing, RectOffset padding)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing; g.padding = padding; g.childAlignment = TextAnchor.UpperLeft;
            g.childForceExpandWidth = true; g.childForceExpandHeight = false; g.childControlWidth = true; g.childControlHeight = false;
            return g;
        }

        public static LayoutElement Size(Component c, float w, float h)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = h; le.minWidth = w; le.minHeight = h;
            return le;
        }
    }
}
