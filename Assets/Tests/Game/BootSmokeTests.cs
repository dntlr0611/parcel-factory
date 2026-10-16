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
    }
}
