using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public enum CellType { Empty, Locked, Conveyor, FastConveyor, Splitter, Inlet, Outlet, Table, Scanner, ReturnDock, Pallet, Bridge, AirOutlet, AirReturnDock, Labeler }

    /// <summary>팔레트 종류. 색은 안내용이며 판정과 무관하다(스펙 10.2).</summary>
    public enum PalletKind { None, Blue, Red }

    /// <summary>분배기·자동 검사기의 분류 모드(스펙 14.5). Inspect = 검사 결과(기본), Air = 운송 수단(항공/일반).</summary>
    public enum RouteMode { Inspect, Air }

    public sealed class Cell
    {
        /// <summary>팔레트 한 칸에 쌓을 수 있는 택배 수.</summary>
        public const int PalletCapacity = 6;

        public CellType Type = CellType.Empty;
        public Direction Direction = Direction.East;
        public bool Demolishing;
        public float DemolishRemaining;
        /// <summary>셀 위에 있는 택배 id. 없으면 -1. 팔레트 더미는 여기에 기록하지 않는다.</summary>
        public int OccupantId = -1;
        /// <summary>브릿지 아랫길(Direction과 수직인 축) 위 택배 id(스펙 11.12). 없으면 -1. 브릿지가 아니면 항상 -1.
        /// 브릿지의 OccupantId는 윗길이다.</summary>
        public int LowerOccupantId = -1;
        /// <summary>팔레트 종류. 팔레트가 아니면 None.</summary>
        public PalletKind PalletKind = PalletKind.None;
        /// <summary>팔레트 위 더미(아래 → 위 순서). 흐름(ConveyorSim.Packages)에는 없는 택배들이다.</summary>
        public readonly List<Package> Pallet = new List<Package>();
        /// <summary>분배기의 명시 빨간 출구(스펙 11.8). null이면 자동 규칙(11.7). 분배기가 아니면 null.</summary>
        public Direction? RedDirection;
        /// <summary>분배기·자동 검사기의 모드(스펙 14.5). 다른 칸은 항상 Inspect.</summary>
        public RouteMode Mode = RouteMode.Inspect;
        /// <summary>자동 검사기의 항공 출구(2번 출구, 스펙 14.5). 항공 모드로 처음 바꿀 때 정해진다. 검사기가 아니면 null.</summary>
        public Direction? AirDirection;

        public bool IsOccupied => OccupantId >= 0;

        /// <summary>손으로 택배를 놓을 차선이 남아 있는가. 브릿지는 윗길 또는 아랫길 중 하나라도 비면 true.</summary>
        public bool HasFreeLane => !IsOccupied || (Type == CellType.Bridge && LowerOccupantId < 0);

        /// <summary>차선별 점유 id(브릿지가 아니면 Upper만 의미 있다).</summary>
        public int OccupantOf(Lane lane) => lane == Lane.Lower ? LowerOccupantId : OccupantId;

        public void SetOccupant(Lane lane, int id)
        {
            if (lane == Lane.Lower) LowerOccupantId = id;
            else OccupantId = id;
        }

        public bool IsPalletFull => Pallet.Count >= PalletCapacity;

        /// <summary>택배가 올라가 이동할 수 있는 셀. 팔레트는 흐름이 아니라 더미이므로 포함하지 않는다.</summary>
        public bool IsTrack =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Inlet ||
            Type == CellType.Table || Type == CellType.Scanner || Type == CellType.Bridge || Type == CellType.Labeler;

        /// <summary>플레이어가 설치·해체할 수 있는 셀.</summary>
        public bool IsBuilding =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Table || Type == CellType.Scanner ||
            Type == CellType.Pallet || Type == CellType.Bridge || Type == CellType.Labeler;

        /// <summary>택배를 받아 판정하는 셀(트럭 도크: 출고구, 반품구, 항공 출고·반품 — 스펙 14.1).</summary>
        public bool IsDock => IsDockType(Type);

        public static bool IsDockType(CellType t) =>
            t == CellType.Outlet || t == CellType.ReturnDock || IsAirDock(t);

        /// <summary>항공 트럭 도크(스펙 14.1). 부지 확장 2(12×12)에서 생긴다.</summary>
        public static bool IsAirDock(CellType t) => t == CellType.AirOutlet || t == CellType.AirReturnDock;

        public float Speed => Type == CellType.FastConveyor ? 2f : 1f;
    }
}
