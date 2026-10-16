using System;

namespace ParcelFactory.Core
{
    /// <summary>택배를 만든다. 난수를 주입받아 테스트에서 결정적으로 쓸 수 있다.</summary>
    public sealed class PackageFactory
    {
        public const double BadChance = 0.3;
        private static readonly string[] Destinations = { "서울", "부산", "대구", "광주", "인천" };
        private static readonly string[] Methods = { "일반", "익일", "당일" };

        private readonly Random rng;
        private readonly double badChance;

        public PackageFactory(Random rng, double badChance = BadChance)
        {
            this.rng = rng ?? new Random();
            this.badChance = badChance;
        }

        public Package Create(int id, int x, int y, Direction heading)
        {
            bool bad = rng.NextDouble() < badChance;
            int weight = 1 + rng.Next(8);
            return new Package
            {
                Id = id,
                Kind = PackageKind.Normal,
                X = x,
                Y = y,
                Progress = 0f,
                Heading = heading,
                EntryHeading = heading,
                Bad = bad,
                Label = new PackageLabel
                {
                    Destination = Destinations[rng.Next(Destinations.Length)],
                    Weight = weight,
                    Method = Methods[rng.Next(Methods.Length)],
                    Scale = bad ? weight + 3 + rng.Next(5) : weight,
                },
            };
        }
    }
}
