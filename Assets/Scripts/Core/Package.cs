namespace ParcelFactory.Core
{
    public enum PackageKind { Normal }

    /// <summary>택배 라벨. 불량이면 저울 실측(Scale)이 표기 무게(Weight)와 다르다.</summary>
    public sealed class PackageLabel
    {
        public string Destination;
        public int Weight;
        public string Method;
        public int Scale;

        public bool Mismatch => Scale != Weight;
    }

    public sealed class Package
    {
        public int Id;
        public PackageKind Kind;
        public int X;
        public int Y;
        /// <summary>현재 셀 안에서의 진행도 0~1.</summary>
        public float Progress;
        /// <summary>현재 셀에서 나갈 방향.</summary>
        public Direction Heading;
        /// <summary>현재 셀에 들어올 때의 이동 방향. 렌더링에서 코너 보간에 쓴다.</summary>
        public Direction EntryHeading;

        /// <summary>실제 불량 여부. 겉으로는 알 수 없고 검사로만 드러난다.</summary>
        public bool Bad;
        public bool Inspected;
        /// <summary>검사 결과 거부됨. Inspected가 true일 때만 의미 있다.</summary>
        public bool Rejected;
        public PackageLabel Label;

        public void Inspect(bool reject)
        {
            Inspected = true;
            Rejected = reject;
        }
    }
}
