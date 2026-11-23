# R8 — 런타임의 이동의 축을 따라간다 (리그 20노드 → 22노드)

2026-11-23 · `dev` · 상태: **S1 끝(조사 — §4) · §3 정함(P1 ~ P3 제안대로) · S2 하는 중.** 런타임은 `dev` @ `ddee266e`(이동의 축이 합쳐지고 덤프를 다시 낸 커밋).
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

## 3. 정한 것 (소유자, 2026-11-23 — 셋 다 제안대로)

| # | 물음 | 정한 것 |
|---|---|---|
| P1 | 새 노드 둘(`CharSlot_Move_X` · `_Y`)을 컨트롤러로 열어 모션에서 키를 잡게 하나 | **열지 않는다 · 툴에서 따로 잠그지 않는다** — 이동은 대본의 커맨드가 하는 일이고 모션은 초상 계층만 움직인다(런타임 M1). 컨트롤러 표에 줄을 더하지 않는다. 키잉 금지는 R7처럼 코어 `MotionRules.ForbiddenNodes` 그대로다(§4 — 두 노드는 거기 없다). 막아야 한다면 런타임이 그 목록에 더하고 다음 동기화로 들어온다(그러면 하이어아키에 저절로 "키잉 금지"로 보인다). 툴은 도움말의 낱말만 고친다 |
| P2 | 씬 오브젝트의 오프셋(`CharSlot_Track`)을 그대로 두나 | **그대로 둔다** — 씬은 "모션이 없을 때의 자리" 하나다. 런타임의 쓰는 규칙(시작 자리는 `place`)과도 맞는다. 씬의 자리(화면 지점)를 `CharFocus_Track`에 푸는 것도 그대로 — 셋 다 회전 · 배율 위의 평행이동이라 화면은 같다. `CharSlot_Track`에 푸는 쪽은 R7 §6 P2로 따로 남는다 |
| P3 | 덤프를 뜰 런타임 커밋 | **`ddee266e`**(소유자가 알려 줬다) — 이동의 축(X1 ~ X3 · §10)이 `dev`에 합쳐지고 튜닝 덤프를 다시 낸 커밋. 덤프는 X1에서 손으로 맞춘 것과 내용이 같다(§4) |

## 4. 조사에서 찾은 것

2026-11-23 · 런타임 `ddee266e`(`dev` — X1 ~ X3 · §10 · 덤프를 다시 낸 커밋)에서 `git archive`로 떴다. 지금 사본은 `b56a2ebc`와 `diff -r` 0줄이다 — 출발점에 로컬 수정이 없다.

| | 본 것 |
|---|---|
| 코어 차이 | ⓐ 런타임에만: `Read/` 3(`YarnLineReader` · `LineRead` · `LineTimetable`) · `Spec/` 4(`StageCommandSpec` · `StageCommandSpecTable` · `.Core` · `.Fork`) · `Tuning/CommandSpecsFileDto.cs`. ⓑ 내용이 다른 것 11: `NodeTargets.Placement` · `.Staging` · `StageReducer` · `.Anchor` · `.Background` · `.Placement` · `.Staging` · `LineBeatSchedule` · `YarnCommandTextExtractor` · `UnitToken` · `Documentation~/target-boundary.md`. ⓒ 사본에만: `Reduce/SlideMotion.cs` — 런타임이 걷었다(M9). 로컬 수정은 없다 |
| 이동의 축 밖의 것 | `b56a2ebc..ddee266e`의 코어 커밋 9개 가운데 넷은 R8 밖이다 — 명세 표(K1 `e5f81084`) · 줄 읽기(K2 `33c89805`) · 옛 표기 걷기(N5 `ebbde5ad` — `LineBeatSchedule.Build` · `ScheduledCommand` · `LegacyStepName` · 추출기의 `LegacySteps` · `BareCommands` · `step`) · 종류 여섯(`fbb7e3a2`). 전체 동기화(R7 D1)라 함께 들어온다. 이동의 축이 더한 것: `SlotMoveXNodeId` · `SlotMoveYNodeId` · `TryGetSlotMove` · `PlaceForFocusAt` · `ScreenPointRatios.Names` · `UnitToken.FormatUnits` · 폴드의 넷 · `move_reset`(Move만) · `cast`의 되돌리기 목록 |
| 사라지는 이름의 소비자 | **없다.** 소비자(`Mt.*`와 그 시험)가 부르는 코어는 `MotionRules` · `NodeTargets`(깊이 · 초상 · 포커스 자리) · `StageReducer.Apply` · `ApplyAll` · `CreateInitialState`(`slot` · `motion`만 접는다)다 — 걷힌 것(`SlideMotion` · 위 N5의 것)을 부르는 곳이 없다. 컴파일 적응은 없다 |
| 새 노드와 키잉 금지 | `MotionRules`는 바뀌지 않았다 — `ForbiddenNodes`는 포커스 셋 그대로라 **코어는 `CharSlot_Move_X` · `_Y`의 키를 받는다**(`CharSlot_Track` · `CharSlot_Rotation`과 같다). 런타임 M1("이동 계층은 모션 계층과 섞지 않는다")은 검증 규칙이 아니라 쓰는 법으로만 있다 |
| 새 노드와 화면 | 컨트롤러 표(`controllers.json`)에 줄을 더하지 않으면 새 노드에 손잡이가 없다 — 기본(컨트롤러 보기)의 하이어아키 · 채널 박스에는 보이지 않는다. 리그 노드 보기에서는 `CharSlot_Track` 밑에 두 단이 끼고, 이름이 `CharSlot_`로 시작하니 저절로 "배치 축" 색과 도움말이 붙는다(`PlacementNodes.IsPlacementAxis`). 매니퓰레이터의 노드 후보(`ViewportView.NodeChoices` — 덤프의 노드에서 키잉 금지만 뺀다)에도 든다 |
| 덤프 | character 21 → 23항목(`__root` 포함). 새 노드 둘은 `CharSlot_Track`의 값 그대로(부모만 다르다), `CharFocus_Rotation`의 부모가 `CharSlot_Move_Y`로. 다른 리그 · `base-resolution.json` · `presets/depth.json` · `focus-tuning.json`은 같다. `export-report.json`은 시각만(11-23 03:38 UTC). 런타임이 함께 내는 `command-specs.json` 등은 들이지 않는다 — 이 툴은 대본 · 커맨드를 모른다 |
| 픽스처 · 견본 | `Fixtures/rig-schemas.json`은 덤프와 같은 것으로 바꾼다(지금도 `b56a2ebc`의 덤프와 바이트가 같다). `stage-parity/motions.json` · `poses.json`은 런타임 `ToolExport~`와 그대로 같다 — 넘길 것이 없다. `motions.nod-bow.json`의 원본(`Assets/Dialogue/motions.json`)도 `b56a2ebc` 그대로다 |
| 순수 시험 | 이쪽 29개(28 + 이쪽 판 `EmojiPlaybackTests` — 런타임 쪽은 바뀌지 않았다) → 31개. 빠지는 것 `SlideTargetTests`(12건). 드는 것 `LineTimetableTests`(8) · `StageCommandSpecTableTests`(14) · `YarnLineReaderTests`(16 — 그 가운데 하나는 `Assets/Dialogue`의 예제 대본이 없으면 Ignore). `StageCommandSpecTableParserTests`는 들이지 않는다 — using 없이 런타임의 `StageCommandParser` · `ScreenCoverPresetDBSO`를 쓰고 파서 표의 원문 파일을 읽는다. 바뀐 파일 7개(`StageReducerTests` · `UnitTokenTests` …)는 그대로 교체. 시험 속성을 세면 580 → 616(건너뜀 1) |
| 소비자 시험 | 22노드가 닿는 곳 둘 — `RigSchemaFileTests`의 `21`(→ 23)과 `MainWindowTests`의 하이어아키 경로(`CharSlot_Track` 다음에 두 단). 같은 파일의 주석 "덤프 노드 26단"은 R7 전의 수다 |
| 낱말 | `move_by`가 남은 곳: `ScenePreview.cs` 63행 · `MainWindowTests.cs` 970행 · **`controllers.json`의 `placement` 묶음 `note`**(채널 박스 머리의 도움말 — "배치 커맨드(move_by·rotate_by)와 같은 노드라…"). `MainWindow.Scene.cs`에는 없다. 같은 결로 낡은 말: 하이어아키의 배치 축 도움말과 `PlacementNodes` 주석의 "런타임 place/size가 움직인다" — R7 뒤로 `size`는 초상 계층(`CharacterPortrait_DepthScale` · `_DepthOffset`)을 놓는다 |
| 저작본 | 저장소의 저작본(`library/` · `examples/`)에 걷히는 노드의 키는 없다. 다만 **스키마 지문(`builtOn`)이 든 문서는 열 때 알림이 뜬다** — 새 노드 [`CharSlot_Move_X`, `CharSlot_Move_Y`] · 부모가 바뀐 노드 [`CharFocus_Rotation`: `CharSlot_Track` → `CharSlot_Move_Y`]. 사라진 노드가 없어 이름 바뀜으로 읽히지 않고, 막지 않는다. 커밋된 저작본에는 지문이 없다. 소유자 PC의 **커밋되지 않은** `library/cards.motion-authoring.json`(과 자동 저장본)에는 20노드의 지문이 들어 있다 — 소유자의 것이라 손대지 않는다. 열어서 "지금의 스키마를 문서에 적기"로 받으면 알림이 걷힌다 |
| 그림 | 그림 설명서의 그림 가운데 리그 노드 보기를 찍은 것이 없다(`GuideShots` · `EmojiShots`) — 새 노드가 보이는 그림이 없으니 다시 뽑을 그림도 없을 것이다. S3에서 노드 보기를 한 장 뽑아 눈으로 본다 |
| 시험의 기준선 | `LibraryTests`는 작업 트리의 `library/cards.motion-authoring.json`을 읽는다 — 소유자 PC에서는 커밋되지 않은 그 파일이다. 기준선은 S2 전에 소유자 PC에서 지금의 `HEAD`로 잰다 |
| 따로 볼 것(이번에 하지 않는다) | 코어가 `ScreenPointRatios.Names`(13점)를 내놓았다 — 씬의 자리 콤보(`ScenePreview.ScreenPoints` — 9점의 손 사본)가 그것을 부를 수 있다 |

## 5. 하지 않는 것

- 저작본의 변환 — 걷히는 노드가 없다.
- 커맨드 넷의 미리보기 · 대본 읽기.
- 초상의 크기(R7이 남긴 것 — `PLAN-rig-layers.md` §6 P3)는 이 일과 따로다.
