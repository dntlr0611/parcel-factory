using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>상점, 작업자 상태창, 검사 카드, 해체 확인, 하루 요약. 열려 있으면 월드 입력을 막는다.
    /// 상점과 상태창은 같은 자리에 뜨며 동시에 열리지 않는다(하나를 열면 다른 하나는 닫힌다).
    /// 하루 요약은 모달이다: Escape·CloseAll로 닫히지 않고 "다음 날" 버튼(Session.NextDay)으로만 닫힌다.</summary>
    [DefaultExecutionOrder(10)]
    public sealed class Panels : MonoBehaviour
    {
        private RectTransform shop, status, inspect, demolish, summary, shopList, statusList;
        /// <summary>검사 카드 배송 방법 줄의 비행기 아이콘(항공 택배일 때만, 스펙 14.2).</summary>
        private Image iMethodAir;
        /// <summary>검사 카드의 운송장 상태(스펙 15.3).</summary>
        private Text iWaybill;
        private Text iDest, iWeight, iMethod, iScale, iId, dText, dRefund, shopMoney, statusExp, sTitle, sDelivered, sLost, sInspected, sMoney, sExp, sTrucks;
        /// <summary>검사 카드의 취급주의·파손 줄(스펙 17.1·17.2). 취급주의 택배일 때만 보이고 그만큼 종이·카드가 길어진다.</summary>
        private Text iFragile, iBroken;
        /// <summary>취급주의 줄의 깨진 유리잔 아이콘(ui-fragile, 주황).</summary>
        private Image iFragileIcon;
        private RectTransform iPaper, iCard, iHint;
        private float iPaperH, iCardH, iHintY;
        /// <summary>검사 카드 한 줄 높이(줄 22 + 간격 7).</summary>
        public const float InspectRowStep = 29f;
        /// <summary>검사 카드 취급주의 줄 색(종이 위 주황, mark-fragile #F28B30보다 어둡게 해 글자가 읽히게).</summary>
        public static readonly Color FragileInk = new Color(0xC8 / 255f, 0x5E / 255f, 0x12 / 255f);
        /// <summary>상태창 행 순서.</summary>
        public static readonly Stat[] StatOrder = { Stat.Stamina, Stat.Speed, Stat.Regen };
        private (int x, int y)? pendingDemolish;
        private InputController input;
        private HudController hud;

        public bool AnyOpen => (shop != null && shop.gameObject.activeSelf) || StatusOpen || (inspect != null && inspect.gameObject.activeSelf) || (demolish != null && demolish.gameObject.activeSelf) || SummaryOpen || WaybillOpen;
        /// <summary>운송장 카드(스펙 15.2).</summary>
        public WaybillCard Waybill { get; private set; }
        public bool WaybillOpen => Waybill != null && Waybill.IsOpen;
        /// <summary>상점만 열려 있는가. 이때는 B 키·상점 버튼이 상점을 닫는 토글로 동작한다.</summary>
        public bool ShopIsOnlyOpen => shop != null && shop.gameObject.activeSelf && !inspect.gameObject.activeSelf && !demolish.gameObject.activeSelf && !SummaryOpen && !WaybillOpen;
        /// <summary>작업자 상태창이 떠 있는가.</summary>
        public bool StatusOpen => status != null && status.gameObject.activeSelf;
        /// <summary>상태창만 열려 있는가. 이때는 C 키·상태 버튼이 상태창을 닫는 토글로 동작한다(B는 상점으로 바꾼다).</summary>
        public bool StatusIsOnlyOpen => StatusOpen && !inspect.gameObject.activeSelf && !demolish.gameObject.activeSelf && !SummaryOpen && !WaybillOpen;
        /// <summary>하루 요약 패널이 떠 있는가.</summary>
        public bool SummaryOpen => summary != null && summary.gameObject.activeSelf;
        private bool lastAnyOpen;
        private Economy watchedEconomy;
        private GameSession watchedSession;

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input; hud = boot.Hud;
            BuildShop(); BuildStatus(); BuildInspect(); BuildDemolish(); BuildSummary();
            Waybill = gameObject.AddComponent<WaybillCard>();
            Waybill.Build(hud.Root);
            Waybill.Closed += Sync;
            input.ToggleShop += ToggleShop;
            input.ToggleStatus += ToggleStatus;
            input.Escape += CloseAll;
            input.Interacted += r =>
            {
                if (r == InteractResult.InspectionRequired) OpenInspect();
                else if (r == InteractResult.LabelRequired) OpenWaybill();
            };
            WatchEconomy(boot.Session);
            WatchSession(boot.Session);
            boot.SessionReplaced += s => { WatchEconomy(s); WatchSession(s); CloseAll(); };
        }

        /// <summary>DayEnded를 받을 세션을 바꾼다. 이미 퇴근 상태로 불러온 세션이면 요약을 바로 띄우고, 아니면 요약을 닫는다.</summary>
        private void WatchSession(GameSession s)
        {
            if (watchedSession != null) { watchedSession.DayEnded -= ShowSummary; watchedSession.Progression.ExpChanged -= OnExpChanged; }
            watchedSession = s;
            watchedSession.DayEnded += ShowSummary;
            watchedSession.Progression.ExpChanged += OnExpChanged;
            if (!s.Clock.IsOpen) ShowSummary(s.BuildSummary());
            else { summary.gameObject.SetActive(false); Sync(); }
        }

        private void WatchEconomy(GameSession s)
        {
            if (watchedEconomy != null) watchedEconomy.MoneyChanged -= OnMoneyChanged;
            watchedEconomy = s.Economy;
            watchedEconomy.MoneyChanged += OnMoneyChanged;
        }

        private void OnMoneyChanged(int _) { if (shop != null && shop.gameObject.activeSelf) RefreshShop(); }

        private void OnExpChanged(int _) { if (StatusOpen) RefreshStatus(); }

        private void OnDestroy()
        {
            if (watchedEconomy != null) watchedEconomy.MoneyChanged -= OnMoneyChanged;
            if (watchedSession != null) { watchedSession.DayEnded -= ShowSummary; watchedSession.Progression.ExpChanged -= OnExpChanged; }
        }

        /// <summary>패널 열림 상태를 입력 차단에 즉시 반영한다. 열고 닫는 같은 프레임에 InputController.Update가 낡은 상태를 보지 않게 한다.</summary>
        private void Sync()
        {
            bool open = AnyOpen;
            if (open == lastAnyOpen) return;
            lastAnyOpen = open;
            input.SetBlock("panels", open);
            if (open) input.ClearPad();
        }

        private void Update()
        {
            var session = GameBootstrap.Instance.Session;
            if (inspect.gameObject.activeSelf && session.Worker.PendingInspection == null) inspect.gameObject.SetActive(false);
            if (WaybillOpen && !Waybill.Attached && session.Worker.PendingLabel == null) Waybill.Close(); // 다른 경로로 대기가 풀렸다
            if (SummaryOpen && session.Clock.IsOpen) summary.gameObject.SetActive(false); // 다른 경로로 다음 날이 시작됐다
            Sync();
        }

        /// <summary>닫을 수 있는 패널을 모두 닫는다. 하루 요약은 모달이라 그대로 둔다.</summary>
        public void CloseAll()
        {
            shop.gameObject.SetActive(false);
            status.gameObject.SetActive(false);
            if (inspect.gameObject.activeSelf) { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); }
            demolish.gameObject.SetActive(false); pendingDemolish = null;
            if (WaybillOpen && !Waybill.Attached) Waybill.Close();
            Sync();
        }

        /// <summary>부착대에서 E(LabelRequired): 대기 택배로 운송장 카드를 연다(스펙 15.2).</summary>
        private void OpenWaybill()
        {
            var p = GameBootstrap.Instance.Session.Worker.PendingLabel;
            if (p == null) return;
            Waybill.Open(p);
            Sync();
        }

        /// <summary>모달 카드(시안 .modal + .card): 화면 전체 막 "name" 아래 "Card"(panel-2, 반경 14, 폭 380, 패딩 18, 제목 Noto Bold 15).</summary>
        private RectTransform Card(string name, Vector2 size, string title)
        {
            var dim = UiKit.ModalBackdrop(name, hud.Root).rectTransform;
            UiKit.Card("Card", dim, size, title);
            dim.gameObject.SetActive(false);
            return dim;
        }

        /// <summary>상점 서랍 높이: 머리 14 + 30 + 6, 항목 12개 × 42(버튼 30 + 위아래 6), 아래 10.</summary>
        public const float ShopHeight = 14f + 30f + 6f + 12f * ShopRowH + 10f;
        private const float ShopRowH = 42f, DrawerTop = 62f;

        /// <summary>시안 .drawer 머리: 제목(Noto Bold 14) + 오른쪽 값 + 닫기 버튼(높이 30, 12).</summary>
        private Text DrawerHeader(RectTransform panel, string title, string valueName, Color valueColor, Font valueFont, int valueSize)
        {
            var t = UiKit.Label("Title", panel, title, 14, UiKit.Fg, TextAnchor.MiddleLeft, Fonts.Bold);
            UiKit.Top(t.rectTransform, 14f, 30f, 14f, 14f);
            var v = UiKit.Label(valueName, panel, "", valueSize, valueColor, TextAnchor.MiddleRight, valueFont);
            UiKit.Top(v.rectTransform, 14f, 30f, 14f, 14f + 56f + 10f);
            var close = UiKit.Btn("Close", panel, "닫기", CloseAll, height: 30f, fontSize: 12);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(56f, 30f));
            return v;
        }

        private void BuildShop()
        {
            // 시안 .drawer: 오른쪽 16, top 62, 폭 330, 패딩 14, 행 사이 1px 선, 구매 버튼(.buy) 최소 76 · 높이 30 · Sora 12.
            shop = UiKit.PanelBox("Shop", hud.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -DrawerTop), new Vector2(330f, ShopHeight), UiKit.Panel2).rectTransform;
            shopMoney = DrawerHeader(shop, "해금 상점", "Money", UiKit.Accent, Fonts.Bold, 12);
            shopList = UiKit.Rect("List", shop, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(-28f, 12f * ShopRowH));
            var col = UiKit.Column(shopList, 0f, new RectOffset(0, 0, 0, 0));
            col.childControlHeight = true;
            bool first = true;
            foreach (var def in UnlockCatalog.All)
            {
                var row = UiKit.Rect("Row", shopList, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, ShopRowH));
                UiKit.Size(row, -1f, ShopRowH);
                if (!first) UiKit.Divider("Line", row);
                first = false;
                var name = UiKit.Label("Name", row, def.Name, 13, UiKit.Fg, TextAnchor.MiddleLeft);
                name.rectTransform.offsetMin = new Vector2(0f, 0f); name.rectTransform.offsetMax = new Vector2(-92f, 0f);
                var id = def.Id;
                var b = UiKit.Btn("Buy", row, def.Price.ToString(), () => { var r = GameBootstrap.Instance.Session.Unlocks.TryPurchase(id); hud.Toast(r == UnlockResult.Ok ? def.Name + " 해금" : r == UnlockResult.NotEnoughMoney ? "잔액이 부족합니다" : r == UnlockResult.RequirementMissing ? "먼저 해금해야 하는 항목이 있습니다" : "이미 보유 중입니다", r == UnlockResult.Ok ? HudController.ToastKind.Good : HudController.ToastKind.Info); RefreshShop(); },
                    font: Fonts.Num, fontSize: 12, height: 30f, fixedSize: new Vector2(76f, 30f));
                UiKit.Place((RectTransform)b.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(76f, 30f));
            }
            shop.gameObject.SetActive(false);
        }

        private void RefreshShop()
        {
            var s = GameBootstrap.Instance.Session;
            shopMoney.text = "₩ " + s.Economy.Money;
            int i = 0;
            foreach (var def in UnlockCatalog.All)
            {
                var row = shopList.GetChild(i++);
                var b = row.Find("Buy").GetComponent<Button>();
                bool owned = s.Unlocks.IsPurchased(def.Id), blocked = def.Requires.HasValue && !s.Unlocks.IsPurchased(def.Requires.Value), afford = s.Economy.CanAfford(def.Price);
                UiKit.SetButtonText(b, owned ? "보유" : blocked ? "잠김" : def.Price.ToString(), num: true);
                b.interactable = !owned && !blocked && afford;
                UiKit.SetButtonStyle(b, !owned && !blocked && afford ? BtnStyle.Primary : BtnStyle.Normal);
            }
        }

        /// <summary>B 키·상점 버튼. 상태창이 열려 있으면 닫고 상점을 연다.</summary>
        private void ToggleShop()
        {
            if (inspect.gameObject.activeSelf || demolish.gameObject.activeSelf || SummaryOpen) return;
            bool open = !shop.gameObject.activeSelf;
            status.gameObject.SetActive(false);
            shop.gameObject.SetActive(open);
            if (open) RefreshShop();
            Sync();
        }

        /// <summary>C 키·상태 버튼. 상점이 열려 있으면 닫고 상태창을 연다. 검사·해체·하루 요약 중에는 무시한다.</summary>
        public void ToggleStatus()
        {
            if (inspect.gameObject.activeSelf || demolish.gameObject.activeSelf || SummaryOpen) return;
            bool open = !status.gameObject.activeSelf;
            shop.gameObject.SetActive(false);
            status.gameObject.SetActive(open);
            if (open) RefreshStatus();
            Sync();
        }

        public static string StatName(Stat st) => st == Stat.Stamina ? "체력" : st == Stat.Speed ? "이동 속도" : "회복력";

        /// <summary>현재 효과: "최대 체력 120", "속도 +8%", "회복 +50%".</summary>
        public static string StatEffect(Progression p, Stat st) =>
            st == Stat.Stamina ? "최대 체력 " + Mathf.RoundToInt(p.MaxStamina)
            : st == Stat.Speed ? "속도 +" + Mathf.RoundToInt(Progression.SpeedPerLevel * 100f * p.Level(st)) + "%"
            : "회복 +" + Mathf.RoundToInt(Progression.RegenPerLevel * 100f * p.Level(st)) + "%";

        /// <summary>강화 성공 토스트: "체력 강화! 최대 체력 140".</summary>
        public static string UpgradeToast(Progression p, Stat st) => StatName(st) + " 강화! " + StatEffect(p, st);

        private void BuildStatus()
        {
            // 시안 .wpanel: 서랍 폭 390, 간격 8. 행(.wrow)은 반경 10, 흰색 .05, 1px 선, 패딩 10 12.
            const float w = 390f, rowH = 62f;
            status = UiKit.PanelBox("WorkerStatus", hud.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -DrawerTop), new Vector2(w, 14f + 30f + 8f + 3f * rowH + 2f * 8f + 8f + 18f + 12f), UiKit.Panel2).rectTransform;
            var title = UiKit.Label("Title", status, "작업자 상태", 15, UiKit.Fg, TextAnchor.MiddleLeft, Fonts.Bold);
            UiKit.Top(title.rectTransform, 14f, 30f, 14f, 14f);
            statusExp = UiKit.Label("Exp", status, "", 13, UiKit.ExpColor, TextAnchor.MiddleRight, Fonts.NumBold);
            UiKit.Top(statusExp.rectTransform, 14f, 30f, 14f, 14f + 56f + 10f);
            var close = UiKit.Btn("Close", status, "닫기", CloseAll, height: 30f, fontSize: 12);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(56f, 30f));
            statusList = UiKit.Rect("List", status, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(-28f, 3f * rowH + 16f));
            UiKit.Column(statusList, 8f, new RectOffset(0, 0, 0, 0));
            foreach (var st in StatOrder)
            {
                var stat = st;
                var row = UiKit.PanelBox("Row_" + st, statusList, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, rowH), UiKit.RowBg, UiKit.RButton, UiKit.Line, UiKit.Elevation.None).rectTransform;
                UiKit.Size(row, -1f, rowH);
                var name = UiKit.Label("Name", row, StatName(st), 14, UiKit.Fg, TextAnchor.LowerLeft, Fonts.Bold);
                UiKit.Top(name.rectTransform, 10f, 22f, 12f, 130f);
                var effect = UiKit.Label("Effect", row, "", 12, UiKit.ExpColor, TextAnchor.UpperLeft);
                UiKit.Top(effect.rectTransform, 35f, 18f, 12f, 130f);
                var level = UiKit.Label("Level", row, "", 11, UiKit.Muted, TextAnchor.UpperRight, Fonts.Num);
                UiKit.Top(level.rectTransform, 8f, 14f, 12f, 12f);
                var b = UiKit.Btn("Upgrade", row, "", () => Upgrade(stat), fontSize: 12, height: 30f, fixedSize: new Vector2(108f, 30f));
                UiKit.Place((RectTransform)b.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 8f), new Vector2(108f, 30f));
            }
            var hint = UiKit.Label("Hint", status, "EXP는 정상 출고 +10 · 불량 반품 +8 · 검사 +2로 모입니다.", 11, UiKit.Muted, TextAnchor.LowerLeft);
            hint.rectTransform.offsetMin = new Vector2(16f, 12f); hint.rectTransform.offsetMax = new Vector2(-16f, 0f);
            status.gameObject.SetActive(false);
        }

        private void Upgrade(Stat st)
        {
            var p = GameBootstrap.Instance.Session.Progression;
            var r = p.TryUpgrade(st);
            if (r == UpgradeResult.Ok) hud.Toast(UpgradeToast(p, st), HudController.ToastKind.Good);
            else if (r == UpgradeResult.NotEnoughExp) hud.Toast("EXP가 부족합니다");
            else hud.Toast("이미 최대 레벨입니다");
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var p = GameBootstrap.Instance.Session.Progression;
            statusExp.text = "EXP " + p.Exp;
            for (int i = 0; i < StatOrder.Length; i++)
            {
                var st = StatOrder[i];
                var row = statusList.GetChild(i);
                row.Find("Effect").GetComponent<Text>().text = StatEffect(p, st);
                row.Find("Level").GetComponent<Text>().text = "Lv " + p.Level(st) + "/" + p.MaxLevel(st);
                var b = row.Find("Upgrade").GetComponent<Button>();
                bool maxed = p.IsMaxed(st), can = !maxed && p.Exp >= p.Cost(st);
                UiKit.SetButtonText(b, maxed ? "MAX" : "강화 " + p.Cost(st) + " EXP", num: true, bold: true);
                b.interactable = can;
                UiKit.SetButtonStyle(b, can ? BtnStyle.Primary : BtnStyle.Normal);
            }
        }

        private const float CardW = 380f, CardPad = 18f, CardBtnH = 34f;

        /// <summary>카드 아래 오른쪽 정렬 버튼 줄(시안 .row-btns, 간격 8). 버튼은 내용 폭.</summary>
        private static RectTransform ButtonRow(Transform card, TextAnchor align = TextAnchor.MiddleRight)
        {
            var row = UiKit.Rect("Buttons", card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-2f * CardPad, CardBtnH));
            UiKit.Row(row, 8f, new RectOffset(0, 0, 0, 0)).childAlignment = align;
            return row;
        }

        private void BuildInspect()
        {
            // 시안 .card + .label: 종이색 #f6f2e8 반경 10 상자(안쪽 1px #e2d9c6), 행 12/14, 저울 실측은 점선 위 빨강.
            const float paperH = 12f + 4f * 22f + 3f * 7f + 10f + 10f;
            inspect = Card("Inspect", new Vector2(CardW, CardPad + 22f + 12f + paperH + 12f + 36f + 12f + CardBtnH + 16f), "택배 검사");
            var card = inspect.Find("Card");
            iCard = (RectTransform)card; iCardH = iCard.sizeDelta.y; iPaperH = paperH; iHintY = CardPad + 22f + 12f + paperH + 12f;
            var idKey = UiKit.Keycap(card, "#1", 20f, 11, "IdKey");
            UiKit.Place((RectTransform)idKey.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-CardPad, -CardPad), new Vector2(30f, 20f));
            iId = idKey.Text; iId.name = "Id";
            iWaybill = UiKit.Label("WaybillState", card, "운송장 없음", 12, UiKit.Muted, TextAnchor.MiddleRight, Fonts.Bold);
            UiKit.Place(iWaybill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-CardPad - 30f - 8f, -CardPad), new Vector2(120f, 20f));
            var paper = UiKit.Box("Paper", card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(CardPad + 22f + 12f)), new Vector2(-2f * CardPad, paperH), UiKit.Paper, UiKit.RButton).rectTransform;
            UiKit.AddBorder(paper, UiKit.RButton, UiKit.PaperLine);
            Text Row(string n, string lbl, float y, Color c)
            {
                var l = UiKit.Label(n + "L", paper, lbl, 12, UiKit.PaperMuted, TextAnchor.MiddleLeft); UiKit.Top(l.rectTransform, y, 22f, 14f, 14f);
                var v = UiKit.Label(n, paper, "", 14, c, TextAnchor.MiddleRight, Fonts.Num); UiKit.Top(v.rectTransform, y, 22f, 14f, 14f);
                return v;
            }
            iDest = Row("Dest", "목적지", 12f, UiKit.PaperInk); iWeight = Row("Weight", "표기 무게", 12f + 29f, UiKit.PaperInk); iMethod = Row("Method", "배송 방법", 12f + 58f, UiKit.PaperInk);
            var airRt = UiKit.Rect("MethodAir", paper, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-14f, -(12f + 58f + 11f)), new Vector2(16f, 16f));
            iMethodAir = airRt.gameObject.AddComponent<Image>();
            iMethodAir.sprite = UiKit.Icon("ui-air"); iMethodAir.preserveAspect = true; iMethodAir.raycastTarget = false;
            iMethodAir.color = WorldBuilder.AirBlue;
            airRt.gameObject.SetActive(false);
            var dash = UiKit.Rect("Dash", paper, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(12f + 87f + 2f)), new Vector2(-28f, 1f));
            var dashImg = dash.gameObject.AddComponent<Image>(); dashImg.sprite = UiSprites.Dash(); dashImg.type = Image.Type.Tiled; dashImg.color = UiKit.PaperDash; dashImg.raycastTarget = false;
            iScale = Row("Scale", "저울 실측", 12f + 87f + 10f, UiKit.Danger);
            // 취급주의(주황)·파손(빨강) 줄은 저울 실측 아래에 붙는다. 보일 때만 종이가 아래로 길어진다(OpenInspect).
            iFragile = Row("Fragile", "취급", 12f + 87f + 10f + InspectRowStep, FragileInk);
            iBroken = Row("Broken", "상태", 12f + 87f + 10f + 2f * InspectRowStep, UiKit.Danger);
            var fragRt = UiKit.Rect("FragileIcon", paper, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-14f, -(12f + 87f + 10f + InspectRowStep + 11f)), new Vector2(16f, 16f));
            iFragileIcon = fragRt.gameObject.AddComponent<Image>();
            iFragileIcon.sprite = UiKit.Icon("ui-fragile"); iFragileIcon.preserveAspect = true; iFragileIcon.raycastTarget = false;
            iFragileIcon.color = FragileInk;
            iPaper = paper;
            var hint = UiKit.Label("Hint", card, "표기와 실측이 다르면 거부하세요. 거부한 택배는 반품구로 보내야 합니다.", 12, UiKit.Muted, TextAnchor.UpperLeft);
            hint.lineSpacing = 1.1f;
            UiKit.Top(hint.rectTransform, CardPad + 22f + 12f + paperH + 12f, 36f, CardPad, CardPad);
            iHint = hint.rectTransform;
            var buttons = ButtonRow(card);
            UiKit.Btn("CancelInspect", buttons, "취소", () => { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); Sync(); }, minWidth: 64f);
            UiKit.Btn("Reject", buttons, "거부", () => { GameBootstrap.Instance.Session.Worker.Inspect(true); inspect.gameObject.SetActive(false); Sync(); }, style: BtnStyle.Danger, minWidth: 72f);
            UiKit.Btn("Approve", buttons, "승인", () => { GameBootstrap.Instance.Session.Worker.Inspect(false); inspect.gameObject.SetActive(false); Sync(); }, style: BtnStyle.Ok, minWidth: 72f);
        }

        private void OpenInspect()
        {
            var p = GameBootstrap.Instance.Session.Worker.PendingInspection;
            if (p == null) return;
            iId.text = "#" + p.Id;
            UiKit.SetNumText(iDest, p.Label.Destination, false); UiKit.SetNumText(iWeight, p.Label.Weight + " kg", false);
            UiKit.SetNumText(iMethod, p.Label.Method, false); UiKit.SetNumText(iScale, p.Label.Scale + " kg", false);
            // 항공이면 배송 방법 글자 왼쪽에 비행기 아이콘(값은 오른쪽 정렬, 오른쪽 여백 14).
            iMethodAir.gameObject.SetActive(p.Air);
            iWaybill.text = p.Waybill ? "운송장 ✓" : "운송장 없음"; // 스펙 15.3
            iWaybill.color = p.Waybill ? UiKit.Good : UiKit.Muted;
            if (p.Air) iMethodAir.rectTransform.anchoredPosition = new Vector2(-(14f + Mathf.Ceil(iMethod.preferredWidth) + 6f), iMethodAir.rectTransform.anchoredPosition.y);
            // 취급주의 줄(스펙 17.1), 파손 줄 빨강(스펙 17.2: 파손품은 거부가 맞다).
            int rows = p.Fragile ? (p.Broken ? 2 : 1) : 0;
            SetRow(iFragile, rows >= 1); SetRow(iBroken, rows >= 2);
            UiKit.SetNumText(iFragile, HudController.FragileTag, false);
            UiKit.SetNumText(iBroken, HudController.BrokenTag + " · 거부하세요", false);
            iFragileIcon.gameObject.SetActive(rows >= 1);
            if (rows >= 1) iFragileIcon.rectTransform.anchoredPosition = new Vector2(-(14f + Mathf.Ceil(iFragile.preferredWidth) + 6f), iFragileIcon.rectTransform.anchoredPosition.y);
            float extra = rows * InspectRowStep;
            iPaper.sizeDelta = new Vector2(iPaper.sizeDelta.x, iPaperH + extra);
            iCard.sizeDelta = new Vector2(iCard.sizeDelta.x, iCardH + extra);
            UiKit.Top(iHint, iHintY + extra, 36f, CardPad, CardPad);
            inspect.gameObject.SetActive(true);
            Sync();
        }

        /// <summary>검사 카드 종이 줄(값 + 왼쪽 라벨 "…L")을 함께 켜고 끈다.</summary>
        private static void SetRow(Text value, bool on)
        {
            value.gameObject.SetActive(on);
            var label = value.transform.parent.Find(value.name + "L");
            if (label != null) label.gameObject.SetActive(on);
        }

        private void BuildDemolish()
        {
            demolish = Card("Demolish", new Vector2(CardW, CardPad + 22f + 12f + 20f + 6f + 18f + 16f + CardBtnH + 16f), "해체");
            var card = demolish.Find("Card");
            dText = UiKit.Label("Text", card, "", 13, UiKit.Fg, TextAnchor.UpperLeft); UiKit.Top(dText.rectTransform, CardPad + 22f + 12f, 20f, CardPad, CardPad);
            dRefund = UiKit.Label("Refund", card, "", 12, UiKit.Muted, TextAnchor.UpperLeft); UiKit.Top(dRefund.rectTransform, CardPad + 22f + 12f + 26f, 18f, CardPad, CardPad);
            var buttons = ButtonRow(card);
            UiKit.Btn("Cancel", buttons, "취소", () => { pendingDemolish = null; demolish.gameObject.SetActive(false); Sync(); }, minWidth: 64f);
            UiKit.Btn("Confirm", buttons, "해체", () => { if (pendingDemolish.HasValue) { var r = GameBootstrap.Instance.Session.Build.BeginDemolish(pendingDemolish.Value.x, pendingDemolish.Value.y); if (r != BuildResult.Ok) hud.Toast(HudController.DemolishMessage(r)); } pendingDemolish = null; demolish.gameObject.SetActive(false); Sync(); }, style: BtnStyle.Danger, minWidth: 72f);
        }

        private void BuildSummary()
        {
            // 시안 .sum: 행 사이 1px 선, 패딩 9 0, 라벨 muted 13, 값 Sora Bold 16. "다음 날"은 primary, 최소 150 · 높이 40 · 15.
            const float rowH = 36f, top = CardPad + 22f + 6f;
            summary = Card("DaySummary", new Vector2(CardW, top + 6f * rowH + 16f + 40f + 18f), "1일차 마감");
            var card = summary.Find("Card");
            sTitle = card.Find("Title").GetComponent<Text>();
            int index = 0;
            Text Row(string n, string lbl, Color c)
            {
                float y = top + index * rowH;
                if (index > 0) { var line = UiKit.Divider(n + "Line", card); UiKit.Top(line.rectTransform, y, 1f, CardPad, CardPad); }
                index++;
                var l = UiKit.Label(n + "L", card, lbl, 13, UiKit.Muted, TextAnchor.MiddleLeft); UiKit.Top(l.rectTransform, y, rowH, CardPad, CardPad);
                var v = UiKit.Label(n, card, "", 16, c, TextAnchor.MiddleRight, Fonts.NumBold); UiKit.Top(v.rectTransform, y, rowH, CardPad, CardPad);
                return v;
            }
            sDelivered = Row("Delivered", "배송", UiKit.Good);
            sLost = Row("Lost", "소실", UiKit.Bad);
            sInspected = Row("Inspected", "검사", UiKit.Fg);
            sExp = Row("Exp", "획득 EXP", UiKit.ExpColor);
            sMoney = Row("Money", "잔액 변화", UiKit.Accent);
            sTrucks = Row("Trucks", "출발한 트럭", UiKit.Fg); // 스펙 18
            var next = UiKit.Btn("NextDay", card, "다음 날", NextDay, style: BtnStyle.Primary, height: 40f, fontSize: 15, fixedSize: new Vector2(150f, 40f));
            UiKit.Place((RectTransform)next.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(150f, 40f));
        }

        /// <summary>하루 요약을 채워 띄운다(DayEnded, 또는 퇴근 상태로 불러온 세션).</summary>
        private void ShowSummary(DaySummary d)
        {
            shop.gameObject.SetActive(false);
            status.gameObject.SetActive(false);
            demolish.gameObject.SetActive(false); pendingDemolish = null;
            if (inspect.gameObject.activeSelf) { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); }
            sTitle.text = d.Day + "일차 마감";
            UiKit.SetNumText(sDelivered, d.Delivered + "건", true);
            UiKit.SetNumText(sLost, d.Lost + "건", true);
            UiKit.SetNumText(sInspected, d.Inspected + "건", true);
            UiKit.SetNumText(sMoney, d.StartMoney + " → " + d.EndMoney + " (" + HudController.Signed(d.EndMoney - d.StartMoney) + ")", true);
            sMoney.color = d.EndMoney >= d.StartMoney ? UiKit.Good : UiKit.Bad;
            UiKit.SetNumText(sExp, d.Exp.ToString(), true);
            UiKit.SetNumText(sTrucks, d.TrucksDeparted + "대", true);
            summary.gameObject.SetActive(true);
            summary.SetAsLastSibling(); // 운송장 카드 등 나중에 만든 모달보다 위에
            input.ClearPad();
            Sync();
        }

        /// <summary>"다음 날": 다음 날 09:00으로 넘기고(체력 회복) 요약을 닫는다.</summary>
        private void NextDay()
        {
            var s = GameBootstrap.Instance.Session;
            s.NextDay();
            if (s.Clock.IsOpen) summary.gameObject.SetActive(false);
            Sync();
        }

        /// <summary>해체 확인 창을 연다. InputController.Demolish가 직접 BeginDemolish 하지 않고 이쪽을 거친다.</summary>
        public void Ask()
        {
            var s = GameBootstrap.Instance.Session;
            var (fx, fy) = s.Worker.FacingTile();
            if (!s.Grid.InBounds(fx, fy)) { hud.Toast("해체할 수 있는 부품이 아닙니다"); return; }
            var c = s.Grid.Get(fx, fy);
            if (!c.IsBuilding) { hud.Toast("해체할 수 있는 부품이 아닙니다"); return; }
            if (c.Demolishing) { hud.Toast("이미 해체 중입니다"); return; }
            if (c.Type == CellType.Pallet && c.Pallet.Count > 0) { hud.Toast(HudController.DemolishMessage(BuildResult.Occupied)); return; }
            pendingDemolish = (fx, fy);
            dText.text = HudController.Name(c.Type) + " " + HudController.DirLabel(c) + " 를 해체합니다.";
            dRefund.text = "환불 ₩" + BuildService.RefundFor(c.Type) + " · 소요 " + BuildService.DemolishSeconds + "초";
            demolish.gameObject.SetActive(true);
            Sync();
        }
    }
}
