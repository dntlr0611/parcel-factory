using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class GameState
    {
        /// <summary>기본값 0. Capture가 명시적으로 채우므로 필드가 없는 JSON은 버전 불일치로 거부된다.</summary>
        public int Version;
        public int Money;
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
        /// <summary>트럭 네 대의 적재 수·대기(v9, 스펙 18, TruckFleet.Docks 순서). 없으면(null, v8 이하) 모두 빈 트럭.</summary>
        public List<TruckState> Trucks;
        /// <summary>그날 출발한 트럭 수(v9). 하루 요약용.</summary>
        public int TrucksDepartedToday;
        public int DeliveredCount;
        public int LostCount;
        public bool Paused;
        public float Speed;
        public WorkerState Worker;
        /// <summary>근무 시계(v5, 스펙 11.1).</summary>
        public int Day;
        public float MinuteOfDay;
        /// <summary>그날 시작 잔액·누적 카운터(v5). 하루 요약은 이 값 대비 증가분.</summary>
        public int DayStartMoney;
        public int DayStartDelivered;
        public int DayStartLost;
        public int DayStartInspected;
        /// <summary>누적 검사 수(v5).</summary>
        public int InspectedCount;
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
    }

    public sealed class TruckState
    {
        /// <summary>도크 종류 이름(Outlet/ReturnDock/AirOutlet/AirReturnDock).</summary>
        public string Dock;
        public int Loaded;
        /// <summary>빈 트럭이 들어올 때까지 남은 게임 분. 0이면 트럭이 도크에 있다.</summary>
        public float AwayMinutes;
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
