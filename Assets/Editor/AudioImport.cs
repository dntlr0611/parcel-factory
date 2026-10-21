using UnityEditor;
using UnityEngine;

namespace ParcelFactory.EditorTools
{
    /// <summary>Assets/Resources/Audio/ 아래 음원 임포트 설정(스펙 13.1): Vorbis 품질 0.6, 백그라운드 로드. 음악은 Streaming, 배경소리 등 나머지는 Compressed In Memory.</summary>
    public sealed class AudioImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Audio/";

        public override uint GetVersion() => 1;

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ai = (AudioImporter)assetImporter;
            ai.loadInBackground = true;
            bool music = System.IO.Path.GetFileNameWithoutExtension(assetPath) == "music-shift";
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            ai.defaultSampleSettings = s;
        }
    }
}
