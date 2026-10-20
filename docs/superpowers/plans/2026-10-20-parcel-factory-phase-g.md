# Parcel Factory Phase G — 체력 회복 상한·점심 휴식, 브릿지 레일
**Spec:** 11.11, 11.12 (우선), 11.3/11.5, 3.2.

### Task 1 (G1): 코어
**Files:** `Worker.cs`(Rest/Move 플래그·RegenCap), `Cell.cs`(Bridge, LowerOccupantId), `FactoryGrid.cs`(IsPlaceable), `ConveyorSim.cs`(브릿지 차선 로직: 진입 차선 결정, 점유/대기, Take/PlaceCarried/PackageAt/RemovePackagesAt가 두 차선을 다룸, `Package.Lane`), `BuildingCosts.cs`(Bridge 40), `Toolbar.cs`(BridgeOwned 4방향, OwnedTools 순서), `Unlocks.cs`(Bridge 200, Requires Plot1; 총 12개), `BuildService.cs`(SetDirection에 Bridge 포함), `GameState/SaveSerializer`(PackageState.Lane, 복원 시 LowerOccupantId 재계산). Tests: new `BridgeTests.cs`, `StaminaTests` 보정(80% 상한, 점심 휴식은 정지 시만).
- 브릿지 진입 규칙: 다음 칸이 Bridge일 때, 택배 heading이 bridge.Direction과 평행(같거나 반대)이면 윗길(OccupantId, heading = Direction), 수직이면 아랫길(LowerOccupantId, heading 유지). 해당 차선이 점유/해체 중이면 대기. 브릿지 위 택배가 다음 칸으로 나갈 때 자기 차선의 점유를 푼다. `PackageAt(x,y)`는 윗길 우선, 아랫길도 찾도록 `PackagesAt` 추가(작업자 집기는 윗길→아랫길 순). 손으로 놓기(PlaceCarried)는 빈 차선 중 윗길 우선.
- 테스트: 교차 흐름(E 라인과 N 라인이 브릿지에서 교차해 둘 다 통과, 서로 막지 않음), 평행/수직 진입 판정, 양 차선 점유 대기, 아랫길 양방향 직진, 집기/놓기, 해체 시 두 차선 택배 소실, 저장 왕복(Lane), 상점 해금(Plot1 필요, 200), 핫바 순서, 체력 80% 상한(걷기/정지), 점심 정지 시 100%까지·점심 이동 시 80% 상한, 강화 시 상한 비례. 총 ≥ 290.
- 검증·커밋(게임 레이어 컴파일 유지: `CellType.Bridge` 추가로 switch default 경로 확인).

### Task 2 (G2): Unity
- CellViews: Bridge 뷰(바닥 벨트 수직축 + 기둥 4개 + 고가 벨트 0.9 + 화살표 둘), PackageViews: 높이 Lane에 따라 1.3/0.4, PalletView 무관. InputController.SlotOrder에 Bridge 3번째, 7개 슬롯(키 7 바인딩). HudController Names/프롬프트/상태바 "브릿지 ↑"; 체력 바 80% 눈금; 상점에 브릿지 행(자동). PlacementGhost는 BuildPreview 재사용. 스모크: 교차 배치 후 두 택배가 서로 다른 높이로 그려짐, 슬롯 7개, 80% 눈금 존재. 스크린샷 `unity-bridge.png`(교차 장면).
### Task 3 (G3): 3D 시안 동기화.
