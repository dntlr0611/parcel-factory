# Parcel Factory Phase D 구현 계획 — 근무 시간·점심·스태미나

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.

**Goal:** 스펙 11절. 하루 09:00–18:00, 12시 점심, 퇴근 시 하루 요약, 작업자 스태미나.
**Spec:** `docs/superpowers/specs/2026-10-15-parcel-factory-design.md` 11절(우선), 3.5, 9.7.

## Global Constraints
- Phase B 규칙 그대로(Unity 배치 명령, 트레일러). 코어 변경은 Task 1에서만.
- 모든 테스트 통과: dotnet + EditMode(현재 162), PlayMode(현재 6 + 스크린샷).

### Task 1 (D1): 코어 — Clock, 하루 요약, 스태미나, 저장 v5
**Files:** Create `Assets/Scripts/Core/Clock.cs`; Modify `GameSession.cs`, `Spawner.cs`(또는 GameSession에서 게이트), `Worker.cs`, `GameState.cs`, `SaveSerializer.cs`; Tests: new `ClockTests.cs`, `StaminaTests.cs`, 기존 테스트 보정.
**Produces:**
```csharp
public enum DayPhase { Working, Lunch, Closed }
public sealed class Clock {
  public const int OpenMinute = 9*60, LunchStart = 12*60, LunchEnd = 13*60, CloseMinute = 18*60;
  public const float MinutesPerSecond = 1f;
  public int Day { get; }            // 1부터
  public float MinuteOfDay { get; }  // 540.0 .. 1080.0
  public DayPhase Phase { get; }
  public bool IsOpen => Phase != DayPhase.Closed;
  public void Advance(float seconds);   // Closed가 되면 그 자리에서 멈춤(1080에 고정)
  public void NextDay();                // Day++, MinuteOfDay = 540
  public string TimeText => $"{(int)MinuteOfDay/60:00}:{(int)MinuteOfDay%60:00}";
}
public sealed class DaySummary { public int Day, Delivered, Lost, Inspected; public int StartMoney, EndMoney; }
// GameSession: Clock Clock; int DayStartMoney; event Action<DaySummary> DayEnded; void NextDay();
//   Tick: Paused || !Clock.IsOpen → return; Clock.Advance(d); if Phase==Working → Spawner.Tick(d); Sim/Build 항상; Worker.Rest(d, Phase); Closed로 바뀐 틱에 DayEnded 발생(요약 생성; Delivered/Lost/Inspected는 그날 시작 대비 증가분).
// Worker: float Stamina = 100; WorkerService: const float WalkDrain = 1.2f, CarryDrain = 2.2f, StandRegen = 0.8f, LunchRegen = 20f, SlowBelow = 30f;
//   Move: 속도 = Speed * (Stamina < SlowBelow ? Lerp(0.5, 1, Stamina/SlowBelow) : 1); 실제로 움직인 시간만큼 소모(carrying이면 CarryDrain); Rest(dt, phase): phase==Lunch → +LunchRegen*dt(항상), 아니면 이번 프레임에 Move로 움직이지 않았으면 +StandRegen*dt (움직임 여부는 Move가 세운 플래그를 Rest가 소비).
//   Preview: 빈손이고 집을 택배가 있는데 Stamina <= 0 → InteractResult.Exhausted (PutDown/Delivered는 허용).
// Save v5: Day, MinuteOfDay, DayStartMoney, Stamina, DayStartDelivered/Lost/Inspected. v<5 거부.
```
- [ ] Step 1 테스트 먼저: 시계 경계(09:00 시작, 11:59→12:00 Lunch, 13:00 Working, 18:00 Closed 고정, NextDay), 점심에 입고 안 됨(타이머 정지), Closed면 Tick이 아무것도 안 함, DayEnded 1회 발생 + 요약 수치, 스태미나 소모(걷기/들고 걷기/정지 회복/점심 회복, 0~100 클램프), 속도 저하(스태미나 15 → 0.75배), 0이면 Exhausted(PutDown은 가능), 저장 v5 왕복·v4 거부, 배속 2x에서 시계 2배.
- [ ] Step 2 구현. 기존 테스트에서 `s.Tick(...)` 누적이 9분(540초)을 넘는 것이 있으면 Closed에 걸리므로 확인(없을 것).
- [ ] Step 3 검증: dotnet 전부, EditMode 전부, 컴파일 0, PlayMode 기존 통과(게임 레이어는 컴파일만 유지). 커밋 1개.

### Task 2 (D2): Unity — 시계 HUD, 점심 배너, 스태미나 바, 하루 요약 패널
**Files:** `HudController.cs`, `Panels.cs`, `InputController.cs`(요약 패널 블록), `WorkerView.cs`(지침 표현은 선택), `BootSmokeTests.cs`, `ScreenshotTests.cs`.
- [ ] 상단 중앙 패널 "1일차 09:00" (Noto 18 bold), 점심이면 옆에 노란 "점심시간" 배지, 퇴근이면 "퇴근".
- [ ] 스태미나 바: 상태줄 왼쪽 140×10, 색 초록(>50)→노랑(>20)→빨강, 20 아래 깜빡임, 라벨 "체력".
- [ ] 하루 요약 패널(모달, SetBlock("panels")): 제목 "n일차 마감", 배송/소실/검사/잔액 변화, 버튼 "다음 날" → `Session.NextDay()`; Escape로는 닫히지 않는다(다음 날만). `DayEnded` 구독(SessionReplaced 재구독).
- [ ] Exhausted 토스트 "지쳤습니다. 잠시 쉬세요".
- [ ] 스모크: 시계 텍스트 "1일차 09:00" 존재; `Session.Clock`을 12:00으로 강제(테스트용 `Clock.SetMinute` 또는 Advance)하고 한 프레임 후 배너 활성; 18:00 → 요약 패널 활성·UiBlocksInput; NextDay → 닫힘. 스크린샷: 데모 시각 10:40 정도로 설정해 시계가 보이게, 추가로 `unity-lunch.png`(12:20, 배너) 캡처.
- [ ] 검증·커밋.

### Task 3 (D3): 3D 시안 동기화
- [ ] 프로토타입에 같은 규칙(시계, 점심 입고 정지, 퇴근 요약, 스태미나 바·속도·Exhausted). `#demo`는 10:40. 렌더·커밋·아티팩트 게시(게시는 코디네이터).
