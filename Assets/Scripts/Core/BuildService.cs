using System;

namespace ParcelFactory.Core
{
    public enum BuildResult { Ok, Locked, Occupied, NotOwned, NotEnoughMoney, NotDemolishable, AlreadyDemolishing }

    public sealed class BuildService
    {
        public const float DemolishSeconds = 2f;
        public const float RefundRate = 0.8f;

        /// <summary>float 누적 오차 허용치. 1.9+0.1 같은 합이 정확히 0이 되지 않는 문제를 흡수한다.</summary>
        private const float TimerEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly Toolbar toolbar;
        private readonly ConveyorSim sim;

        public event Action<int, int> CellChanged;

        public BuildService(FactoryGrid grid, Economy economy, Toolbar toolbar, ConveyorSim sim)
        {
            this.grid = grid;
            this.economy = economy;
            this.toolbar = toolbar;
            this.sim = sim;
        }

        public static int RefundFor(CellType type) =>
            (int)Math.Round(BuildingCosts.Cost(type) * RefundRate);

        public BuildResult TryPlace(int x, int y, CellType type, Direction dir)
        {
            if (!grid.IsUnlocked(x, y)) return BuildResult.Locked;
            if (grid.Get(x, y).Type != CellType.Empty) return BuildResult.Occupied;
            if (!toolbar.Owns(type, dir)) return BuildResult.NotOwned;
            if (!economy.TrySpend(BuildingCosts.Cost(type))) return BuildResult.NotEnoughMoney;
            grid.Place(x, y, type, dir);
            CellChanged?.Invoke(x, y);
            return BuildResult.Ok;
        }

        public BuildResult BeginDemolish(int x, int y)
        {
            if (!grid.InBounds(x, y)) return BuildResult.NotDemolishable;
            var c = grid.Get(x, y);
            if (!c.IsBuilding) return BuildResult.NotDemolishable;
            if (c.Demolishing) return BuildResult.AlreadyDemolishing;
            c.Demolishing = true;
            c.DemolishRemaining = DemolishSeconds;
            CellChanged?.Invoke(x, y);
            return BuildResult.Ok;
        }

        public void Tick(float dt)
        {
            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = grid.Get(x, y);
                    if (!c.Demolishing) continue;
                    c.DemolishRemaining -= dt;
                    if (c.DemolishRemaining > TimerEpsilon) continue;
                    c.DemolishRemaining = 0f;

                    int refund = RefundFor(c.Type);
                    sim.RemovePackagesAt(x, y);
                    grid.Clear(x, y);
                    economy.Earn(refund);
                    CellChanged?.Invoke(x, y);
                }
            }
        }
    }
}
