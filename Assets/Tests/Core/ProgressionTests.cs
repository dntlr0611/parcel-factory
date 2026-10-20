using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class ProgressionTests
    {
        private static Package Pkg(int id, bool bad, bool? reject)
        {
            var p = new PackageFactory(new Random(id), 0.0).Create(id, 0, 0, Direction.East);
            p.Bad = bad;
            if (reject.HasValue) p.Inspect(reject.Value);
            return p;
        }

        [Test]
        public void StartsAtZero_WithBaseMultipliers()
        {
            var p = new Progression();
            Assert.AreEqual(0, p.Exp);
            foreach (Stat s in Enum.GetValues(typeof(Stat))) Assert.AreEqual(0, p.Level(s));
            Assert.AreEqual(100f, p.MaxStamina);
            Assert.AreEqual(1f, p.SpeedMultiplier);
            Assert.AreEqual(1f, p.RegenMultiplier);
        }

        [Test]
        public void MaxLevels_Are5_5_3()
        {
            var p = new Progression();
            Assert.AreEqual(5, p.MaxLevel(Stat.Stamina));
            Assert.AreEqual(5, p.MaxLevel(Stat.Speed));
            Assert.AreEqual(3, p.MaxLevel(Stat.Regen));
        }

        [Test]
        public void Cost_IsBaseTimesLevelBeingBought()
        {
            var p = new Progression();
            Assert.AreEqual(40, p.Cost(Stat.Stamina));
            Assert.AreEqual(50, p.Cost(Stat.Speed));
            Assert.AreEqual(40, p.Cost(Stat.Regen));
            p.AddExp(1000);
            Assert.AreEqual(UpgradeResult.Ok, p.TryUpgrade(Stat.Speed));
            Assert.AreEqual(100, p.Cost(Stat.Speed));
            Assert.AreEqual(UpgradeResult.Ok, p.TryUpgrade(Stat.Speed));
            Assert.AreEqual(150, p.Cost(Stat.Speed));
            Assert.AreEqual(40, p.Cost(Stat.Stamina));
        }

        [Test]
        public void TryUpgrade_SpendsExp_AndRaisesLevel()
        {
            var p = new Progression();
            p.AddExp(100);
            Assert.AreEqual(UpgradeResult.Ok, p.TryUpgrade(Stat.Stamina));
            Assert.AreEqual(60, p.Exp);
            Assert.AreEqual(1, p.Level(Stat.Stamina));
            Assert.AreEqual(UpgradeResult.NotEnoughExp, p.TryUpgrade(Stat.Stamina)); // 80 필요
            Assert.AreEqual(60, p.Exp);
            Assert.AreEqual(1, p.Level(Stat.Stamina));
        }

        [Test]
        public void NotEnoughExp_ChangesNothing()
        {
            var p = new Progression();
            p.AddExp(49);
            int upgrades = 0;
            p.Upgraded += (s, l) => upgrades++;
            Assert.AreEqual(UpgradeResult.NotEnoughExp, p.TryUpgrade(Stat.Speed));
            Assert.AreEqual(49, p.Exp);
            Assert.AreEqual(0, p.Level(Stat.Speed));
            Assert.AreEqual(0, upgrades);
        }

        [Test]
        public void MaxLevel_StopsUpgrades()
        {
            var p = new Progression();
            p.AddExp(10000);
            for (int i = 0; i < 3; i++) Assert.AreEqual(UpgradeResult.Ok, p.TryUpgrade(Stat.Regen));
            int exp = p.Exp;
            Assert.AreEqual(UpgradeResult.MaxLevel, p.TryUpgrade(Stat.Regen));
            Assert.AreEqual(3, p.Level(Stat.Regen));
            Assert.AreEqual(exp, p.Exp);
            for (int i = 0; i < 5; i++) Assert.AreEqual(UpgradeResult.Ok, p.TryUpgrade(Stat.Stamina));
            Assert.AreEqual(UpgradeResult.MaxLevel, p.TryUpgrade(Stat.Stamina));
            Assert.AreEqual(40 * (1 + 2 + 3) + 40 * (1 + 2 + 3 + 4 + 5), 10000 - p.Exp);
        }

        [Test]
        public void MaxLevel_TakesPrecedenceOverNotEnoughExp()
        {
            var p = new Progression();
            p.Restore(0, 0, 0, 3);
            Assert.AreEqual(UpgradeResult.MaxLevel, p.TryUpgrade(Stat.Regen));
        }

        [Test]
        public void Multipliers_AreLinearInLevel()
        {
            var p = new Progression();
            p.Restore(0, 3, 4, 2);
            Assert.AreEqual(160f, p.MaxStamina, 1e-5f);
            Assert.AreEqual(1.32f, p.SpeedMultiplier, 1e-5f);
            Assert.AreEqual(2f, p.RegenMultiplier, 1e-5f);
            p.Restore(0, 5, 5, 3);
            Assert.AreEqual(200f, p.MaxStamina, 1e-5f);
            Assert.AreEqual(1.4f, p.SpeedMultiplier, 1e-5f);
            Assert.AreEqual(2.5f, p.RegenMultiplier, 1e-5f);
        }

        [Test]
        public void AddExp_IgnoresNonPositive_AndRaisesExpChanged()
        {
            var p = new Progression();
            var seen = new List<int>();
            p.ExpChanged += e => seen.Add(e);
            p.AddExp(10);
            p.AddExp(-5);
            p.AddExp(0);
            p.AddExp(2);
            Assert.AreEqual(12, p.Exp);
            CollectionAssert.AreEqual(new[] { 10, 12 }, seen);
        }

        [Test]
        public void TryUpgrade_RaisesUpgradedAndExpChanged()
        {
            var p = new Progression();
            p.AddExp(50);
            Stat? stat = null; int level = -1, exp = -1;
            p.Upgraded += (s, l) => { stat = s; level = l; };
            p.ExpChanged += e => exp = e;
            Assert.AreEqual(UpgradeResult.Ok, p.TryUpgrade(Stat.Regen));
            Assert.AreEqual(Stat.Regen, stat);
            Assert.AreEqual(1, level);
            Assert.AreEqual(10, exp);
        }

        [Test]
        public void Restore_RejectsOutOfRange()
        {
            var p = new Progression();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Restore(-1, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Restore(0, 6, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Restore(0, 0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Restore(0, 0, 0, 4));
        }

        // ---- WorkerService와 연결 ----

        [Test]
        public void SpeedUpgrade_MovesFarther_WalkingAndRunning()
        {
            var s = new GameSession();
            var w = s.Worker.Worker;
            s.Progression.Restore(0, 0, 2, 0);
            Assert.AreEqual(WorkerService.Speed * 1.16f, s.Worker.EffectiveSpeed, 1e-4f);
            w.X = 2f; w.Y = 4f;
            Assert.IsTrue(s.Worker.Move(0f, -1f, 0.1f));
            Assert.AreEqual(4f - WorkerService.Speed * 1.16f * 0.1f, w.Y, 1e-4f);
            w.X = 2f; w.Y = 4f;
            Assert.IsTrue(s.Worker.Move(0f, -1f, 0.1f, run: true));
            Assert.AreEqual(4f - WorkerService.Speed * 1.16f * WorkerService.RunSpeedMultiplier * 0.1f, w.Y, 1e-4f);
        }

        [Test]
        public void RegenUpgrade_AppliesWhileWalking_NotAtLunch()
        {
            var s = new GameSession();
            s.Progression.Restore(0, 0, 0, 1);
            var w = s.Worker.Worker;
            w.Stamina = 50f;
            Assert.IsTrue(s.Worker.Move(0f, 1f, 0.5f));
            s.Worker.Rest(1f, DayPhase.Working);
            Assert.AreEqual(51.2f, w.Stamina, 1e-4f);
            s.Worker.Rest(1f, DayPhase.Lunch);
            Assert.AreEqual(71.2f, w.Stamina, 1e-4f);
        }

        [Test]
        public void StaminaUpgrade_RaisesCurrentStamina_AndCap()
        {
            var s = new GameSession();
            var w = s.Worker.Worker;
            s.Progression.AddExp(40);
            Assert.AreEqual(100f, w.Stamina);
            Assert.AreEqual(UpgradeResult.Ok, s.Progression.TryUpgrade(Stat.Stamina));
            Assert.AreEqual(120f, s.Worker.EffectiveMaxStamina);
            Assert.AreEqual(120f, w.Stamina, 1e-4f);
            w.Stamina = 110f;
            s.Worker.Rest(10f, DayPhase.Lunch);
            Assert.AreEqual(120f, w.Stamina, 1e-4f);
        }

        [Test]
        public void StaminaUpgrade_FromLowStamina_AddsTwenty()
        {
            var s = new GameSession();
            s.Worker.Worker.Stamina = 30f;
            s.Progression.AddExp(40);
            s.Progression.TryUpgrade(Stat.Stamina);
            Assert.AreEqual(50f, s.Worker.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void OtherUpgrades_DoNotChangeStamina()
        {
            var s = new GameSession();
            s.Worker.Worker.Stamina = 30f;
            s.Progression.AddExp(100);
            s.Progression.TryUpgrade(Stat.Speed);
            s.Progression.TryUpgrade(Stat.Regen);
            Assert.AreEqual(30f, s.Worker.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void NextDay_RefillsToEffectiveMax()
        {
            var s = new GameSession();
            s.Progression.Restore(0, 2, 0, 0);
            s.Tick(540f);
            s.Worker.Worker.Stamina = 5f;
            s.NextDay();
            Assert.AreEqual(140f, s.Worker.Worker.Stamina, 1e-4f);
        }

        // ---- EXP 적립 ----

        [Test]
        public void Exp_SuccessfulDeliveries_Give10And8()
        {
            var s = new GameSession();
            s.Sim.DeliverCarried(Pkg(1, false, false), CellType.Outlet);
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp);
            Assert.AreEqual(10, s.Progression.Exp);
            s.Sim.DeliverCarried(Pkg(2, true, true), CellType.ReturnDock);
            Assert.AreEqual(18, s.Progression.Exp);
        }

        [Test]
        public void Exp_FailedDeliveries_GiveNothing()
        {
            var s = new GameSession();
            s.Sim.DeliverCarried(Pkg(1, false, null), CellType.Outlet);
            s.Sim.DeliverCarried(Pkg(2, true, false), CellType.Outlet);
            s.Sim.DeliverCarried(Pkg(3, false, true), CellType.ReturnDock);
            s.Sim.DeliverCarried(Pkg(4, false, null), CellType.ReturnDock);
            Assert.AreEqual(0, s.Progression.Exp);
        }

        [Test]
        public void Exp_Inspection_Gives2_TableAndScanner()
        {
            var s = new GameSession(new Economy(500), new PackageFactory(new Random(1), 0.0));
            int events = 0;
            s.Sim.Inspected += () => events++;
            s.Build.TryPlace(1, 2, CellType.Table, Direction.East);
            s.Tick(Spawner.StartInterval);
            s.Tick(1f);
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Inspect(false));
            Assert.AreEqual(2, s.Progression.Exp);
            Assert.AreEqual(1, events);

            var s2 = new GameSession(new Economy(500), new PackageFactory(new Random(1), 0.0));
            s2.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s2.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            s2.Tick(Spawner.StartInterval);
            Assert.AreEqual(1, s2.Sim.InspectedCount);
            Assert.AreEqual(2, s2.Progression.Exp);
        }

        [Test]
        public void Summary_ReportsExpEarnedToday()
        {
            var s = new GameSession();
            s.Progression.AddExp(30);
            s.Tick(540f);
            s.NextDay();
            Assert.AreEqual(30, s.DayStartExp);
            s.Sim.DeliverCarried(Pkg(1, false, false), CellType.Outlet);
            s.Sim.NoteInspected();
            s.Progression.TryUpgrade(Stat.Stamina); // 지출은 획득량을 깎지 않는다
            DaySummary summary = null;
            s.DayEnded += d => summary = d;
            s.Tick(540f);
            Assert.IsNotNull(summary);
            Assert.AreEqual(12, summary.Exp);
            Assert.AreEqual(12, s.BuildSummary().Exp);
        }

        // ---- 저장 v6 ----

        [Test]
        public void Save_V6_RoundTripsExpLevelsAndDayStartExp()
        {
            Assert.AreEqual(6, SaveSerializer.CurrentVersion);
            var s = new GameSession();
            s.Progression.AddExp(500);
            s.Progression.TryUpgrade(Stat.Stamina);
            s.Progression.TryUpgrade(Stat.Stamina);
            s.Progression.TryUpgrade(Stat.Speed);
            s.Progression.TryUpgrade(Stat.Regen);
            s.Worker.Worker.Stamina = 135f;
            s.Tick(540f);
            s.NextDay();
            s.Progression.AddExp(14);

            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(6, st.Version);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            Assert.AreEqual(s.Progression.Exp, r.Progression.Exp);
            Assert.AreEqual(2, r.Progression.Level(Stat.Stamina));
            Assert.AreEqual(1, r.Progression.Level(Stat.Speed));
            Assert.AreEqual(1, r.Progression.Level(Stat.Regen));
            Assert.AreEqual(s.DayStartExp, r.DayStartExp);
            Assert.AreEqual(140f, r.Worker.Worker.Stamina, 1e-4f);
            Assert.AreEqual(14, r.BuildSummary().Exp);
        }

        [Test]
        public void Save_V5_IsRejected()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Version = 5;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        [Test]
        public void Save_RejectsInvalidProgression()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Exp = -1;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st = SaveSerializer.Capture(new GameSession());
            st.SpeedLevel = 6;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st = SaveSerializer.Capture(new GameSession());
            st.RegenLevel = 4;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st = SaveSerializer.Capture(new GameSession());
            st.StaminaLevel = -1;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        [Test]
        public void Save_StaminaAboveBaseMax_AllowedOnlyWithStaminaLevel()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Stamina = 120f;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st.StaminaLevel = 1;
            Assert.AreEqual(120f, SaveSerializer.Restore(st).Worker.Worker.Stamina, 1e-4f);
        }
    }
}
