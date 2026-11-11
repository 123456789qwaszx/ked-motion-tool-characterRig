# 이모지 — 새 정의에 맞춘다 (계획, 2026-11-11)

이 문서는 툴의 이모지 쪽을 런타임의 새 정의에 맞추는 계획이다. 런타임의 과제 3(`ked-presentation-runtime` `docs/PLAN_emoji.md` §5)의 툴 쪽 절반이다.
정의는 런타임의 `docs/emoji-rig.md`, 나가는 형식은 `docs/emoji-format.md`가 규범이다 — 이 문서는 그것을 다시 적지 않고 **툴이 무엇을 바꾸는가**만 적는다.

| | |
|---|---|
| 런타임 | `feat/emojiRigSchema` @ `007d4c16` — 틀(`EmojiRigSchema`) · 대본(`emoji` · `emoji_off`) · 형식(`emojis/2`) · 재생(`EmojiPlaybackSystem`)이 서 있다. `007d4c16`에서 리그의 기본 배율 · 회전이 들어왔다(`65f001f9` 다음) |
| 툴의 지금 | `dev` @ `f842ace`(R3-2) 위에 **E1 ~ E7을 마쳤다**(2026-11-11, 단계마다 커밋 — E3 · E4도 따로) — 런타임의 이모지 코어와 새 덤프가 들어왔고, 버린 설계(공용 `emoji` 리그 · 스킨 · 공용 모션 셋 · 머무는 시간 · 붙는 리그)는 없다. 툴에서 새 정의의 이모지를 만들고 `emojis.json`(`emojis/2`)을 낸다 |
| 상태 | **계획이 굳었다**(소유자, 2026-11-11 — §6). **E1 ~ E7 끝**(§5). **E8(좌우) — E8-1 ~ E8-3 끝, 다음은 E8-4**(§5 "E8 — 좌우"). 이어받는 길은 §8 |

## 0. 한 줄

> 툴에서 이모지 하나를 열면 **그 이모지의 리그들이 한 타임라인에 함께 서고**, 키를 잡고, 구간을 나누고, `emojis.json`(`emojis/2`) 한 덩어리로 나간다. 미리보기는 런타임과 같은 계산이다.

## 1. 모델 — 지금과 새것

| 지금(`EmojiAsset`) | 새 모델 |
|---|---|
| `Rig` — 공용 `emoji` 리그(조각 6칸)를 가리킨다 | `Rigs` — 이모지가 리그 목록을 직접 가진다(이름 · 그림들 · **기본 자리 · 배율 · 회전**). 리그의 수와 이름은 여기서 정한다 |
| `Skin` · `SkinFlipped` — 조각마다 그림을 주는 스킨을 가리킨다 | 리그마다 그림(첫 장이 기본 스프라이트, 나머지는 그림 바꾸기 채널이 고른다). **스킨은 없어진다** — 스킨의 "놓임(자리 · 크기 · 회전)"은 리그의 기본값으로 옮겼다(런타임 `rigs[].place · scale · rotation` → `EmojiRig_Place`). 키는 그 위에서 1을 기준으로 잡는다 |
| `Enter` · `Hold` · `Exit` — 공용 모션을 이름으로 가리킨다 | 이모지가 **자기 타임라인 하나**를 가진다. 구간을 이름 붙여 나누고 등장 · 머묾 · 퇴장에 잇는다 |
| `HoldSeconds` — 머무는 시간 | 없어진다 — 머묾 구간을 이었는가가 종류를 정한다 |
| `AnchorNode` · `Locator` · `OffsetX/Y` | `OffsetX/Y`만 남는다(`Emoji_Offset`). 캐릭터 쪽 자리(RootSpace)는 런타임의 것이다 |

**리그의 틀은 덤프에서 온다**(`rig-schemas.json`의 `emoji` — 로케이터 · 오프셋 · 리그 하나, `repeatsPerRig`). 툴은 틀을 만들지 않고, 틀에 맞는 리그를 이모지에 몇 개 놓을지를 정한다.

## 2. 타임라인을 어디에 싣는가

툴의 편집 기반(그래프 에디터 · 도프 시트 · 타임 슬라이더 · 키 연산 · 되돌리기)은 모션(`MotionAsset` — 리그 + 채널 + 키)을 편집한다. 이것을 그대로 쓴다.

- 이모지의 타임라인 = **그 이모지가 소유한 모션 하나.** 이모지와 일대일이다(이름을 바꾸거나 지우면 함께). `motions.json`에는 나가지 않는다.
- 그 모션의 리그 = **이모지마다 조립한 리그**: 틀의 `Emoji_Offset` + 리그 이름마다 두 단(`EmojiRig_Place` · `EmojiRig_Image`). 채널의 대상은 `Emoji_Offset`(이모지 전체)과 리그의 그림 노드다.
- 키의 시각은 저작본 안에서는 지금처럼 초다(24fps). 내보낼 때 프레임으로 바꾼다.

## 3. 채널 — 불투명도와 그림 바꾸기

지금 채널의 종류는 코어의 `MotionChannelKind`(자리 · 배율 · 회전)다. 불투명도는 R3-1에서 걷었다(런타임의 캐릭터 모션이 받지 않는다).

- **툴 쪽에 채널 종류를 따로 둔다**(코어의 셋 + 불투명도 + 그림). 코어 사본은 고치지 않는다.
- 불투명도 · 그림은 **이모지의 타임라인에만** 쓴다 — 캐릭터 모션에 쓰면 검증이 막는다(런타임이 받지 않는다).
- 그림 바꾸기 = **계단 키**. 값은 그 리그의 그림 목록에서 몇 번째인가다(내보낼 때 스프라이트 키가 된다).
- 수치 채널의 계단 키도 둔다(`emojis/2`의 `step`).

## 4. 구간과 종류

- 타임라인에 **이름 붙은 구간**(시작 프레임 · 끝 프레임)을 둔다. 겹쳐도 된다.
- 구간을 **등장 · 머묾 · 퇴장**에 잇는다. 머묾을 이으면 머무는 이모지, 잇지 않으면 전체를 한 번 재생하고 사라지는 이모지다.
- 내보낼 때 구간마다 **따로 선 클립**으로 자른다(구간의 첫 프레임이 클립의 프레임 0). 구간의 양 끝에 키가 없으면 그 프레임의 값과 기울기로 키를 세운다 — 잘라도 모양이 같다.
- 구간이 없는 이모지는 타임라인 전체가 `main` 클립 하나다.

## 5. 단계

각 단계는 전체 빌드와 전체 테스트가 통과한 채로 끝낸다.

| 단계 | 내용 | 끝나면 |
|---|---|---|
| E1 | **들이기** — 코어 사본에 런타임의 이모지 코어(`Emoji/` · `EmojisFileDto`)를 들인다. 내장 덤프를 런타임 `65f001f9`의 것으로 바꾼다. 옛 이모지(공용 리그 · 스킨 · 공용 모션 참조 · 머무는 시간 · 붙이기)를 걷어 낸다 — 이름이 부딪친다(`EmojiPhase`) | 옛 설계의 흔적이 없다. 툴은 잠깐 이모지를 못 다룬다 — **끝**(아래 "E1에서 한 것") |
| E2 | **채널** — 툴 쪽 채널 종류(불투명도 · 그림 · 계단 키) | 이모지의 타임라인이 쓸 채널이 선다 — **끝**(아래 "E2에서 한 것") |
| E3 | **모델** — 새 `EmojiAsset`(오프셋 · 리그들 · 타임라인 · 구간 · 잇기), 편집 명령, 검증, 저작본 읽고 쓰기. 이모지마다 조립한 리그 | 문서에 새 이모지를 담는다(화면 없이) — **끝** |
| E4 | **내보내기** — `emojis.json`(`emojis/2`): 구간 자르기 · 초 → 프레임 · 코어의 규칙(`EmojiRules`)으로 검증. `mt` 명령 | 툴이 런타임이 읽는 파일을 낸다 — **끝** |
| E5 | **미리보기와 대조** — 미리보기는 내보낸 정의를 코어(`EmojiPlayback` · `EmojiClipMath`)로 돌린다. 툴이 낸 파일을 코어 로더로 읽어 프레임마다 그래프의 값과 맞는지 잰다 | 툴 = 게임 — **끝** |
| E6 | **화면** — 이모지 탭: 리그 목록(더하기 · 이름 · 순서 · 그림 · 기본 자리 끌기), 타임라인(그래프 · 도프 시트 · 타임 슬라이더), 구간 표지와 잇기, 재생(등장 → 머묾 되풀이 → 퇴장) | 툴에서 이모지를 만든다 — **끝** |
| E7 | 예시(런타임의 견본 셋 — `star` · `question` · `twinkle`)를 툴 문서로 다시 짓는다. README · 사용 안내 | 툴이 낸 파일 = 런타임의 견본 — **끝** |
| E8 | **좌우** — 런타임이 더한 두 칸(`fixedSide` · `upright`)과 부호 계산(`EmojiSide`)을 들이고, 모델 · 내보내기 · "왼쪽을 보는 인물로 보기" · 예시를 맞춘다 | 툴에서 양쪽을 다 보고, 낸 파일이 런타임의 견본과 같다 — **시작 전**(아래 "E8 — 좌우") |

### E1에서 한 것 (2026-11-11)

- **들였다** — 런타임 `007d4c16`(`65f001f9` 다음 커밋 — 리그의 기본 배율 · 회전이 들어온 것)에서 `Emoji/EmojiClip.cs` · `EmojiDefinition.cs` · `EmojiPlayback.cs` · `Tuning/EmojisFileDto.cs`, 그리고 `Tuning/RigSchemaDto.cs`를 통째로(`repeatsPerRig`가 들어오고 `hasImage`가 빠졌다).
  내장 덤프(`rig-schemas.json` · `export-report.json`)와 시험 픽스처도 같은 커밋의 것 — `emoji` 틀은 `__root › Emoji_Offset › EmojiRig_Place › EmojiRig_Image`(뒤 둘이 `repeatsPerRig`). `BuiltinTuning.Source`를 고쳤다.
  순수 테스트 `EmojiPlaybackTests`(형식의 규칙 · 그 프레임의 모습 · 재생의 단계 — 10개). `vendoring.md`에 "부분 반입"으로 적었다 — 나머지 사본은 아직 `ked-stage-runtime`의 것이라 런타임과의 `diff -r`는 0줄이 아니다.
- **걷었다** — §8의 목록 그대로(모델 · 편집 · 미리보기 · 검증 · `RigParts` · 화면 셋 · 뷰포트의 조각 그리기와 조각 놓기 · 타임 슬라이더의 구간 틱 · `mt emojis`와 `info`의 이모지 · 스킨 · `attachTo` · 문서의 `skins` · `emojis` · 스키마 계약/지문/이름 따라가기의 이모지 · 스킨 대목 · 테스트 · `examples/emoji`).
  붙이기는 모델째 걷었다 — `SceneObject.AttachTo` · `AttachNode` · `Skin` · `SceneAsset.ParentOf` · `AttachedTo` · `MotionEditor.AttachObject`, 미리보기 · 하이어아키 · 씬 탭의 "붙은 것" 갈래. 인스펙터 탭은 넷이 됐다(설정에 남은 탭 번호는 0~3으로 조인다).
  계획서 `PLAN-parts.md`도 지웠다(git 이력에 있다). README · `PLAN.md` · `PLAN-characterRig.md` · `CLAUDE.md`의 해당 대목을 고쳤다.
- **옛 문서** — 저작본의 `skins` · `emojis` · 오브젝트의 `attachTo` · `attachNode` · `skin`은 이제 모르는 필드라 읽을 때 버려지고, 다시 저장하면 사라진다(§6의 5 — 옮겨 읽지 않는다).
- **확인** — 전체 빌드 · 전체 테스트 761개 통과(코어 424 · 도메인 252 · 화면 85). 걷은 테스트만큼 줄고 `EmojiPlaybackTests` 10개가 늘었다.

### E2에서 한 것 (2026-11-11)

- **채널 종류** — `Mt.Authoring.Model.ChannelKind`(`position · scale · euler · alpha · image`)를 두고, 툴의 코드와 테스트가 코어의 `MotionChannelKind` 대신 이것을 쓴다(39개 파일).
  코어와 맞닿는 곳은 `ChannelKinds.ToCore` · `FromCore` 두 길뿐이다(`MotionAssetCodec` · 미리보기). `motions.json`을 읽고 쓰는 `MotionsJson`은 코어의 것 그대로다.
  앞의 셋은 코어와 같은 이름이라 옛 저작본이 그대로 읽힌다.
- **종류의 규칙은 한 곳**(`ChannelKinds`) — 성분(`Axes`: 회전 XYZ · 자리/배율 XY · 불투명도/그림 X), 중립값(배율 · 불투명도 1, 나머지 0 — 코어의 두 표와 같다), 이모지 전용(`IsEmojiOnly`), 언제나 계단(`IsAlwaysStepped` — 그림).
- **계단 키** — `TangentMode.Step`. 저장된 기울기는 들어오는 쪽 0 · 나가는 쪽 0이고, 코어로 건널 때(`KeyAsset.ToCurveKey`) 나가는 기울기가 무한이 된다 — 저작본 JSON에 무한을 싣지 않는다.
  `CurveSampler`는 다음 키 바로 앞에 점을 둬 계단을 세워 그린다. E4가 내보낼 때 `"step": true`가 된다.
- **그림 채널의 키는 언제나 계단** — 키를 넣는 길마다 지키지 않고 `MotionEditor.Mutate`의 끝 한 곳에서 바로잡는다(같은 칸 — 다른 모드로 바꾸려 해도 기록이 남지 않는다).
- **캐릭터 모션에는 못 쓴다** — `MotionValidator.EmojiOnlyIn`이 불투명도 · 그림 채널과 계단 키를 막는다(내보내기도 막힌다). 미리보기(`MotionPreview` · `ClipBake`)는 그런 채널을 빼고 얹는다(리그 트리에 자리가 없다).
  E3의 이모지 타임라인은 `motions.json`에 나가지 않으므로 이 검증을 타지 않는다 — 그쪽 검증은 E3 · E4에서 선다.
- **화면은 아직 없다** — 계단 단추와 `alpha` · `image` 채널을 만드는 손은 E6(이모지 탭)에 둔다. 지금 캐릭터 그래프 편집기에 두면 막히는 것을 만드는 손만 늘어난다.
- **확인** — 전체 빌드 · 전체 테스트 769개 통과(코어 424 · 도메인 260 · 화면 85). 새 테스트 `ChannelKindTests` 8개.

### E3에서 한 것 (2026-11-11)

- **모델** — `Model/EmojiAsset.cs`: `EmojiAsset`(이름 · 오프셋 · `Rigs` · `Timeline` · `Spans` · `In` · `Hold` · `Out`) · `EmojiRigAsset`(이름 · 그림들 · 기본 자리 · 배율 · 회전) · `EmojiSpan`(이름 · 시작/끝 프레임, 끝 포함).
  문서의 `emojis`에 실린다 — 이모지가 없는 문서는 글자가 전과 같다.
- **타임라인 = 이모지가 소유한 모션** — 이름은 `emoji:{이모지}`(모션 이름 규칙 밖의 글자라 겹치지 않는다). `MotionDocument.Find`가 이 이름도 찾으므로 키 명령(`AddChannel` · `SetKeys` · `KeyPose` …)과 그래프 · 도프 시트가 그대로 쓴다.
  `Motions` 목록에는 없어서 `motions.json` · 모션 검증 · 클립이 보지 않는다. 모션의 이름 바꾸기 · 지우기 · 나누기는 타임라인을 거절한다(이모지와 함께다). `AllMotions`가 둘을 함께 돈다(그림 채널의 계단 지키기 등).
- **틀은 덤프에서** — `Rig/EmojiRigFrame.cs`. 노드의 역할을 이름이 아니라 덤프의 모양(`repeatsPerRig`)에서 읽는다: 되풀이되는 줄기의 맨 위 = 기본 자리, 맨 아래 = 그림(키를 받는다), 그 부모 = 오프셋.
  **조립한 리그**(`Assemble`)는 되풀이되는 노드에 리그 이름을 머리로 단다 — `body/EmojiRig_Place › body/EmojiRig_Image`. 오프셋 · 기본 자리 · 배율 · 회전을 얹은 채 코어 `RigSchemaLoader.BuildTree`로 트리가 선다.
  타임라인의 채널은 `Emoji_Offset`(이모지 전체)과 `{리그}/EmojiRig_Image`만 가리킨다(`TryTarget`).
- **편집 명령**(`MotionEditor.Emojis.cs`, 조작 하나 = 한 칸) — 이모지 더하기 · 지우기 · 이름 바꾸기(타임라인이 따라온다) · 복제(키까지 — 재사용은 가져와서 고치는 것) · 오프셋,
  리그 더하기 · 지우기(그 리그의 채널도) · 이름 바꾸기(채널의 노드가 따라온다) · 순서 · 기본 자리 · 그림들, 구간 더하기 · 고치기 · 이름 바꾸기(잇기가 따라온다) · 지우기(잇기를 비운다), 잇기(셋을 한 칸).
  구간 이름 `main`은 타임라인 전체의 클립 이름이라 쓰지 않는다. 바뀜의 종류 `MotionChangeKind.Emoji`를 더했다.
- **검증**(`Validation/EmojiValidator.cs`) — 이름 · 리그(없음 · 이름 · 그림 없음 · 배율 0) · 채널(키 받는 노드인가 · 있는 리그인가 · 그림은 리그에만 · 회전은 z 하나 · 성분 · 키가 타임라인 안인가 · 그림 키가 그 리그의 그림을 가리키는가) ·
  구간(이름 · 타임라인 안) · 잇기(있는 구간 · 등장/퇴장은 머묾이 있을 때만). 실린 덤프에 이모지 틀이 없으면 문서 수준의 문제 하나.
- **읽을 때** — 타임라인의 이름 · 리그는 이모지가 정한다(손으로 쓴 문서가 비워 둬도 된다). 그림 채널의 키는 읽을 때도 계단으로 바로잡는다.
- **확인** — 전체 테스트 779개 통과(코어 424 · 도메인 270 · 화면 85). `EmojiModelTests` 10개.

### E4에서 한 것 (2026-11-11)

- **`Validation/EmojiExport.cs`** — 검증을 지나야 나간다. 머묾을 이었으면 구간마다 클립(+ `in` · `hold` · `out`), 아니면 타임라인 전체가 `main`이고 구간들도 클립으로 함께 나간다(런타임은 잇지 않은 클립을 쓰지 않는다).
- **자르기** — 구간 안의 키는 시각만 당기고, 양 끝에 키가 없으면 그 프레임의 값과 기울기로 키를 세운다. 기울기는 코어 Hermite 식을 미분한 값(수치 미분의 오차를 싣지 않는다). 계단 안에서 시작하면 세운 키도 계단이다.
  그림 채널은 시작 앞의 마지막 키를 프레임 0에 다시 세운다(첫 키 앞이면 기본 그림이라 세우지 않는다).
- **단위** — 키 시각 × 24, 기울기 ÷ 24(`emojis/2`는 프레임당). 값은 소수 넷째 자리까지. 기본값(기울기 0 · 배율 1 · 회전 0)은 적지 않는다.
- **같은 규칙** — 낸 글자를 코어 `EmojiDefinitionLoader.Build`(+ `EmojiRules`)로 다시 읽어, 거르는 이모지가 있으면 내지 않는다. `EmojiExport.ToDefinitions`가 검증 없이 정의를 낸다(미리보기 · 테스트용).
- **`mt emojis <문서> <emojis.json> [--rig]`** — 같은 바이트. `info`에 이모지(리그 · 프레임 수 · 클립 · 머무는가), `validate`에 `kind: "emoji"`.
- **확인** — 전체 테스트 786개 통과(코어 424 · 도메인 277 · 화면 85). `EmojiModelTests` 10 · `EmojiExportTests` 7 — 자른 클립을 코어로 읽어 프레임마다 타임라인과 0.001 안에서 같은지 잰다.

### E5에서 한 것 (2026-11-11)

- **`Preview/EmojiPreview.cs`** — 미리보기는 내보낸 정의를 코어로 돌린다. `Definition`은 이모지 하나를 `EmojiExport`의 글자 → 코어 로더로 읽은 것(런타임이 거르면 null과 까닭),
  `Timeline`은 잇기와 상관없이 타임라인 전체를 같은 길로 읽은 클립(편집 중에 보이는 것).
- **그 프레임의 모습**(`Pose`) — 조립한 리그의 트리 위에 코어 `EmojiClipMath.SampleNode`의 값을 런타임 `EmojiPlaybackSystem.Apply`와 같은 법으로 옮긴다:
  이모지 전체의 자리는 오프셋에 더하고, 리그의 키는 그림 노드가 받고, 기본 자리는 건드리지 않는다. 불투명도 · 보이는 그림(스프라이트 키와 그 그림 파일)은 따로 낸다(코어 트리에는 불투명도가 없다).
- **수명**(`Moment` · `OffAt` · `LifeSeconds`) — 코어 `EmojiPlayback`을 처음부터 다시 돌린다: 등장 → 머묾 ⟲ → (내리면) 퇴장, 머묾이 없으면 main 한 번.
- **`mt pose <문서> --emoji <이름> --time <초> [--off <초>] [--view life|timeline]`** — 단계 · 클립 · 프레임과 오프셋 · 리그마다의 값 · 불투명도 · 그림.
- **대조** — 툴이 낸 파일을 코어 로더로 읽어, 모든 클립의 모든 프레임에서 코어의 값이 그래프의 곡선과 0.001 안에서 같다(자리 · 배율 · 회전 · 불투명도 · 그림, 계단 · 스플라인 · plateau 포함).
  타임라인 보기도 같다.
- **자르기를 하나 고쳤다** — 구간의 끝이 마지막 키 뒤(또는 시작이 첫 키 앞)라 키를 세울 때, 그쪽을 향한 진짜 키의 기울기를 0으로 둔다. 툴의 곡선은 끝값으로 평평한데,
  남겨 두면 세운 키까지 Hermite가 휘었다(대조가 잡았다).
- **확인** — 전체 테스트 791개 통과(코어 424 · 도메인 282 · 화면 85). `EmojiPreviewTests` 5개.

### E6에서 한 것 (2026-11-11)

- **왼쪽 이모지 목록** — 추가(리그 `body`와 함께 한 칸) · 삭제 · 복제 · `emojis.json…` 내보내기. 고르면 그래프 · 도프 시트 · 뷰포트가 그 타임라인을 묶고 모션 · 클립 선택이 풀린다(시간의 주인은 하나). 모션이나 클립을 고르면 이모지가 풀린다.
- **오른쪽 이모지 탭**(탭이 다섯) — 이름 · 오프셋 · 프레임 길이 / 리그(더하기 · 이름 · 순서 · 그림 목록과 "그림 더하기…" · 기본 자리 · 배율 · 회전) / **지금 키**(대상 · 종류) /
  **그림 넘기기**(`LayEmojiImageFlips` — 시작 · 간격 · 횟수로 계단 키를 한 번에) / 구간 · 잇기 / 보기(수명 재생 · 머묾 바퀴 · 이모지에 맞춤). 칸 하나 = 되돌리기 한 칸.
- **뷰포트**(`ViewportView.Emoji.cs`) — 이모지 트리를 고른 캐릭터의 `CharSlot_Scale` 밑에 세워(키 머리 `emoji/`) 미리보기(`EmojiPreview`)의 값으로 그린다: 로케이터 십자 · 리그마다 그림(없으면 자리표시) · 불투명도.
  그림을 끌면 기본 자리(`SetEmojiRigPlace`, 키가 아니다). **수명 재생**이면 시계가 수명(`EmojiPreview.Moment`)을 돈다.
  로케이터가 발치라 그대로는 무대 아래 작게 보인다 — 이모지를 고르면 그림에 맞추고(`FrameEmoji`), **F**와 "이모지에 맞춤"이 다시 맞춘다. 이모지를 놓으면 무대 맞춤으로 돌아온다.
- **타임 슬라이더** — 구간이 이름 붙은 띠(`SliderMark`). 수명 재생이면 등장 · 머묾 × 바퀴 · 퇴장의 띠.
- **그래프** — 툴바 **step**(계단). "키 없음" 줄은 이모지의 타임라인이면 키를 받는 노드(이모지 전체 · 리그마다 그림 노드)와 이모지의 다섯 종류만 낸다(그림 바꾸기는 그림이 있는 리그에만) — 리그 덤프의 노드를 그대로 늘어놓으면 키를 받지 않는 기본 자리까지 나왔다.
- **문제 칸 · 세션** — 이모지의 문제(`EmojiValidator`)가 문제 칸에, `DocumentSession.ExportEmojis`가 막히면 상태 줄에. 문서 지문에 그림 목록이 들어간다.
- **장면** — `EmojiShots`(`MT_RENDER_DIR`이면 타임라인 보기 · 수명 보기를 그림으로).
- **확인** — 전체 테스트 800개 통과(코어 424 · 도메인 282 · 화면 94). `EmojiShellTests` 8개 · `EmojiShots` 1개.

### E7에서 한 것 (2026-11-11)

- **예시** — `examples/emoji`: 런타임 `007d4c16`의 `Assets/Dialogue/emojis.json`(견본 셋)을 편집 명령으로 다시 지은 문서(`emoji-examples.motion-authoring.json`), 그것을 내보낸 `emojis.json`, 그림(`art/star.png` · `art/question.png`), `README.md`.
  빌더는 `tests/Mt.Authoring.Tests/EmojiExamples.cs` — 런타임의 클립을 한 타임라인에 이어 놓고(`star`는 `in` 0~4 · `hold` 5~20 · `out` 21~26, 경계의 양쪽 끝 키를 다 둔다), 머묾이 없는 둘은 타임라인 전체가 `main`이다. 키는 flat(런타임 파일이 기울기를 적지 않았다).
- **대조**(`EmojiExampleTests`) — 런타임의 파일을 픽스처(`Fixtures/runtime-emojis.json`)로 두고, 둘을 코어 로더로 읽어 오프셋 · 리그(이름 · 그림 · 기본 자리 · 배율 · 회전) · 클립(이름 · 길이) · 잇기가 같고,
  모든 클립의 ¼ 프레임마다 이모지 전체와 리그마다의 값(자리 · 배율 · 회전 · 불투명도 · 그림)이 0.0001 안에서 같은지 잰다. 글자는 다르다 — 툴은 구간 바깥의 값도 상수 트랙으로 적는다(재생되는 값은 같다).
  예시 파일은 빌더가 낸 바이트와 같아야 한다(`MT_WRITE_EXAMPLES=1`이면 다시 쓴다).
- **장면** — `EmojiShots`가 예시 문서를 열어 `twinkle` · `star`(타임라인 · 수명)를 그린다.
- **확인** — 전체 테스트 803개 통과(코어 424 · 도메인 285 · 화면 94).

### E8 — 좌우 (지시, 2026-11-12)

런타임이 좌우를 정하고 썼다(`ked-presentation-runtime` `feat/emojiRigSchema` @ `32858197`). 툴은 그것을 가져간다 — **형식과 계산은 런타임의 것이고 툴에서 새로 짓지 않는다.**
규범: 런타임 `docs/emoji-format.md` §6(두 칸의 뜻) · `docs/emoji-rig.md` §3.1 · `docs/PLAN_emoji-placement.md` §3(정한 것과 까닭 · "툴에 넘길 것").

**정해진 것 (소유자)**

- 이모지는 **오른쪽을 보는(뒤집히지 않은) 인물**을 기준으로 만든다. 인물이 뒤집혀 있으면 런타임이 `Emoji_Locator`를 뒤집어(가로 배율 −1, 자리 x도) 이모지를 통째로 거울에 비춘다 — 이모지의 값과 키는 그대로다.
- 예외 둘, 둘 다 참/거짓(없으면 거짓): 이모지의 **`fixedSide`**(방향을 따르지 않는다) · 리그의 **`upright`**(뒤집히면 안 되는 그림 — 그림과 그 리그의 키는 만든 그대로, 기본 자리만 거울 쪽).
- 이름(`fixedSide` · `upright`)은 계약이다 — 바꾸지 않는다.

**할 일** — 한 번에 하나, 단계마다 전체 빌드와 전체 테스트가 통과한 채로.

| | 작업 | 끝나면 |
|---|---|---|
| E8-1 | **들이기** — 런타임 `32858197`의 `Core/Emoji/EmojiDefinition.cs`(`EmojiRigEntry.Upright` · `EmojiDefinition.FixedSide` · `EmojiSide`) · `Core/Tuning/EmojisFileDto.cs`(`fixedSide` · `upright`)를 **통째로** 다시 들인다. `vendoring.md`의 "부분 반입"을 고친다(원본 커밋). 순수 테스트 둘을 가져온다 — 런타임 `EmojiPlaybackTests`의 `형식의_좌우_표시를_읽는다_없으면_거짓이다` · `좌우의_부호_누가_한_번_더_뒤집는가` | 사본이 런타임과 같다. 툴의 코드는 아직 두 칸을 쓰지 않는다 — **끝** |
| E8-2 | **모델과 내보내기** — `EmojiAsset.FixedSide` · `EmojiRigAsset.Upright`, 편집 명령 둘(조작 하나 = 되돌리기 한 칸, 같은 값은 기록하지 않는다), 저작본 읽고 쓰기, 복제가 함께 가져간다, 문서 지문. `EmojiPreview.Definition`과 `EmojiExport`가 두 칸을 싣는다 — **거짓이면 파일에 적지 않는다**(지금 파일과 글자가 같게) | 문서에 두 칸을 담고 `emojis.json`에 나간다(화면 없이) — **끝** |
| E8-3 | **미리보기** — "왼쪽을 보는 인물로 보기". 보기의 상태다(문서에 적지 않고 되돌리기에 들지 않는다). 켜면 미리보기의 로케이터를 뒤집고(가로 배율 −1 · 자리 x), 예외의 부호를 코어 **`EmojiSide`**로 곱한다 — `OffsetSign`은 `Emoji_Offset`의 자리 x · 가로 배율 · 회전에, `PlaceSign`은 `EmojiRig_Place`의 가로 배율 · 회전에. 런타임 `Stage/Emoji/EmojiPlaybackSystem.cs`의 `Apply`와 `EmojiLocatorSystem.cs`가 본이다 — **부호를 툴에서 따로 계산하지 않는다**. 켠 동안 뷰포트에서 그림을 끌어 기본 자리를 옮기는 것은 막는다(거울 속에서 끄는 값이 헷갈린다 — 끄고 옮긴다) | 툴에서 보는 뒤집힌 모습 = 게임 — **끝** |
| E8-4 | **화면** — 이모지 탭에 칸 둘(이모지: 방향을 따르지 않는다 / 리그: 뒤집히지 않는 그림)과 보기 절의 "왼쪽을 보는 인물로". 테스트 손잡이(`internal … FromForm()`)와 헤드리스 테스트. `fixedSide`인 이모지에서는 리그의 `upright` 칸이 뜻이 없다 — 흐리게 하거나 한 줄로 알린다(막지는 않는다). F1 표 · README · `docs/USER-GUIDE.md` | 툴에서 두 칸을 고친다 |
| E8-5 | **예시와 대조** — 빌더(`tests/Mt.Authoring.Tests/EmojiExamples.cs`)에서 `question`의 리그 `mark`에 `upright`. 픽스처 `Fixtures/runtime-emojis.json`을 런타임 `32858197`의 `Assets/Dialogue/emojis.json`으로 다시 가져간다. `EmojiExampleTests`의 대조가 **두 칸도** 본다(이모지의 `FixedSide` · 리그의 `Upright`). 예시를 다시 쓴다(`MT_WRITE_EXAMPLES=1`). `EmojiShots`에 뒤집힌 보기 한 장 | 툴이 낸 파일 = 런타임의 견본(값으로) |

**알아 둘 것**

- 런타임의 `Assets/Dialogue/emojis.json`은 툴이 낸 파일에 `"upright": true` 한 줄을 **손으로** 더한 것이다(`mark` 리그의 `scale` 다음). 툴이 내는 파일과 칸의 순서가 달라도 된다 — 대조는 값으로 한다. E8이 끝나면 소유자가 툴이 낸 `examples/emoji/emojis.json`을 런타임에 다시 놓는다.
- 툴의 미리보기에서 로케이터의 자리는 기본값이다(캐릭터 + 포즈별 표는 런타임의 것이고 툴은 읽지 않는다 — §7). 뒤집힌 보기에서도 그 기본값의 x를 뒤집으면 된다.
- `upright` 리그가 바깥으로 흐르는 x 키를 가졌다면 뒤집힌 보기에서는 안쪽으로 흐른다 — 버그가 아니다(키가 만든 그대로 보인다). 그런 움직임은 이모지 전체의 키로 잡는 것이 규칙이다. 사용 안내에 한 줄 적는다.

**하지 않는 것**

- 코어 사본을 고치는 것 — 모자란 것이 보이면 고치지 말고 보고한다(런타임에서 먼저 정한다).
- 캐릭터 + 포즈별 자리 표(`presets/emoji-locator.json`)를 읽는 것 · 대본이 쪽을 고르는 칸 · 그림만 다른 이모지 묶음(런타임 `PLAN_emoji-placement.md` 결정 3 — 하지 않기로 닫았다).
- R6(코어 사본을 통째로 다시 들이기) · R4 · R5 — 이 단계의 밖이다.

**끝나면** — 이 절 아래에 "E8에서 한 것"(된 것 · 정한 것 · 아직 불편한 것 · 테스트 수)을 적고 상태 줄을 고친다. 커밋은 소유자.

## 6. 정해진 것 (소유자, 2026-11-11)

1. **타임라인을 모션으로 싣는다**(§2) — 편집 기반을 그대로 쓴다.
2. **채널 종류를 툴 쪽에 따로 둔다**(§3) — 코어 사본을 고치지 않고 불투명도 · 그림을 얻는다. 채널 종류를 쓰는 곳(120여 곳)을 한 번에 고치는 단계가 E2다.
3. **스킨을 통째로 걷는다**(E1) — 스킨 자산 · 스킨 탭 · 스킨 편집 · 스킨 테스트. 스킨이 하던 일 넷은 이렇게 간다:
   - 조각마다 그림 → 리그가 그림을 직접 가진다.
   - 놓임(자리 · 크기 · 회전) → 리그의 기본 자리 · 배율 · 회전(런타임에 넣었다).
   - 그림 여러 장 넘기기 → 그림 바꾸기 채널의 계단 키. "몇 프레임마다 다음 장"을 한 번에 깔아 주는 편의 기능을 E6에 둔다.
   - 같은 움직임에 다른 그림 묶음(반전 판 포함) → **두지 않는다**(소유자, 2026-11-12). 반전 판은 좌우(E8)가 푼다 — 인물이 뒤집히면 런타임이 이모지를 거울에 비춘다. 그림만 다른 이모지는 실례가 없어 길을 내지 않는다(나오면 이모지를 따로 만든다 — 복제).
4. **E1과 E6 사이에는 툴이 이모지를 못 다뤘다** — 캐릭터 모션 쪽은 그대로 돌았다. E6에서 다시 선다.
5. 옛 이모지 문서를 옮겨 읽지 않는다. 불투명도 · 그림 채널은 이모지의 타임라인에만 쓴다.

## 7. 하지 않는 것

- 리그의 틀을 툴에서 바꾸는 것(노드를 더하거나 채널을 늘리는 것) — 런타임의 일이다.
- 캐릭터 + 바리에이션별 자리(RootSpace) — 런타임의 일이다. 툴은 기본값으로 세워 본다.
- 옛 이모지 문서를 옮겨 읽는 것 — 예시는 새로 짓는다.

## 8. 이어받는 세션에게

**빌드** — 이 툴은 NuGet 패키지(Avalonia · xunit)가 있어야 빌드된다. NuGet에 닿지 않는 작업 공간에서는 소유자 컴퓨터의 캐시(`C:\Users\river\.nuget\packages`)에서 필요한 패키지만 묶어 가져와 오프라인으로 복원한다:

1. `src/Mt.App/obj/project.assets.json`의 패키지 목록 + 테스트용(`avalonia.headless` · `xunit*` · `nunit` 3.14.0 · `nunit3testadapter` 4.6.0 · `microsoft.net.test.sdk` 17.12.0 · `microsoft.codecoverage` · `microsoft.testplatform.*` · `coverlet.collector` 6.0.4 · `newtonsoft.json` · `system.reflection.metadata` · `nuget.frameworks` · `netstandard.library` · `microsoft.netcore.platforms`)을 `*.nupkg`와 리눅스가 아닌 네이티브(`runtimes/`)를 빼고 묶는다(64MB쯤).
2. 풀어 놓은 폴더를 `NUGET_PACKAGES`로 가리키고 `dotnet restore KedMotionTool.sln -s <빈 폴더> -p:NuGetAudit=false`.
3. 캐시에 `netstandard2.1` 참조 팩이 없다 — **작업 공간의 사본에서만** `src/Ked.Presentation.Core/Ked.Presentation.Core.csproj`의 대상 프레임워크를 `net10.0`으로 바꿔 빌드한다(저장소에는 옮기지 않는다).
4. 그러면 `dotnet build` · `dotnet test`가 전부 돈다 — `f842ace`에서 779개(코어 414 · 도메인 271 · 화면 94, 헤드리스 화면 포함), E1 뒤 761개(코어 424 · 도메인 252 · 화면 85) 통과를 확인했다(2026-11-11).
5. 묶음은 이미 캐시 폴더에 있다 — `C:\Users\river\.nuget\packages\_claude_bundle.tar.gz`(68MB). E1 세션은 이것을 그대로 가져와 풀었다.
6. **작업 공간에 .NET SDK가 없고 dot.net · nuget.org에 닿지 않으면** — conda-forge(`conda.anaconda.org`)에는 닿았다: `linux-64/dotnet-sdk-10.0.401-h4e4d5bb_0.conda` · `dotnet-runtime-10.0.12-h4e4d5bb_0.conda`를 받아(`.conda`는 zip 안의 `pkg-*.tar.zst`) 풀면 `lib/dotnet/dotnet`이 선다. `DOTNET_ROOT`를 그곳으로, 감사 경고(NU1900)는 `NuGetAudit=false`를 환경 변수로도 줘야 빌드에서 다시 안 뜬다.

**런타임에서 들일 것**(E1) — `ked-presentation-runtime` `feat/emojiRigSchema`의 `Assets/Ked.Presentation/Core/Emoji/`(`EmojiClip.cs` · `EmojiDefinition.cs` · `EmojiPlayback.cs`) · `Core/Tuning/EmojisFileDto.cs` · `RigSchemaDto.cs`의 `repeatsPerRig`, 그리고 `ExportedTuning/rig-schemas.json`. 순수 테스트는 런타임 `Tests/EditMode/EmojiPlaybackTests.cs`에서 유니티를 쓰지 않는 절(형식의 규칙 · 그 프레임의 모습 · 재생의 단계)만. 사본의 `hasImage`는 런타임에 없다(버린 설계의 것) — 스킨과 함께 걷는다. `vendoring.md`에 적는다.

**이름이 부딪친다** — 코어의 `Ked.Presentation.Core.EmojiPhase`와 툴의 `Mt.Authoring.Preview.EmojiPhase`. 그래서 "들이기"와 "옛 이모지 걷기"를 한 단계(E1)로 묶었다.

**E1에서 걷을 것** — `Model/EmojiAsset.cs` · `Model/SkinAsset.cs` · `Editing/MotionEditor.Emojis.cs` · `MotionEditor.Skins.cs` · `Preview/EmojiPreview.cs` · `Validation/EmojiValidator.cs` · `Rig/RigParts.cs` · `Mt.App/MainWindow.Emojis.cs` · `MainWindow.Skins.cs` · `MainWindow.Attach.cs`(붙이기) · 뷰포트의 이모지 그리기 · `mt`의 이모지 명령 · 문서(`MotionDocument`)의 `Skins` · `Emojis` · 스키마 계약/지문의 이모지 대목 · 테스트(`EmojiTests` · `SkinTests` · `AttachTests` · `EmojiExampleTests` · `EmojiExamples` · `SkinShellTests` · `EmojiExampleShots`) · `examples/emoji`. 컴파일러가 가리키는 대로 따라가며 걷는다.
