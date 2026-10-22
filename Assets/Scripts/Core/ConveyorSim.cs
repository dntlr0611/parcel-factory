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
        /// <summary>새 택배를 만드는 팩토리. GameSession이 부지 확장 2 이후 AirChance를 켠다(스펙 14.2).</summary>
        public PackageFactory Factory => factory;
        /// <summary>동쪽 트럭 네 대의 적재량·교대(스펙 18). 도크 판정 때마다 실고, 떠나 있는 도크로는 보내지 않는다.</summary>
        public TruckFleet Trucks { get; } = new TruckFleet();
        /// <summary>가장 최근 판정이 난 도크 종류. PackageDelivered 핸들러 안에서 그 판정의 도크다(금액 팝업 위치용).</summary>
        public CellType LastDeliveredDock { get; private set; } = CellType.Empty;
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
        /// <summary>취급주의 택배가 파손된 순간(스펙 17.2). Tick·Move 중간일 수 있으므로 핸들러는 읽기만 해야 한다.</summary>
        public event Action<Package> PackageBroken;

        /// <summary>
        /// 취급주의 택배를 파손시킨다(스펙 17.2): Broken, 실제 불량(Bad)이 되고 검사 결과를 지운다(다시 검사해야 한다). 운송장·스티커는 그대로.
        /// 취급주의가 아니거나 이미 파손됐으면 false. 파손 조건(고속 레일 진입·들고 달리기·위에 2개 이상)은 호출하는 쪽이 판단한다.
        /// </summary>
        public bool Break(Package p)
        {
            if (p == null || !p.Fragile || p.Broken) return false;
            p.Broken = true;
            p.Bad = true;
            p.Inspected = false;
            p.Rejected = false;
            PackageBroken?.Invoke(p);
            return true;
        }

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
            grid.Get(package.X, package.Y).SetOccupant(package.Lane, package.Id);
        }

        /// <summary>칸 위의 택배. 브릿지는 윗길을 먼저, 없으면 아랫길(스펙 11.12).</summary>
        public Package PackageAt(int x, int y)
        {
            Package lower = null;
            foreach (var p in packages)
            {
                if (p.X != x || p.Y != y) continue;
                if (p.Lane == Lane.Upper) return p;
                lower = p;
            }
            return lower;
        }

        /// <summary>칸 위의 택배 전부(0..2, 브릿지만 2). 윗길 → 아랫길 순서.</summary>
        public IReadOnlyList<Package> PackagesAt(int x, int y)
        {
            var result = new List<Package>(2);
            foreach (var p in packages)
                if (p.X == x && p.Y == y && p.Lane == Lane.Upper) result.Add(p);
            foreach (var p in packages)
                if (p.X == x && p.Y == y && p.Lane == Lane.Lower) result.Add(p);
            return result;
        }

        /// <summary>브릿지 진입 차선(스펙 11.12): 진행 방향이 브릿지 Direction과 평행(같거나 반대)이면 윗길, 수직이면 아랫길.
        /// 브릿지가 아니면 항상 Upper.</summary>
        public static Lane EntryLane(Cell cell, Direction travel)
        {
            if (cell.Type != CellType.Bridge) return Lane.Upper;
            return travel == cell.Direction || travel == cell.Direction.Opposite() ? Lane.Upper : Lane.Lower;
        }

        /// <summary>택배가 있는 칸에서 자기 차선의 점유를 푼다.</summary>
        private void Release(Package p) => grid.Get(p.X, p.Y).SetOccupant(p.Lane, -1);

        /// <summary>검사대 위에서 검사를 기다리는 택배인가.</summary>
        public bool IsAwaitingInspection(Package p) =>
            !p.Inspected && grid.Get(p.X, p.Y).Type == CellType.Table;

        /// <summary>운송장 부착대 위에서 운송장을 기다리는 택배인가(스펙 15.4): 운송장이 없으면 검사 여부와 상관없이 멈춘다.
        /// 취급주의 택배는 스티커도 붙어야 지나간다(스펙 17.3).</summary>
        public bool IsAwaitingLabel(Package p) =>
            NeedsLabel(p) && grid.Get(p.X, p.Y).Type == CellType.Labeler;

        /// <summary>부착대에서 붙일 것이 남았는가: 운송장이 없거나, 취급주의인데 스티커가 없다(스펙 17.3).</summary>
        public static bool NeedsLabel(Package p) => !p.Waybill || (p.Fragile && !p.FragileSticker);

        /// <summary>운송장 부착대에서 운송장을 기다리는 택배 수(HUD 칩 "운송장 대기 n").</summary>
        public int AwaitingLabelCount
        {
            get { int n = 0; foreach (var p in packages) if (IsAwaitingLabel(p)) n++; return n; }
        }

        /// <summary>항공 모드 검사기가 택배를 붙잡기 시작한 틱에 한 번 발생한다(스펙 14.5). 그 검사기가 풀렸다가 다시 막히면 또 발생한다.
        /// Tick 끝에서 발생하므로 핸들러는 읽기만 해야 한다.</summary>
        public event Action<int, int> ScannerBlocked;
        private HashSet<(int, int)> holding = new HashSet<(int, int)>(), holdingNext = new HashSet<(int, int)>();

        public int AwaitingInspectionCount
        {
            get { int n = 0; foreach (var p in packages) if (IsAwaitingInspection(p)) n++; return n; }
        }

        /// <summary>작업자가 집는다. 소실로 세지 않는다.</summary>
        public void Take(Package p)
        {
            int i = packages.IndexOf(p);
            if (i < 0) throw new InvalidOperationException($"package {p.Id} is not on the line");
            Release(p);
            p.Lane = Lane.Upper;
            packages.RemoveAt(i);
        }

        /// <summary>작업자가 들고 있던 택배를 트랙 칸에 내려놓는다.</summary>
        public void PlaceCarried(Package p, int x, int y)
        {
            if (!grid.InBounds(x, y)) throw new InvalidOperationException($"({x},{y}) out of bounds");
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || !cell.HasFreeLane || cell.Demolishing)
                throw new InvalidOperationException($"cannot put package at ({x},{y})");
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            p.X = x;
            p.Y = y;
            p.Progress = 0f;
            if (cell.Type == CellType.Bridge)
            {
                // 빈 차선 중 윗길 우선. 아랫길에 놓으면 Direction 기준 오른쪽으로 직진한다(스펙 11.12).
                p.Lane = cell.IsOccupied ? Lane.Lower : Lane.Upper;
                p.Heading = p.Lane == Lane.Upper ? cell.Direction : cell.Direction.TurnRight();
            }
            else
            {
                p.Lane = Lane.Upper;
                p.Heading = HeadingFor(x, y, cell.Direction, p); // 분배기에 손으로 놓아도 같은 분류 규칙
            }
            p.EntryHeading = p.Heading;
            cell.SetOccupant(p.Lane, p.Id);
            packages.Add(p);
            if (cell.Type == CellType.Scanner) Scan(p); // 스캐너는 놓는 순간에도 판정한다
            if (cell.Type == CellType.FastConveyor) Break(p); // 손으로 고속 레일에 올려도 고속 레일 칸에 들어간 것이다(스펙 17.2)
        }

        /// <summary>작업자가 들고 있던 택배를 도크에 직접 넣는다. 판정 증감을 돌려준다. 그 도크의 트럭이 떠나 있으면 InvalidOperationException
        /// (WorkerService.Preview가 TruckAway로 먼저 막는다, 스펙 18).</summary>
        public int DeliverCarried(Package p, CellType dockType)
        {
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            if (Trucks.IsAway(dockType)) throw new InvalidOperationException($"the {dockType} truck is away");
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

        /// <summary>팔레트 더미에서 같은 자리 위층에 이 개수 이상이 쌓이면 아래의 취급주의 택배가 파손된다(스펙 17.2).</summary>
        public const int CrushAbove = 2;

        /// <summary>더미에서 인덱스 i 상자의 같은 자리 위층에 놓인 상자 수(j > i, (j − i) % PalletSlots == 0). 화면에 그려진 쌓임 그대로다.</summary>
        public static int StackedAbove(int count, int i) => Math.Max(0, (count - 1 - i) / Cell.PalletSlots);

        /// <summary>더미에 올린다(손·레일 모두 이 길). 같은 자리 위층에 CrushAbove개 이상 쌓인 취급주의 택배는 파손된다
        /// (2자리 × 3층이라 실제로는 1층 상자가 그 자리에 3층 상자가 올라오는 순간).</summary>
        private void Stack(Package p, Cell pallet, int x, int y)
        {
            p.X = x;
            p.Y = y;
            p.Progress = 0f;
            p.EntryHeading = p.Heading;
            p.Lane = Lane.Upper;
            pallet.Pallet.Add(p);
            for (int i = 0; i < pallet.Pallet.Count; i++)
                if (StackedAbove(pallet.Pallet.Count, i) >= CrushAbove) Break(pallet.Pallet[i]);
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
            var cell = grid.Get(x, y);
            foreach (var p in packages)
            {
                if (p.X != x || p.Y != y) continue;
                if (cell.Type == CellType.Bridge) p.Heading = BridgeHeading(cell, p.Lane, p.EntryHeading);
                else p.Heading = HeadingFor(x, y, p.EntryHeading, p);
            }
        }

        /// <summary>브릿지 위 차선의 진행 방향. 윗길 = Direction. 아랫길 = 들어온 방향 그대로 직진하되,
        /// 재설정으로 그 방향이 윗길 축과 평행해졌으면 아랫길 축의 Direction 기준 오른쪽.</summary>
        private static Direction BridgeHeading(Cell bridge, Lane lane, Direction entry)
        {
            if (lane == Lane.Upper) return bridge.Direction;
            return EntryLane(bridge, entry) == Lane.Lower ? entry : bridge.Direction.TurnRight();
        }

        /// <summary>운송 수단(항공/일반)으로 가르는 칸 = 항공 모드 자동 검사기(스펙 14.5). 이 칸에서는 정해진 출구가 막히거나
        /// 받을 수 없는 칸이면 소실하지 않고 기다린다(다른 출구로 새지 않는다). 항공 모드 분배기는 검사 모드와 같은 규칙이라 해당 없다.</summary>
        public static bool IsCarrierSorter(Cell c) => c.Mode == RouteMode.Air && c.Type == CellType.Scanner;

        /// <summary>자동 검사기 항공 출구의 기본값(스펙 14.5): 1번 출구(Direction)의 오른쪽, 그 칸이 택배를 받을 수 없는 곳이면 왼쪽,
        /// 둘 다 아니면 격자 안쪽인 쪽(오른쪽 우선).</summary>
        public static Direction DefaultScannerAirExit(FactoryGrid grid, int x, int y)
        {
            var d = grid.Get(x, y).Direction;
            foreach (var cand in new[] { d.TurnRight(), d.TurnLeft() })
            {
                int nx = x + cand.Dx(), ny = y + cand.Dy();
                if (grid.InBounds(nx, ny) && AcceptsFromSplitter(grid.Get(nx, ny))) return cand;
            }
            var right = d.TurnRight();
            return grid.InBounds(x + right.Dx(), y + right.Dy()) ? right : d.TurnLeft();
        }

        /// <summary>자동 검사기의 두 출구(스펙 14.5): ground = 1번(Direction), air = 명시 AirDirection(1번과 다를 때) 또는 기본 규칙.
        /// 검사 모드에서는 air를 쓰지 않는다.</summary>
        public static void ScannerRoutes(FactoryGrid grid, int x, int y, out Direction ground, out Direction air)
        {
            var c = grid.Get(x, y);
            ground = c.Direction;
            air = c.AirDirection.HasValue && c.AirDirection.Value != ground ? c.AirDirection.Value : DefaultScannerAirExit(grid, x, y);
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
            if (cell.Type == CellType.Bridge) return BridgeHeading(cell, EntryLane(cell, incoming), incoming);
            if (cell.Type == CellType.Scanner)
            {
                if (cell.Mode != RouteMode.Air || package == null || !package.Air) return cell.Direction;
                ScannerRoutes(grid, x, y, out _, out var airExit);
                return airExit;
            }
            if (cell.Type != CellType.Splitter) return cell.Direction;
            // 분배기 항공 모드(스펙 14.5)는 색만 다르고 가르는 규칙은 검사 모드와 같다.
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
                if (IsAwaitingLabel(p)) continue; // 운송장을 붙이기 전까지 부착대에서 대기(스펙 15.1)

                int nx = p.X + p.Heading.Dx();
                int ny = p.Y + p.Heading.Dy();
                bool hold = IsCarrierSorter(cell); // 항공 모드 검사기는 나갈 곳이 없으면 기다린다(스펙 14.5)
                if (!grid.InBounds(nx, ny)) { if (hold) continue; Lose(i, p); i--; continue; }

                var next = grid.Get(nx, ny);
                if (next.IsDock)
                {
                    if (Trucks.IsAway(next.Type)) continue; // 트럭이 떠나 있으면 도크 앞에서 기다린다(소실 아님, 스펙 18)
                    Deliver(i, p, next.Type); i--; continue;
                }
                if (next.Type == CellType.Pallet)
                {
                    if (next.IsPalletFull || next.Demolishing) continue; // 대기
                    Release(p);
                    packages.RemoveAt(i);
                    i--;
                    Stack(p, next, nx, ny); // 흐름에서 빠져 더미로. 팔레트에서 레일로 유출은 없다.
                    continue;
                }
                if (!next.IsTrack || next.Type == CellType.Inlet) { if (hold) continue; Lose(i, p); i--; continue; }
                var lane = EntryLane(next, p.Heading); // 브릿지면 평행 → 윗길, 수직 → 아랫길(스펙 11.12)
                if (next.OccupantOf(lane) >= 0 || next.Demolishing) continue; // 대기(차선별)

                Release(p);
                next.SetOccupant(lane, p.Id);
                p.Lane = lane;
                p.X = nx;
                p.Y = ny;
                p.Progress = 0f;
                p.EntryHeading = p.Heading;
                p.Heading = HeadingFor(nx, ny, p.Heading, p);
                if (next.Type == CellType.Scanner) Scan(p);
                if (next.Type == CellType.FastConveyor) Break(p); // 고속 레일 칸에 들어가는 순간 파손(스펙 17.2)
            }
            UpdateHolding();
        }

        /// <summary>틱 끝에 진행도 1로 남아 있는 항공 모드 검사기 위 택배 = 출구가 막혀 기다리는 중. 새로 막힌 검사기만 알린다.</summary>
        private void UpdateHolding()
        {
            holdingNext.Clear();
            foreach (var p in packages)
                if (p.Progress >= 1f && IsCarrierSorter(grid.Get(p.X, p.Y))) holdingNext.Add((p.X, p.Y));
            foreach (var k in holdingNext)
                if (!holding.Contains(k)) ScannerBlocked?.Invoke(k.Item1, k.Item2);
            var t = holding; holding = holdingNext; holdingNext = t;
        }

        /// <summary>항공 모드 검사기 (x, y)가 지금 택배를 붙잡고 있는가(마지막 틱 기준). 미니 상태바용.</summary>
        public bool IsHolding(int x, int y) => holding.Contains((x, y));

        /// <summary>자동 검사기: 아직 판정 안 된 택배를 정확히 판정하고(사람 판정은 그대로, 파손품은 Bad라 거부), 운송장과
        /// 취급주의 스티커도 붙인다(스펙 15.3, 17.3).</summary>
        private void Scan(Package p)
        {
            if (!p.Inspected) { p.Inspect(p.Bad); NoteInspected(); }
            p.Waybill = true;
            if (p.Fragile) p.FragileSticker = true;
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
            LastDeliveredDock = dockType;
            economy.Adjust(delta);
            if (DeliveryScoring.IsSuccess(outcome)) DeliveredCount++;
            PackageDelivered?.Invoke(p, outcome, delta);
            Trucks.Load(dockType); // 판정·돈은 그 순간, 30개째면 트럭 출발(스펙 18)
            return delta;
        }

        private void Deliver(int index, Package p, CellType dockType)
        {
            Release(p);
            packages.RemoveAt(index);
            ApplyDelivery(p, dockType);
        }

        private void Lose(int index, Package p)
        {
            Release(p);
            packages.RemoveAt(index);
            LostCount++;
            PackageLost?.Invoke(p);
        }
    }
}
