using System;

namespace ParcelFactory.Core
{
    public sealed class GameSession
    {
        public readonly FactoryGrid Grid;
        public readonly Economy Economy;
        public readonly ConveyorSim Sim;
        public readonly Spawner Spawner;
        public readonly Toolbar Toolbar;
        public readonly BuildService Build;
        public readonly UnlockService Unlocks;
        public readonly WorkerService Worker;
        /// <summary>근무 시계(스펙 11.1).</summary>
        public readonly Clock Clock = new Clock();
        /// <summary>EXP와 작업자 강화(스펙 11.5). Worker가 같은 인스턴스를 읽는다.</summary>
        public readonly Progression Progression = new Progression();

        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        /// <summary>그날 시작 시점의 잔액·누적 카운터. 하루 요약은 이 값 대비 증가분이다.</summary>
        public int DayStartMoney { get; set; }
        public int DayStartDelivered { get; set; }
        public int DayStartLost { get; set; }
        public int DayStartInspected { get; set; }
        /// <summary>그날 시작 시점의 Progression.TotalEarned. 하루 요약의 획득 EXP는 이 값 대비 증가분이다.</summary>
        public int DayStartExp { get; set; }

        /// <summary>취급주의 택배가 들어오기 시작하는 날(스펙 17.1): 부지 확장 2를 산 날 + 1. 사기 전에는 0. 바꾸면 확률을 다시 정한다.</summary>
        public int FragileFromDay
        {
            get => fragileFromDay;
            set { fragileFromDay = value; UpdateFragileChance(); }
        }
        private int fragileFromDay;

        /// <summary>18:00이 되는 틱에 한 번 발생한다.</summary>
        public event Action<DaySummary> DayEnded;

        public GameSession() : this(new Economy(), null) { }

        public GameSession(Economy economy) : this(economy, null) { }

        public GameSession(Economy economy, PackageFactory factory)
        {
            Grid = new FactoryGrid();
            Economy = economy;
            ownsFactory = factory == null;
            Sim = new ConveyorSim(Grid, Economy, factory);
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, Economy, Toolbar, Sim);
            Unlocks = new UnlockService(Economy, Grid, Toolbar);
            Worker = new WorkerService(Grid, Sim, Build, Progression);
            Sim.PackageDelivered += OnPackageDelivered;
            Sim.Inspected += OnInspected;
            Progression.Upgraded += OnUpgraded;
            Unlocks.Unlocked += OnUnlocked;
            UpdateAirChance();
            CaptureDayStart();
        }

        /// <summary>취급주의 택배는 FragileFromDay(부지 확장 2를 산 다음 날) 09:00부터 15% 생긴다. 세션이 만든 팩토리만 바꾼다.</summary>
        private void UpdateFragileChance()
        {
            if (ownsFactory)
                Sim.Factory.FragileChance = fragileFromDay > 0 && Clock.Day >= fragileFromDay ? PackageFactory.FragileChanceAfterPlot2 : 0.0;
        }

        /// <summary>항공 택배는 부지 확장 2를 가진 동안에만 25% 생긴다(스펙 14.2). 저장 복원(Grant)도 같은 이벤트로 켠다.</summary>
        private void OnUnlocked(UnlockId id)
        {
            if (id != UnlockId.Plot2) return;
            UpdateAirChance();
            if (fragileFromDay == 0) FragileFromDay = Clock.Day + 1; // 산 날에는 없고 다음 날 아침부터(스펙 17.1)
        }

        /// <summary>세션이 직접 만든 팩토리만 바꾼다. 주입한 팩토리(테스트 등)의 항공 확률은 그대로 둔다.</summary>
        private void UpdateAirChance()
        {
            if (ownsFactory) Sim.Factory.AirChance = Unlocks.IsPurchased(UnlockId.Plot2) ? PackageFactory.AirChanceAfterPlot2 : 0.0;
        }

        private readonly bool ownsFactory;

        /// <summary>EXP 적립(스펙 11.5): 정상 출고 +10(취급주의 정상 출고도 같다), 불량 반품 +8. 실패 판정은 0.</summary>
        private void OnPackageDelivered(Package p, DeliveryOutcome outcome, int delta)
        {
            if (!DeliveryScoring.IsSuccess(outcome)) return;
            Progression.AddExp(outcome == DeliveryOutcome.ReturnedBad ? Progression.ReturnedBadExp : Progression.DeliveredExp);
        }

        /// <summary>검사 판정(검사대·자동 검사기) +2.</summary>
        private void OnInspected() => Progression.AddExp(Progression.InspectExp);

        /// <summary>체력 강화 시 현재 체력도 같은 양만큼 오른다.</summary>
        private void OnUpgraded(Stat stat, int level)
        {
            if (stat == Stat.Stamina) Worker.AddStamina(Progression.StaminaPerLevel);
        }

        /// <summary>일시정지이거나 퇴근(Closed)이면 아무것도 하지 않는다. 시간은 단계 경계(12:00·13:00·18:00)에서 나눠 처리한다:
        /// 입고와 트럭 대기 시간(스펙 18)은 Working 구간에서만, 시뮬레이션·해체는 18:00까지 항상, 작업자 회복은 구간의 단계로.
        /// 18:00에 닿은 틱에 적재 중인 트럭이 모두 출발하고 DayEnded가 한 번 발생한다.</summary>
        public void Tick(float dt)
        {
            if (Paused || !(dt > 0f) || !Clock.IsOpen) return;
            float remaining = dt * Speed;
            float workSeconds = 0f, lunchSeconds = 0f;
            while (remaining > 0f && Clock.IsOpen)
            {
                var phase = Clock.Phase;
                float seg = Math.Min(remaining, Clock.SecondsToNextBoundary);
                if (phase == DayPhase.Working) { Spawner.Tick(seg); workSeconds += seg; }
                else lunchSeconds += seg;
                Sim.Tick(seg);
                if (phase == DayPhase.Working) Sim.Trucks.Advance(seg * Clock.MinutesPerSecond); // 점심에는 대기도 멈춘다
                Build.Tick(seg);
                Clock.Advance(seg);
                remaining -= seg;
            }
            bool moved = Worker.ConsumeMoved(); // 한 프레임의 이동 여부를 두 구간에 같이 쓴다(스펙 11.11)
            if (workSeconds > 0f) Worker.Rest(workSeconds, DayPhase.Working, moved);
            if (lunchSeconds > 0f) Worker.Rest(lunchSeconds, DayPhase.Lunch, moved);
            if (!Clock.IsOpen)
            {
                Sim.Trucks.DepartLoaded(); // 퇴근: 적재 중인 트럭은 모두 출발(스펙 18)
                DayEnded?.Invoke(BuildSummary());
            }
        }

        /// <summary>현재 날짜의 요약(그날 시작 대비). 퇴근 상태로 복원된 세션에서 요약 패널을 다시 띄울 때도 쓴다.</summary>
        public DaySummary BuildSummary() => new DaySummary
        {
            Day = Clock.Day,
            Delivered = Sim.DeliveredCount - DayStartDelivered,
            Lost = Sim.LostCount - DayStartLost,
            Inspected = Sim.InspectedCount - DayStartInspected,
            StartMoney = DayStartMoney,
            EndMoney = Economy.Money,
            Exp = Progression.TotalEarned - DayStartExp,
            TrucksDeparted = Sim.Trucks.DepartedToday,
        };

        /// <summary>퇴근 상태에서만 동작: 다음 날 09:00, 스태미나 가득, 그날 시작 값 기록, 트럭 네 대 모두 빈 트럭으로(스펙 18),
        /// 취급주의 확률 갱신(스펙 17.1). 레일·팔레트 위 택배는 그대로.</summary>
        public void NextDay()
        {
            if (Clock.IsOpen) return;
            Clock.NextDay();
            Worker.Refill();
            Sim.Trucks.StartDay();
            UpdateFragileChance();
            CaptureDayStart();
        }

        private void CaptureDayStart()
        {
            DayStartMoney = Economy.Money;
            DayStartDelivered = Sim.DeliveredCount;
            DayStartLost = Sim.LostCount;
            DayStartInspected = Sim.InspectedCount;
            DayStartExp = Progression.TotalEarned;
        }
    }
}
