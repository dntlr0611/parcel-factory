using NUnit.Framework;

namespace ParcelFactory.Game.Tests
{
    /// <summary>
    /// 이 어셈블리의 테스트는 시작 화면 없이 바로 게임이 도는 부팅을 전제로 한다. 시작 화면은 StartScreenTests가 직접 켜서 확인한다.
    /// 테스트 러너가 Play 모드에 들어갈 때 자동 생성된 부트스트랩이 이미 시작 화면을 띄웠다면 닫는다.
    /// </summary>
    [SetUpFixture]
    public class PlayModeSetup
    {
        /// <summary>덮어쓰기 전 StartScreen.ShowOnBoot 값(제품 기본값). StartScreenTests가 true인지 확인한다.</summary>
        public static bool ProductionShowOnBoot { get; private set; }
        private static bool captured;

        [OneTimeSetUp]
        public void DisableStartScreen()
        {
            if (!captured) { ProductionShowOnBoot = StartScreen.ShowOnBoot; captured = true; }
            StartScreen.ShowOnBoot = false;
            var boot = GameBootstrap.Instance;
            if (boot != null && boot.StartScreen != null && boot.StartScreen.IsOpen) boot.StartScreen.Hide();
        }

        [OneTimeTearDown]
        public void RestoreStartScreen() => StartScreen.ShowOnBoot = true;
    }
}
