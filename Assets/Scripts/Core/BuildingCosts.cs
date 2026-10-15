using System;

namespace ParcelFactory.Core
{
    public static class BuildingCosts
    {
        public static int Cost(CellType type)
        {
            switch (type)
            {
                case CellType.Conveyor: return 10;
                case CellType.FastConveyor: return 25;
                case CellType.Splitter: return 60;
                default: throw new ArgumentException($"{type} is not buildable", nameof(type));
            }
        }
    }
}
