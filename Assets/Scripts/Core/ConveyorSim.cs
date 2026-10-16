using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class ConveyorSim
    {
        private const float ArriveEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly PackageFactory factory;
        private readonly List<Package> packages = new List<Package>();

        public IReadOnlyList<Package> Packages => packages;
        public int NextId { get; set; } = 1;
        /// <summary>성공 판정(+15, +5) 횟수.</summary>
        public int DeliveredCount { get; set; }
        public int LostCount { get; set; }

        /// <summary>Tick 루프 중간에 발생한다. 핸들러는 시뮬레이션을 변경(생성·제거)하면 안 되며 읽기만 해야 한다.</summary>
        public event Action<Package, DeliveryOutcome, int> PackageDelivered;
        /// <summary>Tick 루프 중간에 발생한다. 핸들러는 시뮬레이션을 변경(생성·제거)하면 안 되며 읽기만 해야 한다.</summary>
        public event Action<Package> PackageLost;

        public ConveyorSim(FactoryGrid grid, Economy economy, PackageFactory factory = null)
        {
            this.grid = grid;
            this.economy = economy;
            this.factory = factory ?? new PackageFactory(new Random());
        }

        public bool TrySpawn(int x, int y, PackageKind kind, out Package package)
        {
            package = null;
            if (!grid.InBounds(x, y)) return false;
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || cell.IsOccupied || cell.Demolishing) return false;

            var heading = HeadingFor(cell, cell.Direction);
            package = factory.Create(NextId++, x, y, heading);
            package.Kind = kind;
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

        public Package PackageAt(int x, int y)
        {
            foreach (var p in packages)
                if (p.X == x && p.Y == y) return p;
            return null;
        }

        /// <summary>작업자가 집는다. 소실로 세지 않는다.</summary>
        public void Take(Package p)
        {
            int i = packages.IndexOf(p);
            if (i < 0) throw new InvalidOperationException($"package {p.Id} is not on the line");
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(i);
        }

        /// <summary>작업자가 들고 있던 택배를 트랙 칸에 내려놓는다.</summary>
        public void PlaceCarried(Package p, int x, int y)
        {
            if (!grid.InBounds(x, y)) throw new InvalidOperationException($"({x},{y}) out of bounds");
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || cell.IsOccupied || cell.Demolishing)
                throw new InvalidOperationException($"cannot put package at ({x},{y})");
            p.X = x;
            p.Y = y;
            p.Progress = 0f;
            p.Heading = HeadingFor(cell, cell.Direction);
            p.EntryHeading = p.Heading;
            cell.OccupantId = p.Id;
            packages.Add(p);
        }

        /// <summary>작업자가 들고 있던 택배를 도크에 직접 넣는다. 판정 증감을 돌려준다.</summary>
        public int DeliverCarried(Package p, CellType dockType) => ApplyDelivery(p, dockType);

        /// <summary>분배기는 자기 방향 기준 좌·우로 번갈아 내보내되, 들어온 방향의 정반대로는 절대 내보내지 않는다.</summary>
        private static Direction HeadingFor(Cell cell, Direction incoming)
        {
            if (cell.Type != CellType.Splitter) return cell.Direction;
            var left = cell.Direction.TurnLeft();
            var right = cell.Direction.TurnRight();
            var chosen = cell.SplitterToggle ? right : left;
            var other = cell.SplitterToggle ? left : right;
            if (chosen == incoming.Opposite()) chosen = other;
            cell.SplitterToggle = !cell.SplitterToggle;
            return chosen;
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
                if (cell.Type == CellType.Table && !p.Inspected) continue; // 검사 전까지 검사대에서 대기

                int nx = p.X + p.Heading.Dx();
                int ny = p.Y + p.Heading.Dy();
                if (!grid.InBounds(nx, ny)) { Lose(i, p); i--; continue; }

                var next = grid.Get(nx, ny);
                if (next.IsDock) { Deliver(i, p, next.Type); i--; continue; }
                if (!next.IsTrack || next.Type == CellType.Inlet) { Lose(i, p); i--; continue; }
                if (next.IsOccupied || next.Demolishing) continue; // 대기

                cell.OccupantId = -1;
                next.OccupantId = p.Id;
                p.X = nx;
                p.Y = ny;
                p.Progress = 0f;
                p.EntryHeading = p.Heading;
                p.Heading = HeadingFor(next, p.Heading);
                if (next.Type == CellType.Scanner && !p.Inspected) p.Inspect(p.Bad);
            }
        }

        public void RemovePackagesAt(int x, int y)
        {
            for (int i = packages.Count - 1; i >= 0; i--)
                if (packages[i].X == x && packages[i].Y == y)
                    Lose(i, packages[i]);
        }

        private int ApplyDelivery(Package p, CellType dockType)
        {
            var outcome = DeliveryScoring.Outcome(p, dockType);
            int delta = DeliveryScoring.Delta(outcome);
            economy.Adjust(delta);
            if (DeliveryScoring.IsSuccess(outcome)) DeliveredCount++;
            PackageDelivered?.Invoke(p, outcome, delta);
            return delta;
        }

        private void Deliver(int index, Package p, CellType dockType)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            ApplyDelivery(p, dockType);
        }

        private void Lose(int index, Package p)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            LostCount++;
            PackageLost?.Invoke(p);
        }
    }
}
