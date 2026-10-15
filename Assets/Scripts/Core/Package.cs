namespace ParcelFactory.Core
{
    public enum PackageKind { Normal }

    public sealed class Package
    {
        public int Id;
        public PackageKind Kind;
        public int X;
        public int Y;
        /// <summary>현재 셀 안에서의 진행도 0~1.</summary>
        public float Progress;
        /// <summary>현재 셀에서 나갈 방향.</summary>
        public Direction Heading;
    }
}
