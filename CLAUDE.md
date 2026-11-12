# ked-motion-tool-characterRig

**캐릭터 리그와 이모지**를 다루는 저작 도구(.NET 10 + Avalonia 12). 구조는 유니티가 정한다 — 유니티가 덤프한 스키마를 단단하게 따르고, 캐릭터와 이모지를 전용 컨트롤러로 다룬다.

- 먼저 읽을 것: `docs/PLAN-characterRig.md` — 방향 · 정해진 것 · 단계(R1~R6) · 먼저 정해야 하는 것. 단계가 끝날 때마다 그 문서에 결정과 상태를 적는다.
- 이모지는 `docs/PLAN-emoji.md`(런타임의 새 정의에 맞추는 단계 E1~E7)를 따른다.
- 이모지 모드의 화면(작업 모드 · 수명의 띠 · 뷰포트의 인물 · 움직임 가져오기 · 새 이모지의 첫 화면)은 `docs/PLAN-ux.md`(U1~U10)가 정한 것이다. 트윈머신(따로 뜨는 창)은 `docs/PLAN-tween.md`.
- 그 밖의 계획서(`docs/PLAN.md` · `PLAN-controller.md` · `PLAN-clip.md` · `PLAN-scene.md` …)는 지금 있는 기능의 명세다. 인수인계는 `docs/HANDOFF.md`, 코어 사본의 동기화는 `docs/MAINTENANCE.md`와 `src/Ked.Presentation.Core/vendoring.md`.
- `main`은 출발점(`ked-motion-tool-extend`의 `d37c678`) 그대로다. 일은 `dev`에서 한다.
- 형제 저장소: `ked-motion-tool-extend`(컷 — 씬 + 카메라 + 타임라인) · `ked-motion-tool-screenTransition`(화면 전환). 여기서는 그 둘의 일(세트 · 조립 · 컷 · 전환)을 하지 않는다.

## 구조

`Ked.Presentation.Core`(런타임 코어의 사본 — 정본은 런타임. 순수 C#, netstandard2.1 · C# 9) → `Mt.Authoring`(도메인) → `Mt.App`(Avalonia 화면) + `Mt.Cli`.
테스트: `tests/Mt.Authoring.Tests` · `tests/Mt.App.Tests`(xunit — 화면은 `HeadlessUi.Run`) · `tests/Ked.Presentation.Core.Tests`(NUnit).

## 명령

```bash
dotnet build KedMotionTool.sln -c Release
```

```bash
dotnet test KedMotionTool.sln -c Release --no-build
```

예시 문서(`examples/emoji` — 런타임의 견본 셋을 편집 명령으로 지은 것)를 다시 쓴다(빌더 `tests/Mt.Authoring.Tests/EmojiExamples.cs`를 고친 뒤):

```bash
MT_WRITE_EXAMPLES=1 dotnet test tests/Mt.Authoring.Tests -c Release --filter EmojiExampleTests
```

화면을 그림으로 뽑는다(헤드리스가 Skia로 실제 렌더 — 고친 화면은 눈으로 확인한다). `MT_RENDER_DIR`이 있을 때만 켜지고, 장면 테스트는 `RenderShots`(`tests/Mt.App.Tests/HeadlessUi.cs`)로 그림을 낸다 — 이모지 화면은 `EmojiShots`(예시 문서의 장면 `emoji-*`와 이모지 모드의 화면 `ux-*`):

```bash
MT_RENDER_DIR=<폴더> dotnet test tests/Mt.App.Tests -c Release --filter EmojiShots
```

## 관습

- 코드 주석 · 문서 · 커밋 메시지는 **한국어**. 커밋은 "무엇을 왜" 한 줄 + 본문.
- **한 번에 하나.** 단계마다 전체 테스트가 통과한 채로 커밋한다.
- `TreatWarningsAsErrors` — 안 쓰는 필드·using·인자는 빌드가 막는다.
- 편집은 `MotionEditor`의 명령으로만. **조작 하나 = 되돌리기 한 칸**, 같은 값은 기록하지 않는다. 테스트가 `editor.Revision`으로 이것을 본다.
- **안 쓰는 기능은 흔적 없이 지운다** — 코드 · 주석 · 문서 · 테스트까지.
- 단축키는 F1 표에, 쓰는 법은 README와 `docs/USER-GUIDE.md`에.
- 화면의 기능에는 테스트 손잡이(`internal … FromForm()`)와 헤드리스 테스트를 붙인다.
- **코어 사본은 고치지 않는다** — 정본은 런타임이고 흐름은 한 방향이다(런타임 → 사본). 필요한 것은 런타임에서 먼저 정한다. 이미 앞서 넣은 것(선반영)은 `vendoring.md`에 적혀 있고 갚아야 할 빚이다.
- **구조는 유니티가 정한다** — 리그 · 노드 · 컨트롤러 표를 툴에서 새로 짓지 않는다.
- PR은 요청받았을 때만 만든다.
