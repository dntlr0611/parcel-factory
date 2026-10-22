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

        public static GameObject Create(Transform parent, Material material = null, string name = "Chevron")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Get();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material != null ? material : MaterialLibrary.Emissive(new Color(1f, 0xD3 / 255f, 0x6A / 255f), 0.5f);
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
        private readonly List<BoxMarks> boxes = new List<BoxMarks>();

        private void LateUpdate()
        {
            if (Cell == null) return;
            if (pile == null) { pile = new GameObject("Pile").transform; pile.SetParent(transform, false); }
            int n = Cell.Pallet.Count;
            while (boxes.Count < n) boxes.Add(PackageViews.MakeBox(Catalog, pile).GetComponent<BoxMarks>()); // 레일·운반 상자와 같은 크기(box-small 0.8배)
            for (int i = 0; i < boxes.Count; i++)
            {
                bool on = i < n;
                if (boxes[i].gameObject.activeSelf != on) boxes[i].gameObject.SetActive(on);
                if (!on) continue;
                int layer = i / PackageViews.PalletPerLayer, slot = i % PackageViews.PalletPerLayer;
                boxes[i].transform.localPosition = new Vector3(slot == 0 ? -0.22f : 0.22f, PackageViews.SurfaceHeight(CellType.Pallet) + PackageViews.PalletLayerHeight * layer, 0f);
                boxes[i].transform.localRotation = Quaternion.identity;
                var p = Cell.Pallet[i];
                boxes[i].Set(p);
            }
        }
    }

    public sealed class CellViews : MonoBehaviour
    {
        private readonly Dictionary<(int, int), GameObject> objs = new Dictionary<(int, int), GameObject>();
        private readonly Dictionary<(int, int), Transform> demoBars = new Dictionary<(int, int), Transform>();
        private GameSession bound;
        /// <summary>지금 묶인 공장의 설치·해금(공장을 옮기면 바뀐다, 스펙 19.3). 구독 해제는 이 객체들에 한다.</summary>
        private BuildService boundBuild;
        private UnlockService boundUnlocks;
        private ModelCatalog cat;
        private Transform root;

        /// <summary>세션의 지금 공장에 묶고 모든 칸을 다시 그린다(세션 교체·공장 옮기기).</summary>
        public void Bind(GameSession s)
        {
            cat = GameBootstrap.Instance.Catalog;
            if (root == null) { root = new GameObject("Cells").transform; root.SetParent(GameBootstrap.Instance.WorldRoot, false); }
            if (boundBuild != null) boundBuild.CellChanged -= OnCellChanged;
            if (boundUnlocks != null) boundUnlocks.Unlocked -= OnUnlocked;
            bound = s;
            boundBuild = s.Build;
            boundUnlocks = s.Unlocks;
            boundBuild.CellChanged += OnCellChanged;
            boundUnlocks.Unlocked += OnUnlocked;
            RebuildAll();
        }

        private void OnDestroy()
        {
            if (boundBuild != null) boundBuild.CellChanged -= OnCellChanged;
            if (boundUnlocks != null) boundUnlocks.Unlocked -= OnUnlocked;
        }

        private void OnUnlocked(UnlockId _) => RebuildAll();

        /// <summary>칸이 바뀌면 그 칸과, 출구 화살표가 달라질 수 있는 이웃 분배기를 다시 그린다(스펙 11.6·11.7).</summary>
        private void OnCellChanged(int x, int y)
        {
            Rebuild(x, y);
            for (int d = 0; d < 4; d++)
            {
                var dir = (Direction)d;
                int nx = x + dir.Dx(), ny = y + dir.Dy();
                if (!bound.Grid.InBounds(nx, ny)) continue;
                var nt = bound.Grid.Get(nx, ny).Type;
                if (nt == CellType.Splitter || nt == CellType.Scanner) Rebuild(nx, ny); // 검사기 항공 출구 기본값도 이웃에 달렸다(스펙 14.5)
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

        private static GameObject Model(ModelCatalog cat, string key, Transform parent, Vector3 localPos, Quaternion localRot, float scale)
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
        public static readonly Color SplitterBlue = new Color(0x3D / 255f, 0x7B / 255f, 0xE0 / 255f);
        public static readonly Color SplitterRed = new Color(0xD9 / 255f, 0x5A / 255f, 0x4A / 255f);
        /// <summary>검사기 항공 모드 화살표 색(스펙 14.5): 1번 일반 = 흰색, 2번 항공 = 항공 마크 파랑 #2F86EB.</summary>
        public static readonly Color AirGroundArrow = Color.white;
        public static Color AirExitArrow => WorldBuilder.AirBlue;
        /// <summary>분배기 항공 모드 화살표 색(스펙 14.5): 1번 정상 = 초록(항공 출고 트럭), 2번 반품 = 노랑(항공 반품 트럭).</summary>
        public static Color AirOkArrow => WorldBuilder.AirOutletColor;
        public static Color AirReturnArrow => WorldBuilder.AirReturnColor;

        /// <summary>출구 화살표 이름별 색(뷰·미리보기 공용). 분배기 BlueArrow/RedArrow는 모드에 따라 파랑·빨강 또는 초록·노랑.</summary>
        public static Color ArrowColor(string name, RouteMode mode)
        {
            switch (name)
            {
                case "BlueArrow": return mode == RouteMode.Air ? AirOkArrow : SplitterBlue;
                case "RedArrow": return mode == RouteMode.Air ? AirReturnArrow : SplitterRed;
                case "GroundArrow": return AirGroundArrow;
                case "AirArrow": return AirExitArrow;
                default: return Color.white;
            }
        }

        private static Material ArrowMat(Color c, bool connected)
        {
            if (connected) return MaterialLibrary.Emissive(c, 0.5f);
            c.a = 0.4f;
            return MaterialLibrary.Transparent(c);
        }

        /// <summary>브릿지 기둥 색(#5A5F66).</summary>
        public static readonly Color BridgePost = new Color(0x5A / 255f, 0x5F / 255f, 0x66 / 255f);

        /// <summary>분배기 출구 화살표: 허브 중심에서 출구 쪽으로 0.3, 상판 위 0.03.</summary>
        private static void SplitterArrow(Transform parent, string name, Direction d, float top, Material mat)
        {
            var chevron = ChevronMesh.Create(parent, mat, name);
            chevron.transform.localRotation = Grid3D.Facing(d);
            chevron.transform.localPosition = Grid3D.Forward(d) * 0.3f + new Vector3(0f, top + 0.03f, 0f);
        }

        private GameObject Make(Cell c, int x, int y)
        {
            if (!c.IsBuilding) return null;
            Direction blue = c.Direction;
            Direction? red = null;
            bool blueConnected = true, redConnected = true;
            if (c.Type == CellType.Splitter)
                ConveyorSim.SplitterRoutes(bound.Grid, x, y, null, out blue, out red, out blueConnected, out redConnected);
            else if (c.Type == CellType.Scanner && c.Mode == RouteMode.Air)
            {
                ConveyorSim.ScannerRoutes(bound.Grid, x, y, out blue, out var air);
                red = air;
                var outs = ConveyorSim.SplitterOutputs(bound.Grid, x, y, null);
                blueConnected = outs.Contains(blue);
                redConnected = outs.Contains(air);
            }
            return Build(cat, c.Type, c.Direction, c.PalletKind, blue, red, blueConnected, redConnected, c, c.Mode);
        }

        /// <summary>설치 미리보기(스펙 11.9)용: 실제 칸 뷰와 같은 모델을 parent 아래에 만든다. 분배기는 파란 화살표 = dir, 빨간 화살표 = red(있을 때만).
        /// 팔레트 더미(PalletView)는 붙이지 않는다. 재질·그림자·충돌체 처리는 호출하는 쪽(PlacementGhost)이 한다.</summary>
        public static GameObject BuildPreview(Transform parent, CellType type, Direction dir, PalletKind kind, Direction? red, RouteMode mode = RouteMode.Inspect)
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Catalog == null) return null;
            // 검사기 항공 모드 미리보기: 항공 출구는 기본값(오른쪽)으로 보인다(설치 때 실제 이웃으로 다시 정한다).
            if (type == CellType.Scanner && mode == RouteMode.Air && !red.HasValue) red = dir.TurnRight();
            var go = Build(boot.Catalog, type, dir, kind, dir, red, true, true, null, mode);
            if (go == null) return null;
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>칸 종류·방향·팔레트 종류로 모델을 만든다. cell이 있으면 팔레트에 더미 뷰(PalletView)를 붙인다.</summary>
        private static GameObject Build(ModelCatalog cat, CellType type, Direction direction, PalletKind palletKind,
            Direction blue, Direction? red, bool blueConnected, bool redConnected, Cell cell, RouteMode mode)
        {
            var go = new GameObject(type.ToString());
            var facing = Grid3D.Facing(direction);
            switch (type)
            {
                case CellType.Conveyor:
                case CellType.FastConveyor:
                case CellType.Scanner:
                {
                    Model(cat, "conveyor-stripe", go.transform, Vector3.zero, facing, 1f);
                    var arrowPivot = new GameObject("ArrowPivot").transform;
                    arrowPivot.SetParent(go.transform, false);
                    arrowPivot.localRotation = facing;
                    var arrow = ChevronMesh.Create(arrowPivot);
                    arrow.transform.localPosition = new Vector3(0f, 0.43f, -0.32f);
                    var ba = arrow.AddComponent<BeltArrow>();
                    ba.Speed = type == CellType.FastConveyor ? 2f : 1f;
                    if (type == CellType.FastConveyor)
                    {
                        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        stripe.transform.SetParent(go.transform, false);
                        stripe.transform.localRotation = facing;
                        stripe.transform.localPosition = new Vector3(0f, 0.41f, 0f);
                        stripe.transform.localScale = new Vector3(0.9f, 0.02f, 0.08f);
                        stripe.GetComponent<Renderer>().material = MaterialLibrary.Emissive(new Color(1f, 0.3f, 0.25f), 2f);
                        Destroy(stripe.GetComponent<Collider>());
                    }
                    if (type == CellType.Scanner)
                    {
                        Model(cat, "scanner-low", go.transform, new Vector3(0f, 0.02f, 0f), facing * Quaternion.Euler(0f, 90f, 0f), 0.58f);
                        var laser = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        laser.transform.SetParent(go.transform, false);
                        laser.transform.localRotation = facing;
                        laser.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                        laser.transform.localScale = new Vector3(0.8f, 0.5f, 0.02f);
                        laser.GetComponent<Renderer>().material = MaterialLibrary.Transparent(new Color(1f, 0.2f, 0.2f, 0.45f));
                        Destroy(laser.GetComponent<Collider>());
                        if (mode == RouteMode.Air && red.HasValue)
                        {
                            // 항공 모드(스펙 14.5): 1번(일반) 흰 화살표, 2번(항공) 파란 화살표. 나갈 곳이 없으면 흐리게.
                            // 아치·레이저(높이 0.85까지)에 가리지 않도록 그 위에 띄운다.
                            const float top = 0.9f;
                            SplitterArrow(go.transform, "GroundArrow", blue, top, ArrowMat(ArrowColor("GroundArrow", mode), blueConnected));
                            SplitterArrow(go.transform, "AirArrow", red.Value, top, ArrowMat(ArrowColor("AirArrow", mode), redConnected));
                        }
                    }
                    break;
                }
                case CellType.Bridge:
                {
                    // 브릿지(스펙 11.12): 아랫길 = Direction과 수직인 바닥 벨트(양방향 직진이라 화살표 없음),
                    // 모서리 기둥 4개, 윗길 = 높이 0.9의 고가 벨트(Direction 축, 폭 0.9) + 미끄러지는 화살표.
                    var lower = Model(cat, "conveyor-stripe", go.transform, Vector3.zero, facing * Quaternion.Euler(0f, 90f, 0f), 1f);
                    lower.name = "LowerBelt";
                    const float deck = PackageViews.BridgeDeckHeight;
                    var postMat = MaterialLibrary.Lit(BridgePost, 0.3f);
                    foreach (var px in new[] { -0.38f, 0.38f })
                        foreach (var pz in new[] { -0.38f, 0.38f })
                            Box("BridgePost", go.transform, new Vector3(px, deck / 2f, pz), new Vector3(0.08f, deck, 0.08f), postMat);
                    var upper = Model(cat, "conveyor-stripe", go.transform, new Vector3(0f, deck, 0f), facing, 1f);
                    upper.name = "UpperBelt";
                    upper.transform.localScale = new Vector3(0.9f, 1f, 1f); // 폭만 0.9(길이는 1칸 그대로 이웃 벨트와 이어진다)
                    var upperPivot = new GameObject("ArrowPivot").transform;
                    upperPivot.SetParent(go.transform, false);
                    upperPivot.localPosition = new Vector3(0f, deck, 0f);
                    upperPivot.localRotation = facing;
                    var upperArrow = ChevronMesh.Create(upperPivot);
                    upperArrow.transform.localPosition = new Vector3(0f, 0.43f, -0.32f);
                    upperArrow.AddComponent<BeltArrow>();
                    break;
                }
                case CellType.Splitter:
                {
                    // 보라 허브 0.9×0.4×0.9(윗면 = 택배 표면 0.4) + 얇은 어두운 상판 + 파란 화살표(승인 출구) + 빨간 화살표(거부 출구)(스펙 11.7).
                    // 파란 출구가 연결돼 있지 않아도 설정을 보이도록 40% 알파로 흐리게 그린다. 빨간 출구는 있을 때만.
                    float top = PackageViews.SurfaceHeight(CellType.Splitter);
                    Box("SplitterHub", go.transform, new Vector3(0f, top / 2f, 0f), new Vector3(0.9f, top, 0.9f), MaterialLibrary.Lit(SplitterHub, 0.3f));
                    Box("SplitterPlate", go.transform, new Vector3(0f, top + 0.005f, 0f), new Vector3(0.72f, 0.01f, 0.72f), MaterialLibrary.Lit(new Color(0.16f, 0.14f, 0.2f), 0.2f));
                    // 빨간 출구는 명시 설정(RedDirection, 스펙 11.8)이면 연결되지 않아도 흐리게 보인다. 자동 규칙이면 연결된 출구가 있을 때만.
                    // 항공 모드(스펙 14.5)는 같은 두 출구(정상/반품)를 초록·노랑으로 그린다.
                    SplitterArrow(go.transform, "BlueArrow", blue, top, ArrowMat(ArrowColor("BlueArrow", mode), blueConnected));
                    if (red.HasValue) SplitterArrow(go.transform, "RedArrow", red.Value, top, ArrowMat(ArrowColor("RedArrow", mode), redConnected));
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
                    var band = MaterialLibrary.Lit(palletKind == PalletKind.Blue ? PalletView.BlueBand : PalletView.RedBand, 0.3f);
                    const float bh = 0.04f, bw = 0.05f, outer = 0.94f;
                    float by = top - bh / 2f + 0.005f;
                    Box("Band", go.transform, new Vector3(0f, by, (outer - bw) / 2f), new Vector3(outer, bh, bw), band);
                    Box("Band", go.transform, new Vector3(0f, by, -(outer - bw) / 2f), new Vector3(outer, bh, bw), band);
                    Box("Band", go.transform, new Vector3((outer - bw) / 2f, by, 0f), new Vector3(bw, bh, outer - 2f * bw), band);
                    Box("Band", go.transform, new Vector3(-(outer - bw) / 2f, by, 0f), new Vector3(bw, bh, outer - 2f * bw), band);
                    if (cell != null)
                    {
                        var pv = go.AddComponent<PalletView>();
                        pv.Cell = cell;
                        pv.Catalog = cat;
                    }
                    break;
                }
                case CellType.Table:
                case CellType.Labeler:
                {
                    // 강철 작업대: 몸체 0.9×0.56×0.9 + 밝은 상판 0.96×0.04×0.96, 윗면 = 택배 표면 높이(0.6).
                    float top = PackageViews.SurfaceHeight(CellType.Table);
                    Box("TableBody", go.transform, new Vector3(0f, (top - 0.04f) / 2f, 0f), new Vector3(0.9f, top - 0.04f, 0.9f), MaterialLibrary.Lit(new Color(0x8E / 255f, 0x94 / 255f, 0x9B / 255f), 0.3f));
                    Box("TableTop", go.transform, new Vector3(0f, top - 0.02f, 0f), new Vector3(0.96f, 0.04f, 0.96f), MaterialLibrary.Lit(new Color(0xC9 / 255f, 0xCD / 255f, 0xD2 / 255f), 0.35f));
                    if (type == CellType.Labeler)
                    {
                        // 운송장 부착대(스펙 15.1): 같은 작업대 + 뒤쪽(북) 가장자리에 운송장 롤(흰 원통, 가로) + 옆에 작은 프린터(어두운 상자 + 흰 출력구).
                        var roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        roll.name = "WaybillRoll";
                        Destroy(roll.GetComponent<Collider>());
                        roll.transform.SetParent(go.transform, false);
                        roll.transform.localPosition = new Vector3(-0.18f, top + 0.09f, 0.33f);
                        roll.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                        roll.transform.localScale = new Vector3(0.18f, 0.16f, 0.18f);
                        roll.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Lit(new Color(0.96f, 0.95f, 0.92f), 0.2f);
                        Box("Printer", go.transform, new Vector3(0.22f, top + 0.07f, 0.33f), new Vector3(0.3f, 0.14f, 0.22f), MaterialLibrary.Lit(new Color(0.18f, 0.2f, 0.23f), 0.4f));
                        Box("PrinterSlot", go.transform, new Vector3(0.22f, top + 0.1f, 0.215f), new Vector3(0.2f, 0.02f, 0.01f), MaterialLibrary.Emissive(new Color(0.95f, 0.95f, 0.9f), 0.4f));
                        break;
                    }
                    // 검사 화면: 상판 뒤쪽(북쪽) 가장자리에 카메라를 향해 세운다. 택배를 가리지 않도록 작게.
                    var panel = Model(cat, "screen-panel-flat", go.transform, new Vector3(0f, top, 0.3f), Quaternion.Euler(0f, 180f, 0f), 0.4f);
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
