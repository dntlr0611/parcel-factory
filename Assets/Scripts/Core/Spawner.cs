using System;

namespace ParcelFactory.Core
{
    /// <summary>입고 주기: 처음엔 8초에 1개, 성공 배송 5건마다 0.5초씩 빨라져 3초에서 멈춘다(스펙 3.5).</summary>
    public sealed class Spawner
    {
        public const float StartInterval = 8f;
        public const float MinInterval = 3f;
        public const int DeliveriesPerStep = 5;
        public const float StepSeconds = 0.5f;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;

        public float Timer { get; set; }

        /// <summary>현재 입고 주기(초). 성공 배송 수에 따라 줄어든다.</summary>
        public float Interval => IntervalFor(sim.DeliveredCount);

        public static float IntervalFor(int delivered) =>
            Math.Max(MinInterval, StartInterval - StepSeconds * (Math.Max(0, delivered) / DeliveriesPerStep));

        public Spawner(FactoryGrid grid, ConveyorSim sim)
        {
            this.grid = grid;
            this.sim = sim;
        }

        public void Tick(float dt)
        {
            Timer += dt;
            while (Timer >= Interval)
            {
                Timer -= Interval;
                sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _);
            }
        }
    }
}
