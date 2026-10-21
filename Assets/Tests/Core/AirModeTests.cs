using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 14.5: 분배기·자동 검사기의 항공 모드(Cell.Mode), 검사기 항공 출구, 막힌 출구 대기, 부지 확장 2 조건, 저장 v7.</summary>
    public class AirModeTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private ConveyorSim sim;
        private BuildService build;
        private List<(int, int)> changed;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(5000);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            build = new BuildService(grid, economy, toolbar, sim);
            changed = new List<(int, int)>();
            build.CellChanged += (x, y) => changed.Add((x, y));
        }

        private void Put(int x, int y, Direction d, CellType type = CellType.Conveyor) => grid.Place(x, y, type, d);

        private int nextId = 500;

        private Package Make(bool air, bool? rejected, bool bad = false)
        {
            var p = new PackageFactory(new Random(nextId), bad ? 1.0 : 0.0).Create(nextId++, 0, 0, Direction.East);
            p.Air = air;
            if (rejected.HasValue) p.Inspect(rejected.Value);
            return p;
        }

        /// <summary>(2,3) 동향 레일 → (3,3) 동향 칸(분배기·검사기), 동 (4,3)·남 (3,2)·북 (3,4) 레일.</summary>
        private void Hub(CellType type, bool south = true, bool north = true, bool east = true)
        {
            grid.UnlockRegion(12);
            Put(2, 3, Direction.East);
            Put(3, 3, Direction.East, type);
            if (east) Put(4, 3, Direction.East);
            if (south) Put(3, 2, Direction.South);
            if (north) Put(3, 4, Direction.North);
        }

        /// <summary>p를 (2,3)에 놓고 허브를 지나 한 칸 나갈 때까지 돌린 뒤 위치를 돌려준다.</summary>
        private (int, int) Route(Package p)
        {
            sim.PlaceCarried(p, 2, 3);
            sim.Tick(1f); // 허브로
            sim.Tick(1f); // 허브에서 나감
            return (p.X, p.Y);
        }

        // ---- 모드 전환 ----

        [Test]
        public void NewCells_DefaultToInspectMode()
        {
            Hub(CellType.Splitter);
            Assert.AreEqual(RouteMode.Inspect, grid.Get(3, 3).Mode);
            Assert.AreEqual(RouteMode.Inspect, grid.Get(4, 3).Mode);
        }

        [Test]
        public void SetMode_RequiresPlot2()
        {
            Put(2, 2, Direction.East, CellType.Splitter);
            Put(3, 2, Direction.East, CellType.Scanner);
            Assert.AreEqual(ReconfigureResult.RequiresPlot2, build.SetMode(2, 2, RouteMode.Air));
            Assert.AreEqual(ReconfigureResult.RequiresPlot2, build.SetMode(3, 2, RouteMode.Air));
            Assert.AreEqual(RouteMode.Inspect, grid.Get(2, 2).Mode);
            Assert.AreEqual(0, changed.Count);
            grid.UnlockRegion(9);
            Assert.AreEqual(ReconfigureResult.RequiresPlot2, build.SetMode(2, 2, RouteMode.Air));
            grid.UnlockRegion(12);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(2, 2, RouteMode.Air));
            Assert.AreEqual(RouteMode.Air, grid.Get(2, 2).Mode);
            CollectionAssert.Contains(changed, (2, 2));
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(2, 2, RouteMode.Inspect));
            Assert.AreEqual(RouteMode.Inspect, grid.Get(2, 2).Mode);
        }

        [Test]
        public void SetMode_OnlySplitterAndScanner()
        {
            grid.UnlockRegion(12);
            Put(2, 2, Direction.East);
            Put(3, 3, Direction.East, CellType.Table);
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetMode(2, 2, RouteMode.Air));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetMode(3, 3, RouteMode.Air));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetMode(11, 7, RouteMode.Air));
            Put(4, 4, Direction.East, CellType.Splitter);
            grid.Get(4, 4).Demolishing = true;
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetMode(4, 4, RouteMode.Air));
        }

        [Test]
        public void Place_And_Clear_ResetMode()
        {
            grid.UnlockRegion(12);
            Put(2, 2, Direction.East, CellType.Scanner);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(2, 2, RouteMode.Air));
            grid.Clear(2, 2);
            Assert.AreEqual(RouteMode.Inspect, grid.Get(2, 2).Mode);
            Assert.IsNull(grid.Get(2, 2).AirDirection);
        }

        // ---- 분배기 ----

        [Test]
        public void Splitter_InspectMode_Unchanged()
        {
            Hub(CellType.Splitter);
            Assert.AreEqual((4, 3), Route(Make(false, false)));
            Assert.AreEqual((3, 2), Route(Make(false, true)));   // 빨강 자동 = 오른쪽(남)
            Assert.AreEqual((3, 2), Route(Make(true, true)));    // 검사 모드는 운송 수단을 보지 않는다
            Assert.AreEqual((4, 3), Route(Make(true, null)));
        }

        [Test]
        public void Splitter_AirMode_RoutesExactlyLikeInspectMode()
        {
            // 분배기 항공 모드(스펙 14.5 사용자 수정): 1번 = 승인, 2번 = 거부. 운송 수단은 보지 않는다(색만 초록/노랑).
            Hub(CellType.Splitter);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(3, 3, RouteMode.Air));
            foreach (var air in new[] { false, true })
            {
                Assert.AreEqual((4, 3), Route(Make(air, false)), "approved air=" + air);
                Assert.AreEqual((3, 2), Route(Make(air, true)), "rejected air=" + air);
                Assert.AreEqual((4, 3), Route(Make(air, null)), "uninspected air=" + air);
            }
            Assert.AreEqual(ReconfigureResult.Ok, build.SetSplitterRed(3, 3, Direction.North));
            Assert.AreEqual((3, 4), Route(Make(true, true)));
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(3, 3, RouteMode.Inspect));
            Assert.AreEqual(Direction.North, grid.Get(3, 3).RedDirection);
            Assert.AreEqual((3, 4), Route(Make(false, true)));
        }

        [Test]
        public void Splitter_AirMode_KeepsExistingLossRule()
        {
            // 출구가 없는 쪽으로 가면 검사 모드와 같이 소실된다(분배기 규칙 그대로).
            Hub(CellType.Splitter, south: false, north: false, east: false);
            build.SetMode(3, 3, RouteMode.Air);
            Route(Make(false, false));
            sim.Tick(1f);
            Assert.AreEqual(1, sim.LostCount);
        }

        // ---- 자동 검사기 ----

        [Test]
        public void Scanner_AirMode_DefaultAirExitIsRightThenLeft()
        {
            Hub(CellType.Scanner);
            Assert.IsNull(grid.Get(3, 3).AirDirection);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(3, 3, RouteMode.Air));
            Assert.AreEqual(Direction.South, grid.Get(3, 3).AirDirection); // 동향의 오른쪽

            Hub2(south: false);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(6, 6, RouteMode.Air));
            Assert.AreEqual(Direction.North, grid.Get(6, 6).AirDirection); // 오른쪽이 나갈 수 없으면 왼쪽

            // 모드를 다시 바꿔도 처음 정한 항공 출구는 유지
            build.SetMode(3, 3, RouteMode.Inspect);
            build.SetMode(3, 3, RouteMode.Air);
            Assert.AreEqual(Direction.South, grid.Get(3, 3).AirDirection);
        }

        [Test]
        public void Scanner_DefaultAirExit_EdgeOfGrid_UsesLeft()
        {
            grid.UnlockRegion(12);
            Put(5, 0, Direction.East, CellType.Scanner); // 오른쪽(남)은 격자 밖
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(5, 0, RouteMode.Air));
            Assert.AreEqual(Direction.North, grid.Get(5, 0).AirDirection);
        }

        /// <summary>(6,6) 동향 검사기: 동 (7,6), 북 (6,7), 남은 south일 때만.</summary>
        private void Hub2(bool south)
        {
            Put(6, 6, Direction.East, CellType.Scanner);
            Put(7, 6, Direction.East);
            Put(6, 7, Direction.North);
            if (south) Put(6, 5, Direction.South);
        }

        [Test]
        public void Scanner_AirMode_InspectsThenSortsByCarrier()
        {
            Hub(CellType.Scanner);
            build.SetMode(3, 3, RouteMode.Air);
            var air = Make(true, null, bad: true);
            Assert.AreEqual((3, 2), Route(air));
            Assert.IsTrue(air.Inspected);
            Assert.IsTrue(air.Rejected);
            var ground = Make(false, null);
            Assert.AreEqual((4, 3), Route(ground));
            Assert.IsTrue(ground.Inspected);
            Assert.IsFalse(ground.Rejected);
            Assert.AreEqual(2, sim.InspectedCount);

            Assert.AreEqual(ReconfigureResult.Ok, build.SetScannerAirExit(3, 3, Direction.North));
            Assert.AreEqual((3, 4), Route(Make(true, null)));
            // 검사 모드: 출구 하나(기존 방향)
            build.SetMode(3, 3, RouteMode.Inspect);
            Assert.AreEqual((4, 3), Route(Make(true, null)));
        }

        [Test]
        public void Scanner_SetAirExit_Rules()
        {
            Hub(CellType.Scanner);
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetScannerAirExit(3, 3, Direction.East)); // 1번 출구와 같음
            Assert.AreEqual(ReconfigureResult.Ok, build.SetScannerAirExit(3, 3, Direction.North));
            Assert.AreEqual(Direction.North, grid.Get(3, 3).AirDirection);
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetScannerAirExit(2, 3, Direction.North));
            // 1번 출구를 항공 출구 쪽으로 돌리면 항공 출구는 기본 규칙(오른쪽 → 왼쪽)으로 계산한다.
            toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(3, 3, Direction.North));
            ConveyorSim.ScannerRoutes(grid, 3, 3, out var groundExit, out var airExit);
            Assert.AreEqual(Direction.North, groundExit);
            Assert.AreEqual(Direction.East, airExit); // 북향의 오른쪽 = 동 (4,3) 레일
        }

        [Test]
        public void Scanner_SetDirectionOntoAirExit_MovesAirExitAndReroutesWaitingParcel()
        {
            Hub(CellType.Scanner, south: false, north: false, east: false);
            toolbar.Add(CellType.Scanner, Direction.East);
            Put(3, 2, Direction.South); // 남쪽만 받을 수 있다
            build.SetMode(3, 3, RouteMode.Air);
            Assert.AreEqual(Direction.South, grid.Get(3, 3).AirDirection);
            var a = Make(true, null);
            sim.PlaceCarried(a, 3, 3); // 항공 → 남
            Put(3, 4, Direction.North);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetScannerAirExit(3, 3, Direction.North));
            Assert.AreEqual(Direction.North, a.Heading, "waiting parcel follows the new air exit");
            // 1번 출구를 항공 출구(북) 쪽으로 돌리면 항공 출구는 새 1번 기준 기본값(북향의 오른쪽 = 동, 받을 수 없으면 왼쪽 = 서 → 서는 레일 (2,3))
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(3, 3, Direction.North));
            Assert.AreEqual(Direction.West, grid.Get(3, 3).AirDirection);
            Assert.AreEqual(Direction.West, a.Heading);
        }

        [Test]
        public void Scanner_Hold_RaisesBlockedOncePerBlock()
        {
            Hub(CellType.Scanner, south: false, north: false);
            build.SetMode(3, 3, RouteMode.Air); // 항공 출구 남(받을 곳 없음)
            var events = new List<(int, int)>();
            sim.ScannerBlocked += (x, y) => events.Add((x, y));
            var a = Make(true, null);
            sim.PlaceCarried(a, 2, 3);
            for (int i = 0; i < 6; i++) sim.Tick(0.5f);
            Assert.AreEqual(1, events.Count, "once per block");
            Assert.AreEqual((3, 3), events[0]);
            Put(3, 2, Direction.South); // 풀리면 나간다
            sim.Tick(1f);
            Assert.AreEqual((3, 2), (a.X, a.Y));
            grid.Clear(3, 2);
            sim.Take(a);
            var b = Make(true, null);
            sim.PlaceCarried(b, 2, 3);
            for (int i = 0; i < 6; i++) sim.Tick(0.5f);
            Assert.AreEqual(2, events.Count, "blocks again after a release");
        }

        [Test]
        public void Scanner_AirMode_HandPlacedIsInspectedAndRouted()
        {
            Hub(CellType.Scanner);
            build.SetMode(3, 3, RouteMode.Air);
            var a = Make(true, null);
            sim.PlaceCarried(a, 3, 3);
            Assert.IsTrue(a.Inspected);
            Assert.AreEqual(Direction.South, a.Heading);
        }

        [Test]
        public void Scanner_AirMode_BlockedAirExitWaits()
        {
            Hub(CellType.Scanner, south: false, north: false);
            build.SetMode(3, 3, RouteMode.Air); // 항공 출구 기본 = 남(나갈 곳 없음)
            var a = Make(true, null);
            sim.PlaceCarried(a, 2, 3);
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual((3, 3), (a.X, a.Y));
            Assert.AreEqual(0, sim.LostCount);
        }

        // ---- 12×12 자동화 ----

        [Test]
        public void FullAutomation_FourTrucks_AllSucceed()
        {
            grid.UnlockRegion(12);
            foreach (var d in new[] { Direction.North, Direction.South, Direction.West }) toolbar.Add(CellType.Conveyor, d);
            toolbar.Add(CellType.Splitter, Direction.East);
            toolbar.Add(CellType.Scanner, Direction.East);
            grid.Clear(5, 2); grid.Clear(5, 4); // 시작 팔레트
            BuildAutomationLine(build, grid);

            var kinds = new[] { (false, false), (false, true), (true, false), (true, true) };
            int money0 = economy.Money;
            var seen = new List<(Package, DeliveryOutcome)>();
            sim.PackageDelivered += (p, o, d) => seen.Add((p, o));
            foreach (var (air, bad) in kinds)
            {
                var p = Make(air, null, bad);
                sim.PlaceCarried(p, 1, 2);
                for (int i = 0; i < 60 && sim.Packages.Count > 0; i++) sim.Tick(0.5f);
                Assert.AreEqual(0, sim.Packages.Count, $"air {air} bad {bad} did not arrive");
            }
            Assert.AreEqual(4, seen.Count);
            Assert.AreEqual(DeliveryOutcome.Delivered, seen[0].Item2);
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, seen[1].Item2);
            Assert.AreEqual(DeliveryOutcome.Delivered, seen[2].Item2);
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, seen[3].Item2);
            Assert.AreEqual(money0 + 30 + 10 + 30 + 10, economy.Money);
            Assert.AreEqual(0, sim.LostCount);
        }

        /// <summary>
        /// 자동화 줄(스크린샷과 같은 배치): 입고 → (1,2)(2,2) → 검사기 (3,2) 항공 모드(항공 출구 북) →
        /// 일반: (4..9,2) → 분배기 (10,2) → 출고 (11,2) / 거부 북 (10,3) → (10,4) 동 → 반품 (11,4).
        /// 항공: (3,3..6) 북 → (3,7) 동 → (4..9,7) → 분배기 (10,7) 항공 모드 → 항공 출고 (11,7) / 거부 북 (10,8) → (10,9) 동 → 항공 반품 (11,9).
        /// </summary>
        public static void BuildAutomationLine(BuildService build, FactoryGrid grid)
        {
            void P(int x, int y, CellType t, Direction d) => Assert.AreEqual(BuildResult.Ok, build.TryPlace(x, y, t, d), $"{t} at ({x},{y})");
            P(1, 2, CellType.Conveyor, Direction.East);
            P(2, 2, CellType.Conveyor, Direction.East);
            P(3, 2, CellType.Scanner, Direction.East);
            for (int x = 4; x <= 9; x++) P(x, 2, CellType.Conveyor, Direction.East);
            P(10, 2, CellType.Splitter, Direction.East);
            P(10, 3, CellType.Conveyor, Direction.North);
            P(10, 4, CellType.Conveyor, Direction.East);
            for (int y = 3; y <= 6; y++) P(3, y, CellType.Conveyor, Direction.North);
            P(3, 7, CellType.Conveyor, Direction.East);
            for (int x = 4; x <= 9; x++) P(x, 7, CellType.Conveyor, Direction.East);
            P(10, 7, CellType.Splitter, Direction.East);
            P(10, 8, CellType.Conveyor, Direction.North);
            P(10, 9, CellType.Conveyor, Direction.East);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(10, 7, RouteMode.Air)); // 항공 줄 분배기: 초록/노랑
            Assert.AreEqual(ReconfigureResult.Ok, build.SetMode(3, 2, RouteMode.Air));
            Assert.AreEqual(ReconfigureResult.Ok, build.SetScannerAirExit(3, 2, Direction.North));
        }

        // ---- 저장 v7 ----

        [Test]
        public void Save_RoundTripsModeAndScannerAirExit()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Toolbar.Add(CellType.Splitter, Direction.East);
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            s.Build.TryPlace(2, 2, CellType.Splitter, Direction.East);
            s.Build.TryPlace(3, 3, CellType.Scanner, Direction.East);
            s.Build.TryPlace(4, 4, CellType.Scanner, Direction.East);
            s.Build.SetMode(2, 2, RouteMode.Air);
            s.Build.SetMode(3, 3, RouteMode.Air);
            s.Build.SetScannerAirExit(3, 3, Direction.North);
            var json = SaveSerializer.ToJson(SaveSerializer.Capture(s));
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));
            Assert.AreEqual(RouteMode.Air, r.Grid.Get(2, 2).Mode);
            Assert.AreEqual(RouteMode.Air, r.Grid.Get(3, 3).Mode);
            Assert.AreEqual(Direction.North, r.Grid.Get(3, 3).AirDirection);
            Assert.AreEqual(RouteMode.Inspect, r.Grid.Get(4, 4).Mode);
            Assert.IsNull(r.Grid.Get(4, 4).AirDirection);
        }

        [Test]
        public void Save_MissingModeReadsAsInspect_AndGarbageIsRejected()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            s.Build.TryPlace(3, 3, CellType.Scanner, Direction.East);
            s.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East);
            var st = SaveSerializer.Capture(s);
            foreach (var c in st.Cells) { c.Mode = null; c.AirDirection = null; }
            st.Version = 6;
            var r = SaveSerializer.Restore(st);
            Assert.AreEqual(RouteMode.Inspect, r.Grid.Get(3, 3).Mode);

            var bad = SaveSerializer.Capture(s);
            bad.Cells.Find(c => c.Type == "Conveyor").Mode = "Air";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            bad = SaveSerializer.Capture(s);
            bad.Cells.Find(c => c.Type == "Conveyor").AirDirection = "North";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            bad = SaveSerializer.Capture(s);
            bad.Cells.Find(c => c.Type == "Scanner").Mode = "Sideways";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
        }
    }
}
