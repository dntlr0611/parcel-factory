using System.Text;
using UnityEditor;
using UnityEngine;

namespace ParcelFactory.EditorTools
{
    /// <summary>임포트된 Kenney 모델의 크기·리그를 로그로 찍는다. 배치 모드: -executeMethod ParcelFactory.EditorTools.AssetProbe.Run</summary>
    public static class AssetProbe
    {
        private static readonly string[] Paths =
        {
            "Assets/ThirdParty/Kenney/factory-kit/FBX/conveyor.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/conveyor-stripe.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/conveyor-corner.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/conveyor-sides.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/conveyor-junction-t.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/scanner-low.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/scanner-high.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/box-small.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/box-large.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/machine.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/floor.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/floor-large.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/structure-wall.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/warning-traffic.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/screen-panel-flat.fbx",
            "Assets/ThirdParty/Kenney/city-kit-industrial/FBX/shipping-container-a.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/machine-window.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/hopper-round.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/pipe-large-long.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/robot-arm-a.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/screen-hanging-wide.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/box-wide.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/box-long.fbx",
            "Assets/ThirdParty/Kenney/factory-kit/FBX/cone.fbx",
            "Assets/ThirdParty/Kenney/city-kit-industrial/FBX/water-tower.fbx",
            "Assets/ThirdParty/Kenney/city-kit-industrial/FBX/chimney-medium.fbx",
            "Assets/ThirdParty/Kenney/city-kit-industrial/FBX/building-a.fbx",
            "Assets/ThirdParty/Kenney/city-kit-industrial/FBX/detail-tank.fbx",
            "Assets/ThirdParty/Kenney/car-kit/FBX/cone-flat.fbx",
            "Assets/ThirdParty/Kenney/car-kit/FBX/delivery.fbx",
            "Assets/ThirdParty/Kenney/car-kit/FBX/truck.fbx",
            "Assets/ThirdParty/Kenney/car-kit/FBX/van.fbx",
            "Assets/ThirdParty/Kenney/mini-characters/FBX/character-male-a.fbx",
            "Assets/ThirdParty/Kenney/mini-characters/FBX/character-female-b.fbx",
        };

        public static void Run()
        {
            var sb = new StringBuilder("ASSETPROBE_BEGIN\n");
            foreach (var path in Paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { sb.Append("MISSING ").Append(path).Append('\n'); continue; }
                var inst = Object.Instantiate(go);
                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                var b = new Bounds(inst.transform.position, Vector3.zero);
                bool first = true;
                foreach (var r in renderers) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                int skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                var mats = new StringBuilder();
                foreach (var r in renderers) foreach (var m in r.sharedMaterials) if (m != null) mats.Append(m.name).Append('|').Append(m.shader != null ? m.shader.name : "null").Append(';');
                sb.Append(System.IO.Path.GetFileNameWithoutExtension(path))
                  .Append(" size=").Append(b.size.ToString("F3"))
                  .Append(" min=").Append(b.min.ToString("F3"))
                  .Append(" renderers=").Append(renderers.Length)
                  .Append(" skinned=").Append(skinned)
                  .Append(" mats=").Append(mats)
                  .Append('\n');
                Object.DestroyImmediate(inst);
            }
            var ch = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/Kenney/mini-characters/FBX/character-male-a.fbx");
            var anim = ch != null ? ch.GetComponent<Animation>() : null;
            sb.Append("ANIM character-male-a animation=").Append(anim != null);
            if (anim != null)
            {
                var clips = AnimationUtility.GetAnimationClips(ch);
                sb.Append(" clips=").Append(clips.Length).Append(':');
                foreach (var c in clips) if (c != null) sb.Append(' ').Append(c.name).Append(c.legacy ? "" : "(non-legacy)");
                sb.Append(" default=").Append(anim.clip != null ? anim.clip.name : "null");
            }
            sb.Append('\n');
            var tex = AssetDatabase.FindAssets("colormap t:Texture2D", new[] { "Assets/ThirdParty/Kenney" });
            foreach (var g in tex) sb.Append("TEX ").Append(AssetDatabase.GUIDToAssetPath(g)).Append('\n');
            sb.Append("ASSETPROBE_END");
            Debug.Log(sb.ToString());
        }
    }
}
