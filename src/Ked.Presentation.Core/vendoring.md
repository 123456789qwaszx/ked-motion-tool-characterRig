# Ked.Presentation.Core — 복사 반입본 (정본은 ked-stage-runtime)

`ked-stage-runtime` 저장소 `Assets/Ked.Presentation/Core/`의 복사본이다.
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: 무대 런타임 → 이 사본.**
패키지화는 하지 않는다(소유자 결정). 아래 절차가 반복 가능한 대체물이다.

| 항목 | 값 |
|---|---|
| 원본 | `ked-stage-runtime` `dev` @ `dc690c1` (배경 리그 제거 — `bg_*`는 캐스팅하지 않은 슬롯, 재선언은 새 리그. 그 전: `640ac60` motions/2) |
| 반입일 | 2026-10-29 |
| 범위 | `Ease/`·`Emoji/`·`Motion/`·`Primitives/`·`Reduce/`·`State/`·`Tokens/`·`Transforms/`·`Tuning/` 소스와 `Documentation~/` |
| 제외 | `Tests/`·`*.meta`·`*.asmdef` |
| 테스트 | 순수 테스트(유니티·런타임·DOTween 참조 없는 것)만 `tests/Ked.Presentation.Core.Tests/EditMode/`로 |
| 이쪽 살림 | `Ked.Presentation.Core.csproj`·`vendoring.md` (동기화 때 유지) |

## 부분 반입 — 이모지 (2026-11-11, `docs/history/PLAN-emoji.md` E1)

정본이 `ked-presentation-runtime`으로 옮겨 가는 중이라 전체 동기화 대신 **이모지에 필요한 파일만** 그쪽에서 가져왔다. 나머지는 위 표의 원본 그대로다 —
그래서 지금 `ked-presentation-runtime`과의 `diff -r`는 0줄이 아니다(전체 동기화는 따로 한다).

| 항목 | 값 |
|---|---|
| 원본 | `ked-presentation-runtime` `feat/emojiRigSchema` @ `32858197` (좌우 — `fixedSide` · `upright` · `EmojiSide`까지). 처음 반입은 `007d4c16`(E1)이고, `32858197`에서 바뀐 `EmojiDefinition.cs` · `EmojisFileDto.cs`를 통째로 다시 들였다(E8-1). 아래 파일은 모두 `32858197`의 것과 같다 |
| 파일 | `Emoji/EmojiClip.cs` · `Emoji/EmojiDefinition.cs` · `Emoji/EmojiPlayback.cs` · `Tuning/EmojisFileDto.cs` · `Tuning/RigSchemaDto.cs`(통째 교체 — `repeatsPerRig`가 들어오고 `hasImage`가 빠졌다) |
| 테스트 | 런타임 `Tests/EditMode/EmojiPlaybackTests.cs`의 유니티를 쓰지 않는 절(형식의 규칙 · 그 프레임의 모습 · 재생의 단계 · 좌우의 둘 — 형식의 표시와 `EmojiSide`의 부호)만 `EditMode/EmojiPlaybackTests.cs`로. 접기(`StageReducer.Emoji` — 사본에 없다)와 재생 시스템 · 뒤집힌 로케이터 밑의 노드는 저쪽에 남는다 |
| 덤프 | `Mt.Authoring/Defaults/ExportedTuning/rig-schemas.json` · `export-report.json`과 시험 픽스처 `tests/Mt.Authoring.Tests/Fixtures/rig-schemas.json`은 `007d4c16`의 `ExportedTuning/`에서(`rig-schemas.json`은 `32858197`에서도 같다 — 로케이터의 자리 표 `presets/emoji-locator.json`은 툴이 읽지 않는다) |
| 견본 | 시험 픽스처 `tests/Mt.Authoring.Tests/Fixtures/runtime-emojis.json`은 처음에 `32858197`의 `Assets/Dialogue/emojis.json`(이모지 견본 셋 — `question`의 `mark`가 `upright`) 그대로였다. **2026-11-18부터는 이 툴이 30fps로 옮겨 지은 견본**(아래 절)이다 — 런타임이 새 견본으로 받는다. 툴의 예시(`examples/emoji`)가 이것과 프레임마다 같은지 잰다 |

선반영은 이제 없다. `RigSchemaNodeDto.hasImage`(옛 이모지 설계의 그림 노드 표시)는 런타임 `465f5d43`에 들어갔다가 새 정의에서 `repeatsPerRig`(리그마다 되풀이되는 노드)로 바뀌었고, 이 반입으로 사본에서도 사라졌다.

**걷어 낸 것(2026-11-11, `docs/PLAN-characterRig.md` R3).** 모션의 불투명도 채널(`MotionChannelKind.Alpha`)과 이벤트 키(`MotionEvent` · `MotionClip.Events`)는
런타임이 받지 않기로 했다(런타임 `docs/PLAN_emoji.md` §8-4 · 이모지는 대본의 커맨드로 띄운다). `Motion/MotionClip.cs` · `MotionMath.cs` · `MotionRules.cs` ·
`Reduce/StageReducer.Staging.cs`를 반입본(`ea59698`)으로 되돌렸다 — 산출물은 다시 `motions/2` 하나다.

## 부분 반입 — 30fps · 이모지의 쪽 (2026-11-18)

런타임이 프레임을 30fps로 세기로 했다(런타임 `docs/PLAN_frames-30fps.md`). 이번에도 전체 동기화 대신 **그 일에 닿는 파일만** 들였다.

| 항목 | 값 |
|---|---|
| 원본 | `ked-presentation-runtime` `fix/progressionScene` @ `0faca847`(30fps). 이 커밋의 Presentation Core는 `dev`(`ed950b09`)에 30fps만 더한 것이다 — 그 가지의 다른 커밋은 진행 · 문서뿐이다. 이모지의 쪽(`flip`)은 `dev`의 `ef0f5865`(11-17)에 먼저 들어간 것 |
| 파일 | `Tokens/DurationToken.cs`(`FramesPerSecond` 30) · `Emoji/EmojiDefinition.cs`(`DefaultFps` 30, `EmojiSide`의 세 함수가 `flip`을 받는다 — 기본값이 있어 소비자는 그대로 컴파일된다, `EmojiSideToken`) · `Tuning/EmojisFileDto.cs`(주석) — 통째로. 세 파일은 `0faca847`의 것과 같다 |
| 테스트 | `DurationTokenTests.cs`(통째 — 이름공간만 이쪽 것), `EmojiPlaybackTests.cs`의 유니티를 쓰지 않는 절(파일 fps 30 · fps가 없으면 30이고 대본의 프레임과 같다), `EmojiSideTests.cs`(새로 — 순수 산수, `flip`의 진리표). `flip`을 노드에 옮기는 절은 저쪽(Unity)에 남는다 |
| 소비자 | 툴의 24(시간 스냅 · 눈금 · 화면 글자)를 30으로, 저작본 판을 `motion-authoring/3`으로 올렸다 — 이모지의 프레임(구간 · 타임라인)이 30fps라 /2(24fps)는 읽지 않는다. 옛 문서는 `tools/authoring-24to30.py`로 한 번 옮긴다 |

그 밖의 코어는 여전히 위 표의 원본(옛 판)이다 — 전체 재반입은 남은 빚(11-16~17의 줄의 자리 · 무대 스텝 · `<<Nfr>>` 별칭 걷기 …).

## 동기화 절차

1. **감사** — `diff -r`로 세 갈래를 가른다: ⓐ 저쪽에만 있는 파일(가져온다) ⓑ 내용이 다른
   파일(저쪽 본으로 교체) ⓒ 이쪽에만 있는 코드 멤버 — 그건 과거 로컬 수정이다. 목록으로
   남기고 저쪽 반영을 소유자에게 물은 뒤 처리한다.
2. **이식** — 저쪽 트리에서 `Tests/`·`*.meta`·`*.asmdef` 제외 전부 복사. 테스트는
   `Tests/EditMode/*.cs` 중 순수한 것만 `tests/Ked.Presentation.Core.Tests/EditMode/`로.
   기준: `UnityEngine`·`UnityEditor`·`DG.`·`Ked.Presentation.Runtime/Editor`·
   `Ked.CommandExecutor`·`Newtonsoft`·`LogAssert`를 참조하면 제외.
3. **보수** — 소비자(`Mt.Authoring`·`Mt.App`)의 컴파일·의미 적응은 **소비자 쪽에서** 고친다.
   이 사본의 파일을 고쳐 맞추지 않는다 — 사본을 고치는 순간 다음 동기화가 그 수정을
   조용히 지운다.
4. **고정** — 전체 빌드 + 전체 테스트. 이 표의 원본 커밋·반입일을 갱신한다.

## 검증

```
diff -r --exclude=Tests --exclude='*.meta' --exclude='*.asmdef' \
        --exclude=Ked.Presentation.Core.csproj --exclude=vendoring.md \
        <ked-stage-runtime>/Assets/Ked.Presentation/Core src/Ked.Presentation.Core
```

차이 0줄이 동기화 완료다.

## 유니티 호환 안전핀

csproj가 원본 asmdef의 `noEngineReferences: true`와 유니티 C# 수준(LangVersion 9,
netstandard2.1)을 컴파일러 제약으로 유지한다.
