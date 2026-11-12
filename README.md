# ked-motion-tool-characterRig

**캐릭터 모션과 이모지**를 만드는 저작 도구(.NET 10 + Avalonia 12). 유니티 런타임(`ked-presentation-runtime`)이
정한 리그 위에서 마야식 그래프로 움직임을 만들고, 런타임이 읽는 파일로 내보낸다.

| 만드는 것 | 내보내는 파일 |
|---|---|
| 캐릭터 모션 — 초상을 리그의 컨트롤러로 움직인다(끄덕 · 숙임 · 깡충 …) | `motions.json` (`motions/2`) |
| 이모지 — 그림 여럿 + 자기 타임라인, 등장 · 머묾 · 퇴장 | `emojis.json` (`emojis/2`) |
| 연출 클립 — 모션 여럿을 한 캐릭터(또는 여러 캐릭터)에 겹쳐 보고, 구워서 모션 하나로 | (굽기 → `motions.json`) |

- **구조는 유니티가 정한다.** 리그 · 노드 · 컨트롤러 표는 런타임의 덤프(`ExportedTuning/rig-schemas.json`)에서 온다. 툴은 그 위에서 움직임만 만든다.
- **보이는 것 = 재생하는 것.** 미리보기는 런타임과 같은 계산(`Ked.Presentation.Core`)이다.
- 툴은 대본(Yarn) · 커맨드를 모른다. 카메라를 움직이는 일 · 컷 · 화면 전환은 형제 저장소의 일이다(`ked-motion-tool-extend` · `ked-motion-tool-screenTransition`).

## 처음 열기

```
dotnet run --project src/Mt.App/Mt.App.csproj
```

1. 툴바 맨 앞에서 **캐릭터 모션 | 이모지** 가운데 할 일을 고른다.
2. 예시를 연다: **열기…** → `examples/emoji/emoji-examples.motion-authoring.json`(이모지 견본 셋).
3. 캐릭터 모션은 왼쪽 **프리셋에서 추가**로 시작해 보거나, 런타임의 `motions.json`을 **가져오기**로 읽는다.
4. 고친 뒤 **저장**(저작 원본 `*.motion-authoring.json`) → **내보내기**(`motions.json` · `emojis.json`). 아래 문제 칸에 막는 문제가 없어야 나간다.

리그 덤프를 따로 싣지 않으면 내장본을 쓴다. 조작은 마야를 따른다 — **F1**이 단축키 표다.

## 명령줄 `mt` — 창 없이(AI · 스크립트 · CI)

```
dotnet run --project src/Mt.Cli -- info     a.motion-authoring.json
dotnet run --project src/Mt.Cli -- validate a.motion-authoring.json
dotnet run --project src/Mt.Cli -- export   a.motion-authoring.json motions.json
dotnet run --project src/Mt.Cli -- emojis   a.motion-authoring.json emojis.json
dotnet run --project src/Mt.Cli -- pose     a.motion-authoring.json --motion nod --time 0.3
dotnet run --project src/Mt.Cli -- pose     a.motion-authoring.json --emoji star --time 1.2 --off 1.0
dotnet run --project src/Mt.Cli -- bake     a.motion-authoring.json --clip greet --object s1 --name greet_s1
```

결과는 stdout의 JSON, 종료 코드는 0 됨 · 1 막는 문제 · 2 쓰는 법. 인자 없이 부르면 전체 쓰는 법이 나온다.

## 문서

| 읽을 것 | 내용 |
|---|---|
| [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) | 화면으로 만드는 법 — 캐릭터 모션 · 클립 · 이모지 |
| [`docs/AUTHORING-FILES.md`](docs/AUTHORING-FILES.md) | 파일을 직접 써서 만드는 법(AI · 스크립트) — 형식 · 규칙 · `mt`. 예시는 [`examples/authoring/`](examples/authoring) |
| [`docs/HANDOFF.md`](docs/HANDOFF.md) | 일을 이어받는 사람이 먼저 읽는 것 — 무엇인가 · 구조 · 지켜야 하는 것 · 연관 저장소 |
| [`docs/MAINTENANCE.md`](docs/MAINTENANCE.md) | 코드를 고치는 사람 — 코드 지도 · 불변식 · 자주 하는 작업 · 테스트 요령 · 함정 |
| [`docs/PLAN-characterRig.md`](docs/PLAN-characterRig.md) | 이 저장소의 방향 |
| [`docs/PLAN-cleanup.md`](docs/PLAN-cleanup.md) | 지금 하는 일(정리 → 설명서 → 불편 고치기) |
| [`docs/history/`](docs/history/README.md) | 끝난 일의 계획서 — 결정의 까닭 |
| [`examples/emoji/README.md`](examples/emoji/README.md) | 이모지 예시 |

## 빌드 · 테스트

```
dotnet build KedMotionTool.sln -c Release
dotnet test  KedMotionTool.sln -c Release --no-build
```

.NET 10 SDK(`global.json`). 패키지 판은 `Directory.Packages.props` 한 곳. 경고는 빌드를 깬다(`TreatWarningsAsErrors`).
