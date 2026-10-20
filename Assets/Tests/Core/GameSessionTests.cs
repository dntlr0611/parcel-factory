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
            Assert.AreEqual(WorkerService.StartX, s.Worker.Worker.X);
            Assert.AreEqual(WorkerService.StartY, s.Worker.Worker.Y);
            Assert.AreEqual((2, 4), s.Worker.Tile());
            Assert.IsTrue(s.Toolbar.TableOwned);
        }

        [Test]
        public void StraightLine_UninspectedDeliveryIsPenalized()
        {
            var s = new GameSession(new Economy(200), new PackageFactory(new Random(1), 0.0));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Grid.Clear(5, 2); // 시작 팔레트를 치우고 트럭 도크 (11,2)까지 레일
            for (int x = 1; x <= 10; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(100, s.Economy.Money);
            for (int i = 0; i < 290; i++) s.Tick(0.05f); // 14.5초: 3초에 생성, 11초 이동, 검사 없이 출고 → −10
            Assert.AreEqual(90, s.Economy.Money);
            Assert.AreEqual(0, s.Sim.DeliveredCount);
        }

        [Test]
        public void StraightLine_WithScanner_Earns15()
        {
            var s = new GameSession(new Economy(400), new PackageFactory(new Random(1), 0.0));
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Grid.Clear(5, 2);
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            for (int x = 2; x <= 10; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(190, s.Economy.Money);
            for (int i = 0; i < 290; i++) s.Tick(0.05f);
            Assert.AreEqual(205, s.Economy.Money);
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
    

        [Test]
        public void HandCarry_EndToEnd_WithoutRails()
        {
            var s = new GameSession(new Economy(100), new PackageFactory(new Random(1), 0.0));
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Table, Direction.East));
            Assert.AreEqual(60, s.Economy.Money);
            s.Tick(3f); // 생성 후 같은 틱에 검사대로 이동
            s.Tick(1f); // 검사 대기
            Assert.AreEqual(1, s.Sim.AwaitingInspectionCount);
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Inspect(false));
            Assert.AreEqual(InteractResult.PickedUp, s.Worker.Interact(null));
            s.Worker.Worker.X = 4; s.Worker.Worker.Y = 2; s.Worker.Worker.Facing = Direction.East; // 파랑 팔레트 (5,2)
            Assert.AreEqual(InteractResult.PutDown, s.Worker.Interact(null));
            Assert.AreEqual(1, s.Sim.PalletCount(5, 2));
            Assert.AreEqual(InteractResult.PickedUp, s.Worker.Interact(null));
            s.Worker.Worker.X = 10; s.Worker.Worker.Y = 2; s.Worker.Worker.Facing = Direction.East; // 트럭 도크 (11,2)
            Assert.AreEqual(InteractResult.Delivered, s.Worker.Interact(null));
            Assert.AreEqual(75, s.Economy.Money);
            Assert.AreEqual(1, s.Sim.DeliveredCount);
        }

        [Test]
        public void Worker_WalksAcrossRails_InSession()
        {
            var s = new GameSession(new Economy(100), new PackageFactory(new Random(1), 0.0));
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 4, CellType.Conveyor, Direction.East));
            for (int i = 0; i < 10; i++) s.Worker.Move(-1f, 0f, 0.1f);
            Assert.AreEqual((0, 4), s.Worker.Tile());
        }
    }
}
