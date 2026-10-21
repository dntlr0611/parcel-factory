# Parcel Factory Phase K — 운송장 부착대 (스펙 §15)

Phase J(항공) 위에 이어서 한다. 같은 저장 버전 v7.

## Task K1 — 코어 (TDD)
- `CellType.Labeler`(enum 끝), 설치비 ₩20, Toolbar 순서(검사대 다음, 1~8), 해체 규칙.
- ConveyorSim: 검사됐고 운송장 없는 택배는 부착대에서 멈춘다. 나머지는 통과. WorkerService: 부착대에 올려놓기(미검사면 거부 결과), 집기, 걷기 불가. 운송장 붙이기 API(`AttachWaybill(x,y)` 등).
- 자동 검사기가 운송장도 붙인다.
- DeliveryScoring: `NoWaybillToOutlet` −20, `NoWaybillToReturn` −10, 판정 순서 스펙 15.3.
- 저장 v7 `Waybill`. 테스트: 멈춤/통과 조합, 붙인 뒤 출발, 판정 순서 전 조합, 검사기 자동 부착, 저장 왕복.

## Task K2 — 뷰와 카드
- 부착대 모델(작업대 + 운송장 롤 + 프린터), 미리보기, 미니 상태바, 핫바 슬롯 아이콘(코드로 그리거나 기존 아이콘 조합; 새 외부 에셋 금지).
- 운송장 카드(스펙 15.2): UGUI 드래그(IBeginDrag/IDrag/IEndDrag), 부착 영역 판정, 착 붙는 애니메이션, 키보드 대체, Esc 닫기, 입력 차단. 마우스·터치 둘 다.
- 3D 운송장 쿼드(상자 윗면 중앙), 항공 마크를 모서리로 이동. 머리띠 색 파랑/빨강.
- 프롬프트·토스트·도움말 문구.

## Task K3 — 테스트와 스크린샷
- PlayMode: 부착대에서 멈춤 → 카드 열림 → 드래그 시뮬레이션(PointerEventData로 영역 안/밖) → 붙음/복귀, 키보드 대체, 운송장 없이 배달 −20/−10.
- 스크린샷 `docs/design/unity-waybill.png`(카드가 열린 화면, 운송장을 끌고 있는 중간 상태)와 `unity-air.png`에 운송장 붙은 상자.
