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
            yield return null;
            Assert.IsNotNull(boot.Session);
            Assert.IsNotNull(boot.Catalog, "PF_Catalog missing — run CatalogBuilder.BuildAll");
            float t0 = boot.Session.Spawner.Timer;
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(boot.Session.Spawner.Timer + boot.Session.Sim.Packages.Count * 3f, t0, "game loop did not tick");
            LogAssert.NoUnexpectedReceived();
            Object.Destroy(boot.gameObject);
            yield return null;
        }
    }
}
