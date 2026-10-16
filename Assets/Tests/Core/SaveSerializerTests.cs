using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class SaveSerializerTests
    {
        private static GameSession BuildSample()
        {
            var s = new GameSession(new Economy(5000), new PackageFactory(new Random(11), 1.0));
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
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 4; s.Worker.Worker.Facing = Direction.West;
            s.Speed = 2f;
            return s;
        }

        [Test]
        public void Capture_RecordsEverything()
        {
            var st = SaveSerializer.Capture(BuildSample());
            Assert.AreEqual(2, st.Version);
            Assert.AreEqual(9, st.UnlockedSize);
            CollectionAssert.AreEquivalent(new[] { "Plot1", "RailNorth", "Splitter" }, st.Purchased);
            Assert.AreEqual(4, st.Cells.Count);
            Assert.AreEqual(1, st.Packages.Count);
            Assert.IsTrue(st.Paused);
            Assert.AreEqual(2f, st.Speed);
            Assert.AreEqual(1, st.Worker.X);
            Assert.AreEqual("West", st.Worker.Facing);
            Assert.IsNull(st.Worker.Carrying);
            Assert.IsTrue(st.Packages[0].Bad);
            Assert.IsTrue(st.Packages[0].LabelScale > st.Packages[0].LabelWeight);
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
            Assert.AreEqual(original.Sim.Packages[0].EntryHeading, p.EntryHeading);
            Assert.AreEqual(p.Id, restored.Grid.Get(1, 2).OccupantId);
            Assert.AreEqual(original.Sim.NextId, restored.Sim.NextId);
            Assert.AreEqual(original.Spawner.Timer, restored.Spawner.Timer, 1e-4f);
            Assert.IsTrue(restored.Paused);
            Assert.AreEqual(2f, restored.Speed);
            Assert.AreEqual((1, 4), (restored.Worker.Worker.X, restored.Worker.Worker.Y));
            Assert.AreEqual(Direction.West, restored.Worker.Worker.Facing);
            Assert.AreEqual(original.Sim.Packages[0].Bad, p.Bad);
            Assert.AreEqual(original.Sim.Packages[0].Label.Destination, p.Label.Destination);
            Assert.AreEqual(original.Sim.Packages[0].Label.Weight, p.Label.Weight);
            Assert.AreEqual(original.Sim.Packages[0].Label.Method, p.Label.Method);
            Assert.AreEqual(original.Sim.Packages[0].Label.Scale, p.Label.Scale);
            Assert.AreEqual(original.Sim.DeliveredCount, restored.Sim.DeliveredCount);
            Assert.AreEqual(original.Sim.LostCount, restored.Sim.LostCount);
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
        public void V1Save_IsRejected()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(SaveSerializer.FromJson("{\"Version\":1}")));
        }

        [Test]
        public void RoundTrip_PreservesCarriedAndInspectedPackage()
        {
            var s = new GameSession(new Economy(500), new PackageFactory(new Random(2), 1.0));
            s.Build.TryPlace(1, 2, CellType.Table, Direction.East);
            s.Tick(3f);
            s.Tick(1f); // 택배가 검사대 (1,2)에 도착해 대기
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Inspect(true));
            Assert.AreEqual(InteractResult.PickedUp, s.Worker.Interact(null));
            Assert.AreEqual(0, s.Sim.Packages.Count);

            var restored = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(SaveSerializer.Capture(s))));
            var c = restored.Worker.Worker.Carrying;
            Assert.IsNotNull(c);
            Assert.IsTrue(c.Inspected);
            Assert.IsTrue(c.Rejected);
            Assert.IsTrue(c.Bad);
            Assert.AreEqual(0, restored.Sim.Packages.Count);
            Assert.AreEqual(-1, restored.Grid.Get(1, 2).OccupantId);
            Assert.AreEqual(s.Sim.NextId, restored.Sim.NextId);
        }

        [Test]
        public void Restore_RejectsWorkerOnNonFloor()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Worker.X = 0; st.Worker.Y = 2; // 입고구
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st.Worker.X = 99;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            var st2 = SaveSerializer.Capture(new GameSession());
            st2.Worker = null;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st2));
        }

        [Test]
        public void GarbageJson_Throws()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson("null"));
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson("{not json"));
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson(null));
        }

        [Test]
        public void MissingVersion_Throws()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(SaveSerializer.FromJson("{}")));
        }

        [Test]
        public void CorruptContents_ThrowSaveVersionException()
        {
            Assert.Throws<SaveVersionException>(() =>
                SaveSerializer.Restore(SaveSerializer.FromJson("{\"Version\":1,\"Cells\":null}")));
            Assert.Throws<SaveVersionException>(() =>
                SaveSerializer.Restore(SaveSerializer.FromJson("{\"Version\":1,\"Purchased\":[\"Bogus\"]}")));

            var outOfBounds = SaveSerializer.Capture(new GameSession());
            outOfBounds.Packages.Add(new PackageState { Id = 1, Kind = "Normal", X = 99, Y = 0, Progress = 0f, Heading = "East" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(outOfBounds));

            var onEmptyCell = SaveSerializer.Capture(new GameSession());
            onEmptyCell.Packages.Add(new PackageState { Id = 1, Kind = "Normal", X = 1, Y = 1, Progress = 0f, Heading = "East" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(onEmptyCell));

            var nonBuilding = SaveSerializer.Capture(new GameSession());
            nonBuilding.Cells.Add(new CellState { X = 1, Y = 1, Type = "Outlet", Direction = "East" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(nonBuilding));

            var badDirection = SaveSerializer.Capture(new GameSession());
            badDirection.Cells.Add(new CellState { X = 1, Y = 1, Type = "Conveyor", Direction = "7" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(badDirection));

            var badSpeed = SaveSerializer.Capture(new GameSession());
            badSpeed.Speed = -1f;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(badSpeed));

            var staleNextId = SaveSerializer.Capture(BuildSample());
            staleNextId.NextPackageId = staleNextId.Packages[0].Id;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(staleNextId));
        }
    }
}
