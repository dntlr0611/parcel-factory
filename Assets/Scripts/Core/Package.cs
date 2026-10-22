namespace ParcelFactory.Core
{
    public enum PackageKind { Normal }

    /// <summary>브릿지 위 차선(스펙 11.12). 브릿지가 아닌 칸·팔레트·손에 든 택배는 항상 Upper.</summary>
    public enum Lane { Upper, Lower }

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
        /// <summary>브릿지 위에서 타고 있는 차선. 브릿지를 벗어나면 Upper로 돌아간다.</summary>
        public Lane Lane;

        /// <summary>실제 불량 여부. 겉으로는 알 수 없고 검사로만 드러난다.</summary>
        public bool Bad;
        public bool Inspected;
        /// <summary>검사 결과 거부됨. Inspected가 true일 때만 의미 있다.</summary>
        public bool Rejected;
        /// <summary>항공 택배(스펙 14.2). 겉 라벨이라 검사 전에도 보인다. 항공 트럭으로만 보내야 한다.</summary>
        public bool Air;
        /// <summary>운송장이 붙었는가(스펙 15). 부착대에서 작업자가 붙이거나 자동 검사기가 붙인다.</summary>
        public bool Waybill;
        /// <summary>취급주의 택배(스펙 17.1). 겉 표시라 검사 전에도 보인다. 항공과 따로 정한다.</summary>
        public bool Fragile;
        /// <summary>파손됨(스펙 17.2). 파손되면 Bad도 true가 되고 검사 결과가 지워진다. 운송장·스티커는 그대로.</summary>
        public bool Broken;
        /// <summary>취급주의 스티커가 붙었는가(스펙 17.3). 부착대에서 작업자가 붙이거나 자동 검사기가 붙인다.</summary>
        public bool FragileSticker;
        public PackageLabel Label;

        public void Inspect(bool reject)
        {
            Inspected = true;
            Rejected = reject;
        }
    }
}
