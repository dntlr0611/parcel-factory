using System;

namespace ParcelFactory.Core
{
    public sealed class Worker
    {
        public int X;
        public int Y;
        public Direction Facing;
        /// <summary>들고 있는 택배. 없으면 null. 라인(ConveyorSim)에는 없는 상태다.</summary>
        public Package Carrying;
    }

    public enum InteractResult
    {
        PickedUp, PutDown, Delivered, InspectionRequired, Placed,
        NoFloor, CannotPutHere, NoTool, Locked, Occupied, NotOwned, NotEnoughMoney,
    }

    /// <summary>작업자 규칙: 빈 바닥만 걷고, 바라보는 칸에 대해 집기/놓기/검사/설치/해체를 한다.</summary>
    public sealed class WorkerService
    {
        public const int StartX = 2, StartY = 4;

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

        public (int x, int y) FacingTile() => (Worker.X + Worker.Facing.Dx(), Worker.Y + Worker.Facing.Dy());

        public bool CanWalk(int x, int y) => grid.InBounds(x, y) && grid.Get(x, y).Type == CellType.Empty;

        /// <summary>방향은 항상 바뀌고, 갈 수 있을 때만 이동한다.</summary>
        public bool TryMove(Direction dir)
        {
            Worker.Facing = dir;
            int nx = Worker.X + dir.Dx(), ny = Worker.Y + dir.Dy();
            if (!CanWalk(nx, ny)) return false;
            Worker.X = nx;
            Worker.Y = ny;
            return true;
        }

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
