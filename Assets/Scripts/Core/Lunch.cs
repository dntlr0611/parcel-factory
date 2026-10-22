using System;

namespace ParcelFactory.Core
{
    /// <summary>구내식당 메뉴(스펙 21.2). 하루 하나, 확정 순간부터 18:00까지. None = 고르지 않음.</summary>
    public enum LunchMenu { None, Gukbap, Jeyuk, Salad, Rest }

    /// <summary>메뉴 표(스펙 21.2): 가격·이름·효과.</summary>
    public static class LunchMenus
    {
        /// <summary>카드에 보이는 순서.</summary>
        public static readonly LunchMenu[] All = { LunchMenu.Gukbap, LunchMenu.Jeyuk, LunchMenu.Salad, LunchMenu.Rest };
        /// <summary>국밥: 달리기 체력 소모 ×0.7. 제육볶음: 이동 속도 ×1.15(능력치 배수에 곱함). 샐러드: 검사 EXP ×2.</summary>
        public const float GukbapRunDrain = 0.7f, JeyukSpeed = 1.15f;
        public const int SaladInspectMultiplier = 2;

        public static int Price(LunchMenu m)
        {
            switch (m)
            {
                case LunchMenu.Gukbap: return 40;
                case LunchMenu.Jeyuk: return 60;
                case LunchMenu.Salad: return 50;
                case LunchMenu.Rest:
                case LunchMenu.None: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(m), m, "unknown menu");
            }
        }

        public static string Name(LunchMenu m)
        {
            switch (m)
            {
                case LunchMenu.Gukbap: return "국밥";
                case LunchMenu.Jeyuk: return "제육볶음";
                case LunchMenu.Salad: return "샐러드";
                case LunchMenu.Rest: return "그냥 쉬기";
                default: return "";
            }
        }

        public static string Effect(LunchMenu m)
        {
            switch (m)
            {
                case LunchMenu.Gukbap: return "달리기 체력 소모 −30%";
                case LunchMenu.Jeyuk: return "이동 속도 +15%";
                case LunchMenu.Salad: return "검사 EXP 2배";
                case LunchMenu.Rest: return "효과 없음";
                default: return "";
            }
        }
    }

    /// <summary>오후 목표 계약 종류(스펙 21.3). 순서는 카드에 보이는 순서다.</summary>
    public enum ContractKind { Deliver, NoLoss, Return, Air, Fragile, NoMissingWaybill }

    /// <summary>계약 표(스펙 21.3): 보상·목표(입고구 수 k 비례)·문구.</summary>
    public static class ContractCatalog
    {
        public static readonly ContractKind[] All =
            { ContractKind.Deliver, ContractKind.NoLoss, ContractKind.Return, ContractKind.Air, ContractKind.Fragile, ContractKind.NoMissingWaybill };

        /// <summary>"0개" 류: 진행은 위반 수이고 18:00에 0이면 달성.</summary>
        public static bool IsZeroKind(ContractKind k) => k == ContractKind.NoLoss || k == ContractKind.NoMissingWaybill;

        public static int Reward(ContractKind k)
        {
            switch (k)
            {
                case ContractKind.Deliver: return 200;
                case ContractKind.NoLoss: return 150;
                case ContractKind.Return: return 150;
                case ContractKind.Air: return 250;
                case ContractKind.Fragile: return 300;
                case ContractKind.NoMissingWaybill: return 150;
                default: throw new ArgumentOutOfRangeException(nameof(k), k, "unknown contract");
            }
        }

        /// <summary>회사 전체 입고구 수 inlets(k)에 비례: 정상 출고 12k, 반품 3k, 항공 5k, 취급주의 3k(최소 1). "0개" 류는 0.</summary>
        public static int Target(ContractKind k, int inlets)
        {
            int per;
            switch (k)
            {
                case ContractKind.Deliver: per = 12; break;
                case ContractKind.Return: per = 3; break;
                case ContractKind.Air: per = 5; break;
                case ContractKind.Fragile: per = 3; break;
                case ContractKind.NoLoss:
                case ContractKind.NoMissingWaybill: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(k), k, "unknown contract");
            }
            return Math.Max(1, per * inlets);
        }

        /// <summary>카드·하루 요약의 이름: "항공 출고 10개".</summary>
        public static string Title(ContractKind k, int target)
        {
            switch (k)
            {
                case ContractKind.Deliver: return "정상 출고 " + target + "개";
                case ContractKind.NoLoss: return "소실 0개로 퇴근";
                case ContractKind.Return: return "불량 반품 " + target + "개";
                case ContractKind.Air: return "항공 출고 " + target + "개";
                case ContractKind.Fragile: return "취급주의 파손 없이 " + target + "개 출고";
                case ContractKind.NoMissingWaybill: return "운송장 누락 0개";
                default: return k.ToString();
            }
        }

        /// <summary>카드의 조건 설명(muted 한 줄).</summary>
        public static string Condition(ContractKind k)
        {
            switch (k)
            {
                case ContractKind.Deliver: return "모든 공장 합계 · 달성 즉시 지급";
                case ContractKind.NoLoss: return "오후 소실 0 · 18:00에 판정";
                case ContractKind.Return: return "거부한 불량 택배만 반품 트럭으로 · 정상품 반품은 세지 않음";
                case ContractKind.Air: return "항공 택배를 항공 트럭으로 정상 출고";
                case ContractKind.Fragile: return "취급주의를 파손 없이 정상 출고";
                case ContractKind.NoMissingWaybill: return "운송장 없이 트럭에 들어간 택배가 하나라도 있으면 실패";
                default: return "";
            }
        }

        /// <summary>진행 칩의 짧은 이름: "항공 출고".</summary>
        public static string ShortName(ContractKind k)
        {
            switch (k)
            {
                case ContractKind.Deliver: return "정상 출고";
                case ContractKind.Return: return "불량 반품";
                case ContractKind.Air: return "항공 출고";
                case ContractKind.Fragile: return "취급주의 출고";
                default: return Title(k, 0);
            }
        }

        /// <summary>목표가 공식(Target(kind, k), k = 1 이상의 입고구 수)으로 나올 수 있는 값인가(저장 검사). 입고구는 회사 전체 최대 9개.</summary>
        public static bool IsValidTarget(ContractKind kind, int target)
        {
            if (IsZeroKind(kind)) return target == 0;
            for (int k = 1; k <= 3 * FactoryGrid.InletTiles.Length; k++) if (Target(kind, k) == target) return true;
            return false;
        }

        /// <summary>"0개" 류의 위반 이름(하루 요약 "미달 · 소실 2개").</summary>
        public static string ViolationName(ContractKind k) => k == ContractKind.NoLoss ? "소실" : "누락";
    }

    /// <summary>오늘의 계약(스펙 21.3): 종류·목표·진행·달성. 개수 계약은 진행이 목표에 닿는 순간 달성(보상 즉시),
    /// "0개" 류는 진행이 위반 수이고 18:00에 0이면 달성. 실패는 감점이 없다.</summary>
    public sealed class Contract
    {
        public const int RewardExp = 30;

        public ContractKind Kind;
        public int Target;
        public int Progress;
        public bool Achieved;

        public int Reward => ContractCatalog.Reward(Kind);
        public bool IsZeroKind => ContractCatalog.IsZeroKind(Kind);
        /// <summary>"0개" 류가 이미 위반됐다(더는 달성할 수 없다).</summary>
        public bool Failed => IsZeroKind && Progress > 0;
        public string Title => ContractCatalog.Title(Kind, Target);

        /// <summary>진행 칩: "계약 · 항공 출고 7/10", "0개" 류는 "계약 · 소실 0개로 퇴근 · 지금 1".</summary>
        public string ChipText => IsZeroKind
            ? "계약 · " + Title + " · 지금 " + Progress
            : "계약 · " + ContractCatalog.ShortName(Kind) + " " + Progress + "/" + Target;

        /// <summary>하루 요약 줄: "오후 계약: 항공 출고 10개 — 달성 +₩250" / "— 미달 7/10" / "— 미달 · 소실 2개".</summary>
        public string SummaryText => "오후 계약: " + Title + " — " + (Achieved ? "달성 +₩" + Reward
            : IsZeroKind ? "미달 · " + ContractCatalog.ViolationName(Kind) + " " + Progress + "개" : "미달 " + Progress + "/" + Target);

        /// <summary>달성 토스트: "계약 달성! +₩250 · EXP +30".</summary>
        public string AchievedToast => "계약 달성! +₩" + Reward + " · EXP +" + RewardExp;

        public Contract Clone() => new Contract { Kind = Kind, Target = Target, Progress = Progress, Achieved = Achieved };
    }

    /// <summary>점심 카드 확정 결과(스펙 21.1). 순서: 점심시간 → 이미 확정 → 아무것도 안 고름 → 계약 번호 → 잔액.</summary>
    public enum LunchConfirmResult { Ok, NotLunch, AlreadyConfirmed, NothingChosen, InvalidContract, NotEnoughMoney }
}
