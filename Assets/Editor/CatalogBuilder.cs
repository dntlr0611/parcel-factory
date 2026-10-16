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
            ("factory-kit", new[] { "conveyor", "conveyor-stripe", "conveyor-corner", "conveyor-junction-t", "scanner-low", "box-small", "box-large", "box-wide", "box-long", "screen-panel-flat", "floor", "floor-large", "structure-wall", "structure-window-wide", "structure-doorway-wide", "warning-traffic", "cone", "machine", "machine-window", "hopper-round", "pipe-large-long", "robot-arm-a", "screen-hanging-wide", "indicator-special-area" }),
            ("city-kit-industrial", new[] { "shipping-container-a", "shipping-container-b", "shipping-container-c", "water-tower", "chimney-medium", "building-a", "building-c", "building-h", "detail-tank" }),
            ("car-kit", new[] { "delivery", "truck", "truck-flat", "van", "box", "cone-flat" }),
            ("mini-characters", new[] { "character-male-a", "character-male-b", "character-female-a", "character-female-b" }),
        };

        public const string WorkerCharacterPath = Root + "mini-characters/FBX/character-male-a.fbx";

        public static void BuildAll()
        {
            ConfigureCharacterImport();
            BuildCatalog();
            CreateMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log("CATALOGBUILDER_DONE");
        }

        /// <summary>작업자 캐릭터 FBX를 Legacy 애니메이션으로 임포트한다(프리팹 루트에 Animation 컴포넌트 + idle/walk/holding-both 등 클립).</summary>
        public static void ConfigureCharacterImport()
        {
            var imp = AssetImporter.GetAtPath(WorkerCharacterPath) as ModelImporter;
            if (imp == null) { Debug.LogError("ModelImporter not found: " + WorkerCharacterPath); return; }
            if (imp.animationType != ModelImporterAnimationType.Legacy || !imp.importAnimation)
            {
                imp.animationType = ModelImporterAnimationType.Legacy;
                imp.importAnimation = true;
                imp.SaveAndReimport();
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerCharacterPath);
            var anim = go != null ? go.GetComponent<Animation>() : null;
            var names = new List<string>();
            if (anim != null) foreach (var c in AnimationUtility.GetAnimationClips(go)) if (c != null) names.Add(c.name);
            Debug.Log($"CHARACTER_ANIM animation={(anim != null)} clips={names.Count}: " + string.Join(",", names));
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
