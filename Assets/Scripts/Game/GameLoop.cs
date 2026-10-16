using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class GameLoop : MonoBehaviour
    {
        public const float TickSeconds = 0.05f;
        private const float MaxFrameSeconds = 0.25f;
        private float accumulator;

        private void Update()
        {
            var session = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            if (session == null) return;
            accumulator += Mathf.Min(Time.deltaTime, MaxFrameSeconds);
            while (accumulator >= TickSeconds)
            {
                accumulator -= TickSeconds;
                session.Tick(TickSeconds);
            }
        }
    }
}
