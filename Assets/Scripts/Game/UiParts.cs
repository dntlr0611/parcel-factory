using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>버튼 변형(시안 .btn, .btn.on, .btn.primary, .btn.danger, .btn.ok).</summary>
    public enum BtnStyle { Normal, On, Primary, Danger, Ok }

    /// <summary>
    /// 시안 .btn의 상태 표현: hover(배경 흰색 .08 · 선 line-2), 눌림(내용 1px 아래), 비활성(투명도 .4), 변형별 색.
    /// Unity Button의 색 전환은 끄고 이 컴포넌트가 채움·선·글자·아이콘·키캡 색을 직접 칠한다.
    /// </summary>
    public sealed class UiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Button Button;
        public Image Fill, Border, Icon;
        public Text Label;
        public Keycap Key;
        public HorizontalLayoutGroup Layout;
        public BtnStyle Style;
        /// <summary>선택 링(시작 화면 메뉴 등에서 켠다). 없으면 null.</summary>
        public Image Focus;
        public event Action Hovered;
        private bool hover, down;
        /// <summary>마우스가 올라가 있는가.</summary>
        public bool Hover => hover;
        private RectOffset basePadding;

        public void SetStyle(BtnStyle s) { Style = s; Apply(); }
        public void SetFocus(bool on) { if (Focus != null && Focus.gameObject.activeSelf != on) Focus.gameObject.SetActive(on); }

        public void OnPointerEnter(PointerEventData e) { hover = true; Apply(); Hovered?.Invoke(); }
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { down = true; Apply(); }
        public void OnPointerUp(PointerEventData e) { down = false; Apply(); }
        /// <summary>hover·눌림 중에 숨겨진 버튼이 다시 보일 때 그 모양으로 나타나지 않게 한다.</summary>
        private void OnDisable() { hover = false; down = false; if (Fill != null) Apply(); }

        public void Apply()
        {
            if (Fill == null) return;
            bool enabledLook = Button == null || Button.interactable;
            bool h = hover && enabledLook;
            Color fill, line, fg, keyBg, keyLine;
            switch (Style)
            {
                case BtnStyle.On:
                case BtnStyle.Primary:
                    fill = h ? Color.Lerp(UiKit.Accent, Color.white, 0.12f) : UiKit.Accent; line = fill; fg = UiKit.AccentFg;
                    keyBg = UiKit.Black(0.12f); keyLine = UiKit.Black(0.2f); break;
                case BtnStyle.Danger:
                    fill = h ? Color.Lerp(UiKit.Danger, Color.white, 0.1f) : UiKit.Danger; line = fill; fg = Color.white;
                    keyBg = UiKit.Black(0.12f); keyLine = UiKit.Black(0.2f); break;
                case BtnStyle.Ok:
                    fill = h ? Color.Lerp(UiKit.Good, Color.white, 0.12f) : UiKit.Good; line = fill; fg = UiKit.OkFg;
                    keyBg = UiKit.Black(0.12f); keyLine = UiKit.Black(0.2f); break;
                default:
                    // 시안 hover는 흰색 .08 배경(뒤를 흐리는 backdrop-filter 전제). 블러가 없으니 패널색을 흰색 쪽으로 8% 당긴다.
                    fill = h ? UiKit.HoverFill : UiKit.Panel; line = h ? UiKit.Line2 : UiKit.Line; fg = UiKit.Fg;
                    keyBg = UiKit.KeyBg; keyLine = UiKit.Line2; break;
            }
            float k = enabledLook ? 1f : 0.4f;
            Fill.color = Mul(fill, k);
            if (Border != null) Border.color = Mul(line, k);
            if (Label != null) Label.color = Mul(fg, k);
            if (Icon != null) Icon.color = Mul(fg, k);
            if (Key != null) Key.SetColors(Mul(keyBg, k), Mul(keyLine, k), Mul(fg, k));
            if (Layout != null)
            {
                if (basePadding == null) basePadding = new RectOffset(Layout.padding.left, Layout.padding.right, Layout.padding.top, Layout.padding.bottom);
                int shift = down && enabledLook ? 1 : 0;
                if (Layout.padding.top != basePadding.top + shift * 2)
                {
                    Layout.padding = new RectOffset(basePadding.left, basePadding.right, basePadding.top + shift * 2, basePadding.bottom);
                    LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
                }
            }
        }

        private static Color Mul(Color c, float k) { c.a *= k; return c; }
    }

    /// <summary>마우스가 올라가 있는지만 기억한다(핫바 슬롯 hover 선).</summary>
    public sealed class UiHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public bool Over { get; private set; }
        public void OnPointerEnter(PointerEventData e) => Over = true;
        public void OnPointerExit(PointerEventData e) => Over = false;
        private void OnDisable() => Over = false;
    }

    /// <summary>키캡(시안 .key): 반경 5, 흰색 .12 배경 + line-2 선, Sora SemiBold 10. span이라 content-box: 높이 18+2, 최소 폭 18+10+2(시안 화면에서 30×20).</summary>
    public sealed class Keycap : MonoBehaviour
    {
        public Image Bg, Border;
        public Text Text;
        public void SetColors(Color bg, Color line, Color fg) { Bg.color = bg; Border.color = line; Text.color = fg; }
        public void SetText(string s) => UiKit.SetNumText(Text, s, false);
    }

    public static partial class UiKit
    {
        /// <summary>키캡 겉 크기(시안 .key는 content-box라 테두리·패딩을 더해 20 높이, 최소 30 폭으로 그려진다).</summary>
        public const float KeyHeight = 20f, KeyMinWidth = 30f;

        /// <summary>키캡 하나. 부모가 레이아웃 그룹이면 내용 폭으로, 아니면 sizeDelta로 크기를 잡는다.</summary>
        public static Keycap Keycap(Transform parent, string text, float height = KeyHeight, int fontSize = 10, string name = "Key")
        {
            float minW = Mathf.Round(height * KeyMinWidth / KeyHeight);
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(minW, height));
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.Rounded(RKey); bg.type = Image.Type.Sliced; bg.raycastTarget = false;
            var border = AddBorder(rt, RKey, Line2);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.padding = new RectOffset(6, 6, 0, 0); g.childAlignment = TextAnchor.MiddleCenter;
            g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            var le = rt.gameObject.AddComponent<LayoutElement>(); le.minWidth = minW; le.minHeight = height; le.preferredHeight = height;
            var t = Label("Text", rt, text, fontSize, Fg, TextAnchor.MiddleCenter, Fonts.Num);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>(); fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var k = rt.gameObject.AddComponent<Keycap>();
            k.Bg = bg; k.Border = border; k.Text = t;
            k.SetText(text);
            k.SetColors(KeyBg, Line2, Fg);
            return k;
        }

        /// <summary>
        /// 시안 .btn: 높이 34, 좌우 12(+1px 선, button은 border-box), 아이콘 15 + 라벨 + 키캡, 간격 8, 반경 10. 레이아웃 그룹 안이면 내용 폭이 선호 폭이 된다.
        /// fixedSize를 주면 그 크기로 고정(가운데 정렬). 레이아웃 그룹 밖에서 내용 폭으로 쓰려면 fit = true.
        /// </summary>
        public static Button Btn(string name, Transform parent, string label, Action onClick, string icon = null, string key = null, BtnStyle style = BtnStyle.Normal,
            float height = 34f, int fontSize = 13, Font font = null, int padX = 13, float minWidth = 0f, Vector2? fixedSize = null, int radius = RButton,
            bool fit = false, Elevation elevation = Elevation.Low, TextAnchor align = TextAnchor.MiddleCenter, float iconSize = 15f)
        {
            var size = fixedSize ?? new Vector2(Mathf.Max(minWidth, 40f), height);
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var fill = rt.gameObject.AddComponent<Image>();
            fill.sprite = UiSprites.Rounded(radius); fill.type = Image.Type.Sliced;
            if (elevation != Elevation.None) AddShadow(rt, radius, elevation);
            var border = AddBorder(rt, radius, Line);
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.targetGraphic = fill;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.padding = new RectOffset(padX, padX, 0, 0); g.spacing = 8f; g.childAlignment = align;
            g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = size.y;
            if (minWidth > 0f) le.minWidth = minWidth;
            if (fixedSize.HasValue) { le.minWidth = le.preferredWidth = size.x; }
            var ui = rt.gameObject.AddComponent<UiButton>();
            ui.Button = b; ui.Fill = fill; ui.Border = border; ui.Layout = g; ui.Style = style;
            if (!string.IsNullOrEmpty(icon))
            {
                var irt = Rect("Icon", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
                var img = irt.gameObject.AddComponent<Image>(); img.sprite = Icon(icon); img.preserveAspect = true; img.raycastTarget = false;
                Size(img, iconSize, iconSize);
                ui.Icon = img;
            }
            if (label != null)
            {
                var t = Label("Label", rt, label, fontSize, Fg, TextAnchor.MiddleCenter, font != null ? font : (style == BtnStyle.Primary ? Fonts.Bold : Fonts.Body));
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                ui.Label = t;
            }
            if (!string.IsNullOrEmpty(key)) ui.Key = Keycap(rt, key);
            if (fit)
            {
                var f = rt.gameObject.AddComponent<ContentSizeFitter>(); f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            ui.Apply();
            return b;
        }

        /// <summary>예전 호출 형태. 고정 크기 버튼.</summary>
        public static Button Button(string name, Transform parent, string text, Vector2 size, Action onClick, BtnStyle style = BtnStyle.Normal, int fontSize = 13, Vector2? anchor = null)
        {
            var b = Btn(name, parent, text, onClick, style: style, fontSize: fontSize, fixedSize: size);
            if (anchor.HasValue) { var rt = (RectTransform)b.transform; rt.anchorMin = rt.anchorMax = anchor.Value; }
            return b;
        }

        /// <summary>켜짐(앰버) / 보통. 비활성 버튼은 흐리게(UiButton이 interactable을 읽는다).</summary>
        public static void SetButtonLook(Button b, bool on) => SetButtonStyle(b, on ? BtnStyle.On : BtnStyle.Normal);

        public static void SetButtonStyle(Button b, BtnStyle style)
        {
            var ui = b.GetComponent<UiButton>();
            if (ui != null) ui.SetStyle(style);
        }

        /// <summary>버튼 라벨 글자를 바꾼다(숫자면 Sora).</summary>
        public static void SetButtonText(Button b, string s, bool num = false, bool bold = false)
        {
            var ui = b.GetComponent<UiButton>();
            var t = ui != null && ui.Label != null ? ui.Label : b.GetComponentInChildren<Text>();
            if (num) SetNumText(t, s, bold);
            else if (t.text != s) t.text = s;
        }

        /// <summary>알약(높이의 절반 반경) 패널.</summary>
        public static Image Pill(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, float height, float width, Color? fill = null, Color? line = null, Elevation elevation = Elevation.Panel)
        {
            int r = Mathf.RoundToInt(height * 0.5f);
            return PanelBox(name, parent, anchor, anchor, pivot, pos, new Vector2(width, height), fill ?? Panel, r, line ?? Line, elevation);
        }

        /// <summary>시안 .chip: 높이 28 알약, 좌우 10, 점(7) + 라벨(muted 12) + 값(Sora SemiBold 13). 값 Text를 돌려준다.</summary>
        public static Text Chip(string name, Transform parent, Color dot, string label, string value)
        {
            var img = PanelBox(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(80f, 28f), Panel, 14, Line, Elevation.None);
            var rt = img.rectTransform;
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.padding = new RectOffset(10, 10, 0, 0); g.spacing = 6f; g.childAlignment = TextAnchor.MiddleLeft;
            g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            Size(img, -1f, 28f);
            var d = Box("Dot", rt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(7f, 7f), dot, 4);
            Size(d, 7f, 7f);
            var l = Label("Label", rt, label, 12, Muted, TextAnchor.MiddleLeft); l.horizontalOverflow = HorizontalWrapMode.Overflow;
            var v = Label("Value", rt, value, 13, Fg, TextAnchor.MiddleLeft, Fonts.Num); v.horizontalOverflow = HorizontalWrapMode.Overflow;
            return v;
        }

        /// <summary>색 타일(반경 7) 안의 흰 아이콘(시안 .slot .ico).</summary>
        public static Image IconTile(string name, Transform parent, string icon, Color color, float size = 26f, float iconSize = 16f)
        {
            var tile = Box(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), color, RTile);
            var irt = Rect("Icon", tile.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
            var img = irt.gameObject.AddComponent<Image>(); img.sprite = Icon(icon); img.preserveAspect = true; img.raycastTarget = false;
            return tile;
        }

        /// <summary>배지(반경 8) + 굵은 글자.</summary>
        public static Text Badge(string name, Transform parent, string text, Color bg, Color fg, float height = 28f, int fontSize = 13)
        {
            var img = Box(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, height), bg, RBadge);
            AddShadow(img.rectTransform, RBadge, Elevation.Panel);
            var t = Label("Text", img.transform, text, fontSize, fg, TextAnchor.MiddleCenter, Fonts.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        /// <summary>모달 뒤 막(시안 .modal, rgba(5,8,10,.45)). 화면 전체라 모서리가 없다. 클릭을 막는다.</summary>
        public static Image ModalBackdrop(string name, Transform parent)
        {
            var rt = Fill(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Backdrop; img.raycastTarget = true;
            return img;
        }

        /// <summary>시안 .card: panel-2, 반경 14, line-2 선, 큰 그림자. 제목(Noto Bold 15)을 "Title"로 붙인다.</summary>
        public static RectTransform Card(string name, Transform parent, Vector2 size, string title)
        {
            var card = PanelBox(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, Panel2, RCard, Line2, Elevation.Card).rectTransform;
            if (title != null)
            {
                var t = Label("Title", card, title, 15, Fg, TextAnchor.UpperLeft, Fonts.Bold);
                t.rectTransform.offsetMin = new Vector2(18f, 0f); t.rectTransform.offsetMax = new Vector2(-18f, -18f);
            }
            return card;
        }

        /// <summary>자식 하나를 부모 위쪽(top px 아래)에 높이 h, 좌우 여백 pad로 놓는다.</summary>
        public static void Top(RectTransform rt, float top, float h, float padL, float padR)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(padL, -top - h); rt.offsetMax = new Vector2(-padR, -top);
        }
    }
}
