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
    }

    public enum InteractResult
    {
        PickedUp, PutDown, Delivered, InspectionRequired, Placed,
        NoFloor, CannotPutHere, NoTool, Locked, Occupied, NotOwned, NotEnoughMoney,
        /// <summary>막는 부품을 작업자의 충돌원과 겹치는 칸에 놓으려 함.</summary>
        Standing,
    }

    /// <summary>작업자 규칙: 해금 부지 안에서 연속 좌표로 걷고(레일·도크 위 통과), 바라보는 칸에 대해 집기/놓기/검사/설치/해체를 한다.</summary>
    public sealed class WorkerService
    {
        public const float StartX = 2f, StartY = 4f, Speed = 3.4f, Radius = 0.28f, Reach = 0.8f;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;
        private readonly BuildService build;

        public Worker Worker { get; } = new Worker { X = StartX, Y = StartY, Facing = Direction.South };
        /// <summary>검사대에서 검사를 기다리는 택배. 검사 카드 UI가 이를 보고 Inspect를 호출한다.</summary>
        public Package PendingInspection { get; private set; }

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

        public static bool IsWalkable(CellType t)
        {
            switch (t)
            {
                case CellType.Empty:
                case CellType.Conveyor:
                case CellType.FastConveyor:
                case CellType.Inlet:
                case CellType.Outlet:
                case CellType.ReturnDock:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>충돌원(반지름 R)이 해금 부지 안에 있고, 그 사각 외곽이 걸치는 칸이 모두 걸을 수 있으면 true.
        /// 칸 경계에 정확히 닿기만 한 것은 걸친 것으로 보지 않는다(Overlaps와 같은 규칙).</summary>
        public bool CanStand(float px, float py)
        {
            float lo = -0.5f + Radius, hi = grid.UnlockedSize - 0.5f - Radius;
            if (!(px >= lo && px <= hi && py >= lo && py <= hi)) return false; // NaN도 여기서 거부
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

            float step = Speed * dt / len;
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
            if (moved) CancelInspection();
            return moved;
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
                if (fc.IsTrack && !fc.IsOccupied && !fc.Demolishing) return InteractResult.PutDown;
                return InteractResult.CannotPutHere;
            }

            var p = sim.PackageAt(fx, fy);
            if (p != null)
                return (fc.Type == CellType.Table && !p.Inspected) ? InteractResult.InspectionRequired : InteractResult.PickedUp;

            if (!tool.HasValue) return InteractResult.NoTool;
            if (!IsWalkable(tool.Value.Type) && Overlaps(fx, fy)) return InteractResult.Standing;
            return Map(build.Check(fx, fy, tool.Value.Type, tool.Value.Direction));
        }

        /// <summary>우선순위: 운반 중이면 놓기 → 택배가 있으면 검사/집기 → 도구가 있으면 설치. Preview와 같은 결정을 실행한다.</summary>
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
                    sim.PlaceCarried(Worker.Carrying, fx, fy);
                    Worker.Carrying = null;
                    break;
                case InteractResult.InspectionRequired:
                    PendingInspection = sim.PackageAt(fx, fy);
                    break;
                case InteractResult.PickedUp:
                    var p = sim.PackageAt(fx, fy);
                    sim.Take(p);
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
