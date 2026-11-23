# 목표 계산 경계 — "스펙 → 타깃" 규약

연출 커맨드는 트윈을 시작하기 전에 "끝나면 노드가 어떤 값이어야 하는가"를 먼저 정한다.
그 계산을 엔진 의존이 없는 순수 함수(`NodeTargets` · `ShotIntents`)로 떼어 낸 것이 이 경계다.
떼어 낸 이유는 하나다 — 같은 값이 장부 · 트윈 · 정지 프레임 세 곳에 쓰이므로,
계산이 한 자리에 있어야 셋이 갈라지지 않는다(바로 아래).

처음에는 코드베이스 **57곳**에 흩어져 있던 `ClaimTarget` 관습이었고, 그것을 규약으로 올렸다.
이후 커맨드 이관이 전부 이 문서의 모양을 따른다.

본보기: [`Reduce/NodeTargets.Staging.cs`](../Reduce/NodeTargets.Staging.cs) (MoveBy · ScaleTo)
↔ 호스트 `MoveByCommandCharR` · `ScaleToCommandCharR`.

## 타깃이 흐르는 세 갈래

```
              NodeTarget
                    │
   ┌────────────────┼────────────────┐
   ↓                ↓                ↓
① 장부 게시      ② 트윈 종점      ③ 상태 폴드
Ledger.Publish   DOTween 종점     target.ApplyTo(tree)
(정착 예약)      (재생)           (정지 프레임)
```

**셋이 같은 값을 보므로 "재생 결과 = 정착 예약 = 정지 프레임"이 한 곳에서 갈라진다.**
하나라도 빠뜨리면 나중에 원인 모를 불일치가 난다.
(`NodeTargetTests.세_갈래가_같은_값을_본다`가 이 성질을 고정한다.)

## 코어로 가는 것 / 호스트에 남는 것

| | 어디 | 왜 |
|---|---|---|
| 스펙 → 타깃 변환 (`NodeTargets.Xxx` · `ShotIntents.Xxx`) | **코어** | 정지 프레임이 필요한 전부. 순수 산수다 |
| 출력 타입 (`NodeTarget`) | **코어** | 게시·트윈 종점·폴드가 같은 값을 봐야 한다 |
| `CommandBase` · 코루틴 · `WaitForCompletion` | 호스트 | MonoBehaviour 호스트를 문다 |
| `ResolveRefs` (roleKey → RectTransform) | 호스트 | 유니티 참조 해석이다 |
| `DOKill` · 트윈 생성/가속 (`OnStepLifetimeFinished`) | 호스트 | 시간의 세계다 |
| Commit의 트랜스폼 쓰기 · Ledger `Clear` | 호스트 | 유니티에 쓰는 일이다 |
| `HasClaimedTarget` 수명 관리 | 호스트 | 실행 수명의 일부다 |

## 목표 계산의 형태

결과물 이름의 클래스에, 커맨드 이름의 메서드다. 노드 타깃은 `NodeTargets`(partial — 파일은
리듀서와 같은 갈래로 Staging · Anchor · Portrait · Placement), 샷 의도는 `ShotIntents`.

```csharp
public static partial class NodeTargets         // Reduce/NodeTargets.*.cs
{
    public readonly struct XxxArgs { ... }      // 파싱된 스펙 값만. UnityEngine 타입 금지
    public static NodeTarget Xxx(               // 순수 함수. 이름 = 실행 커맨드 (MoveByCommandCharR → MoveBy)
        string nodeKey,                         // 논리 노드 키
        in XxxArgs args,
        /* 필요한 현재 상태를 명시적 인자로 */ ...);
}
```

무대 상태를 읽고 실패 사유를 낼 수 있는 것은 `Try`로 시작한다 —
`TrySolveFocusPlacement` · `TrySetDepth` · `TryPortraitSizing`(`out` 목표 · `out string reason`).

- **순수**: 시간·랜덤·IO·전역 상태 없음. 트랜스폼을 **읽지도** 않는다 —
  현재 상태가 필요하면 값으로 받는다. 같은 입력은 언제나 같은 출력.
- **출력은 `NodeTarget`.** 복수 노드를 겨누면 배열이다(SetDepth처럼 위치+스케일 두 장).
  스케일 타깃의 z 보존 규약은 `ApplyTo`가 안다.

## 호스트 어댑터의 형태 (본보기에서)

```csharp
protected override void ClaimTarget()
{
    _rect.DOKill(true);                                    // 호스트: 시간의 세계
    _startPos = _rect.anchoredPosition;                    // 호스트: 현재 상태 읽기

    NodeTarget target = RigTargets.MoveBy(_rect, _spec, _startPos);     // ── 코어 ── (변환은 RigTargets가)
    _rig.PlacementTargets.Reserve(_rect, target, Releases);               // 호스트: 장부에 게시 (해제는 확정 때 실행기가 — CommitReleases)
    _destPos = target.Value.XY.ToUnity();                                 // 호스트: 트윈 종점은 타깃에서 읽는다
}

// 리그 커맨드의 Unity → Core 경계(Stage/Character/Commands/RigTargets.cs).
// 커맨드는 변환과 Args 조립을 들고 있지 않는다.
public static class RigTargets
{
    public static NodeTarget MoveBy(RectTransform rect, MoveByCommandSpecCharR spec, Vector2 start)
        => NodeTargets.MoveBy(
            rect.name,
            new NodeTargets.MoveByArgs(spec.useAbsolutePosition, spec.delta.ToCore()),
            start.ToCore());
}
```

**동작 불변이 규약이다**: 어댑터는 계산의 **자리만** 바꾸고 값·순서·부작용을 바꾸지 않는다.
그래서 목표 계산 테스트의 기대값은 **종전 식**에서 온다 — 새 기대값을 만들면 불변을 주장할 수 없다.

호스트에서 노드의 정체는 `RectTransform` 참조이고, 타깃의 `NodeKey`는 진단용 이름이다.
장부(`CharacterPlacementTargetLedger`)가 참조 ↔ 키 대응을 안다.

유니티 ↔ 코어 벡터 변환은 `PresentationCoreConversions`의 `ToCore()` / `ToUnity()` 한 곳에 둔다.

## 표본 정독 보고 — 편차 축

착수 전 표본 8곳 정독(placement / setup / acting / color / overlay). 결과:

> ⚠ **이 절은 조사 시점의 기록이다.** 아래에 근거로 든 커맨드 중
> 절차적 연기 계열(`Tremble` · `Sway` · `Breath` · `Walk` · `Hop` · `Bounce`)과
> `ColorTo` · 오버레이/이모지 리그는 **이후 삭제됐다**(연기는 Spine으로 주인이 바뀌었다 —
> [SCOPE-BOUNDARY.md](../../../../SCOPE-BOUNDARY.md) 참조).
> 이름을 남겨 둔 이유는 **경계가 왜 여기 그어졌는지의 근거**이기 때문이다.
> 코드에서 찾지 말 것.

### 축 ① — dest 출처

| 유형 | 예 | 이관 |
|---|---|---|
| 순수 (스펙 + 현재 값) | MoveBy · ScaleTo · OverlaySize · ColorTo | 그대로 가능 |
| 튜닝 값 필요 | SetDepth (depth 프리셋) | 파싱된 값을 `Args`에 담아 받는다 |
| **현재 정착 상태 의존** | (없다 — 2026-11-21에 PlaceCharacterFocus를 걷었다) | 이런 계산은 커맨드가 아니라 도구의 보조 계산으로 둔다 |
| **절차적 (시간·랜덤)** | Tremble · Sway · Breath · Walk · Hop · Bounce | **불가** (아래) |

### 축 ② — 상태 종류

트랜스폼(anchoredPosition / localScale / localEuler) · alpha(Fade) · color(ColorTo) ·
sizeDelta(Overlay) · 마스크(StageMaskMotion).
`NodeTarget`은 트랜스폼 셋으로 시작하고, 필요한 종류를 그때 더한다.

### 축 ③ — 게시 여부

> **2026-11-05 갱신.** 처음(2026-08)엔 "남이 알아야 하는 노드"만 골라 4곳(MoveBy · ScaleTo ·
> PlaceCharacterFocus · SetDepth)에 손으로 게시를 넣었다. 그 판정에서 **회전(SwayPivot)과
> 슬라이드(Track)가 빠졌다** — 둘 다 측정 체인 위라 같은 줄의 place가 트윈 중간값을 읽었다.
> 지금은 판정하지 않는다. 커맨드가 내는 타깃은 **전부** 장부의 `Reserve`로 게시되고
> 확정 때 실행기의 `CommitReleases`가 푼다(2026-11-06 — 그 전엔 상속 층 `RigClaimTweenCommandBase`가 했다). 체인 아래 노드도 게시된다 — 아무도 안 읽을 뿐 틀리지 않는다.
> 해제를 잊는 상태는 구조적으로 없다 — 게시하는 그 호출이 해제를 등록한다. 목표 계산 없이 끝값을 정하는 커맨드(Slide ·
> Mirror)는 호스트가 타깃을 만든다 — "타깃 = 목표 계산 출력"의 유일한 예외다.

## 두 가지 주의 (표본 정독에서 드러난 것)

**1. 이관 단위는 `ClaimTarget` 메서드가 아니라 "dest를 계산하는 자리"다.**
`ScaleToCommandCharR.ClaimTarget`은 `DOKill`만 하고, 계산은 `CaptureTweenEndpoints` →
`ResolveTargetScale`에, 게시는 `PublishSettledTarget`에 따로 있다.
메서드 이름으로 찾으면 놓친다.

**2. 절차적 커맨드는 이관 대상이 아니다.**
`Tremble`은 `_basePos`와 `Random.Range` 시드만 잡고 **끝값 자체가 없다** —
시간 함수로 계속 흔들 뿐이다. 정지 프레임에서 이런 커맨드는 목표가 정의되지 않으므로,
리듀서에서 `Unhandled`로 남는 것이 맞다. 이관 개수 추정에서 뺀다.

## 사장 코드

`PlaceToCommandCharR`은 게시·클리어가 통째로 주석 처리돼 있다. 이관 대상이 아니다.

## 이관 커버리지 — 무엇이 끝났고 무엇이 남았나

yarn 4묶음 기준. "이관 안 함"은 **조용히 버린 것이 아니라 판정한 것**이다.

### placement (char_rig_placement)

| 커맨드 | 처리 |
|---|---|
| `place` · `left` · `right` · `up` · `down` · `move_reset` | `NodeTargets.MoveBy` — 슬롯의 본래 자리(`CharSlot_Track` — `place`, 절대)와 그 위의 움직임(`CharSlot_Move_X` · `_Y` — 넷은 상대, `move_reset`은 둘을 0으로). 노드와 방향은 `NodeTargets.TryGetSlotMove`(docs/PLAN_move-axes.md) |
| `focus_move` · `focus_rotate` · `focus_scale` · `focus_reset` | `NodeTargets.MoveBy` · `RotateTo` · `ScaleTo`(절대값) — 포커스 계층(`CharFocus_*`) |
| `cast`(set_anchor) | `NodeTargets.SetAnchor` — 런타임 파서가 `cast`를 배역 → pose → face → SetAnchor로 팬아웃하므로 `cast`도 축 리셋 + 역할 앵커를 접는다(`StageReducer.ApplyRoleAnchor`) |
| `fade_in` / `fade_out` | `NodeTargets.FadeIn` / `NodeTargets.FadeOut` (가시성 축) |
| (보조 계산 — 커맨드 아님) | `NodeTargets.TrySolveFocusPlacement` → `SettledFocusMath.SolveFocusPlacement`. 초점이 화면 지점에 오는 이동 노드(`CharSlot_Track` · `CharFocus_Track`)의 값 — 도구가 부르고 대본에는 `place` · `focus_move`로 적힌다 |
| `set_portrait_sprite` | `NodeTargets.SourcePixelsSizing` — 원본 픽셀 크기를 접는다(아래 참조) |

### 초상 (portrait)

접는 것은 **사이징 한 축**이다: 어느 스프라이트인가가 아니라 그 스프라이트가 만드는
`CharacterPortraitSprite_Image.sizeDelta`. 초상은 원본 픽셀 크기 그대로라(2026-11-09, 재생 저장소
`docs/PLAN_portrait-source-pixels.md`) 그림 크기가 `portrait-dimensions.json`에서 오고,
조회 규약(캐릭터 소문자 · 변형은 문자열 전체 · 표정 2자리 · (캐릭터,"a","01") 폴백)은
런타임 `PortraitResolver`를 그대로 옮긴 것이다.

| 커맨드 | 변형 | 표정 | 사이징 |
|---|---|---|---|
| `cast (slot, char, var, emo)` | 갱신 | 인자 | ✅ |
| `face` · `face_swap (target, emo)` | 유지 | 인자 | ✅ |
| `pose (target, variant)` | 갱신 | — | ❌ |

**표정은 상태가 아니다** — 런타임 `CastBinding`도 변형만 들고, 표정은 커맨드마다 인자로 온다.
**`pose`가 사이징을 다시 접지 않는 것도 실동작이다** — `SetPortraitPoseCommandCharR`의
스프라이트 교체가 비활성이라, 크기는 다음 `face`/`face_swap`에서야 바뀐다.

### depth

`PlaceCharacterFocus`의 계산부가 `SettledFocusMath`로 갔다.
이 묶음의 실작업은 solver 순수화 하나였고, `TryMeasureFocusWithTemporaryDepthTransform`
("적용→측정→복원")도 함께 사라졌다. `SetDepth`는 2026-11-21부터 초점을 재지 않는다 — 깊이 레벨의 값을
그대로 놓는 순수 타깃이다(`NodeTargets.TrySetDepth`). `keep_y`면 배율만, `x배율`이면 배율을 직접 놓고 Y는 건드리지 않는다(`SizeToken`).

### shot

`shot_zoom` · `shot_pan` · `shot_focus` · `shot_track` · `shot_dolly` · `shot_roll` · `shot_reset` · `shot_reset_all` 전부(2026-11-17 — 종전 5종).
`shot_<축>`은 그 축 하나를 절대값으로 놓고 나머지는 그대로 둔다 — `ShotIntents`의 함수가 전부 `from`을 받아 제 축만 바꾼다. 상대값 명령은 없다.
`ShotIntentMath`가 zoom · dolly→배율 규약(0.05)의 유일한 자리다 — 커맨드와 적용측이 같이 본다.

샷 목표 계산의 출력은 노드 타깃이 아니라 **샷 의도 `ShotIntentState` 하나**다 — 렌즈(zoom · pan · focus) · 헤드(roll) · 몸(track · dolly).
그 값이 무대 노드(무대마다 카메라 리그 · 층마다 하나)에 무엇을 쓰는지는 **`Transforms/ShotLayoutMath`가 유일한 자리**다(2026-11-05).

`shot_shake`(화면 흔들림)는 **접을 것이 없다.** 카메라 리그에 건 `restore` 모션 클립이라 끝나면 0이고 상태에 남기는 것이 없다 —
위의 절차적 커맨드(끝값이 정의되지 않아 `Unhandled`)와 다른 부류다. 시간 함수가 아니라 키가 있는 클립이고, 끝값이 "아무것도 남기지 않음"으로 정의돼 있다.
틀 수 없는 줄(모르는 이름 · 카메라 리그가 아닌 클립)만 `Unhandled`로 남는다.

| 누가 | 무엇을 |
|---|---|
| 런타임 `PresentationShotResponseSystem` | 식의 출력을 노드에 쓴다(applier). 중립 = `Solve(Default)` |
| 폴드 `StageReducer.Shot` | 의도만 접는다. 층 반응은 상태에 저장하지 않는다 — 의도 + 부착 d에서 언제든 풀린다 |
| 하네스 `StageStateComparer.CompareStageView` | 같은 식으로 기대값(카메라 · 층마다 중심 · 전체 배율)을 풀어 캡처와 비교한다. 샷은 본 무대만 움직이므로(`StageRoles`, 2026-11-06) 덮는 무대의 기대값은 `Solve(Default)` |

보조 계산 `ShotIntents.FocusTo`(걷은 `shot_focus_to`의 식)는 정방향 식의 역으로 팬을 푼다(`ShotLayoutMath.SolvePanToPlace`). 입력은 무대 좌표의 focus와 그 슬롯이 선 층 d다.
팬 1px당 그 점이 화면에서 움직이는 양이 층마다 일정(`1 + panResponse`)해서 축마다 나눗셈 한 번이다.
종전에는 "지금 카메라를 씌웠다 벗기는" 왕복 뒤 카메라만 본 식(`desired − p × 배율`)이었고, mid가 아닌 층에서 층 반응만큼 빗나갔다
(close에서 약 96px). 런타임과 코어가 똑같이 틀려 하네스가 잡지 못했다(`a7974fa2`에서 고침).
식 · 계층 · 결정은 [stage-rig.md](../../Runtime/StageRig/Documentation~/stage-rig.md).

### staging (12커맨드)

| 커맨드 | 스펙 클래스 | 처리 |
|---|---|---|
| `left` · `right` · `up` · `down` · `move_reset` | `MoveByCommandSpecCharR` | placement 목표 계산이 이미 덮는다 (`move_by`는 2026-11-23에 걷었다) |
| `rotate_by` · `rotate_reset` | `RotateToCommandSpecCharR` | `NodeTargets.RotateTo` |
| `sibling_front` · `sibling_back` | — | **이관 안 함** (아래) |
| `char_to` | — | **어휘에서 걷었다 (2026-11-06)** — 리그는 무대 · 층을 옮기지 않는다. 다른 자리는 fade_out 뒤 새 slot([stage-rig.md](../../Runtime/StageRig/Documentation~/stage-rig.md) §2.4). 무대를 이름에 박은 `char_to_s0/s1/s2`는 그 전 2026-11-03에 걷었다 |

**구조 커맨드(`sibling_front` · `sibling_back`, 그리고 걷기 전의 `char_to`)를 이관하지 않는 이유: 변환부가 항등이다.**
스펙 값(stage·layer·순서 모드)이 곧 목표이고 본문은 유니티 구조 조작(`SetParent` ·
`SetSiblingIndex`)뿐이라 **코어로 옮길 계산이 없다.**
이 구조 축(리그가 어느 스테이지/레이어에 어떤 순서로 붙는가) 중 **부착은 데이터 모델에 들어갔다** —
`StageState`의 `SlotAttachment`(무대 키 · 층 라벨 · 깊이 d)를 slot · bg_spawn이 스폰 때 한 번 접는다.
층 d는 2026-11-05부터 하네스의 층 판정과 `ShotIntents.FocusTo`의 팬 역산이 읽는다. 형제 순서(`sibling_*`)는 아직 모델 밖이다.

### 이관 대상이 아닌 부류 — 절차적 커맨드

`Tremble` · `Sway` · `Breath` · `Walk` · `Hop` · `Bounce` 등은 끝값 자체가 없다.
`_basePos`와 랜덤 시드를 잡고 시간 함수로 계속 흔들 뿐이다.
정지 프레임에서 이들의 목표는 **정의되지 않으므로** 리듀서에서 `Unhandled`로 남는 것이 맞다.

**이 판정은 등가성 하네스의 실재생으로 확인됐다.** 라인 단위(HurryUpLine) 재생에서
절차적 축이 그대로 드러났다:

```
CharacterPortrait_SwayPivot.localEulerAngles  접힘=(0,0,0) vs 캡처=(0,0,7.53) / (0,0,359.62) …
```

값이 매 라인 제각각인 것이 특징이다 — 끝값이 아니라 **시간 함수의 스냅샷**이라서.
랩드스킵 재생에서는 즉시 확정되어 누적될 시간이 없으므로 이 축이 보이지 않는다.
그래서 폴드 대조의 기준 프로토콜은 랩드스킵이다(하네스 파일 상단 주석 참조).

### 남은 Unhandled의 회계 (대표 에피소드 실측)

수렴이 끝난 시점의 리포트 `finalUnhandled` 153건을 **하나도 남김없이** 분류한 것이다.
"아직 못 접는 것"의 목록은 작업 목록이어야 한다 — 작업이 아닌 것이 섞이면 거짓말이 된다.

| 부류 | 건수 | 판정 |
|---|---|---|
| 시간 커맨드 (`pause` · `1fr`~`48fr`) | 19 | **접을 것이 없다** → 무해하게 접도록 고쳤다 |
| 절차적 연기 (`tap`·`sway*`·`jolt`·`dip`·`idle_*`) | 38 | **이관 대상 아님** — 끝값이 정의되지 않는다 |
| 화면 효과 (`screen_vignette/noise/flash`, 그리고 층 블러 — 지금 `stage_blur`) | 36 | 무대 상태 모델 밖 (포스트 이펙트 축). 단 `stage_blur`는 이후 리그마다 거는 셰이더 블러가 되어 `char_visual_*`과 같은 머티리얼 축이다(2026-11-03 주). 2026-11-17에 더한 화면 덮개(`screen_cover` · `screen_cover_clear`)와 층의 어둡기 판(`stage_dim` · `stage_dim_reset`)도 같은 경계로 접지 않는다(docs/PLAN_screen-cover.md §6) — 접는다면 화면 효과 전체를 함께 정한다 |
| 배경 (`bg_*`) | 19 | **이관 완료 (2026-10-29)** — 배경은 캐스팅하지 않은 캐릭터 슬롯이 됐다. 아래 참조 |
| 캐릭터 비주얼 (`char_visual_*`) | 14 | 무대 상태 모델 밖 (머티리얼 축) |
| 오디오 (`bgm` · `stop_bgm` · `sfx`) | 14 | 무대 상태 모델 밖 |
| 무대 마스크 (그때 `stage_mask_*`, 지금 `stage_mask`) | 10 | 무대 상태 모델 밖 |
| 캐릭터 리그 트랜스폼 (`slide_in`) | 2 | **이관 완료 (2026-08-24)** — 아래 참조. 2026-11-23에 어휘에서 걷었다 |
| 대사창 (`surface_reset`) | 1 | 무대 상태 모델 밖 |

즉 **캐릭터 리그의 좌표·가시성 축은 이제 전부 접힌다** — 마지막까지 남아 있던 `slide_in`을
2026-08-24에 이관했다(소유자 보고: "SlideOut, SlideIn이 지금 동작하지 않는데").

**예외 — 컷인 판 · 팝업 그림(2026-11-17).** 둘은 배경처럼 캐릭터 리그로 서지만 **폴드 밖**이다(재생 저장소
`docs/PLAN_stage-visibility.md` §3.5). 자리 · 배율이 대본 인자 그대로라 맞춰 볼 계산이 없고, 툴이 쓸 것은 끝 상태 표
(`CutIns` · `Popups`)에 이미 있다. 런타임이 세울 때 표시(`outsideFold`)를 달아 하네스의 캡처가 건너뛴다 — 리듀서는 그대로다.
대본이 판을 치우는 줄(`bg_fade_out stage01_cutin_panel`)은 폴드가 그 슬롯을 몰라 Unhandled로 남는다(불일치가 아니다).

**예외 — 대사창 초상의 인물(2026-11-18).** 초상 인물도 캐릭터 리그로 서지만 **폴드 밖**이다 — 화면 밖 촬영소에 서고
그 그림을 대사창이 보인다. 코어가 알 필요가 없는 리그다(소유자). `portrait` · `portrait_close`와 슬롯 키 `portrait`에 쓴 명령은
접지 못한 명령으로 남고, 하네스의 캡처는 같은 표시(`outsideFold`)로 건너뛴다 — 리듀서는 그대로다(docs/PLAN_portrait-capture.md §5).

**배경(`bg_*`)은 2026-10-29에 모델 안으로 들어왔다.** 별도 배경 리그를 지우고 배경을 **캐스팅하지 않은
캐릭터 슬롯**으로 세우기로 했기 때문이다(재생 저장소 `docs/PLAN_background-as-slot.md`). 그래서 `StageState`가
"캐릭터 리그만 담는다"는 문장은 더 정확히 "캐릭터 리그 *스키마*만 담는다"가 됐고, 배경 슬롯은 그 스키마의
인스턴스다. 폴드는 새 목표 계산을 만들지 않았다 — `StageReducer.Background.cs`는 슬롯 스폰·
fade·move·scale을 **같은 노드, 같은 목표 계산**으로 접는다. 사이징도 초상과 같다 — 처음엔 둘 다 높이 맞춤이었고,
2026-11-09부터 둘 다 원본 픽셀 크기 그대로다(`NodeTargets.SourcePixelsSizing`, 재생 저장소
`docs/PLAN_background-source-pixels.md` · `docs/PLAN_portrait-source-pixels.md`). 다른 것은 크기의 출처 하나 — `background-dimensions.json`.
`bg_size`·`bg_place`는 어휘에서 지웠으므로 케이스가 없다 — 남은 대본이 있으면 여기서 소리가 난다(의도).

같은 작업에서 **슬롯 재선언의 의미**를 런타임에 맞췄다. 런타임은 같은 키로 `slot`/`bg_spawn`을 다시 부르면
새 리그를 짓고 옛 리그를 부순다. 폴드는 "재사용한다 — 좌표도 유지"로 접고 있었다(사실이 아니었다).
배경을 합치며 같은 키의 `bg_spawn` 재선언을 검토하다 드러났다(배경 그림만 바꾸는 것은 `bg_sprite`). 지금은 노드·α가 스키마로 돌아가고 배역만 남는다.

> 2026-11-23: `slide_in` · `slide_out` · `SlideMotion` · `SlideCommandCharR`을 걷었다(재생 저장소 `docs/PLAN_move-axes.md` M9) — 들고 나는 움직임은
> `left` · `right` · `up` · `down`으로 적는다(움직임 노드를 밀어 두었다가 0으로 되돌린다 — 본래 자리가 바뀌지 않는다). 튐(punch)은 사라졌다. 아래는 걷기 전의 기록이다.

**둘은 대칭이 아니다.** `SlideCommandCharR`(2026-11-06까지 `SlideCommandBase` + 파생 둘)가 등장·퇴장의 차이를 "현재 위치가 도착점이냐
출발점이냐" 하나로 환원하기 때문이다:

| | 정착 상태 | 표적 | 기본 방향·거리 |
|---|---|---|---|
| `slide_in` | **항등** — 도착점이 클레임 시점의 현재 위치이고, 화면 밖 출발점과 punch 오버슈트는 트윈 중에만 존재한다 | `CharSlot_Track` | left · 12u |
| `slide_out` | **방향 × 거리** — 나간 자리에 남는다(가시성은 안 건드린다) | `CharSlot_Track` | right · 12u |

그래서 `slide_in`의 폴드는 "**위치를 바꾸지 않는다**"를 명시하는 것이고 판정에는 영향이
없다 — 값이 아니라 `Unhandled`가 줄어든다(고칠 것이 없는 항목은 작업 목록의 소음이다).
반대로 `slide_out`은 **접히지 않는 동안 정착 상태가 실제로 틀렸다**: 나갔는데 폴드는
제자리를 말했다. 거리·방향 파싱은 브리지가 지나는 것과 같은 함수를 쓴다
(`UnitToken.TryParsePixels` — 음수는 거부가 아니라 0으로 클램프).

⚠ 트윈 중의 모양(등장이 들어오는 궤적, punch)은 폴드의 것이 <b>아니다</b> — 시간 축이고,
정지 프레임을 판정하는 이 문서의 범위 밖이다. 다만 **그 축도 값은 여기서 온다**: 방향 낱말
표·스펙 기본값(방향·거리·ease·punch)·튐 모양을 `SlideMotion` 하나가 갖고, 리듀서와 툴의
시간 계획이 같은 것을 본다(사본 금지). 등장의 화면 밖 출발점은 그 값으로 **합성**한다 —
`현재 자리 + 방향 × 거리`. 순변위가 0인 채로 궤적이 서는 이유다.

### 백로그

- 구조 축 상태 모델의 형제 순서(`sibling_*`). 부착(stage/layer)은 `SlotAttachment`로 들어갔다
- `SetPortraitSprite` (초상 축)
- 화면 지점 비율표(`ScreenPointRatios`) — 게임별 값이므로 코어 코드가 아니라 **tuning 데이터**가 맞다. U12 덤프 추가 후보.
- SO 저작 전용 스펙 — yarn 4묶음 밖. 필요하면 같은 규약으로. (`RotateByCommandCharR`는 대본이 낸 적이 없어 2026-11-06에 지웠다 — `rotate_by`는 `RotateToCommandCharR`의 상대 모드다.)
- **커맨드 수명 통합** — **완료** (`PresentationCore/Base/ClaimTweenCommandBase.cs`).
  `ShotIntentCommandBase`가 shot 계열에 했던 통합의 일반화. 하네스가 오라클이므로
  **이관 배치마다 랩드스킵 재판정으로 리포트가 리팩터 전과 같은지 확인했다.**
  - 배치 1: MoveBy · ScaleTo · RotateTo (코어 배선이 끝난 셋)
  - 배치 2·3: Fade 4종(`CanvasFadeCommandBase` — 2026-11-06에 `FadeCommandCharR` 하나로 합쳤다) · BgR 트랜스폼 계열 · RotateBy 쌍
  - 배치 4: PivotRotateTo · Slide 4종(`SlideCommandBase` — 2026-11-06에 `SlideCommandCharR` 하나로 합쳤다) ·
    Overlay Move/Scale/Size · Overlay Show/Hide(`OverlayRootFadeCommandBase`) ·
    SetDepth(타깃 2장 + 시퀀스) · PlaceCharacterFocus(타깃 1장 + 게시) ·
    MirrorCharacter · CharVisualFocus · StageDepthDefocus

  스텝 경계 가속(2026-11-06 개편): 처음엔 트윈을 죽이고 남은 비율(`MeasureRemainingRatio` — 커맨드마다 거리·각도·배율로 어림)로
  새 트윈(`CreateAcceleratedTween`)을 만들었고, 그 변형점으로 `AccelerateOnStepFinish` · `StepFinishSpeedUpMultiplier`가 있었다.
  지금은 **지금 트윈의 `timeScale`을 30으로 올린다** — 같은 길을 빠르게 끝까지 돌고, 남은 시간은 트윈이 안다.
  그래서 커맨드가 채울 훅은 `ResolveRefs` · `ClaimTarget` · `CreateTween` · `OnCommitFinalState` 넷뿐이다.
  이게 되는 까닭은 값과 타이밍이 갈라져서다 — 끝값은 장부의 타깃이 쥐고(트윈 전에 게시, 확정 때 해제),
  트윈이 언제 거기 닿느냐는 보이는 연출일 뿐이다. 장부가 없던 때는 다음 커맨드가 라이브 트랜스폼을 읽었기에
  남은 만큼을 재서 다시 만들어야 했다. 샷과 화면 효과·블러의 전환(`IntentTransition`)도 같은 날 같은 방식으로 바꿨다 —
  거기서는 `Intent`가 끝값을 쥔다.

  - 대상 아님: 절차적 연기 —
    끝값이 정의되지 않는다. 즉시 확정(`SetAnchor`)은 트윈이 없어 모양이 다르다.
  - 기반이 의도적으로 통일한 미세 차이 (실경로 무영향):
    ① 해석 실패 시 NRE 대신 경고+건너뛰기
    (예전의 ② 가속 잔여시간 0 경계 즉시 확정 · ③ 0.01초 하한은 timeScale 개편으로 사라졌다 — 잔여시간을 계산하지 않는다.)
       종전에 하한이 없던 것은 `SetDepth` 하나뿐이다.
    ④ `PlaceCharacterFocus`는 출발=목표일 때 잔여비율이 NaN이 되어
       NaN 길이 트윈을 만들고 있었다. 기반의 0 가드가 이를 없앤다.
