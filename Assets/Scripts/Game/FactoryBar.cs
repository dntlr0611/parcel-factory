using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 여러 공장(스펙 19.3)의 HUD: 시계 위 가운데 공장 탭 알약(`1공장 · 2공장 · 3공장`, 가진 공장만, 지금 공장은 앰버, 클릭으로 옮기기),
    /// 상단 오른쪽 버튼 줄 아래 다른 공장 요약 칩(`2공장 · 대기 5 · 소실 2`), 경보(소실이 새로 생기거나 대기가 6 이상이면 칩이 빨갛게 깜빡이고
    /// 토스트 `2공장: 택배가 쌓이고 있습니다`, 공장마다 30초에 한 번까지), 옮길 때 0.3초 페이드, 손에 택배가 있으면 `손을 비우고 이동하세요`.
    /// 공장이 하나뿐이면 탭과 칩을 숨긴다.
    /// </summary>
    public sealed class FactoryBar : MonoBehaviour
    {
        public const float FadeSeconds = 0.3f, AlertCooldownSeconds = 30f, AlertBlinkSeconds = 3f;
        public const int WaitingAlertAt = 6;
        /// <summary>탭 줄: 버튼 줄(top 16, 높이 34) 아래 top 58, 높이 30 + 위아래 3. 공장이 둘 이상이면 시계·토스트를 TabShift만큼 내린다.</summary>
        public const float TabHeight = 28f, TabPad = 3f, TabTop = 58f, TabShift = 40f, ChipTop = 58f, ChipHeight = 26f;
        public const string CarryingToast = "손을 비우고 이동하세요";

        /// <summary>경보 토스트: "2공장: 택배가 쌓이고 있습니다".</summary>
        public static string AlertToast(FactorySite f) => f.Profile.ShortName + ": 택배가 쌓이고 있습니다";

        /// <summary>요약 칩 문구: "2공장 · 대기 5 · 소실 2"(소실은 그날 소실).</summary>
        public static string ChipText(FactorySite f) => ChipText(f, f.WaitingCount, f.Sim.LostCount - f.DayStartLost);

        private static string ChipText(FactorySite f, int waiting, int lostToday) => f.Profile.ShortName + " · 대기 " + waiting + " · 소실 " + lostToday;

        private sealed class Watch
        {
            public FactorySite Site;
            public int LastLost;
            public bool WasCrowded;
            /// <summary>이번 프레임에 한 번 센 값(대기 = 검사 대기 + 운송장 대기, 그날 소실)과 칩 문구를 마지막으로 만든 값.</summary>
            public int Waiting, LostToday, ShownWaiting = -1, ShownLost = -1;
            /// <summary>마지막 경보 토스트 시각(unscaled). 처음에는 아주 옛날.</summary>
            public float LastToast = float.NegativeInfinity;
            /// <summary>이 시각까지 칩이 깜빡인다(새 소실).</summary>
            public float BlinkUntil;
            public RectTransform Chip;
            public Image Bg, Border;
            public Text Label;
        }

        private RectTransform tabs, tabRow, chips;
        private Image fade;
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<Watch> watches = new List<Watch>();
        private GameSession boundSession;
        private int shownCount = -1, shownCurrent = -1;
        private float fadeT = 1f;

        /// <summary>지금 보이는 탭 수(공장이 하나면 0). 테스트용.</summary>
        public int TabCount => tabs != null && tabs.gameObject.activeSelf ? tabButtons.Count : 0;
        /// <summary>공장 번호 index(0부터)의 요약 칩 문구. 칩이 없으면(지금 공장이거나 없는 공장) null.</summary>
        public string ChipTextOf(int index)
        {
            foreach (var w in watches) if (w.Chip != null && w.Chip.gameObject.activeSelf && w.Site == FactoryAt(index)) return w.Label.text;
            return null;
        }
        /// <summary>그 공장 칩이 경보(빨강) 상태인가. 테스트용.</summary>
        public bool IsAlerting(int index)
        {
            foreach (var w in watches) if (w.Site == FactoryAt(index)) return Alerting(w);
            return false;
        }
        /// <summary>페이드 막의 지금 불투명도(0 = 안 보임).</summary>
        public float FadeAlpha => fade != null ? fade.color.a : 0f;

        private FactorySite FactoryAt(int index)
        {
            var s = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            return s != null && index >= 0 && index < s.Company.Factories.Count ? s.Company.Factories[index] : null;
        }

        /// <summary>HUD 캔버스가 생긴 뒤(HudController.Start) 한 번 만든다.</summary>
        private void Build(GameBootstrap boot)
        {
            var root = boot.Hud.Root;
            // 페이드 막은 HUD 맨 아래(월드 위)에 깐다. 옮기는 순간 검정에서 0.3초 동안 걷힌다.
            var frt = UiKit.Fill("FactoryFade", root);
            fade = frt.gameObject.AddComponent<Image>();
            fade.color = new Color(0.03f, 0.04f, 0.05f, 0f);
            fade.raycastTarget = false;
            frt.SetAsFirstSibling();

            // 알약 배경(테두리·그림자 자식이 있어 레이아웃을 걸지 않는다) 안에 탭 줄(Row)을 따로 둔다.
            tabs = UiKit.Pill("FactoryTabs", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TabTop), TabHeight + 2f * TabPad, 200f).rectTransform;
            tabRow = UiKit.Rect("Row", tabs, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, TabHeight));
            var g = UiKit.Row(tabRow, 4f, new RectOffset(0, 0, 0, 0));
            g.childAlignment = TextAnchor.MiddleCenter;
            g.childForceExpandWidth = false;
            var fit = tabRow.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            tabs.gameObject.SetActive(false);

            chips = UiKit.Rect("FactoryChips", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -ChipTop), new Vector2(600f, ChipHeight));
            UiKit.Row(chips, 6f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;

            // 상점·조작 서랍·카드가 칩과 탭을 덮도록 HUD 위쪽 요소들 바로 뒤에 둔다(나중에 만들어져도 맨 위로 오지 않게).
            var topRight = root.Find("TopRight");
            if (topRight != null) chips.SetSiblingIndex(topRight.GetSiblingIndex() + 1);
            var clock = root.Find("Clock");
            if (clock != null) tabs.SetSiblingIndex(clock.GetSiblingIndex() + 1);
            boot.Input.FactorySwitchRequested += OnSwitchRequested;
            boot.FactorySwitched += _ => { fadeT = 0f; shownCurrent = -1; };
            boot.SessionReplaced += _ => { shownCount = -1; shownCurrent = -1; };
        }

        private void OnDestroy()
        {
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.Input != null) boot.Input.FactorySwitchRequested -= OnSwitchRequested;
        }

        private void OnSwitchRequested(FactorySwitchResult r)
        {
            if (r == FactorySwitchResult.Carrying) GameBootstrap.Instance.Hud.Toast(CarryingToast, HudController.ToastKind.Bad);
        }

        private void RebuildTabs(GameSession s)
        {
            for (int i = tabRow.childCount - 1; i >= 0; i--) DestroyImmediate(tabRow.GetChild(i).gameObject);
            tabButtons.Clear();
            var list = s.Company.Factories;
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var b = UiKit.Btn("Tab" + (i + 1), tabRow, list[i].Profile.ShortName, () => GameBootstrap.Instance.Input.SwitchFactory(idx),
                    fontSize: 12, height: TabHeight, radius: Mathf.RoundToInt(TabHeight / 2f), padX: 14, elevation: UiKit.Elevation.None, font: Fonts.Bold);
                tabButtons.Add(b);
            }
            UiKit.Keycap(tabRow, "Tab");
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabRow);
            FitTabs();
            tabs.gameObject.SetActive(list.Count > 1);
            GameBootstrap.Instance.Hud.CenterShift = list.Count > 1 ? TabShift : 0f;

            foreach (var wt in watches) if (wt.Chip != null) Destroy(wt.Chip.gameObject);
            watches.Clear();
            foreach (var f in list)
            {
                int waiting = f.WaitingCount;
                var wt = new Watch { Site = f, LastLost = f.Sim.LostCount, WasCrowded = waiting >= WaitingAlertAt, Waiting = waiting };
                wt.Bg = UiKit.Pill("FactoryChip" + f.Profile.Number, chips, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, ChipHeight, 120f, UiKit.Panel, UiKit.Line, UiKit.Elevation.None);
                wt.Chip = wt.Bg.rectTransform;
                UiKit.Size(wt.Bg, -1f, ChipHeight);
                wt.Border = wt.Chip.Find("Border").GetComponent<Image>();
                wt.Label = UiKit.Label("Text", wt.Chip, "", 12, UiKit.Fg, TextAnchor.MiddleCenter, Fonts.Bold);
                wt.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
                watches.Add(wt);
            }
            shownCount = list.Count;
        }

        /// <summary>알약 배경을 탭 줄(ContentSizeFitter가 맞춘 폭)에 맞춘다. 글자 폭은 레이아웃이 끝난 뒤에야 정해지므로 매 프레임 맞춘다.</summary>
        private void FitTabs()
        {
            var size = new Vector2(tabRow.rect.width + 2f * TabPad + 8f, TabHeight + 2f * TabPad);
            if ((tabs.sizeDelta - size).sqrMagnitude > 0.25f) tabs.sizeDelta = size;
        }

        /// <summary>w.Waiting은 Update에서 그 프레임에 센 값이다.</summary>
        private static bool Alerting(Watch w) => Time.unscaledTime < w.BlinkUntil || w.Waiting >= WaitingAlertAt;

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Session == null || boot.Hud == null || boot.Hud.Root == null) return;
            if (tabs == null) Build(boot);
            var s = boot.Session;
            if (s != boundSession) { boundSession = s; shownCount = -1; }
            var list = s.Company.Factories;
            if (shownCount != list.Count) RebuildTabs(s);
            if (shownCurrent != s.Company.CurrentIndex)
            {
                shownCurrent = s.Company.CurrentIndex;
                for (int i = 0; i < tabButtons.Count; i++) UiKit.SetButtonStyle(tabButtons[i], i == shownCurrent ? BtnStyle.On : BtnStyle.Normal);
            }

            if (tabs.gameObject.activeSelf) FitTabs();
            bool many = list.Count > 1;
            if (chips.gameObject.activeSelf != many) chips.gameObject.SetActive(many);
            var hud = boot.Hud;
            foreach (var w in watches)
            {
                var f = w.Site;
                bool current = f == s.Factory;
                // 경보(스펙 19.3): 새 소실 → 깜빡임 + 토스트, 대기가 6 이상이 되는 순간 → 토스트(대기가 6 이상인 동안 깜빡임). 지금 공장은 화면에 보이므로 알리지 않는다.
                int lost = f.Sim.LostCount;
                w.Waiting = f.WaitingCount; // 택배 목록을 두 번 훑으므로 프레임당 한 번만 센다
                w.LostToday = lost - f.DayStartLost;
                bool crowded = w.Waiting >= WaitingAlertAt;
                bool newLoss = lost > w.LastLost, becameCrowded = crowded && !w.WasCrowded;
                w.LastLost = lost;
                w.WasCrowded = crowded;
                if (!current && (newLoss || becameCrowded))
                {
                    if (newLoss) w.BlinkUntil = Time.unscaledTime + AlertBlinkSeconds;
                    if (Time.unscaledTime - w.LastToast >= AlertCooldownSeconds)
                    {
                        w.LastToast = Time.unscaledTime;
                        hud.Toast(AlertToast(f), HudController.ToastKind.Bad);
                    }
                }
                bool show = many && !current;
                if (w.Chip.gameObject.activeSelf != show) w.Chip.gameObject.SetActive(show);
                if (!show) continue;
                if (w.Waiting != w.ShownWaiting || w.LostToday != w.ShownLost)
                {
                    w.ShownWaiting = w.Waiting;
                    w.ShownLost = w.LostToday;
                    w.Label.text = ChipText(f, w.Waiting, w.LostToday);
                    w.Chip.sizeDelta = new Vector2(Mathf.Ceil(w.Label.preferredWidth) + 24f, ChipHeight);
                    UiKit.Size(w.Bg, w.Chip.sizeDelta.x, ChipHeight);
                }
                bool alert = Alerting(w);
                float blink = alert ? 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * 8f) : 0f;
                w.Label.color = alert ? Color.Lerp(UiKit.Bad, UiKit.Fg, blink * 0.4f) : UiKit.Fg;
                w.Border.color = alert ? new Color(UiKit.Bad.r, UiKit.Bad.g, UiKit.Bad.b, 0.5f + 0.5f * blink) : UiKit.Line;
                w.Bg.color = alert ? new Color(0.35f, 0.08f, 0.06f, UiKit.DarkAlpha(0.55f + 0.3f * blink)) : UiKit.Panel;
            }

            if (fadeT < 1f) fadeT = Mathf.Min(1f, fadeT + Time.unscaledDeltaTime / FadeSeconds);
            var fc = fade.color; fc.a = 1f - fadeT;
            fade.color = fc;
            if (fc.a > 0f) fade.rectTransform.SetAsFirstSibling();
        }
    }
}
