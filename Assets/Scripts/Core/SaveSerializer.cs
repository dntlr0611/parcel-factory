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
                });
            }
            return st;
        }

        public static GameSession Restore(GameState st)
        {
            if (st == null) throw new SaveVersionException("empty save");
            if (st.Version != CurrentVersion)
                throw new SaveVersionException($"save version {st.Version} != {CurrentVersion}");

            var s = new GameSession(new Economy(st.Money));

            // Plot2가 Plot1보다 먼저 적용되면 UnlockRegion이 축소를 거부하므로 enum 순서로 적용한다.
            var ids = new List<UnlockId>();
            foreach (var name in st.Purchased)
                ids.Add((UnlockId)Enum.Parse(typeof(UnlockId), name));
            ids.Sort();
            foreach (var id in ids)
                s.Unlocks.Grant(id);

            foreach (var c in st.Cells)
            {
                var type = (CellType)Enum.Parse(typeof(CellType), c.Type);
                var dir = (Direction)Enum.Parse(typeof(Direction), c.Direction);
                s.Grid.Place(c.X, c.Y, type, dir);
                var cell = s.Grid.Get(c.X, c.Y);
                cell.Demolishing = c.Demolishing;
                cell.DemolishRemaining = c.DemolishRemaining;
                cell.SplitterToggle = c.SplitterToggle;
            }

            foreach (var p in st.Packages)
            {
                s.Sim.Restore(new Package
                {
                    Id = p.Id,
                    Kind = (PackageKind)Enum.Parse(typeof(PackageKind), p.Kind),
                    X = p.X, Y = p.Y,
                    Progress = p.Progress,
                    Heading = (Direction)Enum.Parse(typeof(Direction), p.Heading),
                });
            }

            s.Sim.NextId = st.NextPackageId;
            s.Spawner.Timer = st.SpawnTimer;
            s.Paused = st.Paused;
            s.Speed = st.Speed;
            return s;
        }

        public static string ToJson(GameState st) => JsonConvert.SerializeObject(st, Formatting.Indented);

        public static GameState FromJson(string json)
        {
            GameState st;
            try
            {
                st = JsonConvert.DeserializeObject<GameState>(json);
            }
            catch (JsonException e)
            {
                throw new SaveVersionException("invalid save json", e);
            }
            if (st == null) throw new SaveVersionException("empty save");
            return st;
        }
    }
}
