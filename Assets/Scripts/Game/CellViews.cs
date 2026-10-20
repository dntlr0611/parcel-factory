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

    /// <summary>벨트 흐름 표시용 납작한 화살표(셰브런) 메시. XZ 평면에 +z를 가리키고 윗면 법선은 +y.</summary>
    public static class ChevronMesh
    {
        private static Mesh mesh;

        // 7점 다각형 (x, z): 머리 삼각형 + 몸통 사각형으로 나눈다.
        private static readonly Vector2[] Shape =
        {
            new Vector2(0f, 0.16f), new Vector2(0.14f, -0.02f), new Vector2(0.05f, -0.02f), new Vector2(0.05f, -0.16f),
            new Vector2(-0.05f, -0.16f), new Vector2(-0.05f, -0.02f), new Vector2(-0.14f, -0.02f),
        };

        // 위(+y)에서 내려다볼 때 시계 방향 = Unity 앞면. 머리 (0,1,6), 몸통 (2,3,4)·(2,4,5).
        private static readonly int[] TopTris = { 0, 1, 6, 2, 3, 4, 2, 4, 5 };

        public static Mesh Get()
        {
            if (mesh != null) return mesh;
            int n = Shape.Length;
            var verts = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                verts[i] = verts[i + n] = new Vector3(Shape[i].x, 0f, Shape[i].y);
                normals[i] = Vector3.up;
                normals[i + n] = Vector3.down;
            }
            var tris = new int[TopTris.Length * 2];
            for (int i = 0; i < TopTris.Length; i += 3)
            {
                tris[i] = TopTris[i]; tris[i + 1] = TopTris[i + 1]; tris[i + 2] = TopTris[i + 2];
                // 뒷면(아래에서 볼 때)도 보이도록 반대 와인딩 복사본
                int j = TopTris.Length + i;
                tris[j] = TopTris[i] + n; tris[j + 1] = TopTris[i + 2] + n; tris[j + 2] = TopTris[i + 1] + n;
            }
            mesh = new Mesh { name = "Chevron", vertices = verts, normals = normals, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject Create(Transform parent)
        {
            var go = new GameObject("Chevron");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Get();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Emissive(new Color(1f, 0xD3 / 255f, 0x6A / 255f), 0.5f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }

    /// <summary>팔레트 칸의 더미를 매 프레임 Cell.Pallet에서 그린다(풀링). 층당 2개(x ±0.22), 최대 3층(y 0.12/0.52/0.92).
    /// 더미 택배는 Sim.Packages에 없다.</summary>
    public sealed class PalletView : MonoBehaviour
    {
        public static readonly Color Wood = new Color(0xB9 / 255f, 0x8A / 255f, 0x5C / 255f);
        public static readonly Color BlueBand = new Color(0x3D / 255f, 0x7B / 255f, 0xE0 / 255f);
        public static readonly Color RedBand = new Color(0xD9 / 255f, 0x5A / 255f, 0x4A / 255f);

        public Cell Cell;
        public ModelCatalog Catalog;
        private Transform pile;
        private readonly List<GameObject> boxes = new List<GameObject>();

        private void LateUpdate()
        {
            if (Cell == null) return;
            if (pile == null) { pile = new GameObject("Pile").transform; pile.SetParent(transform, false); }
            int n = Cell.Pallet.Count;
            while (boxes.Count < n) boxes.Add(PackageViews.MakeBox(Catalog, pile)); // 레일·운반 상자와 같은 크기(box-small 0.8배)
            for (int i = 0; i < boxes.Count; i++)
            {
                bool on = i < n;
                if (boxes[i].activeSelf != on) boxes[i].SetActive(on);
                if (!on) continue;
                int layer = i / PackageViews.PalletPerLayer, slot = i % PackageViews.PalletPerLayer;
                boxes[i].transform.localPosition = new Vector3(slot == 0 ? -0.22f : 0.22f, PackageViews.SurfaceHeight(CellType.Pallet) + PackageViews.PalletLayerHeight * layer, 0f);
                boxes[i].transform.localRotation = Quaternion.identity;
                var p = Cell.Pallet[i];
                PackageViews.SetMarks(boxes[i], p);
            }
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
            if (bound != null) { bound.Build.CellChanged -= OnCellChanged; bound.Unlocks.Unlocked -= OnUnlocked; }
            bound = s;
            bound.Build.CellChanged += OnCellChanged;
            bound.Unlocks.Unlocked += OnUnlocked;
            RebuildAll();
        }

        private void OnUnlocked(UnlockId _) => RebuildAll();

        /// <summary>칸이 바뀌면 그 칸과, 출구 화살표가 달라질 수 있는 이웃 분배기를 다시 그린다(스펙 11.6).</summary>
        private void OnCellChanged(int x, int y)
        {
            Rebuild(x, y);
            for (int d = 0; d < 4; d++)
            {
                var dir = (Direction)d;
                int nx = x + dir.Dx(), ny = y + dir.Dy();
                if (bound.Grid.InBounds(nx, ny) && bound.Grid.Get(nx, ny).Type == CellType.Splitter) Rebuild(nx, ny);
            }
        }

        public void RebuildAll()
        {
            for (int y = 0; y < FactoryGrid.Height; y++) for (int x = 0; x < FactoryGrid.Width; x++) Rebuild(x, y);
        }

        public void Rebuild(int x, int y)
        {
            if (objs.TryGetValue((x, y), out var old)) { Destroy(old); objs.Remove((x, y)); }
            var c = bound.Grid.Get(x, y);
            var go = Make(c, x, y);
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

        private static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos;
            b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = mat;
            return b;
        }

        public static readonly Color SplitterHub = new Color(0x7A / 255f, 0x5C / 255f, 0xC9 / 255f);

        private GameObject Make(Cell c, int x, int y)
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
                    var arrow = ChevronMesh.Create(arrowPivot);
                    arrow.transform.localPosition = new Vector3(0f, 0.43f, -0.32f);
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
                {
                    // 보라 허브 0.9×0.4×0.9(윗면 = 택배 표면 0.4) + 얇은 어두운 상판 + 연결된 출구마다 노란 화살표(스펙 11.6).
                    float top = PackageViews.SurfaceHeight(CellType.Splitter);
                    Box("SplitterHub", go.transform, new Vector3(0f, top / 2f, 0f), new Vector3(0.9f, top, 0.9f), MaterialLibrary.Lit(SplitterHub, 0.3f));
                    Box("SplitterPlate", go.transform, new Vector3(0f, top + 0.005f, 0f), new Vector3(0.72f, 0.01f, 0.72f), MaterialLibrary.Lit(new Color(0.16f, 0.14f, 0.2f), 0.2f));
                    foreach (var d in ConveyorSim.SplitterOutputs(bound.Grid, x, y, null))
                    {
                        var chevron = ChevronMesh.Create(go.transform);
                        chevron.transform.localRotation = Grid3D.Facing(d);
                        chevron.transform.localPosition = Grid3D.Forward(d) * 0.3f + new Vector3(0f, top + 0.03f, 0f);
                    }
                    break;
                }
                case CellType.Pallet:
                {
                    // 나무 팔레트 0.9×0.12×0.9 + 종류 색 테두리 띠(0.04). 색은 PalletKind에서만 읽는다(Direction 아님).
                    float top = PackageViews.SurfaceHeight(CellType.Pallet);
                    Box("PalletBase", go.transform, new Vector3(0f, top / 2f, 0f), new Vector3(0.9f, top, 0.9f), MaterialLibrary.Lit(PalletView.Wood, 0.1f));
                    var gapColor = PalletView.Wood * 0.7f; gapColor.a = 1f;
                    var slat = MaterialLibrary.Lit(gapColor, 0.05f);
                    foreach (var z in new[] { -0.15f, 0.15f })
                        Box("PalletGap", go.transform, new Vector3(0f, top + 0.002f, z), new Vector3(0.84f, 0.004f, 0.025f), slat);
                    var band = MaterialLibrary.Lit(c.PalletKind == PalletKind.Blue ? PalletView.BlueBand : PalletView.RedBand, 0.3f);
                    const float bh = 0.04f, bw = 0.05f, outer = 0.94f;
                    float by = top - bh / 2f + 0.005f;
                    Box("Band", go.transform, new Vector3(0f, by, (outer - bw) / 2f), new Vector3(outer, bh, bw), band);
                    Box("Band", go.transform, new Vector3(0f, by, -(outer - bw) / 2f), new Vector3(outer, bh, bw), band);
                    Box("Band", go.transform, new Vector3((outer - bw) / 2f, by, 0f), new Vector3(bw, bh, outer - 2f * bw), band);
                    Box("Band", go.transform, new Vector3(-(outer - bw) / 2f, by, 0f), new Vector3(bw, bh, outer - 2f * bw), band);
                    var pv = go.AddComponent<PalletView>();
                    pv.Cell = c;
                    pv.Catalog = cat;
                    break;
                }
                case CellType.Table:
                {
                    // 강철 작업대: 몸체 0.9×0.56×0.9 + 밝은 상판 0.96×0.04×0.96, 윗면 = 택배 표면 높이(0.6).
                    float top = PackageViews.SurfaceHeight(CellType.Table);
                    Box("TableBody", go.transform, new Vector3(0f, (top - 0.04f) / 2f, 0f), new Vector3(0.9f, top - 0.04f, 0.9f), MaterialLibrary.Lit(new Color(0x8E / 255f, 0x94 / 255f, 0x9B / 255f), 0.3f));
                    Box("TableTop", go.transform, new Vector3(0f, top - 0.02f, 0f), new Vector3(0.96f, 0.04f, 0.96f), MaterialLibrary.Lit(new Color(0xC9 / 255f, 0xCD / 255f, 0xD2 / 255f), 0.35f));
                    // 검사 화면: 상판 뒤쪽(북쪽) 가장자리에 카메라를 향해 세운다. 택배를 가리지 않도록 작게.
                    var panel = Model("screen-panel-flat", go.transform, new Vector3(0f, top, 0.3f), Quaternion.Euler(0f, 180f, 0f), 0.4f);
                    panel.name = "TableScreen";
                    break;
                }
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
