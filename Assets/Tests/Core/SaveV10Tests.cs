using System;
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    /// <summary>스펙 19.5(O4): 저장 v10 = 회사 + 공장 목록. v6~v9는 1공장 하나짜리 회사로 읽는다. 공장 수 1~3, 종류 중복 금지, 현재 번호 범위.</summary>
    public class SaveV10Tests
    {
        private static Package Good(int id, bool air = false)
        {
            var p = new PackageFactory(new Random(id), 0.0).Create(id, 0, 0, Direction.East);
            p.Inspect(false);
            p.Waybill = true;
            p.Air = air;
            return p;
        }

        /// <summary>공장 셋: 1공장 부지 확장 2·자동 검사기, 2공장(공항) 레일 위 택배·트럭 적재, 3공장(정밀) 팔레트 더미. 작업자는 2공장에서 택배를 든다.</summary>
        private static GameSession ThreeFactories()
        {
            var s = new GameSession(new Economy(50000));
            var c = s.Company;
            s.Unlocks.Grant(UnlockId.Plot2);
            s.Unlocks.Grant(UnlockId.Scanner);
            Assert.AreEqual(FactoryPurchaseResult.Ok, c.TryBuyFactory(FactoryKind.Airport));
            var airport = c.Factories[1];
            airport.Unlocks.Grant(UnlockId.Plot2);
            airport.Unlocks.Grant(UnlockId.Scanner);
            Assert.AreEqual(FactoryPurchaseResult.Ok, c.TryBuyFactory(FactoryKind.Precision));
            var precision = c.Factories[2];

            Assert.AreEqual(BuildResult.Ok, airport.Build.TryPlace(3, 3, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, airport.Build.TryPlace(4, 3, CellType.Table, Direction.East));
            var onRail = Good(airport.Sim.NextId++, air: true);
            onRail.Inspected = false; // 검사대 (4,3)에서 멈춘다
            airport.Sim.PlaceCarried(onRail, 3, 3);
            for (int i = 0; i < 7; i++) airport.Sim.DeliverCarried(Good(500 + i, air: true), CellType.AirOutlet);
            precision.Sim.PutOnPallet(Good(precision.Sim.NextId++), FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY);
            precision.Unlocks.Grant(UnlockId.Plot1);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));

            Assert.AreEqual(FactorySwitchResult.Ok, c.TrySwitch(1));
            var carried = Good(airport.Sim.NextId++);
            s.Worker.Worker.Carrying = carried;
            s.Tick(3f);
            return s;
        }

        [Test]
        public void V10_RoundTripsThreeFactories()
        {
            var s = ThreeFactories();
            var st = SaveSerializer.Capture(s);
            Assert.AreEqual(11, st.Version); // v11: 점심 카드(스펙 21.4)
            Assert.AreEqual(1, st.CurrentFactory);
            Assert.AreEqual("Headquarters", st.Kind);
            Assert.AreEqual(2, st.OtherFactories.Count);
            Assert.AreEqual("Airport", st.OtherFactories[0].Kind);
            Assert.AreEqual("Precision", st.OtherFactories[1].Kind);
            Assert.AreEqual(4, st.OtherFactories[0].Trucks.Count, "the airport has its air trucks");

            var r = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(st)));
            var rc = r.Company;
            Assert.AreEqual(3, rc.Factories.Count);
            Assert.AreEqual(1, rc.CurrentIndex);
            Assert.AreSame(rc.Factories[1], r.Factory);
            Assert.AreEqual(s.Economy.Money, r.Economy.Money);
            Assert.AreEqual(s.Worker.Worker.X, r.Worker.Worker.X);
            Assert.AreEqual(s.Worker.Worker.Carrying.Id, r.Worker.Worker.Carrying.Id);
            Assert.AreSame(rc.Factories[1].Grid, r.Grid, "the worker is bound to the current factory");

            var hq = rc.Factories[0];
            Assert.IsTrue(hq.Unlocks.IsPurchased(UnlockId.Scanner));
            Assert.AreEqual(CellType.Scanner, hq.Grid.Get(1, 2).Type);
            var airport = rc.Factories[1];
            Assert.AreEqual(FactoryKind.Airport, airport.Kind);
            Assert.AreEqual(1, airport.Sim.Packages.Count);
            Assert.AreEqual(1, airport.Sim.AwaitingInspectionCount);
            Assert.IsTrue(airport.Sim.Packages[0].Air);
            Assert.AreEqual(7, airport.Trucks.Loaded(CellType.AirOutlet));
            Assert.AreEqual(0.6, airport.Sim.Factory.AirChance);
            Assert.AreEqual(s.Company.Factories[1].Sim.NextId, airport.Sim.NextId);
            Assert.AreEqual(s.Company.Factories[1].EarnedToday, airport.EarnedToday);
            Assert.AreEqual(7, airport.Sim.DeliveredCount);
            var precision = rc.Factories[2];
            Assert.AreEqual(9, precision.Grid.UnlockedSize);
            Assert.AreEqual(1, precision.Sim.PalletCount(FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY));
            Assert.AreEqual(0.5, precision.Sim.Factory.FragileChance);
            Assert.AreEqual(12, hq.Grid.UnlockedSize);
            Assert.AreEqual(s.BuildSummary().Factories.Count, r.BuildSummary().Factories.Count);
            Assert.AreEqual(s.BuildSummary().Factories[1].Earned, r.BuildSummary().Factories[1].Earned);
        }

        /// <summary>최상위 키(들여쓰기 두 칸)만 지운다. 택배의 "Kind" 같은 같은 이름의 안쪽 키는 건드리지 않는다. 배열 값은 닫는 괄호까지.</summary>
        public static string StripTopLevel(string json, params string[] keys)
        {
            foreach (var k in keys)
            {
                json = System.Text.RegularExpressions.Regex.Replace(json, "\n  \"" + k + "\": \\[[^\\n]*\\],?(?=\\n)", "");          // 한 줄 배열([])
                json = System.Text.RegularExpressions.Regex.Replace(json, "\n  \"" + k + "\": \\[\\n.*?\\n  \\],?(?=\\n)", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline);                                                   // 여러 줄 배열
                json = System.Text.RegularExpressions.Regex.Replace(json, "\n  \"" + k + "\": [^\\[\\n][^\\n]*(?=\\n)", "");    // 값 한 줄
            }
            return json;
        }

        /// <summary>트럭 항목 안의 "Vehicles" 배열(v10)을 지운다(v9 트럭에는 없다).</summary>
        public static string StripVehicles(string json) =>
            System.Text.RegularExpressions.Regex.Replace(json, ",?\\n( +)\"Vehicles\": \\[.*?\\n\\1\\]", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        [Test]
        public void V9_ReadsAsASingleFactoryCompany()
        {
            var s = new GameSession(new Economy(700));
            s.Unlocks.Grant(UnlockId.Plot1);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 3, CellType.Conveyor, Direction.East));
            var onRail = Good(s.Sim.NextId++);
            s.Sim.PlaceCarried(onRail, 1, 3);
            var pile = Good(s.Sim.NextId++);
            Assert.IsTrue(s.Sim.PutOnPallet(pile, FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY));
            for (int i = 0; i < 3; i++) s.Sim.DeliverCarried(Good(900 + i), CellType.Outlet);
            var st = SaveSerializer.Capture(s);
            var json = SaveSerializer.ToJson(st).Replace("\"Version\": 11", "\"Version\": 9");
            json = StripTopLevel(json, "Kind", "EarnedToday", "CurrentFactory", "OtherFactories", "LunchMenu", "LunchConfirmed", "Contract", "ContractCandidates");
            json = StripVehicles(json);
            StringAssert.DoesNotContain("OtherFactories", json);
            StringAssert.DoesNotContain("CurrentFactory", json);
            StringAssert.DoesNotContain("Vehicles", json);
            StringAssert.DoesNotContain("\"Kind\": \"Headquarters\"", json);
            StringAssert.Contains("\"Kind\": \"Normal\"", json, "package kinds stay");
            var r = SaveSerializer.Restore(SaveSerializer.FromJson(json));
            Assert.AreEqual(1, r.Company.Factories.Count);
            Assert.AreEqual(FactoryKind.Headquarters, r.Factory.Kind);
            Assert.AreEqual(0, r.Company.CurrentIndex);
            Assert.AreEqual(9, r.Grid.UnlockedSize);
            Assert.AreEqual(s.Economy.Money, r.Economy.Money);
            Assert.AreEqual(1, r.Sim.Packages.Count);
            Assert.AreEqual(onRail.Id, r.Sim.Packages[0].Id);
            Assert.AreEqual(1, r.Sim.PalletCount(FactoryGrid.StartBluePalletX, FactoryGrid.StartBluePalletY));
            Assert.AreEqual(3, r.Sim.Trucks.Loaded(CellType.Outlet));
            Assert.AreEqual(1, r.Sim.Trucks.Count(CellType.Outlet), "v9 reads one truck per dock");
            Assert.AreEqual(0, r.Factory.EarnedToday, "v9 has no per-factory earnings");
            // 다시 저장하면 지금 버전(v11).
            var again = SaveSerializer.Capture(r);
            Assert.AreEqual(SaveSerializer.CurrentVersion, again.Version);
            Assert.AreEqual("Headquarters", again.Kind);
            Assert.IsEmpty(again.OtherFactories);
        }

        [Test]
        public void V9_WithOtherFactories_IsCorrupt()
        {
            var st = SaveSerializer.Capture(ThreeFactories());
            st.Version = 9;
            st.CurrentFactory = 0;
            st.Worker.X = 2f; st.Worker.Y = 4f; st.Worker.Carrying = null;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        [Test]
        public void Validation_CountKindsAndCurrentIndex()
        {
            GameState Fresh() => SaveSerializer.Capture(ThreeFactories());
            Assert.DoesNotThrow(() => SaveSerializer.Restore(Fresh()));

            var st = Fresh();
            st.OtherFactories.Add(st.OtherFactories[1]); // 공장 넷
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories[1].Kind = "Airport"; // 종류 중복
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.Kind = "Airport"; // 1공장은 본사
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories.RemoveAt(0); // 공항 없이 정밀
            st.CurrentFactory = 0;
            st.Worker.X = 2f; st.Worker.Y = 4f; st.Worker.Carrying = null;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories[0].Kind = "Bogus";
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.CurrentFactory = 3;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st.CurrentFactory = -1;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories[0] = null;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories[0].NextPackageId = 1; // 들고 있는 택배 id(2공장)보다 작다
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories[1].Trucks.Add(new TruckState { Dock = "AirOutlet" }); // 정밀은 부지 확장 2 전이라 항공 트럭이 없다
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));

            st = Fresh();
            st.OtherFactories = null; // 2·3공장 목록이 없으면 1공장 하나(지금 번호 1은 범위 밖)
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st.CurrentFactory = 0;
            st.Worker.X = 2f; st.Worker.Y = 4f; st.Worker.Carrying = null;
            Assert.AreEqual(1, SaveSerializer.Restore(st).Company.Factories.Count);
        }
    }
}
