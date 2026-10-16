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
