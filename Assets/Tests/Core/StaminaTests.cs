using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class StaminaTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private ConveyorSim sim;
        private BuildService build;
        private WorkerService ws;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(200);
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            build = new BuildService(grid, economy, new Toolbar(), sim);
            ws = new WorkerService(grid, sim, build);
        }

        private Package NewPackage(int id) => new PackageFactory(new Random(id)).Create(id, 0, 0, Direction.East);

        [Test]
        public void StartsFull()
        {
            Assert.AreEqual(100f, ws.Worker.Stamina);
            Assert.AreEqual(1f, ws.SpeedFactor);
        }

        [Test]
        public void Walking_DoesNotDrain()
        {
            Assert.IsTrue(ws.Move(0f, 1f, 1f));
            Assert.AreEqual(100f, ws.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void WalkingWhileCarrying_DoesNotDrain()
        {
            ws.Worker.Carrying = NewPackage(1);
            Assert.IsTrue(ws.Move(0f, 1f, 1f));
            Assert.AreEqual(100f, ws.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void BlockedMove_DoesNotDrain()
        {
            ws.Worker.X = -0.5f + WorkerService.Radius; ws.Worker.Y = -0.5f + WorkerService.Radius;
            Assert.IsFalse(ws.Move(-1f, -1f, 1f, run: true));
            Assert.IsFalse(ws.IsRunning);
            Assert.AreEqual(100f, ws.Worker.Stamina);
        }

        [Test]
        public void Standing_Regens0_8PerSecond()
        {
            ws.Worker.Stamina = 50f;
            ws.Rest(1f, DayPhase.Working);
            Assert.AreEqual(50.8f, ws.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void Walking_StillRegens0_8PerSecond()
        {
            ws.Worker.Stamina = 50f;
            Assert.IsTrue(ws.Move(0f, 1f, 0.5f));
            Assert.AreEqual(50f, ws.Worker.Stamina, 1e-4f);
            ws.Rest(0.5f, DayPhase.Working);
            Assert.AreEqual(50.4f, ws.Worker.Stamina, 1e-4f);
            ws.Worker.Carrying = NewPackage(1);
            Assert.IsTrue(ws.Move(0f, 1f, 0.5f));
            ws.Rest(0.5f, DayPhase.Working);
            Assert.AreEqual(50.8f, ws.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void Lunch_Regens20PerSecond_EvenWhileWalking()
        {
            ws.Worker.Stamina = 50f;
            Assert.IsTrue(ws.Move(0f, 1f, 0.5f));
            ws.Rest(0.5f, DayPhase.Lunch);
            Assert.AreEqual(60f, ws.Worker.Stamina, 1e-4f);
        }

        [Test]
        public void Stamina_IsClampedTo0_100()
        {
            ws.Worker.Stamina = 99.9f;
            ws.Rest(10f, DayPhase.Lunch);
            Assert.AreEqual(100f, ws.Worker.Stamina);
            ws.Worker.Stamina = 10.5f; // RunMinStamina 이상이라 달린다: 5.5/초 × 2초 > 10.5
            ws.Worker.Carrying = NewPackage(1);
            ws.Worker.X = 2f; ws.Worker.Y = 0f; // 북쪽으로 약 10.9칸 비어 있다
            Assert.IsTrue(ws.Move(0f, 1f, 2f, run: true));
            Assert.AreEqual(0f, ws.Worker.Stamina);
        }

        [Test]
        public void SpeedFactor_LerpsBelow30()
        {
            ws.Worker.Stamina = 30f;
            Assert.AreEqual(1f, ws.SpeedFactor, 1e-5f);
            ws.Worker.Stamina = 15f;
            Assert.AreEqual(0.75f, ws.SpeedFactor, 1e-5f);
            ws.Worker.Stamina = 0f;
            Assert.AreEqual(0.5f, ws.SpeedFactor, 1e-5f);
        }

        [Test]
        public void LowStamina_SlowsWalking()
        {
            ws.Worker.Stamina = 15f;
            float y0 = ws.Worker.Y;
            Assert.IsTrue(ws.Move(0f, 1f, 0.1f));
            Assert.AreEqual(WorkerService.Speed * 0.75f * 0.1f, ws.Worker.Y - y0, 1e-4f);
        }

        [Test]
        public void Exhausted_CannotPickUpFromPallet()
        {
            Assert.IsTrue(sim.PutOnPallet(NewPackage(1), 5, 2));
            ws.Worker.X = 4f; ws.Worker.Y = 2f; ws.Worker.Facing = Direction.East;
            ws.Worker.Stamina = 0f;
            Assert.AreEqual(InteractResult.Exhausted, ws.Preview(null));
            Assert.AreEqual(InteractResult.Exhausted, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreEqual(1, sim.PalletCount(5, 2));
        }

        [Test]
        public void Exhausted_CannotPickUpFromLine()
        {
            Assert.IsTrue(sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _));
            ws.Worker.X = 1f; ws.Worker.Y = 2f; ws.Worker.Facing = Direction.West;
            ws.Worker.Stamina = 0f;
            Assert.AreEqual(InteractResult.Exhausted, ws.Interact(null));
            Assert.AreEqual(1, sim.Packages.Count);
            ws.Worker.Stamina = 0.1f;
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
        }

        [Test]
        public void Exhausted_CanStillPutDownAndDeliver()
        {
            ws.Worker.Carrying = NewPackage(1);
            ws.Worker.Stamina = 0f;
            ws.Worker.X = 4f; ws.Worker.Y = 2f; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.AreEqual(1, sim.PalletCount(5, 2));
            ws.Worker.Carrying = NewPackage(2);
            ws.Worker.X = 10f; ws.Worker.Y = 2f;
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
        }

        [Test]
        public void Exhausted_CanStillBuild()
        {
            ws.Worker.Stamina = 0f;
            ws.Worker.X = 2f; ws.Worker.Y = 4f; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.Placed, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void Session_TickRegensWorker_AndNextDayRefills()
        {
            var s = new GameSession();
            s.Worker.Worker.Stamina = 50f;
            s.Tick(1f);
            Assert.AreEqual(50.8f, s.Worker.Worker.Stamina, 1e-3f);
            s.Tick(179f); // 12:00
            s.Worker.Worker.Stamina = 10f;
            s.Tick(1f);
            Assert.AreEqual(30f, s.Worker.Worker.Stamina, 1e-3f);
            s.Tick(1000f);
            Assert.AreEqual(DayPhase.Closed, s.Clock.Phase);
            s.Worker.Worker.Stamina = 5f;
            s.NextDay();
            Assert.AreEqual(100f, s.Worker.Worker.Stamina);
        }

        [Test]
        public void InspectedCount_CountsTableAndScanner()
        {
            var s = new GameSession(new Economy(500), new PackageFactory(new Random(1), 0.0));
            s.Build.TryPlace(1, 2, CellType.Table, Direction.East);
            s.Tick(Spawner.StartInterval);
            s.Tick(1f);
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Inspect(false));
            Assert.AreEqual(1, s.Sim.InspectedCount);

            var s2 = new GameSession(new Economy(500), new PackageFactory(new Random(1), 0.0));
            s2.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s2.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            s2.Tick(Spawner.StartInterval); // 생성 후 같은 틱에 스캐너로 진입
            Assert.AreEqual(1, s2.Sim.InspectedCount);
        }
    }
}
