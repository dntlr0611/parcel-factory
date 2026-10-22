using System.Collections;
using System.IO;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>
    /// 사람이 화면을 판단할 수 있도록 실제 렌더 결과를 docs/design/unity-*.png로 남긴다.
    /// -nographics 없이 -batchmode로 실행해야 한다:
    /// Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testCategory Screenshot
    /// 그래픽 장치가 없으면(-nographics) Ignore된다.
    /// </summary>
    [Category("Screenshot")]
    public class ScreenshotTests
    {
        private const int Width = 1600, Height = 900;
        private const int MinBytes = 10 * 1024;

        private static string OutDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "design"));

        [UnityTest]
        public IEnumerator Capture_Start_Demo_Touch_Wide()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            Debug.Log($"[Screenshot] requested {Width}x{Height}, actual Screen {Screen.width}x{Screen.height}, device {SystemInfo.graphicsDeviceType}");

            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(1.0f);
                Debug.Log($"[Screenshot] booted, loop ticks {boot.Loop.Ticks}");
                var yard = GameObject.Find("World/Yard");
                if (yard != null)
                    foreach (Transform t in yard.transform)
                    {
                        var rs = t.GetComponentsInChildren<Renderer>();
                        if (rs.Length == 0) continue;
                        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                        Debug.Log($"[Screenshot] yard {t.name} center {b.center:F1} size {b.size:F1}");
                    }

                // 참고용: 화면 버퍼 캡처가 배치 모드에서 동작하는지 기록만 한다(결과물로 쓰지 않는다).
                string probe = Path.Combine(Path.GetTempPath(), "pf-screencapture-probe.png");
                if (File.Exists(probe)) File.Delete(probe);
                ScreenCapture.CaptureScreenshot(probe);
                for (int i = 0; i < 30 && !File.Exists(probe); i++) yield return null;
                Debug.Log($"[Screenshot] ScreenCapture probe {(File.Exists(probe) ? "wrote " + new FileInfo(probe).Length + " bytes" : "wrote nothing")} at Screen {Screen.width}x{Screen.height}");

                // (a) 시작 상태
                yield return Capture(boot, "unity-start.png", Width, Height);

                // (b) 샘플 배치: 입고 → 레일 → 검사대 → 레일 → 파랑 팔레트(5,2)에 검사한 상자 3개(맨 위는 거부 도장, 나머지는 초록 체크),
                //     작업자는 검사한 상자(초록 체크)를 들고 트럭 도크(11,2) 앞에 서 있다(프롬프트 "출고 +30").
                var s = boot.Session;
                s.Economy.Earn(500);
                Place(s, 1, 2, CellType.Conveyor, Direction.East);
                Place(s, 2, 2, CellType.Conveyor, Direction.East);
                Place(s, 3, 2, CellType.Table, Direction.East);
                Place(s, 4, 2, CellType.Conveyor, Direction.East);
                var factory = new PackageFactory(new System.Random(5), 0.0);
                for (int i = 0; i < 3; i++)
                {
                    var pp = factory.Create(s.Sim.NextId++, FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY, Direction.East);
                    pp.Inspect(i == 2); // 맨 위 상자만 거부
                    Assert.IsTrue(s.Sim.PutOnPallet(pp, FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY), "pile " + i);
                }
                var carried = factory.Create(s.Sim.NextId++, 10, 2, Direction.East);
                carried.Inspect(false);
                carried.Waybill = true; // 운송장(스펙 15)을 붙인 상자라 "출고 +30"
                s.Worker.Worker.Carrying = carried;
                s.Worker.Worker.X = 10.0f; s.Worker.Worker.Y = 2.0f; s.Worker.Worker.Facing = Direction.East;
                boot.Input.SelectSlot(0);
                yield return new WaitForSeconds(1.0f);
                // 1초 동안 흘러간 택배와 상관없이 입구 하나, 벨트 위 하나를 확실히 둔다.
                s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _);
                s.Sim.TrySpawn(2, 2, PackageKind.Normal, out _);
                yield return new WaitForSeconds(0.2f);
                Debug.Log($"[Screenshot] packages on line: {s.Sim.Packages.Count}, pallet pile {s.Sim.PalletCount(5, 2)}, selected slot {boot.Input.SelectedSlot}");
                var demoPrompt = boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>();
                Debug.Log($"[Screenshot] demo prompt '{demoPrompt.text}'");
                StringAssert.Contains("출고 +30", demoPrompt.text);
                s.Clock.Set(1, 640f); // 시계가 보이게 10:40
                yield return Capture(boot, "unity-demo.png", Width, Height);

                // (c) 터치 모드
                boot.Hud.ForceTouchMode(true);
                yield return null;
                yield return new WaitForSeconds(0.2f);
                yield return Capture(boot, "unity-touch.png", Width, Height);
                boot.Hud.ForceTouchMode(false);

                // (c2) 설치 프롬프트: 빈손이 아닌 레일 도구로 빈 칸 (3,3)을 바라본다 → "레일 설치 · ₩10" + 키캡 E.
                s.Worker.Worker.Carrying = null;
                s.Worker.Worker.X = 3.0f; s.Worker.Worker.Y = 4.0f; s.Worker.Worker.Facing = Direction.South;
                boot.Input.SelectSlot(0);
                Assert.AreEqual(CellType.Conveyor, boot.Input.SelectedTool.Value.Type);
                Assert.AreEqual(CellType.Empty, s.Grid.Get(3, 3).Type);
                yield return null;
                yield return new WaitForSeconds(0.2f);
                var placePrompt = boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>();
                Debug.Log($"[Screenshot] place prompt '{placePrompt.text}'");
                StringAssert.Contains("레일 설치 · ₩10", placePrompt.text);
                yield return Capture(boot, "unity-place.png", Width, Height);

                // (c2b) 설치 미리보기(스펙 11.9): 레일 도구로 라인 아래 빈 칸 (3,0)을 (3,1)에서 바라본다 → 초록 반투명 레일.
                //      카메라 쪽(남쪽) 칸이라 작업자에 가리지 않는다. 설정 모드는 끄고 찍는다.
                boot.Input.CancelConfig();
                s.Worker.Worker.X = 3.0f; s.Worker.Worker.Y = 1.0f; s.Worker.Worker.Facing = Direction.South;
                Assert.AreEqual(CellType.Empty, s.Grid.Get(3, 0).Type);
                yield return null;
                yield return new WaitForSeconds(0.2f);
                Assert.IsTrue(boot.Ghost.Visible, "ghost hidden");
                Assert.AreEqual(PlacementGhost.GreenTint, boot.Ghost.Tint);
                yield return Capture(boot, "unity-ghost.png", Width, Height);

                // (c3) 점심시간 12:20: 시계 옆 노란 "점심시간" 배지, 체력 15라 바가 빨갛다.
                //      점심에는 체력이 초당 20 회복되므로 일시정지해 캡처 동안 값을 고정한다(일시정지 버튼이 켜져 보인다).
                s.Paused = true;
                s.Clock.Set(1, 12 * 60 + 20);
                s.Worker.Worker.Stamina = 15f;
                yield return null;
                yield return new WaitForSeconds(0.2f);
                Assert.AreEqual(DayPhase.Lunch, s.Clock.Phase);
                Assert.IsTrue(boot.Hud.Root.Find("Clock/Badge").gameObject.activeSelf, "lunch badge hidden");
                yield return Capture(boot, "unity-lunch.png", Width, Height);

                // (c4) 상태창: EXP 120, 체력 Lv 1(최대 120). 근무 중 15:00, 일시정지로 값을 고정한다.
                s.Clock.Set(1, 15 * 60);
                s.Progression.Restore(120, 1, 0, 0, 160);
                s.Worker.Worker.Stamina = 96f;
                boot.Panels.ToggleStatus();
                Assert.IsTrue(boot.Panels.StatusIsOnlyOpen, "status panel did not open");
                yield return null;
                yield return new WaitForSeconds(0.2f);
                Assert.AreEqual("EXP 120", boot.Hud.Root.Find("TopLeft/Exp").GetComponent<Text>().text);
                yield return Capture(boot, "unity-status.png", Width, Height);
                boot.Panels.CloseAll();
                s.Paused = false;

                // (c5) 미니 상태바 + 설정 배너: 분배기 (3,3) 북쪽, 북(3,4)·동(4,3)에 레일. 작업자는 서쪽 (2,3)에서 분배기를 바라보고 R → "1 파랑 · 2 빨강 선택".
                s.Worker.Worker.X = 2.0f; s.Worker.Worker.Y = 3.0f; s.Worker.Worker.Facing = Direction.East;
                s.Toolbar.Add(CellType.Splitter, Direction.North);
                s.Toolbar.Add(CellType.Conveyor, Direction.North);
                Place(s, 3, 3, CellType.Splitter, Direction.North);
                Place(s, 3, 4, CellType.Conveyor, Direction.North);
                Place(s, 4, 3, CellType.Conveyor, Direction.East);
                boot.Input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickSlot, boot.Input.ConfigMode);
                yield return null;
                yield return new WaitForSeconds(0.2f);
                Debug.Log($"[Screenshot] status bars {boot.StatusBars.Count}, splitter '{boot.StatusBars.TextAt(3, 3)}', table '{boot.StatusBars.TextAt(3, 2)}'");
                Assert.IsTrue(boot.Hud.Root.Find("ConfigBanner").gameObject.activeSelf, "config banner hidden");
                StringAssert.EndsWith("R 설정", boot.StatusBars.TextAt(3, 3));
                yield return Capture(boot, "unity-statusbars.png", Width, Height);
                boot.Input.CancelConfig();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
            yield return null;

            // (d) 넓은 화면: 새 부팅을 2400x1080에서 캡처한다.
            Screen.SetResolution(2400, 1080, false);
            yield return null;
            yield return null;
            Debug.Log($"[Screenshot] wide requested 2400x1080, actual Screen {Screen.width}x{Screen.height}");
            var boot2 = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(1.0f);
                yield return Capture(boot2, "unity-wide.png", 2400, 1080);
            }
            finally
            {
                Object.Destroy(boot2.gameObject);
                Screen.SetResolution(Width, Height, false);
            }
            yield return null;
        }

        /// <summary>
        /// 시작 화면(스펙 12.1): 저장 파일(3일차 · ₩1,240)이 있는 상태로 부팅 메뉴를 띄워 docs/design/unity-title.png로 남긴다.
        /// 뒤에는 데모 라인(입고 → 레일 → 검사대 → 레일 → 팔레트)이 깔린 공장이 그대로 보인다.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Title()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            string save = Path.Combine(Path.GetTempPath(), "pf-title-save.json");
            SaveFile.PathOverride = save;
            var saved = new GameSession();
            saved.Clock.Set(3, 10 * 60);
            saved.Economy.Earn(1240 - saved.Economy.Money);
            Assert.IsTrue(SaveFile.TrySave(saved, out var err), err);
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                Place(s, 1, 2, CellType.Conveyor, Direction.East);
                Place(s, 2, 2, CellType.Conveyor, Direction.East);
                Place(s, 3, 2, CellType.Table, Direction.East);
                Place(s, 4, 2, CellType.Conveyor, Direction.East);
                s.Sim.TrySpawn(2, 2, PackageKind.Normal, out _);
                boot.StartScreen.Show(false);
                Assert.IsTrue(boot.StartScreen.ButtonFor(StartScreen.Item.Continue).gameObject.activeSelf, "이어하기 hidden");
                yield return new WaitForSeconds(0.5f);
                yield return Capture(boot, "unity-title.png", Width, Height);
                boot.StartScreen.Hide();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
                SaveFile.PathOverride = null;
                if (File.Exists(save)) File.Delete(save);
            }
            yield return null;
        }

        /// <summary>설정 카드(스펙 13.3): 시작 화면 위에 연 카드, 첫 행(음악) 선택. docs/design/unity-settings.png.</summary>
        [UnityTest]
        public IEnumerator Capture_Settings()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var prefs = SettingsTests.BackupPrefs();
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                boot.StartScreen.Show(false);
                boot.StartScreen.OpenSettings();
                var card = boot.StartScreen.Settings;
                Assert.IsTrue(card.IsOpen, "settings card hidden");
                Assert.AreEqual((int)SettingsCard.Row.Music, card.SelectedRow, "음악 행이 선택돼 열린다(앰버 테두리)");
                yield return new WaitForSeconds(0.5f);
                yield return Capture(boot, "unity-settings.png", Width, Height);
                boot.StartScreen.Hide();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
                SettingsTests.RestorePrefs(prefs);
            }
            yield return null;
        }

        /// <summary>
        /// 브릿지 교차(스펙 11.12): 데모 라인(입고 → 레일 → 검사대 → 레일 → 파랑 팔레트) 옆, 부지 확장 1(9×9)에서
        /// 브릿지 (3,4) 북쪽. 동쪽 줄 (2,4)→(3,4)→(4,4)→빨강 팔레트(5,4)는 아랫길, 북쪽 줄 (3,3)→(3,4)→(3,5)→(3,6)은 윗길.
        /// 두 차선 모두에 택배가 올라 있고 뒤에 한 개씩 더 기다린다. 작업자는 (2,6)에서 동쪽을 본다. 일시정지로 고정한다.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Bridge()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(0.5f);
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                s.Unlocks.Grant(UnlockId.Plot1);
                s.Unlocks.Grant(UnlockId.Bridge);
                s.Unlocks.Grant(UnlockId.FastEast);
                s.Unlocks.Grant(UnlockId.Splitter);
                s.Toolbar.Add(CellType.Scanner, Direction.East); // 핫바 7칸이 보이게(자동 검사기 해금은 부지 2가 필요하므로 도구만 준다)
                s.Toolbar.Add(CellType.Conveyor, Direction.North);
                boot.Hud.RefreshHotbar(); // Toolbar.Add는 해금 이벤트를 내지 않는다
                Place(s, 1, 2, CellType.Conveyor, Direction.East);
                Place(s, 2, 2, CellType.Conveyor, Direction.East);
                Place(s, 3, 2, CellType.Table, Direction.East);
                Place(s, 4, 2, CellType.Conveyor, Direction.East);
                Place(s, 3, 4, CellType.Bridge, Direction.North);
                Place(s, 2, 4, CellType.Conveyor, Direction.East);
                Place(s, 4, 4, CellType.Conveyor, Direction.East);
                Place(s, 3, 3, CellType.Conveyor, Direction.North);
                Place(s, 3, 5, CellType.Conveyor, Direction.North);
                Place(s, 3, 6, CellType.Conveyor, Direction.North);
                Assert.IsTrue(s.Sim.TrySpawn(2, 4, PackageKind.Normal, out var east), "spawn east");
                Assert.IsTrue(s.Sim.TrySpawn(3, 3, PackageKind.Normal, out var north), "spawn north");
                for (int i = 0; i < 80 && s.Sim.PackagesAt(3, 4).Count < 2; i++) s.Sim.Tick(0.05f);
                Assert.AreEqual(2, s.Sim.PackagesAt(3, 4).Count, "both lanes should hold a package");
                for (int i = 0; i < 6; i++) s.Sim.Tick(0.05f); // 칸 가운데 쪽으로
                // 아랫길 택배는 고가 벨트 밑을 지나는 동안 위에서 가려지므로, 동쪽으로 빠져나오는 순간(진행 0.9)으로 옮겨 찍는다.
                east.Progress = 0.9f;
                Assert.IsTrue(s.Sim.TrySpawn(2, 4, PackageKind.Normal, out _), "spawn east 2");
                Assert.IsTrue(s.Sim.TrySpawn(3, 3, PackageKind.Normal, out _), "spawn north 2");
                s.Sim.TrySpawn(1, 2, PackageKind.Normal, out _);
                Assert.AreEqual(Lane.Upper, north.Lane);
                Assert.AreEqual(Lane.Lower, east.Lane);
                s.Worker.Worker.X = 2.0f; s.Worker.Worker.Y = 6.0f; s.Worker.Worker.Facing = Direction.East;
                s.Worker.Worker.Stamina = 90f; // 80% 눈금이 채움 위에 보이게
                s.Clock.Set(1, 640f);
                yield return null;
                yield return new WaitForSeconds(0.3f);
                Debug.Log($"[Screenshot] bridge status '{boot.StatusBars.TextAt(3, 4)}', slots {boot.Input.AvailableSlots().Count}, packages {s.Sim.Packages.Count}, north {north.X},{north.Y} p{north.Progress:F2}, east {east.X},{east.Y} p{east.Progress:F2}");
                StringAssert.Contains("브릿지 ↑", boot.StatusBars.TextAt(3, 4));
                yield return Capture(boot, "unity-bridge.png", Width, Height);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        /// <summary>
        /// 항공 택배와 12×12 자동화(스펙 14, 15): 부지 확장 2. 동쪽 벽에 출고(파랑)·반품(빨강) 트럭과 항공 출고(초록 (11,7))·항공 반품(노랑 (11,9)) 트럭,
        /// 항공 트럭 지붕에 비행기 표지판. 입고 → (1,2)(2,2) → 자동 검사기 (3,2) 항공 모드(흰 화살표 일반 동 · 파란 화살표 항공 북):
        /// 일반 줄 (4..9,2) → 분배기 (10,2) 검사 모드(파랑 → 출고, 빨강 ↑ → (10,3)(10,4) → 반품),
        /// 항공 줄 (3,3..6) ↑ → (3,7) → (4..9,7) → 분배기 (10,7) 항공 모드(초록 → 항공 출고, 노랑 ↑ → (10,8)(10,9) → 항공 반품).
        /// 검사기 앞에는 운송장 없는 상자(항공 마크만), 뒤에는 운송장(먹색 머리띠, 스펙 15.4)과 검사 도장(거부 빨강·승인 초록)이 붙은 상자, 항공 상자는 윗면 전체가 항공 마크.
        /// 작업자는 운송장을 붙인 항공 상자를 들고 (11,6)에서 북쪽 초록 트럭 도크 (11,7)을 본다(프롬프트 "항공 출고 +30"). 일시정지로 고정한다.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Air()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(0.5f);
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(5000);
                s.Unlocks.Grant(UnlockId.Plot1);
                s.Unlocks.Grant(UnlockId.Plot2);
                s.Unlocks.Grant(UnlockId.Splitter);
                s.Unlocks.Grant(UnlockId.Scanner);
                s.Unlocks.Grant(UnlockId.RailNorth);
                boot.Hud.RefreshHotbar();
                yield return null;
                Assert.AreEqual(CellType.AirOutlet, s.Grid.Get(11, 7).Type);
                Assert.AreEqual(CellType.AirReturnDock, s.Grid.Get(11, 9).Type);
                Assert.AreEqual(BuildResult.Ok, s.Build.BeginDemolish(5, 2)); // 시작 파랑 팔레트 자리를 일반 줄이 지난다
                s.Build.Tick(BuildService.DemolishSeconds + 0.1f);
                yield return new WaitForSeconds(HudController.PopupSeconds + 0.2f); // 환불 팝업(+16)이 사라질 때까지
                Place(s, 1, 2, CellType.Conveyor, Direction.East);
                Place(s, 2, 2, CellType.Conveyor, Direction.East);
                Place(s, 3, 2, CellType.Scanner, Direction.East);
                for (int x = 4; x <= 9; x++) Place(s, x, 2, CellType.Conveyor, Direction.East);
                Place(s, 10, 2, CellType.Splitter, Direction.East);
                Place(s, 10, 3, CellType.Conveyor, Direction.North);
                Place(s, 10, 4, CellType.Conveyor, Direction.East);
                for (int y = 3; y <= 6; y++) Place(s, 3, y, CellType.Conveyor, Direction.North);
                Place(s, 3, 7, CellType.Conveyor, Direction.East);
                for (int x = 4; x <= 9; x++) Place(s, x, 7, CellType.Conveyor, Direction.East);
                Place(s, 10, 7, CellType.Splitter, Direction.East);
                Place(s, 10, 8, CellType.Conveyor, Direction.North);
                Place(s, 10, 9, CellType.Conveyor, Direction.East);
                Assert.AreEqual(ReconfigureResult.Ok, s.Build.SetMode(3, 2, RouteMode.Air));
                Assert.AreEqual(ReconfigureResult.Ok, s.Build.SetScannerAirExit(3, 2, Direction.North));
                Assert.AreEqual(ReconfigureResult.Ok, s.Build.SetMode(10, 7, RouteMode.Air));

                var f = new PackageFactory(new System.Random(21), 0.0);
                Package Put(int x, int y, bool air, bool? reject)
                {
                    var p = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                    p.Air = air;
                    if (air) p.Label.Method = PackageFactory.AirMethod;
                    if (reject.HasValue) { p.Inspect(reject.Value); p.Waybill = true; } // 검사기를 지난 상자: 판정 + 운송장
                    if (x >= 0) { s.Sim.PlaceCarried(p, x, y); p.Progress = 0.5f; }
                    return p;
                }
                Put(1, 2, true, null);     // 검사기 앞: 운송장 없음, 항공 마크만
                Put(2, 2, false, null);
                Put(5, 2, false, false);   // 일반 줄: 출고 운송장(파랑)
                Put(7, 2, false, true);    // 반품 운송장(빨강)
                Put(9, 2, false, false);
                Put(3, 4, true, false);    // 항공 줄
                Put(3, 6, true, true);
                Put(5, 7, true, false);
                Put(7, 7, true, false);
                Put(9, 7, true, true);
                var carried = Put(-1, 0, true, false);
                s.Worker.Worker.Carrying = carried;
                s.Worker.Worker.X = 11.0f; s.Worker.Worker.Y = 6.0f; s.Worker.Worker.Facing = Direction.North;
                s.Clock.Set(1, 640f);
                yield return null;
                yield return new WaitForSeconds(HudController.PopupSeconds + 0.2f); // 설치비 팝업이 사라질 때까지
                var prompt = boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>();
                Debug.Log($"[Screenshot] air prompt '{prompt.text}', packages {s.Sim.Packages.Count}, scanner '{boot.StatusBars.TextAt(3, 2)}', air splitter '{boot.StatusBars.TextAt(10, 7)}'");
                Assert.AreEqual("항공 출고 +30 · 0/30", prompt.text);
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-outlet/AirSign"), "air sign missing");
                // 상자 윗면의 항공 마크·운송장이 읽히도록 동쪽 절반을 가까이(거리 14) 찍는다. 카메라 각도는 게임과 같다.
                yield return Capture(boot, "unity-air.png", Width, Height, cam =>
                {
                    var target = Grid3D.TileF(8.2f, 5.6f);
                    cam.transform.position = target + new Vector3(0f, 14.5f, -10.5f).normalized * 17f;
                    cam.transform.LookAt(target);
                });
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        /// <summary>
        /// 운송장 카드(스펙 15.2): 입고 → 레일 → 검사대 → 레일 → 부착대 (5,2)(시작 팔레트 자리) 라인. 검사를 마친 항공 거부 택배가 부착대에 멈춰 있고
        /// 작업자가 (5,3)에서 E → 카드. 운송장(한 종류: 먹색 머리띠 + 항공 배지, 스펙 15.4)을 상자 쪽으로 반쯤 끌고 있는 중간 상태(1.05배, 영역 테두리 앰버).
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Waybill()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(0.5f);
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(500);
                Assert.AreEqual(BuildResult.Ok, s.Build.BeginDemolish(5, 2));
                s.Build.Tick(BuildService.DemolishSeconds + 0.1f);
                Place(s, 1, 2, CellType.Conveyor, Direction.East);
                Place(s, 2, 2, CellType.Conveyor, Direction.East);
                Place(s, 3, 2, CellType.Table, Direction.East);
                Place(s, 4, 2, CellType.Conveyor, Direction.East);
                Place(s, 5, 2, CellType.Labeler, Direction.East);
                var f = new PackageFactory(new System.Random(31), 1.0, 1.0);
                var p = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                p.Inspect(true);
                s.Sim.PlaceCarried(p, 5, 2);
                s.Sim.Tick(1f);
                Assert.IsTrue(s.Sim.IsAwaitingLabel(p));
                s.Sim.TrySpawn(2, 2, PackageKind.Normal, out _);
                s.Worker.Worker.X = 5.0f; s.Worker.Worker.Y = 3.0f; s.Worker.Worker.Facing = Direction.South;
                s.Clock.Set(1, 640f);
                yield return null;
                boot.Input.Interact();
                yield return null;
                var card = boot.Panels.Waybill;
                Assert.IsTrue(card.IsOpen, "waybill card did not open");
                var paper = card.Paper.gameObject;
                var start = RectTransformUtility.WorldToScreenPoint(null, card.Paper.position);
                var zone = RectTransformUtility.WorldToScreenPoint(null, card.Zone.position);
                var e = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = start };
                UnityEngine.EventSystems.ExecuteEvents.Execute(paper, e, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
                e.position = Vector2.Lerp(start, zone, 0.55f) + new Vector2(0f, 14f);
                UnityEngine.EventSystems.ExecuteEvents.Execute(paper, e, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
                Assert.IsTrue(card.IsDragging);
                yield return new WaitForSeconds(0.2f);
                yield return Capture(boot, "unity-waybill.png", Width, Height);
                UnityEngine.EventSystems.ExecuteEvents.Execute(paper, e, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        /// <summary>
        /// 부지마다 입고구(스펙 16.2): 12×12에서 서쪽 벽 (0,2)·(0,7)·(0,10) 세 입고구와 초록 입고 트럭, 각 입고구 앞 짧은 레일(1..3, ·)에 택배.
        /// 일시정지로 고정한다.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Inlets()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(0.5f);
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                s.Unlocks.Grant(UnlockId.Plot1);
                s.Unlocks.Grant(UnlockId.Plot2);
                yield return null;
                foreach (var (x, y) in FactoryGrid.InletTiles)
                {
                    Assert.AreEqual(CellType.Inlet, s.Grid.Get(x, y).Type, $"inlet ({x},{y})");
                    for (int rx = 1; rx <= 3; rx++) Place(s, rx, y, CellType.Conveyor, Direction.East);
                    Assert.IsTrue(s.Sim.TrySpawn(x, y, PackageKind.Normal, out var a), "spawn at inlet");
                    a.Progress = 0.5f;
                    Assert.IsTrue(s.Sim.TrySpawn(2, y, PackageKind.Normal, out var b), "spawn on rail");
                    b.Progress = 0.5f;
                }
                s.Worker.Worker.X = 4.0f; s.Worker.Worker.Y = 5.0f; s.Worker.Worker.Facing = Direction.West;
                s.Clock.Set(1, 640f);
                yield return new WaitForSeconds(HudController.PopupSeconds + 0.2f); // 설치비 팝업이 사라질 때까지
                Assert.IsNotNull(GameObject.Find("World/InletDocks/Dock_inlet-7/van"));
                Assert.IsNotNull(GameObject.Find("World/InletDocks/Dock_inlet-10/van"));
                yield return Capture(boot, "unity-inlets.png", Width, Height);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        /// <summary>
        /// 취급주의 택배(스펙 17): 북쪽으로 흐르는 세로 줄 (3,0) 레일 → (3,1) 고속 레일 → (3,2)(3,3)(3,4) 레일. 아래부터 옆면 흐린 인쇄(스티커 전) 상자,
        /// 고속 레일에서 파손된 상자(윗면 금 무늬, 검사 지워짐), 항공 + 취급주의(인쇄), 스티커 + 운송장 + 승인 상자, 항공 + 스티커 상자.
        /// 부착대 (5,2)(시작 팔레트 자리)의 취급주의 택배로 운송장 카드를 열었다. 운송장은 이미 가운데에 붙었고(두 장 중 하나), 취급주의 스티커를
        /// 상자 그림 왼쪽 아래 옆면 칸 쪽으로 끌고 있다(칸 테두리 앰버). 상자 줄은 카드 오른쪽 화면에 오도록 카메라를 서쪽으로 옮겨 가까이 찍는다. 일시정지로 고정한다.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Fragile()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(0.5f);
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(2000);
                s.Unlocks.Grant(UnlockId.FastNorth);
                s.Unlocks.Grant(UnlockId.RailNorth);
                Assert.AreEqual(BuildResult.Ok, s.Build.BeginDemolish(5, 2));
                s.Build.Tick(BuildService.DemolishSeconds + 0.1f);
                Place(s, 3, 0, CellType.Conveyor, Direction.North);
                Place(s, 3, 1, CellType.FastConveyor, Direction.North);
                for (int y = 2; y <= 4; y++) Place(s, 3, y, CellType.Conveyor, Direction.North);
                Place(s, 5, 2, CellType.Labeler, Direction.East);
                var f = new PackageFactory(new System.Random(41), 0.0);
                Package Put(int x, int y, bool sticker, bool air = false, bool? reject = null, float progress = 0.5f)
                {
                    var p = f.Create(s.Sim.NextId++, 0, 0, Direction.North);
                    p.Fragile = true;
                    p.Air = air;
                    if (air) p.Label.Method = PackageFactory.AirMethod;
                    if (reject.HasValue) p.Inspect(reject.Value);
                    p.Waybill = sticker;
                    p.FragileSticker = sticker;
                    s.Sim.PlaceCarried(p, x, y);
                    p.Progress = progress;
                    return p;
                }
                Put(3, 0, false);                             // 흐린 인쇄
                var broken = Put(3, 1, true, reject: false);  // 고속 레일 → 파손(금 무늬, 검사 지워짐)
                Assert.IsTrue(broken.Broken);
                Put(3, 2, false, air: true);                  // 항공 + 취급주의(인쇄)
                Put(3, 3, true, reject: false);               // 스티커 + 운송장 + 승인
                Put(3, 4, true, air: true, reject: true);     // 항공 + 스티커 + 거부 도장
                var onLabeler = Put(5, 2, false, reject: false, progress: 1f);
                onLabeler.Waybill = true; // 운송장은 붙었고 스티커만 남음 → 카드에서 하나 붙은 상태
                Assert.IsTrue(s.Sim.IsAwaitingLabel(onLabeler));
                s.Worker.Worker.X = 5.0f; s.Worker.Worker.Y = 3.0f; s.Worker.Worker.Facing = Direction.South;
                s.Clock.Set(2, 640f);
                yield return new WaitForSeconds(2f); // 설치비 팝업·파손 토스트(1.8초)가 사라질 때까지
                boot.Input.Interact();
                yield return null;
                var card = boot.Panels.Waybill;
                Assert.IsTrue(card.IsOpen, "waybill card did not open");
                Assert.IsTrue(card.WaybillDone && !card.StickerDone);
                var st = card.Sticker.gameObject;
                var start = RectTransformUtility.WorldToScreenPoint(null, card.Sticker.position);
                var zone = RectTransformUtility.WorldToScreenPoint(null, card.StickerZone.position);
                var e = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = start };
                UnityEngine.EventSystems.ExecuteEvents.Execute(st, e, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
                // 옆면 칸 바로 오른쪽(카드 좌표 (−200, −78)): 상자 옆면 띠 위, 운송장과 겹치지 않게
                e.position = RectTransformUtility.WorldToScreenPoint(null, card.Card.TransformPoint(new Vector3(-200f, -78f, 0f))) + (start - RectTransformUtility.WorldToScreenPoint(null, card.Sticker.position));
                UnityEngine.EventSystems.ExecuteEvents.Execute(st, e, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
                Assert.IsTrue(card.IsDragging);
                yield return new WaitForSeconds(0.2f);
                Debug.Log($"[Screenshot] fragile labeler '{boot.StatusBars.TextAt(5, 2)}', prompt '{boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>().text}', broken #{broken.Id} inspected={broken.Inspected}");
                yield return Capture(boot, "unity-fragile.png", Width, Height, cam =>
                {
                    var target = Grid3D.TileF(0.4f, 2.1f);
                    cam.transform.position = target + new Vector3(0f, 14.5f, -10.5f).normalized * 9.6f;
                    cam.transform.LookAt(target);
                });
                UnityEngine.EventSystems.ExecuteEvents.Execute(st, e, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        /// <summary>
        /// 트럭 적재량과 교대(스펙 18): 부지 확장 2의 동쪽 트럭 네 대. 출고(파랑) 트럭이 30개를 싣고 벽 밖으로 빠져나가는 중(알약 "다음 차 0:20"),
        /// 반품 12/30, 항공 출고 27/30(앰버에 가깝게), 항공 반품 3/30. 출고 앞 레일 (10,2)에는 택배가 기다린다.
        /// 출발 연출 중간에서 게임을 일시정지해(트럭도 멈춘다) 찍는다.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_Trucks()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); screenshots need a renderer");

            Directory.CreateDirectory(OutDir);
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                yield return new WaitForSeconds(0.5f);
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(2000);
                s.Unlocks.Grant(UnlockId.Plot1);
                s.Unlocks.Grant(UnlockId.Plot2);
                yield return null;
                for (int x = 7; x <= 10; x++) Place(s, x, 2, CellType.Conveyor, Direction.East);
                var f = new PackageFactory(new System.Random(51), 0.0);
                for (int x = 9; x <= 10; x++)
                {
                    var p = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                    p.Inspect(false); p.Waybill = true;
                    s.Sim.PlaceCarried(p, x, 2);
                    p.Progress = x == 10 ? 1f : 0.5f;
                }
                s.Sim.Trucks.Restore(CellType.ReturnDock, 12, 0f);
                s.Sim.Trucks.Restore(CellType.AirOutlet, 27, 0f);
                s.Sim.Trucks.Restore(CellType.AirReturnDock, 3, 0f);
                s.Worker.Worker.X = 9.0f; s.Worker.Worker.Y = 5.0f; s.Worker.Worker.Facing = Direction.North;
                s.Clock.Set(2, 700f);
                yield return new WaitForSeconds(HudController.PopupSeconds + 0.2f); // 설치비 팝업이 사라질 때까지
                Assert.AreEqual("27/30", boot.Trucks.TextOf(CellType.AirOutlet));
                s.Sim.Trucks.Restore(CellType.Outlet, 0, TruckFleet.TruckReturnMinutes); // 방금 30개째를 싣고 출발
                yield return null;
                // 트럭 연출은 게임 일시정지 중에는 멈추므로(스펙 17.4) 잠깐 풀었다가 중간에서 다시 멈춘다. 대기 시간이 1분 안팎 줄어든다.
                s.Paused = false;
                float until = Time.realtimeSinceStartup + 3f;
                while (boot.Trucks.Progress(CellType.Outlet) < 0.55f && Time.realtimeSinceStartup < until) yield return null;
                s.Paused = true;
                Assert.IsTrue(boot.Trucks.IsDriving(CellType.Outlet), "outlet truck should be mid-departure");
                Debug.Log($"[Screenshot] trucks outlet '{boot.Trucks.TextOf(CellType.Outlet)}' progress {boot.Trucks.Progress(CellType.Outlet):F2}, return '{boot.Trucks.TextOf(CellType.ReturnDock)}', air '{boot.Trucks.TextOf(CellType.AirOutlet)}' / '{boot.Trucks.TextOf(CellType.AirReturnDock)}'");
                StringAssert.StartsWith("다음 차 0:", boot.Trucks.TextOf(CellType.Outlet));
                yield return Capture(boot, "unity-trucks.png", Width, Height, cam =>
                {
                    var target = Grid3D.TileF(10.4f, 5.4f);
                    cam.transform.position = target + new Vector3(0f, 14.5f, -10.5f).normalized * 17f;
                    cam.transform.LookAt(target);
                });
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        private static void Place(GameSession s, int x, int y, CellType t, Direction d)
        {
            var r = s.Build.TryPlace(x, y, t, d);
            Assert.AreEqual(BuildResult.Ok, r, $"place {t} {d} at ({x},{y})");
        }

        /// <summary>
        /// 배치 모드 에디터에서는 게임 뷰가 640x480으로 고정되고 WaitForEndOfFrame이 돌아오지 않으므로
        /// 메인 카메라를 w x h RenderTexture에 직접 렌더한다. HUD 캔버스는 잠시 ScreenSpaceCamera로 바꿔 같은 카메라로 그리고,
        /// 카메라 프레이밍(WorldBuilder.FrameLot)은 RenderTexture 비율로 다시 잡는다. 끝나면 모두 되돌린다.
        /// </summary>
        private static IEnumerator Capture(GameBootstrap boot, string file, int w, int h, System.Action<Camera> cameraOverride = null)
        {
            string path = Path.Combine(OutDir, file);
            if (File.Exists(path)) File.Delete(path);

            var cam = boot.World.MainCamera;
            // HUD와 시작 화면 캔버스(둘 다 부트스트랩 아래 루트 캔버스)를 같은 카메라로 그린다. 정렬은 sortingOrder를 따른다.
            var canvases = new System.Collections.Generic.List<Canvas>();
            foreach (var c in boot.GetComponentsInChildren<Canvas>(true)) if (c.isRootCanvas) canvases.Add(c);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var prevModes = new RenderMode[canvases.Count];
            var prevScaleModes = new CanvasScaler.ScaleMode[canvases.Count];
            var prevScaleFactors = new float[canvases.Count];
            cam.targetTexture = rt;
            Reframe(boot.World);
            cameraOverride?.Invoke(cam); // 가까이 찍을 때(상자 윗면 표시를 읽을 수 있게). 끝나면 Reframe으로 되돌린다
            for (int i = 0; i < canvases.Count; i++)
            {
                var canvas = canvases[i];
                prevModes[i] = canvas.renderMode;
                // 배치 모드 화면(640x480) 기준 배율 대신 RenderTexture 높이 기준(게임 기준 해상도 1280x800의 800)으로 HUD를 키워 글자를 선명하게 한다.
                var scaler = canvas.GetComponent<CanvasScaler>();
                prevScaleModes[i] = scaler != null ? scaler.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
                prevScaleFactors[i] = scaler != null ? scaler.scaleFactor : 1f;
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = h / 800f;
                }
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = cam.nearClipPlane + 0.2f;
            }
            yield return null;
            // 배율이 바뀌어도 Text는 예전 픽셀 크기로 만든 글리프를 그대로 늘려 쓰므로 다시 만들게 한다.
            foreach (var canvas in canvases) Redraw(canvas);
            yield return null;
            yield return null;
            FixPrompt(boot, cam);
            if (boot.StatusBars != null) boot.StatusBars.Refresh(); // 카메라 기준으로 다시 투영
            if (boot.Trucks != null) boot.Trucks.Refresh();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            float spread = Spread(tex);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);

            cam.targetTexture = null;
            for (int i = 0; i < canvases.Count; i++)
            {
                var canvas = canvases[i];
                canvas.renderMode = prevModes[i];
                canvas.worldCamera = null;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.scaleFactor = prevScaleFactors[i];
                    scaler.uiScaleMode = prevScaleModes[i];
                }
                Redraw(canvas);
            }
            Reframe(boot.World);
            rt.Release();
            Object.Destroy(rt);

            yield return null;
            Assert.IsTrue(File.Exists(path), path + " not written");
            long size = new FileInfo(path).Length;
            Debug.Log($"[Screenshot] wrote {path} via RenderTexture {w}x{h}, {size} bytes, luminance spread {spread:F3}");
            Assert.Greater(spread, 0.05f, path + " looks blank (uniform colour)");
            Assert.Greater(size, MinBytes, path + " is too small (blank render?)");
        }

        private static void Redraw(Canvas canvas)
        {
            foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.SetAllDirty();
        }

        /// <summary>WorldBuilder.FrameLot(현재 크기)를 다시 불러 카메라 비율(=RenderTexture 비율)에 맞춘다.</summary>
        private static void Reframe(WorldBuilder world)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            int n = (int)typeof(WorldBuilder).GetField("framedSize", flags).GetValue(world);
            if (n < 0) return;
            typeof(WorldBuilder).GetMethod("FrameLot", flags).Invoke(world, new object[] { n });
        }

        /// <summary>HUD는 프롬프트를 Overlay 캔버스 기준(camera=null)으로 놓는다. ScreenSpaceCamera 동안에는 카메라 기준으로 다시 놓는다.</summary>
        private static void FixPrompt(GameBootstrap boot, Camera cam)
        {
            var prompt = boot.Hud.Root.Find("Prompt") as RectTransform;
            if (prompt == null || !prompt.gameObject.activeSelf) return;
            var (fx, fy) = boot.Session.Worker.FacingTile();
            var sp = cam.WorldToScreenPoint(HudController.PromptAnchor(boot.Session, fx, fy));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(boot.Hud.Root, sp, cam, out var lp);
            prompt.anchoredPosition = lp + boot.Hud.Root.rect.size * 0.5f;
        }

        /// <summary>표본 픽셀 밝기의 최대-최소. 0에 가까우면 빈 화면이다.</summary>
        private static float Spread(Texture2D t)
        {
            float lo = 1f, hi = 0f;
            for (int i = 0; i < 400; i++)
            {
                var c = t.GetPixel((i * 37) % t.width, (i * 53) % t.height);
                float v = c.grayscale;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            return hi - lo;
        }
    }
}
