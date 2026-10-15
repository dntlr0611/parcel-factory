using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class UnlockTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private UnlockService unlocks;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(1000);
            toolbar = new Toolbar();
            unlocks = new UnlockService(economy, grid, toolbar);
        }

        [Test]
        public void Catalog_HasTenItemsWithSpecPrices()
        {
            Assert.AreEqual(10, UnlockCatalog.All.Count);
            Assert.AreEqual(200, UnlockCatalog.Get(UnlockId.Plot1).Price);
            Assert.AreEqual(500, UnlockCatalog.Get(UnlockId.Plot2).Price);
            Assert.AreEqual(50, UnlockCatalog.Get(UnlockId.RailSouth).Price);
            Assert.AreEqual(50, UnlockCatalog.Get(UnlockId.RailWest).Price);
            Assert.AreEqual(50, UnlockCatalog.Get(UnlockId.RailNorth).Price);
            Assert.AreEqual(100, UnlockCatalog.Get(UnlockId.FastEast).Price);
            Assert.AreEqual(100, UnlockCatalog.Get(UnlockId.FastNorth).Price);
            Assert.AreEqual(300, UnlockCatalog.Get(UnlockId.Splitter).Price);
            Assert.AreEqual(UnlockId.Plot1, UnlockCatalog.Get(UnlockId.Plot2).Requires);
            Assert.IsNull(UnlockCatalog.Get(UnlockId.Plot1).Requires);
        }

        [Test]
        public void Plot1_Expands9AndDeducts()
        {
            var seen = new List<UnlockId>();
            unlocks.Unlocked += seen.Add;
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(9, grid.UnlockedSize);
            Assert.AreEqual(800, economy.Money);
            Assert.IsTrue(unlocks.IsPurchased(UnlockId.Plot1));
            Assert.AreEqual(UnlockId.Plot1, seen[0]);
        }

        [Test]
        public void Plot2_RequiresPlot1()
        {
            Assert.AreEqual(UnlockResult.RequirementMissing, unlocks.TryPurchase(UnlockId.Plot2));
            Assert.AreEqual(1000, economy.Money);
            unlocks.TryPurchase(UnlockId.Plot1);
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Plot2));
            Assert.AreEqual(12, grid.UnlockedSize);
        }

        [Test]
        public void RailDirection_AddsTool()
        {
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.RailNorth));
            Assert.IsTrue(toolbar.Owns(CellType.Conveyor, Direction.North));
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.FastWest));
            Assert.IsTrue(toolbar.Owns(CellType.FastConveyor, Direction.West));
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Splitter));
            Assert.IsTrue(toolbar.SplitterOwned);
        }

        [Test]
        public void CannotBuyTwice()
        {
            unlocks.TryPurchase(UnlockId.RailSouth);
            Assert.AreEqual(UnlockResult.AlreadyOwned, unlocks.TryPurchase(UnlockId.RailSouth));
            Assert.AreEqual(950, economy.Money);
        }

        [Test]
        public void NotEnoughMoney_LeavesStateUntouched()
        {
            var poor = new UnlockService(new Economy(10), grid, toolbar);
            Assert.AreEqual(UnlockResult.NotEnoughMoney, poor.TryPurchase(UnlockId.RailSouth));
            Assert.IsFalse(poor.IsPurchased(UnlockId.RailSouth));
            Assert.IsFalse(toolbar.Owns(CellType.Conveyor, Direction.South));
        }

        [Test]
        public void Grant_AppliesWithoutPayment()
        {
            unlocks.Grant(UnlockId.Plot1);
            unlocks.Grant(UnlockId.Plot1);
            Assert.AreEqual(9, grid.UnlockedSize);
            Assert.AreEqual(1000, economy.Money);
            Assert.AreEqual(1, unlocks.Purchased.Count);
        }

        [Test]
        public void Grant_OutOfOrder_AppliesPrerequisiteFirst()
        {
            unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(12, grid.UnlockedSize);
            Assert.IsTrue(unlocks.IsPurchased(UnlockId.Plot1));
            Assert.IsTrue(unlocks.IsPurchased(UnlockId.Plot2));
            unlocks.Grant(UnlockId.Plot1);
            Assert.AreEqual(2, unlocks.Purchased.Count);
            Assert.AreEqual(1000, economy.Money);
        }
    }
}
