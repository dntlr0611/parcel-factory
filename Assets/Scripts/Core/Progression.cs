using System;

namespace ParcelFactory.Core
{
    /// <summary>작업자 능력치(스펙 11.5).</summary>
    public enum Stat { Stamina, Speed, Regen }

    public enum UpgradeResult { Ok, MaxLevel, NotEnoughExp }

    /// <summary>EXP와 작업자 강화(스펙 11.5). EXP는 잔액과 별개의 누적치이며 강화에 쓴다.
    /// 효과는 레벨에 선형: 최대 체력 100 + 20n, 속도 ×(1 + 0.08n), 회복 ×(1 + 0.5n).
    /// 비용은 레벨 n으로 올릴 때 40n / 50n / 40n, 최대 레벨 5 / 5 / 3.</summary>
    public sealed class Progression
    {
        public const int DeliveredExp = 10, ReturnedBadExp = 8, InspectExp = 2;
        public const float BaseMaxStamina = 100f, StaminaPerLevel = 20f, SpeedPerLevel = 0.08f, RegenPerLevel = 0.5f;

        private static readonly int[] MaxLevels = { 5, 5, 3 };
        private static readonly int[] CostBases = { 40, 50, 40 };

        private readonly int[] levels = new int[3];

        /// <summary>쓸 수 있는 EXP.</summary>
        public int Exp { get; private set; }
        /// <summary>지금까지 획득한 EXP 총량(지출과 무관하게 증가만 한다). 하루 요약의 "획득 EXP" 기준.</summary>
        public int TotalEarned { get; private set; }

        /// <summary>새 Exp 값.</summary>
        public event Action<int> ExpChanged;
        /// <summary>(능력치, 새 레벨).</summary>
        public event Action<Stat, int> Upgraded;

        public int Level(Stat s) => levels[Index(s)];
        public int MaxLevel(Stat s) => MaxLevels[Index(s)];
        public bool IsMaxed(Stat s) => Level(s) >= MaxLevel(s);

        /// <summary>다음 레벨(현재 + 1)로 올리는 비용. 최대 레벨이면 0.</summary>
        public int Cost(Stat s) => IsMaxed(s) ? 0 : CostBases[Index(s)] * (Level(s) + 1);

        public float MaxStamina => BaseMaxStamina + StaminaPerLevel * levels[(int)Stat.Stamina];
        public float SpeedMultiplier => 1f + SpeedPerLevel * levels[(int)Stat.Speed];
        public float RegenMultiplier => 1f + RegenPerLevel * levels[(int)Stat.Regen];

        /// <summary>EXP 적립. 0 이하는 무시한다.</summary>
        public void AddExp(int amount)
        {
            if (amount <= 0) return;
            Exp += amount;
            TotalEarned += amount;
            ExpChanged?.Invoke(Exp);
        }

        /// <summary>즉시 적용되고 되돌릴 수 없다. 최대 레벨 검사가 EXP 부족보다 먼저다.</summary>
        public UpgradeResult TryUpgrade(Stat s)
        {
            if (IsMaxed(s)) return UpgradeResult.MaxLevel;
            int cost = Cost(s);
            if (Exp < cost) return UpgradeResult.NotEnoughExp;
            Exp -= cost;
            levels[Index(s)]++;
            ExpChanged?.Invoke(Exp);
            Upgraded?.Invoke(s, levels[Index(s)]);
            return UpgradeResult.Ok;
        }

        /// <summary>저장 복원·테스트용. TotalEarned는 exp로 둔다. 이벤트는 발생하지 않는다.</summary>
        public void Restore(int exp, int staminaLevel, int speedLevel, int regenLevel) =>
            Restore(exp, staminaLevel, speedLevel, regenLevel, exp);

        /// <summary>저장 복원용. 범위 밖이면 ArgumentOutOfRangeException. 이벤트는 발생하지 않는다.</summary>
        public void Restore(int exp, int staminaLevel, int speedLevel, int regenLevel, int totalEarned)
        {
            if (exp < 0) throw new ArgumentOutOfRangeException(nameof(exp), exp, "exp < 0");
            if (totalEarned < exp) throw new ArgumentOutOfRangeException(nameof(totalEarned), totalEarned, "totalEarned < exp");
            CheckLevel(Stat.Stamina, staminaLevel);
            CheckLevel(Stat.Speed, speedLevel);
            CheckLevel(Stat.Regen, regenLevel);
            Exp = exp;
            TotalEarned = totalEarned;
            levels[(int)Stat.Stamina] = staminaLevel;
            levels[(int)Stat.Speed] = speedLevel;
            levels[(int)Stat.Regen] = regenLevel;
        }

        private void CheckLevel(Stat s, int level)
        {
            if (level < 0 || level > MaxLevel(s))
                throw new ArgumentOutOfRangeException(s.ToString(), level, $"{s} level outside 0..{MaxLevel(s)}");
        }

        private static int Index(Stat s)
        {
            int i = (int)s;
            if (i < 0 || i >= MaxLevels.Length) throw new ArgumentOutOfRangeException(nameof(s), s, "unknown stat");
            return i;
        }
    }
}
