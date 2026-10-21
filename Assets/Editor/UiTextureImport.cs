using UnityEditor;
using UnityEngine;

namespace ParcelFactory.EditorTools
{
    /// <summary>Assets/Resources/UI/ 아래 이미지(흰색 아이콘)를 UI 스프라이트로 고정한다: 밉맵 없음, 무압축, Clamp, Bilinear(스펙 12.2).
    /// 예외: 3D 월드에 붙이는 데칼(파일 이름 mark-*, 스펙 14.2 상자 윗면 항공 마크)은 멀리서 작게 보이므로 밉맵(알파 커버리지 보존)을 켜고 Trilinear.</summary>
    public sealed class UiTextureImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/UI/";

        public override uint GetVersion() => 2;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.spritePixelsPerUnit = 100f;
            if (System.IO.Path.GetFileName(assetPath).StartsWith("mark-"))
            {
                ti.mipmapEnabled = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.5f;
                ti.filterMode = FilterMode.Trilinear;
            }
        }
    }
}
