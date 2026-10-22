using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    /// <summary>새 공장 구매 결과(스펙 19.2).</summary>
    public enum FactoryPurchaseResult { Ok, AlreadyOwned, RequirementMissing, NotEnoughMoney }

    /// <summary>공장 옮기기 결과(스펙 19.3).</summary>
    public enum FactorySwitchResult { Ok, AlreadyHere, NoSuchFactory, Carrying }

    /// <summary>
    /// 회사(스펙 19.1): 공장(FactorySite) 1~3곳과 회사 공유 상태 — 돈(Economy), EXP·능력치(Progression), 시계(Clock), 작업자 1명.
    /// 모든 공장은 같은 시계로 동시에 시뮬레이션된다. 작업자는 지금 공장(Current)에 있고, 옮기면 그 공장의 격자·라인에 다시 묶인다.
    /// </summary>
    public sealed class Company
    {
        public const int MaxFactories = 3;
        /// <summary>공장을 옮기면 작업자가 나타나는 입구 칸(스펙 19.3: 입고구 옆 빈 칸).</summary>
        public const int EntranceX = 1, EntranceY = 1;

        public readonly Economy Economy;
        /// <summary>근무 시계(스펙 11.1). 모든 공장이 같이 쓴다.</summary>
        public readonly Clock Clock = new Clock();
        /// <summary>EXP와 작업자 강화(스펙 11.5).</summary>
        public readonly Progression Progression = new Progression();
        /// <summary>작업자 1명. 지금 공장의 격자·라인·설치에 묶여 있다.</summary>
        public readonly WorkerService Worker;

        private readonly List<FactorySite> factories = new List<FactorySite>();
        public IReadOnlyList<FactorySite> Factories => factories;
        public int CurrentIndex { get; private set; }
        public FactorySite Current => factories[CurrentIndex];

        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        /// <summary>그날 시작 시점의 잔액과 누적 획득 EXP. 하루 요약은 이 값 대비 증가분이다.</summary>
        public int DayStartMoney { get; set; }
        public int DayStartExp { get; set; }

        /// <summary>18:00이 되는 틱에 한 번 발생한다.</summary>
        public event Action<DaySummary> DayEnded;
        /// <summary>공장이 새로 생겼다(구매·복원).</summary>
        public event Action<FactorySite> FactoryAdded;
        /// <summary>지금 공장이 바뀌었다(새 번호).</summary>
        public event Action<int> FactorySwitched;

        /// <summary>공장마다 팩토리 난수의 씨앗을 뽑는 회사 하나의 난수원. 공장은 new Random(seeds.Next())로 만든다.</summary>
        private readonly Random seeds;

        /// <summary>factory는 1공장이 쓸 팩토리(테스트용, 주입한 난수는 건드리지 않는다). seedSource를 주면 공장 팩토리의 씨앗이 결정적이다.</summary>
        public Company(Economy economy, PackageFactory factory = null, Random seedSource = null)
        {
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            seeds = seedSource ?? new Random();
            var hq = AddSite(FactoryKind.Headquarters, factory, null);
            Worker = new WorkerService(hq.Grid, hq.Sim, hq.Build, Progression);
            Progression.Upgraded += OnUpgraded;
            CaptureDayStart();
        }

        /// <summary>종류의 공장을 찾는다. 없으면 null.</summary>
        public FactorySite Find(FactoryKind kind)
        {
            foreach (var f in factories) if (f.Kind == kind) return f;
            return null;
        }

        /// <summary>결제 없이 공장을 더한다(저장 복원·테스트용). 종류 순서대로만(본사 → 공항 → 정밀) 더할 수 있다. rng는 그 공장 팩토리의 난수(테스트용).</summary>
        public FactorySite AddFactory(FactoryKind kind, Random rng = null) => AddSite(kind, null, rng);

        /// <summary>다음에 살 수 있는 공장 종류(번호 순서). 셋 다 있으면 null.</summary>
        public FactoryKind? NextFactoryKind => factories.Count < MaxFactories ? (FactoryKind)factories.Count : (FactoryKind?)null;

        /// <summary>직전 공장이 부지 확장 2와 자동 검사기를 가졌는가(스펙 19.2). 직전 공장이 없으면 false.</summary>
        public bool MeetsRequirement(FactoryKind kind)
        {
            if (kind == FactoryKind.Headquarters) return true;
            var prev = Find(FactoryProfile.Previous(kind));
            return prev != null && prev.Unlocks.IsPurchased(UnlockId.Plot2) && prev.Unlocks.IsPurchased(UnlockId.Scanner);
        }

        /// <summary>TryBuyFactory와 같은 검사를 하되 아무것도 바꾸지 않는다(상점 표시용). 순서: 보유 → 조건 → 잔액.</summary>
        public FactoryPurchaseResult CanBuyFactory(FactoryKind kind)
        {
            if (Find(kind) != null) return FactoryPurchaseResult.AlreadyOwned;
            if (!MeetsRequirement(kind)) return FactoryPurchaseResult.RequirementMissing;
            if (!Economy.CanAfford(FactoryProfile.Of(kind).Price)) return FactoryPurchaseResult.NotEnoughMoney;
            return FactoryPurchaseResult.Ok;
        }

        /// <summary>회사 돈으로 새 공장을 산다(스펙 19.2). 작업자는 지금 공장에 그대로 있다.</summary>
        public FactoryPurchaseResult TryBuyFactory(FactoryKind kind)
        {
            var r = CanBuyFactory(kind);
            if (r != FactoryPurchaseResult.Ok) return r;
            Economy.TrySpend(FactoryProfile.Of(kind).Price);
            AddSite(kind, null, null);
            return FactoryPurchaseResult.Ok;
        }

        private FactorySite AddSite(FactoryKind kind, PackageFactory factory, Random rng)
        {
            if (factories.Count >= MaxFactories) throw new InvalidOperationException("a company has at most 3 factories");
            if ((int)kind != factories.Count) throw new InvalidOperationException($"factory {kind} out of order (have {factories.Count})");
            if (factory == null && rng == null) rng = new Random(seeds.Next());
            var site = new FactorySite(kind, Economy, Clock, factory, rng);
            site.Sim.PackageDelivered += OnPackageDelivered;
            site.Sim.Inspected += OnInspected;
            factories.Add(site);
            FactoryAdded?.Invoke(site);
            return site;
        }

        /// <summary>EXP 적립(스펙 11.5): 정상 출고 +10(취급주의 정상 출고도 같다), 불량 반품 +8. 실패 판정은 0. 어느 공장의 판정이든 회사 EXP다.</summary>
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

        /// <summary>일시정지이거나 퇴근(Closed)이면 아무것도 하지 않는다. 시간은 단계 경계(12:00·13:00·18:00)에서 나눠 모든 공장에 같은 구간을 흘린다:
        /// 입고와 트럭 대기 시간(스펙 18)은 Working 구간에서만, 시뮬레이션·해체는 18:00까지 항상, 작업자 회복은 구간의 단계로.
        /// 18:00에 닿은 틱에 모든 공장의 적재 중인 트럭이 출발하고 DayEnded가 한 번 발생한다.</summary>
        public void Tick(float dt)
        {
            if (Paused || !(dt > 0f) || !Clock.IsOpen) return;
            float remaining = dt * Speed;
            float workSeconds = 0f, lunchSeconds = 0f;
            while (remaining > 0f && Clock.IsOpen)
            {
                bool working = Clock.Phase == DayPhase.Working;
                float seg = Math.Min(remaining, Clock.SecondsToNextBoundary);
                foreach (var f in factories) f.TickSegment(seg, working);
                if (working) workSeconds += seg; else lunchSeconds += seg;
                Clock.Advance(seg);
                remaining -= seg;
            }
            bool moved = Worker.ConsumeMoved(); // 한 프레임의 이동 여부를 두 구간에 같이 쓴다(스펙 11.11)
            if (workSeconds > 0f) Worker.Rest(workSeconds, DayPhase.Working, moved);
            if (lunchSeconds > 0f) Worker.Rest(lunchSeconds, DayPhase.Lunch, moved);
            if (!Clock.IsOpen)
            {
                int departed = 0;
                foreach (var f in factories) departed += f.Trucks.DepartLoaded(); // 퇴근: 모든 공장의 적재 중인 트럭은 모두 출발(스펙 18)
                var summary = BuildSummary();
                summary.ClosingDepartures = departed;
                DayEnded?.Invoke(summary);
            }
        }

        /// <summary>현재 날짜의 요약(그날 시작 대비): 회사 합계 + 공장별 한 줄(스펙 19.1).</summary>
        public DaySummary BuildSummary()
        {
            var d = new DaySummary
            {
                Day = Clock.Day,
                StartMoney = DayStartMoney,
                EndMoney = Economy.Money,
                Exp = Progression.TotalEarned - DayStartExp,
            };
            foreach (var f in factories)
            {
                var line = f.BuildSummary();
                d.Factories.Add(line);
                d.Delivered += line.Delivered;
                d.Lost += line.Lost;
                d.Inspected += line.Inspected;
                d.TrucksDeparted += line.TrucksDeparted;
            }
            return d;
        }

        /// <summary>퇴근 상태에서만 동작: 다음 날 09:00, 스태미나 가득, 그날 시작 값 기록, 모든 공장의 트럭을 빈 트럭으로(스펙 18),
        /// 취급주의 확률 갱신(스펙 17.1). 레일·팔레트 위 택배는 그대로.</summary>
        public void NextDay()
        {
            if (Clock.IsOpen) return;
            Clock.NextDay();
            Worker.Refill();
            foreach (var f in factories)
            {
                f.Trucks.StartDay();
                f.UpdateChances();
            }
            CaptureDayStart();
        }

        private void CaptureDayStart()
        {
            DayStartMoney = Economy.Money;
            DayStartExp = Progression.TotalEarned;
            foreach (var f in factories) f.CaptureDayStart();
        }

        /// <summary>
        /// 지금 공장을 바꾼다(스펙 19.3). 손에 택배가 있으면 Carrying. 바뀌면 작업자를 그 공장에 묶고 입구 (1,1)(서 있을 수 없으면 가장 가까운 설 수 있는 칸)에
        /// 동쪽을 보고 세우며, 대기 중인 검사·운송장을 푼다.
        /// </summary>
        public FactorySwitchResult TrySwitch(int index)
        {
            if (index < 0 || index >= factories.Count) return FactorySwitchResult.NoSuchFactory;
            if (index == CurrentIndex) return FactorySwitchResult.AlreadyHere;
            if (Worker.Worker.Carrying != null) return FactorySwitchResult.Carrying;
            SetCurrent(index);
            var (x, y) = EntranceOf(Current);
            Worker.Worker.X = x;
            Worker.Worker.Y = y;
            Worker.Worker.Facing = Direction.East;
            FactorySwitched?.Invoke(index);
            return FactorySwitchResult.Ok;
        }

        /// <summary>저장 복원용: 작업자 위치는 바꾸지 않고 공장만 묶는다(이벤트 없음).</summary>
        public void RestoreCurrent(int index)
        {
            if (index < 0 || index >= factories.Count) throw new ArgumentOutOfRangeException(nameof(index), index, "no such factory");
            SetCurrent(index);
        }

        private void SetCurrent(int index)
        {
            CurrentIndex = index;
            var f = Current;
            Worker.Rebind(f.Grid, f.Sim, f.Build);
        }

        /// <summary>입구 칸(스펙 19.3): 서쪽 입고구 (0,2)에서 설 수 있는 칸만 밟아(4방향) 닿는 칸 중 (1,1)에 가장 가까운 칸
        /// (유클리드, 같으면 y·x 작은 쪽). (1,1)이 닿으면 그 칸이다. 닿는 칸이 없으면 (1,1).</summary>
        public (float x, float y) EntranceOf(FactorySite site)
        {
            var g = site.Grid;
            var seen = new bool[FactoryGrid.Width, FactoryGrid.Height];
            var queue = new Queue<(int x, int y)>();
            if (WorkerService.CanStandOn(g, FactoryGrid.InletX, FactoryGrid.InletY))
            {
                seen[FactoryGrid.InletX, FactoryGrid.InletY] = true;
                queue.Enqueue((FactoryGrid.InletX, FactoryGrid.InletY));
            }
            int bx = EntranceX, by = EntranceY, best = int.MaxValue;
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                int d = (x - EntranceX) * (x - EntranceX) + (y - EntranceY) * (y - EntranceY);
                if (d < best || (d == best && (y < by || (y == by && x < bx)))) { best = d; bx = x; by = y; }
                for (int k = 0; k < 4; k++)
                {
                    var dir = (Direction)k;
                    int nx = x + dir.Dx(), ny = y + dir.Dy();
                    if (!g.InBounds(nx, ny) || seen[nx, ny] || !WorkerService.CanStandOn(g, nx, ny)) continue;
                    seen[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
            return (bx, by);
        }
    }
}
