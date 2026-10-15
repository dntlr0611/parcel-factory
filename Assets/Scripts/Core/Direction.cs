namespace ParcelFactory.Core
{
    public enum Direction { North, East, South, West }

    public static class DirectionExt
    {
        public static int Dx(this Direction d) => d == Direction.East ? 1 : d == Direction.West ? -1 : 0;
        public static int Dy(this Direction d) => d == Direction.North ? 1 : d == Direction.South ? -1 : 0;
        public static Direction TurnRight(this Direction d) => (Direction)(((int)d + 1) % 4);
        public static Direction TurnLeft(this Direction d) => (Direction)(((int)d + 3) % 4);
    }
}
