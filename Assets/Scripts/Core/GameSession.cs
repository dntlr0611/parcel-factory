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

        /// <summary>항공 택배는 부지 확장 2를 가진 동안에만 25% 생긴다(스펙 14.2). 저장 복원(Grant)도 같은 이벤트로 켠다.</summary>
        private void OnUnlocked(UnlockId id)
        {
            if (id == UnlockId.Plot2) UpdateAirChance();
        }

        /// <summary>세션이 직접 만든 팩토리만 바꾼다. 주입한 팩토리(테스트 등)의 항공 확률은 그대로 둔다.</summary>
        private void UpdateAirChance()
        {
            if (ownsFactory) Sim.Factory.AirChance = Unlocks.IsPurchased(UnlockId.Plot2) ? PackageFactory.AirChanceAfterPlot2 : 0.0;
        }

        private readonly bool ownsFactory;

        /// <summary>EXP 적립(스펙 11.5): 정상 출고 +10, 불량 반품 +8. 실패 판정은 0.</summary>
        private void OnPackageDelivered(Package p, DeliveryOutcome outcome, int delta)
        {
            if (!DeliveryScoring.IsSuccess(outcome)) return;
            Progression.AddExp(outcome == DeliveryOutcome.Delivered ? Progression.DeliveredExp : Progression.ReturnedBadExp);
        }

        /// <summary>검사 판정(검사대·자동 검사기) +2.</summary>
        private void OnInspected() => Progression.AddExp(Progression.InspectExp);

        /// <summary>체력 강화 시 현재 체력도 같은 양만큼 오른다.</summary>
        private void OnUpgraded(Stat stat, int level)
        {
            if (stat == Stat.Stamina) Worker.AddStamina(Progression.StaminaPerLevel);
        }

        /// <summary>일시정지이거나 퇴근(Closed)이면 아무것도 하지 않는다. 시간은 단계 경계(12:00·13:00·18:00)에서 나눠 처리한다:
        /// 입고는 Working 구간에서만, 시뮬레이션·해체는 18:00까지 항상, 작업자 회복은 구간의 단계로.
        /// 18:00에 닿은 틱에 DayEnded가 한 번 발생한다.</summary>
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
                Build.Tick(seg);
                Clock.Advance(seg);
                remaining -= seg;
            }
            bool moved = Worker.ConsumeMoved(); // 한 프레임의 이동 여부를 두 구간에 같이 쓴다(스펙 11.11)
            if (workSeconds > 0f) Worker.Rest(workSeconds, DayPhase.Working, moved);
            if (lunchSeconds > 0f) Worker.Rest(lunchSeconds, DayPhase.Lunch, moved);
            if (!Clock.IsOpen) DayEnded?.Invoke(BuildSummary());
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
        };

        /// <summary>퇴근 상태에서만 동작: 다음 날 09:00, 스태미나 가득, 그날 시작 값 기록. 레일·팔레트 위 택배는 그대로.</summary>
        public void NextDay()
        {
            if (Clock.IsOpen) return;
            Clock.NextDay();
            Worker.Refill();
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
