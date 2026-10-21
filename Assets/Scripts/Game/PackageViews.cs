using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>상자 뷰의 윗면 표시 참조(만들 때 한 번 모아 둔다). 매 프레임 Find·GetComponent·재질 조회 없이 켜고 끈다.</summary>
    public sealed class BoxMarks : MonoBehaviour
    {
        public GameObject Stamp, Check, AirMark, Waybill;
        public Renderer WaybillRenderer;
        private int band = -1; // 운송장 머리띠: -1 미정, 0 출고, 1 반품

        /// <summary>검사 표시 + 항공 마크(겉 라벨, 스펙 14.2) + 운송장(거부면 반품 빨강 머리띠, 스펙 15.3).</summary>
        public void Set(Package p)
        {
            Set(p.Inspected, p.Rejected);
            On(AirMark, p.Air);
            On(Waybill, p.Waybill);
            if (!p.Waybill) return;
            int b = p.Rejected ? 1 : 0;
            if (b == band) return; // 판정이 바뀔 때만 재질을 바꾼다
            band = b;
            WaybillRenderer.sharedMaterial = MaterialLibrary.Waybill(p.Rejected);
        }

        public void Set(bool inspected, bool rejected)
        {
            On(Stamp, inspected && rejected);
            On(Check, inspected && !rejected);
        }

        private static void On(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) g.SetActive(on);
        }
    }

    /// <summary>매 프레임 Sim.Packages를 읽어 상자를 그린다. 들고 있는 택배는 WorkerView가 그린다.</summary>
    public sealed class PackageViews : MonoBehaviour
    {
        private readonly Dictionary<int, BoxMarks> active = new Dictionary<int, BoxMarks>();
        private readonly Stack<BoxMarks> pool = new Stack<BoxMarks>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private Transform root;

        /// <summary>택배 id의 상자 오브젝트(지금 그리는 중이 아니면 null). 테스트용.</summary>
        public GameObject ViewOf(int id) => active.TryGetValue(id, out var box) ? box.gameObject : null;

        public static float SurfaceHeight(CellType t)
        {
            switch (t)
            {
                case CellType.Table:
                case CellType.Labeler: return 0.6f; // 부착대는 검사대와 같은 작업대(스펙 15.1)
                case CellType.Inlet:
                case CellType.Outlet:
                case CellType.ReturnDock:
                case CellType.AirOutlet:
                case CellType.AirReturnDock: return 0.22f; // 도크 슬래브 윗면
                case CellType.Pallet: return 0.12f;     // 나무 팔레트 윗면(더미 1층 바닥)
                default: return 0.4f;
            }
        }

        /// <summary>팔레트 더미 한 층의 높이. 상자는 층당 2개(x ±0.22), 용량 6 = 3층.</summary>
        public const float PalletLayerHeight = 0.4f;
        public const int PalletPerLayer = 2;

        /// <summary>브릿지 고가 벨트 바닥 높이와 윗길 택배 높이(스펙 11.12). 아랫길은 레일과 같은 0.4.</summary>
        public const float BridgeDeckHeight = 0.9f, BridgeUpperHeight = 1.3f;

        /// <summary>칸 위 택배가 놓일 높이. 브릿지는 차선에 따라 윗길 1.3, 아랫길 0.4.</summary>
        public static float PackageHeight(Cell c, Lane lane) =>
            c.Type == CellType.Bridge && lane == Lane.Upper ? BridgeUpperHeight : SurfaceHeight(c.Type);

        /// <summary>칸 위 표시물(커서 등)이 놓일 높이. 팔레트는 더미 꼭대기, 브릿지는 고가 벨트 윗면, 바닥 칸은 0.</summary>
        public static float TopHeight(Cell c)
        {
            if (c.Type == CellType.Bridge) return BridgeUpperHeight;
            if (c.Type == CellType.Pallet)
                return SurfaceHeight(CellType.Pallet) + PalletLayerHeight * ((c.Pallet.Count + PalletPerLayer - 1) / PalletPerLayer);
            return c.IsBuilding || c.IsDock || c.Type == CellType.Inlet ? SurfaceHeight(c.Type) : 0f;
        }

        /// <summary>검사 통과 표시(초록) 색. 거부 도장(빨강)과 같은 자리에 놓이고 둘 중 하나만 보인다.</summary>
        public static readonly Color CheckColor = new Color(0x3F / 255f, 0xA3 / 255f, 0x5A / 255f);
        public static readonly Color StampColor = new Color(0.85f, 0.2f, 0.15f);

        public static GameObject MakeBox(ModelCatalog cat, Transform parent)
        {
            var go = new GameObject("Package");
            go.transform.SetParent(parent, false);
            var m = Object.Instantiate(cat.Get("box-small"), go.transform);
            m.transform.localScale = Vector3.one * 0.8f;
            var b = LocalBounds(go.transform, m);
            var marks = go.AddComponent<BoxMarks>();
            marks.Waybill = WaybillQuad(go.transform, b);
            marks.WaybillRenderer = marks.Waybill.GetComponent<Renderer>();
            marks.AirMark = AirMark(go.transform, b);
            marks.Stamp = Mark(go.transform, "Stamp", StampColor);
            marks.Check = Mark(go.transform, "Check", CheckColor);
            return go;
        }

        /// <summary>항공 마크가 상자 윗면 폭에서 차지하는 비율. 스펙 15.3으로 윗면 모서리로 옮기면서 14.2의 55%에서 줄였다.</summary>
        public const float AirMarkFraction = 0.45f;
        /// <summary>운송장 쿼드 크기(윗면 폭 대비 가로·세로). 가운데에 놓이고, 모서리의 항공 마크가 운송장 귀퉁이에 살짝 걸치면 마크가 위에 보인다.</summary>
        public const float WaybillWidthFraction = 0.5f, WaybillDepthFraction = 0.32f;

        /// <summary>
        /// 상자 윗면 모서리(로컬 −x, +z)의 항공 마크(Resources/UI/Icons/mark-air: 파란 둥근 사각 + 흰 비행기 + 흰 테두리, 스펙 14.2·15.3).
        /// 가운데 운송장보다 살짝 위, 검사 도장·체크(두께 0.02)보다 아래. 꺼진 채로 만든다.
        /// </summary>
        private static GameObject AirMark(Transform box, Bounds b)
        {
            float w = Mathf.Min(b.size.x, b.size.z);
            float side = w * AirMarkFraction, inset = w * 0.03f;
            var pos = new Vector3(b.min.x + inset + side / 2f, b.max.y + 0.012f, b.max.z - inset - side / 2f); // 운송장(+0.006) 위, z-파이팅 방지
            return TopQuad(box, "AirMark", pos, new Vector2(side, side), MaterialLibrary.Decal(Resources.Load<Texture2D>("UI/Icons/mark-air")));
        }

        /// <summary>상자 윗면 가운데의 운송장(스펙 15.3): 흰 종이 + 머리띠(출고 파랑 / 반품 빨강) + 바코드 줄. 꺼진 채로 만든다.</summary>
        private static GameObject WaybillQuad(Transform box, Bounds b)
        {
            float w = Mathf.Min(b.size.x, b.size.z);
            var pos = new Vector3(b.center.x, b.max.y + 0.006f, b.center.z);
            return TopQuad(box, "Waybill", pos, new Vector2(w * WaybillWidthFraction, w * WaybillDepthFraction), MaterialLibrary.Waybill(false));
        }

        private static GameObject TopQuad(Transform box, string name, Vector3 localPos, Vector2 size, Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(box, false);
            q.transform.localPosition = localPos;
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 윗면(+y)을 본다. 텍스처 위쪽 = 로컬 +z
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.SetActive(false);
            return q;
        }

        /// <summary>model 메시 경계를 box 로컬 좌표로.</summary>
        private static Bounds LocalBounds(Transform box, GameObject model)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = box.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                    else b.Encapsulate(lp);
                }
            }
            return any ? b : new Bounds(new Vector3(0f, 0.46f, 0f), new Vector3(0.5f, 0f, 0.5f));
        }

        /// <summary>상자 윗면의 납작한 표시. 꺼진 채로 만든다.</summary>
        private static GameObject Mark(Transform box, string name, Color color)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(mark.GetComponent<Collider>());
            mark.name = name;
            mark.transform.SetParent(box, false);
            mark.transform.localPosition = new Vector3(0.1f, 0.46f, 0.08f);
            mark.transform.localScale = new Vector3(0.14f, 0.02f, 0.14f);
            mark.GetComponent<Renderer>().material = MaterialLibrary.Emissive(color, 1.2f);
            mark.SetActive(false);
            return mark;
        }

        /// <summary>검사 상태 표시: 거부되면 빨간 도장, 검사를 통과하면 초록 체크, 미검사면 둘 다 없음.</summary>
        public static void SetMarks(GameObject box, bool inspected, bool rejected) => box.GetComponent<BoxMarks>().Set(inspected, rejected);

        /// <summary>표시 전체(BoxMarks.Set). 매 프레임 부르는 곳은 BoxMarks를 직접 들고 있는다.</summary>
        public static void SetMarks(GameObject box, Package p) => box.GetComponent<BoxMarks>().Set(p);

        private void LateUpdate()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            if (root == null) { root = new GameObject("Packages").transform; root.SetParent(boot.WorldRoot, false); }
            seen.Clear();
            foreach (var p in boot.Session.Sim.Packages)
            {
                seen.Add(p.Id);
                if (!active.TryGetValue(p.Id, out var box))
                {
                    box = pool.Count > 0 ? pool.Pop() : MakeBox(boot.Catalog, root).GetComponent<BoxMarks>();
                    box.gameObject.SetActive(true);
                    active[p.Id] = box;
                }
                var go = box.gameObject;
                var dir = p.Progress < 0.5f ? p.EntryHeading : p.Heading;
                float off = p.Progress - 0.5f;
                var pos = Grid3D.TileF(p.X + dir.Dx() * off, p.Y + dir.Dy() * off);
                pos.y = PackageHeight(boot.Session.Grid.Get(p.X, p.Y), p.Lane);
                go.transform.position = pos;
                go.transform.rotation = Grid3D.Facing(dir);
                box.Set(p);
            }
            var gone = new List<int>();
            foreach (var kv in active) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { active[id].gameObject.SetActive(false); pool.Push(active[id]); active.Remove(id); }
        }
    }
}
