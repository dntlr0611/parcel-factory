using System;
using System.Linq;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>브릿지 레일(스펙 11.12): 한 칸에 윗길(Direction 축)과 아랫길(수직 축) 두 차선.
    /// 평행 진입 → 윗길(Heading = Direction), 수직 진입 → 아랫길(Heading 유지, 직진). 차선별 점유·대기.</summary>
    public class BridgeTests
    {
        private const int BX = 2, BY = 2;

        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private ConveyorSim sim;
        private BuildService build;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(0);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            build = new BuildService(grid, economy, toolbar, sim);
        }

        private void Put(int x, int y, Direction d, CellType type = CellType.Conveyor) => grid.Place(x, y, type, d);

        private Package Spawn(int x, int y)
        {
            Assert.IsTrue(sim.TrySpawn(x, y, PackageKind.Normal, out var p), $"spawn at ({x},{y}) failed");
            return p;
        }

        private static Package NewPackage(int id) => new PackageFactory(new Random(id)).Create(id, 0, 0, Direction.East);

        private Cell Bridge => grid.Get(BX, BY);

        // ---- 교차 흐름 ----

        [Test]
        public void Crossing_EastLineAndNorthLine_BothPassThroughOneBridge()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(1, 2, Direction.East); Put(3, 2, Direction.East);
            Put(2, 1, Direction.North); Put(2, 3, Direction.North);
            var a = Spawn(1, 2);
            var b = Spawn(2, 1);

            sim.Tick(1f);
            Assert.AreEqual((BX, BY), (a.X, a.Y));
            Assert.AreEqual((BX, BY), (b.X, b.Y));
            Assert.AreEqual(Lane.Upper, a.Lane);
            Assert.AreEqual(Lane.Lower, b.Lane);
            Assert.AreEqual(a.Id, Bridge.OccupantId);
            Assert.AreEqual(b.Id, Bridge.LowerOccupantId);
            CollectionAssert.AreEquivalent(new[] { a, b }, sim.PackagesAt(BX, BY));

            sim.Tick(1f);
            Assert.AreEqual((3, 2), (a.X, a.Y));
            Assert.AreEqual((2, 3), (b.X, b.Y));
            Assert.AreEqual(Direction.East, a.Heading);
            Assert.AreEqual(Direction.North, b.Heading);
            Assert.AreEqual(Lane.Upper, b.Lane, "leaving the bridge returns to the single lane");
            Assert.AreEqual(-1, Bridge.OccupantId);
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
            Assert.AreEqual(0, sim.LostCount);
        }

        [Test]
        public void UpperJam_DoesNotBlockLower()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(1, 2, Direction.East); Put(3, 2, Direction.East, CellType.Table);
            Put(2, 1, Direction.North); Put(2, 3, Direction.North);
            Spawn(3, 2); // 검사대에서 영원히 대기 → 윗길 출구가 막힌다
            var a = Spawn(1, 2);
            sim.Tick(1f);
            Assert.AreEqual((BX, BY), (a.X, a.Y));
            var b = Spawn(2, 1);
            sim.Tick(1f);
            Assert.AreEqual((BX, BY), (b.X, b.Y));
            Assert.AreEqual(Lane.Lower, b.Lane);
            sim.Tick(1f);
            Assert.AreEqual((2, 3), (b.X, b.Y), "lower lane passes under the jammed upper lane");
            Assert.AreEqual((BX, BY), (a.X, a.Y));
            Assert.AreEqual(a.Id, Bridge.OccupantId);
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
        }

        [Test]
        public void LowerJam_DoesNotBlockUpper()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(1, 2, Direction.East); Put(3, 2, Direction.East);
            Put(2, 1, Direction.North); Put(2, 3, Direction.North, CellType.Table);
            Spawn(2, 3);
            var b = Spawn(2, 1);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual((BX, BY), (b.X, b.Y));
            Assert.AreEqual(Lane.Lower, b.Lane);
            var a = Spawn(1, 2);
            sim.Tick(1f);
            Assert.AreEqual(Lane.Upper, a.Lane);
            sim.Tick(1f);
            Assert.AreEqual((3, 2), (a.X, a.Y), "upper lane passes over the jammed lower lane");
            Assert.AreEqual(b.Id, Bridge.LowerOccupantId);
            Assert.AreEqual(-1, Bridge.OccupantId);
        }

        // ---- 진입 차선 판정 ----

        [TestCase(Direction.East, Direction.East, Lane.Upper, Direction.East)]
        [TestCase(Direction.East, Direction.West, Lane.Upper, Direction.East)]
        [TestCase(Direction.East, Direction.North, Lane.Lower, Direction.North)]
        [TestCase(Direction.East, Direction.South, Lane.Lower, Direction.South)]
        [TestCase(Direction.North, Direction.East, Lane.Lower, Direction.East)]
        [TestCase(Direction.North, Direction.South, Lane.Upper, Direction.North)]
        public void Entry_ParallelIsUpper_PerpendicularIsLower(Direction bridgeDir, Direction travel, Lane lane, Direction heading)
        {
            Put(BX, BY, bridgeDir, CellType.Bridge);
            int sx = BX - travel.Dx(), sy = BY - travel.Dy();
            Put(sx, sy, travel);
            var p = Spawn(sx, sy);
            sim.Tick(1f);
            Assert.AreEqual((BX, BY), (p.X, p.Y));
            Assert.AreEqual(lane, p.Lane);
            Assert.AreEqual(heading, p.Heading);
            Assert.AreEqual(travel, p.EntryHeading);
            Assert.AreEqual(p.Id, lane == Lane.Upper ? Bridge.OccupantId : Bridge.LowerOccupantId);
            Assert.AreEqual(-1, lane == Lane.Upper ? Bridge.LowerOccupantId : Bridge.OccupantId);
        }

        [TestCase(Direction.East, Direction.North)]
        [TestCase(Direction.East, Direction.South)]
        [TestCase(Direction.North, Direction.East)]
        [TestCase(Direction.North, Direction.West)]
        public void LowerLane_GoesStraightThrough_BothWays(Direction bridgeDir, Direction travel)
        {
            Put(BX, BY, bridgeDir, CellType.Bridge);
            int sx = BX - travel.Dx(), sy = BY - travel.Dy();
            int tx = BX + travel.Dx(), ty = BY + travel.Dy();
            Put(sx, sy, travel);
            Put(tx, ty, travel);
            var p = Spawn(sx, sy);
            sim.Tick(1f);
            Assert.AreEqual(Lane.Lower, p.Lane);
            sim.Tick(1f);
            Assert.AreEqual((tx, ty), (p.X, p.Y));
            Assert.AreEqual(travel, p.Heading);
            Assert.AreEqual(Lane.Upper, p.Lane);
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
        }

        [Test]
        public void SplitterFeedingBridge_UsesSameEntryRule()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(BX, BY, Direction.North, CellType.Bridge);
            Put(3, 2, Direction.East);
            var p = Spawn(FactoryGrid.InletX, FactoryGrid.InletY);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(Direction.East, p.Heading, "bridge counts as a connected blue exit");
            sim.Tick(1f);
            Assert.AreEqual((BX, BY), (p.X, p.Y));
            Assert.AreEqual(Lane.Lower, p.Lane);
            Assert.AreEqual(Direction.East, p.Heading);
            sim.Tick(1f);
            Assert.AreEqual((3, 2), (p.X, p.Y));
        }

        // ---- 대기 ----

        [Test]
        public void UpperOccupied_ParallelEntryWaits_EvenIfLowerFree()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(1, 2, Direction.East); Put(3, 2, Direction.East, CellType.Table);
            Spawn(3, 2);
            var a = Spawn(1, 2);
            sim.Tick(1f);
            var c = Spawn(1, 2);
            sim.Tick(3f);
            Assert.AreEqual((1, 2), (c.X, c.Y));
            Assert.AreEqual(1f, c.Progress);
            Assert.AreEqual(a.Id, Bridge.OccupantId);
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
            Assert.AreEqual(0, sim.LostCount);
        }

        [Test]
        public void BothLanesOccupied_BothEntriesWait()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(1, 2, Direction.East); Put(3, 2, Direction.East, CellType.Table);
            Put(2, 1, Direction.North); Put(2, 3, Direction.North, CellType.Table);
            Spawn(3, 2);
            Spawn(2, 3);
            var a = Spawn(1, 2);
            var b = Spawn(2, 1);
            sim.Tick(1f);
            CollectionAssert.AreEquivalent(new[] { a, b }, sim.PackagesAt(BX, BY));
            var c = Spawn(1, 2);
            var d = Spawn(2, 1);
            sim.Tick(5f);
            Assert.AreEqual((1, 2), (c.X, c.Y));
            Assert.AreEqual((2, 1), (d.X, d.Y));
            CollectionAssert.AreEquivalent(new[] { a, b }, sim.PackagesAt(BX, BY));
            Assert.AreEqual(0, sim.LostCount);
        }

        // ---- 손으로 집기·놓기 ----

        [Test]
        public void PlaceCarried_PrefersUpper_ThenLower_ThenRefuses()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            var p1 = NewPackage(1);
            var p2 = NewPackage(2);
            sim.PlaceCarried(p1, BX, BY);
            Assert.AreEqual(Lane.Upper, p1.Lane);
            Assert.AreEqual(Direction.East, p1.Heading);
            Assert.AreEqual(p1.Id, Bridge.OccupantId);
            sim.PlaceCarried(p2, BX, BY);
            Assert.AreEqual(Lane.Lower, p2.Lane);
            Assert.AreEqual(Direction.East.TurnRight(), p2.Heading);
            Assert.AreEqual(p2.Id, Bridge.LowerOccupantId);
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(NewPackage(3), BX, BY));
        }

        [Test]
        public void PackageAt_ReturnsUpperFirst_TakeReleasesOnlyItsLane()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            var up = NewPackage(1);
            var low = NewPackage(2);
            sim.PlaceCarried(up, BX, BY);
            sim.PlaceCarried(low, BX, BY);
            Assert.AreSame(up, sim.PackageAt(BX, BY));
            Assert.AreEqual(2, sim.PackagesAt(BX, BY).Count);

            sim.Take(up);
            Assert.AreEqual(-1, Bridge.OccupantId);
            Assert.AreEqual(low.Id, Bridge.LowerOccupantId);
            Assert.AreSame(low, sim.PackageAt(BX, BY));

            sim.Take(low);
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
            Assert.IsNull(sim.PackageAt(BX, BY));
            Assert.AreEqual(0, sim.PackagesAt(BX, BY).Count);
        }

        [Test]
        public void Worker_PutsIntoFreeLane_AndPicksUpperThenLower()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            var ws = new WorkerService(grid, sim, build);
            ws.Worker.X = 2f; ws.Worker.Y = 3f; ws.Worker.Facing = Direction.South;
            Assert.AreEqual((BX, BY), ws.FacingTile());

            var up = NewPackage(1);
            var low = NewPackage(2);
            ws.Worker.Carrying = up;
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            ws.Worker.Carrying = low;
            Assert.AreEqual(InteractResult.PutDown, ws.Preview(null), "lower lane is still free");
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.AreEqual(Lane.Lower, low.Lane);
            ws.Worker.Carrying = NewPackage(3);
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Preview(null));
            ws.Worker.Carrying = null;

            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(up, ws.Worker.Carrying);
            ws.Worker.Carrying = null;
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(low, ws.Worker.Carrying);
            Assert.AreEqual(0, sim.Packages.Count);
        }

        // ---- 해체 ----

        [Test]
        public void Demolish_LosesBothLanes_AndRefunds()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            sim.PlaceCarried(NewPackage(1), BX, BY);
            sim.PlaceCarried(NewPackage(2), BX, BY);
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(BX, BY));
            build.Tick(BuildService.DemolishSeconds);
            Assert.AreEqual(2, sim.LostCount);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(CellType.Empty, Bridge.Type);
            Assert.AreEqual(-1, Bridge.OccupantId);
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
            Assert.AreEqual(32, economy.Money); // 40 × 0.8
        }

        [Test]
        public void Demolishing_BlocksBothLanes()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(1, 2, Direction.East);
            Put(2, 1, Direction.North);
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(BX, BY));
            var a = Spawn(1, 2);
            var b = Spawn(2, 1);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (a.X, a.Y));
            Assert.AreEqual((2, 1), (b.X, b.Y));
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(NewPackage(9), BX, BY));
        }

        // ---- 설치·재설정·걷기 ----

        [Test]
        public void Bridge_IsTrackBuildingPlaceableWalkable_Costs40()
        {
            Put(BX, BY, Direction.East, CellType.Bridge);
            Assert.IsTrue(Bridge.IsTrack);
            Assert.IsTrue(Bridge.IsBuilding);
            Assert.IsTrue(FactoryGrid.IsPlaceable(CellType.Bridge));
            Assert.IsTrue(WorkerService.IsWalkable(CellType.Bridge));
            Assert.AreEqual(40, BuildingCosts.Cost(CellType.Bridge));
            Assert.AreEqual(-1, Bridge.LowerOccupantId);
        }

        [Test]
        public void TryPlace_NeedsBridgeOwned_AllDirections()
        {
            economy.Earn(100);
            Assert.AreEqual(BuildResult.NotOwned, build.TryPlace(3, 3, CellType.Bridge, Direction.North));
            toolbar.Add(CellType.Bridge, Direction.East);
            foreach (Direction d in Enum.GetValues(typeof(Direction)))
                Assert.IsTrue(toolbar.Owns(CellType.Bridge, d));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Bridge, Direction.North));
            Assert.AreEqual(60, economy.Money);
            Assert.AreEqual(CellType.Bridge, grid.Get(3, 3).Type);
        }

        [Test]
        public void SetDirection_RotatesUpperLane_LowerKeepsGoingStraight()
        {
            toolbar.Add(CellType.Bridge, Direction.East);
            Put(BX, BY, Direction.East, CellType.Bridge);
            Put(2, 1, Direction.North);
            var low = Spawn(2, 1);
            sim.Tick(1f);
            Assert.AreEqual(Lane.Lower, low.Lane);
            var up = NewPackage(50);
            sim.PlaceCarried(up, BX, BY);

            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(BX, BY, Direction.West));
            Assert.AreEqual(Direction.West, up.Heading);
            Assert.AreEqual(Direction.North, low.Heading);
            Assert.AreEqual(Lane.Upper, up.Lane);
            Assert.AreEqual(Lane.Lower, low.Lane);
        }

        // ---- 저장 ----

        private static GameSession BridgeSession(out Package up, out Package low)
        {
            var s = new GameSession(new Economy(1000), new PackageFactory(new Random(3), 0.0));
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Bridge));
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(7, 7, CellType.Bridge, Direction.North));
            up = NewPackage(100);
            low = NewPackage(101);
            s.Sim.PlaceCarried(up, 7, 7);
            s.Sim.PlaceCarried(low, 7, 7);
            low.Progress = 0.25f;
            s.Sim.NextId = 200;
            return s;
        }

        [Test]
        public void Save_RoundTrip_PreservesLanes_AndRebuildsLowerOccupancy()
        {
            var s = BridgeSession(out var up, out var low);
            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(7, st.Version);
            Assert.AreEqual("Upper", st.Packages.Single(p => p.Id == up.Id).Lane);
            Assert.AreEqual("Lower", st.Packages.Single(p => p.Id == low.Id).Lane);

            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            Assert.IsTrue(r.Toolbar.BridgeOwned);
            var cell = r.Grid.Get(7, 7);
            Assert.AreEqual(CellType.Bridge, cell.Type);
            Assert.AreEqual(Direction.North, cell.Direction);
            Assert.AreEqual(up.Id, cell.OccupantId);
            Assert.AreEqual(low.Id, cell.LowerOccupantId);
            var rlow = r.Sim.Packages.Single(p => p.Id == low.Id);
            Assert.AreEqual(Lane.Lower, rlow.Lane);
            Assert.AreEqual(low.Heading, rlow.Heading);
            Assert.AreEqual(0.25f, rlow.Progress);
            Assert.AreEqual(Lane.Upper, r.Sim.Packages.Single(p => p.Id == up.Id).Lane);
        }

        [Test]
        public void Save_MissingLane_DefaultsToUpper()
        {
            var s = BridgeSession(out var up, out var low);
            s.Sim.Take(low);
            var st = SaveSerializer.Capture(s);
            var json = SaveSerializer.ToJson(st).Replace("\"Lane\": \"Upper\",", "").Replace("\"Lane\": \"Upper\"", "");
            StringAssert.DoesNotContain("\"Lane\"", json);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));
            Assert.AreEqual(Lane.Upper, r.Sim.Packages.Single().Lane);
            Assert.AreEqual(up.Id, r.Grid.Get(7, 7).OccupantId);
        }

        [Test]
        public void Save_RejectsLowerLaneOffBridge_AndDoubleLowerLane()
        {
            var s = BridgeSession(out _, out _);
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            var st = SaveSerializer.Capture(s);
            st.Packages.Add(new PackageState { Id = 150, Kind = "Normal", X = 1, Y = 2, Heading = "East", EntryHeading = "East", Lane = "Lower" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = SaveSerializer.Capture(s);
            st.Packages.Add(new PackageState { Id = 150, Kind = "Normal", X = 7, Y = 7, Heading = "East", EntryHeading = "East", Lane = "Lower" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = SaveSerializer.Capture(s);
            st.Packages[0].Lane = "Sideways";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        // ---- 해금 ----

        [Test]
        public void Unlock_RequiresPlot1_Costs200_GrantsAllDirections()
        {
            var g = new FactoryGrid();
            var e = new Economy(1000);
            var t = new Toolbar();
            var u = new UnlockService(e, g, t);
            Assert.AreEqual(200, UnlockCatalog.Get(UnlockId.Bridge).Price);
            Assert.AreEqual(UnlockId.Plot1, UnlockCatalog.Get(UnlockId.Bridge).Requires);
            Assert.AreEqual(UnlockResult.RequirementMissing, u.TryPurchase(UnlockId.Bridge));
            Assert.IsFalse(t.BridgeOwned);
            Assert.AreEqual(UnlockResult.Ok, u.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(UnlockResult.Ok, u.TryPurchase(UnlockId.Bridge));
            Assert.AreEqual(600, e.Money);
            Assert.IsTrue(t.BridgeOwned);
            foreach (Direction d in Enum.GetValues(typeof(Direction)))
                Assert.IsTrue(t.Owns(CellType.Bridge, d));

            var t2 = new Toolbar();
            var u2 = new UnlockService(new Economy(0), new FactoryGrid(), t2);
            u2.Grant(UnlockId.Bridge);
            Assert.IsTrue(u2.IsPurchased(UnlockId.Plot1), "Grant applies the requirement first");
            Assert.IsTrue(t2.BridgeOwned);
        }
    }
}
