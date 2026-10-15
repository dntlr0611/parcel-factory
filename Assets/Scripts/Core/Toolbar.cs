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

        public Toolbar()
        {
            owned.Add(new Tool(CellType.Conveyor, Direction.East));
        }

        public bool Owns(CellType type, Direction direction) =>
            type == CellType.Splitter ? SplitterOwned : owned.Contains(new Tool(type, direction));

        public void Add(CellType type, Direction direction)
        {
            if (type == CellType.Splitter) SplitterOwned = true;
            else owned.Add(new Tool(type, direction));
        }

        public IEnumerable<Tool> OwnedTools()
        {
            foreach (var t in owned.OrderBy(t => t.Type).ThenBy(t => t.Direction))
                yield return t;
            if (SplitterOwned)
                foreach (Direction d in Enum.GetValues(typeof(Direction)))
                    yield return new Tool(CellType.Splitter, d);
        }
    }
}
