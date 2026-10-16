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
        private Transform root, lotRoot;
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
        }

        private void OnUnlocked(UnlockId _) => RebuildLot(bound);

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
            var walls = Group("Walls");
            const float h = HalfBuilding;
            WallRun(walls, -h, h, h, h); // 북쪽 전체. 남쪽(카메라 쪽)은 트임.
            SideWall(walls, -h, new[] { RowZ(FactoryGrid.InletY) });
            SideWall(walls, h, new[] { RowZ(FactoryGrid.OutletY), RowZ(FactoryGrid.ReturnDockY) });
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

            // 미해금 구역에 쌓인 재고 상자
            var rng = new System.Random(7);
            string[] kinds = { "box-large", "box-wide", "box-long" };
            var spots = new[] { (6.2f, 5.6f), (5.2f, 5.7f), (6.1f, 4.6f), (-6.1f, 5.5f), (-6.2f, 4.4f), (-5.1f, 5.6f) };
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
            Dock(docks, FactoryGrid.InletX, FactoryGrid.InletY, new Color(0.25f, 0.64f, 0.35f), "van", -8.5f, -90f);
            Dock(docks, FactoryGrid.OutletX, FactoryGrid.OutletY, new Color(0.24f, 0.48f, 0.88f), "delivery", 8.6f, 90f);
            Dock(docks, FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY, new Color(0.85f, 0.35f, 0.29f), "truck", 8.6f, 90f);
        }

        private void Dock(Transform parent, int gx, int gy, Color color, string vehicle, float vehicleX, float vehicleYaw)
        {
            var tile = Grid3D.Tile(gx, gy);
            var d = new GameObject("Dock_" + vehicle).transform;
            d.SetParent(parent, false);
            Box("DockSlab", d, tile + new Vector3(0f, 0.11f, 0f), new Vector3(1f, 0.22f, 1f), MaterialLibrary.Lit(color, 0.25f));
            Box("DockMark", d, tile + new Vector3(0f, 0.23f, 0f), new Vector3(0.72f, 0.02f, 0.72f), MaterialLibrary.Lit(Hex(0xF5F0E6), 0.2f), castShadow: false);
            Spawn("warning-traffic", tile + new Vector3(0f, 0f, 0.62f), Quaternion.identity, 0.6f, d);
            Spawn(vehicle, new Vector3(vehicleX, 0f, tile.z), Quaternion.Euler(0f, vehicleYaw, 0f), 0.64f, d);

            // 도크 칸 가장자리 → 출입구까지 바닥 차선 2줄
            float side = Mathf.Sign(vehicleX);
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
