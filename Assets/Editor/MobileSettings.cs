using UnityEditor;
using UnityEngine;

namespace ParcelFactory.EditorTools
{
    /// <summary>
    /// 모바일 플레이어 설정을 코드로 맞춘다(빌드는 2차).
    /// Unity -batchmode -nographics -projectPath . -executeMethod ParcelFactory.EditorTools.MobileSettings.Apply -quit
    /// </summary>
    public static class MobileSettings
    {
        public static void Apply()
        {
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25; // Unity 6.3 최소 지원 API(24는 obsolete, 읽으면 25로 올라간다)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            AssetDatabase.SaveAssets();
            Debug.Log($"[MobileSettings] Android minSdk {PlayerSettings.Android.minSdkVersion}, orientation {PlayerSettings.defaultInterfaceOrientation}, " +
                      $"portrait {PlayerSettings.allowedAutorotateToPortrait}/{PlayerSettings.allowedAutorotateToPortraitUpsideDown}, " +
                      $"landscape L/R {PlayerSettings.allowedAutorotateToLandscapeLeft}/{PlayerSettings.allowedAutorotateToLandscapeRight}");
        }
    }
}
