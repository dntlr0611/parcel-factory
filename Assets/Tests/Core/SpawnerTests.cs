using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class SpawnerTests
    {
        private FactoryGrid grid;
        private ConveyorSim sim;
        private Spawner spawner;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            sim = new ConveyorSim(grid, new Economy());
            spawner = new Spawner(grid, sim);
        }

        [Test]
        public void NothingBeforeInterval()
        {
            spawner.Tick(2.9f);
            Assert.AreEqual(0, sim.Packages.Count);
        }

        [Test]
        public void SpawnsAtInletEveryThreeSeconds()
        {
            spawner.Tick(3f);
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual((0, 2), (sim.Packages[0].X, sim.Packages[0].Y));
        }

        [Test]
        public void SkipsWhenInletOccupied()
        {
            spawner.Tick(3f);
            spawner.Tick(3f); // sim은 틱하지 않았으므로 입고구가 여전히 점유됨
            Assert.AreEqual(1, sim.Packages.Count);
        }

        [Test]
        public void TimerCarriesRemainder()
        {
            spawner.Tick(3.5f);
            Assert.AreEqual(0.5f, spawner.Timer, 1e-4f);
        }
    }
}
