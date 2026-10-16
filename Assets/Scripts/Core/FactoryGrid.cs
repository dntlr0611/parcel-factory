using System;

namespace ParcelFactory.Core
{
    public sealed class FactoryGrid
    {
        public const int Width = 12;
        public const int Height = 12;
        public const int InitialUnlocked = 6;
        public const int InletX = 0, InletY = 2;
        public const int OutletX = 5, OutletY = 2;
        public const int ReturnDockX = 5, ReturnDockY = 4;

        private readonly Cell[,] cells = new Cell[Width, Height];

        public int UnlockedSize { get; private set; } = InitialUnlocked;

        public FactoryGrid()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    cells[x, y] = new Cell { Type = IsUnlocked(x, y) ? CellType.Empty : CellType.Locked };

            cells[InletX, InletY].Type = CellType.Inlet;
            cells[InletX, InletY].Direction = Direction.East;
            cells[OutletX, OutletY].Type = CellType.Outlet;
            cells[ReturnDockX, ReturnDockY].Type = CellType.ReturnDock;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsUnlocked(int x, int y) => InBounds(x, y) && x < UnlockedSize && y < UnlockedSize;

        public Cell Get(int x, int y) => cells[x, y];

        public void UnlockRegion(int size)
        {
            if (size < UnlockedSize || size > Width)
                throw new ArgumentOutOfRangeException(nameof(size));
            UnlockedSize = size;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (cells[x, y].Type == CellType.Locked && IsUnlocked(x, y))
                        cells[x, y].Type = CellType.Empty;
        }

        public static bool IsPlaceable(CellType type) =>
            type == CellType.Conveyor || type == CellType.FastConveyor || type == CellType.Splitter ||
            type == CellType.Table || type == CellType.Scanner;

        public bool CanPlace(int x, int y) => IsUnlocked(x, y) && cells[x, y].Type == CellType.Empty;

        public void Place(int x, int y, CellType type, Direction dir)
        {
            if (!CanPlace(x, y))
                throw new InvalidOperationException($"Cannot place at ({x},{y})");
            if (!IsPlaceable(type))
                throw new ArgumentException($"{type} is not placeable", nameof(type));
            var c = cells[x, y];
            c.Type = type;
            c.Direction = dir;
            c.Demolishing = false;
            c.DemolishRemaining = 0f;
            c.SplitterToggle = false;
        }

        public void Clear(int x, int y)
        {
            var c = Get(x, y);
            if (!c.IsBuilding)
                throw new InvalidOperationException($"({x},{y}) is not a building");
            c.Type = CellType.Empty;
            c.Demolishing = false;
            c.DemolishRemaining = 0f;
            c.SplitterToggle = false;
            c.OccupantId = -1;
        }
    }
}
