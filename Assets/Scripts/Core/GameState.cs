using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class GameState
    {
        /// <summary>기본값 0. Capture가 명시적으로 채우므로 필드가 없는 JSON은 버전 불일치로 거부된다.</summary>
        public int Version;
        public int Money;
        public int UnlockedSize;
        public List<string> Purchased = new List<string>();
        public List<CellState> Cells = new List<CellState>();
        public List<PackageState> Packages = new List<PackageState>();
        public float SpawnTimer;
        public int NextPackageId;
        public bool Paused;
        public float Speed;
    }

    public sealed class CellState
    {
        public int X;
        public int Y;
        public string Type;
        public string Direction;
        public bool Demolishing;
        public float DemolishRemaining;
        public bool SplitterToggle;
    }

    public sealed class PackageState
    {
        public int Id;
        public string Kind;
        public int X;
        public int Y;
        public float Progress;
        public string Heading;
        public string EntryHeading;
    }
}
