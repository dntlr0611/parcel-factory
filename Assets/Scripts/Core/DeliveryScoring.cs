using System;

namespace ParcelFactory.Core
{
    public enum DeliveryOutcome
    {
        Delivered,            // 승인·정상 → 출고구 +15
        ReturnedBad,          // 거부·불량 → 반품구 +5
        UninspectedToOutlet,  // −10
        RejectedToOutlet,     // −10
        BadToOutlet,          // 승인·불량 → 출고구 −20
        UninspectedToReturn,  // −5
        GoodToReturn,         // 거부·정상 → 반품구 −5
        ApprovedToReturn,     // 승인 → 반품구 −5
    }

    /// <summary>스펙 9.3 판정표.</summary>
    public static class DeliveryScoring
    {
        public static DeliveryOutcome Outcome(Package p, CellType dock)
        {
            switch (dock)
            {
                case CellType.Outlet:
                    if (!p.Inspected) return DeliveryOutcome.UninspectedToOutlet;
                    if (p.Rejected) return DeliveryOutcome.RejectedToOutlet;
                    return p.Bad ? DeliveryOutcome.BadToOutlet : DeliveryOutcome.Delivered;
                case CellType.ReturnDock:
                    if (!p.Inspected) return DeliveryOutcome.UninspectedToReturn;
                    if (p.Rejected) return p.Bad ? DeliveryOutcome.ReturnedBad : DeliveryOutcome.GoodToReturn;
                    return DeliveryOutcome.ApprovedToReturn;
                default:
                    throw new ArgumentException($"{dock} is not a dock", nameof(dock));
            }
        }

        public static int Delta(DeliveryOutcome o)
        {
            switch (o)
            {
                case DeliveryOutcome.Delivered: return 15;
                case DeliveryOutcome.ReturnedBad: return 5;
                case DeliveryOutcome.UninspectedToOutlet: return -10;
                case DeliveryOutcome.RejectedToOutlet: return -10;
                case DeliveryOutcome.BadToOutlet: return -20;
                case DeliveryOutcome.UninspectedToReturn: return -5;
                case DeliveryOutcome.GoodToReturn: return -5;
                case DeliveryOutcome.ApprovedToReturn: return -5;
                default: throw new ArgumentOutOfRangeException(nameof(o));
            }
        }

        public static bool IsSuccess(DeliveryOutcome o) =>
            o == DeliveryOutcome.Delivered || o == DeliveryOutcome.ReturnedBad;
    }
}
