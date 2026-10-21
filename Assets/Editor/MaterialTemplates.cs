using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParcelFactory.EditorTools
{
    /// <summary>
    /// 빌드에 URP 셰이더 변형이 남도록 Resources에 템플릿 재질을 만든다.
    /// 코드에서 Shader.Find로만 만든 재질은 참조하는 에셋이 없어 빌드에서 셰이더가 빠진다.
    /// Unity -batchmode -projectPath . -executeMethod ParcelFactory.EditorTools.MaterialTemplates.Build -quit
    /// </summary>
    public static class MaterialTemplates
    {
        public const string Dir = "Assets/Resources/Materials";

        [MenuItem("Parcel Factory/Build Material Templates")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Materials");

            var tr = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            tr.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.5f));
            tr.SetFloat("_Surface", 1f);
            tr.SetFloat("_Blend", 0f);
            tr.SetFloat("_ZWrite", 0f);
            tr.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            tr.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            tr.SetOverrideTag("RenderType", "Transparent");
            tr.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            tr.renderQueue = (int)RenderQueue.Transparent;
            Save(tr, "PF_Transparent");

            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            lit.SetFloat("_Metallic", 0f);
            lit.SetFloat("_Smoothness", 0.1f);
            Save(lit, "PF_Lit");

            var emi = new Material(lit);
            emi.EnableKeyword("_EMISSION");
            emi.SetColor("_EmissionColor", Color.white);
            emi.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Save(emi, "PF_Emissive");

            AssetDatabase.SaveAssets();
            Debug.Log("[MaterialTemplates] built");
        }

        private static void Save(Material m, string name)
        {
            string path = Dir + "/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }
    }
}
