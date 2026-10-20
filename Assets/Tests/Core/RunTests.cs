using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class RunTests
    {
        private static WorkerService Make(out Worker w)
        {
            var s = new GameSession();
            w = s.Worker.Worker;
            w.X = 2f; w.Y = 4f; w.Stamina = 100f;
            return s.Worker;
        }

        [Test]
        public void Running_IsFasterAndDrainsMore()
        {
            var ws = Make(out var w);
            Assert.IsTrue(ws.Move(0f, -1f, 0.1f, run: true));
            Assert.IsTrue(ws.IsRunning);
            Assert.AreEqual(4f - WorkerService.Speed * WorkerService.RunSpeedMultiplier * 0.1f, w.Y, 1e-4f);
            Assert.AreEqual(100f - 3.0f * 0.1f, w.Stamina, 1e-4f);
            Assert.AreEqual(3.0f, WorkerService.RunDrain);

            var ws2 = Make(out var w2);
            Assert.IsTrue(ws2.Move(0f, -1f, 0.1f, run: false));
            Assert.IsFalse(ws2.IsRunning);
            Assert.AreEqual(4f - WorkerService.Speed * 0.1f, w2.Y, 1e-4f);
            Assert.AreEqual(100f, w2.Stamina, 1e-4f); // 걷기는 소모 없음(스펙 11.5)
        }

        [Test]
        public void Running_WhileCarrying_UsesRunCarryDrain()
        {
            var ws = Make(out var w);
            w.Carrying = new PackageFactory(new System.Random(1), 0.0).Create(1, 0, 0, Direction.East);
            Assert.IsTrue(ws.Move(0f, 1f, 0.5f, run: true)); // 북쪽은 비어 있다 (동쪽 (5,4)는 팔레트)
            Assert.AreEqual(100f - 5.5f * 0.5f, w.Stamina, 1e-4f);
            Assert.AreEqual(5.5f, WorkerService.RunCarryDrain);
        }

        [Test]
        public void CannotRun_BelowMinStamina_FallsBackToWalking()
        {
            var ws = Make(out var w);
            w.Stamina = WorkerService.RunMinStamina - 1f;
            Assert.IsFalse(ws.CanRun);
            float factor = ws.SpeedFactor; // 이동 전 값(소모 후에는 바뀐다)
            Assert.IsTrue(ws.Move(1f, 0f, 0.1f, run: true));
            Assert.IsFalse(ws.IsRunning);
            Assert.AreEqual(2f + WorkerService.Speed * factor * 0.1f, w.X, 1e-4f);
        }

        [Test]
        public void IsRunning_ClearsWhenBlockedOrIdle()
        {
            var ws = Make(out var w);
            ws.Move(1f, 0f, 0.1f, run: true);
            Assert.IsTrue(ws.IsRunning);
            Assert.IsFalse(ws.Move(0f, 0f, 0.1f, run: true)); // 입력 없음
            Assert.IsFalse(ws.IsRunning);
        }

        [Test]
        public void LegacyOverload_Walks()
        {
            var ws = Make(out var w);
            ws.Move(1f, 0f, 0.1f);
            Assert.IsFalse(ws.IsRunning);
        }
    }
}
