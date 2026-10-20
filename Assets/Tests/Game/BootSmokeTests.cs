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
                Assert.IsNotNull(workerAnim.GetClip("sprint"), "sprint clip missing");
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
                boot.Input.SelectSlot(1); // 재탭 → 설정 모드(= R): 팔레트는 1/2 고르기
                Assert.AreEqual(ConfigMode.PickSlot, boot.Input.ConfigMode);
                boot.Input.SelectSlot(1); // 핫바 2 = 빨강
                Assert.AreEqual(ConfigMode.None, boot.Input.ConfigMode);
                Assert.AreEqual(ParcelFactory.Core.PalletKind.Red, ParcelFactory.Core.Tool.KindOf(boot.Input.SelectedTool.Value.Direction));
                boot.Input.SelectSlot(0);
                Assert.AreEqual(ParcelFactory.Core.CellType.Conveyor, boot.Input.SelectedTool.Value.Type);
                Assert.AreEqual(0, boot.Input.SelectedSlot);
                boot.Input.SelectSlot(0);
                Assert.IsTrue(boot.Input.SelectedTool.HasValue); // re-tap enters config, does not clear
                Assert.AreEqual(ConfigMode.PickDirection, boot.Input.ConfigMode);
                boot.Input.CancelConfig();
                Assert.AreEqual(ConfigMode.None, boot.Input.ConfigMode);
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
        public IEnumerator ConfigMode_ToolAndCellTargets_StatusBars_AndMovementSuppressed()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 택배 흐름과 무관하게 칸 상태만 본다
                s.Economy.Adjust(10000);
                var w = s.Worker.Worker;
                var input = boot.Input;

                // 도구 대상: 빈 칸 (2,3)을 바라보고 레일 도구로 R → ↑. 레일 북쪽은 시작 때 미보유 → 토스트, 도구 그대로.
                w.X = 2f; w.Y = 4f; w.Facing = ParcelFactory.Core.Direction.South;
                input.SelectSlot(0);
                Assert.AreEqual(ParcelFactory.Core.Direction.East, input.SelectedTool.Value.Direction);
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                Assert.IsFalse(input.ConfigTargetIsCell);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.North);
                Assert.AreEqual("해금되지 않은 방향입니다", boot.Hud.LastToast);
                Assert.AreEqual(ParcelFactory.Core.Direction.East, input.SelectedTool.Value.Direction);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                s.Toolbar.Add(ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.North);
                input.BeginConfig();
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.North);
                Assert.AreEqual(ParcelFactory.Core.Direction.North, input.SelectedTool.Value.Direction);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                input.BeginConfig();
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.East);
                Assert.AreEqual(ParcelFactory.Core.Direction.East, input.SelectedTool.Value.Direction);
                // 이미 동쪽을 누른 채 설정 모드에 들어가면 그 방향은 입력이 아니고, 북쪽을 더 누르면 북쪽이 입력된다.
                input.SetPad(ParcelFactory.Core.Direction.East, true);
                yield return null;
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                Assert.IsFalse(input.ConfigTargetIsCell);
                yield return null;
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode, "held direction must not apply");
                input.SetPad(ParcelFactory.Core.Direction.North, true);
                yield return null;
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(ParcelFactory.Core.Direction.North, input.SelectedTool.Value.Direction, "newly added direction should apply");
                input.ClearPad();
                yield return null;
                w.X = 2f; w.Y = 4f; w.Facing = ParcelFactory.Core.Direction.South;
                // 빈손 + 빈 칸 → 대상 없음
                input.ClearTool();
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual("설정할 대상이 없습니다", boot.Hud.LastToast);

                // 셀 대상: (1,2) 레일 동쪽을 (1,3)에서 바라보고 R → ↓.
                s.Toolbar.Add(ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.South);
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(1, 2, ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.East));
                w.X = 1f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.South;
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                Assert.IsTrue(input.ConfigTargetIsCell);
                Assert.AreEqual((1, 2), input.ConfigCell);
                yield return null;
                var banner = boot.Hud.Root.Find("ConfigBanner");
                Assert.IsTrue(banner.gameObject.activeSelf, "config banner hidden");
                Assert.AreEqual(HudController.DirectionBannerText, banner.Find("Text").GetComponent<UnityEngine.UI.Text>().text);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.South);
                Assert.AreEqual(ParcelFactory.Core.Direction.South, s.Grid.Get(1, 2).Direction);
                StringAssert.Contains("방향 변경", boot.Hud.LastToast);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                input.BeginConfig();
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.West); // 레일 서쪽 미보유
                Assert.AreEqual("해금되지 않은 방향입니다", boot.Hud.LastToast);
                Assert.AreEqual(ParcelFactory.Core.Direction.South, s.Grid.Get(1, 2).Direction);
                yield return null;
                Assert.IsFalse(banner.gameObject.activeSelf, "config banner should hide after applying");

                // 설정 중 이동 억제: 화살표(D-패드)를 새로 누르면 이동 대신 방향 입력이 되고, 누른 채로도 걷지 않는다.
                input.BeginConfig();
                float x0 = w.X, y0 = w.Y;
                input.SetPad(ParcelFactory.Core.Direction.East, true);
                yield return null; yield return null; yield return null;
                Assert.AreEqual(x0, w.X, 1e-5f, "worker moved during config");
                Assert.AreEqual(y0, w.Y, 1e-5f, "worker moved during config");
                Assert.AreEqual(ParcelFactory.Core.Direction.East, s.Grid.Get(1, 2).Direction, "newly pressed direction should apply");
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                input.SetPad(ParcelFactory.Core.Direction.East, false);

                // 분배기 (2,2) 동쪽: R → 2 → ↓ = 빨간 출구 남쪽. 파랑과 같은 방향은 거절.
                Assert.AreEqual(ParcelFactory.Core.UnlockResult.Ok, s.Unlocks.TryPurchase(ParcelFactory.Core.UnlockId.Splitter));
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(2, 2, ParcelFactory.Core.CellType.Splitter, ParcelFactory.Core.Direction.East));
                w.X = 2f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.South;
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickSlot, input.ConfigMode);
                yield return null;
                Assert.AreEqual(HudController.SlotBannerText, banner.Find("Text").GetComponent<UnityEngine.UI.Text>().text);
                // PickSlot에서는 화살표를 눌러도 걷지 않고 모드도 그대로다.
                input.SetPad(ParcelFactory.Core.Direction.West, true);
                yield return null; yield return null;
                Assert.AreEqual(2f, w.X, 1e-5f, "worker moved during slot pick");
                Assert.AreEqual(ConfigMode.PickSlot, input.ConfigMode);
                input.SetPad(ParcelFactory.Core.Direction.West, false);
                input.ApplyConfigSlot(2);
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                Assert.AreEqual(ConfigWhich.Secondary, input.ConfigWhich);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.South);
                Assert.AreEqual(ParcelFactory.Core.Direction.South, s.Grid.Get(2, 2).RedDirection);
                StringAssert.Contains("빨간 출구", boot.Hud.LastToast);
                yield return null;
                // 명시 빨간 출구는 연결되지 않아도 흐린(40%) 화살표로 보인다.
                var splitterView = FindCellView("Splitter", 2, 2);
                Assert.IsNotNull(splitterView.transform.Find("RedArrow"), "explicit red arrow missing");
                Assert.Less(splitterView.transform.Find("RedArrow").GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor").a, 0.5f, "unconnected explicit red should be dimmed");
                input.BeginConfig();
                input.ApplyConfigSlot(2);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.East);
                Assert.AreEqual("파란 출구와 같은 방향입니다", boot.Hud.LastToast);
                Assert.AreEqual(ParcelFactory.Core.Direction.South, s.Grid.Get(2, 2).RedDirection);

                // 분배기 도구의 빨간 출구는 다른 종류를 고르면 버린다.
                w.X = 2f; w.Y = 5f; w.Facing = ParcelFactory.Core.Direction.North; // (2,4) 빈 칸
                int splitterSlot = input.AvailableSlots().IndexOf(ParcelFactory.Core.CellType.Splitter);
                Assert.GreaterOrEqual(splitterSlot, 0);
                input.SelectSlot(splitterSlot);
                input.BeginConfig();
                Assert.IsFalse(input.ConfigTargetIsCell);
                input.ApplyConfigSlot(2);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.South);
                Assert.AreEqual(ParcelFactory.Core.Direction.South, input.ToolSplitterRed);
                input.SelectSlot(0);
                Assert.IsNull(input.ToolSplitterRed, "red must reset when another type is selected");
                input.SelectSlot(splitterSlot);
                Assert.IsNull(input.ToolSplitterRed);
                input.BeginConfig(); input.ApplyConfigSlot(2); input.ApplyConfigDirection(ParcelFactory.Core.Direction.South);
                input.ClearTool();
                Assert.IsNull(input.ToolSplitterRed, "red must reset on ClearTool");

                // 팔레트 (5,2): R → 2 = 빨강, 곧바로 적용.
                w.X = 5f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.South;
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickSlot, input.ConfigMode);
                input.ApplyConfigSlot(2);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(ParcelFactory.Core.PalletKind.Red, s.Grid.Get(5, 2).PalletKind);
                Assert.AreEqual("팔레트 빨강", boot.Hud.LastToast);

                // 미니 상태바: 설치된 부품마다 하나, 분배기 "파 → · 빨 ↓", 바라보는 부품은 " · R 설정".
                yield return null;
                int buildings = 0;
                for (int y = 0; y < ParcelFactory.Core.FactoryGrid.Height; y++)
                    for (int x = 0; x < ParcelFactory.Core.FactoryGrid.Width; x++)
                        if (s.Grid.Get(x, y).IsBuilding) buildings++;
                Assert.AreEqual(4, buildings); // 레일, 분배기, 시작 팔레트 2개
                Assert.AreEqual(buildings, boot.StatusBars.Count);
                StringAssert.Contains("파 →", boot.StatusBars.TextAt(2, 2));
                StringAssert.Contains("빨 ↓", boot.StatusBars.TextAt(2, 2));
                Assert.AreEqual("→", boot.StatusBars.TextAt(1, 2));
                Assert.AreEqual("빨강 0/6", boot.StatusBars.TextAt(5, 4)); // 시작 빨강 팔레트
                Assert.IsTrue(boot.StatusBars.TextAt(5, 2).StartsWith("빨강 0/6"), boot.StatusBars.TextAt(5, 2));
                Assert.IsTrue(boot.StatusBars.TextAt(5, 2).EndsWith("R 설정"), boot.StatusBars.TextAt(5, 2));
                Assert.IsTrue(boot.StatusBars.IsFocused(5, 2));
                Assert.IsFalse(boot.StatusBars.IsFocused(2, 2));
                // 설치·해체로 집합이 바뀐다.
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(3, 3, ParcelFactory.Core.CellType.Table, ParcelFactory.Core.Direction.East));
                yield return null;
                Assert.AreEqual(5, boot.StatusBars.Count);
                Assert.AreEqual("검사대", boot.StatusBars.TextAt(3, 3));

                // 터치: R 버튼 = 설정 모드, D-패드 누름 = 방향 1회.
                boot.Hud.ForceTouchMode(true);
                yield return null;
                w.X = 1f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.South;
                GameObject.Find("TouchR").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                var padN = GameObject.Find("TouchPadN");
                var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                UnityEngine.EventSystems.ExecuteEvents.Execute(padN, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                Assert.AreEqual(ParcelFactory.Core.Direction.North, s.Grid.Get(1, 2).Direction);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                UnityEngine.EventSystems.ExecuteEvents.Execute(padN, ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                boot.Hud.ForceTouchMode(false);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        private static int ChildCount(GameObject view, string name)
        {
            int n = 0;
            foreach (Transform t in view.transform) if (t.name == name) n++;
            return n;
        }

        /// <summary>화살표가 분배기 뷰 안에서 가리키는 방향(xz).</summary>
        private static Vector3 Pointing(Transform arrow) => arrow.localRotation * Vector3.forward;

        [UnityTest]
        public IEnumerator SplitterView_ShowsBlueAndRedArrows_AndUpdatesWithNeighbours()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Economy.Adjust(10000);
                Assert.AreEqual(ParcelFactory.Core.UnlockResult.Ok, s.Unlocks.TryPurchase(ParcelFactory.Core.UnlockId.Splitter));
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(3, 4, ParcelFactory.Core.CellType.Splitter, ParcelFactory.Core.Direction.East));
                yield return null;
                var view = FindCellView("Splitter", 3, 4);
                Assert.IsNotNull(view, "splitter view not created");
                Assert.IsNotNull(view.transform.Find("SplitterHub"), "splitter hub missing");
                Assert.IsNotNull(view.transform.Find("SplitterPlate"), "splitter top plate missing");
                // 연결된 출구가 없어도 파란 화살표(설정)는 흐리게 보이고, 빨간 화살표는 없다.
                Assert.AreEqual(1, ChildCount(view, "BlueArrow"));
                Assert.AreEqual(0, ChildCount(view, "RedArrow"));
                Assert.Less(view.transform.Find("BlueArrow").GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor").a, 0.5f, "unconnected blue arrow should be dimmed");

                // 동(4,4)·남(3,3)에 레일 → 이웃 변경으로 다시 그려져 파랑 +x, 빨강 −z.
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(4, 4, ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.East));
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(3, 3, ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.East));
                yield return null;
                view = FindCellView("Splitter", 3, 4);
                var blue = view.transform.Find("BlueArrow");
                var red = view.transform.Find("RedArrow");
                Assert.IsNotNull(blue, "blue arrow missing");
                Assert.IsNotNull(red, "red arrow missing");
                Assert.AreEqual(1, ChildCount(view, "BlueArrow"));
                Assert.AreEqual(1, ChildCount(view, "RedArrow"));
                Assert.Greater(Vector3.Dot(Pointing(blue), Vector3.right), 0.99f, "blue arrow should point +x");
                Assert.Greater(blue.localPosition.x, 0.29f);
                Assert.Greater(Vector3.Dot(Pointing(red), Vector3.back), 0.99f, "red arrow should point -z");
                Assert.Less(red.localPosition.z, -0.29f);
                Assert.AreEqual(1f, blue.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor").a, 1e-3f, "connected blue arrow is opaque");

                // 남쪽 레일 해체 완료(2초) → 빨간 화살표 사라짐, 파란 화살표는 남는다.
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.BeginDemolish(3, 3));
                s.Build.Tick(ParcelFactory.Core.BuildService.DemolishSeconds + 0.1f);
                Assert.AreEqual(ParcelFactory.Core.CellType.Empty, s.Grid.Get(3, 3).Type);
                yield return null;
                view = FindCellView("Splitter", 3, 4);
                Assert.AreEqual(0, ChildCount(view, "RedArrow"));
                Assert.AreEqual(1, ChildCount(view, "BlueArrow"));
                Assert.AreEqual(0, ChildCount(view, "Chevron"), "no leftover yellow chevrons");
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
                foreach (var name in new[] { "TouchE", "TouchX", "TouchR", "TouchRun", "TouchPadN", "TouchPadS", "TouchPadW", "TouchPadE" })
                {
                    var go = GameObject.Find(name);
                    Assert.IsNotNull(go, name + " missing");
                    var rt = go.GetComponent<RectTransform>();
                    Assert.GreaterOrEqual(Mathf.Min(rt.rect.width, rt.rect.height), 44f, name + " smaller than 44 px");
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    foreach (var c in corners)
                        Assert.IsTrue(c.x >= min.x - 0.01f && c.x <= max.x + 0.01f && c.y >= min.y - 0.01f && c.y <= max.y + 0.01f,
                            name + " corner " + c + " outside canvas " + min + ".." + max);
                }
                // 달리기 버튼: 누르는 동안만 달리기 요청, 떼거나 밖으로 나가면 해제.
                var runBtn = GameObject.Find("TouchRun");
                var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                Assert.IsFalse(boot.Input.RunRequested);
                UnityEngine.EventSystems.ExecuteEvents.Execute(runBtn, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                Assert.IsTrue(boot.Input.RunRequested, "PointerDown should request running");
                UnityEngine.EventSystems.ExecuteEvents.Execute(runBtn, ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                Assert.IsFalse(boot.Input.RunRequested, "PointerUp should release running");
                UnityEngine.EventSystems.ExecuteEvents.Execute(runBtn, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(runBtn, ped, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                Assert.IsFalse(boot.Input.RunRequested, "PointerExit should release running");
                boot.Input.SetRun(true);
                boot.Input.ClearPad();
                Assert.IsFalse(boot.Input.RunRequested, "ClearPad should release running");
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
        public IEnumerator HandDelivery_Uninspected_ShowsPenaltyPromptAndToast()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                // 입고구에서 택배 하나를 집어 든다(이미 입고된 택배가 있으면 그것을 쓴다).
                if (!s.Sim.TrySpawn(ParcelFactory.Core.FactoryGrid.InletX, ParcelFactory.Core.FactoryGrid.InletY, ParcelFactory.Core.PackageKind.Normal, out var p))
                    p = s.Sim.PackageAt(ParcelFactory.Core.FactoryGrid.InletX, ParcelFactory.Core.FactoryGrid.InletY);
                Assert.IsNotNull(p);
                Assert.IsFalse(p.Inspected);
                s.Sim.Take(p);
                s.Worker.Worker.Carrying = p;
                s.Worker.Worker.X = 10f; s.Worker.Worker.Y = 2f; s.Worker.Worker.Facing = ParcelFactory.Core.Direction.East;
                Assert.AreEqual((11, 2), s.Worker.FacingTile());
                yield return null;
                var promptText = boot.Hud.Root.Find("Prompt/Text").GetComponent<UnityEngine.UI.Text>();
                StringAssert.Contains("출고 (미검사 −10)", promptText.text);
                // 프롬프트는 숨은 불량 여부를 드러내지 않는다: 검사 판정이 옳다고 가정한 금액만 보인다.
                Assert.AreEqual("출고 (미검사 −10)", HudController.DeliverLabel(true, false, false));
                Assert.AreEqual("반품 (미검사 −5)", HudController.DeliverLabel(false, false, false));
                Assert.AreEqual("출고 +30", HudController.DeliverLabel(true, true, false));
                Assert.AreEqual("반품 (승인한 택배 −5)", HudController.DeliverLabel(false, true, false));
                Assert.AreEqual("반품 +10", HudController.DeliverLabel(false, true, true));
                Assert.AreEqual("출고 (거부한 택배 −10)", HudController.DeliverLabel(true, true, true));
                int money0 = s.Economy.Money;
                boot.Input.Interact();
                Assert.IsNull(s.Worker.Worker.Carrying);
                Assert.AreEqual(money0 - 10, s.Economy.Money);
                StringAssert.Contains("검사대를 거치지 않은", boot.Hud.LastToast);
                // 세션을 바꾸면 새 세션의 배송도 토스트가 뜬다(이전 구독은 끊김).
                boot.StartNewGame();
                yield return null;
                var s2 = boot.Session;
                s2.Paused = true;
                var q = new ParcelFactory.Core.PackageFactory(new System.Random(3)).Create(s2.Sim.NextId++, 10, 2, ParcelFactory.Core.Direction.East);
                q.Bad = false; q.Inspect(false);
                s2.Sim.DeliverCarried(q, ParcelFactory.Core.CellType.Outlet);
                Assert.AreEqual("정상 출고 +30", boot.Hud.LastToast);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Money_ShownAtPointOfAction_PromptPopupsAndFlash()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 게임 루프가 해체·택배를 진행시키지 않게 한다
                var promptText = boot.Hud.Root.Find("Prompt/Text").GetComponent<UnityEngine.UI.Text>();
                var moneyText = boot.Hud.Root.Find("TopLeft/Money").GetComponent<UnityEngine.UI.Text>();
                // 빈 칸 (1,2)를 레일 도구로 바라본다: 가격이 프롬프트에 보인다.
                s.Worker.Worker.X = 1f; s.Worker.Worker.Y = 3f; s.Worker.Worker.Facing = ParcelFactory.Core.Direction.South;
                Assert.AreEqual((1, 2), s.Worker.FacingTile());
                Assert.AreEqual(ParcelFactory.Core.CellType.Empty, s.Grid.Get(1, 2).Type);
                boot.Input.SelectSlot(0);
                Assert.AreEqual(ParcelFactory.Core.CellType.Conveyor, boot.Input.SelectedTool.Value.Type);
                yield return null;
                Assert.IsTrue(promptText.gameObject.activeInHierarchy, "placement prompt hidden");
                StringAssert.Contains("₩10", promptText.text);
                StringAssert.Contains("레일 설치 · ₩10", promptText.text);
                Assert.AreEqual("파랑 팔레트 설치 · ₩20", HudController.PlaceLabel(new ParcelFactory.Core.Tool(ParcelFactory.Core.CellType.Pallet, ParcelFactory.Core.Direction.East), true));
                Assert.AreEqual("분배기 설치 (파랑 →) · ₩60", HudController.PlaceLabel(new ParcelFactory.Core.Tool(ParcelFactory.Core.CellType.Splitter, ParcelFactory.Core.Direction.East), true));
                Assert.AreEqual("분배기 설치 (파랑 ↓) · ₩60 (잔액 부족)", HudController.PlaceLabel(new ParcelFactory.Core.Tool(ParcelFactory.Core.CellType.Splitter, ParcelFactory.Core.Direction.South), false));

                // 잔액이 모자라도 프롬프트는 뜨고 빨갛게 "(잔액 부족)".
                int keep = s.Economy.Money;
                s.Economy.Adjust(5 - keep);
                yield return null;
                Assert.IsTrue(promptText.gameObject.activeInHierarchy, "prompt must stay visible when unaffordable");
                StringAssert.Contains("레일 설치 · ₩10 (잔액 부족)", promptText.text);
                Assert.AreEqual(UiKit.Bad, promptText.color);
                Assert.AreEqual(UiKit.Bad, moneyText.color, "balance should flash red after a decrease");
                s.Economy.Adjust(keep - 5);
                yield return null;
                Assert.AreEqual(UiKit.Good, moneyText.color, "balance should flash green after an increase");
                StringAssert.Contains("레일 설치 · ₩10   [E]", promptText.text);
                Assert.AreEqual(UiKit.Fg, promptText.color);

                // 설치하면 그 칸에 빨간 −10 팝업.
                Assert.IsNull(GameObject.Find("MoneyPopup"), "no popup before acting");
                boot.Input.Interact();
                Assert.AreEqual(ParcelFactory.Core.CellType.Conveyor, s.Grid.Get(1, 2).Type);
                var popup = GameObject.Find("MoneyPopup");
                Assert.IsNotNull(popup, "MoneyPopup not shown after placing");
                Assert.IsTrue(popup.activeInHierarchy);
                Assert.AreEqual("−10", popup.GetComponent<UnityEngine.UI.Text>().text);

                // 해체가 끝나면 그 칸에 초록 +8 팝업(CellChanged로 귀속).
                Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.BeginDemolish(1, 2));
                s.Build.Tick(ParcelFactory.Core.BuildService.DemolishSeconds + 0.1f);
                Assert.AreEqual(ParcelFactory.Core.CellType.Empty, s.Grid.Get(1, 2).Type);
                bool sawRefund = false;
                foreach (var t in boot.Hud.Root.GetComponentsInChildren<UnityEngine.UI.Text>())
                    if (t.name == "MoneyPopup" && t.text == "+8") sawRefund = true;
                Assert.IsTrue(sawRefund, "refund popup +8 missing");

                // 배송 팝업: +30 초록.
                var q = new ParcelFactory.Core.PackageFactory(new System.Random(3)).Create(s.Sim.NextId++, 10, 2, ParcelFactory.Core.Direction.East);
                q.Bad = false; q.Inspect(false);
                s.Sim.DeliverCarried(q, ParcelFactory.Core.CellType.Outlet);
                bool sawDelivery = false;
                foreach (var t in boot.Hud.Root.GetComponentsInChildren<UnityEngine.UI.Text>())
                    if (t.name == "MoneyPopup" && t.text == "+30") sawDelivery = true;
                Assert.IsTrue(sawDelivery, "delivery popup +30 missing");

                // 승인했지만 실제로는 불량인 택배: 프롬프트는 "출고 +30"(불량을 드러내지 않음), 지급·토스트는 진짜 결과 −30.
                var bad = new ParcelFactory.Core.PackageFactory(new System.Random(4)).Create(s.Sim.NextId++, 10, 2, ParcelFactory.Core.Direction.East);
                bad.Bad = true; bad.Inspect(false);
                s.Worker.Worker.Carrying = bad;
                s.Worker.Worker.X = 10f; s.Worker.Worker.Y = 2f; s.Worker.Worker.Facing = ParcelFactory.Core.Direction.East;
                yield return null;
                StringAssert.Contains("출고 +30", promptText.text);
                int before = s.Economy.Money;
                boot.Input.Interact();
                Assert.AreEqual(before - 30, s.Economy.Money);
                Assert.AreEqual("불량을 승인해 출고 −30", boot.Hud.LastToast);

                // 0.9초 뒤 모두 사라진다.
                yield return new WaitForSecondsRealtime(HudController.PopupSeconds + 0.2f);
                Assert.IsNull(GameObject.Find("MoneyPopup"), "popups should expire");
                Assert.AreEqual(UiKit.Accent, moneyText.color, "flash should end");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Clock_Lunch_DaySummary_NextDay_AndStaminaBar()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                var clockText = boot.Hud.Root.Find("Clock/Text").GetComponent<UnityEngine.UI.Text>();
                var badge = boot.Hud.Root.Find("Clock/Badge");
                Assert.AreEqual("1일차 09:00", clockText.text);
                Assert.IsFalse(badge.gameObject.activeSelf, "no badge while working");

                // 12:05 → 점심 배지.
                s.Clock.Set(1, 12 * 60 + 5);
                yield return null;
                Assert.IsTrue(badge.gameObject.activeSelf, "lunch badge should show");
                Assert.AreEqual(HudController.LunchBadgeText, badge.Find("Text").GetComponent<UnityEngine.UI.Text>().text);
                StringAssert.StartsWith("1일차 12:0", clockText.text);

                // 17:59:30에서 1초 진행 → 18:00 퇴근, DayEnded → 요약 패널(모달).
                var summary = boot.Hud.Root.Find("DaySummary");
                Assert.IsNotNull(summary, "day summary panel missing");
                Assert.IsFalse(summary.gameObject.activeSelf);
                s.Economy.Earn(40);
                s.Progression.AddExp(12);
                s.Clock.Set(1, 1079.5f);
                s.Tick(1f);
                Assert.IsFalse(s.Clock.IsOpen);
                Assert.IsTrue(summary.gameObject.activeSelf, "summary should open on DayEnded");
                Assert.IsTrue(boot.Panels.SummaryOpen);
                Assert.IsTrue(boot.Input.UiBlocksInput, "summary must block input in the same frame");
                Assert.AreEqual("1일차 마감", summary.Find("Card/Title").GetComponent<UnityEngine.UI.Text>().text);
                StringAssert.Contains("+40", summary.Find("Card/Money").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("획득 EXP", summary.Find("Card/ExpL").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("12", summary.Find("Card/Exp").GetComponent<UnityEngine.UI.Text>().text);
                yield return null;
                Assert.AreEqual(HudController.ClosedBadgeText, badge.Find("Text").GetComponent<UnityEngine.UI.Text>().text);
                // Escape(CloseAll)로는 닫히지 않는다.
                boot.Panels.CloseAll();
                yield return null;
                Assert.IsTrue(summary.gameObject.activeSelf, "summary must ignore Escape");
                Assert.IsTrue(boot.Input.UiBlocksInput);

                // "다음 날" → 2일차 09:00, 체력 가득, 패널 닫힘.
                s.Worker.Worker.Stamina = 10f;
                summary.Find("Card/NextDay").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.IsFalse(summary.gameObject.activeSelf, "summary should close");
                Assert.AreEqual(2, s.Clock.Day);
                Assert.IsTrue(s.Clock.IsOpen);
                Assert.AreEqual(100f, s.Worker.Worker.Stamina);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                yield return null;
                Assert.AreEqual("2일차 09:00", clockText.text);
                Assert.IsFalse(badge.gameObject.activeSelf);

                // 체력 바: 40이면 폭 56, 노랑. 15면 빨강.
                s.Paused = true;
                var fill = boot.Hud.Root.Find("Stamina/Bar/Fill").GetComponent<UnityEngine.UI.Image>();
                Assert.AreEqual(HudController.StaminaBarWidth, fill.rectTransform.sizeDelta.x, 0.01f);
                s.Worker.Worker.Stamina = 40f;
                yield return null;
                Assert.AreEqual(HudController.StaminaBarWidth * 0.4f, fill.rectTransform.sizeDelta.x, 0.01f);
                Assert.AreEqual(UiKit.Warn, fill.color);
                Assert.AreEqual(UiKit.Good, HudController.StaminaColor(51f));
                Assert.AreEqual(UiKit.Warn, HudController.StaminaColor(21f));
                Assert.AreEqual(UiKit.Bad, HudController.StaminaColor(20f));

                // 지친 상태로 택배를 바라보면 빨간 "집기 (지침)", E는 토스트.
                s.Worker.Worker.Stamina = 0f;
                s.Sim.TrySpawn(ParcelFactory.Core.FactoryGrid.InletX, ParcelFactory.Core.FactoryGrid.InletY, ParcelFactory.Core.PackageKind.Normal, out _);
                Assert.IsNotNull(s.Sim.PackageAt(ParcelFactory.Core.FactoryGrid.InletX, ParcelFactory.Core.FactoryGrid.InletY), "need a package at the inlet");
                s.Worker.Worker.X = ParcelFactory.Core.FactoryGrid.InletX + 1; s.Worker.Worker.Y = ParcelFactory.Core.FactoryGrid.InletY; s.Worker.Worker.Facing = ParcelFactory.Core.Direction.West;
                yield return null;
                var promptText = boot.Hud.Root.Find("Prompt/Text").GetComponent<UnityEngine.UI.Text>();
                Assert.AreEqual(HudController.ExhaustedPrompt, promptText.text);
                Assert.AreEqual(UiKit.Bad, promptText.color);
                boot.Input.Interact();
                Assert.AreEqual(HudController.ExhaustedToast, boot.Hud.LastToast);
                Assert.IsNull(s.Worker.Worker.Carrying);

                // 퇴근 상태로 저장된 세션을 불러오면 요약이 바로 뜬다.
                var closed = new ParcelFactory.Core.GameSession();
                closed.Clock.Set(3, ParcelFactory.Core.Clock.CloseMinute);
                boot.ReplaceSession(closed);
                Assert.IsTrue(summary.gameObject.activeSelf, "loaded closed session should show summary");
                Assert.AreEqual("3일차 마감", summary.Find("Card/Title").GetComponent<UnityEngine.UI.Text>().text);
                Assert.IsTrue(boot.Input.UiBlocksInput);
                summary.Find("Card/NextDay").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.AreEqual(4, closed.Clock.Day);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        /// <summary>(1,3)에서 동쪽 패드를 10프레임 누른다. 결과: [이동 거리, 체력 소모, 누르는 중 IsRunning(1/0)].</summary>
        private static IEnumerator Stride(GameBootstrap boot, bool run, float[] result)
        {
            var s = boot.Session;
            var w = s.Worker.Worker;
            w.X = 1f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.South; w.Stamina = 100f;
            boot.Input.SetRun(run);
            boot.Input.SetPad(ParcelFactory.Core.Direction.East, true);
            for (int i = 0; i < 10; i++) yield return null;
            result[0] = w.X - 1f;
            result[1] = 100f - w.Stamina;
            result[2] = s.Worker.IsRunning ? 1f : 0f;
            boot.Input.ClearPad();
            yield return null; // 멈춘 프레임: 달리기 즉시 해제
            Assert.IsFalse(s.Worker.IsRunning, "IsRunning must clear when the worker stops");
        }

        [UnityTest]
        public IEnumerator Running_MovesFartherAndDrainsMore_AndNeedsStamina()
        {
            var boot = GameBootstrap.Ensure();
            Time.captureDeltaTime = 1f / 30f; // 걷기·달리기 구간이 같은 시간(10프레임)이 되게 프레임 시간을 고정한다
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 회복·시계·택배를 멈춘다(일시정지 중에도 걸을 수 있다)
                Assert.AreEqual(ParcelFactory.Core.CellType.Empty, s.Grid.Get(2, 3).Type);
                Assert.AreEqual(ParcelFactory.Core.CellType.Empty, s.Grid.Get(3, 3).Type);

                var walk = new float[3];
                yield return Stride(boot, false, walk);
                Assert.Greater(walk[0], 0.5f, "walk did not move");
                Assert.AreEqual(0f, walk[2], "walking must not report IsRunning");

                var run = new float[3];
                var statusText = boot.Hud.Root.Find("Status/Text").GetComponent<UnityEngine.UI.Text>();
                yield return Stride(boot, true, run); // Stride가 위치·체력을 다시 맞춘다
                Assert.AreEqual(1f, run[2], "worker should be running while Space/touch run is held");
                Assert.Greater(run[0], walk[0] * 1.3f, "running should move farther (" + run[0] + " vs " + walk[0] + ")");
                Assert.Greater(run[1], walk[1] * 2f, "running should drain more stamina (" + run[1] + " vs " + walk[1] + ")");

                // 상태줄 접두어: 달리는 동안 "달리는 중 · ".
                var w = s.Worker.Worker;
                w.X = 1f; w.Y = 3f; w.Stamina = 100f;
                boot.Input.SetRun(true);
                boot.Input.SetPad(ParcelFactory.Core.Direction.East, true);
                yield return null;
                yield return null;
                Assert.IsTrue(s.Worker.IsRunning);
                StringAssert.StartsWith(HudController.RunningPrefix, statusText.text);
                boot.Input.ClearPad();
                yield return null;
                yield return null;
                Assert.IsFalse(statusText.text.StartsWith(HudController.RunningPrefix), "prefix should clear after stopping");

                // 체력 5(< RunMinStamina 10): 달리기를 눌러도 걷는다.
                w.X = 1f; w.Y = 3f; w.Stamina = 5f;
                boot.Input.SetRun(true);
                boot.Input.SetPad(ParcelFactory.Core.Direction.East, true);
                Assert.IsTrue(boot.Input.RunRequested);
                yield return null;
                yield return null;
                Assert.Greater(w.X, 1f, "worker should still walk when tired");
                Assert.IsFalse(s.Worker.IsRunning, "cannot run below RunMinStamina");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                boot.Input.ClearPad();
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Exp_Label_StatusPanel_Upgrade_AndStaminaBarMax()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 게임 루프가 택배·회복을 진행시키지 않게 한다
                var expText = boot.Hud.Root.Find("TopLeft/Exp").GetComponent<UnityEngine.UI.Text>();
                Assert.AreEqual("EXP 0", expText.text);

                // 검사한 정상 택배를 손으로 출고 → EXP +10, 라벨 갱신·초록 깜빡임.
                var q = new ParcelFactory.Core.PackageFactory(new System.Random(3)).Create(s.Sim.NextId++, 10, 2, ParcelFactory.Core.Direction.East);
                q.Bad = false; q.Inspect(false);
                s.Worker.Worker.Carrying = q;
                s.Worker.Worker.X = 10f; s.Worker.Worker.Y = 2f; s.Worker.Worker.Facing = ParcelFactory.Core.Direction.East;
                boot.Input.Interact();
                Assert.IsNull(s.Worker.Worker.Carrying);
                yield return null;
                Assert.AreEqual("EXP 10", expText.text);
                Assert.AreEqual(UiKit.Good, expText.color, "EXP should flash green after an increase");

                // 상태창(C 키와 같은 경로): 열리면 입력 차단.
                var status = boot.Hud.Root.Find("WorkerStatus");
                Assert.IsNotNull(status, "status panel missing");
                Assert.IsFalse(status.gameObject.activeSelf);
                boot.Panels.ToggleStatus();
                Assert.IsTrue(status.gameObject.activeSelf, "status panel should open");
                Assert.IsTrue(boot.Panels.StatusIsOnlyOpen);
                Assert.IsTrue(boot.Input.UiBlocksInput, "status panel must block input");
                Assert.AreEqual("EXP 10", status.Find("Exp").GetComponent<UnityEngine.UI.Text>().text);
                var row = status.Find("List/Row_Stamina");
                var up = row.Find("Upgrade").GetComponent<UnityEngine.UI.Button>();
                Assert.IsFalse(up.interactable, "10 EXP cannot buy a 40 EXP upgrade");
                Assert.AreEqual("강화 40 EXP", up.GetComponentInChildren<UnityEngine.UI.Text>().text);
                Assert.AreEqual("Lv 0/5", row.Find("Level").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("최대 체력 100", row.Find("Effect").GetComponent<UnityEngine.UI.Text>().text);

                // EXP 100 추가 → 패널 갱신, 체력 강화 클릭 → 레벨 1, EXP −40, 토스트.
                s.Progression.AddExp(100);
                int exp0 = s.Progression.Exp;
                Assert.IsTrue(up.interactable, "panel should refresh on ExpChanged");
                up.onClick.Invoke();
                Assert.AreEqual(1, s.Progression.Level(ParcelFactory.Core.Stat.Stamina));
                Assert.AreEqual(exp0 - 40, s.Progression.Exp);
                StringAssert.Contains("체력 강화", boot.Hud.LastToast);
                Assert.AreEqual("체력 강화! 최대 체력 120", boot.Hud.LastToast);
                Assert.AreEqual("Lv 1/5", row.Find("Level").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("최대 체력 120", row.Find("Effect").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("강화 80 EXP", up.GetComponentInChildren<UnityEngine.UI.Text>().text);
                Assert.AreEqual("EXP " + s.Progression.Exp, status.Find("Exp").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("속도 +0%", status.Find("List/Row_Speed/Effect").GetComponent<UnityEngine.UI.Text>().text);
                Assert.AreEqual("Lv 0/3", status.Find("List/Row_Regen/Level").GetComponent<UnityEngine.UI.Text>().text);
                // 70 EXP로는 80짜리 강화를 못 한다.
                Assert.IsFalse(up.interactable);
                up.onClick.Invoke();
                Assert.AreEqual("EXP가 부족합니다", boot.Hud.LastToast);
                Assert.AreEqual(1, s.Progression.Level(ParcelFactory.Core.Stat.Stamina));
                Assert.AreEqual("이동 속도 강화! 속도 +8%", Panels.UpgradeToast(Speed1(), ParcelFactory.Core.Stat.Speed));
                Assert.AreEqual("회복력 강화! 회복 +50%", Panels.UpgradeToast(Regen1(), ParcelFactory.Core.Stat.Regen));
                yield return null;
                Assert.AreEqual("EXP " + s.Progression.Exp, expText.text);
                Assert.AreEqual(UiKit.Bad, expText.color, "EXP should flash red after spending");

                // 체력 바: 최대 120 기준. 60이면 폭 70.
                var fill = boot.Hud.Root.Find("Stamina/Bar/Fill").GetComponent<UnityEngine.UI.Image>();
                Assert.AreEqual(120f, s.Worker.EffectiveMaxStamina);
                s.Worker.Worker.Stamina = 60f;
                yield return null;
                Assert.AreEqual(70f, fill.rectTransform.sizeDelta.x, 0.01f);

                // 상점과 상태창은 동시에 열리지 않는다.
                boot.Input.RequestShop();
                Assert.IsTrue(boot.Panels.ShopIsOnlyOpen, "shop should replace the status panel");
                Assert.IsFalse(status.gameObject.activeSelf);
                boot.Input.RequestStatus();
                Assert.IsTrue(boot.Panels.StatusIsOnlyOpen, "status panel should replace the shop");
                Assert.IsFalse(boot.Hud.Root.Find("Shop").gameObject.activeSelf);

                // 닫기 → 차단 해제.
                status.Find("Close").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Assert.IsFalse(status.gameObject.activeSelf);
                Assert.IsFalse(boot.Panels.AnyOpen);
                Assert.IsFalse(boot.Input.UiBlocksInput);
                // 다시 토글하면 열리고, 한 번 더 토글하면 닫힌다(C 키 규칙).
                boot.Panels.ToggleStatus();
                Assert.IsTrue(boot.Input.UiBlocksInput);
                boot.Panels.ToggleStatus();
                Assert.IsFalse(boot.Input.UiBlocksInput);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        private static ParcelFactory.Core.Progression Speed1() { var p = new ParcelFactory.Core.Progression(); p.Restore(0, 0, 1, 0); return p; }
        private static ParcelFactory.Core.Progression Regen1() { var p = new ParcelFactory.Core.Progression(); p.Restore(0, 0, 0, 1); return p; }

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

        [UnityTest]
        public IEnumerator Escape_Priority_ConfigThenPanelsThenTool_AndHotbarKept()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true;
                var w = s.Worker.Worker;
                var input = boot.Input;
                w.X = 2f; w.Y = 4f; w.Facing = ParcelFactory.Core.Direction.South; // (2,3) 빈 칸

                // 설정 모드에서 화살표를 입력해도 핫바 선택은 그대로, 설정 모드는 끝난다.
                input.SelectSlot(0);
                Assert.AreEqual(0, input.SelectedSlot);
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.East);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(0, input.SelectedSlot);
                input.BeginConfig();
                input.CancelConfig();
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(0, input.SelectedSlot);

                // 설정 모드 중 다른 슬롯 숫자 = 취소 + 그 슬롯 선택. 같은 슬롯 숫자 = 취소만(다시 설정 모드에 들어가지 않음).
                input.SelectSlot(0); // 재탭 → 설정 모드
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                input.SelectSlot(1);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(1, input.SelectedSlot, "digit in config mode must select that slot");
                input.SelectSlot(0);
                Assert.AreEqual(0, input.SelectedSlot);
                input.SelectSlot(0); // 재탭 → 설정 모드
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                input.SelectSlot(0); // 같은 슬롯 → 취소만
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(0, input.SelectedSlot);

                // PickSlot(분배기 도구)에서 1·2는 파랑/빨강 선택이고 슬롯은 바뀌지 않는다.
                s.Economy.Adjust(10000);
                Assert.AreEqual(ParcelFactory.Core.UnlockResult.Ok, s.Unlocks.TryPurchase(ParcelFactory.Core.UnlockId.Splitter));
                int splitterSlot = input.AvailableSlots().IndexOf(ParcelFactory.Core.CellType.Splitter);
                input.SelectSlot(splitterSlot);
                input.BeginConfig();
                Assert.AreEqual(ConfigMode.PickSlot, input.ConfigMode);
                Assert.IsFalse(input.ConfigTargetIsCell);
                input.SelectSlot(0);
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                Assert.AreEqual(ConfigWhich.Primary, input.ConfigWhich);
                Assert.AreEqual(splitterSlot, input.SelectedSlot, "1 in PickSlot is the blue choice, not a slot change");
                input.CancelConfig();
                input.SelectSlot(0);
                Assert.AreEqual(0, input.SelectedSlot);

                // ① 설정 모드 중 Esc = 취소만(도구 유지)
                input.BeginConfig();
                input.PressEscape();
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                Assert.AreEqual(0, input.SelectedSlot, "Esc in config mode must keep the tool");

                // ② 패널이 열려 있으면 Esc = 패널 닫기(도구 유지)
                boot.Panels.ToggleStatus();
                Assert.IsTrue(boot.Panels.AnyOpen);
                input.PressEscape();
                Assert.IsFalse(boot.Panels.AnyOpen, "Esc should close the panel");
                Assert.IsFalse(input.UiBlocksInput);
                Assert.AreEqual(0, input.SelectedSlot, "Esc closing a panel must keep the tool");

                // ③ 아무것도 열려 있지 않으면 Esc = 빈손
                input.PressEscape();
                Assert.IsNull(input.SelectedTool);
                Assert.AreEqual(-1, input.SelectedSlot);
                Assert.AreEqual(InputController.EmptyHandMessage, boot.Hud.LastToast);
                input.PressEscape(); // 이미 빈손이면 아무 일도 없다
                Assert.IsNull(input.SelectedTool);
                Assert.AreEqual(ConfigMode.None, input.ConfigMode);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlacementGhost_TintFollowsPreview_HidesWithoutTool_AndShowsSplitterRed()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 입고구에 택배가 생기지 않게
                s.Economy.Adjust(10000);
                var w = s.Worker.Worker;
                var input = boot.Input;
                var ghost = boot.Ghost;
                Assert.IsNotNull(ghost, "boot.Ghost missing");
                yield return null;
                Assert.IsFalse(ghost.Visible, "no tool → no ghost");

                // 레일 도구로 빈 칸 (2,3) → 초록
                w.X = 2f; w.Y = 4f; w.Facing = ParcelFactory.Core.Direction.South;
                input.SelectSlot(0);
                yield return null;
                Assert.IsTrue(ghost.Visible, "ghost should show on an empty unlocked tile");
                Assert.GreaterOrEqual(ghost.Model.GetComponentsInChildren<Renderer>().Length, 1);
                Assert.IsTrue(ghost.Placeable);
                Assert.AreEqual(PlacementGhost.GreenTint, ghost.Tint);
                Assert.AreEqual(Grid3D.Tile(2, 3).x, ghost.Model.transform.position.x, 1e-3f);
                Assert.AreEqual(Grid3D.Tile(2, 3).z, ghost.Model.transform.position.z, 1e-3f);
                foreach (var r in ghost.Model.GetComponentsInChildren<Renderer>())
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name + " casts shadows");
                int rebuilds = ghost.Rebuilds;
                yield return null; yield return null;
                Assert.AreEqual(rebuilds, ghost.Rebuilds, "ghost must not rebuild when nothing changed");
                Assert.AreEqual(0, ghost.Model.GetComponentsInChildren<Collider>().Length, "ghost has colliders");

                // 입고구(점유) → 빨강
                w.X = 1f; w.Y = 2f; w.Facing = ParcelFactory.Core.Direction.West;
                yield return null;
                Assert.IsTrue(ghost.Visible);
                Assert.IsFalse(ghost.Placeable);
                Assert.AreEqual(PlacementGhost.RedTint, ghost.Tint);

                // 격자 밖 → 숨김
                w.X = 0f; w.Y = 4f; w.Facing = ParcelFactory.Core.Direction.West;
                yield return null;
                Assert.IsFalse(ghost.Visible, "out of bounds → hidden");

                // 택배를 들고 있으면 숨김
                w.X = 2f; w.Y = 4f; w.Facing = ParcelFactory.Core.Direction.South;
                w.Carrying = new ParcelFactory.Core.PackageFactory(new System.Random(1)).Create(9100, 2, 4, ParcelFactory.Core.Direction.East);
                yield return null;
                Assert.IsFalse(ghost.Visible, "carrying → hidden");
                w.Carrying = null;
                yield return null;
                Assert.IsTrue(ghost.Visible);

                // 빈손 → 숨김
                input.ClearTool();
                yield return null;
                Assert.IsFalse(ghost.Visible, "ClearTool → hidden");

                // 분배기 도구 + 빨간 출구 → 파란·빨간 화살표
                Assert.AreEqual(ParcelFactory.Core.UnlockResult.Ok, s.Unlocks.TryPurchase(ParcelFactory.Core.UnlockId.Splitter));
                int splitterSlot = input.AvailableSlots().IndexOf(ParcelFactory.Core.CellType.Splitter);
                input.SelectSlot(splitterSlot);
                var blue = input.SelectedTool.Value.Direction;
                var red = blue == ParcelFactory.Core.Direction.South ? ParcelFactory.Core.Direction.North : ParcelFactory.Core.Direction.South;
                input.BeginConfig();
                Assert.IsFalse(input.ConfigTargetIsCell);
                input.ApplyConfigSlot(2);
                yield return null;
                Assert.IsTrue(ghost.Visible, "ghost stays visible during config");
                input.ApplyConfigDirection(red);
                Assert.AreEqual(red, input.ToolSplitterRed);
                yield return null;
                Assert.IsTrue(ghost.Visible);
                Assert.IsNotNull(ghost.Model.transform.Find("BlueArrow"), "ghost BlueArrow missing");
                Assert.IsNotNull(ghost.Model.transform.Find("RedArrow"), "ghost RedArrow missing");
                Assert.AreEqual(Pointing(ghost.Model.transform.Find("RedArrow")).z, Grid3D.Forward(red).z, 1e-3f);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        [UnityTest]
        public IEnumerator Bridge_Crossing_LanesAtTwoHeights_SevenSlots_StatusBar_StaminaTick_AndShopRow()
        {
            var boot = GameBootstrap.Ensure();
            try
            {
                yield return null;
                var s = boot.Session;
                s.Paused = true; // 입고구에 택배가 생기지 않게. 시뮬레이션은 아래에서 직접 돌린다
                s.Economy.Adjust(10000);
                var input = boot.Input;
                var w = s.Worker.Worker;
                // 7종 모두 보유: 레일, 고속, 브릿지, 팔레트, 검사대, 분배기, 자동 검사기(Grant는 선행 부지까지 적용)
                s.Unlocks.Grant(ParcelFactory.Core.UnlockId.Plot1);
                s.Unlocks.Grant(ParcelFactory.Core.UnlockId.Bridge);
                s.Unlocks.Grant(ParcelFactory.Core.UnlockId.FastEast);
                s.Unlocks.Grant(ParcelFactory.Core.UnlockId.Splitter);
                s.Unlocks.Grant(ParcelFactory.Core.UnlockId.Scanner);
                s.Toolbar.Add(ParcelFactory.Core.CellType.Conveyor, ParcelFactory.Core.Direction.North);
                yield return null;

                // 핫바 7칸, 브릿지는 세 번째(키 3)
                Assert.AreEqual(7, InputController.SlotCount);
                var slots = input.AvailableSlots();
                Assert.AreEqual(7, slots.Count, string.Join(",", slots));
                Assert.AreEqual(ParcelFactory.Core.CellType.Bridge, slots[2]);
                Assert.IsTrue(InputController.IsConfigurable(ParcelFactory.Core.CellType.Bridge));
                var hotbar = boot.Hud.Root.Find("Hotbar") as RectTransform;
                int shownSlots = 0;
                foreach (Transform t in hotbar) if (t.gameObject.activeSelf) shownSlots++;
                Assert.AreEqual(7, shownSlots);
                Assert.AreEqual("브릿지", hotbar.Find("Slot3").GetComponentInChildren<UnityEngine.UI.Text>().text);

                // 터치 모드: 7칸 핫바가 캔버스 안에 있고 터치 버튼과 겹치지 않는다
                boot.Hud.ForceTouchMode(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var canvasRect = ScreenRect(boot.Hud.Root);
                var barRect = ScreenRect(hotbar);
                Assert.GreaterOrEqual(barRect.xMin, canvasRect.xMin - 0.01f, "hotbar sticks out left");
                Assert.LessOrEqual(barRect.xMax, canvasRect.xMax + 0.01f, "hotbar sticks out right");
                foreach (var name in new[] { "TouchE", "TouchX", "TouchR", "TouchRun", "TouchPadN", "TouchPadS", "TouchPadW", "TouchPadE" })
                    Assert.IsFalse(barRect.Overlaps(ScreenRect(GameObject.Find(name).GetComponent<RectTransform>())), "hotbar overlaps " + name);
                boot.Hud.ForceTouchMode(false);

                // 브릿지 도구(슬롯 index 2) → R → ↑ → (2,3)에서 동쪽 (3,3)에 설치
                w.X = 2f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.East;
                input.SelectSlot(2);
                Assert.AreEqual(ParcelFactory.Core.CellType.Bridge, input.SelectedTool.Value.Type);
                input.BeginConfig();
                Assert.IsFalse(input.ConfigTargetIsCell);
                Assert.AreEqual(ConfigMode.PickDirection, input.ConfigMode);
                input.ApplyConfigDirection(ParcelFactory.Core.Direction.North);
                Assert.AreEqual(ParcelFactory.Core.Direction.North, input.SelectedTool.Value.Direction);
                yield return null;
                Assert.IsTrue(boot.Ghost.Visible, "bridge ghost hidden");
                Assert.IsNotNull(boot.Ghost.Model.transform.Find("UpperBelt"), "bridge ghost has no upper belt");
                input.Interact();
                var cell = s.Grid.Get(3, 3);
                Assert.AreEqual(ParcelFactory.Core.CellType.Bridge, cell.Type);
                Assert.AreEqual(ParcelFactory.Core.Direction.North, cell.Direction);
                Assert.AreEqual(1.3f, PackageViews.TopHeight(cell), 1e-4f);

                // 동쪽 줄 (2,3)→(3,3)→(4,3), 북쪽 줄 (3,2)→(3,3)→(3,4). 작업자는 비켜 선다.
                w.X = 5f; w.Y = 3f; w.Facing = ParcelFactory.Core.Direction.East;
                foreach (var (x, y, d) in new[] { (2, 3, ParcelFactory.Core.Direction.East), (4, 3, ParcelFactory.Core.Direction.East), (3, 2, ParcelFactory.Core.Direction.North), (3, 4, ParcelFactory.Core.Direction.North) })
                    Assert.AreEqual(ParcelFactory.Core.BuildResult.Ok, s.Build.TryPlace(x, y, ParcelFactory.Core.CellType.Conveyor, d), $"rail at ({x},{y})");
                yield return null;

                // 뷰: 바닥 벨트 + 기둥 4개 + 고가 벨트(0.9) + 윗길 화살표
                var view = FindCellView("Bridge", 3, 3);
                Assert.IsNotNull(view, "bridge view missing");
                Assert.IsNotNull(view.transform.Find("LowerBelt"), "lower belt missing");
                var upperBelt = view.transform.Find("UpperBelt");
                Assert.IsNotNull(upperBelt, "upper belt missing");
                Assert.AreEqual(0.9f, upperBelt.localPosition.y, 1e-4f);
                int posts = 0;
                foreach (Transform t in view.transform) if (t.name == "BridgePost") posts++;
                Assert.AreEqual(4, posts);
                var arrowPivot = view.transform.Find("ArrowPivot");
                Assert.IsNotNull(arrowPivot, "upper arrow pivot missing");
                Assert.AreEqual(0.9f, arrowPivot.localPosition.y, 1e-4f);
                Assert.IsNotNull(arrowPivot.Find("Chevron").GetComponent<BeltArrow>());
                Assert.Greater(Vector3.Dot(Pointing(arrowPivot), Vector3.forward), 0.99f, "upper arrow should point north (+z)");

                // 상태바 "브릿지 ↑"
                StringAssert.Contains("브릿지 ↑", boot.StatusBars.TextAt(3, 3));

                // 두 택배를 흘려 둘 다 브릿지에 올린다: 북쪽 줄 = 윗길, 동쪽 줄 = 아랫길
                Assert.IsTrue(s.Sim.TrySpawn(2, 3, ParcelFactory.Core.PackageKind.Normal, out var east));
                Assert.IsTrue(s.Sim.TrySpawn(3, 2, ParcelFactory.Core.PackageKind.Normal, out var north));
                for (int i = 0; i < 80 && s.Sim.PackagesAt(3, 3).Count < 2; i++) s.Sim.Tick(0.05f);
                Assert.AreEqual(2, s.Sim.PackagesAt(3, 3).Count, "both packages should be on the bridge");
                Assert.AreEqual(ParcelFactory.Core.Lane.Upper, north.Lane);
                Assert.AreEqual(ParcelFactory.Core.Lane.Lower, east.Lane);
                yield return null;
                var pv = boot.GetComponent<PackageViews>();
                var upperBox = pv.ViewOf(north.Id);
                var lowerBox = pv.ViewOf(east.Id);
                Assert.IsNotNull(upperBox, "upper package not drawn");
                Assert.IsNotNull(lowerBox, "lower package not drawn");
                Assert.AreEqual(1.3f, upperBox.transform.position.y, 1e-3f, "upper lane height");
                Assert.AreEqual(0.4f, lowerBox.transform.position.y, 1e-3f, "lower lane height");
                var tile = Grid3D.Tile(3, 3);
                foreach (var box in new[] { upperBox, lowerBox })
                {
                    Assert.LessOrEqual(Mathf.Abs(box.transform.position.x - tile.x), 0.5f, box.name + " not over the bridge tile");
                    Assert.LessOrEqual(Mathf.Abs(box.transform.position.z - tile.z), 0.5f, box.name + " not over the bridge tile");
                }

                // 체력 바 80% 눈금
                var tick = boot.Hud.Root.Find("Stamina/Bar/" + HudController.StaminaTickName) as RectTransform;
                Assert.IsNotNull(tick, "stamina 80% tick missing");
                Assert.AreEqual(ParcelFactory.Core.WorkerService.RegenCap, tick.anchorMin.x, 1e-4f);
                Assert.AreEqual(1f, tick.rect.width, 1e-3f);
                Assert.Greater(tick.GetSiblingIndex(), boot.Hud.Root.Find("Stamina/Bar/Fill").GetSiblingIndex(), "tick must draw over the fill");

                // 상점: 12번째 행(브릿지)이 패널 안에 보인다
                input.RequestShop();
                Assert.IsTrue(boot.Panels.ShopIsOnlyOpen, "shop did not open");
                yield return null;
                Canvas.ForceUpdateCanvases();
                var shop = boot.Hud.Root.Find("Shop") as RectTransform;
                var list = shop.Find("List");
                Assert.AreEqual(ParcelFactory.Core.UnlockCatalog.All.Count, list.childCount);
                Transform bridgeRow = null;
                foreach (Transform row in list) if (row.Find("Name").GetComponent<UnityEngine.UI.Text>().text == "브릿지") bridgeRow = row;
                Assert.IsNotNull(bridgeRow, "shop has no 브릿지 row");
                Assert.IsTrue(bridgeRow.gameObject.activeInHierarchy);
                var shopRect = ScreenRect(shop);
                foreach (Transform row in list)
                {
                    var r = ScreenRect((RectTransform)row);
                    Assert.GreaterOrEqual(r.yMin, shopRect.yMin - 0.01f, row.Find("Name").GetComponent<UnityEngine.UI.Text>().text + " row below the shop panel");
                }
                Assert.AreEqual("보유", bridgeRow.Find("Buy").GetComponentInChildren<UnityEngine.UI.Text>().text);
                boot.Panels.CloseAll();
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
