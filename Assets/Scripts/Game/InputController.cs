using System;
using System.Collections.Generic;
using System.Linq;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ParcelFactory.Game
{
    /// <summary>설정 입력 단계(스펙 11.8). None = 평소, PickSlot = 1(파랑)/2(빨강) 고르기, PickDirection = 화살표 입력 대기.</summary>
    public enum ConfigMode { None, PickSlot, PickDirection }
    /// <summary>설정 대상: 손에 든 도구, 또는 바라보는 칸에 설치된 부품.</summary>
    public enum ConfigTarget { Tool, Cell }
    /// <summary>설정이 둘인 부품(분배기·팔레트)에서 어느 쪽인가. Primary = 파랑, Secondary = 빨강.</summary>
    public enum ConfigWhich { Primary, Secondary }

    /// <summary>키보드·게임패드·화면 버튼 입력을 코어 호출로 바꾼다. 핫바는 부품 종류당 한 칸(키 1~6).
    /// 방향은 순환하지 않는다(스펙 11.8): R → 화살표, 분배기는 R → 1/2 → 화살표, 팔레트는 R → 1(파랑)/2(빨강).
    /// R을 누를 때 바라보는 칸에 설치된 부품이 있으면 그 부품을 그 자리에서 바꾸고, 아니면 손에 든 도구를 바꾼다.
    /// 팔레트 도구는 방향 대신 종류: East = 파랑, West = 빨강(Tool.KindOf).</summary>
    public sealed class InputController : MonoBehaviour
    {
        public static readonly CellType[] SlotOrder = { CellType.Conveyor, CellType.FastConveyor, CellType.Pallet, CellType.Table, CellType.Splitter, CellType.Scanner };
        public static int SlotCount => SlotOrder.Length;

        public Tool? SelectedTool { get; private set; }
        public int SelectedSlot => SelectedTool.HasValue ? AvailableSlots().IndexOf(SelectedTool.Value.Type) : -1;
        private readonly HashSet<string> blockers = new HashSet<string>();
        /// <summary>UI가 월드 입력을 막고 있는가. 차단 주체(조작 서랍, 패널 등)가 하나라도 있으면 true.</summary>
        public bool UiBlocksInput => blockers.Count > 0;

        public event Action<InteractResult> Interacted;
        public event Action<BuildResult> DemolishRequested;
        public event Action TogglePause, ToggleSpeed, ToggleShop, ToggleStatus, ToggleHelp, Escape, ToolChanged;

        private InputAction move, interact, demolish, rotate, clear, pause, speed, shop, status, help, escape, runAction;
        private readonly InputAction[] slots = new InputAction[SlotOrder.Length];
        private readonly Dictionary<CellType, Direction> lastDir = new Dictionary<CellType, Direction>();
        private readonly HashSet<Direction> padHeld = new HashSet<Direction>(); // 화면 D-패드: 누르고 있는 방향
        private bool runHeld; // 화면 "달리기" 버튼을 누르고 있는가
        private const float MoveSubStep = 0.05f;

        // 설정 입력 상태(스펙 11.8)
        public ConfigMode ConfigMode { get; private set; }
        public ConfigWhich ConfigWhich { get; private set; }
        private ConfigTarget configTarget;
        public bool ConfigTargetIsCell => ConfigMode != ConfigMode.None && configTarget == ConfigTarget.Cell;
        /// <summary>대상 칸(ConfigTargetIsCell일 때만 의미가 있다).</summary>
        public (int x, int y) ConfigCell { get; private set; }
        /// <summary>대상 부품 종류(도구면 도구 종류, 칸이면 그 칸의 종류).</summary>
        public CellType ConfigType { get; private set; }
        /// <summary>분배기 도구로 설치할 때 함께 적용할 빨간 출구(없으면 자동 규칙).</summary>
        public Direction? ToolSplitterRed { get; private set; }
        public event Action ConfigChanged;
        private readonly HashSet<Direction> prevDirs = new HashSet<Direction>(); // 지난 프레임에 눌려 있던 방향(키·스틱·D-패드 합산 벡터의 축별)
        private readonly HashSet<Direction> curDirs = new HashSet<Direction>();
        private bool holdUntilRelease; // 화살표로 설정을 끝낸 뒤 그 키를 뗄 때까지 걷지 않는다

        private void Awake()
        {
            move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/a").With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");
            move.AddBinding("<Gamepad>/dpad");
            interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth");
            demolish = Button("Demolish", "<Keyboard>/x", "<Gamepad>/buttonWest");
            rotate = Button("Rotate", "<Keyboard>/r", "<Gamepad>/buttonNorth");
            clear = Button("Clear", "<Keyboard>/q", "<Gamepad>/buttonEast");
            pause = Button("Pause", "<Keyboard>/p", "<Gamepad>/start");
            speed = Button("Speed", "<Keyboard>/t", null);
            shop = Button("Shop", "<Keyboard>/b", "<Gamepad>/select");
            status = Button("Status", "<Keyboard>/c", null);
            help = Button("Help", "<Keyboard>/o", null);
            escape = Button("Escape", "<Keyboard>/escape", null);
            runAction = Button("Run", "<Keyboard>/space", "<Gamepad>/leftTrigger");
            runAction.AddBinding("<Gamepad>/leftShoulder");
            for (int i = 0; i < slots.Length; i++) slots[i] = Button("Slot" + (i + 1), "<Keyboard>/" + (i + 1), null);
        }

        private static InputAction Button(string name, string key, string pad)
        {
            var a = new InputAction(name, InputActionType.Button, key);
            if (pad != null) a.AddBinding(pad);
            return a;
        }

        private void OnEnable()
        {
            foreach (var a in All()) a.Enable();
            // macOS 한글 입력기 등 IME가 켜져 있으면 조합 중인 키가 Input System에 닿지 않는다. 게임 중에는 IME를 끈다.
            // 바인딩은 모두 물리 키 경로(<Keyboard>/w, <Keyboard>/1 …)라 입력 소스와 상관없이 같은 키 자리로 동작한다.
            DisableIme(Keyboard.current);
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var a in All()) a.Disable();
        }

        private static void DisableIme(Keyboard kb) => kb?.SetIMEEnabled(false);

        /// <summary>키보드가 새로 붙거나 다시 연결되면 IME 끄기를 다시 적용한다.</summary>
        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is Keyboard kb && (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled))
                DisableIme(kb);
        }

        private void OnDestroy()
        {
            foreach (var a in All()) a.Dispose();
        }

        private IEnumerable<InputAction> All()
        {
            yield return move; yield return interact; yield return demolish; yield return rotate; yield return clear;
            yield return pause; yield return speed; yield return shop; yield return status; yield return help; yield return escape; yield return runAction;
            foreach (var s in slots) yield return s;
        }

        private GameSession S => GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;

        /// <summary>달리기 입력(스페이스·LT/LB 유지 또는 화면 버튼)을 누르고 있는가. 실제로 달리는지는 코어가 체력으로 판단한다(Worker.IsRunning).</summary>
        public bool RunRequested => (runAction != null && runAction.IsPressed()) || runHeld;

        private void Update()
        {
            if (S == null) return;
            if (escape.WasPressedThisFrame()) PressEscape();
            // 상점·상태창이 유일하게 열려 있으면 B·C 키가 닫기(또는 서로 바꾸기) 토글로 동작해야 하므로 차단 검사보다 먼저 읽는다.
            var panels = GameBootstrap.Instance.Panels;
            bool toggleable = !UiBlocksInput || (panels != null && (panels.ShopIsOnlyOpen || panels.StatusIsOnlyOpen));
            if (shop.WasPressedThisFrame() && toggleable) { CancelConfig(); ToggleShop?.Invoke(); }
            else if (status.WasPressedThisFrame() && toggleable) { CancelConfig(); ToggleStatus?.Invoke(); }
            if (UiBlocksInput) { CancelConfig(); StopMoving(); prevDirs.Clear(); return; }
            if (pause.WasPressedThisFrame()) TogglePause?.Invoke();
            if (speed.WasPressedThisFrame()) ToggleSpeed?.Invoke();
            if (help.WasPressedThisFrame()) ToggleHelp?.Invoke();

            var v = move.ReadValue<Vector2>();
            foreach (var d in padHeld) v += new Vector2(d.Dx(), d.Dy());
            bool moveActive = v.sqrMagnitude > 0.04f;
            // 이번 프레임에 새로 눌린 방향(지난 프레임에 없던 축 방향). 설정 모드의 방향 입력에 쓴다.
            curDirs.Clear();
            if (v.x > AxisPress) curDirs.Add(Direction.East); else if (v.x < -AxisPress) curDirs.Add(Direction.West);
            if (v.y > AxisPress) curDirs.Add(Direction.North); else if (v.y < -AxisPress) curDirs.Add(Direction.South);
            Direction? pressed = null;
            foreach (var d in curDirs) if (!prevDirs.Contains(d)) { pressed = d; break; }
            prevDirs.Clear(); prevDirs.UnionWith(curDirs);

            if (ConfigMode != ConfigMode.None) { UpdateConfig(pressed); return; }
            if (!moveActive) holdUntilRelease = false;

            for (int i = 0; i < slots.Length; i++) if (slots[i].WasPressedThisFrame()) SelectSlot(i);
            if (rotate.WasPressedThisFrame()) BeginConfig();
            if (ConfigMode != ConfigMode.None) { StopMoving(); return; } // 1~6 재탭·R로 방금 설정 모드에 들어갔다
            if (clear.WasPressedThisFrame()) ClearTool();
            if (interact.WasPressedThisFrame()) Interact();
            if (demolish.WasPressedThisFrame()) Demolish();

            // 퇴근 후에는 걷지 않는다(일시정지 중에는 걸을 수 있다: 일시정지 중 건설 허용)
            if (!S.Clock.IsOpen || !moveActive || holdUntilRelease) { StopMoving(); return; }
            bool run = RunRequested;
            for (float left = Time.deltaTime; left > 0f; left -= MoveSubStep) S.Worker.Move(v.x, v.y, Mathf.Min(left, MoveSubStep), run); // 서브스텝: 프레임 스파이크에도 1칸 장애물을 뚫지 않는다. 코어가 정규화·충돌·Facing·CancelInspection·달리기 판정을 처리한다
        }

        /// <summary>합산 이동 벡터의 축 성분이 이 값을 넘으면 그 방향이 눌린 것으로 본다.</summary>
        private const float AxisPress = 0.3f;

        /// <summary>설정 모드의 한 프레임: 이동은 막고, 새로 눌린 방향(이미 다른 방향을 누르고 있어도 새로 더해진 쪽)으로 방향을, 1/2로 설정을 고른다.
        /// Esc·E·X·Q·숫자(PickSlot의 1/2 제외)는 취소.</summary>
        private void UpdateConfig(Direction? pressed)
        {
            StopMoving();
            if (interact.WasPressedThisFrame() || demolish.WasPressedThisFrame() || clear.WasPressedThisFrame()) { CancelConfig(); return; } // 취소만 한다(설치·해체·빈손은 하지 않음)
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].WasPressedThisFrame()) { SelectSlot(i); return; }
            if (rotate.WasPressedThisFrame()) { CancelConfig(); return; } // R을 다시 누르면 취소
            if (ConfigMode != ConfigMode.PickDirection || !pressed.HasValue) return;
            holdUntilRelease = true; // 입력한 화살표를 누른 채라도 걸어가지 않게
            ApplyConfigDirection(pressed.Value);
        }

        /// <summary>이동하지 않는 프레임: 영벡터 Move로 코어의 IsRunning만 끈다(멈추면 달리기 즉시 해제, 스펙 11.4). 위치·방향·체력은 바뀌지 않는다.</summary>
        private void StopMoving() => S.Worker.Move(0f, 0f, 0f);

        /// <summary>차단 주체별로 입력 차단을 켜고 끈다. 서로 다른 UI가 서로의 차단을 풀어버리지 않는다.</summary>
        public void SetBlock(string owner, bool on)
        {
            if (on) blockers.Add(owner); else blockers.Remove(owner);
        }

        /// <summary>Esc(스펙 11.9): ① 설정 모드 취소(도구 선택은 유지) → ② 열린 패널·조작 서랍 닫기(하루 요약은 모달이라 그대로) → ③ 손에 든 도구 내려놓기(빈손, Q와 같음).
        /// 한 번에 한 단계만 처리한다.</summary>
        public void PressEscape()
        {
            if (ConfigMode != ConfigMode.None) { CancelConfig(); return; }
            var panels = GameBootstrap.Instance != null ? GameBootstrap.Instance.Panels : null;
            if (UiBlocksInput || (panels != null && panels.AnyOpen)) { Escape?.Invoke(); return; }
            if (!SelectedTool.HasValue) return;
            ClearTool();
            Feedback(EmptyHandMessage);
        }

        public const string EmptyHandMessage = "빈손";

        public void RequestShop() => ToggleShop?.Invoke();
        public void RequestStatus() => ToggleStatus?.Invoke();
        public void RequestHelp() => ToggleHelp?.Invoke();

        /// <summary>화면 D-패드용. 누르는 동안 held=true, 떼면 false. 이동 자체는 Update에서 매 프레임 적용된다.</summary>
        public void SetPad(Direction d, bool held)
        {
            if (held) padHeld.Add(d); else padHeld.Remove(d);
        }

        /// <summary>화면 "달리기" 버튼용. 누르는 동안 held=true, 떼면 false.</summary>
        public void SetRun(bool held) => runHeld = held;

        /// <summary>화면 D-패드·달리기 입력을 모두 해제한다. 패널이 열릴 때 이동을 멈추는 데 쓴다.</summary>
        public void ClearPad() { padHeld.Clear(); runHeld = false; }

        private void OnApplicationFocus(bool focus) { if (!focus) ClearPad(); }

        public void Interact()
        {
            if (S == null) return;
            if (ConfigMode != ConfigMode.None) { CancelConfig(); return; } // 설정 중 E(화면 버튼 포함)는 취소
            var tool = SelectedTool;
            var (fx, fy) = S.Worker.FacingTile();
            var r = S.Worker.Interact(tool);
            // 분배기 도구에 빨간 출구를 정해 두었으면 설치 직후 그 칸에 적용한다(파랑과 같으면 코어가 거절 → 자동 규칙).
            if (r == InteractResult.Placed && tool.HasValue && tool.Value.Type == CellType.Splitter && ToolSplitterRed.HasValue)
                S.Build.SetSplitterRed(fx, fy, ToolSplitterRed.Value);
            Interacted?.Invoke(r);
        }

        public void Demolish()
        {
            if (S == null) return;
            if (ConfigMode != ConfigMode.None) { CancelConfig(); return; } // 설정 중 X(화면 버튼 포함)는 취소
            var panels = GameBootstrap.Instance.Panels;
            if (panels != null) panels.Ask();
            else DemolishRequested?.Invoke(S.Worker.TryDemolishFacing());
        }

        /// <summary>보유 방향이 하나라도 있는 종류만 슬롯이 된다.</summary>
        public List<CellType> AvailableSlots()
        {
            var owned = S.Toolbar.OwnedTools().ToList();
            return SlotOrder.Where(t => owned.Any(o => o.Type == t)).ToList();
        }

        public List<Direction> OwnedDirections(CellType t) =>
            S.Toolbar.OwnedTools().Where(o => o.Type == t).Select(o => o.Direction).ToList();

        /// <summary>그 종류를 고를 때 쓸 방향(마지막으로 고른 방향, 없으면 첫 보유 방향). 팔레트면 종류(East=파랑).</summary>
        public Direction PreferredDirection(CellType type)
        {
            var dirs = OwnedDirections(type);
            if (dirs.Count == 0) return Direction.East;
            return lastDir.TryGetValue(type, out var d) && dirs.Contains(d) ? d : dirs[0];
        }

        /// <summary>핫바 슬롯 선택(키 1~6·화면 탭). 설정 모드에서는: PickSlot이면 1·2가 선택지(파랑/빨강), 그 밖에는 설정을 취소하고 그 슬롯을 고른다
        /// (이미 고른 슬롯이면 취소만 하고 설정 모드에 다시 들어가지 않는다). 평소에 이미 고른 슬롯을 다시 고르면 설정 모드에 들어간다(= R).</summary>
        public void SelectSlot(int i)
        {
            if (ConfigMode == ConfigMode.PickSlot && (i == 0 || i == 1)) { ApplyConfigSlot(i + 1); return; }
            bool wasConfig = ConfigMode != ConfigMode.None;
            CancelConfig();
            var available = AvailableSlots();
            if (i < 0 || i >= available.Count) return;
            var type = available[i];
            if (SelectedTool.HasValue && SelectedTool.Value.Type == type) { if (!wasConfig) BeginConfig(); return; }
            ToolSplitterRed = null; // 다른 종류를 고르면 분배기 도구의 빨간 출구 설정은 버린다
            var dir = PreferredDirection(type);
            SelectedTool = new Tool(type, dir);
            ToolChanged?.Invoke();
        }

        public void ClearTool()
        {
            CancelConfig();
            SelectedTool = null;
            ToolSplitterRed = null;
            ToolChanged?.Invoke();
        }

        /// <summary>설정할 수 있는 부품(레일·고속·검사대·자동 검사기·분배기·팔레트). 입고구·트럭 도크는 바꿀 수 없다.</summary>
        public static bool IsConfigurable(CellType t) =>
            t == CellType.Conveyor || t == CellType.FastConveyor || t == CellType.Table || t == CellType.Scanner || t == CellType.Splitter || t == CellType.Pallet;

        /// <summary>설정이 둘인 부품: 먼저 1(파랑)/2(빨강)을 고른다.</summary>
        public static bool HasTwoSettings(CellType t) => t == CellType.Splitter || t == CellType.Pallet;

        /// <summary>R: 바라보는 칸에 설정 가능한 부품이 있으면 그 칸, 아니면 손에 든 도구를 대상으로 설정 모드에 들어간다.</summary>
        public void BeginConfig()
        {
            if (S == null) return;
            var (fx, fy) = S.Worker.FacingTile();
            if (S.Grid.InBounds(fx, fy) && IsConfigurable(S.Grid.Get(fx, fy).Type))
            {
                configTarget = ConfigTarget.Cell;
                ConfigCell = (fx, fy);
                ConfigType = S.Grid.Get(fx, fy).Type;
            }
            else if (SelectedTool.HasValue)
            {
                configTarget = ConfigTarget.Tool;
                ConfigType = SelectedTool.Value.Type;
            }
            else { Feedback("설정할 대상이 없습니다"); return; }
            ConfigWhich = ConfigWhich.Primary;
            ConfigMode = HasTwoSettings(ConfigType) ? ConfigMode.PickSlot : ConfigMode.PickDirection;
            ConfigChanged?.Invoke();
        }

        /// <summary>PickSlot에서 1(파랑) 또는 2(빨강). 팔레트는 곧바로 적용하고 끝, 분배기는 방향 입력으로 넘어간다.</summary>
        public void ApplyConfigSlot(int n)
        {
            if (ConfigMode != ConfigMode.PickSlot || (n != 1 && n != 2)) return;
            ConfigWhich = n == 1 ? ConfigWhich.Primary : ConfigWhich.Secondary;
            if (ConfigType != CellType.Pallet) { ConfigMode = ConfigMode.PickDirection; ConfigChanged?.Invoke(); return; }
            var kind = n == 1 ? PalletKind.Blue : PalletKind.Red;
            if (configTarget == ConfigTarget.Tool) SetToolDirection(kind == PalletKind.Blue ? Direction.East : Direction.West);
            else
            {
                var r = S.Build.SetPalletKind(ConfigCell.x, ConfigCell.y, kind);
                Feedback(r == ReconfigureResult.Ok ? "팔레트 " + HudController.KindName(kind) : ResultMessage(r), r == ReconfigureResult.Ok);
            }
            EndConfig();
        }

        /// <summary>PickDirection에서 방향 입력. 적용하고 설정 모드를 끝낸다.</summary>
        public void ApplyConfigDirection(Direction d)
        {
            if (ConfigMode != ConfigMode.PickDirection || S == null) return;
            bool red = ConfigType == CellType.Splitter && ConfigWhich == ConfigWhich.Secondary;
            if (configTarget == ConfigTarget.Tool)
            {
                if (!SelectedTool.HasValue || SelectedTool.Value.Type != ConfigType) { EndConfig(); return; }
                if (red)
                {
                    if (d == SelectedTool.Value.Direction) Feedback(SameAsBlueMessage);
                    else { ToolSplitterRed = d; ToolChanged?.Invoke(); }
                }
                else if (!S.Toolbar.Owns(ConfigType, d)) Feedback("해금되지 않은 방향입니다");
                else SetToolDirection(d);
            }
            else
            {
                var (x, y) = ConfigCell;
                var r = red ? S.Build.SetSplitterRed(x, y, d) : S.Build.SetDirection(x, y, d);
                Feedback(r == ReconfigureResult.Ok ? (red ? "빨간 출구 " : "방향 변경 ") + HudController.Arrow(d) : ResultMessage(r), r == ReconfigureResult.Ok);
            }
            EndConfig();
        }

        /// <summary>설정 모드를 끝낸다(아무것도 바꾸지 않음). 설정 모드가 아니면 아무 일도 없다.</summary>
        public void CancelConfig()
        {
            if (ConfigMode == ConfigMode.None) return;
            EndConfig();
        }

        private void EndConfig()
        {
            ConfigMode = ConfigMode.None;
            ConfigWhich = ConfigWhich.Primary;
            ConfigChanged?.Invoke();
        }

        private void SetToolDirection(Direction d)
        {
            var t = SelectedTool.Value;
            lastDir[t.Type] = d;
            SelectedTool = new Tool(t.Type, d);
            if (t.Type == CellType.Splitter && ToolSplitterRed == d) ToolSplitterRed = null; // 파랑과 같아진 빨강은 의미가 없다
            ToolChanged?.Invoke();
        }

        public const string SameAsBlueMessage = "파란 출구와 같은 방향입니다";

        /// <summary>재설정 실패 문구.</summary>
        private string ResultMessage(ReconfigureResult r)
        {
            switch (r)
            {
                case ReconfigureResult.NotOwned: return "해금되지 않은 방향입니다";
                case ReconfigureResult.Invalid:
                    var (x, y) = ConfigCell;
                    if (S.Grid.InBounds(x, y) && S.Grid.Get(x, y).Demolishing) return "해체 중입니다";
                    return ConfigType == CellType.Splitter && ConfigWhich == ConfigWhich.Secondary ? SameAsBlueMessage : "같은 방향으로는 설정할 수 없습니다";
                default: return "설정할 수 없는 대상입니다";
            }
        }

        private static void Feedback(string message, bool good = false)
        {
            var hud = GameBootstrap.Instance != null ? GameBootstrap.Instance.Hud : null;
            if (hud != null) hud.Toast(message, good ? HudController.ToastKind.Good : HudController.ToastKind.Info);
        }
    }
}
