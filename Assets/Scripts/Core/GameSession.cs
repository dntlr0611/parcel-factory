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
        public readonly WorkerService Worker;

        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        public GameSession() : this(new Economy(), null) { }

        public GameSession(Economy economy) : this(economy, null) { }

        public GameSession(Economy economy, PackageFactory factory)
        {
            Grid = new FactoryGrid();
            Economy = economy;
            Sim = new ConveyorSim(Grid, Economy, factory);
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, Economy, Toolbar, Sim);
            Unlocks = new UnlockService(Economy, Grid, Toolbar);
            Worker = new WorkerService(Grid, Sim, Build);
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
