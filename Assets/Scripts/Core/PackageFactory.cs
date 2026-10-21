using System;

namespace ParcelFactory.Core
{
    /// <summary>택배를 만든다. 난수를 주입받아 테스트에서 결정적으로 쓸 수 있다.</summary>
    public sealed class PackageFactory
    {
        public const double BadChance = 0.3;
        /// <summary>부지 확장 2(12×12) 이후 항공 택배 비율(스펙 14.2). GameSession이 AirChance에 넣는다.</summary>
        public const double AirChanceAfterPlot2 = 0.25;
        /// <summary>항공 택배의 라벨 배송 방법.</summary>
        public const string AirMethod = "항공";
        private static readonly string[] Destinations = { "서울", "부산", "대구", "광주", "인천" };
        private static readonly string[] Methods = { "일반", "익일", "당일" };

        private readonly Random rng;
        private readonly double badChance;

        /// <summary>새 택배가 항공일 확률. 0이면 항공 추첨을 하지 않아 난수 순서가 이전과 같다.</summary>
        public double AirChance { get; set; }

        public PackageFactory(Random rng, double badChance = BadChance, double airChance = 0.0)
        {
            this.rng = rng ?? new Random();
            this.badChance = badChance;
            AirChance = airChance;
        }

        public Package Create(int id, int x, int y, Direction heading)
        {
            bool bad = rng.NextDouble() < badChance;
            int weight = 1 + rng.Next(8);
            var label = new PackageLabel
            {
                Destination = Destinations[rng.Next(Destinations.Length)],
                Weight = weight,
                Method = Methods[rng.Next(Methods.Length)],
                Scale = bad ? weight + 3 + rng.Next(5) : weight,
            };
            // 항공 추첨은 맨 마지막: 같은 시드에서 다른 필드가 바뀌지 않는다.
            bool air = AirChance > 0.0 && rng.NextDouble() < AirChance;
            if (air) label.Method = AirMethod;
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
                Air = air,
                Label = label,
            };
        }
    }
}
