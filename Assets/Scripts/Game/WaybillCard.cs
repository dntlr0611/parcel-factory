using System;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>운송장 종이에 붙는 UGUI 드래그 핸들러. 마우스와 터치 모두 같은 이벤트로 온다.</summary>
    public sealed class WaybillDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public WaybillCard Card;
        public void OnBeginDrag(PointerEventData e) { if (Card != null) Card.BeginDrag(e); }
        public void OnDrag(PointerEventData e) { if (Card != null) Card.Drag(e); }
        public void OnEndDrag(PointerEventData e) { if (Card != null) Card.EndDrag(e); }
    }

    /// <summary>
    /// 운송장 카드(스펙 15.2): 왼쪽에 위에서 내려다본 상자(골판지색 둥근 사각, 테이프 줄, 항공이면 모서리에 항공 마크)와
    /// 가운데 점선 부착 영역, 오른쪽에 택배에 맞는 운송장 한 장(승인 → 출고 운송장 파랑 머리띠, 거부 → 반품 운송장 빨강, 항공 배지).
    /// 운송장을 끌어 중심이 영역 안에 놓이면 영역 중앙에 착 붙고(WorkerService.AttachWaybill) 0.4초 뒤 닫히며 토스트 "운송장 부착".
    /// 영역 밖이면 제자리로 돌아간다. 끄는 동안 1.05배, 영역 테두리는 앰버. Enter·E·게임패드 A = 바로 붙이기, Esc·게임패드 B = 붙이지 않고 닫기(Panels.CloseAll).
    /// 모든 좌표는 카드 중심 기준(앵커·피벗 0.5)이다.
    /// </summary>
    public sealed class WaybillCard : MonoBehaviour
    {
        public const float CardW = 600f, CardH = 372f, Pad = 18f, BoxW = 250f, BoxH = 216f, PaperW = 168f, PaperH = 116f, ZoneW = 184f, ZoneH = 132f;
        public const float SnapSeconds = 0.12f, CloseDelay = 0.4f, DragScale = 1.05f;
        public static readonly Color Cardboard = new Color(0xC8 / 255f, 0x9B / 255f, 0x6A / 255f), Tape = new Color(0xE3 / 255f, 0xC4 / 255f, 0x92 / 255f);
        public static readonly Color PaperWhite = new Color(0xFB / 255f, 0xFA / 255f, 0xF6 / 255f);

        public RectTransform Root { get; private set; }
        public RectTransform Card { get; private set; }
        public RectTransform Paper { get; private set; }
        public RectTransform Zone { get; private set; }
        public bool IsOpen => Root != null && Root.gameObject.activeSelf;
        public bool IsDragging { get; private set; }
        /// <summary>붙인 뒤 닫히기를 기다리는 중(0.4초).</summary>
        public bool Attached { get; private set; }
        /// <summary>운송장 종이의 집 자리(카드 중심 기준).</summary>
        public static readonly Vector2 PaperHome = new Vector2(CardW / 2f - Pad - (CardW - 3f * Pad - BoxW) / 2f + 6f, 20f);
        public static readonly Vector2 BoxCenter = new Vector2(-CardW / 2f + Pad + BoxW / 2f, 20f);

        /// <summary>닫힐 때(붙였든 아니든) 알린다. Panels가 입력 차단을 갱신한다.</summary>
        public event Action Closed;

        private Image band, zoneFill, airMark, airBadgeIcon;
        private Image[] zoneEdges;
        private Text bandText, airBadgeText, dest, idText, weight;
        private RectTransform barcode;
        private Vector2 dragStartLocal, dragStartPos, snapFrom;
        private float snapT, closeAt;
        private int openFrame = -1;
        private Package package;

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

            // 상자 중앙 점선 부착 영역(카드 직속: 운송장과 같은 좌표계)
            Zone = UiKit.Rect("Zone", Card, Center, Center, Center, BoxCenter, new Vector2(ZoneW, ZoneH));
            zoneFill = UiKit.Box("Fill", Zone, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.14f), 10);
            zoneEdges = new[]
            {
                DashEdge("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), 0f), DashEdge("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), 0f),
                DashEdge("Left", new Vector2(0f, 0f), new Vector2(0f, 1f), 90f), DashEdge("Right", new Vector2(1f, 0f), new Vector2(1f, 1f), 90f),
            };
            var zl = UiKit.Label("Label", Zone, "여기에 붙이기", 13, Color.white, TextAnchor.MiddleCenter, Fonts.Bold);
            zl.raycastTarget = false;
            var zo = zl.gameObject.AddComponent<Outline>(); // 상자 윗면 전체의 항공 마크(파랑·흰 비행기) 위에서도 읽히게
            zo.effectColor = new Color(0.05f, 0.08f, 0.12f, 0.85f); zo.effectDistance = new Vector2(1.5f, -1.5f);

            // 오른쪽: 운송장 한 장(끌 수 있다)
            Paper = UiKit.Box("Paper", Card, Center, Center, Center, PaperHome, new Vector2(PaperW, PaperH), PaperWhite, 6).rectTransform;
            UiKit.AddShadow(Paper, 6, UiKit.Elevation.Panel);
            var paperImg = Paper.GetComponent<Image>();
            paperImg.raycastTarget = true;
            Paper.gameObject.AddComponent<WaybillDrag>().Card = this;
            band = UiKit.Box("Band", Paper, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 26f), MaterialLibrary.WaybillOutBand, 6);
            bandText = UiKit.Label("Kind", band.rectTransform, "출고 운송장", 12, Color.white, TextAnchor.MiddleLeft, Fonts.Bold);
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

            var hint = UiKit.Label("Hint", Card, "운송장을 끌어 상자 가운데 점선 칸에 놓으세요. Enter·E로 바로 붙이고, Esc는 붙이지 않고 닫습니다.", 12, UiKit.Muted, TextAnchor.UpperLeft);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f); hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 16f + 34f + 10f); hint.rectTransform.sizeDelta = new Vector2(-2f * Pad, 18f);
            var buttons = UiKit.Rect("Buttons", Card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-2f * Pad, 34f));
            UiKit.Row(buttons, 8f, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleRight;
            UiKit.Btn("Close", buttons, "닫기", () => GameBootstrap.Instance.Panels.CloseAll(), key: "Esc", minWidth: 72f);
            UiKit.Btn("Attach", buttons, "붙이기", () => AttachNow(), key: "Enter", style: BtnStyle.Primary, minWidth: 88f);
            Root.gameObject.SetActive(false);
        }

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private Image DashEdge(string name, Vector2 a0, Vector2 a1, float rot)
        {
            bool vertical = rot != 0f;
            var rt = UiKit.Rect(name, Zone, a0, a1, Center, Vector2.zero, Vector2.zero);
            if (vertical)
            {
                // 세로 변: 가로 점선을 90° 돌린다(길이 = 영역 높이)
                rt.anchorMin = rt.anchorMax = (a0 + a1) / 2f;
                rt.sizeDelta = new Vector2(ZoneH, 2f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else rt.sizeDelta = new Vector2(0f, 2f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Dash(); img.type = Image.Type.Tiled; img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0.75f);
            return img;
        }

        /// <summary>부착 대기 택배로 카드를 연다. 운송장 종류(출고/반품)·항공 배지·번호·목적지·무게·바코드를 채운다.</summary>
        public void Open(Package p)
        {
            if (p == null || Root == null) return;
            package = p;
            bool ret = p.Rejected;
            band.color = ret ? MaterialLibrary.WaybillReturnBand : MaterialLibrary.WaybillOutBand;
            bandText.text = ret ? "반품 운송장" : "출고 운송장";
            airBadgeIcon.gameObject.SetActive(p.Air);
            airBadgeText.gameObject.SetActive(p.Air);
            airMark.gameObject.SetActive(p.Air);
            dest.text = p.Label != null ? p.Label.Destination : "";
            UiKit.SetNumText(idText, "#" + p.Id, false);
            weight.text = p.Label != null ? "표기 무게 " + p.Label.Weight + " kg" : "";
            BuildBarcode(p.Id);
            Paper.anchoredPosition = PaperHome;
            Paper.localScale = Vector3.one;
            IsDragging = false;
            Attached = false;
            SetZoneHot(false);
            openFrame = Time.frameCount;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
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

        /// <summary>붙이지 않고 닫는다(Esc·닫기). 택배는 부착대에 남는다.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            Root.gameObject.SetActive(false);
            IsDragging = false;
            Attached = false;
            package = null;
            var s = GameBootstrap.Instance != null ? GameBootstrap.Instance.Session : null;
            if (s != null) s.Worker.CancelLabel();
            Closed?.Invoke();
        }

        private void SetZoneHot(bool hot)
        {
            var c = hot ? UiKit.Accent : new Color(1f, 1f, 1f, 0.75f);
            foreach (var e in zoneEdges) e.color = c;
            zoneFill.color = hot ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.18f) : new Color(1f, 1f, 1f, 0.14f);
        }

        private bool ToCardLocal(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Card, e.position, e.pressEventCamera, out local);

        public void BeginDrag(PointerEventData e)
        {
            if (!IsOpen || Attached || !ToCardLocal(e, out dragStartLocal)) return;
            dragStartPos = Paper.anchoredPosition;
            IsDragging = true;
            snapT = 0f;
            Paper.localScale = Vector3.one * DragScale;
            Paper.SetAsLastSibling();
            SetZoneHot(true);
        }

        public void Drag(PointerEventData e)
        {
            if (!IsDragging || !ToCardLocal(e, out var local)) return;
            Paper.anchoredPosition = dragStartPos + (local - dragStartLocal);
        }

        public void EndDrag(PointerEventData e)
        {
            if (!IsDragging) return;
            IsDragging = false;
            Paper.localScale = Vector3.one;
            SetZoneHot(false);
            if (ZoneContains(Paper.anchoredPosition)) Attach();
            else StartSnap(); // 제자리로
        }

        /// <summary>카드 기준 점(운송장 중심)이 부착 영역 안인가.</summary>
        public bool ZoneContains(Vector2 cardPoint)
        {
            var d = cardPoint - Zone.anchoredPosition;
            return Mathf.Abs(d.x) <= ZoneW / 2f && Mathf.Abs(d.y) <= ZoneH / 2f;
        }

        /// <summary>키보드 대체(Enter·E) 또는 붙이기 버튼: 끌지 않고 바로 붙인다.</summary>
        public void AttachNow()
        {
            if (!IsOpen || Attached) return;
            IsDragging = false;
            Paper.localScale = Vector3.one;
            SetZoneHot(false);
            Attach();
        }

        private void Attach()
        {
            var s = GameBootstrap.Instance.Session;
            bool ok = s.Worker.AttachWaybill();
            var hud = GameBootstrap.Instance.Hud;
            if (!ok)
            {
                if (hud != null) hud.Toast("운송장을 붙일 수 없습니다", HudController.ToastKind.Bad);
                Close();
                return;
            }
            Attached = true;
            closeAt = Time.unscaledTime + CloseDelay;
            StartSnap();
        }

        private void StartSnap()
        {
            snapFrom = Paper.anchoredPosition;
            snapT = SnapSeconds;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (snapT > 0f)
            {
                snapT = Mathf.Max(0f, snapT - Time.unscaledDeltaTime);
                var target = Attached ? Zone.anchoredPosition : PaperHome;
                float k = 1f - snapT / SnapSeconds;
                Paper.anchoredPosition = Vector2.Lerp(snapFrom, target, k * k * (3f - 2f * k));
            }
            if (Attached)
            {
                if (Time.unscaledTime >= closeAt)
                {
                    Attached = false;
                    Close();
                    var hud = GameBootstrap.Instance.Hud;
                    if (hud != null) hud.Toast(HudController.WaybillAttachedToast, HudController.ToastKind.Good);
                }
                return;
            }
            if (Time.frameCount == openFrame) return; // 카드를 연 E가 같은 프레임에 붙이기로 읽히지 않게
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (IsDragging) return;
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame)) AttachNow();       // 게임패드 A = 붙이기
            else if (pad != null && pad.buttonEast.wasPressedThisFrame) GameBootstrap.Instance.Panels.CloseAll(); // 게임패드 B = 닫기
        }

        /// <summary>대기 중인 택배(카드가 연 것). 테스트용.</summary>
        public Package Package => package;
    }
}
