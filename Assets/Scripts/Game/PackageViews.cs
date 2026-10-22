using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>상자 뷰의 윗면 표시 참조(만들 때 한 번 모아 둔다). 매 프레임 Find·GetComponent·재질 조회 없이 켜고 끈다.</summary>
    public sealed class BoxMarks : MonoBehaviour
    {
        public GameObject Stamp, Check, AirMark, Waybill;
        /// <summary>취급주의(스펙 17.1): 옆면 네 곳의 흐린 인쇄(스티커 전) / 선명한 스티커(부착 후), 파손 시 윗면 금 무늬(17.2).</summary>
        public GameObject FragilePrint, FragileSticker, Crack;

        /// <summary>검사 표시(거부 도장·승인 체크) + 항공 마크(겉 라벨, 스펙 14.2) + 운송장(한 종류, 스펙 15.4) + 취급주의 옆면·금 무늬(스펙 17).</summary>
        public void Set(Package p)
        {
            Set(p.Inspected, p.Rejected);
            On(AirMark, p.Air);
            On(Waybill, p.Waybill);
            On(FragilePrint, p.Fragile && !p.FragileSticker);
            On(FragileSticker, p.Fragile && p.FragileSticker);
            On(Crack, p.Broken);
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

    /// <summary>
    /// 취급주의 택배 파손 효과(스펙 17.2): 골판지 조각 10개가 위·바깥으로 튀었다가 떨어지며 작아진다(0.7초, 일시정지와 무관하게 실제 시간).
    /// 끝나면 스스로 지운다.
    /// </summary>
    public sealed class BreakFx : MonoBehaviour
    {
        public const float Seconds = 0.7f;
        private const int Count = 10;
        private readonly Transform[] shards = new Transform[Count];
        private readonly Vector3[] velocity = new Vector3[Count], spin = new Vector3[Count];
        private float age;

        public static BreakFx Spawn(Transform parent, Vector3 world)
        {
            var go = new GameObject("BreakFx");
            go.transform.SetParent(parent, false);
            go.transform.position = world;
            return go.AddComponent<BreakFx>();
        }

        private void Start()
        {
            var rng = new System.Random(GetInstanceID());
            var cardboard = MaterialLibrary.Lit(new Color(0xC8 / 255f, 0x9B / 255f, 0x6A / 255f), 0.1f);
            var orange = MaterialLibrary.Lit(MaterialLibrary.FragileOrange, 0.2f);
            for (int i = 0; i < Count; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.GetComponent<Collider>());
                c.name = "Shard";
                c.transform.SetParent(transform, false);
                float sz = 0.05f + 0.04f * (float)rng.NextDouble();
                c.transform.localScale = new Vector3(sz, sz * 0.35f, sz * 0.8f);
                c.GetComponent<Renderer>().sharedMaterial = i % 4 == 0 ? orange : cardboard;
                float a = (i + (float)rng.NextDouble() * 0.6f) / Count * Mathf.PI * 2f;
                velocity[i] = new Vector3(Mathf.Cos(a) * 1.6f, 2.2f + 1.2f * (float)rng.NextDouble(), Mathf.Sin(a) * 1.6f);
                spin[i] = new Vector3(rng.Next(-720, 720), rng.Next(-720, 720), rng.Next(-720, 720));
                shards[i] = c.transform;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            age += dt;
            float k = Mathf.Clamp01(1f - age / Seconds);
            for (int i = 0; i < Count; i++)
            {
                if (shards[i] == null) continue;
                velocity[i].y -= 9f * dt;
                shards[i].localPosition += velocity[i] * dt;
                shards[i].localRotation *= Quaternion.Euler(spin[i] * dt);
                shards[i].localScale *= Mathf.Lerp(1f, 0.96f, 1f - k);
            }
            if (age >= Seconds) Destroy(gameObject);
        }
    }

    /// <summary>매 프레임 Sim.Packages를 읽어 상자를 그린다. 들고 있는 택배는 WorkerView가 그린다. 파손되면 그 자리에 BreakFx를 띄운다.</summary>
    public sealed class PackageViews : MonoBehaviour
    {
        private readonly Dictionary<int, BoxMarks> active = new Dictionary<int, BoxMarks>();
        private readonly Stack<BoxMarks> pool = new Stack<BoxMarks>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private Transform root;
        private ConveyorSim boundSim;

        /// <summary>마지막 파손 효과(테스트용). 끝나면 null이 된다(Unity의 파괴된 오브젝트).</summary>
        public BreakFx LastBreakFx { get; private set; }

        private void Bind(ConveyorSim sim)
        {
            if (boundSim == sim) return;
            if (boundSim != null) boundSim.PackageBroken -= OnBroken;
            boundSim = sim;
            if (boundSim != null) boundSim.PackageBroken += OnBroken;
        }

        private void OnDestroy() => Bind(null);

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null) return;
            if (boot.Session != null) Bind(boot.Session.Sim);
            boot.SessionReplaced += s => Bind(s.Sim); // 같은 오브젝트의 컴포넌트라 함께 파괴된다
        }

        /// <summary>파손 순간 상자가 있는 곳: 손에 들었으면 작업자 손, 팔레트 더미면 그 층, 아니면 칸 위 택배 높이.</summary>
        private void OnBroken(Package p)
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            var s = boot.Session;
            Vector3 pos;
            if (s.Worker.Worker.Carrying == p && boot.Worker != null && boot.Worker.CarryAnchor != null) pos = boot.Worker.CarryAnchor.position;
            else
            {
                var c = s.Grid.Get(p.X, p.Y);
                pos = Grid3D.Tile(p.X, p.Y);
                int i = c.Type == CellType.Pallet ? c.Pallet.IndexOf(p) : -1;
                pos.y = i >= 0 ? SurfaceHeight(CellType.Pallet) + PalletLayerHeight * (i / PalletPerLayer) : PackageHeight(c, p.Lane);
            }
            LastBreakFx = BreakFx.Spawn(boot.WorldRoot, pos + new Vector3(0f, 0.3f, 0f));
        }

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
        public const int PalletPerLayer = Cell.PalletSlots;

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
            marks.AirMark = AirMark(go.transform, b);
            marks.Stamp = Mark(go.transform, b, "Stamp", StampColor);
            marks.Check = Mark(go.transform, b, "Check", CheckColor);
            marks.FragilePrint = SideMarks(go.transform, b, "FragilePrint", MaterialLibrary.FragilePrint());
            marks.FragileSticker = SideMarks(go.transform, b, "FragileSticker", MaterialLibrary.Decal(Resources.Load<Texture2D>("UI/Icons/mark-fragile")));
            marks.Crack = CrackQuad(go.transform, b);
            return go;
        }

        /// <summary>취급주의 스티커가 옆면 짧은 변(높이·폭 중 작은 것)에서 차지하는 비율.</summary>
        public const float FragileStickerFraction = 0.62f;

        /// <summary>
        /// 옆면 네 곳(±x, ±z)의 가운데에 붙인 같은 표시(스펙 17.1). 윗면은 항공 마크·운송장 자리라 쓰지 않는다. 묶음(name) 아래 Side0..3, 꺼진 채로 만든다.
        /// Quad 앞면(−z)이 바깥을 보도록 돌리고 면에서 0.004 띄운다. 텍스처 위쪽 = 월드 위.
        /// </summary>
        private static GameObject SideMarks(Transform box, Bounds b, string name, Material mat)
        {
            var group = new GameObject(name);
            group.transform.SetParent(box, false);
            float side = Mathf.Min(b.size.y, Mathf.Min(b.size.x, b.size.z)) * FragileStickerFraction;
            const float off = 0.004f;
            var faces = new[]
            {
                (new Vector3(b.max.x + off, b.center.y, b.center.z), Quaternion.Euler(0f, -90f, 0f)),
                (new Vector3(b.min.x - off, b.center.y, b.center.z), Quaternion.Euler(0f, 90f, 0f)),
                (new Vector3(b.center.x, b.center.y, b.max.z + off), Quaternion.Euler(0f, 180f, 0f)),
                (new Vector3(b.center.x, b.center.y, b.min.z - off), Quaternion.identity),
            };
            for (int i = 0; i < faces.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(q.GetComponent<Collider>());
                q.name = "Side" + i;
                q.transform.SetParent(group.transform, false);
                q.transform.localPosition = faces[i].Item1;
                q.transform.localRotation = faces[i].Item2;
                q.transform.localScale = new Vector3(side, side, 1f);
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            group.SetActive(false);
            return group;
        }

        /// <summary>파손 금 무늬(mark-crack, 스펙 17.2): 윗면 전체, 항공 마크(+0.006)와 운송장(+0.012) 사이(+0.009). 꺼진 채로 만든다.
        /// 선이 가늘어(128px에 2px) 멀리서 끊기지 않도록 알파 컷오프를 낮춰(0.2) 안티에일리어싱 가장자리까지 그린다.</summary>
        private static GameObject CrackQuad(Transform box, Bounds b)
        {
            float w = Mathf.Min(b.size.x, b.size.z) * AirMarkFraction;
            var pos = new Vector3(b.center.x, b.max.y + 0.009f, b.center.z);
            return TopQuad(box, "Crack", pos, new Vector2(w, w), MaterialLibrary.Decal(Resources.Load<Texture2D>("UI/Icons/mark-crack"), 0f, 0.2f));
        }

        /// <summary>항공 마크가 상자 윗면 폭에서 차지하는 비율(사용자 요청 2026-10-22: 윗면 전체에 보이게). 가장자리 3%만 골판지가 보인다.</summary>
        public const float AirMarkFraction = 0.94f;
        /// <summary>운송장 쿼드 크기(윗면 폭 대비 가로·세로). 가운데, 항공 마크 위에 붙는다.</summary>
        public const float WaybillWidthFraction = 0.5f, WaybillDepthFraction = 0.32f;

        /// <summary>
        /// 상자 윗면 전체를 덮는 항공 마크(Resources/UI/Icons/mark-air: 파란 둥근 사각 + 흰 비행기 + 흰 테두리, 스펙 14.2·15.3).
        /// 운송장은 그 위 가운데에 붙는다(마크 +0.006, 운송장 +0.012). 꺼진 채로 만든다.
        /// </summary>
        private static GameObject AirMark(Transform box, Bounds b)
        {
            float w = Mathf.Min(b.size.x, b.size.z);
            float side = w * AirMarkFraction;
            var pos = new Vector3(b.center.x, b.max.y + 0.006f, b.center.z); // 운송장(+0.012) 아래, z-파이팅 방지
            return TopQuad(box, "AirMark", pos, new Vector2(side, side), MaterialLibrary.Decal(Resources.Load<Texture2D>("UI/Icons/mark-air")));
        }

        /// <summary>상자 윗면 가운데의 운송장(스펙 15.3·15.4): 흰 종이 + 먹색 머리띠 + 바코드 줄(한 종류). 꺼진 채로 만든다.</summary>
        private static GameObject WaybillQuad(Transform box, Bounds b)
        {
            float w = Mathf.Min(b.size.x, b.size.z);
            var pos = new Vector3(b.center.x, b.max.y + 0.012f, b.center.z);
            return TopQuad(box, "Waybill", pos, new Vector2(w * WaybillWidthFraction, w * WaybillDepthFraction), MaterialLibrary.Waybill());
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
        /// <summary>검사 표시(두께 0.02)는 윗면 위에 얹어 운송장(+0.012)·항공 마크보다 높게 둔다. 반품 여부는 이 도장으로만 보인다(스펙 15.4).</summary>
        private static GameObject Mark(Transform box, Bounds b, string name, Color color)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(mark.GetComponent<Collider>());
            mark.name = name;
            mark.transform.SetParent(box, false);
            mark.transform.localPosition = new Vector3(0.1f, b.max.y + 0.01f, 0.08f);
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
            Bind(boot.Session.Sim);
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
