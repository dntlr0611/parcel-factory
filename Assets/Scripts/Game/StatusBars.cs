using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 미니 상태바(스펙 11.8, 12.3): 설치된 부품마다 작은 반투명 알약(Noto Bold 11, 1px 선)을 HUD 캔버스에 띄운다.
    /// 위치는 매 프레임 PackageViews.TopHeight(칸) + 0.45, 카메라 반대쪽(+z)으로 0.35를 화면에 투영한다(금액 팝업과 같은 방식).
    /// 부품 집합은 CellChanged 때 다시 모으고, 문구는 매 프레임 계산하되 바뀌었을 때만 대입한다.
    /// 작업자가 바라보는 부품은 진한 알약 + 앰버 선·글자·빛에 " · R 설정"이 붙는다.
    /// </summary>
    public sealed class StatusBars : MonoBehaviour
    {
        public const int FontSize = 11;
        /// <summary>칸 윗면 위로 Lift, 카메라 반대쪽(+z, 북)으로 AwayFromCamera만큼 옮겨 띄운다. 화면에서는 칸의 먼 가장자리 위에 놓여 분배기 허브의 화살표를 가리지 않는다.</summary>
        public const float Lift = 0.45f, AwayFromCamera = 0.35f, Height = 18f, PadX = 16f;
        public const string FocusSuffix = " · R 설정";
        private const string DimOpen = "<color=#8E959C>", DimClose = "</color>";
        /// <summary>항공 모드 출구 색(스펙 14.5): 1번 일반 흰색, 2번 항공 #2F86EB.</summary>
        private const string GroundOpen = "<color=#FFFFFF>", GroundClose = "</color>", AirOpen = "<color=#2F86EB>", AirClose = "</color>";
        /// <summary>분배기 항공 모드 출구 색: 1번 정상 초록(#36B565), 2번 반품 노랑(#F2C230) — 항공 트럭 색.</summary>
        private const string OkOpen = "<color=#36B565>", OkClose = "</color>", ReturnOpen = "<color=#F2C230>", ReturnClose = "</color>";
        // 시안 .sbar: rgba(10,13,15,.62) + 흰색 .1 선, 글자 #e9ece6. 바라보는 대상(.sbar.on)은 .94 + 앰버 선·글자 + 앰버 빛.
        // 색 공간 보정(UiKit.DarkAlpha)은 QualitySettings를 읽으므로 MonoBehaviour 생성 중(정적 필드 초기화)에는 부를 수 없다 → 속성으로 둔다.
        private static Color PillBg => new Color(10f / 255f, 13f / 255f, 15f / 255f, UiKit.DarkAlpha(0.62f));
        private static Color PillFocusBg => new Color(10f / 255f, 13f / 255f, 15f / 255f, UiKit.DarkAlpha(0.94f));
        private static Color PillLine => UiKit.White(0.1f);
        private static readonly Color PillFg = new Color(0xe9 / 255f, 0xec / 255f, 0xe6 / 255f);

        private sealed class Bar
        {
            public RectTransform Rect;
            public Image Bg, Border;
            public GameObject Glow;
            public Text Label;
            public int X, Y;
            public bool Focused;
        }

        private readonly List<Bar> active = new List<Bar>();
        private readonly Stack<Bar> pool = new Stack<Bar>();
        private RectTransform layer;
        private GameSession bound;
        private bool dirty = true;

        /// <summary>지금 보이는 상태바 수(= 설치된 부품 수).</summary>
        public int Count => active.Count;

        /// <summary>칸 (x, y) 상태바의 문구(리치 텍스트 태그 포함). 없으면 null.</summary>
        public string TextAt(int x, int y)
        {
            foreach (var b in active) if (b.X == x && b.Y == y) return b.Label.text;
            return null;
        }

        /// <summary>칸 (x, y) 상태바가 강조(바라보는 부품) 상태인가.</summary>
        public bool IsFocused(int x, int y)
        {
            foreach (var b in active) if (b.X == x && b.Y == y) return b.Focused;
            return false;
        }

        private void OnDestroy() => Bind(null);

        private void Bind(GameSession s)
        {
            if (bound == s) return;
            if (bound != null) bound.Build.CellChanged -= OnCellChanged;
            bound = s;
            if (bound != null) bound.Build.CellChanged += OnCellChanged;
            dirty = true;
        }

        private void OnCellChanged(int x, int y) => dirty = true;

        private void LateUpdate() => Refresh();

        /// <summary>집합·문구·위치를 지금 상태로 맞춘다. 스크린샷 캡처처럼 캔버스 모드를 바꾼 직후에도 부를 수 있다.</summary>
        public void Refresh()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null || boot.Hud == null || boot.Hud.Root == null) return;
            if (layer == null)
            {
                layer = UiKit.Rect("StatusBars", boot.Hud.Root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                layer.SetAsFirstSibling(); // 다른 HUD 요소(프롬프트·패널) 아래에 그린다
            }
            Bind(boot.Session);
            if (dirty) Collect();
            var (fx, fy) = bound.Worker.FacingTile();
            foreach (var b in active)
            {
                var c = bound.Grid.Get(b.X, b.Y);
                bool focus = b.X == fx && b.Y == fy;
                var text = Describe(bound, b.X, b.Y) + (focus ? FocusSuffix : "");
                if (focus != b.Focused || b.Label.text != text)
                {
                    b.Focused = focus;
                    b.Label.text = text;
                    b.Label.color = focus ? UiKit.Accent : PillFg;
                    b.Bg.color = focus ? PillFocusBg : PillBg;
                    b.Border.color = focus ? UiKit.Accent : PillLine;
                    if (b.Glow.activeSelf != focus) b.Glow.SetActive(focus);
                    b.Rect.sizeDelta = new Vector2(b.Label.preferredWidth + PadX, Height);
                    if (focus) b.Rect.SetAsLastSibling(); // 겹치면 바라보는 것이 위
                }
                if (boot.Hud.TryWorldToCanvas(Grid3D.Tile(b.X, b.Y) + new Vector3(0f, PackageViews.TopHeight(c) + Lift, AwayFromCamera), out var pos))
                    b.Rect.anchoredPosition = pos;
            }
        }

        /// <summary>설치된 부품 칸마다 상태바 하나. 남는 것은 풀로 돌린다.</summary>
        private void Collect()
        {
            dirty = false;
            foreach (var b in active) { b.Rect.gameObject.SetActive(false); pool.Push(b); }
            active.Clear();
            for (int y = 0; y < FactoryGrid.Height; y++)
                for (int x = 0; x < FactoryGrid.Width; x++)
                {
                    if (!bound.Grid.Get(x, y).IsBuilding) continue;
                    var b = pool.Count > 0 ? pool.Pop() : Make();
                    b.X = x; b.Y = y;
                    b.Label.text = null; // 다음 Refresh에서 문구·크기를 다시 정한다
                    b.Rect.gameObject.SetActive(true);
                    active.Add(b);
                }
        }

        private Bar Make()
        {
            const int r = (int)(Height / 2f);
            var bg = UiKit.Box("StatusBar", layer, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, Height), PillBg, r);
            var glow = UiKit.AddGlow(bg.rectTransform, r, 0, 12, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.35f));
            glow.gameObject.SetActive(false);
            var border = UiKit.AddBorder(bg.rectTransform, r, PillLine);
            var label = UiKit.Label("Text", bg.transform, "", FontSize, PillFg, TextAnchor.MiddleCenter, Fonts.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.supportRichText = true;
            return new Bar { Rect = bg.rectTransform, Bg = bg, Border = border, Glow = glow.gameObject, Label = label };
        }

        /// <summary>
        /// 부품 상태 문구: 레일 "→", 고속 "고속 →", 브릿지 "브릿지 ↑"(윗길 방향), 검사대 "검사대" 또는 "검사 대기 1", 자동 검사기 "자동 →",
        /// 분배기 "파 → · 빨 ↓"(빨간 출구가 없거나 연결되지 않았으면 흐린 글자), 팔레트 "파랑 3/6".
        /// 항공 모드(스펙 14.5): 분배기 "분배기 · 항공 1→ · 2↓"(1번 초록, 2번 노랑), 검사기 "검사기 · 항공 1→ · 2↓"(1번 흰색, 2번 #2F86EB).
        /// </summary>
        public static string Describe(GameSession s, int x, int y)
        {
            var c = s.Grid.Get(x, y);
            switch (c.Type)
            {
                case CellType.Conveyor: return HudController.Arrow(c.Direction);
                case CellType.FastConveyor: return "고속 " + HudController.Arrow(c.Direction);
                case CellType.Scanner:
                {
                    if (c.Mode != RouteMode.Air) return "자동 " + HudController.Arrow(c.Direction);
                    ConveyorSim.ScannerRoutes(s.Grid, x, y, out var ground, out var air);
                    // 받을 수 없는 출구, 그리고 붙잡힌 택배가 기다리는 출구는 흐리게(스펙 14.5).
                    var outs = ConveyorSim.SplitterOutputs(s.Grid, x, y, null);
                    var held = s.Sim.IsHolding(x, y) ? s.Sim.PackageAt(x, y) : null;
                    bool groundOk = outs.Contains(ground) && !(held != null && held.Heading == ground);
                    bool airOk = outs.Contains(air) && !(held != null && held.Heading == air);
                    return "검사기 · 항공 " + (groundOk ? GroundOpen : DimOpen) + "1" + HudController.Arrow(ground) + GroundClose + " · "
                        + (airOk ? AirOpen : DimOpen) + "2" + HudController.Arrow(air) + AirClose;
                }
                case CellType.Bridge: return "브릿지 " + HudController.Arrow(c.Direction); // 윗길 방향
                case CellType.Labeler:
                {
                    // 운송장 부착대(스펙 15.1·15.3): "부착대", 택배가 있으면 운송장 상태를 붙인다.
                    var p = s.Sim.PackageAt(x, y);
                    return p == null ? "부착대" : "부착대 · " + (p.Waybill ? "운송장 ✓" : "운송장 없음");
                }
                case CellType.Table:
                {
                    var p = s.Sim.PackageAt(x, y);
                    int n = p != null && s.Sim.IsAwaitingInspection(p) ? 1 : 0;
                    return n > 0 ? "검사 대기 " + n : "검사대";
                }
                case CellType.Splitter:
                {
                    ConveyorSim.SplitterRoutes(s.Grid, x, y, null, out var blue, out var red, out _, out var redConnected);
                    if (c.Mode == RouteMode.Air) // 정상(초록) · 반품(노랑), 반품 출구가 없거나 끊기면 흐리게
                        return "분배기 · 항공 " + OkOpen + "1" + HudController.Arrow(blue) + OkClose + " · "
                            + (!red.HasValue ? DimOpen + "2 –" + DimClose
                               : redConnected ? ReturnOpen + "2" + HudController.Arrow(red.Value) + ReturnClose
                               : DimOpen + "2" + HudController.Arrow(red.Value) + DimClose);
                    string redText = !red.HasValue ? DimOpen + "빨 –" + DimClose
                        : redConnected ? "빨 " + HudController.Arrow(red.Value)
                        : DimOpen + "빨 " + HudController.Arrow(red.Value) + DimClose;
                    return "파 " + HudController.Arrow(blue) + " · " + redText;
                }
                case CellType.Pallet: return HudController.KindName(c.PalletKind) + " " + c.Pallet.Count + "/" + Cell.PalletCapacity;
                default: return "";
            }
        }
    }
}
