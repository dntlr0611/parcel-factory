using System.Collections.Generic;

namespace ParcelFactory.Core
{
    /// <summary>
    /// 공장 한 곳의 저장 상태(v10, 스펙 19.5): 격자·택배·팔레트·입고구 타이머·해금·항공/취급주의 상태·트럭·집계.
    /// 1공장은 GameState의 최상위 필드 그대로(GameState가 이 클래스를 잇는다)라서 v6~v9 저장은 바꾸지 않고 1공장 하나짜리 회사로 읽힌다.
    /// 2·3공장은 GameState.OtherFactories에 들어간다.
    /// </summary>
    public class FactoryState
    {
        /// <summary>공장 종류 이름(Headquarters/Airport/Precision, v10). 없으면(null) 최상위는 Headquarters.</summary>
        public string Kind;
        public int UnlockedSize;
        public List<string> Purchased = new List<string>();
        public List<CellState> Cells = new List<CellState>();
        public List<PackageState> Packages = new List<PackageState>();
        /// <summary>입고구 (0,2)의 타이머. v8부터는 InletTimers에도 있다.</summary>
        public float SpawnTimer;
        /// <summary>입고구마다의 타이머(v8, 스펙 16.2). 없으면(null, v7 이하) (0,2)는 SpawnTimer, 나머지는 기본값(간격의 절반).</summary>
        public List<InletTimerState> InletTimers;
        public int NextPackageId;
        /// <summary>취급주의 택배 시작일(v9, 스펙 17.1). 부지 확장 2가 없으면 0. v8 이하에는 없다(부지 확장 2가 있으면 읽은 날 + 1).</summary>
        public int FragileFromDay;
        /// <summary>트럭 적재 수·대기(v9, 스펙 18, TruckFleet.Docks 순서). 없으면(null, v8 이하) 모두 빈 트럭.</summary>
        public List<TruckState> Trucks;
        /// <summary>그날 출발한 트럭 수(v9). 하루 요약용.</summary>
        public int TrucksDepartedToday;
        public int DeliveredCount;
        public int LostCount;
        /// <summary>누적 검사 수(v5).</summary>
        public int InspectedCount;
        /// <summary>그날 시작 누적 카운터(v5). 하루 요약은 이 값 대비 증가분.</summary>
        public int DayStartDelivered;
        public int DayStartLost;
        public int DayStartInspected;
        /// <summary>그날 이 공장의 배송 판정 금액 합(v10, 하루 요약의 공장별 잔액 기여). v9 이하에는 없다(0).</summary>
        public int EarnedToday;
    }

    /// <summary>저장 파일(v11): 회사(돈·EXP·능력치·시계·작업자·현재 공장 번호·점심 카드) + 1공장(상속한 최상위 필드) + 나머지 공장(OtherFactories).</summary>
    public sealed class GameState : FactoryState
    {
        /// <summary>기본값 0. Capture가 명시적으로 채우므로 필드가 없는 JSON은 버전 불일치로 거부된다.</summary>
        public int Version;
        public int Money;
        public bool Paused;
        public float Speed;
        /// <summary>작업자(지금 공장 기준 좌표, 들고 있는 택배는 지금 공장의 것).</summary>
        public WorkerState Worker;
        /// <summary>근무 시계(v5, 스펙 11.1).</summary>
        public int Day;
        public float MinuteOfDay;
        /// <summary>그날 시작 잔액(v5).</summary>
        public int DayStartMoney;
        /// <summary>작업자 스태미나 0..최대 체력(v5; v6부터 최대는 100 + 20 × StaminaLevel).</summary>
        public float Stamina;
        /// <summary>쓸 수 있는 EXP(v6, 스펙 11.5).</summary>
        public int Exp;
        /// <summary>누적 획득 EXP(v6). 하루 요약의 획득 EXP 계산용.</summary>
        public int TotalExp;
        /// <summary>그날 시작 시점의 누적 획득 EXP(v6).</summary>
        public int DayStartExp;
        /// <summary>강화 레벨(v6).</summary>
        public int StaminaLevel;
        public int SpeedLevel;
        public int RegenLevel;
        /// <summary>지금 공장 번호(0부터, v10). 0 = 1공장.</summary>
        public int CurrentFactory;
        /// <summary>2·3공장(v10, 번호 순서). 없으면(null, v9 이하) 1공장 하나.</summary>
        public List<FactoryState> OtherFactories;
        /// <summary>오늘의 메뉴 이름(v11, 스펙 21.4). 없으면(null, v10 이하) None.</summary>
        public string LunchMenu;
        /// <summary>오늘 점심 카드를 확정했는가(v11).</summary>
        public bool LunchConfirmed;
        /// <summary>오늘의 계약(v11). 없으면 null.</summary>
        public ContractState Contract;
        /// <summary>오늘 계약 후보(v11, 점심 중에 저장해도 같은 후보가 다시 뜨게). 없으면(null) 비어 있음.</summary>
        public List<ContractState> ContractCandidates;
    }

    /// <summary>계약 하나(v11): 종류 이름(Deliver/NoLoss/Return/Air/Fragile/NoMissingWaybill)·목표·진행·달성.</summary>
    public sealed class ContractState
    {
        public string Kind;
        public int Target;
        public int Progress;
        public bool Achieved;
    }

    public sealed class TruckState
    {
        /// <summary>도크 종류 이름(Outlet/ReturnDock/AirOutlet/AirReturnDock).</summary>
        public string Dock;
        public int Loaded;
        /// <summary>도크에 트럭이 설 때까지 남은 게임 분. 0이면 트럭이 도크에 있다. v10에서는 Vehicles가 우선이고 이 값은 참고용이다.</summary>
        public float AwayMinutes;
        /// <summary>차량별 상태(v10, 스펙 20). 대수 = 개수(1~3). 없으면(null, v9) 1대: AwayMinutes > 0이면 운행 중, 아니면 도크.</summary>
        public List<VehicleState> Vehicles;
    }

    public sealed class VehicleState
    {
        /// <summary>AtDock/Garage/Driving/Swapping.</summary>
        public string Status;
        /// <summary>운행·교대 남은 게임 분(도크·차고면 0).</summary>
        public float Minutes;
    }

    public sealed class InletTimerState
    {
        public int X;
        public int Y;
        public float Timer;
    }

    public sealed class CellState
    {
        public int X;
        public int Y;
        public string Type;
        public string Direction;
        public bool Demolishing;
        public float DemolishRemaining;
        // 분배기 상태 필드는 없다(스펙 11.7: 분배기는 검사 결과로 분류해 상태가 없다). 옛 저장의 SplitterIndex(11.6)·
        // SplitterToggle 키는 Json 미지 필드로 무시된다. 버전은 v6 그대로.
        /// <summary>팔레트 종류(None/Blue/Red). v4.</summary>
        public string PalletKind;
        /// <summary>팔레트 더미(아래 → 위). 팔레트가 아니면 null. v4.</summary>
        public List<PackageState> Pallet;
        /// <summary>분배기의 명시 빨간 출구(스펙 11.8). 설정 안 됐거나 분배기가 아니면 null. 선택 필드라 버전은 v6 그대로.</summary>
        public string RedDirection;
        /// <summary>분배기·자동 검사기 모드 "Inspect"/"Air"(v7, 스펙 14.5). 없으면(null) Inspect.</summary>
        public string Mode;
        /// <summary>자동 검사기의 항공 출구(v7). 정해지지 않았거나 검사기가 아니면 null.</summary>
        public string AirDirection;
    }

    public sealed class PackageState
    {
        public int Id;
        public string Kind;
        public int X;
        public int Y;
        public float Progress;
        public string Heading;
        public string EntryHeading;
        /// <summary>브릿지 차선 "Upper"/"Lower"(스펙 11.12). 없으면(null) Upper. 선택 필드라 버전은 v6 그대로.
        /// Cell.LowerOccupantId는 저장하지 않고 복원 시 이 값으로 다시 세운다.</summary>
        public string Lane;
        public bool Bad;
        public bool Inspected;
        public bool Rejected;
        /// <summary>항공 택배(v7, 스펙 14.4). v6 저장에는 없으므로 false로 읽힌다.</summary>
        public bool Air;
        /// <summary>운송장(v7, 스펙 15.3). 없으면 false.</summary>
        public bool Waybill;
        /// <summary>취급주의·파손·취급주의 스티커(v9, 스펙 17). 없으면 false.</summary>
        public bool Fragile;
        public bool Broken;
        public bool FragileSticker;
        public string LabelDestination;
        public int LabelWeight;
        public string LabelMethod;
        public int LabelScale;
    }

    public sealed class WorkerState
    {
        /// <summary>연속 좌표(칸 단위).</summary>
        public float X;
        public float Y;
        public string Facing;
        /// <summary>들고 있는 택배. 없으면 null. X/Y는 무시된다.</summary>
        public PackageState Carrying;
    }
}
