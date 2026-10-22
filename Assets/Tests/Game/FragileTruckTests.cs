using System.Collections;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>스펙 17·18 화면: 취급주의 옆면 표시 전환(흐린 인쇄 → 스티커), 파손 금 무늬·효과·토스트, 검사 카드 줄,
    /// 운송장 카드 두 장 드래그, 트럭 알약·출발/도착 연출·떠난 도크 거부, 하루 요약 출발 대수.</summary>
    public class FragileTruckTests
    {
        private static Package Fragile(GameSession s, bool? rejected = null)
        {
            var p = new PackageFactory(new System.Random(17), 0.0).Create(s.Sim.NextId++, 0, 0, Direction.East);
            p.Fragile = true;
            if (rejected.HasValue) p.Inspect(rejected.Value);
            return p;
        }

        private static PointerEventData Pointer(Vector2 screen) =>
            new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };

        private static Vector2 ScreenOf(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.position);

        private static void DragTo(GameObject item, RectTransform from, Vector2 to)
        {
            var e = Pointer(ScreenOf(from));
            ExecuteEvents.Execute(item, e, ExecuteEvents.beginDragHandler);
            e.position = to;
            ExecuteEvents.Execute(item, e, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(item, e, ExecuteEvents.endDragHandler);
        }

        [UnityTest]
        public IEnumerator FragileBox_PrintThenSticker_BreakShowsCrackFxAndToast()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(200);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Conveyor, Direction.East));
                var p = Fragile(s);
                s.Sim.PlaceCarried(p, 2, 3);
                yield return null;
                var box = boot.GetComponent<PackageViews>().ViewOf(p.Id).transform;
                var print = box.Find("FragilePrint");
                var sticker = box.Find("FragileSticker");
                Assert.AreEqual(4, print.childCount, "four side faces");
                Assert.IsTrue(print.gameObject.activeSelf, "faint print before the sticker");
                Assert.IsFalse(sticker.gameObject.activeSelf);
                Assert.IsFalse(box.Find("Crack").gameObject.activeSelf);
                Assert.IsFalse(box.Find("AirMark").gameObject.activeSelf, "the top stays free for the air mark and waybill");
                p.FragileSticker = true;
                yield return null;
                Assert.IsFalse(print.gameObject.activeSelf);
                Assert.IsTrue(sticker.gameObject.activeSelf, "vivid sticker after attaching");
                // 네 면이 모두 바깥을 본다(Quad 앞면 = 로컬 −z)
                foreach (Transform side in sticker)
                {
                    var outward = side.position - box.position; outward.y = 0f;
                    Assert.Greater(Vector3.Dot(-side.forward, outward.normalized), 0.9f, side.name + " faces out");
                }

                Assert.IsTrue(s.Sim.Break(p));
                Assert.AreEqual(HudController.BrokenToast, boot.Hud.LastToast);
                Assert.IsNotNull(boot.GetComponent<PackageViews>().LastBreakFx, "break effect spawned");
                yield return null;
                Assert.IsTrue(box.Find("Crack").gameObject.activeSelf, "crack pattern on top");
                Assert.IsTrue(sticker.gameObject.activeSelf, "the sticker stays");
                yield return new WaitForSecondsRealtime(BreakFx.Seconds + 0.2f);
                Assert.IsTrue(boot.GetComponent<PackageViews>().LastBreakFx == null, "effect cleans itself up");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InspectCard_ShowsFragileAndBrokenRows()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(200);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Table, Direction.East));
                var p = Fragile(s);
                s.Sim.Break(p);
                s.Sim.PlaceCarried(p, 2, 3);
                var w = s.Worker.Worker;
                w.X = 2f; w.Y = 4f; w.Facing = Direction.South;
                yield return null;
                StringAssert.StartsWith(HudController.BrokenTag + " 검사", boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>().text);
                StringAssert.Contains("· 파손", boot.StatusBars.TextAt(2, 3));
                boot.Input.Interact();
                yield return null;
                var paper = boot.Hud.Root.Find("Inspect/Card/Paper");
                Assert.IsTrue(paper.Find("Fragile").gameObject.activeSelf);
                Assert.AreEqual(HudController.FragileTag, paper.Find("Fragile").GetComponent<Text>().text);
                Assert.IsTrue(paper.Find("Broken").gameObject.activeSelf);
                Assert.IsTrue(paper.Find("FragileIcon").gameObject.activeSelf, "glass icon on the fragile row");
                StringAssert.StartsWith(HudController.BrokenTag, paper.Find("Broken").GetComponent<Text>().text);
                Assert.AreEqual(UiKit.Danger, paper.Find("Broken").GetComponent<Text>().color);
                float tall = ((RectTransform)paper).sizeDelta.y;
                boot.Panels.CloseAll();
                s.Sim.Take(p);
                var plain = new PackageFactory(new System.Random(3), 0.0).Create(s.Sim.NextId++, 0, 0, Direction.East);
                s.Sim.PlaceCarried(plain, 2, 3);
                yield return null;
                boot.Input.Interact();
                yield return null;
                Assert.IsFalse(paper.Find("Fragile").gameObject.activeSelf, "no fragile row for a normal parcel");
                Assert.IsFalse(paper.Find("Broken").gameObject.activeSelf);
                Assert.IsFalse(paper.Find("FragileIcon").gameObject.activeSelf);
                Assert.AreEqual(tall - 2f * Panels.InspectRowStep, ((RectTransform)paper).sizeDelta.y, 0.01f);
                boot.Panels.CloseAll();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WaybillCard_FragileNeedsBothDrags_ThenCloses()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(200);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Labeler, Direction.East));
                var p = Fragile(s, false);
                s.Sim.PlaceCarried(p, 2, 3);
                var w = s.Worker.Worker;
                w.X = 2f; w.Y = 4f; w.Facing = Direction.South;
                yield return null;
                StringAssert.Contains(HudController.FragileTag, boot.StatusBars.TextAt(2, 3));
                StringAssert.Contains("스티커 없음", boot.StatusBars.TextAt(2, 3));
                boot.Input.Interact();
                yield return null;
                var card = boot.Panels.Waybill;
                Assert.IsTrue(card.IsOpen);
                Assert.IsTrue(card.NeedsSticker);
                Assert.IsTrue(card.Sticker.gameObject.activeSelf, "second sheet: the fragile sticker");
                Assert.IsTrue(card.StickerZone.gameObject.activeSelf, "side band cell");
                Assert.IsTrue(card.Card.Find("BoxTop/Side").gameObject.activeSelf);
                Assert.AreEqual(WaybillCard.FragileHintText, card.Card.Find("Hint").GetComponent<Text>().text);

                // 스티커를 운송장 영역에 놓으면 제자리로
                DragTo(card.Sticker.gameObject, card.Sticker, ScreenOf(card.Zone));
                Assert.IsFalse(card.StickerDone);
                Assert.IsFalse(p.FragileSticker);
                yield return new WaitForSecondsRealtime(WaybillCard.SnapSeconds + 0.1f);
                Assert.AreEqual(WaybillCard.StickerHome.x, card.Sticker.anchoredPosition.x, 0.5f);

                // 끄는 중 두 번째 포인터: 시작·이동·놓기 모두 무시
                var first = Pointer(ScreenOf(card.Paper));
                first.pointerId = 1;
                ExecuteEvents.Execute(card.Paper.gameObject, first, ExecuteEvents.beginDragHandler);
                var second = Pointer(ScreenOf(card.Sticker));
                second.pointerId = 2;
                ExecuteEvents.Execute(card.Sticker.gameObject, second, ExecuteEvents.beginDragHandler);
                second.position = ScreenOf(card.StickerZone);
                ExecuteEvents.Execute(card.Sticker.gameObject, second, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(card.Sticker.gameObject, second, ExecuteEvents.endDragHandler);
                Assert.IsTrue(card.IsDragging, "the first drag goes on");
                Assert.IsFalse(card.StickerDone);
                Assert.AreEqual(WaybillCard.StickerHome.x, card.Sticker.anchoredPosition.x, 0.5f, "the second pointer did not move the sticker");
                ExecuteEvents.Execute(card.Paper.gameObject, first, ExecuteEvents.endDragHandler); // 제자리에 놓아 되돌린다
                Assert.IsFalse(card.IsDragging);
                Assert.IsFalse(card.WaybillDone);
                yield return new WaitForSecondsRealtime(WaybillCard.SnapSeconds + 0.1f);

                // 운송장 먼저: 붙지만 카드는 열려 있다
                DragTo(card.Paper.gameObject, card.Paper, ScreenOf(card.Zone));
                Assert.IsTrue(card.WaybillDone);
                Assert.IsTrue(p.Waybill);
                Assert.IsFalse(card.Attached, "still needs the sticker");
                yield return new WaitForSecondsRealtime(WaybillCard.CloseDelay + 0.1f);
                Assert.IsTrue(card.IsOpen, "card stays open until both are attached");
                Assert.AreSame(p, s.Worker.PendingLabel);
                Assert.IsTrue(s.Sim.IsAwaitingLabel(p));

                // 스티커를 옆면 칸에
                DragTo(card.Sticker.gameObject, card.Sticker, ScreenOf(card.StickerZone));
                Assert.IsTrue(card.StickerDone);
                Assert.IsTrue(p.FragileSticker);
                Assert.IsTrue(card.Attached);
                yield return new WaitForSecondsRealtime(WaybillCard.SnapSeconds + 0.05f);
                Assert.AreEqual(card.StickerZone.anchoredPosition.x, card.Sticker.anchoredPosition.x, 0.5f, "sticker snaps into the side cell");
                Assert.AreEqual(WaybillCard.StickerZoneSize / WaybillCard.StickerSize, card.Sticker.localScale.x, 0.01f);
                yield return new WaitForSecondsRealtime(WaybillCard.CloseDelay);
                Assert.IsFalse(card.IsOpen);
                Assert.AreEqual(HudController.WaybillAttachedToast, boot.Hud.LastToast);
                Assert.IsFalse(s.Sim.IsAwaitingLabel(p));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WaybillCard_KeyboardAttachesBoth_AndOnlyStickerWhenWaybillIsOn()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(200);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Labeler, Direction.East));
                var w = s.Worker.Worker;
                w.X = 2f; w.Y = 4f; w.Facing = Direction.South;
                var p = Fragile(s, false);
                s.Sim.PlaceCarried(p, 2, 3);
                yield return null;
                boot.Input.Interact();
                yield return null;
                var card = boot.Panels.Waybill;
                card.AttachNow();
                Assert.IsTrue(p.Waybill && p.FragileSticker, "Enter attaches whatever is left");
                yield return new WaitForSecondsRealtime(WaybillCard.CloseDelay + 0.1f);
                Assert.IsFalse(card.IsOpen);

                s.Sim.Take(p);
                var q = Fragile(s, true);
                q.Waybill = true; // 운송장은 이미 붙었고 스티커만 남음
                s.Sim.PlaceCarried(q, 2, 3);
                yield return null;
                StringAssert.EndsWith(HudController.StickerPrompt, boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>().text);
                boot.Input.Interact();
                yield return null;
                Assert.IsTrue(card.IsOpen);
                Assert.IsTrue(card.WaybillDone, "an existing waybill shows already attached");
                Assert.AreEqual(card.Zone.anchoredPosition.x, card.Paper.anchoredPosition.x, 0.5f);
                Assert.IsFalse(card.StickerDone);
                DragTo(card.Sticker.gameObject, card.Sticker, ScreenOf(card.StickerZone));
                Assert.IsTrue(q.FragileSticker);
                Assert.IsTrue(card.Attached);
                yield return new WaitForSecondsRealtime(WaybillCard.CloseDelay + 0.1f);
                Assert.IsFalse(card.IsOpen);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        private static Package Good(GameSession s)
        {
            var p = new PackageFactory(new System.Random(5), 0.0).Create(s.Sim.NextId++, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            return p;
        }

        [UnityTest]
        public IEnumerator Trucks_PillCountsUp_DepartsAndReturns_DockRefusesWhileAway()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                var trucks = boot.Trucks;
                yield return null;
                Assert.AreEqual("0/" + TruckFleet.TruckCapacity, trucks.TextOf(CellType.Outlet));
                Assert.AreEqual("0/30", trucks.TextOf(CellType.ReturnDock));
                Assert.IsNull(trucks.TextOf(CellType.AirOutlet), "no air truck before Plot2");
                Assert.AreEqual(0f, trucks.Progress(CellType.Outlet));
                for (int i = 0; i < 12; i++) s.Sim.DeliverCarried(Good(s), CellType.Outlet);
                yield return null;
                Assert.AreEqual("12/30", trucks.TextOf(CellType.Outlet));
                var low = trucks.ColorOf(CellType.Outlet);
                for (int i = 0; i < 15; i++) s.Sim.DeliverCarried(Good(s), CellType.Outlet);
                yield return null;
                Assert.AreEqual("27/30", trucks.TextOf(CellType.Outlet));
                var high = trucks.ColorOf(CellType.Outlet);
                Assert.Less(Vector4.Distance(high, UiKit.Accent), Vector4.Distance(low, UiKit.Accent), "closer to 30 = more amber");

                var car = boot.World.TruckOf(CellType.Outlet);
                for (int i = 0; i < 3; i++) s.Sim.DeliverCarried(Good(s), CellType.Outlet);
                Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.Outlet));
                Assert.AreEqual("출고 트럭 출발 · 다음 차 0:20", boot.Hud.LastToast, "departure toast (spec 17.4)");
                yield return null;
                yield return new WaitForSeconds(0.3f);
                Assert.AreEqual(0f, trucks.Progress(CellType.Outlet), "the drive waits while the game is paused");
                Assert.IsTrue(car.gameObject.activeSelf);

                // 떠난 도크에 손으로 넣기 → 거부
                var w = s.Worker.Worker;
                var held = Good(s);
                w.Carrying = held;
                w.X = 10f; w.Y = 2f; w.Facing = Direction.East;
                yield return null;
                var prompt = boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>();
                Assert.AreEqual("트럭 대기 중 · 다음 차 0:20", prompt.text);
                Assert.AreEqual(UiKit.Bad, prompt.color);
                boot.Input.Interact();
                Assert.AreSame(held, w.Carrying);
                Assert.AreEqual("트럭 대기 중 · 다음 차 0:20", boot.Hud.LastToast);

                s.Paused = false;
                yield return new WaitForSeconds(TruckViews.DriveSeconds * 0.5f);
                Assert.IsTrue(trucks.IsDriving(CellType.Outlet), "drives off over 1.5 s");
                Assert.Greater(trucks.Offset(CellType.Outlet), 0.1f, "moves east, out of the wall");
                StringAssert.StartsWith("다음 차 0:", trucks.TextOf(CellType.Outlet));
                yield return new WaitForSeconds(TruckViews.DriveSeconds * 0.5f + 0.2f);
                Assert.AreEqual(1f, trucks.Progress(CellType.Outlet));
                Assert.IsFalse(car.gameObject.activeSelf, "gone while away");
                Assert.AreEqual("0/30", trucks.TextOf(CellType.ReturnDock), "the other trucks stay");

                s.Sim.Trucks.Advance(TruckFleet.TruckReturnMinutes);
                yield return null;
                yield return new WaitForSeconds(TruckViews.DriveSeconds * 0.5f);
                Assert.IsTrue(car.gameObject.activeSelf, "new truck drives in");
                Assert.IsTrue(trucks.IsDriving(CellType.Outlet));
                yield return new WaitForSeconds(TruckViews.DriveSeconds * 0.5f + 0.2f);
                Assert.AreEqual(0f, trucks.Progress(CellType.Outlet));
                Assert.AreEqual(0f, trucks.Offset(CellType.Outlet), 1e-3f, "parks at home");
                yield return null;
                StringAssert.StartsWith("출고 +30 · 0/30", prompt.text);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Trucks_SessionReplace_SnapsToTheLoadedState()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                boot.Session.Paused = true;
                yield return null;
                var car = boot.World.TruckOf(CellType.Outlet);
                Assert.AreEqual(0f, boot.Trucks.Progress(CellType.Outlet));
                var loaded = new GameSession(new Economy(100)) { Paused = true };
                loaded.Sim.Trucks.Restore(CellType.Outlet, 0, 12f);
                boot.ReplaceSession(loaded);
                yield return null;
                Assert.AreEqual(1f, boot.Trucks.Progress(CellType.Outlet), "an away truck is out at once, no drive");
                Assert.IsFalse(car.gameObject.activeSelf);
                Assert.AreEqual("다음 차 0:12", boot.Trucks.TextOf(CellType.Outlet));
                boot.ReplaceSession(new GameSession(new Economy(100)) { Paused = true });
                yield return null;
                Assert.AreEqual(0f, boot.Trucks.Progress(CellType.Outlet), "a present truck is parked at once");
                Assert.IsTrue(car.gameObject.activeSelf);
                Assert.AreEqual(0f, boot.Trucks.Offset(CellType.Outlet), 1e-3f);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Trucks_AirTrucksGetPills_SummaryCountsDepartures()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Unlocks.Grant(UnlockId.Plot2);
                yield return null;
                yield return null;
                Assert.AreEqual("0/30", boot.Trucks.TextOf(CellType.AirOutlet));
                Assert.AreEqual("0/30", boot.Trucks.TextOf(CellType.AirReturnDock));
                var air = Good(s);
                air.Air = true;
                s.Sim.DeliverCarried(air, CellType.AirOutlet);
                s.Sim.DeliverCarried(Good(s), CellType.Outlet);
                s.Clock.Set(1, 17 * 60 + 59.9f);
                s.Paused = false;
                float until = Time.realtimeSinceStartup + 3f;
                while (s.Clock.IsOpen && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                Assert.IsFalse(s.Clock.IsOpen);
                var row = boot.Hud.Root.Find("DaySummary/Card/Trucks");
                Assert.IsNotNull(row, "summary row");
                Assert.AreEqual("2대", row.GetComponent<Text>().text);
                Assert.AreEqual("트럭 2대 출발", boot.Hud.LastToast, "one toast for the 18:00 departures");
                Assert.IsTrue(s.Sim.Trucks.IsAway(CellType.AirOutlet));
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }
    }
}
