# R8 — 런타임의 이동의 축을 따라간다 (리그 20노드 → 22노드)

2026-11-23 · **계획 — 구현 전.** 런타임의 일이 `dev`에 합쳐진 뒤에 시작한다.
읽을 것: 런타임 `ked-presentation-runtime` `docs/PLAN_move-axes.md`(무엇이 왜 바뀌나) → 이 저장소 `CLAUDE.md` · `docs/MAINTENANCE.md` · `src/Ked.Presentation.Core/vendoring.md`(동기화 절차) · 앞선 같은 일 [`PLAN-rig-layers.md`](PLAN-rig-layers.md)(R7).

## 0. 한 문장

> 런타임이 슬롯 계층에 노드 둘(`CharSlot_Move_X` · `CharSlot_Move_Y`)을 더하고 커맨드 `move_by` · `slide_in` · `slide_out`을 걷는다. 이 툴은 **코어 사본과 리그 덤프를 다시 들이는 것**으로 따라간다 — 걷히는 노드가 없어 저작본은 그대로다.

## 1. 이 툴에 닿는 것 · 닿지 않는 것

| | |
|---|---|
| 닿는다 | 코어 사본(`src/Ked.Presentation.Core` — 노드 상수 · 폴드 · `SlideMotion`이 사라진다) · 내장 덤프(`Mt.Authoring/Defaults/ExportedTuning/rig-schemas.json` 외)와 시험 픽스처 · 하이어아키에 보이는 노드 수 · 문서와 그림 설명서의 그림 |
| 닿지 않는다 | **모션 · 이모지 · 클립의 키** — 새 노드는 슬롯 계층이고 모션이 키를 잡는 것은 초상 계층이다(런타임 M1: 이동 계층은 모션 계층과 섞지 않는다). 컨트롤러 표 · `motions/2` · `emojis/2` · `library/` |
| 그대로 | 씬의 자리 — 자리(place)는 `CharFocus_Track`에서 풀고 오프셋은 `CharSlot_Track`에 넣는다(`SceneAsset.Position` · `ScenePreview.PlaceNode`). 두 노드는 남는다 |

이 툴은 대본 · 커맨드를 모른다 — 커맨드 넷(`left` · `right` · `up` · `down`)을 툴에 들일 일은 없다. 주석에 남은 `move_by`라는 낱말만 고친다(`ScenePreview.cs` 63행 언저리 · `MainWindowTests.cs` 970행 언저리 · `MainWindow.Scene.cs`의 도움말).

## 2. 단계 (한 번에 하나 — 단계마다 전체 테스트가 통과한 채로 커밋)

| # | 내용 | 확인 |
|---|---|---|
| S1 | 조사 — 런타임의 그 커밋에서 코어와 덤프를 떠(`git archive`) `diff -r`로 가른다. 소비자(`Mt.Authoring` · `Mt.App` · 테스트)가 쓰는 코어의 이름 가운데 사라지는 것을 찾는다(`SlideMotion` · 옮겨진 노드 상수 …). **새 노드가 `MotionRules`의 키잉 금지 노드 · 컨트롤러 표에 어떻게 실려 오는지** 본다 | 목록을 이 문서 §4에 적는다 |
| S2 | 코어 사본 전체 동기화 + 순수 테스트 · 덤프(`rig-schemas.json` · `export-report.json` …) · 픽스처. 소비자의 컴파일 · 의미 적응은 소비자 쪽에서 | `diff -r` 0줄 · 전체 빌드와 테스트 · `vendoring.md`의 표 |
| S3 | 화면 — 하이어아키 · 채널 박스에 새 노드가 어떻게 보이는지 정한다(§3 P1). 주석의 낱말 | 헤드리스 그림(`MT_RENDER_DIR`)으로 본다 |
| S4 | 문서 — `HANDOFF.md` · `MAINTENANCE.md` · `USER-GUIDE.md` · `AUTHORING-FILES.md`의 노드 수 · 계층 그림, 그림 설명서의 그림(바뀐 화면만) · `SESSION-HANDOFF.md`에 한 줄 | |

## 3. 정할 것 — 구현 세션이 소유자에게 묻는다

| # | 물음 | 제안 |
|---|---|---|
| P1 | 새 노드 둘(`CharSlot_Move_X` · `_Y`)을 컨트롤러로 열어 모션에서 키를 잡게 하나 | **열지 않는다** — 이동은 대본의 커맨드가 하는 일이고 모션은 초상 계층만 움직인다(런타임 M1). 슬롯 계층의 다른 노드와 같은 대우(무대 뼈대처럼 감추거나 잠근다) |
| P2 | 씬 오브젝트의 오프셋(`CharSlot_Track`)을 그대로 두나 | 그대로 둔다 — 씬은 "모션이 없을 때의 자리" 하나다. 런타임의 쓰는 규칙(시작 자리는 `place`)과도 맞는다 |
| P3 | 덤프를 뜰 런타임 커밋 | 이동의 축이 `dev`에 합쳐지고 튜닝 덤프를 다시 낸 커밋(런타임 X3). 소유자에게 커밋을 받는다 |

## 4. 조사에서 찾은 것

(S1에서 채운다)

## 5. 하지 않는 것

- 저작본의 변환 — 걷히는 노드가 없다.
- 커맨드 넷의 미리보기 · 대본 읽기.
- 초상의 크기(R7이 남긴 것 — `PLAN-rig-layers.md` §6 P3)는 이 일과 따로다.
