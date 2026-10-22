using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    /// <summary>공장 종류(스펙 19.2). 번호 순서(1공장 = 본사)이고 저장·구매 순서도 이 순서다.</summary>
    public enum FactoryKind { Headquarters, Airport, Precision }

    /// <summary>공장별 정상 출고 보상(스펙 19.2). 판정(DeliveryOutcome)은 같고 금액만 바꾼다. 전역 판정표(DeliveryScoring.Delta)는 그대로다.</summary>
    public sealed class RewardProfile
    {
        /// <summary>항공 택배의 정상 출고(Delivered) 금액. 기본 +30.</summary>
        public int AirDelivered = 30;
        /// <summary>취급주의 정상 출고(DeliveredFragile) 금액. 기본 +45. 항공이면서 취급주의면 이 값이다.</summary>
        public int FragileDelivered = 45;
    }

    /// <summary>공장 종류마다 정해진 값(스펙 19.2).</summary>
    public sealed class FactoryProfile
    {
        public const int AirportPrice = 3000, PrecisionPrice = 8000;
        public const double AirportAirChance = 0.6, PrecisionFragileChance = 0.5;
        public const int AirportAirDelivered = 35, PrecisionFragileDelivered = 60;

        public FactoryKind Kind;
        /// <summary>1부터. 상단 탭의 "n공장".</summary>
        public int Number;
        /// <summary>"본사 물류센터" 같은 이름.</summary>
        public string Name;
        /// <summary>구매 가격(1공장은 0, 처음부터 있다).</summary>
        public int Price;
        /// <summary>항공 도크 두 곳(과 동쪽 트럭 네 대)이 처음부터 있다(공항).</summary>
        public bool AirDocksFromStart;
        /// <summary>부지 확장과 무관한 항공 비율(공항 60%). null이면 부지 확장 2 뒤 25%(스펙 14.2).</summary>
        public double? FixedAirChance;
        /// <summary>처음부터의 취급주의 비율(정밀 50%). null이면 부지 확장 2 다음 날부터 15%(스펙 17.1).</summary>
        public double? FixedFragileChance;
        public RewardProfile Rewards = new RewardProfile();

        /// <summary>"1공장".</summary>
        public string ShortName => Number + "공장";
        /// <summary>"1공장 · 본사 물류센터".</summary>
        public string FullName => ShortName + " · " + Name;

        public static readonly IReadOnlyList<FactoryProfile> All = new[]
        {
            new FactoryProfile { Kind = FactoryKind.Headquarters, Number = 1, Name = "본사 물류센터" },
            new FactoryProfile
            {
                Kind = FactoryKind.Airport, Number = 2, Name = "공항 물류센터", Price = AirportPrice,
                AirDocksFromStart = true, FixedAirChance = AirportAirChance, Rewards = new RewardProfile { AirDelivered = AirportAirDelivered },
            },
            new FactoryProfile
            {
                Kind = FactoryKind.Precision, Number = 3, Name = "정밀화물 센터", Price = PrecisionPrice,
                FixedFragileChance = PrecisionFragileChance, Rewards = new RewardProfile { FragileDelivered = PrecisionFragileDelivered },
            },
        };

        /// <summary>구매 조건을 보는 직전 공장(스펙 19.2: 2공장은 1공장, 3공장은 2공장). 1공장은 없음(예외).</summary>
        public static FactoryKind Previous(FactoryKind kind)
        {
            if (kind == FactoryKind.Headquarters) throw new ArgumentOutOfRangeException(nameof(kind), kind, "the first factory has no previous one");
            return (FactoryKind)((int)kind - 1);
        }

        public static FactoryProfile Of(FactoryKind kind)
        {
            foreach (var p in All) if (p.Kind == kind) return p;
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown factory kind");
        }
    }

    /// <summary>택배차 구매 결과(스펙 20).</summary>
    public enum TruckPurchaseResult { Ok, MaxTrucks, NoDock, NotEnoughMoney }

    /// <summary>공장 한 곳의 하루 요약 줄(스펙 19.1): 그날 시작 대비.</summary>
    public sealed class FactoryDaySummary
    {
        public FactoryKind Kind;
        public int Number;
        public string Name;
        public int Delivered, Lost, Inspected, TrucksDeparted;
        /// <summary>그날 이 공장의 배송 판정 금액 합(잔액 기여). 설치·해금 지출은 회사 돈이라 넣지 않는다.</summary>
        public int Earned;
    }

    /// <summary>
    /// 공장 한 곳(스펙 19.1): 격자(부지 확장 포함), 컨베이어·택배·팔레트, 입고구 타이머, 해금, 항공·취급주의 시작 상태, 트럭, 배송/소실/검사 집계.
    /// 돈(Economy)과 시계(Clock)는 회사 것을 받아 쓴다. 시간은 Company.Tick이 TickSegment로 흘린다.
    /// </summary>
    public sealed class FactorySite
    {
        public readonly FactoryKind Kind;
        public readonly FactoryGrid Grid;
        public readonly ConveyorSim Sim;
        public readonly Spawner Spawner;
        public readonly Toolbar Toolbar;
        public readonly BuildService Build;
        public readonly UnlockService Unlocks;
        public FactoryProfile Profile => FactoryProfile.Of(Kind);
        public TruckFleet Trucks => Sim.Trucks;

        /// <summary>그날 시작 시점의 누적 카운터. 하루 요약은 이 값 대비 증가분이다.</summary>
        public int DayStartDelivered { get; set; }
        public int DayStartLost { get; set; }
        public int DayStartInspected { get; set; }
        /// <summary>그날 배송 판정 금액 합(하루 요약의 공장별 잔액 기여). 하루 시작에 0.</summary>
        public int EarnedToday { get; set; }

        /// <summary>취급주의 택배가 들어오기 시작하는 날(스펙 17.1): 이 공장의 부지 확장 2를 산 날 + 1. 사기 전에는 0. 바꾸면 확률을 다시 정한다.</summary>
        public int FragileFromDay
        {
            get => fragileFromDay;
            set { fragileFromDay = value; UpdateChances(); }
        }
        private int fragileFromDay;

        private readonly Clock clock;
        private readonly Economy economy;
        private readonly bool ownsFactory;

        /// <summary>factory가 null이면 공장이 직접 만든 팩토리를 쓰고 항공·취급주의 확률을 규칙대로 관리한다. 주입한 팩토리(테스트 등)의 확률은 건드리지 않는다.</summary>
        /// <summary>rng는 공장이 직접 만드는 팩토리의 난수(테스트에서 결정적으로). factory를 주면 무시한다.</summary>
        public FactorySite(FactoryKind kind, Economy economy, Clock clock, PackageFactory factory = null, Random rng = null)
        {
            var profile = FactoryProfile.Of(kind); // 알 수 없는 종류면 예외
            Kind = kind;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.economy = economy;
            Grid = new FactoryGrid();
            if (profile.AirDocksFromStart) Grid.AddAirDocks(); // 공항: 항공 도크·트럭이 처음부터(스펙 19.2)
            ownsFactory = factory == null;
            Sim = new ConveyorSim(Grid, economy, factory ?? new PackageFactory(rng ?? new Random())) { Rewards = profile.Rewards };
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, economy, Toolbar, Sim);
            Unlocks = new UnlockService(economy, Grid, Toolbar);
            Sim.PackageDelivered += (p, o, delta) => EarnedToday += delta;
            Unlocks.Unlocked += OnUnlocked;
            UpdateChances();
            CaptureDayStart();
        }

        /// <summary>항공 택배는 부지 확장 2를 가진 동안 25%(스펙 14.2), 취급주의는 FragileFromDay 09:00부터 15%(스펙 17.1).
        /// 프로필에 고정 비율이 있으면 그 값(공항 항공 60%, 정밀 취급주의 50%, 스펙 19.2). 세션이 만든 팩토리만 바꾼다.
        /// 다음 날 아침(Company.NextDay)과 해금 때 다시 부른다.</summary>
        public void UpdateChances()
        {
            if (!ownsFactory) return;
            var profile = Profile;
            Sim.Factory.AirChance = profile.FixedAirChance ?? (Unlocks.IsPurchased(UnlockId.Plot2) ? PackageFactory.AirChanceAfterPlot2 : 0.0);
            Sim.Factory.FragileChance = profile.FixedFragileChance ??
                (fragileFromDay > 0 && clock.Day >= fragileFromDay ? PackageFactory.FragileChanceAfterPlot2 : 0.0);
        }

        /// <summary>항공 도크 두 곳이 있는가(부지 확장 2 뒤, 또는 공항은 처음부터).</summary>
        public bool HasAirDocks => Grid.HasAirDocks;

        /// <summary>저장 복원(Grant)도 같은 이벤트로 켠다.</summary>
        private void OnUnlocked(UnlockId id)
        {
            if (id != UnlockId.Plot2) return;
            if (fragileFromDay == 0) FragileFromDay = clock.Day + 1; // 산 날에는 없고 다음 날 아침부터(스펙 17.1)
            else UpdateChances();
        }

        /// <summary>시계 한 구간(단계 경계 안) 동안의 진행. 입고와 트럭 대기는 근무 중에만, 시뮬레이션·해체는 항상(스펙 11.1, 18).</summary>
        internal void TickSegment(float seconds, bool working)
        {
            if (working) Spawner.Tick(seconds);
            Sim.Tick(seconds);
            if (working) Sim.Trucks.Advance(seconds * Clock.MinutesPerSecond); // 점심에는 대기도 멈춘다
            Build.Tick(seconds);
        }

        public void CaptureDayStart()
        {
            DayStartDelivered = Sim.DeliveredCount;
            DayStartLost = Sim.LostCount;
            DayStartInspected = Sim.InspectedCount;
            EarnedToday = 0;
        }

        public FactoryDaySummary BuildSummary() => new FactoryDaySummary
        {
            Kind = Kind,
            Number = Profile.Number,
            Name = Profile.Name,
            Delivered = Sim.DeliveredCount - DayStartDelivered,
            Lost = Sim.LostCount - DayStartLost,
            Inspected = Sim.InspectedCount - DayStartInspected,
            TrucksDeparted = Sim.Trucks.DepartedToday,
            Earned = EarnedToday,
        };

        /// <summary>이 공장에 그 도크의 트럭이 있는가: 출고·반품은 항상, 항공 도크는 항공 도크가 있을 때만.</summary>
        public bool HasTruckDock(CellType dock) =>
            Array.IndexOf(TruckFleet.Docks, dock) >= 0 && (!Cell.IsAirDock(dock) || HasAirDocks);

        /// <summary>TryBuyTruck과 같은 검사, 지출 없음(상점 표시용). 순서: 도크 → 최대 대수 → 잔액.</summary>
        public TruckPurchaseResult CanBuyTruck(CellType dock)
        {
            if (!HasTruckDock(dock)) return TruckPurchaseResult.NoDock;
            var price = Trucks.NextTruckPrice(dock);
            if (!price.HasValue) return TruckPurchaseResult.MaxTrucks;
            return economy.CanAfford(price.Value) ? TruckPurchaseResult.Ok : TruckPurchaseResult.NotEnoughMoney;
        }

        /// <summary>이 공장의 그 도크에 택배차 한 대를 회사 돈으로 산다(스펙 20: 2대째 ₩400, 3대째 ₩900, 최대 3대).</summary>
        public TruckPurchaseResult TryBuyTruck(CellType dock)
        {
            var r = CanBuyTruck(dock);
            if (r != TruckPurchaseResult.Ok) return r;
            economy.TrySpend(Trucks.NextTruckPrice(dock).Value);
            Trucks.AddVehicle(dock);
            return TruckPurchaseResult.Ok;
        }

        /// <summary>검사 대기 + 운송장 대기 택배 수(다른 공장 요약 칩, 스펙 19.3).</summary>
        public int WaitingCount => Sim.AwaitingInspectionCount + Sim.AwaitingLabelCount;
    }
}
