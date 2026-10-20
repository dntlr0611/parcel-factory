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

                // (c2) 설치 프롬프트: 빈손이 아닌 레일 도구로 빈 칸 (3,3)을 바라본다 → "레일 설치 · ₩10   [E]".
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
            var canvas = boot.Hud.Root.GetComponent<Canvas>();
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            var prevMode = canvas.renderMode;
            // 배치 모드 화면(640x480) 기준 배율 대신 RenderTexture 높이 기준(게임 기준 해상도 1280x800의 800)으로 HUD를 키워 글자를 선명하게 한다.
            var scaler = canvas.GetComponent<CanvasScaler>();
            var prevScaleMode = scaler != null ? scaler.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
            float prevScaleFactor = scaler != null ? scaler.scaleFactor : 1f;
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = h / 800f;
            }
            cam.targetTexture = rt;
            Reframe(boot.World);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.2f;
            yield return null;
            // 배율이 바뀌어도 Text는 예전 픽셀 크기로 만든 글리프를 그대로 늘려 쓰므로 다시 만들게 한다.
            Redraw(canvas);
            yield return null;
            yield return null;
            FixPrompt(boot, cam);
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
            canvas.renderMode = prevMode;
            canvas.worldCamera = null;
            if (scaler != null)
            {
                scaler.scaleFactor = prevScaleFactor;
                scaler.uiScaleMode = prevScaleMode;
            }
            Redraw(canvas);
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
