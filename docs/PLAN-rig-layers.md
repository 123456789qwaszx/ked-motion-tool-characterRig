# 리그의 계층을 따라간다 — R7

2026-11-22 · `dev` · 상태: **조사 · 결정 끝, 고치기 전** · 넘겨받은 일: `ked-story-tools`의 `docs/handoff/character-rig.md` · 런타임 `docs/PLAN_rig-layers.md`(§2 · §3 · §6 · §7 R7)

## 0. 한 문장

런타임(`ked-presentation-runtime` `dev` @ `b56a2ebc`)이 캐릭터 리그를 25노드에서 20노드로 바꿨다(포커스 · 슬롯 · 초상). 구조는 유니티가 정하고 이 툴은 따른다 —
코어 사본을 다시 들이고, 덤프를 바꾸고, 걷히거나 이름이 바뀐 노드 11개를 가리키는 곳을 고친다.

## 1. 조사 (2026-11-22, `538a99e`에서)

| | 본 것 |
|---|---|
| 코어 차이 | 줄바꿈을 빼고 견주면 런타임에만 있는 파일 21 · 내용이 다른 파일 22 · 사본에만 있는 파일 10. 런타임 작업 트리의 고친 표시는 줄바꿈뿐이다 — 내용은 `b56a2ebc`와 같다 |
| 부분 반입이 안 되는 까닭 | 옛 노드 이름은 사본의 리듀서(`StageReducer.cs` · `.Staging.cs` · `.Show.cs` · `FocusStageReductions.cs`)에 있는데, 런타임은 그 리듀서를 `NodeTargets`로 다시 짰다(`StageNodeClaim` → `NodeTarget`, `PlaceFocusStageReduction` · `SetDepthStageReduction`은 없다). 닿는 파일만 들이려면 사본을 손으로 고쳐야 한다 |
| 소비자 | 코어의 걷힌 형식을 쓰는 곳은 `Preview/ScenePreview.cs`(자리 · 깊이) · `PortraitLayout.cs` · `MotionPreview.cs`(주석)와 `SceneTests`. 런타임의 순수 시험은 30개 파일(지금 사본 쪽은 25개) |
| 새 덤프 | character 26 → 21항목(`__root` 포함). `camera` 리그(7항목)와 `screenEffect`의 `Cover_Image`가 새로 들어 있다. 런타임 코어는 카메라 리그의 모션(흔들림 — `Camera_Shake`만, restore만)도 받는다 |
| 컨트롤러 표 | 옛 노드를 가리키는 손잡이: `stance`(`CharSlot_Scale`) · `depth`(`CharSlot_DepthScale`, 굽기 `CharSlot_DepthY`) · `facing`(`CharacterPortrait_ActingScale_X`) · `body`의 별칭(`Track_Move_X` · `_Y`) · `squash`의 별칭(`ActingScale_Y`) |
| 저작본 | 저장소 안의 5개(`library/` · `examples/`)에는 걷힌 노드의 키가 없다 — 모션은 `CharacterPortrait_Track_Move` · `SwayPivot` · `ActingScale`만 쓴다. `examples/emoji`의 씬에 `scaleX` · `scaleY`(1)가 적혀 있다 |
| 견본 | `Fixtures/stage-parity/motions.json`의 `step`(`CharSlot_Track_X`) · `grow`(`CharSlot_Scale`)와 `motions.nod-bow.json`의 `nod`(`Track_Move_Y`)에 걷힌 노드의 키가 있다. 런타임 `ToolExport~`의 사본은 `b56a2ebc`에서 **이미 남는 노드로 손으로 옮겨져 있었다**(`step` → `CharSlot_Track`, `grow` → `CharacterPortrait_ActingScale`) — 처음 조사에서 "30fps 전의 것이라 다르다"고 적은 것은 틀렸다 |

## 2. 정한 것 (소유자, 2026-11-22)

| # | 결정 |
|---|---|
| D1 | **코어는 전체 동기화한다** — `vendoring.md`의 검증(`diff -r` 0줄)까지. 부분 반입으로 쌓인 빚을 함께 갚는다 |
| D2 | **옛 저작본을 옮기는 변환은 두지 않는다**(`MAINTENANCE.md` §4.6). 걷힌 노드의 키가 든 문서는 스키마 계약이 막는 문제로 알리고, 그 채널은 다시 잡는다 |
| D3 | **`stance` 컨트롤러와 씬 오브젝트의 배율(`scaleX` · `scaleY`)을 걷는다** — 노드(`CharSlot_Scale`)가 사라졌고 런타임도 `scale_by`를 걷었다(L7). 크기는 깊이로만 놓는다 |

물어 두고 답을 받지 못한 것 — 말이 없으면 이대로 한다:

- 견본의 `grow`(배율 · commit)는 `CharacterPortrait_ActingScale`로 옮긴다. `step`은 `CharSlot_Track`의 x.
- `camera` 리그는 덤프에 두되 툴에서 열지 않는다(이 툴은 카메라를 움직이지 않는다).

이름만 바뀐 것은 그대로 따라간다: `depth` → `CharacterPortrait_DepthScale`(굽기의 Y는 `CharacterPortrait_DepthOffset`) · `facing` → `CharacterPortrait_Mirror` ·
`body` · `squash`의 별칭 노드는 걷는다 · 이모지가 서는 자리 `CharSlot_Scale` → `CharacterPortrait_DepthScale` · 키잉 금지는 코어의 `MotionRules.ForbiddenNodes`(포커스 셋) 그대로.

## 3. 단계

앞의 둘은 옛 스키마 위에서도 통과하게 잡았다 — 단계마다 전체 테스트가 통과한 채로 커밋한다.

| 단계 | 무엇 | 상태 |
|---|---|---|
| S1 | `stance` 컨트롤러와 씬의 배율을 걷는다 — 모델 · 편집 명령 · 직렬화 · 화면 · `mt` · 시험 · `AUTHORING-FILES.md` · 예시 | 끝 — Windows에서 기준선과 같다(Core 438 · App 144 · Authoring 332/334, §4의 둘만 실패). 예시 파일은 손으로 두 줄을 지웠다 — 빌더와 바이트가 같은지는 줄바꿈 실패에 가려 Windows에서 확인되지 않는다 |
| S2 | 시험과 견본의 키를 남는 노드로 — `Track_Move_X` · `_Y` → `CharacterPortrait_Track_Move`의 x · y, `CharSlot_Track_X` · `_Y` → `CharSlot_Track`. `stage-parity` 견본을 다시 낸다 | 끝 — 기준선과 같다. `grow`는 `CharacterPortrait_ActingScale`로 옮겼다(값은 그대로, 노드 이름만). `nod` · `bow`는 런타임 `Assets/Dialogue/motions.json`의 것과 글자가 같다 |
| S3 | 코어 전체 동기화 + 덤프 교체 + 소비자 적응 + 이름이 바뀐 노드 — 한 커밋(나누면 중간이 통과하지 않는다) | 끝 — Core 580/580 · App 144/144 · Authoring 332/334(§4의 둘만). `diff -r` 0줄 |
| S4 | 문서 · `vendoring.md` · 그림 다시 뽑기 · 런타임에 넘길 견본 | 문서 끝. **견본은 넘길 것이 없다** — S2에서 툴이 다시 낸 `stage-parity/motions.json` · `poses.json`이 런타임 `b56a2ebc`의 `ToolExport~` 사본과 바이트까지 같다(런타임이 손으로 옮긴 것과 툴이 낸 것이 일치). 런타임 쪽은 시험 머리 주석의 "손으로 옮겼다"를 출처 커밋 `f3b2858`로 고치면 된다. 그림은 아직 — 소유자 PC에서 `MT_RENDER_DIR`로 뽑아야 한다 |

## 4. 테스트를 돌리는 길

이 일을 하는 세션(클라우드 · PC의 리눅스 VM)에는 .NET SDK가 없고 받을 수도 없다. 소유자가 PC에서 돌리고 세션이 기록을 읽는다(`artifacts/`는 git 밖):

```powershell
dotnet build KedMotionTool.sln -c Release *> artifacts\r7-build.log; dotnet test KedMotionTool.sln -c Release --no-build *> artifacts\r7-test.log
```

**기준선(`538a99e`, Windows)**: 빌드 경고 0 · 오류 0. Core 438/438 · App 144/144 · Authoring 332/334.
Authoring의 둘은 이 일과 무관하게 Windows에서만 실패한다 — `LibraryTests.내보낸_것이_export에_둔_산출물과_같다`(emojis)와
`EmojiExampleTests.예시_파일은_빌더가_낸_것과_같다`. 툴이 쓴 JSON의 줄바꿈이 `\r\n`이고 저장소의 파일은 `\n`이다
(`EmojiExport` · `MotionDocumentJson`의 `WriteIndented`가 그 OS의 줄바꿈을 쓴다). CI(ubuntu)에서는 드러나지 않는다. 고칠지는 소유자가 정한다.

## 5. 이어받을 때 (2026-11-22 01:30에 멈췄다)

**어디서 멈췄나(11-22 02:00 고침).** S1~S3을 커밋했다(`6859998` · `f3b2858` · `9988f28`) — 아래 "S1에서 고칠 자리" · "S3에서 바꿔 부를 것"은 끝난 일의 기록이다.
남은 것: 그림 다시 뽑기(S4) · §6의 P2~P4를 소유자가 확인 · §4의 줄바꿈 실패를 고칠지.

**S1에서 고칠 자리**(`stance` · 씬의 배율 — `grep`으로 센 것, `bin` · `obj` 제외):

| 자리 | 무엇 |
|---|---|
| `Mt.Authoring/Defaults/controllers.json` | `stance` 줄 |
| `Model/SceneAsset.cs` | `SceneObject.ScaleX` · `ScaleY`, `PlacementNodes.Scale`, 머리 주석의 "배율 → `CharSlot_Scale`" |
| `Editing/MotionEditor.cs` | 오브젝트 복제(161~162) · `SetObjectPlacement`의 `scaleX` · `scaleY` 인자(212~228) |
| `Preview/ScenePreview.cs` | 배율을 트리에 넣는 줄(122 · 138) |
| `Serialization/MotionDocumentJson.cs` | 씬 오브젝트의 `scaleX` · `scaleY` 읽고 쓰기 |
| `Mt.Cli/MtCli.Build.cs` | `--scale` · `--scale-x` · `--scale-y`와 출력의 `scaleX` · `scaleY`(83~99) |
| `Mt.App/MainWindow.axaml.cs` · `MainWindow.Scene.cs` | `_objSx` · `_objSy` 칸, `CommitObjectPlacementFromForm`의 인자, 배치 칸을 펴는 조건 |
| 시험 | `MainWindowTests`(511 · 572의 컨트롤러 순서) · `SceneTests`(39 · 74) · `MtCliCreateTests`(153) |
| 예시 · 문서 | `examples/emoji/emoji-examples.motion-authoring.json`(빌더 `EmojiExamples.cs`를 고치고 `MT_WRITE_EXAMPLES=1`로 다시 쓴다) · `AUTHORING-FILES.md` · `USER-GUIDE.md` · `guide/README.md` |

이모지의 `scaleX` · `scaleY`(`EmojiExport` · `emojis.json`의 채널 이름)는 다른 것이다 — 손대지 않는다.

**S3에서 소비자가 바꿔 부를 것**(런타임 코어의 새 이름):

| 지금 | 새 코어 |
|---|---|
| `StageNodeClaim` · `MotionMath.ClaimFor` | `NodeTarget` · `MotionMath.TargetFor` |
| `PlaceFocusStageReduction.TryReduce` | `NodeTargets.TrySolveFocusPlacement`(`Reduce/NodeTargets.Placement.cs`) — 푼 값은 `CharSlot_Track`(`place x y`)이나 `CharFocus_Track`(`focus_move`)에 놓는다. 씬의 자리가 어느 쪽인지는 그때 정한다 |
| `SetDepthStageReduction.TryReduce` · `DepthYNodeId` · `DepthScaleNodeId` | `NodeTargets.TrySetDepth` · `DepthOffsetNodeId` · `DepthScaleNodeId` |
| `PortraitSizingReduction.Reduce` · `PortraitSizingStageReduction.ImageNodeId` · `ParentNodeId` | `NodeTargets.TryPortraitSizing` · `SourcePixelsSizing` · `PortraitImageNodeId` · `PortraitParentNodeId` |
| `ViewportView.Emoji.cs`의 `HostNode`(`CharSlot_Scale`) · `EmojiPreview` | `CharacterPortrait_DepthScale` |

사본의 순수 시험 가운데 런타임에 없는 형식을 보는 것(`OscillationFunctionsTests` · `PlacementBundleReductionTests` · `ReductionTests` …)은 런타임의 순수 시험 30개로 통째 바꾼다(`vendoring.md` 절차 2).

**조심할 것.**

- **git은 세션의 리눅스 VM에서 돈다.** 작업 트리의 `tools/authoring-24to30.py` · `guide-figures.py`가 고친 것으로 보이지만 줄바꿈(CRLF)뿐이다 — 소유자 PC의 것이니 `git add -A`를 쓰지 않고 고친 파일만 올린다. VM에는 git 사용자 설정이 없어 커밋할 때 `-c user.name=Claude -c user.email=noreply@anthropic.com`을 준다(앞선 커밋들과 같다).
- **런타임에서 들이는 파일은 CRLF다**(그쪽 작업 트리). 사본에 넣을 때 `\r`을 걷는다 — 견줄 때는 `diff --strip-trailing-cr`.
- **기록 파일은 UTF-16이고 한글이 깨져 있다.** 숫자와 영문(`.dll` 이름 · `Expected` · `Actual`)으로 읽는다. 요약 줄이 셋(Core · Authoring · App)인지 센다.
- **세션은 PC의 터미널에 글자를 넣지 못한다**(컴퓨터 사용에서 터미널 · 탐색기는 보기와 클릭만). 테스트는 §4의 한 줄을 소유자가 돌린다.
- **런타임 저장소는 읽기만 한다.** 견본(`stage-parity/motions.json` · `poses.json`)은 이쪽이 원본이다 — S4에서 두 파일을 넘기고 커밋은 소유자가 한다(`MAINTENANCE.md` §4.7).
- 그림 설명서의 그림은 화면이 바뀌면 다시 뽑는다(S1에서 Scale X · Y 칸이 빠진다) — `MT_RENDER_DIR`도 소유자 PC에서 돌려야 한다.

## 6. S3에서 정하고 간 것 — 소유자가 뒤집을 수 있다

전체 동기화를 하니 노드 이름 말고도 런타임의 동작이 바뀐 곳이 드러났다. 묻지 않고 아래처럼 두었다(2026-11-22).

| # | 무엇 | 둔 것 | 까닭 |
|---|---|---|---|
| P1 | 깊이의 Y | level 커브의 값 그대로(`DepthTuningFile.TryDepthOf` — 코어 `NodeTargets.TrySetDepth`와 같다). mid −664 · close −1000(전에는 −580.56 · −984). `FocusHeightOf` · `PreservedFocus`는 지웠다 | 런타임 `size`가 초점 보존을 걷었다(L6). 보이는 것 = 재생하는 것 |
| P2 | 씬의 자리(place) | 코어의 보조 계산(`NodeTargets.TrySolveFocusPlacement`)으로 `CharFocus_Track`의 값을 푼다. 오프셋은 전처럼 `CharSlot_Track` | 옛 `Track_Focus`의 이름 바뀜을 그대로 따랐다. 런타임에는 이 일을 하는 커맨드가 없다(L11) — 본래 자리(`CharSlot_Track`)에 푸는 쪽이 맞다면 `ScenePreview.PlaceNode` 한 줄과 오프셋을 더하는 순서를 고친다 |
| P3 | 초상의 크기 | 높이 맞춤 그대로(`PortraitLayout.ApplySizing`에 식 한 줄) | 런타임은 11-09에 원본 픽셀 크기로 바꿨다. 따라가려면 그림의 픽셀 크기와 배역 앵커(`role-anchor.json`)를 들여야 한다 — R7 밖. **지금 툴의 초상 크기는 런타임과 다르다** |
| P4 | 컨트롤러의 별칭 | 내장 표에서만 뺐다(`body` · `squash`). 기능(`ControllerAlias` · 채널 박스의 합산 · 도프 시트의 라벨)은 남아 있다 | `PLAN-cleanup.md` §105가 남긴 까닭(런타임의 `nod`가 별칭 노드를 쓴다)이 사라졌다 — 걷을지는 소유자가 정한다 |
| P5 | `camera` 리그 · `Cover_Image` | 덤프에 있을 뿐 툴에서 따로 열지 않는다 | 이 툴은 카메라를 움직이지 않는다 |
