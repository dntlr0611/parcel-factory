# Parcel Factory 1차 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 공장 1개에서 입고 → 레일 → 출고로 택배를 운반해 돈을 벌고, 방향별 레일·고속 레일·분배기·부지를 해금하는 2D PC 게임의 1차 버전을 만든다.

**Architecture:** 순수 C# 코어(`Assets/Scripts/Core`)가 그리드·택배 이동·경제·설치/해체·해금·저장을 담당하고 UnityEngine을 참조하지 않는다. Unity 레이어(`Assets/Scripts/Game`)는 코어를 읽어 Tilemap과 스프라이트로 그리고, 마우스 입력과 IMGUI HUD를 코어 서비스 호출로 바꾼다. 코어 테스트는 Unity 없이 `dotnet test`로 실행되며, 같은 파일이 Unity Test Framework(EditMode)에서도 돈다.

**Tech Stack:** Unity 6 LTS (6000.0.x), C# 9, .NET SDK 10 (테스트 실행용), NUnit 3.14, Newtonsoft.Json 13, Unity Tilemap, IMGUI.

**Spec:** `docs/superpowers/specs/2026-10-15-parcel-factory-design.md`

## Global Constraints

- 코어 코드는 `UnityEngine`을 참조하지 않는다. asmdef에 `noEngineReferences: true`.
- C# 언어 버전 9.0 이하만 사용 (Unity 6 호환). `record`, `switch` 식은 가능, `required`, `file` 키워드, 기본 인터페이스 구현 이상은 금지.
- NUnit은 클래식 API(`Assert.AreEqual`)만 사용. NUnit 3.14.0에 고정.
- 그리드 12×12, 원점 좌측 하단, y는 위로 증가. 입고구 (0,2), 출고구 (5,2). 초기 해금 6×6.
- 수치: 시작 잔액 100, 레일 10, 고속 레일 25, 분배기 60, 배송 +15, 환불 80%, 해체 2초, 입고 3초, 레일 1칸/초, 고속 2칸/초, 틱 0.05초.
- 해금 가격: 부지1 200, 부지2 500, 레일 남/서/북 각 50, 고속 레일 4방향 각 100, 분배기 300.
- 커밋은 태스크마다 1개 이상. 커밋 메시지 끝에 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## 파일 구조

```
parcel-factory/
  Assets/Scripts/Core/ParcelFactory.Core.asmdef
  Assets/Scripts/Core/Direction.cs        방향 enum과 오프셋·회전
  Assets/Scripts/Core/Cell.cs             CellType, Cell
  Assets/Scripts/Core/FactoryGrid.cs      12×12 셀, 해금 영역, 설치/제거
  Assets/Scripts/Core/Economy.cs          잔액
  Assets/Scripts/Core/BuildingCosts.cs    부품별 설치비
  Assets/Scripts/Core/Package.cs          PackageKind, Package
  Assets/Scripts/Core/ConveyorSim.cs      택배 이동·배송·소실
  Assets/Scripts/Core/Spawner.cs          입고 주기
  Assets/Scripts/Core/Toolbar.cs          Tool, Toolbar (보유 도구)
  Assets/Scripts/Core/BuildService.cs     설치, 해체 타이머, 환불
  Assets/Scripts/Core/Unlocks.cs          UnlockId, UnlockDef, UnlockCatalog, UnlockService
  Assets/Scripts/Core/GameSession.cs      모든 코어 객체를 묶고 Tick
  Assets/Scripts/Core/GameState.cs        저장 DTO
  Assets/Scripts/Core/SaveSerializer.cs   세션 ↔ DTO ↔ JSON
  Assets/Scripts/Game/ParcelFactory.Game.asmdef
  Assets/Scripts/Game/PlaceholderSprites.cs  런타임 생성 스프라이트
  Assets/Scripts/Game/GameBootstrap.cs       씬 없이 자동 생성되는 진입점
  Assets/Scripts/Game/GameLoop.cs            고정 20Hz 틱
  Assets/Scripts/Game/GridView.cs            Tilemap 렌더링
  Assets/Scripts/Game/PackageView.cs         택배 스프라이트 풀
  Assets/Scripts/Game/BuildController.cs     마우스 설치/해체
  Assets/Scripts/Game/HudView.cs             IMGUI HUD, 상점, 토스트, 팝업
  Assets/Scripts/Game/SaveFile.cs            save.json 읽기/쓰기
  Assets/Tests/Core/ParcelFactory.Core.Tests.asmdef
  Assets/Tests/Core/*Tests.cs
  DotnetTests/Core/ParcelFactory.Core.csproj          Assets/Scripts/Core/**/*.cs 포함
  DotnetTests/Core.Tests/ParcelFactory.Core.Tests.csproj  Assets/Tests/Core/**/*.cs 포함
```

Phase A(Task 1~9)는 Unity 없이 지금 바로 진행한다. Phase B(Task 10~16)는 Unity Hub와 Unity 6 LTS 설치 후 진행한다.

---

## Phase A — 코어 (Unity 불필요)

> 구현 완료. 리뷰 중 적용된 수정(타이머 epsilon, Grant 선행 적용, 저장 검증 강화, 분배기 역방향 금지, EntryHeading 등)은 git 이력과 `.superpowers/sdd/.../progress.md` 레저에 기록되어 있다. 아래 코드 블록은 최초 계획이며 현재 소스가 우선한다.

### Task 1: 프로젝트 골격과 테스트 러너

**Files:**
- Modify: `.gitignore`
- Create: `Assets/Scripts/Core/ParcelFactory.Core.asmdef`
- Create: `Assets/Scripts/Game/ParcelFactory.Game.asmdef`
- Create: `Assets/Tests/Core/ParcelFactory.Core.Tests.asmdef`
- Create: `DotnetTests/Core/ParcelFactory.Core.csproj`
- Create: `DotnetTests/Core.Tests/ParcelFactory.Core.Tests.csproj`
- Create: `Assets/Scripts/Core/Direction.cs`
- Test: `Assets/Tests/Core/DirectionTests.cs`

**Interfaces:**
- Produces: `enum Direction { North, East, South, West }`, 확장 메서드 `Dx()`, `Dy()`, `TurnLeft()`, `TurnRight()`.

- [ ] **Step 1: .NET SDK 확인**

Run: `dotnet --version`
Expected: `10.x.x`. 없으면 `brew install --cask dotnet-sdk` 후 새 셸에서 재확인.

- [ ] **Step 2: .gitignore 수정**

Unity가 루트에 만드는 csproj/sln만 무시하도록 바꾸고 bin/ 추가. `.gitignore`의 `*.csproj`, `*.sln` 두 줄을 아래로 교체하고 `[Bb]in/` 한 줄을 추가한다.

```gitignore
/*.csproj
/*.sln
[Bb]in/
```

- [ ] **Step 3: asmdef 3개 작성**

`Assets/Scripts/Core/ParcelFactory.Core.asmdef`:
```json
{
  "name": "ParcelFactory.Core",
  "rootNamespace": "ParcelFactory.Core",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true
}
```

`Assets/Scripts/Game/ParcelFactory.Game.asmdef`:
```json
{
  "name": "ParcelFactory.Game",
  "rootNamespace": "ParcelFactory.Game",
  "references": ["ParcelFactory.Core"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`Assets/Tests/Core/ParcelFactory.Core.Tests.asmdef`:
```json
{
  "name": "ParcelFactory.Core.Tests",
  "rootNamespace": "ParcelFactory.Core.Tests",
  "references": ["ParcelFactory.Core", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 4: .NET 프로젝트 2개 작성**

`DotnetTests/Core/ParcelFactory.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <RootNamespace>ParcelFactory.Core</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/Scripts/Core/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

`DotnetTests/Core.Tests/ParcelFactory.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Assets/Tests/Core/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Core/ParcelFactory.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: 실패하는 테스트 작성**

`Assets/Tests/Core/DirectionTests.cs`:
```csharp
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class DirectionTests
    {
        [Test]
        public void Offsets_MatchCompass()
        {
            Assert.AreEqual((0, 1), (Direction.North.Dx(), Direction.North.Dy()));
            Assert.AreEqual((1, 0), (Direction.East.Dx(), Direction.East.Dy()));
            Assert.AreEqual((0, -1), (Direction.South.Dx(), Direction.South.Dy()));
            Assert.AreEqual((-1, 0), (Direction.West.Dx(), Direction.West.Dy()));
        }

        [Test]
        public void TurnRight_RotatesClockwise()
        {
            Assert.AreEqual(Direction.East, Direction.North.TurnRight());
            Assert.AreEqual(Direction.South, Direction.East.TurnRight());
            Assert.AreEqual(Direction.West, Direction.South.TurnRight());
            Assert.AreEqual(Direction.North, Direction.West.TurnRight());
        }

        [Test]
        public void TurnLeft_RotatesCounterClockwise()
        {
            Assert.AreEqual(Direction.West, Direction.North.TurnLeft());
            Assert.AreEqual(Direction.North, Direction.East.TurnLeft());
        }
    }
}
```

- [ ] **Step 6: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `Direction` 타입 없음.

- [ ] **Step 7: Direction 구현**

`Assets/Scripts/Core/Direction.cs`:
```csharp
namespace ParcelFactory.Core
{
    public enum Direction { North, East, South, West }

    public static class DirectionExt
    {
        public static int Dx(this Direction d) => d == Direction.East ? 1 : d == Direction.West ? -1 : 0;
        public static int Dy(this Direction d) => d == Direction.North ? 1 : d == Direction.South ? -1 : 0;
        public static Direction TurnRight(this Direction d) => (Direction)(((int)d + 1) % 4);
        public static Direction TurnLeft(this Direction d) => (Direction)(((int)d + 3) % 4);
    }
}
```

- [ ] **Step 8: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 9: 커밋**

```bash
git add .gitignore Assets DotnetTests
git commit -m "chore: core/test scaffolding with Direction

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Cell과 FactoryGrid

**Files:**
- Create: `Assets/Scripts/Core/Cell.cs`
- Create: `Assets/Scripts/Core/FactoryGrid.cs`
- Test: `Assets/Tests/Core/FactoryGridTests.cs`

**Interfaces:**
- Consumes: `Direction`.
- Produces:
  - `enum CellType { Empty, Locked, Conveyor, FastConveyor, Splitter, Inlet, Outlet }`
  - `class Cell { CellType Type; Direction Direction; bool Demolishing; float DemolishRemaining; bool SplitterToggle; int OccupantId; bool IsOccupied; bool IsTrack; bool IsBuilding; float Speed; }`
  - `class FactoryGrid { const int Width=12, Height=12, InitialUnlocked=6, InletX=0, InletY=2, OutletX=5, OutletY=2; int UnlockedSize; bool InBounds(x,y); bool IsUnlocked(x,y); Cell Get(x,y); void UnlockRegion(int size); bool CanPlace(x,y); void Place(x,y,CellType,Direction); void Clear(x,y); }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/FactoryGridTests.cs`:
```csharp
using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class FactoryGridTests
    {
        [Test]
        public void NewGrid_HasInletAndOutletFixed()
        {
            var g = new FactoryGrid();
            Assert.AreEqual(CellType.Inlet, g.Get(0, 2).Type);
            Assert.AreEqual(Direction.East, g.Get(0, 2).Direction);
            Assert.AreEqual(CellType.Outlet, g.Get(5, 2).Type);
        }

        [Test]
        public void NewGrid_UnlocksOnlyInitialRegion()
        {
            var g = new FactoryGrid();
            Assert.IsTrue(g.IsUnlocked(5, 5));
            Assert.IsFalse(g.IsUnlocked(6, 0));
            Assert.IsFalse(g.IsUnlocked(0, 6));
            Assert.AreEqual(CellType.Locked, g.Get(6, 0).Type);
            Assert.AreEqual(CellType.Empty, g.Get(1, 1).Type);
        }

        [Test]
        public void CanPlace_RejectsLockedAndOccupied()
        {
            var g = new FactoryGrid();
            Assert.IsFalse(g.CanPlace(6, 0));
            Assert.IsFalse(g.CanPlace(0, 2));
            Assert.IsTrue(g.CanPlace(1, 1));
            g.Place(1, 1, CellType.Conveyor, Direction.North);
            Assert.IsFalse(g.CanPlace(1, 1));
        }

        [Test]
        public void Place_SetsTypeAndDirection()
        {
            var g = new FactoryGrid();
            g.Place(2, 3, CellType.FastConveyor, Direction.South);
            var c = g.Get(2, 3);
            Assert.AreEqual(CellType.FastConveyor, c.Type);
            Assert.AreEqual(Direction.South, c.Direction);
            Assert.AreEqual(2f, c.Speed);
            Assert.IsTrue(c.IsBuilding);
            Assert.IsTrue(c.IsTrack);
        }

        [Test]
        public void Place_OnLockedCell_Throws()
        {
            var g = new FactoryGrid();
            Assert.Throws<InvalidOperationException>(() => g.Place(6, 0, CellType.Conveyor, Direction.East));
        }

        [Test]
        public void UnlockRegion_ConvertsLockedToEmpty()
        {
            var g = new FactoryGrid();
            g.UnlockRegion(9);
            Assert.AreEqual(9, g.UnlockedSize);
            Assert.AreEqual(CellType.Empty, g.Get(8, 8).Type);
            Assert.AreEqual(CellType.Locked, g.Get(9, 0).Type);
            Assert.IsTrue(g.CanPlace(8, 8));
        }

        [Test]
        public void UnlockRegion_CannotShrinkOrExceed()
        {
            var g = new FactoryGrid();
            Assert.Throws<ArgumentOutOfRangeException>(() => g.UnlockRegion(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.UnlockRegion(13));
        }

        [Test]
        public void Clear_ResetsBuildingCell()
        {
            var g = new FactoryGrid();
            g.Place(1, 1, CellType.Conveyor, Direction.East);
            g.Get(1, 1).Demolishing = true;
            g.Get(1, 1).OccupantId = 7;
            g.Clear(1, 1);
            var c = g.Get(1, 1);
            Assert.AreEqual(CellType.Empty, c.Type);
            Assert.IsFalse(c.Demolishing);
            Assert.AreEqual(-1, c.OccupantId);
        }

        [Test]
        public void Clear_OnInletOrOutlet_Throws()
        {
            var g = new FactoryGrid();
            Assert.Throws<InvalidOperationException>(() => g.Clear(0, 2));
            Assert.Throws<InvalidOperationException>(() => g.Clear(5, 2));
        }

        [Test]
        public void Cell_Speed_Is1ForNormalTrack()
        {
            var c = new Cell { Type = CellType.Conveyor };
            Assert.AreEqual(1f, c.Speed);
            Assert.IsFalse(c.IsOccupied);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `FactoryGrid`, `CellType` 없음.

- [ ] **Step 3: Cell 구현**

`Assets/Scripts/Core/Cell.cs`:
```csharp
namespace ParcelFactory.Core
{
    public enum CellType { Empty, Locked, Conveyor, FastConveyor, Splitter, Inlet, Outlet }

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
            Type == CellType.Splitter || Type == CellType.Inlet;

        /// <summary>플레이어가 설치·해체할 수 있는 셀.</summary>
        public bool IsBuilding =>
            Type == CellType.Conveyor || Type == CellType.FastConveyor || Type == CellType.Splitter;

        public float Speed => Type == CellType.FastConveyor ? 2f : 1f;
    }
}
```

- [ ] **Step 4: FactoryGrid 구현**

`Assets/Scripts/Core/FactoryGrid.cs`:
```csharp
using System;

namespace ParcelFactory.Core
{
    public sealed class FactoryGrid
    {
        public const int Width = 12;
        public const int Height = 12;
        public const int InitialUnlocked = 6;
        public const int InletX = 0, InletY = 2;
        public const int OutletX = 5, OutletY = 2;

        private readonly Cell[,] cells = new Cell[Width, Height];

        public int UnlockedSize { get; private set; } = InitialUnlocked;

        public FactoryGrid()
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    cells[x, y] = new Cell { Type = IsUnlocked(x, y) ? CellType.Empty : CellType.Locked };

            cells[InletX, InletY].Type = CellType.Inlet;
            cells[InletX, InletY].Direction = Direction.East;
            cells[OutletX, OutletY].Type = CellType.Outlet;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool IsUnlocked(int x, int y) => InBounds(x, y) && x < UnlockedSize && y < UnlockedSize;

        public Cell Get(int x, int y) => cells[x, y];

        public void UnlockRegion(int size)
        {
            if (size < UnlockedSize || size > Width)
                throw new ArgumentOutOfRangeException(nameof(size));
            UnlockedSize = size;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (cells[x, y].Type == CellType.Locked && IsUnlocked(x, y))
                        cells[x, y].Type = CellType.Empty;
        }

        public bool CanPlace(int x, int y) => IsUnlocked(x, y) && cells[x, y].Type == CellType.Empty;

        public void Place(int x, int y, CellType type, Direction dir)
        {
            if (!CanPlace(x, y))
                throw new InvalidOperationException($"Cannot place at ({x},{y})");
            var c = cells[x, y];
            c.Type = type;
            c.Direction = dir;
            c.Demolishing = false;
            c.DemolishRemaining = 0f;
            c.SplitterToggle = false;
        }

        public void Clear(int x, int y)
        {
            var c = Get(x, y);
            if (!c.IsBuilding)
                throw new InvalidOperationException($"({x},{y}) is not a building");
            c.Type = CellType.Empty;
            c.Demolishing = false;
            c.DemolishRemaining = 0f;
            c.SplitterToggle = false;
            c.OccupantId = -1;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 13`

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Core/Cell.cs Assets/Scripts/Core/FactoryGrid.cs Assets/Tests/Core/FactoryGridTests.cs
git commit -m "feat(core): cell model and factory grid with unlock region

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Economy와 BuildingCosts

**Files:**
- Create: `Assets/Scripts/Core/Economy.cs`
- Create: `Assets/Scripts/Core/BuildingCosts.cs`
- Test: `Assets/Tests/Core/EconomyTests.cs`

**Interfaces:**
- Produces:
  - `class Economy { const int StartingMoney=100; int Money; event Action<int> MoneyChanged; Economy(int money=StartingMoney); bool CanAfford(int); bool TrySpend(int); void Earn(int); }`
  - `static class BuildingCosts { static int Cost(CellType) }` — Conveyor 10, FastConveyor 25, Splitter 60, 그 외 `ArgumentException`.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/EconomyTests.cs`:
```csharp
using System;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class EconomyTests
    {
        [Test]
        public void StartsWith100()
        {
            Assert.AreEqual(100, new Economy().Money);
        }

        [Test]
        public void TrySpend_DeductsWhenAffordable()
        {
            var e = new Economy();
            Assert.IsTrue(e.TrySpend(30));
            Assert.AreEqual(70, e.Money);
        }

        [Test]
        public void TrySpend_FailsWhenShort()
        {
            var e = new Economy(5);
            Assert.IsFalse(e.TrySpend(10));
            Assert.AreEqual(5, e.Money);
        }

        [Test]
        public void Earn_AddsAndRaisesEvent()
        {
            var e = new Economy(0);
            int seen = -1;
            e.MoneyChanged += m => seen = m;
            e.Earn(15);
            Assert.AreEqual(15, e.Money);
            Assert.AreEqual(15, seen);
        }

        [Test]
        public void NegativeAmounts_Throw()
        {
            var e = new Economy();
            Assert.Throws<ArgumentOutOfRangeException>(() => e.Earn(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.TrySpend(-1));
        }

        [Test]
        public void BuildingCosts_MatchSpec()
        {
            Assert.AreEqual(10, BuildingCosts.Cost(CellType.Conveyor));
            Assert.AreEqual(25, BuildingCosts.Cost(CellType.FastConveyor));
            Assert.AreEqual(60, BuildingCosts.Cost(CellType.Splitter));
            Assert.Throws<ArgumentException>(() => BuildingCosts.Cost(CellType.Inlet));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `Economy` 없음.

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Economy.cs`:
```csharp
using System;

namespace ParcelFactory.Core
{
    public sealed class Economy
    {
        public const int StartingMoney = 100;

        public int Money { get; private set; }

        public event Action<int> MoneyChanged;

        public Economy(int money = StartingMoney)
        {
            Money = money;
        }

        public bool CanAfford(int amount) => Money >= amount;

        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (Money < amount) return false;
            Money -= amount;
            MoneyChanged?.Invoke(Money);
            return true;
        }

        public void Earn(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Money += amount;
            MoneyChanged?.Invoke(Money);
        }
    }
}
```

`Assets/Scripts/Core/BuildingCosts.cs`:
```csharp
using System;

namespace ParcelFactory.Core
{
    public static class BuildingCosts
    {
        public static int Cost(CellType type)
        {
            switch (type)
            {
                case CellType.Conveyor: return 10;
                case CellType.FastConveyor: return 25;
                case CellType.Splitter: return 60;
                default: throw new ArgumentException($"{type} is not buildable", nameof(type));
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 19`

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core/Economy.cs Assets/Scripts/Core/BuildingCosts.cs Assets/Tests/Core/EconomyTests.cs
git commit -m "feat(core): economy and building costs

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Package와 ConveyorSim (이동·배송·소실)

**Files:**
- Create: `Assets/Scripts/Core/Package.cs`
- Create: `Assets/Scripts/Core/ConveyorSim.cs`
- Test: `Assets/Tests/Core/ConveyorSimTests.cs`

**Interfaces:**
- Consumes: `FactoryGrid`, `Cell`, `Economy`, `Direction`.
- Produces:
  - `enum PackageKind { Normal }`
  - `class Package { int Id; PackageKind Kind; int X; int Y; float Progress; Direction Heading; }`
  - `class ConveyorSim { const int DeliveryReward=15; ConveyorSim(FactoryGrid, Economy); IReadOnlyList<Package> Packages; int NextId {get;set;}; event Action<Package> PackageDelivered, PackageLost; bool TrySpawn(int x,int y,PackageKind,out Package); void Restore(Package); void Tick(float dt); void RemovePackagesAt(int x,int y); }`

이동 규칙: 택배는 셀 안에서 `Progress`가 0→1로 증가(속도 = 셀 Speed). 1에 도달하면 `Heading` 방향의 다음 칸으로 간다. 다음 칸이 출고구면 배송(+15), 트랙이 아니거나 입고구거나 범위 밖이면 소실, 점유 중이거나 해체 중이면 대기. 분배기에 들어갈 때 `Heading`은 `SplitterToggle`에 따라 좌/우로 정해지고 토글이 뒤집힌다. 한 틱에 1칸까지만 전진한다(초과 진행도는 버림).

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/ConveyorSimTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class ConveyorSimTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private ConveyorSim sim;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(0);
            sim = new ConveyorSim(grid, economy);
        }

        private void Rail(int x, int y, Direction d, CellType type = CellType.Conveyor) =>
            grid.Place(x, y, type, d);

        private Package SpawnAtInlet()
        {
            Assert.IsTrue(sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out var p));
            return p;
        }

        [Test]
        public void Spawn_PlacesPackageOnInletHeadingEast()
        {
            var p = SpawnAtInlet();
            Assert.AreEqual((0, 2), (p.X, p.Y));
            Assert.AreEqual(Direction.East, p.Heading);
            Assert.AreEqual(0f, p.Progress);
            Assert.AreEqual(p.Id, grid.Get(0, 2).OccupantId);
            Assert.AreEqual(1, sim.Packages.Count);
        }

        [Test]
        public void Spawn_FailsWhenOccupied()
        {
            SpawnAtInlet();
            Assert.IsFalse(sim.TrySpawn(0, 2, PackageKind.Normal, out _));
        }

        [Test]
        public void Spawn_FailsOnNonTrack()
        {
            Assert.IsFalse(sim.TrySpawn(1, 1, PackageKind.Normal, out _));
        }

        [Test]
        public void AfterOneSecond_MovesOneCell()
        {
            Rail(1, 2, Direction.East);
            var p = SpawnAtInlet();
            for (int i = 0; i < 20; i++) sim.Tick(0.05f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(0f, p.Progress);
            Assert.AreEqual(-1, grid.Get(0, 2).OccupantId);
            Assert.AreEqual(p.Id, grid.Get(1, 2).OccupantId);
        }

        [Test]
        public void HalfSecond_OnlyAdvancesProgress()
        {
            Rail(1, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(0.5f);
            Assert.AreEqual((0, 2), (p.X, p.Y));
            Assert.AreEqual(0.5f, p.Progress, 1e-4f);
        }

        [Test]
        public void Corner_ChangesHeading()
        {
            Rail(1, 2, Direction.North);
            Rail(1, 3, Direction.North);
            var p = SpawnAtInlet();
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(Direction.North, p.Heading);
            sim.Tick(1f);
            Assert.AreEqual((1, 3), (p.X, p.Y));
        }

        [Test]
        public void FastConveyor_MovesTwiceAsFast()
        {
            Rail(1, 2, Direction.East, CellType.FastConveyor);
            Rail(2, 2, Direction.East);
            Rail(3, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(1f);               // inlet → (1,2)
            sim.Tick(0.5f);             // fast: (1,2) → (2,2)
            Assert.AreEqual((2, 2), (p.X, p.Y));
        }

        [Test]
        public void Delivery_RemovesPackageAndPays()
        {
            for (int x = 1; x <= 4; x++) Rail(x, 2, Direction.East);
            var p = SpawnAtInlet();
            var delivered = new List<Package>();
            sim.PackageDelivered += delivered.Add;
            for (int i = 0; i < 5; i++) sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreEqual(15, economy.Money);
            Assert.AreSame(p, delivered[0]);
            Assert.AreEqual(-1, grid.Get(4, 2).OccupantId);
        }

        [Test]
        public void EndOfTrack_LosesPackage()
        {
            Rail(1, 2, Direction.East);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            var p = SpawnAtInlet();
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreSame(p, lost[0]);
            Assert.AreEqual(0, economy.Money);
        }

        [Test]
        public void OutOfBounds_LosesPackage()
        {
            Rail(1, 2, Direction.South);
            Rail(1, 1, Direction.South);
            Rail(1, 0, Direction.South);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            SpawnAtInlet();
            for (int i = 0; i < 4; i++) sim.Tick(1f);
            Assert.AreEqual(1, lost.Count);
        }

        [Test]
        public void MovingIntoInlet_LosesPackage()
        {
            Rail(1, 2, Direction.West);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            SpawnAtInlet();
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(1, lost.Count);
        }

        [Test]
        public void BlockedByOccupiedCell_Waits()
        {
            Rail(1, 2, Direction.East);
            Rail(2, 2, Direction.West); // (1,2)와 (2,2)가 서로를 막음
            var a = SpawnAtInlet();
            sim.Tick(1f);               // a → (1,2)
            Assert.IsTrue(sim.TrySpawn(2, 2, PackageKind.Normal, out var b));
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (a.X, a.Y));
            Assert.AreEqual(1f, a.Progress);
            Assert.AreEqual((2, 2), (b.X, b.Y));
            Assert.AreEqual(2, sim.Packages.Count);
        }

        [Test]
        public void BlockedByDemolishingCell_Waits()
        {
            Rail(1, 2, Direction.East);
            Rail(2, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(1f);
            grid.Get(2, 2).Demolishing = true;
            sim.Tick(1f);
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(1f, p.Progress);
        }

        [Test]
        public void Unblocked_ResumesImmediately()
        {
            Rail(1, 2, Direction.East);
            Rail(2, 2, Direction.East);
            Rail(3, 2, Direction.East);
            var p = SpawnAtInlet();
            sim.Tick(1f);
            grid.Get(2, 2).Demolishing = true;
            sim.Tick(1f);
            grid.Get(2, 2).Demolishing = false;
            sim.Tick(0.05f);
            Assert.AreEqual((2, 2), (p.X, p.Y));
        }

        [Test]
        public void Splitter_AlternatesLeftThenRight()
        {
            Rail(1, 2, Direction.East, CellType.Splitter);
            Rail(1, 3, Direction.North); // 동쪽 기준 왼쪽 = 북
            Rail(1, 1, Direction.South); // 동쪽 기준 오른쪽 = 남
            var a = SpawnAtInlet();
            sim.Tick(1f);               // a → splitter
            Assert.AreEqual(Direction.North, a.Heading);
            var b = SpawnAtInlet();
            sim.Tick(1f);               // a → (1,3), b → splitter
            Assert.AreEqual((1, 3), (a.X, a.Y));
            Assert.AreEqual(Direction.South, b.Heading);
            sim.Tick(1f);
            Assert.AreEqual((1, 1), (b.X, b.Y));
        }

        [Test]
        public void RemovePackagesAt_RaisesLost()
        {
            Rail(1, 2, Direction.East);
            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            var p = SpawnAtInlet();
            sim.Tick(1f);
            sim.RemovePackagesAt(1, 2);
            Assert.AreEqual(0, sim.Packages.Count);
            Assert.AreSame(p, lost[0]);
            Assert.AreEqual(-1, grid.Get(1, 2).OccupantId);
        }

        [Test]
        public void Restore_RegistersOccupancyAndKeepsIds()
        {
            Rail(1, 2, Direction.East);
            sim.NextId = 42;
            sim.Restore(new Package { Id = 7, Kind = PackageKind.Normal, X = 1, Y = 2, Progress = 0.25f, Heading = Direction.East });
            Assert.AreEqual(7, grid.Get(1, 2).OccupantId);
            var p = SpawnAtInlet();
            Assert.AreEqual(42, p.Id);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `ConveyorSim`, `Package` 없음.

- [ ] **Step 3: Package 구현**

`Assets/Scripts/Core/Package.cs`:
```csharp
namespace ParcelFactory.Core
{
    public enum PackageKind { Normal }

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
    }
}
```

- [ ] **Step 4: ConveyorSim 구현**

`Assets/Scripts/Core/ConveyorSim.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public sealed class ConveyorSim
    {
        public const int DeliveryReward = 15;
        private const float ArriveEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly List<Package> packages = new List<Package>();

        public IReadOnlyList<Package> Packages => packages;
        public int NextId { get; set; } = 1;

        public event Action<Package> PackageDelivered;
        public event Action<Package> PackageLost;

        public ConveyorSim(FactoryGrid grid, Economy economy)
        {
            this.grid = grid;
            this.economy = economy;
        }

        public bool TrySpawn(int x, int y, PackageKind kind, out Package package)
        {
            package = null;
            if (!grid.InBounds(x, y)) return false;
            var cell = grid.Get(x, y);
            if (!cell.IsTrack || cell.IsOccupied || cell.Demolishing) return false;

            package = new Package
            {
                Id = NextId++,
                Kind = kind,
                X = x,
                Y = y,
                Progress = 0f,
                Heading = HeadingFor(cell),
            };
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

        private static Direction HeadingFor(Cell cell)
        {
            if (cell.Type != CellType.Splitter) return cell.Direction;
            var heading = cell.SplitterToggle ? cell.Direction.TurnRight() : cell.Direction.TurnLeft();
            cell.SplitterToggle = !cell.SplitterToggle;
            return heading;
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

                int nx = p.X + p.Heading.Dx();
                int ny = p.Y + p.Heading.Dy();
                if (!grid.InBounds(nx, ny)) { Lose(i, p); i--; continue; }

                var next = grid.Get(nx, ny);
                if (next.Type == CellType.Outlet) { Deliver(i, p); i--; continue; }
                if (!next.IsTrack || next.Type == CellType.Inlet) { Lose(i, p); i--; continue; }
                if (next.IsOccupied || next.Demolishing) continue; // 대기

                cell.OccupantId = -1;
                next.OccupantId = p.Id;
                p.X = nx;
                p.Y = ny;
                p.Progress = 0f;
                p.Heading = HeadingFor(next);
            }
        }

        public void RemovePackagesAt(int x, int y)
        {
            for (int i = packages.Count - 1; i >= 0; i--)
                if (packages[i].X == x && packages[i].Y == y)
                    Lose(i, packages[i]);
        }

        private void Deliver(int index, Package p)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            economy.Earn(DeliveryReward);
            PackageDelivered?.Invoke(p);
        }

        private void Lose(int index, Package p)
        {
            grid.Get(p.X, p.Y).OccupantId = -1;
            packages.RemoveAt(index);
            PackageLost?.Invoke(p);
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 36`

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Core/Package.cs Assets/Scripts/Core/ConveyorSim.cs Assets/Tests/Core/ConveyorSimTests.cs
git commit -m "feat(core): conveyor simulation with delivery, loss, blocking and splitter

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Spawner

**Files:**
- Create: `Assets/Scripts/Core/Spawner.cs`
- Test: `Assets/Tests/Core/SpawnerTests.cs`

**Interfaces:**
- Consumes: `ConveyorSim.TrySpawn`, `FactoryGrid.InletX/InletY`.
- Produces: `class Spawner { const float Interval=3f; float Timer {get;set;}; Spawner(FactoryGrid, ConveyorSim); void Tick(float dt); }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/SpawnerTests.cs`:
```csharp
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class SpawnerTests
    {
        private FactoryGrid grid;
        private ConveyorSim sim;
        private Spawner spawner;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            sim = new ConveyorSim(grid, new Economy());
            spawner = new Spawner(grid, sim);
        }

        [Test]
        public void NothingBeforeInterval()
        {
            spawner.Tick(2.9f);
            Assert.AreEqual(0, sim.Packages.Count);
        }

        [Test]
        public void SpawnsAtInletEveryThreeSeconds()
        {
            spawner.Tick(3f);
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreEqual((0, 2), (sim.Packages[0].X, sim.Packages[0].Y));
        }

        [Test]
        public void SkipsWhenInletOccupied()
        {
            spawner.Tick(3f);
            spawner.Tick(3f); // sim은 틱하지 않았으므로 입고구가 여전히 점유됨
            Assert.AreEqual(1, sim.Packages.Count);
        }

        [Test]
        public void TimerCarriesRemainder()
        {
            spawner.Tick(3.5f);
            Assert.AreEqual(0.5f, spawner.Timer, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `Spawner` 없음.

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Spawner.cs`:
```csharp
namespace ParcelFactory.Core
{
    public sealed class Spawner
    {
        public const float Interval = 3f;

        private readonly FactoryGrid grid;
        private readonly ConveyorSim sim;

        public float Timer { get; set; }

        public Spawner(FactoryGrid grid, ConveyorSim sim)
        {
            this.grid = grid;
            this.sim = sim;
        }

        public void Tick(float dt)
        {
            Timer += dt;
            while (Timer >= Interval)
            {
                Timer -= Interval;
                sim.TrySpawn(FactoryGrid.InletX, FactoryGrid.InletY, PackageKind.Normal, out _);
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 40`

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core/Spawner.cs Assets/Tests/Core/SpawnerTests.cs
git commit -m "feat(core): inlet spawner

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Toolbar와 BuildService (설치·해체·환불)

**Files:**
- Create: `Assets/Scripts/Core/Toolbar.cs`
- Create: `Assets/Scripts/Core/BuildService.cs`
- Test: `Assets/Tests/Core/ToolbarTests.cs`
- Test: `Assets/Tests/Core/BuildServiceTests.cs`

**Interfaces:**
- Consumes: `FactoryGrid`, `Economy`, `ConveyorSim.RemovePackagesAt`, `BuildingCosts`.
- Produces:
  - `struct Tool { CellType Type; Direction Direction; Tool(CellType, Direction); }` (IEquatable)
  - `class Toolbar { Toolbar(); bool SplitterOwned; bool Owns(CellType, Direction); void Add(CellType, Direction); IEnumerable<Tool> OwnedTools(); }` — 새 툴바는 레일 동쪽만 보유. `Add(Splitter, any)`는 분배기 4방향 모두 해금.
  - `enum BuildResult { Ok, Locked, Occupied, NotOwned, NotEnoughMoney, NotDemolishable, AlreadyDemolishing }`
  - `class BuildService { const float DemolishSeconds=2f, RefundRate=0.8f; BuildService(FactoryGrid, Economy, Toolbar, ConveyorSim); event Action<int,int> CellChanged; static int RefundFor(CellType); BuildResult TryPlace(x,y,CellType,Direction); BuildResult BeginDemolish(x,y); void Tick(float dt); }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/ToolbarTests.cs`:
```csharp
using System.Linq;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class ToolbarTests
    {
        [Test]
        public void NewToolbar_OwnsOnlyEastRail()
        {
            var t = new Toolbar();
            Assert.IsTrue(t.Owns(CellType.Conveyor, Direction.East));
            Assert.IsFalse(t.Owns(CellType.Conveyor, Direction.North));
            Assert.IsFalse(t.Owns(CellType.FastConveyor, Direction.East));
            Assert.IsFalse(t.Owns(CellType.Splitter, Direction.East));
            Assert.AreEqual(1, t.OwnedTools().Count());
        }

        [Test]
        public void Add_GrantsSpecificDirection()
        {
            var t = new Toolbar();
            t.Add(CellType.FastConveyor, Direction.South);
            Assert.IsTrue(t.Owns(CellType.FastConveyor, Direction.South));
            Assert.IsFalse(t.Owns(CellType.FastConveyor, Direction.North));
        }

        [Test]
        public void AddSplitter_GrantsAllDirections()
        {
            var t = new Toolbar();
            t.Add(CellType.Splitter, Direction.East);
            Assert.IsTrue(t.SplitterOwned);
            Assert.IsTrue(t.Owns(CellType.Splitter, Direction.West));
            Assert.AreEqual(5, t.OwnedTools().Count());
        }

        [Test]
        public void OwnedTools_IsSortedByTypeThenDirection()
        {
            var t = new Toolbar();
            t.Add(CellType.Conveyor, Direction.North);
            t.Add(CellType.FastConveyor, Direction.East);
            var tools = t.OwnedTools().ToList();
            Assert.AreEqual(new Tool(CellType.Conveyor, Direction.North), tools[0]);
            Assert.AreEqual(new Tool(CellType.Conveyor, Direction.East), tools[1]);
            Assert.AreEqual(new Tool(CellType.FastConveyor, Direction.East), tools[2]);
        }
    }
}
```

`Assets/Tests/Core/BuildServiceTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class BuildServiceTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private ConveyorSim sim;
        private BuildService build;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(100);
            toolbar = new Toolbar();
            sim = new ConveyorSim(grid, economy);
            build = new BuildService(grid, economy, toolbar, sim);
        }

        [Test]
        public void Place_DeductsCostAndSetsCell()
        {
            var changed = new List<(int, int)>();
            build.CellChanged += (x, y) => changed.Add((x, y));
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(90, economy.Money);
            Assert.AreEqual(CellType.Conveyor, grid.Get(1, 2).Type);
            Assert.AreEqual((1, 2), changed[0]);
        }

        [Test]
        public void Place_RejectsLocked()
        {
            Assert.AreEqual(BuildResult.Locked, build.TryPlace(7, 7, CellType.Conveyor, Direction.East));
            Assert.AreEqual(100, economy.Money);
        }

        [Test]
        public void Place_RejectsOccupied()
        {
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            Assert.AreEqual(BuildResult.Occupied, build.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(BuildResult.Occupied, build.TryPlace(0, 2, CellType.Conveyor, Direction.East));
        }

        [Test]
        public void Place_RejectsUnownedTool()
        {
            Assert.AreEqual(BuildResult.NotOwned, build.TryPlace(1, 2, CellType.Conveyor, Direction.North));
            Assert.AreEqual(100, economy.Money);
        }

        [Test]
        public void Place_RejectsWhenBroke()
        {
            var poor = new BuildService(grid, new Economy(5), toolbar, sim);
            Assert.AreEqual(BuildResult.NotEnoughMoney, poor.TryPlace(1, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(CellType.Empty, grid.Get(1, 2).Type);
        }

        [Test]
        public void RefundFor_Is80Percent()
        {
            Assert.AreEqual(8, BuildService.RefundFor(CellType.Conveyor));
            Assert.AreEqual(20, BuildService.RefundFor(CellType.FastConveyor));
            Assert.AreEqual(48, BuildService.RefundFor(CellType.Splitter));
        }

        [Test]
        public void BeginDemolish_MarksCellWithoutRefund()
        {
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.BeginDemolish(1, 2));
            var c = grid.Get(1, 2);
            Assert.IsTrue(c.Demolishing);
            Assert.AreEqual(2f, c.DemolishRemaining);
            Assert.AreEqual(90, economy.Money);
            Assert.AreEqual(CellType.Conveyor, c.Type);
        }

        [Test]
        public void BeginDemolish_RejectsNonBuildingAndDuplicates()
        {
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(1, 1));
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(0, 2));
            Assert.AreEqual(BuildResult.NotDemolishable, build.BeginDemolish(-1, 0));
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            build.BeginDemolish(1, 2);
            Assert.AreEqual(BuildResult.AlreadyDemolishing, build.BeginDemolish(1, 2));
        }

        [Test]
        public void Demolish_CompletesAfterTwoSecondsWithRefund()
        {
            Assert.AreEqual(BuildResult.NotOwned, build.TryPlace(1, 2, CellType.FastConveyor, Direction.East));
            Assert.AreEqual(100, economy.Money);
            toolbar.Add(CellType.FastConveyor, Direction.East);
            Assert.AreEqual(BuildResult.Ok, build.TryPlace(1, 2, CellType.FastConveyor, Direction.East));
            Assert.AreEqual(75, economy.Money);

            build.BeginDemolish(1, 2);
            build.Tick(1.9f);
            Assert.AreEqual(CellType.FastConveyor, grid.Get(1, 2).Type);
            Assert.AreEqual(75, economy.Money);

            var changed = new List<(int, int)>();
            build.CellChanged += (x, y) => changed.Add((x, y));
            build.Tick(0.1f);
            Assert.AreEqual(CellType.Empty, grid.Get(1, 2).Type);
            Assert.IsFalse(grid.Get(1, 2).Demolishing);
            Assert.AreEqual(95, economy.Money);
            Assert.AreEqual((1, 2), changed[0]);
        }

        [Test]
        public void Demolish_LosesPackageStillOnCell()
        {
            build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            build.TryPlace(2, 2, CellType.Conveyor, Direction.East);
            toolbar.Add(CellType.Conveyor, Direction.West);
            build.TryPlace(3, 2, CellType.Conveyor, Direction.West);
            Assert.IsTrue(sim.TrySpawn(0, 2, PackageKind.Normal, out var a));
            sim.Tick(1f);
            sim.Tick(1f); // a → (2,2)
            Assert.IsTrue(sim.TrySpawn(3, 2, PackageKind.Normal, out var b)); // a와 b가 서로 막음
            sim.Tick(1f);
            Assert.AreEqual((2, 2), (a.X, a.Y));

            var lost = new List<Package>();
            sim.PackageLost += lost.Add;
            build.BeginDemolish(2, 2);
            sim.Tick(2f);
            build.Tick(2f);
            Assert.AreEqual(1, lost.Count);
            Assert.AreSame(a, lost[0]);
            Assert.AreEqual(1, sim.Packages.Count);
            Assert.AreSame(b, sim.Packages[0]);
            Assert.AreEqual(CellType.Empty, grid.Get(2, 2).Type);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `Toolbar`, `BuildService` 없음.

- [ ] **Step 3: Toolbar 구현**

`Assets/Scripts/Core/Toolbar.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace ParcelFactory.Core
{
    public readonly struct Tool : IEquatable<Tool>
    {
        public readonly CellType Type;
        public readonly Direction Direction;

        public Tool(CellType type, Direction direction)
        {
            Type = type;
            Direction = direction;
        }

        public bool Equals(Tool other) => Type == other.Type && Direction == other.Direction;
        public override bool Equals(object obj) => obj is Tool t && Equals(t);
        public override int GetHashCode() => ((int)Type << 2) | (int)Direction;
        public override string ToString() => $"{Type}:{Direction}";
    }

    public sealed class Toolbar
    {
        private readonly HashSet<Tool> owned = new HashSet<Tool>();

        public bool SplitterOwned { get; private set; }

        public Toolbar()
        {
            owned.Add(new Tool(CellType.Conveyor, Direction.East));
        }

        public bool Owns(CellType type, Direction direction) =>
            type == CellType.Splitter ? SplitterOwned : owned.Contains(new Tool(type, direction));

        public void Add(CellType type, Direction direction)
        {
            if (type == CellType.Splitter) SplitterOwned = true;
            else owned.Add(new Tool(type, direction));
        }

        public IEnumerable<Tool> OwnedTools()
        {
            foreach (var t in owned.OrderBy(t => t.Type).ThenBy(t => t.Direction))
                yield return t;
            if (SplitterOwned)
                foreach (Direction d in Enum.GetValues(typeof(Direction)))
                    yield return new Tool(CellType.Splitter, d);
        }
    }
}
```

- [ ] **Step 4: BuildService 구현**

`Assets/Scripts/Core/BuildService.cs`:
```csharp
using System;

namespace ParcelFactory.Core
{
    public enum BuildResult { Ok, Locked, Occupied, NotOwned, NotEnoughMoney, NotDemolishable, AlreadyDemolishing }

    public sealed class BuildService
    {
        public const float DemolishSeconds = 2f;
        public const float RefundRate = 0.8f;
        /// <summary>float 누적 오차 허용치. 1.9+0.1 같은 합이 정확히 0이 되지 않는 문제를 흡수한다.</summary>
        private const float TimerEpsilon = 1e-4f;

        private readonly FactoryGrid grid;
        private readonly Economy economy;
        private readonly Toolbar toolbar;
        private readonly ConveyorSim sim;

        public event Action<int, int> CellChanged;

        public BuildService(FactoryGrid grid, Economy economy, Toolbar toolbar, ConveyorSim sim)
        {
            this.grid = grid;
            this.economy = economy;
            this.toolbar = toolbar;
            this.sim = sim;
        }

        public static int RefundFor(CellType type) =>
            (int)Math.Round(BuildingCosts.Cost(type) * RefundRate);

        public BuildResult TryPlace(int x, int y, CellType type, Direction dir)
        {
            if (!grid.IsUnlocked(x, y)) return BuildResult.Locked;
            if (grid.Get(x, y).Type != CellType.Empty) return BuildResult.Occupied;
            if (!toolbar.Owns(type, dir)) return BuildResult.NotOwned;
            if (!economy.TrySpend(BuildingCosts.Cost(type))) return BuildResult.NotEnoughMoney;
            grid.Place(x, y, type, dir);
            CellChanged?.Invoke(x, y);
            return BuildResult.Ok;
        }

        public BuildResult BeginDemolish(int x, int y)
        {
            if (!grid.InBounds(x, y)) return BuildResult.NotDemolishable;
            var c = grid.Get(x, y);
            if (!c.IsBuilding) return BuildResult.NotDemolishable;
            if (c.Demolishing) return BuildResult.AlreadyDemolishing;
            c.Demolishing = true;
            c.DemolishRemaining = DemolishSeconds;
            CellChanged?.Invoke(x, y);
            return BuildResult.Ok;
        }

        public void Tick(float dt)
        {
            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = grid.Get(x, y);
                    if (!c.Demolishing) continue;
                    c.DemolishRemaining -= dt;
                    if (c.DemolishRemaining > TimerEpsilon) continue;
                    c.DemolishRemaining = 0f;

                    int refund = RefundFor(c.Type);
                    sim.RemovePackagesAt(x, y);
                    grid.Clear(x, y);
                    economy.Earn(refund);
                    CellChanged?.Invoke(x, y);
                }
            }
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 54`

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Core/Toolbar.cs Assets/Scripts/Core/BuildService.cs Assets/Tests/Core/ToolbarTests.cs Assets/Tests/Core/BuildServiceTests.cs
git commit -m "feat(core): toolbar ownership, placement and timed demolition with refund

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: 해금 카탈로그와 UnlockService

**Files:**
- Create: `Assets/Scripts/Core/Unlocks.cs`
- Test: `Assets/Tests/Core/UnlockTests.cs`

**Interfaces:**
- Consumes: `Economy`, `FactoryGrid.UnlockRegion`, `Toolbar.Add`.
- Produces:
  - `enum UnlockId { Plot1, Plot2, RailSouth, RailWest, RailNorth, FastEast, FastSouth, FastWest, FastNorth, Splitter }`
  - `class UnlockDef { UnlockId Id; string Name; int Price; UnlockId? Requires; }`
  - `static class UnlockCatalog { IReadOnlyList<UnlockDef> All; UnlockDef Get(UnlockId); }`
  - `enum UnlockResult { Ok, AlreadyOwned, RequirementMissing, NotEnoughMoney }`
  - `class UnlockService { UnlockService(Economy, FactoryGrid, Toolbar); IReadOnlyCollection<UnlockId> Purchased; bool IsPurchased(UnlockId); UnlockResult TryPurchase(UnlockId); void Grant(UnlockId); event Action<UnlockId> Unlocked; }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/UnlockTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class UnlockTests
    {
        private FactoryGrid grid;
        private Economy economy;
        private Toolbar toolbar;
        private UnlockService unlocks;

        [SetUp]
        public void SetUp()
        {
            grid = new FactoryGrid();
            economy = new Economy(1000);
            toolbar = new Toolbar();
            unlocks = new UnlockService(economy, grid, toolbar);
        }

        [Test]
        public void Catalog_HasTenItemsWithSpecPrices()
        {
            Assert.AreEqual(10, UnlockCatalog.All.Count);
            Assert.AreEqual(200, UnlockCatalog.Get(UnlockId.Plot1).Price);
            Assert.AreEqual(500, UnlockCatalog.Get(UnlockId.Plot2).Price);
            Assert.AreEqual(50, UnlockCatalog.Get(UnlockId.RailSouth).Price);
            Assert.AreEqual(50, UnlockCatalog.Get(UnlockId.RailWest).Price);
            Assert.AreEqual(50, UnlockCatalog.Get(UnlockId.RailNorth).Price);
            Assert.AreEqual(100, UnlockCatalog.Get(UnlockId.FastEast).Price);
            Assert.AreEqual(100, UnlockCatalog.Get(UnlockId.FastNorth).Price);
            Assert.AreEqual(300, UnlockCatalog.Get(UnlockId.Splitter).Price);
            Assert.AreEqual(UnlockId.Plot1, UnlockCatalog.Get(UnlockId.Plot2).Requires);
            Assert.IsNull(UnlockCatalog.Get(UnlockId.Plot1).Requires);
        }

        [Test]
        public void Plot1_Expands9AndDeducts()
        {
            var seen = new List<UnlockId>();
            unlocks.Unlocked += seen.Add;
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Plot1));
            Assert.AreEqual(9, grid.UnlockedSize);
            Assert.AreEqual(800, economy.Money);
            Assert.IsTrue(unlocks.IsPurchased(UnlockId.Plot1));
            Assert.AreEqual(UnlockId.Plot1, seen[0]);
        }

        [Test]
        public void Plot2_RequiresPlot1()
        {
            Assert.AreEqual(UnlockResult.RequirementMissing, unlocks.TryPurchase(UnlockId.Plot2));
            Assert.AreEqual(1000, economy.Money);
            unlocks.TryPurchase(UnlockId.Plot1);
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Plot2));
            Assert.AreEqual(12, grid.UnlockedSize);
        }

        [Test]
        public void RailDirection_AddsTool()
        {
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.RailNorth));
            Assert.IsTrue(toolbar.Owns(CellType.Conveyor, Direction.North));
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.FastWest));
            Assert.IsTrue(toolbar.Owns(CellType.FastConveyor, Direction.West));
            Assert.AreEqual(UnlockResult.Ok, unlocks.TryPurchase(UnlockId.Splitter));
            Assert.IsTrue(toolbar.SplitterOwned);
        }

        [Test]
        public void CannotBuyTwice()
        {
            unlocks.TryPurchase(UnlockId.RailSouth);
            Assert.AreEqual(UnlockResult.AlreadyOwned, unlocks.TryPurchase(UnlockId.RailSouth));
            Assert.AreEqual(950, economy.Money);
        }

        [Test]
        public void NotEnoughMoney_LeavesStateUntouched()
        {
            var poor = new UnlockService(new Economy(10), grid, toolbar);
            Assert.AreEqual(UnlockResult.NotEnoughMoney, poor.TryPurchase(UnlockId.RailSouth));
            Assert.IsFalse(poor.IsPurchased(UnlockId.RailSouth));
            Assert.IsFalse(toolbar.Owns(CellType.Conveyor, Direction.South));
        }

        [Test]
        public void Grant_AppliesWithoutPayment()
        {
            unlocks.Grant(UnlockId.Plot1);
            unlocks.Grant(UnlockId.Plot1);
            Assert.AreEqual(9, grid.UnlockedSize);
            Assert.AreEqual(1000, economy.Money);
            Assert.AreEqual(1, unlocks.Purchased.Count);
        }

        [Test]
        public void Grant_OutOfOrder_AppliesPrerequisiteFirst()
        {
            unlocks.Grant(UnlockId.Plot2);
            Assert.AreEqual(12, grid.UnlockedSize);
            Assert.IsTrue(unlocks.IsPurchased(UnlockId.Plot1));
            Assert.IsTrue(unlocks.IsPurchased(UnlockId.Plot2));
            unlocks.Grant(UnlockId.Plot1);
            Assert.AreEqual(2, unlocks.Purchased.Count);
            Assert.AreEqual(1000, economy.Money);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `UnlockCatalog` 없음.

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/Unlocks.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace ParcelFactory.Core
{
    public enum UnlockId { Plot1, Plot2, RailSouth, RailWest, RailNorth, FastEast, FastSouth, FastWest, FastNorth, Splitter }

    public sealed class UnlockDef
    {
        public UnlockId Id;
        public string Name;
        public int Price;
        public UnlockId? Requires;
    }

    public static class UnlockCatalog
    {
        public static readonly IReadOnlyList<UnlockDef> All = new[]
        {
            new UnlockDef { Id = UnlockId.Plot1, Name = "부지 확장 1 (9×9)", Price = 200 },
            new UnlockDef { Id = UnlockId.Plot2, Name = "부지 확장 2 (12×12)", Price = 500, Requires = UnlockId.Plot1 },
            new UnlockDef { Id = UnlockId.RailSouth, Name = "레일 ↓", Price = 50 },
            new UnlockDef { Id = UnlockId.RailWest, Name = "레일 ←", Price = 50 },
            new UnlockDef { Id = UnlockId.RailNorth, Name = "레일 ↑", Price = 50 },
            new UnlockDef { Id = UnlockId.FastEast, Name = "고속 레일 →", Price = 100 },
            new UnlockDef { Id = UnlockId.FastSouth, Name = "고속 레일 ↓", Price = 100 },
            new UnlockDef { Id = UnlockId.FastWest, Name = "고속 레일 ←", Price = 100 },
            new UnlockDef { Id = UnlockId.FastNorth, Name = "고속 레일 ↑", Price = 100 },
            new UnlockDef { Id = UnlockId.Splitter, Name = "분배기", Price = 300 },
        };

        public static UnlockDef Get(UnlockId id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    public enum UnlockResult { Ok, AlreadyOwned, RequirementMissing, NotEnoughMoney }

    public sealed class UnlockService
    {
        private readonly Economy economy;
        private readonly FactoryGrid grid;
        private readonly Toolbar toolbar;
        private readonly HashSet<UnlockId> purchased = new HashSet<UnlockId>();

        public IReadOnlyCollection<UnlockId> Purchased => purchased;

        public event Action<UnlockId> Unlocked;

        public UnlockService(Economy economy, FactoryGrid grid, Toolbar toolbar)
        {
            this.economy = economy;
            this.grid = grid;
            this.toolbar = toolbar;
        }

        public bool IsPurchased(UnlockId id) => purchased.Contains(id);

        public UnlockResult TryPurchase(UnlockId id)
        {
            var def = UnlockCatalog.Get(id);
            if (purchased.Contains(id)) return UnlockResult.AlreadyOwned;
            if (def.Requires.HasValue && !purchased.Contains(def.Requires.Value)) return UnlockResult.RequirementMissing;
            if (!economy.TrySpend(def.Price)) return UnlockResult.NotEnoughMoney;
            Apply(id);
            return UnlockResult.Ok;
        }

        /// <summary>
        /// 저장 복원용. 결제 없이 효과만 적용한다. 이미 보유 중이면 무시.
        /// 선행 항목이 미보유면 먼저 적용하므로 호출 순서에 의존하지 않는다.
        /// </summary>
        public void Grant(UnlockId id)
        {
            if (purchased.Contains(id)) return;
            var def = UnlockCatalog.Get(id);
            if (def.Requires.HasValue) Grant(def.Requires.Value);
            Apply(id);
        }

        private void Apply(UnlockId id)
        {
            purchased.Add(id);
            switch (id)
            {
                case UnlockId.Plot1: grid.UnlockRegion(9); break;
                case UnlockId.Plot2: grid.UnlockRegion(12); break;
                case UnlockId.RailSouth: toolbar.Add(CellType.Conveyor, Direction.South); break;
                case UnlockId.RailWest: toolbar.Add(CellType.Conveyor, Direction.West); break;
                case UnlockId.RailNorth: toolbar.Add(CellType.Conveyor, Direction.North); break;
                case UnlockId.FastEast: toolbar.Add(CellType.FastConveyor, Direction.East); break;
                case UnlockId.FastSouth: toolbar.Add(CellType.FastConveyor, Direction.South); break;
                case UnlockId.FastWest: toolbar.Add(CellType.FastConveyor, Direction.West); break;
                case UnlockId.FastNorth: toolbar.Add(CellType.FastConveyor, Direction.North); break;
                case UnlockId.Splitter: toolbar.Add(CellType.Splitter, Direction.East); break;
            }
            Unlocked?.Invoke(id);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 62`

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core/Unlocks.cs Assets/Tests/Core/UnlockTests.cs
git commit -m "feat(core): unlock catalog and purchase service

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: GameSession (일시정지·배속·틱 순서)

**Files:**
- Create: `Assets/Scripts/Core/GameSession.cs`
- Test: `Assets/Tests/Core/GameSessionTests.cs`

**Interfaces:**
- Consumes: 모든 코어 클래스.
- Produces: `class GameSession { FactoryGrid Grid; Economy Economy; ConveyorSim Sim; Spawner Spawner; Toolbar Toolbar; BuildService Build; UnlockService Unlocks; bool Paused; float Speed; GameSession(); GameSession(Economy); void Tick(float dt); }`
- 틱 순서: Spawner → Sim → Build. Paused면 아무것도 진행하지 않는다. dt에 Speed를 곱한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/GameSessionTests.cs`:
```csharp
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class GameSessionTests
    {
        [Test]
        public void NewSession_StartsPerSpec()
        {
            var s = new GameSession();
            Assert.AreEqual(100, s.Economy.Money);
            Assert.AreEqual(6, s.Grid.UnlockedSize);
            Assert.IsTrue(s.Toolbar.Owns(CellType.Conveyor, Direction.East));
            Assert.IsFalse(s.Paused);
            Assert.AreEqual(1f, s.Speed);
        }

        [Test]
        public void StraightLine_DeliversAndEarns()
        {
            var s = new GameSession();
            for (int x = 1; x <= 4; x++)
                Assert.AreEqual(BuildResult.Ok, s.Build.TryPlace(x, 2, CellType.Conveyor, Direction.East));
            Assert.AreEqual(60, s.Economy.Money);
            for (int i = 0; i < 180; i++) s.Tick(0.05f); // 9초: 3초에 생성, 5초 이동. 두 번째 택배(6초 생성)는 아직 도착 전
            Assert.AreEqual(75, s.Economy.Money);
        }

        [Test]
        public void Paused_FreezesEverything()
        {
            var s = new GameSession();
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 2);
            s.Paused = true;
            s.Tick(5f);
            Assert.AreEqual(0, s.Sim.Packages.Count);
            Assert.AreEqual(2f, s.Grid.Get(1, 2).DemolishRemaining);
            Assert.AreEqual(0f, s.Spawner.Timer);
        }

        [Test]
        public void Speed2_RunsTwiceAsFast()
        {
            var s = new GameSession { Speed = 2f };
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Tick(1.5f); // 3초 상당: 생성되고 같은 틱에 (1,2)로 이동
            Assert.AreEqual(1, s.Sim.Packages.Count);
            Assert.AreEqual((1, 2), (s.Sim.Packages[0].X, s.Sim.Packages[0].Y));
        }

        [Test]
        public void DemolishTimerAdvancesWithTick()
        {
            var s = new GameSession();
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Build.BeginDemolish(1, 2);
            s.Tick(2f);
            Assert.AreEqual(CellType.Empty, s.Grid.Get(1, 2).Type);
            Assert.AreEqual(98, s.Economy.Money);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `GameSession` 없음.

- [ ] **Step 3: 구현**

`Assets/Scripts/Core/GameSession.cs`:
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

        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        public GameSession() : this(new Economy()) { }

        public GameSession(Economy economy)
        {
            Grid = new FactoryGrid();
            Economy = economy;
            Sim = new ConveyorSim(Grid, Economy);
            Spawner = new Spawner(Grid, Sim);
            Toolbar = new Toolbar();
            Build = new BuildService(Grid, Economy, Toolbar, Sim);
            Unlocks = new UnlockService(Economy, Grid, Toolbar);
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

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 67`

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Core/GameSession.cs Assets/Tests/Core/GameSessionTests.cs
git commit -m "feat(core): game session with pause, speed and tick ordering

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: 저장 DTO와 SaveSerializer

**Files:**
- Create: `Assets/Scripts/Core/GameState.cs`
- Create: `Assets/Scripts/Core/SaveSerializer.cs`
- Test: `Assets/Tests/Core/SaveSerializerTests.cs`

**Interfaces:**
- Consumes: `GameSession`, `UnlockService.Grant`, `ConveyorSim.Restore/NextId`, `Spawner.Timer`, Newtonsoft.Json.
- Produces:
  - `class GameState { int Version; int Money; int UnlockedSize; List<string> Purchased; List<CellState> Cells; List<PackageState> Packages; float SpawnTimer; int NextPackageId; bool Paused; float Speed; }`
  - `class CellState { int X, Y; string Type; string Direction; bool Demolishing; float DemolishRemaining; bool SplitterToggle; }`
  - `class PackageState { int Id; string Kind; int X, Y; float Progress; string Heading; }`
  - `class SaveVersionException : Exception`
  - `static class SaveSerializer { const int CurrentVersion=1; GameState Capture(GameSession); GameSession Restore(GameState); string ToJson(GameState); GameState FromJson(string); }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/Core/SaveSerializerTests.cs`:
```csharp
using NUnit.Framework;
using ParcelFactory.Core;

namespace ParcelFactory.Core.Tests
{
    public class SaveSerializerTests
    {
        private static GameSession BuildSample()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.TryPurchase(UnlockId.Plot1);
            s.Unlocks.TryPurchase(UnlockId.RailNorth);
            s.Unlocks.TryPurchase(UnlockId.Splitter);
            s.Build.TryPlace(1, 2, CellType.Conveyor, Direction.East);
            s.Build.TryPlace(2, 2, CellType.Splitter, Direction.East);
            s.Build.TryPlace(2, 3, CellType.Conveyor, Direction.North);
            s.Build.TryPlace(7, 7, CellType.Conveyor, Direction.North);
            s.Tick(3f);      // 택배 1개 생성, 같은 틱에 (1,2)로 이동 (progress 0)
            s.Build.BeginDemolish(7, 7);
            s.Tick(0.5f);    // 택배 progress 0.5, DemolishRemaining 1.5, SpawnTimer 0.5
            s.Paused = true;
            s.Speed = 2f;
            return s;
        }

        [Test]
        public void Capture_RecordsEverything()
        {
            var st = SaveSerializer.Capture(BuildSample());
            Assert.AreEqual(1, st.Version);
            Assert.AreEqual(9, st.UnlockedSize);
            CollectionAssert.AreEquivalent(new[] { "Plot1", "RailNorth", "Splitter" }, st.Purchased);
            Assert.AreEqual(4, st.Cells.Count);
            Assert.AreEqual(1, st.Packages.Count);
            Assert.IsTrue(st.Paused);
            Assert.AreEqual(2f, st.Speed);
        }

        [Test]
        public void RoundTrip_ThroughJson_PreservesState()
        {
            var original = BuildSample();
            var json = SaveSerializer.ToJson(SaveSerializer.Capture(original));
            var restored = SaveSerializer.Restore(SaveSerializer.FromJson(json));

            Assert.AreEqual(original.Economy.Money, restored.Economy.Money);
            Assert.AreEqual(9, restored.Grid.UnlockedSize);
            Assert.IsTrue(restored.Toolbar.Owns(CellType.Conveyor, Direction.North));
            Assert.IsTrue(restored.Toolbar.SplitterOwned);
            Assert.IsTrue(restored.Unlocks.IsPurchased(UnlockId.Plot1));

            Assert.AreEqual(CellType.Splitter, restored.Grid.Get(2, 2).Type);
            Assert.AreEqual(Direction.North, restored.Grid.Get(2, 3).Direction);
            Assert.IsTrue(restored.Grid.Get(7, 7).Demolishing);
            Assert.AreEqual(1.5f, restored.Grid.Get(7, 7).DemolishRemaining, 1e-4f);

            Assert.AreEqual(1, restored.Sim.Packages.Count);
            var p = restored.Sim.Packages[0];
            Assert.AreEqual((1, 2), (p.X, p.Y));
            Assert.AreEqual(0.5f, p.Progress, 1e-4f);
            Assert.AreEqual(p.Id, restored.Grid.Get(1, 2).OccupantId);
            Assert.AreEqual(original.Sim.NextId, restored.Sim.NextId);
            Assert.AreEqual(original.Spawner.Timer, restored.Spawner.Timer, 1e-4f);
            Assert.IsTrue(restored.Paused);
            Assert.AreEqual(2f, restored.Speed);
        }

        [Test]
        public void RestoredSession_ContinuesSimulating()
        {
            var restored = SaveSerializer.Restore(SaveSerializer.Capture(BuildSample()));
            restored.Paused = false;
            restored.Speed = 1f;
            restored.Tick(0.5f); // progress 0.5 → 1.0, 분배기로 진입
            Assert.AreEqual((2, 2), (restored.Sim.Packages[0].X, restored.Sim.Packages[0].Y));
            Assert.AreEqual(Direction.North, restored.Sim.Packages[0].Heading);
        }

        [Test]
        public void SplitterToggle_SurvivesRoundTrip()
        {
            var s = new GameSession(new Economy(5000));
            s.Unlocks.TryPurchase(UnlockId.Splitter);
            s.Build.TryPlace(1, 2, CellType.Splitter, Direction.East);
            s.Grid.Get(1, 2).SplitterToggle = true;
            var restored = SaveSerializer.Restore(SaveSerializer.Capture(s));
            Assert.IsTrue(restored.Grid.Get(1, 2).SplitterToggle);
        }

        [Test]
        public void WrongVersion_Throws()
        {
            var st = SaveSerializer.Capture(new GameSession());
            st.Version = 99;
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(st));
        }

        [Test]
        public void GarbageJson_Throws()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson("null"));
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson("{not json"));
            Assert.Throws<SaveVersionException>(() => SaveSerializer.FromJson(null));
        }

        [Test]
        public void MissingVersion_Throws()
        {
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(SaveSerializer.FromJson("{}")));
        }

        [Test]
        public void CorruptContents_ThrowSaveVersionException()
        {
            Assert.Throws<SaveVersionException>(() =>
                SaveSerializer.Restore(SaveSerializer.FromJson("{\"Version\":1,\"Cells\":null}")));
            Assert.Throws<SaveVersionException>(() =>
                SaveSerializer.Restore(SaveSerializer.FromJson("{\"Version\":1,\"Purchased\":[\"Bogus\"]}")));

            var outOfBounds = SaveSerializer.Capture(new GameSession());
            outOfBounds.Packages.Add(new PackageState { Id = 1, Kind = "Normal", X = 99, Y = 0, Progress = 0f, Heading = "East" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(outOfBounds));

            var onEmptyCell = SaveSerializer.Capture(new GameSession());
            onEmptyCell.Packages.Add(new PackageState { Id = 1, Kind = "Normal", X = 1, Y = 1, Progress = 0f, Heading = "East" });
            Assert.Throws<SaveVersionException>(() => SaveSerializer.Restore(onEmptyCell));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: 빌드 실패, `SaveSerializer` 없음.

- [ ] **Step 3: GameState 구현**

`Assets/Scripts/Core/GameState.cs`:
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
        public bool Paused;
        public float Speed;
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
    }
}
```

- [ ] **Step 4: SaveSerializer 구현**

`Assets/Scripts/Core/SaveSerializer.cs`:
```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ParcelFactory.Core
{
    public sealed class SaveVersionException : Exception
    {
        public SaveVersionException(string message) : base(message) { }
        public SaveVersionException(string message, Exception inner) : base(message, inner) { }
    }

    public static class SaveSerializer
    {
        public const int CurrentVersion = 1;

        public static GameState Capture(GameSession s)
        {
            var st = new GameState
            {
                Version = CurrentVersion,
                Money = s.Economy.Money,
                UnlockedSize = s.Grid.UnlockedSize,
                SpawnTimer = s.Spawner.Timer,
                NextPackageId = s.Sim.NextId,
                Paused = s.Paused,
                Speed = s.Speed,
            };

            foreach (var id in s.Unlocks.Purchased)
                st.Purchased.Add(id.ToString());

            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = s.Grid.Get(x, y);
                    if (!c.IsBuilding) continue;
                    st.Cells.Add(new CellState
                    {
                        X = x, Y = y,
                        Type = c.Type.ToString(),
                        Direction = c.Direction.ToString(),
                        Demolishing = c.Demolishing,
                        DemolishRemaining = c.DemolishRemaining,
                        SplitterToggle = c.SplitterToggle,
                    });
                }
            }

            foreach (var p in s.Sim.Packages)
            {
                st.Packages.Add(new PackageState
                {
                    Id = p.Id, Kind = p.Kind.ToString(), X = p.X, Y = p.Y,
                    Progress = p.Progress, Heading = p.Heading.ToString(),
                });
            }
            return st;
        }

        public static GameSession Restore(GameState st)
        {
            if (st == null) throw new SaveVersionException("empty save");
            if (st.Version != CurrentVersion)
                throw new SaveVersionException($"save version {st.Version} != {CurrentVersion}");
            try
            {
                return RestoreUnchecked(st);
            }
            catch (SaveVersionException)
            {
                throw;
            }
            catch (Exception e)
            {
                // null 리스트, 알 수 없는 enum 이름, 잘못된 좌표 등은 모두 손상된 저장으로 취급한다.
                throw new SaveVersionException("corrupt save: " + e.Message, e);
            }
        }

        private static GameSession RestoreUnchecked(GameState st)
        {
            var s = new GameSession(new Economy(st.Money));

            // Grant가 선행 항목을 스스로 적용하므로 정렬은 결정적 순서를 위한 것이다.
            var ids = new List<UnlockId>();
            foreach (var name in st.Purchased)
                ids.Add((UnlockId)Enum.Parse(typeof(UnlockId), name));
            ids.Sort();
            foreach (var id in ids)
                s.Unlocks.Grant(id);

            foreach (var c in st.Cells)
            {
                var type = (CellType)Enum.Parse(typeof(CellType), c.Type);
                var dir = (Direction)Enum.Parse(typeof(Direction), c.Direction);
                s.Grid.Place(c.X, c.Y, type, dir);
                var cell = s.Grid.Get(c.X, c.Y);
                cell.Demolishing = c.Demolishing;
                cell.DemolishRemaining = c.DemolishRemaining;
                cell.SplitterToggle = c.SplitterToggle;
            }

            foreach (var p in st.Packages)
            {
                if (!s.Grid.InBounds(p.X, p.Y))
                    throw new SaveVersionException($"package {p.Id} out of bounds ({p.X},{p.Y})");
                var target = s.Grid.Get(p.X, p.Y);
                if (!target.IsTrack || target.IsOccupied)
                    throw new SaveVersionException($"package {p.Id} on invalid cell ({p.X},{p.Y})");
                s.Sim.Restore(new Package
                {
                    Id = p.Id,
                    Kind = (PackageKind)Enum.Parse(typeof(PackageKind), p.Kind),
                    X = p.X, Y = p.Y,
                    Progress = p.Progress,
                    Heading = (Direction)Enum.Parse(typeof(Direction), p.Heading),
                });
            }

            s.Sim.NextId = st.NextPackageId;
            s.Spawner.Timer = st.SpawnTimer;
            s.Paused = st.Paused;
            s.Speed = st.Speed;
            return s;
        }

        public static string ToJson(GameState st) => JsonConvert.SerializeObject(st, Formatting.Indented);

        public static GameState FromJson(string json)
        {
            if (json == null) throw new SaveVersionException("empty save");
            GameState st;
            try
            {
                st = JsonConvert.DeserializeObject<GameState>(json);
            }
            catch (Exception e)
            {
                throw new SaveVersionException("invalid save json", e);
            }
            if (st == null) throw new SaveVersionException("empty save");
            return st;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 80`

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/Core/GameState.cs Assets/Scripts/Core/SaveSerializer.cs Assets/Tests/Core/SaveSerializerTests.cs
git commit -m "feat(core): save state capture, restore and JSON serialization

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Phase B — Unity 레이어 (Unity 6 LTS 설치 후)

### Task 10: Unity 프로젝트 생성과 EditMode 테스트 실행

사용자가 직접 할 일: Unity Hub 설치 → 로그인(Personal 라이선스) → Installs에서 Unity 6 LTS(6000.0.x) 설치. 모듈 추가 없음.

**Files:**
- Create (Unity가 생성): `Packages/manifest.json`, `ProjectSettings/*`, `Assets/**/*.meta`
- Modify: `Packages/manifest.json` (Newtonsoft 추가)

- [ ] **Step 1: Unity 설치 확인**

Run: `ls /Applications/Unity/Hub/Editor/`
Expected: `6000.0.xxf1` 형태의 폴더 1개. 아래에서 `$UNITY`는 `/Applications/Unity/Hub/Editor/<버전>/Unity.app/Contents/MacOS/Unity`.

- [ ] **Step 2: 기존 폴더에 Unity 프로젝트 생성**

```bash
UNITY=$(ls -d /Applications/Unity/Hub/Editor/6000.0.*/Unity.app/Contents/MacOS/Unity | head -1)
"$UNITY" -batchmode -nographics -createProject /Users/mac/Documents/development/parcel-factory -quit -logFile /tmp/unity-create.log
tail -5 /tmp/unity-create.log
```
Expected: `Packages/`, `ProjectSettings/`가 생기고 `Assets/` 안 기존 파일에 `.meta`가 붙는다. 로그에 `error CS` 없음.

- [ ] **Step 3: Newtonsoft 패키지 추가**

`Packages/manifest.json`의 `"dependencies"` 안에 추가:
```json
"com.unity.nuget.newtonsoft-json": "3.2.1",
```
`com.unity.test-framework`가 없으면 `"com.unity.test-framework": "1.4.5"`도 추가.

- [ ] **Step 4: 입력 설정 확인**

`ProjectSettings/ProjectSettings.asset`에서 `activeInputHandler:` 값이 `0`(Input Manager) 또는 `2`(Both)인지 확인. `1`이면 `0`으로 바꾼다.

```bash
grep -n "activeInputHandler" ProjectSettings/ProjectSettings.asset
```

- [ ] **Step 5: Unity에서 EditMode 테스트 실행**

```bash
UNITY=$(ls -d /Applications/Unity/Hub/Editor/6000.0.*/Unity.app/Contents/MacOS/Unity | head -1)
"$UNITY" -batchmode -nographics -projectPath /Users/mac/Documents/development/parcel-factory -runTests -testPlatform EditMode -testResults /tmp/unity-editmode.xml -logFile /tmp/unity-test.log
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/unity-editmode.xml | head -1
```
Expected: `total="80" passed="80" failed="0"`. 컴파일 오류가 나면 `/tmp/unity-test.log`에서 `error CS`를 찾아 고친다. 흔한 원인: C# 9 초과 문법, `System.Linq` 누락.

- [ ] **Step 6: dotnet 테스트도 여전히 통과하는지 확인**

Run: `dotnet test DotnetTests/Core.Tests`
Expected: `Failed: 0, Passed: 80`

- [ ] **Step 7: 커밋**

```bash
git add Packages ProjectSettings Assets
git commit -m "chore: unity project files, newtonsoft package, meta files

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 11: PlaceholderSprites와 GridView

**Files:**
- Create: `Assets/Scripts/Game/PlaceholderSprites.cs`
- Create: `Assets/Scripts/Game/GridView.cs`
- Create: `Assets/Scripts/Game/GameBootstrap.cs`

**Interfaces:**
- Consumes: `GameSession`, `FactoryGrid`, `Cell`, `BuildService.CellChanged`, `UnlockService.Unlocked`.
- Produces:
  - `static class PlaceholderSprites { const int Size=32; Sprite Floor; Sprite Locked; Sprite Inlet; Sprite Outlet; Sprite Package; Sprite Building(CellType, Direction); Sprite Ghost(CellType, Direction); }`
  - `class GridView : MonoBehaviour { void Bind(GameSession); void RedrawAll(); void RedrawCell(int x,int y); static Vector3 CellCenter(int x,int y); static bool WorldToCell(Vector3 world, out int x, out int y); }`
  - `class GameBootstrap : MonoBehaviour { static GameBootstrap Instance; GameSession Session; GridView Grid; void StartNewGame(); void ReplaceSession(GameSession); event Action<GameSession> SessionReplaced; }` — 씬에 아무것도 없어도 `RuntimeInitializeOnLoadMethod`로 자동 생성된다.

- [ ] **Step 1: PlaceholderSprites 작성**

`Assets/Scripts/Game/PlaceholderSprites.cs`:
```csharp
using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>아트 없이 런타임에 색 사각형과 화살표 스프라이트를 생성한다.</summary>
    public static class PlaceholderSprites
    {
        public const int Size = 32;
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Floor => Solid("floor", new Color(0.82f, 0.82f, 0.80f));
        public static Sprite Locked => Solid("locked", new Color(0.25f, 0.25f, 0.28f));
        public static Sprite Inlet => Solid("inlet", new Color(0.30f, 0.70f, 0.35f));
        public static Sprite Outlet => Solid("outlet", new Color(0.25f, 0.45f, 0.85f));
        public static Sprite Package => Solid("package", new Color(0.90f, 0.55f, 0.15f), 20);

        public static Sprite Building(CellType type, Direction dir)
        {
            switch (type)
            {
                case CellType.Conveyor: return Arrow($"conv{dir}", new Color(0.55f, 0.55f, 0.58f), dir, Color.white);
                case CellType.FastConveyor: return Arrow($"fast{dir}", new Color(0.75f, 0.35f, 0.30f), dir, Color.white);
                case CellType.Splitter: return Arrow($"split{dir}", new Color(0.60f, 0.40f, 0.75f), dir, Color.yellow);
                default: return Floor;
            }
        }

        public static Sprite Ghost(CellType type, Direction dir)
        {
            var key = $"ghost{type}{dir}";
            if (cache.TryGetValue(key, out var s)) return s;
            var src = Building(type, dir).texture;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = src.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i].a = 0.5f;
            tex.SetPixels(px);
            tex.Apply();
            return cache[key] = Make(tex);
        }

        private static Sprite Solid(string key, Color color, int inner = Size)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            int margin = (Size - inner) / 2;
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    bool inside = x >= margin && x < Size - margin && y >= margin && y < Size - margin;
                    bool border = inside && (x == margin || y == margin || x == Size - margin - 1 || y == Size - margin - 1);
                    tex.SetPixel(x, y, !inside ? Color.clear : border ? color * 0.7f : color);
                }
            tex.Apply();
            return cache[key] = Make(tex);
        }

        private static Sprite Arrow(string key, Color bg, Direction dir, Color fg)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (int px = 0; px < Size; px++)
            {
                for (int py = 0; py < Size; py++)
                {
                    // 동쪽 기준 화살표를 그린 뒤 방향에 맞게 좌표를 회전한다.
                    int sx, sy;
                    switch (dir)
                    {
                        case Direction.North: sx = py; sy = Size - 1 - px; break;
                        case Direction.West: sx = Size - 1 - px; sy = py; break;
                        case Direction.South: sx = Size - 1 - py; sy = px; break;
                        default: sx = px; sy = py; break;
                    }
                    bool shaft = sx >= 6 && sx <= 18 && Mathf.Abs(sy - 16) <= 3;
                    bool head = sx >= 18 && sx <= 27 && Mathf.Abs(sy - 16) <= (27 - sx);
                    bool border = px == 0 || py == 0 || px == Size - 1 || py == Size - 1;
                    tex.SetPixel(px, py, border ? bg * 0.6f : (shaft || head) ? fg : bg);
                }
            }
            tex.Apply();
            return cache[key] = Make(tex);
        }

        private static Sprite Make(Texture2D tex)
        {
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }
    }
}
```

- [ ] **Step 2: GridView 작성**

`Assets/Scripts/Game/GridView.cs`:
```csharp
using ParcelFactory.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ParcelFactory.Game
{
    public sealed class GridView : MonoBehaviour
    {
        private Tilemap floor;
        private Tilemap buildings;
        private GameSession session;

        public static Vector3 CellCenter(int x, int y) => new Vector3(x + 0.5f, y + 0.5f, 0f);

        public static bool WorldToCell(Vector3 world, out int x, out int y)
        {
            x = Mathf.FloorToInt(world.x);
            y = Mathf.FloorToInt(world.y);
            return x >= 0 && y >= 0 && x < FactoryGrid.Width && y < FactoryGrid.Height;
        }

        private void Awake()
        {
            var grid = gameObject.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            floor = MakeLayer("Floor", 0);
            buildings = MakeLayer("Buildings", 1);
        }

        private Tilemap MakeLayer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
            return tm;
        }

        public void Bind(GameSession s)
        {
            if (session != null)
            {
                session.Build.CellChanged -= RedrawCell;
                session.Unlocks.Unlocked -= OnUnlocked;
            }
            session = s;
            session.Build.CellChanged += RedrawCell;
            session.Unlocks.Unlocked += OnUnlocked;
            RedrawAll();
        }

        private void OnUnlocked(UnlockId _) => RedrawAll();

        public void RedrawAll()
        {
            for (int x = 0; x < FactoryGrid.Width; x++)
                for (int y = 0; y < FactoryGrid.Height; y++)
                    RedrawCell(x, y);
        }

        public void RedrawCell(int x, int y)
        {
            var c = session.Grid.Get(x, y);
            var pos = new Vector3Int(x, y, 0);
            floor.SetTile(pos, MakeTile(FloorSprite(c)));
            buildings.SetTile(pos, c.IsBuilding ? MakeTile(PlaceholderSprites.Building(c.Type, c.Direction)) : null);
            if (c.IsBuilding)
            {
                buildings.SetTileFlags(pos, TileFlags.None);
                buildings.SetColor(pos, c.Demolishing ? new Color(1f, 0.5f, 0.5f, 0.6f) : Color.white);
            }
        }

        private static Sprite FloorSprite(Cell c)
        {
            switch (c.Type)
            {
                case CellType.Locked: return PlaceholderSprites.Locked;
                case CellType.Inlet: return PlaceholderSprites.Inlet;
                case CellType.Outlet: return PlaceholderSprites.Outlet;
                default: return PlaceholderSprites.Floor;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<Sprite, Tile> tiles =
            new System.Collections.Generic.Dictionary<Sprite, Tile>();

        private static Tile MakeTile(Sprite sprite)
        {
            if (tiles.TryGetValue(sprite, out var t)) return t;
            t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            return tiles[sprite] = t;
        }
    }
}
```

- [ ] **Step 3: GameBootstrap 작성**

`Assets/Scripts/Game/GameBootstrap.cs`:
```csharp
using System;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    /// <summary>씬 설정 없이 플레이를 누르면 자동으로 게임 오브젝트를 만든다.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public GameSession Session { get; private set; }
        public GridView Grid { get; private set; }

        public event Action<GameSession> SessionReplaced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("ParcelFactory");
            go.AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            Instance = this;
            EnsureCamera();

            var gridGo = new GameObject("GridView");
            gridGo.transform.SetParent(transform, false);
            Grid = gridGo.AddComponent<GridView>();

            StartNewGame();
        }

        private static void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 7.5f;
            cam.transform.position = new Vector3(FactoryGrid.Width / 2f, FactoryGrid.Height / 2f, -10f);
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        public void StartNewGame() => ReplaceSession(new GameSession());

        public void ReplaceSession(GameSession s)
        {
            Session = s;
            Grid.Bind(s);
            SessionReplaced?.Invoke(s);
        }
    }
}
```

- [ ] **Step 4: 에디터에서 확인**

Unity Hub에서 프로젝트를 열고(Add project from disk → `parcel-factory`), 아무 씬에서 Play. 콘솔에 오류가 없고 12×12 격자가 보이며 좌하단 6×6은 밝고 나머지는 어둡고, (0,2) 녹색, (5,2) 파란색이어야 한다.

배치 모드 검증:
```bash
UNITY=$(ls -d /Applications/Unity/Hub/Editor/6000.0.*/Unity.app/Contents/MacOS/Unity | head -1)
"$UNITY" -batchmode -nographics -projectPath /Users/mac/Documents/development/parcel-factory -quit -logFile /tmp/unity-compile.log
grep -c "error CS" /tmp/unity-compile.log
```
Expected: `0`

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): placeholder sprites, tilemap grid view and bootstrap

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 12: GameLoop와 PackageView

**Files:**
- Create: `Assets/Scripts/Game/GameLoop.cs`
- Create: `Assets/Scripts/Game/PackageView.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs` (Awake에서 두 컴포넌트 추가)

**Interfaces:**
- Consumes: `GameSession.Tick`, `ConveyorSim.Packages`, `Package`, `GridView.CellCenter`, `GameBootstrap.SessionReplaced`.
- Produces:
  - `class GameLoop : MonoBehaviour { const float TickSeconds=0.05f; }` — `Update`에서 누적 시간만큼 `Session.Tick(0.05f)` 반복, 한 프레임 최대 0.25초.
  - `class PackageView : MonoBehaviour` — 택배 id별 SpriteRenderer 풀. 위치 = 셀 중심 + (Progress<0.5 ? EntryHeading : Heading) × (Progress − 0.5).

- [ ] **Step 1: GameLoop 작성**

`Assets/Scripts/Game/GameLoop.cs`:
```csharp
using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class GameLoop : MonoBehaviour
    {
        public const float TickSeconds = 0.05f;
        private const float MaxFrameSeconds = 0.25f;
        private float accumulator;

        private void Update()
        {
            var session = GameBootstrap.Instance.Session;
            if (session == null) return;
            accumulator += Mathf.Min(Time.deltaTime, MaxFrameSeconds);
            while (accumulator >= TickSeconds)
            {
                accumulator -= TickSeconds;
                session.Tick(TickSeconds);
            }
        }
    }
}
```

- [ ] **Step 2: PackageView 작성**

`Assets/Scripts/Game/PackageView.cs`:
```csharp
using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class PackageView : MonoBehaviour
    {
        private readonly Dictionary<int, SpriteRenderer> active = new Dictionary<int, SpriteRenderer>();
        private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
        private readonly HashSet<int> seen = new HashSet<int>();

        private void Start()
        {
            GameBootstrap.Instance.SessionReplaced += _ => ReleaseAll();
        }

        private void LateUpdate()
        {
            var session = GameBootstrap.Instance.Session;
            if (session == null) return;

            seen.Clear();
            foreach (var p in session.Sim.Packages)
            {
                seen.Add(p.Id);
                if (!active.TryGetValue(p.Id, out var sr))
                {
                    sr = Acquire();
                    active[p.Id] = sr;
                }
                // 전반(진입)은 들어온 방향, 후반(진출)은 나가는 방향을 따라 보간해 코너에서 점프가 없다.
                var dir = p.Progress < 0.5f ? p.EntryHeading : p.Heading;
                var offset = new Vector3(dir.Dx(), dir.Dy(), 0f) * (p.Progress - 0.5f);
                sr.transform.position = GridView.CellCenter(p.X, p.Y) + offset + new Vector3(0f, 0f, -1f);
            }

            var gone = new List<int>();
            foreach (var kv in active)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                Release(active[id]);
                active.Remove(id);
            }
        }

        private SpriteRenderer Acquire()
        {
            if (pool.Count > 0)
            {
                var r = pool.Pop();
                r.gameObject.SetActive(true);
                return r;
            }
            var go = new GameObject("Package");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderSprites.Package;
            sr.sortingOrder = 5;
            return sr;
        }

        private void Release(SpriteRenderer r)
        {
            r.gameObject.SetActive(false);
            pool.Push(r);
        }

        private void ReleaseAll()
        {
            foreach (var kv in active) Release(kv.Value);
            active.Clear();
        }
    }
}
```

- [ ] **Step 3: Bootstrap에 연결**

`GameBootstrap.Awake`에서 `Grid = gridGo.AddComponent<GridView>();` 다음 줄에 추가:
```csharp
            gameObject.AddComponent<GameLoop>();
            var pkgGo = new GameObject("PackageView");
            pkgGo.transform.SetParent(transform, false);
            pkgGo.AddComponent<PackageView>();
```

- [ ] **Step 4: 에디터에서 확인**

Play 후 3초마다 (0,2)에 주황 사각형이 나타나고 1초 뒤 오른쪽으로 빠지며 사라진다(레일이 없어서 소실). 콘솔 오류 없음.

배치 컴파일 검증:
```bash
UNITY=$(ls -d /Applications/Unity/Hub/Editor/6000.0.*/Unity.app/Contents/MacOS/Unity | head -1)
"$UNITY" -batchmode -nographics -projectPath /Users/mac/Documents/development/parcel-factory -quit -logFile /tmp/unity-compile.log
grep -c "error CS" /tmp/unity-compile.log
```
Expected: `0`

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): fixed-step game loop and pooled package renderer

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 13: HudView 뼈대 (잔액·속도·툴바·토스트)

**Files:**
- Create: `Assets/Scripts/Game/HudView.cs`
- Modify: `Assets/Scripts/Game/GameBootstrap.cs` (HudView 추가)

**Interfaces:**
- Consumes: `GameSession`, `Toolbar.OwnedTools`, `Tool`.
- Produces: `class HudView : MonoBehaviour { Tool? SelectedTool; bool IsPointerOverHud(Vector2 screenPos); void Toast(string message); bool ShopOpen; }` — 상점과 해체 팝업은 Task 15에서 추가.

- [ ] **Step 1: HudView 작성**

`Assets/Scripts/Game/HudView.cs`:
```csharp
using System.Collections.Generic;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class HudView : MonoBehaviour
    {
        private const float TopBarHeight = 36f;
        private const float ToolbarWidth = 150f;

        public Tool? SelectedTool { get; private set; }
        public bool ShopOpen { get; set; }

        private string toastText;
        private float toastUntil;
        private readonly List<Rect> hudRects = new List<Rect>();

        public void Toast(string message)
        {
            toastText = message;
            toastUntil = Time.unscaledTime + 1.5f;
        }

        public bool IsPointerOverHud(Vector2 screenPos)
        {
            var guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in hudRects) if (r.Contains(guiPos)) return true;
            return false;
        }

        private void OnGUI()
        {
            hudRects.Clear();
            var session = GameBootstrap.Instance.Session;
            if (session == null) return;
            DrawTopBar(session);
            DrawToolbar(session);
            DrawToast();
        }

        private void DrawTopBar(GameSession s)
        {
            var bar = new Rect(0, 0, Screen.width, TopBarHeight);
            hudRects.Add(bar);
            GUI.Box(bar, GUIContent.none);
            GUI.Label(new Rect(10, 8, 160, 24), $"잔액: {s.Economy.Money}");

            float x = 180;
            if (GUI.Toggle(new Rect(x, 6, 70, 24), s.Paused, "일시정지", "Button") != s.Paused) s.Paused = !s.Paused;
            x += 76;
            if (GUI.Toggle(new Rect(x, 6, 40, 24), !s.Paused && s.Speed == 1f, "1x", "Button")) { s.Paused = false; s.Speed = 1f; }
            x += 46;
            if (GUI.Toggle(new Rect(x, 6, 40, 24), !s.Paused && s.Speed == 2f, "2x", "Button")) { s.Paused = false; s.Speed = 2f; }
            x += 60;
            if (GUI.Button(new Rect(x, 6, 60, 24), "상점")) ShopOpen = !ShopOpen;
        }

        private void DrawToolbar(GameSession s)
        {
            var panel = new Rect(0, TopBarHeight, ToolbarWidth, Screen.height - TopBarHeight);
            hudRects.Add(panel);
            GUI.Box(panel, "도구");
            float y = TopBarHeight + 24;
            if (GUI.Toggle(new Rect(6, y, ToolbarWidth - 12, 24), SelectedTool == null, "선택 없음", "Button")) SelectedTool = null;
            y += 28;
            foreach (var tool in s.Toolbar.OwnedTools())
            {
                bool on = SelectedTool.HasValue && SelectedTool.Value.Equals(tool);
                string label = $"{ToolName(tool.Type)} {Arrow(tool.Direction)}  ({BuildingCosts.Cost(tool.Type)})";
                if (GUI.Toggle(new Rect(6, y, ToolbarWidth - 12, 24), on, label, "Button") && !on) SelectedTool = tool;
                y += 28;
            }
        }

        private void DrawToast()
        {
            if (Time.unscaledTime > toastUntil || string.IsNullOrEmpty(toastText)) return;
            var r = new Rect(Screen.width / 2f - 150, Screen.height - 60, 300, 30);
            GUI.Box(r, toastText);
        }

        public static string ToolName(CellType t)
        {
            switch (t)
            {
                case CellType.Conveyor: return "레일";
                case CellType.FastConveyor: return "고속 레일";
                case CellType.Splitter: return "분배기";
                default: return t.ToString();
            }
        }

        public static string Arrow(Direction d)
        {
            switch (d)
            {
                case Direction.North: return "↑";
                case Direction.East: return "→";
                case Direction.South: return "↓";
                default: return "←";
            }
        }
    }
}
```

- [ ] **Step 2: Bootstrap에 연결**

`GameBootstrap.Awake`의 PackageView 추가 다음 줄에:
```csharp
            Hud = gameObject.AddComponent<HudView>();
```
그리고 클래스에 프로퍼티 추가:
```csharp
        public HudView Hud { get; private set; }
```

- [ ] **Step 3: 에디터에서 확인**

Play: 상단에 잔액 100, 일시정지/1x/2x/상점 버튼, 좌측에 "선택 없음"과 "레일 → (10)" 버튼이 보인다. 일시정지를 누르면 택배 생성이 멈춘다.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): IMGUI hud with money, speed controls, toolbar and toast

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 14: BuildController (호버 고스트, 설치, 해체 팝업, 진행 바)

**Files:**
- Create: `Assets/Scripts/Game/BuildController.cs`
- Modify: `Assets/Scripts/Game/HudView.cs` (해체 확인 팝업과 진행 바 그리기)
- Modify: `Assets/Scripts/Game/GameBootstrap.cs` (BuildController 추가)

**Interfaces:**
- Consumes: `HudView.SelectedTool/IsPointerOverHud/Toast`, `BuildService.TryPlace/BeginDemolish/RefundFor/DemolishSeconds`, `GridView.WorldToCell/CellCenter`, `PlaceholderSprites.Ghost`.
- Produces:
  - `class BuildController : MonoBehaviour { (int x,int y)? PendingDemolish; void ConfirmDemolish(); void CancelDemolish(); }`
  - `HudView`에 `DrawDemolishPopup(GameSession, BuildController)`, `DrawDemolishBars(GameSession)` 추가.

- [ ] **Step 1: BuildController 작성**

`Assets/Scripts/Game/BuildController.cs`:
```csharp
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    public sealed class BuildController : MonoBehaviour
    {
        private SpriteRenderer ghost;
        private HudView hud;

        public (int x, int y)? PendingDemolish { get; private set; }

        private void Start()
        {
            hud = GameBootstrap.Instance.Hud;
            var go = new GameObject("Ghost");
            go.transform.SetParent(transform, false);
            ghost = go.AddComponent<SpriteRenderer>();
            ghost.sortingOrder = 3;
            ghost.enabled = false;
        }

        private void Update()
        {
            var session = GameBootstrap.Instance.Session;
            if (session == null) return;

            var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool onGrid = GridView.WorldToCell(world, out int x, out int y);
            bool overHud = hud.IsPointerOverHud(Input.mousePosition) || PendingDemolish.HasValue;

            UpdateGhost(session, onGrid && !overHud, x, y);
            if (overHud || !onGrid) return;

            if (Input.GetMouseButtonDown(0) && hud.SelectedTool.HasValue)
            {
                var tool = hud.SelectedTool.Value;
                var result = session.Build.TryPlace(x, y, tool.Type, tool.Direction);
                if (result != BuildResult.Ok) hud.Toast(Describe(result));
            }
            else if (Input.GetMouseButtonDown(1))
            {
                var c = session.Grid.Get(x, y);
                if (!c.IsBuilding) hud.Toast("해체할 수 있는 부품이 아닙니다");
                else if (c.Demolishing) hud.Toast("이미 해체 중입니다");
                else PendingDemolish = (x, y);
            }
        }

        private void UpdateGhost(GameSession session, bool show, int x, int y)
        {
            if (!show || !hud.SelectedTool.HasValue)
            {
                ghost.enabled = false;
                return;
            }
            var tool = hud.SelectedTool.Value;
            ghost.enabled = true;
            ghost.sprite = PlaceholderSprites.Ghost(tool.Type, tool.Direction);
            ghost.transform.position = GridView.CellCenter(x, y) + new Vector3(0f, 0f, -0.5f);
            ghost.color = session.Grid.CanPlace(x, y) ? Color.white : new Color(1f, 0.4f, 0.4f);
        }

        public void ConfirmDemolish()
        {
            if (!PendingDemolish.HasValue) return;
            var (x, y) = PendingDemolish.Value;
            var result = GameBootstrap.Instance.Session.Build.BeginDemolish(x, y);
            if (result != BuildResult.Ok) hud.Toast(Describe(result));
            PendingDemolish = null;
        }

        public void CancelDemolish() => PendingDemolish = null;

        public static string Describe(BuildResult r)
        {
            switch (r)
            {
                case BuildResult.Locked: return "잠긴 부지입니다";
                case BuildResult.Occupied: return "이미 무언가 있습니다";
                case BuildResult.NotOwned: return "해금되지 않은 도구입니다";
                case BuildResult.NotEnoughMoney: return "잔액이 부족합니다";
                case BuildResult.NotDemolishable: return "해체할 수 없습니다";
                case BuildResult.AlreadyDemolishing: return "이미 해체 중입니다";
                default: return "";
            }
        }
    }
}
```

- [ ] **Step 2: HudView에 팝업과 진행 바 추가**

`HudView.OnGUI`의 `DrawToast();` 앞에 두 줄 추가:
```csharp
            DrawDemolishBars(session);
            DrawDemolishPopup(session, GameBootstrap.Instance.Build);
```

`HudView` 클래스에 메서드 추가:
```csharp
        private void DrawDemolishBars(GameSession s)
        {
            var cam = Camera.main;
            for (int x = 0; x < FactoryGrid.Width; x++)
            {
                for (int y = 0; y < FactoryGrid.Height; y++)
                {
                    var c = s.Grid.Get(x, y);
                    if (!c.Demolishing) continue;
                    var sp = cam.WorldToScreenPoint(GridView.CellCenter(x, y));
                    float frac = 1f - Mathf.Clamp01(c.DemolishRemaining / BuildService.DemolishSeconds);
                    var bg = new Rect(sp.x - 14, Screen.height - sp.y - 4, 28, 8);
                    GUI.Box(bg, GUIContent.none);
                    GUI.color = new Color(1f, 0.6f, 0.2f);
                    GUI.Box(new Rect(bg.x + 1, bg.y + 1, (bg.width - 2) * frac, bg.height - 2), GUIContent.none);
                    GUI.color = Color.white;
                }
            }
        }

        private void DrawDemolishPopup(GameSession s, BuildController build)
        {
            if (build == null || !build.PendingDemolish.HasValue) return;
            var (x, y) = build.PendingDemolish.Value;
            var c = s.Grid.Get(x, y);
            if (!c.IsBuilding) { build.CancelDemolish(); return; }

            var r = new Rect(Screen.width / 2f - 150, Screen.height / 2f - 60, 300, 120);
            hudRects.Add(r);
            GUI.Box(r, "해체");
            GUI.Label(new Rect(r.x + 12, r.y + 28, 280, 22), $"{ToolName(c.Type)} {Arrow(c.Direction)} 를 해체합니다.");
            GUI.Label(new Rect(r.x + 12, r.y + 50, 280, 22), $"환불 {BuildService.RefundFor(c.Type)} · 소요 {BuildService.DemolishSeconds:0}초");
            if (GUI.Button(new Rect(r.x + 40, r.y + 82, 100, 26), "해체")) build.ConfirmDemolish();
            if (GUI.Button(new Rect(r.x + 160, r.y + 82, 100, 26), "취소")) build.CancelDemolish();
        }
```

- [ ] **Step 3: Bootstrap에 연결**

`GameBootstrap`에 프로퍼티와 생성 추가. `Hud = gameObject.AddComponent<HudView>();` 다음 줄:
```csharp
            Build = gameObject.AddComponent<BuildController>();
```
클래스에:
```csharp
        public BuildController Build { get; private set; }
```

- [ ] **Step 4: 에디터에서 확인**

Play 후:
1. "레일 →" 선택 → 마우스를 격자 위로 옮기면 반투명 화살표가 따라오고, 잠긴 칸이나 입고구 위에서는 붉게 변한다.
2. (1,2)~(4,2)에 좌클릭 설치 → 잔액 60. 택배가 출고구에 도착할 때마다 +15.
3. 레일 위 우클릭 → 팝업에 "환불 8 · 소요 2초". 해체 → 셀이 붉어지고 진행 바가 차오른 뒤 사라지며 잔액 +8.
4. 잔액 부족 상태에서 설치 → 토스트 "잔액이 부족합니다".

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): mouse placement with ghost preview, demolish popup and progress bars

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 15: 해금 상점 패널

**Files:**
- Modify: `Assets/Scripts/Game/HudView.cs`

**Interfaces:**
- Consumes: `UnlockCatalog.All`, `UnlockService.TryPurchase/IsPurchased`, `UnlockResult`.

- [ ] **Step 1: 상점 그리기 추가**

`HudView.OnGUI`의 `DrawToast();` 앞에 추가:
```csharp
            if (ShopOpen) DrawShop(session);
```

`HudView` 클래스에 메서드 추가:
```csharp
        private void DrawShop(GameSession s)
        {
            var r = new Rect(Screen.width - 330, TopBarHeight + 10, 320, 40 + UnlockCatalog.All.Count * 30);
            hudRects.Add(r);
            GUI.Box(r, "해금 상점");
            float y = r.y + 28;
            foreach (var def in UnlockCatalog.All)
            {
                bool owned = s.Unlocks.IsPurchased(def.Id);
                bool blocked = def.Requires.HasValue && !s.Unlocks.IsPurchased(def.Requires.Value);
                GUI.Label(new Rect(r.x + 10, y, 190, 24), def.Name);
                GUI.enabled = !owned && !blocked && s.Economy.CanAfford(def.Price);
                string label = owned ? "보유" : blocked ? "잠김" : $"{def.Price}";
                if (GUI.Button(new Rect(r.x + 210, y, 100, 24), label))
                {
                    var result = s.Unlocks.TryPurchase(def.Id);
                    Toast(DescribeUnlock(result, def));
                }
                GUI.enabled = true;
                y += 30;
            }
        }

        private static string DescribeUnlock(UnlockResult r, UnlockDef def)
        {
            switch (r)
            {
                case UnlockResult.Ok: return $"{def.Name} 해금";
                case UnlockResult.AlreadyOwned: return "이미 보유 중입니다";
                case UnlockResult.RequirementMissing: return "먼저 해금해야 하는 항목이 있습니다";
                case UnlockResult.NotEnoughMoney: return "잔액이 부족합니다";
                default: return "";
            }
        }
```

- [ ] **Step 2: 에디터에서 확인**

Play → 상점 → 레일 ↓ 구매(잔액 50 이상일 때) → 툴바에 "레일 ↓ (10)" 추가, 잔액 −50. 부지 확장 2는 부지 확장 1 전에는 "잠김". 돈을 모아 부지 확장 1 구매 → 격자의 9×9가 밝아진다.

- [ ] **Step 3: 커밋**

```bash
git add Assets/Scripts/Game/HudView.cs
git commit -m "feat(game): unlock shop panel

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 16: 저장·불러오기·새 게임

**Files:**
- Create: `Assets/Scripts/Game/SaveFile.cs`
- Modify: `Assets/Scripts/Game/HudView.cs` (상단 바 버튼 3개)

**Interfaces:**
- Consumes: `SaveSerializer`, `GameBootstrap.ReplaceSession/StartNewGame`, `HudView.Toast`.
- Produces: `static class SaveFile { string Path; bool TrySave(GameSession, out string error); bool TryLoad(out GameSession, out string error); }`

- [ ] **Step 1: SaveFile 작성**

`Assets/Scripts/Game/SaveFile.cs`:
```csharp
using System;
using System.IO;
using ParcelFactory.Core;
using UnityEngine;

namespace ParcelFactory.Game
{
    public static class SaveFile
    {
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "save.json");

        /// <summary>임시 파일에 쓴 뒤 교체한다. 실패 시 false와 사유를 돌려주고 기존 파일은 유지된다.</summary>
        public static bool TrySave(GameSession session, out string error)
        {
            error = null;
            try
            {
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, SaveSerializer.ToJson(SaveSerializer.Capture(session)));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(tmp, Path);
                return true;
            }
            catch (Exception e)
            {
                error = $"저장 실패: {e.GetType().Name}";
                return false;
            }
        }

        public static bool TryLoad(out GameSession session, out string error)
        {
            session = null;
            error = null;
            if (!File.Exists(Path)) { error = "저장 파일이 없습니다"; return false; }
            try
            {
                session = SaveSerializer.Restore(SaveSerializer.FromJson(File.ReadAllText(Path)));
                return true;
            }
            catch (SaveVersionException e)
            {
                error = $"저장 파일을 읽을 수 없습니다: {e.Message}";
                return false;
            }
            catch (Exception e)
            {
                error = $"저장 파일이 손상되었습니다: {e.GetType().Name}";
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: 상단 바에 버튼 추가**

`HudView.DrawTopBar`의 마지막 `if (GUI.Button(... "상점"))` 줄 뒤에 추가:
```csharp
            x += 80;
            if (GUI.Button(new Rect(x, 6, 60, 24), "저장")) { Toast(SaveFile.TrySave(s, out var saveErr) ? "저장했습니다" : saveErr); }
            x += 66;
            if (GUI.Button(new Rect(x, 6, 70, 24), "불러오기"))
            {
                if (SaveFile.TryLoad(out var loaded, out var err)) { GameBootstrap.Instance.ReplaceSession(loaded); Toast("불러왔습니다"); }
                else { Toast(err); }
            }
            x += 76;
            if (GUI.Button(new Rect(x, 6, 70, 24), "새 게임")) { GameBootstrap.Instance.StartNewGame(); Toast("새 게임"); }
```

- [ ] **Step 3: 에디터에서 확인**

1. 레일 몇 개 설치, 해금 1개 구매, 저장.
2. 새 게임 → 빈 격자, 잔액 100.
3. 불러오기 → 레일·해금·잔액이 돌아오고 택배가 이어서 움직인다.
4. `~/Library/Application Support/DefaultCompany/parcel-factory/save.json`을 열어 `"Version": 1`을 `99`로 바꾸고 불러오기 → 토스트 "저장 파일을 읽을 수 없습니다: ..." 와 함께 기존 세션 유지.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Scripts/Game
git commit -m "feat(game): save, load and new game

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 17: 최종 검증

- [ ] **Step 1: 두 테스트 러너 모두 통과**

```bash
dotnet test DotnetTests/Core.Tests
```
Expected: `Failed: 0, Passed: 80`

```bash
UNITY=$(ls -d /Applications/Unity/Hub/Editor/6000.0.*/Unity.app/Contents/MacOS/Unity | head -1)
"$UNITY" -batchmode -nographics -projectPath /Users/mac/Documents/development/parcel-factory -runTests -testPlatform EditMode -testResults /tmp/unity-editmode.xml -logFile /tmp/unity-test.log
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' /tmp/unity-editmode.xml | head -1
```
Expected: `total="80" passed="80" failed="0"`

- [ ] **Step 2: 수동 플레이 체크리스트**

Play 상태에서 아래를 순서대로 확인하고 결과를 기록한다.

1. 시작: 잔액 100, 6×6 밝음, 툴바에 레일 → 만 있음.
2. 입고구부터 출고구까지 레일 4개 설치 → 잔액 60 → 택배가 도착할 때마다 +15.
3. 2x → 택배가 2배 빠르게 흐름. 일시정지 → 모두 멈춤. 일시정지 중 설치 가능.
4. 레일 우클릭 → 팝업 → 해체 → 2초 후 사라지고 +8. 해체 중 레일로 향하던 택배는 앞 칸에서 대기.
5. 상점에서 레일 ↓ 구매 후 코너 경로 구성 → 택배가 방향을 바꿈.
6. 잔액 200 이상에서 부지 확장 1 → 9×9 밝아짐.
7. 분배기 구매 후 설치 → 택배가 좌·우로 번갈아 나감.
8. 저장 → 새 게임 → 불러오기 → 상태 복원.
9. 콘솔에 오류·경고 없음.

- [ ] **Step 3: 작업 트리 깨끗한지 확인**

```bash
git status --short
```
Expected: 출력 없음.
