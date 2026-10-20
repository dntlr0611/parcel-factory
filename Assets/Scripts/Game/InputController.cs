using System;
using System.Collections.Generic;
using System.Linq;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ParcelFactory.Game
{
    /// <summary>키보드·게임패드·화면 버튼 입력을 코어 호출로 바꾼다. 핫바는 부품 종류당 한 칸(키 1~6), 방향은 R로 순환.
    /// 팔레트는 방향 대신 종류: East = 파랑, West = 빨강(Tool.KindOf)이므로 같은 순환이 파랑↔빨강이 된다.</summary>
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
        public event Action TogglePause, ToggleSpeed, ToggleShop, ToggleHelp, Escape, ToolChanged;

        private InputAction move, interact, demolish, rotate, clear, pause, speed, shop, help, escape;
        private readonly InputAction[] slots = new InputAction[SlotOrder.Length];
        private readonly Dictionary<CellType, Direction> lastDir = new Dictionary<CellType, Direction>();
        private readonly HashSet<Direction> padHeld = new HashSet<Direction>(); // 화면 D-패드: 누르고 있는 방향
        private const float MoveSubStep = 0.05f;

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
            help = Button("Help", "<Keyboard>/o", null);
            escape = Button("Escape", "<Keyboard>/escape", null);
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
            yield return pause; yield return speed; yield return shop; yield return help; yield return escape;
            foreach (var s in slots) yield return s;
        }

        private GameSession S => GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;

        private void Update()
        {
            if (S == null) return;
            if (escape.WasPressedThisFrame()) Escape?.Invoke();
            // 상점이 유일하게 열려 있으면 B 키가 닫기 토글로 동작해야 하므로 차단 검사보다 먼저 읽는다.
            var panels = GameBootstrap.Instance.Panels;
            if (shop.WasPressedThisFrame() && (!UiBlocksInput || (panels != null && panels.ShopIsOnlyOpen))) ToggleShop?.Invoke();
            if (UiBlocksInput) return;
            if (pause.WasPressedThisFrame()) TogglePause?.Invoke();
            if (speed.WasPressedThisFrame()) ToggleSpeed?.Invoke();
            if (help.WasPressedThisFrame()) ToggleHelp?.Invoke();
            for (int i = 0; i < slots.Length; i++) if (slots[i].WasPressedThisFrame()) SelectSlot(i);
            if (rotate.WasPressedThisFrame()) RotateTool();
            if (clear.WasPressedThisFrame()) ClearTool();
            if (interact.WasPressedThisFrame()) Interact();
            if (demolish.WasPressedThisFrame()) Demolish();

            var v = move.ReadValue<Vector2>();
            foreach (var d in padHeld) v += new Vector2(d.Dx(), d.Dy());
            if (!S.Clock.IsOpen) return; // 퇴근 후에는 걷지 않는다(일시정지 중에는 걸을 수 있다: 일시정지 중 건설 허용)
            if (v.sqrMagnitude > 0.04f) for (float left = Time.deltaTime; left > 0f; left -= MoveSubStep) S.Worker.Move(v.x, v.y, Mathf.Min(left, MoveSubStep)); // 서브스텝: 프레임 스파이크에도 1칸 장애물을 뚫지 않는다. 코어가 정규화·충돌·Facing·CancelInspection을 처리한다
        }

        /// <summary>차단 주체별로 입력 차단을 켜고 끈다. 서로 다른 UI가 서로의 차단을 풀어버리지 않는다.</summary>
        public void SetBlock(string owner, bool on)
        {
            if (on) blockers.Add(owner); else blockers.Remove(owner);
        }

        public void RequestShop() => ToggleShop?.Invoke();
        public void RequestHelp() => ToggleHelp?.Invoke();

        /// <summary>화면 D-패드용. 누르는 동안 held=true, 떼면 false. 이동 자체는 Update에서 매 프레임 적용된다.</summary>
        public void SetPad(Direction d, bool held)
        {
            if (held) padHeld.Add(d); else padHeld.Remove(d);
        }

        /// <summary>화면 D-패드 입력을 모두 해제한다. 패널이 열릴 때 이동을 멈추는 데 쓴다.</summary>
        public void ClearPad() => padHeld.Clear();

        private void OnApplicationFocus(bool focus) { if (!focus) ClearPad(); }

        public void Interact()
        {
            if (S == null) return;
            var r = S.Worker.Interact(SelectedTool);
            Interacted?.Invoke(r);
        }

        public void Demolish()
        {
            if (S == null) return;
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

        public void SelectSlot(int i)
        {
            var available = AvailableSlots();
            if (i < 0 || i >= available.Count) return;
            var type = available[i];
            if (SelectedTool.HasValue && SelectedTool.Value.Type == type) { RotateTool(); return; }
            var dir = PreferredDirection(type);
            SelectedTool = new Tool(type, dir);
            ToolChanged?.Invoke();
        }

        public void ClearTool()
        {
            SelectedTool = null;
            ToolChanged?.Invoke();
        }

        public void RotateTool()
        {
            if (!SelectedTool.HasValue) return;
            var t = SelectedTool.Value;
            var dirs = OwnedDirections(t.Type);
            if (dirs.Count == 0) { ClearTool(); return; }
            int idx = dirs.IndexOf(t.Direction);
            var next = dirs[(idx + 1) % dirs.Count];
            lastDir[t.Type] = next;
            SelectedTool = new Tool(t.Type, next);
            ToolChanged?.Invoke();
        }
    }
}
