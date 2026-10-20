# Parcel Factory

택배 공장 경영 게임. 작업자를 직접 조종해 레일·검사대·분배기·팔레트를 설치하고, 입고된 택배를 검사해 출고 트럭과 반품 트럭으로 보낸다. Unity 6 (URP) 기반, PC와 모바일(가로) 지원.

## 구조

- `Assets/Scripts/Core/` — 순수 C# 게임 로직 (격자, 컨베이어 시뮬레이션, 경제, 작업자, 시계, 저장). Unity에 의존하지 않으며 `DotnetTests/`에서 NUnit으로도 돌아간다.
- `Assets/Scripts/Game/` — Unity 레이어 (월드 생성, 뷰, 입력, HUD, 패널). 씬은 코드로 조립된다.
- `Assets/Tests/` — EditMode(코어) · PlayMode(부팅·입력·HUD 스모크, 스크린샷 하네스).
- `Assets/ThirdParty/Kenney/` — Kenney CC0 3D 에셋. `Assets/Resources/Fonts/` — Noto Sans KR (SIL OFL).
- `docs/superpowers/specs/` — 설계 문서(규칙의 단일 기준). `docs/superpowers/plans/` — 단계별 구현 계획.
- `docs/design/` — 3D 시안 프로토타입(`prototype/parcel-factory-3d.html`)과 렌더 스크린샷.

## 실행

- Unity 6000.3.26f1으로 프로젝트를 열고 `Assets/Scenes/Main.unity`를 Play.
- macOS 빌드: `Unity -batchmode -nographics -projectPath . -buildOSXUniversalPlayer Builds/ParcelFactory.app -quit`

## 테스트

```bash
~/.dotnet/dotnet test DotnetTests/Core.Tests
```

```bash
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults /tmp/edit.xml
```

## 조작

WASD/방향키 이동 · Space 달리기 · E 집기/놓기/설치/검사 · X 해체 · R → 화살표로 방향 설정(분배기·팔레트는 R → 1/2) · 1~7 도구 · B 상점 · C 상태창 · Esc 취소/닫기/빈손.
