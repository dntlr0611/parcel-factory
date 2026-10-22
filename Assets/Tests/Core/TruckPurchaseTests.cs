using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 20(O6): 도크별 택배차 1~3대, 교대 2분·운행 20분, 운행 복귀 후 차고 대기, 근무 중에만 흐르는 시간, 18시·09시, 구매(₩400/₩900), 저장 v10.</summary>
    public class TruckPurchaseTests
    {
        private int nextId = 3000;

        private Package Good(bool air = false)
        {
            var p = new PackageFactory(new Random(nextId), 0.0).Create(nextId++, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            return p;
        }

        private static void Fill(TruckFleet t, CellType dock)
        {
            for (int i = 0; i < TruckFleet.TruckCapacity; i++) t.Load(dock);
        }

        [Test]
        public void Constants()
        {
            Assert.AreEqual(2f, TruckFleet.TruckSwapMinutes);
            Assert.AreEqual(3, TruckFleet.MaxTrucksPerDock);
            Assert.AreEqual(400, TruckFleet.SecondTruckPrice);
            Assert.AreEqual(900, TruckFleet.ThirdTruckPrice);
            var t = new TruckFleet();
            foreach (var d in TruckFleet.Docks) Assert.AreEqual(1, t.Count(d));
            Assert.AreEqual(400, t.NextTruckPrice(CellType.Outlet));
            Assert.IsTrue(t.AddVehicle(CellType.Outlet));
            Assert.AreEqual(900, t.NextTruckPrice(CellType.Outlet));
            Assert.IsTrue(t.AddVehicle(CellType.Outlet));
            Assert.IsNull(t.NextTruckPrice(CellType.Outlet));
            Assert.IsFalse(t.AddVehicle(CellType.Outlet));
            Assert.AreEqual(3, t.Count(CellType.Outlet));
            Assert.AreEqual(1, t.Count(CellType.ReturnDock), "per dock");
        }

        [Test]
        public void OneTruck_Waits20Minutes_AsBefore()
        {
            var t = new TruckFleet();
            Fill(t, CellType.Outlet);
            Assert.IsTrue(t.IsAway(CellType.Outlet));
            Assert.IsFalse(t.IsSwapping(CellType.Outlet));
            Assert.AreEqual(20f, t.AwayMinutes(CellType.Outlet));
            t.Advance(19.5f);
            Assert.IsTrue(t.IsAway(CellType.Outlet));
            t.Advance(0.5f);
            Assert.IsFalse(t.IsAway(CellType.Outlet));
        }

        [Test]
        public void TwoTrucks_SwapIn2Minutes_ThenTheReturningTruckWaitsInTheGarage()
        {
            var t = new TruckFleet();
            t.AddVehicle(CellType.Outlet);
            Assert.IsFalse(t.IsAway(CellType.Outlet));
            Assert.AreEqual(1, t.GarageCount(CellType.Outlet));
            var arrived = new List<CellType>();
            t.TruckArrived += d => arrived.Add(d);
            Fill(t, CellType.Outlet);
            Assert.IsTrue(t.IsAway(CellType.Outlet), "no truck at the dock during the swap");
            Assert.IsTrue(t.IsSwapping(CellType.Outlet));
            Assert.AreEqual(2f, t.AwayMinutes(CellType.Outlet));
            Assert.AreEqual(0, t.GarageCount(CellType.Outlet));
            t.Advance(1.5f);
            Assert.AreEqual(0.5f, t.AwayMinutes(CellType.Outlet), 1e-4f);
            t.Advance(0.5f);
            Assert.IsFalse(t.IsAway(CellType.Outlet));
            CollectionAssert.AreEqual(new[] { CellType.Outlet }, arrived);
            // 두 번째 트럭도 가득 차면: 첫 트럭은 아직 운행 중(18분 남음) → 차고 비어 있음 → 첫 트럭이 돌아오는 즉시 도크로.
            Fill(t, CellType.Outlet);
            Assert.IsFalse(t.IsSwapping(CellType.Outlet));
            Assert.AreEqual(18f, t.AwayMinutes(CellType.Outlet), 1e-4f, "the first truck comes back first");
            t.Advance(18f);
            Assert.IsFalse(t.IsAway(CellType.Outlet));
            Assert.AreEqual(2, arrived.Count);
            t.Advance(2f); // 두 번째 트럭이 돌아오지만 도크가 차 있다 → 차고
            Assert.AreEqual(1, t.GarageCount(CellType.Outlet));
            Assert.AreEqual(2, arrived.Count, "parking in the garage is not an arrival at the dock");
            Fill(t, CellType.Outlet);
            Assert.IsTrue(t.IsSwapping(CellType.Outlet), "the parked truck swaps in");
        }

        [Test]
        public void WaitTimes_OneTwoThreeTrucks()
        {
            // 도크에 120개를 손으로 넣을 때(즉시 실을 수 있으면 바로) 도크가 비는 총 시간.
            float Downtime(int trucks)
            {
                var t = new TruckFleet();
                for (int i = 1; i < trucks; i++) t.AddVehicle(CellType.Outlet);
                float waited = 0f;
                for (int n = 0; n < 120; n++)
                {
                    while (t.IsAway(CellType.Outlet)) { t.Advance(0.25f); waited += 0.25f; }
                    t.Load(CellType.Outlet);
                }
                return waited;
            }
            Assert.AreEqual(60f, Downtime(1), 1e-3f, "3 departures before the 120th, 20 min each");
            Assert.AreEqual(2f + 18f + 2f, Downtime(2), 1e-3f, "swap, wait for the first truck, then the second comes back");
            Assert.AreEqual(2f + 2f + 16f, Downtime(3), 1e-3f, "swap, swap, then wait for the first truck");
        }

        [Test]
        public void SameTickReturns_InOrder_DepartLoadedCounts_DepartedLoad()
        {
            var t = new TruckFleet();
            t.AddVehicle(CellType.Outlet);
            t.AddVehicle(CellType.Outlet);
            Fill(t, CellType.Outlet);           // 1번 운행 20, 2번 교대
            Assert.AreEqual(30, t.DepartedLoad(CellType.Outlet));
            t.Advance(2f);                      // 2번 도크
            Fill(t, CellType.Outlet);           // 2번 운행 20, 3번 교대
            t.Advance(2f);                      // 3번 도크, 1번 16 · 2번 18 남음
            Fill(t, CellType.Outlet);           // 3번 운행 20, 차고 비어 있음
            Assert.AreEqual(16f, t.AwayMinutes(CellType.Outlet), 1e-4f);
            var arrived = 0;
            t.TruckArrived += _ => arrived++;
            t.Advance(30f);                     // 셋 다 한 번에 돌아온다: 가장 먼저(1번)만 도크, 나머지는 차고
            Assert.AreEqual(1, arrived);
            var vs = t.VehiclesOf(CellType.Outlet);
            Assert.AreEqual(VehicleStatus.AtDock, vs[0].Status);
            Assert.AreEqual(VehicleStatus.Garage, vs[1].Status);
            Assert.AreEqual(VehicleStatus.Garage, vs[2].Status);
            for (int i = 0; i < 7; i++) t.Load(CellType.Outlet);
            t.Load(CellType.ReturnDock);
            Assert.AreEqual(2, t.DepartLoaded());
            Assert.AreEqual(7, t.DepartedLoad(CellType.Outlet));
            Assert.AreEqual(0, t.DepartLoaded(), "nothing left to depart");
        }

        [Test]
        public void AddingATruck_WhileTheDockIsEmpty_SwapsItIn()
        {
            var t = new TruckFleet();
            Fill(t, CellType.ReturnDock);
            Assert.AreEqual(20f, t.AwayMinutes(CellType.ReturnDock));
            t.AddVehicle(CellType.ReturnDock);
            Assert.IsTrue(t.IsSwapping(CellType.ReturnDock));
            Assert.AreEqual(2f, t.AwayMinutes(CellType.ReturnDock));
            t.Advance(2f);
            Assert.IsFalse(t.IsAway(CellType.ReturnDock));
            t.Advance(18f);
            Assert.AreEqual(1, t.GarageCount(CellType.ReturnDock));
        }

        [Test]
        public void Session_SwapTimeStopsAtLunch_18hDeparts_09hEveryTruckBack()
        {
            var s = new GameSession(new Economy(5000));
            Assert.AreEqual(TruckPurchaseResult.Ok, s.Factory.TryBuyTruck(CellType.Outlet));
            Assert.AreEqual(4600, s.Economy.Money);
            Assert.AreEqual(TruckPurchaseResult.Ok, s.Factory.TryBuyTruck(CellType.Outlet));
            Assert.AreEqual(3700, s.Economy.Money);
            Assert.AreEqual(TruckPurchaseResult.MaxTrucks, s.Factory.TryBuyTruck(CellType.Outlet));
            Assert.AreEqual(TruckPurchaseResult.NoDock, s.Factory.TryBuyTruck(CellType.AirOutlet), "no air docks before Plot2");
            Assert.AreEqual(TruckPurchaseResult.NoDock, s.Factory.TryBuyTruck(CellType.Inlet));
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            Assert.IsTrue(s.Sim.Trucks.IsSwapping(CellType.Outlet));
            s.Clock.Set(1, 11 * 60 + 59);
            s.Tick(1f); // 11:59 → 12:00, 교대 1분
            Assert.AreEqual(1f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f);
            s.Tick(30f);
            Assert.AreEqual(1f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f, "lunch stops the swap");
            s.Paused = true; s.Tick(5f); s.Paused = false;
            Assert.AreEqual(1f, s.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f, "pause stops it");
            s.Clock.Set(1, 13 * 60);
            s.Tick(1f);
            Assert.IsFalse(s.Sim.Trucks.IsAway(CellType.Outlet));
            for (int i = 0; i < 4; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            s.Clock.Set(1, 17 * 60 + 59);
            s.Tick(2f);
            Assert.IsFalse(s.Clock.IsOpen);
            Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.Outlet), "the loaded truck leaves at 18:00");
            s.NextDay();
            var vs = s.Sim.Trucks.VehiclesOf(CellType.Outlet);
            Assert.AreEqual(3, vs.Count);
            Assert.AreEqual(VehicleStatus.AtDock, vs[0].Status);
            Assert.AreEqual(VehicleStatus.Garage, vs[1].Status);
            Assert.AreEqual(VehicleStatus.Garage, vs[2].Status);
            Assert.AreEqual(0, s.Sim.Trucks.Loaded(CellType.Outlet));
            Assert.AreEqual(2, s.Sim.Trucks.GarageCount(CellType.Outlet));
        }

        [Test]
        public void Purchase_IsPerFactory_AndAirDocksOnlyWhenPresent()
        {
            var s = new GameSession(new Economy(5000));
            var airport = s.Company.AddFactory(FactoryKind.Airport);
            Assert.AreEqual(TruckPurchaseResult.Ok, airport.TryBuyTruck(CellType.AirReturnDock), "the airport has air docks from the start");
            Assert.AreEqual(2, airport.Trucks.Count(CellType.AirReturnDock));
            Assert.AreEqual(1, airport.Trucks.Count(CellType.Outlet));
            Assert.AreEqual(1, s.Sim.Trucks.Count(CellType.AirReturnDock), "the first factory is separate");
            Assert.AreEqual(TruckPurchaseResult.NoDock, s.Factory.CanBuyTruck(CellType.AirReturnDock));
            s.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(TruckPurchaseResult.Ok, s.Factory.CanBuyTruck(CellType.AirReturnDock));
            var poor = new GameSession(new Economy(399));
            Assert.AreEqual(TruckPurchaseResult.NotEnoughMoney, poor.Factory.TryBuyTruck(CellType.Outlet));
            Assert.AreEqual(399, poor.Economy.Money);
        }

        [Test]
        public void Save_V10_RoundTripsVehicles_V9ReadsOneTruckPerDock_ValidatesStates()
        {
            var s = new GameSession(new Economy(5000));
            s.Factory.TryBuyTruck(CellType.Outlet);
            s.Factory.TryBuyTruck(CellType.Outlet);
            s.Factory.TryBuyTruck(CellType.ReturnDock);
            for (int i = 0; i < 30; i++) s.Sim.DeliverCarried(Good(), CellType.Outlet);
            s.Tick(1.5f); // 교대 0.5분 남음
            for (int i = 0; i < 5; i++) s.Sim.DeliverCarried(Good(), CellType.ReturnDock);
            var st = SaveSerializer.Capture(s);
            var outlet = st.Trucks[0];
            Assert.AreEqual("Outlet", outlet.Dock);
            Assert.AreEqual(3, outlet.Vehicles.Count);
            Assert.AreEqual("Driving", outlet.Vehicles[0].Status);
            Assert.AreEqual("Swapping", outlet.Vehicles[1].Status);
            Assert.AreEqual("Garage", outlet.Vehicles[2].Status);

            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            var rt = r.Sim.Trucks;
            Assert.AreEqual(3, rt.Count(CellType.Outlet));
            Assert.IsTrue(rt.IsSwapping(CellType.Outlet));
            Assert.AreEqual(0.5f, rt.AwayMinutes(CellType.Outlet), 1e-3f);
            Assert.AreEqual(18.5f, rt.VehiclesOf(CellType.Outlet)[0].Minutes, 1e-3f);
            Assert.AreEqual(2, rt.Count(CellType.ReturnDock));
            Assert.AreEqual(5, rt.Loaded(CellType.ReturnDock));
            Assert.AreEqual(1, rt.GarageCount(CellType.ReturnDock));

            // v9: Vehicles 없음 → 도크마다 1대(대기 중이면 운행 중).
            var old = SaveSerializer.Capture(s);
            old.Version = 9;
            foreach (var t in old.Trucks) t.Vehicles = null;
            var r9 = SaveSerializer.Restore(old);
            foreach (var d in TruckFleet.Docks) Assert.AreEqual(1, r9.Sim.Trucks.Count(d));
            Assert.IsTrue(r9.Sim.Trucks.IsAway(CellType.Outlet));
            Assert.AreEqual(0.5f, r9.Sim.Trucks.AwayMinutes(CellType.Outlet), 1e-3f, "AwayMinutes is read as the trip");
            Assert.AreEqual(5, r9.Sim.Trucks.Loaded(CellType.ReturnDock));

            void Corrupt(Action<GameState> edit)
            {
                var bad = SaveSerializer.Capture(s);
                edit(bad);
                Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            }
            Corrupt(b => b.Trucks[0].Vehicles.Add(new VehicleState { Status = "Garage" })); // 4대
            Corrupt(b => b.Trucks[0].Vehicles.Clear()); // 0대
            Corrupt(b => b.Trucks[0].Vehicles[2].Status = "AtDock"); // 도크와 교대가 함께
            Corrupt(b => b.Trucks[0].Vehicles[1].Minutes = 3f); // 교대 2분 초과
            Corrupt(b => b.Trucks[0].Vehicles[0].Minutes = 0f); // 운행 0분
            Corrupt(b => b.Trucks[0].Vehicles[2].Minutes = 1f); // 차고 차량에 시간
            Corrupt(b => b.Trucks[0].Vehicles[1].Status = "Bogus");
            Corrupt(b => b.Trucks[0].Vehicles[1] = null);
            Corrupt(b => { b.Trucks[0].Vehicles[1].Status = "Garage"; b.Trucks[0].Vehicles[1].Minutes = 0f; }); // 도크 비었는데 교대 없음 + 차고
            Corrupt(b => b.Trucks[0].Loaded = 3); // 떠나 있는 도크에 적재
        }
    }
}
