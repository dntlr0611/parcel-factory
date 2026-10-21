using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>상단 바, 근무 시계, 핫바, 프롬프트, 작업자 상태·체력, 토스트, 조작 설명, 터치 컨트롤. 배치와 크기는 시안 CSS(스펙 12.3)를 1280×800 기준으로 옮긴 것이다.</summary>
    public sealed class HudController : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public bool TouchMode { get; private set; }
        public Font Font => UiKit.DefaultFont;

        private Text money, cur, moneyLbl, expText, delivered, lost, waiting, labelWaiting, workerText, promptText, toastText, helpText;
        private Button pauseBtn, speedBtn;
        private RectTransform topLeft, chips, hotbar, hotbarFrame, prompt, toast, help, touchPad, touchButtons, status, stamina, promptKey;
        private Image toastBar, promptBorder, promptTailLineImg;
        private readonly List<Button> slotButtons = new List<Button>();
        private readonly List<Text> slotArrows = new List<Text>();
        private readonly List<Text> slotPrices = new List<Text>();
        private readonly List<Text> slotKeys = new List<Text>();
        private readonly List<Text> slotNames = new List<Text>();
        private readonly List<Image> slotTiles = new List<Image>();
        private readonly List<Image> slotIcons = new List<Image>();
        private readonly List<Image> slotBorders = new List<Image>();
        private readonly List<GameObject> slotGlows = new List<GameObject>();
        private readonly List<GameObject> slotDirs = new List<GameObject>();
        private readonly List<UiHover> slotHovers = new List<UiHover>();
        private float toastUntil;
        private GameSession boundSession;
        private Canvas rootCanvas;
        private CanvasGroup rootGroup;
        private Image promptBg;
        // 잔액 깜빡임: 값이 바뀌면 0.4초 동안 초록(증가)/빨강(감소).
        public const float MoneyFlashSeconds = 0.4f;
        private int lastMoney;
        private GameSession moneySession;
        private float moneyFlashUntil;
        private Color moneyFlashColor;
        // EXP 깜빡임: Progression.ExpChanged마다 잔액과 같은 규칙(증가 초록, 감소 빨강).
        private int lastExp;
        private float expFlashUntil;
        private Color expFlashColor;
        // 월드 금액 팝업 풀. 칸 위에서 0.9초 동안 0.6 올라가며 사라진다.
        public const int PopupPoolSize = 8;
        public const float PopupSeconds = 0.9f, PopupRise = 0.6f, PopupBaseHeight = 0.8f;
        private readonly List<Text> popups = new List<Text>();
        private readonly List<Vector3> popupWorld = new List<Vector3>();
        private readonly List<float> popupAge = new List<float>();
        private readonly List<Color> popupColor = new List<Color>();
        // 해체 중인 칸 → 완료 시 환불액. 자세한 내용은 OnCellChanged 참고.
        private readonly Dictionary<(int x, int y), int> pendingRefunds = new Dictionary<(int x, int y), int>();
        /// <summary>마지막으로 띄운 토스트 문구(테스트용).</summary>
        public string LastToast { get; private set; }
        public enum ToastKind { Info, Good, Bad }
        private Text touchRotateLabel, bannerText;
        private RectTransform banner;
        public const string DirectionBannerText = "방향 입력: ← ↑ → ↓  (Esc 취소)", SlotBannerText = "1 파랑 · 2 빨강 선택  (Esc 취소)";
        /// <summary>분배기·검사기 칸 설정 배너(스펙 14.5: "3 모드" 추가). 항공 모드는 1 일반 · 2 항공.</summary>
        public const string SplitterSlotBannerText = "1 파랑 · 2 빨강 · 3 모드  (Esc 취소)", AirSplitterSlotBannerText = "1 초록 · 2 노랑 · 3 모드  (Esc 취소)",
            AirSlotBannerText = "1 일반 · 2 항공 · 3 모드  (Esc 취소)", ScannerDirectionBannerText = "방향 입력: ← ↑ → ↓ · 3 모드  (Esc 취소)";
        private InputController input;
        // 근무 시계(스펙 11.1): 상단 중앙 "1일차 09:00", 점심이면 옆에 노란 "점심시간", 퇴근이면 "퇴근" 배지.
        private Text clockText, badgeText;
        private Image badgeBg, staminaFill;
        public const string LunchBadgeText = "점심시간", ClosedBadgeText = "퇴근";
        // 체력 바(스펙 11.3): 상태줄 왼쪽 140×10. 50 초과 초록, 20 초과 노랑, 그 외 빨강. 20 아래면 깜빡인다.
        public const float StaminaBarWidth = 140f, StaminaBarHeight = 10f, StaminaPulseBelow = 20f;
        public const string ExhaustedToast = "지쳤습니다. 잠시 쉬세요", ExhaustedPrompt = "집기 (지침)";
        /// <summary>운송장 부착대(스펙 15.1): 미검사 택배를 올리려 할 때 토스트, 부착 대기 택배 앞 프롬프트.</summary>
        public const string NeedsInspectionMessage = "먼저 검사하세요", LabelPrompt = "운송장 붙이기", WaybillAttachedToast = "운송장 부착";
        // 시안 배치(1280×800 기준 px): 핫바 bottom 16, 슬롯 72, 상태줄 bottom 104(핫바 바로 위), 설정 배너 bottom 142. 터치 모드는 96 올린다.
        private const float SlotSize = 72f, HotbarPad = 8f, HotbarGap = 8f, HotbarY = 16f, StatusY = 106f, StatusH = 28f, BannerY = 144f, TouchLift = 96f;

        /// <summary>조작 설명(인게임 조작 서랍과 시작 화면 조작법 카드가 같이 쓴다). 키 이름은 밝은 글자로.</summary>
        public const string HelpText =
            "<color=#f4f3ee>WASD/방향키</color> 이동 · <color=#f4f3ee>E</color> 집기·놓기·설치·검사 · <color=#f4f3ee>X</color> 해체\n" +
            "<color=#f4f3ee>Space</color> 달리기(체력 소모)\n" +
            "<color=#f4f3ee>B</color> 상점 · <color=#f4f3ee>C</color> 상태창(EXP 강화) · <color=#f4f3ee>P</color> 일시정지 · <color=#f4f3ee>T</color> 배속\n" +
            "<color=#f4f3ee>1~8</color> 도구 선택 · <color=#f4f3ee>Q·Esc</color> 빈손\n" +
            "<color=#f4f3ee>R</color> 설정 → 화살표로 방향(분배기·팔레트는 R → 1 파랑/2 빨강). 설치된 부품을 바라보고 R이면 그 자리에서 바꿉니다\n" +
            "검사대 위 택배는 검사 전까지 멈춥니다. 검사한 택배는 부착대에서 E로 운송장을 끌어다 붙이세요(운송장 없이 출고 −20 · 반품 −10).\n" +
            "팔레트에서 집어 트럭으로: 정상은 출고, 불량은 반품.\n" +
            "입고 8초마다 1개(배송 5건마다 빨라져 최소 3초) · 정상 출고 +30, 불량 반품 +10 · 해체 2초 후 80% 환불\n" +
            "택배는 검사대(3)에서 E로 검사한 뒤 출고/반품하세요. 검사 없이 트럭에 넣으면 출고 −20 · 반품 −10(스펙 16.1).\n" +
            "분배기: 파란 화살표 = 승인 택배, 빨간 화살표 = 거부 택배\n" +
            "브릿지: 윗길은 설정 방향, 아랫길은 옆에서 들어온 택배가 직진 (레일 교차용)\n" +
            "부지 확장 2 이후: 파란 비행기 마크 = 항공 택배 → 항공 트럭(초록 출고 · 노랑 반품). 운송 수단이 틀리면 −10\n" +
            "분배기·검사기 <color=#f4f3ee>R → 3</color> 모드(검사/항공): 검사기 항공 모드는 흰 화살표 = 일반 · 파란 화살표 = 항공 택배, 분배기 항공 모드는 초록 = 정상 · 노랑 = 반품";

        private static readonly Dictionary<CellType, string> Names = new Dictionary<CellType, string>
        {
            { CellType.Conveyor, "레일" }, { CellType.FastConveyor, "고속 레일" }, { CellType.Bridge, "브릿지" }, { CellType.Pallet, "팔레트" }, { CellType.Table, "검사대" }, { CellType.Labeler, "부착대" }, { CellType.Splitter, "분배기" }, { CellType.Scanner, "자동 검사기" },
            { CellType.Inlet, "입고구" }, { CellType.Outlet, "출고구" }, { CellType.ReturnDock, "반품구" },
            { CellType.AirOutlet, "항공 출고" }, { CellType.AirReturnDock, "항공 반품" }, { CellType.Empty, "빈 칸" }, { CellType.Locked, "잠긴 칸" },
        };
        public static string Name(CellType t) => Names.TryGetValue(t, out var n) ? n : t.ToString();
        public static string Arrow(Direction d) => d == Direction.North ? "↑" : d == Direction.East ? "→" : d == Direction.South ? "↓" : "←";
        public static string KindName(PalletKind k) => k == PalletKind.Blue ? "파랑" : "빨강";
        /// <summary>도구의 방향 표기. 팔레트는 화살표 대신 종류(파랑/빨강).</summary>
        public static string DirLabel(CellType t, Direction d) => t == CellType.Pallet ? KindName(Tool.KindOf(d)) : Arrow(d);
        /// <summary>놓여 있는 칸의 방향 표기. 팔레트는 Cell.PalletKind에서 읽는다.</summary>
        public static string DirLabel(Cell c) => c.Type == CellType.Pallet ? KindName(c.PalletKind) : Arrow(c.Direction);

        /// <summary>핫바 아이콘 타일 색(시안 SW)과 아이콘 이름.</summary>
        public static Color ToolColor(CellType t)
        {
            switch (t)
            {
                case CellType.Conveyor: return new Color32(0x3f, 0xa3, 0xa3, 0xff);
                case CellType.FastConveyor: return new Color32(0xc2, 0x5a, 0x66, 0xff);
                case CellType.Bridge: return new Color32(0x8a, 0x9a, 0x5b, 0xff);
                case CellType.Pallet: return new Color32(0x9a, 0x6b, 0x40, 0xff);
                case CellType.Table: return new Color32(0xb0, 0x7a, 0x44, 0xff);
                case CellType.Labeler: return new Color32(0x5f, 0x8f, 0x8a, 0xff); // 운송장 부착대(스펙 15.1)
                case CellType.Splitter: return new Color32(0x7a, 0x62, 0xc0, 0xff);
                case CellType.Scanner: return new Color32(0x4e, 0x86, 0xe8, 0xff);
                default: return UiKit.Muted;
            }
        }

        public const string LabelerIconName = "tool-labeler";

        public static string ToolIcon(CellType t)
        {
            switch (t)
            {
                case CellType.Conveyor: return "tool-conveyor";
                case CellType.FastConveyor: return "tool-fast";
                case CellType.Bridge: return "tool-bridge";
                case CellType.Pallet: return "tool-pallet";
                case CellType.Table: return "tool-table";
                case CellType.Labeler: return LabelerIconName; // 아이콘 묶음에 없어 코드로 그린다(UiSprites.WaybillIcon)
                case CellType.Splitter: return "tool-splitter";
                case CellType.Scanner: return "tool-scanner";
                default: return "ui-parcel";
            }
        }

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input;
            TouchMode = Application.isMobilePlatform || UnityEngine.InputSystem.Touchscreen.current != null;
            var canvas = UiKit.MakeCanvas();
            canvas.transform.SetParent(transform, false);
            Root = canvas.GetComponent<RectTransform>();
            rootCanvas = canvas;
            rootGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            BuildTopLeft(); BuildTopRight(); BuildHotbar(); BuildPrompt(); BuildPopups(); BuildStatus(); BuildStamina(); BuildClock(); BuildToast(); BuildHelp(); BuildBanner();
            if (TouchMode) BuildTouchControls();
            input.ToolChanged += RefreshHotbar;
            input.ToolChanged += RefreshTouchLabels;
            input.ConfigChanged += RefreshTouchLabels;
            input.Interacted += OnInteracted;
            input.DemolishRequested += r => { if (r != BuildResult.Ok) Toast(DemolishMessage(r)); };
            input.TogglePause += () => { boot.Session.Paused = !boot.Session.Paused; };
            input.ToggleSpeed += () => { boot.Session.Speed = boot.Session.Speed >= 2f ? 1f : 2f; };
            input.ToggleHelp += () => SetHelp(!help.gameObject.activeSelf);
            input.Escape += () => { if (help.gameObject.activeSelf) SetHelp(false); };
            boot.SessionReplaced += s => { s.Unlocks.Unlocked += _ => RefreshHotbar(); BindSession(s); RefreshHotbar(); };
            boot.Session.Unlocks.Unlocked += _ => RefreshHotbar();
            BindSession(boot.Session);
            RefreshHotbar();
        }

        /// <summary>HUD 전체를 보이거나 숨긴다(시작 화면이 떠 있는 동안 숨김). 숨기면 클릭도 받지 않는다.</summary>
        public void SetVisible(bool visible)
        {
            if (rootGroup == null) return;
            rootGroup.alpha = visible ? 1f : 0f;
            rootGroup.blocksRaycasts = visible;
            rootGroup.interactable = visible;
        }

        /// <summary>HUD가 보이는가.</summary>
        public bool Visible => rootGroup == null || rootGroup.alpha > 0f;

        /// <summary>배송 토스트·금액 팝업을 받을 세션을 바꾼다. 이전 세션 구독은 끊고 남은 팝업은 숨긴다.</summary>
        private void BindSession(GameSession session)
        {
            if (boundSession == session) return;
            if (boundSession != null)
            {
                boundSession.Sim.PackageDelivered -= OnPackageDelivered;
                boundSession.Sim.ScannerBlocked -= OnScannerBlocked;
                boundSession.Build.CellChanged -= OnCellChanged;
                boundSession.Progression.ExpChanged -= OnExpChanged;
            }
            boundSession = session;
            pendingRefunds.Clear();
            for (int i = 0; i < popups.Count; i++) popups[i].gameObject.SetActive(false);
            expFlashUntil = 0f;
            if (boundSession == null) return;
            boundSession.Sim.PackageDelivered += OnPackageDelivered;
            boundSession.Sim.ScannerBlocked += OnScannerBlocked;
            boundSession.Build.CellChanged += OnCellChanged;
            boundSession.Progression.ExpChanged += OnExpChanged;
            lastExp = boundSession.Progression.Exp;
            if (expText != null) { expText.text = ExpLabel(lastExp); expText.color = UiKit.ExpColor; }
            // 불러온 저장에 해체 중인 칸이 있으면 완료 때 환불 팝업을 띄울 수 있게 미리 적어 둔다.
            for (int x = 0; x < FactoryGrid.Width; x++)
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = boundSession.Grid.Get(x, y);
                    if (c.Demolishing) pendingRefunds[(x, y)] = BuildService.RefundFor(c.Type);
                }
        }

        private void OnDestroy() => BindSession(null);

        /// <summary>좌상단 EXP 줄 문구: "EXP 10".</summary>
        public static string ExpLabel(int exp) => "EXP " + exp;

        private void OnExpChanged(int exp)
        {
            if (exp != lastExp)
            {
                expFlashColor = exp > lastExp ? UiKit.Good : UiKit.Bad;
                expFlashUntil = Time.unscaledTime + MoneyFlashSeconds;
                lastExp = exp;
            }
            if (expText != null) expText.text = ExpLabel(exp); // 색(깜빡임)은 Update에서
        }

        private void OnPackageDelivered(Package p, DeliveryOutcome outcome, int delta)
        {
            var msg = DeliveryMessage(outcome);
            if (p.Air && outcome != DeliveryOutcome.WrongCarrier) msg = AirTag + " " + msg;
            Toast(msg, DeliveryScoring.IsSuccess(outcome) ? ToastKind.Good : ToastKind.Bad);
            var (dx, dy) = DockTile(boundSession.Sim.LastDeliveredDock);
            SpawnMoneyPopup(dx, dy, delta);
        }

        /// <summary>항공 모드 검사기가 처음 막혔을 때(스펙 14.5).</summary>
        public const string ScannerBlockedToast = "검사기 출구가 막혔습니다";

        private void OnScannerBlocked(int x, int y) => Toast(ScannerBlockedToast, ToastKind.Bad);

        /// <summary>항공 택배 꼬리표(스펙 14.2).</summary>
        public const string AirTag = "항공";

        /// <summary>트럭 도크 칸 좌표(금액 팝업 위치). 도크가 아니면 출고 도크.</summary>
        public static (int x, int y) DockTile(CellType dock)
        {
            switch (dock)
            {
                case CellType.ReturnDock: return (FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY);
                case CellType.AirOutlet: return (FactoryGrid.AirOutletX, FactoryGrid.AirOutletY);
                case CellType.AirReturnDock: return (FactoryGrid.AirReturnDockX, FactoryGrid.AirReturnDockY);
                default: return (FactoryGrid.OutletX, FactoryGrid.OutletY);
            }
        }

        /// <summary>
        /// 환불 귀속: 코어에는 환불 이벤트가 없다. BuildService는 해체 시작(BeginDemolish) 때와 완료(Tick) 때 CellChanged를 낸다.
        /// 시작 때 칸이 Demolishing이면 (x,y)와 그 부품의 환불액(RefundFor)을 적어 두고, 같은 칸이 Demolishing이 아닌 빈 칸으로
        /// 다시 바뀌면 해체가 끝나 환불된 것이므로 그 칸에 +환불 팝업을 띄운다. 잔액 변화량을 추측하지 않으므로 같은 틱의 배송과 섞이지 않는다.
        /// </summary>
        private void OnCellChanged(int x, int y)
        {
            var c = boundSession.Grid.Get(x, y);
            if (c.Demolishing) { pendingRefunds[(x, y)] = BuildService.RefundFor(c.Type); return; }
            if (!pendingRefunds.TryGetValue((x, y), out var refund)) return;
            pendingRefunds.Remove((x, y));
            if (c.Type == CellType.Empty) SpawnMoneyPopup(x, y, refund);
        }

        /// <summary>금액 표기: +30, −10(유니코드 마이너스).</summary>
        public static string Signed(int delta) => delta >= 0 ? "+" + delta : "−" + (-delta);

        /// <summary>배송 결과 문구: 이유와 잔액 변화. 벌점이면 왜 깎였는지 알려 준다.</summary>
        public static string DeliveryMessage(DeliveryOutcome o)
        {
            switch (o)
            {
                case DeliveryOutcome.Delivered: return "정상 출고 +30";
                case DeliveryOutcome.ReturnedBad: return "불량 반품 +10";
                case DeliveryOutcome.UninspectedToOutlet: return "검사대를 거치지 않은 택배 · 출고 " + Signed(DeliveryScoring.Delta(o));
                case DeliveryOutcome.UninspectedToReturn: return "검사대를 거치지 않은 택배 · 반품 " + Signed(DeliveryScoring.Delta(o));
                case DeliveryOutcome.RejectedToOutlet: return "거부한 택배를 출고 −10";
                case DeliveryOutcome.BadToOutlet: return "불량을 승인해 출고 −30";
                case DeliveryOutcome.GoodToReturn: return "정상 택배를 반품 −5";
                case DeliveryOutcome.ApprovedToReturn: return "승인한 택배를 반품 −5";
                case DeliveryOutcome.WrongCarrier: return WrongCarrierLabel;
                case DeliveryOutcome.NoWaybillToOutlet: return "출고 " + Signed(DeliveryScoring.Delta(o)) + " · 운송장 없음";
                case DeliveryOutcome.NoWaybillToReturn: return "반품 " + Signed(DeliveryScoring.Delta(o)) + " · 운송장 없음";
                default: return o.ToString();
            }
        }

        private static Text TopLeftText(string name, Transform parent, string text, int size, Color color, Font font, float top, float h)
        {
            var t = UiKit.Label(name, parent, text, size, color, TextAnchor.LowerLeft, font);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -top), new Vector2(200f, h));
            return t;
        }

        /// <summary>시안 .money 카드(₩ 앰버 14 · 금액 Sora Bold 30 · 잔액 muted 12, 아래 EXP)와 그 아래 칩 3개.</summary>
        private void BuildTopLeft()
        {
            topLeft = UiKit.PanelBox("TopLeft", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(252f, MoneyCardH)).rectTransform;
            cur = TopLeftText("Cur", topLeft, "₩", 14, UiKit.Accent, Fonts.Bold, 8f, 32f);
            money = TopLeftText("Money", topLeft, "100", 30, UiKit.Fg, Fonts.NumBold, 10f, 36f);
            moneyLbl = TopLeftText("MoneyLbl", topLeft, "잔액", 12, UiKit.Muted, Fonts.Body, 8f, 32f);
            expText = TopLeftText("Exp", topLeft, ExpLabel(0), 13, UiKit.ExpColor, Fonts.Num, 46f, 20f);
            expText.alignment = TextAnchor.MiddleLeft;
            chips = UiKit.Rect("Chips", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f - MoneyCardH - 8f), new Vector2(252f, 28f));
            UiKit.Row(chips, 6f, new RectOffset(0, 0, 0, 0));
            var fitter = chips.gameObject.AddComponent<ContentSizeFitter>(); fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            delivered = UiKit.Chip("Delivered", chips, UiKit.Good, "배송", "0");
            lost = UiKit.Chip("Lost", chips, UiKit.Bad, "소실", "0");
            waiting = UiKit.Chip("Waiting", chips, UiKit.Accent, "검사 대기", "0");
            labelWaiting = UiKit.Chip("LabelWaiting", chips, UiKit.Accent, "운송장 대기", "0"); // 스펙 15: 0이면 숨긴다
            labelWaiting.transform.parent.gameObject.SetActive(false);
            LayoutMoney();
        }

        private const float MoneyCardH = 74f;

        /// <summary>잔액 줄 가로 배치: ₩ · 금액(간격 10) · 잔액. 카드 폭은 칩 줄과 내용 중 넓은 쪽(시안: 세로 flex라 같은 폭).</summary>
        private void LayoutMoney()
        {
            float x = 16f;
            cur.rectTransform.anchoredPosition = new Vector2(x, -8f); // 아래 정렬 + 2px: ₩·잔액의 기준선을 금액(Sora 30) 기준선에 맞춘다
            x += cur.preferredWidth + 8f;
            money.rectTransform.anchoredPosition = new Vector2(x, -8f);
            x += money.preferredWidth + 10f;
            moneyLbl.rectTransform.anchoredPosition = new Vector2(x, -8f);
            x += moneyLbl.preferredWidth + 16f;
            float w = Mathf.Max(x, chips.rect.width, expText.preferredWidth + 32f);
            if (Mathf.Abs(topLeft.sizeDelta.x - w) > 0.5f) topLeft.sizeDelta = new Vector2(w, MoneyCardH);
        }

        private void BuildTopRight()
        {
            var row = UiKit.Rect("TopRight", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(900f, 34f));
            UiKit.Row(row, 6f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            var boot = GameBootstrap.Instance;
            pauseBtn = UiKit.Btn("Pause", row, null, () => boot.Session.Paused = !boot.Session.Paused, icon: "ui-pause", key: "P");
            speedBtn = UiKit.Btn("Speed", row, "1x", () => boot.Session.Speed = boot.Session.Speed >= 2f ? 1f : 2f, key: "T", font: Fonts.Num);
            // 상점·상태창은 서로 바꿔 열 수 있다: 둘 중 하나만 열려 있으면 다른 버튼도 동작한다.
            bool Toggleable() => !input.UiBlocksInput || boot.Panels.ShopIsOnlyOpen || boot.Panels.StatusIsOnlyOpen;
            UiKit.Btn("Shop", row, "상점", () => { if (Toggleable()) input.RequestShop(); }, icon: "ui-shop", key: "B");
            UiKit.Btn("StatusButton", row, "상태", () => { if (Toggleable()) input.RequestStatus(); }, icon: "ui-status", key: "C");
            UiKit.Btn("Help", row, "조작", () => SetHelp(!help.gameObject.activeSelf), icon: "ui-help", key: "O");
            UiKit.Btn("Save", row, "저장", () => { if (SaveFile.TrySave(boot.Session, out var e1)) Toast("저장했습니다", ToastKind.Good); else Toast(e1, ToastKind.Bad); });
            UiKit.Btn("Load", row, "불러오기", () => { if (SaveFile.TryLoad(out var s2, out var e2)) { boot.ReplaceSession(s2); Toast("불러왔습니다", ToastKind.Good); } else Toast(e2, ToastKind.Bad); });
            // 스펙 12.1: 새 게임은 시작 화면에서만. 인게임 버튼은 "메뉴"로 시작 화면을 다시 연다(세션 유지, 계속하기가 첫 버튼).
            UiKit.Btn("Menu", row, "메뉴", () => { if (boot.StartScreen != null) boot.StartScreen.Show(true); });
        }

        private void BuildHotbar()
        {
            // 패널 모양(채움·선·그림자)은 바로 앞 형제 "HotbarFrame"이 그린다. "Hotbar"의 자식은 슬롯뿐이다(테스트가 보이는 슬롯 수를 센다).
            var size = new Vector2(HotbarWidth(InputController.SlotCount), SlotSize + 2f * HotbarPad);
            hotbarFrame = UiKit.PanelBox("HotbarFrame", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, HotbarY), size).rectTransform;
            hotbar = UiKit.Rect("Hotbar", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, HotbarY), size);
            var g = UiKit.Row(hotbar, HotbarGap, new RectOffset((int)HotbarPad, (int)HotbarPad, (int)HotbarPad, (int)HotbarPad));
            g.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < InputController.SlotCount; i++)
            {
                int idx = i;
                // 시안 .slot: 72×72, 반경 10, 검정 .3, 1px 선. 선택이면 앰버 선 + 바깥 2px 앰버 .45 + 24px 앰버 빛.
                var slot = UiKit.Box("Slot" + (i + 1), hotbar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotSize, SlotSize), UiKit.SlotBg, UiKit.RSlot);
                slot.raycastTarget = true;
                UiKit.Size(slot, SlotSize, SlotSize);
                var rt = slot.rectTransform;
                var glow = UiKit.AddGlow(rt, UiKit.RSlot, 0, 24, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.25f));
                var ring = UiKit.AddBorder(rt, UiKit.RSlot, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.45f), 2f, 2f, "Ring");
                ring.transform.SetParent(glow.transform, true); // 빛과 같이 켜고 끈다
                var border = UiKit.AddBorder(rt, UiKit.RSlot, UiKit.Line);
                var b = slot.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None; b.navigation = new Navigation { mode = Navigation.Mode.None }; b.targetGraphic = slot;
                b.onClick.AddListener(() => { if (input.UiBlocksInput) return; input.SelectSlot(idx); });
                slotHovers.Add(slot.gameObject.AddComponent<UiHover>());
                // 이름이 슬롯의 첫 Text(테스트가 GetComponentInChildren<Text>()로 읽는다).
                var name = UiKit.Label("Name", rt, "", 11, UiKit.Fg, TextAnchor.MiddleCenter);
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 7f), new Vector2(SlotSize, 16f));
                var key = UiKit.Label("Key", rt, (i + 1).ToString(), 10, UiKit.Muted, TextAnchor.UpperLeft, Fonts.Num);
                UiKit.Place(key.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(7f, -5f), new Vector2(30f, 14f));
                var price = UiKit.Label("Price", rt, "", 10, UiKit.Accent, TextAnchor.UpperRight, Fonts.Num);
                UiKit.Place(price.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-7f, -5f), new Vector2(40f, 14f));
                var tile = UiKit.IconTile("Tile", rt, "tool-conveyor", UiKit.Muted);
                UiKit.Place(tile.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(26f, 26f));
                var dir = UiKit.Box("Dir", rt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-6f, 6f), new Vector2(16f, 16f), UiKit.Accent, 4);
                var arrow = UiKit.Label("Arrow", dir.transform, "", 11, UiKit.AccentFg, TextAnchor.MiddleCenter, Fonts.Bold);
                arrow.horizontalOverflow = HorizontalWrapMode.Overflow;
                slotButtons.Add(b); slotArrows.Add(arrow); slotPrices.Add(price); slotKeys.Add(key); slotNames.Add(name);
                slotTiles.Add(tile); slotIcons.Add(tile.transform.Find("Icon").GetComponent<Image>()); slotBorders.Add(border);
                slotGlows.Add(glow.gameObject); slotDirs.Add(dir.gameObject);
            }
        }

        private static float HotbarWidth(int n) => n * SlotSize + (n - 1) * HotbarGap + 2f * HotbarPad;

        /// <summary>시안 .prompt: panel-2 알약 + line-2 선 + 키캡 E + 아래 꼬리. 경고(실행 불가)는 bad 선·글자.</summary>
        private void BuildPrompt()
        {
            promptBg = UiKit.PanelBox("Prompt", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(170f, PromptH), UiKit.Panel2, (int)(PromptH / 2f), UiKit.Line2);
            promptBg.raycastTarget = false;
            prompt = promptBg.rectTransform;
            promptBorder = prompt.Find("Border").GetComponent<Image>();
            promptText = UiKit.Label("Text", prompt, "", 13, UiKit.Fg, TextAnchor.MiddleLeft);
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow; // 폭은 Update에서 글자에 맞춘다
            UiKit.Place(promptText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(100f, PromptH));
            var key = UiKit.Keycap(prompt, "E");
            promptKey = (RectTransform)key.transform;
            UiKit.Place(promptKey, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(UiKit.KeyHeight, UiKit.KeyHeight));
            var tail = UiKit.Rect("TailFill", prompt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 3f), Vector2.zero);
            var tf = tail.gameObject.AddComponent<Image>(); tf.sprite = UiSprites.Tail(false); tf.color = UiKit.Panel2; tf.raycastTarget = false; tf.SetNativeSize();
            var tl = UiKit.Rect("TailLine", prompt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 3f), Vector2.zero);
            promptTailLineImg = tl.gameObject.AddComponent<Image>(); promptTailLineImg.sprite = UiSprites.Tail(true); promptTailLineImg.color = UiKit.Line2; promptTailLineImg.raycastTarget = false; promptTailLineImg.SetNativeSize();
            prompt.gameObject.SetActive(false);
        }

        private const float PromptH = 34f; // 7 + 키캡 20 + 7

        private void BuildPopups()
        {
            var layer = UiKit.Rect("MoneyPopups", Root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < PopupPoolSize; i++)
            {
                // 시안 .pop: Sora Bold 18, text-shadow 0 1px 3px .85 + 0 0 6px .6 → Outline + Shadow로 근사.
                var t = UiKit.Label("MoneyPopup", layer, "", 18, UiKit.Good, TextAnchor.MiddleCenter, Fonts.NumBold);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(90f, 26f);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.6f); outline.effectDistance = new Vector2(1f, -1f);
                var shadow = t.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.85f); shadow.effectDistance = new Vector2(0f, -1.5f);
                t.gameObject.SetActive(false);
                popups.Add(t); popupWorld.Add(Vector3.zero); popupAge.Add(0f); popupColor.Add(UiKit.Good);
            }
        }

        /// <summary>칸 (x, y) 위에 금액 팝업을 띄운다. 양수는 초록, 음수는 빨강. 풀이 차면 가장 오래된 것을 다시 쓴다.</summary>
        public void SpawnMoneyPopup(int x, int y, int delta)
        {
            if (popups.Count == 0 || delta == 0) return;
            int slot = -1;
            for (int i = 0; i < popups.Count; i++)
            {
                if (!popups[i].gameObject.activeSelf) { slot = i; break; }
                if (slot < 0 || popupAge[i] > popupAge[slot]) slot = i;
            }
            var t = popups[slot];
            var color = delta > 0 ? UiKit.Good : UiKit.Bad;
            t.text = Signed(delta);
            t.color = color;
            popupColor[slot] = color;
            popupWorld[slot] = Grid3D.Tile(x, y) + new Vector3(0f, PopupBaseHeight, 0f);
            popupAge[slot] = 0f;
            t.transform.SetAsLastSibling(); // 최신 팝업이 위에
            t.gameObject.SetActive(true);
            PlacePopup(slot);
        }

        private void PlacePopup(int i)
        {
            float k = Mathf.Clamp01(popupAge[i] / PopupSeconds);
            float rise = PopupRise * (1f - (1f - k) * (1f - k)); // ease-out
            var c = popupColor[i]; c.a = 1f - k * k;
            popups[i].color = c;
            if (TryWorldToCanvas(popupWorld[i] + new Vector3(0f, rise, 0f), out var pos)) popups[i].rectTransform.anchoredPosition = pos;
        }

        /// <summary>월드 좌표 → 캔버스 좌하단 기준 좌표. Overlay가 아닐 때(스크린샷 캡처)는 캔버스 카메라 기준으로 계산한다.</summary>
        public bool TryWorldToCanvas(Vector3 world, out Vector2 pos)
        {
            pos = Vector2.zero;
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.World == null || boot.World.MainCamera == null) return false;
            var sp = boot.World.MainCamera.WorldToScreenPoint(world);
            var uiCam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, sp, uiCam, out var lp);
            pos = lp + Root.rect.size * 0.5f;
            return true;
        }

        /// <summary>설정 입력 배너(스펙 11.8, 시안 .cfgbanner): 상태줄 위 앰버 알약(Noto Bold 13). 설정 모드일 때만 보인다.</summary>
        private void BuildBanner()
        {
            var bg = UiKit.Pill("ConfigBanner", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BannerY), 30f, 300f, UiKit.Accent, UiKit.Accent);
            bg.raycastTarget = false;
            banner = bg.rectTransform;
            bannerText = UiKit.Label("Text", banner, "", 13, UiKit.AccentFg, TextAnchor.MiddleCenter, Fonts.Bold);
            bannerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            banner.gameObject.SetActive(false);
        }

        /// <summary>설정 모드에 맞는 배너 문구. 설정 모드가 아니면 null.</summary>
        public static string BannerText(ConfigMode mode) =>
            mode == ConfigMode.PickDirection ? DirectionBannerText : mode == ConfigMode.PickSlot ? SlotBannerText : null;

        /// <summary>설정 대상 칸까지 본 배너 문구. 분배기·검사기 칸이면 "3 모드"가 붙는다(스펙 14.5).</summary>
        public static string BannerText(ConfigMode mode, CellType? cellType, RouteMode cellMode)
        {
            if (mode == ConfigMode.None || !cellType.HasValue || !InputController.HasMode(cellType.Value)) return BannerText(mode);
            if (mode == ConfigMode.PickSlot)
                return cellType.Value == CellType.Scanner ? AirSlotBannerText : cellMode == RouteMode.Air ? AirSplitterSlotBannerText : SplitterSlotBannerText;
            return cellType.Value == CellType.Scanner && cellMode == RouteMode.Inspect ? ScannerDirectionBannerText : DirectionBannerText;
        }

        private void UpdateBanner()
        {
            CellType? cellType = null;
            var cellMode = RouteMode.Inspect;
            if (input.ConfigTargetIsCell && boundSession != null && boundSession.Grid.InBounds(input.ConfigCell.x, input.ConfigCell.y))
            {
                var c = boundSession.Grid.Get(input.ConfigCell.x, input.ConfigCell.y);
                cellType = c.Type;
                cellMode = c.Mode;
            }
            else if (input.ConfigMode != ConfigMode.None && input.SelectedTool.HasValue)
            {
                cellType = input.SelectedTool.Value.Type; // 도구도 R → 3으로 모드를 고른다
                cellMode = input.ToolMode;
            }
            var text = BannerText(input.ConfigMode, cellType, cellMode);
            bool show = text != null;
            if (banner.gameObject.activeSelf != show) banner.gameObject.SetActive(show);
            if (!show || bannerText.text == text) return;
            bannerText.text = text;
            banner.sizeDelta = new Vector2(bannerText.preferredWidth + 32f, 30f);
        }

        /// <summary>시안 .status: 핫바 바로 위 알약, muted 12. 폭은 글자에 맞춘다.</summary>
        private void BuildStatus()
        {
            status = UiKit.Pill("Status", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, StatusY), StatusH, 300f).rectTransform;
            workerText = UiKit.Label("Text", status, "", 12, UiKit.Muted, TextAnchor.MiddleCenter);
            workerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>상태줄 왼쪽에 붙는 체력 알약: 라벨 "체력"(Noto Bold 12) + 140×10 둥근 바. 채움 폭은 Update에서 체력/최대 체력(강화 반영).</summary>
        private void BuildStamina()
        {
            stamina = UiKit.Pill("Stamina", Root, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-158f, StatusY), StatusH, 200f).rectTransform;
            var lbl = UiKit.Label("Label", stamina, "체력", 12, UiKit.Muted, TextAnchor.MiddleLeft, Fonts.Bold);
            lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            float lw = Mathf.Ceil(lbl.preferredWidth);
            UiKit.Place(lbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(lw + 2f, StatusH));
            var staminaBar = UiKit.Box("Bar", stamina, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f + lw + 8f, 0f), new Vector2(StaminaBarWidth, StaminaBarHeight), UiKit.KeyBg, 5).rectTransform;
            staminaFill = UiKit.Box("Fill", staminaBar, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(StaminaBarWidth, 0f), UiKit.Good, 5);
            // 평소 회복 상한(스펙 11.11): 바 폭의 80% 자리에 2px 흰 눈금. 채움 위에 그린다. 위치는 고정(최대 체력이 바뀌어도 비율).
            var tick = UiKit.Box(StaminaTickName, staminaBar, new Vector2(WorkerService.RegenCap, 0.5f), new Vector2(WorkerService.RegenCap, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, StaminaBarHeight), StaminaTickColor, 0);
            tick.raycastTarget = false;
            stamina.sizeDelta = new Vector2(10f + lw + 8f + StaminaBarWidth + 12f, StatusH);
        }

        /// <summary>체력 바의 80% 눈금(회복 상한) 오브젝트 이름과 색.</summary>
        public const string StaminaTickName = "Tick80";
        public static readonly Color StaminaTickColor = new Color(1f, 1f, 1f, 0.85f); // 80% 눈금: 녹색 채움과 빈 트랙 양쪽에서 보이도록 밝게

        /// <summary>달리는 중(Worker.IsRunning)이면 상태줄 앞에 붙는다.</summary>
        public const string RunningPrefix = "달리는 중 · ";

        /// <summary>체력 바 색: 50 초과 초록, 20 초과 노랑, 그 외 빨강.</summary>
        public static Color StaminaColor(float stamina) => stamina > 50f ? UiKit.Good : stamina > 20f ? UiKit.Warn : UiKit.Bad;

        /// <summary>시계 문구: "1일차 09:00".</summary>
        public static string ClockLabel(Clock c) => c.Day + "일차 " + c.TimeText;

        /// <summary>상단 중앙 시계(시안 .clock: 150×36, 반경 10, top 62). 배지는 시계 오른쪽 바깥 8px(반경 8, 높이 28).</summary>
        private void BuildClock()
        {
            var box = UiKit.PanelBox("Clock", Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(150f, 36f), UiKit.Panel, UiKit.RButton);
            clockText = UiKit.Label("Text", box.transform, "1일차 09:00", 17, UiKit.Fg, TextAnchor.MiddleCenter, Fonts.Bold); // 한글이 섞여 Sora 대신 Noto Bold
            clockText.horizontalOverflow = HorizontalWrapMode.Overflow;
            badgeText = UiKit.Badge("Badge", box.transform, LunchBadgeText, UiKit.Warn, UiKit.AccentFg);
            badgeBg = badgeText.transform.parent.GetComponent<Image>();
            UiKit.Place(badgeBg.rectTransform, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(badgeText.preferredWidth + 24f, 28f));
            badgeBg.gameObject.SetActive(false);
        }

        /// <summary>시안 .toast: panel-2, 반경 10, line-2 선, 왼쪽 4×18 색 막대(info/good/bad). 시계 아래 가운데.</summary>
        private void BuildToast()
        {
            toast = UiKit.PanelBox("Toast", Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(240f, 38f), UiKit.Panel2, UiKit.RButton, UiKit.Line2).rectTransform;
            toast.GetComponent<Image>().raycastTarget = false;
            toastBar = UiKit.Box("Bar", toast, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(4f, 18f), UiKit.Info, 2);
            toastText = UiKit.Label("Text", toast, "", 13, UiKit.Fg, TextAnchor.MiddleLeft);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Place(toastText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(200f, 38f));
            toast.gameObject.SetActive(false);
        }

        /// <summary>조작 서랍(시안 .help): 오른쪽 위(top 62), 폭 320, 12px muted 글자.</summary>
        private void BuildHelp()
        {
            help = UiKit.PanelBox("Help", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -62f), new Vector2(320f, 380f), UiKit.Panel2).rectTransform;
            var title = UiKit.Label("Title", help, "조작", 14, UiKit.Fg, TextAnchor.MiddleLeft, Fonts.Bold);
            UiKit.Top(title.rectTransform, 14f, 30f, 16f, 16f);
            var close = UiKit.Btn("Close", help, "닫기", () => SetHelp(false), height: 30f, fontSize: 12, fit: true);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(56f, 30f));
            helpText = UiKit.Label("Text", help, HelpText, 12, UiKit.Muted, TextAnchor.UpperLeft);
            helpText.supportRichText = true;
            helpText.lineSpacing = 1.15f;
            UiKit.Top(helpText.rectTransform, 54f, 300f, 16f, 16f);
            help.sizeDelta = new Vector2(320f, 54f + helpText.preferredHeight + 16f);
            help.gameObject.SetActive(false);
        }

        private void BuildTouchControls()
        {
            // 시안 .touch: 54px 둥근 사각(반경 12) 3×3 격자, 간격 4. .touch2: 원형 버튼(E 86 primary, 나머지 62), 간격 8, 아래 정렬.
            touchPad = UiKit.Rect("TouchPad", Root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(16f, 16f), new Vector2(170f, 170f));
            void Pad(string name, string txt, Vector2 pos, Direction d)
            {
                var b = UiKit.Btn(name, touchPad, txt, null, fontSize: 20, fixedSize: new Vector2(54f, 54f), radius: 12, padX: 0);
                UiKit.Place((RectTransform)b.transform, Vector2.zero, new Vector2(0.5f, 0.5f), pos, new Vector2(54f, 54f));
                var et = b.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                void On(UnityEngine.EventSystems.EventTriggerType t, bool held) { var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = t }; entry.callback.AddListener(_ => { if (held && input.ConfigMode == ConfigMode.PickDirection) input.ApplyConfigDirection(d); else input.SetPad(d, held); }); et.triggers.Add(entry); }
                On(UnityEngine.EventSystems.EventTriggerType.PointerDown, true); On(UnityEngine.EventSystems.EventTriggerType.PointerUp, false); On(UnityEngine.EventSystems.EventTriggerType.PointerExit, false);
            }
            Pad("TouchPadN", "↑", new Vector2(85f, 143f), Direction.North); Pad("TouchPadW", "←", new Vector2(27f, 85f), Direction.West); Pad("TouchPadE", "→", new Vector2(143f, 85f), Direction.East); Pad("TouchPadS", "↓", new Vector2(85f, 27f), Direction.South);
            touchButtons = UiKit.Rect("TouchButtons", Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(300f, 90f));
            Button Round(string name, string txt, float size, BtnStyle style, Font font, int fs, float cx, System.Action onClick)
            {
                var b = UiKit.Btn(name, touchButtons, txt, onClick, style: style, font: font, fontSize: fs, fixedSize: new Vector2(size, size), radius: Mathf.RoundToInt(size / 2f), padX: 0);
                UiKit.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(cx, size / 2f), new Vector2(size, size));
                return b;
            }
            Round("TouchE", "E", 86f, BtnStyle.Primary, Fonts.NumBold, 18, -43f, () => { if (input.UiBlocksInput) return; input.Interact(); });
            // 달리기: 누르고 있는 동안만 달린다(PointerUp/Exit로 해제).
            var run = Round("TouchRun", "달리기", 62f, BtnStyle.Normal, Fonts.Body, 12, -86f - 8f - 31f, null);
            Round("TouchX", "해체", 62f, BtnStyle.Normal, Fonts.Body, 12, -86f - 16f - 62f - 31f, () => { if (input.UiBlocksInput) return; input.Demolish(); });
            var r = Round("TouchR", "설정", 62f, BtnStyle.Normal, Fonts.Body, 12, -86f - 24f - 124f - 31f, () => { if (input.UiBlocksInput) return; if (input.ConfigMode != ConfigMode.None) input.CancelConfig(); else input.BeginConfig(); });
            touchRotateLabel = r.GetComponent<UiButton>().Label;
            var ret = run.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            void OnRun(UnityEngine.EventSystems.EventTriggerType t, bool held) { var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = t }; entry.callback.AddListener(_ => input.SetRun(held)); ret.triggers.Add(entry); }
            OnRun(UnityEngine.EventSystems.EventTriggerType.PointerDown, true); OnRun(UnityEngine.EventSystems.EventTriggerType.PointerUp, false); OnRun(UnityEngine.EventSystems.EventTriggerType.PointerExit, false);
            RefreshTouchLabels();
        }

        /// <summary>조작 설명 서랍을 열고 닫는다. 열려 있는 동안 입력을 막고 터치 이동을 멈춘다.</summary>
        private void SetHelp(bool open)
        {
            help.gameObject.SetActive(open);
            input.SetBlock("help", open);
            if (open) input.ClearPad();
        }

        /// <summary>조작 서랍을 닫는다(시작 화면을 열 때).</summary>
        public void CloseHelp() { if (help != null && help.gameObject.activeSelf) SetHelp(false); }

        /// <summary>플랫폼 감지와 상관없이 터치 컨트롤을 켜거나 끈다(테스트·디버그용).</summary>
        public void ForceTouchMode(bool on)
        {
            TouchMode = on;
            if (on && touchPad == null) BuildTouchControls();
            if (touchPad != null) touchPad.gameObject.SetActive(on);
            if (touchButtons != null) touchButtons.gameObject.SetActive(on);
        }

        /// <summary>BeginDemolish 실패 문구. 더미가 있는 팔레트는 Occupied.</summary>
        public static string DemolishMessage(BuildResult r) =>
            r == BuildResult.AlreadyDemolishing ? "이미 해체 중입니다" : r == BuildResult.Occupied ? "팔레트를 먼저 비우세요" : "해체할 수 있는 부품이 아닙니다";

        public void Toast(string message) => Toast(message, ToastKind.Info);

        /// <summary>잠시 뜨는 알림. 왼쪽 색 막대가 Info 파랑, Good 초록, Bad 빨강.</summary>
        public void Toast(string message, ToastKind kind)
        {
            LastToast = message;
            if (toastText == null) return; // Start 전
            toastText.text = message;
            toastBar.color = kind == ToastKind.Good ? UiKit.Good : kind == ToastKind.Bad ? UiKit.Bad : UiKit.Info;
            toast.sizeDelta = new Vector2(24f + toastText.preferredWidth + 14f, 38f);
            toastUntil = Time.unscaledTime + 1.8f;
            toast.gameObject.SetActive(true);
        }

        private void OnInteracted(InteractResult r)
        {
            switch (r)
            {
                case InteractResult.NoFloor: Toast("바닥이 없는 곳입니다"); break;
                case InteractResult.CannotPutHere: Toast(CannotPutMessage()); break;
                case InteractResult.NoTool: Toast("도구를 고르세요 (1~8)"); break;
                case InteractResult.Locked: Toast("아직 사지 않은 부지입니다"); break;
                case InteractResult.Occupied: Toast("이미 무언가 있습니다"); break;
                case InteractResult.NotOwned: Toast("해금되지 않은 방향입니다"); break;
                case InteractResult.NotEnoughMoney: Toast("잔액이 부족합니다", ToastKind.Bad); break;
                case InteractResult.Standing: Toast("서 있는 자리에는 놓을 수 없습니다"); break;
                case InteractResult.Exhausted: Toast(ExhaustedToast, ToastKind.Bad); break;
                case InteractResult.NeedsInspection: Toast(NeedsInspectionMessage, ToastKind.Bad); break;
                case InteractResult.Placed:
                    var tool = input.SelectedTool;
                    var s = GameBootstrap.Instance.Session;
                    if (tool.HasValue && s != null) { var (fx, fy) = s.Worker.FacingTile(); SpawnMoneyPopup(fx, fy, -BuildingCosts.Cost(tool.Value.Type)); }
                    break;
            }
        }

        /// <summary>
        /// 트럭 도크 앞 프롬프트: 도크 이름과 예상 잔액 변화. 숨은 불량 여부(Package.Bad)는 보지 않고 검사 결과가 옳다고 가정한다:
        /// 승인 → 출고 +30 / 반품 −5, 거부 → 반품 +10 / 출고 −10, 미검사 → 출고 −20 / 반품 −10(스펙 16.1).
        /// 실제 지급·토스트·팝업은 DeliveryScoring의 진짜 결과를 따르므로 검사 실수는 배송 뒤에 드러난다.
        /// </summary>
        public static string DeliverLabel(bool outlet, bool inspected, bool rejected)
        {
            int d = ExpectedDelta(outlet, inspected, rejected);
            if (!inspected) return (outlet ? "출고" : "반품") + " (미검사 " + Signed(d) + ")";
            if (outlet) return rejected ? "출고 (거부한 택배 " + Signed(d) + ")" : "출고 " + Signed(d);
            return rejected ? "반품 " + Signed(d) : "반품 (승인한 택배 " + Signed(d) + ")";
        }

        /// <summary>운송 수단이 틀린 배송(스펙 14.3) 문구.</summary>
        public static readonly string WrongCarrierLabel = "운송 수단 틀림 " + Signed(DeliveryScoring.Delta(DeliveryOutcome.WrongCarrier));

        /// <summary>도크 종류와 항공 여부까지 본 트럭 도크 프롬프트(스펙 14.2–14.3): 운송 수단이 틀리면 "운송 수단 틀림 −10",
        /// 항공 택배를 항공 트럭에 넣으면 "항공 출고 +30"처럼 꼬리표를 붙인다. 항공 도크는 대응하는 일반 도크 문구.</summary>
        public static string DeliverLabel(CellType dock, bool air, bool inspected, bool rejected, bool waybill = true)
        {
            if (air != Cell.IsAirDock(dock)) return WrongCarrierLabel;
            bool outlet = IsOutletDock(dock);
            // 운송장 없음(스펙 15.3): 미검사 다음 순서. "출고 −20 · 운송장 없음"
            var label = inspected && !waybill
                ? (outlet ? "출고 " : "반품 ") + Signed(DeliveryScoring.Delta(outlet ? DeliveryOutcome.NoWaybillToOutlet : DeliveryOutcome.NoWaybillToReturn)) + " · 운송장 없음"
                : DeliverLabel(outlet, inspected, rejected);
            return air ? AirTag + " " + label : label;
        }

        public static int ExpectedDelta(CellType dock, bool air, bool inspected, bool rejected, bool waybill = true)
        {
            if (air != Cell.IsAirDock(dock)) return DeliveryScoring.Delta(DeliveryOutcome.WrongCarrier);
            bool outlet = IsOutletDock(dock);
            if (inspected && !waybill) return DeliveryScoring.Delta(outlet ? DeliveryOutcome.NoWaybillToOutlet : DeliveryOutcome.NoWaybillToReturn);
            return ExpectedDelta(outlet, inspected, rejected);
        }

        private static bool IsOutletDock(CellType dock) => dock == CellType.Outlet || dock == CellType.AirOutlet;

        /// <summary>프롬프트용 예상 증감. 검사 판정이 옳다고 가정한 결과(불량 여부는 보지 않음)의 DeliveryScoring.Delta.</summary>
        public static int ExpectedDelta(bool outlet, bool inspected, bool rejected)
        {
            DeliveryOutcome o;
            if (!inspected) o = outlet ? DeliveryOutcome.UninspectedToOutlet : DeliveryOutcome.UninspectedToReturn;
            else if (outlet) o = rejected ? DeliveryOutcome.RejectedToOutlet : DeliveryOutcome.Delivered;
            else o = rejected ? DeliveryOutcome.ReturnedBad : DeliveryOutcome.ApprovedToReturn;
            return DeliveryScoring.Delta(o);
        }

        /// <summary>설치 프롬프트: "레일 설치 · ₩10", 팔레트는 "파랑 팔레트 설치 · ₩20", 분배기는 파란 출구 방향을 붙여
        /// "분배기 설치 (파랑 →) · ₩60"(스펙 11.7). 잔액이 모자라면 " (잔액 부족)".</summary>
        public static string PlaceLabel(Tool tool, bool affordable, Direction? red = null) =>
            (tool.Type == CellType.Pallet ? KindName(Tool.KindOf(tool.Direction)) + " " : "") + Name(tool.Type) + " 설치"
            + (tool.Type == CellType.Splitter ? " (파랑 " + Arrow(tool.Direction) + (red.HasValue && red.Value != tool.Direction ? " · 빨강 " + Arrow(red.Value) : "") + ")" : "")
            + " · ₩" + BuildingCosts.Cost(tool.Type) + (affordable ? "" : " (잔액 부족)");

        private string CannotPutMessage()
        {
            var s = GameBootstrap.Instance.Session;
            var (fx, fy) = s.Worker.FacingTile();
            var c = s.Grid.InBounds(fx, fy) ? s.Grid.Get(fx, fy) : null;
            if (c != null && c.Type == CellType.Pallet)
            {
                if (c.IsPalletFull) return "팔레트가 가득 찼습니다";
                if (c.Demolishing) return "해체 중인 팔레트입니다";
            }
            return "여기에는 내려놓을 수 없습니다";
        }

        /// <summary>프롬프트를 띄울 월드 위치. 팔레트는 더미 꼭대기 + 0.3(3층 더미도 가리지 않게), 브릿지는 고가 벨트 위 + 0.3, 그 외는 칸 위 0.9.</summary>
        public static Vector3 PromptAnchor(GameSession s, int x, int y)
        {
            var c = s.Grid.Get(x, y);
            float h = c.Type == CellType.Pallet || c.Type == CellType.Bridge ? PackageViews.TopHeight(c) + 0.3f : 0.9f;
            return Grid3D.Tile(x, y) + new Vector3(0f, h, 0f);
        }

        private void RefreshTouchLabels()
        {
            if (touchRotateLabel == null) return;
            touchRotateLabel.text = input.ConfigMode != ConfigMode.None ? "취소" : "설정";
        }

        public void RefreshHotbar()
        {
            var slots = input.AvailableSlots();
            int shown = Mathf.Clamp(slots.Count, 1, slotButtons.Count);
            hotbar.sizeDelta = new Vector2(HotbarWidth(shown), SlotSize + 2f * HotbarPad); // 보이는 칸 수에 맞춘다
            hotbarFrame.sizeDelta = hotbar.sizeDelta;
            for (int i = 0; i < slotButtons.Count; i++)
            {
                bool has = i < slots.Count;
                slotButtons[i].gameObject.SetActive(has);
                if (!has) continue;
                var type = slots[i];
                slotNames[i].text = Name(type);
                slotPrices[i].text = BuildingCosts.Cost(type).ToString();
                slotTiles[i].color = ToolColor(type);
                slotIcons[i].sprite = UiKit.Icon(ToolIcon(type));
                bool on = input.SelectedSlot == i && input.SelectedTool.HasValue;
                slotGlows[i].SetActive(on);
                slotDirs[i].SetActive(on);
                slotBorders[i].color = on ? UiKit.Accent : slotHovers[i].Over ? UiKit.Line2 : UiKit.Line;
                if (on)
                {
                    slotArrows[i].text = DirLabel(type, input.SelectedTool.Value.Direction);
                    var drt = (RectTransform)slotDirs[i].transform;
                    drt.sizeDelta = new Vector2(Mathf.Max(16f, slotArrows[i].preferredWidth + 6f), 16f);
                }
            }
        }

        /// <summary>잔액 표시. 값이 바뀌면 MoneyFlashSeconds 동안 증가는 초록, 감소는 빨강. 세션이 바뀐 직후는 깜빡이지 않는다.</summary>
        private void UpdateMoney(GameSession s)
        {
            int m = s.Economy.Money;
            if (moneySession != s) { moneySession = s; lastMoney = m; moneyFlashUntil = 0f; }
            else if (m != lastMoney)
            {
                moneyFlashColor = m > lastMoney ? UiKit.Good : UiKit.Bad;
                moneyFlashUntil = Time.unscaledTime + MoneyFlashSeconds;
                lastMoney = m;
            }
            var text = m.ToString();
            if (money.text != text) { UiKit.SetNumText(money, text, true); LayoutMoney(); }
            money.color = Time.unscaledTime < moneyFlashUntil ? moneyFlashColor : UiKit.Fg;
        }

        private void UpdateClock(Clock c)
        {
            clockText.text = ClockLabel(c);
            var phase = c.Phase;
            bool show = phase != DayPhase.Working;
            if (badgeBg.gameObject.activeSelf != show) badgeBg.gameObject.SetActive(show);
            if (!show) return;
            bool lunch = phase == DayPhase.Lunch;
            var text = lunch ? LunchBadgeText : ClosedBadgeText;
            if (badgeText.text != text)
            {
                badgeText.text = text;
                badgeBg.rectTransform.sizeDelta = new Vector2(badgeText.preferredWidth + 24f, 28f);
            }
            badgeBg.color = lunch ? UiKit.Warn : UiKit.Bad;
            badgeText.color = lunch ? UiKit.AccentFg : UiKit.BadFg;
        }

        private void UpdateStamina(float stamina, float max, bool running)
        {
            float st = Mathf.Clamp(stamina, 0f, max);
            staminaFill.rectTransform.sizeDelta = new Vector2(max > 0f ? StaminaBarWidth * st / max : 0f, 0f);
            var col = StaminaColor(st);
            if (running) { float a = col.a; col = Color.Lerp(col, Color.white, 0.35f); col.a = a; } // 달리는 중: 조금 밝게
            if (st < StaminaPulseBelow) col.a = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * 8f));
            staminaFill.color = col;
        }

        private static void SetChip(Text value, int n)
        {
            var s = n.ToString();
            if (value.text == s) return;
            value.text = s;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)value.transform.parent);
        }

        /// <summary>프롬프트 폭: 왼쪽 14 + 글자 + (키캡이면 10 + 키캡 + 10, 아니면 14).</summary>
        private void LayoutPrompt(bool canAct)
        {
            float tw = Mathf.Ceil(promptText.preferredWidth);
            promptText.rectTransform.sizeDelta = new Vector2(tw + 2f, PromptH);
            if (promptKey.gameObject.activeSelf != canAct) promptKey.gameObject.SetActive(canAct);
            float w = 14f + tw + (canAct ? 10f + promptKey.rect.width + 10f : 14f);
            prompt.sizeDelta = new Vector2(Mathf.Max(64f, w), PromptH);
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            var s = boot.Session;
            BindSession(s);
            UpdateMoney(s);
            expText.text = ExpLabel(s.Progression.Exp);
            expText.color = Time.unscaledTime < expFlashUntil ? expFlashColor : UiKit.ExpColor;
            SetChip(delivered, s.Sim.DeliveredCount);
            SetChip(lost, s.Sim.LostCount);
            SetChip(waiting, s.Sim.AwaitingInspectionCount);
            int labels = s.Sim.AwaitingLabelCount;
            var labelChip = labelWaiting.transform.parent.gameObject;
            if (labelChip.activeSelf != labels > 0) { labelChip.SetActive(labels > 0); LayoutRebuilder.MarkLayoutForRebuild(chips); }
            if (labels > 0) SetChip(labelWaiting, labels);
            LayoutMoney();
            UiKit.SetButtonLook(pauseBtn, s.Paused);
            UiKit.SetButtonText(speedBtn, s.Speed >= 2f ? "2x" : "1x");
            UiKit.SetButtonLook(speedBtn, s.Speed >= 2f);
            for (int i = 0; i < slotButtons.Count; i++)
                if (slotButtons[i].gameObject.activeSelf && !slotGlows[i].activeSelf) slotBorders[i].color = slotHovers[i].Over ? UiKit.Line2 : UiKit.Line;

            var w = s.Worker.Worker;
            UpdateClock(s.Clock);
            UpdateStamina(w.Stamina, s.Worker.EffectiveMaxStamina, s.Worker.IsRunning);
            var tool = input.SelectedTool;
            var statusLine = (s.Worker.IsRunning ? RunningPrefix : "") + (w.Carrying != null ? ((w.Carrying.Air ? AirTag + " " : "") + "택배 #" + w.Carrying.Id + " 운반 중 · 레일·팔레트·트럭을 보고 E")
                : tool.HasValue ? (Name(tool.Value.Type) + " " + DirLabel(tool.Value.Type, tool.Value.Direction) + " 들고 있음 · E로 설치, R 설정")
                : "빈손 · 1~8로 도구 선택");
            if (workerText.text != statusLine)
            {
                workerText.text = statusLine;
                status.sizeDelta = new Vector2(Mathf.Ceil(workerText.preferredWidth) + 24f, StatusH);
            }
            // 터치 모드에서는 방향패드·원형 버튼 위로 핫바·상태줄·배너를 올린다(시안 @media (pointer: coarse)).
            float lift = touchPad != null && touchPad.gameObject.activeSelf ? TouchLift : 0f;
            hotbar.anchoredPosition = new Vector2(0f, HotbarY + lift);
            hotbarFrame.anchoredPosition = hotbar.anchoredPosition;
            status.anchoredPosition = new Vector2(0f, StatusY + lift);
            stamina.anchoredPosition = new Vector2(-status.sizeDelta.x * 0.5f - 8f, StatusY + lift);
            banner.anchoredPosition = new Vector2(0f, BannerY + lift);

            // 프롬프트: Preview 결과로 문구를 만들고 바라보는 칸 위에 띄운다.
            var r = s.Worker.Preview(tool);
            var (fx, fy) = s.Worker.FacingTile();
            string label = null;
            bool canAct = true;
            Color textColor = UiKit.Fg;
            switch (r)
            {
                case InteractResult.PickedUp: label = s.Grid.Get(fx, fy).Type == CellType.Pallet ? "집기 (" + s.Sim.PalletCount(fx, fy) + ")" : "집기"; break;
                case InteractResult.PutDown: label = "놓기"; break;
                case InteractResult.Delivered:
                    var dock = s.Grid.Get(fx, fy).Type;
                    label = DeliverLabel(dock, w.Carrying.Air, w.Carrying.Inspected, w.Carrying.Rejected, w.Carrying.Waybill);
                    textColor = ExpectedDelta(dock, w.Carrying.Air, w.Carrying.Inspected, w.Carrying.Rejected, w.Carrying.Waybill) >= 0 ? UiKit.Good : UiKit.Bad;
                    break;
                case InteractResult.InspectionRequired: label = "검사"; break;
                case InteractResult.LabelRequired: label = LabelPrompt; break;
                case InteractResult.NeedsInspection: label = NeedsInspectionMessage; canAct = false; textColor = UiKit.Bad; break;
                case InteractResult.Exhausted: label = ExhaustedPrompt; canAct = false; textColor = UiKit.Bad; break;
                case InteractResult.Placed: label = PlaceLabel(tool.Value, true, input.ToolSplitterRed); break;
                case InteractResult.NotEnoughMoney: label = PlaceLabel(tool.Value, false, input.ToolSplitterRed); canAct = false; textColor = UiKit.Bad; break;
            }
            if (label != null && TryWorldToCanvas(PromptAnchor(s, fx, fy), out var promptPos))
            {
                prompt.anchoredPosition = promptPos;
                promptText.text = label;
                promptText.color = textColor;
                var line = canAct ? UiKit.Line2 : UiKit.Bad; // 시안 .prompt.warn: bad 선·글자
                promptBorder.color = line;
                promptTailLineImg.color = line;
                LayoutPrompt(canAct);
                prompt.gameObject.SetActive(true);
            }
            else prompt.gameObject.SetActive(false);

            for (int i = 0; i < popups.Count; i++)
            {
                if (!popups[i].gameObject.activeSelf) continue;
                popupAge[i] += Time.unscaledDeltaTime;
                if (popupAge[i] >= PopupSeconds) { popups[i].gameObject.SetActive(false); continue; }
                PlacePopup(i);
            }

            UpdateBanner();
            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
        }
    }
}
