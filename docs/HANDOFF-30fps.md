# 30fps로 — 런타임이 프레임의 기준을 바꿨다 (2026-11-18)

> 상태: **끝(11-18)** — 아래 §0. 남은 것은 런타임 쪽의 견본 바꾸기 하나(§0 표의 끝 줄).
> 근거: 공개 런타임 `ked-presentation-runtime` `fix/progressionScene` `0faca847`(한 커밋), 그쪽 `docs/PLAN_frames-30fps.md`.
> §1 ~ §4는 일을 시작할 때 쓴 계획 그대로 두었다.

## 0. 정한 것 · 한 것 (11-18)

정한 것(소유자 — "추천대로", `_l`은 걷는다):

| | 정한 것 |
|---|---|
| ① 반입 기준 | `0faca847`에서 지금 들인다 — 그 가지의 다른 커밋은 진행 · 문서뿐이라 Presentation Core는 `dev` + 30fps와 같다. 세 파일만 부분 반입(전체 재반입은 남은 빚) |
| ② 저작본의 키 | 판을 `motion-authoring/3`으로 올려 옛 문서(/2)는 읽지 않는다. 옮기는 것은 툴 밖의 한 번 쓰는 `tools/authoring-24to30.py`(프레임 n → round(n × 1.25), 0.5는 올림 — 초를 지키고 차이 ≤ 1/60초. 파일의 꼴은 그대로 두고 숫자만). 툴에 옮겨 읽는 코드는 두지 않는다(마이그레이션을 두지 않는다는 결정 그대로) |
| ③ 예시 · 견본 | 툴이 예시를 30fps로 옮겨 짓고, 그 `emojis.json`(fps 30)을 런타임이 새 견본으로 받는다. 시험의 비교 기준(`Fixtures/runtime-emojis.json`)도 그것 |
| ④ `flip` | 툴에 "⇄ 반대쪽 보기"와 `mt pose --side flip`을 둔다. library의 좌우 사본 `_l` 22개를 걷는다 |

한 것:

| 커밋 | 무엇 |
|---|---|
| `040bd40` | 코어 사본 세 파일 · 순수 시험(`EmojiSideTests` 새로) · 툴의 24 → 30(스냅 · 눈금 · 화면 글자) · 판 /3 · 옮기는 스크립트 · `examples` · `library` 옮김 · 예시 빌더 · 견본 픽스처 · 시험 |
| `c614bb0` | `flip` 미리보기(`EmojiPreview` · 뷰포트 · "⇄ 반대쪽 보기" · `mt pose --side`) · library `_l` 걷기(남은 22종의 꼴은 그대로) · README |
| (다음 커밋) | 문서(`AUTHORING-FILES` · `USER-GUIDE` · 그림 설명서 · `MAINTENANCE` · `HANDOFF` · 예시 README) · 그림 다시 뽑기 |
| **남은 것 — 런타임** | 런타임의 견본 `Assets/Dialogue/emojis.json`을 `examples/emoji/emojis.json`(fps 30)으로 바꾼다. 그쪽 `docs/emoji-format.md`의 "견본은 24로 적어 두었다"와 `PLAN_frames-30fps.md` §3도 함께. 런타임은 소유자가 커밋한다 |
| 소유자 PC | 자동 저장본(`examples/emoji/emoji-examples.autosave.motion-authoring.json` 등)은 /2라 열리지 않는다 — 필요하면 `python3 tools/authoring-24to30.py <파일>` |

## 1. 런타임에서 바뀐 것

| | 전 → 뒤 |
|---|---|
| `DurationToken.FramesPerSecond` — 대본의 `Nfr` | 24 → **30** |
| `EmojiDefinition.DefaultFps` — `emojis.json`에 `fps`가 없을 때 | 24 → **30**. `fps`를 적은 파일은 적힌 값 그대로 |
| 파서의 프레임 생략값 · 데모 대본의 `Nfr` | 같은 초가 되게 30fps 프레임으로 옮겼다(`fade_in` 14fr → 18fr 등, 차이 ≤ 1/60초) |
| 런타임 견본 `Assets/Dialogue/emojis.json` | **그대로** `"fps": 24` — 24로 저작한 키라 옮기지 않았다 |

- 모션(`motions.json`)은 초라 상관없다. 재생은 모두 초로 돈다 — 24 · 30은 저작 단위다.
- 소유자 결정(11-18): 원본 영상과 작업공간 도구가 30fps라 저작 단위를 맞춘다.

## 2. 이 툴에서 할 일

### 2.1 코어 사본 — 정본에서 들인다

사본은 고치지 않는다(`src/Ked.Presentation.Core/vendoring.md` — 흐름은 런타임 → 사본). 바뀐 곳은 셋이다.

| 사본 파일 | 정본과의 차이 |
|---|---|
| `Tokens/DurationToken.cs` | 상수와 주석 두 줄뿐 |
| `Tuning/EmojisFileDto.cs` | 주석 두 줄뿐 |
| `Emoji/EmojiDefinition.cs` | 30fps 말고도 58줄이 다르다 — 사본이 옛 판이라 런타임의 이모지 쪽 고르기(`<<emoji 누구 이름 flip>>`, 런타임 `docs/PLAN_emoji-side.md`)가 들어 있지 않다. 이 파일을 들이면 그것도 같이 온다 |

- **정할 것 ①** — 세 파일만 부분 반입하나, 사본 전체를 맞추나. 부분 반입이면 `EmojiDefinition.cs`의 `flip` 쪽 변경에 소비자(`Mt.Authoring` · `Mt.App`)가 따라야 하는지 본다. 전체 동기화는 11-16~17의 큰 변경(줄의 자리 L1–L5 · `<<Nfr>>` 별칭 걷기 · 무대 스텝 …)까지 함께 온다.
- 순수 시험(`tests/Ked.Presentation.Core.Tests/EditMode/DurationTokenTests.cs` · `EmojiPlaybackTests.cs`)도 정본 것으로 바꾼다.
- `vendoring.md`의 표(원본 커밋 · 반입일 · 부분 반입 절)를 고친다.

### 2.2 툴의 24

코어를 따라가는 곳(`EmojiAsset.Fps = EmojiDefinition.DefaultFps`, `TimeSnap.FramesPerSecond` · `PlaybackClock.Frame` = `DurationToken.FramesPerSecond`)은 사본을 들이면 저절로 30이 된다. 손으로 고칠 것은 상수 · 글자 · 표다.

| 곳 | 지금 |
|---|---|
| `src/Mt.Authoring/Graph/TimeSnap.cs` | `DefaultStep = 1.0 / 24.0`(11), 눈금 단계 `{ 1, 2, 4, 6, 12, 24, 48, 120, 240, … }`(34) — 30의 약수 · 배수로(예: 1 · 2 · 3 · 5 · 10 · 15 · 30 · 60 · 150 · 300) |
| `src/Mt.App/MainWindow.axaml.cs` | 스냅 콤보 `"1/24초 (1프레임)"`와 `SnapSteps`의 `1.0 / 24.0`(50 · 52), 시간 표시 `"프레임 (24fps)"`(174), 글자 `"24 fps"`(520) |
| `src/Mt.App/Views/ClipTimelineView.cs` | `SnapStep = 1.0 / 24.0`(76) |
| `src/Mt.Authoring/Editing/MotionEditor.Emojis.cs` | 새 이모지 길이 1초 — 주석 "24프레임"(13). 1초 그대로면 30프레임 |
| `src/Mt.App/MainWindow.NewEmoji.cs` | "길이 1초(24프레임 — 뒤에 바꾼다)"(294) |
| `src/Mt.App/MainWindow.Emojis.cs` · `MainWindow.Shortcuts.cs` · `Preview/PlaybackClock.cs` · `Settings/ToolSettings.cs` · `Preview/ClipBake.cs` · `Validation/EmojiValidator.cs` · `Validation/EmojiExport.cs` · 보기들(타임 슬라이더 · 그래프 · 도프시트 · 구간 슬라이더 · 이모지 삶 띠 · 뷰포트) | 주석 · 툴팁 · 알림 글의 "24fps" · "1/24초" |

찾는 법: `grep -rn "24" src --include=*.cs | grep -i -E "fps|frame|프레임"`(코어 사본 `src/Ked.Presentation.Core`는 빼고 본다).

### 2.3 이미 만든 것 — 정할 것

- **정할 것 ②: 저작본의 키.** 모션 · 이모지 저작본의 키 시각은 초다. 1/24초 격자에 놓은 키는 30fps에서 프레임 사이에 놓인다(1/24초 = 1.25프레임).
  - 모션: 초 그대로 나가므로 재생은 같다. 스냅 격자와만 어긋난다.
  - 이모지: 내보낼 때 초 × 30으로 프레임이 되어 소수 프레임(6.25 …)이 된다. `EmojiValidator`가 "프레임에 안 맞는 키"로 알린다. 가까운 30fps 프레임으로 옮길지 소유자가 정한다.
- **정할 것 ③: 예시 `examples/emoji`.** 지금 `"fps": 24`다. 빌더 `tests/Mt.Authoring.Tests/EmojiExamples.cs`로 짓고, `EmojiExampleTests`가 런타임 견본(`tests/Mt.Authoring.Tests/Fixtures/runtime-emojis.json`, `fps` 24)과 **프레임마다** 같은지 잰다. 툴이 30으로 내보내면 프레임으로는 맞댈 수 없다.
  - 시간(초)으로 맞대게 시험을 바꾸거나, 견본을 30으로 다시 짓는다. 다시 짓는다면 런타임 견본이 먼저다(정본은 런타임).
- `examples/authoring`의 이모지 예시도 같이 본다.

### 2.4 문서 · 그림

- `docs/USER-GUIDE.md`(264), `docs/AUTHORING-FILES.md`(124 · 334 · 335 · 391 · 398 · 406 — `n / 24` 표와 보기), `docs/guide/README.md`(178), `docs/MAINTENANCE.md`(40 · 122), `examples/emoji/README.md`(14).
- 저작본의 형식 · 검증 규칙이 바뀌므로 `docs/AUTHORING-FILES.md`와 `examples/authoring`을 같이 고친다(`CLAUDE.md` 관습).
- 화면 글자("24 fps" · "1/24초")가 바뀌므로 그림 설명서의 그림을 다시 뽑는다 — `MT_RENDER_DIR=<폴더> dotnet test tests/Mt.App.Tests -c Release --filter Shots` 뒤 `python3 tools/guide-figures.py <폴더> docs/guide/img`.
- `docs/history/`는 끝난 일의 기록이라 고치지 않는다.

### 2.5 시험

24를 기대값으로 쓰는 시험 파일이 30개쯤이다(코어 사본 시험 · `Mt.Authoring.Tests` · `Mt.App.Tests`). 2.1 · 2.2를 한 뒤 전체 테스트로 찾는다. `MAINTENANCE.md` 122의 "시험은 기대값을 1/60으로 쓰는 곳이 있어 `SnapStep = 1.0 / 60`을 둔다"도 확인한다.

## 3. 차례 제안

1. 정할 것 ① ~ ③을 소유자에게 묻는다.
2. 코어 사본 들이기(2.1) — 한 커밋. 전체 테스트 통과.
3. 툴의 24(2.2)와 시험(2.5) — 한 커밋.
4. 저작본 · 예시(2.3) — 정한 대로.
5. 문서 · 그림(2.4) — 한 커밋.

## 4. 하지 않는 것

- 코어 사본을 손으로 고치지 않는다.
- 런타임의 견본 `emojis.json`을 여기서 바꾸지 않는다 — 바꾼다면 런타임에서 먼저(그쪽은 소유자가 커밋한다).
- 이 일의 다른 소비처(작업공간 포크 · 추출기)는 각자의 세션이 한다 — 런타임 `docs/PLAN_frames-30fps.md` §4.
