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
        }

        [Test]
        public void StraightLine_DeliversAndEarns()
        {
            var s = new GameSession();
            for (int x = 1; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(60, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f); // 9초: 3초에 생성, 5초 이동. 두 번째 택배(6초 생성)는 아직 도착 전
            Assert.AreEqual(75, s.Economy.Money);
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
