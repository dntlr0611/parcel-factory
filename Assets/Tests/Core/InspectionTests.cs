using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class InspectionTests
    {
        private FactoryGrid grid;
        private Economy economy;

        private ConveyorSim NewSim(double badChance)
        {
            grid = new FactoryGrid();
            economy = new Economy(0);
            return new ConveyorSim(grid, economy, new PackageFactory(new Random(7), badChance));
        }

        private static Package Pkg(bool bad, bool inspected, bool rejected) =>
            new Package { Id = 1, Bad = bad, Inspected = inspected, Rejected = rejected, Label = new PackageLabel { Weight = 2, Scale = bad ? 6 : 2 } };

        [Test]
        public void Scoring_OutletMatchesSpecTable()
        {
            Assert.AreEqual(DeliveryOutcome.UninspectedToOutlet, DeliveryScoring.Outcome(Pkg(false, false, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.Delivered, DeliveryScoring.Outcome(Pkg(false, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.BadToOutlet, DeliveryScoring.Outcome(Pkg(true, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.RejectedToOutlet, DeliveryScoring.Outcome(Pkg(true, true, true), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.RejectedToOutlet, DeliveryScoring.Outcome(Pkg(false, true, true), CellType.Outlet));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.UninspectedToOutlet));
            Assert.AreEqual(15, DeliveryScoring.Delta(DeliveryOutcome.Delivered));
            Assert.AreEqual(-20, DeliveryScoring.Delta(DeliveryOutcome.BadToOutlet));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.RejectedToOutlet));
        }

        [Test]
        public void Scoring_ReturnDockMatchesSpecTable()
        {
            Assert.AreEqual(DeliveryOutcome.UninspectedToReturn, DeliveryScoring.Outcome(Pkg(true, false, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ApprovedToReturn, DeliveryScoring.Outcome(Pkg(false, true, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ApprovedToReturn, DeliveryScoring.Outcome(Pkg(true, true, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, DeliveryScoring.Outcome(Pkg(true, true, true), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.GoodToReturn, DeliveryScoring.Outcome(Pkg(false, true, true), CellType.ReturnDock));
            Assert.AreEqual(-5, DeliveryScoring.Delta(DeliveryOutcome.UninspectedToReturn));
            Assert.AreEqual(-5, DeliveryScoring.Delta(DeliveryOutcome.ApprovedToReturn));
            Assert.AreEqual(5, DeliveryScoring.Delta(DeliveryOutcome.ReturnedBad));
            Assert.AreEqual(-5, DeliveryScoring.Delta(DeliveryOutcome.GoodToReturn));
            Assert.IsTrue(DeliveryScoring.IsSuccess(DeliveryOutcome.Delivered));
            Assert.IsTrue(DeliveryScoring.IsSuccess(DeliveryOutcome.ReturnedBad));
            Assert.IsFalse(DeliveryScoring.IsSuccess(DeliveryOutcome.GoodToReturn));
            Assert.Throws<ArgumentException>(() => DeliveryScoring.Outcome(Pkg(false, true, false), CellType.Conveyor));
        }

        [Test]
        public void Factory_BadPackagesHaveMismatchedScale()
        {
            var bad = new PackageFactory(new Random(3), 1.0).Create(1, 0, 2, Direction.East);
            Assert.IsTrue(bad.Bad);
            Assert.IsTrue(bad.Label.Mismatch);
            Assert.IsTrue(bad.Label.Scale >= bad.Label.Weight + 3 && bad.Label.Scale <= bad.Label.Weight + 7);
            var good = new PackageFactory(new Random(3), 0.0).Create(2, 0, 2, Direction.East);
            Assert.IsFalse(good.Bad);
            Assert.IsFalse(good.Label.Mismatch);
            Assert.IsTrue(good.Label.Weight >= 1 && good.Label.Weight <= 8);
            Assert.IsFalse(string.IsNullOrEmpty(good.Label.Destination));
            Assert.IsFalse(string.IsNullOrEmpty(good.Label.Method));
            Assert.IsFalse(good.Inspected);
            Assert.AreEqual(Direction.East, good.EntryHeading);
        }

        [Test]
        public void Table_HoldsUninspectedPackage_ThenReleasesAfterInspect()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 2, CellType.Table, Direction.East);
            grid.Place(2, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y)); // 검사 전까지 검사대에 머무름
            Assert.AreEqual(1f, p.Progress);
            p.Inspect(false);
            sim.Tick(0.05f);
            Assert.AreEqual((2, 2), (p.X, p.Y)); // 검사 직후 즉시 이동
            Assert.IsTrue(p.Inspected);
            Assert.IsFalse(p.Rejected);
        }

        [Test]
        public void Scanner_AutoInspectsOnEntry()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 2, CellType.Scanner, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            Assert.IsFalse(p.Inspected);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected); // 불량이므로 거부
        }

        [Test]
        public void Scanner_DoesNotReinspect()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 2, CellType.Scanner, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(false); // 사람이 먼저 승인했다면 스캐너는 뒤집지 않는다
            sim.Tick(1f);
            Assert.IsFalse(p.Rejected);
        }

        [Test]
        public void ReturnDock_PaysForRejectedBad()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 2, CellType.Conveyor, Direction.North);
            grid.Place(1, 3, CellType.Conveyor, Direction.North);
            for (int x = 1; x <= 4; x++) grid.Place(x, 4, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(true);
            DeliveryOutcome seen = DeliveryOutcome.Delivered; int seenDelta = 0;
            sim.PackageDelivered += (pk, o, d) => { seen = o; seenDelta = d; };
            for (int i = 0; i < 8; i++) sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(5, economy.Money);
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, seen);
            Assert.AreEqual(5, seenDelta);
            Assert.AreEqual(1, sim.DeliveredCount);
        }

        [Test]
        public void Outlet_UninspectedGoesNegative()
        {
            var sim = NewSim(0.0);
            for (int x = 1; x <= 4; x++) grid.Place(x, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
            for (int i = 0; i < 5; i++) sim.Tick(1f);
            Assert.AreEqual(-10, economy.Money);
            Assert.AreEqual(0, sim.DeliveredCount);
        }

        [Test]
        public void Take_RemovesPackageAndFreesCell()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreSame(p, sim.PackageAt(1, 2));
            sim.Take(p);
            Assert.IsNull(sim.PackageAt(1, 2));
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(-1, grid.Get(1, 2).OccupantId);
            Assert.AreEqual(0, sim.LostCount);
            Assert.Throws<InvalidOperationException>(() => sim.Take(p));
        }

        [Test]
        public void PlaceCarried_PutsPackageOnTrackWithCellHeading()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 1, CellType.Conveyor, Direction.North);
            var p = new PackageFactory(new Random(1), 0.0).Create(99, 0, 0, Direction.East);
            sim.PlaceCarried(p, 1, 1);
            Assert.AreSame(p, sim.PackageAt(1, 1));
            Assert.AreEqual(Direction.North, p.Heading);
            Assert.AreEqual(Direction.North, p.EntryHeading);
            Assert.AreEqual(0f, p.Progress);
            Assert.AreEqual(99, grid.Get(1, 1).OccupantId);
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(p, 1, 1)); // 점유 중
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(p, 2, 2)); // 트랙 아님
        }

        [Test]
        public void PlaceCarried_OnScanner_InspectsImmediately()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 1, CellType.Scanner, Direction.East);
            var p = new PackageFactory(new Random(1), 1.0).Create(7, 0, 0, Direction.East);
            Assert.IsFalse(p.Inspected);
            sim.PlaceCarried(p, 1, 1);
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected);
        }

        [Test]
        public void CarryPrimitives_RejectPackageStillOnLine()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 2, CellType.Conveyor, Direction.East);
            grid.Place(1, 1, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(p, 1, 1));
            Assert.Throws<InvalidOperationException>(() => sim.DeliverCarried(p, CellType.Outlet));
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual(0, economy.Money);
        }

        [Test]
        public void DeliverCarried_AppliesScoring()
        {
            var sim = NewSim(0.0);
            var p = new PackageFactory(new Random(1), 0.0).Create(5, 0, 0, Direction.East);
            p.Inspect(false);
            Assert.AreEqual(15, sim.DeliverCarried(p, CellType.Outlet));
            Assert.AreEqual(15, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
            var q = new PackageFactory(new Random(1), 0.0).Create(6, 0, 0, Direction.East);
            Assert.AreEqual(-5, sim.DeliverCarried(q, CellType.ReturnDock));
            Assert.AreEqual(10, economy.Money);
            Assert.Throws<ArgumentException>(() => sim.DeliverCarried(q, CellType.Conveyor));
        }
    }
}
