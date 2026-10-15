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
            Assert.AreEqual(1, t.OwnedTools().Count());
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
            Assert.AreEqual(5, t.OwnedTools().Count());
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
