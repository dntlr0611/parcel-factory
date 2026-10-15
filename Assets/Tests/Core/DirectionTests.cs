using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class DirectionTests
    {
        [Test]
        public void Offsets_MatchCompass()
        {
            Assert.AreEqual((0, 1), (Direction.North.Dx(), Direction.North.Dy()));
            Assert.AreEqual((1, 0), (Direction.East.Dx(), Direction.East.Dy()));
            Assert.AreEqual((0, -1), (Direction.South.Dx(), Direction.South.Dy()));
            Assert.AreEqual((-1, 0), (Direction.West.Dx(), Direction.West.Dy()));
        }

        [Test]
        public void TurnRight_RotatesClockwise()
        {
            Assert.AreEqual(Direction.East, Direction.North.TurnRight());
            Assert.AreEqual(Direction.South, Direction.East.TurnRight());
            Assert.AreEqual(Direction.West, Direction.South.TurnRight());
            Assert.AreEqual(Direction.North, Direction.West.TurnRight());
        }

        [Test]
        public void TurnLeft_RotatesCounterClockwise()
        {
            Assert.AreEqual(Direction.West, Direction.North.TurnLeft());
            Assert.AreEqual(Direction.North, Direction.East.TurnLeft());
        }
    }
}
