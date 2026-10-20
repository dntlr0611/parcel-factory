using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class ClockTests
    {
        [Test]
        public void NewClock_StartsDay1At0900Working()
        {
            var c = new Clock();
            Assert.AreEqual(1, c.Day);
            Assert.AreEqual(540f, c.MinuteOfDay);
            Assert.AreEqual(DayPhase.Working, c.Phase);
            Assert.IsTrue(c.IsOpen);
            Assert.AreEqual("09:00", c.TimeText);
        }

        [Test]
        public void Advance_OneSecondIsOneGameMinute()
        {
            var c = new Clock();
            c.Advance(75f);
            Assert.AreEqual(615f, c.MinuteOfDay, 1e-3f);
            Assert.AreEqual("10:15", c.TimeText);
        }

        [Test]
        public void Advance_EntersLunchAtNoon()
        {
            var c = new Clock();
            c.Advance(179f);
            Assert.AreEqual("11:59", c.TimeText);
            Assert.AreEqual(DayPhase.Working, c.Phase);
            c.Advance(1f);
            Assert.AreEqual("12:00", c.TimeText);
            Assert.AreEqual(DayPhase.Lunch, c.Phase);
            Assert.IsTrue(c.IsOpen);
        }

        [Test]
        public void Advance_LeavesLunchAt1300()
        {
            var c = new Clock();
            c.Advance(239f);
            Assert.AreEqual(DayPhase.Lunch, c.Phase);
            c.Advance(1f);
            Assert.AreEqual("13:00", c.TimeText);
            Assert.AreEqual(DayPhase.Working, c.Phase);
        }

        [Test]
        public void Advance_StopsExactlyAt1800_Closed()
        {
            var c = new Clock();
            c.Advance(539f);
            Assert.AreEqual(DayPhase.Working, c.Phase);
            c.Advance(10000f);
            Assert.AreEqual(1080f, c.MinuteOfDay);
            Assert.AreEqual(DayPhase.Closed, c.Phase);
            Assert.IsFalse(c.IsOpen);
            Assert.AreEqual("18:00", c.TimeText);
            c.Advance(5f);
            Assert.AreEqual(1080f, c.MinuteOfDay);
        }

        [Test]
        public void Advance_IgnoresNonPositiveAndNonFinite()
        {
            var c = new Clock();
            c.Advance(0f);
            c.Advance(-3f);
            c.Advance(float.NaN);
            c.Advance(float.PositiveInfinity);
            Assert.AreEqual(540f, c.MinuteOfDay);
        }

        [Test]
        public void NextDay_ResetsTo0900_AndIncrementsDay()
        {
            var c = new Clock();
            c.Advance(600f);
            c.NextDay();
            Assert.AreEqual(2, c.Day);
            Assert.AreEqual(540f, c.MinuteOfDay);
            Assert.AreEqual(DayPhase.Working, c.Phase);
            Assert.AreEqual("09:00", c.TimeText);
        }

        [Test]
        public void Set_RestoresAndValidates()
        {
            var c = new Clock();
            c.Set(3, 750f);
            Assert.AreEqual(3, c.Day);
            Assert.AreEqual(DayPhase.Lunch, c.Phase);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Set(0, 600f));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Set(1, 539f));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Set(1, 1081f));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Set(1, float.NaN));
        }

        // ---- GameSession과 시계 ----

        [Test]
        public void Session_ClockAdvancesWithTick_AndStopsWhenPaused()
        {
            var s = new GameSession();
            Assert.AreEqual(1, s.Clock.Day);
            s.Tick(60f);
            Assert.AreEqual("10:00", s.Clock.TimeText);
            s.Paused = true;
            s.Tick(60f);
            Assert.AreEqual("10:00", s.Clock.TimeText);
        }

        [Test]
        public void Session_Speed2_ClockRunsTwiceAsFast()
        {
            var s = new GameSession { Speed = 2f };
            s.Tick(30f);
            Assert.AreEqual(600f, s.Clock.MinuteOfDay, 1e-3f);
        }

        [Test]
        public void Lunch_StopsSpawning_AndFreezesTimer()
        {
            var s = new GameSession();
            s.Tick(180f); // 12:00
            Assert.AreEqual(DayPhase.Lunch, s.Clock.Phase);
            float timer = s.Spawner.Timer;
            int nextId = s.Sim.NextId;
            s.Tick(30f);
            Assert.AreEqual(timer, s.Spawner.Timer);
            Assert.AreEqual(nextId, s.Sim.NextId);
            s.Tick(30f); // 13:00
            Assert.AreEqual(DayPhase.Working, s.Clock.Phase);
            s.Tick(1f);
            Assert.AreEqual(timer + 1f, s.Spawner.Timer, 1e-3f);
        }

        [Test]
        public void TickCrossingNoon_SpawnerOnlyRunsTheWorkingPart()
        {
            var s = new GameSession();
            s.Tick(179f); // 11:59, 타이머 179 mod 8 = 3
            Assert.AreEqual(3f, s.Spawner.Timer, 1e-3f);
            s.Tick(2f);   // 1초는 근무, 1초는 점심
            Assert.AreEqual(4f, s.Spawner.Timer, 1e-3f);
        }

        [Test]
        public void Lunch_SimulationKeepsRunning()
        {
            var s = new GameSession();
            s.Tick(180f);
            s.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 3);
            s.Tick(2f);
            Assert.AreEqual(CellType.Empty, s.Grid.Get(1, 3).Type);
        }

        [Test]
        public void Closed_TickDoesNothing()
        {
            var s = new GameSession();
            s.Tick(540f);
            Assert.AreEqual(DayPhase.Closed, s.Clock.Phase);
            s.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 3);
            float timer = s.Spawner.Timer;
            s.Tick(5f);
            Assert.AreEqual(2f, s.Grid.Get(1, 3).DemolishRemaining);
            Assert.AreEqual(timer, s.Spawner.Timer);
            Assert.AreEqual(1080f, s.Clock.MinuteOfDay);
        }

        [Test]
        public void TickCrossingClose_OnlySimulatesUntil1800()
        {
            var s = new GameSession();
            s.Tick(539f); // 17:59
            s.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 3);
            s.Tick(10f);
            Assert.AreEqual(DayPhase.Closed, s.Clock.Phase);
            Assert.AreEqual(1f, s.Grid.Get(1, 3).DemolishRemaining, 1e-3f);
        }

        [Test]
        public void DayEnded_FiresOnce_WithDeltasSinceDayStart()
        {
            var s = new GameSession(new Economy(100));
            var summaries = new List<DaySummary>();
            s.DayEnded += summaries.Add;
            s.Economy.Earn(50);
            s.Sim.DeliveredCount += 3;
            s.Sim.NoteInspected();
            s.Sim.NoteInspected();
            for (int i = 0; i < 6; i++) s.Tick(100f);
            Assert.AreEqual(1, summaries.Count);
            var d1 = summaries[0];
            Assert.AreEqual(1, d1.Day);
            Assert.AreEqual(3, d1.Delivered);
            Assert.AreEqual(s.Sim.LostCount, d1.Lost);
            Assert.AreEqual(2, d1.Inspected);
            Assert.AreEqual(100, d1.StartMoney);
            Assert.AreEqual(150, d1.EndMoney);

            s.NextDay();
            Assert.AreEqual(2, s.Clock.Day);
            Assert.AreEqual(540f, s.Clock.MinuteOfDay);
            Assert.AreEqual(150, s.DayStartMoney);
            int lostAtStart = s.Sim.LostCount;
            s.Sim.DeliveredCount += 1;
            s.Economy.Adjust(-20);
            s.Tick(540f);
            Assert.AreEqual(2, summaries.Count);
            var d2 = summaries[1];
            Assert.AreEqual(2, d2.Day);
            Assert.AreEqual(1, d2.Delivered);
            Assert.AreEqual(s.Sim.LostCount - lostAtStart, d2.Lost);
            Assert.AreEqual(0, d2.Inspected);
            Assert.AreEqual(150, d2.StartMoney);
            Assert.AreEqual(130, d2.EndMoney);
        }

        [Test]
        public void NextDay_IsIgnoredWhileOpen()
        {
            var s = new GameSession();
            s.Tick(60f);
            s.NextDay();
            Assert.AreEqual(1, s.Clock.Day);
            Assert.AreEqual(600f, s.Clock.MinuteOfDay, 1e-3f);
        }

        [Test]
        public void BuildSummary_ReflectsCurrentDay()
        {
            var s = new GameSession(new Economy(80));
            s.Sim.DeliveredCount = 4;
            var sum = s.BuildSummary();
            Assert.AreEqual(1, sum.Day);
            Assert.AreEqual(4, sum.Delivered);
            Assert.AreEqual(80, sum.StartMoney);
            Assert.AreEqual(80, sum.EndMoney);
        }
    }
}
