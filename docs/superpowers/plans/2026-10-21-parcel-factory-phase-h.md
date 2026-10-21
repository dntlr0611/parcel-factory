# Parcel Factory Phase H — 시작 화면과 UI 품질 (스펙 §12)

목표: 유니티 UI를 3D 시안(`docs/design/prototype/parcel-factory-3d.html`의 CSS, `docs/design/3d-mockup-*.png`)과 같은 수준으로 올리고 시작 화면을 추가한다. 게임 규칙(코어)은 바꾸지 않는다.

이미 준비된 에셋 (코디네이터가 넣음):
- `Assets/Resources/Fonts/NotoSansKR-Regular.ttf`(정적 400으로 교체, 기존 GUID 유지) · `NotoSansKR-Medium.ttf` · `NotoSansKR-Bold.ttf` · `Sora-SemiBold.ttf` · `Sora-Bold.ttf` · `Sora-OFL.txt`. 모두 SIL OFL. Sora는 라틴 전용(한글·₩ 없음).
- `Assets/Resources/UI/Icons/*.png` 64px 흰색 아이콘 19개: `tool-{conveyor,fast,bridge,pallet,table,splitter,scanner}`, `ui-{pause,play,shop,status,help,save,load,new,exit,close,check,parcel}`. 스캐너 아이콘만 빨간 선이 들어 있다.

## Task H1 — UiKit을 테마 + 부품 라이브러리로 리팩토링

- `UiTheme`(또는 UiKit 안의 상수 블록): 스펙 §12.2의 색·반경·글꼴 토큰. 기존 `UiKit.Panel/Fg/Accent…` 이름을 쓰는 곳은 새 토큰으로 옮긴다.
- 둥근 모서리 스프라이트: 런타임에 안티에일리어싱 SDF로 반경별 9-slice 스프라이트를 만들어 캐시한다(채움용, 1px 테두리용, 그림자용 블러). `Image.type = Sliced`. 반경은 디자인 px 기준으로 CanvasScaler 기준 해상도(1280×800)에서 시안과 같은 크기로 보이게 한다.
- 글꼴: `Fonts.Body`(Noto Medium) · `Fonts.Bold`(Noto Bold) · `Fonts.Regular` · `Fonts.Num`(Sora SemiBold) · `Fonts.NumBold`(Sora Bold). Resources에서 못 찾으면 Noto Regular → 내장 폰트 순으로 대체. `FontStyle.Bold` 사용을 전부 없앤다.
- 아이콘: `UiKit.Icon(name)` → Sprite(캐시). 임포트 설정은 `Assets/Editor/`의 AssetPostprocessor로 `Assets/Resources/UI/` 아래를 Sprite·밉맵 없음·무압축·Clamp·Bilinear로 고정한다(.meta를 손으로 쓰지 않는다).
- 부품 팩토리: Panel(반경·선·그림자), Pill, Button(아이콘?·라벨?·키캡?·변형 normal/on/primary/danger/ok, hover·pressed 표현), Keycap, Chip(점+라벨+값), IconTile(색 타일+아이콘), Card(모달 카드), ModalBackdrop, Divider, Badge. 레이아웃은 HorizontalLayoutGroup/ContentSizeFitter로 내용 폭에 맞춘다(고정 폭 하드코딩 최소화).
- 테스트(PlayMode 또는 EditMode): 스프라이트가 border를 가진 Sliced인지, 다섯 폰트가 서로 다른 객체로 로드되는지, 아이콘 19개가 로드되는지.

## Task H2 — HUD를 부품으로 다시 짓기

`HudController`의 Build* 함수를 스펙 §12.3대로 다시 만든다. 잔액 카드 + 칩, 상단 버튼 줄(아이콘·키캡, `새 게임` → `메뉴`), 시계 + 배지, 핫바 슬롯(번호·가격·아이콘 타일·이름·방향 배지·선택 빛), 프롬프트 알약 + 꼬리, 토스트 + 색 막대, 상태줄 + 체력 알약(80% 흰 눈금 유지), 설정 배너, 돈 팝업(Sora Bold 18 + 그림자 Outline), 터치 버튼(둥근 사각·원형). 기존 동작(텍스트 내용, 갱신, 키 처리, 테스트가 찾는 이름)은 유지한다. 1280×800 기준으로 배치하고 좁은 화면 규칙(시안 `@media`)은 지금 있는 좁은 화면 처리 수준에서 맞춘다.

## Task H3 — 패널과 미니 상태바

`Panels`의 상점·상태 서랍, 검사·해체·하루 요약 카드, 조작 카드를 시안 카드로 다시 짓는다(검사 라벨 종이색 상자, 승인 ok·거부 danger 버튼, 요약 행 구분선, 구매 버튼). `StatusBars`의 미니 상태바를 알약으로 바꾼다(바라보는 대상 앰버 테두리). 열기/닫기/Esc 우선순위 동작은 그대로.

## Task H4 — 시작 화면

- 새 컴포넌트 `StartScreen`(Game 레이어). 부팅 직후 표시, 세션 일시정지·HUD 숨김·`InputController.SetBlock` 사용. 스펙 §12.1의 배치·메뉴·키보드 조작·푸터.
- 이어하기: `SaveFile`이 있으면 표시하고 미리 읽어 `N일차 · ₩금액`을 보여준다. 읽기 실패면 버튼을 숨긴다. 실행 시 `boot.ReplaceSession`.
- 새 게임: `boot.StartNewGame()` 후 닫기. 종료: `Application.Quit()`(에디터·모바일에선 숨김).
- 인게임 `메뉴` 버튼: 일시정지 후 시작 화면 재표시, 첫 버튼은 **계속하기**(현재 세션 재개, 닫을 때 원래 일시정지 상태 복원).
- 테스트 보호: 기존 PlayMode 테스트가 시작 화면에 막히지 않도록 정적 플래그(예: `StartScreen.ShowOnBoot`)를 두고, 테스트 어셈블리의 `[SetUpFixture]`에서 꺼 둔다. 시작 화면 자체 테스트는 플래그를 켜고 부팅해 ① 표시되고 틱이 멈춤 ② 새 게임 후 숨겨지고 틱 진행 ③ 저장 파일(`SaveFile.PathOverride` 임시 경로) 있을 때만 이어하기 표시 ④ 메뉴 → 계속하기가 세션을 유지함을 확인한다.

## Task H5 — 스크린샷

`ScreenshotTests`에 시작 화면 캡처 `docs/design/unity-title.png`를 추가하고 기존 캡처를 다시 만든다. 시안 `3d-mockup-demo.png`와 `unity-demo.png`를 나란히 놓고 부품별 차이를 적는다.

## 검증 명령

```bash
~/.dotnet/dotnet test DotnetTests/Core.Tests
```
```bash
/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults /tmp/pf-edit.xml -logFile /tmp/pf-edit.log
```
```bash
/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults /tmp/pf-play.xml -logFile /tmp/pf-play.log
```
```bash
/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testCategory Screenshot -testResults /tmp/pf-shot.xml -logFile /tmp/pf-shot.log
```
