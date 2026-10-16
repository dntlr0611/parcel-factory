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

        /// <summary>우선순위: 운반 중이면 놓기 → 택배가 있으면 검사/집기 → 도구가 있으면 설치.</summary>
        public InteractResult Interact(Tool? tool)
        {
            var (fx, fy) = FacingTile();
            if (!grid.InBounds(fx, fy)) return InteractResult.NoFloor;
            var fc = grid.Get(fx, fy);

            if (Worker.Carrying != null)
            {
                if (fc.IsDock)
                {
                    sim.DeliverCarried(Worker.Carrying, fc.Type);
                    Worker.Carrying = null;
                    return InteractResult.Delivered;
                }
                if (fc.IsTrack && !fc.IsOccupied && !fc.Demolishing)
                {
                    sim.PlaceCarried(Worker.Carrying, fx, fy);
                    Worker.Carrying = null;
                    return InteractResult.PutDown;
                }
                return InteractResult.CannotPutHere;
            }

            var p = sim.PackageAt(fx, fy);
            if (p != null)
            {
                if (fc.Type == CellType.Table && !p.Inspected)
                {
                    PendingInspection = p;
                    return InteractResult.InspectionRequired;
                }
                sim.Take(p);
                Worker.Carrying = p;
                return InteractResult.PickedUp;
            }

            if (!tool.HasValue) return InteractResult.NoTool;
            switch (build.TryPlace(fx, fy, tool.Value.Type, tool.Value.Direction))
            {
                case BuildResult.Ok: return InteractResult.Placed;
                case BuildResult.Locked: return InteractResult.Locked;
                case BuildResult.Occupied: return InteractResult.Occupied;
                case BuildResult.NotOwned: return InteractResult.NotOwned;
                default: return InteractResult.NotEnoughMoney;
            }
        }

        /// <summary>대기 중인 검사를 확정한다. 택배가 그 사이 검사대를 떠났으면 false.</summary>
        public bool Inspect(bool reject)
        {
            var p = PendingInspection;
            PendingInspection = null;
            if (p == null) return false;
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
