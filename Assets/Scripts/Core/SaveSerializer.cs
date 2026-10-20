using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ParcelFactory.Core
{
    public sealed class SaveVersionException : Exception
    {
        public SaveVersionException(string message) : base(message) { }
        public SaveVersionException(string message, Exception inner) : base(message, inner) { }
    }

    public static class SaveSerializer
    {
        public const int CurrentVersion = 6;
        /// <summary>읽을 수 있는 가장 오래된 버전. v6(EXP·강화, 스펙 11.5) 미만은 거부한다.</summary>
        public const int MinReadableVersion = 6;

        public static GameState Capture(GameSession s)
        {
            var st = new GameState
            {
                Version = CurrentVersion,
                Money = s.Economy.Money,
                UnlockedSize = s.Grid.UnlockedSize,
                SpawnTimer = s.Spawner.Timer,
                NextPackageId = s.Sim.NextId,
                DeliveredCount = s.Sim.DeliveredCount,
                LostCount = s.Sim.LostCount,
                Paused = s.Paused,
                Speed = s.Speed,
                Day = s.Clock.Day,
                MinuteOfDay = s.Clock.MinuteOfDay,
                DayStartMoney = s.DayStartMoney,
                DayStartDelivered = s.DayStartDelivered,
                DayStartLost = s.DayStartLost,
                DayStartInspected = s.DayStartInspected,
                InspectedCount = s.Sim.InspectedCount,
                Stamina = s.Worker.Worker.Stamina,
                Exp = s.Progression.Exp,
                TotalExp = s.Progression.TotalEarned,
                DayStartExp = s.DayStartExp,
                StaminaLevel = s.Progression.Level(Stat.Stamina),
                SpeedLevel = s.Progression.Level(Stat.Speed),
                RegenLevel = s.Progression.Level(Stat.Regen),
            };

            foreach (var id in s.Unlocks.Purchased)
                st.Purchased.Add(id.ToString());

            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = s.Grid.Get(x, y);
                    if (!c.IsBuilding) continue;
                    List<PackageState> pile = null;
                    if (c.Type == CellType.Pallet)
                    {
                        pile = new List<PackageState>();
                        foreach (var p in c.Pallet) pile.Add(ToState(p));
                    }
                    st.Cells.Add(new CellState
                    {
                        X = x, Y = y,
                        Type = c.Type.ToString(),
                        Direction = c.Direction.ToString(),
                        Demolishing = c.Demolishing,
                        DemolishRemaining = c.DemolishRemaining,
                        PalletKind = c.PalletKind.ToString(),
                        Pallet = pile,
                        RedDirection = c.RedDirection?.ToString(),
                    });
                }
            }

            foreach (var p in s.Sim.Packages)
                st.Packages.Add(ToState(p));
            var w = s.Worker.Worker;
            st.Worker = new WorkerState { X = w.X, Y = w.Y, Facing = w.Facing.ToString(), Carrying = w.Carrying == null ? null : ToState(w.Carrying) };
            return st;
        }

        public static GameSession Restore(GameState st)
        {
            if (st == null) throw new SaveVersionException("empty save");
            if (st.Version < MinReadableVersion || st.Version > CurrentVersion)
                throw new SaveVersionException($"save version {st.Version} not in [{MinReadableVersion},{CurrentVersion}]");
            try
            {
                return RestoreUnchecked(st);
            }
            catch (SaveVersionException)
            {
                throw;
            }
            catch (Exception e)
            {
                // null 리스트, 알 수 없는 enum 이름, 잘못된 좌표 등은 모두 손상된 저장으로 취급한다.
                throw new SaveVersionException("corrupt save: " + e.Message, e);
            }
        }

        private static GameSession RestoreUnchecked(GameState st)
        {
            if (float.IsNaN(st.Speed) || float.IsInfinity(st.Speed) || st.Speed <= 0f)
                throw new SaveVersionException($"invalid speed {st.Speed}");
            var s = new GameSession(new Economy(st.Money));

            // Grant가 선행 항목을 스스로 적용하므로 정렬은 결정적 순서를 위한 것이다.
            var ids = new List<UnlockId>();
            foreach (var name in st.Purchased)
                ids.Add(ParseDefined<UnlockId>(name));
            ids.Sort();
            foreach (var id in ids)
                s.Unlocks.Grant(id);

            // 새 세션은 시작 팔레트를 갖고 있다. 저장된 건물 목록이 전부이므로(철거된 시작 팔레트 포함) 먼저 비운다.
            for (int x = 0; x < FactoryGrid.Width; x++)
                for (int y = 0; y < FactoryGrid.Height; y++)
                    if (s.Grid.Get(x, y).IsBuilding) s.Grid.Clear(x, y);

            int maxId = -1;
            foreach (var c in st.Cells)
            {
                var type = ParseDefined<CellType>(c.Type);
                var dir = ParseDefined<Direction>(c.Direction);
                if (float.IsNaN(c.DemolishRemaining) || c.DemolishRemaining < 0f || c.DemolishRemaining > BuildService.DemolishSeconds)
                    throw new SaveVersionException($"invalid DemolishRemaining {c.DemolishRemaining} at ({c.X},{c.Y})");
                s.Grid.Place(c.X, c.Y, type, dir);
                var cell = s.Grid.Get(c.X, c.Y);
                cell.Demolishing = c.Demolishing;
                cell.DemolishRemaining = c.DemolishRemaining;
                if (type == CellType.Pallet)
                {
                    var kind = ParseDefined<PalletKind>(c.PalletKind);
                    if (kind == PalletKind.None) throw new SaveVersionException($"pallet at ({c.X},{c.Y}) has no kind");
                    cell.PalletKind = kind;
                    var pile = c.Pallet ?? new List<PackageState>();
                    if (pile.Count > Cell.PalletCapacity)
                        throw new SaveVersionException($"pallet at ({c.X},{c.Y}) holds {pile.Count} > {Cell.PalletCapacity}");
                    foreach (var ps in pile)
                    {
                        var pk = FromState(ps);
                        pk.X = c.X; pk.Y = c.Y;
                        pk.Lane = Lane.Upper; // 더미에는 차선이 없다
                        cell.Pallet.Add(pk); // 더미는 흐름(Sim.Packages)에 넣지 않는다
                        if (pk.Id > maxId) maxId = pk.Id;
                    }
                }
                else if (c.Pallet != null && c.Pallet.Count > 0)
                    throw new SaveVersionException($"non-pallet cell ({c.X},{c.Y}) has a pile");
                if (c.RedDirection != null)
                {
                    if (type != CellType.Splitter)
                        throw new SaveVersionException($"non-splitter cell ({c.X},{c.Y}) has a red direction");
                    cell.RedDirection = ParseDefined<Direction>(c.RedDirection);
                }
            }

            foreach (var p in st.Packages)
            {
                if (!s.Grid.InBounds(p.X, p.Y))
                    throw new SaveVersionException($"package {p.Id} out of bounds ({p.X},{p.Y})");
                var target = s.Grid.Get(p.X, p.Y);
                var pk = FromState(p);
                if (!target.IsTrack || target.OccupantOf(pk.Lane) >= 0)
                    throw new SaveVersionException($"package {p.Id} on invalid cell ({p.X},{p.Y})");
                if (pk.Lane == Lane.Lower && target.Type != CellType.Bridge)
                    throw new SaveVersionException($"package {p.Id} on lower lane off a bridge ({p.X},{p.Y})");
                s.Sim.Restore(pk); // 차선에 맞춰 OccupantId 또는 LowerOccupantId를 세운다
                if (p.Id > maxId) maxId = p.Id;
            }

            if (st.Worker == null) throw new SaveVersionException("missing worker");
            if (!s.Worker.CanStand(st.Worker.X, st.Worker.Y))
                throw new SaveVersionException($"worker on invalid position ({st.Worker.X},{st.Worker.Y})");
            s.Worker.Worker.X = st.Worker.X;
            s.Worker.Worker.Y = st.Worker.Y;
            s.Worker.Worker.Facing = ParseDefined<Direction>(st.Worker.Facing);
            if (st.Worker.Carrying != null)
            {
                var carried = FromState(st.Worker.Carrying);
                carried.Lane = Lane.Upper; // 손에 든 택배에는 차선이 없다
                if (carried.Id > maxId) maxId = carried.Id;
                s.Worker.Worker.Carrying = carried;
            }
            if (st.NextPackageId <= maxId)
                throw new SaveVersionException($"NextPackageId {st.NextPackageId} <= package id {maxId}");
            if (st.NextPackageId < 1) throw new SaveVersionException($"NextPackageId {st.NextPackageId} < 1");

            s.Sim.NextId = st.NextPackageId;
            s.Sim.DeliveredCount = st.DeliveredCount;
            s.Sim.LostCount = st.LostCount;
            s.Spawner.Timer = st.SpawnTimer;
            s.Paused = st.Paused;
            s.Speed = st.Speed;

            s.Clock.Set(st.Day, st.MinuteOfDay); // 범위 밖이면 ArgumentOutOfRangeException → corrupt save
            // 레벨 범위 밖·음수 EXP는 ArgumentOutOfRangeException → corrupt save. 최대 체력이 레벨에 달렸으므로 체력보다 먼저.
            s.Progression.Restore(st.Exp, st.StaminaLevel, st.SpeedLevel, st.RegenLevel, st.TotalExp);
            if (st.DayStartExp < 0 || st.DayStartExp > st.TotalExp)
                throw new SaveVersionException($"invalid DayStartExp {st.DayStartExp}");
            if (!(st.Stamina >= 0f && st.Stamina <= s.Worker.EffectiveMaxStamina))
                throw new SaveVersionException($"invalid stamina {st.Stamina}");
            s.Worker.Worker.Stamina = st.Stamina;
            s.Sim.InspectedCount = st.InspectedCount;
            s.DayStartMoney = st.DayStartMoney;
            s.DayStartDelivered = st.DayStartDelivered;
            s.DayStartLost = st.DayStartLost;
            s.DayStartInspected = st.DayStartInspected;
            s.DayStartExp = st.DayStartExp;
            return s;
        }

        private static PackageState ToState(Package p) => new PackageState
        {
            Id = p.Id, Kind = p.Kind.ToString(), X = p.X, Y = p.Y,
            Progress = p.Progress, Heading = p.Heading.ToString(), EntryHeading = p.EntryHeading.ToString(),
            Lane = p.Lane.ToString(),
            Bad = p.Bad, Inspected = p.Inspected, Rejected = p.Rejected,
            LabelDestination = p.Label?.Destination, LabelWeight = p.Label?.Weight ?? 0,
            LabelMethod = p.Label?.Method, LabelScale = p.Label?.Scale ?? 0,
        };

        private static Package FromState(PackageState p)
        {
            if (p == null) throw new SaveVersionException("null package");
            if (float.IsNaN(p.Progress) || p.Progress < 0f || p.Progress > 1f)
                throw new SaveVersionException($"package {p.Id} invalid progress {p.Progress}");
            if (p.Id < 0) throw new SaveVersionException($"package id {p.Id} < 0");
            return new Package
            {
                Id = p.Id,
                Kind = ParseDefined<PackageKind>(p.Kind),
                X = p.X, Y = p.Y,
                Progress = p.Progress,
                Heading = ParseDefined<Direction>(p.Heading),
                EntryHeading = ParseDefined<Direction>(p.EntryHeading),
                Lane = p.Lane == null ? Lane.Upper : ParseDefined<Lane>(p.Lane),
                Bad = p.Bad, Inspected = p.Inspected, Rejected = p.Rejected,
                Label = new PackageLabel { Destination = p.LabelDestination ?? "", Weight = p.LabelWeight, Method = p.LabelMethod ?? "", Scale = p.LabelScale },
            };
        }

        private static T ParseDefined<T>(string name) where T : struct
        {
            if (name == null || !Enum.TryParse(name, out T value) || !Enum.IsDefined(typeof(T), value))
                throw new SaveVersionException($"invalid {typeof(T).Name} '{name}'");
            return value;
        }

        public static string ToJson(GameState st) => JsonConvert.SerializeObject(st, Formatting.Indented);

        public static GameState FromJson(string json)
        {
            if (json == null) throw new SaveVersionException("empty save");
            GameState st;
            try
            {
                st = JsonConvert.DeserializeObject<GameState>(json);
            }
            catch (Exception e)
            {
                throw new SaveVersionException("invalid save json", e);
            }
            if (st == null) throw new SaveVersionException("empty save");
            return st;
        }
    }
}
