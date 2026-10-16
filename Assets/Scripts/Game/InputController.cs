using System;
using System.Collections.Generic;
using System.Linq;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ParcelFactory.Game
{
    /// <summary>키보드·게임패드·화면 버튼 입력을 코어 호출로 바꾼다. 핫바는 부품 종류당 한 칸, 방향은 R로 순환.</summary>
    public sealed class InputController : MonoBehaviour
    {
        public static readonly CellType[] SlotOrder = { CellType.Conveyor, CellType.FastConveyor, CellType.Table, CellType.Splitter, CellType.Scanner };

        public Tool? SelectedTool { get; private set; }
        public int SelectedSlot => SelectedTool.HasValue ? AvailableSlots().IndexOf(SelectedTool.Value.Type) : -1;
        public bool UiBlocksInput { get; set; }

        public event Action<InteractResult> Interacted;
        public event Action<BuildResult> DemolishRequested;
        public event Action TogglePause, ToggleSpeed, ToggleShop, ToggleHelp, Escape, ToolChanged;

        private InputAction move, interact, demolish, rotate, clear, pause, speed, shop, help, escape;
        private readonly InputAction[] slots = new InputAction[5];
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
            for (int i = 0; i < 5; i++) slots[i] = Button("Slot" + (i + 1), "<Keyboard>/" + (i + 1), null);
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
        }

        private void OnDisable()
        {
            foreach (var a in All()) a.Disable();
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
            if (UiBlocksInput) return;
            if (pause.WasPressedThisFrame()) TogglePause?.Invoke();
            if (speed.WasPressedThisFrame()) ToggleSpeed?.Invoke();
            if (shop.WasPressedThisFrame()) ToggleShop?.Invoke();
            if (help.WasPressedThisFrame()) ToggleHelp?.Invoke();
            for (int i = 0; i < 5; i++) if (slots[i].WasPressedThisFrame()) SelectSlot(i);
            if (rotate.WasPressedThisFrame()) RotateTool();
            if (clear.WasPressedThisFrame()) ClearTool();
            if (interact.WasPressedThisFrame()) Interact();
            if (demolish.WasPressedThisFrame()) Demolish();

            var v = move.ReadValue<Vector2>();
            foreach (var d in padHeld) v += new Vector2(d.Dx(), d.Dy());
            if (v.sqrMagnitude > 0.04f) for (float left = Time.deltaTime; left > 0f; left -= MoveSubStep) S.Worker.Move(v.x, v.y, Mathf.Min(left, MoveSubStep)); // 서브스텝: 프레임 스파이크에도 1칸 장애물을 뚫지 않는다. 코어가 정규화·충돌·Facing·CancelInspection을 처리한다
        }

        public void RequestShop() => ToggleShop?.Invoke();
        public void RequestHelp() => ToggleHelp?.Invoke();

        /// <summary>화면 D-패드용. 누르는 동안 held=true, 떼면 false. 이동 자체는 Update에서 매 프레임 적용된다.</summary>
        public void SetPad(Direction d, bool held)
        {
            if (held) padHeld.Add(d); else padHeld.Remove(d);
        }

        private void OnApplicationFocus(bool focus) { if (!focus) padHeld.Clear(); }

        public void Interact()
        {
            if (S == null) return;
            var r = S.Worker.Interact(SelectedTool);
            Interacted?.Invoke(r);
        }

        public void Demolish()
        {
            if (S == null) return;
            DemolishRequested?.Invoke(S.Worker.TryDemolishFacing());
        }

        /// <summary>보유 방향이 하나라도 있는 종류만 슬롯이 된다.</summary>
        public List<CellType> AvailableSlots()
        {
            var owned = S.Toolbar.OwnedTools().ToList();
            return SlotOrder.Where(t => owned.Any(o => o.Type == t)).ToList();
        }

        public List<Direction> OwnedDirections(CellType t) =>
            S.Toolbar.OwnedTools().Where(o => o.Type == t).Select(o => o.Direction).ToList();

        public void SelectSlot(int i)
        {
            var available = AvailableSlots();
            if (i < 0 || i >= available.Count) return;
            var type = available[i];
            if (SelectedTool.HasValue && SelectedTool.Value.Type == type) { RotateTool(); return; }
            var dirs = OwnedDirections(type);
            var dir = lastDir.TryGetValue(type, out var d) && dirs.Contains(d) ? d : dirs[0];
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
