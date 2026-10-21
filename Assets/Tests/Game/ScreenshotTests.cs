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
        private static IEnumerator Capture(GameBootstrap boot, string file, int w, int h)
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
