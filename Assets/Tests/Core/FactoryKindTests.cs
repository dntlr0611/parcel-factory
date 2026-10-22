using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 19.2(O2): 공장 종류와 프로필(가격·항공/취급주의 비율·보상 보정·처음부터 있는 도크), 구매 조건(직전 공장의 부지 확장 2 + 자동 검사기).</summary>
    public class FactoryKindTests
    {
        private static Package Good(int id, bool air = false, bool fragile = false)
        {
            var p = new PackageFactory(new Random(id), 0.0).Create(id, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            p.Fragile = fragile;
            p.FragileSticker = fragile;
            return p;
        }

        [Test]
        public void Profiles_PerSpecTable()
        {
            var hq = FactoryProfile.Of(FactoryKind.Headquarters);
            var air = FactoryProfile.Of(FactoryKind.Airport);
            var pre = FactoryProfile.Of(FactoryKind.Precision);
            Assert.AreEqual("1공장 · 본사 물류센터", hq.FullName);
            Assert.AreEqual("2공장 · 공항 물류센터", air.FullName);
            Assert.AreEqual("3공장 · 정밀화물 센터", pre.FullName);
            Assert.AreEqual(0, hq.Price);
            Assert.AreEqual(3000, air.Price);
            Assert.AreEqual(8000, pre.Price);
            Assert.IsFalse(hq.AirDocksFromStart);
            Assert.IsTrue(air.AirDocksFromStart);
            Assert.IsFalse(pre.AirDocksFromStart);
            Assert.AreEqual(0.6, air.FixedAirChance);
            Assert.IsNull(hq.FixedAirChance);
            Assert.AreEqual(0.5, pre.FixedFragileChance);
            Assert.IsNull(air.FixedFragileChance);
            Assert.AreEqual(35, air.Rewards.AirDelivered);
            Assert.AreEqual(60, pre.Rewards.FragileDelivered);
            Assert.AreEqual(30, hq.Rewards.AirDelivered);
            Assert.AreEqual(45, hq.Rewards.FragileDelivered);
            Assert.AreEqual(FactoryKind.Headquarters, FactoryProfile.Previous(FactoryKind.Airport));
            Assert.AreEqual(FactoryKind.Airport, FactoryProfile.Previous(FactoryKind.Precision));
        }

        [Test]
        public void Buying_NeedsPlot2AndScannerInThePreviousFactory_AndMoney()
        {
            var s = new GameSession(new Economy(20000));
            var c = s.Company;
            Assert.AreEqual(FactoryKind.Airport, c.NextFactoryKind);
            Assert.AreEqual(FactoryPurchaseResult.RequirementMissing, c.CanBuyFactory(FactoryKind.Airport));
            s.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(FactoryPurchaseResult.RequirementMissing, c.TryBuyFactory(FactoryKind.Airport), "scanner too");
            s.Unlocks.Grant(UnlockId.Scanner);
            Assert.AreEqual(FactoryPurchaseResult.RequirementMissing, c.CanBuyFactory(FactoryKind.Precision), "the airport comes first");
            Assert.AreEqual(FactoryPurchaseResult.Ok, c.CanBuyFactory(FactoryKind.Airport));
            Assert.AreEqual(20000, s.Economy.Money, "Can does not spend");
            FactorySite added = null;
            c.FactoryAdded += f => added = f;
            Assert.AreEqual(FactoryPurchaseResult.Ok, c.TryBuyFactory(FactoryKind.Airport));
            Assert.AreEqual(17000, s.Economy.Money);
            Assert.AreEqual(2, c.Factories.Count);
            Assert.AreSame(c.Factories[1], added);
            Assert.AreEqual(FactoryKind.Airport, added.Kind);
            Assert.AreEqual(0, c.CurrentIndex, "buying does not move the worker");
            Assert.AreEqual(FactoryPurchaseResult.AlreadyOwned, c.TryBuyFactory(FactoryKind.Airport));
            Assert.AreEqual(FactoryPurchaseResult.AlreadyOwned, c.CanBuyFactory(FactoryKind.Headquarters));
            Assert.AreEqual(FactoryKind.Precision, c.NextFactoryKind);
            Assert.AreEqual(FactoryPurchaseResult.RequirementMissing, c.TryBuyFactory(FactoryKind.Precision));
            added.Unlocks.Grant(UnlockId.Plot2);
            added.Unlocks.Grant(UnlockId.Scanner);
            Assert.AreEqual(FactoryPurchaseResult.Ok, c.TryBuyFactory(FactoryKind.Precision));
            Assert.AreEqual(9000, s.Economy.Money);
            Assert.IsNull(c.NextFactoryKind);
            Assert.AreEqual(3, c.Factories.Count);
        }

        [Test]
        public void Buying_WithoutEnoughMoney_ChangesNothing()
        {
            var s = new GameSession(new Economy(2999));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Unlocks.Grant(UnlockId.Scanner);
            Assert.AreEqual(FactoryPurchaseResult.NotEnoughMoney, s.Company.TryBuyFactory(FactoryKind.Airport));
            Assert.AreEqual(2999, s.Economy.Money);
            Assert.AreEqual(1, s.Company.Factories.Count);
        }

        [Test]
        public void NewFactory_StartsLikeTheFirst_6x6_TwoPallets_OneInlet()
        {
            var c = new Company(new Economy());
            var f = c.AddFactory(FactoryKind.Airport);
            Assert.AreEqual(6, f.Grid.UnlockedSize);
            Assert.AreEqual(CellType.Pallet, f.Grid.Get(FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY).Type);
            Assert.AreEqual(CellType.Pallet, f.Grid.Get(FactoryGrid.StartRedPalletX, FactoryGrid.StartRedPalletY).Type);
            Assert.AreEqual(CellType.Inlet, f.Grid.Get(FactoryGrid.InletX, FactoryGrid.InletY).Type);
            Assert.AreEqual(1, f.Spawner.Timers().Count);
            Assert.IsTrue(f.Toolbar.Owns(CellType.Conveyor, Direction.East));
            Assert.IsFalse(f.Toolbar.ScannerOwned);
        }

        [Test]
        public void Airport_HasAirDocksFromTheStart_60PercentAir_FragileAfterItsOwnPlot2()
        {
            var c = new Company(new Economy(5000));
            var f = c.AddFactory(FactoryKind.Airport, new Random(7));
            Assert.IsTrue(f.HasAirDocks);
            Assert.AreEqual(CellType.AirOutlet, f.Grid.Get(FactoryGrid.AirOutletX, FactoryGrid.AirOutletY).Type);
            Assert.AreEqual(CellType.AirReturnDock, f.Grid.Get(FactoryGrid.AirReturnDockX, FactoryGrid.AirReturnDockY).Type);
            Assert.IsFalse(c.Factories[0].HasAirDocks);
            Assert.AreEqual(0.6, f.Sim.Factory.AirChance);
            Assert.AreEqual(0.0, f.Sim.Factory.FragileChance);
            int air = 0;
            for (int i = 0; i < 2000; i++) if (f.Sim.Factory.Create(i, 0, 0, Direction.East).Air) air++;
            Assert.That(air, Is.InRange(1140, 1260), "about 60% with a fixed seed");
            // 항공 트럭도 바로 쓸 수 있다.
            f.Sim.DeliverCarried(Good(1, air: true), CellType.AirOutlet);
            Assert.AreEqual(1, f.Trucks.Loaded(CellType.AirOutlet));
            // 취급주의는 이 공장의 부지 확장 2를 산 다음 날부터 15%, 항공 비율은 확장과 무관하게 60%.
            f.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(0.6, f.Sim.Factory.AirChance);
            Assert.AreEqual(2, f.FragileFromDay);
            Assert.AreEqual(0.0, f.Sim.Factory.FragileChance);
            Assert.AreEqual(0, c.Factories[0].FragileFromDay, "the first factory is not affected");
            c.Clock.Set(1, Clock.CloseMinute);
            c.NextDay();
            Assert.AreEqual(PackageFactory.FragileChanceAfterPlot2, f.Sim.Factory.FragileChance);
            Assert.AreEqual(0.0, c.Factories[0].Sim.Factory.FragileChance);
        }

        [Test]
        public void Precision_HalfFragileFromTheStart_AirAfterItsPlot2()
        {
            var c = new Company(new Economy(5000));
            c.AddFactory(FactoryKind.Airport);
            var f = c.AddFactory(FactoryKind.Precision, new Random(9));
            Assert.IsFalse(f.HasAirDocks);
            Assert.AreEqual(0.5, f.Sim.Factory.FragileChance);
            Assert.AreEqual(0.0, f.Sim.Factory.AirChance);
            int fragile = 0;
            for (int i = 0; i < 2000; i++) if (f.Sim.Factory.Create(i, 0, 0, Direction.East).Fragile) fragile++;
            Assert.That(fragile, Is.InRange(940, 1060));
            f.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(PackageFactory.AirChanceAfterPlot2, f.Sim.Factory.AirChance);
            Assert.AreEqual(0.5, f.Sim.Factory.FragileChance, "still half fragile");
            Assert.IsTrue(f.HasAirDocks);
            c.Clock.Set(1, Clock.CloseMinute);
            c.NextDay();
            Assert.AreEqual(0.5, f.Sim.Factory.FragileChance, "the 15% rule does not replace the fixed rate");
        }

        [Test]
        public void SameSeed_SameParcels_InEveryKind_RngOrderUnchanged()
        {
            // 항공·취급주의 추첨 순서는 그대로다: 같은 시드에서 공장 종류가 달라도 불량·무게·목적지는 같다.
            var c = new Company(new Economy());
            var a = c.AddFactory(FactoryKind.Airport, new Random(3));
            var plain = new PackageFactory(new Random(3)).Create(1, 0, 0, Direction.East);
            var p = a.Sim.Factory.Create(1, 0, 0, Direction.East);
            Assert.AreEqual(plain.Bad, p.Bad);
            Assert.AreEqual(plain.Label.Weight, p.Label.Weight);
            Assert.AreEqual(plain.Label.Destination, p.Label.Destination);
            Assert.AreEqual(plain.Label.Scale, p.Label.Scale);
        }

        [Test]
        public void Rewards_ArePerFactory_GlobalTableUnchanged()
        {
            var s = new GameSession(new Economy(0));
            var c = s.Company;
            var airport = c.AddFactory(FactoryKind.Airport);
            var precision = c.AddFactory(FactoryKind.Precision);

            Assert.AreEqual(35, airport.Sim.DeliverCarried(Good(1, air: true), CellType.AirOutlet), "airport: air delivered +35");
            Assert.AreEqual(30, airport.Sim.DeliverCarried(Good(2), CellType.Outlet), "airport: plain delivered stays +30");
            Assert.AreEqual(45, airport.Sim.DeliverCarried(Good(3, fragile: true), CellType.Outlet), "airport: fragile stays +45");
            Assert.AreEqual(10, airport.Sim.DeliverCarried(RejectedBad(4, air: true), CellType.AirReturnDock), "returns unchanged");
            Assert.AreEqual(60, precision.Sim.DeliverCarried(Good(5, fragile: true), CellType.Outlet), "precision: fragile delivered +60");
            Assert.AreEqual(30, precision.Sim.DeliverCarried(Good(6), CellType.Outlet));
            Assert.AreEqual(45, s.Sim.DeliverCarried(Good(7, fragile: true), CellType.Outlet), "headquarters unchanged");
            Assert.AreEqual(-10, s.Sim.DeliverCarried(Good(8, air: true), CellType.Outlet), "headquarters: wrong carrier as before");
            Assert.AreEqual(35 + 30 + 45 + 10 + 60 + 30 + 45 - 10, s.Economy.Money);
            // 전역 판정표는 그대로다.
            Assert.AreEqual(30, DeliveryScoring.Delta(DeliveryOutcome.Delivered));
            Assert.AreEqual(45, DeliveryScoring.Delta(DeliveryOutcome.DeliveredFragile));
            Assert.AreEqual(35, DeliveryScoring.Delta(DeliveryOutcome.Delivered, Good(9, air: true), FactoryProfile.Of(FactoryKind.Airport).Rewards));
            Assert.AreEqual(30, DeliveryScoring.Delta(DeliveryOutcome.Delivered, Good(9, air: true), null));
            Assert.AreEqual(-30, DeliveryScoring.Delta(DeliveryOutcome.BadToOutlet, Good(9, air: true), FactoryProfile.Of(FactoryKind.Airport).Rewards));
        }

        private static Package RejectedBad(int id, bool air)
        {
            var p = new PackageFactory(new Random(id), 1.0).Create(id, 0, 0, Direction.East);
            p.Inspect(true);
            p.Waybill = true;
            p.Air = air;
            return p;
        }
    }
}
