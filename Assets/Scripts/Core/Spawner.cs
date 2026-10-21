using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    /// <summary>입고 주기: 처음엔 8초에 1개, 성공 배송 5건마다 0.5초씩 빨라져 3초에서 멈춘다(스펙 3.5).
    /// 입고구마다 자기 타이머를 가진다(스펙 16.2). 처음부터 있는 (0,2)는 0에서, 나중에 생긴 입고구는 간격의 절반에서 시작해
    /// 첫 택배가 생긴 뒤 간격의 절반 뒤에 나온다. 새 입고구는 다음 Tick(또는 TimerAt)에서 격자를 보고 찾는다.</summary>
    public sealed class Spawner
    {
        public const float StartInterval = 8f;
        public const float MinInterval = 3f;
        public const int DeliveriesPerStep = 5;
        public const float StepSeconds = 0.5f;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;
        private readonly Dictionary<(int, int), float> timers = new Dictionary<(int, int), float>();

        /// <summary>처음부터 있는 입고구 (0,2)의 타이머(이전 저장·테스트와 같은 뜻).</summary>
        public float Timer
        {
            get => TimerAt(FactoryGrid.InletX, FactoryGrid.InletY);
            set => SetTimer(FactoryGrid.InletX, FactoryGrid.InletY, value);
        }

        /// <summary>현재 입고 주기(초). 성공 배송 수에 따라 줄어든다. 모든 입고구에 같다.</summary>
        public float Interval => IntervalFor(sim.DeliveredCount);

        public static float IntervalFor(int delivered) =>
            Math.Max(MinInterval, StartInterval - StepSeconds * (Math.Max(0, delivered) / DeliveriesPerStep));

        public Spawner(FactoryGrid grid, ConveyorSim sim)
        {
            this.grid = grid;
            this.sim = sim;
        }

        private bool IsInlet(int x, int y) => grid.Get(x, y).Type == CellType.Inlet;

        /// <summary>격자에 새로 생긴 입고구에 타이머를 붙인다: (0,2)는 0, 나머지는 간격의 절반.</summary>
        private void Sync()
        {
            foreach (var t in FactoryGrid.InletTiles)
                if (!timers.ContainsKey(t) && IsInlet(t.x, t.y))
                    timers[t] = t == (FactoryGrid.InletX, FactoryGrid.InletY) ? 0f : Interval / 2f;
        }

        /// <summary>입고구 (x, y)의 타이머. 입고구가 아니면 0.</summary>
        public float TimerAt(int x, int y)
        {
            Sync();
            return timers.TryGetValue((x, y), out var t) ? t : 0f;
        }

        /// <summary>저장 복원용. 입고구가 아닌 칸이거나 음수·NaN·무한이면 ArgumentException.</summary>
        public void SetTimer(int x, int y, float value)
        {
            if (!grid.InBounds(x, y) || !IsInlet(x, y)) throw new ArgumentException($"({x},{y}) is not an inlet");
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            Sync();
            timers[(x, y)] = value;
        }

        /// <summary>지금 있는 입고구들과 타이머(InletTiles 순서). 저장용.</summary>
        public List<(int x, int y, float timer)> Timers()
        {
            Sync();
            var list = new List<(int, int, float)>();
            foreach (var t in FactoryGrid.InletTiles)
                if (timers.TryGetValue(t, out var v)) list.Add((t.x, t.y, v));
            return list;
        }

        public void Tick(float dt)
        {
            Sync();
            foreach (var t in FactoryGrid.InletTiles)
            {
                if (!timers.TryGetValue(t, out var timer)) continue;
                timer += dt;
                while (timer >= Interval)
                {
                    timer -= Interval;
                    sim.TrySpawn(t.x, t.y, PackageKind.Normal, out _); // 앞이 막혀 있으면 건너뛴다(기존 규칙)
                }
                timers[t] = timer;
            }
        }
    }
}
