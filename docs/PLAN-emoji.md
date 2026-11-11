# 이모지 — 새 정의에 맞춘다 (계획, 2026-11-11)

이 문서는 툴의 이모지 쪽을 런타임의 새 정의에 맞추는 계획이다. 런타임의 과제 3(`ked-presentation-runtime` `docs/PLAN_emoji.md` §5)의 툴 쪽 절반이다.
정의는 런타임의 `docs/emoji-rig.md`, 나가는 형식은 `docs/emoji-format.md`가 규범이다 — 이 문서는 그것을 다시 적지 않고 **툴이 무엇을 바꾸는가**만 적는다.

| | |
|---|---|
| 런타임 | `feat/emojiRigSchema` @ `007d4c16` — 틀(`EmojiRigSchema`) · 대본(`emoji` · `emoji_off`) · 형식(`emojis/2`) · 재생(`EmojiPlaybackSystem`)이 서 있다. `007d4c16`에서 리그의 기본 배율 · 회전이 들어왔다(`65f001f9` 다음) |
| 툴의 지금 | `dev` @ `f842ace`(R3-2) 위에 **E1을 마쳤다**(2026-11-11, 커밋은 소유자) — 런타임의 이모지 코어와 새 덤프가 들어왔고, 버린 설계(공용 `emoji` 리그 · 스킨 · 공용 모션 셋 · 머무는 시간 · 붙는 리그)는 없다. 툴은 지금 이모지를 다루지 않는다 |
| 상태 | **계획이 굳었다**(소유자, 2026-11-11 — §6). **E1 끝**(§5). 다음은 E2 — 이어받는 길은 §8 |

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
| E2 | **채널** — 툴 쪽 채널 종류(불투명도 · 그림 · 계단 키) | 이모지의 타임라인이 쓸 채널이 선다 |
| E3 | **모델** — 새 `EmojiAsset`(오프셋 · 리그들 · 타임라인 · 구간 · 잇기), 편집 명령, 검증, 저작본 읽고 쓰기. 이모지마다 조립한 리그 | 문서에 새 이모지를 담는다(화면 없이) |
| E4 | **내보내기** — `emojis.json`(`emojis/2`): 구간 자르기 · 초 → 프레임 · 코어의 규칙(`EmojiRules`)으로 검증. `mt` 명령 | 툴이 런타임이 읽는 파일을 낸다 |
| E5 | **미리보기와 대조** — 미리보기는 내보낸 정의를 코어(`EmojiPlayback` · `EmojiClipMath`)로 돌린다. 툴이 낸 파일을 코어 로더로 읽어 프레임마다 그래프의 값과 맞는지 잰다 | 툴 = 게임 |
| E6 | **화면** — 이모지 탭: 리그 목록(더하기 · 이름 · 순서 · 그림 · 기본 자리 끌기), 타임라인(그래프 · 도프 시트 · 타임 슬라이더), 구간 표지와 잇기, 재생(등장 → 머묾 되풀이 → 퇴장) | 툴에서 이모지를 만든다 |
| E7 | 예시(런타임의 견본 셋 — `star` · `question` · `twinkle`)를 툴 문서로 다시 짓는다. README · 사용 안내 | |

### E1에서 한 것 (2026-11-11)

- **들였다** — 런타임 `007d4c16`(`65f001f9` 다음 커밋 — 리그의 기본 배율 · 회전이 들어온 것)에서 `Emoji/EmojiClip.cs` · `EmojiDefinition.cs` · `EmojiPlayback.cs` · `Tuning/EmojisFileDto.cs`, 그리고 `Tuning/RigSchemaDto.cs`를 통째로(`repeatsPerRig`가 들어오고 `hasImage`가 빠졌다).
  내장 덤프(`rig-schemas.json` · `export-report.json`)와 시험 픽스처도 같은 커밋의 것 — `emoji` 틀은 `__root › Emoji_Offset › EmojiRig_Place › EmojiRig_Image`(뒤 둘이 `repeatsPerRig`). `BuiltinTuning.Source`를 고쳤다.
  순수 테스트 `EmojiPlaybackTests`(형식의 규칙 · 그 프레임의 모습 · 재생의 단계 — 10개). `vendoring.md`에 "부분 반입"으로 적었다 — 나머지 사본은 아직 `ked-stage-runtime`의 것이라 런타임과의 `diff -r`는 0줄이 아니다.
- **걷었다** — §8의 목록 그대로(모델 · 편집 · 미리보기 · 검증 · `RigParts` · 화면 셋 · 뷰포트의 조각 그리기와 조각 놓기 · 타임 슬라이더의 구간 틱 · `mt emojis`와 `info`의 이모지 · 스킨 · `attachTo` · 문서의 `skins` · `emojis` · 스키마 계약/지문/이름 따라가기의 이모지 · 스킨 대목 · 테스트 · `examples/emoji`).
  붙이기는 모델째 걷었다 — `SceneObject.AttachTo` · `AttachNode` · `Skin` · `SceneAsset.ParentOf` · `AttachedTo` · `MotionEditor.AttachObject`, 미리보기 · 하이어아키 · 씬 탭의 "붙은 것" 갈래. 인스펙터 탭은 넷이 됐다(설정에 남은 탭 번호는 0~3으로 조인다).
  계획서 `PLAN-parts.md`도 지웠다(git 이력에 있다). README · `PLAN.md` · `PLAN-characterRig.md` · `CLAUDE.md`의 해당 대목을 고쳤다.
- **옛 문서** — 저작본의 `skins` · `emojis` · 오브젝트의 `attachTo` · `attachNode` · `skin`은 이제 모르는 필드라 읽을 때 버려지고, 다시 저장하면 사라진다(§6의 5 — 옮겨 읽지 않는다).
- **확인** — 전체 빌드 · 전체 테스트 761개 통과(코어 424 · 도메인 252 · 화면 85). 걷은 테스트만큼 줄고 `EmojiPlaybackTests` 10개가 늘었다.

## 6. 정해진 것 (소유자, 2026-11-11)

1. **타임라인을 모션으로 싣는다**(§2) — 편집 기반을 그대로 쓴다.
2. **채널 종류를 툴 쪽에 따로 둔다**(§3) — 코어 사본을 고치지 않고 불투명도 · 그림을 얻는다. 채널 종류를 쓰는 곳(120여 곳)을 한 번에 고치는 단계가 E2다.
3. **스킨을 통째로 걷는다**(E1) — 스킨 자산 · 스킨 탭 · 스킨 편집 · 스킨 테스트. 스킨이 하던 일 넷은 이렇게 간다:
   - 조각마다 그림 → 리그가 그림을 직접 가진다.
   - 놓임(자리 · 크기 · 회전) → 리그의 기본 자리 · 배율 · 회전(런타임에 넣었다).
   - 그림 여러 장 넘기기 → 그림 바꾸기 채널의 계단 키. "몇 프레임마다 다음 장"을 한 번에 깔아 주는 편의 기능을 E6에 둔다.
   - 같은 움직임에 다른 그림 묶음(반전 판 포함) → **지금은 없다.** 좌우를 누가 정하는가를 정할 때 함께 정한다(형식에 그림 묶음을 더하는 것은 기존 파일을 깨지 않는다).
4. **E1과 E6 사이에는 툴이 이모지를 못 다룬다** — 캐릭터 모션 쪽은 그대로 돈다.
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
