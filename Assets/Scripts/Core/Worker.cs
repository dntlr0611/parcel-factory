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
        /// <summary>0..100, 하루 시작 100(스펙 11.3).</summary>
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
        /// <summary>스태미나(스펙 11.3): 초당 소모·회복량과 속도 저하 시작점.</summary>
        public const float MaxStamina = 100f, WalkDrain = 1.2f, CarryDrain = 2.2f, StandRegen = 0.8f, LunchRegen = 20f, SlowBelow = 30f;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;
        private readonly BuildService build;

        public Worker Worker { get; } = new Worker { X = StartX, Y = StartY, Facing = Direction.South };
        /// <summary>검사대에서 검사를 기다리는 택배. 검사 카드 UI가 이를 보고 Inspect를 호출한다.</summary>
        public Package PendingInspection { get; private set; }

        /// <summary>마지막 Rest 이후 Move로 실제로 움직였는가. Rest가 소비한다.</summary>
        private bool movedSinceRest;

        /// <summary>걷는 속도 배율: 스태미나가 SlowBelow 아래면 lerp(0.5, 1, stamina/SlowBelow), 아니면 1.</summary>
        public float SpeedFactor
        {
            get
            {
                float st = Math.Max(0f, Worker.Stamina);
                return st < SlowBelow ? 0.5f + 0.5f * (st / SlowBelow) : 1f;
            }
        }

        public WorkerService(FactoryGrid grid, ConveyorSim sim, BuildService build)
        {
            this.grid = grid;
            this.sim = sim;
            this.build = build;
        }

        private static int Round(float v) => (int)Math.Floor(v + 0.5f);

        public (int x, int y) Tile() => (Round(Worker.X), Round(Worker.Y));

        public (int x, int y) FacingTile() =>
            (Round(Worker.X + Worker.Facing.Dx() * Reach), Round(Worker.Y + Worker.Facing.Dy() * Reach));

        /// <summary>막는 부품(검사대·자동 검사기·분배기·팔레트)만 false. 잠긴 칸은 바닥일 뿐이므로 걸을 수 있다(스펙 10.3).</summary>
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
        public bool Move(float vx, float vy, float dt)
        {
            float len = (float)Math.Sqrt(vx * vx + vy * vy);
            if (!(len >= 1e-6f) || float.IsInfinity(len)) return false;
            Worker.Facing = Math.Abs(vx) >= Math.Abs(vy)
                ? (vx > 0f ? Direction.East : Direction.West)
                : (vy > 0f ? Direction.North : Direction.South);
            if (!(dt > 0f) || float.IsInfinity(dt)) return false;

            float step = Speed * SpeedFactor * dt / len;
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
                CancelInspection();
                movedSinceRest = true;
                // 움직인 프레임만 소모한다(양 축 모두 막히면 소모 없음).
                Worker.Stamina = Clamp(Worker.Stamina - (Worker.Carrying != null ? CarryDrain : WalkDrain) * dt);
            }
            return moved;
        }

        /// <summary>스태미나 회복(GameSession.Tick이 호출). 점심이면 항상 +LunchRegen/초, 아니면 지난 Rest 이후 움직이지 않았을 때만 +StandRegen/초.
        /// 이동 플래그는 여기서 소비된다.</summary>
        public void Rest(float dt, DayPhase phase)
        {
            bool moved = movedSinceRest;
            movedSinceRest = false;
            if (!(dt > 0f) || float.IsInfinity(dt)) return;
            if (phase == DayPhase.Lunch) Worker.Stamina = Clamp(Worker.Stamina + LunchRegen * dt);
            else if (phase == DayPhase.Working && !moved) Worker.Stamina = Clamp(Worker.Stamina + StandRegen * dt);
        }

        /// <summary>다음 날 시작: 스태미나 가득.</summary>
        public void Refill()
        {
            Worker.Stamina = MaxStamina;
            movedSinceRest = false;
        }

        private static float Clamp(float v) => v < 0f ? 0f : v > MaxStamina ? MaxStamina : v;

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
                if (fc.IsTrack && !fc.IsOccupied && !fc.Demolishing) return InteractResult.PutDown;
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
