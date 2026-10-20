using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class FactoryGridTests
    {
        [Test]
        public void NewGrid_HasInletAndOutletFixed()
        {
            var g = new FactoryGrid();
            Assert.AreEqual(CellType.Inlet, g.Get(0, 2).Type);
            Assert.AreEqual(Direction.East, g.Get(0, 2).Direction);
            Assert.AreEqual(CellType.Outlet, g.Get(11, 2).Type);
            Assert.AreEqual(CellType.Pallet, g.Get(5, 2).Type);
        }

        [Test]
        public void NewGrid_UnlocksOnlyInitialRegion()
        {
            var g = new FactoryGrid();
            Assert.IsTrue(g.IsUnlocked(5, 5));
            Assert.IsFalse(g.IsUnlocked(6, 0));
            Assert.IsFalse(g.IsUnlocked(0, 6));
            Assert.AreEqual(CellType.Locked, g.Get(6, 0).Type);
            Assert.AreEqual(CellType.Empty, g.Get(1, 1).Type);
        }

        [Test]
        public void CanPlace_RejectsLockedAndOccupied()
        {
            var g = new FactoryGrid();
            Assert.IsFalse(g.CanPlace(6, 0));
            Assert.IsFalse(g.CanPlace(0, 2));
            Assert.IsTrue(g.CanPlace(1, 1));
            g.Place(1, 1, CellType.Conveyor, Direction.North);
            Assert.IsFalse(g.CanPlace(1, 1));
        }

        [Test]
        public void Place_SetsTypeAndDirection()
        {
            var g = new FactoryGrid();
            g.Place(2, 3, CellType.FastConveyor, Direction.South);
            var c = g.Get(2, 3);
            Assert.AreEqual(CellType.FastConveyor, c.Type);
            Assert.AreEqual(Direction.South, c.Direction);
            Assert.AreEqual(2f, c.Speed);
            Assert.IsTrue(c.IsBuilding);
            Assert.IsTrue(c.IsTrack);
        }

        [Test]
        public void Place_RejectsNonBuildingType()
        {
            var g = new FactoryGrid();
            Assert.Throws<ArgumentException>(() => g.Place(1, 1, CellType.Outlet, Direction.East));
            Assert.Throws<ArgumentException>(() => g.Place(1, 1, CellType.Empty, Direction.East));
        }

        [Test]
        public void Place_OnLockedCell_Throws()
        {
            var g = new FactoryGrid();
            Assert.Throws<InvalidOperationException>(() => g.Place(6, 0, CellType.Conveyor, Direction.East));
        }

        [Test]
        public void UnlockRegion_ConvertsLockedToEmpty()
        {
            var g = new FactoryGrid();
            g.UnlockRegion(9);
            Assert.AreEqual(9, g.UnlockedSize);
            Assert.AreEqual(CellType.Empty, g.Get(8, 8).Type);
            Assert.AreEqual(CellType.Locked, g.Get(9, 0).Type);
            Assert.IsTrue(g.CanPlace(8, 8));
        }

        [Test]
        public void UnlockRegion_CannotShrinkOrExceed()
        {
            var g = new FactoryGrid();
            Assert.Throws<ArgumentOutOfRangeException>(() => g.UnlockRegion(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.UnlockRegion(13));
        }

        [Test]
        public void Clear_ResetsBuildingCell()
        {
            var g = new FactoryGrid();
            g.Place(1, 1, CellType.Conveyor, Direction.East);
            g.Get(1, 1).Demolishing = true;
            g.Get(1, 1).OccupantId = 7;
            g.Clear(1, 1);
            var c = g.Get(1, 1);
            Assert.AreEqual(CellType.Empty, c.Type);
            Assert.IsFalse(c.Demolishing);
            Assert.AreEqual(-1, c.OccupantId);
        }

        [Test]
        public void Clear_OnInletOrOutlet_Throws()
        {
            var g = new FactoryGrid();
            Assert.Throws<InvalidOperationException>(() => g.Clear(0, 2));
            Assert.Throws<InvalidOperationException>(() => g.Clear(11, 2));
        }

        [Test]
        public void Cell_Speed_Is1ForNormalTrack()
        {
            var c = new Cell { Type = CellType.Conveyor };
            Assert.AreEqual(1f, c.Speed);
            Assert.IsFalse(c.IsOccupied);
        }

        [Test]
        public void NewGrid_HasReturnDockFixed()
        {
            var g = new FactoryGrid();
            Assert.AreEqual(CellType.ReturnDock, g.Get(11, 4).Type);
            Assert.IsTrue(g.Get(11, 4).IsDock);
            Assert.IsTrue(g.Get(11, 2).IsDock);
            Assert.IsFalse(g.Get(0, 2).IsDock);
            Assert.IsFalse(g.Get(5, 4).IsDock); // 옛 반품구 자리는 이제 팔레트
            Assert.Throws<InvalidOperationException>(() => g.Clear(11, 4));
            Assert.IsFalse(g.CanPlace(11, 4));
            Assert.IsFalse(g.CanPlace(5, 4));
        }

        [Test]
        public void Place_AcceptsTableAndScanner()
        {
            var g = new FactoryGrid();
            g.Place(1, 1, CellType.Table, Direction.North);
            g.Place(2, 1, CellType.Scanner, Direction.West);
            Assert.AreEqual(CellType.Table, g.Get(1, 1).Type);
            Assert.AreEqual(Direction.West, g.Get(2, 1).Direction);
            Assert.IsTrue(FactoryGrid.IsPlaceable(CellType.Table));
            Assert.IsFalse(FactoryGrid.IsPlaceable(CellType.ReturnDock));
        }

        [Test]
        public void Cell_TrackAndBuildingFlags_IncludeTableAndScanner()
        {
            Assert.IsTrue(new Cell { Type = CellType.Table }.IsTrack);
            Assert.IsTrue(new Cell { Type = CellType.Table }.IsBuilding);
            Assert.IsTrue(new Cell { Type = CellType.Scanner }.IsTrack);
            Assert.IsTrue(new Cell { Type = CellType.Scanner }.IsBuilding);
            Assert.IsFalse(new Cell { Type = CellType.ReturnDock }.IsTrack);
            Assert.IsFalse(new Cell { Type = CellType.ReturnDock }.IsBuilding);
            Assert.AreEqual(1f, new Cell { Type = CellType.Scanner }.Speed);
        }
    }
}
