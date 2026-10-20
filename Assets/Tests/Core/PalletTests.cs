using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 10절: 트럭 도크 (11,2)·(11,4), 팔레트, 건물 전체 걷기, 저장 v4.</summary>
    public class PalletTests
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

        private Package Loose(bool bad = false)
        {
            return new PackageFactory(new Random(nextId), bad ? 1.0 : 0.0).Create(nextId++, 0, 0, Direction.East);
        }

        private void FillPallet(int x, int y, int n)
        {
            for (int i = 0; i < n; i++) Assert.IsTrue(sim.PutOnPallet(Loose(), x, y));
        }

        [Test]
        public void NewGrid_HasStartPalletsAndTruckDocks()
        {
            Assert.AreEqual(11, FactoryGrid.OutletX);
            Assert.AreEqual(2, FactoryGrid.OutletY);
            Assert.AreEqual(11, FactoryGrid.ReturnDockX);
            Assert.AreEqual(4, FactoryGrid.ReturnDockY);
            Assert.AreEqual(CellType.Inlet, grid.Get(0, 2).Type);
            Assert.AreEqual(CellType.Outlet, grid.Get(11, 2).Type);
            Assert.AreEqual(CellType.ReturnDock, grid.Get(11, 4).Type);
            Assert.IsTrue(grid.Get(11, 2).IsDock);
            Assert.IsTrue(grid.Get(11, 4).IsDock);
            Assert.IsFalse(grid.IsUnlocked(11, 2)); // 잠긴 구역이어도 도크는 존재한다

            var blue = grid.Get(5, 2);
            var red = grid.Get(5, 4);
            Assert.AreEqual(CellType.Pallet, blue.Type);
            Assert.AreEqual(PalletKind.Blue, blue.PalletKind);
            Assert.AreEqual(CellType.Pallet, red.Type);
            Assert.AreEqual(PalletKind.Red, red.PalletKind);
            Assert.AreEqual(0, blue.Pallet.Count);
            Assert.IsTrue(blue.IsBuilding);
            Assert.IsFalse(blue.IsTrack);
            Assert.IsFalse(blue.IsDock);
            Assert.AreEqual(6, Cell.PalletCapacity);
            Assert.IsFalse(blue.IsPalletFull);
            Assert.IsTrue(FactoryGrid.IsPlaceable(CellType.Pallet));
            Assert.AreEqual(PalletKind.None, grid.Get(1, 1).PalletKind);
        }

        [Test]
        public void UnlockRegion_LeavesDocksAndPalletsAlone()
        {
            FillPallet(5, 2, 2);
            grid.UnlockRegion(9);
            grid.UnlockRegion(12);
            Assert.AreEqual(CellType.Outlet, grid.Get(11, 2).Type);
            Assert.AreEqual(CellType.ReturnDock, grid.Get(11, 4).Type);
            Assert.AreEqual(CellType.Pallet, grid.Get(5, 2).Type);
            Assert.AreEqual(2, sim.PalletCount(5, 2));
            Assert.AreEqual(CellType.Empty, grid.Get(10, 2).Type);
        }

        [Test]
        public void Rail_IntoPallet_StacksPackage_KeepingInspection()
        {
            for (int x = 1; x <= 4; x++) grid.Place(x, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(true);
            for (int i = 0; i < 5; i++) sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(1, sim.PalletCount(5, 2));
            Assert.AreSame(p, grid.Get(5, 2).Pallet[0]);
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected);
            Assert.AreEqual(-1, grid.Get(4, 2).OccupantId);
            Assert.AreEqual(-1, grid.Get(5, 2).OccupantId);
            Assert.AreEqual(0, sim.DeliveredCount);
            Assert.AreEqual(0, sim.LostCount);
            Assert.AreEqual(200, economy.Money); // 팔레트는 판정하지 않는다
            Assert.IsNull(sim.PackageAt(5, 2));
        }

        [Test]
        public void FullPallet_FrontRailWaits()
        {
            FillPallet(5, 2, Cell.PalletCapacity);
            Assert.IsTrue(grid.Get(5, 2).IsPalletFull);
            grid.Place(4, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(4, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual((4, 2), (p.X, p.Y));
            Assert.AreEqual(1f, p.Progress);
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual(6, sim.PalletCount(5, 2));
            Assert.AreEqual(0, sim.LostCount);

            sim.TakeFromPallet(5, 2); // 자리가 나면 즉시 들어간다
            sim.Tick(0.05f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(6, sim.PalletCount(5, 2));
            Assert.AreSame(p, grid.Get(5, 2).Pallet.Last());
        }

        [Test]
        public void DemolishingPallet_FrontRailWaits()
        {
            grid.Place(4, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(4, 2, PackageKind.Normal, out var p));
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(5, 2));
            sim.Tick(2f);
            Assert.AreEqual((4, 2), (p.X, p.Y));
            Assert.AreEqual(0, sim.PalletCount(5, 2));
        }

        [Test]
        public void TakeFromPallet_ReturnsTopAndDecrements()
        {
            var a = Loose();
            var b = Loose();
            Assert.IsTrue(sim.PutOnPallet(a, 5, 4));
            Assert.IsTrue(sim.PutOnPallet(b, 5, 4));
            Assert.AreEqual(2, sim.PalletCount(5, 4));
            Assert.AreSame(b, sim.TakeFromPallet(5, 4));
            Assert.AreEqual(1, sim.PalletCount(5, 4));
            Assert.AreSame(a, sim.TakeFromPallet(5, 4));
            Assert.AreEqual(0, sim.PalletCount(5, 4));
            Assert.IsNull(sim.TakeFromPallet(5, 4));
            Assert.IsNull(sim.TakeFromPallet(1, 1)); // 팔레트 아님
            Assert.IsNull(sim.TakeFromPallet(-1, 0));
            Assert.AreEqual(0, sim.PalletCount(1, 1));
            Assert.AreEqual(0, sim.LostCount);
        }

        [Test]
        public void PutOnPallet_RespectsCapacity()
        {
            for (int i = 0; i < Cell.PalletCapacity; i++)
                Assert.IsTrue(sim.PutOnPallet(Loose(), 5, 2));
            Assert.IsTrue(grid.Get(5, 2).IsPalletFull);
            var extra = Loose();
            Assert.IsFalse(sim.PutOnPallet(extra, 5, 2));
            Assert.AreEqual(6, sim.PalletCount(5, 2));
            Assert.IsFalse(sim.PutOnPallet(extra, 1, 1)); // 팔레트 아님
            Assert.IsFalse(sim.PutOnPallet(extra, 99, 0));
            Assert.IsTrue(sim.PutOnPallet(extra, 5, 4));
            Assert.AreEqual((5, 4), (extra.X, extra.Y));
        }

        [Test]
        public void PutOnPallet_RejectsPackageStillOnLine()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            Assert.Throws<InvalidOperationException>(() => sim.PutOnPallet(p, 5, 2));
            Assert.AreEqual(0, sim.PalletCount(5, 2));
        }

        [Test]
        public void Worker_EmptyHanded_PicksTopFromPallet()
        {
            var a = Loose();
            var b = Loose();
            sim.PutOnPallet(a, 5, 2);
            sim.PutOnPallet(b, 5, 2);
            ws.Worker.X = 4; ws.Worker.Y = 2; ws.Worker.Facing = Direction.East;
            Assert.AreEqual((5, 2), ws.FacingTile());
            Assert.AreEqual(InteractResult.PickedUp, ws.Preview(null));
            Assert.AreEqual(2, sim.PalletCount(5, 2)); // Preview는 부작용 없음
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            Assert.AreSame(b, ws.Worker.Carrying);
            Assert.AreEqual(1, sim.PalletCount(5, 2));
        }

        [Test]
        public void Worker_Carrying_PutsDownOnPalletWithRoom()
        {
            var p = Loose();
            ws.Worker.Carrying = p;
            ws.Worker.X = 4; ws.Worker.Y = 4; ws.Worker.Facing = Direction.East; // (5,4) 빨강
            Assert.AreEqual(InteractResult.PutDown, ws.Preview(null));
            Assert.AreSame(p, ws.Worker.Carrying);
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreEqual(1, sim.PalletCount(5, 4));
            Assert.AreSame(p, grid.Get(5, 4).Pallet[0]);
            Assert.AreEqual(0, sim.Packages.Count);
        }

        [Test]
        public void Worker_Carrying_FullPallet_CannotPutHere()
        {
            FillPallet(5, 2, Cell.PalletCapacity);
            var p = Loose();
            ws.Worker.Carrying = p;
            ws.Worker.X = 4; ws.Worker.Y = 2; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Preview(null));
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(null));
            Assert.AreSame(p, ws.Worker.Carrying);
            Assert.AreEqual(6, sim.PalletCount(5, 2));
        }

        [Test]
        public void Worker_EmptyHanded_EmptyPallet_FallsThroughToTool()
        {
            ws.Worker.X = 4; ws.Worker.Y = 2; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.NoTool, ws.Preview(null));
            Assert.AreEqual(InteractResult.Occupied, ws.Preview(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void Demolish_Pallet_OccupiedWhilePileExists_ThenRefunds80Percent()
        {
            FillPallet(5, 2, 1);
            Assert.AreEqual(BuildResult.Occupied, build.BeginDemolish(5, 2));
            Assert.IsFalse(grid.Get(5, 2).Demolishing);
            sim.TakeFromPallet(5, 2);
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(5, 2));
            build.Tick(BuildService.DemolishSeconds);
            Assert.AreEqual(CellType.Empty, grid.Get(5, 2).Type);
            Assert.AreEqual(PalletKind.None, grid.Get(5, 2).PalletKind);
            Assert.AreEqual(216, economy.Money);
            Assert.AreEqual(16, BuildService.RefundFor(CellType.Pallet));

            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(5, 4)); // 빈 빨강 팔레트도 해체 가능
        }

        [Test]
        public void Worker_DemolishFacingPalletWithPile_IsOccupied()
        {
            FillPallet(5, 4, 2);
            ws.Worker.X = 4; ws.Worker.Y = 4; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(BuildResult.Occupied, ws.TryDemolishFacing());
        }

        [Test]
        public void RemovePackagesAt_ClearsPalletPile_AsLost()
        {
            FillPallet(5, 2, 3);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            sim.RemovePackagesAt(5, 2);
            Assert.AreEqual(0, sim.PalletCount(5, 2));
            Assert.AreEqual(3, lost.Count);
            Assert.AreEqual(3, sim.LostCount);
        }

        [Test]
        public void PlacePallet_KindFollowsToolDirection()
        {
            Assert.AreEqual(20, BuildingCosts.Cost(CellType.Pallet));
            Assert.AreEqual(PalletKind.Blue, Tool.KindOf(Direction.East));
            Assert.AreEqual(PalletKind.Red, Tool.KindOf(Direction.West));
            Assert.AreEqual(PalletKind.Red, Tool.KindOf(Direction.North));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 1, CellType.Pallet, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 1, CellType.Pallet, Direction.West));
            Assert.AreEqual(160, economy.Money);
            Assert.AreEqual(CellType.Pallet, grid.Get(1, 1).Type);
            Assert.AreEqual(PalletKind.Blue, grid.Get(1, 1).PalletKind);
            Assert.AreEqual(PalletKind.Red, grid.Get(2, 1).PalletKind);
        }

        [Test]
        public void Toolbar_OwnsPalletFromStart_OrderedAfterRails()
        {
            var t = new Toolbar();
            Assert.IsTrue(t.PalletOwned);
            Assert.IsTrue(t.Owns(CellType.Pallet, Direction.East));
            Assert.IsTrue(t.Owns(CellType.Pallet, Direction.West));
            t.Add(CellType.FastConveyor, Direction.East);
            t.Add(CellType.Splitter, Direction.East);
            t.Add(CellType.Scanner, Direction.East);
            var tools = t.OwnedTools().ToList();
            Assert.AreEqual(new Tool(CellType.Conveyor, Direction.East), tools[0]);
            Assert.AreEqual(new Tool(CellType.FastConveyor, Direction.East), tools[1]);
            Assert.AreEqual(new Tool(CellType.Pallet, Direction.East), tools[2]);
            Assert.AreEqual(new Tool(CellType.Pallet, Direction.West), tools[3]);
            Assert.AreEqual(CellType.Table, tools[4].Type);
            Assert.AreEqual(CellType.Splitter, tools[8].Type);
            Assert.AreEqual(CellType.Scanner, tools[12].Type);
            Assert.AreEqual(16, tools.Count);
        }

        [Test]
        public void Worker_CannotWalkOntoPallet()
        {
            Assert.IsFalse(WorkerService.IsWalkable(CellType.Pallet));
            Assert.IsFalse(ws.CanStand(5f, 2f));
            ws.Worker.X = 4; ws.Worker.Y = 2;
            for (int i = 0; i < 10; i++) ws.Move(1f, 0f, 0.1f);
            Assert.LessOrEqual(ws.Worker.X, 4.5f - WorkerService.Radius + 1e-4f);
        }

        [Test]
        public void Worker_WalksWholeBuilding_IncludingLockedCells()
        {
            Assert.AreEqual(6, grid.UnlockedSize);
            Assert.IsTrue(WorkerService.IsWalkable(CellType.Locked));
            Assert.IsTrue(ws.CanStand(8f, 8f));
            Assert.IsTrue(ws.CanStand(11f, 11f));
            Assert.IsTrue(ws.CanStand(11f, 2f)); // 트럭 도크 위
            Assert.IsFalse(ws.CanStand(12f, 3f));
            Assert.IsFalse(ws.CanStand(11.5f, 3f));
            ws.Worker.X = 4; ws.Worker.Y = 3;
            for (int i = 0; i < 40; i++) ws.Move(1f, 0f, 0.1f);
            Assert.AreEqual(11, ws.Tile().x);
            Assert.LessOrEqual(ws.Worker.X, 11.5f - WorkerService.Radius + 1e-4f);
        }

        [Test]
        public void HandDelivery_AtTruckDocks_ScoresAndChangesMoney()
        {
            var good = Loose();
            good.Inspect(false);
            ws.Worker.Carrying = good;
            ws.Worker.X = 10; ws.Worker.Y = 2; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.Delivered, ws.Preview(null));
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(215, economy.Money);
            Assert.IsNull(ws.Worker.Carrying);

            var bad = Loose(bad: true);
            bad.Inspect(true);
            ws.Worker.Carrying = bad;
            ws.Worker.X = 10; ws.Worker.Y = 4; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(220, economy.Money);
            Assert.AreEqual(2, sim.DeliveredCount);
        }

        [Test]
        public void RailIntoTruckDock_RequiresTwelveUnlock_ThenScores()
        {
            Assert.AreEqual(BuildResult.Locked, build.Check(10, 2, CellType.Conveyor, Direction.East));
            grid.UnlockRegion(12);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(10, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Occupied, build.Check(11, 2, CellType.Conveyor, Direction.East));
            Assert.IsTrue(sim.TrySpawn(10, 2, PackageKind.Normal, out var p));
            p.Inspect(false);
            DeliveryOutcome seen = DeliveryOutcome.GoodToReturn;
            sim.PackageDelivered += (pk, o, d) => seen = o;
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(DeliveryOutcome.Delivered, seen);
            Assert.AreEqual(205, economy.Money); // 200 − 10 + 15
        }

        [Test]
        public void Session_RailToPallet_WorkerCarriesToTruck()
        {
            var s = new GameSession(new Economy(300), new PackageFactory(new Random(1), 0.0));
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            for (int x = 2; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(150, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f); // 3초 생성 + 5초 이동 → 파랑 팔레트
            Assert.AreEqual(1, s.Sim.PalletCount(5, 2));
            Assert.AreEqual(150, s.Economy.Money); // 팔레트는 판정하지 않는다

            s.Worker.Worker.X = 5; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South; // (5,2)
            Assert.AreEqual(InteractResult.PickedUp, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Worker.Carrying.Inspected);
            for (int i = 0; i < 40; i++) s.Worker.Move(1f, 0f, 0.05f); // 잠긴 구역을 지나 동쪽 벽까지
            Assert.AreEqual((11, 3), s.Worker.Tile());
            s.Worker.Move(0f, -1f, 0f); // 제자리에서 남쪽(트럭 도크 (11,2))을 바라본다
            Assert.AreEqual((11, 2), s.Worker.FacingTile());
            Assert.AreEqual(InteractResult.Delivered, s.Worker.Interact(null));
            Assert.AreEqual(165, s.Economy.Money);
            Assert.AreEqual(1, s.Sim.DeliveredCount);
        }

        [Test]
        public void Save_V4_RoundTrip_PreservesPalletsAndPiles()
        {
            var s = new GameSession(new Economy(500), new PackageFactory(new Random(3), 1.0));
            var a = new PackageFactory(new Random(4), 1.0).Create(1, 0, 0, Direction.East);
            a.Inspect(true);
            var b = new PackageFactory(new Random(5), 0.0).Create(2, 0, 0, Direction.East);
            Assert.IsTrue(s.Sim.PutOnPallet(a, 5, 4));
            Assert.IsTrue(s.Sim.PutOnPallet(b, 5, 4));
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 1, CellType.Pallet, Direction.East));
            Assert.AreEqual(BuildResult.Ok, s.Build.BeginDemolish(5, 2));
            s.Build.Tick(BuildService.DemolishSeconds); // 시작 파랑 팔레트 철거
            s.Sim.NextId = 3;

            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(4, st.Version);
            var json = SaveSerializer.ToJson(st);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));

            Assert.AreEqual(CellType.Empty, r.Grid.Get(5, 2).Type); // 철거된 시작 팔레트는 돌아오지 않는다
            Assert.AreEqual(CellType.Pallet, r.Grid.Get(1, 1).Type);
            Assert.AreEqual(PalletKind.Blue, r.Grid.Get(1, 1).PalletKind);
            var red = r.Grid.Get(5, 4);
            Assert.AreEqual(CellType.Pallet, red.Type);
            Assert.AreEqual(PalletKind.Red, red.PalletKind);
            Assert.AreEqual(2, r.Sim.PalletCount(5, 4));
            Assert.AreEqual(0, r.Sim.Packages.Count); // 더미는 흐름에 넣지 않는다
            var top = r.Sim.TakeFromPallet(5, 4);
            Assert.AreEqual(2, top.Id);
            Assert.IsFalse(top.Inspected);
            var bottom = r.Sim.TakeFromPallet(5, 4);
            Assert.AreEqual(1, bottom.Id);
            Assert.IsTrue(bottom.Bad);
            Assert.IsTrue(bottom.Inspected);
            Assert.IsTrue(bottom.Rejected);
            Assert.AreEqual(a.Label.Scale, bottom.Label.Scale);
            Assert.AreEqual(CellType.Outlet, r.Grid.Get(11, 2).Type);
            Assert.AreEqual(CellType.ReturnDock, r.Grid.Get(11, 4).Type);
        }

        [Test]
        public void Save_RestoreRejectsStalePileIdsAndOverfullPile()
        {
            var s = new GameSession();
            Assert.IsTrue(s.Sim.PutOnPallet(new PackageFactory(new Random(1), 0.0).Create(9, 0, 0, Direction.East), 5, 2));
            var st = SaveSerializer.Capture(s);
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st)); // NextPackageId 1 ≤ 9
            st.NextPackageId = 10;
            Assert.AreEqual(1, SaveSerializer.Restore(st).Sim.PalletCount(5, 2));

            var full = SaveSerializer.Capture(new GameSession());
            var cell = full.Cells.First(c => c.X == 5 && c.Y == 2);
            for (int i = 0; i < Cell.PalletCapacity + 1; i++)
                cell.Pallet.Add(new PackageState { Id = i + 1, Kind = "Normal", Heading = "East", EntryHeading = "East" });
            full.NextPackageId = 100;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(full));
        }

        [Test]
        public void Save_V3_IsRejected()
        {
            Assert.AreEqual(4, SaveSerializer.CurrentVersion);
            Assert.AreEqual(4, SaveSerializer.MinReadableVersion);
            var st = SaveSerializer.Capture(new GameSession());
            st.Version = 3;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }
    }
}
