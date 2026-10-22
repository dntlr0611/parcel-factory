using System;
using System.Linq;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 15: 운송장 부착대(CellType.Labeler), Package.Waybill, 운송장 없음 판정, 검사기 자동 부착, 저장 v7.</summary>
    public class LabelerTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private ConveyorSim sim;
        private BuildService build;
        private WorkerService ws;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(1000);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            build = new BuildService(grid, economy, toolbar, sim);
            ws = new WorkerService(grid, sim, build);
        }

        private int nextId = 700;

        private Package Make(bool? rejected, bool waybill = false, bool air = false, bool bad = false)
        {
            var p = new PackageFactory(new Random(nextId), bad ? 1.0 : 0.0).Create(nextId++, 0, 0, Direction.East);
            if (rejected.HasValue) p.Inspect(rejected.Value);
            p.Waybill = waybill;
            p.Air = air;
            return p;
        }

        /// <summary>(1,3) 동향 레일 → 부착대 (2,3) 동향 → (3,3) 동향 레일.</summary>
        private void Line()
        {
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Labeler, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Conveyor, Direction.East));
        }

        // ---- 설비 ----

        [Test]
        public void Labeler_IsAStartingBuilding_LikeTheTable()
        {
            Assert.AreEqual(20, BuildingCosts.Cost(CellType.Labeler));
            Assert.AreEqual(16, BuildService.RefundFor(CellType.Labeler));
            Assert.IsTrue(FactoryGrid.IsPlaceable(CellType.Labeler));
            foreach (Direction d in Enum.GetValues(typeof(Direction))) Assert.IsTrue(toolbar.Owns(CellType.Labeler, d));
            Assert.IsFalse(WorkerService.IsWalkable(CellType.Labeler));
            build.TryPlace(2, 2, CellType.Labeler, Direction.North);
            var c = grid.Get(2, 2);
            Assert.IsTrue(c.IsBuilding);
            Assert.IsTrue(c.IsTrack);
            Assert.IsFalse(c.IsDock);
            toolbar.Add(CellType.Splitter, Direction.East);
            var types = toolbar.OwnedTools().Select(t => t.Type).Distinct().ToList();
            Assert.Less(types.IndexOf(CellType.Table), types.IndexOf(CellType.Labeler));
            Assert.Less(types.IndexOf(CellType.Labeler), types.IndexOf(CellType.Splitter));
            Assert.AreEqual(ReconfigureResult.Ok, build.SetDirection(2, 2, Direction.West));
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(2, 2));
        }

        // ---- 멈춤 / 통과 ----

        [Test]
        public void StopsEveryParcelWithoutWaybill_InspectedOrNot()
        {
            // 스펙 15.4: 운송장은 한 종류라 검사 여부와 상관없이 운송장 없는 택배는 모두 멈춘다.
            Line();
            foreach (var stop in new[] { Make(false), Make(true), Make(null) })
            {
                sim.PlaceCarried(stop, 1, 3);
                for (int i = 0; i < 6; i++) sim.Tick(0.5f);
                Assert.AreEqual((2, 3), (stop.X, stop.Y), "inspected=" + stop.Inspected);
                Assert.IsTrue(sim.IsAwaitingLabel(stop));
                Assert.AreEqual(1, sim.AwaitingLabelCount);
                sim.Take(stop);
            }

            foreach (var pass in new[] { Make(null, waybill: true), Make(false, waybill: true), Make(true, waybill: true) })
            {
                sim.PlaceCarried(pass, 1, 3);
                sim.Tick(1f); sim.Tick(1f);
                Assert.AreEqual((3, 3), (pass.X, pass.Y), "a parcel with a waybill passes the labeler");
                Assert.IsFalse(sim.IsAwaitingLabel(pass));
                sim.Take(pass);
            }
        }

        [Test]
        public void UninspectedParcel_LabelerThenTable_StopsAtBoth()
        {
            // 부착대 (2,3) → 검사대 (3,3) → 레일 (4,3): 검사 전 택배가 부착대에서 멈추고, 운송장을 붙이면 검사대로 가 검사를 기다린다.
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Labeler, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Table, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(4, 3, CellType.Conveyor, Direction.East));
            var p = Make(null);
            sim.PlaceCarried(p, 1, 3);
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual((2, 3), (p.X, p.Y));
            Assert.IsTrue(sim.IsAwaitingLabel(p));
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.LabelRequired, ws.Interact(null));
            Assert.IsTrue(ws.AttachWaybill());
            Assert.IsFalse(p.Inspected);
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual((3, 3), (p.X, p.Y), "should continue to the table and wait there");
            Assert.IsTrue(sim.IsAwaitingInspection(p));
            ws.Worker.X = 3; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Interact(null));
            Assert.IsTrue(ws.Inspect(false));
            sim.Tick(1f);
            Assert.AreEqual((4, 3), (p.X, p.Y));
            Assert.IsTrue(p.Waybill && p.Inspected && !p.Rejected);
        }

        [Test]
        public void InspectedParcel_TableThenLabeler_StopsAtBoth()
        {
            // 반대 순서: 검사대 (2,3) → 부착대 (3,3) → 레일 (4,3).
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Table, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Labeler, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(4, 3, CellType.Conveyor, Direction.East));
            var p = Make(null);
            sim.PlaceCarried(p, 1, 3);
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual((2, 3), (p.X, p.Y));
            p.Inspect(true);
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual((3, 3), (p.X, p.Y));
            Assert.IsTrue(sim.IsAwaitingLabel(p));
            p.Waybill = true;
            sim.Tick(1f);
            Assert.AreEqual((4, 3), (p.X, p.Y));
        }

        [Test]
        public void Worker_OpensLabelCard_AttachesWaybill_ThenParcelLeaves()
        {
            Line();
            var p = Make(false);
            sim.PlaceCarried(p, 1, 3);
            sim.Tick(1f); sim.Tick(1f);
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            Assert.AreEqual((2, 3), ws.FacingTile());
            Assert.AreEqual(InteractResult.LabelRequired, ws.Preview(null));
            Assert.AreEqual(InteractResult.LabelRequired, ws.Interact(null));
            Assert.AreSame(p, ws.PendingLabel);
            Assert.IsTrue(ws.AttachWaybill());
            Assert.IsTrue(p.Waybill);
            Assert.IsNull(ws.PendingLabel);
            sim.Tick(1f);
            Assert.AreEqual((3, 3), (p.X, p.Y));
        }

        [Test]
        public void AttachWaybill_FailsWhenWorkerLooksAwayOrMoved()
        {
            Line();
            var p = Make(true);
            sim.PlaceCarried(p, 2, 3);
            sim.Tick(1f);
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            ws.Interact(null);
            Assert.IsNotNull(ws.PendingLabel);
            ws.Move(0f, 1f, 0.1f); // 움직이면 대기 취소
            Assert.IsNull(ws.PendingLabel);
            Assert.IsFalse(ws.AttachWaybill());
            Assert.IsFalse(p.Waybill);
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            ws.Interact(null);
            ws.Worker.Facing = Direction.West;
            Assert.IsFalse(ws.AttachWaybill());
            Assert.IsFalse(p.Waybill);
        }

        [Test]
        public void Worker_PutDownOnLabeler_AnyParcel_WaitsForWaybill()
        {
            // 스펙 15.4: 검사 전 택배도 손으로 올려놓을 수 있다("먼저 검사하세요" 거부 없음).
            Line();
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            var u = Make(null);
            ws.Worker.Carrying = u;
            Assert.AreEqual(InteractResult.PutDown, ws.Preview(null));
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            sim.Tick(1f);
            Assert.AreEqual((2, 3), (u.X, u.Y)); // 놓인 자리에서 운송장을 기다린다
            Assert.AreEqual(InteractResult.LabelRequired, ws.Preview(null));
            Assert.IsFalse(Enum.IsDefined(typeof(InteractResult), "NeedsInspection"));
        }

        // ---- 자동 검사기 ----

        [Test]
        public void Scanner_AttachesWaybillToo()
        {
            toolbar.Add(CellType.Scanner, Direction.East);
            build.TryPlace(1, 3, CellType.Conveyor, Direction.East);
            build.TryPlace(2, 3, CellType.Scanner, Direction.East);
            build.TryPlace(3, 3, CellType.Conveyor, Direction.East);
            var a = Make(null);
            sim.PlaceCarried(a, 1, 3);
            sim.Tick(1f);
            Assert.IsTrue(a.Inspected);
            Assert.IsTrue(a.Waybill);
            sim.Take(a);
            var b = Make(true); // 사람이 이미 거부한 택배: 판정은 그대로, 운송장만 붙는다
            sim.PlaceCarried(b, 2, 3);
            Assert.IsTrue(b.Rejected);
            Assert.IsTrue(b.Waybill);
        }

        // ---- 판정 ----

        private static Package State(bool air, bool? rejected, bool waybill, bool bad)
        {
            var p = new Package { Air = air, Bad = bad, Waybill = waybill, Label = new PackageLabel() };
            if (rejected.HasValue) p.Inspect(rejected.Value);
            return p;
        }

        [Test]
        public void Scoring_Order_WrongCarrier_Uninspected_NoWaybill_Existing()
        {
            // 1) 운송 수단 틀림이 가장 먼저
            Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(true, null, false, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(false, false, false, false), CellType.AirReturnDock));
            // 2) 미검사(운송장 여부 무관)
            Assert.AreEqual(DeliveryOutcome.UninspectedToOutlet, DeliveryScoring.Outcome(State(false, null, false, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.UninspectedToReturn, DeliveryScoring.Outcome(State(true, null, true, false), CellType.AirReturnDock));
            // 3) 운송장 없음: 출고 −20, 반품 −10 (승인/거부, 일반/항공 모두)
            foreach (var rej in new[] { false, true })
                foreach (var bad in new[] { false, true })
                {
                    Assert.AreEqual(DeliveryOutcome.NoWaybillToOutlet, DeliveryScoring.Outcome(State(false, rej, false, bad), CellType.Outlet));
                    Assert.AreEqual(DeliveryOutcome.NoWaybillToOutlet, DeliveryScoring.Outcome(State(true, rej, false, bad), CellType.AirOutlet));
                    Assert.AreEqual(DeliveryOutcome.NoWaybillToReturn, DeliveryScoring.Outcome(State(false, rej, false, bad), CellType.ReturnDock));
                    Assert.AreEqual(DeliveryOutcome.NoWaybillToReturn, DeliveryScoring.Outcome(State(true, rej, false, bad), CellType.AirReturnDock));
                }
            Assert.AreEqual(-20, DeliveryScoring.Delta(DeliveryOutcome.NoWaybillToOutlet));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.NoWaybillToReturn));
            Assert.IsFalse(DeliveryScoring.IsSuccess(DeliveryOutcome.NoWaybillToOutlet));
            Assert.IsFalse(DeliveryScoring.IsSuccess(DeliveryOutcome.NoWaybillToReturn));
            // 4) 운송장이 있으면 기존 표
            Assert.AreEqual(DeliveryOutcome.Delivered, DeliveryScoring.Outcome(State(false, false, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.BadToOutlet, DeliveryScoring.Outcome(State(false, false, true, true), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.RejectedToOutlet, DeliveryScoring.Outcome(State(false, true, true, true), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, DeliveryScoring.Outcome(State(true, true, true, true), CellType.AirReturnDock));
            Assert.AreEqual(DeliveryOutcome.ApprovedToReturn, DeliveryScoring.Outcome(State(false, false, true, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.GoodToReturn, DeliveryScoring.Outcome(State(false, true, true, false), CellType.ReturnDock));
        }

        [Test]
        public void Session_NoWaybillDelivery_CostsAndGivesNoExp()
        {
            var s = new GameSession(new Economy(500));
            s.Sim.DeliverCarried(Make(false), CellType.Outlet);
            Assert.AreEqual(480, s.Economy.Money);
            s.Sim.DeliverCarried(Make(true, bad: true), CellType.ReturnDock);
            Assert.AreEqual(470, s.Economy.Money);
            Assert.AreEqual(0, s.Progression.Exp);
            Assert.AreEqual(0, s.Sim.DeliveredCount);
            s.Sim.DeliverCarried(Make(false, waybill: true), CellType.Outlet);
            Assert.AreEqual(500, s.Economy.Money);
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp);
        }

        // ---- 저장 v7 ----

        [Test]
        public void Save_RoundTripsWaybillAndLabeler_MissingFieldReadsFalse()
        {
            var s = new GameSession(new Economy(500));
            s.Build.TryPlace(2, 3, CellType.Labeler, Direction.North);
            var onLabeler = Make(false);
            onLabeler.Id = s.Sim.NextId++;
            s.Sim.PlaceCarried(onLabeler, 2, 3);
            var carried = Make(false, waybill: true);
            carried.Id = s.Sim.NextId++;
            s.Worker.Worker.Carrying = carried;
            var pile = Make(true, waybill: true);
            pile.Id = s.Sim.NextId++;
            Assert.IsTrue(s.Sim.PutOnPallet(pile, 5, 2));

            var json = SaveSerializer.ToJson(SaveSerializer.Capture(s));
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));
            Assert.AreEqual(CellType.Labeler, r.Grid.Get(2, 3).Type);
            Assert.AreEqual(Direction.North, r.Grid.Get(2, 3).Direction);
            Assert.IsFalse(r.Sim.PackageAt(2, 3).Waybill);
            Assert.IsTrue(r.Worker.Worker.Carrying.Waybill);
            Assert.IsTrue(r.Grid.Get(5, 2).Pallet[0].Waybill);

            var old = json.Replace("\"Waybill\": true,", "").Replace("\"Waybill\": false,", "");
            StringAssert.DoesNotContain("Waybill", old);
            var r2 = SaveSerializer.Restore(SaveSerializer.FromJson(old));
            Assert.IsFalse(r2.Worker.Worker.Carrying.Waybill);
        }
    }
}
