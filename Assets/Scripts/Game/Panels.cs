using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ParcelFactory.Game
{
    /// <summary>상점, 검사 카드, 해체 확인. 열려 있으면 월드 입력을 막는다.</summary>
    [DefaultExecutionOrder(10)]
    public sealed class Panels : MonoBehaviour
    {
        private RectTransform shop, inspect, demolish, shopList;
        private Text iDest, iWeight, iMethod, iScale, iId, dText, dRefund, shopMoney;
        private (int x, int y)? pendingDemolish;
        private InputController input;
        private HudController hud;

        public bool AnyOpen => (shop != null && shop.gameObject.activeSelf) || (inspect != null && inspect.gameObject.activeSelf) || (demolish != null && demolish.gameObject.activeSelf);
        /// <summary>상점만 열려 있는가. 이때는 B 키·상점 버튼이 상점을 닫는 토글로 동작한다.</summary>
        public bool ShopIsOnlyOpen => shop != null && shop.gameObject.activeSelf && !inspect.gameObject.activeSelf && !demolish.gameObject.activeSelf;
        private bool lastAnyOpen;
        private Economy watchedEconomy;

        private void Start()
        {
            var boot = GameBootstrap.Instance;
            input = boot.Input; hud = boot.Hud;
            BuildShop(); BuildInspect(); BuildDemolish();
            input.ToggleShop += () => { if (inspect.gameObject.activeSelf || demolish.gameObject.activeSelf) return; shop.gameObject.SetActive(!shop.gameObject.activeSelf); RefreshShop(); Sync(); };
            input.Escape += CloseAll;
            input.Interacted += r => { if (r == InteractResult.InspectionRequired) OpenInspect(); };
            WatchEconomy(boot.Session);
            boot.SessionReplaced += s => { WatchEconomy(s); CloseAll(); };
        }

        private void WatchEconomy(GameSession s)
        {
            if (watchedEconomy != null) watchedEconomy.MoneyChanged -= OnMoneyChanged;
            watchedEconomy = s.Economy;
            watchedEconomy.MoneyChanged += OnMoneyChanged;
        }

        private void OnMoneyChanged(int _) { if (shop != null && shop.gameObject.activeSelf) RefreshShop(); }

        private void OnDestroy() { if (watchedEconomy != null) watchedEconomy.MoneyChanged -= OnMoneyChanged; }

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
            if (inspect.gameObject.activeSelf && GameBootstrap.Instance.Session.Worker.PendingInspection == null) inspect.gameObject.SetActive(false);
            Sync();
        }

        public void CloseAll()
        {
            shop.gameObject.SetActive(false);
            if (inspect.gameObject.activeSelf) { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); }
            demolish.gameObject.SetActive(false); pendingDemolish = null;
            Sync();
        }

        private RectTransform Card(string name, Vector2 size, string title)
        {
            var dim = UiKit.PanelBox(name, hud.Root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.35f)).rectTransform;
            var card = UiKit.PanelBox("Card", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, UiKit.Panel2).rectTransform;
            var t = UiKit.Label("Title", card, title, 16, UiKit.Fg, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.offsetMin = new Vector2(18f, 0f); t.rectTransform.offsetMax = new Vector2(-18f, -14f);
            dim.gameObject.SetActive(false);
            return dim;
        }

        private void BuildShop()
        {
            shop = UiKit.PanelBox("Shop", hud.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -60f), new Vector2(340f, 470f), UiKit.Panel2).rectTransform;
            var title = UiKit.Label("Title", shop, "해금 상점", 16, UiKit.Fg, TextAnchor.UpperLeft, FontStyle.Bold);
            title.rectTransform.offsetMin = new Vector2(16f, 0f); title.rectTransform.offsetMax = new Vector2(-16f, -12f);
            shopMoney = UiKit.Label("Money", shop, "", 12, UiKit.Muted, TextAnchor.UpperRight);
            shopMoney.rectTransform.offsetMin = new Vector2(16f, 0f); shopMoney.rectTransform.offsetMax = new Vector2(-92f, -14f);
            var shopClose = UiKit.Button("Close", shop, "닫기", new Vector2(72f, 32f), CloseAll, null, null, 14, new Vector2(1f, 1f));
            shopClose.GetComponent<RectTransform>().anchoredPosition = new Vector2(-46f, -26f);
            shopList = UiKit.Rect("List", shop, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(-24f, -56f));
            UiKit.Column(shopList, 4f, new RectOffset(0, 0, 0, 0));
            foreach (var def in UnlockCatalog.All)
            {
                var row = UiKit.Rect("Row", shopList, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 34f));
                UiKit.Size(row, 300f, 34f);
                var name = UiKit.Label("Name", row, def.Name, 13, UiKit.Fg, TextAnchor.MiddleLeft);
                name.rectTransform.offsetMin = new Vector2(4f, 0f); name.rectTransform.offsetMax = new Vector2(-110f, 0f);
                var id = def.Id;
                var b = UiKit.Button("Buy", row, def.Price.ToString(), new Vector2(96f, 30f), () => { var r = GameBootstrap.Instance.Session.Unlocks.TryPurchase(id); hud.Toast(r == UnlockResult.Ok ? def.Name + " 해금" : r == UnlockResult.NotEnoughMoney ? "잔액이 부족합니다" : r == UnlockResult.RequirementMissing ? "먼저 해금해야 하는 항목이 있습니다" : "이미 보유 중입니다"); RefreshShop(); });
                var brt = b.GetComponent<RectTransform>(); brt.anchorMin = new Vector2(1f, 0.5f); brt.anchorMax = new Vector2(1f, 0.5f); brt.pivot = new Vector2(1f, 0.5f); brt.anchoredPosition = new Vector2(-4f, 0f);
            }
            shop.gameObject.SetActive(false);
        }

        private void RefreshShop()
        {
            var s = GameBootstrap.Instance.Session;
            shopMoney.text = "잔액 " + s.Economy.Money;
            int i = 0;
            foreach (var def in UnlockCatalog.All)
            {
                var row = shopList.GetChild(i++);
                var b = row.Find("Buy").GetComponent<Button>();
                bool owned = s.Unlocks.IsPurchased(def.Id), blocked = def.Requires.HasValue && !s.Unlocks.IsPurchased(def.Requires.Value), afford = s.Economy.CanAfford(def.Price);
                b.GetComponentInChildren<Text>().text = owned ? "보유" : blocked ? "잠김" : def.Price.ToString();
                b.interactable = !owned && !blocked && afford;
                UiKit.SetButtonLook(b, !owned && !blocked && afford);
            }
        }

        private void BuildInspect()
        {
            inspect = Card("Inspect", new Vector2(380f, 300f), "택배 검사");
            var card = inspect.Find("Card");
            iId = UiKit.Label("Id", card, "", 12, UiKit.Muted, TextAnchor.UpperRight); iId.rectTransform.offsetMin = new Vector2(18f, 0f); iId.rectTransform.offsetMax = new Vector2(-18f, -16f);
            var paper = UiKit.PanelBox("Paper", card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(-36f, 140f), new Color(0.97f, 0.95f, 0.9f)).rectTransform;
            Text Row(string n, string lbl, float y, Color c, bool bold = false)
            {
                var l = UiKit.Label(n + "L", paper, lbl, 13, new Color(0.48f, 0.43f, 0.36f), TextAnchor.MiddleLeft); l.rectTransform.anchorMin = new Vector2(0f, 1f); l.rectTransform.anchorMax = new Vector2(1f, 1f); l.rectTransform.pivot = new Vector2(0.5f, 1f); l.rectTransform.anchoredPosition = new Vector2(0f, y); l.rectTransform.sizeDelta = new Vector2(-28f, 26f);
                var v = UiKit.Label(n, paper, "", 13, c, TextAnchor.MiddleRight, bold ? FontStyle.Bold : FontStyle.Normal); v.rectTransform.anchorMin = new Vector2(0f, 1f); v.rectTransform.anchorMax = new Vector2(1f, 1f); v.rectTransform.pivot = new Vector2(0.5f, 1f); v.rectTransform.anchoredPosition = new Vector2(0f, y); v.rectTransform.sizeDelta = new Vector2(-28f, 26f);
                return v;
            }
            var dark = new Color(0.16f, 0.15f, 0.12f);
            iDest = Row("Dest", "목적지", -8f, dark, true); iWeight = Row("Weight", "표기 무게", -38f, dark, true); iMethod = Row("Method", "배송 방법", -68f, dark, true); iScale = Row("Scale", "저울 실측", -104f, new Color(0.79f, 0.28f, 0.18f), true);
            var hint = UiKit.Label("Hint", card, "표기와 실측이 다르면 거부하세요. 거부한 택배는 반품구로 보내야 합니다.", 12, UiKit.Muted, TextAnchor.UpperLeft);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f); hint.rectTransform.pivot = new Vector2(0.5f, 0f); hint.rectTransform.anchoredPosition = new Vector2(0f, 58f); hint.rectTransform.sizeDelta = new Vector2(-36f, 40f);
            var reject = UiKit.Button("Reject", card, "거부", new Vector2(110f, 38f), () => { GameBootstrap.Instance.Session.Worker.Inspect(true); inspect.gameObject.SetActive(false); Sync(); }, new Color(0.79f, 0.28f, 0.18f, 1f));
            reject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -115f);
            var approve = UiKit.Button("Approve", card, "승인", new Vector2(110f, 38f), () => { GameBootstrap.Instance.Session.Worker.Inspect(false); inspect.gameObject.SetActive(false); Sync(); }, UiKit.Good, new Color(0.06f, 0.16f, 0.09f));
            approve.GetComponent<RectTransform>().anchoredPosition = new Vector2(125f, -115f);
            var cancelInspect = UiKit.Button("CancelInspect", card, "취소", new Vector2(110f, 38f), () => { GameBootstrap.Instance.Session.Worker.CancelInspection(); inspect.gameObject.SetActive(false); Sync(); });
            cancelInspect.GetComponent<RectTransform>().anchoredPosition = new Vector2(-125f, -115f);
        }

        private void OpenInspect()
        {
            var p = GameBootstrap.Instance.Session.Worker.PendingInspection;
            if (p == null) return;
            iId.text = "#" + p.Id; iDest.text = p.Label.Destination; iWeight.text = p.Label.Weight + " kg"; iMethod.text = p.Label.Method; iScale.text = p.Label.Scale + " kg";
            inspect.gameObject.SetActive(true);
            Sync();
        }

        private void BuildDemolish()
        {
            demolish = Card("Demolish", new Vector2(360f, 190f), "해체");
            var card = demolish.Find("Card");
            dText = UiKit.Label("Text", card, "", 13, UiKit.Fg, TextAnchor.UpperLeft); dText.rectTransform.offsetMin = new Vector2(18f, 0f); dText.rectTransform.offsetMax = new Vector2(-18f, -46f);
            dRefund = UiKit.Label("Refund", card, "", 13, UiKit.Muted, TextAnchor.UpperLeft); dRefund.rectTransform.offsetMin = new Vector2(18f, 0f); dRefund.rectTransform.offsetMax = new Vector2(-18f, -74f);
            var cancel = UiKit.Button("Cancel", card, "취소", new Vector2(100f, 36f), () => { pendingDemolish = null; demolish.gameObject.SetActive(false); Sync(); });
            cancel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-60f, -62f);
            var ok = UiKit.Button("Confirm", card, "해체", new Vector2(100f, 36f), () => { if (pendingDemolish.HasValue) { var r = GameBootstrap.Instance.Session.Build.BeginDemolish(pendingDemolish.Value.x, pendingDemolish.Value.y); if (r != BuildResult.Ok) hud.Toast(HudController.DemolishMessage(r)); } pendingDemolish = null; demolish.gameObject.SetActive(false); Sync(); }, new Color(0.79f, 0.28f, 0.18f, 1f));
            ok.GetComponent<RectTransform>().anchoredPosition = new Vector2(60f, -62f);
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
