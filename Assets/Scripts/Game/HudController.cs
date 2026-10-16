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
        private InputController input;

        private static readonly Dictionary<CellType, string> Names = new Dictionary<CellType, string>
        {
            { CellType.Conveyor, "레일" }, { CellType.FastConveyor, "고속 레일" }, { CellType.Table, "검사대" }, { CellType.Splitter, "분배기" }, { CellType.Scanner, "자동 검사기" },
            { CellType.Inlet, "입고구" }, { CellType.Outlet, "출고구" }, { CellType.ReturnDock, "반품구" }, { CellType.Empty, "빈 칸" }, { CellType.Locked, "잠긴 칸" },
        };
        public static string Name(CellType t) => Names.TryGetValue(t, out var n) ? n : t.ToString();
        public static string Arrow(Direction d) => d == Direction.North ? "↑" : d == Direction.East ? "→" : d == Direction.South ? "↓" : "←";

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input;
            TouchMode = Application.isMobilePlatform || UnityEngine.InputSystem.Touchscreen.current != null;
            var canvas = UiKit.MakeCanvas();
            canvas.transform.SetParent(transform, false);
            Root = canvas.GetComponent<RectTransform>();
            BuildTopLeft(); BuildTopRight(); BuildHotbar(); BuildPrompt(); BuildStatus(); BuildToast(); BuildHelp();
            if (TouchMode) BuildTouchControls();
            input.ToolChanged += RefreshHotbar;
            input.Interacted += OnInteracted;
            input.DemolishRequested += r => { if (r != BuildResult.Ok) Toast(r == BuildResult.AlreadyDemolishing ? "이미 해체 중입니다" : "해체할 수 있는 부품이 아닙니다"); };
            input.TogglePause += () => { boot.Session.Paused = !boot.Session.Paused; };
            input.ToggleSpeed += () => { boot.Session.Speed = boot.Session.Speed >= 2f ? 1f : 2f; };
            input.ToggleHelp += () => SetHelp(!help.gameObject.activeSelf);
            input.Escape += () => { if (help.gameObject.activeSelf) SetHelp(false); };
            boot.SessionReplaced += s => { s.Unlocks.Unlocked += _ => RefreshHotbar(); RefreshHotbar(); };
            boot.Session.Unlocks.Unlocked += _ => RefreshHotbar();
            RefreshHotbar();
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
            hotbar = UiKit.PanelBox("Hotbar", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(5 * (SlotW + 6f) - 6f + 16f, SlotH + 16f)).rectTransform;
            UiKit.Row(hotbar, 6f, new RectOffset(8, 8, 8, 8)).childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < 5; i++)
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
            prompt = UiKit.PanelBox("Prompt", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(170f, 32f), UiKit.Panel2).rectTransform;
            promptText = UiKit.Label("Text", prompt, "", 14, UiKit.Fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            prompt.gameObject.SetActive(false);
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
            help = UiKit.PanelBox("Help", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -60f), new Vector2(330f, 196f)).rectTransform;
            helpText = UiKit.Label("Text", help, "WASD/방향키 이동 · E 집기·놓기·설치·검사 · X 해체\n1~5 도구 선택, 같은 키 다시 누르면 빈손 · R 방향 · Q 빈손\n검사대 위 택배는 검사 전까지 멈춥니다.\n정상은 출고구, 불량은 반품구로 보내세요.\n입고 3초마다 1개 · 배송 +15 · 해체 2초 후 80% 환불", 12, UiKit.Muted, TextAnchor.UpperLeft);
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

        public void Toast(string message)
        {
            toastText.text = message;
            toastUntil = Time.unscaledTime + 1.8f;
            toast.gameObject.SetActive(true);
        }

        private void OnInteracted(InteractResult r)
        {
            switch (r)
            {
                case InteractResult.NoFloor: Toast("바닥이 없는 곳입니다"); break;
                case InteractResult.CannotPutHere: Toast("여기에는 내려놓을 수 없습니다"); break;
                case InteractResult.NoTool: Toast("도구를 고르세요 (1~5)"); break;
                case InteractResult.Locked: Toast("아직 사지 않은 부지입니다"); break;
                case InteractResult.Occupied: Toast("이미 무언가 있습니다"); break;
                case InteractResult.NotOwned: Toast("해금되지 않은 방향입니다"); break;
                case InteractResult.NotEnoughMoney: Toast("잔액이 부족합니다"); break;
                case InteractResult.Standing: Toast("서 있는 자리에는 놓을 수 없습니다"); break;
            }
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
                slotArrows[i].text = on ? Arrow(input.SelectedTool.Value.Direction) : (input.OwnedDirections(type).Count > 1 ? "R" : Arrow(input.OwnedDirections(type)[0]));
            }
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            var s = boot.Session;
            money.text = s.Economy.Money.ToString();
            delivered.text = "배송 " + s.Sim.DeliveredCount;
            lost.text = "소실 " + s.Sim.LostCount;
            waiting.text = "검사 대기 " + s.Sim.AwaitingInspectionCount;
            UiKit.SetButtonLook(pauseBtn, s.Paused);
            speedBtn.GetComponentInChildren<Text>().text = (s.Speed >= 2f ? "2x" : "1x") + " T";
            UiKit.SetButtonLook(speedBtn, s.Speed >= 2f);

            var w = s.Worker.Worker;
            workerText.text = w.Carrying != null ? ("택배 #" + w.Carrying.Id + " 운반 중 · 레일이나 도크를 보고 E")
                : input.SelectedTool.HasValue ? (Name(input.SelectedTool.Value.Type) + " " + Arrow(input.SelectedTool.Value.Direction) + " 들고 있음 · E로 설치, R로 방향")
                : "빈손 · 1~5로 도구 선택";

            // 프롬프트: Preview 결과로 문구를 만들고 바라보는 칸 위에 띄운다.
            var r = s.Worker.Preview(input.SelectedTool);
            var (fx, fy) = s.Worker.FacingTile();
            string label = null;
            switch (r)
            {
                case InteractResult.PickedUp: label = "집기"; break;
                case InteractResult.PutDown: label = "놓기"; break;
                case InteractResult.Delivered: label = s.Grid.Get(fx, fy).Type == CellType.Outlet ? "출고" : "반품"; break;
                case InteractResult.InspectionRequired: label = "검사"; break;
                case InteractResult.Placed: label = Name(input.SelectedTool.Value.Type) + " 설치"; break;
            }
            if (label != null && boot.World != null && boot.World.MainCamera != null)
            {
                var sp = boot.World.MainCamera.WorldToScreenPoint(Grid3D.Tile(fx, fy) + new Vector3(0f, 0.9f, 0f));
                var canvasRt = Root;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var lp);
                prompt.anchoredPosition = lp + canvasRt.rect.size * 0.5f;
                promptText.text = label + "   [E]";
                prompt.gameObject.SetActive(true);
            }
            else prompt.gameObject.SetActive(false);

            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
        }
    }
}
