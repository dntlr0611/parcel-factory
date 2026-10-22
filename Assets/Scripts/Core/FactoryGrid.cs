using System;

namespace ParcelFactory.Core
{
    public sealed class FactoryGrid
    {
        public const int Width = 12;
        public const int Height = 12;
        public const int InitialUnlocked = 6;
        public const int InletX = 0, InletY = 2;
        /// <summary>부지마다 하나씩 늘어나는 입고구(스펙 16.2): 확장 1 → (0,7), 확장 2 → (0,10). 모두 동쪽으로 내보낸다.</summary>
        public const int Plot1InletY = 7, Plot2InletY = 10;
        /// <summary>입고구가 생길 수 있는 칸 전부(처음부터 있는 것 먼저). 입고 순서도 이 순서다.</summary>
        public static readonly (int x, int y)[] InletTiles = { (InletX, InletY), (InletX, Plot1InletY), (InletX, Plot2InletY) };
        /// <summary>트럭 도크(스펙 10.1): 동쪽 벽 출입구 바로 안쪽. 해금 여부와 무관하게 처음부터 존재한다.</summary>
        public const int OutletX = 11, OutletY = 2;
        public const int ReturnDockX = 11, ReturnDockY = 4;
        /// <summary>항공 트럭 도크(스펙 14.1): 동쪽 벽 안쪽. 부지 확장 2(UnlockRegion(Width))에서 생긴다. 그 전에는 잠긴 칸이다.</summary>
        public const int AirOutletX = 11, AirOutletY = 7;
        public const int AirReturnDockX = 11, AirReturnDockY = 9;
        /// <summary>시작 팔레트(스펙 10.2): 옛 출고구·반품구 자리. 플레이어가 해체할 수 있다.</summary>
        public const int StartBluePalletX = 5, StartBluePalletY = 2;
        public const int StartRedPalletX = 5, StartRedPalletY = 4;

        private readonly Cell[,] cells = new Cell[Width, Height];

        public int UnlockedSize { get; private set; } = InitialUnlocked;

        public FactoryGrid()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    cells[x, y] = new Cell { Type = IsUnlocked(x, y) ? CellType.Empty : CellType.Locked };

            SetInlet(cells[InletX, InletY]);
            cells[OutletX, OutletY].Type = CellType.Outlet;
            cells[ReturnDockX, ReturnDockY].Type = CellType.ReturnDock;

            SetPallet(cells[StartBluePalletX, StartBluePalletY], PalletKind.Blue);
            SetPallet(cells[StartRedPalletX, StartRedPalletY], PalletKind.Red);
        }

        private static void SetInlet(Cell c)
        {
            c.Type = CellType.Inlet;
            c.Direction = Direction.East;
        }

        private static void SetPallet(Cell c, PalletKind kind)
        {
            c.Type = CellType.Pallet;
            c.Direction = kind == PalletKind.Blue ? Direction.East : Direction.West;
            c.PalletKind = kind;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsUnlocked(int x, int y) => InBounds(x, y) && x < UnlockedSize && y < UnlockedSize;

        public Cell Get(int x, int y) => cells[x, y];

        /// <summary>잠긴 칸만 빈 칸으로 바꾼다. 도크·팔레트 등 이미 무언가 있는 칸은 건드리지 않는다.
        /// 격자 전체(12×12)를 열면 항공 도크 두 칸을 만든다(스펙 14.1).</summary>
        public void UnlockRegion(int size)
        {
            if (size < UnlockedSize || size > Width)
                throw new ArgumentOutOfRangeException(nameof(size));
            UnlockedSize = size;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (cells[x, y].Type == CellType.Locked && IsUnlocked(x, y))
                        cells[x, y].Type = CellType.Empty;
            // 부지 확장 1(9×9) 이상이면 (0,7), 2(12×12)면 (0,10)에 입고구(스펙 16.2). 잠긴 칸이던 자리라 충돌이 없다.
            if (size >= 9) SetInlet(cells[InletX, Plot1InletY]);
            if (size == Width) SetInlet(cells[InletX, Plot2InletY]);
            if (size == Width) AddAirDocks();
        }

        /// <summary>항공 도크 두 칸을 만든다(부지 확장 2, 또는 공항 물류센터는 처음부터 — 스펙 19.2). 잠긴 칸이던 자리라 충돌이 없다. 여러 번 불러도 같다.</summary>
        public void AddAirDocks()
        {
            cells[AirOutletX, AirOutletY].Type = CellType.AirOutlet;
            cells[AirReturnDockX, AirReturnDockY].Type = CellType.AirReturnDock;
        }

        /// <summary>항공 도크 두 칸이 있는가(부지 확장 2 뒤, 또는 공항 물류센터는 처음부터).</summary>
        public bool HasAirDocks =>
            cells[AirOutletX, AirOutletY].Type == CellType.AirOutlet && cells[AirReturnDockX, AirReturnDockY].Type == CellType.AirReturnDock;

        public static bool IsPlaceable(CellType type) =>
            type == CellType.Conveyor || type == CellType.FastConveyor || type == CellType.Splitter ||
            type == CellType.Table || type == CellType.Scanner || type == CellType.Pallet || type == CellType.Bridge ||
            type == CellType.Labeler;

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
            c.PalletKind = type == CellType.Pallet ? Tool.KindOf(dir) : PalletKind.None;
            c.Pallet.Clear();
            c.RedDirection = null;
            c.Mode = RouteMode.Inspect;
            c.AirDirection = null;
            c.LowerOccupantId = -1;
        }

        public void Clear(int x, int y)
        {
            var c = Get(x, y);
            if (!c.IsBuilding)
                throw new InvalidOperationException($"({x},{y}) is not a building");
            c.Type = CellType.Empty;
            c.Demolishing = false;
            c.DemolishRemaining = 0f;
            c.OccupantId = -1;
            c.LowerOccupantId = -1;
            c.PalletKind = PalletKind.None;
            c.Pallet.Clear();
            c.RedDirection = null;
            c.Mode = RouteMode.Inspect;
            c.AirDirection = null;
        }
    }
}
