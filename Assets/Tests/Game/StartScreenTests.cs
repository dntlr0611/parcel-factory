using System.Collections;
using System.IO;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>시작 화면(스펙 12.1): 부팅 직후 표시·정지, 새 게임, 저장이 있을 때만 이어하기, 인게임 메뉴 → 계속하기.</summary>
    public class StartScreenTests
    {
        private string savePath;

        [SetUp]
        public void SetUp()
        {
            StartScreen.ShowOnBoot = true;
            savePath = Path.Combine(Path.GetTempPath(), "pf-start-" + System.Guid.NewGuid().ToString("N") + ".json");
            SaveFile.PathOverride = savePath;
        }

        [TearDown]
        public void TearDown()
        {
            StartScreen.ShowOnBoot = false;
            SaveFile.PathOverride = null;
            foreach (var p in new[] { savePath, savePath + ".tmp" }) if (File.Exists(p)) File.Delete(p);
        }

        /// <summary>이전 테스트(또는 자동 생성)의 부트스트랩을 치우고 새로 부팅해 Start까지 돌린다.</summary>
        private static IEnumerator FreshBoot(System.Action<GameBootstrap> got)
        {
            if (GameBootstrap.Instance != null) { Object.Destroy(GameBootstrap.Instance.gameObject); yield return null; }
            var boot = GameBootstrap.Ensure();
            yield return null;
            yield return null;
            got(boot);
        }

        [Test]
        public void ShowOnBoot_ProductionDefaultIsTrue()
        {
            Assert.IsTrue(PlayModeSetup.ProductionShowOnBoot, "the shipped build must open on the start screen");
        }

        [UnityTest]
        public IEnumerator ShownOnBoot_FreezesGame_NewGameStartsTicking()
        {
            GameBootstrap boot = null;
            yield return FreshBoot(b => boot = b);
            try
            {
                var start = boot.StartScreen;
                Assert.IsTrue(start.IsOpen, "start screen should show on boot");
                Assert.IsFalse(start.InGame);
                Assert.IsFalse(boot.Hud.Visible, "HUD should hide behind the start screen");
                Assert.IsTrue(boot.Input.UiBlocksInput, "world input should be blocked");
                Assert.IsTrue(boot.Session.Paused);
                Assert.IsFalse(start.ButtonFor(StartScreen.Item.Continue).gameObject.activeSelf, "no save → no 이어하기");
                Assert.IsFalse(start.ButtonFor(StartScreen.Item.Resume).gameObject.activeSelf);
                Assert.IsTrue(start.ButtonFor(StartScreen.Item.NewGame).gameObject.activeSelf);
                Assert.IsTrue(start.ButtonFor(StartScreen.Item.Controls).gameObject.activeSelf);
                Assert.AreEqual(StartScreen.QuitAvailable, start.ButtonFor(StartScreen.Item.Quit).gameObject.activeSelf);
                Assert.AreEqual(StartScreen.Item.NewGame, start.Selected, "first visible item is selected");
                Assert.AreEqual(BtnStyle.Primary, start.ButtonFor(StartScreen.Item.NewGame).GetComponent<UiButton>().Style, "first item is amber");
                Assert.AreEqual(BtnStyle.Normal, start.ButtonFor(StartScreen.Item.Controls).GetComponent<UiButton>().Style);

                int ticks = boot.Loop.Ticks;
                float minute = boot.Session.Clock.MinuteOfDay;
                yield return new WaitForSeconds(0.5f);
                Assert.AreEqual(ticks, boot.Loop.Ticks, "game loop must not tick behind the start screen");
                Assert.AreEqual(minute, boot.Session.Clock.MinuteOfDay, "clock must not run behind the start screen");

                // 조작법: ↓로 선택 이동 후 실행 → 조작 카드, 닫기로 돌아온다.
                start.MoveSelection(2); // 새 게임 → 설정 → 조작법
                Assert.AreEqual(StartScreen.Item.Controls, start.Selected);
                start.Activate();
                Assert.IsTrue(start.ControlsOpen);
                boot.transform.Find("StartScreen/Controls/Card/Close").GetComponent<Button>().onClick.Invoke();
                Assert.IsFalse(start.ControlsOpen);
                Assert.IsTrue(start.IsOpen);
                start.MoveSelection(-2);
                Assert.AreEqual(StartScreen.Item.NewGame, start.Selected);

                var old = boot.Session;
                start.Activate(); // 새 게임
                Assert.IsFalse(start.IsOpen);
                Assert.AreNotSame(old, boot.Session, "새 게임 should start a fresh session");
                Assert.IsTrue(boot.Hud.Visible);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                Assert.IsFalse(boot.Session.Paused);
                ticks = boot.Loop.Ticks;
                minute = boot.Session.Clock.MinuteOfDay;
                yield return new WaitForSeconds(0.4f);
                Assert.Greater(boot.Loop.Ticks, ticks, "game loop should tick after 새 게임");
                Assert.Greater(boot.Session.Clock.MinuteOfDay, minute, "clock should run after 새 게임");
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Continue_OnlyWithReadableSave_LoadsIt()
        {
            var saved = new GameSession();
            saved.Clock.Set(3, 10 * 60);
            saved.Economy.Earn(1140);
            saved.Paused = true; // 일시정지 상태로 저장된 세션이어도 이어하기는 바로 진행해야 한다
            Assert.IsTrue(SaveFile.TrySave(saved, out var err), err);
            int savedMoney = saved.Economy.Money;

            GameBootstrap boot = null;
            yield return FreshBoot(b => boot = b);
            try
            {
                var start = boot.StartScreen;
                Assert.IsTrue(start.IsOpen);
                var cont = start.ButtonFor(StartScreen.Item.Continue);
                Assert.IsTrue(cont.gameObject.activeSelf, "save present → 이어하기 shown");
                Assert.AreEqual(StartScreen.Item.Continue, start.Selected, "이어하기 is the first item");
                Assert.IsNotNull(start.SavedPreview);
                var sub = cont.transform.Find("Lines/Sub").GetComponent<Text>();
                Assert.AreEqual("3일차 · ₩" + savedMoney.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), sub.text);

                // 읽을 수 없는 저장이면 숨긴다.
                start.Hide();
                File.WriteAllText(savePath, "{ not json");
                start.Show(false);
                Assert.IsFalse(start.ButtonFor(StartScreen.Item.Continue).gameObject.activeSelf, "broken save → 이어하기 hidden");
                Assert.IsTrue(start.SaveUnreadable);
                Assert.IsTrue(boot.transform.Find("StartScreen/Column/SaveError").gameObject.activeSelf, "unreadable-save notice hidden");
                Assert.AreEqual(StartScreen.Item.NewGame, start.Selected);
                start.Hide();

                Assert.IsTrue(SaveFile.TrySave(saved, out err), err);
                start.Show(false);
                Assert.IsTrue(start.ButtonFor(StartScreen.Item.Continue).gameObject.activeSelf);
                start.Activate();
                Assert.IsFalse(start.IsOpen);
                Assert.AreEqual(3, boot.Session.Clock.Day, "이어하기 should load the saved day");
                Assert.AreEqual(savedMoney, boot.Session.Economy.Money);
                Assert.IsFalse(boot.Session.Paused, "이어하기 must resume unpaused");
                Assert.IsFalse(boot.Input.UiBlocksInput);
                int ticks = boot.Loop.Ticks;
                float minute = boot.Session.Clock.MinuteOfDay;
                yield return new WaitForSeconds(0.3f);
                Assert.Greater(boot.Session.Clock.MinuteOfDay, minute, "loaded session should run");
                Assert.Greater(boot.Loop.Ticks, ticks);
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MenuButton_ReopensWithResume_KeepsSessionAndPauseState()
        {
            GameBootstrap boot = null;
            yield return FreshBoot(b => boot = b);
            try
            {
                var start = boot.StartScreen;
                start.ButtonFor(StartScreen.Item.NewGame).onClick.Invoke();
                yield return null;
                var session = boot.Session;
                session.Economy.Earn(77);
                Assert.IsFalse(session.Paused);

                var menu = boot.Hud.Root.Find("TopRight/Menu").GetComponent<Button>();
                Assert.AreEqual("메뉴", menu.GetComponent<UiButton>().Label.text);
                menu.onClick.Invoke();
                Assert.IsTrue(start.IsOpen);
                Assert.IsTrue(start.InGame);
                Assert.AreEqual(StartScreen.Item.Resume, start.Selected, "계속하기 is the first item in-game");
                Assert.IsTrue(start.ButtonFor(StartScreen.Item.Resume).gameObject.activeSelf);
                Assert.IsFalse(start.ButtonFor(StartScreen.Item.Continue).gameObject.activeSelf);
                Assert.IsTrue(session.Paused, "menu pauses the running session");
                Assert.IsFalse(boot.Hud.Visible);
                int ticks = boot.Loop.Ticks;
                yield return new WaitForSeconds(0.3f);
                Assert.AreEqual(ticks, boot.Loop.Ticks);

                start.Activate(); // 계속하기
                Assert.IsFalse(start.IsOpen);
                Assert.AreSame(session, boot.Session, "계속하기 keeps the session");
                Assert.IsFalse(session.Paused, "계속하기 restores the previous pause state");
                Assert.IsTrue(boot.Hud.Visible);
                Assert.IsFalse(boot.Input.UiBlocksInput, "계속하기 must release world input");

                // 일시정지 중에 메뉴를 열었다 닫으면 일시정지는 그대로.
                session.Paused = true;
                menu.onClick.Invoke();
                Assert.IsTrue(start.IsOpen);
                start.ButtonFor(StartScreen.Item.Resume).onClick.Invoke();
                Assert.IsTrue(session.Paused);
                Assert.AreSame(session, boot.Session);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                session.Paused = false;

                // 인게임 새 게임은 두 번 눌러야 한다: 첫 누름은 확인 상태(위험 모양)만 켜고 세션을 바꾸지 않는다.
                menu.onClick.Invoke();
                var newGame = start.ButtonFor(StartScreen.Item.NewGame);
                var newUi = newGame.GetComponent<UiButton>();
                start.MoveSelection(1);
                Assert.AreEqual(StartScreen.Item.NewGame, start.Selected);
                start.Activate();
                Assert.IsTrue(start.IsOpen, "first press must not start a new game");
                Assert.AreSame(session, boot.Session, "first press must keep the session");
                Assert.AreEqual(StartScreen.Item.NewGame, start.Armed);
                Assert.AreEqual(BtnStyle.Danger, newUi.Style);
                Assert.AreEqual(StartScreen.ConfirmNewGameText, newUi.Label.text);
                Assert.AreEqual(StartScreen.ConfirmSubText, newGame.transform.Find("Lines/Sub").GetComponent<Text>().text);
                // 선택을 옮기면 풀린다.
                start.MoveSelection(1);
                Assert.IsNull(start.Armed);
                Assert.AreEqual("새 게임", newUi.Label.text);
                Assert.AreEqual(BtnStyle.Normal, newUi.Style);
                start.MoveSelection(-1);
                start.Activate();
                Assert.AreEqual(StartScreen.Item.NewGame, start.Armed);
                start.Activate(); // 두 번째 누름: 실행
                Assert.IsFalse(start.IsOpen);
                Assert.AreNotSame(session, boot.Session, "second press starts a new game");
                Assert.IsFalse(boot.Input.UiBlocksInput);
                Assert.AreEqual("새 게임", newUi.Label.text, "confirm state must reset after running");
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }
    }
}
