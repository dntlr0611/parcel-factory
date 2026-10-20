using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>분배기 규칙(스펙 11.6): 연결된 출구로 라운드로빈, 후보 순서 왼쪽 → 앞 → 오른쪽 → 뒤.</summary>
    public class SplitterTests
    {
        private FactoryGrid grid;
        private ConveyorSim sim;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            sim = new ConveyorSim(grid, new Economy(0), new PackageFactory(new Random(1), 0.0));
        }

        private void Put(int x, int y, Direction d, CellType type = CellType.Conveyor) => grid.Place(x, y, type, d);

        /// <summary>(fx,fy) 트랙에서 택배를 만들어 1초 흘려 분배기에 들인 뒤, 분배기가 정한 방향을 돌려주고 택배를 치운다.</summary>
        private Direction Feed(int fx, int fy, int sx, int sy)
        {
            Assert.IsTrue(sim.TrySpawn(fx, fy, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreEqual((sx, sy), (p.X, p.Y), "package did not enter the splitter");
            var heading = p.Heading;
            sim.Take(p);
            return heading;
        }

        private Direction FeedFromInlet() => Feed(FactoryGrid.InletX, FactoryGrid.InletY, 1, 2);

        [Test]
        public void StraightLine_PackagesGoStraight_NoLoss()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(2, 2, Direction.East);
            Put(3, 2, Direction.East);
            Put(4, 2, Direction.East);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
                sim.Tick(1f);
                Assert.AreEqual(Direction.East, sim.PackageAt(1, 2).Heading);
            }
            sim.Tick(1f);
            Assert.AreEqual(0, sim.LostCount);
            Assert.AreEqual(3, sim.Packages.Count);
            Assert.IsNull(sim.PackageAt(1, 3));
            Assert.IsNull(sim.PackageAt(1, 1));
        }

        [Test]
        public void T_WithLeftAndRight_AlternatesLeftRightLeft()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(1, 3, Direction.North); // 동쪽 기준 왼쪽
            Put(1, 1, Direction.South); // 동쪽 기준 오른쪽
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.AreEqual(Direction.South, FeedFromInlet());
            Assert.AreEqual(Direction.North, FeedFromInlet());
        }

        [Test]
        public void LeftAndStraight_Alternates()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(1, 3, Direction.North);
            Put(2, 2, Direction.East);
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.AreEqual(Direction.East, FeedFromInlet());
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.AreEqual(Direction.East, FeedFromInlet());
        }

        [Test]
        public void OnlyRightConnected_AlwaysRight()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(1, 1, Direction.South);
            for (int i = 0; i < 3; i++) Assert.AreEqual(Direction.South, FeedFromInlet());
        }

        [Test]
        public void SideEntry_NeverSendsBack()
        {
            // 동쪽을 보는 분배기에 북쪽(1,3)에서 남쪽으로 진입. 왼쪽(북)은 되돌아가는 방향이라 후보가 아니다.
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(1, 3, Direction.South);
            Put(1, 1, Direction.South);
            for (int i = 0; i < 3; i++) Assert.AreEqual(Direction.South, Feed(1, 3, 1, 2));
            CollectionAssert.AreEqual(new[] { Direction.South }, ConveyorSim.SplitterOutputs(grid, 1, 2, Direction.South));
        }

        [Test]
        public void SideEntry_UsesStemAsLastCandidate()
        {
            // 북쪽을 보는 분배기에 서쪽(반입구)에서 동쪽으로 진입: 앞(북) → 오른쪽(동) → 뒤(남, T자 줄기).
            Put(1, 2, Direction.North, CellType.Splitter);
            Put(1, 3, Direction.North);
            Put(2, 2, Direction.East);
            Put(1, 1, Direction.South);
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.AreEqual(Direction.East, FeedFromInlet());
            Assert.AreEqual(Direction.South, FeedFromInlet());
            Assert.AreEqual(Direction.North, FeedFromInlet());
        }

        [Test]
        public void SideEntry_OnlyStemConnected_AlwaysStem()
        {
            Put(1, 2, Direction.North, CellType.Splitter);
            Put(1, 1, Direction.South);
            for (int i = 0; i < 3; i++) Assert.AreEqual(Direction.South, FeedFromInlet());
        }

        [Test]
        public void Outputs_NullIncoming_ListsAllConnectedInOrder_AndSkipsInlet()
        {
            Put(1, 2, Direction.North, CellType.Splitter);
            Put(1, 3, Direction.North);
            Put(2, 2, Direction.East);
            Put(1, 1, Direction.South);
            // 왼쪽(서) = 반입구 → 후보 아님(들어가면 소실).
            CollectionAssert.AreEqual(new[] { Direction.North, Direction.East, Direction.South },
                ConveyorSim.SplitterOutputs(grid, 1, 2, null));
            Put(3, 3, Direction.East, CellType.Splitter);
            Put(2, 3, Direction.East);
            Put(4, 3, Direction.East);
            Put(3, 4, Direction.North);
            Put(3, 2, Direction.South);
            CollectionAssert.AreEqual(new[] { Direction.North, Direction.East, Direction.South, Direction.West },
                ConveyorSim.SplitterOutputs(grid, 3, 3, null));
            CollectionAssert.AreEqual(new[] { Direction.North, Direction.East, Direction.South },
                ConveyorSim.SplitterOutputs(grid, 3, 3, Direction.East));
        }

        [Test]
        public void OutputsToPalletAndTruckDock_Count()
        {
            grid.UnlockRegion(FactoryGrid.Width);
            Put(9, 2, Direction.East);
            Put(10, 2, Direction.East, CellType.Splitter); // 앞 = 출고구 (11,2)
            Put(10, 3, Direction.East, CellType.Pallet);   // 왼쪽 = 파랑 팔레트
            CollectionAssert.AreEqual(new[] { Direction.North, Direction.East },
                ConveyorSim.SplitterOutputs(grid, 10, 2, Direction.East));

            int delivered = 0;
            sim.PackageDelivered += (_, __, ___) => delivered++;
            Assert.IsTrue(sim.TrySpawn(9, 2, PackageKind.Normal, out var a));
            sim.Tick(1f);
            Assert.AreEqual(Direction.North, a.Heading);
            sim.Tick(1f); // a → 팔레트 더미
            Assert.AreEqual(1, sim.PalletCount(10, 3));
            Assert.IsTrue(sim.TrySpawn(9, 2, PackageKind.Normal, out var b));
            sim.Tick(1f);
            Assert.AreEqual(Direction.East, b.Heading);
            sim.Tick(1f); // b → 트럭 도크
            Assert.AreEqual(1, delivered);
            Assert.AreEqual(0, sim.LostCount);
        }

        [Test]
        public void NoOutputs_GoesStraight_AndIsLost()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            CollectionAssert.IsEmpty(ConveyorSim.SplitterOutputs(grid, 1, 2, Direction.East));
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreEqual(Direction.East, p.Heading);
            sim.Tick(1f);
            Assert.AreEqual(1, sim.LostCount);
            Assert.AreEqual(0, sim.Packages.Count);
        }

        [Test]
        public void RoundRobin_SurvivesNeighbourDemolished()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(1, 3, Direction.North);
            Put(2, 2, Direction.East);
            Put(1, 1, Direction.South);
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.AreEqual(Direction.East, FeedFromInlet());
            grid.Clear(2, 2); // 후보 3 → 2, 인덱스 2는 나머지로 0
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.AreEqual(Direction.South, FeedFromInlet());
            Assert.AreEqual(Direction.North, FeedFromInlet());
            Assert.GreaterOrEqual(grid.Get(1, 2).SplitterIndex, 0);
        }

        [Test]
        public void Place_ResetsSplitterIndex()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            grid.Get(1, 2).SplitterIndex = 5;
            grid.Clear(1, 2);
            Assert.AreEqual(0, grid.Get(1, 2).SplitterIndex);
            Put(1, 2, Direction.East, CellType.Splitter);
            Assert.AreEqual(0, grid.Get(1, 2).SplitterIndex);
        }
    }
}
