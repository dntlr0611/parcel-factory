using System;
using System.IO;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    public static class SaveFile
    {
        /// <summary>테스트용 경로 재정의. null이면 persistentDataPath/save.json.</summary>
        public static string PathOverride { get; set; }

        public static string Path => PathOverride ?? System.IO.Path.Combine(Application.persistentDataPath, "save.json");

        /// <summary>임시 파일에 쓴 뒤 교체한다. 실패해도 기존 파일은 남는다.</summary>
        public static bool TrySave(GameSession session, out string error)
        {
            error = null;
            try
            {
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, SaveSerializer.ToJson(SaveSerializer.Capture(session)));
                if (File.Exists(Path)) File.Replace(tmp, Path, null);
                else File.Move(tmp, Path);
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
