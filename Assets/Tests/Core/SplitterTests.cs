using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>분배기 = 분류기(스펙 11.7): 승인·미검사 → 파란 출구(분배기 Direction), 거부 → 빨간 출구
    /// (파란 기준 오른쪽 → 왼쪽 → 뒤 중 처음 연결된 출구). 빨강이 없으면 거부도 파랑, 파랑이 연결 안 됐으면 Direction으로 직진.</summary>
    public class SplitterTests
    {
        private enum Verdict { Uninspected, Approved, Rejected }

        private FactoryGrid grid;
        private ConveyorSim sim;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            sim = new ConveyorSim(grid, new Economy(0), new PackageFactory(new Random(1), 0.0));
        }

        private void Put(int x, int y, Direction d, CellType type = CellType.Conveyor) => grid.Place(x, y, type, d);

        private static void Apply(Package p, Verdict v)
        {
            if (v == Verdict.Approved) p.Inspect(false);
            else if (v == Verdict.Rejected) p.Inspect(true);
        }

        /// <summary>(fx,fy) 트랙에서 택배를 만들고 판정을 붙여 1초 흘려 분배기 (sx,sy)에 들인 뒤, 정해진 방향을 돌려주고 택배를 치운다.</summary>
        private Direction Feed(int fx, int fy, int sx, int sy, Verdict v)
        {
            Assert.IsTrue(sim.TrySpawn(fx, fy, PackageKind.Normal, out var p));
            Apply(p, v);
            sim.Tick(1f);
            Assert.AreEqual((sx, sy), (p.X, p.Y), "package did not enter the splitter");
            var heading = p.Heading;
            sim.Take(p);
            return heading;
        }

        private Direction FeedFromInlet(Verdict v) => Feed(FactoryGrid.InletX, FactoryGrid.InletY, 1, 2, v);

        /// <summary>동쪽(파랑)을 보는 분배기 (1,2): 앞 (2,2), 오른쪽 남 (1,1), 왼쪽 북 (1,3) 모두 레일.</summary>
        private void EastSplitterWithAllOutputs()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(2, 2, Direction.East);
            Put(1, 1, Direction.South);
            Put(1, 3, Direction.North);
        }

        [Test]
        public void Approved_GoesBlue()
        {
            EastSplitterWithAllOutputs();
            for (int i = 0; i < 3; i++) Assert.AreEqual(Direction.East, FeedFromInlet(Verdict.Approved));
        }

        [Test]
        public void Rejected_GoesRed()
        {
            EastSplitterWithAllOutputs();
            for (int i = 0; i < 3; i++) Assert.AreEqual(Direction.South, FeedFromInlet(Verdict.Rejected));
        }

        [Test]
        public void Uninspected_GoesBlue_EvenIfBad()
        {
            EastSplitterWithAllOutputs();
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Bad = true; // 실제 불량이어도 검사 전에는 알 수 없다
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(Direction.East, p.Heading);
        }

        [Test]
        public void MixedStream_SortsEachPackage_NoRoundRobin()
        {
            EastSplitterWithAllOutputs();
            Assert.AreEqual(Direction.South, FeedFromInlet(Verdict.Rejected));
            Assert.AreEqual(Direction.East, FeedFromInlet(Verdict.Approved));
            Assert.AreEqual(Direction.East, FeedFromInlet(Verdict.Uninspected));
            Assert.AreEqual(Direction.South, FeedFromInlet(Verdict.Rejected));
            Assert.AreEqual(Direction.South, FeedFromInlet(Verdict.Rejected));
        }

        [Test]
        public void RedMissing_RejectedGoesBlue()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(2, 2, Direction.East);
            ConveyorSim.SplitterRoutes(grid, 1, 2, Direction.East, out var blue, out var red, out var blueConnected);
            Assert.AreEqual(Direction.East, blue);
            Assert.IsNull(red);
            Assert.IsTrue(blueConnected);
            Assert.AreEqual(Direction.East, FeedFromInlet(Verdict.Rejected));
            Assert.AreEqual(Direction.East, FeedFromInlet(Verdict.Approved));
        }

        [Test]
        public void BlueNotConnected_ApprovedAndUninspectedGoStraight_AndAreLost()
        {
            // 파랑(동) 앞이 비어 있고 빨강(북)만 연결: 승인·미검사는 Direction(동)으로 직진해 소실, 거부는 빨강.
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(1, 3, Direction.North);
            ConveyorSim.SplitterRoutes(grid, 1, 2, Direction.East, out _, out var red, out var blueConnected);
            Assert.IsFalse(blueConnected);
            Assert.AreEqual(Direction.North, red);

            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var a));
            a.Inspect(false);
            sim.Tick(1f);
            Assert.AreEqual(Direction.East, a.Heading);
            sim.Tick(1f);
            Assert.AreEqual(1, sim.LostCount);

            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var b));
            sim.Tick(1f);
            Assert.AreEqual(Direction.East, b.Heading);
            sim.Tick(1f);
            Assert.AreEqual(2, sim.LostCount);

            Assert.AreEqual(Direction.North, FeedFromInlet(Verdict.Rejected));
        }

        [Test]
        public void NoOutputs_EverythingGoesStraight_AndIsLost()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            CollectionAssert.IsEmpty(ConveyorSim.SplitterOutputs(grid, 1, 2, Direction.East));
            foreach (var v in new[] { Verdict.Approved, Verdict.Rejected, Verdict.Uninspected })
            {
                Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
                Apply(p, v);
                sim.Tick(1f);
                Assert.AreEqual(Direction.East, p.Heading, v.ToString());
                sim.Tick(1f);
            }
            Assert.AreEqual(3, sim.LostCount);
            Assert.AreEqual(0, sim.Packages.Count);
        }

        [Test]
        public void RedOrder_RightBeforeLeft()
        {
            EastSplitterWithAllOutputs(); // 오른쪽(남)·왼쪽(북) 모두 연결 → 오른쪽
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out _, out var red, out _);
            Assert.AreEqual(Direction.South, red);
            grid.Clear(1, 1); // 오른쪽이 사라지면 왼쪽
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out _, out red, out _);
            Assert.AreEqual(Direction.North, red);
            Assert.AreEqual(Direction.North, FeedFromInlet(Verdict.Rejected));
        }

        [Test]
        public void RedOrder_BackOnlyWhenNeitherSideConnected()
        {
            // 북쪽(파랑)을 보는 분배기 (3,3)에 서쪽 (2,3)에서 동쪽으로 진입. 뒤 = 남 (3,2).
            Put(2, 3, Direction.East);
            Put(3, 3, Direction.North, CellType.Splitter);
            Put(3, 4, Direction.North); // 파랑
            Put(3, 2, Direction.South); // 뒤
            Assert.AreEqual(Direction.South, Feed(2, 3, 3, 3, Verdict.Rejected));
            Put(4, 3, Direction.East);  // 오른쪽(동)이 생기면 뒤보다 먼저
            Assert.AreEqual(Direction.East, Feed(2, 3, 3, 3, Verdict.Rejected));
            Assert.AreEqual(Direction.North, Feed(2, 3, 3, 3, Verdict.Approved));
        }

        [Test]
        public void SideEntry_NeverGoesBack()
        {
            // 동쪽(파랑)을 보는 분배기 (3,3)에 북쪽 (3,4)에서 남쪽으로 진입. 왼쪽(북)은 온 길이라 빨강이 될 수 없다.
            Put(3, 4, Direction.South);
            Put(3, 3, Direction.East, CellType.Splitter);
            Put(4, 3, Direction.East); // 파랑
            Put(2, 3, Direction.West); // 뒤
            ConveyorSim.SplitterRoutes(grid, 3, 3, Direction.South, out _, out var red, out _);
            Assert.AreEqual(Direction.West, red); // 오른쪽(남) 없음, 왼쪽(북) 제외 → 뒤
            Assert.AreEqual(Direction.West, Feed(3, 4, 3, 3, Verdict.Rejected));
            Assert.AreEqual(Direction.East, Feed(3, 4, 3, 3, Verdict.Approved));

            grid.Clear(2, 3); // 다른 출구가 없어도 온 길(북)로는 보내지 않는다 → 거부도 파랑
            ConveyorSim.SplitterRoutes(grid, 3, 3, Direction.South, out _, out red, out _);
            Assert.IsNull(red);
            Assert.AreEqual(Direction.East, Feed(3, 4, 3, 3, Verdict.Rejected));
        }

        [Test]
        public void Scanner_LiveTick_SortsByInspectionResult()
        {
            // 레일 (1,2) → 자동 검사기 (2,2) → 분배기 (3,2, 동) → 파랑 (4,2) / 빨강 (3,1, 남).
            Put(1, 2, Direction.East);
            Put(2, 2, Direction.East, CellType.Scanner);
            Put(3, 2, Direction.East, CellType.Splitter);
            Put(4, 2, Direction.East);
            Put(3, 1, Direction.South);
            Put(3, 0, Direction.South);

            Assert.IsTrue(sim.TrySpawn(1, 2, PackageKind.Normal, out var bad));
            bad.Bad = true;
            sim.Tick(1f); // bad → 검사기(거부 판정)
            Assert.IsTrue(bad.Inspected);
            Assert.IsTrue(bad.Rejected);
            Assert.IsTrue(sim.TrySpawn(1, 2, PackageKind.Normal, out var good));
            sim.Tick(1f); // bad → 분배기, good → 검사기(승인)
            Assert.AreEqual((3, 2), (bad.X, bad.Y));
            Assert.AreEqual(Direction.South, bad.Heading);
            Assert.IsTrue(good.Inspected);
            Assert.IsFalse(good.Rejected);
            sim.Tick(1f); // bad → (3,1), good → 분배기
            Assert.AreEqual((3, 1), (bad.X, bad.Y));
            Assert.AreEqual((3, 2), (good.X, good.Y));
            Assert.AreEqual(Direction.East, good.Heading);
            sim.Tick(1f);
            Assert.AreEqual((4, 2), (good.X, good.Y));
            Assert.AreEqual((3, 0), (bad.X, bad.Y));
            Assert.AreEqual(2, sim.InspectedCount);
            Assert.AreEqual(0, sim.LostCount);
        }

        [Test]
        public void HandPlaced_OnSplitter_UsesSameRule()
        {
            EastSplitterWithAllOutputs();
            Put(4, 4, Direction.East);
            foreach (var (v, expected) in new[] { (Verdict.Rejected, Direction.South), (Verdict.Approved, Direction.East), (Verdict.Uninspected, Direction.East) })
            {
                Assert.IsTrue(sim.TrySpawn(4, 4, PackageKind.Normal, out var p));
                sim.Take(p); // 작업자가 집는다
                Apply(p, v);
                sim.PlaceCarried(p, 1, 2);
                Assert.AreEqual(expected, p.Heading, v.ToString());
                sim.Take(p);
            }
            grid.Clear(1, 1);
            grid.Clear(1, 3); // 빨강 없음 → 손으로 놓은 거부 택배도 파랑
            Assert.IsTrue(sim.TrySpawn(4, 4, PackageKind.Normal, out var r));
            sim.Take(r);
            r.Inspect(true);
            sim.PlaceCarried(r, 1, 2);
            Assert.AreEqual(Direction.East, r.Heading);
        }

        [Test]
        public void SpawnOnSplitter_IsUninspected_GoesBlue()
        {
            EastSplitterWithAllOutputs();
            Assert.IsTrue(sim.TrySpawn(1, 2, PackageKind.Normal, out var p));
            Assert.AreEqual(Direction.East, p.Heading);
        }

        [Test]
        public void SortsIntoPalletAndTruckDock()
        {
            grid.UnlockRegion(FactoryGrid.Width);
            Put(9, 2, Direction.East);
            Put(10, 2, Direction.East, CellType.Splitter); // 파랑 = 출고구 (11,2)
            Put(10, 1, Direction.West, CellType.Pallet);   // 빨강(오른쪽) = 빨강 팔레트
            ConveyorSim.SplitterRoutes(grid, 10, 2, Direction.East, out _, out var red, out var blueConnected);
            Assert.IsTrue(blueConnected);
            Assert.AreEqual(Direction.South, red);

            int delivered = 0;
            sim.PackageDelivered += (_, __, ___) => delivered++;
            Assert.IsTrue(sim.TrySpawn(9, 2, PackageKind.Normal, out var bad));
            bad.Inspect(true);
            sim.Tick(1f);
            Assert.AreEqual(Direction.South, bad.Heading);
            sim.Tick(1f); // 거부 → 팔레트 더미
            Assert.AreEqual(1, sim.PalletCount(10, 1));
            Assert.IsTrue(sim.TrySpawn(9, 2, PackageKind.Normal, out var good));
            good.Inspect(false);
            sim.Tick(1f);
            Assert.AreEqual(Direction.East, good.Heading);
            sim.Tick(1f); // 승인 → 트럭 도크
            Assert.AreEqual(1, delivered);
            Assert.AreEqual(0, sim.LostCount);
        }

        [Test]
        public void Routes_NullIncoming_SkipInlet_AndFollowDirection()
        {
            // 서쪽(파랑)을 보는 분배기 (1,2): 파랑 앞 = 반입구 → 연결 아님(들어가면 소실). 빨강 = 오른쪽(북).
            Put(1, 2, Direction.West, CellType.Splitter);
            Put(1, 3, Direction.North);
            Put(1, 1, Direction.South);
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out var blue, out var red, out var blueConnected);
            Assert.AreEqual(Direction.West, blue);
            Assert.IsFalse(blueConnected);
            Assert.AreEqual(Direction.North, red);
            CollectionAssert.AreEqual(new[] { Direction.South, Direction.North }, ConveyorSim.SplitterOutputs(grid, 1, 2, null));

            grid.Get(1, 2).Direction = Direction.North; // R로 파랑 = 북: 빨강 = 오른쪽(동) 없음 → 왼쪽(서) 반입구 제외 → 뒤(남)
            ConveyorSim.SplitterRoutes(grid, 1, 2, null, out blue, out red, out blueConnected);
            Assert.AreEqual(Direction.North, blue);
            Assert.IsTrue(blueConnected);
            Assert.AreEqual(Direction.South, red);
        }

        [Test]
        public void StraightLine_AllGoStraight_NoLoss()
        {
            Put(1, 2, Direction.East, CellType.Splitter);
            Put(2, 2, Direction.East);
            Put(3, 2, Direction.East);
            Put(4, 2, Direction.East);
            var verdicts = new[] { Verdict.Approved, Verdict.Rejected, Verdict.Uninspected };
            foreach (var v in verdicts)
            {
                Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
                Apply(p, v);
                sim.Tick(1f);
                Assert.AreEqual(Direction.East, sim.PackageAt(1, 2).Heading, v.ToString());
            }
            sim.Tick(1f);
            Assert.AreEqual(0, sim.LostCount);
            Assert.AreEqual(3, sim.Packages.Count);
        }
    }
}
