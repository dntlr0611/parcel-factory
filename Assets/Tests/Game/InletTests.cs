using System.Collections;
using NUnit.Framework;
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ParcelFactory.Game.Tests
{
    /// <summary>스펙 16.2: 부지 확장마다 서쪽 벽에 입고구 출입구와 초록 입고 트럭이 생긴다.</summary>
    public class InletTests
    {
        private static int Doorways(string path)
        {
            var go = GameObject.Find(path);
            if (go == null) return -1;
            int n = 0;
            foreach (Transform t in go.transform) if (t.name == "Doorway") n++;
            return n;
        }

        [UnityTest]
        public IEnumerator PlotUnlocks_AddInletDoorwaysAndTrucks()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                s.Economy.Earn(1000);
                Assert.AreEqual(1, Doorways("World/Walls/West"));
                Assert.IsNotNull(GameObject.Find("World/Docks/Dock_van/van"), "first inlet van missing");
                Assert.IsNull(GameObject.Find("World/InletDocks/Dock_inlet-7"));

                Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot1));
                yield return null;
                Assert.AreEqual(2, Doorways("World/Walls/West"));
                var slab7 = GameObject.Find("World/InletDocks/Dock_inlet-7/DockSlab");
                Assert.IsNotNull(slab7, "plot 1 inlet dock missing");
                Assert.AreEqual(Grid3D.Tile(0, 7).z, slab7.transform.position.z, 1e-3f);
                Assert.IsNotNull(GameObject.Find("World/InletDocks/Dock_inlet-7/van"), "plot 1 inlet truck missing");
                Assert.IsNull(GameObject.Find("World/InletDocks/Dock_inlet-10"));

                Assert.AreEqual(UnlockResult.Ok, s.Unlocks.TryPurchase(UnlockId.Plot2));
                yield return null;
                Assert.AreEqual(3, Doorways("World/Walls/West"));
                Assert.IsNotNull(GameObject.Find("World/InletDocks/Dock_inlet-10/van"), "plot 2 inlet truck missing");
                Assert.AreEqual(Grid3D.Tile(0, 10).z, GameObject.Find("World/InletDocks/Dock_inlet-10/DockSlab").transform.position.z, 1e-3f);

                // 새 게임(확장 전)이면 다시 하나뿐
                boot.StartNewGame();
                yield return null;
                Assert.AreEqual(1, Doorways("World/Walls/West"));
                Assert.IsNull(GameObject.Find("World/InletDocks/Dock_inlet-7"));
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
