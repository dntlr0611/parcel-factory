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

        /// <summary>공장 팩토리 난수의 씨앗(회사 씨앗 난수의 1~3번째 값, 뽑는 순서대로). 공장은 new Random(seeds.Dequeue())로 만든다.
        /// 공장은 최대 3곳이라 미리 뽑아 두어도 예전(그때그때 Next) 순서와 같다.</summary>
        private readonly Queue<int> seeds = new Queue<int>();
        /// <summary>계약 후보 전용 난수(스펙 21.3). 회사 씨앗 난수의 4번째 값으로 만든다(공장 씨앗 순서를 바꾸지 않는다).</summary>
        private readonly Random contractRng;

        /// <summary>factory는 1공장이 쓸 팩토리(테스트용, 주입한 난수는 건드리지 않는다). seedSource를 주면 공장 팩토리의 씨앗과 계약 후보가 결정적이다.</summary>
        public Company(Economy economy, PackageFactory factory = null, Random seedSource = null)
        {
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            var source = seedSource ?? new Random();
            for (int i = 0; i < MaxFactories; i++) seeds.Enqueue(source.Next());
            contractRng = new Random(source.Next());
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
            if (factory == null && rng == null) rng = new Random(seeds.Dequeue());
            var site = new FactorySite(kind, Economy, Clock, factory, rng);
            site.Sim.PackageDelivered += OnPackageDelivered;
            site.Sim.PackageLost += OnPackageLost;
            site.Sim.Inspected += OnInspected;
            factories.Add(site);
            FactoryAdded?.Invoke(site);
            return site;
        }

        /// <summary>EXP 적립(스펙 11.5): 정상 출고 +10(취급주의 정상 출고도 같다), 불량 반품 +8. 실패 판정은 0. 어느 공장의 판정이든 회사 EXP다.</summary>
        private void OnPackageDelivered(Package p, DeliveryOutcome outcome, int delta)
        {
            NoteContract(p, outcome);
            if (!DeliveryScoring.IsSuccess(outcome)) return;
            Progression.AddExp(outcome == DeliveryOutcome.ReturnedBad ? Progression.ReturnedBadExp : Progression.DeliveredExp);
        }

        /// <summary>검사 판정(검사대·자동 검사기) +2. 샐러드를 먹었으면 2배(스펙 21.2).</summary>
        private void OnInspected() => Progression.AddExp(Progression.InspectExp * (Menu == LunchMenu.Salad ? LunchMenus.SaladInspectMultiplier : 1));

        private void OnPackageLost(Package p)
        {
            var c = Contract;
            if (c != null && !c.Achieved && Clock.IsOpen && c.Kind == ContractKind.NoLoss) c.Progress++;
        }

        // ── 점심 카드(스펙 21) ──

        /// <summary>오늘의 메뉴(확정 순간부터 18:00까지). 없으면 None.</summary>
        public LunchMenu Menu { get; private set; }
        /// <summary>오늘 점심 카드를 확정했는가(메뉴만·계약만이어도).</summary>
        public bool LunchConfirmed { get; private set; }
        /// <summary>오늘의 계약(확정한 후보의 복사본). 없으면 null. 18:00 뒤에도 다음 날 아침까지 남는다(하루 요약용).</summary>
        public Contract Contract { get; private set; }
        private readonly List<Contract> candidates = new List<Contract>();
        /// <summary>오늘 계약 후보 세 개(12:00에 뽑는다). 점심 전이면 비어 있다.</summary>
        public IReadOnlyList<Contract> ContractCandidates => candidates;
        /// <summary>점심시간이고 아직 확정하지 않았다: 점심 칩이 보이고 카드를 열 수 있다. 13:00이 지나면 false(그날은 메뉴도 계약도 없다).</summary>
        public bool LunchCardAvailable => Clock.Phase == DayPhase.Lunch && !LunchConfirmed;

        /// <summary>시계가 12:00을 넘는 틱에 한 번(후보를 뽑은 뒤). 시계를 점심 안으로 옮긴 경우(저장 불러오기·테스트)에는 발생하지 않는다.</summary>
        public event Action LunchStarted;
        /// <summary>점심 카드를 확정했다.</summary>
        public event Action LunchSettled;
        /// <summary>계약을 달성해 보상을 지급한 순간(개수 계약은 즉시, "0개" 류는 18:00).</summary>
        public event Action<Contract> ContractAchieved;

        /// <summary>회사 전체 입고구 수(계약 목표 N의 k, 스펙 21.3).</summary>
        public int InletCount
        {
            get
            {
                int n = 0;
                foreach (var f in factories)
                    foreach (var (x, y) in FactoryGrid.InletTiles)
                        if (f.Grid.Get(x, y).Type == CellType.Inlet) n++;
                return n;
            }
        }

        /// <summary>점심시간이고 확정 전인데 후보가 없으면 뽑는다(12:00 틱, 점심 중에 불러온 v10 이하 저장, 일시정지 중 카드 열기).</summary>
        public void EnsureLunchCandidates()
        {
            if (LunchCardAvailable && candidates.Count == 0) DrawCandidates();
        }

        /// <summary>그날 회사 상황에 맞는 종류만(항공 = 항공 도크가 있는 공장, 취급주의 = 취급주의가 나오는 공장, 운송장 = 부착대가 하나라도) 중에서
        /// 같은 종류 없이 셋을 뽑는다(계약 전용 난수, 부분 섞기). 카드에는 종류 순서로 보인다.</summary>
        private void DrawCandidates()
        {
            bool air = false, fragile = false, labeler = false;
            foreach (var f in factories)
            {
                air |= f.HasAirDocks;
                fragile |= f.Sim.Factory.FragileChance > 0.0;
                for (int x = 0; x < FactoryGrid.Width && !labeler; x++)
                    for (int y = 0; y < FactoryGrid.Height && !labeler; y++)
                        labeler = f.Grid.Get(x, y).Type == CellType.Labeler;
            }
            var pool = new List<ContractKind> { ContractKind.Deliver, ContractKind.NoLoss, ContractKind.Return };
            if (air) pool.Add(ContractKind.Air);
            if (fragile) pool.Add(ContractKind.Fragile);
            if (labeler) pool.Add(ContractKind.NoMissingWaybill);
            const int count = 3;
            for (int i = 0; i < count; i++)
            {
                int j = i + contractRng.Next(pool.Count - i);
                var t = pool[i]; pool[i] = pool[j]; pool[j] = t;
            }
            var chosen = pool.GetRange(0, count);
            chosen.Sort();
            int inlets = InletCount;
            candidates.Clear();
            foreach (var k in chosen) candidates.Add(new Contract { Kind = k, Target = ContractCatalog.Target(k, inlets) });
        }

        /// <summary>
        /// 점심 카드 확정(스펙 21.1): 점심시간(12:00~12:59)에 한 번. 메뉴(None이면 안 고름)와 계약 후보 번호(null이면 안 고름) 중 하나 이상.
        /// 메뉴 값을 회사 돈에서 내고(모자라면 NotEnoughMoney, 아무것도 바뀌지 않음) 효과를 바로 켠다. 계약은 후보의 복사본으로 진행 0부터.
        /// </summary>
        public LunchConfirmResult ConfirmLunch(LunchMenu? menu, int? contractIndex)
        {
            if (Clock.Phase != DayPhase.Lunch) return LunchConfirmResult.NotLunch;
            if (LunchConfirmed) return LunchConfirmResult.AlreadyConfirmed;
            var m = menu ?? LunchMenu.None;
            if (m == LunchMenu.None && !contractIndex.HasValue) return LunchConfirmResult.NothingChosen;
            EnsureLunchCandidates();
            if (contractIndex.HasValue && (contractIndex.Value < 0 || contractIndex.Value >= candidates.Count)) return LunchConfirmResult.InvalidContract;
            int price = LunchMenus.Price(m);
            if (price > 0 && !Economy.CanAfford(price)) return LunchConfirmResult.NotEnoughMoney; // 무료 선택은 잔액이 음수여도 된다(스펙 21.5)
            if (price > 0) Economy.TrySpend(price);
            LunchConfirmed = true;
            SetMenu(m);
            if (contractIndex.HasValue)
            {
                var c = candidates[contractIndex.Value].Clone();
                c.Progress = 0;
                c.Achieved = false;
                Contract = c;
            }
            LunchSettled?.Invoke();
            return LunchConfirmResult.Ok;
        }

        /// <summary>메뉴와 작업자 효과를 함께 바꾼다(국밥 달리기 소모 ×0.7, 제육볶음 속도 ×1.15. 샐러드는 OnInspected가 읽는다).</summary>
        private void SetMenu(LunchMenu m)
        {
            Menu = m;
            Worker.RunDrainMultiplier = m == LunchMenu.Gukbap ? LunchMenus.GukbapRunDrain : 1f;
            Worker.MenuSpeedMultiplier = m == LunchMenu.Jeyuk ? LunchMenus.JeyukSpeed : 1f;
        }

        /// <summary>배송 판정마다(모든 공장, 확정 뒤 18:00까지) 계약 진행을 센다. 개수 계약이 목표에 닿으면 즉시 지급.</summary>
        private void NoteContract(Package p, DeliveryOutcome outcome)
        {
            var c = Contract;
            if (c == null || c.Achieved || !Clock.IsOpen) return;
            bool counts;
            switch (c.Kind)
            {
                case ContractKind.Deliver: counts = outcome == DeliveryOutcome.Delivered || outcome == DeliveryOutcome.DeliveredFragile; break;
                case ContractKind.Return: counts = outcome == DeliveryOutcome.ReturnedBad; break;
                case ContractKind.Air: counts = p.Air && (outcome == DeliveryOutcome.Delivered || outcome == DeliveryOutcome.DeliveredFragile); break;
                case ContractKind.Fragile: counts = outcome == DeliveryOutcome.DeliveredFragile; break;
                case ContractKind.NoMissingWaybill: counts = !p.Waybill; break;
                default: counts = false; break;
            }
            if (!counts) return;
            c.Progress++;
            if (!c.IsZeroKind && c.Progress >= c.Target) Achieve(c);
        }

        /// <summary>보상 지급: 돈 + EXP 30. 실패는 감점이 없다.</summary>
        private void Achieve(Contract c)
        {
            c.Achieved = true;
            Economy.Earn(c.Reward);
            Progression.AddExp(Contract.RewardExp);
            ContractAchieved?.Invoke(c);
        }

        /// <summary>18:00: "0개" 류를 판정하고(위반 0이면 지급) 메뉴 효과를 끈다.</summary>
        private void CloseLunchDay()
        {
            var c = Contract;
            if (c != null && !c.Achieved && c.IsZeroKind && c.Progress == 0) Achieve(c);
            SetMenu(LunchMenu.None);
        }

        /// <summary>저장 복원용(검증은 SaveSerializer). 메뉴 효과도 다시 켠다. 이벤트는 발생하지 않는다.</summary>
        public void RestoreLunch(LunchMenu menu, bool confirmed, IEnumerable<Contract> restoredCandidates, Contract contract)
        {
            LunchConfirmed = confirmed;
            SetMenu(menu);
            candidates.Clear();
            if (restoredCandidates != null) foreach (var k in restoredCandidates) candidates.Add(k.Clone());
            Contract = contract?.Clone();
        }

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
            bool lunchStarted = false;
            while (remaining > 0f && Clock.IsOpen)
            {
                bool working = Clock.Phase == DayPhase.Working;
                bool beforeLunch = Clock.MinuteOfDay < Clock.LunchStart;
                float seg = Math.Min(remaining, Clock.SecondsToNextBoundary);
                foreach (var f in factories) f.TickSegment(seg, working);
                if (working) workSeconds += seg; else lunchSeconds += seg;
                Clock.Advance(seg);
                remaining -= seg;
                if (beforeLunch && Clock.MinuteOfDay >= Clock.LunchStart && !LunchConfirmed && candidates.Count == 0)
                {
                    DrawCandidates(); // 12:00 그 순간의 회사 상황으로(스펙 21.3)
                    lunchStarted = true;
                }
            }
            EnsureLunchCandidates();
            if (lunchStarted) LunchStarted?.Invoke();
            bool moved = Worker.ConsumeMoved(); // 한 프레임의 이동 여부를 두 구간에 같이 쓴다(스펙 11.11)
            if (workSeconds > 0f) Worker.Rest(workSeconds, DayPhase.Working, moved);
            if (lunchSeconds > 0f) Worker.Rest(lunchSeconds, DayPhase.Lunch, moved);
            if (!Clock.IsOpen)
            {
                int departed = 0;
                foreach (var f in factories) departed += f.Trucks.DepartLoaded(); // 퇴근: 모든 공장의 적재 중인 트럭은 모두 출발(스펙 18)
                CloseLunchDay(); // "0개" 류 판정과 메뉴 해제(스펙 21). 요약의 잔액에 보상이 들어간다
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
                Contract = Contract?.Clone(),
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
            RestoreLunch(LunchMenu.None, false, null, null); // 점심 카드는 하루 한 번(스펙 21)
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
