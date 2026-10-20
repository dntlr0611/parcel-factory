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
        public float SpawnTimer;
        public int NextPackageId;
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
        public bool Bad;
        public bool Inspected;
        public bool Rejected;
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
