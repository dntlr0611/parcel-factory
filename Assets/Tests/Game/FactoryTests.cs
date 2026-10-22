using System.Collections;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>스펙 19(O5): 새 공장 구매 → 탭, 공장 옮기기(월드 교체·작업자 위치·페이드), 손에 든 택배 거부, 배경 공장이 계속 틱, 경보 칩·토스트,
    /// 하루 요약 공장별 줄. 스펙 20: 상점 택배차 구매와 교대 알약.</summary>
    public class FactoryTests
    {
        private static Package Good(FactorySite f, bool air = false)
        {
            var p = new PackageFactory(new System.Random(9), 0.0).Create(f.Sim.NextId++, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            return p;
        }

        private static Button ShopBuy(GameBootstrap boot, string path) =>
            boot.Hud.Root.Find("Shop/Viewport/Content/" + path + "/Buy").GetComponent<Button>();

        private static string ButtonText(Button b) => b.GetComponent<UiButton>().Label.text;

        [UnityTest]
        public IEnumerator BuyFactory_TabAppears_SwitchRebindsTheWorld_WorkerAtEntrance_RefusedWhileCarrying()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(10000);
                Assert.AreEqual(0, boot.Factories.TabCount, "no tabs with one factory");

                boot.Input.RequestShop();
                yield return null;
                var buyAirport = ShopBuy(boot, "Factories/List/Row_Airport");
                Assert.AreEqual("잠김", ButtonText(buyAirport));
                Assert.IsFalse(buyAirport.interactable);
                Assert.AreEqual("1공장 부지 확장 2 · 자동 검사기 필요", boot.Hud.Root.Find("Shop/Viewport/Content/Factories/List/Row_Airport/Sub").GetComponent<Text>().text);
                boot.Panels.CloseAll();
                s.Unlocks.Grant(UnlockId.Plot2);
                s.Unlocks.Grant(UnlockId.Scanner);
                boot.Input.RequestShop();
                yield return null;
                Assert.AreEqual("3000", ButtonText(buyAirport));
                Assert.IsTrue(buyAirport.interactable);
                int money = s.Economy.Money;
                buyAirport.onClick.Invoke();
                Assert.AreEqual(money - 3000, s.Economy.Money);
                Assert.AreEqual(2, s.Company.Factories.Count);
                StringAssert.StartsWith("2공장 · 공항 물류센터 구입", boot.Hud.LastToast);
                Assert.AreEqual("보유", ButtonText(buyAirport));
                boot.Panels.CloseAll();
                yield return null;
                Assert.AreEqual(2, boot.Factories.TabCount, "a tab per factory");
                Assert.AreEqual("2공장 · 대기 0 · 소실 0", boot.Factories.ChipTextOf(1), "summary chip of the other factory");
                Assert.IsNull(boot.Factories.ChipTextOf(0), "no chip for the factory we are in");

                // 1공장에 레일 하나 → 미니 상태바 3개(팔레트 2 + 레일).
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
                yield return null;
                Assert.AreEqual(3, boot.StatusBars.Count);

                // 손에 택배를 들면 옮길 수 없다.
                var held = Good(s.Factory);
                s.Worker.Worker.Carrying = held;
                Assert.AreEqual(FactorySwitchResult.Carrying, boot.Input.CycleFactory(1));
                Assert.AreEqual(FactoryBar.CarryingToast, boot.Hud.LastToast);
                Assert.AreEqual(0, s.Company.CurrentIndex);
                s.Worker.Worker.Carrying = null;

                boot.Input.SelectSlot(0);
                var tool = boot.Input.SelectedTool;
                Assert.AreEqual(FactorySwitchResult.Ok, boot.Input.CycleFactory(1));
                Assert.AreEqual(1, s.Company.CurrentIndex);
                Assert.AreEqual(tool, boot.Input.SelectedTool, "the tool in hand stays");
                yield return null;
                Assert.Greater(boot.Factories.FadeAlpha, 0f, "a short fade");
                Assert.AreEqual(FactoryKind.Airport, boot.World.Kind);
                Assert.AreEqual((1, 1), s.Worker.Tile(), "the worker appears at the entrance");
                Assert.AreEqual(2, boot.StatusBars.Count, "status bars of the airport (two start pallets)");
                Assert.IsNotNull(boot.World.TruckOf(CellType.AirOutlet), "the airport has air trucks from the start");
                Assert.AreEqual("0/30", boot.Trucks.TextOf(CellType.AirOutlet));
                Assert.AreEqual("1공장 · 대기 0 · 소실 0", boot.Factories.ChipTextOf(0));
                Assert.AreEqual(CellType.Empty, s.Grid.Get(1, 2).Type, "the facade now shows the airport grid");
                yield return new WaitForSeconds(FactoryBar.FadeSeconds + 0.15f);
                Assert.AreEqual(0f, boot.Factories.FadeAlpha);

                // 카드가 열려 있으면 옮기지 않는다.
                boot.Input.RequestShop();
                Assert.IsNull(boot.Input.SwitchFactory(0));
                Assert.AreEqual(1, s.Company.CurrentIndex);
                Assert.AreEqual("해금 상점 · 2공장", boot.Hud.Root.Find("Shop/Title").GetComponent<Text>().text);
                Assert.IsTrue(boot.Hud.Root.Find("Shop/Viewport/Content/Trucks/List/Row_AirOutlet").gameObject.activeSelf, "air truck rows in the airport");
                boot.Panels.CloseAll();

                // 탭 클릭으로 돌아간다.
                boot.Hud.Root.Find("FactoryTabs/Row/Tab1").GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(0, s.Company.CurrentIndex);
                yield return null;
                Assert.AreEqual(FactoryKind.Headquarters, boot.World.Kind);
                Assert.AreEqual(3, boot.StatusBars.Count);

                // 불러오기·새 게임(공장 하나)이면 탭이 사라진다.
                boot.ReplaceSession(new GameSession(new Economy(100)) { Paused = true });
                yield return null;
                Assert.AreEqual(0, boot.Factories.TabCount);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BackgroundFactory_KeepsTicking_ParcelsAdvanceWhileAway()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                var hq = s.Factory;
                var airport = s.Company.AddFactory(FactoryKind.Airport);
                Assert.AreEqual(BuildResult.Ok, hq.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
                Assert.AreEqual(BuildResult.Ok, hq.Build.TryPlace(2, 3, CellType.Conveyor, Direction.East));
                Assert.AreEqual(BuildResult.Ok, hq.Build.TryPlace(3, 3, CellType.Table, Direction.East));
                var p = Good(hq);
                p.Inspected = false; // 검사대 (3,3)에서 멈춘다
                hq.Sim.PlaceCarried(p, 1, 3);
                Assert.AreEqual(FactorySwitchResult.Ok, boot.Input.SwitchFactory(1));
                yield return null;
                Assert.IsNull(boot.GetComponent<PackageViews>().ViewOf(p.Id), "the first factory's parcel is not drawn in the airport");
                s.Paused = false;
                float until = Time.realtimeSinceStartup + 6f;
                while (!(p.X == 3 && p.Progress >= 1f) && Time.realtimeSinceStartup < until) yield return null;
                s.Paused = true;
                Assert.AreEqual((3, 3), (p.X, p.Y), "the parcel moved along the rail in the factory we were not in");
                Assert.AreEqual(1, hq.Sim.AwaitingInspectionCount);
                Assert.AreEqual(FactorySwitchResult.Ok, boot.Input.SwitchFactory(0));
                yield return null;
                yield return null;
                var view = boot.GetComponent<PackageViews>().ViewOf(p.Id);
                Assert.IsNotNull(view, "back home the parcel is drawn where it got to");
                Assert.AreEqual(Grid3D.Tile(3, 3).x, view.transform.position.x, 0.55f, "on the table tile (at its far edge, progress 1)");
                Assert.AreEqual(Grid3D.Tile(3, 3).z, view.transform.position.z, 0.05f);
                Assert.Greater(airport.Spawner.Timer, 0f, "the airport we were in ticked too");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AlertChip_BlinksRed_ToastOncePer30Seconds_SummaryHasFactoryLines()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                var airport = s.Company.AddFactory(FactoryKind.Airport);
                yield return null;
                Assert.IsFalse(boot.Factories.IsAlerting(1));
                // 2공장 입고구 앞이 비어 있어 내보낸 택배가 소실된다.
                Assert.IsTrue(airport.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _));
                airport.Sim.Tick(2f);
                Assert.AreEqual(1, airport.Sim.LostCount);
                yield return null;
                Assert.IsTrue(boot.Factories.IsAlerting(1), "a new loss turns the chip red");
                Assert.AreEqual("2공장: 택배가 쌓이고 있습니다", boot.Hud.LastToast);
                Assert.AreEqual("2공장 · 대기 0 · 소실 1", boot.Factories.ChipTextOf(1));
                boot.Hud.Toast("다른 알림");
                Assert.IsTrue(airport.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _));
                airport.Sim.Tick(2f);
                yield return null;
                Assert.AreEqual("다른 알림", boot.Hud.LastToast, "at most one toast per factory per 30 s");
                Assert.AreEqual("2공장 · 대기 0 · 소실 2", boot.Factories.ChipTextOf(1));

                // 대기 6개 이상이면 깜빡임이 계속된다(새 소실이 없어도).
                foreach (var (x, y) in new[] { (1, 0), (2, 0), (3, 0), (4, 0), (1, 4), (2, 4) })
                {
                    Assert.AreEqual(BuildResult.Ok, airport.Build.TryPlace(x, y, CellType.Table, Direction.North));
                    var p = Good(airport); p.Inspected = false;
                    airport.Sim.PlaceCarried(p, x, y);
                }
                airport.Sim.Tick(2f);
                yield return new WaitForSeconds(FactoryBar.AlertBlinkSeconds + 0.1f);
                Assert.AreEqual(6, airport.WaitingCount);
                Assert.IsTrue(boot.Factories.IsAlerting(1), "six waiting keeps it red");
                StringAssert.Contains("대기 6", boot.Factories.ChipTextOf(1));

                // 하루 요약: 공장별 줄.
                s.Clock.Set(1, 17 * 60 + 59.9f);
                s.Paused = false;
                float until = Time.realtimeSinceStartup + 3f;
                while (s.Clock.IsOpen && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                Assert.IsTrue(boot.Panels.SummaryOpen);
                Assert.AreEqual("1공장 본사 물류센터", boot.Hud.Root.Find("DaySummary/Card/Factories/Factory1L").GetComponent<Text>().text);
                Assert.AreEqual("2공장 공항 물류센터", boot.Hud.Root.Find("DaySummary/Card/Factories/Factory2L").GetComponent<Text>().text);
                StringAssert.Contains("소실 2", boot.Hud.Root.Find("DaySummary/Card/Factories/Factory2").GetComponent<Text>().text);
                Assert.IsFalse(boot.Hud.Root.Find("DaySummary/Card/Factories/Factory3").gameObject.activeSelf);
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShopTrucks_BuySecondTruck_PillShowsCount_SwapDrivesOutThenIn()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(2000);
                boot.Input.RequestShop();
                yield return null;
                Assert.AreEqual("출고 트럭 2대째 · 지금 1대", boot.Hud.Root.Find("Shop/Viewport/Content/Trucks/List/Row_Outlet/Name").GetComponent<Text>().text);
                Assert.IsFalse(boot.Hud.Root.Find("Shop/Viewport/Content/Trucks/List/Row_AirOutlet").gameObject.activeSelf, "no air dock rows before Plot2");
                var buy = ShopBuy(boot, "Trucks/List/Row_Outlet");
                Assert.AreEqual("400", ButtonText(buy));
                int money = s.Economy.Money;
                buy.onClick.Invoke();
                Assert.AreEqual(money - 400, s.Economy.Money);
                Assert.AreEqual(2, s.Sim.Trucks.Count(CellType.Outlet));
                Assert.AreEqual("900", ButtonText(buy));
                Assert.AreEqual("출고 트럭 3대째 · 지금 2대", boot.Hud.Root.Find("Shop/Viewport/Content/Trucks/List/Row_Outlet/Name").GetComponent<Text>().text);
                buy.onClick.Invoke();
                Assert.AreEqual("최대", ButtonText(buy));
                Assert.IsFalse(buy.interactable);
                // 서랍은 화면 안에 들어간다(새 공장 묶음 맨 아래까지).
                Canvas.ForceUpdateCanvases();
                var shop = (RectTransform)boot.Hud.Root.Find("Shop");
                var last = (RectTransform)boot.Hud.Root.Find("Shop/Viewport/Content/Factories/List/Row_Precision");
                var corners = new Vector3[4];
                last.GetWorldCorners(corners);
                float rowBottom = corners[0].y;
                shop.GetWorldCorners(corners);
                Assert.GreaterOrEqual(rowBottom, corners[0].y - 0.01f, "the last row sits inside the drawer");
                boot.Panels.CloseAll();
                yield return null;
                Assert.AreEqual("0/30 · 3대", boot.Trucks.TextOf(CellType.Outlet));

                var f = s.Factory;
                for (int i = 0; i < 30; i++) f.Sim.DeliverCarried(Good(f), CellType.Outlet);
                Assert.IsTrue(f.Trucks.IsSwapping(CellType.Outlet));
                Assert.AreEqual("출고 트럭 출발 · 교대 0:02", boot.Hud.LastToast);
                yield return null;
                Assert.AreEqual("교대 0:02 · 30/30", boot.Trucks.TextOf(CellType.Outlet), "the leaving load stays on the pill until the truck is out");
                s.Paused = false;
                s.Speed = 2f; // 교대(2분 = 1x 2초)가 2배속이면 1초: 출발 연출(1.5초)보다 먼저 끝난다
                float until = Time.realtimeSinceStartup + 4f;
                while (f.Trucks.IsAway(CellType.Outlet) && Time.realtimeSinceStartup < until) yield return null;
                Assert.IsFalse(f.Trucks.IsAway(CellType.Outlet), "the spare truck is at the dock");
                Assert.Greater(boot.Trucks.Progress(CellType.Outlet), 0f, "the leaving truck is still driving off");
                yield return null;
                if (boot.Trucks.Progress(CellType.Outlet) < 1f) Assert.AreEqual("30/30 출발", boot.Trucks.TextOf(CellType.Outlet));
                float peak = 0f;
                until = Time.realtimeSinceStartup + 3f;
                while (boot.Trucks.Progress(CellType.Outlet) < 1f && Time.realtimeSinceStartup < until) { peak = Mathf.Max(peak, boot.Trucks.Progress(CellType.Outlet)); yield return null; }
                Assert.AreEqual(1f, boot.Trucks.Progress(CellType.Outlet), "drives all the way out before the next one comes in");
                until = Time.realtimeSinceStartup + 3f;
                while (boot.Trucks.Progress(CellType.Outlet) > 0f && Time.realtimeSinceStartup < until) yield return null;
                Assert.AreEqual(0f, boot.Trucks.Progress(CellType.Outlet), "the next truck parks");
                s.Paused = true;
                yield return null;
                Assert.AreEqual("0/30 · 3대", boot.Trucks.TextOf(CellType.Outlet));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator Switch_RechecksTheHeldTool_DirectionTypeAndAirMode()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                var input = boot.Input;
                var hq = s.Factory;
                hq.Unlocks.Grant(UnlockId.RailSouth);
                hq.Unlocks.Grant(UnlockId.Scanner); // 부지 확장 2도 같이(선행)
                hq.Unlocks.Grant(UnlockId.Splitter);
                var airport = s.Company.AddFactory(FactoryKind.Airport);
                airport.Unlocks.Grant(UnlockId.Splitter);
                s.Worker.Worker.X = 3f; s.Worker.Worker.Y = 4f; s.Worker.Worker.Facing = Direction.South; // 빈 칸 (3,3)을 본다
                yield return null;

                // 1) 레일 ↓를 든 채 2공장으로: 2공장은 레일 →만 있다 → 방향만 바뀐다.
                input.SelectSlot(input.AvailableSlots().IndexOf(CellType.Conveyor));
                input.BeginConfig();
                input.ApplyConfigDirection(Direction.South);
                Assert.AreEqual(new Tool(CellType.Conveyor, Direction.South), input.SelectedTool);
                Assert.AreEqual(FactorySwitchResult.Ok, input.SwitchFactory(1));
                Assert.AreEqual(new Tool(CellType.Conveyor, Direction.East), input.SelectedTool, "an unowned direction falls back to an owned one");

                // 2) 2공장(항공 도크 있음)에서 분배기를 항공 모드로 들고 1공장(부지 확장 2 있음 → 항공 도크 있음)으로 → 그대로.
                s.Worker.Worker.X = 3f; s.Worker.Worker.Y = 4f; s.Worker.Worker.Facing = Direction.South;
                input.SelectSlot(input.AvailableSlots().IndexOf(CellType.Splitter));
                input.BeginConfig();
                input.SelectSlot(2); // R → 3: 모드
                Assert.AreEqual(RouteMode.Air, input.ToolMode, "the airport allows air mode before Plot2");
                Assert.AreEqual(FactorySwitchResult.Ok, input.SwitchFactory(0));
                Assert.AreEqual(CellType.Splitter, input.SelectedTool.Value.Type);
                Assert.AreEqual(RouteMode.Air, input.ToolMode, "the first factory has air docks (Plot2)");

                // 3) 항공 도크가 없는 공장으로 가면 검사 모드로. 새 회사(1공장 부지 확장 2 없음)에 2공장을 더해 거꾸로 옮긴다.
                var t = new GameSession(new Economy(1000)) { Paused = true };
                t.Unlocks.Grant(UnlockId.Splitter);
                var a2 = t.Company.AddFactory(FactoryKind.Airport);
                a2.Unlocks.Grant(UnlockId.Splitter);
                boot.ReplaceSession(t);
                yield return null;
                Assert.AreEqual(FactorySwitchResult.Ok, input.SwitchFactory(1));
                t.Worker.Worker.X = 3f; t.Worker.Worker.Y = 4f; t.Worker.Worker.Facing = Direction.South;
                input.SelectSlot(input.AvailableSlots().IndexOf(CellType.Splitter));
                input.BeginConfig();
                input.SelectSlot(2);
                Assert.AreEqual(RouteMode.Air, input.ToolMode);
                Assert.AreEqual(FactorySwitchResult.Ok, input.SwitchFactory(0));
                Assert.AreEqual(RouteMode.Inspect, input.ToolMode, "no air docks here: back to inspect mode");
                Assert.AreEqual(CellType.Splitter, input.SelectedTool.Value.Type);

                // 4) 1공장에만 있는 자동 검사기를 들고 2공장으로 → 빈손.
                t.Unlocks.Grant(UnlockId.Scanner);
                yield return null;
                input.SelectSlot(input.AvailableSlots().IndexOf(CellType.Scanner));
                Assert.AreEqual(CellType.Scanner, input.SelectedTool.Value.Type);
                Assert.AreEqual(FactorySwitchResult.Ok, input.SwitchFactory(1));
                Assert.IsFalse(input.SelectedTool.HasValue, "a tool type the factory does not own is dropped");
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Trucks_DepartureAndSwapInBetweenTwoFrames_StillPlaysTheDeparture()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                Assert.AreEqual(TruckPurchaseResult.Ok, s.Factory.TryBuyTruck(CellType.ReturnDock));
                yield return null;
                var f = s.Factory;
                for (int i = 0; i < 30; i++) f.Sim.DeliverCarried(Good(f), CellType.ReturnDock);
                f.Trucks.Advance(TruckFleet.TruckSwapMinutes); // 같은 프레임에 교대까지 끝난다: 다음 프레임에는 도크에 트럭이 있다
                Assert.IsFalse(f.Trucks.IsAway(CellType.ReturnDock));
                yield return null;
                Assert.AreEqual("30/30 출발", boot.Trucks.TextOf(CellType.ReturnDock), "the leaving load shows while the old truck drives off");
                s.Paused = false;
                yield return new WaitForSeconds(TruckViews.DriveSeconds * 0.4f);
                Assert.IsTrue(boot.Trucks.IsDriving(CellType.ReturnDock), "the departure still plays");
                float until = Time.realtimeSinceStartup + 4f;
                while (boot.Trucks.Progress(CellType.ReturnDock) < 1f && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                Assert.AreEqual("0/30 · 2대", boot.Trucks.TextOf(CellType.ReturnDock), "after the old truck left, the pill shows the truck at the dock");
                until = Time.realtimeSinceStartup + 4f;
                while (boot.Trucks.Progress(CellType.ReturnDock) > 0f && Time.realtimeSinceStartup < until) yield return null;
                Assert.AreEqual(0f, boot.Trucks.Progress(CellType.ReturnDock), "then the next one parks");
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Shop_ClampsToTheScreen_ScrollsByWheelOrSelection()
        {
            var boot = GameBootstrap.Ensure();
            CanvasScaler scaler = null;
            var prevMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            float prevFactor = 1f;
            try
            {
                yield return null;
                scaler = boot.Hud.Root.GetComponent<CanvasScaler>();
                prevMode = scaler.uiScaleMode;
                prevFactor = scaler.scaleFactor;
                var s = boot.Session;
                s.Paused = true;
                s.Unlocks.Grant(UnlockId.Plot2); // 택배차 네 줄
                // 캔버스 높이를 420으로: 서랍(제한 전 약 694)이 넘친다.
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = Screen.height / 420f;
                yield return null;
                boot.Input.RequestShop();
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var shop = (RectTransform)boot.Hud.Root.Find("Shop");
                float canvasH = boot.Hud.Root.rect.height;
                Assert.AreEqual(canvasH - 62f - Panels.ShopBottomMargin, shop.rect.height, 0.5f, "drawer height is clamped to the canvas");
                Assert.IsTrue(boot.Panels.ShopScrolls);
                Assert.IsTrue(boot.Hud.Root.Find("Shop/Scrollbar").gameObject.activeSelf, "a scrollbar appears when the content overflows");
                Assert.IsNotNull(boot.Hud.Root.Find("Shop/Viewport").GetComponent<RectMask2D>());
                Assert.AreEqual(1f, boot.Panels.ShopScrollPosition, 1e-3f, "opens at the top");

                var viewport = (RectTransform)boot.Hud.Root.Find("Shop/Viewport");
                bool Inside(RectTransform row)
                {
                    Canvas.ForceUpdateCanvases();
                    var c = new Vector3[4]; var v = new Vector3[4];
                    row.GetWorldCorners(c); viewport.GetWorldCorners(v);
                    return c[0].y >= v[0].y - 0.5f && c[1].y <= v[1].y + 0.5f;
                }
                var last = (RectTransform)boot.Hud.Root.Find("Shop/Viewport/Content/Factories/List/Row_Precision");
                Assert.IsFalse(Inside(last), "the bottom group starts out of view");
                // 키보드 선택: 맨 아래 행까지 내려가면 그 행이 보인다.
                for (int i = 0; i < 30; i++) boot.Panels.MoveShopSelection(1);
                Assert.AreSame(last, boot.Panels.ShopSelectedRowTransform);
                Assert.IsTrue(Inside(last), "the selected row is scrolled into view");
                Assert.IsTrue(last.Find("Buy/Selected").gameObject.activeSelf, "selection ring");
                Assert.Less(boot.Panels.ShopScrollPosition, 0.01f);
                for (int i = 0; i < 30; i++) boot.Panels.MoveShopSelection(-1);
                Assert.IsTrue(Inside((RectTransform)boot.Hud.Root.Find("Shop/Viewport/Content/List").GetChild(0)));
                Assert.Greater(boot.Panels.ShopScrollPosition, 0.99f);
                // 휠(스크롤 이벤트)로도 내려간다.
                var e = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { scrollDelta = new Vector2(0f, -10f) };
                UnityEngine.EventSystems.ExecuteEvents.Execute(viewport.gameObject, e, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
                Canvas.ForceUpdateCanvases();
                Assert.Less(boot.Panels.ShopScrollPosition, 0.99f, "the wheel scrolls");
                boot.Panels.ShopScrollPosition = 0f;
                Assert.IsTrue(Inside(last), "the bottom group is reachable by scrolling");
                boot.Panels.CloseAll();
            }
            finally
            {
                if (scaler != null) { scaler.uiScaleMode = prevMode; scaler.scaleFactor = prevFactor; }
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }
    }
}
