using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 시작 화면(스펙 12.1). 부팅 직후 3D 공장 위에 왼쪽이 짙은 막과 세로 메뉴를 띄운다. 떠 있는 동안 세션은 일시정지, 게임 루프 틱·월드 입력은 멈추고 HUD는 숨긴다.
    /// 메뉴: 이어하기(저장 파일이 있을 때, 아래줄 "N일차 · ₩금액") · 새 게임 · 조작법 · 종료(데스크톱만). 인게임 "메뉴"로 열면 첫 버튼이 계속하기(세션 유지).
    /// 키보드 ↑/↓(W/S) 선택, Enter·Space·E 실행, 마우스 hover도 선택을 옮긴다.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public sealed class StartScreen : MonoBehaviour
    {
        /// <summary>부팅하자마자 띄우는가. PlayMode 테스트는 [SetUpFixture]에서 끈다.</summary>
        public static bool ShowOnBoot = true;

        public const string Version = "v0.8";
        public const string FooterText = Version + " · 에셋 Kenney (CC0) · 폰트 Noto Sans KR · Sora (OFL)";
        public const string TitleText = "PARCEL FACTORY", SubtitleText = "택배 공장 운영 시뮬레이션";
        public const float MenuWidth = 300f, MenuHeight = 52f, MenuGap = 10f, ColumnLeft = 96f;

        public enum Item { Resume, Continue, NewGame, Settings, Controls, Quit }

        public bool IsOpen { get; private set; }
        /// <summary>조작법 카드가 떠 있는가.</summary>
        public bool ControlsOpen => controls != null && controls.gameObject.activeSelf;
        /// <summary>설정 카드(스펙 13.3). 시작 화면 메뉴의 설정 또는 인게임 메뉴 → 설정으로 연다.</summary>
        public SettingsCard Settings { get; private set; }
        public bool SettingsOpen => Settings != null && Settings.IsOpen;
        /// <summary>인게임 메뉴로 열렸는가(첫 버튼이 계속하기).</summary>
        public bool InGame { get; private set; }
        /// <summary>지금 선택된 메뉴 항목.</summary>
        public Item Selected => visible.Count > 0 ? visible[selected] : Item.NewGame;
        /// <summary>미리 읽은 저장 세션(이어하기). 저장이 없거나 읽기 실패면 null.</summary>
        public GameSession SavedPreview { get; private set; }

        private GameBootstrap boot;
        private Canvas canvas;
        private RectTransform root, controls;
        private readonly Dictionary<Item, Button> buttons = new Dictionary<Item, Button>();
        private readonly List<Item> visible = new List<Item>();
        private readonly Dictionary<Item, Text> subs = new Dictionary<Item, Text>();
        private readonly Dictionary<Item, string> labels = new Dictionary<Item, string>();
        private Text continueSub, saveError;
        private GameObject hint;
        private RectTransform titleRow;
        private int selected;
        // 인게임 메뉴의 새 게임·종료는 두 번 눌러야 실행된다(진행 손실 방지). 첫 누름이 확인 상태를 켠다.
        public const float ConfirmSeconds = 3f;
        public const string ConfirmNewGameText = "한 번 더 누르면 새 게임", ConfirmQuitText = "한 번 더 누르면 종료", ConfirmSubText = "저장하지 않은 진행은 사라집니다";
        public const string SaveUnreadableText = "저장 파일을 읽을 수 없습니다";
        private Item? armed;
        private float armedUntil;
        // 마우스가 실제로 움직이기 전에는 hover로 선택을 옮기지 않는다(가만히 놓인 커서가 기본 선택을 빼앗지 않게).
        private bool hoverLive;
        private Vector2 pointerAtShow;
        private Item? hoveredItem;
        private GameSession pausedSession;
        private bool wasPaused;

        /// <summary>메뉴 버튼(테스트용). 숨겨진 항목이면 GameObject가 꺼져 있다.</summary>
        public Button ButtonFor(Item item) => buttons.TryGetValue(item, out var b) ? b : null;

        /// <summary>확인 상태(한 번 더 누르면 실행)인 항목. 없으면 null.</summary>
        public Item? Armed => armed;
        /// <summary>저장 파일이 있지만 읽을 수 없는가(메뉴 아래 안내 줄).</summary>
        public bool SaveUnreadable { get; private set; }

        /// <summary>데스크톱 플레이어 빌드에서만 종료 버튼을 보인다(에디터·모바일·웹에서는 숨김).</summary>
        public static bool QuitAvailable =>
            Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.LinuxPlayer;

        private void Start()
        {
            boot = GameBootstrap.Instance;
            Build();
            if (ShowOnBoot && !IsOpen) Show(false);
        }

        private void Build()
        {
            canvas = UiKit.MakeCanvas("StartScreen", 50);
            canvas.transform.SetParent(transform, false);
            root = canvas.GetComponent<RectTransform>();
            // 왼쪽이 짙고 오른쪽으로 옅어지는 막(3D 공장은 그대로 보인다).
            var shade = UiKit.Fill("Shade", root).gameObject.AddComponent<Image>();
            shade.sprite = UiSprites.HorizontalFade(UiKit.DarkAlpha(0.88f), UiKit.DarkAlpha(0.22f)); shade.color = new Color(8f / 255f, 11f / 255f, 13f / 255f, 1f); shade.raycastTarget = true;

            var col = UiKit.Rect("Column", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ColumnLeft, 0f), new Vector2(MenuWidth, 520f));
            var g = col.gameObject.AddComponent<VerticalLayoutGroup>();
            g.childAlignment = TextAnchor.MiddleLeft; g.spacing = 0f;
            g.childControlWidth = false; g.childControlHeight = false; g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            var fit = col.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var logo = UiKit.IconTile("Logo", col, "ui-parcel", UiKit.Accent, 56f, 32f);
            logo.sprite = UiSprites.Rounded(14);
            logo.transform.Find("Icon").GetComponent<Image>().color = UiKit.AccentFg;
            UiKit.AddShadow(logo.rectTransform, 14, UiKit.Elevation.Panel);
            Spacer(col, 22f);
            // Sora Bold 44, 자간 넓게: UGUI Text에는 자간이 없으므로 글자마다 Text를 두고 간격 3.5(0.08em)로 늘어놓는다.
            var title = titleRow = UiKit.Rect("Title", col, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(640f, 52f));
            var tg = UiKit.Row(title, 3.5f, new RectOffset(0, 0, 0, 0)); tg.childAlignment = TextAnchor.MiddleLeft;
            foreach (var ch in TitleText)
            {
                var t = UiKit.Label(ch == ' ' ? "Space" : ch.ToString(), title, ch == ' ' ? "" : ch.ToString(), 44, UiKit.Fg, TextAnchor.MiddleCenter, Fonts.NumBold);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (ch == ' ') UiKit.Size(t, 8f, 52f);
            }
            Spacer(col, 6f);
            var sub = UiKit.Label("Subtitle", col, SubtitleText, 16, UiKit.Muted, TextAnchor.MiddleLeft);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            sub.rectTransform.sizeDelta = new Vector2(MenuWidth, 24f);
            Spacer(col, 36f);

            var menu = UiKit.Rect("Menu", col, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(MenuWidth, 5f * MenuHeight + 4f * MenuGap));
            var mg = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            mg.spacing = MenuGap; mg.childAlignment = TextAnchor.UpperLeft;
            mg.childControlWidth = true; mg.childControlHeight = true; mg.childForceExpandWidth = true; mg.childForceExpandHeight = false;
            var mfit = menu.gameObject.AddComponent<ContentSizeFitter>(); mfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            MenuButton(menu, Item.Resume, "계속하기", "ui-play", Resume);
            MenuButton(menu, Item.Continue, "이어하기", "ui-load", Continue);
            MenuButton(menu, Item.NewGame, "새 게임", "ui-new", NewGame);
            MenuButton(menu, Item.Settings, "설정", "ui-settings", OpenSettings);
            MenuButton(menu, Item.Controls, "조작법", "ui-help", () => SetControls(true));
            MenuButton(menu, Item.Quit, "종료", "ui-exit", Quit);
            // 이어하기는 버튼 안 아래줄에 "N일차 · ₩금액".
            continueSub = subs[Item.Continue];
            Spacer(col, 10f);
            saveError = UiKit.Label("SaveError", col, SaveUnreadableText, 12, UiKit.Muted, TextAnchor.MiddleLeft);
            saveError.horizontalOverflow = HorizontalWrapMode.Overflow;
            saveError.rectTransform.sizeDelta = new Vector2(MenuWidth, 18f);
            saveError.gameObject.SetActive(false);

            var footer = UiKit.Label("Footer", root, FooterText, 11, UiKit.Muted, TextAnchor.LowerLeft);
            footer.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Place(footer.rectTransform, Vector2.zero, Vector2.zero, new Vector2(96f, 28f), new Vector2(600f, 16f));
            var hintRt = UiKit.Rect("Hint", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 26f), new Vector2(200f, 20f));
            var hg = UiKit.Row(hintRt, 8f, new RectOffset(0, 0, 0, 0)); hg.childAlignment = TextAnchor.MiddleRight;
            UiKit.Keycap(hintRt, "Enter");
            var ht = UiKit.Label("Text", hintRt, "시작", 12, UiKit.Muted, TextAnchor.MiddleLeft); ht.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint = hintRt.gameObject;

            BuildControls();
            Settings = SettingsCard.Build(root);
            root.gameObject.SetActive(false);
        }

        private static void Spacer(Transform parent, float h) => UiKit.Rect("Gap", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1f, h));

        private void MenuButton(Transform parent, Item item, string label, string icon, System.Action onClick)
        {
            var b = UiKit.Btn(item.ToString(), parent, label, onClick, icon: icon, height: MenuHeight, fontSize: 16, font: Fonts.Bold, padX: 20, radius: 12,
                elevation: UiKit.Elevation.Panel, align: TextAnchor.MiddleLeft, iconSize: 18f);
            var ui = b.GetComponent<UiButton>();
            ui.Layout.spacing = 14f;
            // 선택 링: 시안 focus-visible(앰버 2px, 바깥 2px 띄움).
            ui.Focus = UiKit.AddBorder((RectTransform)b.transform, 12, UiKit.Accent, 2f, 4f, "Focus");
            ui.Focus.gameObject.SetActive(false);
            ui.Hovered += () => { hoveredItem = item; if (!hoverLive) return; int i = visible.IndexOf(item); if (i >= 0) Select(i); };
            // 라벨 + 아래줄(이어하기 "N일차 · ₩금액", 확인 상태 경고). 아래줄은 글자가 있을 때만 보인다.
            var lines = UiKit.Rect("Lines", b.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(200f, 40f));
            var lg = lines.gameObject.AddComponent<VerticalLayoutGroup>();
            lg.childAlignment = TextAnchor.MiddleLeft; lg.spacing = 0f; lg.childControlWidth = true; lg.childControlHeight = true; lg.childForceExpandWidth = false; lg.childForceExpandHeight = false;
            ui.Label.transform.SetParent(lines, false);
            ui.Label.alignment = TextAnchor.MiddleLeft;
            var sub = UiKit.Label("Sub", lines, "", 12, UiKit.Muted, TextAnchor.MiddleLeft);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            sub.gameObject.SetActive(item == Item.Continue);
            subs[item] = sub;
            labels[item] = label;
            buttons[item] = b;
        }

        /// <summary>조작법 카드(인게임 조작 서랍과 같은 내용). Esc나 닫기로 돌아온다.</summary>
        private void BuildControls()
        {
            controls = UiKit.ModalBackdrop("Controls", root).rectTransform;
            const float w = 440f, top = 18f + 22f + 12f;
            var card = UiKit.Card("Card", controls, new Vector2(w, 400f), "조작법");
            var help = UiKit.Label("Text", card, HudController.HelpText, 12, UiKit.Muted, TextAnchor.UpperLeft);
            help.supportRichText = true; help.lineSpacing = 1.15f;
            UiKit.Top(help.rectTransform, top, 300f, 18f, 18f);
            float th = Mathf.Ceil(help.preferredHeight) + 4f;
            UiKit.Top(help.rectTransform, top, th, 18f, 18f);
            card.sizeDelta = new Vector2(w, top + th + 16f + 34f + 16f);
            var close = UiKit.Btn("Close", card, "닫기", () => SetControls(false), key: "Esc", fit: true);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(80f, 34f));
            controls.gameObject.SetActive(false);
        }

        private void SetControls(bool open)
        {
            if (controls != null) controls.gameObject.SetActive(open);
        }

        /// <summary>설정 카드를 연다(메뉴의 설정).</summary>
        public void OpenSettings() { if (Settings != null) Settings.Open(SettingsFreeLeft()); }

        /// <summary>설정 카드를 놓을 빈 영역의 왼쪽 끝: 메뉴 열(왼쪽 96 + 제목·메뉴 중 넓은 쪽) + 48.</summary>
        private float SettingsFreeLeft()
        {
            float w = MenuWidth;
            if (titleRow != null) w = Mathf.Max(w, LayoutUtility.GetPreferredWidth(titleRow));
            return ColumnLeft + w + 48f;
        }

        /// <summary>시작 화면을 띄운다. inGame이면 첫 버튼이 계속하기(현재 세션 재개), 아니면 이어하기/새 게임.</summary>
        public void Show(bool inGame)
        {
            if (boot == null) boot = GameBootstrap.Instance;
            if (root == null) Build();
            InGame = inGame;
            if (!IsOpen)
            {
                pausedSession = boot.Session;
                wasPaused = pausedSession != null && pausedSession.Paused;
                if (pausedSession != null) pausedSession.Paused = true;
            }
            IsOpen = true;
            if (boot.Input != null) { boot.Input.CancelConfig(); boot.Input.ClearPad(); boot.Input.SetBlock("start", true); }
            if (boot.Panels != null) boot.Panels.CloseAll();
            if (boot.Hud != null) { boot.Hud.CloseHelp(); boot.Hud.SetVisible(false); }
            RefreshMenu();
            SetControls(false);
            if (Settings != null) Settings.Close();
            if (hint != null) hint.SetActive(boot.Hud == null || !boot.Hud.TouchMode);
            hoverLive = false; hoveredItem = null;
            pointerAtShow = Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
            root.gameObject.SetActive(true);
        }

        /// <summary>시작 화면을 닫는다. 연 세션이 그대로면 원래 일시정지 상태로 되돌린다(새 게임·이어하기는 새 세션이라 그대로 진행).</summary>
        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (boot.Session != null && boot.Session == pausedSession) boot.Session.Paused = wasPaused;
            pausedSession = null;
            if (boot.Input != null) boot.Input.SetBlock("start", false);
            if (boot.Hud != null) boot.Hud.SetVisible(true);
            SetControls(false);
            if (Settings != null) Settings.Close();
            if (root != null) root.gameObject.SetActive(false);
        }

        /// <summary>메뉴 항목을 다시 정한다: 저장 파일을 미리 읽어 이어하기 아래줄을 채우고, 읽을 수 없으면 숨긴다.</summary>
        private void RefreshMenu()
        {
            Disarm();
            SavedPreview = null;
            SaveUnreadable = false;
            if (!InGame && System.IO.File.Exists(SaveFile.Path))
            {
                if (SaveFile.TryLoad(out var saved, out _)) SavedPreview = saved;
                else SaveUnreadable = true;
            }
            saveError.gameObject.SetActive(SaveUnreadable);
            visible.Clear();
            if (InGame) visible.Add(Item.Resume);
            else if (SavedPreview != null) visible.Add(Item.Continue);
            visible.Add(Item.NewGame);
            visible.Add(Item.Settings);
            visible.Add(Item.Controls);
            if (QuitAvailable) visible.Add(Item.Quit);
            foreach (var kv in buttons) kv.Value.gameObject.SetActive(visible.Contains(kv.Key));
            if (SavedPreview != null) continueSub.text = SaveSummary(SavedPreview);
            for (int i = 0; i < visible.Count; i++) Restyle(visible[i]);
            Select(0);
        }

        /// <summary>이어하기 아래줄: "3일차 · ₩1,240".</summary>
        public static string SaveSummary(GameSession s) => s.Clock.Day + "일차 · ₩" + s.Economy.Money.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>항목 기본 모양: 첫 항목은 앰버(primary), 나머지는 보통. 아래줄 색도 바탕에 맞춘다.</summary>
        private void Restyle(Item item)
        {
            bool first = visible.Count > 0 && visible[0] == item;
            UiKit.SetButtonStyle(buttons[item], first ? BtnStyle.Primary : BtnStyle.Normal);
            subs[item].color = first ? new Color(UiKit.AccentFg.r, UiKit.AccentFg.g, UiKit.AccentFg.b, 0.72f) : UiKit.Muted;
        }

        /// <summary>새 게임·종료의 확인 상태를 켠다: 위험(빨강) 모양, "한 번 더 누르면 …", 아래줄 경고. ConfirmSeconds 뒤에 풀린다.</summary>
        private void Arm(Item item)
        {
            Disarm();
            armed = item;
            armedUntil = Time.unscaledTime + ConfirmSeconds;
            UiKit.SetButtonText(buttons[item], item == Item.NewGame ? ConfirmNewGameText : ConfirmQuitText);
            UiKit.SetButtonStyle(buttons[item], BtnStyle.Danger);
            var sub = subs[item];
            sub.text = ConfirmSubText;
            sub.color = new Color(1f, 1f, 1f, 0.78f);
            sub.gameObject.SetActive(true);
        }

        private void Disarm()
        {
            if (!armed.HasValue) return;
            var item = armed.Value;
            armed = null;
            UiKit.SetButtonText(buttons[item], labels[item]);
            subs[item].text = "";
            subs[item].gameObject.SetActive(item == Item.Continue);
            if (visible.Contains(item)) Restyle(item);
        }

        private void Select(int i)
        {
            if (visible.Count == 0) return;
            selected = (i % visible.Count + visible.Count) % visible.Count;
            if (armed.HasValue && visible[selected] != armed.Value) Disarm(); // 선택을 옮기면 확인 상태를 푼다
            for (int k = 0; k < visible.Count; k++) buttons[visible[k]].GetComponent<UiButton>().SetFocus(k == selected);
        }

        /// <summary>선택을 delta칸 옮긴다(↑ = −1, ↓ = +1, 끝에서 돌아간다).</summary>
        public void MoveSelection(int delta) => Select(selected + delta);

        /// <summary>선택된 항목을 실행한다(Enter·Space·E).</summary>
        public void Activate() { if (visible.Count > 0) buttons[visible[selected]].onClick.Invoke(); }

        private void Resume() => Hide();

        private void Continue()
        {
            var s = SavedPreview;
            if (s == null && !SaveFile.TryLoad(out s, out _)) { RefreshMenu(); return; }
            s.Paused = false; // 일시정지 상태로 저장된 세션이어도 이어하기는 바로 진행한다
            boot.ReplaceSession(s);
            Hide();
            if (boot.Hud != null) boot.Hud.Toast("불러왔습니다", HudController.ToastKind.Good);
        }

        private void NewGame()
        {
            if (InGame && armed != Item.NewGame) { Arm(Item.NewGame); return; }
            Disarm();
            boot.StartNewGame();
            Hide();
        }

        private void Quit()
        {
            if (InGame && armed != Item.Quit) { Arm(Item.Quit); return; }
            Disarm();
            Application.Quit();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (armed.HasValue && Time.unscaledTime > armedUntil) Disarm();
            if (!hoverLive && Pointer.current != null && (Pointer.current.position.ReadValue() - pointerAtShow).sqrMagnitude > 4f)
            {
                hoverLive = true; // 커서가 움직였다: 지금 올라가 있는 버튼부터 hover 선택을 받는다
                if (hoveredItem.HasValue && buttons[hoveredItem.Value].GetComponent<UiButton>().Hover) { int i = visible.IndexOf(hoveredItem.Value); if (i >= 0) Select(i); }
            }
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool esc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool enter = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame); // 카드에서는 게임패드 A도 적용
            bool go = enter || (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            HandleKeys(up, down, left, right, SettingsOpen ? enter : go, esc);
        }

        /// <summary>
        /// 키 입력 한 프레임. Esc 우선순위: 설정 카드 → 조작법 카드 → (인게임이면) 계속하기. 카드가 열려 있으면 Esc는 그 카드만 닫는다.
        /// 설정 카드가 열려 있으면 ↑/↓/←/→/Enter도 카드로 간다.
        /// </summary>
        public void HandleKeys(bool up, bool down, bool left, bool right, bool go, bool esc)
        {
            if (!IsOpen) return;
            if (SettingsOpen) { Settings.HandleKeys(up, down, left, right, go, esc); return; }
            if (ControlsOpen) { if (esc) SetControls(false); return; }
            if (up) MoveSelection(-1);
            else if (down) MoveSelection(1);
            else if (go) Activate();
            else if (esc && InGame) Resume(); // 인게임 메뉴는 Esc로도 돌아간다
        }
    }
}
