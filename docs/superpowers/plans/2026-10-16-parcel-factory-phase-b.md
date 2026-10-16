# Parcel Factory Phase B 구현 계획 — Unity 3D 레이어

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase A/A2 코어 위에 Unity 6.3 URP 3D 렌더링·입력·HUD를 얹어, 에디터 Play와 PC 빌드에서 스펙 9절의 게임이 실제로 돌아가게 한다(모바일 빌드 모듈은 B7에서 설치만 한다).

**Architecture:** 씬 파일에 손으로 넣는 것은 없다. `GameBootstrap`이 Play 시작 시 자동 생성되어 `ModelCatalog`(에디터 스크립트가 만든 ScriptableObject, Kenney FBX 참조)로 월드·뷰·입력·HUD를 코드로 조립한다. 뷰는 코어를 읽기만 한다(매 프레임 `Sim.Packages`, `Worker.Carrying`, 셀 변경 이벤트). 입력은 Input System 액션을 코드로 만들고, HUD는 UGUI를 코드로 만든다(TMP·UXML 없음).

**Tech Stack:** Unity 6000.3.26f1, URP 17.3.0, Input System 1.20.0, Newtonsoft 3.2.2, Test Framework 1.6.0, UGUI(레거시 Text), Kenney CC0 FBX (`Assets/ThirdParty/Kenney/*/FBX`).

**Spec:** `docs/superpowers/specs/2026-10-15-parcel-factory-design.md` — 9.1(조작), 9.4(아트·HUD), 9.5(Unity 레이어 규칙). 코어 API는 9.5에 정리되어 있다.

## Global Constraints

- 게임 레이어 코드는 `Assets/Scripts/Game/`(asmdef `ParcelFactory.Game`)에만 둔다. 코어(`Assets/Scripts/Core`)와 코어 테스트는 수정하지 않는다.
- 에디터 전용 스크립트는 `Assets/Editor/`. 런타임에서 `UnityEditor`를 참조하지 않는다.
- 월드 단위: 1칸 = 1m. 격자 (gx, gy) → 월드 `(gx − 5.5, 0, gy − 5.5)`. 북쪽(gy+) = +z = 화면 위.
- 카메라 고정: 위치 (0, 17, −9.5), 원점을 바라봄, FOV 38. 따뜻한 방향광 + 부드러운 그림자, 안개, URP Volume(ACES 톤매핑, 비네팅, 약한 블룸).
- 핫바: 부품 종류당 1칸(레일, 고속 레일, 검사대, 분배기, 자동 검사기 순, 키 1~5), 방향은 R 또는 칸 재탭으로 보유 방향 순환.
- 조작(9.1·9.7): WASD/방향키/게임패드 스틱 이동(누르고 있는 동안 연속 이동, `Worker.Move(vx, vy, dt)`), E 상호작용, X 해체, R 방향, Q 빈손, P 일시정지, T 배속, B 상점, O 조작, Esc 패널 닫기. 터치 기기에서는 화면 D-패드와 E/X/R 버튼.
- 작업자가 이동하면 `Worker.CancelInspection()`을 호출한다. 프롬프트 문구는 `Worker.Preview(tool)` 결과로 만든다.
- 모든 Unity 검증은 배치 모드로 한다: `UNITY="/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity"`, 항상 `-batchmode -nographics -logFile <path>`. 컴파일 확인은 `-quit` 실행 후 `grep -c "error CS" <log>`가 0.
- 커밋 트레일러: 서브에이전트는 자기 모델의 `Co-Authored-By`를 쓴다.

## 파일 구조

```
Assets/Editor/ProjectSetup.cs         (기존) URP 파이프라인
Assets/Editor/CatalogBuilder.cs       ModelCatalog 에셋 생성, Main 씬 생성·빌드 설정
Assets/Resources/PF_Catalog.asset     (생성물) Kenney 프리팹 참조 목록
Assets/Scenes/Main.unity              (생성물) 빈 씬
Assets/Scripts/Game/ModelCatalog.cs   ScriptableObject + Get(key)
Assets/Scripts/Game/Grid3D.cs         격자↔월드 변환, 방향 회전
Assets/Scripts/Game/MaterialLibrary.cs URP 재질 헬퍼(단색, 반투명)
Assets/Scripts/Game/GameBootstrap.cs  자동 생성 진입점, 세션 보유·교체
Assets/Scripts/Game/GameLoop.cs       고정 20Hz 틱
Assets/Scripts/Game/WorldBuilder.cs   지면·부지·울타리·숲·트럭·도크·조명·카메라·볼륨
Assets/Scripts/Game/CellViews.cs      셀 오브젝트 생성·교체, BeltArrow
Assets/Scripts/Game/PackageViews.cs   택배 풀
Assets/Scripts/Game/WorkerView.cs     작업자 모델·보간·들고 있는 택배·커서
Assets/Scripts/Game/InputController.cs Input System 액션 → 코어 호출
Assets/Scripts/Game/UiKit.cs          UGUI 코드 생성 헬퍼
Assets/Scripts/Game/HudController.cs  상단 바·핫바·프롬프트·상태·토스트·조작·터치 컨트롤
Assets/Scripts/Game/Panels.cs         상점·검사 카드·해체 확인
Assets/Scripts/Game/SaveFile.cs       save.json 읽기·쓰기(원자적)
Assets/Tests/Game/ParcelFactory.Game.Tests.asmdef
Assets/Tests/Game/BootSmokeTests.cs   PlayMode: 부팅·틱·뷰 생성 확인
```

---

### Task 1 (B1): 카탈로그·씬·부트스트랩·게임 루프·스모크 테스트

**Files:**
- Create: `Assets/Editor/CatalogBuilder.cs`
- Create: `Assets/Scripts/Game/ModelCatalog.cs`, `Grid3D.cs`, `MaterialLibrary.cs`, `GameBootstrap.cs`, `GameLoop.cs`
- Modify: `Assets/Scripts/Game/ParcelFactory.Game.asmdef`
- Create: `Assets/Tests/Game/ParcelFactory.Game.Tests.asmdef`, `Assets/Tests/Game/BootSmokeTests.cs`

**Interfaces:**
- Produces:
  - `ModelCatalog : ScriptableObject { GameObject Get(string key) }` — 키는 FBX 파일명(확장자 없음, 예: `conveyor-stripe`, `tree_pineRoundA`, `character-male-a`).
  - `static class Grid3D { Vector3 Tile(int gx,int gy); Vector3 TileF(float gx,float gy); Quaternion Facing(Direction d); Vector3 Forward(Direction d) }`
  - `static class MaterialLibrary { Material Lit(Color c, float smooth=0.1f); Material Transparent(Color c); Material Emissive(Color c, float strength) }`
  - `class GameBootstrap : MonoBehaviour { static GameBootstrap Instance; static GameBootstrap Ensure(); GameSession Session; ModelCatalog Catalog; Transform WorldRoot; event Action<GameSession> SessionReplaced; void StartNewGame(); void ReplaceSession(GameSession) }` — Task B2~B6에서 `Awake`에 컴포넌트를 추가한다.
  - `class GameLoop : MonoBehaviour { const float TickSeconds = 0.05f }`

- [ ] **Step 1: asmdef 참조 추가**

`Assets/Scripts/Game/ParcelFactory.Game.asmdef`의 `references`를 다음으로 바꾼다:
```json
  "references": ["ParcelFactory.Core", "Unity.InputSystem", "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Core.Runtime"],
```

- [ ] **Step 2: 카탈로그 ScriptableObject**

`Assets/Scripts/Game/ModelCatalog.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>Kenney FBX 프리팹을 이름으로 찾는다. 에디터의 CatalogBuilder가 채운다.</summary>
    public sealed class ModelCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public GameObject Prefab;
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, GameObject> map;

        public GameObject Get(string key)
        {
            if (map == null)
            {
                map = new Dictionary<string, GameObject>();
                foreach (var e in Entries) if (e.Prefab != null && !map.ContainsKey(e.Key)) map[e.Key] = e.Prefab;
            }
            if (!map.TryGetValue(key, out var go))
                throw new KeyNotFoundException($"ModelCatalog has no '{key}'");
            return go;
        }

        public bool Has(string key)
        {
            try { Get(key); return true; } catch (KeyNotFoundException) { return false; }
        }
    }
}
```

- [ ] **Step 3: 카탈로그·씬 생성 에디터 스크립트**

`Assets/Editor/CatalogBuilder.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using ParcelFactory.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ParcelFactory.EditorTools
{
    /// <summary>-executeMethod ParcelFactory.EditorTools.CatalogBuilder.BuildAll</summary>
    public static class CatalogBuilder
    {
        private const string Root = "Assets/ThirdParty/Kenney/";
        private static readonly (string kit, string[] names)[] Wanted =
        {
            ("factory-kit", new[] { "conveyor", "conveyor-stripe", "conveyor-corner", "conveyor-junction-t", "scanner-low", "box-small", "box-large", "screen-panel-flat", "floor", "floor-large", "structure-wall", "warning-traffic", "arrow", "cone", "machine" }),
            ("city-kit-industrial", new[] { "shipping-container-a", "shipping-container-b", "shipping-container-c" }),
            ("nature-kit", new[] { "tree_pineRoundA", "tree_pineRoundB", "tree_pineRoundC", "tree_pineRoundD", "tree_pineRoundE", "tree_pineRoundF", "tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_pineTallD", "tree_default", "tree_oak", "tree_detailed", "rock_largeA", "rock_largeB", "rock_largeC", "rock_smallA", "rock_smallB", "plant_bush", "plant_bushLarge", "fence_simple", "fence_corner", "grass_large", "grass", "ground_grass", "stump_round", "log" }),
            ("car-kit", new[] { "delivery", "truck", "van", "box" }),
            ("mini-characters", new[] { "character-male-a", "character-male-b", "character-female-a", "character-female-b" }),
        };

        public static void BuildAll()
        {
            BuildCatalog();
            CreateMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("CATALOGBUILDER_DONE");
        }

        public static void BuildCatalog()
        {
            Directory.CreateDirectory("Assets/Resources");
            const string path = "Assets/Resources/PF_Catalog.asset";
            var cat = AssetDatabase.LoadAssetAtPath<ModelCatalog>(path);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<ModelCatalog>();
                AssetDatabase.CreateAsset(cat, path);
            }
            cat.Entries.Clear();
            var missing = new List<string>();
            foreach (var (kit, names) in Wanted)
            {
                foreach (var n in names)
                {
                    var p = $"{Root}{kit}/FBX/{n}.fbx";
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go == null) { missing.Add(p); continue; }
                    cat.Entries.Add(new ModelCatalog.Entry { Key = n, Prefab = go });
                }
            }
            EditorUtility.SetDirty(cat);
            Debug.Log($"CATALOG entries={cat.Entries.Count} missing={missing.Count}\n" + string.Join("\n", missing));
        }

        public static void CreateMainScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            const string path = "Assets/Scenes/Main.unity";
            if (!File.Exists(path))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, path);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        }
    }
}
```

- [ ] **Step 4: 격자·재질 헬퍼**

`Assets/Scripts/Game/Grid3D.cs`:
```csharp
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>격자 (gx, gy) ↔ 월드. 1칸 = 1m, 북쪽(gy+) = +z.</summary>
    public static class Grid3D
    {
        public const float Half = 5.5f;

        public static Vector3 Tile(int gx, int gy) => new Vector3(gx - Half, 0f, gy - Half);
        public static Vector3 TileF(float gx, float gy) => new Vector3(gx - Half, 0f, gy - Half);

        public static Vector3 Forward(Direction d) => new Vector3(d.Dx(), 0f, d.Dy());
        public static Quaternion Facing(Direction d) => Quaternion.LookRotation(Forward(d), Vector3.up);

        /// <summary>월드 좌표를 격자 칸으로. 범위 밖이면 false.</summary>
        public static bool ToTile(Vector3 world, out int gx, out int gy)
        {
            gx = Mathf.FloorToInt(world.x + Half + 0.5f);
            gy = Mathf.FloorToInt(world.z + Half + 0.5f);
            return gx >= 0 && gy >= 0 && gx < FactoryGrid.Width && gy < FactoryGrid.Height;
        }
    }
}
```

`Assets/Scripts/Game/MaterialLibrary.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParcelFactory.Game
{
    /// <summary>URP Lit/Unlit 재질을 코드로 만든다. 같은 인자는 캐시해 재사용한다.</summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Lit(Color c, float smoothness = 0.1f)
        {
            string key = $"lit:{c}:{smoothness}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            return cache[key] = m;
        }

        public static Material Emissive(Color c, float strength = 1.5f)
        {
            string key = $"emi:{c}:{strength}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = Lit(c, 0.3f);
            m = new Material(m);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return cache[key] = m;
        }

        public static Material Transparent(Color c)
        {
            string key = $"tr:{c}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return cache[key] = m;
        }
    }
}
```

- [ ] **Step 5: 부트스트랩과 게임 루프**

`Assets/Scripts/Game/GameLoop.cs`:
```csharp
using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class GameLoop : MonoBehaviour
    {
        public const float TickSeconds = 0.05f;
        private const float MaxFrameSeconds = 0.25f;
        private float accumulator;

        private void Update()
        {
            var session = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            if (session == null) return;
            accumulator += Mathf.Min(Time.deltaTime, MaxFrameSeconds);
            while (accumulator >= TickSeconds)
            {
                accumulator -= TickSeconds;
                session.Tick(TickSeconds);
            }
        }
    }
}
```

`Assets/Scripts/Game/GameBootstrap.cs`:
```csharp
using System;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>씬 설정 없이 Play를 누르면 모든 것을 코드로 조립하는 진입점.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public GameSession Session { get; private set; }
        public ModelCatalog Catalog { get; private set; }
        public Transform WorldRoot { get; private set; }

        public event Action<GameSession> SessionReplaced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate() => Ensure();

        public static GameBootstrap Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ParcelFactory");
            return go.AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Catalog = Resources.Load<ModelCatalog>("PF_Catalog");
            if (Catalog == null) Debug.LogError("PF_Catalog not found. Run CatalogBuilder.BuildAll in the editor.");
            WorldRoot = new GameObject("World").transform;
            WorldRoot.SetParent(transform, false);
            gameObject.AddComponent<GameLoop>();
            Session = new GameSession();
            // B2~B6에서 아래에 컴포넌트를 추가하고 Session에 묶는다. 생성 순서: 컴포넌트 추가 → World.Build → 뷰 Bind → HUD.
            SessionReplaced?.Invoke(Session);
        }

        public void StartNewGame() => ReplaceSession(new GameSession());

        public void ReplaceSession(GameSession s)
        {
            Session = s;
            SessionReplaced?.Invoke(s);
        }
    }
}
```

- [ ] **Step 6: PlayMode 스모크 테스트**

`Assets/Tests/Game/ParcelFactory.Game.Tests.asmdef`:
```json
{
  "name": "ParcelFactory.Game.Tests",
  "rootNamespace": "ParcelFactory.Game.Tests",
  "references": ["ParcelFactory.Core", "ParcelFactory.Game", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`Assets/Tests/Game/BootSmokeTests.cs`:
```csharp
using System.Collections;
using NUnit.Framework;
using ParcelFactory.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace ParcelFactory.Game.Tests
{
    public class BootSmokeTests
    {
        [UnityTest]
        public IEnumerator Boot_CreatesSessionAndTicks()
        {
            var boot = GameBootstrap.Ensure();
            yield return null;
            Assert.IsNotNull(boot.Session);
            Assert.IsNotNull(boot.Catalog, "PF_Catalog missing — run CatalogBuilder.BuildAll");
            float t0 = boot.Session.Spawner.Timer;
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(boot.Session.Spawner.Timer + boot.Session.Sim.Packages.Count * 3f, t0, "game loop did not tick");
            LogAssert.NoUnexpectedReceived();
            Object.Destroy(boot.gameObject);
            yield return null;
        }
    }
}
```

- [ ] **Step 7: 카탈로그·씬 생성 실행, 컴파일, 테스트**

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod ParcelFactory.EditorTools.CatalogBuilder.BuildAll -quit -logFile /tmp/pf-b1.log
grep -c "error CS" /tmp/pf-b1.log; grep -E "CATALOG entries|CATALOGBUILDER_DONE" /tmp/pf-b1.log
```
Expected: `0`, `CATALOG entries=53 missing=0`(누락이 있으면 그 이름을 Wanted에서 제거하거나 올바른 이름으로 고친다), `CATALOGBUILDER_DONE`. `Assets/Resources/PF_Catalog.asset`, `Assets/Scenes/Main.unity` 존재.

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/pf-b1-edit.xml -logFile /tmp/pf-b1-edit.log
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/pf-b1-edit.xml | head -1
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults /tmp/pf-b1-play.xml -logFile /tmp/pf-b1-play.log
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/pf-b1-play.xml | head -1
```
Expected: EditMode `total="121" passed="121" failed="0"`, PlayMode `total="1" passed="1" failed="0"`.

- [ ] **Step 8: 커밋**

```bash
git add Assets/Editor Assets/Resources Assets/Scenes Assets/Scripts/Game Assets/Tests/Game ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(game): model catalog, main scene, bootstrap, game loop, playmode smoke test"
```

---

### Task 2 (B2): 월드 — 지면·부지·울타리·숲·트럭·도크·조명·카메라·포스트

**Files:**
- Create: `Assets/Scripts/Game/WorldBuilder.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs` (Awake에서 WorldBuilder 추가·Build 호출)

**Interfaces:**
- Consumes: `ModelCatalog`, `Grid3D`, `MaterialLibrary`, `GameSession.Grid.UnlockedSize`, `UnlockService.Unlocked`.
- Produces: `class WorldBuilder : MonoBehaviour { void Build(GameSession); void RebuildLot(GameSession); Camera MainCamera }`.

- [ ] **Step 1: WorldBuilder 작성**

`Assets/Scripts/Game/WorldBuilder.cs`:
```csharp
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
            MainCamera.transform.position = new Vector3(0f, 17f, -9.5f);
            MainCamera.transform.LookAt(Vector3.zero);
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
            ground.GetComponent<Renderer>().material = MaterialLibrary.Lit(new Color(0.36f, 0.52f, 0.25f));
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
            Dock(docks, FactoryGrid.InletX, FactoryGrid.InletY, new Color(0.25f, 0.64f, 0.35f), "van", Grid3D.Tile(FactoryGrid.InletX, FactoryGrid.InletY) + new Vector3(-1.9f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            Dock(docks, FactoryGrid.OutletX, FactoryGrid.OutletY, new Color(0.24f, 0.48f, 0.88f), "delivery", Grid3D.Tile(FactoryGrid.OutletX, FactoryGrid.OutletY) + new Vector3(2.1f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
            Dock(docks, FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY, new Color(0.85f, 0.35f, 0.29f), "truck", Grid3D.Tile(FactoryGrid.ReturnDockX, FactoryGrid.ReturnDockY) + new Vector3(2.1f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
        }

        private void Dock(Transform parent, int gx, int gy, Color color, string vehicle, Vector3 vehiclePos, Quaternion vehicleRot)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "DockSlab";
            slab.transform.SetParent(parent, false);
            slab.transform.position = Grid3D.Tile(gx, gy) + new Vector3(0f, 0.11f, 0f);
            slab.transform.localScale = new Vector3(1f, 0.22f, 1f);
            slab.GetComponent<Renderer>().material = MaterialLibrary.Lit(color, 0.25f);
            Destroy(slab.GetComponent<Collider>());
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.transform.SetParent(parent, false);
            mark.transform.position = Grid3D.Tile(gx, gy) + new Vector3(0f, 0.23f, 0f);
            mark.transform.localScale = new Vector3(0.7f, 0.02f, 0.7f);
            mark.GetComponent<Renderer>().material = MaterialLibrary.Lit(Color.white, 0.2f);
            Destroy(mark.GetComponent<Collider>());
            Spawn(vehicle, vehiclePos, vehicleRot, 0.62f, parent);
            Spawn("warning-traffic", Grid3D.Tile(gx, gy) + new Vector3(0f, 0f, 0.55f), Quaternion.identity, 0.6f, parent);
        }

        public void RebuildLot(GameSession session)
        {
            if (lotRoot != null) Destroy(lotRoot.gameObject);
            lotRoot = new GameObject("Lot").transform;
            lotRoot.SetParent(root, false);
            int n = session.Grid.UnlockedSize;
            var rng = new System.Random(11);
            for (int gy = 0; gy < FactoryGrid.Height; gy++)
            {
                for (int gx = 0; gx < FactoryGrid.Width; gx++)
                {
                    var pos = Grid3D.Tile(gx, gy);
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
            // 울타리: 해금 영역 둘레
            for (int i = 0; i < n; i++)
            {
                Spawn("fence_simple", Grid3D.Tile(i, n - 1), Quaternion.Euler(0f, 0f, 0f), 1f, lotRoot);     // 북쪽 변 (+z)
                Spawn("fence_simple", Grid3D.Tile(i, 0), Quaternion.Euler(0f, 180f, 0f), 1f, lotRoot);        // 남쪽 변 (−z)
                Spawn("fence_simple", Grid3D.Tile(n - 1, i), Quaternion.Euler(0f, 90f, 0f), 1f, lotRoot);     // 동쪽 변 (+x)
                Spawn("fence_simple", Grid3D.Tile(0, i), Quaternion.Euler(0f, -90f, 0f), 1f, lotRoot);        // 서쪽 변 (−x)
            }
        }
    }
}
```
주의: `fence_simple`은 1m 길이이며 로컬 +z 가장자리(z ≈ +0.46)에 놓인다. 도크 칸의 울타리는 입고구(서쪽 변)와 출고구·반품구(동쪽 변, n ≥ 6일 때)에 걸리므로, 울타리 루프에서 `(gx, gy)`가 입고구·출고구·반품구 칸이면 그 변의 울타리를 건너뛴다. 구현: 각 `Spawn("fence_simple", ...)` 앞에 해당 칸이 세 도크 중 하나인지 검사해 `continue`하는 대신 조건문으로 감싼다(세 도크 좌표는 `FactoryGrid` 상수).

- [ ] **Step 2: Bootstrap에 연결**

`GameBootstrap.Awake`에서 `gameObject.AddComponent<GameLoop>();` 다음에:
```csharp
            World = gameObject.AddComponent<WorldBuilder>();
```
그리고 `Session = new GameSession();` **뒤, `SessionReplaced?.Invoke(Session);` 앞**에 `World.Build(Session);`를 둔다. `ReplaceSession`에서는 `if (World != null) World.Bind(s);`를 `SessionReplaced` 호출 전에 추가한다. 클래스에 `public WorldBuilder World { get; private set; }` 추가.

- [ ] **Step 3: 컴파일·스모크**

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -quit -logFile /tmp/pf-b2.log; grep -c "error CS" /tmp/pf-b2.log
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults /tmp/pf-b2-play.xml -logFile /tmp/pf-b2-play.log; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/pf-b2-play.xml | head -1
```
Expected: `0`, `total="1" passed="1" failed="0"`. 실패하면 `/tmp/pf-b2-play.log`에서 `Exception`을 찾는다(흔한 원인: 카탈로그 키 오타).

- [ ] **Step 4: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): world builder — ground, lot, fences, forest, docks, lighting, camera, post"
```

---

### Task 3 (B3): 뷰 — 셀 오브젝트, 벨트 화살표, 택배, 작업자, 커서, 해체 바

**Files:**
- Create: `Assets/Scripts/Game/CellViews.cs`, `PackageViews.cs`, `WorkerView.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs`

**Interfaces:**
- Consumes: 코어 `Cell`, `Package`(X, Y, Progress, Heading, EntryHeading, Inspected, Rejected), `WorkerService.Worker`, `FacingTile()`, `BuildService.CellChanged`, `Grid3D`, `MaterialLibrary`, `ModelCatalog`.
- Produces: `CellViews { void Bind(GameSession); void RebuildAll(); void Rebuild(int x,int y) }`, `PackageViews`(자동 동기화), `WorkerView { Transform CarryAnchor; Transform Model }`.
- 바닥 위 높이: 레일 윗면 0.4, 분배기 0.4, 검사대 윗면 0.6, 스캐너 벨트 0.4, 도크 슬래브 0.22.

- [ ] **Step 1: CellViews**

`Assets/Scripts/Game/CellViews.cs`:
```csharp
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
```

- [ ] **Step 2: PackageViews**

`Assets/Scripts/Game/PackageViews.cs`:
```csharp
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
```

- [ ] **Step 3: WorkerView (모델·보간·들고 있는 택배·커서)**

`Assets/Scripts/Game/WorkerView.cs`:
```csharp
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>작업자 모델을 코어 Worker 위치로 부드럽게 옮기고, 들고 있는 택배와 바라보는 칸 커서를 그린다.</summary>
    public sealed class WorkerView : MonoBehaviour
    {
        /// <summary>Kenney 캐릭터의 정면이 로컬 −z면 true로 바꾼다 (Play에서 확인).</summary>
        public static bool ModelFacesMinusZ = false;

        public Transform Model { get; private set; }
        public Transform CarryAnchor { get; private set; }

        private GameObject carried;
        private Transform cursor;
        private Renderer cursorRenderer;
        private Vector3 lastPos;
        private float bob;

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            Model = Instantiate(boot.Catalog.Get("character-male-a"), transform).transform;
            Model.localScale = Vector3.one * 1.6f;
            Model.localRotation = ModelFacesMinusZ ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            CarryAnchor = new GameObject("CarryAnchor").transform;
            CarryAnchor.SetParent(transform, false);
            CarryAnchor.localPosition = new Vector3(0f, 1.25f, 0f);
            carried = PackageViews.MakeBox(boot.Catalog, CarryAnchor);
            carried.SetActive(false);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Cursor";
            q.transform.SetParent(boot.WorldRoot, false);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = Vector3.one * 0.96f;
            cursorRenderer = q.GetComponent<Renderer>();
            cursorRenderer.material = MaterialLibrary.Transparent(new Color(1f, 0.83f, 0.4f, 0.35f));
            cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cursor = q.transform;

            var w = boot.Session.Worker.Worker;
            transform.position = lastPos = Grid3D.Tile(w.X, w.Y);
        }

        private void LateUpdate()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null || Model == null) return;
            var ws = boot.Session.Worker;
            var w = ws.Worker;
            var target = Grid3D.Tile(w.X, w.Y);
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Pow(0.0005f, Time.deltaTime));
            transform.rotation = Quaternion.Slerp(transform.rotation, Grid3D.Facing(w.Facing), 1f - Mathf.Pow(0.0005f, Time.deltaTime));

            bool moving = (transform.position - lastPos).sqrMagnitude > 1e-6f;
            lastPos = transform.position;
            bob = moving ? bob + Time.deltaTime * 14f : 0f;
            Model.localPosition = new Vector3(0f, moving ? Mathf.Abs(Mathf.Sin(bob)) * 0.06f : 0f, 0f);

            carried.SetActive(w.Carrying != null);
            if (w.Carrying != null) PackageViews.SetStamp(carried, w.Carrying.Inspected && w.Carrying.Rejected);

            var (fx, fy) = ws.FacingTile();
            bool show = boot.Session.Grid.InBounds(fx, fy) && boot.Session.Grid.Get(fx, fy).Type != CellType.Locked;
            cursor.gameObject.SetActive(show);
            if (show)
            {
                cursor.position = Grid3D.Tile(fx, fy) + new Vector3(0f, 0.03f + PackageViews.SurfaceHeight(boot.Session.Grid.Get(fx, fy).Type) * (boot.Session.Grid.Get(fx, fy).IsBuilding || boot.Session.Grid.Get(fx, fy).IsDock || boot.Session.Grid.Get(fx, fy).Type == CellType.Inlet ? 1f : 0f), 0f);
                var c = cursorRenderer.material.GetColor("_BaseColor");
                c.a = 0.22f + 0.16f * Mathf.Sin(Time.time * 6f);
                cursorRenderer.material.SetColor("_BaseColor", c);
            }
        }
    }
}
```

- [ ] **Step 4: Bootstrap에 연결**

`GameBootstrap.Awake`: `World = gameObject.AddComponent<WorldBuilder>();` 다음 줄에
```csharp
            Cells = gameObject.AddComponent<CellViews>();
            gameObject.AddComponent<PackageViews>();
```
`World.Build(Session);` 다음 줄(여전히 `SessionReplaced?.Invoke` 앞)에
```csharp
            Cells.Bind(Session);
            var workerGo = new GameObject("WorkerView");
            workerGo.transform.SetParent(WorldRoot, false);
            Worker = workerGo.AddComponent<WorkerView>();
```
`ReplaceSession`에서 `World.Bind(s)` 다음에 `if (Cells != null) Cells.Bind(s);`. 프로퍼티 `public CellViews Cells { get; private set; }`, `public WorkerView Worker { get; private set; }` 추가.

- [ ] **Step 5: 스모크 테스트 확장**

`BootSmokeTests.Boot_CreatesSessionAndTicks`의 `LogAssert.NoUnexpectedReceived();` 앞에 추가:
```csharp
            boot.Session.Build.TryPlace(1, 2, ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.East);
            yield return null;
            Assert.IsNotNull(GameObject.Find("World/Cells/Conveyor"), "cell view not created");
            Assert.IsNotNull(GameObject.Find("World/WorkerView"), "worker view not created");
```

- [ ] **Step 6: 컴파일·PlayMode**

B2 Step 3과 같은 두 명령(로그 이름만 `pf-b3`). Expected: `0`, PlayMode `total="1" passed="1" failed="0"`.

- [ ] **Step 7: 커밋**

```bash
git add Assets/Scripts/Game Assets/Tests/Game
git commit -m "feat(game): cell, package and worker views with belt arrows, cursor and demolish bars"
```

---

### Task 4 (B4): 입력 — Input System 액션 → 코어

**Files:**
- Create: `Assets/Scripts/Game/InputController.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs`

**Interfaces:**
- Consumes: `WorkerService.Move(vx, vy, dt)/Interact/TryDemolishFacing/CancelInspection/PendingInspection`, `Toolbar.OwnedTools()`. (Task 8이 먼저 끝나 있어야 한다.)
- Produces: `class InputController : MonoBehaviour { Tool? SelectedTool; IReadOnlyList<CellType> SlotTypes; int SelectedSlot; void SelectSlot(int i); void ClearTool(); void RotateTool(); void SetPad(Direction d, bool held); void Interact(); void Demolish(); bool UiBlocksInput (set by HUD); event Action<InteractResult> Interacted; event Action<BuildResult> DemolishRequested; event Action TogglePause, ToggleSpeed, ToggleShop, ToggleHelp, Escape; }`

- [ ] **Step 1: InputController 작성**

`Assets/Scripts/Game/InputController.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ParcelFactory.Game
{
    /// <summary>키보드·게임패드·화면 버튼 입력을 코어 호출로 바꾼다. 핫바는 부품 종류당 한 칸, 방향은 R로 순환.</summary>
    public sealed class InputController : MonoBehaviour
    {
        public static readonly CellType[] SlotOrder = { CellType.Conveyor, CellType.FastConveyor, CellType.Table, CellType.Splitter, CellType.Scanner };

        public Tool? SelectedTool { get; private set; }
        public int SelectedSlot { get; private set; } = -1;
        public bool UiBlocksInput { get; set; }

        public event Action<InteractResult> Interacted;
        public event Action<BuildResult> DemolishRequested;
        public event Action TogglePause, ToggleSpeed, ToggleShop, ToggleHelp, Escape, ToolChanged;

        private InputAction move, interact, demolish, rotate, clear, pause, speed, shop, help, escape;
        private readonly InputAction[] slots = new InputAction[5];
        private readonly Dictionary<CellType, Direction> lastDir = new Dictionary<CellType, Direction>();
        private readonly HashSet<Direction> padHeld = new HashSet<Direction>(); // 화면 D-패드: 누르고 있는 방향
        private const float MoveSubStep = 0.05f;

        private void Awake()
        {
            move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/a").With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");
            move.AddBinding("<Gamepad>/dpad");
            interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth");
            demolish = Button("Demolish", "<Keyboard>/x", "<Gamepad>/buttonWest");
            rotate = Button("Rotate", "<Keyboard>/r", "<Gamepad>/buttonNorth");
            clear = Button("Clear", "<Keyboard>/q", "<Gamepad>/buttonEast");
            pause = Button("Pause", "<Keyboard>/p", "<Gamepad>/start");
            speed = Button("Speed", "<Keyboard>/t", null);
            shop = Button("Shop", "<Keyboard>/b", "<Gamepad>/select");
            help = Button("Help", "<Keyboard>/o", null);
            escape = Button("Escape", "<Keyboard>/escape", null);
            for (int i = 0; i < 5; i++) slots[i] = Button("Slot" + (i + 1), "<Keyboard>/" + (i + 1), null);
        }

        private static InputAction Button(string name, string key, string pad)
        {
            var a = new InputAction(name, InputActionType.Button, key);
            if (pad != null) a.AddBinding(pad);
            return a;
        }

        private void OnEnable()
        {
            foreach (var a in All()) a.Enable();
        }

        private void OnDisable()
        {
            foreach (var a in All()) a.Disable();
        }

        private IEnumerable<InputAction> All()
        {
            yield return move; yield return interact; yield return demolish; yield return rotate; yield return clear;
            yield return pause; yield return speed; yield return shop; yield return help; yield return escape;
            foreach (var s in slots) yield return s;
        }

        private GameSession S => GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;

        private void Update()
        {
            if (S == null) return;
            if (escape.WasPressedThisFrame()) Escape?.Invoke();
            if (UiBlocksInput) return;
            if (pause.WasPressedThisFrame()) TogglePause?.Invoke();
            if (speed.WasPressedThisFrame()) ToggleSpeed?.Invoke();
            if (shop.WasPressedThisFrame()) ToggleShop?.Invoke();
            if (help.WasPressedThisFrame()) ToggleHelp?.Invoke();
            for (int i = 0; i < 5; i++) if (slots[i].WasPressedThisFrame()) SelectSlot(i);
            if (rotate.WasPressedThisFrame()) RotateTool();
            if (clear.WasPressedThisFrame()) ClearTool();
            if (interact.WasPressedThisFrame()) Interact();
            if (demolish.WasPressedThisFrame()) Demolish();

            var v = move.ReadValue<Vector2>();
            foreach (var d in padHeld) v += new Vector2(d.Dx(), d.Dy());
            if (v.sqrMagnitude > 0.04f) for (float left = Time.deltaTime; left > 0f; left -= MoveSubStep) S.Worker.Move(v.x, v.y, Mathf.Min(left, MoveSubStep)); // 서브스텝: 프레임 스파이크에도 1칸 장애물을 뚫지 않는다. 코어가 정규화·충돌·Facing·CancelInspection을 처리한다
        }

        public void RequestShop() => ToggleShop?.Invoke();
        public void RequestHelp() => ToggleHelp?.Invoke();

        /// <summary>화면 D-패드용. 누르는 동안 held=true, 떼면 false. 이동 자체는 Update에서 매 프레임 적용된다.</summary>
        public void SetPad(Direction d, bool held)
        {
            if (held) padHeld.Add(d); else padHeld.Remove(d);
        }

        private void OnApplicationFocus(bool focus) { if (!focus) padHeld.Clear(); }

        public void Interact()
        {
            if (S == null) return;
            var r = S.Worker.Interact(SelectedTool);
            Interacted?.Invoke(r);
        }

        public void Demolish()
        {
            if (S == null) return;
            DemolishRequested?.Invoke(S.Worker.TryDemolishFacing());
        }

        /// <summary>보유 방향이 하나라도 있는 종류만 슬롯이 된다.</summary>
        public List<CellType> AvailableSlots()
        {
            var owned = S.Toolbar.OwnedTools().ToList();
            return SlotOrder.Where(t => owned.Any(o => o.Type == t)).ToList();
        }

        public List<Direction> OwnedDirections(CellType t) =>
            S.Toolbar.OwnedTools().Where(o => o.Type == t).Select(o => o.Direction).ToList();

        public void SelectSlot(int i)
        {
            var slots = AvailableSlots();
            if (i < 0 || i >= slots.Count) return;
            if (SelectedSlot == i) { ClearTool(); return; }
            var type = slots[i];
            var dirs = OwnedDirections(type);
            var dir = lastDir.TryGetValue(type, out var d) && dirs.Contains(d) ? d : dirs[0];
            SelectedSlot = i;
            SelectedTool = new Tool(type, dir);
            ToolChanged?.Invoke();
        }

        public void ClearTool()
        {
            SelectedSlot = -1;
            SelectedTool = null;
            ToolChanged?.Invoke();
        }

        public void RotateTool()
        {
            if (!SelectedTool.HasValue) return;
            var t = SelectedTool.Value;
            var dirs = OwnedDirections(t.Type);
            if (dirs.Count == 0) { ClearTool(); return; }
            int idx = dirs.IndexOf(t.Direction);
            var next = dirs[(idx + 1) % dirs.Count];
            lastDir[t.Type] = next;
            SelectedTool = new Tool(t.Type, next);
            ToolChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 2: Bootstrap에 연결**

`GameBootstrap.Awake`에서 `Cells = gameObject.AddComponent<CellViews>();` 다음에 `Input = gameObject.AddComponent<InputController>();`, 프로퍼티 `public InputController Input { get; private set; }`. `ReplaceSession`에서 `Input?.ClearTool();`를 호출한다.

이동이 연속이므로 B5의 `Interacted` 처리에서 `InteractResult.Standing`은 토스트 "서 있는 자리에는 놓을 수 없습니다"로 보여준다. B5의 터치 D-패드는 `onClick` 대신 `EventTrigger`의 PointerDown/PointerUp/PointerExit로 `input.SetPad(d, true/false)`를 호출한다.

- [ ] **Step 3: 컴파일·PlayMode**

B2 Step 3과 같은 명령(`pf-b4`). Expected: `0`, PlayMode 1/1.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): input system actions, per-type hotbar with direction cycling"
```

---

### Task 5 (B5): HUD — UGUI 코드 생성 (상단 바·핫바·프롬프트·상태·토스트·조작·터치 컨트롤)

**Files:**
- Create: `Assets/Scripts/Game/UiKit.cs`, `Assets/Scripts/Game/HudController.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs`

**Interfaces:**
- Consumes: `InputController`(SelectedTool/SelectedSlot/AvailableSlots/OwnedDirections/events), `WorkerService.Preview`, `ConveyorSim.DeliveredCount/LostCount/AwaitingInspectionCount`, `WorldBuilder.MainCamera`.
- Produces: `UiKit`(헬퍼), `HudController { void Toast(string); RectTransform Root; Font Font; bool TouchMode }`, 패널(B6)이 쓸 `UiKit.Panel/Label/Button`.

- [ ] **Step 1: UiKit**

`Assets/Scripts/Game/UiKit.cs`:
```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>UGUI를 코드로 만든다. 모든 색은 여기 상수에서 나온다.</summary>
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.06f, 0.08f, 0.09f, 0.74f);
        public static readonly Color Panel2 = new Color(0.06f, 0.08f, 0.09f, 0.92f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color Fg = new Color(0.95f, 0.96f, 0.94f);
        public static readonly Color Muted = new Color(0.73f, 0.75f, 0.71f);
        public static readonly Color Accent = new Color(1f, 0.72f, 0.29f);
        public static readonly Color AccentFg = new Color(0.14f, 0.1f, 0.02f);
        public static readonly Color Good = new Color(0.37f, 0.83f, 0.48f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);

        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas MakeCanvas()
        {
            var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 800f);
            scaler.matchWidthOrHeight = 0.5f;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(go.transform, false);
            }
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
            return rt;
        }

        public static Image PanelBox(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color? color = null)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color ?? Panel;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style;
            t.text = text; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string text, Vector2 size, System.Action onClick, Color? bg = null, Color? fg = null, int fontSize = 14)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg ?? new Color(0.15f, 0.17f, 0.2f, 0.9f);
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f); colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); b.colors = colors;
            b.onClick.AddListener(() => onClick?.Invoke());
            var label = Label("Label", rt, text, fontSize, fg ?? Fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.offsetMin = new Vector2(6f, 2f); label.rectTransform.offsetMax = new Vector2(-6f, -2f);
            return b;
        }

        public static void SetButtonLook(Button b, bool on)
        {
            b.GetComponent<Image>().color = on ? Accent : new Color(0.15f, 0.17f, 0.2f, 0.9f);
            b.GetComponentInChildren<Text>().color = on ? AccentFg : Fg;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing, RectOffset padding)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing; g.padding = padding; g.childAlignment = TextAnchor.MiddleLeft;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false; g.childControlWidth = false; g.childControlHeight = false;
            return g;
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing, RectOffset padding)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing; g.padding = padding; g.childAlignment = TextAnchor.UpperLeft;
            g.childForceExpandWidth = true; g.childForceExpandHeight = false; g.childControlWidth = true; g.childControlHeight = false;
            return g;
        }

        public static LayoutElement Size(Component c, float w, float h)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = h; le.minWidth = w; le.minHeight = h;
            return le;
        }
    }
}
```

- [ ] **Step 2: HudController**

`Assets/Scripts/Game/HudController.cs`:
```csharp
using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>상단 바, 핫바, 프롬프트, 작업자 상태, 토스트, 조작 설명, 터치 컨트롤.</summary>
    public sealed class HudController : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public bool TouchMode { get; private set; }

        private Text money, delivered, lost, waiting, workerText, promptText, toastText, helpText;
        private Button pauseBtn, speedBtn, shopBtn, helpBtn;
        private RectTransform hotbar, prompt, toast, help, touchPad, touchButtons;
        private readonly List<Button> slotButtons = new List<Button>();
        private readonly List<Text> slotArrows = new List<Text>();
        private float toastUntil;
        private InputController input;

        private static readonly Dictionary<CellType, string> Names = new Dictionary<CellType, string>
        {
            { CellType.Conveyor, "레일" }, { CellType.FastConveyor, "고속 레일" }, { CellType.Table, "검사대" }, { CellType.Splitter, "분배기" }, { CellType.Scanner, "자동 검사기" },
            { CellType.Inlet, "입고구" }, { CellType.Outlet, "출고구" }, { CellType.ReturnDock, "반품구" }, { CellType.Empty, "빈 칸" }, { CellType.Locked, "잠긴 칸" },
        };
        public static string Name(CellType t) => Names.TryGetValue(t, out var n) ? n : t.ToString();
        public static string Arrow(Direction d) => d == Direction.North ? "↑" : d == Direction.East ? "→" : d == Direction.South ? "↓" : "←";

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input;
            TouchMode = Application.isMobilePlatform || UnityEngine.InputSystem.Touchscreen.current != null;
            var canvas = UiKit.MakeCanvas();
            canvas.transform.SetParent(transform, false);
            Root = canvas.GetComponent<RectTransform>();
            BuildTopLeft(); BuildTopRight(); BuildHotbar(); BuildPrompt(); BuildStatus(); BuildToast(); BuildHelp();
            if (TouchMode) BuildTouchControls();
            input.ToolChanged += RefreshHotbar;
            input.Interacted += OnInteracted;
            input.DemolishRequested += r => { if (r != BuildResult.Ok) Toast(r == BuildResult.AlreadyDemolishing ? "이미 해체 중입니다" : "해체할 수 있는 부품이 아닙니다"); };
            input.TogglePause += () => { boot.Session.Paused = !boot.Session.Paused; };
            input.ToggleSpeed += () => { boot.Session.Speed = boot.Session.Speed >= 2f ? 1f : 2f; };
            input.ToggleHelp += () => help.gameObject.SetActive(!help.gameObject.activeSelf);
            boot.SessionReplaced += s => { s.Unlocks.Unlocked += _ => RefreshHotbar(); RefreshHotbar(); };
            boot.Session.Unlocks.Unlocked += _ => RefreshHotbar();
            RefreshHotbar();
        }

        private void BuildTopLeft()
        {
            var box = UiKit.PanelBox("TopLeft", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(300f, 74f));
            money = UiKit.Label("Money", box.transform, "100", 28, UiKit.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            money.rectTransform.offsetMin = new Vector2(14f, 30f); money.rectTransform.offsetMax = new Vector2(-14f, -6f);
            var lbl = UiKit.Label("MoneyLbl", box.transform, "잔액", 12, UiKit.Muted, TextAnchor.UpperLeft);
            lbl.rectTransform.offsetMin = new Vector2(110f, 36f); lbl.rectTransform.offsetMax = new Vector2(-14f, -14f);
            delivered = UiKit.Label("Delivered", box.transform, "배송 0", 13, UiKit.Good, TextAnchor.LowerLeft);
            delivered.rectTransform.offsetMin = new Vector2(14f, 8f); delivered.rectTransform.offsetMax = new Vector2(-200f, -40f);
            lost = UiKit.Label("Lost", box.transform, "소실 0", 13, UiKit.Bad, TextAnchor.LowerLeft);
            lost.rectTransform.offsetMin = new Vector2(100f, 8f); lost.rectTransform.offsetMax = new Vector2(-120f, -40f);
            waiting = UiKit.Label("Waiting", box.transform, "검사 대기 0", 13, UiKit.Fg, TextAnchor.LowerLeft);
            waiting.rectTransform.offsetMin = new Vector2(180f, 8f); waiting.rectTransform.offsetMax = new Vector2(-10f, -40f);
        }

        private void BuildTopRight()
        {
            var row = UiKit.Rect("TopRight", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(420f, 40f));
            UiKit.Row(row, 6f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            var boot = GameBootstrap.Instance;
            pauseBtn = UiKit.Button("Pause", row, "일시정지 P", new Vector2(100f, 36f), () => boot.Session.Paused = !boot.Session.Paused);
            speedBtn = UiKit.Button("Speed", row, "1x T", new Vector2(64f, 36f), () => boot.Session.Speed = boot.Session.Speed >= 2f ? 1f : 2f);
            shopBtn = UiKit.Button("Shop", row, "상점 B", new Vector2(80f, 36f), () => input.RequestShop());
            helpBtn = UiKit.Button("Help", row, "조작 O", new Vector2(80f, 36f), () => help.gameObject.SetActive(!help.gameObject.activeSelf));
            foreach (var b in new[] { pauseBtn, speedBtn, shopBtn, helpBtn }) UiKit.Size(b, b.GetComponent<RectTransform>().sizeDelta.x, 36f);
        }

        private void BuildHotbar()
        {
            hotbar = UiKit.PanelBox("Hotbar", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(5 * 70f + 16f, 76f)).rectTransform;
            UiKit.Row(hotbar, 6f, new RectOffset(8, 8, 8, 8)).childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var b = UiKit.Button("Slot" + (i + 1), hotbar, "", new Vector2(64f, 60f), () => input.SelectSlot(idx), new Color(0f, 0f, 0f, 0.35f));
                UiKit.Size(b, 64f, 60f);
                var key = UiKit.Label("Key", b.transform, (i + 1).ToString(), 11, UiKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
                key.rectTransform.offsetMin = new Vector2(5f, 0f); key.rectTransform.offsetMax = new Vector2(0f, -3f);
                var arrow = UiKit.Label("Arrow", b.transform, "", 16, UiKit.Accent, TextAnchor.UpperRight, FontStyle.Bold);
                arrow.rectTransform.offsetMin = new Vector2(0f, 0f); arrow.rectTransform.offsetMax = new Vector2(-5f, -2f);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.LowerCenter;
                slotButtons.Add(b); slotArrows.Add(arrow);
            }
        }

        private void BuildPrompt()
        {
            prompt = UiKit.PanelBox("Prompt", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(170f, 32f), UiKit.Panel2).rectTransform;
            promptText = UiKit.Label("Text", prompt, "", 14, UiKit.Fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            prompt.gameObject.SetActive(false);
        }

        private void BuildStatus()
        {
            var box = UiKit.PanelBox("Status", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(420f, 26f));
            workerText = UiKit.Label("Text", box.transform, "", 12, UiKit.Muted, TextAnchor.MiddleCenter);
        }

        private void BuildToast()
        {
            toast = UiKit.PanelBox("Toast", Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(360f, 34f), UiKit.Panel2).rectTransform;
            toastText = UiKit.Label("Text", toast, "", 14, UiKit.Fg, TextAnchor.MiddleCenter);
            toast.gameObject.SetActive(false);
        }

        private void BuildHelp()
        {
            help = UiKit.PanelBox("Help", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -60f), new Vector2(330f, 150f)).rectTransform;
            helpText = UiKit.Label("Text", help, "WASD/방향키 이동 · E 집기·놓기·설치·검사 · X 해체\n1~5 도구 선택, 같은 키 다시 누르면 빈손 · R 방향 · Q 빈손\n검사대 위 택배는 검사 전까지 멈춥니다.\n정상은 출고구, 불량은 반품구로 보내세요.\n입고 3초마다 1개 · 배송 +15 · 해체 2초 후 80% 환불", 12, UiKit.Muted, TextAnchor.UpperLeft);
            helpText.rectTransform.offsetMin = new Vector2(12f, 8f); helpText.rectTransform.offsetMax = new Vector2(-12f, -8f);
            help.gameObject.SetActive(false);
        }

        private void BuildTouchControls()
        {
            touchPad = UiKit.Rect("TouchPad", Root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(16f, 110f), new Vector2(170f, 170f));
            void Pad(string txt, Vector2 pos, Direction d) { var b = UiKit.Button("Pad" + txt, touchPad, txt, new Vector2(52f, 52f), null, null, null, 20); b.GetComponent<RectTransform>().anchoredPosition = pos; var et = b.AddComponent<UnityEngine.EventSystems.EventTrigger>(); void On(UnityEngine.EventSystems.EventTriggerType t, bool held) { var e = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = t }; e.callback.AddListener(_ => input.SetPad(d, held)); et.triggers.Add(e); } On(UnityEngine.EventSystems.EventTriggerType.PointerDown, true); On(UnityEngine.EventSystems.EventTriggerType.PointerUp, false); On(UnityEngine.EventSystems.EventTriggerType.PointerExit, false); }
            Pad("↑", new Vector2(85f, 140f), Direction.North); Pad("←", new Vector2(28f, 85f), Direction.West); Pad("→", new Vector2(142f, 85f), Direction.East); Pad("↓", new Vector2(85f, 30f), Direction.South);
            touchButtons = UiKit.Rect("TouchButtons", Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 110f), new Vector2(200f, 170f));
            var e = UiKit.Button("TouchE", touchButtons, "E", new Vector2(84f, 84f), () => input.Interact(), UiKit.Accent, UiKit.AccentFg, 20); e.GetComponent<RectTransform>().anchoredPosition = new Vector2(150f, 60f);
            var x = UiKit.Button("TouchX", touchButtons, "해체", new Vector2(62f, 62f), () => input.Demolish(), new Color(0.55f, 0.2f, 0.16f, 0.92f), null, 14); x.GetComponent<RectTransform>().anchoredPosition = new Vector2(62f, 40f);
            var r = UiKit.Button("TouchR", touchButtons, "방향", new Vector2(62f, 62f), () => input.RotateTool(), null, null, 14); r.GetComponent<RectTransform>().anchoredPosition = new Vector2(62f, 118f);
            hotbar.anchoredPosition = new Vector2(0f, 14f);
        }

        public void Toast(string message)
        {
            toastText.text = message;
            toastUntil = Time.unscaledTime + 1.8f;
            toast.gameObject.SetActive(true);
        }

        private void OnInteracted(InteractResult r)
        {
            switch (r)
            {
                case InteractResult.NoFloor: Toast("바닥이 없는 곳입니다"); break;
                case InteractResult.CannotPutHere: Toast("여기에는 내려놓을 수 없습니다"); break;
                case InteractResult.NoTool: Toast("도구를 고르세요 (1~5)"); break;
                case InteractResult.Locked: Toast("아직 사지 않은 부지입니다"); break;
                case InteractResult.Occupied: Toast("이미 무언가 있습니다"); break;
                case InteractResult.NotOwned: Toast("해금되지 않은 방향입니다"); break;
                case InteractResult.NotEnoughMoney: Toast("잔액이 부족합니다"); break;
                case InteractResult.Standing: Toast("서 있는 자리에는 놓을 수 없습니다"); break;
            }
        }

        public void RefreshHotbar()
        {
            var slots = input.AvailableSlots();
            for (int i = 0; i < slotButtons.Count; i++)
            {
                bool has = i < slots.Count;
                slotButtons[i].gameObject.SetActive(has);
                if (!has) continue;
                var type = slots[i];
                slotButtons[i].GetComponentInChildren<Text>().text = Name(type) + "\n" + BuildingCosts.Cost(type);
                bool on = input.SelectedSlot == i && input.SelectedTool.HasValue;
                UiKit.SetButtonLook(slotButtons[i], on);
                slotArrows[i].text = on ? Arrow(input.SelectedTool.Value.Direction) : (input.OwnedDirections(type).Count > 1 ? "R" : Arrow(input.OwnedDirections(type)[0]));
            }
        }

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null) return;
            var s = boot.Session;
            money.text = s.Economy.Money.ToString();
            delivered.text = "배송 " + s.Sim.DeliveredCount;
            lost.text = "소실 " + s.Sim.LostCount;
            waiting.text = "검사 대기 " + s.Sim.AwaitingInspectionCount;
            UiKit.SetButtonLook(pauseBtn, s.Paused);
            speedBtn.GetComponentInChildren<Text>().text = (s.Speed >= 2f ? "2x" : "1x") + " T";
            UiKit.SetButtonLook(speedBtn, s.Speed >= 2f);

            var w = s.Worker.Worker;
            workerText.text = w.Carrying != null ? ("택배 #" + w.Carrying.Id + " 운반 중 · 레일이나 도크를 보고 E")
                : input.SelectedTool.HasValue ? (Name(input.SelectedTool.Value.Type) + " " + Arrow(input.SelectedTool.Value.Direction) + " 들고 있음 · E로 설치, R로 방향")
                : "빈손 · 1~5로 도구 선택";

            // 프롬프트: Preview 결과로 문구를 만들고 바라보는 칸 위에 띄운다.
            var r = s.Worker.Preview(input.SelectedTool);
            var (fx, fy) = s.Worker.FacingTile();
            string label = null;
            switch (r)
            {
                case InteractResult.PickedUp: label = "집기"; break;
                case InteractResult.PutDown: label = "놓기"; break;
                case InteractResult.Delivered: label = s.Grid.Get(fx, fy).Type == CellType.Outlet ? "출고" : "반품"; break;
                case InteractResult.InspectionRequired: label = "검사"; break;
                case InteractResult.Placed: label = Name(input.SelectedTool.Value.Type) + " 설치"; break;
            }
            if (label != null && boot.World != null && boot.World.MainCamera != null)
            {
                var sp = boot.World.MainCamera.WorldToScreenPoint(Grid3D.Tile(fx, fy) + new Vector3(0f, 0.9f, 0f));
                var canvasRt = Root;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var lp);
                prompt.anchoredPosition = lp + canvasRt.rect.size * 0.5f;
                promptText.text = label + "   [E]";
                prompt.gameObject.SetActive(true);
            }
            else prompt.gameObject.SetActive(false);

            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
        }
    }
}
```

- [ ] **Step 3: Bootstrap에 연결**

`GameBootstrap.Awake`에서 `Worker = workerGo.AddComponent<WorkerView>();` 다음에:
```csharp
            Hud = gameObject.AddComponent<HudController>();
```
프로퍼티 `public HudController Hud { get; private set; }`.

- [ ] **Step 4: 컴파일·PlayMode** (`pf-b5`). Expected: `0`, 1/1. 스모크 테스트에 `Assert.IsNotNull(GameObject.Find("HUD"));`를 추가한다.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Game Assets/Tests/Game
git commit -m "feat(game): UGUI hud — top bar, hotbar, prompt, status, toast, help, touch controls"
```

---

### Task 6 (B6): 패널(상점·검사 카드·해체 확인)과 저장/불러오기

**Files:**
- Create: `Assets/Scripts/Game/Panels.cs`, `Assets/Scripts/Game/SaveFile.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs`, `Assets/Scripts/Game/HudController.cs` (상단 바에 저장/불러오기/새 게임 버튼; 조작 서랍의 입력 차단을 `SetBlock("help", …)`로 전환), `Assets/Scripts/Game/InputController.cs` (`SetBlock`)

**입력 차단 규칙(B5 리뷰 판정):** `InputController.UiBlocksInput`은 더 이상 대입하지 않는다. `public void SetBlock(string owner, bool on)`을 추가해 차단 주체 집합(HashSet<string>)을 관리하고, `UiBlocksInput => blockers.Count > 0`으로 바꾼다. 조작 서랍은 `SetBlock("help", open)`, 패널은 `SetBlock("panels", AnyOpen)`을 상태가 바뀔 때 호출한다. 패널이 열릴 때 `input.ClearPad()`도 호출한다. 핫바 슬롯·터치 E/X/R·상점 버튼이 `UiBlocksInput`을 우회하지 않도록, `HudController`의 그 핸들러들은 `if (input.UiBlocksInput) return;`으로 시작한다(Escape·패널 자체 버튼은 예외).

**Interfaces:**
- Consumes: `UnlockCatalog.All`, `UnlockService.TryPurchase/IsPurchased`, `WorkerService.PendingInspection/Inspect/CancelInspection`, `BuildService.BeginDemolish/RefundFor`, `SaveSerializer`, `InputController.Interacted/DemolishRequested/ToggleShop/Escape`, `HudController.Toast`.
- Produces: `Panels { bool AnyOpen }`, `SaveFile { bool TrySave(GameSession, out string err); bool TryLoad(out GameSession, out string err) }`.

- [ ] **Step 1: SaveFile**

`Assets/Scripts/Game/SaveFile.cs`:
```csharp
using System;
using System.IO;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    public static class SaveFile
    {
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "save.json");

        /// <summary>임시 파일에 쓴 뒤 교체한다. 실패해도 기존 파일은 남는다.</summary>
        public static bool TrySave(GameSession session, out string error)
        {
            error = null;
            try
            {
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, SaveSerializer.ToJson(SaveSerializer.Capture(session)));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(tmp, Path);
                return true;
            }
            catch (Exception e) { error = "저장 실패: " + e.GetType().Name; return false; }
        }

        public static bool TryLoad(out GameSession session, out string error)
        {
            session = null; error = null;
            if (!File.Exists(Path)) { error = "저장 파일이 없습니다"; return false; }
            try { session = SaveSerializer.Restore(SaveSerializer.FromJson(File.ReadAllText(Path))); return true; }
            catch (SaveVersionException e) { error = "저장 파일을 읽을 수 없습니다: " + e.Message; return false; }
            catch (Exception e) { error = "저장 파일이 손상되었습니다: " + e.GetType().Name; return false; }
        }
    }
}
```

- [ ] **Step 2: Panels**

`Assets/Scripts/Game/Panels.cs`:
```csharp
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>상점, 검사 카드, 해체 확인. 열려 있으면 월드 입력을 막는다.</summary>
    public sealed class Panels : MonoBehaviour
    {
        private RectTransform shop, inspect, demolish, shopList;
        private Text iDest, iWeight, iMethod, iScale, iId, dText, dRefund, shopMoney;
        private (int x, int y)? pendingDemolish;
        private InputController input;
        private HudController hud;

        public bool AnyOpen => (shop != null && shop.gameObject.activeSelf) || (inspect != null && inspect.gameObject.activeSelf) || (demolish != null && demolish.gameObject.activeSelf);
        private bool lastAnyOpen;

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input; hud = boot.Hud;
            BuildShop(); BuildInspect(); BuildDemolish();
            input.ToggleShop += () => { if (inspect.gameObject.activeSelf || demolish.gameObject.activeSelf) return; shop.gameObject.SetActive(!shop.gameObject.activeSelf); RefreshShop(); };
            input.Escape += CloseAll;
            input.Interacted += r => { if (r == InteractResult.InspectionRequired) OpenInspect(); };
            input.DemolishRequested += OnDemolishRequested;
            boot.SessionReplaced += _ => CloseAll();
        }

        private void Update()
        {
            if (AnyOpen != lastAnyOpen) { lastAnyOpen = AnyOpen; input.SetBlock("panels", AnyOpen); if (AnyOpen) input.ClearPad(); }
            if (inspect.gameObject.activeSelf && GameBootstrap.Instance.Session.Worker.PendingInspection == null) inspect.gameObject.SetActive(false);
        }

        private void CloseAll()
        {
            shop.gameObject.SetActive(false);
            if (inspect.gameObject.activeSelf) { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); }
            demolish.gameObject.SetActive(false); pendingDemolish = null;
        }

        private RectTransform Card(string name, Vector2 size, string title)
        {
            var dim = UiKit.PanelBox(name, hud.Root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.35f)).rectTransform;
            var card = UiKit.PanelBox("Card", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, UiKit.Panel2).rectTransform;
            var t = UiKit.Label("Title", card, title, 16, UiKit.Fg, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.offsetMin = new Vector2(18f, 0f); t.rectTransform.offsetMax = new Vector2(-18f, -14f);
            dim.gameObject.SetActive(false);
            return dim;
        }

        private void BuildShop()
        {
            shop = UiKit.PanelBox("Shop", hud.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -60f), new Vector2(340f, 470f), UiKit.Panel2).rectTransform;
            var title = UiKit.Label("Title", shop, "해금 상점", 16, UiKit.Fg, TextAnchor.UpperLeft, FontStyle.Bold);
            title.rectTransform.offsetMin = new Vector2(16f, 0f); title.rectTransform.offsetMax = new Vector2(-16f, -12f);
            shopMoney = UiKit.Label("Money", shop, "", 12, UiKit.Muted, TextAnchor.UpperRight);
            shopMoney.rectTransform.offsetMin = new Vector2(16f, 0f); shopMoney.rectTransform.offsetMax = new Vector2(-16f, -14f);
            shopList = UiKit.Rect("List", shop, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(-24f, -56f));
            UiKit.Column(shopList, 4f, new RectOffset(0, 0, 0, 0));
            foreach (var def in UnlockCatalog.All)
            {
                var row = UiKit.Rect("Row", shopList, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 34f));
                UiKit.Size(row, 300f, 34f);
                var name = UiKit.Label("Name", row, def.Name, 13, UiKit.Fg, TextAnchor.MiddleLeft);
                name.rectTransform.offsetMin = new Vector2(4f, 0f); name.rectTransform.offsetMax = new Vector2(-110f, 0f);
                var id = def.Id;
                var b = UiKit.Button("Buy", row, def.Price.ToString(), new Vector2(96f, 30f), () => { var r = GameBootstrap.Instance.Session.Unlocks.TryPurchase(id); hud.Toast(r == UnlockResult.Ok ? def.Name + " 해금" : r == UnlockResult.NotEnoughMoney ? "잔액이 부족합니다" : r == UnlockResult.RequirementMissing ? "먼저 해금해야 하는 항목이 있습니다" : "이미 보유 중입니다"); RefreshShop(); });
                var brt = b.GetComponent<RectTransform>(); brt.anchorMin = new Vector2(1f, 0.5f); brt.anchorMax = new Vector2(1f, 0.5f); brt.pivot = new Vector2(1f, 0.5f); brt.anchoredPosition = new Vector2(-4f, 0f);
            }
            shop.gameObject.SetActive(false);
        }

        private void RefreshShop()
        {
            var s = GameBootstrap.Instance.Session;
            shopMoney.text = "잔액 " + s.Economy.Money;
            int i = 0;
            foreach (var def in UnlockCatalog.All)
            {
                var row = shopList.GetChild(i++);
                var b = row.Find("Buy").GetComponent<Button>();
                bool owned = s.Unlocks.IsPurchased(def.Id), blocked = def.Requires.HasValue && !s.Unlocks.IsPurchased(def.Requires.Value), afford = s.Economy.CanAfford(def.Price);
                b.GetComponentInChildren<Text>().text = owned ? "보유" : blocked ? "잠김" : def.Price.ToString();
                b.interactable = !owned && !blocked && afford;
                UiKit.SetButtonLook(b, !owned && !blocked && afford);
            }
        }

        private void BuildInspect()
        {
            inspect = Card("Inspect", new Vector2(380f, 300f), "택배 검사");
            var card = inspect.Find("Card");
            iId = UiKit.Label("Id", card, "", 12, UiKit.Muted, TextAnchor.UpperRight); iId.rectTransform.offsetMin = new Vector2(18f, 0f); iId.rectTransform.offsetMax = new Vector2(-18f, -16f);
            var paper = UiKit.PanelBox("Paper", card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(-36f, 140f), new Color(0.97f, 0.95f, 0.9f)).rectTransform;
            Text Row(string n, string lbl, float y, Color c, bool bold = false)
            {
                var l = UiKit.Label(n + "L", paper, lbl, 13, new Color(0.48f, 0.43f, 0.36f), TextAnchor.MiddleLeft); l.rectTransform.anchorMin = new Vector2(0f, 1f); l.rectTransform.anchorMax = new Vector2(1f, 1f); l.rectTransform.pivot = new Vector2(0.5f, 1f); l.rectTransform.anchoredPosition = new Vector2(0f, y); l.rectTransform.sizeDelta = new Vector2(-28f, 26f);
                var v = UiKit.Label(n, paper, "", 13, c, TextAnchor.MiddleRight, bold ? FontStyle.Bold : FontStyle.Normal); v.rectTransform.anchorMin = new Vector2(0f, 1f); v.rectTransform.anchorMax = new Vector2(1f, 1f); v.rectTransform.pivot = new Vector2(0.5f, 1f); v.rectTransform.anchoredPosition = new Vector2(0f, y); v.rectTransform.sizeDelta = new Vector2(-28f, 26f);
                return v;
            }
            var dark = new Color(0.16f, 0.15f, 0.12f);
            iDest = Row("Dest", "목적지", -8f, dark, true); iWeight = Row("Weight", "표기 무게", -38f, dark, true); iMethod = Row("Method", "배송 방법", -68f, dark, true); iScale = Row("Scale", "저울 실측", -104f, new Color(0.79f, 0.28f, 0.18f), true);
            var hint = UiKit.Label("Hint", card, "표기와 실측이 다르면 거부하세요. 거부한 택배는 반품구로 보내야 합니다.", 12, UiKit.Muted, TextAnchor.UpperLeft);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f); hint.rectTransform.pivot = new Vector2(0.5f, 0f); hint.rectTransform.anchoredPosition = new Vector2(0f, 58f); hint.rectTransform.sizeDelta = new Vector2(-36f, 40f);
            var reject = UiKit.Button("Reject", card, "거부", new Vector2(110f, 38f), () => { GameBootstrap.Instance.Session.Worker.Inspect(true); inspect.gameObject.SetActive(false); }, new Color(0.79f, 0.28f, 0.18f, 1f));
            reject.GetComponent<RectTransform>().anchoredPosition = new Vector2(-65f, -115f);
            var approve = UiKit.Button("Approve", card, "승인", new Vector2(110f, 38f), () => { GameBootstrap.Instance.Session.Worker.Inspect(false); inspect.gameObject.SetActive(false); }, UiKit.Good, new Color(0.06f, 0.16f, 0.09f));
            approve.GetComponent<RectTransform>().anchoredPosition = new Vector2(65f, -115f);
        }

        private void OpenInspect()
        {
            var p = GameBootstrap.Instance.Session.Worker.PendingInspection;
            if (p == null) return;
            iId.text = "#" + p.Id; iDest.text = p.Label.Destination; iWeight.text = p.Label.Weight + " kg"; iMethod.text = p.Label.Method; iScale.text = p.Label.Scale + " kg";
            inspect.gameObject.SetActive(true);
        }

        private void BuildDemolish()
        {
            demolish = Card("Demolish", new Vector2(360f, 190f), "해체");
            var card = demolish.Find("Card");
            dText = UiKit.Label("Text", card, "", 13, UiKit.Fg, TextAnchor.UpperLeft); dText.rectTransform.offsetMin = new Vector2(18f, 0f); dText.rectTransform.offsetMax = new Vector2(-18f, -46f);
            dRefund = UiKit.Label("Refund", card, "", 13, UiKit.Muted, TextAnchor.UpperLeft); dRefund.rectTransform.offsetMin = new Vector2(18f, 0f); dRefund.rectTransform.offsetMax = new Vector2(-18f, -74f);
            var cancel = UiKit.Button("Cancel", card, "취소", new Vector2(100f, 36f), () => { pendingDemolish = null; demolish.gameObject.SetActive(false); });
            cancel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-60f, -62f);
            var ok = UiKit.Button("Confirm", card, "해체", new Vector2(100f, 36f), () => { if (pendingDemolish.HasValue) GameBootstrap.Instance.Session.Build.BeginDemolish(pendingDemolish.Value.x, pendingDemolish.Value.y); pendingDemolish = null; demolish.gameObject.SetActive(false); }, new Color(0.79f, 0.28f, 0.18f, 1f));
            ok.GetComponent<RectTransform>().anchoredPosition = new Vector2(60f, -62f);
        }

        /// <summary>InputController는 BeginDemolish를 바로 호출하므로, 확인 창은 "미리 묻기"가 아니라 결과 안내다. 확인 창을 쓰려면 InputController.Demolish 대신 아래 Ask를 호출한다.</summary>
        private void OnDemolishRequested(BuildResult r) { }

        public void Ask()
        {
            var s = GameBootstrap.Instance.Session;
            var (fx, fy) = s.Worker.FacingTile();
            if (!s.Grid.InBounds(fx, fy)) { hud.Toast("해체할 수 있는 부품이 아닙니다"); return; }
            var c = s.Grid.Get(fx, fy);
            if (!c.IsBuilding) { hud.Toast("해체할 수 있는 부품이 아닙니다"); return; }
            if (c.Demolishing) { hud.Toast("이미 해체 중입니다"); return; }
            pendingDemolish = (fx, fy);
            dText.text = HudController.Name(c.Type) + " " + HudController.Arrow(c.Direction) + " 를 해체합니다.";
            dRefund.text = "환불 " + BuildService.RefundFor(c.Type) + " · 소요 2초";
            demolish.gameObject.SetActive(true);
        }
    }
}
```
그리고 `InputController.Demolish()`를 확인 창 경유로 바꾼다:
```csharp
        public void Demolish()
        {
            if (S == null) return;
            var panels = GameBootstrap.Instance.Panels;
            if (panels != null) panels.Ask();
            else DemolishRequested?.Invoke(S.Worker.TryDemolishFacing());
        }
```
`Panels`에서 `OnDemolishRequested`와 그 구독은 삭제한다.

- [ ] **Step 3: 저장/불러오기/새 게임 버튼**

`HudController.BuildTopRight`의 `helpBtn` 생성 다음에 추가:
```csharp
            var saveBtn = UiKit.Button("Save", row, "저장", new Vector2(60f, 36f), () => Toast(SaveFile.TrySave(boot.Session, out var e1) ? "저장했습니다" : e1));
            var loadBtn = UiKit.Button("Load", row, "불러오기", new Vector2(80f, 36f), () => { if (SaveFile.TryLoad(out var s2, out var e2)) { boot.ReplaceSession(s2); Toast("불러왔습니다"); } else Toast(e2); });
            var newBtn = UiKit.Button("New", row, "새 게임", new Vector2(74f, 36f), () => { boot.StartNewGame(); Toast("새 게임"); });
            foreach (var b in new[] { saveBtn, loadBtn, newBtn }) UiKit.Size(b, b.GetComponent<RectTransform>().sizeDelta.x, 36f);
```
`TopRight` 폭을 `new Vector2(660f, 40f)`로 늘린다.

- [ ] **Step 4: Bootstrap에 연결**

`Hud = gameObject.AddComponent<HudController>();` 다음에 `Panels = gameObject.AddComponent<Panels>();`, 프로퍼티 `public Panels Panels { get; private set; }`. `ReplaceSession`은 뷰를 다시 묶는다: `World.Bind(s); Cells.Bind(s); Input.ClearTool();` 그리고 `Worker`(WorkerView)는 `Session` 교체 후 자동으로 새 위치로 보간한다(`WorkerView`가 매 프레임 `Session.Worker`를 읽으므로 추가 작업 없음).

- [ ] **Step 5: 컴파일·PlayMode** (`pf-b6`). 스모크 테스트에 저장 왕복 추가:
```csharp
            Assert.IsTrue(SaveFile.TrySave(boot.Session, out var err), err);
            Assert.IsTrue(SaveFile.TryLoad(out var loaded, out err), err);
            boot.ReplaceSession(loaded);
            yield return null;
            Assert.AreEqual(ParcelFactory.Core.CellType.Conveyor, boot.Session.Grid.Get(1, 2).Type);
```
Expected: `0`, PlayMode 1/1, EditMode 121/121.

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Game Assets/Tests/Game
git commit -m "feat(game): shop, inspection card, demolish confirm, save/load/new game"
```

---

### Task 7 (B7): 검증과 마무리

- [ ] **Step 1: 전체 배치 검증**

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -quit -logFile /tmp/pf-b7.log; grep -c "error CS" /tmp/pf-b7.log
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/pf-b7-edit.xml -logFile /tmp/pf-b7-edit.log; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/pf-b7-edit.xml | head -1
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults /tmp/pf-b7-play.xml -logFile /tmp/pf-b7-play.log; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/pf-b7-play.xml | head -1
~/.dotnet/dotnet test DotnetTests/Core.Tests
```
Expected: `0`, EditMode 121/121, PlayMode 1/1, dotnet 121.

- [ ] **Step 2: PC 빌드 (macOS)**

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD" -buildOSXUniversalPlayer /tmp/pf-build/ParcelFactory.app -quit -logFile /tmp/pf-build.log; grep -E "Build succeeded|Build Finished|error" /tmp/pf-build.log | head -5
```
Expected: 빌드 성공. 실패하면 로그의 첫 오류를 보고한다.

- [ ] **Step 3: 에디터 플레이 체크리스트 (사용자 또는 에디터 GUI에서)**

1. Play 시작: 숲·울타리·트럭·도크·바닥이 보이고 그림자가 진다. 작업자가 (2,4)에 서 있고 노란 커서가 (2,3)에 깜빡인다.
2. 1 → "레일 →" 선택, 작업자를 (1,3)으로 옮겨 남쪽을 보고 E → (1,2)에 레일. 같은 방식으로 (2,2)~(4,2). 택배가 흘러 출고되면 "검사 안 된 택배 출고 −10" 토스트와 잔액 감소.
3. 3 → 검사대를 (1,2) 자리에 두려면 먼저 X로 레일 해체(확인 창) → 2초 후 환불. 검사대 설치 후 택배가 멈추면 "검사 [E]" 프롬프트 → 카드에서 승인/거부.
4. 택배를 집어(E) 머리 위에 들고 도크로 가 E → 판정 토스트.
5. B 상점에서 레일 ↓ 구매 → 핫바 레일 칸에 R 표시, R로 방향 순환.
6. 저장 → 새 게임 → 불러오기.
7. 벨트 화살표가 레일 방향과 일치하는지, 분배기 T자의 입구가 뒤쪽인지, 작업자 모델이 앞을 보는지 확인. 반대면 `CellViews`의 해당 회전에 `* Quaternion.Euler(0,180,0)`를 곱하거나 `WorkerView.ModelFacesMinusZ = true`로 바꾼다.

- [ ] **Step 4: 모바일 모듈 설치 (빌드는 2차)**

```bash
"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install-modules --version 6000.3.26f1 -m android ios --cm
```
설치 후 `ProjectSettings`에서 Android 최소 API 24, 가로 고정(`defaultScreenOrientation` Landscape Left/Right)만 설정하고 빌드는 2차로 미룬다.

- [ ] **Step 5: 작업 트리 확인**

```bash
git status --short
```
Expected: 출력 없음.

### Task 8 (A3): 코어 — 작업자 자유 이동 (스펙 9.7; **Task 4보다 먼저 실행**)

이 태스크만 코어와 코어 테스트를 수정한다(Global Constraint의 "코어 불변"은 이 태스크에 한해 해제). 2026-10-16 저녁 스펙 개정(9.7)을 코어·저장·WorkerView에 반영한다. Task 4의 `Move(Direction)`/`TryMove`는 이 태스크 이후 `Move(vx, vy, dt)`로 바뀐다(Task 4 브리프에서 반영).

**Files:**
- Modify: `Assets/Scripts/Core/Worker.cs`, `Assets/Scripts/Core/GameState.cs`, `Assets/Scripts/Core/SaveSerializer.cs`
- Modify: `Assets/Tests/Core/WorkerTests.cs`, `Assets/Tests/Core/SaveSerializerTests.cs`, `Assets/Tests/Core/GameSessionTests.cs`
- Modify: `Assets/Scripts/Game/WorkerView.cs`
- Modify: `docs/superpowers/specs/2026-10-15-parcel-factory-design.md` 9.5의 WorkerService 줄(새 API로 갱신)

**Interfaces (Produces):**
```csharp
public sealed class Worker { public float X, Y; public Direction Facing; public Package Carrying; }
public enum InteractResult { ..., Standing }   // 막는 부품을 작업자가 겹친 칸에 놓으려 할 때
public sealed class WorkerService
{
    public const float StartX = 2f, StartY = 4f, Speed = 3.4f, Radius = 0.28f, Reach = 0.8f;
    public static bool IsWalkable(CellType t);               // Empty, Conveyor, FastConveyor, Inlet, Outlet, ReturnDock → true
    public bool CanStand(float px, float py);                // 해금 영역 [-0.5+R, n-0.5-R] 안 + 충돌원 네 모서리의 칸이 모두 IsWalkable
    public bool Move(float vx, float vy, float dt);          // 정규화, x축→y축 순서로 따로 시도, 지배 축으로 Facing 갱신, 실제로 움직였으면 true, 움직이면 CancelInspection()
    public (int x, int y) Tile();                            // (round(X), round(Y))
    public (int x, int y) FacingTile();                      // (round(X + Dx*Reach), round(Y + Dy*Reach))
    public bool Overlaps(int gx, int gy);                    // |X-gx| < 0.5+R && |Y-gy| < 0.5+R
    // Preview/Interact/Inspect/CancelInspection/TryDemolishFacing 유지. TryMove/CanWalk 삭제.
}
```
- `Preview(tool)`: 설치 분기에서 `build.Check` 전에 `!IsWalkable(tool.Type) && Overlaps(fx, fy)`이면 `Standing`. 레일/고속 레일은 작업자가 서 있는 칸에도 놓을 수 있다.
- `Move`의 영벡터(길이 < 1e-6)는 false를 돌려주고 Facing을 바꾸지 않는다. 대각선은 정규화(속도 일정). Facing: `|vx| >= |vy|`이면 E/W, 아니면 N/S.
- `GameState`: `WorkerState.X/Y`를 float로. `SaveSerializer.CurrentVersion = 3`. 버전 2 저장은 그대로 읽는다(정수 좌표가 float로 읽힘). 버전 < 2 거부. 읽을 때 `CanStand(X, Y)`가 거짓이면 `SaveVersionException("worker on invalid position")`.
- `GameSession`은 그대로(`Worker` 시작 위치는 `StartX/StartY`).
- `WorkerView.LateUpdate`: 위치는 `Grid3D.TileF(w.X, w.Y)`로 **보간 없이** 직접 대입(코어가 이미 연속 좌표). 회전 보간과 커서/운반 로직은 유지. bob은 유지(애니메이션 클립 적용은 B7).

**Tests (NUnit, `Assets/Tests/Core/WorkerTests.cs` 갱신):**
- 시작 위치 (2,4) → `Tile()` (2,4), `FacingTile()` 남쪽 (2,3).
- `Move(0,1,0.5)` → Y = 5.7 … 아니, 경계 때문에 `Y <= 5.5-R`에서 멈춘다(n=6). 두 번 호출해도 Y가 5.22를 넘지 않는다. (`5 + 0.5 - 0.28 = 5.22`)
- 대각선: `Move(1,1,0.1)` 후 이동 거리 ≈ 0.34(허용오차 1e-4).
- 레일 위로 걷기: (1,4)에 Conveyor 설치 후 (2,4)에서 서쪽으로 1초 → X < 1.5 (들어감). 검사대 설치 후 같은 시도 → X >= 1.5+R−1e-4 (막힘, 벽 따라 미끄러짐은 y 성분 유지로 확인).
- 입고구 (0,2) 위도 걸을 수 있다. 미해금 칸은 못 들어간다(n=6에서 (6,·)).
- `Standing`: 작업자 (2,4)에서 남쪽 바라보고 Y=3.6으로 옮겨 (2,3)에 겹치게 한 뒤 Table 도구 Preview → `Standing`; Conveyor 도구 → `Placed`.
- Facing: `Move(1,0.2,…)` → East; `Move(-0.2,1,…)` → North.
- Move가 움직이면 `PendingInspection`이 null이 된다.
- 저장: float 좌표 왕복; 버전 2 JSON(정수 좌표) 읽기 성공; 좌표가 검사대 위면 `SaveVersionException`.
- 기존 테스트에서 `TryMove`/`CanWalk`를 쓰던 곳은 새 API로 바꾼다. 테스트 총수는 121 이상이어야 한다.

**Verification:**
- `~/.dotnet/dotnet test DotnetTests/Core.Tests` 전부 통과.
- Unity EditMode: `$UNITY -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults /tmp/pf-a3-edit.xml -logFile /tmp/pf-a3-edit.log` 전부 통과.
- Unity 컴파일(게임 레이어 포함): `-quit` 실행 후 `grep -c "error CS"` = 0. PlayMode 스모크 1/1.
- 커밋 1개(.meta 포함).

### Task 9 (B7a): 월드 재작업 — 공장 실내, 작업자 애니메이션, 벨트 화살표 (스펙 9.4 개정판; Task 6 이후, Task 7 이전)

프로토타입(https://claude.ai/artifact/S6mAdo8JggxwFW6uV6f2JK, 소스 `docs/design/prototype/parcel-factory-3d.html`의 `buildStatic`/`rebuildLot`/`dock`/`frameLot`)과 같은 그림을 Unity에서 만든다. 프로토타입은 three.js 오른손 좌표라 x가 뒤집혀 있다(`W = 5.5 − gx`). Unity는 `Grid3D.Tile(gx, gy) = (gx − 5.5, 0, gy − 5.5)`이므로 **동쪽 = +x**, 아래 좌표는 모두 Unity 기준이다.

**Files:**
- Modify: `Assets/Scripts/Game/WorldBuilder.cs`, `Assets/Scripts/Game/MaterialLibrary.cs`, `Assets/Scripts/Game/CellViews.cs`, `Assets/Scripts/Game/WorkerView.cs`, `Assets/Editor/CatalogBuilder.cs`
- Modify: `Assets/Tests/Game/BootSmokeTests.cs` (오브젝트 이름 갱신: `World/Lot` 아래 `Slab`, `World/Walls`, `World/Yard`)

**Interfaces:** 바깥에서 쓰는 API(`WorldBuilder.Build/Bind/RebuildLot/MainCamera`, `CellViews.*`, `WorkerView.Model/CarryAnchor`)는 그대로. 추가: `MaterialLibrary.Concrete(int tiles, Color baseTone)`(절차적 타일 텍스처, 캐시), `MaterialLibrary.Asphalt()`, `MaterialLibrary.Hazard()`(노랑/검정 사선, 반복).

- [ ] **Step 1: 카탈로그 확장** — `CatalogBuilder.Wanted`에 추가: factory-kit `structure-window-wide, structure-doorway-wide, machine-window, hopper-round, pipe-large-long, robot-arm-a, screen-hanging-wide, box-wide, box-long, indicator-special-area`; city-kit-industrial `water-tower, chimney-medium, building-a, building-c, building-h, detail-tank`; car-kit `truck-flat, cone-flat`. nature-kit 항목은 목록에서 제거한다(에셋 파일은 그대로 둔다). `BuildAll` 재실행 후 `PF_Catalog.asset`이 갱신되어야 한다.
  - 같은 스크립트에서 `Assets/ThirdParty/Kenney/mini-characters/FBX/character-male-a.fbx`의 `ModelImporter`를 `animationType = Legacy`, `importAnimation = true`로 설정하고 재임포트한다(FBX에 idle/walk/holding-both/pick-up 테이크가 들어 있다). 결과: 프리팹 루트에 `Animation` 컴포넌트와 클립이 붙는다. 클립 이름 목록을 로그로 남긴다.
- [ ] **Step 2: 재질** — `Concrete(n, tone)`: n×64px 캔버스 느낌의 `Texture2D`(기본색 tone, 미세 노이즈, 칸마다 어두운 이음새 2px + 밝은 하이라이트 1px), sRGB, 캐시. `Asphalt()`: 512px 어두운 회색 노이즈, 타일링 16. `Hazard()`: 64×16 노랑 `#F2C230`/검정 사선, `wrapMode Repeat`.
- [ ] **Step 3: WorldBuilder.Build** (숲·울타리·잔디 코드는 삭제):
  - 지면: 90×90 평면, `Asphalt()`, y = −0.03.
  - 건물 바닥: 14×14 평면(HALF = 7, 원점 중심), `Concrete(14, #8F8C86)`, y = −0.02.
  - 벽(`World/Walls`): 두께 0.3, 높이 1.7, 색 `#D9D3C8`, 굽도리 0.22 `#5A5F66`, 윗면 캡 0.08. 북쪽 벽 전체(z = +7, x −7..7). 서쪽 벽(x = −7)은 입고구 줄(z = Z(InletY))에 폭 2의 출입구, 동쪽 벽(x = +7)은 출고구·반품구 줄(z = Z(OutletY), Z(ReturnDockY))에 출입구. 출입구 = 상인방(높이 0.5, 벽 윗부분) + 양쪽 기둥(0.12) + 노란 띠. 네 모서리 기둥 0.5×2.0×0.5 `#5A5F66`. 남쪽 벽 없음.
  - 북쪽 벽 앞 설비(`World/Props`, z = 7 − 0.9 근처, 벽을 등지게 180° 회전): machine-window(x = 4.6, 0.9배), machine(3.3), hopper-round(1.6, 0.9배), pipe-large-long(−0.6, 0.8배), pipe-large-long(−2.2), robot-arm-a(−4.0, 0.6배), screen-hanging-wide(−5.6, y = 1.0).
  - 재고 더미(미해금 느낌): (6.2,5.6) (5.2,5.7) (6.1,4.6) (−6.1,5.5) (−6.2,4.4) (−5.1,5.6)에 box-large/box-wide/box-long 중 시드 랜덤, 절반은 위에 box-small 0.5 높이.
  - 야드(`World/Yard`): shipping-container-a (11.5, 4.0) 3.0배, -b (11.5, 7.5), -c (13.2, 1.2) yaw 5°; water-tower (−10.5, 9.5) 2.6배; detail-tank (−11.5, 6.0) 3.0배; chimney-medium (9.0, 10.5) 2.6배; building-a (2.0, 13.5) 5배, building-c (−8.0, 14.2) 5배, building-h (10.0, 14.0) 5배; cone-flat 3개 서쪽 벽 밖(x = −8.2, z = −6, −2.8, 0.4).
  - 도크(`World/Docks`): 슬래브 1×0.22×1 + 흰 표식 0.72×0.02 (기존 유지), 콘 `warning-traffic` 0.6배를 슬래브 북쪽 +0.62. 차량은 **벽 밖 출입구 앞에 후진 주차**: 입고 van (x = −8.5, z = Z(InletY)), 출고 delivery (x = +8.6, z = Z(OutletY)), 반품 truck (x = +8.6, z = Z(ReturnDockY)); 차량 모델의 앞이 +z라고 가정하고 van은 yaw −90°(앞이 −x, 뒤가 출입구), delivery/truck은 yaw +90°. B7 플레이 체크에서 반대면 180° 더한다. 도크 칸 가장자리에서 벽까지 바닥 차선 2줄(0.06 폭, `#E9C94A`, z ± 0.42, y 0.0).
  - 도크 좌표는 `FactoryGrid.OutletX/Y`, `ReturnDockX/Y`, `InletX/Y`만 쓴다(상수가 함수로 바뀌어도 그대로 따라가게 한 곳에서 읽는다).
  - 카메라: FOV 36. 방향 `dir = normalize(0, 14.5, −10.5)`. `FrameLot(n)`: `t = tan(18°)`, `a = 화면 종횡비`, `hw = n/2 + 5`, `hv = n/2 + 2.8`, `dist = max(hw/(t·a), hv/t)·1.04`, `target = (cx·0.2, 0, cz + 0.5)`(cx, cz = 해금 영역 중심), `position = target + dir·dist`, `LookAt(target)`. 안개 near = dist + 6, far = dist + 30, 색 `#1D2127`, 배경색 같음. 화면 크기가 바뀌면(`Screen.width/height` 변화) 다시 계산한다(Update에서 비교).
  - 조명: 방향광 1.05, 색 `#FFE6C2`, 반구/환경광은 하늘 `#DFE7F2` 지면 `#6B655C` 0.55 수준(URP: `RenderSettings.ambientMode = Trilight` 로 근사).
- [ ] **Step 4: RebuildLot** — `World/Lot`: 슬래브 `Slab` = n×0.08×n 박스, `Concrete(n, #CFCAC1)`, y = −0.04 (윗면 0.0). 둘레 위험 테이프: 폭 0.14 평면 4개, `Hazard()` 반복 n×1.5, y = 0.012. 모서리 cone 4개(1.0배). 울타리·잔디 없음. 해금 후 차량 위치는 바뀌지 않는다(벽 밖 고정). 차선은 도크 칸이 바뀌지 않는 한 그대로.
- [ ] **Step 5: 벨트 화살표** — Kenney `arrow` 대신 절차 메시: Shape (0,0.16) (0.14,−0.02) (0.05,−0.02) (0.05,−0.16) (−0.05,−0.16) (−0.05,−0.02) (−0.14,−0.02)를 XZ 평면에 +z를 가리키게 깔고(윗면이 +y를 보도록 와인딩 확인), `Emissive(#FFD36A, 0.5)` 재질. `BeltArrow`는 그대로(로컬 z −0.32..0.32 왕복, 높이 0.43).
- [ ] **Step 6: 작업자 애니메이션** — `WorkerView.Start`에서 모델의 `Animation` 컴포넌트를 찾아 `idle/walk/holding-both` 클립을 확인하고(없으면 bob 유지), `LateUpdate`에서 이동 중이면 `walk`(속도 1.6배), 아니면 `Carrying != null ? "holding-both" : "idle"`을 `CrossFade(name, 0.12f)`. 모델 스케일 1.45, 이동 판정은 코어 위치 변화량 > 1e-4/프레임. `ModelFacesMinusZ`는 유지.
- [ ] **Step 7: 검증** — 컴파일 0 오류, EditMode 전부, PlayMode 전부(스모크의 오브젝트 이름 갱신), `AssetProbe.Run`을 돌려 character-male-a의 클립 이름이 로그에 찍히는지 확인(`grep -a "walk" /tmp/pf-b7a.log`). 커밋 1개.

### Task 10 (B7b): 스크린샷 하네스 + 전체 검증 + 빌드 (Task 7을 대체)

- [ ] **Step 1: 스크린샷 테스트** — `Assets/Tests/Game/ScreenshotTests.cs`(PlayMode, `[UnityTest]`, 카테고리 `"Screenshot"`): 부트 후 (a) 시작 상태, (b) 샘플 배치(코어 API로 (1,2)E (2,2)E (3,2) Table (4,2)E 설치, 작업자를 (2.6, 1.2)로, 핫바 1 선택) 두 장면을 각각 1.0초 돌린 뒤 `ScreenCapture.CaptureScreenshot("docs/design/unity-start.png")`, `unity-demo.png`, 그리고 터치 모드 강제 후 `unity-touch.png`. 캡처 뒤 2프레임 대기. `-nographics` 없이 `-batchmode`로 실행해야 렌더링된다: `"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testCategory Screenshot -testResults /tmp/pf-shot.xml -logFile /tmp/pf-shot.log`. 해상도는 `Screen.SetResolution(1600, 900, false)` 호출 후 2프레임 대기(배치 모드에서 무시되면 로그에 남기고 기본 해상도로 진행). 결과 PNG는 커밋한다.
- [ ] **Step 2: 전체 검증** — Task 7 Step 1 명령(기대: 컴파일 0, EditMode 136/136, PlayMode 전부, dotnet 136).
- [ ] **Step 3: macOS 빌드** — Task 7 Step 2.
- [ ] **Step 4: 모바일 모듈** — Task 7 Step 4(설치만; 실패하면 로그 첫 오류 보고).
- [ ] **Step 5: 플레이 체크리스트** — Task 7 Step 3의 항목을 스크린샷으로 가능한 것(1, 2의 레일 배치, 7의 방향)은 스크린샷에서 확인하고, 나머지는 사용자 확인 항목으로 보고서에 남긴다.
- [ ] **Step 6: `git status --short` 비어 있음.**
