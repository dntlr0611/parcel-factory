using System;
using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 정적 월드(공장 실내·벽·설비·야드·도크)를 코드로 만든다. 해금 부지(밝은 슬래브·위험 테이프·콘)는 해금 크기에 따라 다시 만든다.
    /// 좌표는 Unity 기준(동쪽 = +x, 북쪽 = +z). 건물 내부는 원점 중심 14×14.
    /// </summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        public Camera MainCamera { get; private set; }

        private const float HalfBuilding = 7f;      // 건물 내부 14×14
        private const float WallHeight = 1.7f;
        private const float DoorWidth = 2f;
        private const float CameraFov = 36f;
        private static readonly Vector3 CameraDir = new Vector3(0f, 14.5f, -10.5f).normalized;

        private static readonly Color FogColor = Hex(0x1D2127);
        private static readonly Color WallColor = Hex(0xD9D3C8);
        private static readonly Color WallDark = Hex(0x5A5F66);
        private static readonly Color LaneColor = Hex(0xE9C94A);

        private ModelCatalog cat;
        private Transform root, lotRoot, wallsRoot, eastWall, airDocks, westWall, inletDocks;
        private GameSession bound;
        private int framedSize = -1, framedW, framedH;

        public void Build(GameSession session)
        {
            cat = GameBootstrap.Instance.Catalog;
            root = GameBootstrap.Instance.WorldRoot;
            BuildCameraAndLight();
            BuildGround();
            BuildWalls();
            BuildProps();
            BuildYard();
            BuildDocks();
            Bind(session);
        }

        public void Bind(GameSession session)
        {
            if (bound != null) bound.Unlocks.Unlocked -= OnUnlocked;
            bound = session;
            bound.Unlocks.Unlocked += OnUnlocked;
            RebuildLot(session);
            RebuildEastSide(session);
            RebuildWestSide(session);
        }

        private void OnUnlocked(UnlockId id)
        {
            RebuildLot(bound);
            if (id == UnlockId.Plot2) RebuildEastSide(bound); // 항공 도크는 부지 확장 2에서만 생긴다(세션 교체는 Bind가 다시 만든다)
            if (id == UnlockId.Plot1 || id == UnlockId.Plot2) RebuildWestSide(bound); // 부지마다 입고구 하나(스펙 16.2)
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        /// <summary>격자 칸 중심의 월드 z(도크 줄 등). 도크 좌표는 FactoryGrid에서만 읽는다.</summary>
        private static float RowZ(int gy) => Grid3D.Tile(0, gy).z;

        private GameObject Spawn(string key, Vector3 pos, Quaternion rot, float scale, Transform parent)
        {
            var go = Instantiate(cat.Get(key), pos, rot, parent);
            go.name = key;
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            return go;
        }

        private static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool castShadow = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        /// <summary>바닥에 눕힌 Quad(윗면 +y). yaw는 길이(로컬 x) 방향 회전.</summary>
        private static GameObject FlatQuad(string name, Transform parent, Vector3 pos, float length, float width, float yaw, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            go.transform.localScale = new Vector3(length, width, 1f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        private Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        // ---------------- 카메라·조명 ----------------

        private void BuildCameraAndLight()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root, false);
            MainCamera = camGo.AddComponent<Camera>();
            MainCamera.fieldOfView = CameraFov;
            MainCamera.nearClipPlane = 0.3f;
            MainCamera.farClipPlane = 140f;
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = FogColor;
            var camData = MainCamera.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.renderShadows = true;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Hex(0xFFE6C2);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            // 프로토타입 태양 위치 (−9, 17, 4)를 Unity x로 뒤집은 (9, 17, 4)에서 원점을 비춘다.
            sunGo.transform.rotation = Quaternion.LookRotation(-new Vector3(9f, 17f, 4f).normalized, Vector3.up);

            // 반구광 근사: 하늘 #DFE7F2, 지면 #6B655C, 세기 0.55. URP가 three.js보다 어두워 하늘·적도는 40% 올린다.
            const float hemi = 0.55f, lift = 1.4f;
            var sky = Hex(0xDFE7F2) * hemi;
            var gnd = Hex(0x6B655C) * hemi;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * lift;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, gnd, 0.5f) * lift;
            RenderSettings.ambientGroundColor = gnd;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 24f;
            RenderSettings.fogEndDistance = 46f;

            var volGo = new GameObject("PostVolume");
            volGo.transform.SetParent(root, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.profile = profile;
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.45f);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.05f);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.15f); ca.saturation.Override(8f);
        }

        // ---------------- 지면·바닥 ----------------

        private void BuildGround()
        {
            FlatQuad("Ground", root, new Vector3(0f, -0.03f, 0f), 90f, 90f, 0f, MaterialLibrary.Asphalt());
            int inner = Mathf.RoundToInt(HalfBuilding * 2f);
            FlatQuad("Floor", root, new Vector3(0f, -0.02f, 0f), inner, inner, 0f, MaterialLibrary.Concrete(inner, Hex(0x8F8C86)));
        }

        // ---------------- 벽 ----------------

        /// <summary>두께 0.3 벽 + 어두운 굽도리 + 윗면 캡. (x0,z0)→(x1,z1) 직선.</summary>
        private static void WallRun(Transform parent, float x0, float z0, float x1, float z1)
        {
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            if (len < 0.01f) return;
            var g = new GameObject("WallRun").transform;
            g.SetParent(parent, false);
            g.localPosition = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            g.localRotation = Quaternion.Euler(0f, Mathf.Atan2(x1 - x0, z1 - z0) * Mathf.Rad2Deg, 0f);
            Box("Wall", g, new Vector3(0f, WallHeight / 2f, 0f), new Vector3(0.3f, WallHeight, len), MaterialLibrary.Lit(WallColor, 0.1f));
            Box("Plinth", g, new Vector3(0f, 0.11f, 0f), new Vector3(0.34f, 0.22f, len), MaterialLibrary.Lit(WallDark, 0.15f));
            Box("Cap", g, new Vector3(0f, WallHeight + 0.04f, 0f), new Vector3(0.36f, 0.08f, len), MaterialLibrary.Lit(WallDark, 0.15f));
        }

        /// <summary>x = 상수인 동·서 벽. rows의 각 z에 폭 2 출입구(상인방 + 양쪽 기둥 + 노란 띠).</summary>
        private static void SideWall(Transform parent, float x, IEnumerable<float> rows)
        {
            var gaps = new List<(float a, float b)>();
            foreach (var r in rows) gaps.Add((r - DoorWidth / 2f, r + DoorWidth / 2f));
            gaps.Sort((p, q) => p.a.CompareTo(q.a));
            float z = -HalfBuilding;
            var wallMat = MaterialLibrary.Lit(WallColor, 0.1f);
            var darkMat = MaterialLibrary.Lit(WallDark, 0.15f);
            var laneMat = MaterialLibrary.Lit(LaneColor, 0.3f);
            foreach (var (a0, b) in gaps)
            {
                float a = Mathf.Max(a0, z); // 붙어 있는 출입구는 겹치지 않게
                if (b <= a) continue;
                WallRun(parent, x, z, x, a);
                var door = new GameObject("Doorway").transform;
                door.SetParent(parent, false);
                float w = b - a, cz = (a + b) / 2f, postH = WallHeight - 0.5f;
                Box("Lintel", door, new Vector3(x, WallHeight - 0.25f, cz), new Vector3(0.3f, 0.5f, w), wallMat);
                Box("Post", door, new Vector3(x, postH / 2f, a + 0.06f), new Vector3(0.36f, postH, 0.12f), darkMat);
                Box("Post", door, new Vector3(x, postH / 2f, b - 0.06f), new Vector3(0.36f, postH, 0.12f), darkMat);
                Box("Stripe", door, new Vector3(x, WallHeight - 0.56f, cz), new Vector3(0.36f, 0.12f, w), laneMat);
                z = b;
            }
            WallRun(parent, x, z, x, HalfBuilding);
        }

        private void BuildWalls()
        {
            var walls = wallsRoot = Group("Walls");
            const float h = HalfBuilding;
            WallRun(walls, -h, h, h, h); // 북쪽 전체. 남쪽(카메라 쪽)은 트임.
            // 서쪽 벽은 입고구 수(스펙 16.2), 동쪽 벽은 항공 도크(스펙 14.1) 유무에 따라 출입구가 달라 RebuildWestSide·RebuildEastSide에서 만든다.
            var darkMat = MaterialLibrary.Lit(WallDark, 0.15f);
            foreach (var (x, z) in new[] { (h, h), (-h, h), (h, -h), (-h, -h) })
                Box("CornerPillar", walls, new Vector3(x, (WallHeight + 0.3f) / 2f, z), new Vector3(0.5f, WallHeight + 0.3f, 0.5f), darkMat);
        }

        // ---------------- 북쪽 벽 앞 설비·재고 ----------------

        private void BuildProps()
        {
            var props = Group("Props");
            const float h = HalfBuilding;
            var back = Quaternion.Euler(0f, 180f, 0f); // 벽을 등지게
            Spawn("machine-window", new Vector3(4.6f, 0f, h - 0.9f), back, 0.9f, props);
            Spawn("machine", new Vector3(3.3f, 0f, h - 0.9f), back, 0.9f, props);
            Spawn("hopper-round", new Vector3(1.6f, 0f, h - 0.75f), back, 0.9f, props);
            Spawn("pipe-large-long", new Vector3(-0.6f, 0f, h - 0.6f), back, 0.8f, props);
            Spawn("pipe-large-long", new Vector3(-2.2f, 0f, h - 0.6f), back, 0.8f, props);
            Spawn("robot-arm-a", new Vector3(-4.0f, 0f, h - 0.8f), back, 0.6f, props);
            Spawn("screen-hanging-wide", new Vector3(-5.6f, 1.0f, h - 0.5f), back, 1.0f, props);

            // 격자 밖 북동·북서 구석(격자 가장자리 ±6과 벽 ±7 사이)에 쌓인 재고 상자. 격자 안은 걸을 수 있는 바닥이라 비워 둔다.
            var rng = new System.Random(7);
            string[] kinds = { "box-large", "box-wide", "box-long" };
            var spots = new[] { (6.45f, 6.45f), (6.45f, 5.5f), (5.5f, 6.45f), (-6.45f, 6.45f), (-6.45f, 5.5f), (-5.5f, 6.45f) };
            foreach (var (x, z) in spots)
            {
                string k = kinds[rng.Next(kinds.Length)];
                Spawn(k, new Vector3(x, 0f, z), Quaternion.Euler(0f, ((float)rng.NextDouble() * 0.6f - 0.3f) * Mathf.Rad2Deg, 0f), 0.9f, props);
                if (rng.NextDouble() < 0.5)
                    Spawn("box-small", new Vector3(x, 0.5f, z), Quaternion.Euler(0f, (float)rng.NextDouble() * 1.5f * Mathf.Rad2Deg, 0f), 0.9f, props);
            }
        }

        // ---------------- 건물 밖 야드 ----------------

        private void BuildYard()
        {
            var yard = Group("Yard");
            // 컨테이너 FBX는 1배에서 약 1.4×1.3×3.0m다. 1.6배(≈2.2×2.1×4.9m)로 두고 x ≥ 18에 둬 16:9·2400x1080 화면 가장자리에 걸리지 않게 한다.
            Spawn("shipping-container-a", new Vector3(18.0f, 0f, 3.5f), Quaternion.identity, 1.6f, yard);
            Spawn("shipping-container-b", new Vector3(18.0f, 0f, 9.0f), Quaternion.identity, 1.6f, yard);
            Spawn("shipping-container-c", new Vector3(19.0f, 0f, 0.5f), Quaternion.Euler(0f, 5f, 0f), 1.6f, yard);
            Spawn("water-tower", new Vector3(-10.5f, 0f, 9.5f), Quaternion.identity, 2.6f, yard);
            Spawn("detail-tank", new Vector3(-11.5f, 0f, 6.0f), Quaternion.identity, 3.0f, yard);
            Spawn("chimney-medium", new Vector3(9.0f, 0f, 10.5f), Quaternion.identity, 2.6f, yard);
            // 멀리 보이는 공장 건물 실루엣: 건물 벽(±7)에서 충분히 떨어뜨린다.
            Spawn("building-c", new Vector3(-9.0f, 0f, 17.5f), Quaternion.identity, 3.2f, yard);
            Spawn("building-a", new Vector3(2.0f, 0f, 17.5f), Quaternion.identity, 3.2f, yard);
            Spawn("building-h", new Vector3(11.0f, 0f, 17.5f), Quaternion.identity, 3.2f, yard);
            foreach (var z in new[] { -6f, -2.8f, 0.4f })
                Spawn("cone-flat", new Vector3(-8.2f, 0f, z), Quaternion.identity, 0.9f, yard);
        }

        // ---------------- 도크·차량·차선 ----------------

        private void BuildDocks()
        {
            var docks = Group("Docks");
            // 차량은 벽 밖 출입구 앞에 후진 주차한다(모델 앞 = +z 가정: 뒤가 출입구를 향하도록 yaw).
            // 트럭 도크(스펙 10.1)는 동쪽 벽 출입구 바로 안쪽 칸 (11,2)·(11,4). 좌표는 FactoryGrid에서만 읽는다.
            Dock(docks, FactoryGrid.InletX, FactoryGrid.InletY, InletSlab, "van", -8.5f, -90f, truck: false, InletVan);
            Dock(docks, FactoryGrid.OutletX, FactoryGrid.OutletY, new Color(0.24f, 0.48f, 0.88f), "delivery", 8.6f, 90f, truck: true, Hex(0x3D7BE0));
            Dock(docks, FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY, new Color(0.85f, 0.35f, 0.29f), "truck", 8.6f, 90f, truck: true, Hex(0xD95A4A));
        }

        /// <summary>항공 트럭 색(스펙 14.1): 항공 출고 초록, 항공 반품 노랑.</summary>
        public static readonly Color AirOutletColor = Hex(0x36B565), AirReturnColor = Hex(0xF2C230);

        /// <summary>
        /// 동쪽 벽과 항공 도크(스펙 14.1). 벽 출입구는 출고·반품 줄에 더해, 격자에 항공 도크 칸이 있으면(부지 확장 2 이후) 그 줄에도 낸다.
        /// 항공 도크는 기존 트럭 도크와 같은 모양(슬래브·신호등·정지선·후진 주차 트럭)에 트럭 위 비행기 표지판을 단다.
        /// 확장 전에는 벽이 막혀 있고 항공 트럭도 없다. 세션 교체(불러오기)와 해금 때 다시 만든다.
        /// </summary>
        private void RebuildEastSide(GameSession session)
        {
            if (wallsRoot == null) return;
            if (eastWall != null) Destroy(eastWall.gameObject);
            if (airDocks != null) Destroy(airDocks.gameObject);
            eastWall = new GameObject("East").transform;
            eastWall.SetParent(wallsRoot, false);
            airDocks = Group("AirDocks");

            var rows = new List<float> { RowZ(FactoryGrid.OutletY), RowZ(FactoryGrid.ReturnDockY) };
            bool airOut = session.Grid.Get(FactoryGrid.AirOutletX, FactoryGrid.AirOutletY).Type == CellType.AirOutlet;
            bool airRet = session.Grid.Get(FactoryGrid.AirReturnDockX, FactoryGrid.AirReturnDockY).Type == CellType.AirReturnDock;
            if (airOut) rows.Add(RowZ(FactoryGrid.AirOutletY));
            if (airRet) rows.Add(RowZ(FactoryGrid.AirReturnDockY));
            SideWall(eastWall, HalfBuilding, rows);

            if (airOut)
                AirSign(Dock(airDocks, FactoryGrid.AirOutletX, FactoryGrid.AirOutletY, AirOutletColor, "delivery", 8.6f, 90f, truck: true, AirOutletColor, "Dock_air-outlet"), AirOutletColor);
            if (airRet)
                AirSign(Dock(airDocks, FactoryGrid.AirReturnDockX, FactoryGrid.AirReturnDockY, AirReturnColor, "truck", 8.6f, 90f, truck: true, AirReturnColor, "Dock_air-return"), AirReturnColor);
        }

        /// <summary>입고 트럭 색(초록). 부지 확장 입고구도 (0,2)와 같다.</summary>
        private static readonly Color InletSlab = new Color(0.25f, 0.64f, 0.35f);
        private static readonly Color InletVan = Hex(0x3FA35A);

        /// <summary>
        /// 서쪽 벽과 부지 확장 입고구(스펙 16.2). 출입구는 격자에 있는 입고구 줄마다 낸다((0,2)는 항상, (0,7)·(0,10)은 확장 뒤).
        /// (0,2)의 도크·밴은 BuildDocks가 만들고, 확장 입고구는 같은 모양(슬래브·신호등·바닥 차선·초록 밴)으로 여기서 만든다.
        /// </summary>
        private void RebuildWestSide(GameSession session)
        {
            if (wallsRoot == null) return;
            if (westWall != null) Destroy(westWall.gameObject);
            if (inletDocks != null) Destroy(inletDocks.gameObject);
            westWall = new GameObject("West").transform;
            westWall.SetParent(wallsRoot, false);
            inletDocks = Group("InletDocks");
            var rows = new List<float>();
            foreach (var (x, y) in FactoryGrid.InletTiles)
            {
                if (session.Grid.Get(x, y).Type != CellType.Inlet) continue;
                rows.Add(RowZ(y));
                if ((x, y) != (FactoryGrid.InletX, FactoryGrid.InletY))
                    Dock(inletDocks, x, y, InletSlab, "van", -8.5f, -90f, truck: false, InletVan, "Dock_inlet-" + y);
            }
            SideWall(westWall, -HalfBuilding, rows);
        }

        /// <summary>
        /// 트럭 지붕에 붙인 항공 마크(사용자 요청: 떠 있는 표지판 대신 지붕에 붙인다). 상자 마크와 같은 mark-air 텍스처
        /// (파란 둥근 사각 + 흰 테두리 + 흰 비행기)를 짐칸 지붕 위에 평평하게 눕힌다. 크기는 지붕 짧은 변의 72%.
        /// 이름(AirSign/Board/Icon)은 테스트가 찾는다.
        /// </summary>
        private static void AirSign(GameObject car, Color frameColor)
        {
            var rs = car.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var sign = new GameObject("AirSign").transform;
            sign.SetParent(car.transform.parent, false);
            sign.position = new Vector3(b.center.x, b.max.y + 0.012f, b.center.z);
            var board = new GameObject("Board").transform;
            board.SetParent(sign, false);
            board.localRotation = Quaternion.Euler(90f, 0f, 0f); // Quad 앞면(−z)이 위를 보게 눕힌다
            float size = Mathf.Min(b.size.x, b.size.z) * 0.72f;
            var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = "Icon";
            Destroy(icon.GetComponent<Collider>());
            icon.transform.SetParent(board, false);
            icon.transform.localScale = new Vector3(size, size, 1f);
            var ir = icon.GetComponent<Renderer>();
            ir.sharedMaterial = MaterialLibrary.Decal(Resources.Load<Texture2D>("UI/Icons/mark-air"), 0.35f);
            ir.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>항공 마크 파랑(mark-air.png의 #2F86EB).</summary>
        public static readonly Color AirBlue = Hex(0x2F86EB);

        private GameObject Dock(Transform parent, int gx, int gy, Color color, string vehicle, float vehicleX, float vehicleYaw, bool truck, Color bodyColor, string name = null)
        {
            var tile = Grid3D.Tile(gx, gy);
            var d = new GameObject(name ?? "Dock_" + vehicle).transform;
            d.SetParent(parent, false);
            Box("DockSlab", d, tile + new Vector3(0f, 0.11f, 0f), new Vector3(1f, 0.22f, 1f), MaterialLibrary.Lit(color, 0.25f));
            Box("DockMark", d, tile + new Vector3(0f, 0.23f, 0f), new Vector3(0.72f, 0.02f, 0.72f), MaterialLibrary.Lit(Hex(0xF5F0E6), 0.2f), castShadow: false);
            // 신호등은 격자 밖(격자 가장자리와 벽 사이 여백)에 둔다. 격자 안은 전부 걸을 수 있는 바닥이다(스펙 10.3).
            float side = Mathf.Sign(vehicleX);
            Spawn("warning-traffic", tile + new Vector3(side * 0.85f, 0f, 0.62f), Quaternion.identity, 0.6f, d);
            var car = Spawn(vehicle, new Vector3(vehicleX, 0f, tile.z), Quaternion.Euler(0f, vehicleYaw, 0f), 0.64f, d);
            TintVehicle(car, vehicle, bodyColor);

            if (truck)
            {
                // 트럭 도크 앞 흰 정지선 1줄: 칸 서쪽 변 바로 바깥(슬래브에 가리지 않게).
                Box("StopLine", d, tile + new Vector3(-0.5f - 0.05f, 0.006f, 0f), new Vector3(0.06f, 0.012f, 1f), MaterialLibrary.Lit(Hex(0xF5F0E6), 0.2f), castShadow: false);
            }
            else
            {
                // 입고구 칸 가장자리 → 서쪽 출입구까지 바닥 차선 2줄
                float doorX = side * HalfBuilding, x0 = tile.x + side * 0.5f;
                float len = Mathf.Abs(doorX - x0);
                if (len > 0.2f)
                {
                    float cx = (doorX + x0) / 2f;
                    var laneMat = MaterialLibrary.Lit(LaneColor, 0.3f);
                    Box("Lane", d, new Vector3(cx, 0f, tile.z - 0.42f), new Vector3(len, 0.012f, 0.06f), laneMat, castShadow: false);
                    Box("Lane", d, new Vector3(cx, 0f, tile.z + 0.42f), new Vector3(len, 0.012f, 0.06f), laneMat, castShadow: false);
                }
            }
            return car;
        }

        // ---------------- 차량 도색 ----------------

        /// <summary>Kenney car-kit 차체의 원래 도장 색(팔레트 텍스처에서 차체 면적이 가장 큰 유채색). 이 색상(hue) 띠만 다시 칠한다.</summary>
        private static readonly Dictionary<string, Color32> BodyPaint = new Dictionary<string, Color32>
        {
            { "van", new Color32(101, 142, 214, 255) },      // 파랑
            { "delivery", new Color32(90, 196, 135, 255) },  // 초록
            { "truck", new Color32(61, 166, 121, 255) },     // 초록
        };

        private static readonly Dictionary<string, Texture2D> paintCache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// 차량을 도크 색으로 칠한다. car-kit은 모든 부품(차체·바퀴·문)이 colormap 머티리얼 하나를 공유하고
        /// 창문·등·범퍼도 차체 메시 안의 팔레트 칸이라 _BaseColor 곱셈으로는 창문까지 물든다.
        /// 그래서 colormap을 복사해 차체 도장색과 같은 색상 띠(채도 0.3 이상)의 픽셀만 목표 색으로 바꾸고(명암 비율 유지),
        /// 그 텍스처를 쓰는 머티리얼 인스턴스를 가장 큰 렌더러(차체) 하나에만 준다. 바퀴·문·창문(무채색)은 그대로다.
        /// </summary>
        private static void TintVehicle(GameObject car, string key, Color target)
        {
            if (!BodyPaint.TryGetValue(key, out var paint)) return;
            Renderer body = null;
            float best = -1f;
            foreach (var r in car.GetComponentsInChildren<Renderer>())
            {
                float v = r.bounds.size.x * r.bounds.size.y * r.bounds.size.z;
                if (v > best) { best = v; body = r; }
            }
            if (body == null || body.sharedMaterial == null) return;
            var src = body.sharedMaterial.GetTexture("_BaseMap") as Texture2D;
            if (src == null || !src.isReadable) return;
            string cacheKey = key + ":" + ColorUtility.ToHtmlStringRGB(target);
            if (!paintCache.TryGetValue(cacheKey, out var tex) || tex == null)
            {
                tex = Repaint(src, paint, target);
                tex.name = "colormap-" + cacheKey;
                paintCache[cacheKey] = tex;
            }
            var mat = new Material(body.sharedMaterial) { name = "VehicleBody-" + key };
            mat.SetTexture("_BaseMap", tex);
            body.sharedMaterial = mat;
        }

        private static Texture2D Repaint(Texture2D src, Color32 paint, Color target)
        {
            Color.RGBToHSV(paint, out float ph, out _, out float pv);
            Color.RGBToHSV(target, out float th, out float ts, out float tv);
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float h, out float sat, out float v);
                float dh = Mathf.Abs(h - ph); dh = Mathf.Min(dh, 1f - dh);
                if (sat < 0.3f || dh > 0.06f) continue;
                var c = Color.HSVToRGB(th, ts, Mathf.Clamp01(v * tv / Mathf.Max(0.01f, pv)));
                px[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), px[i].a);
            }
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { filterMode = src.filterMode, wrapMode = src.wrapMode };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // ---------------- 해금 부지 ----------------

        public void RebuildLot(GameSession session)
        {
            if (lotRoot != null) Destroy(lotRoot.gameObject);
            lotRoot = Group("Lot");
            int n = session.Grid.UnlockedSize;
            var center = Grid3D.TileF((n - 1) / 2f, (n - 1) / 2f);
            float cx = center.x, cz = center.z;

            Box("Slab", lotRoot, new Vector3(cx, -0.04f, cz), new Vector3(n, 0.08f, n), MaterialLibrary.Concrete(n, Hex(0xCFCAC1)), castShadow: false);

            // 둘레 위험 테이프
            var tape = MaterialLibrary.Hazard(n * 1.5f);
            float xa = cx - n / 2f, xb = cx + n / 2f, za = cz - n / 2f, zb = cz + n / 2f;
            const float y = 0.012f, w = 0.14f;
            FlatQuad("TapeS", lotRoot, new Vector3(cx, y, za), n, w, 0f, tape);
            FlatQuad("TapeN", lotRoot, new Vector3(cx, y, zb), n, w, 0f, tape);
            FlatQuad("TapeW", lotRoot, new Vector3(xa, y, cz), n, w, 90f, tape);
            FlatQuad("TapeE", lotRoot, new Vector3(xb, y, cz), n, w, 90f, tape);
            foreach (var (x, z) in new[] { (xa, za), (xb, za), (xa, zb), (xb, zb) })
                Spawn("cone", new Vector3(x, 0.01f, z), Quaternion.identity, 1.0f, lotRoot);

            FrameLot(n);
        }

        // ---------------- 카메라 프레이밍 ----------------

        /// <summary>해금 부지 + 도크 차량이 화면에 들어오도록 카메라를 잡는다. 화면 크기가 바뀌면 Update에서 다시 부른다.</summary>
        private void FrameLot(int n)
        {
            framedSize = n;
            framedW = Screen.width;
            framedH = Screen.height;
            float t = Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad);
            // 화면 비율. 카메라가 RenderTexture에 그리는 동안(스크린샷 하네스)에는 그 텍스처 비율을 쓴다.
            var rt = MainCamera.targetTexture;
            float a = rt != null ? (float)rt.width / rt.height : (float)Screen.width / Mathf.Max(1, Screen.height);
            if (a < 0.01f) a = 1f;
            float hw = n / 2f + 5f, hv = n / 2f + 2.8f;
            float dist = Mathf.Max(hw / (t * a), hv / t) * 1.04f;
            var c = Grid3D.TileF((n - 1) / 2f, (n - 1) / 2f);
            var target = new Vector3(c.x * 0.2f, 0f, c.z + 0.5f);
            MainCamera.transform.position = target + CameraDir * dist;
            MainCamera.transform.LookAt(target);
            RenderSettings.fogStartDistance = dist + 6f;
            RenderSettings.fogEndDistance = dist + 30f;
        }

        private void Update()
        {
            if (MainCamera == null || framedSize < 0) return;
            if (Screen.width != framedW || Screen.height != framedH) FrameLot(framedSize);
        }

        private void OnDestroy()
        {
            if (bound != null) bound.Unlocks.Unlocked -= OnUnlocked;
        }
    }
}
