namespace ParcelFactory.Core
{
    public enum CellType { Empty, Locked, Conveyor, FastConveyor, Splitter, Inlet, Outlet }

    public sealed class Cell
    {
        public CellType Type = CellType.Empty;
        public Direction Direction = Direction.East;
        public bool Demolishing;
        public float DemolishRemaining;
        /// <summary>false → 다음 택배는 왼쪽 출구, true → 오른쪽 출구.</summary>
        public bool SplitterToggle;
        /// <summary>셀 위에 있는 택배 id. 없으면 -1.</summary>
        public int OccupantId = -1;

        public bool IsOccupied => OccupantId >= 0;

        /// <summary>택배가 올라가 이동할 수 있는 셀.</summary>
        public bool IsTrack =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Inlet;

        /// <summary>플레이어가 설치·해체할 수 있는 셀.</summary>
        public bool IsBuilding =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor || Type == CellType.Splitter;

        public float Speed => Type == CellType.FastConveyor ? 2f : 1f;
    }
}
