using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class ConveyorSim
    {
        public const int DeliveryReward = 15;
        private const float ArriveEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly List<Package> packages = new List<Package>();

        public IReadOnlyList<Package> Packages => packages;
        public int NextId { get; set; } = 1;

        public event Action<Package> PackageDelivered;
        public event Action<Package> PackageLost;

        public ConveyorSim(FactoryGrid grid, Economy economy)
        {
            this.grid = grid;
            this.economy = economy;
        }

        public bool TrySpawn(int x, int y, PackageKind kind, out Package package)
        {
            package = null;
            if (!grid.InBounds(x, y)) return false;
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || cell.IsOccupied || cell.Demolishing) return false;

            package = new Package
            {
                Id = NextId++,
                Kind = kind,
                X = x,
                Y = y,
                Progress = 0f,
                Heading = HeadingFor(cell),
            };
            cell.OccupantId = package.Id;
            packages.Add(package);
            return true;
        }

        /// <summary>저장 파일에서 택배를 그대로 되살린다. Heading과 토글은 변경하지 않는다.</summary>
        public void Restore(Package package)
        {
            packages.Add(package);
            grid.Get(package.X, package.Y).OccupantId = package.Id;
        }

        private static Direction HeadingFor(Cell cell)
        {
            if (cell.Type != CellType.Splitter) return cell.Direction;
            var heading = cell.SplitterToggle ? cell.Direction.TurnRight() : cell.Direction.TurnLeft();
            cell.SplitterToggle = !cell.SplitterToggle;
            return heading;
        }

        public void Tick(float dt)
        {
            // 오래된 택배(앞쪽)부터 처리해 앞 칸이 비면 같은 틱에 뒤 택배가 따라올 수 있게 한다.
            for (int i = 0; i < packages.Count; i++)
            {
                var p = packages[i];
                var cell = grid.Get(p.X, p.Y);
                p.Progress = Math.Min(1f, p.Progress + cell.Speed * dt);
                if (p.Progress < 1f - ArriveEpsilon) continue;
                p.Progress = 1f;

                int nx = p.X + p.Heading.Dx();
                int ny = p.Y + p.Heading.Dy();
                if (!grid.InBounds(nx, ny)) { Lose(i, p); i--; continue; }

                var next = grid.Get(nx, ny);
                if (next.Type == CellType.Outlet) { Deliver(i, p); i--; continue; }
                if (!next.IsTrack || next.Type == CellType.Inlet) { Lose(i, p); i--; continue; }
                if (next.IsOccupied || next.Demolishing) continue; // 대기

                cell.OccupantId = -1;
                next.OccupantId = p.Id;
                p.X = nx;
                p.Y = ny;
                p.Progress = 0f;
                p.Heading = HeadingFor(next);
            }
        }

        public void RemovePackagesAt(int x, int y)
        {
            for (int i = packages.Count - 1; i >= 0; i--)
                if (packages[i].X == x && packages[i].Y == y)
                    Lose(i, packages[i]);
        }

        private void Deliver(int index, Package p)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            economy.Earn(DeliveryReward);
            PackageDelivered?.Invoke(p);
        }

        private void Lose(int index, Package p)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            PackageLost?.Invoke(p);
        }
    }
}
