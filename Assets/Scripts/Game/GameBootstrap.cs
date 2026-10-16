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

        public event Action<GameSession> SessionReplaced;

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
            Catalog = Resources.Load<ModelCatalog>("PF_Catalog");
            if (Catalog == null) Debug.LogError("PF_Catalog not found. Run CatalogBuilder.BuildAll in the editor.");
            WorldRoot = new GameObject("World").transform;
            WorldRoot.SetParent(transform, false);
            Loop = gameObject.AddComponent<GameLoop>();
            Session = new GameSession();
            // B2~B6는 Awake 안에서 컴포넌트를 추가하고 Session에 직접 묶는다. 이 Invoke는 Awake 밖에서 구독한 쪽(테스트 등)에는 닿지 않는다.
            SessionReplaced?.Invoke(Session);
        }

        public void StartNewGame() => ReplaceSession(new GameSession());

        public void ReplaceSession(GameSession s)
        {
            Session = s;
            SessionReplaced?.Invoke(s);
        }
    }
}
