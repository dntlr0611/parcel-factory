using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 음악·배경소리(스펙 13.1). AudioSource 두 개로 Resources/Audio/music-shift, Resources/Audio/ambient-factory를 반복 재생한다.
    /// 음원 파일이 없으면 그 소리는 조용히 건너뛴다(볼륨·배수 계산과 설정 저장은 그대로 동작).
    /// 볼륨 = 설정값². 배경소리 배수: 근무 1 · 점심 0.5 · 시작 화면/일시정지/하루 요약 0, 0.5초에 걸쳐 바뀐다. 음악은 늘 재생한다.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public sealed class AudioDirector : MonoBehaviour
    {
        public const string MusicPath = "Audio/music-shift", AmbientPath = "Audio/ambient-factory";
        public const float AmbientFadeSeconds = 0.5f, LunchAmbient = 0.5f;

        public AudioSource MusicSource { get; private set; }
        public AudioSource AmbientSource { get; private set; }
        /// <summary>음원이 있는가(설정 카드가 "음원 없음" 안내를 띄울지 정한다).</summary>
        public bool HasMusic => MusicSource != null && MusicSource.clip != null;
        public bool HasAmbient => AmbientSource != null && AmbientSource.clip != null;
        /// <summary>지금 배경소리 배수(목표 쪽으로 보간 중).</summary>
        public float AmbientMultiplier { get; private set; }

        private GameBootstrap boot;

        /// <summary>배경소리 목표 배수: 시작 화면·일시정지·하루 요약이면 0, 점심 0.5, 근무 1.</summary>
        public static float AmbientTarget(bool startScreenOpen, bool paused, bool summaryOpen, DayPhase phase)
        {
            if (startScreenOpen || paused || summaryOpen || phase == DayPhase.Closed) return 0f;
            return phase == DayPhase.Lunch ? LunchAmbient : 1f;
        }

        private void Awake()
        {
            MusicSource = MakeSource("Music", MusicPath);
            AmbientSource = MakeSource("Ambient", AmbientPath);
        }

        private AudioSource MakeSource(string name, string path)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f;
            var clip = Resources.Load<AudioClip>(path);
            if (clip != null) { src.clip = clip; src.Play(); }
            return src;
        }

        private void Start()
        {
            boot = GameBootstrap.Instance;
            // 씬에는 AudioListener가 없다(카메라를 코드로 만든다). 없으면 메인 카메라에 붙인다.
            if (FindFirstObjectByType<AudioListener>() == null)
            {
                var cam = boot != null && boot.World != null && boot.World.MainCamera != null ? boot.World.MainCamera.gameObject : gameObject;
                cam.AddComponent<AudioListener>();
            }
            AmbientMultiplier = CurrentTarget();
            Apply();
        }

        private float CurrentTarget()
        {
            if (boot == null || boot.Session == null) return 0f;
            var s = boot.Session;
            bool start = boot.StartScreen != null && boot.StartScreen.IsOpen;
            bool summary = boot.Panels != null && boot.Panels.SummaryOpen;
            return AmbientTarget(start, s.Paused, summary, s.Clock.Phase);
        }

        private void Update()
        {
            AmbientMultiplier = Mathf.MoveTowards(AmbientMultiplier, CurrentTarget(), Time.unscaledDeltaTime / AmbientFadeSeconds);
            Apply();
        }

        /// <summary>설정값과 배수를 두 AudioSource 볼륨에 반영한다.</summary>
        private void Apply()
        {
            var settings = boot != null ? boot.Settings : null;
            int music = settings != null ? settings.Music : GameSettings.DefaultMusic;
            int ambient = settings != null ? settings.Ambient : GameSettings.DefaultAmbient;
            MusicSource.volume = GameSettings.Volume(music);
            AmbientSource.volume = GameSettings.Volume(ambient) * AmbientMultiplier;
        }
    }
}
