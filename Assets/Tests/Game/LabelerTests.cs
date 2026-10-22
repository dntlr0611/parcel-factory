using System.Collections;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>스펙 15: 운송장 부착대에서 멈춤 → 카드 → 드래그(영역 밖 복귀 / 안 부착), 키보드 대체, Esc, 운송장 없음 판정, 3D 운송장.</summary>
    public class LabelerTests
    {
        /// <summary>검사한 택배 하나를 부착대 (2,3)(동향) 위에 멈춰 두고, 작업자는 (2,4)에서 남쪽(부착대)을 본다.</summary>
        private static Package Setup(GameBootstrap boot, bool rejected = false, bool air = false)
        {
            var s = boot.Session;
            s.Paused = true;
            s.Economy.Earn(200);
            if (s.Grid.Get(2, 3).Type != CellType.Labeler)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Labeler, Direction.East));
            var p = new PackageFactory(new System.Random(11), 0.0).Create(s.Sim.NextId++, 0, 0, Direction.East);
            p.Inspect(rejected);
            p.Air = air;
            s.Sim.PlaceCarried(p, 2, 3);
            s.Sim.Tick(1f);
            Assert.IsTrue(s.Sim.IsAwaitingLabel(p));
            var w = s.Worker.Worker;
            w.X = 2f; w.Y = 4f; w.Facing = Direction.South;
            return p;
        }

        private static PointerEventData Pointer(Vector2 screen) =>
            new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };

        private static Vector2 ScreenOf(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.position);

        [UnityTest]
        public IEnumerator Labeler_CardDrag_SnapsBackOutside_AttachesInside()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var p = Setup(boot);
                yield return null;
                var labelChip = boot.Hud.Root.Find("Chips/LabelWaiting");
                Assert.IsTrue(labelChip.gameObject.activeSelf, "운송장 대기 chip hidden");
                Assert.AreEqual("1", labelChip.Find("Value").GetComponent<Text>().text);
                Assert.AreEqual(HudController.LabelPrompt, boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>().text);
                StringAssert.StartsWith("부착대 · 운송장 없음", boot.StatusBars.TextAt(2, 3)); // 바라보는 부품이라 " · R 설정"이 붙는다
                boot.Input.Interact();
                yield return null;
                var card = boot.Panels.Waybill;
                Assert.IsTrue(card.IsOpen, "waybill card did not open");
                Assert.IsTrue(boot.Input.UiBlocksInput, "card should block world input");
                Assert.AreSame(p, card.Package);
                Assert.AreEqual(WaybillCard.WaybillTitle, card.Paper.Find("Band/Kind").GetComponent<Text>().text);
                Assert.AreEqual(MaterialLibrary.WaybillInk, card.Paper.Find("Band").GetComponent<Image>().color, "single ink band (spec 15.4)");
                Assert.IsFalse(card.Paper.Find("Band/AirBadge").gameObject.activeSelf);

                // 영역 밖에 놓으면 제자리로 돌아간다
                var paper = card.Paper.gameObject;
                var start = ScreenOf(card.Paper);
                var e = Pointer(start);
                ExecuteEvents.Execute(paper, e, ExecuteEvents.beginDragHandler);
                Assert.IsTrue(card.IsDragging);
                Assert.AreEqual(WaybillCard.DragScale, card.Paper.localScale.x, 1e-4f);
                e.position = start + new Vector2(0f, 140f);
                ExecuteEvents.Execute(paper, e, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(paper, e, ExecuteEvents.endDragHandler);
                Assert.IsFalse(card.Attached);
                yield return new WaitForSecondsRealtime(WaybillCard.SnapSeconds + 0.1f);
                Assert.AreEqual(WaybillCard.PaperHome.x, card.Paper.anchoredPosition.x, 0.5f);
                Assert.AreEqual(WaybillCard.PaperHome.y, card.Paper.anchoredPosition.y, 0.5f);
                Assert.IsFalse(p.Waybill);

                // 영역 안에 놓으면 붙고 0.4초 뒤 닫힌다
                start = ScreenOf(card.Paper);
                e = Pointer(start);
                ExecuteEvents.Execute(paper, e, ExecuteEvents.beginDragHandler);
                e.position = ScreenOf(card.Zone) + new Vector2(6f, -4f);
                ExecuteEvents.Execute(paper, e, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(paper, e, ExecuteEvents.endDragHandler);
                Assert.IsTrue(card.Attached);
                Assert.IsTrue(p.Waybill);
                yield return new WaitForSecondsRealtime(WaybillCard.SnapSeconds + 0.05f);
                Assert.AreEqual(card.Zone.anchoredPosition.x, card.Paper.anchoredPosition.x, 0.5f, "paper should snap to the zone centre");
                yield return new WaitForSecondsRealtime(WaybillCard.CloseDelay);
                Assert.IsFalse(card.IsOpen, "card should close 0.4 s after attaching");
                Assert.AreEqual(HudController.WaybillAttachedToast, boot.Hud.LastToast);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                yield return null;
                StringAssert.StartsWith("부착대 · 운송장 ✓", boot.StatusBars.TextAt(2, 3));
                Assert.IsFalse(labelChip.gameObject.activeSelf, "chip should hide at 0");
                var box = boot.GetComponent<PackageViews>().ViewOf(p.Id);
                Assert.IsTrue(box.transform.Find("Waybill").gameObject.activeSelf, "3D waybill missing");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Labeler_KeyboardFallback_EscCloses_SameWaybillForRejectedAir()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var p = Setup(boot, rejected: true, air: true);
                boot.Input.Interact();
                yield return null;
                var card = boot.Panels.Waybill;
                Assert.IsTrue(card.IsOpen);
                Assert.AreEqual(WaybillCard.WaybillTitle, card.Paper.Find("Band/Kind").GetComponent<Text>().text); // 반품도 같은 운송장(스펙 15.4)
                Assert.AreEqual(MaterialLibrary.WaybillInk, card.Paper.Find("Band").GetComponent<Image>().color);
                Assert.IsTrue(card.Paper.Find("Band/AirBadge").gameObject.activeSelf, "air badge hidden");
                Assert.IsTrue(card.Card.Find("BoxTop/AirMark").gameObject.activeSelf, "air mark on box hidden");

                // Esc: 붙이지 않고 닫는다. 택배는 부착대에 남는다.
                boot.Input.PressEscape();
                yield return null;
                Assert.IsFalse(card.IsOpen);
                Assert.IsFalse(p.Waybill);
                Assert.IsTrue(boot.Session.Sim.IsAwaitingLabel(p));

                // 키보드 대체(Enter·E와 같은 경로)
                boot.Input.Interact();
                yield return null;
                Assert.IsTrue(card.IsOpen);
                card.AttachNow();
                Assert.IsTrue(p.Waybill);
                yield return new WaitForSecondsRealtime(WaybillCard.CloseDelay + 0.1f);
                Assert.IsFalse(card.IsOpen);
                var box = boot.GetComponent<PackageViews>().ViewOf(p.Id);
                var wb = box.transform.Find("Waybill");
                Assert.IsTrue(wb.gameObject.activeSelf);
                Assert.AreSame(MaterialLibrary.Waybill(), wb.GetComponent<Renderer>().sharedMaterial, "one waybill material for every parcel");
                var stamp = box.transform.Find("Stamp");
                Assert.IsTrue(stamp.gameObject.activeSelf, "rejected parcel shows the reject stamp");
                Assert.IsFalse(box.transform.Find("Check").gameObject.activeSelf);
                Assert.Greater(stamp.localPosition.y + stamp.localScale.y / 2f, wb.localPosition.y, "stamp stays visible above the waybill");
                var mark = box.transform.Find("AirMark");
                Assert.IsTrue(mark.gameObject.activeSelf);
                Assert.AreEqual(wb.localPosition.x, mark.localPosition.x, 0.001f, "air mark is centred on the box top");
                Assert.AreEqual(wb.localPosition.z, mark.localPosition.z, 0.001f, "air mark is centred on the box top");
                Assert.Greater(mark.localScale.x, wb.localScale.x * 1.5f, "air mark covers the whole top, larger than the waybill");
                Assert.Greater(wb.localPosition.y, mark.localPosition.y + 0.004f, "waybill sits above the air mark (no z-fighting)");

                // 하루 요약은 운송장 카드보다 위에 뜬다.
                var q = new PackageFactory(new System.Random(13), 0.0).Create(boot.Session.Sim.NextId++, 0, 0, Direction.East);
                q.Inspect(false);
                boot.Session.Sim.Take(p);
                boot.Session.Sim.PlaceCarried(q, 2, 3);
                boot.Session.Sim.Tick(1f);
                boot.Input.Interact();
                yield return null;
                Assert.IsTrue(card.IsOpen);
                boot.Session.Paused = false;
                boot.Session.Clock.Set(1, 1079.5f);
                boot.Session.Tick(1f);
                yield return null;
                var summary = boot.Hud.Root.Find("DaySummary");
                Assert.IsTrue(summary.gameObject.activeSelf, "day summary did not show");
                Assert.Greater(summary.GetSiblingIndex(), card.Root.GetSiblingIndex(), "summary must be above the waybill card");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoWaybill_Delivery_PromptsAndCosts_AndUninspectedCanBeLabelled()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                var f = new PackageFactory(new System.Random(12), 0.0);
                var p = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                p.Inspect(false);
                s.Worker.Worker.Carrying = p;
                s.Worker.Worker.X = 10f; s.Worker.Worker.Y = 2f; s.Worker.Worker.Facing = Direction.East;
                yield return null;
                var prompt = boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>();
                Assert.AreEqual("출고 −20 · 운송장 없음 · 0/30", prompt.text); // 트럭 적재 수(스펙 18)
                int money0 = s.Economy.Money;
                boot.Input.Interact();
                Assert.AreEqual(money0 - 20, s.Economy.Money);
                Assert.AreEqual("출고 −20 · 운송장 없음", boot.Hud.LastToast);

                var r = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                r.Inspect(true);
                s.Worker.Worker.Carrying = r;
                s.Worker.Worker.Y = 4f;
                yield return null;
                Assert.AreEqual("반품 −10 · 운송장 없음 · 0/30", prompt.text);
                boot.Input.Interact();
                Assert.AreEqual(money0 - 30, s.Economy.Money);

                // 미검사 택배도 부착대에 올릴 수 있고, 운송장을 기다린다(스펙 15.4)
                s.Economy.Earn(100);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Labeler, Direction.East));
                var u = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                s.Worker.Worker.Carrying = u;
                s.Worker.Worker.X = 2f; s.Worker.Worker.Y = 4f; s.Worker.Worker.Facing = Direction.South;
                yield return null;
                Assert.AreEqual("놓기", prompt.text);
                Assert.AreEqual("부착대" + StatusBars.FocusSuffix, boot.StatusBars.TextAt(2, 3));
                boot.Input.Interact();
                Assert.IsNull(s.Worker.Worker.Carrying);
                s.Sim.Tick(1f);
                Assert.IsTrue(s.Sim.IsAwaitingLabel(u));
                yield return null;
                Assert.AreEqual(HudController.LabelPrompt, prompt.text);
                StringAssert.StartsWith("부착대 · 운송장 없음", boot.StatusBars.TextAt(2, 3));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }
    }
}
