using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>작업자 모델을 코어 Worker의 연속 좌표에 그대로 두고(회전만 보간), 들고 있는 택배와 바라보는 칸 커서를 그린다.</summary>
    public sealed class WorkerView : MonoBehaviour
    {
        /// <summary>Kenney 캐릭터의 정면이 로컬 −z면 true로 바꾼다 (Play에서 확인).</summary>
        public static bool ModelFacesMinusZ = false;

        public Transform Model { get; private set; }
        public Transform CarryAnchor { get; private set; }

        private GameObject carried;
        private BoxMarks carriedMarks;
        private Transform cursor;
        private Renderer cursorRenderer;
        private Vector3 lastPos;
        private float bob;
        private Animation anim;
        private string currentClip;
        private bool hasHolding, hasSprint;

        private static readonly Color CursorTint = new Color(1f, 0.83f, 0.4f, 0.45f);
        private static readonly Color RefusedTint = new Color(0.95f, 0.32f, 0.26f, 0.45f);

        private const string IdleClip = "idle", WalkClip = "walk", HoldingClip = "holding-both", SprintClip = "sprint";

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            Model = Instantiate(boot.Catalog.Get("character-male-a"), transform).transform;
            Model.localScale = Vector3.one * 1.45f;
            Model.localRotation = ModelFacesMinusZ ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            SetupAnimation();
            CarryAnchor = new GameObject("CarryAnchor").transform;
            CarryAnchor.SetParent(transform, false);
            CarryAnchor.localPosition = new Vector3(0f, 1.22f, 0f);
            carried = PackageViews.MakeBox(boot.Catalog, CarryAnchor);
            carriedMarks = carried.GetComponent<BoxMarks>();
            carried.SetActive(false);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Cursor";
            q.transform.SetParent(boot.WorldRoot, false);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = Vector3.one * 0.96f;
            cursorRenderer = q.GetComponent<Renderer>();
            cursorRenderer.material = MaterialLibrary.Transparent(CursorTint);
            cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cursor = q.transform;

            var w = boot.Session.Worker.Worker;
            transform.position = lastPos = Grid3D.TileF(w.X, w.Y);
        }

        /// <summary>Kenney 캐릭터 FBX(Legacy 임포트)에 내장된 idle/walk/holding-both/sprint 클립을 쓴다. 없으면 bob으로 대신한다.</summary>
        private void SetupAnimation()
        {
            anim = Model.GetComponent<Animation>();
            if (anim == null) anim = Model.GetComponentInChildren<Animation>();
            if (anim == null || anim.GetClip(IdleClip) == null || anim.GetClip(WalkClip) == null) { anim = null; return; }
            hasHolding = anim.GetClip(HoldingClip) != null;
            hasSprint = anim.GetClip(SprintClip) != null;
            foreach (var n in new[] { IdleClip, WalkClip, HoldingClip, SprintClip })
            {
                var st = anim[n];
                if (st == null) continue;
                st.wrapMode = WrapMode.Loop;
                st.speed = n == WalkClip ? 1.6f : n == SprintClip ? 1.3f : 1f;
            }
            anim.Play(IdleClip);
            currentClip = IdleClip;
        }

        private void PlayClip(string name)
        {
            if (anim == null || name == currentClip) return;
            anim.CrossFade(name, 0.12f);
            currentClip = name;
        }

        private void LateUpdate()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null || Model == null) return;
            var ws = boot.Session.Worker;
            var w = ws.Worker;
            transform.position = Grid3D.TileF(w.X, w.Y); // 코어가 이미 연속 좌표라 보간하지 않는다
            transform.rotation = Quaternion.Slerp(transform.rotation, Grid3D.Facing(w.Facing), 1f - Mathf.Pow(0.0005f, Time.deltaTime));

            bool moving = (transform.position - lastPos).magnitude > 1e-4f; // 코어 위치 변화량 / 프레임
            lastPos = transform.position;
            if (anim != null)
            {
                PlayClip(moving ? (ws.IsRunning && hasSprint ? SprintClip : WalkClip) : (w.Carrying != null && hasHolding ? HoldingClip : IdleClip));
            }
            else
            {
                bob = moving ? bob + Time.deltaTime * 14f : 0f;
                Model.localPosition = new Vector3(0f, moving ? Mathf.Abs(Mathf.Sin(bob)) * 0.06f : 0f, 0f);
            }

            carried.SetActive(w.Carrying != null);
            if (w.Carrying != null) carriedMarks.Set(w.Carrying);

            // 잠긴 칸도 걸을 수 있는 바닥이므로 커서를 보인다. 설치 불가는 Preview(Locked)로 커서 색을 바꿔 알린다.
            var (fx, fy) = ws.FacingTile();
            bool show = boot.Session.Grid.InBounds(fx, fy);
            cursor.gameObject.SetActive(show);
            if (show)
            {
                var cell = boot.Session.Grid.Get(fx, fy);
                cursor.position = Grid3D.Tile(fx, fy) + new Vector3(0f, 0.03f + PackageViews.TopHeight(cell), 0f);
                bool refused = boot.Input != null && ws.Preview(boot.Input.SelectedTool) == InteractResult.Locked;
                var c = refused ? RefusedTint : CursorTint;
                c.a = 0.45f + 0.10f * Mathf.Sin(Time.time * 6f); // 0.35~0.55: 어두운 잠긴 바닥 위에서도 보이게
                cursorRenderer.material.SetColor("_BaseColor", c);
            }
        }
    }
}
