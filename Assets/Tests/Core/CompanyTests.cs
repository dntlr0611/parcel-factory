using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 19.1(O1): 회사(돈·EXP·능력치·시계·작업자 공유)와 공장(격자·컨베이어·입고·해금·트럭·집계)의 분리.
    /// 공장이 하나면 분리 전과 결과가 같다(골든 비교).</summary>
    public class CompanyTests
    {
        /// <summary>결정적 시나리오: 부지 확장 2, 입고 (0,2) → 자동 검사기 → 레일(중간에 고속 레일) → 출고 도크. 이틀 동안 돌린다.</summary>
        private static string RunGolden()
        {
            var f = new PackageFactory(new Random(42), 0.3) { AirChance = 0.25, FragileChance = 0.15 };
            var s = new GameSession(new Economy(5000), f);
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Grid.Clear(5, 2);
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            s.Toolbar.Add(CellType.FastConveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            for (int x = 2; x <= 10; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, x == 6 ? CellType.FastConveyor : CellType.Conveyor, Direction.East));
            int departed = 0, summaries = 0, summaryDelivered = 0;
            s.Sim.Trucks.TruckDeparted += _ => departed++;
            s.DayEnded += d => { summaries++; summaryDelivered += d.Delivered; };
            for (int day = 0; day < 2; day++)
            {
                for (int i = 0; i < 12000 && s.Clock.IsOpen; i++)
                {
                    s.Worker.Move(i % 200 < 100 ? 1f : -1f, 0f, 0.05f, i % 7 == 0);
                    s.Tick(0.05f);
                }
                s.NextDay();
            }
            var sb = new StringBuilder();
            sb.Append(s.Economy.Money).Append('|').Append(s.Sim.DeliveredCount).Append('|').Append(s.Sim.LostCount).Append('|')
              .Append(s.Sim.InspectedCount).Append('|').Append(s.Progression.Exp).Append('|').Append(s.Progression.TotalEarned).Append('|')
              .Append(s.Sim.NextId).Append('|').Append(departed).Append('|').Append(summaries).Append('|').Append(summaryDelivered).Append('|')
              .Append(s.Clock.Day).Append('|').Append(s.Worker.Worker.Stamina.ToString("0.000")).Append('|')
              .Append(s.Worker.Worker.X.ToString("0.000")).Append('|');
            foreach (var p in s.Sim.Packages)
                sb.Append(p.Id).Append('@').Append(p.X).Append(',').Append(p.Y).Append(p.Air ? "a" : "").Append(p.Fragile ? "f" : "").Append(p.Broken ? "b" : "").Append(';');
            return sb.ToString();
        }

        [Test]
        public void OneFactory_MatchesThePreSplitGolden()
        {
            // 분리 전 코드(60fdb2d)에서 같은 시나리오로 얻은 값.
            Assert.AreEqual("6385|102|444|222|1464|1464|669|8|2|102|3|100.000|0.368|660@8,2a;663@5,2a;666@2,2af;667@0,7;668@0,10f;", RunGolden());
        }
        /// <summary>승인·운송장 있는 정상 택배(출고 +30).</summary>
        private static Package Good(int id, bool air = false)
        {
            var p = new PackageFactory(new Random(id), 0.0).Create(id, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            return p;
        }

        [Test]
        public void NewSession_IsACompanyWithOneHeadquarters_AndTheFacadePointsAtIt()
        {
            var s = new GameSession();
            Assert.AreEqual(1, s.Company.Factories.Count);
            Assert.AreEqual(0, s.Company.CurrentIndex);
            Assert.AreEqual(FactoryKind.Headquarters, s.Factory.Kind);
            Assert.AreSame(s.Company.Current.Grid, s.Grid);
            Assert.AreSame(s.Company.Current.Sim, s.Sim);
            Assert.AreSame(s.Company.Current.Unlocks, s.Unlocks);
            Assert.AreSame(s.Company.Economy, s.Economy);
            Assert.AreSame(s.Company.Clock, s.Clock);
            Assert.AreSame(s.Company.Worker, s.Worker);
            Assert.AreEqual("1공장 · 본사 물류센터", s.Factory.Profile.FullName);
        }

        [Test]
        public void TwoFactories_TickTogether_OnTheSameClock()
        {
            var s = new GameSession(new Economy(1000));
            var hq = s.Factory;
            var second = s.Company.AddFactory(FactoryKind.Airport);
            Assert.AreEqual(2, s.Company.Factories.Count);
            Assert.AreSame(hq, s.Factory, "adding a factory does not move the worker");
            Assert.AreEqual(BuildResult.Ok, second.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, hq.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            s.Tick(Spawner.StartInterval);
            Assert.AreEqual(1, hq.Sim.Packages.Count, "the factory we look at spawns");
            Assert.AreEqual(1, second.Sim.Packages.Count, "the one we do not look at spawns too");
            s.Tick(0.5f);
            Assert.AreEqual(hq.Sim.Packages[0].Progress, second.Sim.Packages[0].Progress, 1e-5f, "same dt for both");
            Assert.AreEqual(Clock.OpenMinute + Spawner.StartInterval + 0.5f, s.Clock.MinuteOfDay, 1e-3f, "one clock");
        }

        [Test]
        public void MoneyAndExpAreShared_UnlocksAreNot()
        {
            var s = new GameSession(new Economy(1000));
            var second = s.Company.AddFactory(FactoryKind.Airport);
            Assert.AreEqual(BuildResult.Ok, second.Build.TryPlace(2, 2, CellType.Table, Direction.East));
            Assert.AreEqual(960, s.Economy.Money, "building in the second factory spends company money");
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(760, s.Economy.Money);
            Assert.AreEqual(9, s.Grid.UnlockedSize);
            Assert.AreEqual(6, second.Grid.UnlockedSize, "unlocks belong to one factory");
            Assert.IsFalse(second.Unlocks.IsPurchased(UnlockId.Plot1));
            Assert.AreEqual(UnlockResult.Ok, second.Unlocks.TryPurchase(UnlockId.Plot1), "each factory buys its own");
            Assert.AreEqual(560, s.Economy.Money);
            second.Sim.DeliverCarried(Good(1), CellType.Outlet);
            Assert.AreEqual(590, s.Economy.Money, "a delivery in the background factory pays the company");
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp, "and earns company EXP");
            Assert.AreEqual(0, s.Sim.DeliveredCount, "counts are per factory");
            Assert.AreEqual(1, second.Sim.DeliveredCount);
        }

        [Test]
        public void LunchAndClosing_ApplyToEveryFactory_SummaryHasALinePerFactory()
        {
            var s = new GameSession(new Economy(0));
            var second = s.Company.AddFactory(FactoryKind.Airport);
            for (int i = 0; i < 30; i++) second.Sim.DeliverCarried(Good(100 + i), CellType.Outlet);
            Assert.IsTrue(second.Trucks.IsAway(CellType.Outlet));
            s.Clock.Set(1, 12 * 60);
            float timer = second.Spawner.Timer, away = second.Trucks.AwayMinutes(CellType.Outlet);
            s.Tick(30f);
            Assert.AreEqual(timer, second.Spawner.Timer, "no intake at lunch in the background factory");
            Assert.AreEqual(away, second.Trucks.AwayMinutes(CellType.Outlet), "truck wait stops at lunch there too");
            s.Sim.DeliverCarried(Good(200), CellType.ReturnDock);
            second.Sim.DeliverCarried(Good(201), CellType.ReturnDock);
            DaySummary summary = null;
            s.DayEnded += d => summary = d;
            s.Clock.Set(1, 17 * 60 + 59);
            s.Tick(2f);
            Assert.IsNotNull(summary);
            Assert.AreEqual(2, summary.Factories.Count);
            Assert.AreEqual(1, summary.Factories[0].Number);
            Assert.AreEqual("공항 물류센터", summary.Factories[1].Name);
            Assert.AreEqual(30, summary.Factories[1].Delivered);
            Assert.AreEqual(0, summary.Factories[0].Delivered);
            Assert.AreEqual(30, summary.Delivered, "company total");
            Assert.AreEqual(30 * 30 - 5, summary.Factories[1].Earned);
            Assert.AreEqual(-5, summary.Factories[0].Earned);
            Assert.AreEqual(1 + 1, summary.Factories[1].TrucksDeparted, "18:00 departs loaded trucks in every factory");
            Assert.AreEqual(1, summary.Factories[0].TrucksDeparted);
            Assert.AreEqual(3, summary.TrucksDeparted);
            s.NextDay();
            foreach (var f in s.Company.Factories)
                foreach (var d in TruckFleet.Docks)
                {
                    Assert.IsFalse(f.Trucks.IsAway(d));
                    Assert.AreEqual(0, f.Trucks.Loaded(d));
                }
            Assert.AreEqual(0, s.BuildSummary().Factories[1].Delivered);
            Assert.AreEqual(0, s.BuildSummary().Factories[1].Earned);
        }

        [Test]
        public void Switch_MovesTheWorkerToTheEntrance_RefusesWhileCarrying()
        {
            var s = new GameSession(new Economy(1000));
            var hq = s.Factory;
            var second = s.Company.AddFactory(FactoryKind.Airport);
            var switched = new List<int>();
            s.Company.FactorySwitched += i => switched.Add(i);
            Assert.AreEqual(FactorySwitchResult.AlreadyHere, s.Company.TrySwitch(0));
            Assert.AreEqual(FactorySwitchResult.NoSuchFactory, s.Company.TrySwitch(2));
            Assert.AreEqual(FactorySwitchResult.NoSuchFactory, s.Company.TrySwitch(-1));
            s.Worker.Worker.Carrying = Good(1);
            Assert.AreEqual(FactorySwitchResult.Carrying, s.Company.TrySwitch(1));
            Assert.AreSame(hq, s.Factory);
            s.Worker.Worker.Carrying = null;
            s.Worker.Worker.Stamina = 42f;
            Assert.AreEqual(FactorySwitchResult.Ok, s.Company.TrySwitch(1));
            CollectionAssert.AreEqual(new[] { 1 }, switched);
            Assert.AreSame(second, s.Factory);
            Assert.AreSame(second.Grid, s.Grid, "the facade follows the current factory");
            Assert.AreEqual((1, 1), s.Worker.Tile());
            Assert.AreEqual(42f, s.Worker.Worker.Stamina, "the worker keeps stamina");
            // 작업자는 이제 2공장 격자 위에서 설치한다.
            s.Worker.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.Placed, s.Worker.Interact(new Tool(CellType.Conveyor, Direction.East)));
            Assert.AreEqual(CellType.Conveyor, second.Grid.Get(2, 1).Type);
            Assert.AreEqual(CellType.Empty, hq.Grid.Get(2, 1).Type);
        }

        [Test]
        public void Switch_EntranceFallsBackToTheNearestFreeTile_AndDropsPendingInspection()
        {
            var s = new GameSession(new Economy(1000));
            var second = s.Company.AddFactory(FactoryKind.Airport);
            Assert.AreEqual(BuildResult.Ok, second.Build.TryPlace(1, 1, CellType.Table, Direction.East));
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 2, CellType.Table, Direction.East));
            var p = Good(5); p.Inspected = false;
            s.Sim.PlaceCarried(p, 2, 2);
            s.Worker.Worker.X = 2; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsNotNull(s.Worker.PendingInspection);
            Assert.AreEqual(FactorySwitchResult.Ok, s.Company.TrySwitch(1));
            Assert.IsNull(s.Worker.PendingInspection);
            Assert.AreEqual((1, 0), s.Worker.Tile(), "(1,1) is blocked by a table: nearest tile, lower y first");
            Assert.IsTrue(s.Worker.CanStand(s.Worker.Worker.X, s.Worker.Worker.Y));
        }

        [Test]
        public void AtMostThreeFactories_InOrder()
        {
            var c = new Company(new Economy());
            Assert.Throws<InvalidOperationException>(() => c.AddFactory(FactoryKind.Precision), "airport comes first");
            c.AddFactory(FactoryKind.Airport);
            Assert.Throws<InvalidOperationException>(() => c.AddFactory(FactoryKind.Airport));
            c.AddFactory(FactoryKind.Precision);
            Assert.Throws<InvalidOperationException>(() => c.AddFactory(FactoryKind.Precision));
            Assert.AreEqual(3, c.Factories.Count);
            Assert.AreSame(c.Factories[2], c.Find(FactoryKind.Precision));
        }
        [Test]
        public void Entrance_SkipsAnEnclosedTile_TakesTheNearestReachableOne()
        {
            var s = new GameSession(new Economy(1000));
            var second = s.Company.AddFactory(FactoryKind.Airport);
            // (1,1)은 비어 있지만 네 이웃이 검사대로 막혀 입고구 쪽에서 걸어 들어갈 수 없다. (0,0)도 막힌 구석이다.
            foreach (var (x, y) in new[] { (0, 1), (2, 1), (1, 0), (1, 2) })
                Assert.AreEqual(BuildResult.Ok, second.Build.TryPlace(x, y, CellType.Table, Direction.East));
            Assert.AreEqual(CellType.Empty, second.Grid.Get(1, 1).Type);
            Assert.AreEqual(((float)2, (float)0), s.Company.EntranceOf(second), "nearest reachable: (2,0) beats (0,2)/(2,2) on y; (0,0) is enclosed");
            Assert.AreEqual(FactorySwitchResult.Ok, s.Company.TrySwitch(1));
            Assert.AreEqual((2, 0), s.Worker.Tile());
        }

        [Test]
        public void ClosingDepartures_CountEveryFactory()
        {
            var s = new GameSession(new Economy(0));
            var second = s.Company.AddFactory(FactoryKind.Airport);
            s.Sim.DeliverCarried(Good(1), CellType.Outlet);
            second.Sim.DeliverCarried(Good(2), CellType.Outlet);
            second.Sim.DeliverCarried(Good(3, air: true), CellType.AirOutlet);
            DaySummary d = null;
            s.DayEnded += x => d = x;
            s.Clock.Set(1, 17 * 60 + 59);
            s.Tick(2f);
            Assert.AreEqual(3, d.ClosingDepartures);
            Assert.AreEqual(3, d.TrucksDeparted);
            Assert.AreEqual(0, s.BuildSummary().ClosingDepartures, "only the 18:00 event carries it");
        }

        [Test]
        public void SeedSource_MakesEveryFactoryDeterministic()
        {
            string Run()
            {
                var c = new Company(new Economy(), null, new Random(99));
                c.AddFactory(FactoryKind.Airport);
                var sb = new StringBuilder();
                foreach (var f in c.Factories)
                    for (int i = 0; i < 20; i++) { var p = f.Sim.Factory.Create(i, 0, 0, Direction.East); sb.Append(p.Bad ? 'b' : 'g').Append(p.Air ? 'a' : '-').Append(p.Label.Weight); }
                return sb.ToString();
            }
            Assert.AreEqual(Run(), Run());
            var c2 = new Company(new Economy(), null, new Random(99));
            var injected = new Random(5);
            var a = c2.AddFactory(FactoryKind.Airport, injected);
            var plain = new PackageFactory(new Random(5)).Create(1, 0, 0, Direction.East);
            Assert.AreEqual(plain.Label.Weight, a.Sim.Factory.Create(1, 0, 0, Direction.East).Label.Weight, "an injected rng is used as is");
        }

        [Test]
        public void AirMode_IsAllowedWhereverTheFactoryHasAirDocks()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.Grant(UnlockId.Splitter);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 2, CellType.Splitter, Direction.East));
            Assert.AreEqual(ReconfigureResult.RequiresPlot2, s.Build.SetMode(2, 2, RouteMode.Air), "no air docks before Plot2");
            var airport = s.Company.AddFactory(FactoryKind.Airport);
            airport.Unlocks.Grant(UnlockId.Splitter);
            Assert.AreEqual(BuildResult.Ok, airport.Build.TryPlace(2, 2, CellType.Splitter, Direction.East));
            Assert.AreEqual(6, airport.Grid.UnlockedSize);
            Assert.AreEqual(ReconfigureResult.Ok, airport.Build.SetMode(2, 2, RouteMode.Air), "the airport has air docks from day one");
            s.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(ReconfigureResult.Ok, s.Build.SetMode(2, 2, RouteMode.Air));
        }
    }
}
