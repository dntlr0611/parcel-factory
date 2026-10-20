# Parcel Factory Phase F — 방향 입력 통일, 재설정, 미니 상태바
**Spec:** 11.8(우선), 11.7.

### Task 1 (F1): 코어 — RedDirection, 재설정 API, 저장 필드
**Files:** `Cell.cs`, `BuildService.cs`, `ConveyorSim.cs`(SplitterRoutes/HeadingFor), `FactoryGrid.cs`(Clear가 RedDirection 초기화), `GameState.cs`, `SaveSerializer.cs`; Tests: new `ReconfigureTests.cs`, SplitterTests 보정.
- `Cell.RedDirection` (Direction?); `ConveyorSim.SplitterRoutes`: red = RedDirection이 설정돼 있고 blue와 다르고 연결돼 있으면 그것, 아니면 자동 규칙. (연결 안 된 명시 빨강은? → 설정은 유지하되 라우팅은 "빨강 없음"으로 취급: 거부 택배가 파랑으로 감. 뷰는 흐린 빨간 화살표.)
- `BuildService.SetDirection(x,y,dir)`: 대상이 Conveyor/FastConveyor/Scanner/Splitter/Table이면 Direction 갱신(Table 포함 — 방향 있음), 해체 중이면 Invalid, 그 외 NotConfigurable; 그 칸 위 택배의 Heading을 새 방향으로(Progress 유지), 분배기는 EntryHeading 기준으로 다시 계산 — 단순화: 분배기 위 택배는 HeadingFor 재평가. `CellChanged` 발생. 비용 없음. 보유 방향 체크는 하지 않는다(해금은 설치 시점에만 적용; 재설정은 자유) — **아니, 스펙대로 보유하지 않은 방향은 거부: `NotOwned` 결과 추가**(레일·고속만 해당).
- `SetSplitterRed(x,y,Direction?)`: 분배기만, null 허용(자동으로 복귀), blue와 같으면 Invalid.
- `SetPalletKind(x,y,kind)`: 팔레트만, None 불가.
- 저장: `CellState.RedDirection` 선택 필드(없으면 null). 버전 6 유지.
- 테스트: 각 API의 Ok/NotConfigurable/Invalid/NotOwned, CellChanged 발생, 레일 방향 변경 시 그 칸 택배 Heading 변경, 분배기 명시 빨강 우선·같은 방향이면 자동, 저장 왕복(RedDirection null/값). 총 ≥ 258.
- 검증·커밋(트레일러).

### Task 2 (F2): Unity — 입력 모드, 재설정, 미니 상태바
**Files:** `InputController.cs`, `HudController.cs`, `CellViews.cs`(또는 새 `StatusBars.cs`), `Panels.cs`(필요 시), tests.
- InputController 상태 머신: `ConfigMode { None, PickSlot, PickDirection }`, `ConfigTarget { Tool, Cell(x,y) }`, `ConfigWhich { Primary, Secondary }`. R: 바라보는 칸이 설정 가능한 부품이면 Target=Cell, 아니면 선택 도구가 있으면 Target=Tool, 아니면 토스트 "설정할 대상이 없습니다". 분배기/팔레트는 PickSlot(1/2), 나머지는 PickDirection. 화살표·WASD 입력(WasPressedThisFrame; 이동 억제) → 적용 → None. 팔레트 1/2 즉시 적용. Esc/E/X/숫자 3~6 → 취소. 도구 대상: SelectedTool의 Direction 갱신(NotOwned면 토스트). 셀 대상: BuildService API 호출, 결과 토스트("방향 변경 →", "해금되지 않은 방향입니다", "같은 방향으로는 설정할 수 없습니다").
- 기존 RotateTool(순환)은 제거. 슬롯 재탭은 이제 "같은 슬롯 → 설정 모드 진입"(= R과 동일).
- HUD 배너(프롬프트 아래 또는 상태줄 위): "방향 입력: ← ↑ → ↓ (Esc 취소)" / "1 파랑 · 2 빨강 선택". 터치: R 버튼 = R, D-패드 = 방향(PointerDown 1회로 처리), 핫바 1·2 = 선택.
- 미니 상태바: `StatusBars` 컴포넌트(HUD 캔버스에 풀링된 라벨, 매 프레임 투영, 설치된 부품마다 1개, `CellChanged`/팔레트 더미/검사 대기 변화 시 텍스트 갱신 — 간단히 매 프레임 텍스트 비교 후 변경 시만 대입). 바라보는 부품은 밝게 + " · R 설정". 높이: TopHeight + 0.25.
- 스모크: R→화살표로 도구 방향 설정; 설치된 레일 앞에서 R→↑ → Direction North, 토스트; 분배기 R→2→↓ → RedDirection South; 팔레트 R→2 → Red; 상태바 라벨 수 = 설치 부품 수, 텍스트 "파 → · 빨 ↓". 스크린샷 `unity-statusbars.png`(데모 배치 + 분배기, 상태바 보임). 검증·커밋.

### Task 3 (F3): 3D 시안 동기화.
