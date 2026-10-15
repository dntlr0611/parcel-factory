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
        public const int CurrentVersion = 1;

        public static GameState Capture(GameSession s)
        {
            var st = new GameState
            {
                Version = CurrentVersion,
                Money = s.Economy.Money,
                UnlockedSize = s.Grid.UnlockedSize,
                SpawnTimer = s.Spawner.Timer,
                NextPackageId = s.Sim.NextId,
                Paused = s.Paused,
                Speed = s.Speed,
            };

            foreach (var id in s.Unlocks.Purchased)
                st.Purchased.Add(id.ToString());

            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = s.Grid.Get(x, y);
                    if (!c.IsBuilding) continue;
                    st.Cells.Add(new CellState
                    {
                        X = x, Y = y,
                        Type = c.Type.ToString(),
                        Direction = c.Direction.ToString(),
                        Demolishing = c.Demolishing,
                        DemolishRemaining = c.DemolishRemaining,
                        SplitterToggle = c.SplitterToggle,
                    });
                }
            }

            foreach (var p in s.Sim.Packages)
            {
                st.Packages.Add(new PackageState
                {
                    Id = p.Id, Kind = p.Kind.ToString(), X = p.X, Y = p.Y,
                    Progress = p.Progress, Heading = p.Heading.ToString(),
                    EntryHeading = p.EntryHeading.ToString(),
                });
            }
            return st;
        }

        public static GameSession Restore(GameState st)
        {
            if (st == null) throw new SaveVersionException("empty save");
            if (st.Version != CurrentVersion)
                throw new SaveVersionException($"save version {st.Version} != {CurrentVersion}");
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
                cell.SplitterToggle = c.SplitterToggle;
            }

            foreach (var p in st.Packages)
            {
                if (!s.Grid.InBounds(p.X, p.Y))
                    throw new SaveVersionException($"package {p.Id} out of bounds ({p.X},{p.Y})");
                var target = s.Grid.Get(p.X, p.Y);
                if (!target.IsTrack || target.IsOccupied)
                    throw new SaveVersionException($"package {p.Id} on invalid cell ({p.X},{p.Y})");
                if (float.IsNaN(p.Progress) || p.Progress < 0f || p.Progress > 1f)
                    throw new SaveVersionException($"package {p.Id} invalid progress {p.Progress}");
                s.Sim.Restore(new Package
                {
                    Id = p.Id,
                    Kind = ParseDefined<PackageKind>(p.Kind),
                    X = p.X, Y = p.Y,
                    Progress = p.Progress,
                    Heading = ParseDefined<Direction>(p.Heading),
                    EntryHeading = ParseDefined<Direction>(p.EntryHeading),
                });
                if (st.NextPackageId <= p.Id)
                    throw new SaveVersionException($"NextPackageId {st.NextPackageId} <= package id {p.Id}");
            }

            s.Sim.NextId = st.NextPackageId;
            s.Spawner.Timer = st.SpawnTimer;
            s.Paused = st.Paused;
            s.Speed = st.Speed;
            return s;
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
