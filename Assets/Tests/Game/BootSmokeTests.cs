using System.Collections;
using NUnit.Framework;
using ParcelFactory.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace ParcelFactory.Game.Tests
{
    public class BootSmokeTests
    {
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
                Assert.IsNotNull(boot.Input);
                Assert.AreEqual(2, boot.Input.AvailableSlots().Count); // Conveyor + Table (Table is owned by default)
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
