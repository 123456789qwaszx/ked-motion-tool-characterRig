# Ked.Presentation.Core — 복사 반입본 (정본은 ked-presentation-runtime)

`ked-presentation-runtime` 저장소 `Assets/Ked.Presentation/Core/`의 복사본이다.
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: 런타임 → 이 사본.**
패키지화는 하지 않는다(소유자 결정). 아래 절차가 반복 가능한 대체물이다.

| 항목 | 값 |
|---|---|
| 원본 | `ked-presentation-runtime` `dev` @ `b56a2ebc` — 리그의 계층(포커스 · 슬롯 · 초상, 25노드 → 20노드)까지. **전체 동기화**다 — 아래 검증의 `diff -r`가 0줄 |
| 반입일 | 2026-11-22 (`docs/PLAN-rig-layers.md` S3) |
| 뜬 방법 | 작업 트리가 아니라 **커밋**에서 — `git archive b56a2ebc Assets/Ked.Presentation/Core Assets/Ked.Presentation/Tests/EditMode ExportedTuning`. 그날 런타임의 작업 트리는 다른 가지(`feat/command-spec`)였고 커밋되지 않은 새 파일(`Core/Spec/` …)이 있었다 |
| 범위 | `Ease/`·`Emoji/`·`Motion/`·`Primitives/`·`Reduce/`·`State/`·`Tokens/`·`Transforms/`·`Tuning/` 소스와 `Documentation~/` |
| 제외 | `Tests/`·`*.meta`·`*.asmdef` |
| 테스트 | 순수 테스트 28개 파일을 **그대로**(이름공간도 저쪽 것) `tests/Ked.Presentation.Core.Tests/EditMode/`로. `EmojiPlaybackTests.cs`만 다르다 — 저쪽 파일은 유니티를 쓰므로, 유니티를 쓰지 않는 절(형식의 규칙 · 그 프레임의 모습 · 재생의 단계 · 좌우의 둘 · fps 30)만 떼어 둔 이쪽 판을 지킨다(`0faca847` 뒤로 저쪽에서 바뀐 것은 유니티 절의 노드 이름 한 줄뿐이다) |
| 덤프 | `Mt.Authoring/Defaults/ExportedTuning/`의 `rig-schemas.json` · `export-report.json` · `base-resolution.json` · `presets/depth.json` · `presets/focus-tuning.json`과 시험 픽스처 `tests/Mt.Authoring.Tests/Fixtures/rig-schemas.json`은 같은 커밋의 `ExportedTuning/`에서(익스포트 2026-11-21). 픽스처의 `presets/depth.json` · `focus.json`은 손으로 줄인 것이다(커브의 값은 같다) |
| 견본 | 시험 픽스처 `runtime-emojis.json`(이모지 견본 셋)과 `stage-parity/`(무대 등가 견본)는 **이쪽이 원본**이다 — 런타임이 사본을 받는다(`docs/MAINTENANCE.md` §4.7) |
| 이쪽 살림 | `Ked.Presentation.Core.csproj`·`vendoring.md` (동기화 때 유지) |

선반영은 없다. 앞선 부분 반입(이모지 11-11 · 30fps 11-18)은 이 전체 동기화에 들어 있다.

**소비자가 아직 따라가지 않은 것.** 런타임은 2026-11-09에 초상을 원본 픽셀 크기로 그리게 바꿨다(`NodeTargets.SourcePixelsSizing` — 그림 크기와 배역 앵커의 배율이 든다).
이 툴의 미리보기(`Mt.Authoring/Preview/PortraitLayout.cs`)는 높이 맞춤 그대로다 — 사본에는 그 식이 없어 소비자 쪽에 한 줄로 남아 있다. 따로 정할 일이다(`docs/PLAN-rig-layers.md` §6).

## 동기화 절차

0. **뜨기** — 런타임의 작업 트리가 아니라 정한 커밋에서 뜬다(`git archive <커밋> …`). 작업 트리에는 다른 가지의 일이 섞여 있을 수 있고 줄바꿈도 CRLF다.
1. **감사** — `diff -r`로 세 갈래를 가른다: ⓐ 저쪽에만 있는 파일(가져온다) ⓑ 내용이 다른
   파일(저쪽 본으로 교체) ⓒ 이쪽에만 있는 코드 멤버 — 그건 과거 로컬 수정이다. 목록으로
   남기고 저쪽 반영을 소유자에게 물은 뒤 처리한다.
2. **이식** — 저쪽 트리에서 `Tests/`·`*.meta`·`*.asmdef` 제외 전부 복사. 테스트는
   `Tests/EditMode/*.cs` 중 순수한 것만 `tests/Ked.Presentation.Core.Tests/EditMode/`로.
   기준: `UnityEngine`·`UnityEditor`·`DG.`·`Ked.Presentation.Runtime/Editor/Stage/Playback`·
   `Ked.CommandExecutor`·`Newtonsoft`·`LogAssert`를 참조하면 제외. using 없이 런타임의 형식을 쓰는
   파일도 있다(2026-11-22: `CastRegistrySlotOnlyTests` · `CharacterRigRegistryOutsideFoldTests`) — 빌드가 알려 준다.
3. **보수** — 소비자(`Mt.Authoring`·`Mt.App`)의 컴파일·의미 적응은 **소비자 쪽에서** 고친다.
   이 사본의 파일을 고쳐 맞추지 않는다 — 사본을 고치는 순간 다음 동기화가 그 수정을
   조용히 지운다.
4. **고정** — 전체 빌드 + 전체 테스트. 이 표의 원본 커밋·반입일을 갱신한다.

## 검증

```
diff -r --exclude=Tests --exclude='*.meta' --exclude='*.asmdef' \
        --exclude=Ked.Presentation.Core.csproj --exclude=vendoring.md \
        <ked-presentation-runtime의 그 커밋>/Assets/Ked.Presentation/Core src/Ked.Presentation.Core
```

차이 0줄이 동기화 완료다.

## 유니티 호환 안전핀

csproj가 원본 asmdef의 `noEngineReferences: true`와 유니티 C# 수준(LangVersion 9,
netstandard2.1)을 컴파일러 제약으로 유지한다.
