using System;

namespace ParcelFactory.Core
{
    public enum DeliveryOutcome
    {
        Delivered,            // 승인·정상 → 출고구 +30
        ReturnedBad,          // 거부·불량 → 반품구 +10
        UninspectedToOutlet,  // −10
        RejectedToOutlet,     // −10
        BadToOutlet,          // 승인·불량 → 출고구 −30
        UninspectedToReturn,  // −5
        GoodToReturn,         // 거부·정상 → 반품구 −5
        ApprovedToReturn,     // 승인 → 반품구 −5
        WrongCarrier,         // 항공 택배 → 일반 트럭, 일반 택배 → 항공 트럭 −10 (스펙 14.3)
        NoWaybillToOutlet,    // 검사했지만 운송장 없이 출고(일반·항공) −20 (스펙 15.3)
        NoWaybillToReturn,    // 검사했지만 운송장 없이 반품(일반·항공) −10
    }

    /// <summary>스펙 9.3 판정표 + 14.3: 운송 수단(항공/일반)이 틀리면 검사 여부보다 먼저 WrongCarrier.
    /// 항공 출고·반품은 각각 출고·반품 규칙을 따른다. 15.3: 운송 수단 틀림 → 미검사 → 운송장 없음 → 기존 결과 순서.</summary>
    public static class DeliveryScoring
    {
        public static DeliveryOutcome Outcome(Package p, CellType dock)
        {
            if (!Cell.IsDockType(dock)) throw new ArgumentException($"{dock} is not a dock", nameof(dock));
            if (p.Air != Cell.IsAirDock(dock)) return DeliveryOutcome.WrongCarrier;
            switch (dock == CellType.AirOutlet ? CellType.Outlet : dock == CellType.AirReturnDock ? CellType.ReturnDock : dock)
            {
                case CellType.Outlet:
                    if (!p.Inspected) return DeliveryOutcome.UninspectedToOutlet;
                    if (!p.Waybill) return DeliveryOutcome.NoWaybillToOutlet;
                    if (p.Rejected) return DeliveryOutcome.RejectedToOutlet;
                    return p.Bad ? DeliveryOutcome.BadToOutlet : DeliveryOutcome.Delivered;
                case CellType.ReturnDock:
                    if (!p.Inspected) return DeliveryOutcome.UninspectedToReturn;
                    if (!p.Waybill) return DeliveryOutcome.NoWaybillToReturn;
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
                case DeliveryOutcome.Delivered: return 30;
                case DeliveryOutcome.ReturnedBad: return 10;
                case DeliveryOutcome.UninspectedToOutlet: return -10;
                case DeliveryOutcome.RejectedToOutlet: return -10;
                case DeliveryOutcome.BadToOutlet: return -30;
                case DeliveryOutcome.UninspectedToReturn: return -5;
                case DeliveryOutcome.GoodToReturn: return -5;
                case DeliveryOutcome.ApprovedToReturn: return -5;
                case DeliveryOutcome.WrongCarrier: return -10;
                case DeliveryOutcome.NoWaybillToOutlet: return -20;
                case DeliveryOutcome.NoWaybillToReturn: return -10;
                default: throw new ArgumentOutOfRangeException(nameof(o));
            }
        }

        public static bool IsSuccess(DeliveryOutcome o) =>
            o == DeliveryOutcome.Delivered || o == DeliveryOutcome.ReturnedBad;
    }
}
