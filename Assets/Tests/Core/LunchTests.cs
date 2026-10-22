using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 21(P1): 점심 카드 — 12:00 LunchStarted, 구내식당 메뉴(가격·효과·18:00 해제), 오후 계약(후보 조건·N 비례·결정적 난수·
    /// 회사 합계·즉시 지급·18:00 판정·실패 무감점), 13:00 마감, 저장 v11.</summary>
    public class LunchTests
    {
        private static Package Good(int id, bool air = false)
        {
            var p = new PackageFactory(new Random(id), 0.0).Create(id, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            return p;
        }

        private static Package Bad(int id)
        {
            var p = Good(id);
            p.Bad = true;
            p.Inspect(true);
            return p;
        }

        private static Package FragileGood(int id)
        {
            var p = Good(id);
            p.Fragile = true;
            p.FragileSticker = true;
            return p;
        }

        /// <summary>11:59:30에서 1초 흘려 12:00을 넘긴다(점심 시작).</summary>
        private static void ToLunch(GameSession s)
        {
            s.Clock.Set(s.Clock.Day, Clock.LunchStart - 0.5f);
            s.Tick(1f);
            Assert.AreEqual(DayPhase.Lunch, s.Clock.Phase);
        }

        private static void ToClose(GameSession s)
        {
            s.Clock.Set(s.Clock.Day, Clock.CloseMinute - 0.5f);
            s.Tick(1f);
            Assert.IsFalse(s.Clock.IsOpen);
        }

        /// <summary>후보 중 그 종류의 번호. 없으면 실패.</summary>
        private static int IndexOf(Company c, ContractKind kind)
        {
            for (int i = 0; i < c.ContractCandidates.Count; i++) if (c.ContractCandidates[i].Kind == kind) return i;
            Assert.Fail(kind + " not among candidates: " + string.Join(",", c.ContractCandidates.Select(x => x.Kind)));
            return -1;
        }

        /// <summary>후보에 그 종류가 나올 때까지 씨앗을 바꿔 가며 회사를 만든다(결정적).</summary>
        private static GameSession WithCandidate(ContractKind kind, Action<GameSession> setup, int money = 1000)
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var s = new GameSession(new Economy(money), null, new Random(seed));
                setup?.Invoke(s);
                ToLunch(s);
                if (s.Company.ContractCandidates.Any(x => x.Kind == kind)) return s;
            }
            Assert.Fail("no seed produced " + kind);
            return null;
        }

        // ── 12:00 시작 ──

        [Test]
        public void LunchStarted_FiresOnceWhenTheClockCrossesNoon_WithThreeDistinctCandidates()
        {
            var s = new GameSession(new Economy(), null, new Random(1));
            int started = 0;
            s.LunchStarted += () => started++;
            s.Clock.Set(1, Clock.LunchStart - 1f);
            s.Tick(0.5f);
            Assert.AreEqual(0, started);
            Assert.AreEqual(0, s.Company.ContractCandidates.Count, "no candidates before noon");
            Assert.IsFalse(s.Company.LunchCardAvailable);
            s.Tick(1f);
            Assert.AreEqual(1, started);
            s.Tick(1f);
            Assert.AreEqual(1, started, "only the crossing tick");
            var kinds = s.Company.ContractCandidates.Select(x => x.Kind).ToList();
            Assert.AreEqual(3, kinds.Count);
            Assert.AreEqual(3, kinds.Distinct().Count(), "no duplicate kinds");
            Assert.IsTrue(s.Company.LunchCardAvailable);
            Assert.AreEqual(LunchMenu.None, s.Company.Menu);
            Assert.IsNull(s.Company.Contract);
        }

        [Test]
        public void SettingTheClockIntoLunch_GeneratesCandidatesOnTheNextTick_WithoutTheEvent()
        {
            var s = new GameSession(new Economy(), null, new Random(1));
            int started = 0;
            s.LunchStarted += () => started++;
            s.Clock.Set(1, Clock.LunchStart + 20f);
            Assert.IsTrue(s.Company.LunchCardAvailable, "the card is available during lunch");
            s.Tick(0.05f);
            Assert.AreEqual(0, started, "no auto-open when the clock jumped into lunch");
            Assert.AreEqual(3, s.Company.ContractCandidates.Count);
            var paused = new GameSession(new Economy(), null, new Random(1)) { Paused = true };
            paused.Clock.Set(1, Clock.LunchStart + 20f);
            paused.Company.EnsureLunchCandidates();
            Assert.AreEqual(3, paused.Company.ContractCandidates.Count, "the card can ask for candidates while paused");
        }

        // ── 후보 ──

        [Test]
        public void Candidates_AreDeterministicForASeed_AndUseADedicatedRandom()
        {
            string Run(int seed)
            {
                var s = new GameSession(new Economy(5000), null, new Random(seed));
                s.Unlocks.Grant(UnlockId.Plot2);
                s.Grid.Clear(5, 2);
                s.Toolbar.Add(CellType.Labeler, Direction.East);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(3, 3, CellType.Labeler, Direction.East));
                s.FragileFromDay = 1;
                var parts = new List<string>();
                for (int day = 0; day < 4; day++)
                {
                    ToLunch(s);
                    parts.Add(string.Join(",", s.Company.ContractCandidates.Select(x => x.Kind + ":" + x.Target)));
                    ToClose(s);
                    s.NextDay();
                }
                return string.Join("|", parts);
            }
            Assert.AreEqual(Run(7), Run(7));
            var all = Enumerable.Range(0, 20).Select(Run).Distinct().Count();
            Assert.Greater(all, 1, "different seeds give different candidates");
        }

        [Test]
        public void Candidates_DoNotShiftTheFactorySeeds()
        {
            // 공장 팩토리 씨앗은 회사 씨앗 난수의 1·2·3번째 값 그대로다(계약 난수는 그 뒤에서 따로 뽑는다).
            var seeds = new Random(99);
            int hq = seeds.Next(), airport = seeds.Next();
            var c = new Company(new Economy(), null, new Random(99));
            var a = c.AddFactory(FactoryKind.Airport);
            var expectHq = new PackageFactory(new Random(hq)).Create(1, 0, 0, Direction.East);
            var expectAir = new PackageFactory(new Random(airport)).Create(1, 0, 0, Direction.East);
            Assert.AreEqual(expectHq.Label.Weight, c.Factories[0].Sim.Factory.Create(1, 0, 0, Direction.East).Label.Weight);
            Assert.AreEqual(expectAir.Label.Weight, a.Sim.Factory.Create(1, 0, 0, Direction.East).Label.Weight);
        }

        [Test]
        public void Candidates_OnlyIncludeKindsTheCompanyCanDo()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var s = new GameSession(new Economy(), null, new Random(seed));
                ToLunch(s);
                foreach (var k in s.Company.ContractCandidates)
                    CollectionAssert.Contains(new[] { ContractKind.Deliver, ContractKind.NoLoss, ContractKind.Return }, k.Kind, "a new company has no air docks, fragile parcels or labeler");
            }
        }

        [Test]
        public void Candidates_AirNeedsAirDocks_FragileNeedsFragileParcels_WaybillNeedsALabeler()
        {
            var air = WithCandidate(ContractKind.Air, s => s.Unlocks.Grant(UnlockId.Plot2));
            Assert.IsTrue(air.Company.Factories.Any(f => f.HasAirDocks));
            var airport = WithCandidate(ContractKind.Air, s => s.Company.AddFactory(FactoryKind.Airport));
            Assert.IsFalse(airport.Factories0HasAirDocks(), "air docks of another factory count");

            var fragile = WithCandidate(ContractKind.Fragile, s => { s.Company.AddFactory(FactoryKind.Airport); s.Company.AddFactory(FactoryKind.Precision); });
            Assert.Greater(fragile.Company.Factories[2].Sim.Factory.FragileChance, 0.0);

            var waybill = WithCandidate(ContractKind.NoMissingWaybill, s => Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(3, 3, CellType.Labeler, Direction.East)));
            Assert.AreEqual(CellType.Labeler, waybill.Grid.Get(3, 3).Type);

            // 부지 확장 2 다음 날 전에는 취급주의가 없다.
            for (int seed = 0; seed < 40; seed++)
            {
                var s = new GameSession(new Economy(), null, new Random(seed));
                s.Unlocks.Grant(UnlockId.Plot2); // FragileFromDay = 2
                ToLunch(s);
                Assert.IsFalse(s.Company.ContractCandidates.Any(x => x.Kind == ContractKind.Fragile), "fragile parcels start the next day");
            }
        }

        [Test]
        public void Targets_ScaleWithTheCompanyInletCount()
        {
            Assert.AreEqual(12, ContractCatalog.Target(ContractKind.Deliver, 1));
            Assert.AreEqual(36, ContractCatalog.Target(ContractKind.Deliver, 3));
            Assert.AreEqual(9, ContractCatalog.Target(ContractKind.Return, 3));
            Assert.AreEqual(15, ContractCatalog.Target(ContractKind.Air, 3));
            Assert.AreEqual(9, ContractCatalog.Target(ContractKind.Fragile, 3));
            Assert.AreEqual(1, ContractCatalog.Target(ContractKind.Fragile, 0), "at least 1");
            Assert.AreEqual(0, ContractCatalog.Target(ContractKind.NoLoss, 3));
            Assert.AreEqual(0, ContractCatalog.Target(ContractKind.NoMissingWaybill, 3));

            var s = new GameSession(new Economy(), null, new Random(3));
            Assert.AreEqual(1, s.Company.InletCount);
            s.Unlocks.Grant(UnlockId.Plot1);
            s.Unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(3, s.Company.InletCount);
            s.Company.AddFactory(FactoryKind.Airport);
            Assert.AreEqual(4, s.Company.InletCount, "all factories");
            ToLunch(s);
            var deliver = s.Company.ContractCandidates.FirstOrDefault(x => x.Kind == ContractKind.Deliver);
            if (deliver != null) Assert.AreEqual(48, deliver.Target);
            foreach (var k in s.Company.ContractCandidates)
                Assert.AreEqual(ContractCatalog.Target(k.Kind, 4), k.Target);
        }

        [Test]
        public void Rewards_AndTitles()
        {
            Assert.AreEqual(200, ContractCatalog.Reward(ContractKind.Deliver));
            Assert.AreEqual(150, ContractCatalog.Reward(ContractKind.NoLoss));
            Assert.AreEqual(150, ContractCatalog.Reward(ContractKind.Return));
            Assert.AreEqual(250, ContractCatalog.Reward(ContractKind.Air));
            Assert.AreEqual(300, ContractCatalog.Reward(ContractKind.Fragile));
            Assert.AreEqual(150, ContractCatalog.Reward(ContractKind.NoMissingWaybill));
            Assert.AreEqual(30, Contract.RewardExp);
            Assert.AreEqual("정상 출고 12개", ContractCatalog.Title(ContractKind.Deliver, 12));
            Assert.AreEqual("소실 0개로 퇴근", ContractCatalog.Title(ContractKind.NoLoss, 0));
            Assert.AreEqual("불량 반품 3개", ContractCatalog.Title(ContractKind.Return, 3));
            StringAssert.Contains("정상품 반품은 세지 않음", ContractCatalog.Condition(ContractKind.Return));
            StringAssert.Contains("하나라도 있으면 실패", ContractCatalog.Condition(ContractKind.NoMissingWaybill));
            Assert.AreEqual("계약 · 불량 반품 2/3", new Contract { Kind = ContractKind.Return, Target = 3, Progress = 2 }.ChipText);
            Assert.AreEqual("항공 출고 10개", ContractCatalog.Title(ContractKind.Air, 10));
            Assert.AreEqual("취급주의 파손 없이 3개 출고", ContractCatalog.Title(ContractKind.Fragile, 3));
            Assert.AreEqual("운송장 누락 0개", ContractCatalog.Title(ContractKind.NoMissingWaybill, 0));
            var c = new Contract { Kind = ContractKind.Air, Target = 10, Progress = 7 };
            Assert.AreEqual("계약 · 항공 출고 7/10", c.ChipText);
            Assert.AreEqual("오후 계약: 항공 출고 10개 — 미달 7/10", c.SummaryText);
            c.Progress = 10; c.Achieved = true;
            Assert.AreEqual("오후 계약: 항공 출고 10개 — 달성 +₩250", c.SummaryText);
            Assert.AreEqual("계약 달성! +₩250 · EXP +30", c.AchievedToast);
        }

        // ── 메뉴 ──

        [Test]
        public void Menus_PricesAndNames()
        {
            CollectionAssert.AreEqual(new[] { LunchMenu.Gukbap, LunchMenu.Jeyuk, LunchMenu.Salad, LunchMenu.Rest }, LunchMenus.All);
            Assert.AreEqual(40, LunchMenus.Price(LunchMenu.Gukbap));
            Assert.AreEqual(60, LunchMenus.Price(LunchMenu.Jeyuk));
            Assert.AreEqual(50, LunchMenus.Price(LunchMenu.Salad));
            Assert.AreEqual(0, LunchMenus.Price(LunchMenu.Rest));
            Assert.AreEqual("국밥", LunchMenus.Name(LunchMenu.Gukbap));
            Assert.AreEqual("제육볶음", LunchMenus.Name(LunchMenu.Jeyuk));
            Assert.AreEqual("샐러드", LunchMenus.Name(LunchMenu.Salad));
            Assert.AreEqual("그냥 쉬기", LunchMenus.Name(LunchMenu.Rest));
        }

        [Test]
        public void Confirm_PaysTheMenuPrice_AndRefusesWhenShortOfMoney()
        {
            var s = new GameSession(new Economy(100), null, new Random(1));
            ToLunch(s);
            Assert.AreEqual(LunchConfirmResult.NothingChosen, s.ConfirmLunch(null, null));
            Assert.AreEqual(LunchConfirmResult.NothingChosen, s.ConfirmLunch(LunchMenu.None, null));
            Assert.AreEqual(LunchConfirmResult.InvalidContract, s.ConfirmLunch(null, 3));
            s.Economy.TrySpend(50);
            Assert.AreEqual(LunchConfirmResult.NotEnoughMoney, s.ConfirmLunch(LunchMenu.Jeyuk, 0), "₩60 > ₩50");
            Assert.AreEqual(50, s.Economy.Money);
            Assert.IsFalse(s.Company.LunchConfirmed);
            Assert.IsNull(s.Company.Contract, "a refused confirm takes nothing");
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Salad, null));
            Assert.AreEqual(0, s.Economy.Money);
            Assert.AreEqual(LunchMenu.Salad, s.Company.Menu);
            Assert.IsTrue(s.Company.LunchConfirmed);
            Assert.IsFalse(s.Company.LunchCardAvailable, "the card is done for today");
            Assert.AreEqual(LunchConfirmResult.AlreadyConfirmed, s.ConfirmLunch(LunchMenu.Rest, 0));
        }

        [Test]
        public void Confirm_RestIsFree_AndContractOnlyIsAllowed()
        {
            var s = new GameSession(new Economy(0), null, new Random(1));
            ToLunch(s);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Rest, null));
            Assert.AreEqual(0, s.Economy.Money);
            Assert.AreEqual(LunchMenu.Rest, s.Company.Menu);

            var t = new GameSession(new Economy(0), null, new Random(1));
            ToLunch(t);
            int confirmed = 0;
            t.Company.LunchSettled += () => confirmed++;
            Assert.AreEqual(LunchConfirmResult.Ok, t.ConfirmLunch(null, 1));
            Assert.AreEqual(1, confirmed);
            Assert.AreEqual(LunchMenu.None, t.Company.Menu);
            Assert.AreEqual(t.Company.ContractCandidates[1].Kind, t.Company.Contract.Kind);
            Assert.AreEqual(0, t.Company.Contract.Progress);
            Assert.AreNotSame(t.Company.ContractCandidates[1], t.Company.Contract, "the contract is a copy");
        }

        [Test]
        public void FreeChoices_WorkAtANegativeBalance_PaidMenusDoNot()
        {
            var rest = new GameSession(new Economy(0), null, new Random(1));
            rest.Economy.Adjust(-40);
            ToLunch(rest);
            Assert.AreEqual(LunchConfirmResult.NotEnoughMoney, rest.ConfirmLunch(LunchMenu.Gukbap, null));
            Assert.AreEqual(LunchConfirmResult.Ok, rest.ConfirmLunch(LunchMenu.Rest, null), "그냥 쉬기 alone at −₩40");
            Assert.AreEqual(-40, rest.Economy.Money);
            Assert.AreEqual(LunchMenu.Rest, rest.Company.Menu);

            var contractOnly = new GameSession(new Economy(0), null, new Random(1));
            contractOnly.Economy.Adjust(-40);
            ToLunch(contractOnly);
            Assert.AreEqual(LunchConfirmResult.Ok, contractOnly.ConfirmLunch(null, 0), "a contract alone at −₩40");
            Assert.AreEqual(-40, contractOnly.Economy.Money);
            Assert.IsNotNull(contractOnly.Company.Contract);

            var both = new GameSession(new Economy(0), null, new Random(1));
            both.Economy.Adjust(-40);
            ToLunch(both);
            Assert.AreEqual(LunchConfirmResult.Ok, both.ConfirmLunch(LunchMenu.Rest, 2));
        }

        [Test]
        public void Confirm_IsOnlyPossibleDuringLunch()
        {
            var s = new GameSession(new Economy(500), null, new Random(1));
            Assert.AreEqual(LunchConfirmResult.NotLunch, s.ConfirmLunch(LunchMenu.Gukbap, null), "before noon");
            ToLunch(s);
            s.Tick(60f); // 13:00
            Assert.AreEqual(DayPhase.Working, s.Clock.Phase);
            Assert.IsFalse(s.Company.LunchCardAvailable, "the chip goes away at 13:00");
            Assert.AreEqual(LunchConfirmResult.NotLunch, s.ConfirmLunch(LunchMenu.Gukbap, 0));
            Assert.AreEqual(500, s.Economy.Money);
            Assert.AreEqual(LunchMenu.None, s.Company.Menu);
            Assert.IsNull(s.Company.Contract);
        }

        [Test]
        public void Gukbap_CutsTheRunDrainBy30Percent_UntilSix()
        {
            var s = new GameSession(new Economy(), null, new Random(1));
            ToLunch(s);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Gukbap, null));
            Assert.AreEqual(0.7f, s.Worker.RunDrainMultiplier, 1e-6f);
            s.Worker.Worker.X = 2f; s.Worker.Worker.Y = 6f;
            s.Worker.Worker.Stamina = 50f;
            Assert.IsTrue(s.Worker.Move(0f, 1f, 0.5f, true));
            Assert.IsTrue(s.Worker.IsRunning);
            Assert.AreEqual(50f - WorkerService.RunDrain * 0.5f * 0.7f, s.Worker.Worker.Stamina, 1e-4f);
            s.Worker.Worker.Carrying = Good(9);
            s.Worker.Worker.Stamina = 50f;
            Assert.IsTrue(s.Worker.Move(0f, -1f, 0.5f, true));
            Assert.AreEqual(50f - WorkerService.RunCarryDrain * 0.5f * 0.7f, s.Worker.Worker.Stamina, 1e-4f);
            ToClose(s);
            Assert.AreEqual(1f, s.Worker.RunDrainMultiplier, "the effect ends at 18:00");
            Assert.AreEqual(LunchMenu.None, s.Company.Menu);
        }

        [Test]
        public void Jeyuk_MultipliesTheUpgradedSpeedBy115Percent_UntilSix()
        {
            var s = new GameSession(new Economy(), null, new Random(1));
            s.Progression.Restore(0, 0, 2, 0);
            float before = s.Worker.EffectiveSpeed;
            ToLunch(s);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Jeyuk, null));
            Assert.AreEqual(before * 1.15f, s.Worker.EffectiveSpeed, 1e-4f);
            Assert.AreEqual(WorkerService.Speed * 1.16f * 1.15f, s.Worker.EffectiveSpeed, 1e-4f, "multiplies the stat multiplier");
            s.Worker.Worker.X = 2f; s.Worker.Worker.Y = 6f;
            s.Worker.Move(0f, 1f, 0.5f);
            Assert.AreEqual(6f + before * 1.15f * 0.5f, s.Worker.Worker.Y, 1e-4f);
            ToClose(s);
            Assert.AreEqual(before, s.Worker.EffectiveSpeed, 1e-4f);
        }

        [Test]
        public void Salad_DoublesInspectionExp_AtTablesAndScanners_InEveryFactory()
        {
            var s = new GameSession(new Economy(5000), null, new Random(1));
            var second = s.Company.AddFactory(FactoryKind.Airport);
            ToLunch(s);
            s.Sim.NoteInspected();
            Assert.AreEqual(Progression.InspectExp, s.Progression.Exp, "no buff before confirming");
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Salad, null));
            int exp = s.Progression.Exp;

            // 검사대: 작업자가 (3,4)를 바라보고 검사
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(3, 4, CellType.Table, Direction.East));
            s.Worker.Worker.X = 2f; s.Worker.Worker.Y = 4f; s.Worker.Worker.Facing = Direction.East;
            var p = Good(s.Sim.NextId++); p.Inspected = false;
            s.Sim.PlaceCarried(p, 3, 4);
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Inspect(false));
            Assert.AreEqual(exp + 2 * Progression.InspectExp, s.Progression.Exp);

            // 다른 공장의 자동 검사기
            second.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, second.Build.TryPlace(2, 2, CellType.Scanner, Direction.East));
            var q = Good(second.Sim.NextId++); q.Inspected = false;
            second.Sim.PlaceCarried(q, 2, 2);
            Assert.AreEqual(exp + 4 * Progression.InspectExp, s.Progression.Exp);

            ToClose(s);
            int closed = s.Progression.Exp;
            s.NextDay();
            s.Sim.NoteInspected();
            Assert.AreEqual(closed + Progression.InspectExp, s.Progression.Exp, "back to +2 the next day");
        }

        // ── 계약 진행·지급 ──

        [Test]
        public void Deliver_CountsGoodOutletDeliveriesInAllFactories_AndPaysTheMomentItIsMet()
        {
            var s = WithCandidate(ContractKind.Deliver, x => x.Company.AddFactory(FactoryKind.Airport));
            var second = s.Company.Factories[1];
            // 확정 전 배송은 세지 않는다
            s.Sim.DeliverCarried(Good(1), CellType.Outlet);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(null, IndexOf(s.Company, ContractKind.Deliver)));
            var c = s.Company.Contract;
            Assert.AreEqual(ContractCatalog.Target(ContractKind.Deliver, 2), c.Target, "2 inlets (two factories) at noon");
            Contract achieved = null;
            s.Company.ContractAchieved += x => achieved = x;
            s.Sim.DeliverCarried(Bad(2), CellType.ReturnDock); // 반품은 정상 출고가 아니다
            s.Sim.DeliverCarried(Good(3), CellType.ReturnDock); // 실패 판정
            Assert.AreEqual(0, c.Progress);
            int money = s.Economy.Money, exp = s.Progression.Exp;
            for (int i = 0; i < c.Target - 1; i++)
                (i % 2 == 0 ? s.Sim : second.Sim).DeliverCarried(i % 3 == 0 ? FragileGood(100 + i) : Good(100 + i), CellType.Outlet);
            Assert.AreEqual(c.Target - 1, c.Progress);
            Assert.IsNull(achieved);
            int beforeLast = s.Economy.Money;
            second.Sim.DeliverCarried(Good(999, air: true), CellType.AirOutlet); // 항공 정상 출고도 정상 출고
            Assert.AreSame(c, achieved);
            Assert.IsTrue(c.Achieved);
            Assert.AreEqual(beforeLast + FactoryProfile.AirportAirDelivered + 200, s.Economy.Money, "delivery + ₩200 at once");
            Assert.GreaterOrEqual(s.Progression.Exp - exp, Contract.RewardExp);
            int after = s.Economy.Money;
            s.Sim.DeliverCarried(Good(1000), CellType.Outlet);
            Assert.AreEqual(after + 30, s.Economy.Money, "paid only once");
            Assert.AreEqual(c.Target, c.Progress, "progress stops at the target");
            Assert.Greater(after, money);
        }

        [Test]
        public void Return_CountsRejectedBadParcelsOnReturnTrucks()
        {
            var s = WithCandidate(ContractKind.Return, null);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(null, IndexOf(s.Company, ContractKind.Return)));
            var c = s.Company.Contract;
            Assert.AreEqual(3, c.Target);
            s.Sim.DeliverCarried(Good(1), CellType.ReturnDock); // 정상 택배를 반품(실패)
            s.Sim.DeliverCarried(Good(2), CellType.Outlet);
            Assert.AreEqual(0, c.Progress);
            int money = s.Economy.Money;
            for (int i = 0; i < 3; i++) s.Sim.DeliverCarried(Bad(10 + i), CellType.ReturnDock);
            Assert.IsTrue(c.Achieved);
            Assert.AreEqual(money + 3 * 10 + 150, s.Economy.Money);
        }

        [Test]
        public void Air_CountsAirParcelsDeliveredToTheAirOutlet()
        {
            var s = WithCandidate(ContractKind.Air, x => x.Unlocks.Grant(UnlockId.Plot2));
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(null, IndexOf(s.Company, ContractKind.Air)));
            var c = s.Company.Contract;
            Assert.AreEqual(15, c.Target, "3 inlets after Plot2");
            s.Sim.DeliverCarried(Good(1), CellType.Outlet);
            s.Sim.DeliverCarried(Good(2, air: true), CellType.Outlet); // 운송 수단 틀림
            Assert.AreEqual(0, c.Progress);
            s.Sim.DeliverCarried(Good(3, air: true), CellType.AirOutlet);
            Assert.AreEqual(1, c.Progress);
            Assert.AreEqual("계약 · 항공 출고 1/15", c.ChipText);
        }

        [Test]
        public void Fragile_CountsOnlyUnbrokenFragileGoodDeliveries()
        {
            var s = WithCandidate(ContractKind.Fragile, x => { x.Company.AddFactory(FactoryKind.Airport); x.Company.AddFactory(FactoryKind.Precision); });
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(null, IndexOf(s.Company, ContractKind.Fragile)));
            var c = s.Company.Contract;
            var precision = s.Company.Factories[2];
            precision.Sim.DeliverCarried(Good(1), CellType.Outlet);
            var broken = FragileGood(2);
            precision.Sim.Break(broken);
            broken.Inspect(false); // 파손품을 승인해 출고 → 불량 출고
            precision.Sim.DeliverCarried(broken, CellType.Outlet);
            Assert.AreEqual(0, c.Progress);
            precision.Sim.DeliverCarried(FragileGood(3), CellType.Outlet);
            Assert.AreEqual(1, c.Progress);
        }

        [Test]
        public void NoLoss_IsJudgedAtSix_AndFailsWithoutPenalty()
        {
            var ok = WithCandidate(ContractKind.NoLoss, null);
            ok.Sim.PackageLostForTest();
            Assert.AreEqual(LunchConfirmResult.Ok, ok.ConfirmLunch(null, IndexOf(ok.Company, ContractKind.NoLoss)));
            Assert.AreEqual("계약 · 소실 0개로 퇴근 · 지금 0", ok.Company.Contract.ChipText, "a loss before confirming does not count");
            int money = ok.Economy.Money, exp = ok.Progression.TotalEarned;
            Contract achieved = null;
            ok.Company.ContractAchieved += x => achieved = x;
            DaySummary d = null;
            ok.DayEnded += x => d = x;
            ToClose(ok);
            Assert.IsNotNull(achieved, "judged at 18:00");
            Assert.AreEqual(money + 150, ok.Economy.Money);
            Assert.AreEqual(exp + 30, ok.Progression.TotalEarned);
            Assert.AreEqual(money + 150, d.EndMoney, "the summary includes the reward");
            Assert.AreEqual("오후 계약: 소실 0개로 퇴근 — 달성 +₩150", d.Contract.SummaryText);

            var bad = WithCandidate(ContractKind.NoLoss, null);
            Assert.AreEqual(LunchConfirmResult.Ok, bad.ConfirmLunch(null, IndexOf(bad.Company, ContractKind.NoLoss)));
            var second = bad.Company.AddFactory(FactoryKind.Airport);
            second.Sim.PackageLostForTest(); // 다른 공장의 소실도 센다
            Assert.AreEqual(1, bad.Company.Contract.Progress);
            Assert.IsTrue(bad.Company.Contract.Failed);
            int before = bad.Economy.Money;
            DaySummary bd = null;
            bad.DayEnded += x => bd = x;
            ToClose(bad);
            Assert.AreEqual(before, bad.Economy.Money, "no reward, no penalty");
            Assert.IsFalse(bd.Contract.Achieved);
            Assert.AreEqual("오후 계약: 소실 0개로 퇴근 — 미달 · 소실 1개", bd.Contract.SummaryText);
        }

        [Test]
        public void NoMissingWaybill_CountsDockedParcelsWithoutAWaybill()
        {
            var s = WithCandidate(ContractKind.NoMissingWaybill, x => Assert.AreEqual(BuildResult.Ok, x.Build.TryPlace(3, 3, CellType.Labeler, Direction.East)));
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(null, IndexOf(s.Company, ContractKind.NoMissingWaybill)));
            s.Sim.DeliverCarried(Good(1), CellType.Outlet);
            Assert.AreEqual(0, s.Company.Contract.Progress);
            var p = Good(2); p.Waybill = false;
            s.Sim.DeliverCarried(p, CellType.ReturnDock);
            Assert.AreEqual(1, s.Company.Contract.Progress);
            int before = s.Economy.Money;
            DaySummary d = null;
            s.DayEnded += x => d = x;
            ToClose(s);
            Assert.AreEqual(before, s.Economy.Money);
            Assert.AreEqual("오후 계약: 운송장 누락 0개 — 미달 · 누락 1개", d.Contract.SummaryText);
        }

        [Test]
        public void UnmetCountContract_FailsAtSixWithoutPenalty_AndTheNextDayStartsClean()
        {
            var s = WithCandidate(ContractKind.Deliver, null);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Gukbap, IndexOf(s.Company, ContractKind.Deliver)));
            for (int i = 0; i < 5; i++) s.Sim.DeliverCarried(Good(10 + i), CellType.Outlet);
            int money = s.Economy.Money;
            DaySummary d = null;
            s.DayEnded += x => d = x;
            ToClose(s);
            Assert.AreEqual(money, s.Economy.Money);
            Assert.AreEqual("오후 계약: 정상 출고 12개 — 미달 5/12", d.Contract.SummaryText);
            Assert.AreNotSame(s.Company.Contract, d.Contract, "the summary keeps its own copy");
            s.NextDay();
            Assert.IsNull(s.Company.Contract);
            Assert.AreEqual(LunchMenu.None, s.Company.Menu);
            Assert.IsFalse(s.Company.LunchConfirmed);
            Assert.AreEqual(0, s.Company.ContractCandidates.Count);
            Assert.IsNull(s.BuildSummary().Contract, "no contract on a new day");
        }

        // ── 저장 ──

        [Test]
        public void Save_v11_RoundTripsMenuContractConfirmedAndCandidates()
        {
            Assert.AreEqual(11, SaveSerializer.CurrentVersion);
            var s = WithCandidate(ContractKind.Deliver, null);
            var kinds = s.Company.ContractCandidates.Select(x => x.Kind + ":" + x.Target).ToList();
            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(11, st.Version);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            CollectionAssert.AreEqual(kinds, r.Company.ContractCandidates.Select(x => x.Kind + ":" + x.Target).ToList(), "same candidates after loading at lunch");
            Assert.IsFalse(r.Company.LunchConfirmed);
            Assert.IsTrue(r.Company.LunchCardAvailable);

            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Jeyuk, IndexOf(s.Company, ContractKind.Deliver)));
            for (int i = 0; i < 4; i++) s.Sim.DeliverCarried(Good(10 + i), CellType.Outlet);
            var r2 = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(SaveSerializer.Capture(s))));
            Assert.IsTrue(r2.Company.LunchConfirmed);
            Assert.AreEqual(LunchMenu.Jeyuk, r2.Company.Menu);
            Assert.AreEqual(s.Worker.EffectiveSpeed, r2.Worker.EffectiveSpeed, 1e-5f, "the buff is back on");
            Assert.AreEqual(ContractKind.Deliver, r2.Company.Contract.Kind);
            Assert.AreEqual(4, r2.Company.Contract.Progress);
            Assert.AreEqual(12, r2.Company.Contract.Target);
            Assert.IsFalse(r2.Company.Contract.Achieved);
            for (int i = 0; i < 8; i++) r2.Sim.DeliverCarried(Good(20 + i), CellType.Outlet);
            Assert.IsTrue(r2.Company.Contract.Achieved, "counting continues after loading");
            var r3 = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(SaveSerializer.Capture(r2))));
            Assert.IsTrue(r3.Company.Contract.Achieved);
            int money = r3.Economy.Money;
            r3.Sim.DeliverCarried(Good(50), CellType.Outlet);
            Assert.AreEqual(money + 30, r3.Economy.Money, "an achieved contract is not paid twice after loading");
        }

        [Test]
        public void Save_v10_LoadsWithNoMenuNoContract()
        {
            var s = WithCandidate(ContractKind.Deliver, null);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Gukbap, null));
            Assert.AreEqual(0.7f, s.Worker.RunDrainMultiplier, 1e-6f);
            var json = SaveSerializer.ToJson(SaveSerializer.Capture(s)).Replace("\"Version\": 11", "\"Version\": 10");
            json = SaveV10Tests.StripTopLevel(json, "LunchMenu", "LunchConfirmed", "Contract", "ContractCandidates"); // 진짜 v10 모양
            StringAssert.DoesNotContain("Lunch", json);
            StringAssert.DoesNotContain("Contract", json);
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));
            Assert.AreEqual(LunchMenu.None, r.Company.Menu);
            Assert.IsNull(r.Company.Contract);
            Assert.IsFalse(r.Company.LunchConfirmed);
            Assert.AreEqual(1f, r.Worker.RunDrainMultiplier);
            Assert.AreEqual(0, r.Company.ContractCandidates.Count);
            r.Tick(0.05f);
            Assert.AreEqual(3, r.Company.ContractCandidates.Count, "a v10 save loaded at lunch still gets a card");
        }

        [Test]
        public void Save_RejectsBrokenLunchState()
        {
            var s = WithCandidate(ContractKind.Deliver, null);
            Assert.AreEqual(LunchConfirmResult.Ok, s.ConfirmLunch(LunchMenu.Gukbap, IndexOf(s.Company, ContractKind.Deliver)));
            GameState Fresh() => SaveSerializer.FromJson(SaveSerializer.ToJson(SaveSerializer.Capture(s)));
            var a = Fresh(); a.LunchMenu = "Pizza";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(a));
            var b = Fresh(); b.LunchConfirmed = false; // 메뉴·계약이 있는데 확정 안 함
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(b));
            var c = Fresh(); c.Contract.Progress = -1;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(c));
            var d = Fresh(); d.Contract.Kind = "Nope";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(d));
            var e = Fresh(); e.ContractCandidates.Add(new ContractState { Kind = "Deliver", Target = 12 });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(e), "duplicate candidate kinds");
            var f = Fresh(); f.Contract.Progress = 99; // 목표를 넘었는데 달성 안 함
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(f));
            Assert.DoesNotThrow(() => SaveSerializer.Restore(Fresh()), "the unchanged save loads");

            var g = Fresh(); g.ContractCandidates.RemoveAt(2); // 후보는 0개 또는 3개
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(g), "two candidates");
            var g2 = Fresh(); g2.ContractCandidates.Clear(); g2.Contract = null; g2.LunchMenu = "Gukbap";
            Assert.DoesNotThrow(() => SaveSerializer.Restore(g2), "no candidates is fine");
            var h = Fresh(); h.MinuteOfDay = Clock.LunchStart - 1f; // 정오 전에 확정
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(h));
            var i = Fresh(); i.MinuteOfDay = Clock.CloseMinute; // 퇴근 뒤에도 메뉴가 남음
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(i));
            var i2 = Fresh(); i2.MinuteOfDay = Clock.CloseMinute; i2.LunchMenu = "None";
            Assert.DoesNotThrow(() => SaveSerializer.Restore(i2), "a contract stays until the next morning");
            var j = Fresh(); // 후보에 없는 종류의 계약(이 회사에는 항공 도크가 없어 후보가 정상 출고·소실·불량 반품)
            Assert.IsFalse(j.ContractCandidates.Exists(c => c.Kind == "Air"));
            j.Contract = new ContractState { Kind = "Air", Target = 5 };
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(j));
            var k = Fresh(); k.Contract.Target = 13; k.ContractCandidates.Find(c => c.Kind == "Deliver").Target = 13; // 12k가 아니다
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(k));
            var l = Fresh(); l.Contract.Target = 24; l.ContractCandidates.Find(c => c.Kind == "Deliver").Target = 24; // k = 2: 공식에 맞는 값
            Assert.DoesNotThrow(() => SaveSerializer.Restore(l), "a target from an earlier inlet count is valid");
            var m = Fresh(); m.ContractCandidates.Find(c => c.Kind == "NoLoss" || c.Kind == "Return").Target = 5;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(m), "candidates are checked too");
        }

        // ── 기존 규칙 ──

        [Test]
        public void WithoutConfirming_NothingChanges()
        {
            var s = new GameSession(new Economy(), null, new Random(1));
            ToLunch(s);
            s.Tick(60f);
            int money = s.Economy.Money;
            s.Sim.DeliverCarried(Good(1), CellType.Outlet);
            Assert.AreEqual(money + 30, s.Economy.Money);
            Assert.AreEqual(1f, s.Worker.RunDrainMultiplier);
            Assert.AreEqual(WorkerService.Speed, s.Worker.EffectiveSpeed, 1e-6f);
            DaySummary d = null;
            s.DayEnded += x => d = x;
            ToClose(s);
            Assert.IsNull(d.Contract);
        }
    }

    internal static class LunchTestExtensions
    {
        /// <summary>소실 한 건을 일으킨다: 입고구 칸에 택배를 놓고 그 칸의 택배를 소실 처리한다(RemovePackagesAt).</summary>
        public static void PackageLostForTest(this ConveyorSim sim)
        {
            var p = new PackageFactory(new Random(1), 0.0).Create(sim.NextId++, 0, 0, Direction.East);
            sim.PlaceCarried(p, FactoryGrid.InletX, FactoryGrid.InletY);
            sim.RemovePackagesAt(FactoryGrid.InletX, FactoryGrid.InletY);
        }

        public static bool Factories0HasAirDocks(this GameSession s) => s.Company.Factories[0].HasAirDocks;
    }
}
