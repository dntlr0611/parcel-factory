using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParcelFactory.Game
{
    /// <summary>설치 미리보기(스펙 11.9): 도구를 들고 있으면 바라보는 칸에 그 부품의 반투명 모델을 그린다.
    /// 모델은 CellViews.BuildPreview(실제 칸 뷰와 같은 코드)로 만들고, 모든 렌더러를 투명 재질로 바꾼다.
    /// 설치 가능(Preview == Placed)이면 초록, 아니면 빨강. 칸·도구(종류·방향·빨간 출구)·설치 가능 여부가 바뀔 때만 다시 만든다.
    /// 도구가 없거나, 택배를 들고 있거나, 바라보는 칸이 격자 밖이면 숨긴다. 설정 모드 중에도 보인다.</summary>
    public sealed class PlacementGhost : MonoBehaviour
    {
        public static readonly Color GreenTint = new Color(0x3F / 255f, 0xA3 / 255f, 0x5A / 255f, 0.45f);
        public static readonly Color RedTint = new Color(0xD9 / 255f, 0x5A / 255f, 0x4A / 255f, 0.45f);

        /// <summary>지금 그리는 틴트(초록·빨강).</summary>
        public Color Tint { get; private set; }
        /// <summary>마지막으로 그린 미리보기가 설치 가능 상태였는가.</summary>
        public bool Placeable { get; private set; }
        /// <summary>미리보기가 보이는가.</summary>
        public bool Visible => model != null && model.activeSelf;
        /// <summary>현재 미리보기 모델(없으면 null). 테스트용.</summary>
        public GameObject Model => model;
        /// <summary>다시 만든 횟수. 테스트용(캐시 확인).</summary>
        public int Rebuilds { get; private set; }

        private GameObject model;
        private (int x, int y, CellType type, Direction dir, Direction? red, RouteMode mode, bool ok)? key;

        private void LateUpdate() => Refresh();

        /// <summary>현재 상태로 미리보기를 맞춘다(같은 프레임에 즉시 확인할 때도 부른다).</summary>
        public void Refresh()
        {
            var boot = GameBootstrap.Instance;
            var s = boot != null ? boot.Session : null;
            var input = boot != null ? boot.Input : null;
            if (s == null || input == null || !input.SelectedTool.HasValue || s.Worker.Worker.Carrying != null) { Hide(); return; }
            var (fx, fy) = s.Worker.FacingTile();
            if (!s.Grid.InBounds(fx, fy)) { Hide(); return; }

            var tool = input.SelectedTool.Value;
            Direction? red = tool.Type == CellType.Splitter ? input.ToolSplitterRed : null;
            bool ok = s.Worker.Preview(tool) == InteractResult.Placed;
            var mode = InputController.HasMode(tool.Type) ? input.ToolMode : RouteMode.Inspect; // 항공 모드 색(스펙 14.5)
            var k = (fx, fy, tool.Type, tool.Direction, red, mode, ok);
            if (model != null && key.HasValue && key.Value.Equals(k))
            {
                if (!model.activeSelf) model.SetActive(true);
                return;
            }
            Build(k);
        }

        private void Build((int x, int y, CellType type, Direction dir, Direction? red, RouteMode mode, bool ok) k)
        {
            if (model != null) Destroy(model);
            key = k;
            Placeable = k.ok;
            Tint = k.ok ? GreenTint : RedTint;
            var kind = k.type == CellType.Pallet ? Tool.KindOf(k.dir) : PalletKind.None;
            model = CellViews.BuildPreview(transform, k.type, k.dir, kind, k.red, k.mode);
            if (model == null) return;
            model.name = "Ghost";
            // 바닥과 겹쳐 깜빡이지 않도록 살짝 띄운다.
            model.transform.position = Grid3D.Tile(k.x, k.y) + new Vector3(0f, 0.01f, 0f);
            Rebuilds++;

            var body = MaterialLibrary.Transparent(Tint);
            var arrow = Lighter(Tint);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material m = body;
                string n = r.gameObject.name;
                if (n == "BlueArrow" || n == "RedArrow" || n == "GroundArrow" || n == "AirArrow")
                    m = MaterialLibrary.Transparent(WithAlpha(CellViews.ArrowColor(n, k.mode), 0.8f));
                else if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh == ChevronMesh.Get()) m = arrow;
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>벨트 화살표용: 틴트를 흰색 쪽으로 밝히고 더 진하게 해 방향이 보이게 한다.</summary>
        private static Material Lighter(Color tint)
        {
            var c = Color.Lerp(tint, Color.white, 0.55f);
            c.a = 0.85f;
            return MaterialLibrary.Transparent(c);
        }

        private void Hide()
        {
            if (model != null && model.activeSelf) model.SetActive(false);
        }
    }
}
