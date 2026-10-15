using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class SaveSerializerTests
    {
        private static GameSession BuildSample()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.TryPurchase(UnlockId.Plot1);
            s.Unlocks.TryPurchase(UnlockId.RailNorth);
            s.Unlocks.TryPurchase(UnlockId.Splitter);
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Build.TryPlace(2, 2, CellType.Splitter, Direction.East);
            s.Build.TryPlace(2, 3, CellType.Conveyor, Direction.North);
            s.Build.TryPlace(7, 7, CellType.Conveyor, Direction.North);
            s.Tick(3f);      // 택배 1개 생성, 같은 틱에 (1,2)로 이동 (progress 0)
            s.Build.BeginDemolish(7, 7);
            s.Tick(0.5f);    // 택배 progress 0.5, DemolishRemaining 1.5, SpawnTimer 0.5
            s.Paused = true;
            s.Speed = 2f;
            return s;
        }

        [Test]
        public void Capture_RecordsEverything()
        {
            var st = SaveSerializer.Capture(BuildSample());
            Assert.AreEqual(1, st.Version);
            Assert.AreEqual(9, st.UnlockedSize);
            CollectionAssert.AreEquivalent(new[] { "Plot1", "RailNorth", "Splitter" }, st.Purchased);
            Assert.AreEqual(4, st.Cells.Count);
            Assert.AreEqual(1, st.Packages.Count);
            Assert.IsTrue(st.Paused);
            Assert.AreEqual(2f, st.Speed);
        }

        [Test]
        public void RoundTrip_ThroughJson_PreservesState()
        {
            var original = BuildSample();
            var json = SaveSerializer.ToJson(SaveSerializer.Capture(original));
            var restored = SaveSerializer.Restore(SaveSerializer.FromJson(json));

            Assert.AreEqual(original.Economy.Money, restored.Economy.Money);
            Assert.AreEqual(9, restored.Grid.UnlockedSize);
            Assert.IsTrue(restored.Toolbar.Owns(CellType.Conveyor, Direction.North));
            Assert.IsTrue(restored.Toolbar.SplitterOwned);
            Assert.IsTrue(restored.Unlocks.IsPurchased(UnlockId.Plot1));

            Assert.AreEqual(CellType.Splitter, restored.Grid.Get(2, 2).Type);
            Assert.AreEqual(Direction.North, restored.Grid.Get(2, 3).Direction);
            Assert.IsTrue(restored.Grid.Get(7, 7).Demolishing);
            Assert.AreEqual(1.5f, restored.Grid.Get(7, 7).DemolishRemaining, 1e-4f);

            Assert.AreEqual(1, restored.Sim.Packages.Count);
            var p = restored.Sim.Packages[0];
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(0.5f, p.Progress, 1e-4f);
            Assert.AreEqual(p.Id, restored.Grid.Get(1, 2).OccupantId);
            Assert.AreEqual(original.Sim.NextId, restored.Sim.NextId);
            Assert.AreEqual(original.Spawner.Timer, restored.Spawner.Timer, 1e-4f);
            Assert.IsTrue(restored.Paused);
            Assert.AreEqual(2f, restored.Speed);
        }

        [Test]
        public void RestoredSession_ContinuesSimulating()
        {
            var restored = SaveSerializer.Restore(SaveSerializer.Capture(BuildSample()));
            restored.Paused = false;
            restored.Speed = 1f;
            restored.Tick(0.5f); // progress 0.5 → 1.0, 분배기로 진입
            Assert.AreEqual((2, 2), (restored.Sim.Packages[0].X, restored.Sim.Packages[0].Y));
            Assert.AreEqual(Direction.North, restored.Sim.Packages[0].Heading);
        }

        [Test]
        public void SplitterToggle_SurvivesRoundTrip()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.TryPurchase(UnlockId.Splitter);
            s.Build.TryPlace(1, 2, CellType.Splitter, Direction.East);
            s.Grid.Get(1, 2).SplitterToggle = true;
            var restored = SaveSerializer.Restore(SaveSerializer.Capture(s));
            Assert.IsTrue(restored.Grid.Get(1, 2).SplitterToggle);
        }

        [Test]
        public void WrongVersion_Throws()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Version = 99;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        [Test]
        public void GarbageJson_Throws()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson("null"));
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson("{not json"));
        }
    }
}
