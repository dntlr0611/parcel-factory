# Parcel Factory Phase E 구현 계획 — 체력 규칙 개정, EXP, 상태창

**Spec:** 11.5(우선), 11.3, 11.4.

### Task 1 (E1): 코어 — 체력 규칙, Progression, 저장 v6
**Files:** Create `Assets/Scripts/Core/Progression.cs`; Modify `Worker.cs`, `GameSession.cs`, `ConveyorSim.cs`(EXP 훅은 GameSession에서 구독해도 됨), `GameState.cs`, `SaveSerializer.cs`, `Clock.cs`(DaySummary에 Exp); Tests: new `ProgressionTests.cs`, 기존 Stamina/Run 테스트 보정.
- `WorkerService`: `WalkDrain = 0`, `CarryDrain = 0`(상수는 남기되 0), `RunDrain = 3f`, `RunCarryDrain = 5.5f`; Move: 달릴 때만 소모(`RunDrain`/`RunCarryDrain` × dt). Rest: 점심 +20/s, 아니면 **움직였든 아니든** +StandRegen × RegenMultiplier /s. MaxStamina/Speed는 Progression 곱 적용(`EffectiveMaxStamina`, `EffectiveSpeed`). 체력 클램프 상한 = EffectiveMaxStamina. `Refill()`은 EffectiveMaxStamina로.
- `Progression`: 스펙 표 그대로. `enum Stat { Stamina, Speed, Regen }`. `AddExp(int)`(음수 무시), `TryUpgrade`. 체력 강화 시 `Worker.Stamina += 20`(GameSession이 연결).
- `GameSession`: `Progression`; `Sim.PackageDelivered` 구독 → 성공 판정(`DeliveryScoring.IsSuccess`)이면 Delivered +10 / ReturnedBad +8; `Sim.Inspected` 이벤트(새로 추가: `NoteInspected`가 발생) → +2. `DaySummary.Exp` = 그날 획득(DayStartExp 기록).
- 저장 v6: Exp, 레벨 3개, DayStartExp. v<6 거부.
- 테스트: 걷기/들고 걷기 소모 0, 달리기 소모 3.0·5.5, 걷는 중 회복 0.8, 강화 비용·최대 레벨·NotEnoughExp, 체력 강화 시 현재 체력 +20 및 상한, 속도 1.08^n(또는 1+0.08n — **1 + 0.08×n 선형**으로), 회복 1+0.5n, EXP 적립(출고/반품/검사), 요약 Exp, 저장 왕복·v5 거부. 총 ≥ 225.
- 검증: dotnet, EditMode, 컴파일 0, PlayMode(기존 통과; 달리기 스모크는 소모 배수 가정이 바뀌므로 보정 필요 시 최소 수정 보고). 커밋 1개.

### Task 2 (E2): Unity — EXP 표시, 상태창
- 좌상단 패널에 "EXP n" 줄(잔액 아래, 보라/하늘색 강조). 상단 바에 "상태 C" 버튼(상점 옆), C 키(InputController `status` 액션, B와 같은 토글 규칙: 상태창만 열려 있을 때 C/버튼으로 닫힘), 터치: 상단 바 버튼으로 충분.
- 상태창 패널(Panels, "panels" 블록, 닫기 버튼): 제목 "작업자 상태", 현재 EXP, 능력치 3행: 이름 · 현재 효과(예: "최대 체력 140", "속도 +16%", "회복 +50%") · "Lv 2/5" · 버튼 "강화 40 EXP"(부족/최대면 비활성, 최대면 "MAX"). 강화 시 토스트 "체력 강화! 최대 체력 140", 패널 갱신, EXP 플래시.
- 하루 요약에 "획득 EXP" 행. 체력 바 폭은 Stamina/EffectiveMaxStamina.
- 스모크: EXP 라벨 존재, 배송 후 EXP 증가(라벨 갱신), C로 상태창 열림·블록, 강화 버튼 클릭 → 레벨 1·EXP 차감·토스트, 닫기. 스크린샷 `unity-status.png`(상태창 열림, EXP 120 세팅). 검증·커밋.

### Task 3 (E3): 3D 시안 동기화 (같은 규칙, 상태창 UI, `#status` 해시).
