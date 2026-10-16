using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class WorkerTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private ConveyorSim sim;
        private Toolbar toolbar;
        private BuildService build;
        private WorkerService ws;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(200);
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            toolbar = new Toolbar();
            build = new BuildService(grid, economy, toolbar, sim);
            ws = new WorkerService(grid, sim, build);
        }

        [Test]
        public void StartsAt2_4FacingSouth()
        {
            Assert.AreEqual((2, 4), (ws.Worker.X, ws.Worker.Y));
            Assert.AreEqual(Direction.South, ws.Worker.Facing);
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreEqual((2, 3), ws.FacingTile());
        }

        [Test]
        public void Move_WalksOnlyOnEmptyFloor()
        {
            Assert.IsTrue(ws.TryMove(Direction.North));
            Assert.AreEqual((2, 5), (ws.Worker.X, ws.Worker.Y));
            Assert.IsFalse(ws.TryMove(Direction.North)); // (2,6)은 잠긴 칸
            Assert.AreEqual((2, 5), (ws.Worker.X, ws.Worker.Y));
            Assert.AreEqual(Direction.North, ws.Worker.Facing);
            grid.Place(1, 5, CellType.Conveyor, Direction.East);
            Assert.IsFalse(ws.TryMove(Direction.West)); // 레일 위로는 못 감
            Assert.AreEqual(Direction.West, ws.Worker.Facing); // 몸은 돌아감
            Assert.IsFalse(ws.CanWalk(0, 2)); // 입고구
            Assert.IsFalse(ws.CanWalk(-1, 0));
        }

        [Test]
        public void Interact_PicksUpFromInlet_AndPutsDownOnBelt()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South; // 입고구 (0,2)를 바라봄
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(p, ws.Worker.Carrying);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(-1, grid.Get(0, 2).OccupantId);

            grid.Place(1, 3, CellType.Conveyor, Direction.East);
            ws.Worker.Facing = Direction.East; // (1,3)
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreSame(p, sim.PackageAt(1, 3));
            Assert.AreEqual(Direction.East, p.Heading);
        }

        [Test]
        public void Interact_CarryingToDock_DeliversWithScoring()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(false);
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            ws.Worker.X = 4; ws.Worker.Y = 2; ws.Worker.Facing = Direction.East; // 출고구 (5,2)
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreEqual(215, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
        }

        [Test]
        public void Interact_CarryingOntoFloorOrOccupied_Fails()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            ws.Interact(null);
            ws.Worker.Facing = Direction.East; // (1,3) 빈 바닥
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(null));
            Assert.IsNotNull(ws.Worker.Carrying);
            grid.Place(1, 3, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(1, 3, PackageKind.Normal, out _));
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(null)); // 점유 중
            ws.Worker.Facing = Direction.North; // (0,4) 빈 바닥 — 운반 중엔 설치도 불가
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void Interact_OnTable_RequiresInspection_ThenPicksUp()
        {
            grid.Place(1, 2, CellType.Table, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            ws.Worker.X = 1; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Interact(null));
            Assert.AreSame(p, ws.PendingInspection);
            Assert.IsNull(ws.Worker.Carrying);
            Assert.IsTrue(ws.Inspect(true));
            Assert.IsNull(ws.PendingInspection);
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected);
            Assert.IsFalse(ws.Inspect(false)); // 대기 중인 검사가 없음
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(p, ws.Worker.Carrying);
        }

        [Test]
        public void Inspect_FailsIfPackageLeftTable()
        {
            grid.Place(1, 2, CellType.Table, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            ws.Worker.X = 1; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Interact(null));
            sim.Take(p); // 그 사이 택배가 사라짐
            Assert.IsFalse(ws.Inspect(false));
            Assert.IsNull(ws.PendingInspection);
            Assert.IsFalse(p.Inspected);
        }

        [Test]
        public void Interact_PlacesToolOnFacingTile()
        {
            Assert.AreEqual(InteractResult.NoTool, ws.Interact(null));
            Assert.AreEqual(InteractResult.NotOwned, ws.Interact(new Tool(CellType.Conveyor, Direction.North)));
            Assert.AreEqual(InteractResult.Placed, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            Assert.AreEqual(CellType.Conveyor, grid.Get(2, 3).Type);
            Assert.AreEqual(190, economy.Money);
            Assert.AreEqual(InteractResult.Occupied, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            grid.Clear(2, 3);
            toolbar.Add(CellType.Conveyor, Direction.North);
            Assert.AreEqual(InteractResult.Placed, ws.Interact(new Tool(CellType.Conveyor, Direction.North)));
            Assert.AreEqual(Direction.North, grid.Get(2, 3).Direction);
            ws.Worker.X = 5; ws.Worker.Y = 5; ws.Worker.Facing = Direction.East; // (6,5) 잠김
            Assert.AreEqual(InteractResult.Locked, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            ws.Worker.Facing = Direction.North; // (5,6) 잠김
            Assert.AreEqual(InteractResult.Locked, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            var poor = new WorkerService(grid, sim, new BuildService(grid, new Economy(5), toolbar, sim));
            poor.Worker.X = 3; poor.Worker.Y = 3; poor.Worker.Facing = Direction.East; // (4,3) 빈 바닥
            Assert.AreEqual(InteractResult.NotEnoughMoney, poor.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void Interact_OffGrid_IsNoFloor()
        {
            ws.Worker.X = 0; ws.Worker.Y = 0; ws.Worker.Facing = Direction.West;
            Assert.AreEqual(InteractResult.NoFloor, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void DemolishFacing_DelegatesToBuild()
        {
            Assert.AreEqual(BuildResult.NotDemolishable, ws.TryDemolishFacing());
            ws.Interact(new Tool(CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, ws.TryDemolishFacing());
            Assert.IsTrue(grid.Get(2, 3).Demolishing);
            Assert.AreEqual(BuildResult.AlreadyDemolishing, ws.TryDemolishFacing());
        }
    

        private void AssertUnchanged(int money, int packages, Package carrying)
        {
            Assert.AreEqual(money, economy.Money);
            Assert.AreEqual(packages, sim.Packages.Count);
            Assert.AreSame(carrying, ws.Worker.Carrying);
            Assert.IsNull(ws.PendingInspection);
        }

        [Test]
        public void Preview_MatchesInteract_WithoutSideEffects()
        {
            // NoTool / Placed / NotOwned on empty floor
            Assert.AreEqual(InteractResult.NoTool, ws.Preview(null)); AssertUnchanged(200, 0, null);
            Assert.AreEqual(InteractResult.NotOwned, ws.Preview(new Tool(CellType.Conveyor, Direction.North))); AssertUnchanged(200, 0, null);
            Assert.AreEqual(InteractResult.Placed, ws.Preview(new Tool(CellType.Conveyor, Direction.East))); AssertUnchanged(200, 0, null);
            Assert.AreEqual(InteractResult.Placed, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            Assert.AreEqual(190, economy.Money);
            Assert.AreEqual(InteractResult.Occupied, ws.Preview(new Tool(CellType.Conveyor, Direction.East)));

            // PickedUp / InspectionRequired
            grid.Clear(2, 3); grid.Place(2, 3, CellType.Table, Direction.East);
            Assert.IsTrue(sim.TrySpawn(2, 3, PackageKind.Normal, out var p));
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Preview(null)); AssertUnchanged(190, 1, null);
            p.Inspect(false);
            Assert.AreEqual(InteractResult.PickedUp, ws.Preview(null)); AssertUnchanged(190, 1, null);
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(p, ws.Worker.Carrying);

            // CannotPutHere / PutDown / Delivered while carrying
            ws.Worker.Facing = Direction.North; // (2,5) 빈 바닥
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Preview(new Tool(CellType.Conveyor, Direction.East))); AssertUnchanged(190, 0, p);
            ws.Worker.Facing = Direction.South; // (2,3) 검사대, 비어 있음
            Assert.AreEqual(InteractResult.PutDown, ws.Preview(null)); AssertUnchanged(190, 0, p);
            ws.Worker.X = 4; ws.Worker.Y = 4; ws.Worker.Facing = Direction.East; // (5,4) 반품구
            Assert.AreEqual(InteractResult.Delivered, ws.Preview(null)); AssertUnchanged(190, 0, p);
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(185, economy.Money); // 승인된 정상 택배를 반품구에 → −5
            Assert.IsNull(ws.Worker.Carrying);

            // NoFloor
            ws.Worker.X = 0; ws.Worker.Y = 0; ws.Worker.Facing = Direction.West;
            Assert.AreEqual(InteractResult.NoFloor, ws.Preview(null));
        }

        [Test]
        public void Inspect_FailsIfWorkerNoLongerFacesTable()
        {
            grid.Place(1, 2, CellType.Table, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            ws.Worker.X = 1; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Interact(null));
            ws.Worker.Facing = Direction.East;
            Assert.IsFalse(ws.Inspect(false));
            Assert.IsNull(ws.PendingInspection);
            Assert.IsFalse(p.Inspected);
        }

        [Test]
        public void Interact_DeliversRejectedBadToReturnDock()
        {
            var bad = new PackageFactory(new Random(1), 1.0).Create(50, 0, 0, Direction.East);
            bad.Inspect(true);
            ws.Worker.Carrying = bad;
            ws.Worker.X = 4; ws.Worker.Y = 4; ws.Worker.Facing = Direction.East;
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.AreEqual(205, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
        }

        [Test]
        public void PutDown_OntoScannerInspects_OntoDemolishingFails_OntoInletWorks()
        {
            var p = new PackageFactory(new Random(1), 1.0).Create(60, 0, 0, Direction.East);
            ws.Worker.Carrying = p;
            grid.Place(2, 3, CellType.Scanner, Direction.East);
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected);
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            grid.Get(2, 3).Demolishing = true;
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(null));
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South; // (0,2) 입고구
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.AreSame(p, sim.PackageAt(0, 2));
        }
    }
}
