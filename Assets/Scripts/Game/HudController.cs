using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>상단 바, 핫바, 프롬프트, 작업자 상태, 토스트, 조작 설명, 터치 컨트롤.</summary>
    public sealed class HudController : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public bool TouchMode { get; private set; }
        public Font Font => UiKit.DefaultFont;

        private Text money, delivered, lost, waiting, workerText, promptText, toastText, helpText;
        private Button pauseBtn, speedBtn, shopBtn, helpBtn;
        private RectTransform hotbar, prompt, toast, help, touchPad, touchButtons;
        private readonly List<Button> slotButtons = new List<Button>();
        private readonly List<Text> slotArrows = new List<Text>();
        private readonly List<Text> slotPrices = new List<Text>();
        private readonly List<Text> slotKeys = new List<Text>();
        private float toastUntil;
        private GameSession boundSession;
        private Canvas rootCanvas;
        private Image promptBg;
        private static readonly Color PromptBadBg = new Color(0.32f, 0.08f, 0.06f, 0.94f);
        // 잔액 깜빡임: 값이 바뀌면 0.4초 동안 초록(증가)/빨강(감소).
        public const float MoneyFlashSeconds = 0.4f;
        private int lastMoney;
        private GameSession moneySession;
        private float moneyFlashUntil;
        private Color moneyFlashColor;
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
        private Text touchRotateLabel;
        private InputController input;

        private static readonly Dictionary<CellType, string> Names = new Dictionary<CellType, string>
        {
            { CellType.Conveyor, "레일" }, { CellType.FastConveyor, "고속 레일" }, { CellType.Pallet, "팔레트" }, { CellType.Table, "검사대" }, { CellType.Splitter, "분배기" }, { CellType.Scanner, "자동 검사기" },
            { CellType.Inlet, "입고구" }, { CellType.Outlet, "출고구" }, { CellType.ReturnDock, "반품구" }, { CellType.Empty, "빈 칸" }, { CellType.Locked, "잠긴 칸" },
        };
        public static string Name(CellType t) => Names.TryGetValue(t, out var n) ? n : t.ToString();
        public static string Arrow(Direction d) => d == Direction.North ? "↑" : d == Direction.East ? "→" : d == Direction.South ? "↓" : "←";
        public static string KindName(PalletKind k) => k == PalletKind.Blue ? "파랑" : "빨강";
        /// <summary>도구의 방향 표기. 팔레트는 화살표 대신 종류(파랑/빨강).</summary>
        public static string DirLabel(CellType t, Direction d) => t == CellType.Pallet ? KindName(Tool.KindOf(d)) : Arrow(d);
        /// <summary>놓여 있는 칸의 방향 표기. 팔레트는 Cell.PalletKind에서 읽는다.</summary>
        public static string DirLabel(Cell c) => c.Type == CellType.Pallet ? KindName(c.PalletKind) : Arrow(c.Direction);

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input;
            TouchMode = Application.isMobilePlatform || UnityEngine.InputSystem.Touchscreen.current != null;
            var canvas = UiKit.MakeCanvas();
            canvas.transform.SetParent(transform, false);
            Root = canvas.GetComponent<RectTransform>();
            rootCanvas = canvas;
            BuildTopLeft(); BuildTopRight(); BuildHotbar(); BuildPrompt(); BuildPopups(); BuildStatus(); BuildToast(); BuildHelp();
            if (TouchMode) BuildTouchControls();
            input.ToolChanged += RefreshHotbar;
            input.ToolChanged += RefreshTouchLabels;
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

        /// <summary>배송 토스트·금액 팝업을 받을 세션을 바꾼다. 이전 세션 구독은 끊고 남은 팝업은 숨긴다.</summary>
        private void BindSession(GameSession session)
        {
            if (boundSession == session) return;
            if (boundSession != null)
            {
                boundSession.Sim.PackageDelivered -= OnPackageDelivered;
                boundSession.Build.CellChanged -= OnCellChanged;
            }
            boundSession = session;
            pendingRefunds.Clear();
            for (int i = 0; i < popups.Count; i++) popups[i].gameObject.SetActive(false);
            if (boundSession == null) return;
            boundSession.Sim.PackageDelivered += OnPackageDelivered;
            boundSession.Build.CellChanged += OnCellChanged;
            // 불러온 저장에 해체 중인 칸이 있으면 완료 때 환불 팝업을 띄울 수 있게 미리 적어 둔다.
            for (int x = 0; x < FactoryGrid.Width; x++)
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = boundSession.Grid.Get(x, y);
                    if (c.Demolishing) pendingRefunds[(x, y)] = BuildService.RefundFor(c.Type);
                }
        }

        private void OnDestroy() => BindSession(null);

        private void OnPackageDelivered(Package p, DeliveryOutcome outcome, int delta)
        {
            Toast(DeliveryMessage(outcome), DeliveryScoring.IsSuccess(outcome) ? ToastKind.Good : ToastKind.Bad);
            bool outlet = DockOf(outcome) == CellType.Outlet;
            SpawnMoneyPopup(outlet ? FactoryGrid.OutletX : FactoryGrid.ReturnDockX, outlet ? FactoryGrid.OutletY : FactoryGrid.ReturnDockY, delta);
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

        /// <summary>배송 결과가 어느 도크에서 났는가.</summary>
        public static CellType DockOf(DeliveryOutcome o) =>
            o == DeliveryOutcome.Delivered || o == DeliveryOutcome.UninspectedToOutlet || o == DeliveryOutcome.RejectedToOutlet || o == DeliveryOutcome.BadToOutlet
                ? CellType.Outlet : CellType.ReturnDock;

        /// <summary>금액 표기: +30, −10(유니코드 마이너스).</summary>
        public static string Signed(int delta) => delta >= 0 ? "+" + delta : "−" + (-delta);

        /// <summary>배송 결과 문구: 이유와 잔액 변화. 벌점이면 왜 깎였는지 알려 준다.</summary>
        public static string DeliveryMessage(DeliveryOutcome o)
        {
            switch (o)
            {
                case DeliveryOutcome.Delivered: return "정상 출고 +30";
                case DeliveryOutcome.ReturnedBad: return "불량 반품 +10";
                case DeliveryOutcome.UninspectedToOutlet: return "검사대를 거치지 않은 택배 · 출고 −10";
                case DeliveryOutcome.UninspectedToReturn: return "검사대를 거치지 않은 택배 · 반품 −5";
                case DeliveryOutcome.RejectedToOutlet: return "거부한 택배를 출고 −10";
                case DeliveryOutcome.BadToOutlet: return "불량을 승인해 출고 −30";
                case DeliveryOutcome.GoodToReturn: return "정상 택배를 반품 −5";
                case DeliveryOutcome.ApprovedToReturn: return "승인한 택배를 반품 −5";
                default: return o.ToString();
            }
        }

        private void BuildTopLeft()
        {
            var box = UiKit.PanelBox("TopLeft", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(340f, 80f));
            money = UiKit.Label("Money", box.transform, "100", 28, UiKit.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            money.rectTransform.offsetMin = new Vector2(14f, 30f); money.rectTransform.offsetMax = new Vector2(-14f, -6f);
            var lbl = UiKit.Label("MoneyLbl", box.transform, "잔액", 14, UiKit.Muted, TextAnchor.UpperRight);
            lbl.rectTransform.offsetMin = new Vector2(14f, 40f); lbl.rectTransform.offsetMax = new Vector2(-14f, -12f);
            delivered = UiKit.Label("Delivered", box.transform, "배송 0", 16, UiKit.Good, TextAnchor.LowerLeft);
            delivered.rectTransform.offsetMin = new Vector2(14f, 8f); delivered.rectTransform.offsetMax = new Vector2(-226f, -44f);
            lost = UiKit.Label("Lost", box.transform, "소실 0", 16, UiKit.Bad, TextAnchor.LowerLeft);
            lost.rectTransform.offsetMin = new Vector2(112f, 8f); lost.rectTransform.offsetMax = new Vector2(-128f, -44f);
            waiting = UiKit.Label("Waiting", box.transform, "검사 대기 0", 16, UiKit.Fg, TextAnchor.LowerLeft);
            waiting.rectTransform.offsetMin = new Vector2(210f, 8f); waiting.rectTransform.offsetMax = new Vector2(-8f, -44f);
        }

        /// <summary>상단 버튼 높이. 터치 기준 최소 44px를 항상 지킨다.</summary>
        private const float TopButtonHeight = 44f;
        private const float SlotW = 92f, SlotH = 78f;

        private void BuildTopRight()
        {
            var row = UiKit.Rect("TopRight", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(720f, TopButtonHeight));
            UiKit.Row(row, 6f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            var boot = GameBootstrap.Instance;
            const float h = TopButtonHeight;
            const int fs = 15;
            pauseBtn = UiKit.Button("Pause", row, "일시정지 P", new Vector2(116f, h), () => boot.Session.Paused = !boot.Session.Paused, fontSize: fs);
            speedBtn = UiKit.Button("Speed", row, "1x T", new Vector2(70f, h), () => boot.Session.Speed = boot.Session.Speed >= 2f ? 1f : 2f, fontSize: fs);
            shopBtn = UiKit.Button("Shop", row, "상점 B", new Vector2(86f, h), () => { if (input.UiBlocksInput && !boot.Panels.ShopIsOnlyOpen) return; input.RequestShop(); }, fontSize: fs);
            helpBtn = UiKit.Button("Help", row, "조작 O", new Vector2(86f, h), () => SetHelp(!help.gameObject.activeSelf), fontSize: fs);
            var saveBtn = UiKit.Button("Save", row, "저장", new Vector2(66f, h), () => Toast(SaveFile.TrySave(boot.Session, out var e1) ? "저장했습니다" : e1), fontSize: fs);
            var loadBtn = UiKit.Button("Load", row, "불러오기", new Vector2(94f, h), () => { if (SaveFile.TryLoad(out var s2, out var e2)) { boot.ReplaceSession(s2); Toast("불러왔습니다"); } else Toast(e2); }, fontSize: fs);
            var newBtn = UiKit.Button("New", row, "새 게임", new Vector2(84f, h), () => { boot.StartNewGame(); Toast("새 게임"); }, fontSize: fs);
            foreach (var b in new[] { pauseBtn, speedBtn, shopBtn, helpBtn, saveBtn, loadBtn, newBtn }) UiKit.Size(b, b.GetComponent<RectTransform>().sizeDelta.x, h);
        }

        private void BuildHotbar()
        {
            hotbar = UiKit.PanelBox("Hotbar", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(InputController.SlotCount * (SlotW + 6f) - 6f + 16f, SlotH + 16f)).rectTransform;
            UiKit.Row(hotbar, 6f, new RectOffset(8, 8, 8, 8)).childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < InputController.SlotCount; i++)
            {
                int idx = i;
                var b = UiKit.Button("Slot" + (i + 1), hotbar, "", new Vector2(SlotW, SlotH), () => { if (input.UiBlocksInput) return; input.SelectSlot(idx); }, new Color(0f, 0f, 0f, 0.35f), fontSize: 15);
                UiKit.Size(b, SlotW, SlotH);
                // 이름(15)은 가운데, 가격은 그 아래 자기 줄, 키 번호·방향은 위쪽 모서리.
                var name = b.GetComponentInChildren<Text>();
                name.alignment = TextAnchor.MiddleCenter;
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                name.rectTransform.offsetMin = new Vector2(2f, 22f); name.rectTransform.offsetMax = new Vector2(-2f, -18f);
                var price = UiKit.Label("Price", b.transform, "", 13, UiKit.Muted, TextAnchor.LowerCenter, FontStyle.Bold);
                price.rectTransform.offsetMin = new Vector2(2f, 5f); price.rectTransform.offsetMax = new Vector2(-2f, -54f);
                var key = UiKit.Label("Key", b.transform, (i + 1).ToString(), 13, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
                key.rectTransform.offsetMin = new Vector2(6f, 0f); key.rectTransform.offsetMax = new Vector2(0f, -3f);
                var arrow = UiKit.Label("Arrow", b.transform, "", 16, UiKit.Accent, TextAnchor.UpperRight, FontStyle.Bold);
                arrow.rectTransform.offsetMin = new Vector2(0f, 0f); arrow.rectTransform.offsetMax = new Vector2(-6f, -2f);
                slotButtons.Add(b); slotArrows.Add(arrow); slotPrices.Add(price); slotKeys.Add(key);
            }
        }

        private void BuildPrompt()
        {
            promptBg = UiKit.PanelBox("Prompt", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(170f, 32f), UiKit.Panel2);
            prompt = promptBg.rectTransform;
            promptText = UiKit.Label("Text", prompt, "", 14, UiKit.Fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow; // 폭은 Update에서 글자에 맞춘다
            prompt.gameObject.SetActive(false);
        }

        private void BuildPopups()
        {
            var layer = UiKit.Rect("MoneyPopups", Root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < PopupPoolSize; i++)
            {
                var t = UiKit.Label("MoneyPopup", layer, "", 18, UiKit.Good, TextAnchor.MiddleCenter, FontStyle.Bold);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(90f, 26f);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f); outline.effectDistance = new Vector2(1.5f, -1.5f);
                var shadow = t.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.5f); shadow.effectDistance = new Vector2(2f, -3f);
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
        private bool TryWorldToCanvas(Vector3 world, out Vector2 pos)
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

        private void BuildStatus()
        {
            var box = UiKit.PanelBox("Status", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, SlotH + 16f + 14f + 8f), new Vector2(560f, 32f));
            workerText = UiKit.Label("Text", box.transform, "", 16, UiKit.Fg, TextAnchor.MiddleCenter);
        }

        private void BuildToast()
        {
            toast = UiKit.PanelBox("Toast", Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(440f, 40f), UiKit.Panel2).rectTransform;
            toastText = UiKit.Label("Text", toast, "", 16, UiKit.Fg, TextAnchor.MiddleCenter);
            toast.gameObject.SetActive(false);
        }

        private void BuildHelp()
        {
            help = UiKit.PanelBox("Help", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -60f), new Vector2(330f, 228f)).rectTransform;
            helpText = UiKit.Label("Text", help, "WASD/방향키 이동 · E 집기·놓기·설치·검사 · X 해체\n1~6 도구 선택 · R 방향(팔레트는 색) · Q 빈손\n검사대 위 택배는 검사 전까지 멈춥니다.\n팔레트에서 집어 트럭으로: 정상은 출고, 불량은 반품.\n입고 8초마다 1개(배송 5건마다 빨라져 최소 3초) · 정상 출고 +30, 불량 반품 +10 · 해체 2초 후 80% 환불\n택배는 검사대(3)에서 E로 검사한 뒤 출고/반품하세요. 검사 없이 트럭에 넣으면 벌점입니다.", 12, UiKit.Muted, TextAnchor.UpperLeft);
            helpText.rectTransform.offsetMin = new Vector2(12f, 52f); helpText.rectTransform.offsetMax = new Vector2(-12f, -8f);
            var close = UiKit.Button("Close", help, "닫기", new Vector2(100f, 36f), () => SetHelp(false));
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f); crt.pivot = new Vector2(0.5f, 0f); crt.anchoredPosition = new Vector2(0f, 10f);
            help.gameObject.SetActive(false);
        }

        private void BuildTouchControls()
        {
            touchPad = UiKit.Rect("TouchPad", Root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(16f, 110f), new Vector2(170f, 170f));
            void Pad(string name, string txt, Vector2 pos, Direction d) { var b = UiKit.Button(name, touchPad, txt, new Vector2(52f, 52f), null, null, null, 20, Vector2.zero); b.GetComponent<RectTransform>().anchoredPosition = pos; var et = b.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>(); void On(UnityEngine.EventSystems.EventTriggerType t, bool held) { var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = t }; entry.callback.AddListener(_ => input.SetPad(d, held)); et.triggers.Add(entry); } On(UnityEngine.EventSystems.EventTriggerType.PointerDown, true); On(UnityEngine.EventSystems.EventTriggerType.PointerUp, false); On(UnityEngine.EventSystems.EventTriggerType.PointerExit, false); }
            Pad("TouchPadN", "↑", new Vector2(85f, 140f), Direction.North); Pad("TouchPadW", "←", new Vector2(28f, 85f), Direction.West); Pad("TouchPadE", "→", new Vector2(142f, 85f), Direction.East); Pad("TouchPadS", "↓", new Vector2(85f, 30f), Direction.South);
            touchButtons = UiKit.Rect("TouchButtons", Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 110f), new Vector2(200f, 170f));
            var e = UiKit.Button("TouchE", touchButtons, "E", new Vector2(84f, 84f), () => { if (input.UiBlocksInput) return; input.Interact(); }, UiKit.Accent, UiKit.AccentFg, 20, new Vector2(1f, 0f)); e.GetComponent<RectTransform>().anchoredPosition = new Vector2(-50f, 50f);
            var x = UiKit.Button("TouchX", touchButtons, "해체", new Vector2(62f, 62f), () => { if (input.UiBlocksInput) return; input.Demolish(); }, new Color(0.55f, 0.2f, 0.16f, 0.92f), null, 14, new Vector2(1f, 0f)); x.GetComponent<RectTransform>().anchoredPosition = new Vector2(-140f, 40f);
            var r = UiKit.Button("TouchR", touchButtons, "방향", new Vector2(62f, 62f), () => { if (input.UiBlocksInput) return; input.RotateTool(); }, null, null, 14, new Vector2(1f, 0f)); r.GetComponent<RectTransform>().anchoredPosition = new Vector2(-140f, 118f);
            touchRotateLabel = r.GetComponentInChildren<Text>();
            RefreshTouchLabels();
            hotbar.anchoredPosition = new Vector2(0f, 14f);
        }

        /// <summary>조작 설명 서랍을 열고 닫는다. 열려 있는 동안 입력을 막고 터치 이동을 멈춘다.</summary>
        private void SetHelp(bool open)
        {
            help.gameObject.SetActive(open);
            input.SetBlock("help", open);
            if (open) input.ClearPad();
        }

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

        /// <summary>잠시 뜨는 알림. Good은 초록, Bad는 빨강 글자.</summary>
        public void Toast(string message, ToastKind kind)
        {
            LastToast = message;
            toastText.text = message;
            toastText.color = kind == ToastKind.Good ? UiKit.Good : kind == ToastKind.Bad ? UiKit.Bad : UiKit.Fg;
            toastUntil = Time.unscaledTime + 1.8f;
            toast.gameObject.SetActive(true);
        }

        private void OnInteracted(InteractResult r)
        {
            switch (r)
            {
                case InteractResult.NoFloor: Toast("바닥이 없는 곳입니다"); break;
                case InteractResult.CannotPutHere: Toast(CannotPutMessage()); break;
                case InteractResult.NoTool: Toast("도구를 고르세요 (1~6)"); break;
                case InteractResult.Locked: Toast("아직 사지 않은 부지입니다"); break;
                case InteractResult.Occupied: Toast("이미 무언가 있습니다"); break;
                case InteractResult.NotOwned: Toast("해금되지 않은 방향입니다"); break;
                case InteractResult.NotEnoughMoney: Toast("잔액이 부족합니다"); break;
                case InteractResult.Standing: Toast("서 있는 자리에는 놓을 수 없습니다"); break;
                case InteractResult.Placed:
                    var tool = input.SelectedTool;
                    var s = GameBootstrap.Instance.Session;
                    if (tool.HasValue && s != null) { var (fx, fy) = s.Worker.FacingTile(); SpawnMoneyPopup(fx, fy, -BuildingCosts.Cost(tool.Value.Type)); }
                    break;
            }
        }

        /// <summary>트럭 도크 앞 프롬프트: 도크 이름과 잔액 변화. 벌점이면 이유를 괄호에 붙인다(예: "출고 +30", "반품 (미검사 −5)").</summary>
        public static string DeliverLabel(DeliveryOutcome o)
        {
            string dock = DockOf(o) == CellType.Outlet ? "출고" : "반품";
            string money = Signed(DeliveryScoring.Delta(o));
            switch (o)
            {
                case DeliveryOutcome.UninspectedToOutlet:
                case DeliveryOutcome.UninspectedToReturn: return dock + " (미검사 " + money + ")";
                case DeliveryOutcome.RejectedToOutlet: return dock + " (거부한 택배 " + money + ")";
                case DeliveryOutcome.BadToOutlet: return dock + " (불량 " + money + ")";
                case DeliveryOutcome.GoodToReturn: return dock + " (정상 택배 " + money + ")";
                case DeliveryOutcome.ApprovedToReturn: return dock + " (승인한 택배 " + money + ")";
                default: return dock + " " + money;
            }
        }

        /// <summary>설치 프롬프트: "레일 설치 · ₩10", 팔레트는 "파랑 팔레트 설치 · ₩20". 잔액이 모자라면 " (잔액 부족)".</summary>
        public static string PlaceLabel(Tool tool, bool affordable) =>
            (tool.Type == CellType.Pallet ? KindName(Tool.KindOf(tool.Direction)) + " " : "") + Name(tool.Type) + " 설치 · ₩" + BuildingCosts.Cost(tool.Type) + (affordable ? "" : " (잔액 부족)");

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

        /// <summary>프롬프트를 띄울 월드 위치. 팔레트는 더미 꼭대기 + 0.3(3층 더미도 가리지 않게), 그 외는 칸 위 0.9.</summary>
        public static Vector3 PromptAnchor(GameSession s, int x, int y)
        {
            var c = s.Grid.Get(x, y);
            float h = c.Type == CellType.Pallet ? PackageViews.TopHeight(c) + 0.3f : 0.9f;
            return Grid3D.Tile(x, y) + new Vector3(0f, h, 0f);
        }

        private void RefreshTouchLabels()
        {
            if (touchRotateLabel == null) return;
            var t = input.SelectedTool;
            touchRotateLabel.text = t.HasValue && t.Value.Type == CellType.Pallet ? "색" : "방향";
        }

        public void RefreshHotbar()
        {
            var slots = input.AvailableSlots();
            int shown = Mathf.Clamp(slots.Count, 1, slotButtons.Count);
            hotbar.sizeDelta = new Vector2(shown * (SlotW + 6f) - 6f + 16f, SlotH + 16f); // 보이는 칸 수에 맞춘다
            for (int i = 0; i < slotButtons.Count; i++)
            {
                bool has = i < slots.Count;
                slotButtons[i].gameObject.SetActive(has);
                if (!has) continue;
                var type = slots[i];
                slotButtons[i].GetComponentInChildren<Text>().text = Name(type);
                slotPrices[i].text = "₩ " + BuildingCosts.Cost(type);
                bool on = input.SelectedSlot == i && input.SelectedTool.HasValue;
                UiKit.SetButtonLook(slotButtons[i], on);
                slotPrices[i].color = on ? UiKit.AccentFg : UiKit.Accent;
                slotKeys[i].color = on ? UiKit.AccentFg : UiKit.Muted;
                slotArrows[i].color = on ? UiKit.AccentFg : UiKit.Accent;
                if (type == CellType.Pallet) // 화살표 대신 종류: 고른 상태면 현재 종류, 아니면 고르면 놓일 종류
                    slotArrows[i].text = DirLabel(type, on ? input.SelectedTool.Value.Direction : input.PreferredDirection(type));
                else
                    slotArrows[i].text = on ? Arrow(input.SelectedTool.Value.Direction) : (input.OwnedDirections(type).Count > 1 ? "R" : Arrow(input.OwnedDirections(type)[0]));
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
            money.text = m.ToString();
            money.color = Time.unscaledTime < moneyFlashUntil ? moneyFlashColor : UiKit.Accent;
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            var s = boot.Session;
            BindSession(s);
            UpdateMoney(s);
            delivered.text = "배송 " + s.Sim.DeliveredCount;
            lost.text = "소실 " + s.Sim.LostCount;
            waiting.text = "검사 대기 " + s.Sim.AwaitingInspectionCount;
            UiKit.SetButtonLook(pauseBtn, s.Paused);
            speedBtn.GetComponentInChildren<Text>().text = (s.Speed >= 2f ? "2x" : "1x") + " T";
            UiKit.SetButtonLook(speedBtn, s.Speed >= 2f);

            var w = s.Worker.Worker;
            var tool = input.SelectedTool;
            workerText.text = w.Carrying != null ? ("택배 #" + w.Carrying.Id + " 운반 중 · 레일·팔레트·트럭을 보고 E")
                : tool.HasValue ? (Name(tool.Value.Type) + " " + DirLabel(tool.Value.Type, tool.Value.Direction) + " 들고 있음 · E로 설치, R로 " + (tool.Value.Type == CellType.Pallet ? "색" : "방향"))
                : "빈손 · 1~6으로 도구 선택";

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
                    var outcome = DeliveryScoring.Outcome(w.Carrying, s.Grid.Get(fx, fy).Type);
                    label = DeliverLabel(outcome);
                    textColor = DeliveryScoring.Delta(outcome) >= 0 ? UiKit.Good : UiKit.Bad;
                    break;
                case InteractResult.InspectionRequired: label = "검사"; break;
                case InteractResult.Placed: label = PlaceLabel(tool.Value, true); break;
                case InteractResult.NotEnoughMoney: label = PlaceLabel(tool.Value, false); canAct = false; textColor = UiKit.Bad; break;
            }
            if (label != null && TryWorldToCanvas(PromptAnchor(s, fx, fy), out var promptPos))
            {
                prompt.anchoredPosition = promptPos;
                promptText.text = canAct ? label + "   [E]" : label;
                promptText.color = textColor;
                promptBg.color = canAct ? UiKit.Panel2 : PromptBadBg;
                prompt.sizeDelta = new Vector2(Mathf.Max(120f, promptText.preferredWidth + 28f), 32f);
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

            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
        }
    }
}
