using System.Linq;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class ToolbarTests
    {
        [Test]
        public void NewToolbar_OwnsOnlyEastRail()
        {
            var t = new Toolbar();
            Assert.IsTrue(t.Owns(CellType.Conveyor, Direction.East));
            Assert.IsFalse(t.Owns(CellType.Conveyor, Direction.North));
            Assert.IsFalse(t.Owns(CellType.FastConveyor, Direction.East));
            Assert.IsFalse(t.Owns(CellType.Splitter, Direction.East));
            Assert.IsTrue(t.Owns(CellType.Table, Direction.West));
            Assert.IsFalse(t.Owns(CellType.Scanner, Direction.East));
            Assert.IsTrue(t.Owns(CellType.Pallet, Direction.East));
            Assert.AreEqual(7, t.OwnedTools().Count()); // 레일 → + 팔레트 2 + 검사대 4
        }

        [Test]
        public void Add_GrantsSpecificDirection()
        {
            var t = new Toolbar();
            t.Add(CellType.FastConveyor, Direction.South);
            Assert.IsTrue(t.Owns(CellType.FastConveyor, Direction.South));
            Assert.IsFalse(t.Owns(CellType.FastConveyor, Direction.North));
        }

        [Test]
        public void AddSplitter_GrantsAllDirections()
        {
            var t = new Toolbar();
            t.Add(CellType.Splitter, Direction.East);
            Assert.IsTrue(t.SplitterOwned);
            Assert.IsTrue(t.Owns(CellType.Splitter, Direction.West));
            Assert.AreEqual(11, t.OwnedTools().Count());
        }

        [Test]
        public void AddScanner_GrantsAllDirections_AfterSplitterInOrder()
        {
            var t = new Toolbar();
            t.Add(CellType.Scanner, Direction.North);
            Assert.IsTrue(t.ScannerOwned);
            Assert.IsTrue(t.Owns(CellType.Scanner, Direction.South));
            var tools = t.OwnedTools().ToList();
            Assert.AreEqual(11, tools.Count);
            Assert.AreEqual(CellType.Conveyor, tools[0].Type);
            Assert.AreEqual(CellType.Pallet, tools[1].Type);
            Assert.AreEqual(CellType.Table, tools[3].Type);
            Assert.AreEqual(CellType.Scanner, tools[7].Type);
        }

        [Test]
        public void AddBridge_GrantsAllDirections_AfterRailsBeforePallet()
        {
            var t = new Toolbar();
            Assert.IsFalse(t.BridgeOwned);
            Assert.IsFalse(t.Owns(CellType.Bridge, Direction.East));
            t.Add(CellType.Conveyor, Direction.North);
            t.Add(CellType.FastConveyor, Direction.East);
            t.Add(CellType.Splitter, Direction.East);
            t.Add(CellType.Scanner, Direction.East);
            t.Add(CellType.Bridge, Direction.South);
            Assert.IsTrue(t.BridgeOwned);
            Assert.IsTrue(t.Owns(CellType.Bridge, Direction.West));
            var types = t.OwnedTools().Select(x => x.Type).Distinct().ToList();
            CollectionAssert.AreEqual(new[]
            {
                CellType.Conveyor, CellType.FastConveyor, CellType.Bridge, CellType.Pallet,
                CellType.Table, CellType.Splitter, CellType.Scanner,
            }, types);
            var tools = t.OwnedTools().ToList();
            Assert.AreEqual(3 + 4 + 2 + 4 + 4 + 4, tools.Count);
            CollectionAssert.AreEqual(
                new[] { Direction.North, Direction.East, Direction.South, Direction.West },
                tools.Where(x => x.Type == CellType.Bridge).Select(x => x.Direction).ToList());
        }

        [Test]
        public void OwnedTools_IsSortedByTypeThenDirection()
        {
            var t = new Toolbar();
            t.Add(CellType.Conveyor, Direction.North);
            t.Add(CellType.FastConveyor, Direction.East);
            var tools = t.OwnedTools().ToList();
            Assert.AreEqual(new Tool(CellType.Conveyor, Direction.North), tools[0]);
            Assert.AreEqual(new Tool(CellType.Conveyor, Direction.East), tools[1]);
            Assert.AreEqual(new Tool(CellType.FastConveyor, Direction.East), tools[2]);
        }
    }
}
