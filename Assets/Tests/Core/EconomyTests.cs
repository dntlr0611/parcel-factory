using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class EconomyTests
    {
        [Test]
        public void StartsWith100()
        {
            Assert.AreEqual(100, new Economy().Money);
        }

        [Test]
        public void TrySpend_DeductsWhenAffordable()
        {
            var e = new Economy();
            Assert.IsTrue(e.TrySpend(30));
            Assert.AreEqual(70, e.Money);
        }

        [Test]
        public void TrySpend_FailsWhenShort()
        {
            var e = new Economy(5);
            Assert.IsFalse(e.TrySpend(10));
            Assert.AreEqual(5, e.Money);
        }

        [Test]
        public void Earn_AddsAndRaisesEvent()
        {
            var e = new Economy(0);
            int seen = -1;
            e.MoneyChanged += m => seen = m;
            e.Earn(15);
            Assert.AreEqual(15, e.Money);
            Assert.AreEqual(15, seen);
        }

        [Test]
        public void Adjust_AllowsNegativeBalance()
        {
            var e = new Economy(5);
            int seen = 0;
            e.MoneyChanged += m => seen = m;
            e.Adjust(-20);
            Assert.AreEqual(-15, e.Money);
            Assert.AreEqual(-15, seen);
            e.Adjust(30);
            Assert.AreEqual(15, e.Money);
        }

        [Test]
        public void NegativeAmounts_Throw()
        {
            var e = new Economy();
            Assert.Throws<ArgumentOutOfRangeException>(() => e.Earn(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.TrySpend(-1));
        }

        [Test]
        public void BuildingCosts_MatchSpec()
        {
            Assert.AreEqual(10, BuildingCosts.Cost(CellType.Conveyor));
            Assert.AreEqual(25, BuildingCosts.Cost(CellType.FastConveyor));
            Assert.AreEqual(60, BuildingCosts.Cost(CellType.Splitter));
            Assert.AreEqual(40, BuildingCosts.Cost(CellType.Table));
            Assert.AreEqual(120, BuildingCosts.Cost(CellType.Scanner));
            Assert.Throws<ArgumentException>(() => BuildingCosts.Cost(CellType.Inlet));
        }
    }
}
