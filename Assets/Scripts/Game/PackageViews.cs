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

        public static float SurfaceHeight(CellType t)
        {
            switch (t)
            {
                case CellType.Table: return 0.6f;
                case CellType.Inlet: return 0.22f;
                default: return 0.4f;
            }
        }

        public static GameObject MakeBox(ModelCatalog cat, Transform parent)
        {
            var go = new GameObject("Package");
            go.transform.SetParent(parent, false);
            var m = Object.Instantiate(cat.Get("box-small"), go.transform);
            m.transform.localScale = Vector3.one * 0.8f;
            var stamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(stamp.GetComponent<Collider>());
            stamp.name = "Stamp";
            stamp.transform.SetParent(go.transform, false);
            stamp.transform.localPosition = new Vector3(0.1f, 0.46f, 0.08f);
            stamp.transform.localScale = new Vector3(0.14f, 0.02f, 0.14f);
            stamp.GetComponent<Renderer>().material = MaterialLibrary.Emissive(new Color(0.85f, 0.2f, 0.15f), 1.2f);
            stamp.SetActive(false);
            return go;
        }

        public static void SetStamp(GameObject box, bool on)
        {
            var s = box.transform.Find("Stamp");
            if (s != null) s.gameObject.SetActive(on);
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
                pos.y = SurfaceHeight(boot.Session.Grid.Get(p.X, p.Y).Type);
                go.transform.position = pos;
                go.transform.rotation = Grid3D.Facing(dir);
                SetStamp(go, p.Inspected && p.Rejected);
            }
            var gone = new List<int>();
            foreach (var kv in active) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { active[id].SetActive(false); pool.Push(active[id]); active.Remove(id); }
        }
    }
}
