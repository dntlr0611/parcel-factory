using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class ConveyorSimTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private ConveyorSim sim;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(0);
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
        }

        private void Rail(int x, int y, Direction d, CellType type = CellType.Conveyor) =>
            grid.Place(x, y, type, d);

        private Package SpawnAtInlet()
        {
            Assert.IsTrue(sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
            return p;
        }

        [Test]
        public void Spawn_PlacesPackageOnInletHeadingEast()
        {
            var p = SpawnAtInlet();
            Assert.AreEqual((0, 2), (p.X, p.Y));
            Assert.AreEqual(Direction.East, p.Heading);
            Assert.AreEqual(0f, p.Progress);
            Assert.AreEqual(p.Id, grid.Get(0, 2).OccupantId);
            Assert.AreEqual(1, sim.Packages.Count);
        }

        [Test]
        public void Spawn_FailsWhenOccupied()
        {
            SpawnAtInlet();
            Assert.IsFalse(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
        }

        [Test]
        public void Spawn_FailsOnNonTrack()
        {
            Assert.IsFalse(sim.TrySpawn(1, 1, PackageKind.Normal, out _));
        }

        [Test]
        public void AfterOneSecond_MovesOneCell()
        {
            Rail(1, 2, Direction.East);
            var p = SpawnAtInlet();
            for (int i = 0; i < 20; i++) sim.Tick(0.05f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(0f, p.Progress);
            Assert.AreEqual(-1, grid.Get(0, 2).OccupantId);
            Assert.AreEqual(p.Id, grid.Get(1, 2).OccupantId);
        }

        [Test]
        public void HalfSecond_OnlyAdvancesProgress()
        {
            Rail(1, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(0.5f);
            Assert.AreEqual((0, 2), (p.X, p.Y));
            Assert.AreEqual(0.5f, p.Progress, 1e-4f);
        }

        [Test]
        public void Corner_ChangesHeading()
        {
            Rail(1, 2, Direction.North);
            Rail(1, 3, Direction.North);
            var p = SpawnAtInlet();
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(Direction.North, p.Heading);
            sim.Tick(1f);
            Assert.AreEqual((1, 3), (p.X, p.Y));
        }

        [Test]
        public void FastConveyor_MovesTwiceAsFast()
        {
            Rail(1, 2, Direction.East, CellType.FastConveyor);
            Rail(2, 2, Direction.East);
            Rail(3, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(1f);               // inlet → (1,2)
            sim.Tick(0.25f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(0.5f, p.Progress, 1e-4f);
            sim.Tick(0.25f);            // fast: (1,2) → (2,2)
            Assert.AreEqual((2, 2), (p.X, p.Y));
        }

        [Test]
        public void Delivery_RemovesPackageAndPays()
        {
            for (int x = 1; x <= 4; x++) Rail(x, 2, Direction.East);
            var p = SpawnAtInlet();
            p.Inspect(false);
            var delivered = new List<Package>();
            sim.PackageDelivered += (pk, outcome, delta) => delivered.Add(pk);
            for (int i = 0; i < 5; i++) sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(15, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
            Assert.AreSame(p, delivered[0]);
            Assert.AreEqual(-1, grid.Get(4, 2).OccupantId);
        }

        [Test]
        public void EndOfTrack_LosesPackage()
        {
            Rail(1, 2, Direction.East);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            var p = SpawnAtInlet();
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreSame(p, lost[0]);
            Assert.AreEqual(0, economy.Money);
            Assert.AreEqual(1, sim.LostCount);
        }

        [Test]
        public void OutOfBounds_LosesPackage()
        {
            Rail(1, 2, Direction.South);
            Rail(1, 1, Direction.South);
            Rail(1, 0, Direction.South);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            SpawnAtInlet();
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual(1, lost.Count);
        }

        [Test]
        public void MovingIntoInlet_LosesPackage()
        {
            Rail(1, 2, Direction.West);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            SpawnAtInlet();
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(1, lost.Count);
        }

        [Test]
        public void BlockedByOccupiedCell_Waits()
        {
            Rail(1, 2, Direction.East);
            Rail(2, 2, Direction.West); // (1,2)와 (2,2)가 서로를 막음
            var a = SpawnAtInlet();
            sim.Tick(1f);               // a → (1,2)
            Assert.IsTrue(sim.TrySpawn(2, 2, PackageKind.Normal, out var b));
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (a.X, a.Y));
            Assert.AreEqual(1f, a.Progress);
            Assert.AreEqual((2, 2), (b.X, b.Y));
            Assert.AreEqual(2, sim.Packages.Count);
        }

        [Test]
        public void BlockedByDemolishingCell_Waits()
        {
            Rail(1, 2, Direction.East);
            Rail(2, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(1f);
            grid.Get(2, 2).Demolishing = true;
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(1f, p.Progress);
        }

        [Test]
        public void Unblocked_ResumesImmediately()
        {
            Rail(1, 2, Direction.East);
            Rail(2, 2, Direction.East);
            Rail(3, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(1f);
            grid.Get(2, 2).Demolishing = true;
            sim.Tick(1f);
            grid.Get(2, 2).Demolishing = false;
            sim.Tick(0.05f);
            Assert.AreEqual((2, 2), (p.X, p.Y));
        }

        [Test]
        public void Splitter_AlternatesLeftThenRight()
        {
            Rail(1, 2, Direction.East, CellType.Splitter);
            Rail(1, 3, Direction.North); // 동쪽 기준 왼쪽 = 북
            Rail(1, 1, Direction.South); // 동쪽 기준 오른쪽 = 남
            var a = SpawnAtInlet();
            sim.Tick(1f);               // a → splitter
            Assert.AreEqual(Direction.North, a.Heading);
            var b = SpawnAtInlet();
            sim.Tick(1f);               // a → (1,3), b → splitter
            Assert.AreEqual((1, 3), (a.X, a.Y));
            Assert.AreEqual(Direction.South, b.Heading);
            sim.Tick(1f);
            Assert.AreEqual((1, 1), (b.X, b.Y));
        }

        [Test]
        public void Splitter_FedFromSide_NeverSendsBackward()
        {
            // 동쪽을 보는 분배기에 북쪽(1,3)에서 남쪽으로 진입. 왼쪽 출구(북)는 되돌아가는 방향이므로 쓰면 안 된다.
            Rail(1, 2, Direction.East, CellType.Splitter);
            Rail(1, 3, Direction.South);
            Rail(1, 1, Direction.South);
            Rail(1, 0, Direction.South);
            Assert.IsTrue(sim.TrySpawn(1, 3, PackageKind.Normal, out var a));
            sim.Tick(1f); // a → splitter
            Assert.AreEqual((1, 2), (a.X, a.Y));
            Assert.AreEqual(Direction.South, a.Heading);
            Assert.IsTrue(sim.TrySpawn(1, 3, PackageKind.Normal, out var b));
            sim.Tick(1f); // a → (1,1), b → splitter; 토글이 바뀌어도 여전히 남쪽
            Assert.AreEqual((1, 1), (a.X, a.Y));
            Assert.AreEqual((1, 2), (b.X, b.Y));
            Assert.AreEqual(Direction.South, b.Heading);
            sim.Tick(1f);
            Assert.AreEqual(2, sim.Packages.Count); // 아무도 소실되거나 막히지 않음
        }

        [Test]
        public void EntryHeading_RecordsHowPackageEnteredCell()
        {
            Rail(1, 2, Direction.North);
            Rail(1, 3, Direction.North);
            var p = SpawnAtInlet();
            Assert.AreEqual(Direction.East, p.EntryHeading);
            sim.Tick(1f); // (1,2): 동쪽으로 들어와 북쪽으로 나감
            Assert.AreEqual(Direction.East, p.EntryHeading);
            Assert.AreEqual(Direction.North, p.Heading);
            sim.Tick(1f);
            Assert.AreEqual(Direction.North, p.EntryHeading);
        }

        [Test]
        public void FrontPackageRemoved_RearPackageStillAdvancesSameTick()
        {
            for (int x = 1; x <= 4; x++) Rail(x, 2, Direction.East);
            var a = SpawnAtInlet();
            for (int i = 0; i < 4; i++) sim.Tick(1f); // a는 (4,2)에서 배달 대기
            Assert.AreEqual((4, 2), (a.X, a.Y));
            var b = SpawnAtInlet();
            sim.Tick(1f);
            Assert.AreEqual(1, sim.Packages.Count); // a 배달됨
            Assert.AreEqual((1, 2), (b.X, b.Y));
            Assert.AreEqual(0f, b.Progress, 1e-4f); // 같은 틱에 따라 이동
        }

        [Test]
        public void RemovePackagesAt_RaisesLost()
        {
            Rail(1, 2, Direction.East);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            var p = SpawnAtInlet();
            sim.Tick(1f);
            sim.RemovePackagesAt(1, 2);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreSame(p, lost[0]);
            Assert.AreEqual(-1, grid.Get(1, 2).OccupantId);
        }

        [Test]
        public void Restore_RegistersOccupancyAndKeepsIds()
        {
            Rail(1, 2, Direction.East);
            sim.NextId = 42;
            sim.Restore(new Package { Id = 7, Kind = PackageKind.Normal, X = 1, Y = 2, Progress = 0.25f, Heading = Direction.East });
            Assert.AreEqual(7, grid.Get(1, 2).OccupantId);
            var p = SpawnAtInlet();
            Assert.AreEqual(42, p.Id);
        }
    }
}
