namespace ParcelFactory.Core
{
    public sealed class GameSession
    {
        public readonly FactoryGrid Grid;
        public readonly Economy Economy;
        public readonly ConveyorSim Sim;
        public readonly Spawner Spawner;
        public readonly Toolbar Toolbar;
        public readonly BuildService Build;
        public readonly UnlockService Unlocks;

        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        public GameSession() : this(new Economy()) { }

        public GameSession(Economy economy)
        {
            Grid = new FactoryGrid();
            Economy = economy;
            Sim = new ConveyorSim(Grid, Economy);
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, Economy, Toolbar, Sim);
            Unlocks = new UnlockService(Economy, Grid, Toolbar);
        }

        public void Tick(float dt)
        {
            if (Paused || dt <= 0f) return;
            float d = dt * Speed;
            Spawner.Tick(d);
            Sim.Tick(d);
            Build.Tick(d);
        }
    }
}
