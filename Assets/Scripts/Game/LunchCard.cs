using System;
using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>점심 카드의 항목 하나(메뉴 또는 계약) 클릭.</summary>
    public sealed class LunchRowClick : MonoBehaviour, IPointerClickHandler
    {
        public LunchCard Card;
        public int Column, Row;
        public void OnPointerClick(PointerEventData e) { if (Card != null) Card.Click(Column, Row); }
    }

    /// <summary>
    /// 점심 카드(스펙 21.1): §12 카드(폭 640) 두 열 — 왼쪽 `구내식당`(메뉴 4개 중 하나), 오른쪽 `오후 계약`(후보 3개 중 하나).
    /// 고른 항목은 앰버 테두리 + 옅은 앰버 채움 + 체크 원, 키보드 커서는 흰 바깥 링. 돈이 모자란 메뉴는 흐리고 가격이 빨갛다.
    /// 아래 `나중에`(Esc, 닫기) · `확정`(주 버튼, Enter). 하나만 골라도 확정할 수 있다.
    /// 키보드: ←/→ 열, ↑/↓ 항목(맨 아래에서 ↓는 버튼 줄), Enter 선택(이미 고른 항목은 풀기)/확정, Esc 나중에(Panels.CloseAll).
    /// 게임패드: 십자키 = 화살표, A = Enter, B = Esc(InputController의 Escape).
    /// </summary>
    public sealed class LunchCard : MonoBehaviour
    {
        public const float CardW = 640f, Pad = 18f, ColGap = 16f, MenuRowH = 52f, ContractRowH = 72f, RowGap = 8f, HeaderTop = 70f, ListTop = 102f;
        public static readonly float ColW = (CardW - 2f * Pad - ColGap) / 2f;
        public static readonly float ListH = 4f * MenuRowH + 3f * RowGap;
        public static readonly float CardH = ListTop + ListH + 14f + 34f + 16f;
        /// <summary>커서가 있는 곳: 메뉴 열, 계약 열, 아래 버튼 줄(0 = 나중에, 1 = 확정).</summary>
        public enum Area { Menu, Contract, Buttons }
        public const string Title = "점심시간", HintText = "←/→ 열 · ↑/↓ 항목 · Enter 선택 · Esc 나중에";
        public const string NothingChosenToast = "메뉴나 계약을 고르세요", LunchOverToast = "점심시간이 끝났습니다",
            AlreadyConfirmedToast = "오늘 점심은 이미 정했습니다", InvalidContractToast = "계약을 다시 고르세요";

        public RectTransform Root { get; private set; }
        public RectTransform Card { get; private set; }
        public bool IsOpen => Root != null && Root.gameObject.activeSelf;
        /// <summary>고른 메뉴(없으면 null)와 계약 후보 번호(없으면 null).</summary>
        public LunchMenu? SelectedMenu { get; private set; }
        public int? SelectedContract { get; private set; }
        public Area FocusArea { get; private set; }
        public int FocusIndex { get; private set; }
        public Button ConfirmButton { get; private set; }
        public Button LaterButton { get; private set; }
        public event Action Closed;

        private sealed class Row
        {
            public RectTransform Rect;
            public Image Fill, Border, Ring, Radio, Check;
            public Text Name, Sub, Value;
            public CanvasGroup Group;
            public bool Enabled = true;
        }

        private readonly List<Row> menuRows = new List<Row>(), contractRows = new List<Row>();
        private Text deadline;
        private int openFrame = -1, shownMoney = int.MinValue;
        private Area lastColumn = Area.Menu;

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        // MonoBehaviour의 정적 필드 초기화에서 색 공간을 읽을 수 없으므로(UiKit 색) 속성으로 둔다.
        private static Color SelectedFill => new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, UiKit.LightAlpha(0.18f));
        private static Color FocusRing => UiKit.White(0.6f);

        private GameSession S => GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;

        public void Build(RectTransform hudRoot)
        {
            Root = UiKit.ModalBackdrop("Lunch", hudRoot).rectTransform;
            Card = UiKit.Card("Card", Root, new Vector2(CardW, CardH), Title);
            deadline = UiKit.Label("Deadline", Card, "", 12, UiKit.Muted, TextAnchor.UpperRight);
            UiKit.Top(deadline.rectTransform, Pad + 2f, 18f, Pad, Pad);
            var sub = UiKit.Label("Subtitle", Card, "메뉴로 오후 버프를, 계약으로 보너스를 받습니다. 하나만 골라도 됩니다.", 12, UiKit.Muted, TextAnchor.UpperLeft);
            UiKit.Top(sub.rectTransform, Pad + 25f, 18f, Pad, Pad);

            Header("MenuHeader", 0, UiKit.LunchIconName, "구내식당", "하나 · 18:00까지");
            Header("ContractHeader", 1, UiKit.ContractIconName, "오후 계약", "셋 중 하나 · 무료");

            for (int i = 0; i < LunchMenus.All.Length; i++) menuRows.Add(MakeRow("Menu" + i, 0, i, MenuRowH));
            for (int i = 0; i < 3; i++) contractRows.Add(MakeRow("Contract" + i, 1, i, ContractRowH));

            var hint = UiKit.Label("Hint", Card, HintText, 11, UiKit.Muted, TextAnchor.MiddleLeft);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f); hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 16f); hint.rectTransform.sizeDelta = new Vector2(-2f * Pad, 34f);
            var buttons = UiKit.Rect("Buttons", Card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-2f * Pad, 34f));
            UiKit.Row(buttons, 8f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            LaterButton = UiKit.Btn("Later", buttons, "나중에", () => GameBootstrap.Instance.Panels.CloseAll(), key: "Esc", minWidth: 84f);
            ConfirmButton = UiKit.Btn("Confirm", buttons, "확정", Confirm, key: "Enter", style: BtnStyle.Primary, minWidth: 96f);
            AddFocusRing((RectTransform)LaterButton.transform, UiKit.RButton);
            AddFocusRing((RectTransform)ConfirmButton.transform, UiKit.RButton);
            Root.gameObject.SetActive(false);
        }

        private static Image AddFocusRing(RectTransform host, int radius)
        {
            var ring = UiKit.AddBorder(host, radius, FocusRing, 1.5f, 3f, "Focus");
            ring.gameObject.SetActive(false);
            return ring;
        }

        private static float ColX(int col) => Pad + col * (ColW + ColGap);

        private void Header(string name, int col, string icon, string title, string note)
        {
            var h = UiKit.Rect(name, Card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ColX(col), -HeaderTop), new Vector2(ColW, 24f));
            var tile = UiKit.IconTile("Tile", h, icon, col == 0 ? UiKit.Accent : new Color32(0x5f, 0x8f, 0x8a, 0xff), 24f, 16f);
            if (col == 0) tile.transform.Find("Icon").GetComponent<Image>().color = UiKit.AccentFg;
            UiKit.Place(tile.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            var t = UiKit.Label("Title", h, title, 14, UiKit.Fg, TextAnchor.MiddleLeft, Fonts.Bold);
            t.rectTransform.offsetMin = new Vector2(32f, 0f);
            var n = UiKit.Label("Note", h, note, 11, UiKit.Muted, TextAnchor.MiddleRight);
            n.rectTransform.offsetMax = new Vector2(-2f, 0f);
        }

        private Row MakeRow(string name, int col, int index, float h)
        {
            float y = ListTop + index * (h + RowGap);
            var fill = UiKit.Box(name, Card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ColX(col), -y), new Vector2(ColW, h), UiKit.RowBg, UiKit.RButton);
            fill.raycastTarget = true;
            var rt = fill.rectTransform;
            var r = new Row { Rect = rt, Fill = fill, Group = rt.gameObject.AddComponent<CanvasGroup>() };
            r.Border = UiKit.AddBorder(rt, UiKit.RButton, UiKit.Line);
            r.Ring = AddFocusRing(rt, UiKit.RButton);
            var click = rt.gameObject.AddComponent<LunchRowClick>();
            click.Card = this; click.Column = col; click.Row = index;
            // 오른쪽 체크 원(지름 18): 비었으면 line-2 고리, 고르면 앰버 원 + 어두운 체크.
            var radioRt = UiKit.Rect("Radio", rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(18f, 18f));
            r.Radio = radioRt.gameObject.AddComponent<Image>();
            r.Radio.sprite = UiSprites.Ring(9, 1.5f); r.Radio.type = Image.Type.Sliced; r.Radio.color = UiKit.Line2; r.Radio.raycastTarget = false;
            var checkRt = UiKit.Rect("Check", radioRt, Center, Center, Center, Vector2.zero, new Vector2(12f, 12f));
            r.Check = checkRt.gameObject.AddComponent<Image>();
            r.Check.sprite = UiKit.Icon("ui-check"); r.Check.preserveAspect = true; r.Check.color = UiKit.AccentFg; r.Check.raycastTarget = false;
            if (col == 0)
            {
                r.Name = UiKit.Label("Name", rt, "", 14, UiKit.Fg, TextAnchor.UpperLeft, Fonts.Bold);
                UiKit.Top(r.Name.rectTransform, 7f, 20f, 14f, 110f);
                r.Sub = UiKit.Label("Effect", rt, "", 12, UiKit.Muted, TextAnchor.UpperLeft);
                UiKit.Top(r.Sub.rectTransform, 28f, 18f, 14f, 110f);
                r.Value = UiKit.Label("Price", rt, "", 14, UiKit.Accent, TextAnchor.MiddleRight, Fonts.Bold);
                r.Value.rectTransform.offsetMax = new Vector2(-40f, 0f);
            }
            else
            {
                r.Name = UiKit.Label("Name", rt, "", 14, UiKit.Fg, TextAnchor.UpperLeft, Fonts.Bold);
                UiKit.Top(r.Name.rectTransform, 9f, 20f, 14f, 40f);
                r.Sub = UiKit.Label("Condition", rt, "", 12, UiKit.Muted, TextAnchor.UpperLeft);
                UiKit.Top(r.Sub.rectTransform, 30f, 18f, 14f, 40f);
                r.Value = UiKit.Label("Reward", rt, "", 12, UiKit.Good, TextAnchor.UpperLeft, Fonts.Bold);
                UiKit.Top(r.Value.rectTransform, 49f, 16f, 14f, 40f);
            }
            return r;
        }

        /// <summary>카드를 연다: 선택을 비우고 커서는 첫 메뉴. 후보가 아직 없으면(일시정지 등) 지금 뽑는다.</summary>
        public void Open()
        {
            var s = S;
            if (Root == null || s == null || !s.Company.LunchCardAvailable) return;
            s.Company.EnsureLunchCandidates();
            SelectedMenu = null;
            SelectedContract = null;
            FocusArea = Area.Menu;
            FocusIndex = 0;
            lastColumn = Area.Menu;
            shownMoney = int.MinValue;
            shownMinutesLeft = -1;
            FillContracts(s);
            Refresh();
            openFrame = Time.frameCount;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
        }

        /// <summary>닫는다(나중에·Esc·확정 뒤·13:00). 고른 것은 버린다.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            Root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void FillContracts(GameSession s)
        {
            var list = s.Company.ContractCandidates;
            for (int i = 0; i < contractRows.Count; i++)
            {
                var r = contractRows[i];
                bool has = i < list.Count;
                r.Rect.gameObject.SetActive(has);
                if (!has) continue;
                var c = list[i];
                r.Name.text = c.Title;
                r.Sub.text = ContractCatalog.Condition(c.Kind);
                r.Value.text = "+₩" + c.Reward + " · EXP +" + Contract.RewardExp;
            }
        }

        /// <summary>메뉴 행(가격·흐림)과 선택·커서 모양, 확정 버튼을 다시 칠한다.</summary>
        private void Refresh()
        {
            var s = S;
            if (s == null) return;
            shownMoney = s.Economy.Money;
            for (int i = 0; i < menuRows.Count; i++)
            {
                var m = LunchMenus.All[i];
                var r = menuRows[i];
                int price = LunchMenus.Price(m);
                r.Enabled = price == 0 || s.Economy.CanAfford(price); // 무료는 잔액이 음수여도(스펙 21.5)
                r.Name.text = LunchMenus.Name(m);
                r.Sub.text = LunchMenus.Effect(m);
                UiKit.SetNumText(r.Value, price > 0 ? "₩" + price : "무료", true);
                r.Value.font = Fonts.Bold;
                r.Value.color = !r.Enabled ? UiKit.Bad : price > 0 ? UiKit.Accent : UiKit.Good;
                r.Group.alpha = r.Enabled ? 1f : 0.45f;
                if (!r.Enabled && SelectedMenu == m) SelectedMenu = null; // 돈이 줄어 못 사게 됐다
                Paint(r, SelectedMenu == m, FocusArea == Area.Menu && FocusIndex == i);
            }
            for (int i = 0; i < contractRows.Count; i++)
                Paint(contractRows[i], SelectedContract == i, FocusArea == Area.Contract && FocusIndex == i);
            bool any = SelectedMenu.HasValue || SelectedContract.HasValue;
            ConfirmButton.interactable = any;
            int cost = SelectedMenu.HasValue ? LunchMenus.Price(SelectedMenu.Value) : 0;
            UiKit.SetButtonText(ConfirmButton, cost > 0 ? "확정 · ₩" + cost : "확정");
            UiKit.SetButtonStyle(ConfirmButton, any ? BtnStyle.Primary : BtnStyle.Normal);
            LaterButton.transform.Find("Focus").gameObject.SetActive(FocusArea == Area.Buttons && FocusIndex == 0);
            ConfirmButton.transform.Find("Focus").gameObject.SetActive(FocusArea == Area.Buttons && FocusIndex == 1);
        }

        private static void Paint(Row r, bool selected, bool focused)
        {
            r.Fill.color = selected ? SelectedFill : UiKit.RowBg;
            r.Border.sprite = UiSprites.Ring(UiKit.RButton, selected ? 2f : 1f);
            r.Border.color = selected ? UiKit.Accent : UiKit.Line;
            r.Ring.gameObject.SetActive(focused);
            r.Radio.sprite = selected ? UiSprites.Rounded(9) : UiSprites.Ring(9, 1.5f);
            r.Radio.color = selected ? UiKit.Accent : UiKit.Line2;
            r.Check.gameObject.SetActive(selected);
        }

        /// <summary>마우스·터치로 항목을 누른다: 그 항목을 고르고(이미 고른 것이면 풀고) 커서를 옮긴다.</summary>
        public void Click(int column, int row)
        {
            if (!IsOpen) return;
            FocusArea = column == 0 ? Area.Menu : Area.Contract;
            FocusIndex = row;
            lastColumn = FocusArea;
            Toggle(FocusArea, row);
            Refresh();
        }

        /// <summary>항목 고르기/풀기. 돈이 모자란 메뉴·없는 후보는 무시한다. 골랐으면 true.</summary>
        private bool Toggle(Area area, int row)
        {
            if (area == Area.Menu)
            {
                if (row < 0 || row >= menuRows.Count || !menuRows[row].Enabled) return false;
                var m = LunchMenus.All[row];
                SelectedMenu = SelectedMenu == m ? (LunchMenu?)null : m;
                return SelectedMenu.HasValue;
            }
            var s = S;
            if (s == null || row < 0 || row >= Mathf.Min(contractRows.Count, s.Company.ContractCandidates.Count)) return false;
            SelectedContract = SelectedContract == row ? (int?)null : row;
            return SelectedContract.HasValue;
        }

        /// <summary>←/→: 메뉴 ↔ 계약 열(버튼 줄이면 나중에 ↔ 확정).</summary>
        public void MoveColumn(int d)
        {
            if (FocusArea == Area.Buttons) FocusIndex = Mathf.Clamp(FocusIndex + d, 0, 1);
            else
            {
                FocusArea = d > 0 ? Area.Contract : Area.Menu;
                FocusIndex = Mathf.Clamp(FocusIndex, 0, RowCount(FocusArea) - 1);
                lastColumn = FocusArea;
            }
            Refresh();
        }

        /// <summary>↑/↓: 열 안에서 항목을 옮긴다. 맨 아래에서 ↓는 버튼 줄(확정), 버튼 줄에서 ↑는 마지막 열의 맨 아래 항목.</summary>
        public void MoveRow(int d)
        {
            if (FocusArea == Area.Buttons)
            {
                if (d < 0) { FocusArea = lastColumn; FocusIndex = RowCount(lastColumn) - 1; }
            }
            else
            {
                int n = RowCount(FocusArea);
                if (d > 0 && FocusIndex >= n - 1) { lastColumn = FocusArea; FocusArea = Area.Buttons; FocusIndex = 1; }
                else FocusIndex = Mathf.Clamp(FocusIndex + d, 0, n - 1);
            }
            Refresh();
        }

        private int RowCount(Area a) => a == Area.Menu ? menuRows.Count : Mathf.Clamp(S != null ? S.Company.ContractCandidates.Count : 0, 1, contractRows.Count);

        /// <summary>Enter·게임패드 A: 항목이면 고르고(고르면 메뉴 → 계약 열 → 확정 버튼으로 커서가 넘어간다), 버튼이면 누른다.</summary>
        public void Submit()
        {
            if (!IsOpen) return;
            if (FocusArea == Area.Buttons)
            {
                if (FocusIndex == 0) LaterButton.onClick.Invoke();
                else Confirm();
                return;
            }
            if (Toggle(FocusArea, FocusIndex))
            {
                if (FocusArea == Area.Menu)
                {
                    FocusArea = Area.Contract;
                    FocusIndex = SelectedContract ?? 0;
                    lastColumn = Area.Contract;
                }
                else { lastColumn = Area.Contract; FocusArea = Area.Buttons; FocusIndex = 1; }
            }
            Refresh();
        }

        /// <summary>확정(스펙 21.1): 메뉴 값을 내고 효과·계약을 켠 뒤 닫는다. 아무것도 안 골랐으면 토스트만.</summary>
        public void Confirm()
        {
            var s = S;
            if (!IsOpen || s == null) return;
            var hud = GameBootstrap.Instance.Hud;
            var r = s.ConfirmLunch(SelectedMenu, SelectedContract);
            switch (r)
            {
                case LunchConfirmResult.Ok:
                    Close();
                    if (hud != null) hud.Toast(ConfirmedToast(s.Company), HudController.ToastKind.Good);
                    break;
                case LunchConfirmResult.NothingChosen: if (hud != null) hud.Toast(NothingChosenToast); break;
                case LunchConfirmResult.NotEnoughMoney: if (hud != null) hud.Toast("잔액이 부족합니다", HudController.ToastKind.Bad); Refresh(); break;
                case LunchConfirmResult.AlreadyConfirmed: Close(); if (hud != null) hud.Toast(AlreadyConfirmedToast); break;
                case LunchConfirmResult.InvalidContract:
                    SelectedContract = null; // 후보가 바뀌었다: 다시 고르게 한다
                    FillContracts(s);
                    Refresh();
                    if (hud != null) hud.Toast(InvalidContractToast, HudController.ToastKind.Bad);
                    break;
                default: Close(); if (hud != null) hud.Toast(LunchOverToast); break; // NotLunch
            }
        }

        /// <summary>확정 토스트: "국밥 −₩40 · 계약: 항공 출고 10개".</summary>
        public static string ConfirmedToast(Company c)
        {
            var parts = new List<string>();
            if (c.Menu != LunchMenu.None) parts.Add(LunchMenus.Name(c.Menu) + (LunchMenus.Price(c.Menu) > 0 ? " −₩" + LunchMenus.Price(c.Menu) : ""));
            if (c.Contract != null) parts.Add("계약: " + c.Contract.Title);
            return string.Join(" · ", parts);
        }

        /// <summary>남은 시간 문구: "13:00 마감 · 42분 남음".</summary>
        public static string DeadlineText(Clock c) => "13:00 마감 · " + MinutesLeft(c) + "분 남음";

        private static int MinutesLeft(Clock c) => Mathf.Max(0, Mathf.CeilToInt(Clock.LunchEnd - c.MinuteOfDay));
        private int shownMinutesLeft = -1;

        private void Update()
        {
            if (!IsOpen) return;
            var s = S;
            if (s == null) return;
            int minutesLeft = MinutesLeft(s.Clock);
            if (minutesLeft != shownMinutesLeft) { shownMinutesLeft = minutesLeft; deadline.text = DeadlineText(s.Clock); } // 분이 바뀔 때만 문자열을 만든다
            if (s.Economy.Money != shownMoney) Refresh();
            if (Time.frameCount == openFrame) return; // 카드를 연 키(L·Enter)가 같은 프레임에 선택으로 읽히지 않게
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            bool submit = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (left) MoveColumn(-1);
            else if (right) MoveColumn(1);
            else if (up) MoveRow(-1);
            else if (down) MoveRow(1);
            else if (submit) Submit();
        }

        /// <summary>메뉴 행 i의 RectTransform(테스트·스크린샷용).</summary>
        public RectTransform MenuRow(int i) => menuRows[i].Rect;
        public RectTransform ContractRow(int i) => contractRows[i].Rect;
        /// <summary>메뉴 행 i를 고를 수 있는가(돈).</summary>
        public bool MenuEnabled(int i) => menuRows[i].Enabled;
    }
}
