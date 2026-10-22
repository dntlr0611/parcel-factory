using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 17: 취급주의 택배 — 부지 확장 2 다음 날부터 15%, 파손 세 조건(고속 레일·들고 달리기·위에 2개 이상), 파손 시 불량·검사 초기화,
    /// 부착대 스티커, 판정(NoFragileSticker −10, DeliveredFragile +45), 저장 v9.</summary>
    public class FragileTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private ConveyorSim sim;
        private BuildService build;
        private WorkerService ws;
        private List<Package> broken;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(5000);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            build = new BuildService(grid, economy, toolbar, sim);
            ws = new WorkerService(grid, sim, build);
            broken = new List<Package>();
            sim.PackageBroken += p => broken.Add(p);
        }

        private int nextId = 900;

        private Package Make(bool fragile = true, bool? rejected = null, bool waybill = false, bool sticker = false, bool bad = false, bool air = false)
        {
            var p = new PackageFactory(new Random(nextId), bad ? 1.0 : 0.0).Create(nextId++, 0, 0, Direction.East);
            p.Fragile = fragile;
            if (rejected.HasValue) p.Inspect(rejected.Value);
            p.Waybill = waybill;
            p.FragileSticker = sticker;
            p.Air = air;
            return p;
        }

        // ---- 생성 ----

        [Test]
        public void PackageFactory_DefaultsToNoFragile_AndChanceMakesAboutFifteenPercent()
        {
            Assert.AreEqual(0.15, PackageFactory.FragileChanceAfterPlot2);
            var none = new PackageFactory(new Random(3));
            Assert.AreEqual(0.0, none.FragileChance);
            for (int i = 0; i < 300; i++) Assert.IsFalse(none.Create(i + 1, 0, 2, Direction.East).Fragile);

            var f = new PackageFactory(new Random(4), 0.3, 0.25) { FragileChance = PackageFactory.FragileChanceAfterPlot2 };
            int fragile = 0, both = 0;
            const int n = 3000;
            for (int i = 0; i < n; i++)
            {
                var p = f.Create(i + 1, 0, 2, Direction.East);
                Assert.IsFalse(p.Broken);
                Assert.IsFalse(p.FragileSticker);
                if (p.Fragile) fragile++;
                if (p.Fragile && p.Air) both++;
            }
            Assert.That(fragile, Is.InRange(n * 0.12, n * 0.18));
            Assert.Greater(both, 0, "fragile and air are drawn independently (both can happen)");
        }

        [Test]
        public void PackageFactory_FragileDrawIsLast_OtherFieldsUnchanged()
        {
            var plain = new PackageFactory(new Random(5), 0.5, 0.5).Create(1, 0, 2, Direction.East);
            var withFragile = new PackageFactory(new Random(5), 0.5, 0.5) { FragileChance = 1.0 }.Create(1, 0, 2, Direction.East);
            Assert.IsTrue(withFragile.Fragile);
            Assert.AreEqual(plain.Bad, withFragile.Bad);
            Assert.AreEqual(plain.Air, withFragile.Air);
            Assert.AreEqual(plain.Label.Weight, withFragile.Label.Weight);
            Assert.AreEqual(plain.Label.Scale, withFragile.Label.Scale);
            Assert.AreEqual(plain.Label.Destination, withFragile.Label.Destination);
            Assert.AreEqual(plain.Label.Method, withFragile.Label.Method);
        }

        [Test]
        public void Session_NoFragileOnPlot2Day_FragileFromNextMorning()
        {
            var s = new GameSession(new Economy(5000));
            Assert.AreEqual(0, s.FragileFromDay);
            Assert.AreEqual(0.0, s.Sim.Factory.FragileChance);
            s.Clock.Set(3, 700f);
            s.Unlocks.TryPurchase(UnlockId.Plot1);
            Assert.AreEqual(0, s.FragileFromDay);
            Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot2));
            Assert.AreEqual(4, s.FragileFromDay, "the day after Plot2 was bought");
            Assert.AreEqual(0.0, s.Sim.Factory.FragileChance, "not on the purchase day");
            for (int i = 0; i < 300; i++)
            {
                Assert.IsTrue(s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
                Assert.IsFalse(p.Fragile);
                s.Sim.Take(p);
            }
            s.Tick(10000f); // 18:00
            s.NextDay();
            Assert.AreEqual(4, s.Clock.Day);
            Assert.AreEqual(PackageFactory.FragileChanceAfterPlot2, s.Sim.Factory.FragileChance);
            int fragile = 0;
            for (int i = 0; i < 1000; i++)
            {
                Assert.IsTrue(s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
                if (p.Fragile) fragile++;
                s.Sim.Take(p);
            }
            Assert.That(fragile, Is.InRange(100, 200));
        }

        [Test]
        public void Session_LeavesAnInjectedFactoryAlone()
        {
            var f = new PackageFactory(new Random(42), 0.3, 0.0);
            var s = new GameSession(new Economy(5000), f);
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Tick(10000f);
            s.NextDay();
            Assert.AreEqual(0.0, f.FragileChance);
        }

        // ---- 파손 조건 ----

        [Test]
        public void EnteringFastConveyor_Breaks_FragileOnly()
        {
            toolbar.Add(CellType.FastConveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.FastConveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Conveyor, Direction.East));
            var f = Make(rejected: false, waybill: true, sticker: true);
            var plain = Make(fragile: false);
            sim.PlaceCarried(f, 1, 3);
            sim.Tick(0.6f);
            Assert.IsFalse(f.Broken, "still on the plain rail");
            sim.Tick(0.6f);
            Assert.AreEqual((2, 3), (f.X, f.Y));
            Assert.IsTrue(f.Broken, "breaks the moment it enters the fast rail");
            Assert.IsTrue(f.Bad);
            CollectionAssert.AreEqual(new[] { f }, broken);
            sim.Tick(1f);
            Assert.AreEqual(1, broken.Count, "breaks only once");

            sim.PlaceCarried(plain, 1, 3);
            for (int i = 0; i < 4; i++) sim.Tick(0.5f);
            Assert.IsFalse(plain.Broken);
            Assert.IsFalse(plain.Bad);
        }

        [Test]
        public void PuttingFragileOnFastConveyorByHand_Breaks()
        {
            toolbar.Add(CellType.FastConveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.FastConveyor, Direction.East));
            var f = Make();
            sim.PlaceCarried(f, 2, 3);
            Assert.IsTrue(f.Broken);
        }

        [Test]
        public void SplitterBridgeRailTableLabelerScanner_DoNotBreak()
        {
            toolbar.Add(CellType.Splitter, Direction.East);
            toolbar.Add(CellType.Scanner, Direction.East);
            toolbar.Add(CellType.Bridge, Direction.East);
            // (0,2) 입고구 → (1,2) 레일 → (2,2) 브릿지 → (3,2) 분배기 → (4,2) 검사대 → 검사 → (5,2)는 시작 팔레트라 비운다
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Bridge, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Splitter, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(4, 3, CellType.Table, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(5, 3, CellType.Labeler, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 1, CellType.Scanner, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 1, CellType.Conveyor, Direction.East));
            var f = Make();
            sim.PlaceCarried(f, 1, 3);
            for (int i = 0; i < 8; i++) sim.Tick(0.5f);
            Assert.AreEqual((4, 3), (f.X, f.Y), "waits on the table");
            f.Inspect(false);
            for (int i = 0; i < 4; i++) sim.Tick(0.5f);
            Assert.AreEqual((5, 3), (f.X, f.Y), "waits on the labeler");
            Assert.IsFalse(f.Broken);
            sim.Take(f);
            var g = Make();
            sim.PlaceCarried(g, 1, 1);
            for (int i = 0; i < 3; i++) sim.Tick(0.5f);
            Assert.IsFalse(g.Broken);
            Assert.IsEmpty(broken);
        }

        [Test]
        public void RunningWhileCarryingFragile_Breaks_WalkingDoesNot()
        {
            ws.Worker.X = 3f; ws.Worker.Y = 5f;
            var f = Make();
            ws.Worker.Carrying = f;
            ws.Move(1f, 0f, 0.1f);
            Assert.IsFalse(f.Broken, "walking is gentle");
            ws.Move(1f, 0f, 0.1f, true);
            Assert.IsTrue(ws.IsRunning);
            Assert.IsTrue(f.Broken);
            Assert.IsTrue(f.Bad);
            Assert.AreEqual(1, broken.Count);

            var plain = Make(fragile: false);
            ws.Worker.Carrying = plain;
            ws.Move(-1f, 0f, 0.1f, true);
            Assert.IsFalse(plain.Broken);
        }

        [Test]
        public void RunRequest_WithoutActualRun_DoesNotBreak()
        {
            ws.Worker.X = 3f; ws.Worker.Y = 5f;
            var f = Make();
            ws.Worker.Carrying = f;
            ws.Worker.Stamina = WorkerService.RunMinStamina - 1f; // 달릴 수 없다 → 걷기
            ws.Move(1f, 0f, 0.1f, true);
            Assert.IsFalse(ws.IsRunning);
            Assert.IsFalse(f.Broken);
            ws.Worker.Stamina = 100f;
            ws.Worker.X = 0f + WorkerService.Radius - 0.5f; // 서쪽 벽에 붙어 서쪽으로 달리려 함 → 움직이지 못함
            ws.Move(-1f, 0f, 0.1f, true);
            Assert.IsFalse(ws.IsRunning);
            Assert.IsFalse(f.Broken);
        }

        [Test]
        public void PalletPile_TwoInTheSameSlotAboveBreaks_HandStacking()
        {
            // 더미는 2자리 × 3층(인덱스 짝수 = 왼쪽, 홀수 = 오른쪽). 같은 자리 위층에 2개가 올라가야 깨진다(스펙 17.2).
            var f = Make();
            Assert.IsTrue(sim.PutOnPallet(f, 5, 2));                 // 0: 왼쪽 1층
            Assert.IsTrue(sim.PutOnPallet(Make(fragile: false), 5, 2)); // 1: 오른쪽 1층
            Assert.IsTrue(sim.PutOnPallet(Make(fragile: false), 5, 2)); // 2: 왼쪽 2층 (F 위 1개)
            Assert.IsFalse(f.Broken, "[F, a, b]: only one box sits on F's slot");
            Assert.IsTrue(sim.PutOnPallet(Make(fragile: false), 5, 2)); // 3: 오른쪽 2층
            Assert.IsFalse(f.Broken, "a box in the other slot does not press on F");
            Assert.IsTrue(sim.PutOnPallet(Make(fragile: false), 5, 2)); // 4: 왼쪽 3층 (F 위 2개)
            Assert.IsTrue(f.Broken, "two boxes in the same slot above break it");
            Assert.AreEqual(1, broken.Count);
            var top = Make();
            Assert.IsTrue(sim.PutOnPallet(top, 5, 2));                // 5: 오른쪽 3층
            Assert.IsFalse(top.Broken, "the top parcel is not crushed");
        }

        [Test]
        public void PalletPile_SecondLayerFragileNeverBreaks_RightSlotBreaksOnItsThirdLayer()
        {
            var a = Make(); var b = Make(); var c = Make(); var d = Make();
            foreach (var p in new[] { a, b, c, d, Make(fragile: false) }) Assert.IsTrue(sim.PutOnPallet(p, 5, 2));
            Assert.IsTrue(a.Broken, "left layer 0 under indices 2 and 4");
            Assert.IsFalse(b.Broken, "right layer 0 has only index 3 above it so far");
            Assert.IsFalse(c.Broken, "layer 1 has at most one box above it");
            Assert.IsTrue(sim.PutOnPallet(Make(fragile: false), 5, 2));
            Assert.IsTrue(b.Broken);
            Assert.IsFalse(c.Broken);
            Assert.IsFalse(d.Broken);
        }

        [Test]
        public void PalletPile_SameSlotRule_RailStacking()
        {
            // (3,2) → (4,2) 동향 레일 → (5,2) 시작 파랑 팔레트
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(4, 2, CellType.Conveyor, Direction.East));
            var f = Make();
            Assert.IsTrue(sim.PutOnPallet(Make(fragile: false), 5, 2));
            Assert.IsTrue(sim.PutOnPallet(f, 5, 2)); // 인덱스 1: 오른쪽 1층
            for (int n = 3; n <= 6; n++)
            {
                sim.PlaceCarried(Make(fragile: false), 4, 2);
                sim.Tick(1f);
                Assert.AreEqual(n, sim.PalletCount(5, 2));
                Assert.AreEqual(n == 6, f.Broken, $"after the rail stacked box #{n}");
            }
        }

        [Test]
        public void Break_TurnsBad_ClearsInspection_KeepsWaybillAndSticker()
        {
            var f = Make(rejected: false, waybill: true, sticker: true);
            Assert.IsTrue(sim.Break(f));
            Assert.IsTrue(f.Broken);
            Assert.IsTrue(f.Bad);
            Assert.IsFalse(f.Inspected, "inspection is wiped");
            Assert.IsFalse(f.Rejected);
            Assert.IsTrue(f.Waybill);
            Assert.IsTrue(f.FragileSticker);
            Assert.IsFalse(sim.Break(f), "already broken");
            Assert.IsFalse(sim.Break(Make(fragile: false)), "only fragile parcels break");
            Assert.AreEqual(1, broken.Count);
        }

        [Test]
        public void BrokenParcelOnTable_MustBeInspectedAgain()
        {
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Table, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Conveyor, Direction.East));
            var f = Make(rejected: false, waybill: true, sticker: true);
            sim.Break(f);
            sim.PlaceCarried(f, 2, 3);
            sim.Tick(2f);
            Assert.IsTrue(sim.IsAwaitingInspection(f));
        }

        [Test]
        public void Scanner_RejectsBrokenParcel_AndAttachesWaybillAndSticker()
        {
            toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Scanner, Direction.East));
            var good = Make();
            sim.PlaceCarried(good, 2, 3);
            Assert.IsTrue(good.Inspected && !good.Rejected);
            Assert.IsTrue(good.Waybill);
            Assert.IsTrue(good.FragileSticker);
            sim.Take(good);
            var b = Make(rejected: false, waybill: true, sticker: true);
            sim.Break(b);
            sim.PlaceCarried(b, 2, 3);
            Assert.IsTrue(b.Inspected && b.Rejected, "a broken parcel is a defect");
            var plain = Make(fragile: false);
            sim.Take(b);
            sim.PlaceCarried(plain, 2, 3);
            Assert.IsFalse(plain.FragileSticker, "no sticker for a normal parcel");
        }

        // ---- 부착대 ----

        /// <summary>(1,3) 동향 레일 → 부착대 (2,3) 동향 → (3,3) 동향 레일.</summary>
        private void Line()
        {
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(2, 3, CellType.Labeler, Direction.East));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(3, 3, CellType.Conveyor, Direction.East));
        }

        [Test]
        public void Labeler_FragileNeedsWaybillAndSticker()
        {
            Line();
            var onlyWaybill = Make(waybill: true);
            sim.PlaceCarried(onlyWaybill, 1, 3);
            for (int i = 0; i < 4; i++) sim.Tick(0.5f);
            Assert.AreEqual((2, 3), (onlyWaybill.X, onlyWaybill.Y));
            Assert.IsTrue(sim.IsAwaitingLabel(onlyWaybill), "a fragile parcel with a waybill still waits for the sticker");
            sim.Take(onlyWaybill);
            var both = Make(waybill: true, sticker: true);
            sim.PlaceCarried(both, 1, 3);
            for (int i = 0; i < 4; i++) sim.Tick(0.5f);
            Assert.AreEqual((3, 3), (both.X, both.Y));
            var plainWithWaybill = Make(fragile: false, waybill: true);
            sim.PlaceCarried(plainWithWaybill, 1, 3);
            for (int i = 0; i < 4; i++) sim.Tick(0.5f);
            Assert.AreEqual((3, 3), (plainWithWaybill.X, plainWithWaybill.Y), "a normal parcel needs no sticker");
        }

        [Test]
        public void Worker_AttachesWaybillThenSticker_PendingKeptUntilBoth()
        {
            Line();
            var f = Make(rejected: false);
            sim.PlaceCarried(f, 2, 3);
            sim.Tick(1f);
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.LabelRequired, ws.Interact(null));
            Assert.IsTrue(ws.AttachWaybill());
            Assert.IsTrue(f.Waybill);
            Assert.AreSame(f, ws.PendingLabel, "still waiting for the sticker");
            sim.Tick(1f);
            Assert.AreEqual((2, 3), (f.X, f.Y));
            Assert.IsTrue(ws.AttachFragileSticker());
            Assert.IsTrue(f.FragileSticker);
            Assert.IsNull(ws.PendingLabel);
            sim.Tick(1f);
            Assert.AreEqual((3, 3), (f.X, f.Y));
        }

        [Test]
        public void Worker_StickerFirst_ThenWaybill_AlsoWorks_AndStickerRefusedForNormal()
        {
            Line();
            var f = Make();
            sim.PlaceCarried(f, 2, 3);
            ws.Worker.X = 2; ws.Worker.Y = 4; ws.Worker.Facing = Direction.South;
            ws.Interact(null);
            Assert.IsTrue(ws.AttachFragileSticker());
            Assert.AreSame(f, ws.PendingLabel);
            Assert.IsFalse(ws.AttachFragileSticker(), "already stuck");
            ws.Interact(null);
            Assert.IsTrue(ws.AttachWaybill());
            Assert.IsNull(ws.PendingLabel);
            Assert.IsFalse(sim.IsAwaitingLabel(f));

            var plain = Make(fragile: false);
            sim.Take(f);
            sim.PlaceCarried(plain, 2, 3);
            ws.Interact(null);
            Assert.IsFalse(ws.AttachFragileSticker(), "a normal parcel takes no sticker");
            Assert.IsFalse(plain.FragileSticker);
        }

        // ---- 판정 ----

        private static Package State(bool fragile, bool? rejected, bool waybill, bool sticker, bool bad, bool air = false, bool brokenFlag = false)
        {
            var p = new Package { Fragile = fragile, Air = air, Bad = bad, Waybill = waybill, FragileSticker = sticker, Broken = brokenFlag, Label = new PackageLabel() };
            if (rejected.HasValue) p.Inspect(rejected.Value);
            return p;
        }

        [Test]
        public void Scoring_Order_Carrier_Uninspected_Waybill_Sticker_Existing()
        {
            Assert.AreEqual(DeliveryOutcome.WrongCarrier, DeliveryScoring.Outcome(State(true, null, false, false, false, air: true), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.UninspectedToOutlet, DeliveryScoring.Outcome(State(true, null, false, false, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.UninspectedToReturn, DeliveryScoring.Outcome(State(true, null, true, false, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.NoWaybillToOutlet, DeliveryScoring.Outcome(State(true, false, false, false, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.NoWaybillToReturn, DeliveryScoring.Outcome(State(true, true, false, false, true), CellType.ReturnDock));
            foreach (var dock in new[] { CellType.Outlet, CellType.ReturnDock })
                foreach (var rej in new[] { false, true })
                    Assert.AreEqual(DeliveryOutcome.NoFragileSticker, DeliveryScoring.Outcome(State(true, rej, true, false, rej), dock), $"{dock} rejected={rej}");
            Assert.AreEqual(DeliveryOutcome.NoFragileSticker, DeliveryScoring.Outcome(State(true, false, true, false, false, air: true), CellType.AirOutlet));
            Assert.AreEqual(DeliveryOutcome.NoFragileSticker, DeliveryScoring.Outcome(State(true, true, true, false, true, air: true), CellType.AirReturnDock));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.NoFragileSticker));
            Assert.IsFalse(DeliveryScoring.IsSuccess(DeliveryOutcome.NoFragileSticker));

            Assert.AreEqual(DeliveryOutcome.DeliveredFragile, DeliveryScoring.Outcome(State(true, false, true, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.DeliveredFragile, DeliveryScoring.Outcome(State(true, false, true, true, false, air: true), CellType.AirOutlet));
            Assert.AreEqual(45, DeliveryScoring.Delta(DeliveryOutcome.DeliveredFragile));
            Assert.IsTrue(DeliveryScoring.IsSuccess(DeliveryOutcome.DeliveredFragile));
            // 스티커가 붙은 취급주의도 나머지는 기존 표
            Assert.AreEqual(DeliveryOutcome.RejectedToOutlet, DeliveryScoring.Outcome(State(true, true, true, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.GoodToReturn, DeliveryScoring.Outcome(State(true, true, true, true, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ApprovedToReturn, DeliveryScoring.Outcome(State(true, false, true, true, false), CellType.ReturnDock));
            // 파손품 = 불량
            Assert.AreEqual(DeliveryOutcome.BadToOutlet, DeliveryScoring.Outcome(State(true, false, true, true, true, brokenFlag: true), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, DeliveryScoring.Outcome(State(true, true, true, true, true, brokenFlag: true), CellType.ReturnDock));
            // 일반 택배는 스티커와 무관
            Assert.AreEqual(DeliveryOutcome.Delivered, DeliveryScoring.Outcome(State(false, false, true, false, false), CellType.Outlet));
        }

        [Test]
        public void Session_FragileDelivery_Money_Exp()
        {
            var s = new GameSession(new Economy(500));
            s.Sim.DeliverCarried(Make(rejected: false, waybill: true, sticker: true), CellType.Outlet);
            Assert.AreEqual(545, s.Economy.Money);
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp);
            Assert.AreEqual(1, s.Sim.DeliveredCount);
            s.Sim.DeliverCarried(Make(rejected: false, waybill: true), CellType.Outlet);
            Assert.AreEqual(535, s.Economy.Money);
            Assert.AreEqual(Progression.DeliveredExp, s.Progression.Exp);
            var b = Make(rejected: false, waybill: true, sticker: true);
            s.Sim.Break(b);
            b.Inspect(true); // 다시 검사해서 거부
            s.Sim.DeliverCarried(b, CellType.ReturnDock);
            Assert.AreEqual(545, s.Economy.Money);
            Assert.AreEqual(Progression.DeliveredExp + Progression.ReturnedBadExp, s.Progression.Exp);
        }

        // ---- 저장 v9 ----

        [Test]
        public void Save_V9_RoundTripsFragileFields_AndFragileFromDay()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(2, s.FragileFromDay);
            var line = Make(waybill: true);
            line.Id = s.Sim.NextId++;
            s.Build.TryPlace(2, 3, CellType.Conveyor, Direction.East);
            s.Sim.PlaceCarried(line, 2, 3);
            var carried = Make(rejected: true, waybill: true, sticker: true);
            carried.Id = s.Sim.NextId++;
            s.Sim.Break(carried);
            s.Worker.Worker.Carrying = carried;
            var pile = Make(fragile: false);
            pile.Id = s.Sim.NextId++;
            s.Sim.PutOnPallet(pile, 5, 2);

            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(11, st.Version); // v11: 점심 카드(스펙 21.4)
            Assert.AreEqual(2, st.FragileFromDay);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            Assert.AreEqual(2, r.FragileFromDay);
            var rl = r.Sim.PackageAt(2, 3);
            Assert.IsTrue(rl.Fragile && rl.Waybill && !rl.FragileSticker && !rl.Broken);
            var rc = r.Worker.Worker.Carrying;
            Assert.IsTrue(rc.Fragile && rc.Broken && rc.FragileSticker && rc.Bad && !rc.Inspected);
            Assert.IsFalse(r.Grid.Get(5, 2).Pallet[0].Fragile);
            Assert.AreEqual(0.0, r.Sim.Factory.FragileChance, "day 1 < day 2");

            st.Day = 2;
            var r2 = SaveSerializer.Restore(st);
            Assert.AreEqual(PackageFactory.FragileChanceAfterPlot2, r2.Sim.Factory.FragileChance);

            var bad = SaveSerializer.Capture(s);
            bad.FragileFromDay = 0; // 부지 확장 2가 있는데 시작일 없음 → 손상
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(bad));
            var noPlot = SaveSerializer.Capture(new GameSession(new Economy(10)));
            noPlot.FragileFromDay = 3; // 부지 확장 2 없이 시작일 → 손상
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(noPlot));
            var future = SaveSerializer.Capture(s);
            future.FragileFromDay = future.Day + 2; // 산 날 + 1보다 뒤일 수 없다 → 손상
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(future));
            future.FragileFromDay = future.Day + 1;
            Assert.AreEqual(future.Day + 1, SaveSerializer.Restore(future).FragileFromDay);
        }

        [Test]
        public void Save_V8_ReadsWithoutFragile_FragileStartsTheDayAfterTheLoadedDay()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.Grant(UnlockId.Plot2);
            var st = SaveSerializer.Capture(s);
            var json = SaveSerializer.ToJson(st)
                .Replace("\"Version\": 11", "\"Version\": 8") // v11(스펙 21.4)에서 만든 저장을 v8로
                .Replace("\"Fragile\": false,", "").Replace("\"Broken\": false,", "").Replace("\"FragileSticker\": false,", "");
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"FragileFromDay\": \\d+,", "");
            // 진짜 v8 모양: v9의 트럭과 v10의 공장 목록·차량·공장 필드가 없다.
            json = SaveV10Tests.StripTopLevel(json, "Trucks", "TrucksDepartedToday", "OtherFactories", "CurrentFactory", "Kind", "EarnedToday",
                "LunchMenu", "LunchConfirmed", "Contract", "ContractCandidates"); // v11 점심 카드도 없다
            StringAssert.DoesNotContain("FragileFromDay", json);
            StringAssert.DoesNotContain("Vehicles", json);
            StringAssert.DoesNotContain("OtherFactories", json);
            var old = SaveSerializer.FromJson(json);
            old.Day = 5;
            var r = SaveSerializer.Restore(old);
            Assert.AreEqual(6, r.FragileFromDay, "unknown purchase day → from the next day");
            Assert.AreEqual(0.0, r.Sim.Factory.FragileChance);

            var noPlot = SaveSerializer.Capture(new GameSession(new Economy(10)));
            noPlot.Version = 8;
            Assert.AreEqual(0, SaveSerializer.Restore(noPlot).FragileFromDay);
        }
    }
}
