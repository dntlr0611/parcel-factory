namespace ParcelFactory.Core
{
    public sealed class Spawner
    {
        public const float Interval = 3f;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;

        public float Timer { get; set; }

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
