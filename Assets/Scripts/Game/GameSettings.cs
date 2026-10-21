using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 환경설정(스펙 13): 음악·배경소리 크기(0~100), 전체 화면 여부, 해상도. PlayerPrefs(pf.*)에 저장하며 세이브 파일과는 별개다.
    /// 소리는 바꾸는 즉시 반영·저장하고, 화면 값은 SetDisplay(설정 카드의 적용)로만 바뀐다.
    /// </summary>
    public sealed class GameSettings
    {
        public const string KeyMusic = "pf.music", KeyAmbient = "pf.ambient", KeyFullscreen = "pf.fullscreen", KeyResW = "pf.resW", KeyResH = "pf.resH";
        public static readonly string[] Keys = { KeyMusic, KeyAmbient, KeyFullscreen, KeyResW, KeyResH };
        public const int DefaultMusic = 60, DefaultAmbient = 70, MinVolume = 0, MaxVolume = 100;
        public const int MinResolution = 320, MaxResolution = 16384;
        /// <summary>창 모드용 기본 해상도 후보(스펙 13.2). 모니터보다 큰 것은 뺀다.</summary>
        public static readonly Vector2Int[] WindowedDefaults = { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) };

        public int Music { get; private set; } = DefaultMusic;
        public int Ambient { get; private set; } = DefaultAmbient;
        public bool Fullscreen { get; private set; }
        /// <summary>렌더 픽셀 단위(Screen.resolutions·currentResolution·SetResolution 모두 픽셀이다. Retina에서 창의 포인트 크기는 절반).</summary>
        public Vector2Int Resolution { get; private set; }
        /// <summary>화면 값이 저장돼 있는가(pf.resW가 있으면). 없으면 부팅 때 화면을 건드리지 않고 지금 화면 값을 쓴다.</summary>
        public bool DisplaySaved { get; private set; }

        /// <summary>값이 바뀔 때마다(소리·화면 모두).</summary>
        public event Action Changed;

        /// <summary>테스트용: 모니터·지원 해상도를 대신한다. null이면 Screen에서 읽는다.</summary>
        public static Vector2Int? MonitorOverride;
        public static Vector2Int[] SupportedOverride;
        /// <summary>ApplyDisplay가 불린 횟수(플레이어 빌드가 아니라 실제로 바꾸지 않아도 센다). 부팅이 화면을 건드렸는지 테스트가 본다.</summary>
        public static int ApplyRequests;

        /// <summary>창 모드 상한: 모니터 폭 95%, 높이 85%(메뉴 막대·Dock·창 제목줄 여유). 고정 후보는 모니터의 40% 이상일 때만.</summary>
        public const float WindowedMaxWidth = 0.95f, WindowedMaxHeight = 0.85f, PresetMinFraction = 0.4f;
        /// <summary>창 모드 후보: 모니터 비율 그대로 50·60·70·80%. 기본 선택은 70%.</summary>
        public static readonly float[] WindowedFractions = { 0.5f, 0.6f, 0.7f, 0.8f };
        public const float DefaultWindowedFraction = 0.7f;

        public static int ClampVolume(int v) => Mathf.Clamp(v, MinVolume, MaxVolume);
        public static Vector2Int ClampResolution(Vector2Int r) => new Vector2Int(Mathf.Clamp(r.x, MinResolution, MaxResolution), Mathf.Clamp(r.y, MinResolution, MaxResolution));

        /// <summary>실제 볼륨: 설정값²(0~1). 귀에 고르게 들리도록 제곱한다.</summary>
        public static float Volume(int setting) { float t = ClampVolume(setting) / 100f; return t * t; }

        /// <summary>
        /// 모니터(바탕화면) 크기, 네이티브 픽셀. Screen.currentResolution은 전체 화면 창이면 그 창 크기를 돌려주므로(실측: 1280×800 전체 화면에서 1280×800)
        /// Display.main.systemWidth/Height → Screen.mainWindowDisplayInfo → Screen.currentResolution 순으로 0이 아닌 첫 값을 쓴다.
        /// 후보 목록·FitDisplay·설정 카드가 모두 이 한 곳을 쓴다.
        /// </summary>
        public static Vector2Int Monitor
        {
            get
            {
                if (MonitorOverride.HasValue) return MonitorOverride.Value;
                return PickMonitor(new Vector2Int(Display.main.systemWidth, Display.main.systemHeight), MainWindowDisplaySize(),
                    new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height));
            }
        }

        /// <summary>세 출처 중 가로·세로가 모두 0보다 큰 첫 값(모두 0이면 마지막 값).</summary>
        public static Vector2Int PickMonitor(Vector2Int system, Vector2Int windowDisplay, Vector2Int current)
        {
            if (system.x > 0 && system.y > 0) return system;
            if (windowDisplay.x > 0 && windowDisplay.y > 0) return windowDisplay;
            return current;
        }

        private static Vector2Int MainWindowDisplaySize() => TryMainWindowDisplay(out var d) ? new Vector2Int(d.width, d.height) : Vector2Int.zero;

        private static bool TryMainWindowDisplay(out DisplayInfo display)
        {
            try { display = Screen.mainWindowDisplayInfo; return true; }
            catch (System.Exception) { display = default; return false; } // 일부 플랫폼·배치 모드에서는 지원하지 않는다
        }

        /// <summary>PlayerPrefs에서 읽는다. 소리는 없으면 60·70. 화면은 pf.resW가 없으면 "저장 안 됨"으로 보고 지금 화면(Screen.width·height·fullScreen)을 쓴다.</summary>
        public static GameSettings Load()
        {
            var s = new GameSettings
            {
                Music = ClampVolume(PlayerPrefs.GetInt(KeyMusic, DefaultMusic)),
                Ambient = ClampVolume(PlayerPrefs.GetInt(KeyAmbient, DefaultAmbient)),
                DisplaySaved = PlayerPrefs.HasKey(KeyResW),
            };
            if (s.DisplaySaved)
            {
                s.Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) != 0;
                s.Resolution = ClampResolution(new Vector2Int(PlayerPrefs.GetInt(KeyResW, Screen.width), PlayerPrefs.GetInt(KeyResH, Screen.height)));
            }
            else
            {
                s.Fullscreen = Screen.fullScreen;
                s.Resolution = new Vector2Int(Screen.width, Screen.height);
            }
            return s;
        }

        /// <summary>소리 값을 디스크에 쓴다(슬라이더를 놓을 때·카드를 닫을 때·앱 종료/일시정지 때).</summary>
        public void Flush() => PlayerPrefs.Save();

        /// <summary>음악 크기. 자기 키(pf.music)만 쓴다. persist면 바로 디스크에 저장(키보드 ←/→), 아니면 Flush 때(드래그 중).</summary>
        public void SetMusic(int v, bool persist = true)
        {
            v = ClampVolume(v);
            if (v == Music) { if (persist) Flush(); return; }
            Music = v;
            PlayerPrefs.SetInt(KeyMusic, Music);
            if (persist) Flush();
            Changed?.Invoke();
        }

        public void SetAmbient(int v, bool persist = true)
        {
            v = ClampVolume(v);
            if (v == Ambient) { if (persist) Flush(); return; }
            Ambient = v;
            PlayerPrefs.SetInt(KeyAmbient, Ambient);
            if (persist) Flush();
            Changed?.Invoke();
        }

        /// <summary>화면 값(설정 카드의 적용). 화면 키 세 개만 저장하고 바로 화면에 반영한다.</summary>
        public void SetDisplay(bool fullscreen, Vector2Int resolution, MonoBehaviour logHost = null)
        {
            Fullscreen = fullscreen;
            Resolution = ClampResolution(resolution);
            DisplaySaved = true;
            PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(KeyResW, Resolution.x);
            PlayerPrefs.SetInt(KeyResH, Resolution.y);
            PlayerPrefs.Save();
            ApplyDisplay(logHost);
            Changed?.Invoke();
        }

        /// <summary>실제로 쓸 크기: 모니터보다 크면 줄이고, 창 모드면 상한(폭 95%·높이 85%) 안으로 비율을 지켜 줄인다(짝수).</summary>
        public static Vector2Int FitDisplay(Vector2Int requested, bool fullscreen, Vector2Int monitor)
        {
            float w = requested.x, h = requested.y;
            if (monitor.x > 0 && monitor.y > 0)
            {
                float maxW = fullscreen ? monitor.x : monitor.x * WindowedMaxWidth, maxH = fullscreen ? monitor.y : monitor.y * WindowedMaxHeight;
                float k = Mathf.Min(1f, maxW / w, maxH / h);
                w *= k; h *= k;
            }
            return new Vector2Int(Even(w), Even(h));
        }

        private static int Even(float v) => Mathf.Max(2, Mathf.FloorToInt(v / 2f) * 2);

        /// <summary>
        /// 화면 모드·해상도를 바꾼다(데스크톱 플레이어만; 에디터 게임 뷰·모바일·배치 모드는 건드리지 않는다). 크기는 FitDisplay로 맞추고,
        /// 지금 화면과 같으면 부르지 않는다. macOS는 여러 프레임에 걸쳐 바뀌므로 Screen 값이 요청과 같아질 때까지(최대 ApplyWaitFrames) 기다린 뒤
        /// Player.log에 "[Settings] apply ..." 한 줄을 남기고, 창 모드면 창을 그 디스플레이 가운데로 옮긴다.
        /// macOS: 전체 화면 = FullScreenWindow(배타적 전체 화면 없음), 창 모드 = Windowed(ProjectSettings resizableWindow 1).
        /// </summary>
        public void ApplyDisplay(MonoBehaviour logHost = null)
        {
            ApplyRequests++;
            if (Application.isEditor || Application.isMobilePlatform || Application.isBatchMode) return;
            var size = FitDisplay(Resolution, Fullscreen, Monitor);
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (size.x == Screen.width && size.y == Screen.height && mode == Screen.fullScreenMode) return;
            Screen.SetResolution(size.x, size.y, mode);
            if (logHost != null && logHost.isActiveAndEnabled) logHost.StartCoroutine(FinishApply(size, mode));
            else Debug.Log(ApplyLogLine(size, mode) + " (no wait)");
        }

        /// <summary>적용 뒤 Screen 값이 요청과 같아질 때까지 기다리는 최대 프레임.</summary>
        public const int ApplyWaitFrames = 60;

        /// <summary>Screen 값이 요청한 크기·모드와 같은가.</summary>
        public static bool ScreenMatches(Vector2Int size, FullScreenMode mode) => Screen.width == size.x && Screen.height == size.y && Screen.fullScreenMode == mode;

        /// <summary>창을 디스플레이 가운데에 놓는 왼쪽 위 좌표(디스플레이 왼쪽 위 기준, 픽셀). 창이 더 크면 0으로 붙인다.</summary>
        public static Vector2Int CenteredWindowPosition(Vector2Int display, Vector2Int window) =>
            new Vector2Int(Mathf.Max(0, (display.x - window.x) / 2), Mathf.Max(0, (display.y - window.y) / 2));

        /// <summary>
        /// 적용이 실제로 반영될 때까지 기다린다(macOS는 여러 프레임). 같아지면 로그, ApplyWaitFrames를 넘기면 " (timeout)".
        /// 창 모드면 그 뒤 Screen.MoveMainWindowTo로 창을 디스플레이 가운데에 둔다. 좌표는 문서대로 디스플레이 왼쪽 위 기준 픽셀이고,
        /// DisplayInfo.width/height와 Screen.width/height도 픽셀이라 Retina에서도 같은 단위로 계산한다.
        /// </summary>
        private static System.Collections.IEnumerator FinishApply(Vector2Int size, FullScreenMode mode)
        {
            int frames = 0;
            while (!ScreenMatches(size, mode) && frames < ApplyWaitFrames) { frames++; yield return null; }
            bool matched = ScreenMatches(size, mode);
            Debug.Log(ApplyLogLine(size, mode) + (matched ? "" : " (timeout)") + " after " + frames + " frames");
            if (mode != FullScreenMode.Windowed) yield break;
            if (!TryMainWindowDisplay(out var display) || display.width <= 0 || display.height <= 0) yield break;
            var pos = CenteredWindowPosition(new Vector2Int(display.width, display.height), new Vector2Int(Screen.width, Screen.height));
            var op = Screen.MoveMainWindowTo(display, pos);
            if (op != null) yield return op;
            Debug.Log("[Settings] window moved to " + pos.x + "," + pos.y + " on " + display.name + " " + display.width + "x" + display.height
                + " -> mainWindowPosition " + Screen.mainWindowPosition.x + "," + Screen.mainWindowPosition.y + " window " + Screen.width + "x" + Screen.height);
        }

        /// <summary>Player.log 확인용 한 줄: "[Settings] apply requested 2116x1374 Windowed -> screen 2116x1374 Windowed".</summary>
        public static string ApplyLogLine(Vector2Int requested, FullScreenMode mode) =>
            "[Settings] apply requested " + requested.x + "x" + requested.y + " " + mode + " -> screen " + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode;

        /// <summary>
        /// 해상도 후보(스펙 13.2, 모두 렌더 픽셀). 전체 화면: Screen.resolutions 가로×세로 중복 제거 + 모니터 크기.
        /// 창 모드: 모니터 비율 그대로 50·60·70·80%(짝수) + 고정 후보(1280×720 · 1600×900 · 1920×1080 · 2560×1440) 중 상한(폭 95%·높이 85%) 안이고
        /// 가로·세로 모두 모니터의 40% 이상인 것. 모니터 크기 그대로는 창 모드 후보가 아니다. 오름차순.
        /// </summary>
        public static List<Vector2Int> ResolutionOptions(bool fullscreen, IEnumerable<Vector2Int> supported, Vector2Int monitor)
        {
            var set = new HashSet<Vector2Int>();
            if (fullscreen)
            {
                if (supported != null)
                    foreach (var r in supported) if (r.x > 0 && r.y > 0) set.Add(r);
                if (monitor.x > 0 && monitor.y > 0) set.Add(monitor);
            }
            else if (monitor.x > 0 && monitor.y > 0)
            {
                foreach (var f in WindowedFractions) set.Add(new Vector2Int(Even(monitor.x * f), Even(monitor.y * f)));
                foreach (var r in WindowedDefaults)
                    if (FitsWindowed(r, monitor) && r.x >= monitor.x * PresetMinFraction && r.y >= monitor.y * PresetMinFraction) set.Add(r);
                set.Remove(monitor);
            }
            else foreach (var r in WindowedDefaults) set.Add(r);
            var list = new List<Vector2Int>(set);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        /// <summary>창 모드 상한 안인가(폭 ≤ 95%, 높이 ≤ 85%).</summary>
        public static bool FitsWindowed(Vector2Int r, Vector2Int monitor) => r.x <= monitor.x * WindowedMaxWidth && r.y <= monitor.y * WindowedMaxHeight;

        /// <summary>창 모드 기본 크기: 모니터의 70%.</summary>
        public static Vector2Int DefaultWindowed(Vector2Int monitor) => new Vector2Int(Even(monitor.x * DefaultWindowedFraction), Even(monitor.y * DefaultWindowedFraction));

        /// <summary>목록에서 target에 가장 가까운 후보의 번호(가로·세로 거리).</summary>
        public static int Nearest(IList<Vector2Int> options, Vector2Int target)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                float d = (options[i] - target).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>이 기기의 후보(모드별).</summary>
        public static List<Vector2Int> CurrentResolutionOptions(bool fullscreen)
        {
            IEnumerable<Vector2Int> supported = SupportedOverride;
            if (supported == null)
            {
                var list = new List<Vector2Int>();
                foreach (var r in Screen.resolutions) list.Add(new Vector2Int(r.width, r.height));
                supported = list;
            }
            return ResolutionOptions(fullscreen, supported, Monitor);
        }

        /// <summary>해상도 표기: "1920 × 1080".</summary>
        public static string ResolutionLabel(Vector2Int r) => r.x + " × " + r.y;
    }
}
