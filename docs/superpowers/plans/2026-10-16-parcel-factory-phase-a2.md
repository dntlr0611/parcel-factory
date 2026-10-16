# Parcel Factory Phase A2 구현 계획 — 작업자·검사·반품구·저장 v2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 스펙 9절(2026-10-16 개정)의 코어 변경을 순수 C#에 추가한다: 검사대·자동 검사기·반품구, 택배 라벨과 불량, 배송 판정표, 작업자(이동·집기·놓기·검사·설치), 해금 11종, 저장 버전 2.

**Architecture:** Phase A와 같은 구조. `Assets/Scripts/Core/` 순수 C# + `Assets/Tests/Core/` NUnit, `dotnet test DotnetTests/Core.Tests`로 검증. 새 클래스는 `PackageFactory`(난수 주입), `DeliveryScoring`(순수 판정표), `WorkerService`(작업자 규칙). 기존 클래스는 최소 확장.

**Tech Stack:** C# 9, .NET SDK 10 (`~/.dotnet/dotnet`), NUnit 3.14, Newtonsoft.Json 13.

**Spec:** `docs/superpowers/specs/2026-10-15-parcel-factory-design.md` — 9.2(운반), 9.3(검사·판정표), 9.5(코어 변경 요약). 9절이 3~6절과 충돌하면 9절이 우선.

## Global Constraints

- 코어 코드는 `UnityEngine`을 참조하지 않는다. C# 9.0 이하. NUnit 클래식 API만.
- 새 CellType: `Table`(설치비 40, 시작부터 4방향 보유), `Scanner`(설치비 120, 해금 250), `ReturnDock`(고정, (5,4)).
- 불량 확률 30%. 불량 택배는 표기 무게와 실측이 다르다(실측 = 표기 + 3~7). 정상은 같다.
- 판정표 (출고구 / 반품구): 검사 안 함 −10/−5 · 승인·정상 +15/−5 · 승인·불량 −20/−5 · 거부·불량 −10/+5 · 거부·정상 −10/−5. 성공(+15, +5)만 배송 수에 센다. 잔액은 음수가 될 수 있다.
- 검사대 위 미검사 택배는 멈춘다. 자동 검사기는 진입 즉시 정확히 판정한다.
- 작업자: 시작 (2,4) 남향. 빈 바닥(`Empty`)만 걷는다. 막히면 방향만 바뀐다. 택배 1개 운반. 운반 중에는 설치 불가.
- 저장 버전 2. 버전 1 파일은 거부.
- 커밋 트레일러: 서브에이전트는 자기 모델의 `Co-Authored-By`를 쓴다.
- 테스트 개수는 늘어나기만 한다. 각 태스크의 GREEN 기준은 "`Failed: 0`이고 명시한 새 테스트가 모두 포함"이다.

## 파일 구조

```
Assets/Scripts/Core/Cell.cs              CellType 3종 추가, IsTrack/IsBuilding/IsDock
Assets/Scripts/Core/FactoryGrid.cs       ReturnDock 고정, Place 허용 타입 확장
Assets/Scripts/Core/BuildingCosts.cs     Table 40, Scanner 120
Assets/Scripts/Core/Economy.cs           Adjust(delta) — 음수 허용
Assets/Scripts/Core/Package.cs           PackageLabel, Bad/Inspected/Rejected, Inspect()
Assets/Scripts/Core/PackageFactory.cs    (신규) 난수 주입 택배 생성
Assets/Scripts/Core/DeliveryScoring.cs   (신규) 판정표
Assets/Scripts/Core/ConveyorSim.cs       검사대 정지, 스캐너 판정, 도크 판정, Take/PlaceCarried/DeliverCarried/PackageAt, 카운터
Assets/Scripts/Core/Worker.cs            (신규) Worker, InteractResult, WorkerService
Assets/Scripts/Core/Toolbar.cs           Table/Scanner 보유
Assets/Scripts/Core/Unlocks.cs           Scanner 해금
Assets/Scripts/Core/GameSession.cs       PackageFactory 주입, WorkerService
Assets/Scripts/Core/GameState.cs         v2 필드
Assets/Scripts/Core/SaveSerializer.cs    v2 capture/restore
Assets/Tests/Core/*Tests.cs              수정 + InspectionTests.cs, WorkerTests.cs 신규
```

---

### Task 1: 셀 타입·반품구·설치비

**Files:**
- Modify: `Assets/Scripts/Core/Cell.cs`
- Modify: `Assets/Scripts/Core/FactoryGrid.cs`
- Modify: `Assets/Scripts/Core/BuildingCosts.cs`
- Test: `Assets/Tests/Core/FactoryGridTests.cs`, `Assets/Tests/Core/EconomyTests.cs`

**Interfaces:**
- Produces: `CellType.Table`, `CellType.Scanner`, `CellType.ReturnDock`; `Cell.IsDock`; `FactoryGrid.ReturnDockX/Y = 5,4`; `FactoryGrid.IsPlaceable(CellType)`; `BuildingCosts.Cost(Table)=40`, `Cost(Scanner)=120`.

- [ ] **Step 1: 실패하는 테스트 추가**

`Assets/Tests/Core/FactoryGridTests.cs`의 클래스 끝(마지막 `}` 두 개 앞)에 추가:
```csharp
        [Test]
        public void NewGrid_HasReturnDockFixed()
        {
            var g = new FactoryGrid();
            Assert.AreEqual(CellType.ReturnDock, g.Get(5, 4).Type);
            Assert.IsTrue(g.Get(5, 4).IsDock);
            Assert.IsTrue(g.Get(5, 2).IsDock);
            Assert.IsFalse(g.Get(0, 2).IsDock);
            Assert.Throws<InvalidOperationException>(() => g.Clear(5, 4));
            Assert.IsFalse(g.CanPlace(5, 4));
        }

        [Test]
        public void Place_AcceptsTableAndScanner()
        {
            var g = new FactoryGrid();
            g.Place(1, 1, CellType.Table, Direction.North);
            g.Place(2, 1, CellType.Scanner, Direction.West);
            Assert.AreEqual(CellType.Table, g.Get(1, 1).Type);
            Assert.AreEqual(Direction.West, g.Get(2, 1).Direction);
            Assert.IsTrue(FactoryGrid.IsPlaceable(CellType.Table));
            Assert.IsFalse(FactoryGrid.IsPlaceable(CellType.ReturnDock));
        }

        [Test]
        public void Cell_TrackAndBuildingFlags_IncludeTableAndScanner()
        {
            Assert.IsTrue(new Cell { Type = CellType.Table }.IsTrack);
            Assert.IsTrue(new Cell { Type = CellType.Table }.IsBuilding);
            Assert.IsTrue(new Cell { Type = CellType.Scanner }.IsTrack);
            Assert.IsTrue(new Cell { Type = CellType.Scanner }.IsBuilding);
            Assert.IsFalse(new Cell { Type = CellType.ReturnDock }.IsTrack);
            Assert.IsFalse(new Cell { Type = CellType.ReturnDock }.IsBuilding);
            Assert.AreEqual(1f, new Cell { Type = CellType.Scanner }.Speed);
        }
```

`Assets/Tests/Core/EconomyTests.cs`의 `BuildingCosts_MatchSpec`에 두 줄 추가:
```csharp
            Assert.AreEqual(40, BuildingCosts.Cost(CellType.Table));
            Assert.AreEqual(120, BuildingCosts.Cost(CellType.Scanner));
```

- [ ] **Step 2: 실패 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패 (`CellType.Table` 없음).

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Cell.cs` 전체:
```csharp
namespace ParcelFactory.Core
{
    public enum CellType { Empty, Locked, Conveyor, FastConveyor, Splitter, Inlet, Outlet, Table, Scanner, ReturnDock }

    public sealed class Cell
    {
        public CellType Type = CellType.Empty;
        public Direction Direction = Direction.East;
        public bool Demolishing;
        public float DemolishRemaining;
        /// <summary>false → 다음 택배는 왼쪽 출구, true → 오른쪽 출구.</summary>
        public bool SplitterToggle;
        /// <summary>셀 위에 있는 택배 id. 없으면 -1.</summary>
        public int OccupantId = -1;

        public bool IsOccupied => OccupantId >= 0;

        /// <summary>택배가 올라가 이동할 수 있는 셀.</summary>
        public bool IsTrack =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Inlet ||
            Type == CellType.Table || Type == CellType.Scanner;

        /// <summary>플레이어가 설치·해체할 수 있는 셀.</summary>
        public bool IsBuilding =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor ||
            Type == CellType.Splitter || Type == CellType.Table || Type == CellType.Scanner;

        /// <summary>택배를 받아 판정하는 셀(출고구, 반품구).</summary>
        public bool IsDock => Type == CellType.Outlet || Type == CellType.ReturnDock;

        public float Speed => Type == CellType.FastConveyor ? 2f : 1f;
    }
}
```

`Assets/Scripts/Core/FactoryGrid.cs`: 상수와 생성자, `Place` 가드를 아래처럼 바꾼다.
```csharp
        public const int OutletX = 5, OutletY = 2;
        public const int ReturnDockX = 5, ReturnDockY = 4;
```
생성자에서 `cells[OutletX, OutletY].Type = CellType.Outlet;` 다음 줄에:
```csharp
            cells[ReturnDockX, ReturnDockY].Type = CellType.ReturnDock;
```
`Place`의 타입 가드를 교체:
```csharp
            if (!IsPlaceable(type))
                throw new ArgumentException($"{type} is not placeable", nameof(type));
```
`CanPlace` 위에 추가:
```csharp
        public static bool IsPlaceable(CellType type) =>
            type == CellType.Conveyor || type == CellType.FastConveyor || type == CellType.Splitter ||
            type == CellType.Table || type == CellType.Scanner;
```

`Assets/Scripts/Core/BuildingCosts.cs`의 switch에 추가:
```csharp
                case CellType.Table: return 40;
                case CellType.Scanner: return 120;
```

- [ ] **Step 4: 통과 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0`, 새 테스트 3개 포함.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core/Cell.cs Assets/Scripts/Core/FactoryGrid.cs Assets/Scripts/Core/BuildingCosts.cs Assets/Tests/Core/FactoryGridTests.cs Assets/Tests/Core/EconomyTests.cs
git commit -m "feat(core): table, scanner and return dock cell types"
```

---

### Task 2: 택배 라벨·불량·판정표·검사 흐름

**Files:**
- Modify: `Assets/Scripts/Core/Package.cs`, `Assets/Scripts/Core/Economy.cs`, `Assets/Scripts/Core/ConveyorSim.cs`
- Create: `Assets/Scripts/Core/PackageFactory.cs`, `Assets/Scripts/Core/DeliveryScoring.cs`
- Modify: `Assets/Tests/Core/ConveyorSimTests.cs`, `Assets/Tests/Core/EconomyTests.cs`, `Assets/Tests/Core/GameSessionTests.cs`
- Create: `Assets/Tests/Core/InspectionTests.cs`

**Interfaces:**
- Consumes: Task 1의 CellType·IsDock.
- Produces:
  - `class PackageLabel { string Destination; int Weight; string Method; int Scale; bool Mismatch }`
  - `Package` 추가 필드 `bool Bad, Inspected, Rejected; PackageLabel Label; void Inspect(bool reject)`
  - `class PackageFactory { const double BadChance=0.3; PackageFactory(Random rng, double badChance = BadChance); Package Create(int id,int x,int y,Direction heading) }`
  - `enum DeliveryOutcome { Delivered, ReturnedBad, UninspectedToOutlet, RejectedToOutlet, BadToOutlet, UninspectedToReturn, GoodToReturn, ApprovedToReturn }`
  - `static class DeliveryScoring { DeliveryOutcome Outcome(Package, CellType dock); int Delta(DeliveryOutcome); bool IsSuccess(DeliveryOutcome) }`
  - `Economy.Adjust(int delta)` (음수 허용)
  - `ConveyorSim(FactoryGrid, Economy, PackageFactory factory = null)`; `int DeliveredCount {get;set;}`, `int LostCount {get;set;}`; `event Action<Package, DeliveryOutcome, int> PackageDelivered`; `Package PackageAt(int x,int y)`; `void Take(Package)`; `void PlaceCarried(Package,int x,int y)`; `int DeliverCarried(Package, CellType dock)`.
  - `ConveyorSim.DeliveryReward` 상수는 삭제한다.

- [ ] **Step 1: 기존 테스트를 새 계약에 맞게 수정**

`Assets/Tests/Core/ConveyorSimTests.cs`:
- 파일 상단 `using System.Collections.Generic;` 위에 `using System;` 추가.
- `SetUp`의 `sim = new ConveyorSim(grid, economy);` → `sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));` (불량 0% → 결정적).
- `Delivery_RemovesPackageAndPays`를 아래로 교체:
```csharp
        [Test]
        public void Delivery_RemovesPackageAndPays()
        {
            for (int x = 1; x <= 4; x++) Rail(x, 2, Direction.East);
            var p = SpawnAtInlet();
            p.Inspect(false);
            var delivered = new List<Package>();
            sim.PackageDelivered += (pk, outcome, delta) => delivered.Add(pk);
            for (int i = 0; i < 5; i++) sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(15, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
            Assert.AreSame(p, delivered[0]);
            Assert.AreEqual(-1, grid.Get(4, 2).OccupantId);
        }
```
- `EndOfTrack_LosesPackage`의 마지막 줄 뒤에 `Assert.AreEqual(1, sim.LostCount);` 추가.

`Assets/Tests/Core/GameSessionTests.cs`:
- 상단에 `using System;` 추가.
- `StraightLine_DeliversAndEarns`를 아래 두 테스트로 교체:
```csharp
        [Test]
        public void StraightLine_UninspectedDeliveryIsPenalized()
        {
            var s = new GameSession(new Economy(), new PackageFactory(new Random(1), 0.0));
            for (int x = 1; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(60, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f); // 9초: 3초에 생성, 5초 이동, 검사 없이 출고 → −10
            Assert.AreEqual(50, s.Economy.Money);
            Assert.AreEqual(0, s.Sim.DeliveredCount);
        }

        [Test]
        public void StraightLine_WithScanner_Earns15()
        {
            var s = new GameSession(new Economy(300), new PackageFactory(new Random(1), 0.0));
            s.Toolbar.Add(CellType.Scanner, Direction.East);
            Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(1, 2, CellType.Scanner, Direction.East));
            for (int x = 2; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(150, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f);
            Assert.AreEqual(165, s.Economy.Money);
            Assert.AreEqual(1, s.Sim.DeliveredCount);
        }
```
(`GameSession(Economy, PackageFactory)` 생성자는 Task 4에서 추가한다. 이 태스크에서는 임시로 `GameSession.cs`에 아래 생성자만 먼저 넣는다 — Step 3 참고.)

`Assets/Tests/Core/EconomyTests.cs`에 추가:
```csharp
        [Test]
        public void Adjust_AllowsNegativeBalance()
        {
            var e = new Economy(5);
            int seen = 0;
            e.MoneyChanged += m => seen = m;
            e.Adjust(-20);
            Assert.AreEqual(-15, e.Money);
            Assert.AreEqual(-15, seen);
            e.Adjust(30);
            Assert.AreEqual(15, e.Money);
        }
```

- [ ] **Step 2: 새 테스트 파일 작성**

`Assets/Tests/Core/InspectionTests.cs`:
```csharp
using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class InspectionTests
    {
        private FactoryGrid grid;
        private Economy economy;

        private ConveyorSim NewSim(double badChance)
        {
            grid = new FactoryGrid();
            economy = new Economy(0);
            return new ConveyorSim(grid, economy, new PackageFactory(new Random(7), badChance));
        }

        private static Package Pkg(bool bad, bool inspected, bool rejected) =>
            new Package { Id = 1, Bad = bad, Inspected = inspected, Rejected = rejected, Label = new PackageLabel { Weight = 2, Scale = bad ? 6 : 2 } };

        [Test]
        public void Scoring_OutletMatchesSpecTable()
        {
            Assert.AreEqual(DeliveryOutcome.UninspectedToOutlet, DeliveryScoring.Outcome(Pkg(false, false, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.Delivered, DeliveryScoring.Outcome(Pkg(false, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.BadToOutlet, DeliveryScoring.Outcome(Pkg(true, true, false), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.RejectedToOutlet, DeliveryScoring.Outcome(Pkg(true, true, true), CellType.Outlet));
            Assert.AreEqual(DeliveryOutcome.RejectedToOutlet, DeliveryScoring.Outcome(Pkg(false, true, true), CellType.Outlet));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.UninspectedToOutlet));
            Assert.AreEqual(15, DeliveryScoring.Delta(DeliveryOutcome.Delivered));
            Assert.AreEqual(-20, DeliveryScoring.Delta(DeliveryOutcome.BadToOutlet));
            Assert.AreEqual(-10, DeliveryScoring.Delta(DeliveryOutcome.RejectedToOutlet));
        }

        [Test]
        public void Scoring_ReturnDockMatchesSpecTable()
        {
            Assert.AreEqual(DeliveryOutcome.UninspectedToReturn, DeliveryScoring.Outcome(Pkg(true, false, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ApprovedToReturn, DeliveryScoring.Outcome(Pkg(false, true, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ApprovedToReturn, DeliveryScoring.Outcome(Pkg(true, true, false), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, DeliveryScoring.Outcome(Pkg(true, true, true), CellType.ReturnDock));
            Assert.AreEqual(DeliveryOutcome.GoodToReturn, DeliveryScoring.Outcome(Pkg(false, true, true), CellType.ReturnDock));
            Assert.AreEqual(-5, DeliveryScoring.Delta(DeliveryOutcome.UninspectedToReturn));
            Assert.AreEqual(-5, DeliveryScoring.Delta(DeliveryOutcome.ApprovedToReturn));
            Assert.AreEqual(5, DeliveryScoring.Delta(DeliveryOutcome.ReturnedBad));
            Assert.AreEqual(-5, DeliveryScoring.Delta(DeliveryOutcome.GoodToReturn));
            Assert.IsTrue(DeliveryScoring.IsSuccess(DeliveryOutcome.Delivered));
            Assert.IsTrue(DeliveryScoring.IsSuccess(DeliveryOutcome.ReturnedBad));
            Assert.IsFalse(DeliveryScoring.IsSuccess(DeliveryOutcome.GoodToReturn));
            Assert.Throws<ArgumentException>(() => DeliveryScoring.Outcome(Pkg(false, true, false), CellType.Conveyor));
        }

        [Test]
        public void Factory_BadPackagesHaveMismatchedScale()
        {
            var bad = new PackageFactory(new Random(3), 1.0).Create(1, 0, 2, Direction.East);
            Assert.IsTrue(bad.Bad);
            Assert.IsTrue(bad.Label.Mismatch);
            Assert.IsTrue(bad.Label.Scale >= bad.Label.Weight + 3 && bad.Label.Scale <= bad.Label.Weight + 7);
            var good = new PackageFactory(new Random(3), 0.0).Create(2, 0, 2, Direction.East);
            Assert.IsFalse(good.Bad);
            Assert.IsFalse(good.Label.Mismatch);
            Assert.IsTrue(good.Label.Weight >= 1 && good.Label.Weight <= 8);
            Assert.IsFalse(string.IsNullOrEmpty(good.Label.Destination));
            Assert.IsFalse(string.IsNullOrEmpty(good.Label.Method));
            Assert.IsFalse(good.Inspected);
            Assert.AreEqual(Direction.East, good.EntryHeading);
        }

        [Test]
        public void Table_HoldsUninspectedPackage_ThenReleasesAfterInspect()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 2, CellType.Table, Direction.East);
            grid.Place(2, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y)); // 검사 전까지 검사대에 머무름
            Assert.AreEqual(1f, p.Progress);
            p.Inspect(false);
            sim.Tick(0.05f);
            Assert.AreEqual((2, 2), (p.X, p.Y)); // 검사 직후 즉시 이동
            Assert.IsTrue(p.Inspected);
            Assert.IsFalse(p.Rejected);
        }

        [Test]
        public void Scanner_AutoInspectsOnEntry()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 2, CellType.Scanner, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            Assert.IsFalse(p.Inspected);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected); // 불량이므로 거부
        }

        [Test]
        public void Scanner_DoesNotReinspect()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 2, CellType.Scanner, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(false); // 사람이 먼저 승인했다면 스캐너는 뒤집지 않는다
            sim.Tick(1f);
            Assert.IsFalse(p.Rejected);
        }

        [Test]
        public void ReturnDock_PaysForRejectedBad()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 2, CellType.Conveyor, Direction.North);
            grid.Place(1, 3, CellType.Conveyor, Direction.North);
            for (int x = 1; x <= 4; x++) grid.Place(x, 4, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(true);
            DeliveryOutcome seen = DeliveryOutcome.Delivered; int seenDelta = 0;
            sim.PackageDelivered += (pk, o, d) => { seen = o; seenDelta = d; };
            for (int i = 0; i < 8; i++) sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(5, economy.Money);
            Assert.AreEqual(DeliveryOutcome.ReturnedBad, seen);
            Assert.AreEqual(5, seenDelta);
            Assert.AreEqual(1, sim.DeliveredCount);
        }

        [Test]
        public void Outlet_UninspectedGoesNegative()
        {
            var sim = NewSim(0.0);
            for (int x = 1; x <= 4; x++) grid.Place(x, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
            for (int i = 0; i < 5; i++) sim.Tick(1f);
            Assert.AreEqual(-10, economy.Money);
            Assert.AreEqual(0, sim.DeliveredCount);
        }

        [Test]
        public void Take_RemovesPackageAndFreesCell()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 2, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreSame(p, sim.PackageAt(1, 2));
            sim.Take(p);
            Assert.IsNull(sim.PackageAt(1, 2));
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(-1, grid.Get(1, 2).OccupantId);
            Assert.AreEqual(0, sim.LostCount);
            Assert.Throws<InvalidOperationException>(() => sim.Take(p));
        }

        [Test]
        public void PlaceCarried_PutsPackageOnTrackWithCellHeading()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 1, CellType.Conveyor, Direction.North);
            var p = new PackageFactory(new Random(1), 0.0).Create(99, 0, 0, Direction.East);
            sim.PlaceCarried(p, 1, 1);
            Assert.AreSame(p, sim.PackageAt(1, 1));
            Assert.AreEqual(Direction.North, p.Heading);
            Assert.AreEqual(Direction.North, p.EntryHeading);
            Assert.AreEqual(0f, p.Progress);
            Assert.AreEqual(99, grid.Get(1, 1).OccupantId);
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(p, 1, 1)); // 점유 중
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(p, 2, 2)); // 트랙 아님
        }

        [Test]
        public void PlaceCarried_OnScanner_InspectsImmediately()
        {
            var sim = NewSim(1.0);
            grid.Place(1, 1, CellType.Scanner, Direction.East);
            var p = new PackageFactory(new Random(1), 1.0).Create(7, 0, 0, Direction.East);
            Assert.IsFalse(p.Inspected);
            sim.PlaceCarried(p, 1, 1);
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected);
        }

        [Test]
        public void CarryPrimitives_RejectPackageStillOnLine()
        {
            var sim = NewSim(0.0);
            grid.Place(1, 2, CellType.Conveyor, Direction.East);
            grid.Place(1, 1, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            Assert.Throws<InvalidOperationException>(() => sim.PlaceCarried(p, 1, 1));
            Assert.Throws<InvalidOperationException>(() => sim.DeliverCarried(p, CellType.Outlet));
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual(0, economy.Money);
        }

        [Test]
        public void DeliverCarried_AppliesScoring()
        {
            var sim = NewSim(0.0);
            var p = new PackageFactory(new Random(1), 0.0).Create(5, 0, 0, Direction.East);
            p.Inspect(false);
            Assert.AreEqual(15, sim.DeliverCarried(p, CellType.Outlet));
            Assert.AreEqual(15, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
            var q = new PackageFactory(new Random(1), 0.0).Create(6, 0, 0, Direction.East);
            Assert.AreEqual(-5, sim.DeliverCarried(q, CellType.ReturnDock));
            Assert.AreEqual(10, economy.Money);
            Assert.Throws<ArgumentException>(() => sim.DeliverCarried(q, CellType.Conveyor));
        }
    }
}
```

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Package.cs` 전체:
```csharp
namespace ParcelFactory.Core
{
    public enum PackageKind { Normal }

    /// <summary>택배 라벨. 불량이면 저울 실측(Scale)이 표기 무게(Weight)와 다르다.</summary>
    public sealed class PackageLabel
    {
        public string Destination;
        public int Weight;
        public string Method;
        public int Scale;

        public bool Mismatch => Scale != Weight;
    }

    public sealed class Package
    {
        public int Id;
        public PackageKind Kind;
        public int X;
        public int Y;
        /// <summary>현재 셀 안에서의 진행도 0~1.</summary>
        public float Progress;
        /// <summary>현재 셀에서 나갈 방향.</summary>
        public Direction Heading;
        /// <summary>현재 셀에 들어올 때의 이동 방향. 렌더링에서 코너 보간에 쓴다.</summary>
        public Direction EntryHeading;

        /// <summary>실제 불량 여부. 겉으로는 알 수 없고 검사로만 드러난다.</summary>
        public bool Bad;
        public bool Inspected;
        /// <summary>검사 결과 거부됨. Inspected가 true일 때만 의미 있다.</summary>
        public bool Rejected;
        public PackageLabel Label;

        public void Inspect(bool reject)
        {
            Inspected = true;
            Rejected = reject;
        }
    }
}
```

`Assets/Scripts/Core/PackageFactory.cs`:
```csharp
using System;

namespace ParcelFactory.Core
{
    /// <summary>택배를 만든다. 난수를 주입받아 테스트에서 결정적으로 쓸 수 있다.</summary>
    public sealed class PackageFactory
    {
        public const double BadChance = 0.3;
        private static readonly string[] Destinations = { "서울", "부산", "대구", "광주", "인천" };
        private static readonly string[] Methods = { "일반", "익일", "당일" };

        private readonly Random rng;
        private readonly double badChance;

        public PackageFactory(Random rng, double badChance = BadChance)
        {
            this.rng = rng ?? new Random();
            this.badChance = badChance;
        }

        public Package Create(int id, int x, int y, Direction heading)
        {
            bool bad = rng.NextDouble() < badChance;
            int weight = 1 + rng.Next(8);
            return new Package
            {
                Id = id,
                Kind = PackageKind.Normal,
                X = x,
                Y = y,
                Progress = 0f,
                Heading = heading,
                EntryHeading = heading,
                Bad = bad,
                Label = new PackageLabel
                {
                    Destination = Destinations[rng.Next(Destinations.Length)],
                    Weight = weight,
                    Method = Methods[rng.Next(Methods.Length)],
                    Scale = bad ? weight + 3 + rng.Next(5) : weight,
                },
            };
        }
    }
}
```

`Assets/Scripts/Core/DeliveryScoring.cs`:
```csharp
using System;

namespace ParcelFactory.Core
{
    public enum DeliveryOutcome
    {
        Delivered,            // 승인·정상 → 출고구 +15
        ReturnedBad,          // 거부·불량 → 반품구 +5
        UninspectedToOutlet,  // −10
        RejectedToOutlet,     // −10
        BadToOutlet,          // 승인·불량 → 출고구 −20
        UninspectedToReturn,  // −5
        GoodToReturn,         // 거부·정상 → 반품구 −5
        ApprovedToReturn,     // 승인 → 반품구 −5
    }

    /// <summary>스펙 9.3 판정표.</summary>
    public static class DeliveryScoring
    {
        public static DeliveryOutcome Outcome(Package p, CellType dock)
        {
            switch (dock)
            {
                case CellType.Outlet:
                    if (!p.Inspected) return DeliveryOutcome.UninspectedToOutlet;
                    if (p.Rejected) return DeliveryOutcome.RejectedToOutlet;
                    return p.Bad ? DeliveryOutcome.BadToOutlet : DeliveryOutcome.Delivered;
                case CellType.ReturnDock:
                    if (!p.Inspected) return DeliveryOutcome.UninspectedToReturn;
                    if (p.Rejected) return p.Bad ? DeliveryOutcome.ReturnedBad : DeliveryOutcome.GoodToReturn;
                    return DeliveryOutcome.ApprovedToReturn;
                default:
                    throw new ArgumentException($"{dock} is not a dock", nameof(dock));
            }
        }

        public static int Delta(DeliveryOutcome o)
        {
            switch (o)
            {
                case DeliveryOutcome.Delivered: return 15;
                case DeliveryOutcome.ReturnedBad: return 5;
                case DeliveryOutcome.UninspectedToOutlet: return -10;
                case DeliveryOutcome.RejectedToOutlet: return -10;
                case DeliveryOutcome.BadToOutlet: return -20;
                case DeliveryOutcome.UninspectedToReturn: return -5;
                case DeliveryOutcome.GoodToReturn: return -5;
                case DeliveryOutcome.ApprovedToReturn: return -5;
                default: throw new ArgumentOutOfRangeException(nameof(o));
            }
        }

        public static bool IsSuccess(DeliveryOutcome o) =>
            o == DeliveryOutcome.Delivered || o == DeliveryOutcome.ReturnedBad;
    }
}
```

`Assets/Scripts/Core/Economy.cs`: `Earn` 아래에 추가.
```csharp
        /// <summary>판정에 따른 증감. 잔액이 음수가 될 수 있다.</summary>
        public void Adjust(int delta)
        {
            Money += delta;
            MoneyChanged?.Invoke(Money);
        }
```

`Assets/Scripts/Core/ConveyorSim.cs` 전체:
```csharp
using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class ConveyorSim
    {
        private const float ArriveEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly PackageFactory factory;
        private readonly List<Package> packages = new List<Package>();

        public IReadOnlyList<Package> Packages => packages;
        public int NextId { get; set; } = 1;
        /// <summary>성공 판정(+15, +5) 횟수.</summary>
        public int DeliveredCount { get; set; }
        public int LostCount { get; set; }

        /// <summary>Tick 루프 중간에 발생한다. 핸들러는 시뮬레이션을 변경(생성·제거)하면 안 되며 읽기만 해야 한다.</summary>
        public event Action<Package, DeliveryOutcome, int> PackageDelivered;
        /// <summary>Tick 루프 중간에 발생한다. 핸들러는 시뮬레이션을 변경(생성·제거)하면 안 되며 읽기만 해야 한다.</summary>
        public event Action<Package> PackageLost;

        public ConveyorSim(FactoryGrid grid, Economy economy, PackageFactory factory = null)
        {
            this.grid = grid;
            this.economy = economy;
            this.factory = factory ?? new PackageFactory(new Random());
        }

        public bool TrySpawn(int x, int y, PackageKind kind, out Package package)
        {
            package = null;
            if (!grid.InBounds(x, y)) return false;
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || cell.IsOccupied || cell.Demolishing) return false;

            var heading = HeadingFor(cell, cell.Direction);
            package = factory.Create(NextId++, x, y, heading);
            package.Kind = kind;
            cell.OccupantId = package.Id;
            packages.Add(package);
            return true;
        }

        /// <summary>저장 파일에서 택배를 그대로 되살린다. Heading과 토글은 변경하지 않는다.</summary>
        public void Restore(Package package)
        {
            packages.Add(package);
            grid.Get(package.X, package.Y).OccupantId = package.Id;
        }

        public Package PackageAt(int x, int y)
        {
            foreach (var p in packages)
                if (p.X == x && p.Y == y) return p;
            return null;
        }

        /// <summary>작업자가 집는다. 소실로 세지 않는다.</summary>
        public void Take(Package p)
        {
            int i = packages.IndexOf(p);
            if (i < 0) throw new InvalidOperationException($"package {p.Id} is not on the line");
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(i);
        }

        /// <summary>작업자가 들고 있던 택배를 트랙 칸에 내려놓는다.</summary>
        public void PlaceCarried(Package p, int x, int y)
        {
            if (!grid.InBounds(x, y)) throw new InvalidOperationException($"({x},{y}) out of bounds");
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || cell.IsOccupied || cell.Demolishing)
                throw new InvalidOperationException($"cannot put package at ({x},{y})");
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            p.X = x;
            p.Y = y;
            p.Progress = 0f;
            p.Heading = HeadingFor(cell, cell.Direction);
            p.EntryHeading = p.Heading;
            cell.OccupantId = p.Id;
            packages.Add(p);
            if (cell.Type == CellType.Scanner && !p.Inspected) p.Inspect(p.Bad); // 스캐너는 놓는 순간에도 판정한다
        }

        /// <summary>작업자가 들고 있던 택배를 도크에 직접 넣는다. 판정 증감을 돌려준다.</summary>
        public int DeliverCarried(Package p, CellType dockType)
        {
            if (packages.Contains(p)) throw new InvalidOperationException($"package {p.Id} is still on the line");
            return ApplyDelivery(p, dockType);
        }

        /// <summary>분배기는 자기 방향 기준 좌·우로 번갈아 내보내되, 들어온 방향의 정반대로는 절대 내보내지 않는다.</summary>
        private static Direction HeadingFor(Cell cell, Direction incoming)
        {
            if (cell.Type != CellType.Splitter) return cell.Direction;
            var left = cell.Direction.TurnLeft();
            var right = cell.Direction.TurnRight();
            var chosen = cell.SplitterToggle ? right : left;
            var other = cell.SplitterToggle ? left : right;
            if (chosen == incoming.Opposite()) chosen = other;
            cell.SplitterToggle = !cell.SplitterToggle;
            return chosen;
        }

        public void Tick(float dt)
        {
            // 오래된 택배(앞쪽)부터 처리해 앞 칸이 비면 같은 틱에 뒤 택배가 따라올 수 있게 한다.
            for (int i = 0; i < packages.Count; i++)
            {
                var p = packages[i];
                var cell = grid.Get(p.X, p.Y);
                p.Progress = Math.Min(1f, p.Progress + cell.Speed * dt);
                if (p.Progress < 1f - ArriveEpsilon) continue;
                p.Progress = 1f;
                if (cell.Type == CellType.Table && !p.Inspected) continue; // 검사 전까지 검사대에서 대기

                int nx = p.X + p.Heading.Dx();
                int ny = p.Y + p.Heading.Dy();
                if (!grid.InBounds(nx, ny)) { Lose(i, p); i--; continue; }

                var next = grid.Get(nx, ny);
                if (next.IsDock) { Deliver(i, p, next.Type); i--; continue; }
                if (!next.IsTrack || next.Type == CellType.Inlet) { Lose(i, p); i--; continue; }
                if (next.IsOccupied || next.Demolishing) continue; // 대기

                cell.OccupantId = -1;
                next.OccupantId = p.Id;
                p.X = nx;
                p.Y = ny;
                p.Progress = 0f;
                p.EntryHeading = p.Heading;
                p.Heading = HeadingFor(next, p.Heading);
                if (next.Type == CellType.Scanner && !p.Inspected) p.Inspect(p.Bad);
            }
        }

        public void RemovePackagesAt(int x, int y)
        {
            for (int i = packages.Count - 1; i >= 0; i--)
                if (packages[i].X == x && packages[i].Y == y)
                    Lose(i, packages[i]);
        }

        private int ApplyDelivery(Package p, CellType dockType)
        {
            var outcome = DeliveryScoring.Outcome(p, dockType);
            int delta = DeliveryScoring.Delta(outcome);
            economy.Adjust(delta);
            if (DeliveryScoring.IsSuccess(outcome)) DeliveredCount++;
            PackageDelivered?.Invoke(p, outcome, delta);
            return delta;
        }

        private void Deliver(int index, Package p, CellType dockType)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            ApplyDelivery(p, dockType);
        }

        private void Lose(int index, Package p)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            LostCount++;
            PackageLost?.Invoke(p);
        }
    }
}
```

`Assets/Scripts/Core/GameSession.cs`: 생성자를 바꾼다(Task 4에서 Worker를 더한다).
```csharp
        public GameSession() : this(new Economy(), null) { }

        public GameSession(Economy economy) : this(economy, null) { }

        public GameSession(Economy economy, PackageFactory factory)
        {
            Grid = new FactoryGrid();
            Economy = economy;
            Sim = new ConveyorSim(Grid, Economy, factory);
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, Economy, Toolbar, Sim);
            Unlocks = new UnlockService(Economy, Grid, Toolbar);
        }
```

- [ ] **Step 4: 통과 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0`. `InspectionTests` 13개, `Adjust_AllowsNegativeBalance`, 수정된 `GameSessionTests` 2개 포함. `SaveSerializerTests`는 그대로 통과해야 한다(택배 필드가 늘었지만 아직 저장하지 않음).

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core Assets/Tests/Core
git commit -m "feat(core): package labels, inspection table/scanner, delivery scoring, carry primitives"
```

---

### Task 3: 작업자

**Files:**
- Create: `Assets/Scripts/Core/Worker.cs`
- Create: `Assets/Tests/Core/WorkerTests.cs`

**Interfaces:**
- Consumes: `ConveyorSim.PackageAt/Take/PlaceCarried/DeliverCarried`, `BuildService.TryPlace/BeginDemolish`, `Tool`.
- Produces:
  - `class Worker { int X; int Y; Direction Facing; Package Carrying; }`
  - `enum InteractResult { PickedUp, PutDown, Delivered, InspectionRequired, Placed, NoFloor, CannotPutHere, NoTool, Locked, Occupied, NotOwned, NotEnoughMoney }`
  - `class WorkerService { const int StartX=2, StartY=4; Worker Worker; Package PendingInspection; WorkerService(FactoryGrid, ConveyorSim, BuildService); (int x,int y) FacingTile(); bool CanWalk(int x,int y); bool TryMove(Direction); InteractResult Interact(Tool? tool); bool Inspect(bool reject); void CancelInspection(); BuildResult TryDemolishFacing(); }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/WorkerTests.cs`:
```csharp
using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class WorkerTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private ConveyorSim sim;
        private Toolbar toolbar;
        private BuildService build;
        private WorkerService ws;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(200);
            sim = new ConveyorSim(grid, economy, new PackageFactory(new Random(1), 0.0));
            toolbar = new Toolbar();
            build = new BuildService(grid, economy, toolbar, sim);
            ws = new WorkerService(grid, sim, build);
        }

        [Test]
        public void StartsAt2_4FacingSouth()
        {
            Assert.AreEqual((2, 4), (ws.Worker.X, ws.Worker.Y));
            Assert.AreEqual(Direction.South, ws.Worker.Facing);
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreEqual((2, 3), ws.FacingTile());
        }

        [Test]
        public void Move_WalksOnlyOnEmptyFloor()
        {
            Assert.IsTrue(ws.TryMove(Direction.North));
            Assert.AreEqual((2, 5), (ws.Worker.X, ws.Worker.Y));
            Assert.IsFalse(ws.TryMove(Direction.North)); // (2,6)은 잠긴 칸
            Assert.AreEqual((2, 5), (ws.Worker.X, ws.Worker.Y));
            Assert.AreEqual(Direction.North, ws.Worker.Facing);
            grid.Place(1, 5, CellType.Conveyor, Direction.East);
            Assert.IsFalse(ws.TryMove(Direction.West)); // 레일 위로는 못 감
            Assert.AreEqual(Direction.West, ws.Worker.Facing); // 몸은 돌아감
            Assert.IsFalse(ws.CanWalk(0, 2)); // 입고구
            Assert.IsFalse(ws.CanWalk(-1, 0));
        }

        [Test]
        public void Interact_PicksUpFromInlet_AndPutsDownOnBelt()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South; // 입고구 (0,2)를 바라봄
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(p, ws.Worker.Carrying);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(-1, grid.Get(0, 2).OccupantId);

            grid.Place(1, 3, CellType.Conveyor, Direction.East);
            ws.Worker.Facing = Direction.East; // (1,3)
            Assert.AreEqual(InteractResult.PutDown, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreSame(p, sim.PackageAt(1, 3));
            Assert.AreEqual(Direction.East, p.Heading);
        }

        [Test]
        public void Interact_CarryingToDock_DeliversWithScoring()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            p.Inspect(false);
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            ws.Worker.X = 4; ws.Worker.Y = 2; ws.Worker.Facing = Direction.East; // 출고구 (5,2)
            Assert.AreEqual(InteractResult.Delivered, ws.Interact(null));
            Assert.IsNull(ws.Worker.Carrying);
            Assert.AreEqual(215, economy.Money);
            Assert.AreEqual(1, sim.DeliveredCount);
        }

        [Test]
        public void Interact_CarryingOntoFloorOrOccupied_Fails()
        {
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
            ws.Worker.X = 0; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            ws.Interact(null);
            ws.Worker.Facing = Direction.East; // (1,3) 빈 바닥
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(null));
            Assert.IsNotNull(ws.Worker.Carrying);
            grid.Place(1, 3, CellType.Conveyor, Direction.East);
            Assert.IsTrue(sim.TrySpawn(1, 3, PackageKind.Normal, out _));
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(null)); // 점유 중
            ws.Worker.Facing = Direction.North; // (0,4) 빈 바닥 — 운반 중엔 설치도 불가
            Assert.AreEqual(InteractResult.CannotPutHere, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void Interact_OnTable_RequiresInspection_ThenPicksUp()
        {
            grid.Place(1, 2, CellType.Table, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            ws.Worker.X = 1; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Interact(null));
            Assert.AreSame(p, ws.PendingInspection);
            Assert.IsNull(ws.Worker.Carrying);
            Assert.IsTrue(ws.Inspect(true));
            Assert.IsNull(ws.PendingInspection);
            Assert.IsTrue(p.Inspected);
            Assert.IsTrue(p.Rejected);
            Assert.IsFalse(ws.Inspect(false)); // 대기 중인 검사가 없음
            Assert.AreEqual(InteractResult.PickedUp, ws.Interact(null));
            Assert.AreSame(p, ws.Worker.Carrying);
        }

        [Test]
        public void Inspect_FailsIfPackageLeftTable()
        {
            grid.Place(1, 2, CellType.Table, Direction.East);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var p));
            sim.Tick(1f);
            ws.Worker.X = 1; ws.Worker.Y = 3; ws.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, ws.Interact(null));
            sim.Take(p); // 그 사이 택배가 사라짐
            Assert.IsFalse(ws.Inspect(false));
            Assert.IsNull(ws.PendingInspection);
            Assert.IsFalse(p.Inspected);
        }

        [Test]
        public void Interact_PlacesToolOnFacingTile()
        {
            Assert.AreEqual(InteractResult.NoTool, ws.Interact(null));
            Assert.AreEqual(InteractResult.NotOwned, ws.Interact(new Tool(CellType.Conveyor, Direction.North)));
            Assert.AreEqual(InteractResult.Placed, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            Assert.AreEqual(CellType.Conveyor, grid.Get(2, 3).Type);
            Assert.AreEqual(190, economy.Money);
            Assert.AreEqual(InteractResult.Occupied, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            grid.Clear(2, 3);
            toolbar.Add(CellType.Conveyor, Direction.North);
            Assert.AreEqual(InteractResult.Placed, ws.Interact(new Tool(CellType.Conveyor, Direction.North)));
            Assert.AreEqual(Direction.North, grid.Get(2, 3).Direction);
            ws.Worker.X = 5; ws.Worker.Y = 5; ws.Worker.Facing = Direction.East; // (6,5) 잠김
            Assert.AreEqual(InteractResult.Locked, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            ws.Worker.Facing = Direction.North; // (5,6) 잠김
            Assert.AreEqual(InteractResult.Locked, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
            var poor = new WorkerService(grid, sim, new BuildService(grid, new Economy(5), toolbar, sim));
            poor.Worker.X = 3; poor.Worker.Y = 3; poor.Worker.Facing = Direction.East; // (4,3) 빈 바닥
            Assert.AreEqual(InteractResult.NotEnoughMoney, poor.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void Interact_OffGrid_IsNoFloor()
        {
            ws.Worker.X = 0; ws.Worker.Y = 0; ws.Worker.Facing = Direction.West;
            Assert.AreEqual(InteractResult.NoFloor, ws.Interact(new Tool(CellType.Conveyor, Direction.East)));
        }

        [Test]
        public void DemolishFacing_DelegatesToBuild()
        {
            Assert.AreEqual(BuildResult.NotDemolishable, ws.TryDemolishFacing());
            ws.Interact(new Tool(CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Ok, ws.TryDemolishFacing());
            Assert.IsTrue(grid.Get(2, 3).Demolishing);
            Assert.AreEqual(BuildResult.AlreadyDemolishing, ws.TryDemolishFacing());
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패 (`WorkerService` 없음).

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Worker.cs`:
```csharp
namespace ParcelFactory.Core
{
    public sealed class Worker
    {
        public int X;
        public int Y;
        public Direction Facing;
        /// <summary>들고 있는 택배. 없으면 null. 라인(ConveyorSim)에는 없는 상태다.</summary>
        public Package Carrying;
    }

    public enum InteractResult
    {
        PickedUp, PutDown, Delivered, InspectionRequired, Placed,
        NoFloor, CannotPutHere, NoTool, Locked, Occupied, NotOwned, NotEnoughMoney,
    }

    /// <summary>작업자 규칙: 빈 바닥만 걷고, 바라보는 칸에 대해 집기/놓기/검사/설치/해체를 한다.</summary>
    public sealed class WorkerService
    {
        public const int StartX = 2, StartY = 4;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;
        private readonly BuildService build;

        public Worker Worker { get; } = new Worker { X = StartX, Y = StartY, Facing = Direction.South };
        /// <summary>검사대에서 검사를 기다리는 택배. 검사 카드 UI가 이를 보고 Inspect를 호출한다.</summary>
        public Package PendingInspection { get; private set; }

        public WorkerService(FactoryGrid grid, ConveyorSim sim, BuildService build)
        {
            this.grid = grid;
            this.sim = sim;
            this.build = build;
        }

        public (int x, int y) FacingTile() => (Worker.X + Worker.Facing.Dx(), Worker.Y + Worker.Facing.Dy());

        public bool CanWalk(int x, int y) => grid.InBounds(x, y) && grid.Get(x, y).Type == CellType.Empty;

        /// <summary>방향은 항상 바뀌고, 갈 수 있을 때만 이동한다.</summary>
        public bool TryMove(Direction dir)
        {
            Worker.Facing = dir;
            int nx = Worker.X + dir.Dx(), ny = Worker.Y + dir.Dy();
            if (!CanWalk(nx, ny)) return false;
            Worker.X = nx;
            Worker.Y = ny;
            return true;
        }

        /// <summary>우선순위: 운반 중이면 놓기 → 택배가 있으면 검사/집기 → 도구가 있으면 설치.</summary>
        public InteractResult Interact(Tool? tool)
        {
            var (fx, fy) = FacingTile();
            if (!grid.InBounds(fx, fy)) return InteractResult.NoFloor;
            var fc = grid.Get(fx, fy);

            if (Worker.Carrying != null)
            {
                if (fc.IsDock)
                {
                    sim.DeliverCarried(Worker.Carrying, fc.Type);
                    Worker.Carrying = null;
                    return InteractResult.Delivered;
                }
                if (fc.IsTrack && !fc.IsOccupied && !fc.Demolishing)
                {
                    sim.PlaceCarried(Worker.Carrying, fx, fy);
                    Worker.Carrying = null;
                    return InteractResult.PutDown;
                }
                return InteractResult.CannotPutHere;
            }

            var p = sim.PackageAt(fx, fy);
            if (p != null)
            {
                if (fc.Type == CellType.Table && !p.Inspected)
                {
                    PendingInspection = p;
                    return InteractResult.InspectionRequired;
                }
                sim.Take(p);
                Worker.Carrying = p;
                return InteractResult.PickedUp;
            }

            if (!tool.HasValue) return InteractResult.NoTool;
            switch (build.TryPlace(fx, fy, tool.Value.Type, tool.Value.Direction))
            {
                case BuildResult.Ok: return InteractResult.Placed;
                case BuildResult.Locked: return InteractResult.Locked;
                case BuildResult.Occupied: return InteractResult.Occupied;
                case BuildResult.NotOwned: return InteractResult.NotOwned;
                default: return InteractResult.NotEnoughMoney;
            }
        }

        /// <summary>대기 중인 검사를 확정한다. 택배가 그 사이 검사대를 떠났으면 false.</summary>
        public bool Inspect(bool reject)
        {
            var p = PendingInspection;
            PendingInspection = null;
            if (p == null) return false;
            if (sim.PackageAt(p.X, p.Y) != p || grid.Get(p.X, p.Y).Type != CellType.Table) return false;
            p.Inspect(reject);
            return true;
        }

        public void CancelInspection() => PendingInspection = null;

        public BuildResult TryDemolishFacing()
        {
            var (fx, fy) = FacingTile();
            return build.BeginDemolish(fx, fy);
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0`, `WorkerTests` 10개 포함.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core/Worker.cs Assets/Tests/Core/WorkerTests.cs
git commit -m "feat(core): worker movement, carrying, inspection and placement"
```

---

### Task 4: 툴바·해금·세션·저장 v2

**Files:**
- Modify: `Assets/Scripts/Core/Toolbar.cs`, `Assets/Scripts/Core/Unlocks.cs`, `Assets/Scripts/Core/GameSession.cs`, `Assets/Scripts/Core/GameState.cs`, `Assets/Scripts/Core/SaveSerializer.cs`
- Modify: `Assets/Tests/Core/ToolbarTests.cs`, `Assets/Tests/Core/UnlockTests.cs`, `Assets/Tests/Core/SaveSerializerTests.cs`, `Assets/Tests/Core/GameSessionTests.cs`

**Interfaces:**
- Produces:
  - `Toolbar.TableOwned`(기본 true), `Toolbar.ScannerOwned`; `Owns/Add`가 Table·Scanner를 다룸; `OwnedTools()` 순서: 레일·고속(정렬) → Table 4방향 → Splitter 4방향 → Scanner 4방향.
  - `UnlockId.Scanner`(가격 250, 이름 "자동 검사기"), 카탈로그 11개.
  - `GameSession.Worker : WorkerService`.
  - `SaveSerializer.CurrentVersion = 2`; `GameState` 추가: `int DeliveredCount, LostCount; WorkerState Worker`; `PackageState` 추가: `bool Bad, Inspected, Rejected; string LabelDestination, LabelMethod; int LabelWeight, LabelScale`; `class WorkerState { int X, Y; string Facing; PackageState Carrying; }`.

- [ ] **Step 1: 기존 테스트 수정**

`Assets/Tests/Core/ToolbarTests.cs`:
- `NewToolbar_OwnsOnlyEastRail`: 마지막 줄 `Assert.AreEqual(1, t.OwnedTools().Count());` → `Assert.AreEqual(5, t.OwnedTools().Count());` 로 바꾸고 그 앞에 아래 두 줄 추가:
```csharp
            Assert.IsTrue(t.Owns(CellType.Table, Direction.West));
            Assert.IsFalse(t.Owns(CellType.Scanner, Direction.East));
```
- `AddSplitter_GrantsAllDirections`: `Assert.AreEqual(5, ...)` → `Assert.AreEqual(9, t.OwnedTools().Count());`
- 테스트 추가:
```csharp
        [Test]
        public void AddScanner_GrantsAllDirections_AfterSplitterInOrder()
        {
            var t = new Toolbar();
            t.Add(CellType.Scanner, Direction.North);
            Assert.IsTrue(t.ScannerOwned);
            Assert.IsTrue(t.Owns(CellType.Scanner, Direction.South));
            var tools = t.OwnedTools().ToList();
            Assert.AreEqual(9, tools.Count);
            Assert.AreEqual(CellType.Conveyor, tools[0].Type);
            Assert.AreEqual(CellType.Table, tools[1].Type);
            Assert.AreEqual(CellType.Scanner, tools[5].Type);
        }
```

`Assets/Tests/Core/UnlockTests.cs`:
- `Catalog_HasTenItemsWithSpecPrices`: `Assert.AreEqual(10, UnlockCatalog.All.Count);` → `11`, 그리고 `Assert.AreEqual(250, UnlockCatalog.Get(UnlockId.Scanner).Price);` 추가. 메서드 이름은 `Catalog_HasElevenItemsWithSpecPrices`로 바꾼다.
- `RailDirection_AddsTool` 끝에 추가:
```csharp
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Scanner));
            Assert.IsTrue(toolbar.ScannerOwned);
```

`Assets/Tests/Core/GameSessionTests.cs`: `NewSession_StartsPerSpec`에 추가:
```csharp
            Assert.IsNotNull(s.Worker);
            Assert.AreEqual((2, 4), (s.Worker.Worker.X, s.Worker.Worker.Y));
            Assert.IsTrue(s.Toolbar.TableOwned);
```

`Assets/Tests/Core/SaveSerializerTests.cs`:
- `BuildSample`의 첫 줄 `var s = new GameSession(new Economy(5000));` → `var s = new GameSession(new Economy(5000), new PackageFactory(new Random(11), 1.0));` 그리고 상단에 `using System;` 추가. `s.Speed = 2f;` 앞에 추가:
```csharp
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 4; s.Worker.Worker.Facing = Direction.West;
```
- `Capture_RecordsEverything`: `Assert.AreEqual(1, st.Version);` → `Assert.AreEqual(2, st.Version);` 그리고 끝에 추가:
```csharp
            Assert.AreEqual(1, st.Worker.X);
            Assert.AreEqual("West", st.Worker.Facing);
            Assert.IsNull(st.Worker.Carrying);
            Assert.IsTrue(st.Packages[0].Bad);
            Assert.IsTrue(st.Packages[0].LabelScale > st.Packages[0].LabelWeight);
```
- `RoundTrip_ThroughJson_PreservesState` 끝에 추가:
```csharp
            Assert.AreEqual((1, 4), (restored.Worker.Worker.X, restored.Worker.Worker.Y));
            Assert.AreEqual(Direction.West, restored.Worker.Worker.Facing);
            Assert.AreEqual(original.Sim.Packages[0].Bad, p.Bad);
            Assert.AreEqual(original.Sim.Packages[0].Label.Destination, p.Label.Destination);
            Assert.AreEqual(original.Sim.Packages[0].Label.Weight, p.Label.Weight);
            Assert.AreEqual(original.Sim.Packages[0].Label.Method, p.Label.Method);
            Assert.AreEqual(original.Sim.Packages[0].Label.Scale, p.Label.Scale);
            Assert.AreEqual(original.Sim.DeliveredCount, restored.Sim.DeliveredCount);
            Assert.AreEqual(original.Sim.LostCount, restored.Sim.LostCount);
```
- `WrongVersion_Throws` 아래에 추가:
```csharp
        [Test]
        public void V1Save_IsRejected()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(SaveSerializer.FromJson("{\"Version\":1}")));
        }

        [Test]
        public void RoundTrip_PreservesCarriedAndInspectedPackage()
        {
            var s = new GameSession(new Economy(500), new PackageFactory(new Random(2), 1.0));
            s.Build.TryPlace(1, 2, CellType.Table, Direction.East);
            s.Tick(3f);
            s.Tick(1f); // 택배가 검사대 (1,2)에 도착해 대기
            s.Worker.Worker.X = 1; s.Worker.Worker.Y = 3; s.Worker.Worker.Facing = Direction.South;
            Assert.AreEqual(InteractResult.InspectionRequired, s.Worker.Interact(null));
            Assert.IsTrue(s.Worker.Inspect(true));
            Assert.AreEqual(InteractResult.PickedUp, s.Worker.Interact(null));
            Assert.AreEqual(0, s.Sim.Packages.Count);

            var restored = SaveSerializer.Restore(SaveSerializer.FromJson(SaveSerializer.ToJson(SaveSerializer.Capture(s))));
            var c = restored.Worker.Worker.Carrying;
            Assert.IsNotNull(c);
            Assert.IsTrue(c.Inspected);
            Assert.IsTrue(c.Rejected);
            Assert.IsTrue(c.Bad);
            Assert.AreEqual(0, restored.Sim.Packages.Count);
            Assert.AreEqual(-1, restored.Grid.Get(1, 2).OccupantId);
            Assert.AreEqual(s.Sim.NextId, restored.Sim.NextId);
        }

        [Test]
        public void Restore_RejectsWorkerOnNonFloor()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Worker.X = 0; st.Worker.Y = 2; // 입고구
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            st.Worker.X = 99;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
            var st2 = SaveSerializer.Capture(new GameSession());
            st2.Worker = null;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st2));
        }
```

- [ ] **Step 2: 실패 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패 (`TableOwned`, `UnlockId.Scanner`, `GameSession.Worker`, `WorkerState` 없음).

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Toolbar.cs`의 `Toolbar` 클래스 전체를 교체:
```csharp
    public sealed class Toolbar
    {
        private readonly HashSet<Tool> owned = new HashSet<Tool>();

        public bool SplitterOwned { get; private set; }
        /// <summary>검사대는 시작부터 4방향 모두 보유.</summary>
        public bool TableOwned { get; private set; } = true;
        public bool ScannerOwned { get; private set; }

        public Toolbar()
        {
            owned.Add(new Tool(CellType.Conveyor, Direction.East));
        }

        public bool Owns(CellType type, Direction direction)
        {
            switch (type)
            {
                case CellType.Splitter: return SplitterOwned;
                case CellType.Table: return TableOwned;
                case CellType.Scanner: return ScannerOwned;
                default: return owned.Contains(new Tool(type, direction));
            }
        }

        public void Add(CellType type, Direction direction)
        {
            switch (type)
            {
                case CellType.Splitter: SplitterOwned = true; break;
                case CellType.Table: TableOwned = true; break;
                case CellType.Scanner: ScannerOwned = true; break;
                default: owned.Add(new Tool(type, direction)); break;
            }
        }

        public IEnumerable<Tool> OwnedTools()
        {
            foreach (var t in owned.OrderBy(t => t.Type).ThenBy(t => t.Direction))
                yield return t;
            if (TableOwned) foreach (Direction d in Enum.GetValues(typeof(Direction))) yield return new Tool(CellType.Table, d);
            if (SplitterOwned) foreach (Direction d in Enum.GetValues(typeof(Direction))) yield return new Tool(CellType.Splitter, d);
            if (ScannerOwned) foreach (Direction d in Enum.GetValues(typeof(Direction))) yield return new Tool(CellType.Scanner, d);
        }
    }
```

`Assets/Scripts/Core/Unlocks.cs`:
- enum: `..., Splitter, Scanner }`
- 카탈로그 끝에 `new UnlockDef { Id = UnlockId.Scanner, Name = "자동 검사기", Price = 250 },`
- `Apply` switch에 `case UnlockId.Scanner: toolbar.Add(CellType.Scanner, Direction.East); break;`

`Assets/Scripts/Core/GameSession.cs` 전체:
```csharp
namespace ParcelFactory.Core
{
    public sealed class GameSession
    {
        public readonly FactoryGrid Grid;
        public readonly Economy Economy;
        public readonly ConveyorSim Sim;
        public readonly Spawner Spawner;
        public readonly Toolbar Toolbar;
        public readonly BuildService Build;
        public readonly UnlockService Unlocks;
        public readonly WorkerService Worker;

        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        public GameSession() : this(new Economy(), null) { }

        public GameSession(Economy economy) : this(economy, null) { }

        public GameSession(Economy economy, PackageFactory factory)
        {
            Grid = new FactoryGrid();
            Economy = economy;
            Sim = new ConveyorSim(Grid, Economy, factory);
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, Economy, Toolbar, Sim);
            Unlocks = new UnlockService(Economy, Grid, Toolbar);
            Worker = new WorkerService(Grid, Sim, Build);
        }

        public void Tick(float dt)
        {
            if (Paused || dt <= 0f) return;
            float d = dt * Speed;
            Spawner.Tick(d);
            Sim.Tick(d);
            Build.Tick(d);
        }
    }
}
```

`Assets/Scripts/Core/GameState.cs` 전체:
```csharp
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class GameState
    {
        /// <summary>기본값 0. Capture가 명시적으로 채우므로 필드가 없는 JSON은 버전 불일치로 거부된다.</summary>
        public int Version;
        public int Money;
        public int UnlockedSize;
        public List<string> Purchased = new List<string>();
        public List<CellState> Cells = new List<CellState>();
        public List<PackageState> Packages = new List<PackageState>();
        public float SpawnTimer;
        public int NextPackageId;
        public int DeliveredCount;
        public int LostCount;
        public bool Paused;
        public float Speed;
        public WorkerState Worker;
    }

    public sealed class CellState
    {
        public int X;
        public int Y;
        public string Type;
        public string Direction;
        public bool Demolishing;
        public float DemolishRemaining;
        public bool SplitterToggle;
    }

    public sealed class PackageState
    {
        public int Id;
        public string Kind;
        public int X;
        public int Y;
        public float Progress;
        public string Heading;
        public string EntryHeading;
        public bool Bad;
        public bool Inspected;
        public bool Rejected;
        public string LabelDestination;
        public int LabelWeight;
        public string LabelMethod;
        public int LabelScale;
    }

    public sealed class WorkerState
    {
        public int X;
        public int Y;
        public string Facing;
        /// <summary>들고 있는 택배. 없으면 null. X/Y는 무시된다.</summary>
        public PackageState Carrying;
    }
}
```

`Assets/Scripts/Core/SaveSerializer.cs`: 다음 변경을 적용한다.
- `CurrentVersion = 2`.
- `Capture`: `GameState` 초기화에 `DeliveredCount = s.Sim.DeliveredCount, LostCount = s.Sim.LostCount,` 추가. 택배 루프를 헬퍼로 바꾼다:
```csharp
            foreach (var p in s.Sim.Packages)
                st.Packages.Add(ToState(p));
            var w = s.Worker.Worker;
            st.Worker = new WorkerState { X = w.X, Y = w.Y, Facing = w.Facing.ToString(), Carrying = w.Carrying == null ? null : ToState(w.Carrying) };
```
- 클래스에 헬퍼 두 개 추가:
```csharp
        private static PackageState ToState(Package p) => new PackageState
        {
            Id = p.Id, Kind = p.Kind.ToString(), X = p.X, Y = p.Y,
            Progress = p.Progress, Heading = p.Heading.ToString(), EntryHeading = p.EntryHeading.ToString(),
            Bad = p.Bad, Inspected = p.Inspected, Rejected = p.Rejected,
            LabelDestination = p.Label?.Destination, LabelWeight = p.Label?.Weight ?? 0,
            LabelMethod = p.Label?.Method, LabelScale = p.Label?.Scale ?? 0,
        };

        private static Package FromState(PackageState p)
        {
            if (p == null) throw new SaveVersionException("null package");
            if (float.IsNaN(p.Progress) || p.Progress < 0f || p.Progress > 1f)
                throw new SaveVersionException($"package {p.Id} invalid progress {p.Progress}");
            if (p.Id < 0) throw new SaveVersionException($"package id {p.Id} < 0");
            return new Package
            {
                Id = p.Id,
                Kind = ParseDefined<PackageKind>(p.Kind),
                X = p.X, Y = p.Y,
                Progress = p.Progress,
                Heading = ParseDefined<Direction>(p.Heading),
                EntryHeading = ParseDefined<Direction>(p.EntryHeading),
                Bad = p.Bad, Inspected = p.Inspected, Rejected = p.Rejected,
                Label = new PackageLabel { Destination = p.LabelDestination ?? "", Weight = p.LabelWeight, Method = p.LabelMethod ?? "", Scale = p.LabelScale },
            };
        }
```
- `RestoreUnchecked`의 택배 루프를 교체:
```csharp
            int maxId = -1;
            foreach (var p in st.Packages)
            {
                if (!s.Grid.InBounds(p.X, p.Y))
                    throw new SaveVersionException($"package {p.Id} out of bounds ({p.X},{p.Y})");
                var target = s.Grid.Get(p.X, p.Y);
                if (!target.IsTrack || target.IsOccupied)
                    throw new SaveVersionException($"package {p.Id} on invalid cell ({p.X},{p.Y})");
                s.Sim.Restore(FromState(p));
                if (p.Id > maxId) maxId = p.Id;
            }

            if (st.Worker == null) throw new SaveVersionException("missing worker");
            if (!s.Grid.InBounds(st.Worker.X, st.Worker.Y) || s.Grid.Get(st.Worker.X, st.Worker.Y).Type != CellType.Empty)
                throw new SaveVersionException($"worker on invalid cell ({st.Worker.X},{st.Worker.Y})");
            s.Worker.Worker.X = st.Worker.X;
            s.Worker.Worker.Y = st.Worker.Y;
            s.Worker.Worker.Facing = ParseDefined<Direction>(st.Worker.Facing);
            if (st.Worker.Carrying != null)
            {
                var carried = FromState(st.Worker.Carrying);
                if (carried.Id > maxId) maxId = carried.Id;
                s.Worker.Worker.Carrying = carried;
            }
            if (st.NextPackageId <= maxId)
                throw new SaveVersionException($"NextPackageId {st.NextPackageId} <= package id {maxId}");
            if (st.NextPackageId < 1) throw new SaveVersionException($"NextPackageId {st.NextPackageId} < 1");

            s.Sim.NextId = st.NextPackageId;
            s.Sim.DeliveredCount = st.DeliveredCount;
            s.Sim.LostCount = st.LostCount;
```
(기존의 `if (st.NextPackageId <= p.Id) throw ...` 줄과 `s.Sim.NextId = st.NextPackageId;` 줄은 위 코드로 대체된다. `Spawner.Timer`, `Paused`, `Speed` 대입과 `return s;`는 그대로 둔다.)

- [ ] **Step 4: 통과 확인**

Run: `~/.dotnet/dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0`. 새 테스트 `AddScanner_GrantsAllDirections_AfterSplitterInOrder`, `V1Save_IsRejected`, `RoundTrip_PreservesCarriedAndInspectedPackage`, `Restore_RejectsWorkerOnNonFloor` 포함.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core Assets/Tests/Core
git commit -m "feat(core): scanner unlock, table tools, worker in session, save format v2"
```

---

### Task 5: 최종 확인

- [ ] **Step 1: 전체 테스트**

```bash
~/.dotnet/dotnet test DotnetTests/Core.Tests
```
Expected: `Failed: 0`, 경고 0.

- [ ] **Step 2: 스펙 대조**

9.2·9.3·9.5의 각 문장에 대응하는 테스트가 있는지 확인하고, 없으면 추가한다. 특히: 운반 중 설치 불가(`Interact_CarryingOntoFloorOrOccupied_Fails`), 판정표 10칸(`InspectionTests` 2개), 검사대 정지·재개, 자동 검사기, 반품구 고정, 저장 v2·v1 거부.

- [ ] **Step 3: 작업 트리 확인**

```bash
git status --short
```
Expected: 출력 없음.
