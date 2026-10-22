using System.Collections;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>스펙 21(P2·P3): 12:00 자동 열림, 다른 카드 뒤 대기, 나중에 → 점심 칩 → 다시 열기, 확정 → 돈·메뉴 배지·계약 칩, 13:00 칩 사라짐,
    /// 키보드(←/→·↑/↓·Enter·Esc) 조작, 입력 차단, 계약 달성 토스트·초록 칩, 하루 요약 계약 줄.</summary>
    public class LunchCardTests
    {
        /// <summary>11:59:30에서 1초 흘려 12:00을 넘긴다(LunchStarted).</summary>
        private static void CrossNoon(GameSession s)
        {
            s.Clock.Set(s.Clock.Day, Clock.LunchStart - 0.5f);
            s.Tick(1f);
            Assert.AreEqual(DayPhase.Lunch, s.Clock.Phase);
        }

        private static Package Good(int id)
        {
            var p = new PackageFactory(new System.Random(id), 0.0).Create(id, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            return p;
        }

        private static GameObject Chip(GameBootstrap boot, string name) => boot.Hud.Root.Find(name).gameObject;

        [UnityTest]
        public IEnumerator Noon_OpensTheCard_LaterChipReopens_ConfirmPaysAndShowsBadgeAndChip()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 시계는 테스트가 직접 흘린다
                s.Economy.Earn(400);
                var card = boot.Panels.Lunch;
                Assert.IsFalse(card.IsOpen);
                Assert.IsFalse(Chip(boot, "LunchChip").activeSelf, "no chip before noon");

                s.Paused = false;
                CrossNoon(s);
                s.Paused = true;
                yield return null;
                yield return null;
                Assert.IsTrue(card.IsOpen, "the card opens at 12:00");
                Assert.IsTrue(boot.Panels.AnyOpen);
                Assert.IsTrue(boot.Input.UiBlocksInput, "the card blocks world input");
                Assert.AreEqual(3, s.Company.ContractCandidates.Count);
                for (int i = 0; i < 3; i++)
                    Assert.AreEqual(s.Company.ContractCandidates[i].Title, card.ContractRow(i).Find("Name").GetComponent<Text>().text);
                Assert.AreEqual("국밥", card.MenuRow(0).Find("Name").GetComponent<Text>().text);
                Assert.AreEqual("₩40", card.MenuRow(0).Find("Price").GetComponent<Text>().text);
                Assert.AreEqual("무료", card.MenuRow(3).Find("Price").GetComponent<Text>().text);
                Assert.IsFalse(card.ConfirmButton.interactable, "nothing chosen yet");

                // 나중에 → 닫힘, 점심 칩(앰버)이 보인다 → 칩을 누르면 다시 열린다.
                card.LaterButton.onClick.Invoke();
                yield return null;
                Assert.IsFalse(card.IsOpen);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                Assert.IsTrue(Chip(boot, "LunchChip").activeSelf, "lunch chip after 나중에");
                Assert.AreEqual(HudController.LunchChipText, boot.Hud.Root.Find("LunchChip/Label").GetComponent<Text>().text);
                boot.Hud.Root.Find("LunchChip").GetComponent<Button>().onClick.Invoke();
                yield return null;
                Assert.IsTrue(card.IsOpen, "the chip reopens the card");
                // Esc도 나중에와 같다.
                boot.Input.PressEscape();
                yield return null;
                Assert.IsFalse(card.IsOpen, "Esc = 나중에");
                Assert.IsTrue(boot.Panels.OpenLunch(true), "L / chip");
                yield return null;

                // 메뉴(국밥) + 계약(첫 후보) 확정 → 돈 −40, 배지 "국밥", 계약 칩.
                card.Click(0, 0);
                card.Click(1, 0);
                Assert.AreEqual(LunchMenu.Gukbap, card.SelectedMenu);
                Assert.AreEqual(0, card.SelectedContract);
                Assert.IsTrue(card.ConfirmButton.interactable);
                StringAssert.Contains("₩40", card.ConfirmButton.GetComponent<UiButton>().Label.text);
                int money = s.Economy.Money;
                card.ConfirmButton.onClick.Invoke();
                yield return null;
                Assert.IsFalse(card.IsOpen, "confirm closes the card");
                Assert.AreEqual(money - 40, s.Economy.Money);
                Assert.AreEqual(LunchMenu.Gukbap, s.Company.Menu);
                Assert.IsNotNull(s.Company.Contract);
                Assert.IsFalse(Chip(boot, "LunchChip").activeSelf, "no lunch chip after confirming");
                Assert.IsTrue(Chip(boot, "MenuBadge").activeSelf, "menu badge next to the stamina pill");
                Assert.AreEqual("국밥", boot.Hud.Root.Find("MenuBadge/Text").GetComponent<Text>().text);
                Assert.IsTrue(Chip(boot, "ContractChip").activeSelf, "contract chip");
                Assert.AreEqual(s.Company.Contract.ChipText, boot.Hud.Root.Find("ContractChip/Text").GetComponent<Text>().text);
                StringAssert.StartsWith("계약 · ", s.Company.Contract.ChipText);
                Assert.IsFalse(boot.Panels.OpenLunch(true), "the card is done for today");
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Noon_WaitsBehindAnotherCard_AndTheChipGoesAwayAtOne()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                boot.Input.RequestShop();
                yield return null;
                Assert.IsTrue(boot.Panels.ShopIsOnlyOpen);
                CrossNoon(s);
                s.Paused = true;
                yield return null;
                yield return null;
                Assert.IsFalse(boot.Panels.LunchOpen, "the shop is open: the lunch card waits");
                Assert.IsTrue(boot.Panels.LunchPending);
                Assert.IsTrue(boot.Panels.ShopIsOnlyOpen, "the shop stays");
                boot.Panels.CloseAll();
                yield return null;
                yield return null;
                Assert.IsTrue(boot.Panels.LunchOpen, "opens after the shop closes");
                Assert.IsFalse(boot.Panels.LunchPending);

                // 13:00: 카드가 닫히고 칩도 사라진다(그날은 메뉴도 계약도 없다).
                s.Paused = false;
                s.Clock.Set(1, Clock.LunchEnd - 0.5f);
                s.Tick(1f);
                s.Paused = true;
                yield return null;
                yield return null;
                Assert.AreEqual(DayPhase.Working, s.Clock.Phase);
                Assert.IsFalse(boot.Panels.LunchOpen, "closed at 13:00");
                Assert.IsFalse(Chip(boot, "LunchChip").activeSelf, "chip gone at 13:00");
                Assert.IsFalse(Chip(boot, "ContractChip").activeSelf);
                Assert.IsFalse(Chip(boot, "MenuBadge").activeSelf);
                Assert.AreEqual(LunchCard.LunchOverToast, boot.Hud.LastToast, "toast when the card closes at 13:00");
                Assert.IsFalse(boot.Panels.OpenLunch(true));
                Assert.AreEqual("점심 카드는 12:00~13:00에만 열 수 있습니다", boot.Hud.LastToast);
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Keyboard_ArrowsEnterEsc_AndUnaffordableMenusAreDimmed()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Economy.TrySpend(s.Economy.Money - 45); // ₩45: 국밥(40)만 살 수 있다(제육 60·샐러드 50은 흐림)
                CrossNoon(s);
                s.Paused = true;
                yield return null;
                yield return null;
                var card = boot.Panels.Lunch;
                Assert.IsTrue(card.IsOpen);
                Assert.IsTrue(card.MenuEnabled(0));
                Assert.IsFalse(card.MenuEnabled(1), "₩60 > ₩45");
                Assert.IsFalse(card.MenuEnabled(2), "₩50 > ₩45");
                Assert.IsTrue(card.MenuEnabled(3), "free");
                Assert.Less(card.MenuRow(1).GetComponent<CanvasGroup>().alpha, 1f, "dimmed");
                Assert.AreEqual(0, card.FocusIndex, "the cursor starts on the first menu");
                Assert.IsTrue(card.MenuRow(0).Find("Focus").gameObject.activeSelf);
                card.Click(0, 1);
                Assert.IsNull(card.SelectedMenu, "cannot choose an unaffordable menu");

                // 클릭한 제육볶음(흐림)에 커서가 있다. ↓ → 샐러드(흐림) — Enter는 무시. ↓ → 그냥 쉬기, Enter → 고르고 계약 열로.
                Assert.AreEqual(LunchCard.Area.Menu, card.FocusArea);
                Assert.AreEqual(1, card.FocusIndex, "a click moves the cursor");
                Assert.IsTrue(card.MenuRow(1).Find("Focus").gameObject.activeSelf, "keyboard ring");
                Assert.IsFalse(card.MenuRow(0).Find("Focus").gameObject.activeSelf);
                card.MoveRow(1);
                card.Submit();
                Assert.IsNull(card.SelectedMenu);
                card.MoveRow(1);
                card.Submit();
                Assert.AreEqual(LunchMenu.Rest, card.SelectedMenu);
                Assert.AreEqual(LunchCard.Area.Contract, card.FocusArea, "Enter on a menu moves to the contract column");
                // → 계약 열에서 ↓ → 두 번째 후보, Enter → 고르고 확정 버튼으로.
                card.MoveRow(1);
                card.Submit();
                Assert.AreEqual(1, card.SelectedContract);
                Assert.AreEqual(LunchCard.Area.Buttons, card.FocusArea);
                Assert.AreEqual(1, card.FocusIndex, "the confirm button");
                Assert.IsTrue(card.ConfirmButton.transform.Find("Focus").gameObject.activeSelf);
                // ↑ → 계약 열 맨 아래, ← → 메뉴 열, Enter로 그냥 쉬기 풀기 → 다시 고르기.
                card.MoveRow(-1);
                Assert.AreEqual(LunchCard.Area.Contract, card.FocusArea);
                card.MoveColumn(-1);
                Assert.AreEqual(LunchCard.Area.Menu, card.FocusArea);
                Assert.AreEqual(2, card.FocusIndex);
                card.MoveRow(1);
                card.Submit();
                Assert.IsNull(card.SelectedMenu, "Enter on the chosen menu unchooses it");
                card.Submit();
                Assert.AreEqual(LunchMenu.Rest, card.SelectedMenu);
                // 확정 버튼까지 가서 Enter.
                card.MoveColumn(1); card.MoveRow(1); card.MoveRow(1); card.MoveRow(1);
                Assert.AreEqual(LunchCard.Area.Buttons, card.FocusArea);
                card.MoveColumn(-1);
                Assert.AreEqual(0, card.FocusIndex, "← on the button row = 나중에");
                card.MoveColumn(1);
                int money = s.Economy.Money;
                card.Submit();
                yield return null;
                Assert.IsFalse(card.IsOpen);
                Assert.AreEqual(money, s.Economy.Money, "그냥 쉬기 is free");
                Assert.AreEqual(LunchMenu.Rest, s.Company.Menu);
                Assert.AreEqual(s.Company.ContractCandidates[1].Kind, s.Company.Contract.Kind);
                Assert.IsFalse(Chip(boot, "MenuBadge").activeSelf, "no badge for 그냥 쉬기");
                Assert.IsFalse(boot.Input.UiBlocksInput, "input unblocked after confirming");
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NegativeBalance_FreeChoicesStillConfirm_AndSessionReplaceClosesTheCard()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Economy.Adjust(-s.Economy.Money - 40); // −₩40
                CrossNoon(s);
                s.Paused = true;
                yield return null;
                yield return null;
                var card = boot.Panels.Lunch;
                Assert.IsTrue(card.IsOpen);
                Assert.IsFalse(card.MenuEnabled(0), "국밥 needs money");
                Assert.IsTrue(card.MenuEnabled(3), "그냥 쉬기 is free even below zero");
                card.Click(1, 0);
                card.Confirm();
                yield return null;
                Assert.IsFalse(card.IsOpen, "a contract alone confirms at −₩40");
                Assert.AreEqual(-40, s.Economy.Money);
                Assert.IsNotNull(s.Company.Contract);

                // 새 세션(점심시간)으로 바꾸면 열린 카드는 닫힌다.
                var next = new GameSession(new Economy(), null, new System.Random(2));
                next.Clock.Set(1, Clock.LunchStart + 5f);
                next.Paused = true;
                Assert.IsTrue(boot.Panels.OpenLunch(true) == false, "already confirmed on the old session");
                boot.ReplaceSession(next);
                yield return null;
                Assert.IsTrue(boot.Panels.OpenLunch(true), "the new session's card");
                boot.ReplaceSession(new GameSession(new Economy(), null, new System.Random(3)) { Paused = true });
                Assert.IsFalse(boot.Panels.LunchOpen, "closed on session replace");
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContractAchieved_ToastsAndTurnsTheChipGreen_AndTheSummaryShowsIt()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                CrossNoon(s);
                s.Paused = true;
                yield return null;
                yield return null;
                var card = boot.Panels.Lunch;
                Assert.IsTrue(card.IsOpen);
                int idx = -1;
                for (int i = 0; i < s.Company.ContractCandidates.Count; i++) if (s.Company.ContractCandidates[i].Kind == ContractKind.Deliver) idx = i;
                Assert.GreaterOrEqual(idx, 0, "a new company always has 정상 출고 among its three candidates");
                card.Click(1, idx);
                card.Confirm();
                yield return null;
                var c = s.Company.Contract;
                for (int i = 0; i < c.Target; i++) s.Sim.DeliverCarried(Good(1000 + i), CellType.Outlet);
                yield return null;
                Assert.IsTrue(c.Achieved);
                Assert.AreEqual(c.AchievedToast, boot.Hud.LastToast);
                s.Sim.DeliverCarried(Good(2000), CellType.Outlet);
                Assert.AreEqual(c.AchievedToast, boot.Hud.LastToast, "a good delivery toast does not cover the achievement");
                var unchecked_ = Good(2001); unchecked_.Inspected = false;
                s.Sim.DeliverCarried(unchecked_, CellType.Outlet);
                StringAssert.Contains("검사대를 거치지 않은", boot.Hud.LastToast, "penalty toasts are never held back");
                Assert.AreEqual(UiKit.Good, boot.Hud.Root.Find("ContractChip").GetComponent<Image>().color, "green chip");

                s.Paused = false;
                s.Clock.Set(1, Clock.CloseMinute - 0.5f);
                s.Tick(1f);
                yield return null;
                Assert.IsTrue(boot.Panels.SummaryOpen);
                var line = boot.Hud.Root.Find("DaySummary/Card/Contract").GetComponent<Text>();
                Assert.IsTrue(line.gameObject.activeSelf);
                Assert.AreEqual("오후 계약: " + c.Title + " — 달성 +₩200", line.text);
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }
    }
}
