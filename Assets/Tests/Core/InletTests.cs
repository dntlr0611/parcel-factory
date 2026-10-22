using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 16: 미검사 감점 −20/−10, 부지마다 입고구 (0,7)·(0,10), 입고구별 타이머(새 입고구는 간격의 절반 뒤 첫 택배), 저장 v8.</summary>
    public class InletTests
    {
        // ---- 16.1 미검사 감점 ----

        [Test]
        public void UninspectedPenalty_IsTwentyAndTen()
        {
            Assert.AreEqual(-20, DeliveryScoring.Delta(DeliveryOutcome.UninspectedToOutlet));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.UninspectedToReturn));
            var p = new Package { Label = new PackageLabel() };
            Assert.AreEqual(DeliveryOutcome.UninspectedToOutlet, DeliveryScoring.Outcome(p, CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.UninspectedToReturn, DeliveryScoring.Outcome(p, CellType.ReturnDock));
            p.Air = true; // 순서 그대로: 운송 수단이 먼저
            Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(p, CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.UninspectedToOutlet, DeliveryScoring.Outcome(p, CellType.AirOutlet));
        }

        // ---- 16.2 입고구 ----

        [Test]
        public void Inlets_AppearPerPlot_AndNotBefore()
        {
            CollectionAssert.AreEqual(new[] { (0, 2), (0, 7), (0, 10) }, FactoryGrid.InletTiles);
            var g = new FactoryGrid();
            Assert.AreEqual(CellType.Inlet, g.Get(0, 2).Type);
            Assert.AreEqual(CellType.Locked, g.Get(0, 7).Type);
            Assert.AreEqual(CellType.Locked, g.Get(0, 10).Type);
            g.UnlockRegion(9);
            Assert.AreEqual(CellType.Inlet, g.Get(0, 7).Type);
            Assert.AreEqual(Direction.East, g.Get(0, 7).Direction);
            Assert.AreEqual(CellType.Locked, g.Get(0, 10).Type);
            g.UnlockRegion(12);
            Assert.AreEqual(CellType.Inlet, g.Get(0, 10).Type);
            Assert.AreEqual(Direction.East, g.Get(0, 10).Direction);

            var direct = new FactoryGrid();
            direct.UnlockRegion(12);
            Assert.AreEqual(CellType.Inlet, direct.Get(0, 7).Type);
            Assert.AreEqual(CellType.Inlet, direct.Get(0, 10).Type);

            var s = new GameSession(new Economy(1000));
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(CellType.Inlet, s.Grid.Get(0, 7).Type);
            Assert.AreEqual(BuildResult.Occupied, s.Build.Check(0, 7, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.NotDemolishable, s.Build.BeginDemolish(0, 7));
        }

        [Test]
        public void Spawner_TimersAreIndependent_NewInletStartsHalfAnIntervalIn()
        {
            var grid = new FactoryGrid();
            var sim = new ConveyorSim(grid, new Economy(), new PackageFactory(new Random(1), 0.0));
            var spawner = new Spawner(grid, sim);
            grid.UnlockRegion(9);
            Assert.AreEqual(0f, spawner.TimerAt(0, 2), 1e-4f);
            Assert.AreEqual(Spawner.StartInterval / 2f, spawner.TimerAt(0, 7), 1e-4f);
            Assert.AreEqual(0f, spawner.TimerAt(0, 10), 1e-4f, "no inlet there yet");

            spawner.Tick(3.9f);
            Assert.AreEqual(0, sim.Packages.Count);
            spawner.Tick(0.1f); // t = 4: (0,7)이 먼저
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual((0, 7), (sim.Packages[0].X, sim.Packages[0].Y));
            spawner.Tick(4f); // t = 8: (0,2)
            Assert.AreEqual(2, sim.Packages.Count);
            Assert.AreEqual((0, 2), (sim.Packages[1].X, sim.Packages[1].Y));
            Assert.AreEqual(0f, spawner.TimerAt(0, 2), 1e-4f);
            Assert.AreEqual(4f, spawner.TimerAt(0, 7), 1e-4f);
            Assert.AreEqual(0f, spawner.Timer, 1e-4f); // Timer = (0,2)

            // 나중에 생긴 (0,10)도 그때부터 절반 간격
            grid.UnlockRegion(12);
            Assert.AreEqual(Spawner.StartInterval / 2f, spawner.TimerAt(0, 10), 1e-4f);
            sim.DeliveredCount = 10; // 간격 7초: 모든 입고구가 같은 공식
            Assert.AreEqual(7f, spawner.Interval, 1e-4f);
        }

        [Test]
        public void Lunch_PausesEveryInlet()
        {
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Tick(1f);
            float a = s.Spawner.TimerAt(0, 2), b = s.Spawner.TimerAt(0, 7), c = s.Spawner.TimerAt(0, 10);
            s.Clock.Set(1, Clock.LunchStart);
            int n0 = s.Sim.Packages.Count;
            for (int i = 0; i < 30; i++) s.Tick(1f);
            Assert.AreEqual(a, s.Spawner.TimerAt(0, 2), 1e-4f);
            Assert.AreEqual(b, s.Spawner.TimerAt(0, 7), 1e-4f);
            Assert.AreEqual(c, s.Spawner.TimerAt(0, 10), 1e-4f);
            Assert.AreEqual(n0, s.Sim.Packages.Count);
            s.Clock.Set(1, Clock.LunchEnd);
            s.Tick(1f);
            Assert.AreEqual(a + 1f, s.Spawner.TimerAt(0, 2), 1e-3f);
            Assert.AreEqual(b + 1f, s.Spawner.TimerAt(0, 7), 1e-3f);
            Assert.AreEqual(c + 1f, s.Spawner.TimerAt(0, 10), 1e-3f);
        }

        /// <summary>입고구마다 rounds번 내보내고(매번 치워서 막히지 않게) 입고구별로 항공/일반 수를 센다.</summary>
        private static Dictionary<(int, int), (int air, int ground)> SpawnRounds(FactoryGrid grid, ConveyorSim sim, Spawner spawner, int rounds)
        {
            var count = new Dictionary<(int, int), (int air, int ground)>();
            for (int i = 0; i < rounds; i++)
            {
                spawner.Tick(spawner.Interval);
                foreach (var p in new List<Package>(sim.Packages))
                {
                    count.TryGetValue((p.X, p.Y), out var c);
                    count[(p.X, p.Y)] = p.Air ? (c.air + 1, c.ground) : (c.air, c.ground + 1);
                    sim.Take(p);
                }
            }
            return count;
        }

        [Test]
        public void AfterPlot2_EveryInletProducesAirAndGround()
        {
            var grid = new FactoryGrid();
            grid.UnlockRegion(12);
            var sim = new ConveyorSim(grid, new Economy(), new PackageFactory(new Random(5), PackageFactory.BadChance, PackageFactory.AirChanceAfterPlot2));
            var spawner = new Spawner(grid, sim);
            var count = SpawnRounds(grid, sim, spawner, 40);
            foreach (var tile in FactoryGrid.InletTiles)
            {
                Assert.IsTrue(count.ContainsKey(tile), $"inlet {tile} produced nothing");
                Assert.GreaterOrEqual(count[tile].air, 1, $"inlet {tile} produced no air parcel");
                Assert.GreaterOrEqual(count[tile].ground, 1, $"inlet {tile} produced no ground parcel");
            }
        }

        [Test]
        public void BeforePlot2_NoInletProducesAir()
        {
            var s = new GameSession(new Economy(1000)); // 세션이 만든 팩토리: 부지 확장 2 전 항공 확률 0
            s.Unlocks.Grant(UnlockId.Plot1);
            var count = SpawnRounds(s.Grid, s.Sim, s.Spawner, 60);
            Assert.AreEqual(2, count.Count, "both inlets (0,2) and (0,7) should spawn");
            foreach (var kv in count)
            {
                Assert.AreEqual(0, kv.Value.air, $"inlet {kv.Key} produced an air parcel before Plot2");
                Assert.Greater(kv.Value.ground, 0);
            }
        }

        // ---- 저장 v8 ----

        [Test]
        public void Save_V8_RoundTripsEveryInletTimer()
        {
            Assert.AreEqual(9, SaveSerializer.CurrentVersion);
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot1);
            s.Tick(1.5f);
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Tick(0.25f);
            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(9, st.Version);
            Assert.AreEqual(3, st.InletTimers.Count);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            foreach (var (x, y) in FactoryGrid.InletTiles)
                Assert.AreEqual(s.Spawner.TimerAt(x, y), r.Spawner.TimerAt(x, y), 1e-4f, $"timer at ({x},{y})");
            Assert.AreEqual(1.75f, r.Spawner.TimerAt(0, 2), 1e-4f);
            Assert.AreEqual(Spawner.StartInterval / 2f + 1.75f, r.Spawner.TimerAt(0, 7), 1e-4f);
            Assert.AreEqual(Spawner.StartInterval / 2f + 0.25f, r.Spawner.TimerAt(0, 10), 1e-4f);

            // 타이머 목록이 없으면(v7) (0,2)는 SpawnTimer, 나머지는 기본값(간격의 절반)
            st.InletTimers = null;
            st.Version = 7;
            var old = SaveSerializer.Restore(st);
            Assert.AreEqual(1.75f, old.Spawner.TimerAt(0, 2), 1e-4f);
            Assert.AreEqual(Spawner.StartInterval / 2f, old.Spawner.TimerAt(0, 7), 1e-4f);

            var bad = SaveSerializer.Capture(s);
            bad.InletTimers[1].Timer = -1f;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            bad = SaveSerializer.Capture(s);
            bad.InletTimers[1].Y = 5; // 입고구가 아닌 칸
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
        }

        [Test]
        public void Save_V7_BuildingOnNewInletTile_IsRefundedAndItsParcelLost()
        {
            var s = new GameSession(new Economy(1000));
            s.Unlocks.Grant(UnlockId.Plot1);
            var st = SaveSerializer.Capture(s);
            st.Version = 7;
            st.InletTimers = null;
            int money0 = st.Money, lost0 = st.LostCount;
            st.Cells.Add(new CellState { X = 0, Y = 7, Type = "Conveyor", Direction = "North", PalletKind = "None" });
            st.Packages.Add(new PackageState { Id = 50, Kind = "Normal", X = 0, Y = 7, Progress = 0.4f, Heading = "North", EntryHeading = "North", Lane = "Upper" });
            st.NextPackageId = 60;
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            Assert.AreEqual(CellType.Inlet, r.Grid.Get(0, 7).Type);
            Assert.AreEqual(Direction.East, r.Grid.Get(0, 7).Direction);
            Assert.AreEqual(money0 + BuildingCosts.Cost(CellType.Conveyor), r.Economy.Money);
            Assert.AreEqual(lost0 + 1, r.Sim.LostCount);
            Assert.AreEqual(0, r.Sim.Packages.Count);

            st.Version = 8; // v8에서는 그런 저장이 나올 수 없다 → 손상
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }
    }
}
