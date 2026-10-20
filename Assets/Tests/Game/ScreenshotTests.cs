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

                // (b) 샘플 배치: 입고 → 레일 → 검사대 → 레일 → 파랑 팔레트(5,2)에 상자 3개, 작업자는 상자를 들고 트럭 도크(11,2)로 가는 중.
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
                    pp.Inspect(false);
                    Assert.IsTrue(s.Sim.PutOnPallet(pp, FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY), "pile " + i);
                }
                var carried = factory.Create(s.Sim.NextId++, 8, 2, Direction.East);
                carried.Inspect(false);
                s.Worker.Worker.Carrying = carried;
                s.Worker.Worker.X = 8.0f; s.Worker.Worker.Y = 2.0f; s.Worker.Worker.Facing = Direction.East;
                boot.Input.SelectSlot(0);
                yield return new WaitForSeconds(1.0f);
                // 1초 동안 흘러간 택배와 상관없이 입구 하나, 벨트 위 하나를 확실히 둔다.
                s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _);
                s.Sim.TrySpawn(2, 2, PackageKind.Normal, out _);
                yield return new WaitForSeconds(0.2f);
                Debug.Log($"[Screenshot] packages on line: {s.Sim.Packages.Count}, pallet pile {s.Sim.PalletCount(5, 2)}, selected slot {boot.Input.SelectedSlot}");
                yield return Capture(boot, "unity-demo.png", Width, Height);

                // (c) 터치 모드
                boot.Hud.ForceTouchMode(true);
                yield return null;
                yield return new WaitForSeconds(0.2f);
                yield return Capture(boot, "unity-touch.png", Width, Height);
                boot.Hud.ForceTouchMode(false);
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
