using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public enum CellType { Empty, Locked, Conveyor, FastConveyor, Splitter, Inlet, Outlet, Table, Scanner, ReturnDock, Pallet }

    /// <summary>팔레트 종류. 색은 안내용이며 판정과 무관하다(스펙 10.2).</summary>
    public enum PalletKind { None, Blue, Red }

    public sealed class Cell
    {
        /// <summary>팔레트 한 칸에 쌓을 수 있는 택배 수.</summary>
        public const int PalletCapacity = 6;

        public CellType Type = CellType.Empty;
        public Direction Direction = Direction.East;
        public bool Demolishing;
        public float DemolishRemaining;
        /// <summary>분배기 라운드로빈 인덱스(스펙 11.6). 다음 택배는 출구 후보[인덱스 % 후보 수]로 나간다. 항상 0 이상.</summary>
        public int SplitterIndex;
        /// <summary>셀 위에 있는 택배 id. 없으면 -1. 팔레트 더미는 여기에 기록하지 않는다.</summary>
        public int OccupantId = -1;
        /// <summary>팔레트 종류. 팔레트가 아니면 None.</summary>
        public PalletKind PalletKind = PalletKind.None;
        /// <summary>팔레트 위 더미(아래 → 위 순서). 흐름(ConveyorSim.Packages)에는 없는 택배들이다.</summary>
        public readonly List<Package> Pallet = new List<Package>();

        public bool IsOccupied => OccupantId >= 0;

        public bool IsPalletFull => Pallet.Count >= PalletCapacity;

        /// <summary>택배가 올라가 이동할 수 있는 셀. 팔레트는 흐름이 아니라 더미이므로 포함하지 않는다.</summary>
        public bool IsTrack =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Inlet ||
            Type == CellType.Table || Type == CellType.Scanner;

        /// <summary>플레이어가 설치·해체할 수 있는 셀.</summary>
        public bool IsBuilding =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Table || Type == CellType.Scanner ||
            Type == CellType.Pallet;

        /// <summary>택배를 받아 판정하는 셀(트럭 도크: 출고구, 반품구).</summary>
        public bool IsDock => Type == CellType.Outlet || Type == CellType.ReturnDock;

        public float Speed => Type == CellType.FastConveyor ? 2f : 1f;
    }
}
