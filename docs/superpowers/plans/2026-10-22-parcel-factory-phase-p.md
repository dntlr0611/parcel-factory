# Parcel Factory Phase P — 점심 카드: 메뉴와 오후 계약 (스펙 §21)

## P1 — 코어 (TDD)
- `LunchMenu { None, Gukbap, Jeyuk, Salad, Rest }`와 효과 훅: 달리기 소모 배수(WorkerService), 이동 속도 배수(능력치 배수에 곱), 검사 EXP 배수(ConveyorSim NoteInspected 경로, 검사대·검사기). 18:00에 해제.
- `Contract` 종류·목표·진행·보상, 후보 생성(회사 상태 조건, 중복 없음, 결정적 난수, 입고구 수 비례), 회사 단위 집계(모든 공장 이벤트), 즉시 지급/18:00 판정, DaySummary 줄.
- Company/GameSession: 12:00 `LunchStarted` 이벤트, 확정 API(`ConfirmLunch(menu?, contractIndex?)`, 돈 부족 거부), 13:00 이후 확정 불가.
- 저장 v11, v6~v10 호환.
- 테스트: 메뉴별 효과와 해제, 가격·돈 부족, 후보 조건(항공·취급주의·부착대 유무), N 비례, 집계(여러 공장), 즉시 지급·퇴근 판정·실패 무감점, 13:00 마감, 저장 왕복.

## P2 — 뷰
- 점심 카드(§12 카드, 두 열, 선택 상태 앰버 테두리, 가격·효과·보상 표기, 돈 부족 흐림), 자동 열림과 대기(다른 카드 뒤), `점심` 칩과 L 키, 키보드·게임패드 조작, 입력 차단(Panels.AnyOpen·Esc 우선순위에 포함).
- 체력 알약 옆 메뉴 배지, 계약 진행 칩(진행/달성 색), 달성 토스트, 하루 요약 계약 줄. 도움말 문구.

## P3 — 테스트와 스크린샷
- PlayMode: 12:00 자동 열림, 다른 카드 뒤 대기, 나중에 → 칩 → 다시 열기, 확정 → 돈 차감·배지·칩, 13:00 칩 사라짐, 키보드 조작.
- 스크린샷 `docs/design/unity-lunch-card.png`(카드, 메뉴 하나·계약 하나 선택됨)와 `docs/design/unity-contract.png`(오후, 진행 칩·메뉴 배지). 기존 `unity-lunch.png`는 카드가 없는 점심 화면이므로 테스트에서 카드를 닫고 찍는다.
