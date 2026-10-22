using System;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>운송장 종이·취급주의 스티커에 붙는 UGUI 드래그 핸들러. 마우스와 터치 모두 같은 이벤트로 온다.</summary>
    public sealed class WaybillDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public WaybillCard Card;
        /// <summary>true면 취급주의 스티커(스펙 17.3), false면 운송장.</summary>
        public bool Sticker;
        public void OnBeginDrag(PointerEventData e) { if (Card != null) Card.BeginDrag(e, Sticker); }
        public void OnDrag(PointerEventData e) { if (Card != null) Card.Drag(e); }
        public void OnEndDrag(PointerEventData e) { if (Card != null) Card.EndDrag(e); }
    }

    /// <summary>
    /// 운송장 카드(스펙 15.2): 왼쪽에 위에서 내려다본 상자(골판지색 둥근 사각, 테이프 줄, 항공이면 윗면 전체에 항공 마크)와
    /// 가운데 점선 부착 영역, 오른쪽에 운송장 한 장(한 종류: 먹색 머리띠 + 흰 글자 "운송장", 항공이면 비행기 배지 + "항공", 스펙 15.4).
    /// 취급주의 택배면(스펙 17.3) 운송장 아래에 취급주의 스티커가 하나 더 나오고, 상자 그림 아래쪽에 옆면 띠와 왼쪽 아래 점선 칸이 생긴다.
    /// 끌어 중심이 자기 영역 안에 놓이면 영역 중앙에 착 붙는다. 붙이는 것은 카드 안의 상태일 뿐이다(스펙 15.5): 필요한 것이 모두 붙으면
    /// 주 버튼 "완료 · Enter"가 켜지고, 그때 Enter·E·버튼이 코어에 반영한다(WorkerService.AttachWaybill / AttachFragileSticker) → 카드가 닫히고
    /// 토스트 "운송장 부착", 택배가 출발한다. 다 붙이기 전 Enter·E는 토스트로 남은 것을 알려 줄 뿐이다. 영역 밖이면 제자리로 돌아간다.
    /// 끄는 동안 1.05배, 영역 테두리는 앰버. Esc·닫기·게임패드 B = 카드에서 붙인 것을 되돌리고 닫기(Panels.CloseAll).
    /// 게임패드 A = 남은 것 하나 붙이기, 모두 붙었으면 완료. 코어에 이미 붙어 있던 것은 영역에 붙은 채로 보이고 끌 수 없다.
    /// 모든 좌표는 카드 중심 기준(앵커·피벗 0.5)이다.
    /// </summary>
    public sealed class WaybillCard : MonoBehaviour
    {
        public const float CardW = 600f, CardH = 372f, Pad = 18f, BoxW = 250f, BoxH = 216f, PaperW = 168f, PaperH = 116f, ZoneW = 184f, ZoneH = 132f;
        public const float SnapSeconds = 0.12f, DragScale = 1.05f;
        /// <summary>취급주의 스티커(스펙 17.3): 끄는 스티커 한 변, 상자 옆면 띠 높이, 옆면 점선 칸 한 변(붙으면 칸 크기로 줄어든다).</summary>
        public const float StickerSize = 46f, SideBandH = 42f, StickerZoneSize = 38f;
        public static readonly Color Cardboard = new Color(0xC8 / 255f, 0x9B / 255f, 0x6A / 255f), Tape = new Color(0xE3 / 255f, 0xC4 / 255f, 0x92 / 255f);
        public static readonly Color SideCardboard = new Color(0xA8 / 255f, 0x7E / 255f, 0x52 / 255f);
        public static readonly Color PaperWhite = new Color(0xFB / 255f, 0xFA / 255f, 0xF6 / 255f);

        public RectTransform Root { get; private set; }
        public RectTransform Card { get; private set; }
        public RectTransform Paper { get; private set; }
        public RectTransform Zone { get; private set; }
        /// <summary>취급주의 스티커(끄는 것)와 상자 옆면 점선 칸(스펙 17.3). 취급주의가 아니면 꺼져 있다.</summary>
        public RectTransform Sticker { get; private set; }
        public RectTransform StickerZone { get; private set; }
        public bool IsOpen => Root != null && Root.gameObject.activeSelf;
        public bool IsDragging { get; private set; }
        /// <summary>필요한 것(운송장, 취급주의면 스티커까지)이 모두 영역에 붙었는가. 그때만 완료 버튼이 켜진다(스펙 15.5).</summary>
        public bool AllPlaced => paper.Done && (!NeedsSticker || sticker.Done);
        /// <summary>완료 버튼(주 버튼, 키캡 Enter).</summary>
        public Button CompleteButton { get; private set; }
        /// <summary>운송장 / 취급주의 스티커가 영역에 붙어 있는가(카드에서 붙였거나 코어에 원래 붙어 있었다). 카드에서 붙인 것은 완료 전까지 코어에 없다.</summary>
        public bool WaybillDone => paper.Done;
        public bool StickerDone => sticker.Done;
        /// <summary>카드에 취급주의 스티커가 나와 있는가.</summary>
        public bool NeedsSticker { get; private set; }
        /// <summary>운송장 종이의 집 자리(카드 중심 기준).</summary>
        public static readonly Vector2 PaperHome = new Vector2(CardW / 2f - Pad - (CardW - 3f * Pad - BoxW) / 2f + 6f, 20f);
        public static readonly Vector2 BoxCenter = new Vector2(-CardW / 2f + Pad + BoxW / 2f, 20f);
        /// <summary>취급주의 스티커의 집 자리(운송장 아래 왼쪽)와 옆면 점선 칸 중심(상자 그림 왼쪽 아래).</summary>
        public static readonly Vector2 StickerHome = new Vector2(PaperHome.x - PaperW / 2f + StickerSize / 2f, PaperHome.y - PaperH / 2f - 12f - StickerSize / 2f);
        public static readonly Vector2 StickerZoneCenter = new Vector2(BoxCenter.x - BoxW / 2f + 8f + StickerZoneSize / 2f, BoxCenter.y - BoxH / 2f + SideBandH / 2f);

        /// <summary>닫힐 때(붙였든 아니든) 알린다. Panels가 입력 차단을 갱신한다.</summary>
        public event Action Closed;

        /// <summary>끌어 붙이는 것 하나(운송장 또는 스티커).</summary>
        private sealed class Item
        {
            public RectTransform Rect, Zone;
            public Image[] Edges;
            public Image Fill;
            public Vector2 Home;
            public float DoneScale = 1f;
            /// <summary>Done = 영역에 붙어 있음. Committed = 카드를 열 때 이미 코어에 붙어 있었음(되돌리지 않는다).</summary>
            public bool Done, Committed, IsSticker;
            public Vector2 SnapFrom;
            public float SnapT, ScaleFrom = 1f;
        }

        private readonly Item paper = new Item(), sticker = new Item { IsSticker = true };
        private Item dragging;
        private Image band, airMark, airBadgeIcon, sideBand;
        private Text bandText, airBadgeText, dest, idText, weight, hint, stickerCaption;
        private RectTransform barcode;
        private Vector2 dragStartLocal, dragStartPos;
        /// <summary>끌기를 시작한 포인터. 다른 손가락·포인터의 Drag/EndDrag는 무시한다.</summary>
        private int dragPointer;
        private int openFrame = -1;
        private Package package;

        public const string HintText = "운송장을 상자 가운데 점선 칸에 끌어 붙인 뒤 Enter로 완료. Esc는 되돌리고 닫습니다.";
        public const string FragileHintText = "운송장은 가운데, 취급주의 스티커는 왼쪽 아래 옆면 칸에 끌어 붙인 뒤 Enter로 완료.";
        /// <summary>다 붙이기 전 Enter·E(스펙 15.5).</summary>
        public const string NeedWaybillToast = "운송장을 끌어 붙이세요", NeedStickerToast = "취급주의 스티커를 끌어 붙이세요";

        public void Build(RectTransform hudRoot)
        {
            Root = UiKit.ModalBackdrop("Waybill", hudRoot).rectTransform;
            Card = UiKit.Card("Card", Root, new Vector2(CardW, CardH), "운송장 붙이기");

            // 왼쪽: 위에서 본 상자 + 테이프 + 항공 마크(윗면 전체, 3D 상자와 같다)
            var box = UiKit.Box("BoxTop", Card, Center, Center, Center, BoxCenter, new Vector2(BoxW, BoxH), Cardboard, 18).rectTransform;
            UiKit.AddShadow(box, 18, UiKit.Elevation.Low);
            UiKit.Box("Tape", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Center, Vector2.zero, new Vector2(44f, 0f), Tape, 0);
            UiKit.Box("Flap", box, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Center, Vector2.zero, new Vector2(0f, 2f), new Color(0f, 0f, 0f, 0.12f), 0);
            airMark = UiKit.Box("AirMark", box, Center, Center, Center, Vector2.zero, Vector2.one * (Mathf.Min(BoxW, BoxH) - 16f), Color.white, 0);
            airMark.sprite = UiKit.Icon("mark-air"); airMark.type = Image.Type.Simple; airMark.preserveAspect = true;
            // 옆면 띠(취급주의만, 스펙 17.3): 상자 그림 아래쪽의 더 어두운 골판지 띠 + 위 경계선. 왼쪽 아래 점선 칸이 스티커 자리.
            sideBand = UiKit.Box("Side", box, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, SideBandH), SideCardboard, 18);
            UiKit.Box("Square", sideBand.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, SideBandH / 2f), SideCardboard, 0); // 위 모서리는 각지게(아래만 상자 모서리를 따라 둥글게)
            UiKit.Box("Edge", sideBand.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 2f), new Color(0f, 0f, 0f, 0.22f), 0);
            var sideLabel = UiKit.Label("Label", sideBand.rectTransform, "옆면", 11, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleRight, Fonts.Bold);
            sideLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

            // 상자 중앙 점선 부착 영역(카드 직속: 운송장과 같은 좌표계)
            Zone = UiKit.Rect("Zone", Card, Center, Center, Center, BoxCenter, new Vector2(ZoneW, ZoneH));
            paper.Zone = Zone;
            paper.Fill = UiKit.Box("Fill", Zone, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.14f), 10);
            paper.Edges = DashEdges(Zone, ZoneH);
            var zl = UiKit.Label("Label", Zone, "여기에 붙이기", 13, Color.white, TextAnchor.MiddleCenter, Fonts.Bold);
            zl.raycastTarget = false;
            var zo = zl.gameObject.AddComponent<Outline>(); // 상자 윗면 전체의 항공 마크(파랑·흰 비행기) 위에서도 읽히게
            zo.effectColor = new Color(0.05f, 0.08f, 0.12f, 0.85f); zo.effectDistance = new Vector2(1.5f, -1.5f);

            // 옆면 점선 칸(스티커 자리)
            StickerZone = UiKit.Rect("StickerZone", Card, Center, Center, Center, StickerZoneCenter, new Vector2(StickerZoneSize, StickerZoneSize));
            sticker.Zone = StickerZone;
            sticker.Fill = UiKit.Box("Fill", StickerZone, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.14f), 7);
            sticker.Edges = DashEdges(StickerZone, StickerZoneSize);

            // 오른쪽: 운송장 한 장(끌 수 있다)
            Paper = UiKit.Box("Paper", Card, Center, Center, Center, PaperHome, new Vector2(PaperW, PaperH), PaperWhite, 6).rectTransform;
            UiKit.AddShadow(Paper, 6, UiKit.Elevation.Panel);
            var paperImg = Paper.GetComponent<Image>();
            paperImg.raycastTarget = true;
            Paper.gameObject.AddComponent<WaybillDrag>().Card = this;
            paper.Rect = Paper; paper.Home = PaperHome;
            band = UiKit.Box("Band", Paper, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 26f), MaterialLibrary.WaybillInk, 6);
            bandText = UiKit.Label("Kind", band.rectTransform, WaybillTitle, 12, Color.white, TextAnchor.MiddleLeft, Fonts.Bold);
            bandText.rectTransform.offsetMin = new Vector2(10f, 0f);
            airBadgeIcon = UiKit.Box("AirIcon", band.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(14f, 14f), Color.white, 0);
            airBadgeIcon.sprite = UiKit.Icon("ui-air"); airBadgeIcon.type = Image.Type.Simple; airBadgeIcon.preserveAspect = true;
            airBadgeText = UiKit.Label("AirBadge", band.rectTransform, PackageFactory.AirMethod, 11, Color.white, TextAnchor.MiddleRight, Fonts.Bold);
            airBadgeText.rectTransform.offsetMax = new Vector2(-10f, 0f);
            dest = UiKit.Label("Dest", Paper, "", 20, UiKit.PaperInk, TextAnchor.UpperLeft, Fonts.Bold);
            dest.rectTransform.offsetMin = new Vector2(10f, 0f); dest.rectTransform.offsetMax = new Vector2(-10f, -32f);
            idText = UiKit.Label("Id", Paper, "", 11, UiKit.PaperMuted, TextAnchor.UpperRight, Fonts.Num);
            idText.rectTransform.offsetMin = new Vector2(10f, 0f); idText.rectTransform.offsetMax = new Vector2(-10f, -34f);
            weight = UiKit.Label("Weight", Paper, "", 11, UiKit.PaperMuted, TextAnchor.UpperLeft);
            weight.rectTransform.offsetMin = new Vector2(10f, 0f); weight.rectTransform.offsetMax = new Vector2(-10f, -60f);
            barcode = UiKit.Rect("Barcode", Paper, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-20f, 22f));

            // 운송장 아래: 취급주의 스티커(mark-fragile: 주황 둥근 사각 + 흰 깨진 유리잔)와 설명
            var st = UiKit.Box("Sticker", Card, Center, Center, Center, StickerHome, new Vector2(StickerSize, StickerSize), Color.white, 0);
            st.sprite = UiKit.Icon("mark-fragile"); st.type = Image.Type.Simple; st.preserveAspect = true; st.raycastTarget = true;
            Sticker = st.rectTransform;
            UiKit.AddShadow(Sticker, 8, UiKit.Elevation.Low);
            var sd = Sticker.gameObject.AddComponent<WaybillDrag>();
            sd.Card = this; sd.Sticker = true;
            sticker.Rect = Sticker; sticker.Home = StickerHome; sticker.DoneScale = StickerZoneSize / StickerSize;
            stickerCaption = UiKit.Label("StickerCaption", Card, "취급주의 스티커", 12, UiKit.Fg, TextAnchor.MiddleLeft, Fonts.Bold);
            UiKit.Place(stickerCaption.rectTransform, Center, new Vector2(0f, 0.5f), StickerHome + new Vector2(StickerSize / 2f + 10f, 0f), new Vector2(140f, 20f));

            hint = UiKit.Label("Hint", Card, HintText, 12, UiKit.Muted, TextAnchor.UpperLeft);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f); hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 16f + 34f + 10f); hint.rectTransform.sizeDelta = new Vector2(-2f * Pad, 18f);
            var buttons = UiKit.Rect("Buttons", Card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-2f * Pad, 34f));
            UiKit.Row(buttons, 8f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            UiKit.Btn("Close", buttons, "닫기", () => GameBootstrap.Instance.Panels.CloseAll(), key: "Esc", minWidth: 72f);
            CompleteButton = UiKit.Btn("Complete", buttons, "완료", () => Complete(), key: "Enter", style: BtnStyle.Primary, minWidth: 88f);
            Root.gameObject.SetActive(false);
        }

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private static Image[] DashEdges(RectTransform zone, float height) => new[]
        {
            DashEdge(zone, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), false, height), DashEdge(zone, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), false, height),
            DashEdge(zone, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), true, height), DashEdge(zone, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), true, height),
        };

        private static Image DashEdge(RectTransform zone, string name, Vector2 a0, Vector2 a1, bool vertical, float height)
        {
            var rt = UiKit.Rect(name, zone, a0, a1, Center, Vector2.zero, Vector2.zero);
            if (vertical)
            {
                // 세로 변: 가로 점선을 90° 돌린다(길이 = 영역 높이)
                rt.anchorMin = rt.anchorMax = (a0 + a1) / 2f;
                rt.sizeDelta = new Vector2(height, 2f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else rt.sizeDelta = new Vector2(0f, 2f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Dash(); img.type = Image.Type.Tiled; img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0.75f);
            return img;
        }

        /// <summary>운송장 머리띠 글자(스펙 15.4: 출고·반품 구분 없음).</summary>
        public const string WaybillTitle = "운송장";

        /// <summary>부착 대기 택배로 카드를 연다. 항공 배지·번호·목적지·무게·바코드를 채우고, 취급주의면 스티커·옆면 칸을 보인다.
        /// 이미 붙은 것(운송장 또는 스티커)은 자기 영역에 붙은 채로 보이고 끌 수 없다.</summary>
        public void Open(Package p)
        {
            if (p == null || Root == null) return;
            package = p;
            airBadgeIcon.gameObject.SetActive(p.Air);
            airBadgeText.gameObject.SetActive(p.Air);
            airMark.gameObject.SetActive(p.Air);
            dest.text = p.Label != null ? p.Label.Destination : "";
            UiKit.SetNumText(idText, "#" + p.Id, false);
            weight.text = p.Label != null ? "표기 무게 " + p.Label.Weight + " kg" : "";
            BuildBarcode(p.Id);
            NeedsSticker = p.Fragile;
            sideBand.gameObject.SetActive(NeedsSticker);
            StickerZone.gameObject.SetActive(NeedsSticker);
            Sticker.gameObject.SetActive(NeedsSticker);
            stickerCaption.gameObject.SetActive(NeedsSticker && !p.FragileSticker);
            hint.text = NeedsSticker ? FragileHintText : HintText;
            Reset(paper, p.Waybill);
            Reset(sticker, p.FragileSticker);
            IsDragging = false;
            dragging = null;
            RefreshComplete();
            openFrame = Time.frameCount;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
        }

        private static void Reset(Item it, bool done)
        {
            it.Done = done;
            it.Committed = done;
            it.SnapT = 0f;
            it.Rect.anchoredPosition = done ? it.Zone.anchoredPosition : it.Home;
            it.Rect.localScale = Vector3.one * (done ? it.DoneScale : 1f);
            SetZoneHot(it, false);
        }

        private void BuildBarcode(int seed)
        {
            for (int i = barcode.childCount - 1; i >= 0; i--) Destroy(barcode.GetChild(i).gameObject);
            var rng = new System.Random(seed);
            float x = 0f, width = PaperW - 20f;
            while (x < width - 4f)
            {
                float w = 1f + rng.Next(3);
                UiKit.Box("Bar", barcode, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, 0f), UiKit.PaperInk, 0);
                x += w + 2f + rng.Next(3);
            }
        }

        /// <summary>닫는다(Esc·닫기·게임패드 B, 또는 완료 뒤). 카드에서 붙인 것은 코어에 반영되지 않았으므로 그대로 사라진다(되돌림).
        /// 택배는 부착대에 남는다. 다음에 열면 코어 상태로 다시 그린다.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            Root.gameObject.SetActive(false);
            IsDragging = false;
            dragging = null;
            paper.Done = paper.Committed;
            sticker.Done = sticker.Committed;
            package = null;
            var s = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            if (s != null) s.Worker.CancelLabel();
            Closed?.Invoke();
        }

        private static void SetZoneHot(Item it, bool hot)
        {
            var c = hot ? UiKit.Accent : new Color(1f, 1f, 1f, 0.75f);
            foreach (var e in it.Edges) e.color = c;
            it.Fill.color = hot ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.18f) : new Color(1f, 1f, 1f, 0.14f);
        }

        private bool ToCardLocal(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Card, e.position, e.pressEventCamera, out local);

        /// <summary>운송장 끌기 시작(예전 호출 형태).</summary>
        public void BeginDrag(PointerEventData e) => BeginDrag(e, false);

        public void BeginDrag(PointerEventData e, bool stickerItem)
        {
            if (IsDragging) return; // 이미 하나를 끌고 있다(두 번째 손가락)
            var it = stickerItem ? sticker : paper;
            if (!IsOpen || it.Done || (stickerItem && !NeedsSticker) || !ToCardLocal(e, out dragStartLocal)) return;
            dragging = it;
            dragPointer = e.pointerId;
            dragStartPos = it.Rect.anchoredPosition;
            IsDragging = true;
            it.SnapT = 0f;
            it.Rect.localScale = Vector3.one * DragScale;
            it.Rect.SetAsLastSibling();
            SetZoneHot(it, true);
        }

        public void Drag(PointerEventData e)
        {
            if (!IsDragging || dragging == null || e.pointerId != dragPointer || !ToCardLocal(e, out var local)) return;
            dragging.Rect.anchoredPosition = dragStartPos + (local - dragStartLocal);
        }

        public void EndDrag(PointerEventData e)
        {
            if (!IsDragging || dragging == null || e.pointerId != dragPointer) return;
            var it = dragging;
            IsDragging = false;
            dragging = null;
            it.Rect.localScale = Vector3.one;
            SetZoneHot(it, false);
            if (Contains(it.Zone, it.Rect.anchoredPosition)) Place(it);
            else StartSnap(it); // 제자리로
        }

        /// <summary>카드 기준 점(운송장 중심)이 운송장 부착 영역 안인가.</summary>
        public bool ZoneContains(Vector2 cardPoint) => Contains(Zone, cardPoint);

        /// <summary>카드 기준 점이 옆면 스티커 칸 안인가.</summary>
        public bool StickerZoneContains(Vector2 cardPoint) => Contains(StickerZone, cardPoint);

        private static bool Contains(RectTransform zone, Vector2 cardPoint)
        {
            var d = cardPoint - zone.anchoredPosition;
            return Mathf.Abs(d.x) <= zone.sizeDelta.x / 2f && Mathf.Abs(d.y) <= zone.sizeDelta.y / 2f;
        }

        /// <summary>카드 안에서만 붙인다(영역에 착 맞춤). 코어는 완료 때 바뀐다.</summary>
        private void Place(Item it)
        {
            it.Done = true;
            if (it.IsSticker) stickerCaption.gameObject.SetActive(false);
            StartSnap(it);
            RefreshComplete();
        }

        private void RefreshComplete()
        {
            if (CompleteButton == null) return;
            bool ready = AllPlaced;
            CompleteButton.interactable = ready;
            UiKit.SetButtonStyle(CompleteButton, ready ? BtnStyle.Primary : BtnStyle.Normal);
        }

        /// <summary>게임패드 A(끌 수 없으므로): 남은 것 하나(운송장 → 스티커 순)를 붙인다. 모두 붙었으면 완료. 붙였으면 true.</summary>
        public bool PlaceNext()
        {
            if (!IsOpen || IsDragging) return false;
            if (!paper.Done) { Place(paper); return true; }
            if (NeedsSticker && !sticker.Done) { Place(sticker); return true; }
            Complete();
            return false;
        }

        /// <summary>
        /// Enter·E·완료 버튼(스펙 15.5): 필요한 것이 모두 붙었으면 코어에 반영하고(카드에서 붙인 것만) 닫는다 → 토스트 "운송장 부착", 택배 출발.
        /// 아니면 아무것도 하지 않고 남은 것을 토스트로 알린다.
        /// </summary>
        public void Complete()
        {
            if (!IsOpen || IsDragging) return;
            var hud = GameBootstrap.Instance.Hud;
            if (!AllPlaced)
            {
                if (hud != null) hud.Toast(!paper.Done ? NeedWaybillToast : NeedStickerToast);
                return;
            }
            var w = GameBootstrap.Instance.Session.Worker;
            bool ok = (paper.Committed || w.AttachWaybill()) && (!NeedsSticker || sticker.Committed || w.AttachFragileSticker());
            if (ok) { paper.Committed = true; sticker.Committed = NeedsSticker; }
            Close();
            if (hud != null)
            {
                if (ok) hud.Toast(HudController.WaybillAttachedToast, HudController.ToastKind.Good);
                else hud.Toast("운송장을 붙일 수 없습니다", HudController.ToastKind.Bad);
            }
        }

        private static void StartSnap(Item it)
        {
            it.SnapFrom = it.Rect.anchoredPosition;
            it.ScaleFrom = it.Rect.localScale.x;
            it.SnapT = SnapSeconds;
        }

        private static void StepSnap(Item it)
        {
            if (it.SnapT <= 0f) return;
            it.SnapT = Mathf.Max(0f, it.SnapT - Time.unscaledDeltaTime);
            var target = it.Done ? it.Zone.anchoredPosition : it.Home;
            float k = 1f - it.SnapT / SnapSeconds;
            k = k * k * (3f - 2f * k);
            it.Rect.anchoredPosition = Vector2.Lerp(it.SnapFrom, target, k);
            it.Rect.localScale = Vector3.one * Mathf.Lerp(it.ScaleFrom, it.Done ? it.DoneScale : 1f, k);
        }

        private void Update()
        {
            if (!IsOpen) return;
            StepSnap(paper);
            StepSnap(sticker);
            if (Time.frameCount == openFrame) return; // 카드를 연 E가 같은 프레임에 완료로 읽히지 않게
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (IsDragging) return;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) Complete(); // 붙이지는 않는다(스펙 15.5)
            else if (pad != null && pad.buttonSouth.wasPressedThisFrame) PlaceNext();    // 게임패드 A = 하나 붙이기, 다 붙었으면 완료
            else if (pad != null && pad.buttonEast.wasPressedThisFrame) GameBootstrap.Instance.Panels.CloseAll(); // 게임패드 B = 되돌리고 닫기
        }

        /// <summary>대기 중인 택배(카드가 연 것). 테스트용.</summary>
        public Package Package => package;
    }
}
