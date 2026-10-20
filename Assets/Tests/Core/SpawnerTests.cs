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
        public void StartsAtEightSeconds()
        {
            Assert.AreEqual(8f, spawner.Interval, 1e-4f);
            spawner.Tick(7.9f);
            Assert.AreEqual(0, sim.Packages.Count);
            spawner.Tick(0.1f);
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual((0, 2), (sim.Packages[0].X, sim.Packages[0].Y));
        }

        [Test]
        public void IntervalShrinksWithDeliveriesDownToThreeSeconds()
        {
            Assert.AreEqual(8f, Spawner.IntervalFor(0), 1e-4f);
            Assert.AreEqual(8f, Spawner.IntervalFor(4), 1e-4f);
            Assert.AreEqual(7.5f, Spawner.IntervalFor(5), 1e-4f);
            Assert.AreEqual(5f, Spawner.IntervalFor(30), 1e-4f);
            Assert.AreEqual(3f, Spawner.IntervalFor(50), 1e-4f);
            Assert.AreEqual(3f, Spawner.IntervalFor(500), 1e-4f);
            Assert.AreEqual(8f, Spawner.IntervalFor(-3), 1e-4f);
        }

        [Test]
        public void UsesDeliveredCountFromSim()
        {
            sim.DeliveredCount = 10;
            Assert.AreEqual(7f, spawner.Interval, 1e-4f);
            spawner.Tick(6.9f);
            Assert.AreEqual(0, sim.Packages.Count);
            spawner.Tick(0.1f);
            Assert.AreEqual(1, sim.Packages.Count);
        }

        [Test]
        public void SkipsWhenInletOccupied()
        {
            spawner.Tick(8f);
            spawner.Tick(8f); // sim은 틱하지 않았으므로 입고구가 여전히 점유됨
            Assert.AreEqual(1, sim.Packages.Count);
        }

        [Test]
        public void TimerCarriesRemainder()
        {
            spawner.Tick(8.5f);
            Assert.AreEqual(0.5f, spawner.Timer, 1e-4f);
        }
    }
}
