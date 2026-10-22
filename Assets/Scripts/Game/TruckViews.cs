using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 동쪽 트럭 교대(스펙 18)의 화면 쪽: 트럭마다 위에 알약(`12/30`, 30에 가까울수록 앰버 / 떠나 있으면 `다음 차 0:12`)을 띄우고,
    /// 출발하면 트럭이 벽 밖(동쪽)으로 DriveSeconds 동안 빠져나가 사라지고, 빈 트럭이 오면 반대로 들어와 선다.
    /// 상태는 매 프레임 Session.Sim.Trucks를 읽는다(이벤트 없이). 차량이 새로 만들어졌으면(세션 교체·동쪽 재생성) 애니메이션 없이 지금 상태로 맞춘다.
    /// 알약 위치는 StatusBars처럼 매 프레임 화면에 투영한다.
    /// </summary>
    public sealed class TruckViews : MonoBehaviour
    {
        /// <summary>출발·도착 이동 시간(초)과 거리(동쪽, m).</summary>
        public const float DriveSeconds = 1.5f, DriveDistance = 7f;
        public const float PillHeight = 22f, PillPadX = 18f, RoofLift = 0.45f;
        public const int FontSize = 12;

        private static Color PillBg => new Color(10f / 255f, 13f / 255f, 15f / 255f, UiKit.DarkAlpha(0.78f));
        private static readonly Color PillFg = new Color(0xe9 / 255f, 0xec / 255f, 0xe6 / 255f);

        private sealed class Truck
        {
            public Transform Car, Sign;
            public Vector3 CarHome, SignHome;
            public float Roof;
            /// <summary>0 = 도크에 서 있음, 1 = 떠나 보이지 않음.</summary>
            public float T;
            public RectTransform Pill;
            public Image Bg, Border;
            public Text Label;
        }

        private readonly Dictionary<CellType, Truck> trucks = new Dictionary<CellType, Truck>();
        private RectTransform layer;
        /// <summary>마지막으로 그린 세션. 바뀌면(불러오기·새 게임) 애니메이션 없이 지금 상태로 맞춘다.</summary>
        private GameSession boundSession;

        /// <summary>도크 트럭 알약 문구. 트럭이 없으면(항공 도크 확장 전) null.</summary>
        public string TextOf(CellType dock) => trucks.TryGetValue(dock, out var t) && t.Car != null && t.Pill.gameObject.activeSelf ? t.Label.text : null;

        /// <summary>알약 글자색(적재가 많을수록 앰버). 테스트용.</summary>
        public Color ColorOf(CellType dock) => trucks.TryGetValue(dock, out var t) ? t.Label.color : Color.clear;

        /// <summary>트럭 이동 진행도: 0 = 도크에 서 있음, 1 = 떠나 보이지 않음. 트럭이 없으면 −1.</summary>
        public float Progress(CellType dock) => trucks.TryGetValue(dock, out var t) && t.Car != null ? t.T : -1f;

        /// <summary>출발·도착 중(0과 1 사이)인가.</summary>
        public bool IsDriving(CellType dock) { float p = Progress(dock); return p > 0f && p < 1f; }

        /// <summary>트럭이 집 자리에서 동쪽으로 옮겨진 거리(m). 테스트용.</summary>
        public float Offset(CellType dock) => trucks.TryGetValue(dock, out var t) && t.Car != null ? t.Car.position.x - t.CarHome.x : 0f;

        private void LateUpdate() => Refresh(Time.deltaTime);

        /// <summary>시간을 흘리지 않고 위치·문구만 다시 맞춘다(스크린샷 캡처 중 캔버스 모드를 바꾼 직후).</summary>
        public void Refresh() => Refresh(0f);

        private void Refresh(float dt)
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null || boot.World == null || boot.Hud == null || boot.Hud.Root == null) return;
            if (layer == null)
            {
                layer = UiKit.Rect("TruckPills", boot.Hud.Root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                layer.SetAsFirstSibling(); // 프롬프트·패널 아래
            }
            bool snap = boot.Session != boundSession;
            boundSession = boot.Session;
            if (boot.Session.Paused) dt = 0f; // 게임 일시정지 중에는 트럭도 멈춘다(스펙 17.4)
            var fleet = boot.Session.Sim.Trucks;
            foreach (var dock in TruckFleet.Docks)
            {
                var car = boot.World.TruckOf(dock);
                if (!trucks.TryGetValue(dock, out var t))
                {
                    t = new Truck();
                    t.Pill = MakePill(out t.Bg, out t.Border, out t.Label);
                    t.Pill.name = "TruckPill_" + dock;
                    trucks[dock] = t;
                }
                if (t.Pill.gameObject.activeSelf != (car != null)) t.Pill.gameObject.SetActive(car != null);
                if (car == null) { t.Car = null; continue; }
                bool away = fleet.IsAway(dock);
                if (t.Car != car)
                {
                    // 새 차량(첫 프레임·세션 교체·동쪽 재생성): 집 자리를 기억하고 지금 상태로 바로 맞춘다.
                    t.Car = car;
                    t.Sign = boot.World.TruckSignOf(dock);
                    t.CarHome = car.position;
                    t.SignHome = t.Sign != null ? t.Sign.position : Vector3.zero;
                    t.Roof = RoofHeight(car);
                    t.T = away ? 1f : 0f;
                }
                float target = away ? 1f : 0f;
                if (snap) t.T = target;
                else if (dt > 0f) t.T = Mathf.MoveTowards(t.T, target, dt / DriveSeconds);
                Place(t);

                var text = HudController.TruckLabel(fleet, dock); // 상태가 같으면 같은 문자열(캐시)
                var color = away ? UiKit.Muted : Color.Lerp(PillFg, UiKit.Accent, Mathf.Clamp01(fleet.Loaded(dock) / (float)TruckFleet.TruckCapacity));
                if (t.Label.text != text)
                {
                    UiKit.SetNumText(t.Label, text, true); // "다음 차"는 한글이라 Noto, "12/30"은 Sora
                    t.Pill.sizeDelta = new Vector2(Mathf.Ceil(t.Label.preferredWidth) + PillPadX, PillHeight);
                }
                t.Label.color = color;
                t.Border.color = away ? UiKit.Line : new Color(color.r, color.g, color.b, 0.55f);
                // 떠나 있는 동안 알약은 도크 자리(집 위)에 남는다. 움직이는 동안에는 트럭을 따라간다.
                var anchor = (t.T >= 1f ? t.CarHome : t.Car.position) + new Vector3(0f, t.Roof + RoofLift, 0f);
                if (boot.Hud.TryWorldToCanvas(anchor, out var pos)) t.Pill.anchoredPosition = pos;
            }
        }

        /// <summary>출발은 천천히 출발해 빨라지고(ease-in), 도착은 빠르게 들어와 서서히 멈춘다(같은 곡선을 거꾸로).</summary>
        private static void Place(Truck t)
        {
            float k = t.T * t.T;
            var off = new Vector3(DriveDistance * k, 0f, 0f);
            bool visible = t.T < 1f;
            if (t.Car.gameObject.activeSelf != visible) t.Car.gameObject.SetActive(visible);
            t.Car.position = t.CarHome + off;
            if (t.Sign != null)
            {
                if (t.Sign.gameObject.activeSelf != visible) t.Sign.gameObject.SetActive(visible);
                t.Sign.position = t.SignHome + off;
            }
        }

        private static float RoofHeight(Transform car)
        {
            var rs = car.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 1.6f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.max.y - car.position.y;
        }

        private RectTransform MakePill(out Image bg, out Image border, out Text label)
        {
            const int r = (int)(PillHeight / 2f);
            bg = UiKit.Box("TruckPill", layer, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(48f, PillHeight), PillBg, r);
            border = UiKit.AddBorder(bg.rectTransform, r, UiKit.Line);
            label = UiKit.Label("Text", bg.transform, "", FontSize, PillFg, TextAnchor.MiddleCenter, Fonts.NumBold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return bg.rectTransform;
        }
    }
}
