using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class GameSessionTests
    {
        [Test]
        public void NewSession_StartsPerSpec()
        {
            var s = new GameSession();
            Assert.AreEqual(100, s.Economy.Money);
            Assert.AreEqual(6, s.Grid.UnlockedSize);
            Assert.IsTrue(s.Toolbar.Owns(CellType.Conveyor, Direction.East));
            Assert.IsFalse(s.Paused);
            Assert.AreEqual(1f, s.Speed);
            Assert.IsNotNull(s.Worker);
            Assert.AreEqual((2, 4), (s.Worker.Worker.X, s.Worker.Worker.Y));
            Assert.IsTrue(s.Toolbar.TableOwned);
        }

        [Test]
        public void StraightLine_UninspectedDeliveryIsPenalized()
        {
            var s = new GameSession(new Economy(), new PackageFactory(new Random(1), 0.0));
            for (int x = 1; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(60, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f); // 9초: 3초에 생성, 5초 이동, 검사 없이 출고 → −10
            Assert.AreEqual(50, s.Economy.Money);
            Assert.AreEqual(0, s.Sim.DeliveredCount);
        }

        [Test]
        public void StraightLine_WithScanner_Earns15()
        {
            var s = new GameSession(new Economy(300), new PackageFactory(new Random(1), 0.0));
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            for (int x = 2; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(150, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f);
            Assert.AreEqual(165, s.Economy.Money);
            Assert.AreEqual(1, s.Sim.DeliveredCount);
        }

        [Test]
        public void Paused_FreezesEverything()
        {
            var s = new GameSession();
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 2);
            s.Paused = true;
            s.Tick(5f);
            Assert.AreEqual(0, s.Sim.Packages.Count);
            Assert.AreEqual(2f, s.Grid.Get(1, 2).DemolishRemaining);
            Assert.AreEqual(0f, s.Spawner.Timer);
        }

        [Test]
        public void Speed2_RunsTwiceAsFast()
        {
            var s = new GameSession { Speed = 2f };
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Tick(1.25f);
            Assert.AreEqual(0, s.Sim.Packages.Count); // 2.5초 상당, 아직 생성 전
            s.Tick(0.75f); // 누적 4초 상당: 3초에 생성, 이후 1초 이동
            Assert.AreEqual(1, s.Sim.Packages.Count);
            Assert.AreEqual((1, 2), (s.Sim.Packages[0].X, s.Sim.Packages[0].Y));
        }

        [Test]
        public void DemolishTimerAdvancesWithTick()
        {
            var s = new GameSession();
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 2);
            s.Tick(2f);
            Assert.AreEqual(CellType.Empty, s.Grid.Get(1, 2).Type);
            Assert.AreEqual(98, s.Economy.Money);
        }
    }
}
