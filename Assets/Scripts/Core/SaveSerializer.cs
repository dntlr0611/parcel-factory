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
        /// <summary>v7: 택배 Air(스펙 14.4). v6 저장도 읽는다(Air = false, 항공 도크는 부지 확장 2 복원 때 다시 생긴다).
        /// v8: 입고구마다의 타이머(스펙 16.2). v7 이하도 읽는다(부지 확장 입고구 자리의 설치물은 환불 철거).
        /// v9: 취급주의 Fragile·Broken·FragileSticker·FragileFromDay(스펙 17), 트럭 적재·대기(스펙 18). v8 이하는 모두 없는 상태로 읽는다.</summary>
        public const int CurrentVersion = 9;
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
                FragileFromDay = s.FragileFromDay,
                TrucksDepartedToday = s.Sim.Trucks.DepartedToday,
                Trucks = new List<TruckState>(),
            };
            foreach (var d in TruckFleet.Docks)
                if (!Cell.IsAirDock(d) || s.Unlocks.IsPurchased(UnlockId.Plot2)) // 항공 트럭은 부지 확장 2 이후에만 있다
                    st.Trucks.Add(new TruckState { Dock = d.ToString(), Loaded = s.Sim.Trucks.Loaded(d), AwayMinutes = s.Sim.Trucks.AwayMinutes(d) });

            foreach (var id in s.Unlocks.Purchased)
                st.Purchased.Add(id.ToString());
            st.InletTimers = new List<InletTimerState>();
            foreach (var (x, y, timer) in s.Spawner.Timers())
                st.InletTimers.Add(new InletTimerState { X = x, Y = y, Timer = timer });

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
                        Mode = c.Type == CellType.Splitter || c.Type == CellType.Scanner ? c.Mode.ToString() : null,
                        AirDirection = c.AirDirection?.ToString(),
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
            // 예전 저장에서 빈 칸이던 자리가 지금은 해금으로 고정 칸이 된 경우(스펙 14.1, 16.2):
            // v6의 항공 도크 (11,7)·(11,9), v7 이하의 부지 확장 입고구 (0,7)·(0,10). 지금은 Grant가 그 칸을 만들었으므로
            // 거기 지어 둔 설치물은 설치비 전액 환불로 철거하고, 그 위(팔레트 더미·브릿지 아랫길 포함)의 택배는 소실로 센다.
            int droppedParcels = 0, refund = 0;
            bool AirDockTile(int x, int y)
            {
                if (!s.Grid.InBounds(x, y)) return false;
                var t = s.Grid.Get(x, y).Type;
                if (st.Version < 7 && Cell.IsAirDock(t)) return true;
                return st.Version < 8 && t == CellType.Inlet && (x, y) != (FactoryGrid.InletX, FactoryGrid.InletY);
            }
            foreach (var c in st.Cells)
            {
                var type = ParseDefined<CellType>(c.Type);
                if (AirDockTile(c.X, c.Y))
                {
                    refund += BuildingCosts.Cost(type);
                    droppedParcels += c.Pallet?.Count ?? 0;
                    continue;
                }
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
                if (c.Mode != null)
                {
                    var mode = ParseDefined<RouteMode>(c.Mode);
                    if (mode != RouteMode.Inspect && type != CellType.Splitter && type != CellType.Scanner)
                        throw new SaveVersionException($"cell ({c.X},{c.Y}) of type {type} has mode {mode}");
                    cell.Mode = mode;
                }
                if (c.AirDirection != null)
                {
                    if (type != CellType.Scanner)
                        throw new SaveVersionException($"non-scanner cell ({c.X},{c.Y}) has an air exit");
                    cell.AirDirection = ParseDefined<Direction>(c.AirDirection);
                }
            }

            foreach (var p in st.Packages)
            {
                if (AirDockTile(p.X, p.Y)) { droppedParcels++; continue; }
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
            s.Sim.LostCount = st.LostCount + droppedParcels;
            if (refund > 0) s.Economy.Earn(refund);
            s.Spawner.Timer = st.SpawnTimer; // 음수·NaN이면 ArgumentOutOfRangeException → corrupt save
            if (st.InletTimers != null)
                foreach (var it in st.InletTimers)
                {
                    if (it == null) throw new SaveVersionException("null inlet timer");
                    s.Spawner.SetTimer(it.X, it.Y, it.Timer); // 입고구가 아니거나 잘못된 값이면 예외 → corrupt save
                }
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

            // 취급주의 시작일(스펙 17.1): 시계를 복원한 뒤에 넣어야 확률이 그날 기준으로 정해진다.
            bool plot2 = s.Unlocks.IsPurchased(UnlockId.Plot2);
            if (st.Version < 9) s.FragileFromDay = plot2 ? st.Day + 1 : 0; // 산 날을 모르므로 읽은 날의 다음 날부터
            else
            {
                if (plot2 ? st.FragileFromDay < 1 || st.FragileFromDay > st.Day + 1 : st.FragileFromDay != 0)
                    throw new SaveVersionException($"invalid FragileFromDay {st.FragileFromDay}");
                s.FragileFromDay = st.FragileFromDay;
            }
            // 트럭(스펙 18). 없으면 모두 빈 트럭. 잘못된 도크·범위 밖 값은 예외 → corrupt save.
            if (st.Trucks != null)
            {
                var seen = new HashSet<CellType>();
                foreach (var t in st.Trucks)
                {
                    if (t == null) throw new SaveVersionException("null truck");
                    var dock = ParseDefined<CellType>(t.Dock);
                    if (!seen.Add(dock)) throw new SaveVersionException($"duplicate truck {dock}");
                    if (Cell.IsAirDock(dock) && !plot2) throw new SaveVersionException($"air truck {dock} without Plot2");
                    s.Sim.Trucks.Restore(dock, t.Loaded, t.AwayMinutes);
                }
            }
            if (st.TrucksDepartedToday < 0) throw new SaveVersionException($"invalid TrucksDepartedToday {st.TrucksDepartedToday}");
            s.Sim.Trucks.DepartedToday = st.TrucksDepartedToday;
            return s;
        }

        private static PackageState ToState(Package p) => new PackageState
        {
            Id = p.Id, Kind = p.Kind.ToString(), X = p.X, Y = p.Y,
            Progress = p.Progress, Heading = p.Heading.ToString(), EntryHeading = p.EntryHeading.ToString(),
            Lane = p.Lane.ToString(),
            Bad = p.Bad, Inspected = p.Inspected, Rejected = p.Rejected, Air = p.Air, Waybill = p.Waybill,
            Fragile = p.Fragile, Broken = p.Broken, FragileSticker = p.FragileSticker,
            LabelDestination = p.Label?.Destination, LabelWeight = p.Label?.Weight ?? 0,
            LabelMethod = p.Label?.Method, LabelScale = p.Label?.Scale ?? 0,
        };

        private static Package FromState(PackageState p)
        {
            if (p == null) throw new SaveVersionException("null package");
            if (float.IsNaN(p.Progress) || p.Progress < 0f || p.Progress > 1f)
                throw new SaveVersionException($"package {p.Id} invalid progress {p.Progress}");
            if (p.Id < 0) throw new SaveVersionException($"package id {p.Id} < 0");
            if ((p.Broken || p.FragileSticker) && !p.Fragile) throw new SaveVersionException($"package {p.Id} is not fragile but broken/stickered");
            if (p.Broken && !p.Bad) throw new SaveVersionException($"package {p.Id} is broken but not bad");
            return new Package
            {
                Id = p.Id,
                Kind = ParseDefined<PackageKind>(p.Kind),
                X = p.X, Y = p.Y,
                Progress = p.Progress,
                Heading = ParseDefined<Direction>(p.Heading),
                EntryHeading = ParseDefined<Direction>(p.EntryHeading),
                Lane = p.Lane == null ? Lane.Upper : ParseDefined<Lane>(p.Lane),
                Bad = p.Bad, Inspected = p.Inspected, Rejected = p.Rejected, Air = p.Air, Waybill = p.Waybill,
                Fragile = p.Fragile, Broken = p.Broken, FragileSticker = p.FragileSticker,
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
