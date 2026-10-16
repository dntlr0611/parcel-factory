using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>벨트 위를 미끄러지는 화살표. 로컬 +z가 진행 방향.</summary>
    public sealed class BeltArrow : MonoBehaviour
    {
        public float Speed = 1f;
        private float t;
        private void Update()
        {
            var s = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            if (s == null || s.Paused) return;
            t = (t + Time.deltaTime * Speed * s.Speed) % 1f;
            transform.localPosition = new Vector3(0f, 0.43f, Mathf.Lerp(-0.32f, 0.32f, t));
        }
    }

    public sealed class CellViews : MonoBehaviour
    {
        private readonly Dictionary<(int, int), GameObject> objs = new Dictionary<(int, int), GameObject>();
        private readonly Dictionary<(int, int), Transform> demoBars = new Dictionary<(int, int), Transform>();
        private GameSession bound;
        private ModelCatalog cat;
        private Transform root;

        public void Bind(GameSession s)
        {
            cat = GameBootstrap.Instance.Catalog;
            if (root == null) { root = new GameObject("Cells").transform; root.SetParent(GameBootstrap.Instance.WorldRoot, false); }
            if (bound != null) { bound.Build.CellChanged -= Rebuild; bound.Unlocks.Unlocked -= OnUnlocked; }
            bound = s;
            bound.Build.CellChanged += Rebuild;
            bound.Unlocks.Unlocked += OnUnlocked;
            RebuildAll();
        }

        private void OnUnlocked(UnlockId _) => RebuildAll();

        public void RebuildAll()
        {
            for (int y = 0; y < FactoryGrid.Height; y++) for (int x = 0; x < FactoryGrid.Width; x++) Rebuild(x, y);
        }

        public void Rebuild(int x, int y)
        {
            if (objs.TryGetValue((x, y), out var old)) { Destroy(old); objs.Remove((x, y)); }
            var c = bound.Grid.Get(x, y);
            var go = Make(c);
            if (go == null) return;
            go.transform.SetParent(root, false);
            go.transform.position = Grid3D.Tile(x, y);
            objs[(x, y)] = go;
        }

        private GameObject Model(string key, Transform parent, Vector3 localPos, Quaternion localRot, float scale)
        {
            var m = Instantiate(cat.Get(key), parent);
            m.transform.localPosition = localPos;
            m.transform.localRotation = localRot;
            m.transform.localScale = Vector3.one * scale;
            return m;
        }

        private GameObject Make(Cell c)
        {
            if (!c.IsBuilding) return null;
            var go = new GameObject(c.Type.ToString());
            var facing = Grid3D.Facing(c.Direction);
            switch (c.Type)
            {
                case CellType.Conveyor:
                case CellType.FastConveyor:
                case CellType.Scanner:
                {
                    Model("conveyor-stripe", go.transform, Vector3.zero, facing, 1f);
                    var arrowPivot = new GameObject("ArrowPivot").transform;
                    arrowPivot.SetParent(go.transform, false);
                    arrowPivot.localRotation = facing;
                    var arrow = Model("arrow", arrowPivot, Vector3.zero, Quaternion.identity, 0.3f);
                    var ba = arrow.AddComponent<BeltArrow>();
                    ba.Speed = c.Type == CellType.FastConveyor ? 2f : 1f;
                    if (c.Type == CellType.FastConveyor)
                    {
                        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        stripe.transform.SetParent(go.transform, false);
                        stripe.transform.localRotation = facing;
                        stripe.transform.localPosition = new Vector3(0f, 0.41f, 0f);
                        stripe.transform.localScale = new Vector3(0.9f, 0.02f, 0.08f);
                        stripe.GetComponent<Renderer>().material = MaterialLibrary.Emissive(new Color(1f, 0.3f, 0.25f), 2f);
                        Destroy(stripe.GetComponent<Collider>());
                    }
                    if (c.Type == CellType.Scanner)
                    {
                        Model("scanner-low", go.transform, new Vector3(0f, 0.02f, 0f), facing * Quaternion.Euler(0f, 90f, 0f), 0.58f);
                        var laser = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        laser.transform.SetParent(go.transform, false);
                        laser.transform.localRotation = facing;
                        laser.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                        laser.transform.localScale = new Vector3(0.8f, 0.5f, 0.02f);
                        laser.GetComponent<Renderer>().material = MaterialLibrary.Transparent(new Color(1f, 0.2f, 0.2f, 0.45f));
                        Destroy(laser.GetComponent<Collider>());
                    }
                    break;
                }
                case CellType.Splitter:
                    Model("conveyor-junction-t", go.transform, Vector3.zero, facing, 1f);
                    break;
                case CellType.Table:
                    Model("floor", go.transform, Vector3.zero, Quaternion.identity, 1f);
                    Model("screen-panel-flat", go.transform, Vector3.zero, facing, 0.8f);
                    break;
            }
            return go;
        }

        private void LateUpdate()
        {
            if (bound == null) return;
            for (int y = 0; y < FactoryGrid.Height; y++)
            {
                for (int x = 0; x < FactoryGrid.Width; x++)
                {
                    var c = bound.Grid.Get(x, y);
                    bool has = demoBars.TryGetValue((x, y), out var bar);
                    if (c.Demolishing)
                    {
                        if (!has)
                        {
                            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            Destroy(cube.GetComponent<Collider>());
                            cube.transform.SetParent(root, false);
                            cube.transform.position = Grid3D.Tile(x, y) + new Vector3(0f, 1.15f, 0f);
                            cube.GetComponent<Renderer>().material = MaterialLibrary.Emissive(new Color(1f, 0.78f, 0.3f), 1.2f);
                            bar = cube.transform; demoBars[(x, y)] = bar;
                        }
                        bar.localScale = new Vector3(0.8f * Mathf.Max(0.02f, 1f - c.DemolishRemaining / BuildService.DemolishSeconds), 0.05f, 0.12f);
                    }
                    else if (has) { Destroy(bar.gameObject); demoBars.Remove((x, y)); }
                }
            }
        }
    }
}
