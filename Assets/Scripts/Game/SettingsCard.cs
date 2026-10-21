using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>
    /// 설정 카드(스펙 13.3): 시작 화면 위 §12 카드(폭 420). 소리(음악·배경소리 슬라이더, 즉시 반영) · 화면(모드 세그먼트, 해상도 스테퍼, 적용으로만 반영).
    /// 키보드: ↑/↓ 행 이동, ←/→ 값 조절(슬라이더 5씩), Enter 적용, Esc 닫기. 선택 행은 앰버 테두리. 모바일·터치 모드에서는 화면 묶음을 숨긴다.
    /// 키 입력은 StartScreen이 HandleKeys로 넘긴다(Esc가 카드만 닫도록 한곳에서 처리).
    /// </summary>
    public sealed class SettingsCard : MonoBehaviour
    {
        public const float Width = 420f, Pad = 18f, RowH = 44f, NoteRowH = 62f, SliderStep = 5f;
        public const string MissingClipNote = "음원 없음 · Resources/Audio에 넣으면 재생됩니다";
        public const string WindowedLabel = "창 모드", FullscreenLabel = "전체 화면";
        public enum Row { Music, Ambient, Mode, Resolution }

        public bool IsOpen => gameObject.activeSelf;
        /// <summary>선택 행(열면 0 = 음악).</summary>
        public int SelectedRow { get; private set; } = -1;
        /// <summary>화면 묶음이 보이는가(데스크톱·마우스에서만).</summary>
        public bool DisplayVisible { get; private set; }
        public UiSlider MusicSlider { get; private set; }
        public UiSlider AmbientSlider { get; private set; }
        public UiSegment ModeSegment { get; private set; }
        public UiStepper ResolutionStepper { get; private set; }
        public Button ApplyButton { get; private set; }
        public Button CloseButton { get; private set; }
        /// <summary>적용을 기다리는 화면 값.</summary>
        public bool PendingFullscreen { get; private set; }
        public Vector2Int PendingResolution { get; private set; }
        public IReadOnlyList<Vector2Int> ResolutionOptions => options;

        private GameBootstrap boot;
        private RectTransform area, card, body, displayTitle;
        private readonly List<RectTransform> rows = new List<RectTransform>();
        private readonly List<Image> rings = new List<Image>();
        private Text musicNote, ambientNote;
        private List<Vector2Int> options = new List<Vector2Int>();

        /// <summary>parent(시작 화면 캔버스) 아래에 카드를 만든다. 닫힌 채로 돌려준다.</summary>
        public static SettingsCard Build(Transform parent)
        {
            var dim = UiKit.ModalBackdrop("Settings", parent).rectTransform;
            var sc = dim.gameObject.AddComponent<SettingsCard>();
            sc.BuildCard(dim);
            dim.gameObject.SetActive(false);
            return sc;
        }

        private void BuildCard(RectTransform dim)
        {
            // 카드는 "Area"(시작 화면 메뉴 열 오른쪽의 빈 영역, 캔버스 높이 전체)의 가운데. 캔버스 크기가 바뀌어도 따라간다.
            area = UiKit.Fill("Area", dim);
            card = UiKit.Card("Card", area, new Vector2(Width, 400f), "설정");
            body = UiKit.Rect("Body", card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(Pad + 22f + 10f)), new Vector2(-2f * Pad, 300f));
            var col = UiKit.Column(body, 8f, new RectOffset(0, 0, 0, 0));
            col.childForceExpandWidth = true;

            Section("SoundTitle", "소리");
            MusicSlider = SliderRow(Row.Music, "음악", out musicNote);
            AmbientSlider = SliderRow(Row.Ambient, "배경소리", out ambientNote);
            displayTitle = Section("DisplayTitle", "화면");
            var modeRow = MakeRow(Row.Mode, "화면 모드");
            ModeSegment = UiKit.Segment("Mode", modeRow, new[] { WindowedLabel, FullscreenLabel }, 200f);
            UiKit.Place((RectTransform)ModeSegment.transform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-12f, -RowH / 2f), new Vector2(200f, 30f));
            ModeSegment.Changed += i => { SetPendingMode(i == 1); Select((int)Row.Mode); };
            var resRow = MakeRow(Row.Resolution, "해상도");
            ResolutionStepper = UiKit.Stepper("Resolution", resRow, 200f);
            UiKit.Place((RectTransform)ResolutionStepper.transform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-12f, -RowH / 2f), new Vector2(200f, 30f));
            ResolutionStepper.Changed += i => { if (i < options.Count) PendingResolution = options[i]; Select((int)Row.Resolution); RefreshApply(); };

            var buttons = UiKit.Rect("Buttons", card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-2f * Pad, 34f));
            UiKit.Row(buttons, 8f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            CloseButton = UiKit.Btn("Close", buttons, "닫기", Close, key: "Esc", minWidth: 64f);
            ApplyButton = UiKit.Btn("Apply", buttons, "적용", Apply, style: BtnStyle.Primary, key: "Enter", minWidth: 72f);
        }

        private RectTransform Section(string name, string text)
        {
            var t = UiKit.Label(name, body, text, 12, UiKit.Muted, TextAnchor.LowerLeft, Fonts.Bold);
            UiKit.Size(t, -1f, 20f);
            return t.rectTransform;
        }

        /// <summary>행: §12 상태창 행처럼 흰색 .05 · 반경 10 · 1px 선. 선택이면 앰버 테두리("Selected"). 누르면 선택된다.</summary>
        private RectTransform MakeRow(Row row, string label)
        {
            var bg = UiKit.PanelBox(row.ToString(), body, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, RowH), UiKit.RowBg, UiKit.RButton, UiKit.Line, UiKit.Elevation.None);
            UiKit.Size(bg, -1f, RowH);
            var rt = bg.rectTransform;
            var ring = UiKit.AddBorder(rt, UiKit.RButton, UiKit.Accent, 1.5f, 0f, "Selected");
            ring.gameObject.SetActive(false);
            var l = UiKit.Label("Label", rt, label, 13, UiKit.Fg, TextAnchor.MiddleLeft);
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(14f, -RowH / 2f), new Vector2(120f, 24f));
            int index = (int)row;
            var et = rt.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ => Select(index));
            et.triggers.Add(entry);
            rows.Add(rt); rings.Add(ring);
            return rt;
        }

        private UiSlider SliderRow(Row row, string label, out Text note)
        {
            var rt = MakeRow(row, label);
            var slider = UiKit.Slider("Slider", rt, 170f);
            UiKit.Place((RectTransform)slider.transform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-14f, -RowH / 2f), new Vector2(170f + 12f + 32f, 24f));
            int index = (int)row;
            slider.Pressed += () => Select(index);
            // 드래그 중에는 메모리 값과 실제 볼륨만 바꾸고, 디스크 저장(PlayerPrefs.Save)은 손을 뗄 때·카드를 닫을 때 한다.
            slider.Changed += v => { if (boot == null) return; if (row == Row.Music) boot.Settings.SetMusic(v, false); else boot.Settings.SetAmbient(v, false); };
            slider.Released += () => { if (boot != null) boot.Settings.Flush(); };
            note = UiKit.Label("Note", rt, MissingClipNote, 11, UiKit.Muted, TextAnchor.MiddleLeft);
            note.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Place(note.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 8f), new Vector2(360f, 16f));
            note.gameObject.SetActive(false);
            return slider;
        }

        /// <summary>
        /// 현재 설정으로 채워 연다. 화면 값의 미적용 변경은 버린다. 카드는 freeLeft(캔버스 왼쪽에서 잰 빈 영역 시작, 시작 화면 메뉴 열의 오른쪽)부터
        /// 캔버스 오른쪽 끝까지의 가운데, 세로 가운데에 놓인다(제목·메뉴를 가리지 않게). 첫 행(음악)을 선택해 둔다.
        /// </summary>
        public void Open(float freeLeft = 0f)
        {
            boot = GameBootstrap.Instance;
            var s = boot.Settings;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            DisplayVisible = !Application.isMobilePlatform && (boot.Hud == null || !boot.Hud.TouchMode);
            displayTitle.gameObject.SetActive(DisplayVisible);
            rows[(int)Row.Mode].gameObject.SetActive(DisplayVisible);
            rows[(int)Row.Resolution].gameObject.SetActive(DisplayVisible);
            ApplyButton.gameObject.SetActive(DisplayVisible);
            SetNote(Row.Music, musicNote, boot.Audio == null || !boot.Audio.HasMusic);
            SetNote(Row.Ambient, ambientNote, boot.Audio == null || !boot.Audio.HasAmbient);
            PendingFullscreen = s.Fullscreen;
            PendingResolution = s.Resolution;
            ModeSegment.SetSelected(s.Fullscreen ? 1 : 0, false);
            Populate(s.Fullscreen, s.Resolution, true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(body);
            MusicSlider.SetValue(s.Music, false);
            AmbientSlider.SetValue(s.Ambient, false);
            float bodyH = LayoutUtility.GetPreferredHeight(body);
            body.sizeDelta = new Vector2(body.sizeDelta.x, bodyH);
            card.sizeDelta = new Vector2(Width, Pad + 22f + 10f + bodyH + 18f + 34f + 16f);
            PlaceCard(freeLeft);
            Select(0);
            RefreshApply();
        }

        private void PlaceCard(float freeLeft)
        {
            float canvasW = ((RectTransform)transform).rect.width;
            // 빈 영역이 카드보다 좁으면(아주 좁은 창) 화면 가운데로 되돌린다.
            if (canvasW > 0f && canvasW - freeLeft < Width + 32f) freeLeft = 0f;
            area.offsetMin = new Vector2(Mathf.Max(0f, freeLeft), 0f);
        }

        /// <summary>카드가 놓인 빈 영역의 왼쪽 끝(테스트용).</summary>
        public float AreaLeft => area.offsetMin.x;

        /// <summary>모드에 맞는 해상도 후보로 스테퍼를 채우고 target에 가장 가까운 후보를 고른다.
        /// 창 모드인데 target이 창 상한을 넘으면(전체 화면 크기에서 바꾼 경우) 기본 창 크기(모니터 70%)에 가장 가까운 후보.
        /// keepExact(카드를 열 때): 저장된 크기가 후보에 없어도(손으로 바꾼 창 등) 목록에 넣어 그대로 고른다 → 열자마자 적용이 켜지지 않는다.</summary>
        private void Populate(bool fullscreen, Vector2Int target, bool keepExact = false)
        {
            options = GameSettings.CurrentResolutionOptions(fullscreen);
            var monitor = GameSettings.Monitor;
            if (keepExact && target.x > 0 && target.y > 0 && !options.Contains(target))
            {
                options.Add(target);
                options.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            }
            else if (!keepExact && !fullscreen && monitor.x > 0 && !GameSettings.FitsWindowed(target, monitor)) target = GameSettings.DefaultWindowed(monitor);
            int index = options.Count > 0 ? GameSettings.Nearest(options, target) : 0;
            ResolutionStepper.SetItems(options.Count, i => GameSettings.ResolutionLabel(options[i]), index);
            PendingFullscreen = fullscreen;
            PendingResolution = options.Count > 0 ? options[ResolutionStepper.Index] : target;
        }

        /// <summary>세그먼트로 모드를 바꾸면 후보를 다시 채우고 가장 가까운 후보로 맞춘다.</summary>
        private void SetPendingMode(bool fullscreen)
        {
            Populate(fullscreen, PendingResolution);
            RefreshApply();
        }

        private void SetNote(Row row, Text note, bool missing)
        {
            note.gameObject.SetActive(missing);
            UiKit.Size(rows[(int)row], -1f, missing ? NoteRowH : RowH);
        }

        /// <summary>음원이 없어 "음원 없음" 안내가 보이는가.</summary>
        public bool NoteVisible(Row row) => (row == Row.Music ? musicNote : ambientNote).gameObject.activeSelf;

        /// <summary>닫는다. 드래그로 바뀐 소리 값을 디스크에 쓴다.</summary>
        public void Close()
        {
            if (boot != null && gameObject.activeSelf) boot.Settings.Flush();
            gameObject.SetActive(false);
        }

        private int RowCount => DisplayVisible ? 4 : 2;

        private void Select(int i)
        {
            SelectedRow = i < 0 ? -1 : Mathf.Clamp(i, 0, RowCount - 1);
            for (int k = 0; k < rings.Count; k++) rings[k].gameObject.SetActive(k == SelectedRow);
        }

        /// <summary>선택을 delta행 옮긴다. 선택이 없으면 ↓는 첫 행, ↑는 마지막 행.</summary>
        public void MoveRow(int delta)
        {
            if (SelectedRow < 0) { Select(delta > 0 ? 0 : RowCount - 1); return; }
            Select((SelectedRow + delta + RowCount) % RowCount);
        }

        /// <summary>선택 행의 값을 ←(−1)/→(+1)만큼 바꾼다. 슬라이더는 5씩.</summary>
        public void Adjust(int dir)
        {
            switch ((Row)SelectedRow)
            {
                case Row.Music: MusicSlider.SetValue(MusicSlider.Value + dir * (int)SliderStep, true); boot.Settings.Flush(); break; // 키보드는 누를 때마다 저장
                case Row.Ambient: AmbientSlider.SetValue(AmbientSlider.Value + dir * (int)SliderStep, true); boot.Settings.Flush(); break;
                case Row.Mode: ModeSegment.SetSelected(dir < 0 ? 0 : 1, true); break;
                case Row.Resolution: ResolutionStepper.Step(dir); break;
            }
        }

        /// <summary>적용: 화면 값이 바뀌었을 때만 동작한다.</summary>
        public void Apply()
        {
            if (!DisplayChanged) return;
            boot.Settings.SetDisplay(PendingFullscreen, PendingResolution, boot);
            RefreshApply();
        }

        /// <summary>적용 대기 중인 화면 변경이 있는가(적용 버튼 활성 조건).</summary>
        public bool DisplayChanged => boot != null && (PendingFullscreen != boot.Settings.Fullscreen || PendingResolution != boot.Settings.Resolution);

        private void RefreshApply()
        {
            ApplyButton.interactable = DisplayChanged;
            ApplyButton.GetComponent<UiButton>().Apply();
        }

        /// <summary>키 입력 한 프레임(StartScreen이 넘긴다).</summary>
        public void HandleKeys(bool up, bool down, bool left, bool right, bool enter, bool esc)
        {
            if (esc) { Close(); return; }
            if (up) MoveRow(-1);
            else if (down) MoveRow(1);
            else if (left) Adjust(-1);
            else if (right) Adjust(1);
            else if (enter) Apply();
        }
    }
}
