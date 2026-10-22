using System.Collections;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ParcelFactory.Game.Tests
{
    /// <summary>스펙 14: 항공 트럭(동쪽 벽 (11,7) 초록 · (11,9) 노랑)과 항공 택배 마크, 운송 수단 틀림 −10.</summary>
    public class AirTests
    {
        private static int CountDoorways(string path)
        {
            var east = GameObject.Find(path);
            if (east == null) return -1;
            int n = 0;
            foreach (Transform t in east.transform) if (t.name == "Doorway") n++;
            return n;
        }

        private static bool MarkOn(GameObject box)
        {
            var m = box.transform.Find("AirMark");
            return m != null && m.gameObject.activeSelf;
        }

        [UnityTest]
        public IEnumerator Plot2_CreatesAirDocksTrucksAndDoorways()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                Assert.AreEqual(2, CountDoorways("World/Walls/East"), "east wall: outlet + return doorways only");
                Assert.IsNull(GameObject.Find("World/AirDocks/Dock_air-outlet"), "air dock exists before Plot2");

                s.Economy.Earn(1000);
                Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot1));
                yield return null;
                Assert.IsNull(GameObject.Find("World/AirDocks/Dock_air-outlet"), "air dock exists after Plot1");
                Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot2));
                yield return null;

                Assert.AreEqual(4, CountDoorways("World/Walls/East"), "air doorways missing");
                var outSlab = GameObject.Find("World/AirDocks/Dock_air-outlet/DockSlab");
                var retSlab = GameObject.Find("World/AirDocks/Dock_air-return/DockSlab");
                Assert.IsNotNull(outSlab, "air outlet slab missing");
                Assert.IsNotNull(retSlab, "air return slab missing");
                Assert.AreEqual(Grid3D.Tile(11, 7).x, outSlab.transform.position.x, 1e-3f);
                Assert.AreEqual(Grid3D.Tile(11, 7).z, outSlab.transform.position.z, 1e-3f);
                Assert.AreEqual(Grid3D.Tile(11, 9).z, retSlab.transform.position.z, 1e-3f);
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-outlet/delivery"), "green air truck missing");
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-return/delivery"), "yellow air truck missing (box truck, same model as the others)");
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-outlet/AirSign/Board/Icon"), "air sign missing");
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-return/AirSign/Board/Icon"), "air sign missing");
                var outC = outSlab.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
                var retC = retSlab.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.AreEqual(WorldBuilder.AirOutletColor.g, outC.g, 1e-3f, "air outlet slab should be green");
                Assert.AreEqual(WorldBuilder.AirReturnColor.r, retC.r, 1e-3f, "air return slab should be yellow");
                Assert.AreEqual(WorldBuilder.AirReturnColor.b, retC.b, 1e-3f);
                // 기존 도크는 그대로
                Assert.IsNotNull(GameObject.Find("World/Docks/Dock_delivery/DockSlab"));
                Assert.IsNotNull(GameObject.Find("World/Docks/Dock_truck/DockSlab"));

                // 새 게임(확장 전)으로 바꾸면 항공 트럭이 사라지고, 확장 2가 든 세션을 불러오면 다시 생긴다.
                boot.StartNewGame();
                yield return null;
                Assert.AreEqual(2, CountDoorways("World/Walls/East"));
                Assert.IsNull(GameObject.Find("World/AirDocks/Dock_air-outlet"));
                var loaded = SaveSerializer.Restore(SaveSerializer.Capture(s));
                boot.ReplaceSession(loaded);
                yield return null;
                Assert.AreEqual(4, CountDoorways("World/Walls/East"));
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-outlet/DockSlab"));
                Assert.IsNotNull(GameObject.Find("World/AirDocks/Dock_air-return/DockSlab"));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AirPackages_ShowMarkOnRailPalletAndHands()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(100);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 2, CellType.Conveyor, Direction.East));
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(3, 2, CellType.Conveyor, Direction.East));
                var f = new PackageFactory(new System.Random(4), 0.0);
                var air = f.Create(s.Sim.NextId++, 2, 2, Direction.East); air.Air = true;
                var plain = f.Create(s.Sim.NextId++, 3, 2, Direction.East);
                s.Sim.PlaceCarried(air, 2, 2);
                s.Sim.PlaceCarried(plain, 3, 2);
                var pileAir = f.Create(s.Sim.NextId++, 5, 2, Direction.East); pileAir.Air = true;
                Assert.IsTrue(s.Sim.PutOnPallet(pileAir, 5, 2));
                var carried = f.Create(s.Sim.NextId++, 0, 0, Direction.East); carried.Air = true;
                s.Worker.Worker.Carrying = carried;
                yield return null;

                var views = boot.GetComponent<PackageViews>();
                Assert.IsTrue(MarkOn(views.ViewOf(air.Id)), "rail air box has no mark");
                Assert.IsFalse(MarkOn(views.ViewOf(plain.Id)), "plain box shows air mark");
                var mark = views.ViewOf(air.Id).transform.Find("AirMark");
                Assert.IsNotNull(mark.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap"), "air mark texture missing");
                var carriedBox = GameObject.Find("World/WorkerView/CarryAnchor/Package");
                Assert.IsNotNull(carriedBox);
                Assert.IsTrue(MarkOn(carriedBox), "carried air box has no mark");
                var pile = PalletPile(5, 2);
                Assert.IsNotNull(pile, "pallet pile at (5,2) missing");
                Assert.IsTrue(MarkOn(pile.transform.GetChild(0).gameObject), "pallet air box has no mark");
                Assert.AreEqual("항공 택배 #" + carried.Id + " 운반 중 · 레일·팔레트·트럭을 보고 E", boot.Hud.Root.Find("Status/Text").GetComponent<Text>().text);

                // 들고 있던 항공 택배를 내려놓으면 같은 상자 뷰가 일반 택배에 다시 쓰여도 마크가 꺼진다.
                s.Worker.Worker.Carrying = null;
                s.Sim.Take(air);
                yield return null;
                Assert.IsNull(views.ViewOf(air.Id));
                s.Sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _); // 풀에서 꺼낸 상자에 남은 마크가 없어야 한다
                yield return null;
                foreach (var p in s.Sim.Packages)
                    Assert.AreEqual(p.Air, MarkOn(views.ViewOf(p.Id)), "mark mismatch on #" + p.Id);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WrongCarrier_PromptToastAndMinusTen()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Unlocks.Grant(UnlockId.Plot2);
                yield return null;
                var f = new PackageFactory(new System.Random(6), 0.0);
                var plain = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                plain.Inspect(false);
                s.Worker.Worker.Carrying = plain;
                s.Worker.Worker.X = 10f; s.Worker.Worker.Y = 7f; s.Worker.Worker.Facing = Direction.East;
                Assert.AreEqual((11, 7), s.Worker.FacingTile());
                yield return null;
                var promptText = boot.Hud.Root.Find("Prompt/Text").GetComponent<Text>();
                Assert.AreEqual("운송 수단 틀림 −10", promptText.text);
                int money0 = s.Economy.Money;
                boot.Input.Interact();
                Assert.IsNull(s.Worker.Worker.Carrying);
                Assert.AreEqual(money0 - 10, s.Economy.Money);
                Assert.AreEqual("운송 수단 틀림 −10", boot.Hud.LastToast);

                var air = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                air.Air = true;
                air.Inspect(false);
                air.Waybill = true;
                s.Worker.Worker.Carrying = air;
                yield return null;
                Assert.AreEqual("항공 출고 +30", promptText.text);
                boot.Input.Interact();
                Assert.AreEqual(money0 + 20, s.Economy.Money);
                Assert.AreEqual("항공 정상 출고 +30", boot.Hud.LastToast);

                Assert.AreEqual("항공 반품 +10", HudController.DeliverLabel(CellType.AirReturnDock, true, true, true));
                Assert.AreEqual("항공 출고 (미검사 −20)", HudController.DeliverLabel(CellType.AirOutlet, true, false, false));
                Assert.AreEqual("운송 수단 틀림 −10", HudController.DeliverLabel(CellType.Outlet, true, true, false));
                Assert.AreEqual("출고 +30", HudController.DeliverLabel(CellType.Outlet, false, true, false));
                Assert.AreEqual(-10, HudController.ExpectedDelta(CellType.ReturnDock, true, true, true));
                Assert.AreEqual((11, 9), HudController.DockTile(CellType.AirReturnDock));
                Assert.AreEqual("항공 출고", HudController.Name(CellType.AirOutlet));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InspectCard_ShowsAirMethodWithIcon()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(100);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 3, CellType.Table, Direction.East));
                var f = new PackageFactory(new System.Random(8), 0.0, 1.0);
                var p = f.Create(s.Sim.NextId++, 0, 0, Direction.East);
                Assert.IsTrue(p.Air);
                s.Sim.PlaceCarried(p, 2, 3);
                s.Worker.Worker.X = 2f; s.Worker.Worker.Y = 4f; s.Worker.Worker.Facing = Direction.South;
                yield return null;
                boot.Input.Interact();
                yield return null;
                var method = FindDeep(boot.transform, "Method");
                Assert.IsNotNull(method, "method row missing");
                Assert.AreEqual("항공", method.GetComponent<Text>().text);
                var icon = FindDeep(boot.transform, "MethodAir");
                Assert.IsNotNull(icon, "air icon missing");
                Assert.IsTrue(icon.gameObject.activeInHierarchy, "air icon hidden");
                Assert.IsNotNull(icon.GetComponent<Image>().sprite, "ui-air sprite missing");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ModeToggle_RThree_SplitterAndScanner()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(5000);
                var input = boot.Input;
                var w = s.Worker.Worker;
                s.Unlocks.Grant(UnlockId.Splitter);
                s.Toolbar.Add(CellType.Scanner, Direction.East);
                s.Toolbar.Add(CellType.Conveyor, Direction.South);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(2, 2, CellType.Splitter, Direction.East));
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(3, 2, CellType.Conveyor, Direction.East));
                w.X = 2f; w.Y = 3f; w.Facing = Direction.South;
                var banner = boot.Hud.Root.Find("ConfigBanner");

                // 부지 확장 2 전: R → 3은 토스트만
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickSlot, input.ConfigMode);
                yield return null;
                Assert.AreEqual(HudController.SplitterSlotBannerText, banner.Find("Text").GetComponent<Text>().text);
                input.SelectSlot(2);
                Assert.AreEqual(InputController.ModeLockedMessage, boot.Hud.LastToast);
                Assert.AreEqual(RouteMode.Inspect, s.Grid.Get(2, 2).Mode);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);

                s.Unlocks.Grant(UnlockId.Plot2);
                yield return null;
                input.BeginConfig();
                input.SelectSlot(2);
                Assert.AreEqual(RouteMode.Air, s.Grid.Get(2, 2).Mode);
                Assert.AreEqual("분배기 모드: 항공", boot.Hud.LastToast);
                yield return null;
                input.BeginConfig();
                yield return null;
                Assert.AreEqual(HudController.AirSplitterSlotBannerText, banner.Find("Text").GetComponent<Text>().text);
                input.CancelConfig();
                var splitterView = FindView("Splitter", 2, 2);
                Assert.IsNotNull(splitterView);
                var blueArrow = splitterView.transform.Find("BlueArrow").GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.AreEqual(WorldBuilder.AirOutletColor.g, blueArrow.g, 1e-3f, "exit 1 should be green in air mode");
                StringAssert.StartsWith("분배기 · 항공", boot.StatusBars.TextAt(2, 2));

                // 검사기 (5,5) 동향: 오른쪽(남) (5,4) 레일 → 항공 출구 기본 남. R → 2 → ↑ 로 북으로.
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(5, 5, CellType.Scanner, Direction.East));
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(6, 5, CellType.Conveyor, Direction.East));
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(5, 6, CellType.Conveyor, Direction.East));
                s.Grid.Clear(5, 4);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(5, 4, CellType.Conveyor, Direction.South));
                w.X = 4f; w.Y = 5f; w.Facing = Direction.East;
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                yield return null;
                Assert.AreEqual(HudController.ScannerDirectionBannerText, banner.Find("Text").GetComponent<Text>().text);
                input.SelectSlot(2);
                Assert.AreEqual(RouteMode.Air, s.Grid.Get(5, 5).Mode);
                Assert.AreEqual(Direction.South, s.Grid.Get(5, 5).AirDirection);
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickSlot, input.ConfigMode);
                yield return null;
                Assert.AreEqual(HudController.AirSlotBannerText, banner.Find("Text").GetComponent<Text>().text);
                input.ApplyConfigSlot(2);
                input.ApplyConfigDirection(Direction.North);
                Assert.AreEqual(Direction.North, s.Grid.Get(5, 5).AirDirection);
                StringAssert.Contains("항공 출구", boot.Hud.LastToast);
                yield return null;
                var scannerView = FindView("Scanner", 5, 5);
                Assert.IsNotNull(scannerView.transform.Find("GroundArrow"), "ground arrow missing");
                Assert.IsNotNull(scannerView.transform.Find("AirArrow"), "air arrow missing");
                StringAssert.StartsWith("검사기 · 항공", boot.StatusBars.TextAt(5, 5));

                // 도구도 R → 3: 분배기 도구를 항공 모드로 설치, 미리보기 화살표는 초록/노랑
                input.SelectSlot(input.AvailableSlots().IndexOf(CellType.Splitter));
                w.X = 8f; w.Y = 8f; w.Facing = Direction.East;
                input.BeginConfig();
                Assert.IsFalse(input.ConfigTargetIsCell);
                input.SelectSlot(2);
                Assert.AreEqual(RouteMode.Air, input.ToolMode);
                yield return null;
                boot.Ghost.Refresh();
                var ghostArrow = boot.Ghost.Model.transform.Find("BlueArrow").GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.AreEqual(WorldBuilder.AirOutletColor.g, ghostArrow.g, 1e-3f, "preview exit 1 should be green");
                input.Interact();
                Assert.AreEqual(CellType.Splitter, s.Grid.Get(9, 8).Type);
                Assert.AreEqual(RouteMode.Air, s.Grid.Get(9, 8).Mode);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScannerHold_ToastsOnce_AndDimsBlockedExit()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                s.Unlocks.Grant(UnlockId.Plot2);
                s.Toolbar.Add(CellType.Scanner, Direction.East);
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(7, 6, CellType.Conveyor, Direction.East));
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(8, 6, CellType.Scanner, Direction.East));
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(9, 6, CellType.Conveyor, Direction.East));
                Assert.AreEqual(ReconfigureResult.Ok, s.Build.SetMode(8, 6, RouteMode.Air)); // 항공 출구 = 남 (8,5) 빈 칸 → 받을 곳 없음
                yield return null;
                StringAssert.Contains("<color=#8E959C>2", boot.StatusBars.TextAt(8, 6), "unconnected air exit should be dim");
                var a = new PackageFactory(new System.Random(3), 0.0, 1.0).Create(s.Sim.NextId++, 0, 0, Direction.East);
                s.Sim.PlaceCarried(a, 7, 6);
                for (int i = 0; i < 4; i++) s.Sim.Tick(0.5f);
                Assert.AreEqual((8, 6), (a.X, a.Y));
                Assert.AreEqual(HudController.ScannerBlockedToast, boot.Hud.LastToast);
                boot.Hud.Toast("x", HudController.ToastKind.Info);
                s.Sim.Tick(0.5f);
                Assert.AreEqual("x", boot.Hud.LastToast, "toast only once per block");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        private static GameObject FindView(string name, int x, int y)
        {
            var cells = GameObject.Find("World/Cells");
            var at = Grid3D.Tile(x, y);
            foreach (Transform t in cells.transform)
                if (t.name == name && Mathf.Abs(t.position.x - at.x) < 1e-3f && Mathf.Abs(t.position.z - at.z) < 1e-3f) return t.gameObject;
            return null;
        }

        private static GameObject PalletPile(int x, int y)
        {
            var cells = GameObject.Find("World/Cells");
            var at = Grid3D.Tile(x, y);
            foreach (Transform t in cells.transform)
                if (t.name == "Pallet" && Mathf.Abs(t.position.x - at.x) < 1e-3f && Mathf.Abs(t.position.z - at.z) < 1e-3f)
                    return t.Find("Pile")?.gameObject;
            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
