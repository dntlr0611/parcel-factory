using System.Collections;
using NUnit.Framework;
using ParcelFactory.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace ParcelFactory.Game.Tests
{
    public class BootSmokeTests
    {
        private static GameObject FindCellView(string name, int x, int y)
        {
            var cells = GameObject.Find("World/Cells");
            if (cells == null) return null;
            var at = Grid3D.Tile(x, y);
            foreach (Transform t in cells.transform)
                if (t.name == name && Mathf.Abs(t.position.x - at.x) < 1e-3f && Mathf.Abs(t.position.z - at.z) < 1e-3f) return t.gameObject;
            return null;
        }

        [UnityTest]
        public IEnumerator Boot_CreatesSessionAndTicks()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                Assert.IsNotNull(boot.Session);
                Assert.IsNotNull(boot.Catalog, "PF_Catalog missing — run CatalogBuilder.BuildAll");
                int t0 = boot.Loop.Ticks;
                yield return new WaitForSeconds(0.6f);
                Assert.Greater(boot.Loop.Ticks, t0, "game loop did not tick");
                boot.Session.Build.TryPlace(1, 2, ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.East);
                yield return null;
                Assert.IsNotNull(GameObject.Find("World/Cells/Conveyor"), "cell view not created");
                Assert.IsNotNull(GameObject.Find("World/WorkerView"), "worker view not created");
                Assert.IsNotNull(GameObject.Find("World/Lot/Slab"), "lot slab not created");
                Assert.IsNotNull(GameObject.Find("World/Walls"), "factory walls not created");
                Assert.IsNotNull(GameObject.Find("World/Yard"), "yard not created");
                Assert.IsNotNull(GameObject.Find("World/Cells/Conveyor/ArrowPivot/Chevron"), "belt chevron not created");
                var workerAnim = boot.Worker.Model.GetComponent<Animation>();
                Assert.IsNotNull(workerAnim, "worker model has no legacy Animation (CatalogBuilder.ConfigureCharacterImport)");
                Assert.IsNotNull(workerAnim.GetClip("walk"), "walk clip missing");
                Assert.IsNotNull(workerAnim.GetClip("holding-both"), "holding-both clip missing");
                Assert.IsNotNull(boot.Input);
                // 팔레트 뷰: 시작 파랑 팔레트 (5,2)에 "Pallet" 오브젝트, 더미 3개를 올리면 3개를 그린다.
                var pallet = FindCellView("Pallet", 5, 2);
                Assert.IsNotNull(pallet, "pallet view at (5,2) not created");
                var pv = pallet.GetComponent<PalletView>();
                Assert.IsNotNull(pv, "pallet view has no PalletView");
                Assert.AreEqual(ParcelFactory.Core.PalletKind.Blue, pv.Cell.PalletKind);
                var factory = new ParcelFactory.Core.PackageFactory(new System.Random(1));
                for (int i = 0; i < 3; i++)
                    Assert.IsTrue(boot.Session.Sim.PutOnPallet(factory.Create(9000 + i, 5, 2, ParcelFactory.Core.Direction.East), 5, 2));
                yield return null;
                var pileRoot = pallet.transform.Find("Pile");
                Assert.IsNotNull(pileRoot, "pallet pile root missing");
                int activeBoxes = 0;
                foreach (Transform b in pileRoot) if (b.gameObject.activeSelf) activeBoxes++;
                Assert.AreEqual(3, activeBoxes, "pallet pile not drawn");
                // 층당 2개: 세 번째 상자는 2층(y 0.52)
                Assert.AreEqual(0.52f, pileRoot.GetChild(2).localPosition.y, 1e-3f);
                Assert.AreEqual(-0.22f, pileRoot.GetChild(2).localPosition.x, 1e-3f);
                Assert.IsNotNull(FindCellView("Pallet", 5, 4), "pallet view at (5,4) not created");
                // 트럭 도크 슬래브는 (11,2)·(11,4) 칸 위.
                var outletSlab = GameObject.Find("World/Docks/Dock_delivery/DockSlab");
                Assert.IsNotNull(outletSlab, "outlet dock slab missing");
                var tile = Grid3D.Tile(ParcelFactory.Core.FactoryGrid.OutletX, ParcelFactory.Core.FactoryGrid.OutletY);
                Assert.AreEqual(Grid3D.Tile(11, 2).x, tile.x, 1e-4f);
                Assert.AreEqual(tile.x, outletSlab.transform.position.x, 1e-3f);
                Assert.AreEqual(tile.z, outletSlab.transform.position.z, 1e-3f);
                var returnSlab = GameObject.Find("World/Docks/Dock_truck/DockSlab");
                Assert.IsNotNull(returnSlab, "return dock slab missing");
                Assert.AreEqual(Grid3D.Tile(11, 4).x, returnSlab.transform.position.x, 1e-3f);
                Assert.AreEqual(Grid3D.Tile(11, 4).z, returnSlab.transform.position.z, 1e-3f);
                Assert.IsNull(GameObject.Find("World/Docks/Dock_delivery/Lane"), "pallet→door lanes should be gone");
                Assert.IsNotNull(GameObject.Find("World/Docks/Dock_delivery/StopLine"), "truck dock stop line missing");

                Assert.AreEqual(3, boot.Input.AvailableSlots().Count); // Conveyor + Pallet + Table (Pallet, Table owned by default)
                Assert.AreEqual(ParcelFactory.Core.CellType.Pallet, boot.Input.AvailableSlots()[1]);
                boot.Input.SelectSlot(1);
                Assert.AreEqual(ParcelFactory.Core.Direction.East, boot.Input.SelectedTool.Value.Direction); // 파랑
                boot.Input.SelectSlot(1); // 재탭 → 빨강
                Assert.AreEqual(ParcelFactory.Core.PalletKind.Red, ParcelFactory.Core.Tool.KindOf(boot.Input.SelectedTool.Value.Direction));
                boot.Input.SelectSlot(0);
                Assert.AreEqual(ParcelFactory.Core.CellType.Conveyor, boot.Input.SelectedTool.Value.Type);
                Assert.AreEqual(0, boot.Input.SelectedSlot);
                boot.Input.SelectSlot(0);
                Assert.IsTrue(boot.Input.SelectedTool.HasValue); // re-tap cycles, does not clear
                boot.Input.ClearTool();
                Assert.AreEqual(-1, boot.Input.SelectedSlot);
                Assert.IsNotNull(boot.Hud);
                Assert.IsNotNull(GameObject.Find("HUD"));
                boot.Hud.Toast("test");
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator TouchControls_StayInsideCanvas()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                boot.Hud.ForceTouchMode(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var canvasCorners = new Vector3[4];
                boot.Hud.Root.GetWorldCorners(canvasCorners);
                var min = canvasCorners[0];
                var max = canvasCorners[2];
                foreach (var name in new[] { "TouchE", "TouchX", "TouchR", "TouchPadN", "TouchPadS", "TouchPadW", "TouchPadE" })
                {
                    var go = GameObject.Find(name);
                    Assert.IsNotNull(go, name + " missing");
                    var corners = new Vector3[4];
                    go.GetComponent<RectTransform>().GetWorldCorners(corners);
                    foreach (var c in corners)
                        Assert.IsTrue(c.x >= min.x - 0.01f && c.x <= max.x + 0.01f && c.y >= min.y - 0.01f && c.y <= max.y + 0.01f,
                            name + " corner " + c + " outside canvas " + min + ".." + max);
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaveLoad_And_Panels_BlockInput()
        {
            var boot = GameBootstrap.Ensure();
            var tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pf-smoke-" + System.Guid.NewGuid().ToString("N") + ".json");
            SaveFile.PathOverride = tmpPath;
            try
            {
                yield return null;
                Assert.IsNotNull(boot.Panels);
                Assert.IsFalse(boot.Panels.AnyOpen);
                Assert.IsFalse(boot.Input.UiBlocksInput);

                boot.Session.Economy.Earn(77);
                int saved = boot.Session.Economy.Money;
                boot.Session.Build.TryPlace(1, 2, ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.East);
                saved = boot.Session.Economy.Money;
                Assert.IsTrue(SaveFile.TrySave(boot.Session, out var err), err);
                Assert.IsTrue(System.IO.File.Exists(tmpPath));
                boot.Session.Economy.Earn(1000);
                Assert.AreNotEqual(saved, boot.Session.Economy.Money);
                Assert.IsTrue(SaveFile.TryLoad(out var loaded, out err), err);
                boot.ReplaceSession(loaded);
                yield return null;
                Assert.AreEqual(saved, boot.Session.Economy.Money);
                Assert.AreEqual(ParcelFactory.Core.CellType.Conveyor, boot.Session.Grid.Get(1, 2).Type);

                boot.Input.RequestShop();
                yield return null;
                Assert.IsTrue(boot.Panels.AnyOpen);
                Assert.IsTrue(boot.Input.UiBlocksInput);
                boot.Panels.CloseAll();
                yield return null;
                Assert.IsFalse(boot.Panels.AnyOpen);
                Assert.IsFalse(boot.Input.UiBlocksInput);

                // 상점이 유일하게 열려 있으면 다시 요청해 닫을 수 있다.
                boot.Input.RequestShop();
                yield return null;
                Assert.IsTrue(boot.Panels.ShopIsOnlyOpen);
                boot.Input.RequestShop();
                yield return null;
                Assert.IsFalse(boot.Panels.AnyOpen);
                Assert.IsFalse(boot.Input.UiBlocksInput);

                boot.Input.SetBlock("help", true);
                boot.Input.SetBlock("panels", true);
                boot.Input.SetBlock("panels", false);
                Assert.IsTrue(boot.Input.UiBlocksInput, "help block must survive panels release");
                boot.Input.SetBlock("help", false);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                SaveFile.PathOverride = null;
                try { System.IO.File.Delete(tmpPath); System.IO.File.Delete(tmpPath + ".tmp"); } catch { }
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InspectionCard_BlocksInputInSameFrame_AndSurvivesHeldPad()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 게임 루프가 택배를 움직이지 않게 한다
                s.Grid.Place(1, 2, ParcelFactory.Core.CellType.Table, ParcelFactory.Core.Direction.East);
                Assert.IsTrue(s.Sim.TrySpawn(0, 2, ParcelFactory.Core.PackageKind.Normal, out _));
                s.Sim.Tick(1f);
                s.Worker.Worker.X = 1; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = ParcelFactory.Core.Direction.South;
                boot.Input.SetPad(ParcelFactory.Core.Direction.East, true); // 방향 입력을 누른 채
                boot.Input.Interact();
                // 한 프레임도 지나지 않았다
                Assert.IsNotNull(s.Worker.PendingInspection);
                Assert.IsTrue(boot.Panels.AnyOpen, "inspection card should be open");
                Assert.IsTrue(boot.Input.UiBlocksInput, "block must apply in the same frame");
                yield return null;
                yield return null;
                Assert.IsNotNull(s.Worker.PendingInspection, "held pad must not cancel the inspection");
                Assert.IsTrue(boot.Panels.AnyOpen);
                boot.Panels.CloseAll();
                Assert.IsNull(s.Worker.PendingInspection);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                boot.Input.ClearPad();
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }
    }
}
