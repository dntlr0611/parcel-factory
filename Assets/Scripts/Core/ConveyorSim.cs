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
        /// <summary>검사 판정 횟수(검사대에서 작업자 검사 + 자동 검사기). 하루 요약용(스펙 11.1).</summary>
        public int InspectedCount { get; set; }

        /// <summary>검사 한 건을 센다. 검사대 검사(WorkerService.Inspect)와 자동 검사기가 호출한다. Inspected 이벤트를 발생시킨다.</summary>
        public void NoteInspected()
        {
            InspectedCount++;
            Inspected?.Invoke();
        }

        /// <summary>검사 판정 한 건마다(NoteInspected) 발생한다. Tick 루프 중간일 수 있으므로 핸들러는 시뮬레이션을 변경하면 안 된다.</summary>
        public event Action Inspected;

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

            var heading = HeadingFor(x, y, cell.Direction, null); // 새 택배는 미검사 → 분배기면 파란 출구
            package = factory.Create(NextId++, x, y, heading);
            package.Kind = kind;
            cell.OccupantId = package.Id;
            packages.Add(package);
            return true;
        }

        /// <summary>저장 파일에서 택배를 그대로 되살린다. Heading은 변경하지 않는다.</summary>
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

        /// <summary>검사대 위에서 검사를 기다리는 택배인가.</summary>
        public bool IsAwaitingInspection(Package p) =>
            !p.Inspected && grid.Get(p.X, p.Y).Type == CellType.Table;

        public int AwaitingInspectionCount
        {
            get { int n = 0; foreach (var p in packages) if (IsAwaitingInspection(p)) n++; return n; }
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
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            p.X = x;
            p.Y = y;
            p.Progress = 0f;
            p.Heading = HeadingFor(x, y, cell.Direction, p); // 분배기에 손으로 놓아도 같은 분류 규칙
            p.EntryHeading = p.Heading;
            cell.OccupantId = p.Id;
            packages.Add(p);
            if (cell.Type == CellType.Scanner && !p.Inspected) { p.Inspect(p.Bad); NoteInspected(); } // 스캐너는 놓는 순간에도 판정한다
        }

        /// <summary>작업자가 들고 있던 택배를 도크에 직접 넣는다. 판정 증감을 돌려준다.</summary>
        public int DeliverCarried(Package p, CellType dockType)
        {
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            return ApplyDelivery(p, dockType);
        }

        /// <summary>팔레트 더미에 쌓인 택배 수. 팔레트가 아니면 0.</summary>
        public int PalletCount(int x, int y)
        {
            if (!grid.InBounds(x, y)) return 0;
            var c = grid.Get(x, y);
            return c.Type == CellType.Pallet ? c.Pallet.Count : 0;
        }

        /// <summary>팔레트 맨 위(마지막에 쌓인) 택배를 꺼낸다. 비었거나 팔레트가 아니면 null. 소실로 세지 않는다.</summary>
        public Package TakeFromPallet(int x, int y)
        {
            if (!grid.InBounds(x, y)) return null;
            var c = grid.Get(x, y);
            if (c.Type != CellType.Pallet || c.Pallet.Count == 0) return null;
            var p = c.Pallet[c.Pallet.Count - 1];
            c.Pallet.RemoveAt(c.Pallet.Count - 1);
            return p;
        }

        /// <summary>들고 있던 택배를 팔레트 더미에 올린다. 팔레트가 아니거나 가득 찼거나 해체 중이면 false.</summary>
        public bool PutOnPallet(Package p, int x, int y)
        {
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            if (!grid.InBounds(x, y)) return false;
            var c = grid.Get(x, y);
            if (c.Type != CellType.Pallet || c.IsPalletFull || c.Demolishing) return false;
            Stack(p, c, x, y);
            return true;
        }

        private static void Stack(Package p, Cell pallet, int x, int y)
        {
            p.X = x;
            p.Y = y;
            p.Progress = 0f;
            p.EntryHeading = p.Heading;
            pallet.Pallet.Add(p);
        }

        /// <summary>분배기의 연결된 출구(스펙 11.6 판정, 11.7에서도 그대로). 분배기 방향 기준 왼쪽 → 앞 → 오른쪽 → 뒤 순서로,
        /// (a) 택배가 들어온 방향의 정반대가 아니고(incoming이 null이면 제외 없음) (b) 격자 안이며
        /// (c) 택배를 받는 칸(레일·고속·검사대·자동 검사기·분배기, 팔레트, 트럭 도크)인 이웃 방향만 남긴다.</summary>
        public static List<Direction> SplitterOutputs(FactoryGrid grid, int x, int y, Direction? incoming)
        {
            var result = new List<Direction>(4);
            var d = grid.Get(x, y).Direction;
            foreach (var cand in new[] { d.TurnLeft(), d, d.TurnRight(), d.Opposite() })
            {
                if (incoming.HasValue && cand == incoming.Value.Opposite()) continue;
                int nx = x + cand.Dx(), ny = y + cand.Dy();
                if (!grid.InBounds(nx, ny)) continue;
                if (AcceptsFromSplitter(grid.Get(nx, ny))) result.Add(cand);
            }
            return result;
        }

        /// <summary>분배기 = 분류기(스펙 11.7, 11.8). 파란 출구 = 분배기 Direction(연결 여부는 blueConnected),
        /// 빨간 출구(라우팅에 쓰는 것, 없으면 null) = 명시 RedDirection이 파랑과 다르면 그것(연결돼 있을 때만, 아니면 null),
        /// 명시 설정이 없거나 파랑과 같으면 자동 규칙(파란 기준 오른쪽 → 왼쪽 → 뒤 중 처음으로 연결된 출구).
        /// 연결 판정은 SplitterOutputs와 같다(incoming이 null이면 들어온 방향 제외 없음 — 뷰·프롬프트용).</summary>
        public static void SplitterRoutes(FactoryGrid grid, int x, int y, Direction? incoming,
            out Direction blue, out Direction? red, out bool blueConnected)
        {
            SplitterRoutes(grid, x, y, incoming, out blue, out red, out blueConnected, out var redConnected);
            if (!redConnected) red = null;
        }

        /// <summary>뷰용 변형(스펙 11.8). red는 설정된 빨간 출구다: 명시 RedDirection(파랑과 다를 때)이면 연결 여부와 무관하게 그 방향,
        /// 아니면 자동 규칙의 결과(없으면 null). redConnected가 false면 라우팅은 "빨강 없음"으로 취급한다(뷰는 흐린 화살표).</summary>
        public static void SplitterRoutes(FactoryGrid grid, int x, int y, Direction? incoming,
            out Direction blue, out Direction? red, out bool blueConnected, out bool redConnected)
        {
            var cell = grid.Get(x, y);
            blue = cell.Direction;
            var connected = SplitterOutputs(grid, x, y, incoming);
            blueConnected = connected.Contains(blue);
            if (cell.RedDirection.HasValue && cell.RedDirection.Value != blue)
            {
                red = cell.RedDirection.Value;
                redConnected = connected.Contains(red.Value);
                return;
            }
            red = null;
            redConnected = false;
            foreach (var cand in new[] { blue.TurnRight(), blue.TurnLeft(), blue.Opposite() })
                if (connected.Contains(cand)) { red = cand; redConnected = true; break; }
        }

        /// <summary>칸의 방향 설정이 바뀌었을 때(스펙 11.8 재설정) 그 칸 위 택배의 Heading을 다시 정한다. Progress는 유지한다.
        /// 분배기는 EntryHeading을 들어온 방향으로 보고 분류 규칙을 다시 적용한다.</summary>
        public void RefreshHeadingsAt(int x, int y)
        {
            if (!grid.InBounds(x, y)) return;
            foreach (var p in packages)
                if (p.X == x && p.Y == y)
                    p.Heading = HeadingFor(x, y, p.EntryHeading, p);
        }

        /// <summary>분배기가 출구로 삼는 칸. 반입구(Inlet)는 트랙이지만 들어가면 소실되므로 제외한다.</summary>
        private static bool AcceptsFromSplitter(Cell c) =>
            (c.IsTrack && c.Type != CellType.Inlet) || c.Type == CellType.Pallet || c.IsDock;

        /// <summary>칸에서 나갈 방향. 분배기는 검사 결과로 분류한다(스펙 11.7): 거부 판정 → 빨간 출구(없으면 파란 출구),
        /// 승인·미검사 → 파란 출구. 파란 출구가 연결돼 있지 않으면 분배기 Direction으로 직진(그 앞이 비면 소실).
        /// package가 null이면(아직 만들기 전) 미검사로 본다.</summary>
        private Direction HeadingFor(int x, int y, Direction incoming, Package package)
        {
            var cell = grid.Get(x, y);
            if (cell.Type != CellType.Splitter) return cell.Direction;
            SplitterRoutes(grid, x, y, incoming, out var blue, out var red, out var blueConnected);
            bool rejected = package != null && package.Inspected && package.Rejected;
            if (rejected && red.HasValue) return red.Value;
            return blueConnected ? blue : cell.Direction;
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
                if (next.Type == CellType.Pallet)
                {
                    if (next.IsPalletFull || next.Demolishing) continue; // 대기
                    cell.OccupantId = -1;
                    packages.RemoveAt(i);
                    i--;
                    Stack(p, next, nx, ny); // 흐름에서 빠져 더미로. 팔레트에서 레일로 유출은 없다.
                    continue;
                }
                if (!next.IsTrack || next.Type == CellType.Inlet) { Lose(i, p); i--; continue; }
                if (next.IsOccupied || next.Demolishing) continue; // 대기

                cell.OccupantId = -1;
                next.OccupantId = p.Id;
                p.X = nx;
                p.Y = ny;
                p.Progress = 0f;
                p.EntryHeading = p.Heading;
                p.Heading = HeadingFor(nx, ny, p.Heading, p);
                if (next.Type == CellType.Scanner && !p.Inspected) { p.Inspect(p.Bad); NoteInspected(); }
            }
        }

        /// <summary>칸 위의 택배를 모두 소실 처리한다. 팔레트 더미도 비운다(해체는 더미가 있으면 막히지만 안전망).</summary>
        public void RemovePackagesAt(int x, int y)
        {
            for (int i = packages.Count - 1; i >= 0; i--)
                if (packages[i].X == x && packages[i].Y == y)
                    Lose(i, packages[i]);
            if (!grid.InBounds(x, y)) return;
            var pile = grid.Get(x, y).Pallet;
            while (pile.Count > 0)
            {
                var p = pile[pile.Count - 1];
                pile.RemoveAt(pile.Count - 1);
                LostCount++;
                PackageLost?.Invoke(p);
            }
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
