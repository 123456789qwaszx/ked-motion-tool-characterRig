# PLAN-cleanup — 정리

이 툴은 작가용 시나리오 툴(VnTool)에서 갈라져 나와 여러 번 방향을 바꿨다 — 캐릭터 모션 → 씬 → 연출 클립 → 이모지 → 화면 개편.
그 사이에 안 쓰게 된 것, 길이 둘인 것, 지난 사실을 적은 문서가 쌓였다. 사용 설명서를 쓰기 전에 걷어 낸다(소유자, 2026-11-12).

일의 순서(소유자와 정한 것): **1단계 정리(이 문서)** → 2단계 사용 설명서(사람용 · 파일 작성자용) → 3단계 불편 수집과 개선.

## 0. 먼저 정해진 것 (소유자, 2026-11-12)

- **씬 탭은 남긴다** — 잘 쓰고 있다.
- **연출 클립은 남긴다** — 여러 모션을 한 캐릭터에 합성하는 데 쓴다. 모션은 다른 캐릭터와 화면 구도를 보면서 만드는 것이 정확하다.
- 그 안에 과한 지점이 보이면 알린다(§1의 A).

## 1. 조사 (2026-11-12, `dev` `2489759`)

코드 39,871줄(도메인 11,826 · 화면 17,783 · CLI 592 · 테스트 19,670), 문서 3,059줄. 코드는 고치지 않고 읽기만 했다.
**권고**의 뜻: 지운다 · 남긴다 · 고친다 · **묻는다**(소유자의 쓰임을 알아야 정할 수 있다).

### A. 기능 — 이 저장소의 일인가

| # | 무엇 | 지금 | 권고 |
|---|---|---|---|
| A1 | **카메라 모션** — 카메라(zoom · pan)를 키와 커브로 움직이는 모션. 왼쪽 "카메라 모션 추가", 모션 탭의 카메라 값 칸(`MainWindow.Camera.cs` 117줄 · `CameraRig.cs` 66줄 · `CameraMotionTests` 197줄) | 살아 있다 | **묻는다.** `CLAUDE.md`는 "컷 — 씬 + 카메라 + 타임라인"을 형제 저장소(`extend`)의 일이라고 적는다. 화면 구도를 **보는** 데는 아래 A2의 멈춘 카메라로 충분하다 — 카메라를 **움직이는** 것이 이 툴의 일인가 |
| A2 | **씬 탭의 "카메라(샷)"** — 멈춘 zoom · pan 값 | 살아 있다 | **남긴다.** 구도를 보며 모션을 만든다는 쓰임 그대로다 |
| A3 | **클립의 상태 블록** — 타임라인에서 보이기 · 자리 · 층 · 오프셋 · 카메라를 시간에 따라 바꾸는 블록 다섯(`ClipStage.cs` 267줄 · `ClipStageTests` 316줄 + 타임라인 화면) | 살아 있다 | **묻는다.** 모션을 합성하는 데는 모션 블록 · 배역 · 트랙 · 굽기 · 끄기면 된다. 상태 블록은 "컷을 조립하는" 쪽이다 — 쓰는가 |
| A4 | **`clips.json` 내보내기**(`PLAN-clip.md` K6) | 계획만 있고 코드는 없다 | 계획에서 지운다(클립은 굽기로 모션이 되어 나간다). 쓸 생각이면 알려 달라 |
| A5 | **씬 여럿** — `AddScene` · `DuplicateScene` · `RemoveScene` · `RenameScene` · `SetActiveScene` | 편집 명령과 테스트만 있고 **화면이 없다**(README: "씬은 하나만 쓴다") | 명령과 테스트를 **지운다**. 파일의 `scenes[]` 읽기는 남긴다(형식) |
| A6 | 화면이 부르지 않는 편집 명령 — `SetPreviewPortrait`(+ `PreviewSettings`) · `RemoveKey` · `SetClipScene` · `RenameClipRole` · `SetClipRoleRig` · `SetClipBlockRepeat` · `SetClipBlockMotion` · `CancelGesture` | 테스트만 부른다 | **지운다**(명령 · 테스트). `SetClipBlockRepeat` · `SetClipBlockMotion`은 화면에 "되풀이" · "모션 바꾸기"가 정말 없는지 한 번 더 보고 |
| A7 | 이모지 오른쪽의 **"구간과 잇기"** 칸(구간 목록 · 이름 · 시작 · 끝 · 잇기 콤보 셋) | 수명의 띠와 같은 일을 한다 | **남기되 접는다** — 띠가 기본이고 이것은 숫자로 적을 때만 쓴다. 기본으로 접힌 묶음으로 |
| A8 | 타임 슬라이더의 **구간 띠**(이모지) | 바로 위 수명의 띠와 같은 것을 한 번 더 그린다 | 타임라인 보기에서는 **지운다**(게임처럼 재생의 등장 · 머묾 · 퇴장 표시는 남긴다) |
| A9 | `tools/prefab-*.py` — 유니티 프리팹에서 무대 뼈대를 다시 뽑는 스크립트 | `MAINTENANCE.md`가 가리킨다 | **남긴다**(런타임의 프리팹이 바뀌면 쓴다) |
| A10 | 프리셋 모션 9종("프리셋에서 추가") · `motions.json` 가져오기 · CLI `mt` | 살아 있다 | **남긴다** — 2단계의 설명서가 쓴다 |

### B. 꼬인 것 — 동작은 그대로 두고 푼다

| # | 무엇 | 권고 |
|---|---|---|
| B1 | **`MainWindow.axaml.cs` 4,299줄** — 툴바 · 왼쪽 목록 · 채널 박스 · 씬 탭 · 모션 탭 · 그래프 툴바 · 설정 창 · 단축키 창 · 문제 칸 · 전역 단축키가 한 파일에. 이모지 · 클립 · 카메라 · 모드 · 띠 · 트윈은 이미 따로 나와 있다 | 책임별 파일로 **나눈다**(`MainWindow.Toolbar.cs` · `.ChannelBox.cs` · `.SceneTab.cs` · `.Settings.cs` · `.Shortcuts.cs` …). 코드를 옮기기만 한다 |
| B2 | **설정 파일의 자리** — `%APPDATA%/ked-motion-tool/settings.json`. 형제 저장소 셋(`characterRig` · `extend` · `screenTransition`)이 같은 출발점에서 갈라졌으니 **같은 파일을 쓸 가능성이 높다** — 한 툴의 설정(작업 모드 · 창의 자리 · 최근 문서)을 다른 툴이 덮는다. 창 제목도 `ked-motion-tool` | **고친다** — 폴더와 제목을 `ked-motion-tool-characterRig`로. 처음 한 번은 옛 파일을 읽어 온다. 형제 저장소가 실제로 같은 자리를 쓰는지는 고치기 전에 확인한다 |
| B3 | **테스트 파일의 이름** — `EmojiLifeStripShellTests`에 수명의 띠 · 인물 · 움직임 가져오기 · 새 이모지 · 키 놓기 · 종류별 칸 · 트윈머신이 다 들어 있다(이번 세션에 쌓였다) | 주제별 파일로 **나눈다** |
| B4 | 화면의 말과 코드의 말 — 화면은 "그림", 코드와 파일은 `Rig`(런타임의 말) | **그대로 둔다.** 대응표는 설명서에 있다 |
| B5 | `MainWindow.Emojis.cs` 1,273줄 · `ViewportView.Emoji.cs` 1,547줄 · `GraphEditorView.cs` 2,360줄 · `ViewportView.cs` 2,328줄 | 지금은 **그대로 둔다**(한 화면의 일이다). B1을 한 뒤에 다시 본다 |

### C. 문서 — 사실과 다른 것, 지난 기록

| # | 무엇 | 지금 | 권고 |
|---|---|---|---|
| C1 | **`HANDOFF.md`** | 2026-10-28에 멈춰 있다 — "산출물은 `motions.json` 하나", "오른쪽은 탭 셋", "배역 · 클립은 없다(코드는 아직)", 저장소 `ked-motion-tool`, 기준 런타임 `ked-stage-runtime`. 전부 지금과 다르다 | **다시 쓴다**(짧게 — 무엇인가 · 구조 · 불변식 · 연관 저장소 · 어디를 읽는가) |
| C2 | **`README.md` 300줄** | 머리말이 "캐릭터 모션 전용 · 산출물 하나"다. 기능 설명이 단계마다 덧붙어 한 줄이 수백 자다. "탭 셋" 같은 지난 말이 남아 있다 | **줄인다** — 무엇인가 · 처음 열기 · 문서의 지도 · 빌드. 기능 설명은 설명서(2단계)로 옮긴다 |
| C3 | **계획서 13개** — `PLAN.md` · `PLAN-M5` · `-maya` · `-inspector` · `-undo` · `-scene` · `-clip` · `-controller` · `-emoji` · `-ux` · `-tween` · `maya-comparison.md` | 전부 끝난 일의 기록이다(결정의 까닭이 들어 있다). 지금의 명세와 섞여 있어 무엇이 지금 맞는지 알 수 없다 | `docs/history/`로 **옮긴다**(지우지 않는다 — 까닭을 찾을 때 읽는다). `PLAN-characterRig.md`(지금의 방향)와 이 문서만 `docs/`에 남긴다. 계획서에만 적혀 있고 지금도 유효한 규칙은 `MAINTENANCE.md`로 옮긴다 |
| C4 | **`MAINTENANCE.md`** | 코어 재반입 절차가 `ked-stage-runtime` 기준이다(지금 정본은 `ked-presentation-runtime`) | **고친다** — 절차를 지금 것으로, C3에서 온 규칙을 받는다 |
| C5 | **`USER-GUIDE.md`** | 기능을 나열한 372줄 | 2단계에서 **다시 쓴다**(여기서는 손대지 않는다) |
| C6 | 깨진 참조 — `publish-windows.ps1`(없다) · `ked-stage-runtime/docs/motion-contract.md`(다른 저장소) · README의 "VnTool과 같은 판" | | C1 · C2 · C4에서 함께 **고친다** |
| C7 | **`CLAUDE.md`** | 계획서의 이름들을 가리킨다 | C3 뒤에 **맞춘다** |

### D. 확인한 것 — 문제없다

- **코어 사본**(`src/Ked.Presentation.Core`) — 선반영이 없다(`vendoring.md`). 손대지 않는다.
- **안 쓰는 타입** — 화면과 도메인에서 선언만 되고 쓰이지 않는 타입은 `PreviewSettings` 하나다(A6).
- **빌드 경고** — `TreatWarningsAsErrors`라 안 쓰는 필드 · 인자는 이미 없다.
- VnTool의 흔적(대본 · Yarn · 커맨드 편집 · 에셋 탐색기)은 **코드에 남아 있지 않다** — 문서의 문장 몇 개뿐이다(C1 · C2 · C4).

## 2. 소유자가 정한 것 (2026-11-12)

1. **A1 카메라 모션 — 걷는다.** 이 툴은 카메라를 움직이지 않는다. 멈춘 카메라(A2, 씬 탭의 "카메라(샷)")는 남는다.
2. **A3 클립의 상태 블록 — 걷는다.** 카메라 줄도 같이. 클립은 모션을 겹치는 데만 쓴다(모션 블록 · 배역 · 트랙 · 반복 · 끄기 · 굽기).
3. **A4 ~ A8, B1 ~ B3, C1 ~ C7 — 권고대로.**

## 3. 단계 (정한 뒤)

| 단계 | 내용 | 끝났다는 것 |
|---|---|---|
| C-1 | **기능 걷기** — A에서 지우기로 한 것을 하나씩(코드 · 테스트 · 문서). 한 번에 하나, 전체 테스트 통과 | 지운 것이 어디에도 남지 않았다 |
| C-2 | **꼬인 것 풀기** — B2(설정의 자리) → B1(큰 파일 나누기) → B3(테스트 파일) | 동작과 테스트 수가 그대로다 |
| C-3 | **문서** — C3(계획서 옮기기) → C1 · C4(다시 쓰기) → C2(README) → C7 | `docs/`를 처음 보는 사람이 무엇을 읽을지 안다 |

## 4. 진행

| 단계 | 상태 |
|---|---|
| 조사 | 끝 |
| C-1 A1 · A3 | 끝 — 카메라 모션(`CameraRig` · 카메라 값 칸 · "카메라 모션 추가")과 상태 블록(`ClipStage` · `ClipBlockKind` · "상태 놓기" · 카메라 줄)을 걷었다. `ClipBlock`은 `at · motion · mode · repeat · muted`만 남는다. `mt pose --clip`은 노드 값만 낸다(카메라 · 불투명도 없음) |
| C-1 A4 | 끝 — `clips.json`(K6)은 하지 않는다. 클립은 굽기로 모션이 되어 `motions.json`으로 나간다. `PLAN-clip.md`는 C-3에서 지난 기록으로 옮긴다 |
| C-1 A5 · A6 | 끝 — 화면이 부르지 않는 편집 명령을 지웠다: `AddScene` · `DuplicateScene` · `RemoveScene` · `RenameScene` · `SetActiveScene` · `SetPreviewPortrait` · `RemoveKey` · `SetClipScene` · `RenameClipRole` · `SetClipRoleRig` · `SetClipBlockRepeat` · `SetClipBlockMotion` · `CancelGesture` |
| C-1 A7 · A8 | 끝 — 이모지 오른쪽의 "구간과 잇기"는 접힌 채로 시작한다. 타임 슬라이더는 타임라인 보기에서 구간 띠를 깔지 않는다(수명 재생의 단계 띠는 남는다) |
| C-2 B2 | 끝 — 설정은 `%APPDATA%/ked-motion-tool-characterRig/settings.json`. 옛 자리(`ked-motion-tool`)의 것은 읽어 오지 않는다(처음에는 읽어 오게 했다가 소유자 지시로 뺐다 — 새 설정으로 시작한다). 창 제목도 `ked-motion-tool-characterRig` |
| C-2 B1 | 끝 — `MainWindow.axaml.cs` 4,292줄 → 1,409줄. 옮기기만 했다(줄을 모아 견줘 빠진 것이 없음을 확인): `MainWindow.Shortcuts.cs`(전역 단축키 · 시간 명령) · `.Settings.cs`(설정 · 창 배치 · 설정 창 · F1) · `.ChannelBox.cs` · `.Scene.cs`(하이어아키 · 씬 탭) · `.Outliner.cs`(그래프 왼쪽 채널 목록) · `.Problems.cs`(문제 칸) · `.Files.cs`(열기 · 저장 · 가져오기 · 내보내기). 남은 것은 필드 · 생성자 · 화면 짜기 · 모션 목록과 인스펙터 · 명령 · 작은 도우미 |
| C-2 B3 | 끝 — `EmojiLifeStripShellTests`를 주제별로: `EmojiLifeStripShellTests`(수명의 띠) · `EmojiHostShellTests`(인물 · 고르기) · `EmojiMoveShellTests` · `NewEmojiShellTests` · `EmojiKeyShellTests`(키 단추 · 종류별 칸) · `TweenShellTests`. 창을 여는 일은 `EmojiExampleWindow`가 한다. 테스트 수는 그대로(862) |
| C-3 | 끝 — 계획서 12개와 정리 전의 README를 `docs/history/`로 옮겼다(그 폴더의 `README.md`가 무엇이 지금과 다른지 적는다). `HANDOFF.md`를 다시 썼고, `MAINTENANCE.md`를 지금 것으로 고쳤고(코드 지도 · 불변식 · 재반입 절차 · 테스트 요령 · 함정), `README.md`를 줄였고(무엇인가 · 처음 열기 · `mt` · 문서의 지도 · 빌드), `CLAUDE.md`를 맞췄다. 코드 주석의 VnTool 언급도 지웠다 |

**1단계(정리) 끝 — 2026-11-12.** 테스트 859건(코어 426 · 도메인 301 · 화면 132 — 마이그레이션을 걷은 뒤). 다음은 2단계(설명서).

### 적어 둘 것

- **정리 전 README의 기능 설명**은 `docs/history/README-2026-11.md`에 있다 — 2단계의 설명서를 쓸 때 재료로 쓰고, 설명서가 서면 필요 없다. `USER-GUIDE.md`는 2단계에서 다시 쓴다(지금은 지운 기능의 대목만 뺐다).
- **`vendoring.md`의 본문**은 그대로 두었다(반입한 그때의 사실을 적은 표다). 코어의 전체 재반입은 남은 빚이다.

- **안 쓰는 using**은 보통 빌드가 막지 않는다(`IDE0005`는 꺼져 있다). 파일을 나누며 한 번 훑어 저장소 전체에서 지웠다 — 임시 `.editorconfig`(`dotnet_diagnostic.IDE0005.severity = warning`)와 `-p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true`로 빌드하면 나온다.

- **마이그레이션은 두지 않는다**(소유자, 2026-11-12 — 1단계가 끝난 뒤의 지시). 옛 형식을 옮겨 읽던 것을 전부 지웠다: `motion-authoring/1`(`defaultMode` → 재생 기준) · `motions/1`(채널 `mode` 알림) · `preview.portrait`(→ 기본 씬의 초상, `PreviewSettings`) · 카메라 모션 · 카메라 줄 · 상태 블록을 읽을 때 버리던 것. 저작본은 `motion-authoring/2`가 아니면 거부한다.
- **씬이 여럿인 문서는 그대로 읽고 쓴다**(`scenes[]` · `activeScene`). 더하거나 바꾸는 명령만 없다. 테스트가 본다.
- 반복 · 모션 바꾸기는 `UpdateClipBlock`(블록 인스펙터) 하나로 한다.

- 같은 지시로 뺀 것: 설정 파일을 옛 자리에서 읽어 오던 것 · `motions.json`의 스키마가 달라도 읽기를 시도하던 것 · "호환용"으로 남아 있던 `PlaybackClock.Loop` · `ViewportView.Loop` · `ViewportView.SetPortrait(비트맵)`.
- **남긴 것 — 컨트롤러의 별칭**(`controllers.json`의 `aliases` — `Track_Move_X/Y` · `ActingScale_Y`). 주석은 "옛 모션"이라 하지만 런타임의 지금 견본(`nod`)이 그 노드에 키를 둔다 — 옛 파일을 위한 것이 아니라 지금 리그의 축별 노드를 한 손잡이로 보이는 것이다.
