using System;

namespace ParcelFactory.Core
{
    public sealed class Economy
    {
        public const int StartingMoney = 100;

        public int Money { get; private set; }

        public event Action<int> MoneyChanged;

        public Economy(int money = StartingMoney)
        {
            Money = money;
        }

        public bool CanAfford(int amount) => Money >= amount;

        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (Money < amount) return false;
            Money -= amount;
            MoneyChanged?.Invoke(Money);
            return true;
        }

        public void Earn(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Money += amount;
            MoneyChanged?.Invoke(Money);
        }
    }
}
