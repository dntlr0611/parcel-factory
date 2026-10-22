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
        private RectTransform shop, status, inspect, demolish, summary, shopList, statusList, shopTrucks, shopFactories, summaryCard, summaryFactories, shopViewport, shopContent;
        private ScrollRect shopScroll;
        private Scrollbar shopScrollbar;
        /// <summary>상점 내용 전체 높이(머리 제외)와 서랍 전체 높이(제한 전).</summary>
        private float shopContentH, shopFullH;
        /// <summary>키보드·게임패드로 고른 상점 행(보이는 행 중 번호). 없으면 −1.</summary>
        public int ShopSelectedRow { get; private set; } = -1;
        private Text shopTitle, trucksTitle;
        private float summaryBaseH;
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
            boot.FactorySwitched += _ => { if (shop.gameObject.activeSelf) RefreshShop(); }; // 상점은 지금 공장의 해금·택배차를 보인다(스펙 19.4)
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
            if (WaybillOpen && session.Worker.PendingLabel == null) Waybill.Close(); // 다른 경로로 대기가 풀렸다(완료는 스스로 닫는다)
            if (SummaryOpen && session.Clock.IsOpen) summary.gameObject.SetActive(false); // 다른 경로로 다음 날이 시작됐다
            if (shop.gameObject.activeSelf) UpdateShopInput();
            Sync();
        }

        /// <summary>닫을 수 있는 패널을 모두 닫는다. 하루 요약은 모달이라 그대로 둔다.</summary>
        public void CloseAll()
        {
            shop.gameObject.SetActive(false);
            status.gameObject.SetActive(false);
            if (inspect.gameObject.activeSelf) { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); }
            demolish.gameObject.SetActive(false); pendingDemolish = null;
            if (WaybillOpen) Waybill.Close(); // 카드에서 붙인 것은 되돌린다(스펙 15.5)
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

        /// <summary>상점 행 높이(구매 버튼 28 + 위아래 2), 묶음 제목 줄, 새 공장 행(이름 + 조건/특징 두 줄). 서랍 위치 top 62.
        /// 해금 12행 + 택배차(도크마다 1행: 출고·반품, 항공 도크가 있으면 4행) + 새 공장 2행이 화면 높이(1280×800 기준) 안에 들어가게 촘촘하게 둔다.</summary>
        public const float ShopRowH = 32f, ShopBtnH = 28f, ShopGroupH = 24f, ShopFactoryRowH = 40f, ShopHeaderH = 46f;
        private const float DrawerTop = 62f;
        /// <summary>상점 서랍의 제한 전 높이(택배차 행 truckRows개). 실제 높이는 화면 안으로 제한된다(ApplyShopHeight).</summary>
        public static float ShopHeight(int truckRows) =>
            ShopHeaderH + UnlockCatalog.All.Count * ShopRowH + ShopGroupH + truckRows * ShopRowH + ShopGroupH + (Company.MaxFactories - 1) * ShopFactoryRowH + 8f;

        /// <summary>새 공장 행에 보일 조건 문구(스펙 19.2): "1공장 부지 확장 2 · 자동 검사기 필요".</summary>
        public static string FactoryRequirementText(FactoryKind kind) => FactoryProfile.Of(FactoryProfile.Previous(kind)).ShortName + " 부지 확장 2 · 자동 검사기 필요";

        /// <summary>새 공장 행의 특징 문구.</summary>
        public static string FactoryFeatureText(FactoryKind kind) =>
            kind == FactoryKind.Airport ? "항공 도크 처음부터 · 항공 60% · 항공 출고 +" + FactoryProfile.AirportAirDelivered
            : "취급주의 50% 처음부터 · 취급주의 출고 +" + FactoryProfile.PrecisionFragileDelivered;

        /// <summary>택배차 행 이름(스펙 20): "출고 트럭 2대째 · 지금 1대", 최대면 "출고 트럭 3대 · 최대".</summary>
        public static string TruckRowText(TruckFleet t, CellType dock)
        {
            int n = t.Count(dock);
            var name = HudController.TruckName(dock) + " 트럭 ";
            return n >= TruckFleet.MaxTrucksPerDock ? name + n + "대 · 최대" : name + (n + 1) + "대째 · 지금 " + n + "대";
        }

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

        /// <summary>상점 행 하나: 이름(왼쪽) + 구매 버튼(오른쪽, Sora 12). 첫 행이 아니면 위에 1px 선.</summary>
        private Button ShopRow(RectTransform list, string label, float height, bool first, System.Action onBuy, out Text name)
        {
            var row = UiKit.Rect("Row", list, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, height));
            UiKit.Size(row, -1f, height);
            if (!first) UiKit.Divider("Line", row);
            name = UiKit.Label("Name", row, label, 13, UiKit.Fg, TextAnchor.MiddleLeft);
            name.supportRichText = true;
            name.rectTransform.offsetMin = new Vector2(0f, 0f); name.rectTransform.offsetMax = new Vector2(-84f, 0f);
            var b = UiKit.Btn("Buy", row, "", onBuy, font: Fonts.Num, fontSize: 12, height: ShopBtnH, fixedSize: new Vector2(76f, ShopBtnH));
            UiKit.Place((RectTransform)b.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(76f, ShopBtnH));
            // 키보드·게임패드 선택 표시(설정 카드 행과 같은 앰버 테두리). 구매 버튼만 둘러싼다.
            var ring = UiKit.AddBorder((RectTransform)b.transform, UiKit.RButton, UiKit.Accent, 1.5f, 2f, "Selected");
            ring.gameObject.SetActive(false);
            return b;
        }

        /// <summary>상점 묶음: 제목 줄(Noto Bold 11, muted) + 행 목록. name은 묶음 오브젝트 이름.</summary>
        private RectTransform ShopGroup(string name, string title, out Text titleText)
        {
            var group = UiKit.Rect(name, shopContent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
            titleText = UiKit.Label("Title", group, title, 11, UiKit.Muted, TextAnchor.LowerLeft, Fonts.Bold);
            UiKit.Top(titleText.rectTransform, 0f, ShopGroupH - 4f, 0f, 0f);
            var list = UiKit.Rect("List", group, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -ShopGroupH), new Vector2(0f, 100f));
            var col = UiKit.Column(list, 0f, new RectOffset(0, 0, 0, 0));
            col.childControlHeight = true;
            return list;
        }

        private void BuildShop()
        {
            // 시안 .drawer: 오른쪽 16, top 62, 폭 330, 패딩 14, 행 사이 1px 선, 구매 버튼(.buy) 최소 76 · Sora 12.
            // 묶음: 지금 공장의 해금(List) → 지금 공장의 택배차(Trucks, 스펙 20) → 회사 단위 새 공장(Factories, 스펙 19.4, 맨 아래).
            shop = UiKit.PanelBox("Shop", hud.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -DrawerTop), new Vector2(330f, ShopHeight(2)), UiKit.Panel2).rectTransform;
            shopMoney = DrawerHeader(shop, "해금 상점", "Money", UiKit.Accent, Fonts.Bold, 12);
            shopTitle = shop.Find("Title").GetComponent<Text>();
            // 머리 아래는 스크롤 영역(스펙 12 서랍 안, 패딩 14): Viewport(RectMask2D + ScrollRect) → Content(해금 목록 · 택배차 · 새 공장).
            // 서랍 높이는 화면(캔버스) 높이 − top 62 − 아래 16으로 제한하고(ApplyShopHeight), 넘치면 휠·끌기·키보드 선택으로 스크롤한다.
            shopViewport = UiKit.Rect("Viewport", shop, Vector2.zero, Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            shopViewport.offsetMin = new Vector2(14f, 8f); shopViewport.offsetMax = new Vector2(-14f, -ShopHeaderH);
            var hit = shopViewport.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f); // 행 사이 빈 곳에서도 휠을 받는다
            shopViewport.gameObject.AddComponent<RectMask2D>();
            shopContent = UiKit.Rect("Content", shopViewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 100f));
            shopScroll = shopViewport.gameObject.AddComponent<ScrollRect>();
            shopScroll.viewport = shopViewport; shopScroll.content = shopContent;
            shopScroll.horizontal = false; shopScroll.vertical = true;
            shopScroll.movementType = ScrollRect.MovementType.Clamped; shopScroll.inertia = false; shopScroll.scrollSensitivity = ShopRowH;
            // 얇은 둥근 스크롤바(§12: 반경 = 폭의 절반, 흰색 .06 트랙 + 흰색 .35 손잡이). 서랍 오른쪽 패딩 안.
            var bar = UiKit.Box("Scrollbar", shop, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero, UiKit.White(0.06f), 2);
            var brt = bar.rectTransform;
            brt.offsetMin = new Vector2(-9f, 10f); brt.offsetMax = new Vector2(-5f, -(ShopHeaderH + 2f));
            var handle = UiKit.Box("Handle", brt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, UiKit.White(0.35f), 2);
            handle.rectTransform.offsetMin = Vector2.zero; handle.rectTransform.offsetMax = Vector2.zero;
            shopScrollbar = bar.gameObject.AddComponent<Scrollbar>();
            shopScrollbar.handleRect = handle.rectTransform; shopScrollbar.targetGraphic = handle;
            shopScrollbar.direction = Scrollbar.Direction.BottomToTop;
            shopScrollbar.transition = Selectable.Transition.None; shopScrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            shopScroll.verticalScrollbar = shopScrollbar;
            shopScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            shopList = UiKit.Rect("List", shopContent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, UnlockCatalog.All.Count * ShopRowH));
            var col = UiKit.Column(shopList, 0f, new RectOffset(0, 0, 0, 0));
            col.childControlHeight = true;
            bool first = true;
            foreach (var def in UnlockCatalog.All)
            {
                var id = def.Id;
                ShopRow(shopList, def.Name, ShopRowH, first, () => { var r = GameBootstrap.Instance.Session.Unlocks.TryPurchase(id); hud.Toast(r == UnlockResult.Ok ? def.Name + " 해금" : r == UnlockResult.NotEnoughMoney ? "잔액이 부족합니다" : r == UnlockResult.RequirementMissing ? "먼저 해금해야 하는 항목이 있습니다" : "이미 보유 중입니다", r == UnlockResult.Ok ? HudController.ToastKind.Good : HudController.ToastKind.Info); RefreshShop(); }, out _);
                first = false;
            }

            shopTrucks = ShopGroup("Trucks", "택배차", out trucksTitle);
            first = true;
            foreach (var dock in TruckFleet.Docks)
            {
                var d = dock;
                ShopRow(shopTrucks, "", ShopRowH, first, () => BuyTruck(d), out _);
                shopTrucks.GetChild(shopTrucks.childCount - 1).name = "Row_" + dock;
                first = false;
            }

            shopFactories = ShopGroup("Factories", "새 공장 · 회사", out _);
            first = true;
            foreach (var p in FactoryProfile.All)
            {
                if (p.Kind == FactoryKind.Headquarters) continue;
                var kind = p.Kind;
                ShopRow(shopFactories, p.FullName, ShopFactoryRowH, first, () => BuyFactory(kind), out var name);
                var row = (RectTransform)shopFactories.GetChild(shopFactories.childCount - 1);
                row.name = "Row_" + kind;
                name.alignment = TextAnchor.UpperLeft;
                name.rectTransform.offsetMax = new Vector2(-84f, -4f);
                var sub = UiKit.Label("Sub", row, "", 11, UiKit.Muted, TextAnchor.LowerLeft);
                sub.horizontalOverflow = HorizontalWrapMode.Overflow;
                sub.rectTransform.offsetMin = new Vector2(0f, 4f); sub.rectTransform.offsetMax = new Vector2(-84f, 0f);
                first = false;
            }
            shop.gameObject.SetActive(false);
        }

        private void BuyTruck(CellType dock)
        {
            var f = GameBootstrap.Instance.Session.Factory;
            int? price = f.Trucks.NextTruckPrice(dock);
            var r = f.TryBuyTruck(dock);
            hud.Toast(r == TruckPurchaseResult.Ok ? HudController.TruckName(dock) + " 트럭 " + f.Trucks.Count(dock) + "대 · ₩" + price
                : r == TruckPurchaseResult.NotEnoughMoney ? "잔액이 부족합니다" : r == TruckPurchaseResult.MaxTrucks ? "도크마다 최대 3대입니다" : "이 공장에는 그 도크가 없습니다",
                r == TruckPurchaseResult.Ok ? HudController.ToastKind.Good : HudController.ToastKind.Info);
            RefreshShop();
        }

        private void BuyFactory(FactoryKind kind)
        {
            var r = GameBootstrap.Instance.Session.Company.TryBuyFactory(kind);
            var p = FactoryProfile.Of(kind);
            hud.Toast(r == FactoryPurchaseResult.Ok ? p.FullName + " 구입 · Tab으로 이동" : r == FactoryPurchaseResult.NotEnoughMoney ? "잔액이 부족합니다"
                : r == FactoryPurchaseResult.RequirementMissing ? FactoryRequirementText(kind) : "이미 보유 중입니다",
                r == FactoryPurchaseResult.Ok ? HudController.ToastKind.Good : HudController.ToastKind.Info);
            RefreshShop();
        }

        private void RefreshShop()
        {
            var s = GameBootstrap.Instance.Session;
            var f = s.Factory;
            shopMoney.text = "₩ " + s.Economy.Money;
            shopTitle.text = "해금 상점 · " + f.Profile.ShortName;
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

            // 택배차(스펙 20): 지금 공장의 도크마다. 항공 도크 항목은 항공 도크가 있을 때만.
            trucksTitle.text = "택배차 · " + f.Profile.ShortName;
            int truckRows = 0;
            bool firstShown = true;
            foreach (var dock in TruckFleet.Docks)
            {
                var row = shopTrucks.Find("Row_" + dock);
                bool has = f.HasTruckDock(dock);
                row.gameObject.SetActive(has);
                if (!has) continue;
                truckRows++;
                var line = row.Find("Line");
                if (line != null) line.gameObject.SetActive(!firstShown);
                firstShown = false;
                row.Find("Name").GetComponent<Text>().text = TruckRowText(f.Trucks, dock);
                var b = row.Find("Buy").GetComponent<Button>();
                var price = f.Trucks.NextTruckPrice(dock);
                bool can = f.CanBuyTruck(dock) == TruckPurchaseResult.Ok;
                UiKit.SetButtonText(b, price.HasValue ? price.Value.ToString() : "최대", num: true);
                b.interactable = can;
                UiKit.SetButtonStyle(b, can ? BtnStyle.Primary : BtnStyle.Normal);
            }

            // 새 공장(스펙 19.2·19.4): 회사 단위. 조건이 안 되면 흐리게 + 조건 문구.
            foreach (var p in FactoryProfile.All)
            {
                if (p.Kind == FactoryKind.Headquarters) continue;
                var row = shopFactories.Find("Row_" + p.Kind);
                var r = s.Company.CanBuyFactory(p.Kind);
                bool owned = r == FactoryPurchaseResult.AlreadyOwned, locked = r == FactoryPurchaseResult.RequirementMissing;
                var name = row.Find("Name").GetComponent<Text>();
                name.color = locked ? UiKit.Muted : UiKit.Fg;
                var sub = row.Find("Sub").GetComponent<Text>();
                sub.text = owned ? "보유 · " + FactoryFeatureText(p.Kind) : locked ? FactoryRequirementText(p.Kind) : FactoryFeatureText(p.Kind);
                sub.color = locked ? UiKit.Bad : UiKit.Muted;
                var b = row.Find("Buy").GetComponent<Button>();
                UiKit.SetButtonText(b, owned ? "보유" : locked ? "잠김" : p.Price.ToString(), num: true);
                b.interactable = r == FactoryPurchaseResult.Ok;
                UiKit.SetButtonStyle(b, r == FactoryPurchaseResult.Ok ? BtnStyle.Primary : BtnStyle.Normal);
            }

            // 세로 배치(Content 안): 해금 목록 → 택배차 → 새 공장. 서랍 높이는 보이는 행 수에 맞추되 화면 안으로 제한한다.
            float y = UnlockCatalog.All.Count * ShopRowH;
            float trucksH = ShopGroupH + truckRows * ShopRowH;
            ((RectTransform)shopTrucks.parent).anchoredPosition = new Vector2(0f, -y);
            ((RectTransform)shopTrucks.parent).sizeDelta = new Vector2(0f, trucksH);
            shopTrucks.sizeDelta = new Vector2(0f, truckRows * ShopRowH);
            y += trucksH;
            float factoriesH = ShopGroupH + (Company.MaxFactories - 1) * ShopFactoryRowH;
            ((RectTransform)shopFactories.parent).anchoredPosition = new Vector2(0f, -y);
            ((RectTransform)shopFactories.parent).sizeDelta = new Vector2(0f, factoriesH);
            shopFactories.sizeDelta = new Vector2(0f, (Company.MaxFactories - 1) * ShopFactoryRowH);
            shopContentH = y + factoriesH;
            shopContent.sizeDelta = new Vector2(0f, shopContentH);
            shopFullH = ShopHeight(truckRows);
            ApplyShopHeight();
            ShowShopSelection();
        }

        /// <summary>서랍 높이 = min(내용 전체, 캔버스 높이 − top 62 − 아래 16). 캔버스 크기가 바뀌면(창 크기·스크린샷 배율) 다시 맞춘다.</summary>
        private void ApplyShopHeight()
        {
            float max = Mathf.Max(ShopHeaderH + ShopRowH * 3f, hud.Root.rect.height - DrawerTop - ShopBottomMargin);
            float h = Mathf.Min(shopFullH, max);
            if (Mathf.Abs(shop.sizeDelta.y - h) > 0.25f) shop.sizeDelta = new Vector2(330f, h);
        }

        /// <summary>서랍 아래 여백.</summary>
        public const float ShopBottomMargin = 16f;

        /// <summary>상점 서랍이 내용보다 짧아 스크롤되는가(테스트용).</summary>
        public bool ShopScrolls => shopContentH > shopViewport.rect.height + 0.5f;

        /// <summary>상점 스크롤 위치(1 = 맨 위, 0 = 맨 아래).</summary>
        public float ShopScrollPosition
        {
            get => shopScroll.verticalNormalizedPosition;
            set { Canvas.ForceUpdateCanvases(); shopScroll.verticalNormalizedPosition = Mathf.Clamp01(value); }
        }

        /// <summary>지금 보이는 상점 행(해금 → 택배차 → 새 공장 순서).</summary>
        private System.Collections.Generic.List<RectTransform> VisibleShopRows()
        {
            var rows = new System.Collections.Generic.List<RectTransform>();
            foreach (var list in new[] { shopList, shopTrucks, shopFactories })
                for (int i = 0; i < list.childCount; i++)
                    if (list.GetChild(i).gameObject.activeSelf) rows.Add((RectTransform)list.GetChild(i));
            return rows;
        }

        /// <summary>키보드 ↑/↓(W/S)·게임패드 십자키: 고른 행을 옮기고 그 행이 보이게 스크롤한다. 처음 누르면 맨 위(↓) 또는 맨 아래(↑) 행.</summary>
        public void MoveShopSelection(int delta)
        {
            var rows = VisibleShopRows();
            if (rows.Count == 0) return;
            ShopSelectedRow = ShopSelectedRow < 0 ? (delta > 0 ? 0 : rows.Count - 1) : Mathf.Clamp(ShopSelectedRow + delta, 0, rows.Count - 1);
            ShowShopSelection();
            ScrollShopRowIntoView(rows[ShopSelectedRow]);
        }

        /// <summary>Enter·Space·게임패드 A: 고른 행을 산다(살 수 있을 때만).</summary>
        public void BuySelectedShopRow()
        {
            var rows = VisibleShopRows();
            if (ShopSelectedRow < 0 || ShopSelectedRow >= rows.Count) return;
            var b = rows[ShopSelectedRow].Find("Buy").GetComponent<Button>();
            if (b.interactable) b.onClick.Invoke();
        }

        /// <summary>지금 고른 행(없으면 null). 테스트용.</summary>
        public RectTransform ShopSelectedRowTransform
        {
            get { var rows = VisibleShopRows(); return ShopSelectedRow >= 0 && ShopSelectedRow < rows.Count ? rows[ShopSelectedRow] : null; }
        }

        private void ShowShopSelection()
        {
            var rows = VisibleShopRows();
            if (ShopSelectedRow >= rows.Count) ShopSelectedRow = rows.Count - 1;
            foreach (var list in new[] { shopList, shopTrucks, shopFactories })
                for (int i = 0; i < list.childCount; i++)
                {
                    var ring = list.GetChild(i).Find("Buy/Selected");
                    if (ring != null) ring.gameObject.SetActive(ShopSelectedRow >= 0 && ShopSelectedRow < rows.Count && list.GetChild(i) == rows[ShopSelectedRow]);
                }
        }

        /// <summary>행이 Viewport 안에 다 보이도록 Content를 옮긴다(이미 보이면 그대로).</summary>
        private void ScrollShopRowIntoView(RectTransform row)
        {
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            row.GetWorldCorners(corners);
            float top = shopContent.InverseTransformPoint(corners[1]).y, bottom = shopContent.InverseTransformPoint(corners[0]).y; // Content 위쪽 기준(음수)
            float view = shopViewport.rect.height, offset = shopContent.anchoredPosition.y;
            if (top > -offset) offset = -top;
            else if (bottom < -offset - view) offset = -bottom - view;
            offset = Mathf.Clamp(offset, 0f, Mathf.Max(0f, shopContentH - view));
            shopContent.anchoredPosition = new Vector2(0f, offset);
        }

        /// <summary>상점이 열려 있는 동안: 서랍 높이를 화면에 맞추고 키보드·게임패드 선택을 받는다.</summary>
        private void UpdateShopInput()
        {
            ApplyShopHeight();
            if (!ShopIsOnlyOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            bool buy = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (up) MoveShopSelection(-1);
            else if (down) MoveShopSelection(1);
            else if (buy) BuySelectedShopRow();
        }

        /// <summary>B 키·상점 버튼. 상태창이 열려 있으면 닫고 상점을 연다.</summary>
        private void ToggleShop()
        {
            if (inspect.gameObject.activeSelf || demolish.gameObject.activeSelf || SummaryOpen) return;
            bool open = !shop.gameObject.activeSelf;
            status.gameObject.SetActive(false);
            shop.gameObject.SetActive(open);
            if (open) { ShopSelectedRow = -1; RefreshShop(); shopContent.anchoredPosition = Vector2.zero; }
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
            // 공장별 한 줄(스펙 19.1): 공장이 둘 이상일 때만 보인다. 줄은 ShowSummary가 채운다.
            summaryCard = (RectTransform)card;
            summaryBaseH = summaryCard.sizeDelta.y;
            summaryFactories = UiKit.Rect("Factories", card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(top + 6f * rowH)), new Vector2(-2f * CardPad, 0f));
            for (int i = 0; i < Company.MaxFactories; i++)
            {
                float y = i * SummaryFactoryRowH;
                var line = UiKit.Divider("Line" + i, summaryFactories); UiKit.Top(line.rectTransform, y, 1f, 0f, 0f);
                var l = UiKit.Label("Factory" + (i + 1) + "L", summaryFactories, "", 12, UiKit.Muted, TextAnchor.MiddleLeft); UiKit.Top(l.rectTransform, y, SummaryFactoryRowH, 0f, 0f);
                var v = UiKit.Label("Factory" + (i + 1), summaryFactories, "", 13, UiKit.Fg, TextAnchor.MiddleRight, Fonts.Num); UiKit.Top(v.rectTransform, y, SummaryFactoryRowH, 0f, 0f);
                v.supportRichText = true;
            }
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
            // 공장별 줄(스펙 19.1): "2공장 공항 물류센터 · 배송 12 · 소실 1 · +340". 공장이 하나면 숨긴다.
            int lines = d.Factories.Count > 1 ? d.Factories.Count : 0;
            for (int i = 0; i < Company.MaxFactories; i++)
            {
                bool on = i < lines;
                summaryFactories.Find("Line" + i).gameObject.SetActive(on);
                var l = summaryFactories.Find("Factory" + (i + 1) + "L").GetComponent<Text>();
                var v = summaryFactories.Find("Factory" + (i + 1)).GetComponent<Text>();
                l.gameObject.SetActive(on); v.gameObject.SetActive(on);
                if (!on) continue;
                var f = d.Factories[i];
                l.text = FactorySummaryLabel(f);
                v.text = FactorySummaryValue(f);
                v.font = Fonts.Body; // 한글이 섞여 Noto
            }
            summaryCard.sizeDelta = new Vector2(summaryCard.sizeDelta.x, summaryBaseH + lines * SummaryFactoryRowH);
            summary.gameObject.SetActive(true);
            summary.SetAsLastSibling(); // 운송장 카드 등 나중에 만든 모달보다 위에
            input.ClearPad();
            Sync();
        }

        /// <summary>하루 요약 공장별 줄 높이.</summary>
        // 공장별 줄의 금액은 "배송 수입"이다(카드 위쪽 "잔액 변화"와 헷갈리지 않게).
        public const float SummaryFactoryRowH = 30f;

        /// <summary>공장별 줄 왼쪽: "2공장 공항 물류센터".</summary>
        public static string FactorySummaryLabel(FactoryDaySummary f) => f.Number + "공장 " + f.Name;

        /// <summary>공장별 줄 오른쪽: "배송 12 · 소실 1 · 배송 수입 +340"(그날 배송 판정 금액 합. 잔액 변화와 다르다: 설치·해금 지출은 빠져 있다).</summary>
        public static string FactorySummaryValue(FactoryDaySummary f) =>
            "배송 " + f.Delivered + " · 소실 " + f.Lost + " · 배송 수입 " + HudController.Signed(f.Earned);

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
