using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>매 프레임 Sim.Packages를 읽어 상자를 그린다. 들고 있는 택배는 WorkerView가 그린다.</summary>
    public sealed class PackageViews : MonoBehaviour
    {
        private readonly Dictionary<int, GameObject> active = new Dictionary<int, GameObject>();
        private readonly Stack<GameObject> pool = new Stack<GameObject>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private Transform root;

        /// <summary>택배 id의 상자 오브젝트(지금 그리는 중이 아니면 null). 테스트용.</summary>
        public GameObject ViewOf(int id) => active.TryGetValue(id, out var go) ? go : null;

        public static float SurfaceHeight(CellType t)
        {
            switch (t)
            {
                case CellType.Table: return 0.6f;
                case CellType.Inlet:
                case CellType.Outlet:
                case CellType.ReturnDock: return 0.22f; // 도크 슬래브 윗면
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
            Mark(go.transform, "Stamp", StampColor);
            Mark(go.transform, "Check", CheckColor);
            return go;
        }

        /// <summary>상자 윗면의 납작한 표시. 꺼진 채로 만든다.</summary>
        private static void Mark(Transform box, string name, Color color)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(mark.GetComponent<Collider>());
            mark.name = name;
            mark.transform.SetParent(box, false);
            mark.transform.localPosition = new Vector3(0.1f, 0.46f, 0.08f);
            mark.transform.localScale = new Vector3(0.14f, 0.02f, 0.14f);
            mark.GetComponent<Renderer>().material = MaterialLibrary.Emissive(color, 1.2f);
            mark.SetActive(false);
        }

        /// <summary>검사 상태 표시: 거부되면 빨간 도장, 검사를 통과하면 초록 체크, 미검사면 둘 다 없음.</summary>
        public static void SetMarks(GameObject box, bool inspected, bool rejected)
        {
            Toggle(box, "Stamp", inspected && rejected);
            Toggle(box, "Check", inspected && !rejected);
        }

        public static void SetMarks(GameObject box, Package p) => SetMarks(box, p.Inspected, p.Rejected);

        private static void Toggle(GameObject box, string name, bool on)
        {
            var t = box.transform.Find(name);
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        private void LateUpdate()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            if (root == null) { root = new GameObject("Packages").transform; root.SetParent(boot.WorldRoot, false); }
            seen.Clear();
            foreach (var p in boot.Session.Sim.Packages)
            {
                seen.Add(p.Id);
                if (!active.TryGetValue(p.Id, out var go))
                {
                    go = pool.Count > 0 ? pool.Pop() : MakeBox(boot.Catalog, root);
                    go.SetActive(true);
                    active[p.Id] = go;
                }
                var dir = p.Progress < 0.5f ? p.EntryHeading : p.Heading;
                float off = p.Progress - 0.5f;
                var pos = Grid3D.TileF(p.X + dir.Dx() * off, p.Y + dir.Dy() * off);
                pos.y = PackageHeight(boot.Session.Grid.Get(p.X, p.Y), p.Lane);
                go.transform.position = pos;
                go.transform.rotation = Grid3D.Facing(dir);
                SetMarks(go, p);
            }
            var gone = new List<int>();
            foreach (var kv in active) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { active[id].SetActive(false); pool.Push(active[id]); active.Remove(id); }
        }
    }
}
