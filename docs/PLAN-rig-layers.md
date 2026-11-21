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
| 견본 | `Fixtures/stage-parity/motions.json`의 `step`(`CharSlot_Track_X`) · `grow`(`CharSlot_Scale`)와 `motions.nod-bow.json`의 `nod`(`Track_Move_Y`)에 걷힌 노드의 키가 있다. 런타임 `ToolExport~`의 사본은 지금 이쪽 원본과 이미 다르다(30fps 전의 것) |

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
| S1 | `stance` 컨트롤러와 씬의 배율을 걷는다 — 모델 · 편집 명령 · 직렬화 · 화면 · `mt` · 시험 · `AUTHORING-FILES.md` · 예시 | 아직 |
| S2 | 시험과 견본의 키를 남는 노드로 — `Track_Move_X` · `_Y` → `CharacterPortrait_Track_Move`의 x · y, `CharSlot_Track_X` · `_Y` → `CharSlot_Track`. `stage-parity` 견본을 다시 낸다 | 아직 |
| S3 | 코어 전체 동기화 + 덤프 교체 + 소비자 적응 + 이름이 바뀐 노드 — 한 커밋(나누면 중간이 통과하지 않는다) | 아직 |
| S4 | 문서 · `vendoring.md` · 그림 다시 뽑기 · 런타임에 넘길 견본(`motions.json` · `poses.json`) | 아직 |

## 4. 테스트를 돌리는 길

이 일을 하는 세션(클라우드 · PC의 리눅스 VM)에는 .NET SDK가 없고 받을 수도 없다. 소유자가 PC에서 돌리고 세션이 기록을 읽는다(`artifacts/`는 git 밖):

```powershell
dotnet build KedMotionTool.sln -c Release *> artifacts\r7-build.log; dotnet test KedMotionTool.sln -c Release --no-build *> artifacts\r7-test.log
```

**기준선(`538a99e`, Windows)**: 빌드 경고 0 · 오류 0. Core 438/438 · App 144/144 · Authoring 332/334.
Authoring의 둘은 이 일과 무관하게 Windows에서만 실패한다 — `LibraryTests.내보낸_것이_export에_둔_산출물과_같다`(emojis)와
`EmojiExampleTests.예시_파일은_빌더가_낸_것과_같다`. 툴이 쓴 JSON의 줄바꿈이 `\r\n`이고 저장소의 파일은 `\n`이다
(`EmojiExport` · `MotionDocumentJson`의 `WriteIndented`가 그 OS의 줄바꿈을 쓴다). CI(ubuntu)에서는 드러나지 않는다. 고칠지는 소유자가 정한다.
