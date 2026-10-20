using System;

namespace ParcelFactory.Core
{
    public sealed class Worker
    {
        /// <summary>연속 좌표(칸 단위, 칸 중심이 정수).</summary>
        public float X;
        public float Y;
        public Direction Facing;
        /// <summary>들고 있는 택배. 없으면 null. 라인(ConveyorSim)에는 없는 상태다.</summary>
        public Package Carrying;
        /// <summary>0..WorkerService.EffectiveMaxStamina(기본 100), 하루 시작 가득(스펙 11.3, 11.5).</summary>
        public float Stamina = WorkerService.MaxStamina;
    }

    public enum InteractResult
    {
        PickedUp, PutDown, Delivered, InspectionRequired, Placed,
        NoFloor, CannotPutHere, NoTool, Locked, Occupied, NotOwned, NotEnoughMoney,
        /// <summary>막는 부품을 작업자의 충돌원과 겹치는 칸에 놓으려 함.</summary>
        Standing,
        /// <summary>스태미나 0이라 택배를 집을 수 없음(스펙 11.3). 내려놓기·출고는 막지 않는다.</summary>
        Exhausted,
    }

    /// <summary>작업자 규칙: 건물 안 12×12 전체를 연속 좌표로 걷고(레일·도크·잠긴 칸 위 통과, 스펙 10.3),
    /// 바라보는 칸에 대해 집기/놓기/검사/설치/해체를 한다.</summary>
    public sealed class WorkerService
    {
        public const float StartX = 2f, StartY = 4f, Speed = 3.4f, Radius = 0.28f, Reach = 0.8f;
        /// <summary>스태미나(스펙 11.3, 11.5): MaxStamina는 강화 전 기본 최대치. 걷기·들고 걷기는 소모 없음(WalkDrain·CarryDrain = 0).
        /// 회복은 걷든 서든 초당 StandRegen × 회복 배율, 점심에는 초당 LunchRegen. SlowBelow 아래면 속도 저하.</summary>
        public const float MaxStamina = 100f, WalkDrain = 0f, CarryDrain = 0f, StandRegen = 0.8f, LunchRegen = 20f, SlowBelow = 30f;
        /// <summary>달리기(스페이스 유지): 속도 ×1.6, 초당 소모 RunDrain(들고 달리면 RunCarryDrain). 스태미나가 RunMinStamina 미만이면 달릴 수 없다(걷기로 처리).</summary>
        public const float RunSpeedMultiplier = 1.6f, RunDrain = 3f, RunCarryDrain = 5.5f, RunMinStamina = 10f;
        /// <summary>근무 중 회복 상한(스펙 11.11): 최대 체력의 80%까지만 회복한다. 점심에 가만히 서 있을 때만 100%까지.</summary>
        public const float RegenCap = 0.8f;
        /// <summary>직전 Move가 실제로 달리기로 처리됐는지(뷰 애니메이션용).</summary>
        public bool IsRunning { get; private set; }
        public bool CanRun => Worker.Stamina >= RunMinStamina;
        /// <summary>마지막 Rest 이후 Move가 실제로 움직였는가(스펙 11.11 점심 휴식 판정). Rest가 소비한다.</summary>
        private bool movedSinceRest;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;
        private readonly BuildService build;

        public Worker Worker { get; } = new Worker { X = StartX, Y = StartY, Facing = Direction.South };
        /// <summary>강화 상태(스펙 11.5). 최대 체력·속도·회복 배율을 여기서 읽는다.</summary>
        public Progression Progression { get; }
        /// <summary>강화가 반영된 최대 체력(100 + 20 × 체력 레벨).</summary>
        public float EffectiveMaxStamina => Progression.MaxStamina;
        /// <summary>강화가 반영된 기본 걷기 속도(Speed × 속도 배율). 체력 저하·달리기 배율은 Move가 곱한다.</summary>
        public float EffectiveSpeed => Speed * Progression.SpeedMultiplier;
        /// <summary>검사대에서 검사를 기다리는 택배. 검사 카드 UI가 이를 보고 Inspect를 호출한다.</summary>
        public Package PendingInspection { get; private set; }

        /// <summary>걷는 속도 배율: 스태미나가 SlowBelow 아래면 lerp(0.5, 1, stamina/SlowBelow), 아니면 1.</summary>
        public float SpeedFactor
        {
            get
            {
                float st = Math.Max(0f, Worker.Stamina);
                return st < SlowBelow ? 0.5f + 0.5f * (st / SlowBelow) : 1f;
            }
        }

        public WorkerService(FactoryGrid grid, ConveyorSim sim, BuildService build) : this(grid, sim, build, null) { }

        /// <summary>progression이 null이면 새(레벨 0) Progression을 쓴다.</summary>
        public WorkerService(FactoryGrid grid, ConveyorSim sim, BuildService build, Progression progression)
        {
            this.grid = grid;
            this.sim = sim;
            this.build = build;
            Progression = progression ?? new Progression();
        }

        private static int Round(float v) => (int)Math.Floor(v + 0.5f);

        public (int x, int y) Tile() => (Round(Worker.X), Round(Worker.Y));

        public (int x, int y) FacingTile() =>
            (Round(Worker.X + Worker.Facing.Dx() * Reach), Round(Worker.Y + Worker.Facing.Dy() * Reach));

        /// <summary>막는 부품(검사대·자동 검사기·분배기·팔레트)만 false. 브릿지는 레일처럼 지나갈 수 있다(스펙 11.12). 잠긴 칸은 바닥일 뿐이므로 걸을 수 있다(스펙 10.3).</summary>
        public static bool IsWalkable(CellType t)
        {
            switch (t)
            {
                case CellType.Table:
                case CellType.Scanner:
                case CellType.Splitter:
                case CellType.Pallet:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>충돌원(반지름 R)이 격자 안에 있고, 그 사각 외곽이 걸치는 칸이 모두 걸을 수 있으면 true.
        /// 칸 경계에 정확히 닿기만 한 것은 걸친 것으로 보지 않는다(Overlaps와 같은 규칙).</summary>
        public bool CanStand(float px, float py)
        {
            float lo = -0.5f + Radius;
            float hiX = FactoryGrid.Width - 0.5f - Radius, hiY = FactoryGrid.Height - 0.5f - Radius;
            if (!(px >= lo && px <= hiX && py >= lo && py <= hiY)) return false; // NaN도 여기서 거부
            int x0 = (int)Math.Floor(px - Radius - 0.5f) + 1, x1 = (int)Math.Ceiling(px + Radius + 0.5f) - 1;
            int y0 = (int)Math.Floor(py - Radius - 0.5f) + 1, y1 = (int)Math.Ceiling(py + Radius + 0.5f) - 1;
            return WalkableAt(x0, y0) && WalkableAt(x1, y0) && WalkableAt(x0, y1) && WalkableAt(x1, y1);
        }

        private bool WalkableAt(int x, int y) => grid.InBounds(x, y) && IsWalkable(grid.Get(x, y).Type);

        /// <summary>입력 벡터 방향으로 Speed×dt만큼 걷는다(정규화). x축, y축을 따로 시도해 막힌 축만 취소한다(벽 따라 미끄러짐).
        /// 영벡터가 아니면 지배 축으로 Facing을 바꾼다. 실제로 움직였으면 true이고 대기 중인 검사를 취소한다.</summary>
        public bool Move(float vx, float vy, float dt) => Move(vx, vy, dt, false);

        /// <summary>run=true면 달리기 요청. 스태미나가 모자라면 걷기로 떨어진다.</summary>
        public bool Move(float vx, float vy, float dt, bool run)
        {
            IsRunning = false;
            float len = (float)Math.Sqrt(vx * vx + vy * vy);
            if (!(len >= 1e-6f) || float.IsInfinity(len)) return false;
            Worker.Facing = Math.Abs(vx) >= Math.Abs(vy)
                ? (vx > 0f ? Direction.East : Direction.West)
                : (vy > 0f ? Direction.North : Direction.South);
            if (!(dt > 0f) || float.IsInfinity(dt)) return false;

            IsRunning = run && CanRun;
            float step = EffectiveSpeed * SpeedFactor * (IsRunning ? RunSpeedMultiplier : 1f) * dt / len;
            bool moved = false;
            if (vx != 0f)
            {
                float nx = Worker.X + vx * step;
                if (CanStand(nx, Worker.Y)) { Worker.X = nx; moved = true; }
            }
            if (vy != 0f)
            {
                float ny = Worker.Y + vy * step;
                if (CanStand(Worker.X, ny)) { Worker.Y = ny; moved = true; }
            }
            if (moved)
            {
                movedSinceRest = true;
                CancelInspection();
                // 달려서 움직인 프레임만 소모한다(걷기는 소모 없음, 양 축 모두 막히면 소모 없음).
                if (IsRunning) Worker.Stamina = Clamp(Worker.Stamina - (Worker.Carrying != null ? RunCarryDrain : RunDrain) * dt);
            }
            if (!moved) IsRunning = false;
            return moved;
        }

        /// <summary>마지막 Rest 이후 움직였는지 돌려주고 플래그를 지운다. GameSession.Tick이 한 프레임의 여러 Rest 구간에 같은 값을 쓰려고 호출한다.</summary>
        public bool ConsumeMoved()
        {
            bool m = movedSinceRest;
            movedSinceRest = false;
            return m;
        }

        /// <summary>스태미나 회복(스펙 11.11). 마지막 Rest 이후의 이동 여부를 소비한다.</summary>
        public void Rest(float dt, DayPhase phase) => Rest(dt, phase, ConsumeMoved());

        /// <summary>점심에 이동하지 않았으면 +LunchRegen/초로 최대 체력까지. 그 외(근무 중, 또는 점심에 이동함)는
        /// +StandRegen × 회복 배율/초로 최대 체력의 RegenCap까지만(이미 넘으면 회복 없음). 퇴근 중에는 회복 없음.</summary>
        public void Rest(float dt, DayPhase phase, bool moved)
        {
            if (!(dt > 0f) || float.IsInfinity(dt)) return;
            if (phase == DayPhase.Lunch && !moved)
            {
                Worker.Stamina = Clamp(Worker.Stamina + LunchRegen * dt);
                return;
            }
            if (phase != DayPhase.Working && phase != DayPhase.Lunch) return;
            float cap = EffectiveMaxStamina * RegenCap;
            if (Worker.Stamina >= cap) return;
            Worker.Stamina = Math.Min(cap, Worker.Stamina + StandRegen * Progression.RegenMultiplier * dt);
        }

        /// <summary>다음 날 시작: 스태미나를 EffectiveMaxStamina로 가득.</summary>
        public void Refill() => Worker.Stamina = EffectiveMaxStamina;

        /// <summary>현재 체력에 amount를 더하고 0..EffectiveMaxStamina로 자른다(체력 강화 시 +20).</summary>
        public void AddStamina(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount)) return;
            Worker.Stamina = Clamp(Worker.Stamina + amount);
        }

        private float Clamp(float v)
        {
            float max = EffectiveMaxStamina;
            return v < 0f ? 0f : v > max ? max : v;
        }

        /// <summary>작업자의 충돌원이 칸 (gx, gy)와 겹치는가.</summary>
        public bool Overlaps(int gx, int gy) =>
            Math.Abs(Worker.X - gx) < 0.5f + Radius && Math.Abs(Worker.Y - gy) < 0.5f + Radius;

        /// <summary>Interact가 무엇을 할지 부작용 없이 알려준다. UI 프롬프트용.</summary>
        public InteractResult Preview(Tool? tool)
        {
            var (fx, fy) = FacingTile();
            if (!grid.InBounds(fx, fy)) return InteractResult.NoFloor;
            var fc = grid.Get(fx, fy);

            if (Worker.Carrying != null)
            {
                if (fc.IsDock) return InteractResult.Delivered;
                if (fc.Type == CellType.Pallet)
                    return !fc.IsPalletFull && !fc.Demolishing ? InteractResult.PutDown : InteractResult.CannotPutHere;
                if (fc.IsTrack && fc.HasFreeLane && !fc.Demolishing) return InteractResult.PutDown; // 브릿지는 빈 차선이 하나라도 있으면
                return InteractResult.CannotPutHere;
            }

            if (fc.Type == CellType.Pallet && fc.Pallet.Count > 0) return PickUpOrExhausted();

            var p = sim.PackageAt(fx, fy);
            if (p != null)
                return (fc.Type == CellType.Table && !p.Inspected) ? InteractResult.InspectionRequired : PickUpOrExhausted();

            if (!tool.HasValue) return InteractResult.NoTool;
            if (!IsWalkable(tool.Value.Type) && Overlaps(fx, fy)) return InteractResult.Standing;
            return Map(build.Check(fx, fy, tool.Value.Type, tool.Value.Direction));
        }

        private InteractResult PickUpOrExhausted() => Worker.Stamina <= 0f ? InteractResult.Exhausted : InteractResult.PickedUp;

        /// <summary>우선순위: 운반 중이면 놓기(도크·팔레트·트랙) → 팔레트 더미·택배가 있으면 집기/검사 → 도구가 있으면 설치. Preview와 같은 결정을 실행한다.</summary>
        public InteractResult Interact(Tool? tool)
        {
            var result = Preview(tool);
            var (fx, fy) = FacingTile();
            switch (result)
            {
                case InteractResult.Delivered:
                    sim.DeliverCarried(Worker.Carrying, grid.Get(fx, fy).Type);
                    Worker.Carrying = null;
                    break;
                case InteractResult.PutDown:
                    if (grid.Get(fx, fy).Type == CellType.Pallet)
                    {
                        if (!sim.PutOnPallet(Worker.Carrying, fx, fy)) throw new InvalidOperationException("Preview said PutDown but pallet refused");
                    }
                    else sim.PlaceCarried(Worker.Carrying, fx, fy);
                    Worker.Carrying = null;
                    break;
                case InteractResult.InspectionRequired:
                    PendingInspection = sim.PackageAt(fx, fy);
                    break;
                case InteractResult.PickedUp:
                    Package p;
                    if (grid.Get(fx, fy).Type == CellType.Pallet) p = sim.TakeFromPallet(fx, fy);
                    else { p = sim.PackageAt(fx, fy); sim.Take(p); }
                    Worker.Carrying = p;
                    break;
                case InteractResult.Placed:
                    var placed = build.TryPlace(fx, fy, tool.Value.Type, tool.Value.Direction);
                    if (placed != BuildResult.Ok) throw new InvalidOperationException($"Check said Ok but TryPlace returned {placed}");
                    break;
            }
            return result;
        }

        private static InteractResult Map(BuildResult r)
        {
            switch (r)
            {
                case BuildResult.Ok: return InteractResult.Placed;
                case BuildResult.Locked: return InteractResult.Locked;
                case BuildResult.Occupied: return InteractResult.Occupied;
                case BuildResult.NotOwned: return InteractResult.NotOwned;
                case BuildResult.NotEnoughMoney: return InteractResult.NotEnoughMoney;
                default: throw new InvalidOperationException($"unexpected build result {r}");
            }
        }

        /// <summary>대기 중인 검사를 확정한다. 택배가 검사대를 떠났거나 작업자가 더 이상 그 칸을 바라보지 않으면 false.</summary>
        public bool Inspect(bool reject)
        {
            var p = PendingInspection;
            PendingInspection = null;
            if (p == null) return false;
            var (fx, fy) = FacingTile();
            if (fx != p.X || fy != p.Y) return false;
            if (sim.PackageAt(p.X, p.Y) != p || grid.Get(p.X, p.Y).Type != CellType.Table) return false;
            p.Inspect(reject);
            sim.NoteInspected();
            return true;
        }

        public void CancelInspection() => PendingInspection = null;

        public BuildResult TryDemolishFacing()
        {
            var (fx, fy) = FacingTile();
            return build.BeginDemolish(fx, fy);
        }
    }
}
