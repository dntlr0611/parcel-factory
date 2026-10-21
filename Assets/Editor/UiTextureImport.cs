using UnityEditor;
using UnityEngine;

namespace ParcelFactory.EditorTools
{
    /// <summary>Assets/Resources/UI/ 아래 이미지(흰색 아이콘)를 UI 스프라이트로 고정한다: 밉맵 없음, 무압축, Clamp, Bilinear(스펙 12.2).</summary>
    public sealed class UiTextureImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/UI/";

        public override uint GetVersion() => 1;

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
        }
    }
}
