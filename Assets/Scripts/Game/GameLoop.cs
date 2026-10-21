using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class GameLoop : MonoBehaviour
    {
        public const float TickSeconds = 0.05f;
        private const float MaxFrameSeconds = 0.25f;
        private float accumulator;

        public int Ticks { get; private set; }

        private void Update()
        {
            var session = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            if (session == null) return;
            var start = GameBootstrap.Instance.StartScreen;
            if (start != null && start.IsOpen) return; // 시작 화면이 떠 있는 동안 틱을 멈춘다(스펙 12.1)
            accumulator += Mathf.Min(Time.deltaTime, MaxFrameSeconds);
            while (accumulator >= TickSeconds)
            {
                accumulator -= TickSeconds;
                session.Tick(TickSeconds);
                Ticks++;
            }
        }
    }
}
