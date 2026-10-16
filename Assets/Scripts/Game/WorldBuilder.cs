using System;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ParcelFactory.Game
{
    /// <summary>정적 월드를 코드로 만든다. 부지(바닥·울타리·풀)는 해금 크기에 따라 다시 만든다.</summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        public Camera MainCamera { get; private set; }

        private ModelCatalog cat;
        private Transform root, lotRoot;
        private GameSession bound;
        private GameObject vanIn, vanOut, truckRet;

        public void Build(GameSession session)
        {
            cat = GameBootstrap.Instance.Catalog;
            root = GameBootstrap.Instance.WorldRoot;
            BuildCameraAndLight();
            BuildGround();
            BuildForest();
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

        private GameObject Spawn(string key, Vector3 pos, Quaternion rot, float scale, Transform parent)
        {
            var go = Instantiate(cat.Get(key), pos, rot, parent);
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            return go;
        }

        private void BuildCameraAndLight()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root, false);
            MainCamera = camGo.AddComponent<Camera>();
            MainCamera.fieldOfView = 38f;
            MainCamera.nearClipPlane = 0.3f;
            MainCamera.farClipPlane = 80f;
            MainCamera.clearFlags = CameraClearFlags.SolidColor;
            MainCamera.backgroundColor = new Color(0.15f, 0.21f, 0.12f);
            var camData = MainCamera.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.renderShadows = true;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.5f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.26f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = MainCamera.backgroundColor;
            RenderSettings.fogStartDistance = 26f;
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

        private void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root, false);
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            ground.transform.position = new Vector3(0f, -0.02f, 0f);
            ground.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Lit(new Color(0.36f, 0.52f, 0.25f));
            Destroy(ground.GetComponent<Collider>());
        }

        private void BuildForest()
        {
            var forest = new GameObject("Forest").transform;
            forest.SetParent(root, false);
            var rng = new System.Random(7);
            string[] trees = { "tree_pineRoundA", "tree_pineRoundB", "tree_pineRoundC", "tree_pineRoundD", "tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_default", "tree_oak", "tree_detailed" };
            string[] rocks = { "rock_largeA", "rock_largeB", "rock_largeC", "rock_smallA", "rock_smallB" };
            string[] bushes = { "plant_bush", "plant_bushLarge", "grass_large" };
            for (int i = 0; i < 220; i++)
            {
                float x = (float)(rng.NextDouble() - 0.5) * 64f, z = (float)(rng.NextDouble() - 0.5) * 64f;
                if (Mathf.Abs(x) < 8.5f && Mathf.Abs(z) < 8.5f) continue;
                if (z > 20f) continue; // 카메라 뒤쪽은 비워둔다
                double roll = rng.NextDouble();
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                if (roll < 0.72f) Spawn(trees[rng.Next(trees.Length)], new Vector3(x, 0f, z), rot, 1.7f + (float)rng.NextDouble() * 0.9f, forest);
                else if (roll < 0.86f) Spawn(rocks[rng.Next(rocks.Length)], new Vector3(x, 0f, z), rot, 1.1f + (float)rng.NextDouble() * 0.6f, forest);
                else Spawn(bushes[rng.Next(bushes.Length)], new Vector3(x, 0f, z), rot, 1.4f + (float)rng.NextDouble() * 0.6f, forest);
            }
            // 부지 뒤편 컨테이너 몇 개
            Spawn("shipping-container-a", new Vector3(-3f, 0f, 9.2f), Quaternion.Euler(0f, 10f, 0f), 3.2f, forest);
            Spawn("shipping-container-b", new Vector3(1.2f, 0f, 9.6f), Quaternion.Euler(0f, -6f, 0f), 3.2f, forest);
        }

        private void BuildDocks()
        {
            var docks = new GameObject("Docks").transform;
            docks.SetParent(root, false);
            Dock(docks, FactoryGrid.InletX, FactoryGrid.InletY, new Color(0.25f, 0.64f, 0.35f));
            Dock(docks, FactoryGrid.OutletX, FactoryGrid.OutletY, new Color(0.24f, 0.48f, 0.88f));
            Dock(docks, FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY, new Color(0.85f, 0.35f, 0.29f));
            vanIn = Spawn("van", Grid3D.Tile(FactoryGrid.InletX, FactoryGrid.InletY) + new Vector3(-1.9f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), 0.62f, docks);
            vanOut = Spawn("delivery", Grid3D.Tile(FactoryGrid.OutletX, FactoryGrid.OutletY) + new Vector3(2.1f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), 0.62f, docks);
            truckRet = Spawn("truck", Grid3D.Tile(FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY) + new Vector3(2.1f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), 0.62f, docks);
        }

        private void Dock(Transform parent, int gx, int gy, Color color)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "DockSlab";
            slab.transform.SetParent(parent, false);
            slab.transform.position = Grid3D.Tile(gx, gy) + new Vector3(0f, 0.11f, 0f);
            slab.transform.localScale = new Vector3(1f, 0.22f, 1f);
            slab.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Lit(color, 0.25f);
            Destroy(slab.GetComponent<Collider>());
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.transform.SetParent(parent, false);
            mark.transform.position = Grid3D.Tile(gx, gy) + new Vector3(0f, 0.23f, 0f);
            mark.transform.localScale = new Vector3(0.7f, 0.02f, 0.7f);
            mark.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Lit(Color.white, 0.2f);
            Destroy(mark.GetComponent<Collider>());
            Spawn("warning-traffic", Grid3D.Tile(gx, gy) + new Vector3(0f, 0f, 0.55f), Quaternion.identity, 0.6f, parent);
        }

        public void RebuildLot(GameSession session)
        {
            if (lotRoot != null) Destroy(lotRoot.gameObject);
            lotRoot = new GameObject("Lot").transform;
            lotRoot.SetParent(root, false);
            int n = session.Grid.UnlockedSize;
            for (int gy = 0; gy < FactoryGrid.Height; gy++)
            {
                for (int gx = 0; gx < FactoryGrid.Width; gx++)
                {
                    var pos = Grid3D.Tile(gx, gy);
                    var rng = new System.Random(gx * 31 + gy * 17 + 5);
                    if (gx < n && gy < n)
                    {
                        Spawn("floor", pos, Quaternion.identity, 1f, lotRoot);
                    }
                    else
                    {
                        Spawn("ground_grass", pos + new Vector3(0f, 0.05f, 0f), Quaternion.identity, 1f, lotRoot);
                        if (rng.NextDouble() < 0.45) Spawn("grass_large", pos + new Vector3((float)(rng.NextDouble() - 0.5) * 0.5f, 0.05f, (float)(rng.NextDouble() - 0.5) * 0.5f), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), 1.5f, lotRoot);
                    }
                }
            }
            // 울타리: 해금 영역 둘레. 도크 칸의 동·서 변은 컨베이어 출입구이므로 건너뛴다.
            bool IsDock(int gx, int gy) =>
                (gx == FactoryGrid.InletX && gy == FactoryGrid.InletY) ||
                (gx == FactoryGrid.OutletX && gy == FactoryGrid.OutletY) ||
                (gx == FactoryGrid.ReturnDockX && gy == FactoryGrid.ReturnDockY);
            for (int i = 0; i < n; i++)
            {
                Spawn("fence_simple", Grid3D.Tile(i, n - 1), Quaternion.Euler(0f, 0f, 0f), 1f, lotRoot);     // 북쪽 변 (+z)
                Spawn("fence_simple", Grid3D.Tile(i, 0), Quaternion.Euler(0f, 180f, 0f), 1f, lotRoot);        // 남쪽 변 (−z)
                if (!IsDock(n - 1, i)) Spawn("fence_simple", Grid3D.Tile(n - 1, i), Quaternion.Euler(0f, 90f, 0f), 1f, lotRoot);     // 동쪽 변 (+x)
                if (!IsDock(0, i)) Spawn("fence_simple", Grid3D.Tile(0, i), Quaternion.Euler(0f, -90f, 0f), 1f, lotRoot);        // 서쪽 변 (−x)
            }

            float eastX = Grid3D.Tile(n - 1, 0).x + 1.1f + 1.0f;   // 울타리 바깥 한 칸 + 여유
            vanOut.transform.position = new Vector3(eastX, 0f, Grid3D.Tile(FactoryGrid.OutletX, FactoryGrid.OutletY).z);
            truckRet.transform.position = new Vector3(eastX, 0f, Grid3D.Tile(FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY).z);
            FrameLot(n);
        }

        private void FrameLot(int n)
        {
            var center = Grid3D.TileF((n - 1) / 2f, (n - 1) / 2f);
            MainCamera.transform.position = center + new Vector3(0f, 17f, -9.5f);
            MainCamera.transform.LookAt(center);
        }

        private void OnDestroy()
        {
            if (bound != null) bound.Unlocks.Unlocked -= OnUnlocked;
        }
    }
}
