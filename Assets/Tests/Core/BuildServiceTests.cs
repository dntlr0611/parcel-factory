using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class BuildServiceTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private ConveyorSim sim;
        private BuildService build;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(100);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy);
            build = new BuildService(grid, economy, toolbar, sim);
        }

        [Test]
        public void Place_DeductsCostAndSetsCell()
        {
            var changed = new List<(int, int)>();
            build.CellChanged += (x, y) => changed.Add((x, y));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(90, economy.Money);
            Assert.AreEqual(CellType.Conveyor, grid.Get(1, 2).Type);
            Assert.AreEqual((1, 2), changed[0]);
        }

        [Test]
        public void Place_RejectsLocked()
        {
            Assert.AreEqual(BuildResult.Locked, build.TryPlace(7, 7, CellType.Conveyor, Direction.East));
            Assert.AreEqual(100, economy.Money);
        }

        [Test]
        public void Place_RejectsOccupied()
        {
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            Assert.AreEqual(BuildResult.Occupied, build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Occupied, build.TryPlace(0, 2, CellType.Conveyor, Direction.East));
        }

        [Test]
        public void Place_RejectsUnownedTool()
        {
            Assert.AreEqual(BuildResult.NotOwned, build.TryPlace(1, 2, CellType.Conveyor, Direction.North));
            Assert.AreEqual(100, economy.Money);
        }

        [Test]
        public void Place_RejectsWhenBroke()
        {
            var poor = new BuildService(grid, new Economy(5), toolbar, sim);
            Assert.AreEqual(BuildResult.NotEnoughMoney, poor.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(CellType.Empty, grid.Get(1, 2).Type);
        }

        [Test]
        public void RefundFor_Is80Percent()
        {
            Assert.AreEqual(8, BuildService.RefundFor(CellType.Conveyor));
            Assert.AreEqual(20, BuildService.RefundFor(CellType.FastConveyor));
            Assert.AreEqual(48, BuildService.RefundFor(CellType.Splitter));
        }

        [Test]
        public void BeginDemolish_MarksCellWithoutRefund()
        {
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(1, 2));
            var c = grid.Get(1, 2);
            Assert.IsTrue(c.Demolishing);
            Assert.AreEqual(2f, c.DemolishRemaining);
            Assert.AreEqual(90, economy.Money);
            Assert.AreEqual(CellType.Conveyor, c.Type);
        }

        [Test]
        public void BeginDemolish_RejectsNonBuildingAndDuplicates()
        {
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(1, 1));
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(0, 2));
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(-1, 0));
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            build.BeginDemolish(1, 2);
            Assert.AreEqual(BuildResult.AlreadyDemolishing, build.BeginDemolish(1, 2));
        }

        [Test]
        public void Demolish_CompletesAfterTwoSecondsWithRefund()
        {
            Assert.AreEqual(BuildResult.NotOwned, build.TryPlace(1, 2, CellType.FastConveyor, Direction.East));
            Assert.AreEqual(100, economy.Money);
            toolbar.Add(CellType.FastConveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 2, CellType.FastConveyor, Direction.East));
            Assert.AreEqual(75, economy.Money);

            build.BeginDemolish(1, 2);
            build.Tick(1.9f);
            Assert.AreEqual(CellType.FastConveyor, grid.Get(1, 2).Type);
            Assert.AreEqual(75, economy.Money);

            var changed = new List<(int, int)>();
            build.CellChanged += (x, y) => changed.Add((x, y));
            build.Tick(0.1f);
            Assert.AreEqual(CellType.Empty, grid.Get(1, 2).Type);
            Assert.IsFalse(grid.Get(1, 2).Demolishing);
            Assert.AreEqual(95, economy.Money);
            Assert.AreEqual((1, 2), changed[0]);
        }

        [Test]
        public void Demolish_LosesPackageStillOnCell()
        {
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            build.TryPlace(2, 2, CellType.Conveyor, Direction.East);
            toolbar.Add(CellType.Conveyor, Direction.West);
            build.TryPlace(3, 2, CellType.Conveyor, Direction.West);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var a));
            sim.Tick(1f);
            sim.Tick(1f); // a → (2,2)
            Assert.IsTrue(sim.TrySpawn(3, 2, PackageKind.Normal, out var b)); // a와 b가 서로 막음
            sim.Tick(1f);
            Assert.AreEqual((2, 2), (a.X, a.Y));

            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            build.BeginDemolish(2, 2);
            sim.Tick(2f);
            build.Tick(2f);
            Assert.AreEqual(1, lost.Count);
            Assert.AreSame(a, lost[0]);
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreSame(b, sim.Packages[0]);
            Assert.AreEqual(CellType.Empty, grid.Get(2, 2).Type);
        }
    

        [Test]
        public void Check_ReportsWithoutSpendingOrPlacing()
        {
            Assert.AreEqual(BuildResult.Ok, build.Check(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(100, economy.Money);
            Assert.AreEqual(CellType.Empty, grid.Get(1, 2).Type);
            Assert.AreEqual(BuildResult.Locked, build.Check(7, 7, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Occupied, build.Check(0, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.NotOwned, build.Check(1, 2, CellType.Conveyor, Direction.North));
            var poor = new BuildService(grid, new Economy(5), toolbar, sim);
            Assert.AreEqual(BuildResult.NotEnoughMoney, poor.Check(1, 2, CellType.Conveyor, Direction.East));
        }
    }
}
