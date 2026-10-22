using System;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>씬 설정 없이 Play를 누르면 모든 것을 코드로 조립하는 진입점.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public GameSession Session { get; private set; }
        public ModelCatalog Catalog { get; private set; }
        public Transform WorldRoot { get; private set; }
        public GameLoop Loop { get; private set; }
        public WorldBuilder World { get; private set; }
        public CellViews Cells { get; private set; }
        public WorkerView Worker { get; private set; }
        /// <summary>설치 미리보기(스펙 11.9).</summary>
        public PlacementGhost Ghost { get; private set; }
        public InputController Input { get; private set; }
        public HudController Hud { get; private set; }
        public StatusBars StatusBars { get; private set; }
        /// <summary>공장 탭·다른 공장 요약 칩·경보·옮길 때 페이드(스펙 19.3).</summary>
        public FactoryBar Factories { get; private set; }
        /// <summary>동쪽 트럭 적재 알약과 출발·도착 연출(스펙 18).</summary>
        public TruckViews Trucks { get; private set; }
        public Panels Panels { get; private set; }
        /// <summary>시작 화면(스펙 12.1). 부팅 직후 StartScreen.ShowOnBoot이면 뜬다.</summary>
        public StartScreen StartScreen { get; private set; }
        /// <summary>환경설정(스펙 13). 부팅 때 PlayerPrefs에서 읽고 화면 값을 적용한다.</summary>
        public GameSettings Settings { get; private set; }
        public AudioDirector Audio { get; private set; }

        public event Action<GameSession> SessionReplaced;
        /// <summary>지금 공장이 바뀌었다(스펙 19.3). 월드·칸 뷰는 이미 새 공장에 묶인 뒤다. 세션 교체(SessionReplaced) 때는 발생하지 않는다.</summary>
        public event Action<GameSession> FactorySwitched;
        private Company watchedCompany;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate() => Ensure();

        public static GameBootstrap Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ParcelFactory");
            return go.AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Settings = GameSettings.Load();
            if (Settings.DisplaySaved) Settings.ApplyDisplay(this); // 저장된 화면 값이 없으면 첫 실행 화면을 그대로 둔다
            Catalog = Resources.Load<ModelCatalog>("PF_Catalog");
            if (Catalog == null) Debug.LogError("PF_Catalog not found. Run CatalogBuilder.BuildAll in the editor.");
            WorldRoot = new GameObject("World").transform;
            WorldRoot.SetParent(transform, false);
            Loop = gameObject.AddComponent<GameLoop>();
            World = gameObject.AddComponent<WorldBuilder>();
            Cells = gameObject.AddComponent<CellViews>();
            Input = gameObject.AddComponent<InputController>();
            gameObject.AddComponent<PackageViews>();
            Session = new GameSession();
            World.Build(Session);
            Cells.Bind(Session);
            var workerGo = new GameObject("WorkerView");
            workerGo.transform.SetParent(WorldRoot, false);
            Worker = workerGo.AddComponent<WorkerView>();
            var ghostGo = new GameObject("PlacementGhost");
            ghostGo.transform.SetParent(WorldRoot, false);
            Ghost = ghostGo.AddComponent<PlacementGhost>();
            Hud = gameObject.AddComponent<HudController>();
            StatusBars = gameObject.AddComponent<StatusBars>();
            Factories = gameObject.AddComponent<FactoryBar>();
            Trucks = gameObject.AddComponent<TruckViews>();
            Panels = gameObject.AddComponent<Panels>();
            StartScreen = gameObject.AddComponent<StartScreen>();
            Audio = gameObject.AddComponent<AudioDirector>();
            WatchCompany(Session);
            // B2~B6는 Awake 안에서 컴포넌트를 추가하고 Session에 직접 묶는다. 이 Invoke는 Awake 밖에서 구독한 쪽(테스트 등)에는 닿지 않는다.
            SessionReplaced?.Invoke(Session);
        }

        /// <summary>세션의 회사가 공장을 옮기면 월드를 그 공장에 다시 묶는다.</summary>
        private void WatchCompany(GameSession s)
        {
            if (watchedCompany != null) { watchedCompany.FactorySwitched -= OnFactorySwitched; watchedCompany.FactoryAdded -= WorldBuilder.Prewarm; }
            watchedCompany = s?.Company;
            if (watchedCompany == null) return;
            watchedCompany.FactorySwitched += OnFactorySwitched;
            watchedCompany.FactoryAdded += WorldBuilder.Prewarm; // 새로 산 공장의 바닥 텍스처를 미리 굽는다
            foreach (var f in watchedCompany.Factories) WorldBuilder.Prewarm(f); // 불러온(또는 새) 회사의 공장 전부
        }

        private void OnFactorySwitched(int index)
        {
            if (World != null) World.Bind(Session);
            if (Cells != null) Cells.Bind(Session);
            FactorySwitched?.Invoke(Session);
        }

        private void OnDestroy() => WatchCompany(null);

        // 슬라이더 드래그 중의 소리 값은 메모리에만 있다. 앱이 내려가거나 끝날 때 디스크에 쓴다.
        private void OnApplicationPause(bool paused) { if (paused && Settings != null) Settings.Flush(); }
        private void OnApplicationQuit() { if (Settings != null) Settings.Flush(); }

        public void StartNewGame() => ReplaceSession(new GameSession());

        public void ReplaceSession(GameSession s)
        {
            Session = s;
            WatchCompany(s);
            if (World != null) World.Bind(s);
            if (Cells != null) Cells.Bind(s);
            Input?.ClearTool();
            SessionReplaced?.Invoke(s);
        }
    }
}
