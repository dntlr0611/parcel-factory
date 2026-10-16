using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public enum UnlockId { Plot1, Plot2, RailSouth, RailWest, RailNorth, FastEast, FastSouth, FastWest, FastNorth, Splitter, Scanner }

    public sealed class UnlockDef
    {
        public UnlockId Id;
        public string Name;
        public int Price;
        public UnlockId? Requires;
    }

    public static class UnlockCatalog
    {
        public static readonly IReadOnlyList<UnlockDef> All = new[]
        {
            new UnlockDef { Id = UnlockId.Plot1, Name = "부지 확장 1 (9×9)", Price = 200 },
            new UnlockDef { Id = UnlockId.Plot2, Name = "부지 확장 2 (12×12)", Price = 500, Requires = UnlockId.Plot1 },
            new UnlockDef { Id = UnlockId.RailSouth, Name = "레일 ↓", Price = 50 },
            new UnlockDef { Id = UnlockId.RailWest, Name = "레일 ←", Price = 50 },
            new UnlockDef { Id = UnlockId.RailNorth, Name = "레일 ↑", Price = 50 },
            new UnlockDef { Id = UnlockId.FastEast, Name = "고속 레일 →", Price = 100 },
            new UnlockDef { Id = UnlockId.FastSouth, Name = "고속 레일 ↓", Price = 100 },
            new UnlockDef { Id = UnlockId.FastWest, Name = "고속 레일 ←", Price = 100 },
            new UnlockDef { Id = UnlockId.FastNorth, Name = "고속 레일 ↑", Price = 100 },
            new UnlockDef { Id = UnlockId.Splitter, Name = "분배기", Price = 300 },
            new UnlockDef { Id = UnlockId.Scanner, Name = "자동 검사기", Price = 250 },
        };

        public static UnlockDef Get(UnlockId id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    public enum UnlockResult { Ok, AlreadyOwned, RequirementMissing, NotEnoughMoney }

    public sealed class UnlockService
    {
        private readonly Economy economy;
        private readonly FactoryGrid grid;
        private readonly Toolbar toolbar;
        private readonly HashSet<UnlockId> purchased = new HashSet<UnlockId>();

        public IReadOnlyCollection<UnlockId> Purchased => purchased;

        public event Action<UnlockId> Unlocked;

        public UnlockService(Economy economy, FactoryGrid grid, Toolbar toolbar)
        {
            this.economy = economy;
            this.grid = grid;
            this.toolbar = toolbar;
        }

        public bool IsPurchased(UnlockId id) => purchased.Contains(id);

        public UnlockResult TryPurchase(UnlockId id)
        {
            var def = UnlockCatalog.Get(id);
            if (purchased.Contains(id)) return UnlockResult.AlreadyOwned;
            if (def.Requires.HasValue && !purchased.Contains(def.Requires.Value)) return UnlockResult.RequirementMissing;
            if (!economy.TrySpend(def.Price)) return UnlockResult.NotEnoughMoney;
            Apply(id);
            return UnlockResult.Ok;
        }

        /// <summary>
        /// 저장 복원용. 결제 없이 효과만 적용한다. 이미 보유 중이면 무시.
        /// 선행 항목이 미보유면 먼저 적용하므로 호출 순서에 의존하지 않는다.
        /// </summary>
        public void Grant(UnlockId id)
        {
            if (purchased.Contains(id)) return;
            var def = UnlockCatalog.Get(id);
            if (def.Requires.HasValue) Grant(def.Requires.Value);
            Apply(id);
        }

        private void Apply(UnlockId id)
        {
            purchased.Add(id);
            switch (id)
            {
                case UnlockId.Plot1: grid.UnlockRegion(9); break;
                case UnlockId.Plot2: grid.UnlockRegion(12); break;
                case UnlockId.RailSouth: toolbar.Add(CellType.Conveyor, Direction.South); break;
                case UnlockId.RailWest: toolbar.Add(CellType.Conveyor, Direction.West); break;
                case UnlockId.RailNorth: toolbar.Add(CellType.Conveyor, Direction.North); break;
                case UnlockId.FastEast: toolbar.Add(CellType.FastConveyor, Direction.East); break;
                case UnlockId.FastSouth: toolbar.Add(CellType.FastConveyor, Direction.South); break;
                case UnlockId.FastWest: toolbar.Add(CellType.FastConveyor, Direction.West); break;
                case UnlockId.FastNorth: toolbar.Add(CellType.FastConveyor, Direction.North); break;
                case UnlockId.Splitter: toolbar.Add(CellType.Splitter, Direction.East); break;
                case UnlockId.Scanner: toolbar.Add(CellType.Scanner, Direction.East); break;
            }
            Unlocked?.Invoke(id);
        }
    }
}
