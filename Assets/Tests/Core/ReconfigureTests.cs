using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>설치된 부품 재설정(스펙 11.8): SetDirection, SetSplitterRed, SetPalletKind, 분배기 명시 빨간 출구, 저장 필드.</summary>
    public class ReconfigureTests
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
            economy = new Economy(1000);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            build = new BuildService(grid, economy, toolbar, sim);
            changed = new List<(int, int)>();
            build.CellChanged += (x, y) => changed.Add((x, y));
        }

        private void Put(int x, int y, Direction d, CellType type = CellType.Conveyor) => grid.Place(x, y, type, d);

        /// <summary>동쪽(파랑)을 보는 분배기 (1,2): 앞 (2,2), 오른쪽 남 (1,1), 왼쪽 북 (1,3) 모두 레일.</summary>
        private void EastSplitterWithAllOutputs()
        {
            toolbar.Add(CellType.Splitter, Direction.East);
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(2, 2, Direction.East);
            Put(1, 1, Direction.South);
            Put(1, 3, Direction.North);
        }

        /// <summary>반입구에서 택배를 만들어(판정 rejected) 분배기 (1,2)에 들이고 택배를 돌려준다.</summary>
        private Package FeedSplitter(bool? rejected)
        {
            Assert.IsTrue(sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
            if (rejected.HasValue) p.Inspect(rejected.Value);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y), "package did not enter the splitter");
            return p;
        }

        // ---- SetDirection ----

        [Test]
        public void SetDirection_Conveyor_OwnedDirection_ChangesFreeAndFiresCellChanged()
        {
            Put(1, 2, Direction.East);
            toolbar.Add(CellType.Conveyor, Direction.North);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(1, 2, Direction.North));
            Assert.AreEqual(Direction.North, grid.Get(1, 2).Direction);
            Assert.AreEqual(1000, economy.Money, "reconfigure is free");
            CollectionAssert.AreEqual(new[] { (1, 2) }, changed);
        }

        [Test]
        public void SetDirection_Conveyor_UnownedDirection_NotOwned()
        {
            Put(1, 2, Direction.East);
            Assert.AreEqual(ReconfigureResult.NotOwned, build.SetDirection(1, 2, Direction.South));
            Assert.AreEqual(Direction.East, grid.Get(1, 2).Direction);
            Assert.IsEmpty(changed);
        }

        [Test]
        public void SetDirection_FastConveyor_RequiresOwnedDirection()
        {
            Put(1, 2, Direction.East, CellType.FastConveyor);
            Assert.AreEqual(ReconfigureResult.NotOwned, build.SetDirection(1, 2, Direction.West));
            toolbar.Add(CellType.FastConveyor, Direction.West);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(1, 2, Direction.West));
            Assert.AreEqual(Direction.West, grid.Get(1, 2).Direction);
        }

        [Test]
        public void SetDirection_TableScannerSplitter_AllFourDirectionsOwned()
        {
            toolbar.Add(CellType.Scanner, Direction.East);
            toolbar.Add(CellType.Splitter, Direction.East);
            Put(1, 1, Direction.East, CellType.Table);
            Put(3, 1, Direction.East, CellType.Scanner);
            Put(3, 3, Direction.East, CellType.Splitter);
            foreach (var (x, y) in new[] { (1, 1), (3, 1), (3, 3) })
                foreach (Direction d in Enum.GetValues(typeof(Direction)))
                {
                    Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(x, y, d), $"({x},{y}) {d}");
                    Assert.AreEqual(d, grid.Get(x, y).Direction);
                }
        }

        [Test]
        public void SetDirection_SameDirection_OkWithoutCellChanged()
        {
            Put(1, 2, Direction.East);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(1, 2, Direction.East));
            Assert.IsEmpty(changed);
        }

        [Test]
        public void SetDirection_NonDirectionalCells_NotConfigurable()
        {
            Put(1, 2, Direction.East, CellType.Pallet);
            var targets = new[]
            {
                (FactoryGrid.InletX, FactoryGrid.InletY), (FactoryGrid.OutletX, FactoryGrid.OutletY),
                (FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY), (1, 2), (3, 3) /* 빈 칸 */, (8, 8) /* 잠김 */, (-1, 0), (12, 0),
            };
            foreach (var (x, y) in targets)
                Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetDirection(x, y, Direction.North), $"({x},{y})");
            Assert.IsEmpty(changed);
        }

        [Test]
        public void SetDirection_Demolishing_Invalid()
        {
            toolbar.Add(CellType.Conveyor, Direction.North);
            Put(1, 2, Direction.East);
            build.BeginDemolish(1, 2);
            changed.Clear();
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetDirection(1, 2, Direction.North));
            Assert.AreEqual(Direction.East, grid.Get(1, 2).Direction);
            Assert.IsEmpty(changed);
        }

        [Test]
        public void SetDirection_Rail_RedirectsPackageOnIt_KeepingProgress()
        {
            toolbar.Add(CellType.Conveyor, Direction.North);
            Put(1, 2, Direction.East);
            Put(2, 2, Direction.East);
            Put(1, 3, Direction.North);
            Assert.IsTrue(sim.TrySpawn(1, 2, PackageKind.Normal, out var p));
            sim.Tick(0.5f);
            Assert.AreEqual(Direction.East, p.Heading);

            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(1, 2, Direction.North));
            Assert.AreEqual(Direction.North, p.Heading);
            Assert.AreEqual(0.5f, p.Progress, 1e-5f);

            sim.Tick(0.5f);
            Assert.AreEqual((1, 3), (p.X, p.Y), "package follows the new direction");
        }

        [Test]
        public void SetDirection_Splitter_ReevaluatesPackagesOnIt()
        {
            EastSplitterWithAllOutputs();
            var p = FeedSplitter(false);
            Assert.AreEqual(Direction.East, p.Heading);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(1, 2, Direction.North)); // 파랑 = 북 (1,3)
            Assert.AreEqual(Direction.North, p.Heading);
        }

        // ---- SetSplitterRed / RedDirection 라우팅 ----

        [Test]
        public void ExplicitRed_IsPreferredOverAutomaticRule()
        {
            EastSplitterWithAllOutputs(); // 자동 빨강 = 오른쪽(남)
            Assert.AreEqual(ReconfigureResult.Ok, build.SetSplitterRed(1, 2, Direction.North));
            Assert.AreEqual(Direction.North, grid.Get(1, 2).RedDirection);
            CollectionAssert.AreEqual(new[] { (1, 2) }, changed);
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out var blue, out var red, out var blueConnected);
            Assert.AreEqual(Direction.East, blue);
            Assert.IsTrue(blueConnected);
            Assert.AreEqual(Direction.North, red);

            Assert.AreEqual(Direction.North, FeedSplitter(true).Heading);
        }

        [Test]
        public void SetSplitterRed_ReevaluatesRejectedPackageOnIt()
        {
            EastSplitterWithAllOutputs();
            var p = FeedSplitter(true);
            Assert.AreEqual(Direction.South, p.Heading); // 자동 빨강
            build.SetSplitterRed(1, 2, Direction.North);
            Assert.AreEqual(Direction.North, p.Heading);
            build.SetSplitterRed(1, 2, null);
            Assert.AreEqual(Direction.South, p.Heading);
        }

        [Test]
        public void ExplicitRed_Unconnected_TreatedAsNoRed_RejectedGoesBlue()
        {
            toolbar.Add(CellType.Splitter, Direction.East);
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(2, 2, Direction.East);
            Put(1, 1, Direction.South); // 자동 규칙이라면 빨강 = 남
            Assert.AreEqual(ReconfigureResult.Ok, build.SetSplitterRed(1, 2, Direction.North)); // (1,3)은 빈 칸

            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out _, out var red, out _);
            Assert.IsNull(red, "unconnected explicit red routes as no red");
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out _, out var configuredRed, out _, out var redConnected);
            Assert.AreEqual(Direction.North, configuredRed, "the view still sees the configured red");
            Assert.IsFalse(redConnected);

            Assert.AreEqual(Direction.East, FeedSplitter(true).Heading);
            Assert.AreEqual(Direction.North, grid.Get(1, 2).RedDirection, "setting is kept");
        }

        [Test]
        public void AutoRed_ReportsConnected()
        {
            EastSplitterWithAllOutputs();
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out _, out var red, out _, out var redConnected);
            Assert.AreEqual(Direction.South, red);
            Assert.IsTrue(redConnected);
        }

        [Test]
        public void SetSplitterRed_SameAsBlue_Invalid_NullClears_SameValueNoEvent()
        {
            EastSplitterWithAllOutputs();
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetSplitterRed(1, 2, Direction.East));
            Assert.IsNull(grid.Get(1, 2).RedDirection);
            Assert.IsEmpty(changed);

            Assert.AreEqual(ReconfigureResult.Ok, build.SetSplitterRed(1, 2, Direction.West));
            Assert.AreEqual(ReconfigureResult.Ok, build.SetSplitterRed(1, 2, Direction.West));
            Assert.AreEqual(1, changed.Count, "same value does not fire CellChanged");

            Assert.AreEqual(ReconfigureResult.Ok, build.SetSplitterRed(1, 2, null));
            Assert.IsNull(grid.Get(1, 2).RedDirection);
            Assert.AreEqual(2, changed.Count);
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out _, out var red, out _);
            Assert.AreEqual(Direction.South, red, "back to the automatic rule");
        }

        [Test]
        public void SetSplitterRed_NotSplitterOrDemolishing()
        {
            Put(1, 2, Direction.East);
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetSplitterRed(1, 2, Direction.North));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetSplitterRed(3, 3, Direction.North));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetSplitterRed(-1, 3, Direction.North));
            Assert.IsNull(grid.Get(1, 2).RedDirection);

            toolbar.Add(CellType.Splitter, Direction.East);
            Put(3, 3, Direction.East, CellType.Splitter);
            build.BeginDemolish(3, 3);
            changed.Clear();
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetSplitterRed(3, 3, Direction.North));
            Assert.IsEmpty(changed);
        }

        [Test]
        public void BlueRotatedOntoExplicitRed_FallsBackToAutomaticRule()
        {
            EastSplitterWithAllOutputs();
            build.SetSplitterRed(1, 2, Direction.North);
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(1, 2, Direction.North));
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out var blue, out var red, out _);
            Assert.AreEqual(Direction.North, blue);
            Assert.AreEqual(Direction.East, red, "explicit red equal to blue is ignored → right of blue");
        }

        [Test]
        public void FactoryGrid_ClearAndPlace_ResetRedDirection()
        {
            EastSplitterWithAllOutputs();
            build.SetSplitterRed(1, 2, Direction.North);
            grid.Clear(1, 2);
            Assert.IsNull(grid.Get(1, 2).RedDirection);
            grid.Get(1, 2).RedDirection = Direction.South; // 이전 상태가 남아 있어도
            Put(1, 2, Direction.East, CellType.Splitter);
            Assert.IsNull(grid.Get(1, 2).RedDirection);
        }

        // ---- SetPalletKind ----

        [Test]
        public void SetPalletKind_ChangesKindAndDirection()
        {
            int x = FactoryGrid.StartBluePalletX, y = FactoryGrid.StartBluePalletY;
            Assert.AreEqual(ReconfigureResult.Ok, build.SetPalletKind(x, y, PalletKind.Red));
            Assert.AreEqual(PalletKind.Red, grid.Get(x, y).PalletKind);
            Assert.AreEqual(Direction.West, grid.Get(x, y).Direction);
            CollectionAssert.AreEqual(new[] { (x, y) }, changed);

            Assert.AreEqual(ReconfigureResult.Ok, build.SetPalletKind(x, y, PalletKind.Red));
            Assert.AreEqual(1, changed.Count, "same kind does not fire CellChanged");

            Assert.AreEqual(ReconfigureResult.Ok, build.SetPalletKind(x, y, PalletKind.Blue));
            Assert.AreEqual(Direction.East, grid.Get(x, y).Direction);
            Assert.AreEqual(1000, economy.Money);
        }

        [Test]
        public void SetPalletKind_None_Invalid_NotPallet_NotConfigurable_Demolishing_Invalid()
        {
            int x = FactoryGrid.StartRedPalletX, y = FactoryGrid.StartRedPalletY;
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetPalletKind(x, y, PalletKind.None));
            Assert.AreEqual(PalletKind.Red, grid.Get(x, y).PalletKind);

            Put(1, 2, Direction.East);
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetPalletKind(1, 2, PalletKind.Blue));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetPalletKind(FactoryGrid.InletX, FactoryGrid.InletY, PalletKind.Blue));
            Assert.AreEqual(ReconfigureResult.NotConfigurable, build.SetPalletKind(99, 0, PalletKind.Blue));

            build.BeginDemolish(x, y);
            changed.Clear();
            Assert.AreEqual(ReconfigureResult.Invalid, build.SetPalletKind(x, y, PalletKind.Blue));
            Assert.IsEmpty(changed);
        }

        // ---- 저장 ----

        private static GameSession SplitterSession(Direction? red)
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.TryPurchase(UnlockId.Splitter);
            s.Build.TryPlace(1, 2, CellType.Splitter, Direction.East);
            if (red.HasValue) Assert.AreEqual(ReconfigureResult.Ok, s.Build.SetSplitterRed(1, 2, red));
            return s;
        }

        private static GameSession RoundTrip(GameSession s) =>
            SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(SaveSerializer.Capture(s))));

        [Test]
        public void Save_RoundTrip_PreservesRedDirection()
        {
            var restored = RoundTrip(SplitterSession(Direction.North));
            Assert.AreEqual(Direction.North, restored.Grid.Get(1, 2).RedDirection);
            Assert.AreEqual(10, SaveSerializer.CurrentVersion); // v10: 여러 공장(스펙 19.5)
        }

        [Test]
        public void Save_RoundTrip_UnsetRedDirection_StaysNull()
        {
            var s = SplitterSession(null);
            var st = SaveSerializer.Capture(s);
            Assert.IsTrue(st.Cells.TrueForAll(c => c.RedDirection == null));
            Assert.IsNull(RoundTrip(s).Grid.Get(1, 2).RedDirection);
        }

        [Test]
        public void Save_OldSaveWithoutField_LoadsAsNull()
        {
            var json = SaveSerializer.ToJson(SaveSerializer.Capture(SplitterSession(null)));
            var old = json.Replace("\"RedDirection\": null", "\"Unused\": 0");
            Assert.AreNotEqual(json, old);
            StringAssert.DoesNotContain("RedDirection", old);
            var restored = SaveSerializer.Restore(SaveSerializer.FromJson(old));
            Assert.AreEqual(CellType.Splitter, restored.Grid.Get(1, 2).Type);
            Assert.IsNull(restored.Grid.Get(1, 2).RedDirection);
        }

        [Test]
        public void Save_RedDirectionOnNonSplitterOrGarbage_IsRejected()
        {
            var s = SplitterSession(null);
            s.Build.TryPlace(3, 3, CellType.Conveyor, Direction.East);
            var st = SaveSerializer.Capture(s);
            st.Cells.Find(c => c.Type == "Conveyor").RedDirection = "North";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            var st2 = SaveSerializer.Capture(SplitterSession(null));
            st2.Cells.Find(c => c.Type == "Splitter").RedDirection = "Up";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st2));
        }
    }
}
