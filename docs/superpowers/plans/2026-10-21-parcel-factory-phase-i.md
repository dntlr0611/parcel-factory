# Parcel Factory Phase I — 환경설정: 소리와 화면 (스펙 §13)

## Task I1 — 설정 모델과 저장
- `GameSettings`(Game 레이어): 음악·배경소리(0~100 정수), 전체 화면 여부, 해상도 W×H. `PlayerPrefs` 키 `pf.music` `pf.ambient` `pf.fullscreen` `pf.resW` `pf.resH`. 값은 범위로 고정(clamp). 변경 이벤트.
- 해상도 후보 목록 함수(스펙 §13.2): `Screen.resolutions` 중복 제거 + 창 모드 기본 후보, 오름차순. 순수 함수로 분리해 테스트한다.
- ProjectSettings: `resizableWindow: 1`.

## Task I2 — AudioDirector
- 음악·배경소리 AudioSource 두 개, `Resources/Audio/music-shift`, `Resources/Audio/ambient-factory` 반복 재생. 클립이 없으면 조용히 건너뛴다. 음원은 사용자가 나중에 넣는다(`Assets/Resources/Audio/README.txt`에 파일 이름 안내).
- 볼륨 = 설정값²(0~1). 배경소리 목표 배수: 근무 1 · 점심 0.5 · 시작 화면/일시정지/하루 요약 0, 0.5초 보간.
- 오디오 임포트 설정: `Assets/Editor` AssetPostprocessor로 `Assets/Resources/Audio/` 아래를 Vorbis 품질 0.6 · Load In Background · 음악은 Streaming, 배경소리는 Compressed In Memory로 고정.

## Task I3 — 설정 카드
- 스펙 §13.3. §12 부품(UiParts)으로 짓는다. 슬라이더·세그먼트·스테퍼 부품을 UiParts에 추가한다.
- 시작 화면 메뉴에 `설정` 추가(조작법 위). 인게임 `메뉴` 경유로도 열린다. Esc 우선순위: 설정 카드가 열려 있으면 Esc는 카드만 닫는다.
- 모바일(`Application.isMobilePlatform` 또는 터치 모드)에서는 화면 묶음을 숨긴다.

## Task I4 — 테스트와 스크린샷
- PlayMode: 설정 저장·읽기 왕복(PlayerPrefs 키는 테스트 전후 백업·복원), clamp, 해상도 후보 목록, 볼륨 제곱 매핑과 배경소리 배수(시작 화면·점심), 카드 키보드 조작(↓·→로 음악 값이 5 오름), Esc가 카드만 닫음, 적용 버튼이 화면 값 변경 시에만 활성.
- 스크린샷 `docs/design/unity-settings.png`(시작 화면 위 설정 카드).
