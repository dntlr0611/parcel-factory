using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ParcelFactory.Game.Tests
{
    /// <summary>환경설정(스펙 13): 저장 왕복·clamp·해상도 후보·볼륨 매핑·배경소리 배수·설정 카드 키보드·Esc·적용 버튼.</summary>
    public class SettingsTests
    {
        private Dictionary<string, int?> prefs;

        /// <summary>pf.* PlayerPrefs를 백업한다(없던 키는 null).</summary>
        public static Dictionary<string, int?> BackupPrefs()
        {
            var d = new Dictionary<string, int?>();
            foreach (var k in GameSettings.Keys) d[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
            return d;
        }

        /// <summary>백업한 pf.* 값으로 되돌린다(없던 키는 지운다).</summary>
        public static void RestorePrefs(Dictionary<string, int?> d)
        {
            foreach (var kv in d)
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value); else PlayerPrefs.DeleteKey(kv.Key);
            PlayerPrefs.Save();
        }

        [SetUp] public void SetUp() => prefs = BackupPrefs();

        [TearDown]
        public void TearDown()
        {
            RestorePrefs(prefs);
            GameSettings.MonitorOverride = null;
            GameSettings.SupportedOverride = null;
            StartScreen.ShowOnBoot = false;
        }

        [Test]
        public void Prefs_RoundTrip_AndClamp()
        {
            foreach (var k in GameSettings.Keys) PlayerPrefs.DeleteKey(k);
            var fresh = GameSettings.Load();
            Assert.AreEqual(GameSettings.DefaultMusic, fresh.Music);
            Assert.AreEqual(GameSettings.DefaultAmbient, fresh.Ambient);

            fresh.SetMusic(35);
            fresh.SetAmbient(150); // clamp → 100
            fresh.SetDisplay(true, new Vector2Int(1600, 900));
            Assert.AreEqual(100, fresh.Ambient);
            var again = GameSettings.Load();
            Assert.AreEqual(35, again.Music);
            Assert.AreEqual(100, again.Ambient);
            Assert.IsTrue(again.Fullscreen);
            Assert.AreEqual(new Vector2Int(1600, 900), again.Resolution);
            Assert.AreEqual(35, PlayerPrefs.GetInt(GameSettings.KeyMusic));

            again.SetMusic(-20);
            Assert.AreEqual(0, again.Music);
            PlayerPrefs.SetInt(GameSettings.KeyMusic, 999);
            PlayerPrefs.SetInt(GameSettings.KeyAmbient, -3);
            PlayerPrefs.SetInt(GameSettings.KeyResW, 5);
            var clamped = GameSettings.Load();
            Assert.AreEqual(100, clamped.Music, "stored values are clamped on load");
            Assert.AreEqual(0, clamped.Ambient);
            Assert.AreEqual(GameSettings.MinResolution, clamped.Resolution.x);
        }

        [Test]
        public void VolumeChanges_DoNotWriteDisplayKeys_AndLoadWithoutDisplayKeysUsesLiveScreen()
        {
            foreach (var k in GameSettings.Keys) PlayerPrefs.DeleteKey(k);
            var s = GameSettings.Load();
            Assert.IsFalse(s.DisplaySaved, "no pf.resW → display counts as unset");
            Assert.AreEqual(new Vector2Int(Screen.width, Screen.height), s.Resolution, "unset display takes the live screen size");
            Assert.AreEqual(Screen.fullScreen, s.Fullscreen);
            s.SetMusic(30);
            s.SetAmbient(55, false);
            Assert.AreEqual(30, PlayerPrefs.GetInt(GameSettings.KeyMusic));
            Assert.AreEqual(55, PlayerPrefs.GetInt(GameSettings.KeyAmbient));
            foreach (var k in new[] { GameSettings.KeyFullscreen, GameSettings.KeyResW, GameSettings.KeyResH })
                Assert.IsFalse(PlayerPrefs.HasKey(k), k + " must not be written by a volume change");
            Assert.IsFalse(GameSettings.Load().DisplaySaved);
        }

        [UnityTest]
        public IEnumerator Boot_WithoutDisplayKeys_DoesNotApplyDisplay()
        {
            foreach (var k in GameSettings.Keys) PlayerPrefs.DeleteKey(k);
            GameBootstrap boot = null;
            int before = GameSettings.ApplyRequests;
            yield return FreshBoot(b => boot = b);
            try
            {
                Assert.AreEqual(before, GameSettings.ApplyRequests, "boot must not touch the display when nothing was saved");
                Assert.IsFalse(boot.Settings.DisplaySaved);
                Assert.AreEqual(new Vector2Int(Screen.width, Screen.height), boot.Settings.Resolution);
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
            PlayerPrefs.SetInt(GameSettings.KeyResW, 1600); PlayerPrefs.SetInt(GameSettings.KeyResH, 900); PlayerPrefs.SetInt(GameSettings.KeyFullscreen, 0);
            before = GameSettings.ApplyRequests;
            yield return FreshBoot(b => boot = b);
            try { Assert.AreEqual(before + 1, GameSettings.ApplyRequests, "saved display values are applied at boot"); }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [Test]
        public void Monitor_UsesNativeDisplaySize_NotTheFullscreenWindow()
        {
            // 출처 순서: Display.systemWidth/Height → mainWindowDisplayInfo → currentResolution(전체 화면이면 그 창 크기라 마지막).
            var native = new Vector2Int(3024, 1964);
            Assert.AreEqual(native, GameSettings.PickMonitor(native, new Vector2Int(3024, 1964), new Vector2Int(1280, 800)));
            Assert.AreEqual(native, GameSettings.PickMonitor(Vector2Int.zero, native, new Vector2Int(1280, 800)));
            Assert.AreEqual(new Vector2Int(1280, 800), GameSettings.PickMonitor(Vector2Int.zero, Vector2Int.zero, new Vector2Int(1280, 800)));
            if (Display.main.systemWidth > 0 && Display.main.systemHeight > 0)
                Assert.AreEqual(new Vector2Int(Display.main.systemWidth, Display.main.systemHeight), GameSettings.Monitor, "Monitor reads Display.main.system*");
            GameSettings.MonitorOverride = native;
            Assert.AreEqual(native, GameSettings.Monitor);
            // 실측 버그: 1280×800 전체 화면 상태에서 저장된 창 2116×1374가 1046×680으로 줄었다. 모니터가 바르면 그대로다.
            Assert.AreEqual(new Vector2Int(2116, 1374), GameSettings.FitDisplay(new Vector2Int(2116, 1374), false, GameSettings.Monitor));
            Assert.AreEqual(new Vector2Int(2570, 1668), GameSettings.FitDisplay(native, false, native), "3024×1964 windowed is capped to 85% height (aspect kept, even)");
        }

        [Test]
        public void CenteredWindowPosition_CentresAndClamps()
        {
            Assert.AreEqual(new Vector2Int(276, 179), GameSettings.CenteredWindowPosition(new Vector2Int(3024, 1964), new Vector2Int(2472, 1606)));
            Assert.AreEqual(new Vector2Int(454, 295), GameSettings.CenteredWindowPosition(new Vector2Int(3024, 1964), new Vector2Int(2116, 1374)));
            Assert.AreEqual(Vector2Int.zero, GameSettings.CenteredWindowPosition(new Vector2Int(1280, 720), new Vector2Int(1920, 1080)), "never negative");
        }

        [Test]
        public void ResolutionOptions_Fullscreen_StandardUpToMonitor_PlusNative_Sorted()
        {
            var odd = new[] { new Vector2Int(3024, 1890), new Vector2Int(1512, 945), new Vector2Int(1920, 1080) };
            var full = GameSettings.ResolutionOptions(true, odd, new Vector2Int(2560, 1600));
            CollectionAssert.AreEqual(new[]
            {
                new Vector2Int(1280, 720), new Vector2Int(1280, 800), new Vector2Int(1366, 768), new Vector2Int(1440, 900),
                new Vector2Int(1600, 900), new Vector2Int(1680, 1050), new Vector2Int(1920, 1080), new Vector2Int(1920, 1200),
                new Vector2Int(2560, 1440), new Vector2Int(2560, 1600),
            }, full, "only standard 16:9/16:10 sizes; Screen.resolutions values like 3024×1890 are ignored");
            Assert.AreEqual("1920 × 1080", GameSettings.ResolutionLabel(new Vector2Int(1920, 1080)));

            var retina = new Vector2Int(3024, 1964);
            var rFull = GameSettings.ResolutionOptions(true, odd, retina);
            CollectionAssert.Contains(rFull, retina, "fullscreen keeps the monitor's native size");
            CollectionAssert.DoesNotContain(rFull, new Vector2Int(3024, 1890));
            CollectionAssert.DoesNotContain(rFull, new Vector2Int(3840, 2160), "larger than the monitor");
            Assert.IsTrue(GameSettings.IsNative(retina, retina));
        }

        [Test]
        public void ResolutionOptions_Windowed_StandardWithinCap()
        {
            var retina = new Vector2Int(3024, 1964);
            var win = GameSettings.ResolutionOptions(false, null, retina);
            CollectionAssert.DoesNotContain(win, retina, "the raw monitor size is never a windowed size");
            foreach (var r in win)
            {
                CollectionAssert.Contains(GameSettings.StandardResolutions, r, r + " is not a standard resolution");
                Assert.LessOrEqual(r.y, retina.y * GameSettings.WindowedMaxHeight, r + " taller than 85%");
                Assert.LessOrEqual(r.x, retina.x * GameSettings.WindowedMaxWidth, r + " wider than 95%");
            }
            CollectionAssert.Contains(win, new Vector2Int(1280, 720));
            CollectionAssert.Contains(win, new Vector2Int(2560, 1600));
            CollectionAssert.DoesNotContain(win, new Vector2Int(3840, 2160));
            Assert.AreEqual(new Vector2Int(1920, 1080), GameSettings.DefaultWindowed(retina), "default window is 1920×1080 when it fits");
            for (int i = 1; i < win.Count; i++) Assert.LessOrEqual(win[i - 1].x, win[i].x, "ascending");

            var hd = new Vector2Int(1920, 1080);
            var hdWin = GameSettings.ResolutionOptions(false, null, hd);
            CollectionAssert.AreEqual(new[] { new Vector2Int(1280, 720), new Vector2Int(1280, 800), new Vector2Int(1366, 768), new Vector2Int(1440, 900), new Vector2Int(1600, 900) }, hdWin);
            Assert.AreEqual(new Vector2Int(1600, 900), GameSettings.DefaultWindowed(hd), "1920×1080 does not fit a 1920×1080 window → largest that fits");

            // 적용 크기: 모니터보다 크면 줄이고, 창 모드면 상한 안으로(비율 유지, 짝수).
            Assert.AreEqual(hd, GameSettings.FitDisplay(new Vector2Int(3840, 2160), true, hd));
            var fit = GameSettings.FitDisplay(hd, false, hd);
            Assert.LessOrEqual(fit.y, hd.y * GameSettings.WindowedMaxHeight);
            Assert.AreEqual(1632, fit.x); Assert.AreEqual(918, fit.y);
        }

        [Test]
        public void Volume_IsSquared_AmbientTargets()
        {
            Assert.AreEqual(0f, GameSettings.Volume(0), 1e-6f);
            Assert.AreEqual(0.36f, GameSettings.Volume(60), 1e-6f);
            Assert.AreEqual(1f, GameSettings.Volume(100), 1e-6f);
            Assert.AreEqual(1f, GameSettings.Volume(300), 1e-6f);
            Assert.AreEqual(1f, AudioDirector.AmbientTarget(false, false, false, DayPhase.Working));
            Assert.AreEqual(0.5f, AudioDirector.AmbientTarget(false, false, false, DayPhase.Lunch));
            Assert.AreEqual(0f, AudioDirector.AmbientTarget(true, false, false, DayPhase.Working), "start screen");
            Assert.AreEqual(0f, AudioDirector.AmbientTarget(false, true, false, DayPhase.Working), "paused");
            Assert.AreEqual(0f, AudioDirector.AmbientTarget(false, false, true, DayPhase.Closed), "day summary");
        }

        private static IEnumerator FreshBoot(System.Action<GameBootstrap> got)
        {
            if (GameBootstrap.Instance != null) { Object.Destroy(GameBootstrap.Instance.gameObject); yield return null; }
            var boot = GameBootstrap.Ensure();
            yield return null;
            yield return null;
            got(boot);
        }

        [UnityTest]
        public IEnumerator AudioDirector_MissingClipsAreSilent_VolumesAndAmbientFade()
        {
            PlayerPrefs.SetInt(GameSettings.KeyMusic, 60);
            PlayerPrefs.SetInt(GameSettings.KeyAmbient, 80);
            StartScreen.ShowOnBoot = true;
            GameBootstrap boot = null;
            yield return FreshBoot(b => boot = b);
            try
            {
                var audio = boot.Audio;
                Assert.IsNotNull(audio);
                Assert.IsNotNull(audio.MusicSource);
                Assert.IsNotNull(audio.AmbientSource);
                bool musicFile = Resources.Load<AudioClip>(AudioDirector.MusicPath) != null;
                bool ambientFile = Resources.Load<AudioClip>(AudioDirector.AmbientPath) != null;
                Assert.AreEqual(musicFile, audio.HasMusic);
                Assert.AreEqual(ambientFile, audio.HasAmbient);
                if (musicFile) Assert.IsTrue(audio.MusicSource.loop);
                Assert.IsNotNull(Object.FindFirstObjectByType<AudioListener>(), "an AudioListener must exist");

                // 시작 화면: 음악은 설정값², 배경소리 배수 0.
                Assert.IsTrue(boot.StartScreen.IsOpen);
                yield return new WaitForSecondsRealtime(AudioDirector.AmbientFadeSeconds + 0.2f);
                Assert.AreEqual(0.36f, audio.MusicSource.volume, 1e-4f);
                Assert.AreEqual(0f, audio.AmbientMultiplier, 1e-4f);
                Assert.AreEqual(0f, audio.AmbientSource.volume, 1e-4f);

                // 근무: 배수 1 → 배경소리 = 0.8² = 0.64.
                boot.StartScreen.ButtonFor(StartScreen.Item.NewGame).onClick.Invoke();
                yield return new WaitForSecondsRealtime(AudioDirector.AmbientFadeSeconds + 0.2f);
                Assert.AreEqual(1f, audio.AmbientMultiplier, 1e-4f);
                Assert.AreEqual(0.64f, audio.AmbientSource.volume, 1e-4f);

                // 점심: 절반.
                boot.Session.Clock.Set(1, 12 * 60 + 10);
                yield return new WaitForSecondsRealtime(AudioDirector.AmbientFadeSeconds + 0.2f);
                Assert.AreEqual(0.5f, audio.AmbientMultiplier, 1e-4f);
                Assert.AreEqual(0.32f, audio.AmbientSource.volume, 1e-4f);

                // 설정을 바꾸면 바로 반영된다.
                boot.Settings.SetMusic(100);
                yield return null;
                Assert.AreEqual(1f, audio.MusicSource.volume, 1e-4f);
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SettingsCard_Keyboard_EscPriority_ApplyOnlyWhenDisplayChanged()
        {
            PlayerPrefs.SetInt(GameSettings.KeyMusic, 40);
            StartScreen.ShowOnBoot = true;
            GameBootstrap boot = null;
            yield return FreshBoot(b => boot = b);
            try
            {
                var start = boot.StartScreen;
                Assert.IsTrue(start.ButtonFor(StartScreen.Item.Settings).gameObject.activeSelf, "설정 missing from the menu");
                start.MoveSelection(1); // 새 게임 → 설정(조작법 위)
                Assert.AreEqual(StartScreen.Item.Settings, start.Selected);
                start.Activate();
                var card = start.Settings;
                Assert.IsTrue(card.IsOpen);
                Assert.AreEqual((int)SettingsCard.Row.Music, card.SelectedRow, "the first row is selected on open");
                Assert.AreEqual(40, card.MusicSlider.Value);
                Assert.AreEqual(boot.Audio.HasMusic, !card.NoteVisible(SettingsCard.Row.Music), "missing music clip → note under the slider");
                Assert.AreEqual(boot.Audio.HasAmbient, !card.NoteVisible(SettingsCard.Row.Ambient));

                // ↓ ↑ 행 이동, → : 음악 +5 (키보드는 누를 때마다 저장)
                start.HandleKeys(false, true, false, false, false, false);
                Assert.AreEqual((int)SettingsCard.Row.Ambient, card.SelectedRow);
                start.HandleKeys(true, false, false, false, false, false);
                Assert.AreEqual((int)SettingsCard.Row.Music, card.SelectedRow);
                Assert.IsTrue(card.transform.Find("Area/Card/Body/Music/Selected").gameObject.activeSelf, "selected row should show the amber ring");
                start.HandleKeys(false, false, false, true, false, false);
                Assert.AreEqual(45, card.MusicSlider.Value);
                Assert.AreEqual(45, boot.Settings.Music);
                Assert.AreEqual(45, PlayerPrefs.GetInt(GameSettings.KeyMusic));
                start.HandleKeys(false, false, true, false, false, false);
                Assert.AreEqual(40, boot.Settings.Music);

                // 적용 버튼: 화면 값이 바뀌었을 때만.
                Assume.That(card.DisplayVisible, "the display group is hidden on mobile/touch");
                Assert.IsFalse(card.ApplyButton.interactable, "nothing to apply yet");
                bool full = boot.Settings.Fullscreen;
                card.ModeSegment.SetSelected(full ? 0 : 1, true);
                Assert.IsTrue(card.ApplyButton.interactable, "mode change enables 적용");
                Assert.AreEqual((int)SettingsCard.Row.Mode, card.SelectedRow, "clicking the segment selects its row");

                // 카드는 제목·메뉴 열 오른쪽 빈 영역에 놓인다(PARCEL FACTORY를 가리지 않는다).
                Canvas.ForceUpdateCanvases();
                var cardRt = (RectTransform)card.transform.Find("Area/Card");
                var titleRt = (RectTransform)boot.transform.Find("StartScreen/Column/Title");
                var cc = new Vector3[4]; cardRt.GetWorldCorners(cc);
                var tc = new Vector3[4]; titleRt.GetWorldCorners(tc);
                float titleRight = tc[0].x + UnityEngine.UI.LayoutUtility.GetPreferredWidth(titleRt) * titleRt.lossyScale.x;
                Assert.Greater(cc[0].x, titleRight, "settings card must not cover the title");

                // Esc는 카드만 닫는다(시작 화면은 그대로).
                start.HandleKeys(false, false, false, false, false, true);
                Assert.IsFalse(card.IsOpen);
                Assert.IsTrue(start.IsOpen);

                // 인게임 메뉴 → 설정: Esc 한 번은 카드, 두 번째는 계속하기.
                start.ButtonFor(StartScreen.Item.NewGame).onClick.Invoke();
                Assert.IsFalse(start.IsOpen);
                var session = boot.Session;
                boot.Hud.Root.Find("TopRight/Menu").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.IsTrue(start.InGame);
                start.ButtonFor(StartScreen.Item.Settings).onClick.Invoke();
                Assert.IsTrue(card.IsOpen);
                start.HandleKeys(false, false, false, false, false, true);
                Assert.IsFalse(card.IsOpen, "Esc closes only the settings card");
                Assert.IsTrue(start.IsOpen, "the in-game menu stays open");
                start.HandleKeys(false, false, false, false, false, true);
                Assert.IsFalse(start.IsOpen, "second Esc resumes");
                Assert.AreSame(session, boot.Session);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }

        /// <summary>모니터 1920×1080을 흉내 내는 카드: 전체 화면 1920×1080에서 창 모드로 바꾸면 기본 창(70%)으로, 다시 전체 화면이면 가장 가까운 후보로.</summary>
        [UnityTest]
        public IEnumerator SettingsCard_ModeSwitch_SnapsStepper_EnterApplies()
        {
            GameSettings.MonitorOverride = new Vector2Int(1920, 1080);
            GameSettings.SupportedOverride = new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) };
            PlayerPrefs.SetInt(GameSettings.KeyFullscreen, 1);
            PlayerPrefs.SetInt(GameSettings.KeyResW, 1920);
            PlayerPrefs.SetInt(GameSettings.KeyResH, 1080);
            StartScreen.ShowOnBoot = true;
            GameBootstrap boot = null;
            yield return FreshBoot(b => boot = b);
            try
            {
                var start = boot.StartScreen;
                start.OpenSettings();
                var card = start.Settings;
                Assume.That(card.DisplayVisible, "the display group is hidden on mobile/touch");
                Assert.AreEqual(1, card.ModeSegment.Selected);
                CollectionAssert.AreEqual(GameSettings.ResolutionOptions(true, null, new Vector2Int(1920, 1080)), card.ResolutionOptions);
                Assert.AreEqual(new Vector2Int(1920, 1080), card.PendingResolution);
                Assert.AreEqual("1920 × 1080 · 네이티브", card.ResolutionStepper.ValueText.text);

                card.ModeSegment.SetSelected(0, true); // 창 모드: 1920×1080은 창 상한을 넘는다 → 기본 창(상한 안 최대 1600×900)
                Assert.IsFalse(card.PendingFullscreen);
                CollectionAssert.DoesNotContain(card.ResolutionOptions, new Vector2Int(1920, 1080));
                Assert.AreEqual(GameSettings.DefaultWindowed(new Vector2Int(1920, 1080)), card.PendingResolution);
                Assert.AreEqual(GameSettings.ResolutionLabel(card.PendingResolution), card.ResolutionStepper.ValueText.text);
                Assert.IsTrue(card.ApplyButton.interactable);

                Assert.AreEqual(new Vector2Int(1600, 900), card.PendingResolution);
                card.ModeSegment.SetSelected(1, true); // 다시 전체 화면: 1600×900이 그대로 후보
                Assert.AreEqual(new Vector2Int(1600, 900), card.PendingResolution);
                if (card.ResolutionStepper.Count < 2) Assert.Ignore("need ≥2 resolution options");
                card.ResolutionStepper.SetIndex(card.ResolutionStepper.Count - 1, true);
                Assert.AreEqual(new Vector2Int(1920, 1080), card.PendingResolution);
                Assert.IsFalse(card.ApplyButton.interactable, "back to the saved display → nothing to apply");
                card.ResolutionStepper.SetIndex(0, true);
                Assert.IsTrue(card.ApplyButton.interactable);
                var pending = card.PendingResolution;
                start.HandleKeys(false, false, false, false, true, false); // Enter = 적용
                Assert.AreEqual(pending, boot.Settings.Resolution);
                Assert.AreEqual(pending.x, PlayerPrefs.GetInt(GameSettings.KeyResW));
                Assert.AreEqual(1, PlayerPrefs.GetInt(GameSettings.KeyFullscreen));
                Assert.IsFalse(card.ApplyButton.interactable, "applied → nothing pending");
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return null;
        }
    }
}
