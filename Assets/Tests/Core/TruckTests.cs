using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 18: 동쪽 트럭 네 대의 적재량(30)과 교대(20 게임 분), 대기 중 레일 멈춤·손 배달 거부, 근무 시간에만 흐르는 대기,
    /// 퇴근 출발·다음 날 초기화, 하루 요약 출발 대수, 저장 v9.</summary>
    public class TruckTests
    {
        private int nextId = 1000;

        /// <summary>승인·운송장 있는 정상 택배(출고 +30).</summary>
        private Package Good(bool air = false)
        {
            var p = new PackageFactory(new Random(nextId), 0.0).Create(nextId++, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            return p;
        }

        [Test]
        public void Constants_AndEastDocks()
        {
            Assert.AreEqual(30, TruckFleet.TruckCapacity);
            Assert.AreEqual(20f, TruckFleet.TruckReturnMinutes);
            CollectionAssert.AreEqual(new[] { CellType.Outlet, CellType.ReturnDock, CellType.AirOutlet, CellType.AirReturnDock }, TruckFleet.Docks);
            var t = new TruckFleet();
            foreach (var d in TruckFleet.Docks)
            {
                Assert.AreEqual(0, t.Loaded(d));
                Assert.IsFalse(t.IsAway(d));
                Assert.AreEqual(0f, t.AwayMinutes(d));
            }
            Assert.Throws<ArgumentException>(() => t.Loaded(CellType.Inlet));
        }

        [Test]
        public void ThirtiethParcel_IsScored_ThenTruckDeparts()
        {
            var s = new GameSession(new Economy(0));
            var departed = new List<CellType>();
            s.Sim.Trucks.TruckDeparted += d => departed.Add(d);
            for (int i = 0; i < 29; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            Assert.AreEqual(29, s.Sim.Trucks.Loaded(CellType.Outlet));
            Assert.IsFalse(s.Sim.Trucks.IsAway(CellType.Outlet));
            Assert.IsEmpty(departed);
            s.Sim.DeliverCarried(Good(), CellType.Outlet);
            Assert.AreEqual(30 * 30, s.Economy.Money, "the 30th parcel is scored as usual");
            CollectionAssert.AreEqual(new[] { CellType.Outlet }, departed);
            Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.Outlet));
            Assert.AreEqual(TruckFleet.TruckReturnMinutes, s.Sim.Trucks.AwayMinutes(CellType.Outlet));
            Assert.AreEqual(0, s.Sim.Trucks.Loaded(CellType.Outlet));
            Assert.AreEqual(1, s.Sim.Trucks.DepartedToday);
            Assert.IsFalse(s.Sim.Trucks.IsAway(CellType.ReturnDock), "docks are independent");
            Assert.Throws<InvalidOperationException>(() => s.Sim.DeliverCarried(Good(), CellType.Outlet));
        }

        [Test]
        public void WrongResultsCountAsLoadToo()
        {
            var s = new GameSession(new Economy(0));
            var p = new PackageFactory(new Random(3), 0.0).Create(1, 0, 0, Direction.East); // 미검사 → 반품 −10
            s.Sim.DeliverCarried(p, CellType.ReturnDock);
            Assert.AreEqual(1, s.Sim.Trucks.Loaded(CellType.ReturnDock));
        }

        [Test]
        public void WhileAway_RailParcelWaitsInFront_NotLost_ThenGoesIntoTheNewTruck()
        {
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            // (10,2) 동향 레일 → 출고 도크 (11,2)
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(10, 2, CellType.Conveyor, Direction.East));
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.Outlet));
            var p = Good();
            p.Id = s.Sim.NextId++;
            s.Sim.PlaceCarried(p, 10, 2);
            int lost = s.Sim.LostCount, money = s.Economy.Money;
            s.Sim.Tick(1f);
            s.Sim.Tick(1f);
            Assert.AreEqual((10, 2), (p.X, p.Y), "waits on the rail in front of the dock");
            Assert.AreEqual(1f, p.Progress);
            Assert.AreEqual(lost, s.Sim.LostCount);
            Assert.AreEqual(money, s.Economy.Money);
            var arrived = new List<CellType>();
            s.Sim.Trucks.TruckArrived += d => arrived.Add(d);
            s.Sim.Trucks.Advance(TruckFleet.TruckReturnMinutes);
            CollectionAssert.AreEqual(new[] { CellType.Outlet }, arrived);
            Assert.IsFalse(s.Sim.Trucks.IsAway(CellType.Outlet));
            s.Sim.Tick(0.05f);
            Assert.AreEqual(money + 30, s.Economy.Money);
            Assert.AreEqual(1, s.Sim.Trucks.Loaded(CellType.Outlet));
        }

        [Test]
        public void WhileAway_HandDeliveryIsRefused_WorkerKeepsTheParcel()
        {
            var s = new GameSession(new Economy(0));
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.ReturnDock);
            Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.ReturnDock));
            var w = s.Worker.Worker;
            w.X = 10f; w.Y = 4f; w.Facing = Direction.East;
            Assert.AreEqual((11, 4), s.Worker.FacingTile());
            var p = Good();
            w.Carrying = p;
            int money = s.Economy.Money;
            Assert.AreEqual(InteractResult.TruckAway, s.Worker.Preview(null));
            Assert.AreEqual(InteractResult.TruckAway, s.Worker.Interact(null));
            Assert.AreSame(p, w.Carrying);
            Assert.AreEqual(money, s.Economy.Money);
            w.Y = 2f; // 출고 트럭은 그대로
            Assert.AreEqual(InteractResult.Delivered, s.Worker.Preview(null));
        }

        [Test]
        public void Session_AwayTimeRunsOnlyWhileWorking_NotAtLunchOrPaused()
        {
            var s = new GameSession(new Economy(0));
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            s.Clock.Set(1, 11 * 60 + 50); // 11:50
            s.Tick(5f);
            Assert.AreEqual(15f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f);
            s.Paused = true;
            s.Tick(5f);
            Assert.AreEqual(15f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f, "paused");
            s.Paused = false;
            s.Tick(5f); // 11:55 → 12:00
            Assert.AreEqual(10f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f);
            s.Tick(30f); // 점심 12:00 → 12:30
            Assert.AreEqual(DayPhase.Lunch, s.Clock.Phase);
            Assert.AreEqual(10f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f, "lunch stops the wait");
            s.Tick(35f); // 12:30 → 13:05, 근무 5분
            Assert.AreEqual(5f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f);
            s.Speed = 2f;
            s.Tick(2.5f); // 2배속 5분
            Assert.IsFalse(s.Sim.Trucks.IsAway(CellType.Outlet));
            Assert.AreEqual(0f, s.Sim.Trucks.AwayMinutes(CellType.Outlet));
        }

        [Test]
        public void ClosingTime_LoadedTrucksDepart_SummaryCounts_NextDayAllEmpty()
        {
            var s = new GameSession(new Economy(0));
            s.Unlocks.Grant(UnlockId.Plot2);
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet); // 낮에 1대 출발
            for (int i = 0; i < 5; i++) s.Sim.DeliverCarried(Good(true), CellType.AirOutlet);
            s.Sim.DeliverCarried(Good(), CellType.ReturnDock);
            Assert.AreEqual(1, s.Sim.Trucks.DepartedToday);
            DaySummary summary = null;
            s.DayEnded += d => summary = d;
            var departed = new List<CellType>();
            s.Sim.Trucks.TruckDeparted += d => departed.Add(d);
            s.Clock.Set(1, 17 * 60 + 59);
            s.Tick(2f);
            Assert.IsFalse(s.Clock.IsOpen);
            Assert.IsNotNull(summary);
            CollectionAssert.AreEquivalent(new[] { CellType.ReturnDock, CellType.AirOutlet }, departed, "only loaded trucks leave at 18:00");
            Assert.AreEqual(3, summary.TrucksDeparted);
            foreach (var d in TruckFleet.Docks) Assert.AreEqual(0, s.Sim.Trucks.Loaded(d));
            Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.AirOutlet));
            Assert.IsFalse(s.Sim.Trucks.IsAway(CellType.AirReturnDock), "an empty truck stays");

            var arrived = new List<CellType>();
            s.Sim.Trucks.TruckArrived += d => arrived.Add(d);
            s.NextDay();
            foreach (var d in TruckFleet.Docks)
            {
                Assert.IsFalse(s.Sim.Trucks.IsAway(d), d.ToString());
                Assert.AreEqual(0, s.Sim.Trucks.Loaded(d));
            }
            Assert.AreEqual(3, arrived.Count, "every truck that was away comes back at 09:00");
            Assert.AreEqual(0, s.Sim.Trucks.DepartedToday);
            Assert.AreEqual(0, s.BuildSummary().TrucksDeparted);
        }

        [Test]
        public void Restore_ValidatesValues()
        {
            var t = new TruckFleet();
            t.Restore(CellType.Outlet, 12, 0f);
            Assert.AreEqual(12, t.Loaded(CellType.Outlet));
            t.Restore(CellType.ReturnDock, 0, 7.5f);
            Assert.IsTrue(t.IsAway(CellType.ReturnDock));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Restore(CellType.Outlet, TruckFleet.TruckCapacity, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Restore(CellType.Outlet, -1, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Restore(CellType.Outlet, 0, TruckFleet.TruckReturnMinutes + 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Restore(CellType.Outlet, 0, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Restore(CellType.Outlet, 3, 5f), "an away truck carries nothing");
            Assert.Throws<ArgumentException>(() => t.Restore(CellType.Table, 0, 0f));
        }

        [Test]
        public void Save_V9_RoundTripsTrucks_V8ReadsAsEmptyTrucks()
        {
            var s = new GameSession(new Economy(0));
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            s.Tick(3f);
            for (int i = 0; i < 12; i++) s.Sim.DeliverCarried(Good(), CellType.ReturnDock);
            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(11, st.Version); // v11: 점심 카드(스펙 21.4)
            Assert.AreEqual(2, st.Trucks.Count, "air trucks are saved only after Plot2");
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            Assert.AreEqual(17f, r.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f);
            Assert.AreEqual(12, r.Sim.Trucks.Loaded(CellType.ReturnDock));
            Assert.AreEqual(1, r.Sim.Trucks.DepartedToday);
            Assert.AreEqual(1, r.BuildSummary().TrucksDeparted);

            var bad = SaveSerializer.Capture(s);
            bad.Trucks[1].Loaded = 31;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            bad = SaveSerializer.Capture(s);
            bad.Trucks[0].Dock = "Inlet";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            bad = SaveSerializer.Capture(s);
            bad.TrucksDepartedToday = -1;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));

            bad = SaveSerializer.Capture(s);
            bad.Trucks[1].Dock = "Outlet"; // 같은 도크 두 번 → 손상
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            bad = SaveSerializer.Capture(s);
            bad.Trucks.Add(new TruckState { Dock = "AirOutlet", Loaded = 0, AwayMinutes = 0f }); // 부지 확장 2 없이 항공 트럭 항목 → 손상
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));

            var s2 = new GameSession(new Economy(0));
            s2.Unlocks.Grant(UnlockId.Plot2);
            var air = Good(true);
            s2.Sim.DeliverCarried(air, CellType.AirOutlet);
            var st2 = SaveSerializer.Capture(s2);
            Assert.AreEqual(4, st2.Trucks.Count);
            Assert.AreEqual(1, SaveSerializer.Restore(st2).Sim.Trucks.Loaded(CellType.AirOutlet));

            var old = SaveSerializer.Capture(s);
            old.Version = 8;
            old.Trucks = null;
            old.TrucksDepartedToday = 0;
            var r8 = SaveSerializer.Restore(old);
            foreach (var d in TruckFleet.Docks)
            {
                Assert.AreEqual(0, r8.Sim.Trucks.Loaded(d));
                Assert.IsFalse(r8.Sim.Trucks.IsAway(d));
            }
        }
    }
}
