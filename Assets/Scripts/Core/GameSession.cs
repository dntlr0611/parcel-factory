using System;

namespace ParcelFactory.Core
{
    /// <summary>
    /// 게임 한 판. 회사(Company, 스펙 19.1)를 갖고, 예전 API는 그대로 둔 얇은 파사드다: 공장별 항목(Grid·Sim·Spawner·Toolbar·Build·Unlocks·
    /// FragileFromDay·그날 시작 카운터)은 지금 공장(Company.Current)을, 회사 공유 항목(Economy·Clock·Progression·Worker·Paused·Speed·
    /// DayStartMoney·DayStartExp·DayEnded·Tick·NextDay·BuildSummary)은 회사를 가리킨다. 공장을 옮기면 공장별 항목이 가리키는 대상이 바뀐다.
    /// </summary>
    public sealed class GameSession
    {
        public readonly Company Company;

        /// <summary>지금 공장.</summary>
        public FactorySite Factory => Company.Current;
        public FactoryGrid Grid => Factory.Grid;
        public ConveyorSim Sim => Factory.Sim;
        public Spawner Spawner => Factory.Spawner;
        public Toolbar Toolbar => Factory.Toolbar;
        public BuildService Build => Factory.Build;
        public UnlockService Unlocks => Factory.Unlocks;

        public Economy Economy => Company.Economy;
        public WorkerService Worker => Company.Worker;
        /// <summary>근무 시계(스펙 11.1).</summary>
        public Clock Clock => Company.Clock;
        /// <summary>EXP와 작업자 강화(스펙 11.5). Worker가 같은 인스턴스를 읽는다.</summary>
        public Progression Progression => Company.Progression;

        public bool Paused { get => Company.Paused; set => Company.Paused = value; }
        public float Speed { get => Company.Speed; set => Company.Speed = value; }

        /// <summary>그날 시작 시점의 잔액·누적 카운터. 하루 요약은 이 값 대비 증가분이다. 카운터는 지금 공장의 것이다.</summary>
        public int DayStartMoney { get => Company.DayStartMoney; set => Company.DayStartMoney = value; }
        public int DayStartDelivered { get => Factory.DayStartDelivered; set => Factory.DayStartDelivered = value; }
        public int DayStartLost { get => Factory.DayStartLost; set => Factory.DayStartLost = value; }
        public int DayStartInspected { get => Factory.DayStartInspected; set => Factory.DayStartInspected = value; }
        /// <summary>그날 시작 시점의 Progression.TotalEarned. 하루 요약의 획득 EXP는 이 값 대비 증가분이다.</summary>
        public int DayStartExp { get => Company.DayStartExp; set => Company.DayStartExp = value; }

        /// <summary>지금 공장의 취급주의 시작일(스펙 17.1).</summary>
        public int FragileFromDay { get => Factory.FragileFromDay; set => Factory.FragileFromDay = value; }

        /// <summary>18:00이 되는 틱에 한 번 발생한다.</summary>
        public event Action<DaySummary> DayEnded
        {
            add => Company.DayEnded += value;
            remove => Company.DayEnded -= value;
        }

        public GameSession() : this(new Economy(), null) { }

        public GameSession(Economy economy) : this(economy, null) { }

        /// <summary>factory는 1공장이 쓸 팩토리(테스트용). null이면 공장이 직접 만들고 항공·취급주의 확률을 관리한다.</summary>
        public GameSession(Economy economy, PackageFactory factory)
        {
            Company = new Company(economy, factory);
        }

        public void Tick(float dt) => Company.Tick(dt);

        /// <summary>현재 날짜의 요약(그날 시작 대비). 퇴근 상태로 복원된 세션에서 요약 패널을 다시 띄울 때도 쓴다.</summary>
        public DaySummary BuildSummary() => Company.BuildSummary();

        public void NextDay() => Company.NextDay();
    }
}
