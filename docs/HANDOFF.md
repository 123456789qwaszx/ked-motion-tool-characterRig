# 인수인계 — ked-motion-tool-characterRig

이 저장소의 일을 이어받는 사람(세션)이 먼저 읽는다. 쓰는 법은 [`USER-GUIDE.md`](USER-GUIDE.md), 코드를 고치는 절차는
[`MAINTENANCE.md`](MAINTENANCE.md), 방향은 [`PLAN-characterRig.md`](PLAN-characterRig.md), 지금 하는 일은 [`PLAN-cleanup.md`](PLAN-cleanup.md).
문서와 코드가 다르면 코드와 테스트가 이긴다. (2026-11-12 다시 씀)

## 1. 무엇인가

유니티 런타임(`ked-presentation-runtime`)이 정한 리그 위에서 **캐릭터 모션**과 **이모지**를 만드는 저작 도구다.
마야식 그래프 에디터 · 뷰포트 · 도프 시트로 움직임을 만들고, 런타임이 읽는 파일로 내보낸다.

| 만드는 것 | 화면 | 내보내는 것 |
|---|---|---|
| 캐릭터 모션 | "캐릭터 모션" 모드 — 모션 목록 · 하이어아키 · 채널 박스 · 그래프 | `motions.json` (`motions/2`) |
| 이모지 | "이모지" 모드 — 이모지 목록 · 그림 목록 · 수명의 띠 · 그래프 | `emojis.json` (`emojis/2`) |
| 연출 클립 | 캐릭터 모션 모드의 타임라인 탭 — 모션 블록을 배역 · 트랙에 놓아 겹쳐 본다 | 굽기로 모션 하나가 되어 `motions.json`으로 |

- 저장소 `123456789qwaszx/ked-motion-tool-characterRig`. 일은 **`dev`**에서 한다(`main`은 출발점 그대로). CI는 푸시와 PR에서 빌드 · 테스트(Linux).
- .NET 10 SDK · Avalonia 12 · xunit + Avalonia.Headless(화면) · NUnit(코어 사본의 테스트).
- 따로 뜨는 창: 새 이모지 · 트윈머신 · 설정 · 단축키(F1).

## 2. 정해진 것 (다시 논하지 않는다)

| 결정 | 뜻 |
|---|---|
| **구조는 유니티가 정한다** | 리그 · 노드 · 컨트롤러 표는 런타임의 덤프(`rig-schemas.json`)가 유일한 진실이다. 툴은 리그를 짓지 않는다. 이모지는 리그의 **틀**만 런타임이 정하고, 그림이 몇 장인가 · 이름 · 키는 툴이 정한다 |
| **코어 사본은 고치지 않는다** | `src/Ked.Presentation.Core`는 런타임 코어의 사본. 흐름은 한 방향(런타임 → 사본). 필요한 것은 런타임에서 먼저 정한다 — `vendoring.md` |
| **형식을 이끌지 않는다** | `motions/2` · `emojis/2`는 런타임의 형식이다. 툴이 먼저 바꾸지 않는다 |
| **보이는 것 = 재생하는 것** | 미리보기의 포즈는 코어의 함수(`MotionMath` · `EmojiPlayback` · 리듀서)로만 만든다. 화면용 근사식을 두지 않는다 |
| **진실은 저작 원본** | `*.motion-authoring.json`(`motion-authoring/2`)이 주인이고 `motions.json` · `emojis.json`은 산출물이다 |
| **편집은 `MotionEditor`만 지난다** | 조작 하나 = 되돌리기 한 칸. 같은 값은 기록하지 않는다. 테스트가 `editor.Revision`으로 본다 |
| **재생 기준은 파일에 없다** | 상대값/절대값은 대본의 `<<motion>>`이 정한다. 툴의 "재생 기준"은 키 값을 읽는 렌즈다 |
| **조작은 마야** | 단축키 · 그래프 · 타임 슬라이더 · 트윈머신. 표는 F1 |
| **이 툴이 하지 않는 것** | 카메라를 움직이는 것 · 컷 · 세트 · 화면 전환(형제 저장소의 일). 대본(Yarn) · 커맨드. 씬은 "캐릭터를 세워 보는 자리"이고 하나만 쓴다(멈춘 카메라 값은 있다) |
| **안 쓰는 기능은 흔적 없이 지운다** | 코드 · 주석 · 문서 · 테스트까지 |

## 3. 구조

```
Ked.Presentation.Core  (런타임 코어의 사본 — netstandard2.1 · C# 9, 고치지 않는다)
   └─► Mt.Authoring    (도메인 — Avalonia를 모른다)
          Model/          MotionDocument · MotionAsset · SceneAsset · ClipAsset · EmojiAsset · EmojiLife · EmojiMoves
          Editing/        MotionEditor(+ .Clips · .Emojis) — 모든 편집의 통로, 스냅샷 되돌리기, 끄는 조작(BeginGesture…)
          Serialization/  MotionDocumentJson(저작 원본) · MotionsJson(motions/2)
          Schema/         SchemaContract(문서가 스키마를 지키는가 — 막는 문제) · SchemaDiff(덤프가 달라졌을 때)
          Validation/     Motion · Clip · Emoji 검증, MotionFixes(돕는 손), MotionExport · EmojiExport
          Rig/            RigSchemaFile · BuiltinTuning(내장 덤프) · ControllersFile · EmojiRigFrame · EmojiLocatorFile · 깊이 · focus · 기준 해상도
          Preview/        ScenePreview · MotionPreview · ClipPreview · ClipBake · EmojiPreview · PoseEdit · PlaybackClock
          Graph/          순수 함수 — 좌표 · 히트 · 탄젠트 · 스냅 · 키 연산 · 선택 · 도프 행 · TweenMath
          Presets/ · Settings/
   ├─► Mt.App          (Avalonia — 그리기와 입력)
   │      MainWindow.*.cs   셸. 맡은 일별 partial: Mode · Tools · Shortcuts · Settings · Files · Scene · ChannelBox · Outliner ·
   │                        Problems · Clips · Emojis · Life · NewEmoji · Tween (+ axaml.cs = 필드 · 화면 짜기 · 모션 목록 · 명령)
   │      Views/            ViewportView(+ .Emoji) · GraphEditorView · DopeSheetView · TimeSliderView · RangeSliderView ·
   │                        ClipTimelineView · EmojiLifeStripView
   │      Services/         DocumentSession — 경로 · 덤프 · 자산 동기화 · 저장 · 내보내기 · 자동 저장
   └─► Mt.Cli          mt info · validate · export · emojis · pose · bake
tests/    Ked.Presentation.Core.Tests(NUnit) · Mt.Authoring.Tests · Mt.App.Tests(헤드리스 — HeadlessUi.Run)
examples/ emoji/ — 런타임의 이모지 견본 셋(빌더 tests/Mt.Authoring.Tests/EmojiExamples.cs가 쓴다)
tools/    유니티 프리팹에서 무대 뼈대를 다시 뽑는 스크립트
```

## 4. 파일과 형식

| 파일 | 형식 | 주인 | 비고 |
|---|---|---|---|
| `*.motion-authoring.json` | `motion-authoring/2` | 이 툴 | 모션 · 씬 · 클립 · 이모지 · 덤프 참조 · 지을 때의 스키마(`builtOn`). 손으로(또는 AI가) 써도 된다 — 키에는 값과 탄젠트 **종류**만 적으면 읽을 때 값이 선다 |
| `motions.json` | `motions/2` | 런타임 | 정규 직렬화 — 유니티 내보내기와 바이트까지 같다(골든 테스트) |
| `emojis.json` | `emojis/2` | 런타임 | 구간마다 따로 선 클립. 내보낸 것을 런타임 로더로 다시 읽어 본다 |
| `ExportedTuning/rig-schemas.json` 외 | 유니티 덤프 | 런타임 | 내장본 `Mt.Authoring/Defaults/ExportedTuning`. 덤프 옆의 깊이 · focus · 기준 해상도 · 컨트롤러 표 · 이모지 자리 표도 읽는다 |
| `controllers.json` | `controllers/1` | 리거 | 손잡이 표. 내장본 `Defaults/controllers.json` |
| `settings.json` | 툴 내부 | 사용자 | `%APPDATA%/ked-motion-tool-characterRig/` |

읽는 것은 지금 형식뿐이다 — 옛 형식을 옮겨 읽는 길(마이그레이션)은 두지 않는다. 다른 판의 저작본은 거부하고, 지운 기능의 흔적이 든 문서는 검증이 문제로 알린다.

## 5. 연관 저장소

| 저장소 | 관계 |
|---|---|
| `ked-presentation-runtime` | **정본.** 코어 · 리그 스키마 · 덤프 · 이모지의 정의와 재생. 소유자가 직접 커밋한다 |
| `ked-stage-runtime` | 코어 사본의 옛 출처(분리본). 사본의 대부분은 아직 그 `dc690c1` 기준이고 이모지만 정본에서 왔다 — 전체 재반입은 남은 빚이다(`vendoring.md`) |
| `ked-motion-tool-extend` | 형제 — 컷(씬 + 카메라 + 타임라인) |
| `ked-motion-tool-screenTransition` | 형제 — 화면 전환 |

세 툴은 같은 출발점에서 갈라졌다. 설정 파일은 툴마다 따로 둔다.

## 6. 지금 어디인가 (2026-11-12)

- 끝난 것: 이모지(새 정의, `emojis/2`) · 화면 다시 짜기(모드 · 수명의 띠 · 인물 · 움직임 가져오기 · 새 이모지) · 트윈머신 · **정리 1단계**(`PLAN-cleanup.md`).
- 다음: **2단계 설명서** — 사람이 화면으로 쓰는 법, 파일을 직접 쓰는 사람(다른 세션의 AI)이 보는 형식과 `mt` 쓰는 법. 그 다음 **3단계** — 불편을 모아 하나씩 고친다.
- 남은 빚: 코어 사본의 전체 재반입 · Windows 패키지와 그 CI.
- 헤드리스가 못 보는 것(사람이 확인한다): 실제 끌어다 놓기 · 파일 고르는 창 · 따로 뜨는 창이 위에 머무는가 · 한글 입력.

## 7. 일하는 법

`CLAUDE.md`가 정본이다. 요지 — 한 번에 하나, 전체 테스트가 통과한 채로 커밋(한국어, "무엇을 왜"). 단계는 소유자가 시작하라고 할 때 시작하고, 끝나면 계획서에 결정과 상태를 적는다. PR은 요청받았을 때만.
