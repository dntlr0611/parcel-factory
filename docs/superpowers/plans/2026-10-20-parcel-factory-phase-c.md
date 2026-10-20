# Parcel Factory Phase C 구현 계획 — 트럭 도크와 팔레트

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** 스펙 10절을 코어·Unity 레이어에 반영한다. 출고·반품 판정은 동쪽 벽 안쪽 트럭 도크 (11,2)·(11,4)에서만 일어나고, 파란/빨간 칸은 택배가 쌓이는 팔레트가 되어 작업자가 트럭까지 손으로 나른다. 부지를 12칸까지 넓히면 레일로 자동화된다.

**Spec:** `docs/superpowers/specs/2026-10-15-parcel-factory-design.md` 10절(우선), 9.3(판정표), 9.7(이동).

## Global Constraints
- Phase B Global Constraints 그대로(Unity 배치 명령, 커밋 트레일러). 코어 변경은 Task 1에서만.
- 테스트: 코어(dotnet + Unity EditMode) 전부, PlayMode 전부(스크린샷 테스트는 그래픽 있을 때만).
- 기존 테스트 중 (5,2)/(5,4)를 출고구·반품구로 가정한 것은 10절에 맞게 고친다. 스펙 3.1의 "출고구 (5,2) 고정"은 10절이 대체한다.

### Task 1 (C1): 코어 — Pallet, 트럭 도크 좌표, 걷기 범위, 저장 v4

**Files:**
- Modify: `Assets/Scripts/Core/Cell.cs`, `FactoryGrid.cs`, `ConveyorSim.cs`, `Worker.cs`, `BuildService.cs`, `BuildingCosts.cs`, `Toolbar.cs`, `GameState.cs`, `SaveSerializer.cs`
- Modify: `Assets/Tests/Core/*Tests.cs` (해당 테스트), 새 `PalletTests.cs`

**Interfaces (Produces):** 스펙 10.5 그대로. 추가로 `Tool`에 정적 헬퍼 `public static PalletKind KindOf(Direction d)` (East→Blue, 그 외→Red).

- [ ] Step 1 — 테스트 먼저(`PalletTests.cs`): 레일→팔레트 적재(흐름에서 사라지고 `PalletCount` 증가, 검사 상태 유지); 용량 6 초과 시 앞 레일 대기(Progress 1 유지, 위치 불변); `TakeFromPallet`이 맨 위(마지막 적재)를 돌려주고 count 감소; 빈 팔레트 Take → null; `PutOnPallet` 용량 체크; 작업자 Preview: 빈손+팔레트에 택배 → PickedUp, 들고+자리 있음 → PutDown, 가득 → CannotPutHere, Interact가 실제로 옮김; 팔레트 해체: 더미 있으면 Occupied, 비면 Ok; 팔레트 위는 못 걷고 잠긴 칸은 걸음(CanStand (8,8) true at n=6); 트럭 도크 (11,2)에 손으로 놓기 → 판정·잔액 변화; (10,2) 동향 레일에서 흘러 (11,2) 도착 → 판정(12칸 해금 후); 시작 격자: (5,2) Pallet Blue, (5,4) Pallet Red, (11,2) Outlet, (11,4) ReturnDock; 저장 v4 왕복(팔레트 종류·더미·검사 상태), v3 거부.
- [ ] Step 2 — 구현. `Cell`: `PalletKind`, `Pallet` 리스트, `PalletCapacity`, `IsPalletFull`; `IsBuilding`에 Pallet 포함, `IsTrack`에는 포함하지 않는다. `FactoryGrid`: 좌표 상수 변경, 생성자에서 도크는 잠긴 구역이어도 배치하고 팔레트 2개 설치(`Place` 경유하지 말고 직접 세팅), `UnlockRegion`은 도크·팔레트 칸을 건드리지 않는다, `Place(…, Pallet, dir)`은 `PalletKind = Tool.KindOf(dir)`. `ConveyorSim.Tick`: `next.Type == Pallet` → 가득이면 `continue`, 아니면 흐름에서 제거 후 `next.Pallet.Add(p)`; `RemovePackagesAt`은 팔레트 더미도 비운다(해체는 막히지만 안전망). `Worker`: 10.5. `BuildService.BeginDemolish`: Pallet 더미 비어 있지 않으면 `Occupied`. `BuildingCosts.Pallet = 20`. `Toolbar`: `PalletOwned`. `SaveSerializer` v4: `CellState.PalletKind`, `CellState.Pallet` (PackageState 배열); 복원 시 팔레트 택배는 `Sim`에 넣지 않고 더미에만.
- [ ] Step 3 — 기존 테스트 정리: (5,2)·(5,4)를 도크로 쓰던 테스트는 (11,2)·(11,4)로 옮기거나 팔레트 시나리오로 바꾼다. 총 테스트 수 ≥ 150.
- [ ] Step 4 — 검증: dotnet 전부, Unity EditMode 전부, 컴파일 0(게임 레이어는 Task 2 전까지 컴파일만 되면 됨 — `IsDock` 등 기존 API를 유지하므로 깨지지 않아야 한다; 깨지면 최소 수정으로 컴파일 유지). 커밋 1개.

### Task 2 (C2): Unity 레이어 — 팔레트 뷰, 도크 이동, 핫바 6칸, 프롬프트, 스크린샷

**Files:**
- Modify: `Assets/Scripts/Game/CellViews.cs`, `WorldBuilder.cs`, `InputController.cs`, `HudController.cs`, `WorkerView.cs`, `PackageViews.cs`(필요 시), `Assets/Tests/Game/BootSmokeTests.cs`, `ScreenshotTests.cs`

- [ ] Step 1 — `WorldBuilder`: 도크 슬래브는 `FactoryGrid.OutletX/Y`·`ReturnDockX/Y`를 읽으므로 자동으로 (11,·)로 간다. 팔레트→출입구 차선 제거. 트럭 도크 앞 흰 정지선 1줄(0.06×1.0, 칸 서쪽 변)만 남긴다. 출입구는 도크 칸과 같은 줄(변경 없음).
- [ ] Step 2 — `CellViews`: `Pallet` 뷰 = 나무 팔레트(0.9×0.12×0.9, `#B98A5C`) + 종류 색 테두리(파랑 `#3D7BE0` / 빨강 `#D95A4A`, 0.04 높이 띠) + 더미 상자: 매 프레임 `cell.Pallet.Count`만큼 `box-small`(0.8배)을 층당 3개(x −0.26/0/+0.26, z 0), 2층(y 0.12, 0.52)에 배치(풀링). 거부 도장은 `PackageViews.SetStamp`. 커서 높이: 팔레트 0.12 + 0.4×층수.
- [ ] Step 3 — `InputController.SlotOrder` = Conveyor, FastConveyor, Pallet, Table, Splitter, Scanner (키 1~6, 슬롯 액션 6개). `HudController`: 핫바 최대 6칸, 팔레트 칸은 화살표 대신 "파랑"/"빨강"; `Names[Pallet] = "팔레트"`; 프롬프트: 바라보는 칸이 Pallet이면 PickedUp → "집기 (n)", PutDown → "놓기"; 트럭 도크 "출고"/"반품" 유지. 토스트: 팔레트 가득 → "팔레트가 가득 찼습니다"(CannotPutHere + Pallet일 때).
- [ ] Step 4 — `WorkerView`: `IsWalkable` 변경에 따라 커서 표시는 잠긴 칸도 보여주되(바닥), 설치 불가는 Preview 결과(Locked)로 안내.
- [ ] Step 5 — 테스트: 스모크에 (5,2) 팔레트 뷰 존재·(11,2) 도크 슬래브 위치 확인; 스크린샷 데모 배치는 레일 (1,2)E (2,2)E (3,2) Table (4,2)E → 팔레트 (5,2)에 상자 3개 쌓인 상태 + 작업자가 상자를 들고 (8,2)쯤 걸어가는 장면(`Worker.Carrying` 세팅)으로 바꾼다. PNG 재생성·커밋.
- [ ] Step 6 — 검증: 컴파일 0, EditMode 전부, PlayMode 전부(-nographics), 스크린샷 런(그래픽). 커밋 1개.

### Task 3 (C3): 3D 시안 동기화 + 문서
- [ ] `docs/design/prototype/parcel-factory-3d.html`과 아티팩트 https://claude.ai/artifact/S6mAdo8JggxwFW6uV6f2JK 에 같은 규칙 반영(팔레트 더미, 트럭 도크 (11,·), 잠긴 구역 걷기). 스펙 3.1 문구를 10절 참조로 갱신. 스크린샷 비교로 Unity와 그림이 같은지 확인.
