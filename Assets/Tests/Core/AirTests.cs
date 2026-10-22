using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 14: 항공 택배와 항공 트럭 — 도크 (11,7)·(11,9), 부지 확장 2 이후 25% 항공, WrongCarrier −10, 저장 v7.</summary>
    public class AirTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private ConveyorSim sim;
        private Toolbar toolbar;
        private BuildService build;
        private WorkerService ws;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(200);
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            toolbar = new Toolbar();
            build = new BuildService(grid, economy, toolbar, sim);
            ws = new WorkerService(grid, sim, build);
        }

        private int nextId = 1000;

        private Package Loose(bool air, bool bad = false)
        {
            var p = new PackageFactory(new Random(nextId), bad ? 1.0 : 0.0).Create(nextId++, 0, 0, Direction.East);
            p.Air = air;
            return p;
        }

        // ---- 도크 ----

        [Test]
        public void AirDockConstants()
        {
            Assert.AreEqual((11, 7), (FactoryGrid.AirOutletX, FactoryGrid.AirOutletY));
            Assert.AreEqual((11, 9), (FactoryGrid.AirReturnDockX, FactoryGrid.AirReturnDockY));
        }

        [Test]
        public void NewGrid_HasNoAirDocks()
        {
            Assert.AreEqual(CellType.Locked, grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.Locked, grid.Get(11, 9).Type);
            grid.UnlockRegion(9);
            Assert.AreEqual(CellType.Locked, grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.Locked, grid.Get(11, 9).Type);
        }

        [Test]
        public void UnlockRegion12_CreatesAirDocks()
        {
            grid.UnlockRegion(9);
            grid.UnlockRegion(12);
            Assert.AreEqual(CellType.AirOutlet, grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.AirReturnDock, grid.Get(11, 9).Type);
            Assert.IsTrue(grid.Get(11, 7).IsDock);
            Assert.IsTrue(grid.Get(11, 9).IsDock);
            Assert.IsFalse(grid.Get(11, 7).IsBuilding);
            Assert.IsFalse(grid.Get(11, 7).IsTrack);
            Assert.IsTrue(Cell.IsAirDock(CellType.AirOutlet));
            Assert.IsTrue(Cell.IsAirDock(CellType.AirReturnDock));
            Assert.IsFalse(Cell.IsAirDock(CellType.Outlet));
            // 기존 도크와 주변 빈 칸은 그대로
            Assert.AreEqual(CellType.Outlet, grid.Get(11, 2).Type);
            Assert.AreEqual(CellType.ReturnDock, grid.Get(11, 4).Type);
            Assert.AreEqual(CellType.Empty, grid.Get(11, 8).Type);
            Assert.AreEqual(CellType.Empty, grid.Get(10, 7).Type);
            grid.UnlockRegion(12); // 다시 불러도 그대로
            Assert.AreEqual(CellType.AirOutlet, grid.Get(11, 7).Type);
        }

        [Test]
        public void BuyingPlot2_CreatesAirDocks()
        {
            var s = new GameSession(new Economy(1000));
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(CellType.Locked, s.Grid.Get(11, 7).Type);
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot2));
            Assert.AreEqual(CellType.AirOutlet, s.Grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.AirReturnDock, s.Grid.Get(11, 9).Type);
        }

        [Test]
        public void AirDocks_CannotBeBuiltOnOrDemolished_AndAreWalkableLikeTruckDocks()
        {
            grid.UnlockRegion(12);
            Assert.AreEqual(BuildResult.Occupied, build.Check(11, 7, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(11, 7));
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(11, 9));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetDirection(11, 7, Direction.North));
            Assert.IsTrue(WorkerService.IsWalkable(CellType.AirOutlet));
            Assert.IsTrue(WorkerService.IsWalkable(CellType.AirReturnDock));
            Assert.IsTrue(ws.CanStand(11f, 7f));
        }

        [Test]
        public void Splitter_TreatsAirDockAsConnectedExit()
        {
            grid.UnlockRegion(12);
            toolbar.Add(CellType.Splitter, Direction.East);
            economy.Earn(500);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(10, 7, CellType.Splitter, Direction.East));
            CollectionAssert.Contains(ConveyorSim.SplitterOutputs(grid, 10, 7, Direction.East), Direction.East);
        }

        // ---- 항공 택배 생성 ----

        [Test]
        public void PackageFactory_DefaultsToNoAir()
        {
            var f = new PackageFactory(new Random(9));
            Assert.AreEqual(0.0, f.AirChance);
            Assert.AreEqual(0.25, PackageFactory.AirChanceAfterPlot2);
            for (int i = 0; i < 300; i++)
            {
                var p = f.Create(i + 1, 0, 2, Direction.East);
                Assert.IsFalse(p.Air);
                Assert.AreNotEqual(PackageFactory.AirMethod, p.Label.Method);
            }
        }

        [Test]
        public void PackageFactory_AirChance_MakesAboutAQuarterAir_WithAirMethod()
        {
            var f = new PackageFactory(new Random(12), 0.3, PackageFactory.AirChanceAfterPlot2);
            int air = 0;
            const int n = 2000;
            for (int i = 0; i < n; i++)
            {
                var p = f.Create(i + 1, 0, 2, Direction.East);
                if (p.Air) air++;
                Assert.AreEqual(p.Air, p.Label.Method == "항공", "air iff method 항공");
            }
            Assert.That(air, Is.InRange(n * 0.21, n * 0.29));
        }

        [Test]
        public void PackageFactory_AirDrawDoesNotChangeOtherFields()
        {
            // 항공 추첨은 맨 마지막이라 같은 시드의 첫 택배는 불량·무게·목적지가 같다(기존 시드 기반 테스트 유지).
            var plain = new PackageFactory(new Random(5), 0.5).Create(1, 0, 2, Direction.East);
            var withAir = new PackageFactory(new Random(5), 0.5, 1.0).Create(1, 0, 2, Direction.East);
            Assert.IsTrue(withAir.Air);
            Assert.AreEqual("항공", withAir.Label.Method);
            Assert.AreEqual(plain.Bad, withAir.Bad);
            Assert.AreEqual(plain.Label.Weight, withAir.Label.Weight);
            Assert.AreEqual(plain.Label.Scale, withAir.Label.Scale);
            Assert.AreEqual(plain.Label.Destination, withAir.Label.Destination);
        }

        [Test]
        public void Session_LeavesAnInjectedFactoryAlone()
        {
            // 주입한 팩토리(테스트용)의 항공 확률은 세션이 건드리지 않는다. 세션이 만든 팩토리만 부지 확장 2에 따라 바뀐다.
            var f = new PackageFactory(new Random(42), 0.3, 0.0);
            var s = new GameSession(new Economy(5000), f);
            s.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(0.0, f.AirChance);
            var g = new PackageFactory(new Random(42), 0.3, 0.7);
            Assert.AreEqual(0.7, new GameSession(new Economy(), g).Sim.Factory.AirChance);
        }

        [Test]
        public void Session_SpawnsAirOnlyAfterPlot2()
        {
            var s = new GameSession(new Economy(5000));
            Assert.AreEqual(0.0, s.Sim.Factory.AirChance);
            for (int i = 0; i < 200; i++)
            {
                Assert.IsTrue(s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
                Assert.IsFalse(p.Air);
                s.Sim.Take(p);
            }
            s.Unlocks.TryPurchase(UnlockId.Plot1);
            Assert.AreEqual(0.0, s.Sim.Factory.AirChance);
            s.Unlocks.TryPurchase(UnlockId.Plot2);
            Assert.AreEqual(PackageFactory.AirChanceAfterPlot2, s.Sim.Factory.AirChance);
            int air = 0;
            const int n = 1000;
            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
                if (p.Air) air++;
                s.Sim.Take(p);
            }
            Assert.That(air, Is.InRange(n * 0.19, n * 0.31));
        }

        // ---- 판정 ----

        private static Package State(bool air, bool inspected, bool rejected, bool bad)
        {
            var p = new Package { Air = air, Bad = bad, Waybill = true, Label = new PackageLabel() };
            if (inspected) p.Inspect(rejected);
            return p;
        }

        [Test]
        public void Scoring_AirToAirDocks_FollowsTheNormalTable()
        {
            var states = new[] { (false, false, false), (false, false, true), (true, false, false), (true, false, true), (true, true, true), (true, true, false) };
            foreach (var (insp, rej, bad) in states)
            {
                var normal = State(false, insp, rej, bad);
                var air = State(true, insp, rej, bad);
                Assert.AreEqual(DeliveryScoring.Outcome(normal, CellType.Outlet), DeliveryScoring.Outcome(air, CellType.AirOutlet), $"outlet {insp}/{rej}/{bad}");
                Assert.AreEqual(DeliveryScoring.Outcome(normal, CellType.ReturnDock), DeliveryScoring.Outcome(air, CellType.AirReturnDock), $"return {insp}/{rej}/{bad}");
            }
            Assert.AreEqual(DeliveryOutcome.Delivered, DeliveryScoring.Outcome(State(true, true, false, false), CellType.AirOutlet));
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, DeliveryScoring.Outcome(State(true, true, true, true), CellType.AirReturnDock));
            Assert.AreEqual(DeliveryOutcome.BadToOutlet, DeliveryScoring.Outcome(State(true, true, false, true), CellType.AirOutlet));
        }

        [Test]
        public void Scoring_WrongCarrier_BeatsInspection()
        {
            var states = new[] { (false, false, false), (false, false, true), (true, false, false), (true, false, true), (true, true, true), (true, true, false) };
            foreach (var (insp, rej, bad) in states)
            {
                Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(true, insp, rej, bad), CellType.Outlet));
                Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(true, insp, rej, bad), CellType.ReturnDock));
                Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(false, insp, rej, bad), CellType.AirOutlet));
                Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(false, insp, rej, bad), CellType.AirReturnDock));
            }
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.WrongCarrier));
            Assert.IsFalse(DeliveryScoring.IsSuccess(DeliveryOutcome.WrongCarrier));
            Assert.Throws<ArgumentException>(() => DeliveryScoring.Outcome(State(true, true, false, false), CellType.Conveyor));
        }

        [Test]
        public void Rail_IntoAirOutlet_ScoresAndRecordsDock()
        {
            grid.UnlockRegion(12);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(10, 7, CellType.Conveyor, Direction.East));
            var p = Loose(air: true);
            p.Inspect(false);
            p.Waybill = true; // 운송장(스펙 15)
            sim.PlaceCarried(p, 10, 7);
            DeliveryOutcome seen = DeliveryOutcome.GoodToReturn;
            CellType dock = CellType.Empty;
            sim.PackageDelivered += (pk, o, d) => { seen = o; dock = sim.LastDeliveredDock; };
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(DeliveryOutcome.Delivered, seen);
            Assert.AreEqual(CellType.AirOutlet, dock);
            Assert.AreEqual(220, economy.Money); // 200 − 10(레일) + 30
            Assert.AreEqual(1, sim.DeliveredCount);

            var q = Loose(air: false);
            q.Inspect(false);
            q.Waybill = true; // 운송장(스펙 15)
            sim.PlaceCarried(q, 10, 7);
            sim.Tick(1f);
            Assert.AreEqual(DeliveryOutcome.WrongCarrier, seen);
            Assert.AreEqual(210, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
        }

        [Test]
        public void Worker_CarriesAirPackageToAirTrucks()
        {
            grid.UnlockRegion(12);
            var good = Loose(air: true);
            good.Inspect(false);
            good.Waybill = true; // 운송장(스펙 15)
            ws.Worker.Carrying = good;
            ws.Worker.X = 10; ws.Worker.Y = 7; ws.Worker.Facing = Direction.East;
            Assert.AreEqual((11, 7), ws.FacingTile());
            Assert.AreEqual(InteractResult.Delivered, ws.Preview(null));
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(230, economy.Money);
            Assert.IsNull(ws.Worker.Carrying);

            var bad = Loose(air: true, bad: true);
            bad.Inspect(true);
            bad.Waybill = true; // 운송장(스펙 15)
            ws.Worker.Carrying = bad;
            ws.Worker.Y = 9;
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(CellType.AirReturnDock, sim.LastDeliveredDock);
            Assert.AreEqual(240, economy.Money);

            // 항공 택배를 일반 출고 트럭에 → 운송 수단 틀림 −10
            var wrong = Loose(air: true);
            wrong.Inspect(false);
            wrong.Waybill = true; // 운송장(스펙 15)
            ws.Worker.Carrying = wrong;
            ws.Worker.Y = 2;
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(CellType.Outlet, sim.LastDeliveredDock);
            Assert.AreEqual(230, economy.Money);
            Assert.AreEqual(2, sim.DeliveredCount);
        }

        [Test]
        public void Session_AirSuccessEarnsExp_WrongCarrierDoesNot()
        {
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            var p = Loose(air: true);
            p.Inspect(false);
            p.Waybill = true; // 운송장(스펙 15)
            s.Sim.DeliverCarried(p, CellType.AirOutlet);
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp);
            var q = Loose(air: false);
            q.Inspect(false);
            q.Waybill = true; // 운송장(스펙 15)
            s.Sim.DeliverCarried(q, CellType.AirOutlet);
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp);
            Assert.AreEqual(1000 + 30 - 10, s.Economy.Money);
        }

        // ---- 저장 v7 ----

        [Test]
        public void Save_V7_RoundTripsAirOnLinePalletAndHands()
        {
            Assert.AreEqual(10, SaveSerializer.CurrentVersion); // v10: 여러 공장(스펙 19.5)
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            var onLine = Loose(air: true);
            onLine.Id = s.Sim.NextId++;
            s.Sim.PlaceCarried(onLine, 1, 2);
            var onPallet = Loose(air: true);
            onPallet.Id = s.Sim.NextId++;
            Assert.IsTrue(s.Sim.PutOnPallet(onPallet, 5, 2));
            var plain = Loose(air: false);
            plain.Id = s.Sim.NextId++;
            Assert.IsTrue(s.Sim.PutOnPallet(plain, 5, 2));
            var carried = Loose(air: true);
            carried.Id = s.Sim.NextId++;
            s.Worker.Worker.Carrying = carried;

            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(10, st.Version); // v10: 여러 공장(스펙 19.5)
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            Assert.IsTrue(r.Sim.PackageAt(1, 2).Air);
            Assert.IsTrue(r.Grid.Get(5, 2).Pallet[0].Air);
            Assert.IsFalse(r.Grid.Get(5, 2).Pallet[1].Air);
            Assert.IsTrue(r.Worker.Worker.Carrying.Air);
            Assert.AreEqual(CellType.AirOutlet, r.Grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.AirReturnDock, r.Grid.Get(11, 9).Type);
            Assert.AreEqual(PackageFactory.AirChanceAfterPlot2, r.Sim.Factory.AirChance);
        }

        [Test]
        public void Save_V6_IsReadWithoutAir_AndPlot2RecreatesAirDocks()
        {
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(s.Sim.TrySpawn(1, 2, PackageKind.Normal, out _));
            var json = SaveSerializer.ToJson(SaveSerializer.Capture(s));
            var v6 = json.Replace("\"Version\": 7", "\"Version\": 6").Replace("\"Air\": false,", "").Replace("\"Air\": true,", "");
            Assert.AreNotEqual(json, v6);
            StringAssert.DoesNotContain("\"Air\"", v6);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(v6));
            Assert.IsFalse(r.Sim.PackageAt(1, 2).Air);
            Assert.AreEqual(CellType.AirOutlet, r.Grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.AirReturnDock, r.Grid.Get(11, 9).Type);

            var noPlot = SaveSerializer.Capture(new GameSession());
            noPlot.Version = 6;
            var r2 = SaveSerializer.Restore(noPlot);
            Assert.AreEqual(CellType.Locked, r2.Grid.Get(11, 7).Type);
            Assert.AreEqual(0.0, r2.Sim.Factory.AirChance);
        }

        [Test]
        public void Save_V6_BuildingsOnAirDockTiles_AreRefundedAndTheirParcelsLost()
        {
            // v6에서는 부지 확장 2 뒤 (11,7)·(11,9)가 빈 칸이라 설치물을 지을 수 있었다(스펙 14.1).
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East);
            var st = SaveSerializer.Capture(s);
            int lost0 = st.LostCount, money0 = st.Money;
            st.Version = 6;
            st.Cells.Add(new CellState { X = 11, Y = 7, Type = "Conveyor", Direction = "East", PalletKind = "None" });
            st.Cells.Add(new CellState { X = 11, Y = 8, Type = "Bridge", Direction = "North", PalletKind = "None" });
            var pile = new System.Collections.Generic.List<PackageState>
            {
                new PackageState { Id = 90, Kind = "Normal", X = 11, Y = 9, Heading = "East", EntryHeading = "East", Lane = "Upper" },
                new PackageState { Id = 91, Kind = "Normal", X = 11, Y = 9, Heading = "East", EntryHeading = "East", Lane = "Upper" },
            };
            st.Cells.Add(new CellState { X = 11, Y = 9, Type = "Pallet", Direction = "West", PalletKind = "Red", Pallet = pile });
            st.Packages.Add(new PackageState { Id = 92, Kind = "Normal", X = 11, Y = 7, Progress = 0.5f, Heading = "East", EntryHeading = "East", Lane = "Upper" });
            st.Packages.Add(new PackageState { Id = 93, Kind = "Normal", X = 11, Y = 8, Progress = 0.2f, Heading = "East", EntryHeading = "East", Lane = "Lower" });
            st.NextPackageId = 100;
            var json = SaveSerializer.ToJson(st).Replace("\"Air\": false,", "").Replace("\"Waybill\": false,", "");
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));
            Assert.AreEqual(CellType.AirOutlet, r.Grid.Get(11, 7).Type);
            Assert.AreEqual(CellType.AirReturnDock, r.Grid.Get(11, 9).Type);
            Assert.AreEqual(CellType.Bridge, r.Grid.Get(11, 8).Type); // 도크가 아닌 칸은 그대로
            Assert.AreEqual(1, r.Sim.Packages.Count); // (11,8) 아랫길 택배만 남는다
            Assert.AreEqual(money0 + BuildingCosts.Cost(CellType.Conveyor) + BuildingCosts.Cost(CellType.Pallet), r.Economy.Money);
            Assert.AreEqual(lost0 + 3, r.Sim.LostCount); // 레일 위 1 + 팔레트 더미 2
            Assert.AreEqual(1, r.Grid.Get(1, 3).Type == CellType.Conveyor ? 1 : 0);

            // v7에서는 같은 내용이 손상된 저장이다.
            st.Version = 7;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        [Test]
        public void Save_WorkerStandingOnAirDock_Restores()
        {
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Worker.Worker.X = 11f; s.Worker.Worker.Y = 7f;
            var r = SaveSerializer.Restore(SaveSerializer.Capture(s));
            Assert.AreEqual(11f, r.Worker.Worker.X);
        }
    }
}
