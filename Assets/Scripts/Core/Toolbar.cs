using System;
using System.Collections.Generic;
using System.Linq;

namespace ParcelFactory.Core
{
    public readonly struct Tool : IEquatable<Tool>
    {
        public readonly CellType Type;
        public readonly Direction Direction;

        public Tool(CellType type, Direction direction)
        {
            Type = type;
            Direction = direction;
        }

        public bool Equals(Tool other) => Type == other.Type && Direction == other.Direction;
        public override bool Equals(object obj) => obj is Tool t && Equals(t);
        public override int GetHashCode() => ((int)Type << 2) | (int)Direction;
        public override string ToString() => $"{Type}:{Direction}";
    }

    public sealed class Toolbar
    {
        private readonly HashSet<Tool> owned = new HashSet<Tool>();

        public bool SplitterOwned { get; private set; }
        /// <summary>검사대는 시작부터 4방향 모두 보유.</summary>
        public bool TableOwned { get; private set; } = true;
        public bool ScannerOwned { get; private set; }

        public Toolbar()
        {
            owned.Add(new Tool(CellType.Conveyor, Direction.East));
        }

        public bool Owns(CellType type, Direction direction)
        {
            switch (type)
            {
                case CellType.Splitter: return SplitterOwned;
                case CellType.Table: return TableOwned;
                case CellType.Scanner: return ScannerOwned;
                default: return owned.Contains(new Tool(type, direction));
            }
        }

        public void Add(CellType type, Direction direction)
        {
            switch (type)
            {
                case CellType.Splitter: SplitterOwned = true; break;
                case CellType.Table: TableOwned = true; break;
                case CellType.Scanner: ScannerOwned = true; break;
                default: owned.Add(new Tool(type, direction)); break;
            }
        }

        public IEnumerable<Tool> OwnedTools()
        {
            foreach (var t in owned.OrderBy(t => t.Type).ThenBy(t => t.Direction))
                yield return t;
            if (TableOwned) foreach (Direction d in Enum.GetValues(typeof(Direction))) yield return new Tool(CellType.Table, d);
            if (SplitterOwned) foreach (Direction d in Enum.GetValues(typeof(Direction))) yield return new Tool(CellType.Splitter, d);
            if (ScannerOwned) foreach (Direction d in Enum.GetValues(typeof(Direction))) yield return new Tool(CellType.Scanner, d);
        }
    }
}
