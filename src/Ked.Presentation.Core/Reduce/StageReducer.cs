using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>리듀서에 주입되는 게임별 값 (게임 데이터는 코드가 아니라 tuning 인자).</summary>
    public sealed class StageReducerTuning
    {
        /// <summary>ExportedTuning/rig-schemas.json. slot 폴드가 캐릭터 리그를 여기서 세운다.</summary>
        public RigSchemasFileDto RigSchemas;

        /// <summary>기준 해상도 가로 폭. 1u 환산의 유일한 입력.</summary>
        public float ReferenceStageWidth;

        /// <summary>기준 해상도. 무대 루트 공간의 크기다.</summary>
        public Vec2 BaseResolution;

        /// <summary>
        /// presets/role-anchor.json — cast가 입히는 캐릭터별 앵커.
        /// 없으면(null) cast가 Default로 접되 Unhandled로 소리를 낸다 —
        /// 실측 덤프에 비기본값 엔트리가 있으므로(tyrant·Amber) 침묵하면 그 장면이 어긋난다.
        /// </summary>
        public RoleAnchorTuningBodyDto RoleAnchors;

        /// <summary>
        /// presets/depth.json의 프리셋 표. **size 폴드는 더 이상 읽지 않는다** —
        /// 라벨은 level 커브 위의 눈금이 됐다(DepthLevelLabels). 기존 로더가 계속
        /// 컴파일되도록 필드만 남겨 둔다. 사본 동기화 후 지워도 된다.
        /// </summary>
        public DepthPresetSetDto DepthPresets;

        /// <summary>presets/depth.json의 "level" 커브 — 숫자 레벨(size c1 5) 폴드. 없으면 그 커맨드만 Unhandled.</summary>
        public DepthLevelTuningDto DepthLevel;

        /// <summary>presets/focus-tuning.json — 초점의 오프셋. 폴드는 읽지 않는다 — 초점을 무대 지점에 놓는 값을 푸는 도구의 계산(NodeTargets.TrySolveFocusPlacement)이 쓴다.</summary>
        public FocusTuningBodyDto FocusTuning;

        /// <summary>portrait-dimensions.json — 초상 사이징의 그림 크기. 없으면 사이징은 Unhandled.</summary>
        public PortraitDimensionsFileDto PortraitDimensions;

        /// <summary>
        /// motions.json — 이름 → 클립(검증된 것만). motion 폴드가 채널마다 마지막 키를 타깃으로 접는다.
        /// 없으면(null) motion 커맨드는 Unhandled — 재생이 모션을 읽었다면 폴드도 같은 파일을 받아야 한다.
        /// </summary>
        public IReadOnlyDictionary<string, MotionClip> Motions;

        /// <summary>
        /// 이모지 정의(emojis.json) — 재생이 읽은 것과 같은 것. 없으면 이모지 0개다:
        /// 런타임이 모르는 이모지를 경고하고 건너뛰듯, 폴드는 Unhandled로 남긴다.
        /// </summary>
        public IReadOnlyDictionary<string, EmojiDefinition> Emojis;

        /// <summary>background-dimensions.json — 배경(bg_*) 사이징의 그림 크기. 없으면 그 축만 Unhandled.</summary>
        public BackgroundDimensionsFileDto BackgroundDimensions;

        /// <summary>
        /// presets/layer-profile.json — 깊이 층이 샷에 반응하는 곡선(ShotLayoutMath). 없으면(null) 코어 기본
        /// 표본(LayerProfileCurve.Default = 종전 다섯 층 값)을 쓴다. 층 반응은 상태에 저장하지 않고
        /// 샷 의도 + 부착된 층 d에서 푼다(StageState.AttachedLayerDepths, StageStateComparer.CompareStageView).
        /// </summary>
        public LayerProfileFileDto LayerProfile;

        /// <summary>LayerProfile이 있으면 그 곡선, 없으면 코어 기본(= 런타임 조립이 쓰는 값).</summary>
        public LayerProfileCurve ResolveLayerProfileCurve()
            => LayerProfile != null ? LayerProfileCurve.FromDto(LayerProfile) : LayerProfileCurve.Default;
    }

    /// <summary>
    /// 커맨드 열 → 확정 무대 상태.
    ///
    /// Apply(state, command, tuning) → state — 순수 함수다: 입력 상태를 바꾸지 않고
    /// 새 상태를 돌려준다. 시간·랜덤·IO 없음. duration 인자는 정지 프레임에 무의미하므로
    /// 파싱조차 하지 않는다.
    ///
    /// Unhandled 규율 — 네 갈래를 전부 잡는다. 조용히 버리지 않는다:
    ///   ① 모르는 커맨드   ② 잘못된 인자   ③ 미스폰 슬롯   ④ 폴드 중 예외
    /// 이 기록이 "아직 못 접는 것"의 유일한 진실이고, 수렴의 작업 목록이 된다.
    ///
    /// 현재 어휘(v1): slot 계열(리그 스폰) · cast/actor(배역·별칭) ·
    ///   fade_in/out · fade · place · left/right/up/down · focus_* · size 계열 ·
    ///   move/rotate 계열 · shot 계열 ·
    ///   초상 축(pose/face/face_swap) · motion(채널마다 마지막 키) · 시간 커맨드(pause — 접을 것이 없다) ·
    ///   배경 bg_*(캐스팅하지 않은 슬롯 — spawn/sprite/fade/move/scale).
    /// 명시적 한계: 오버레이/이펙트/오디오/트랜지션 리그, 대사창,
    ///   절차적 연기 커맨드(끝값이 정의되지 않음).
    ///
    /// 가정 — 등가성 하네스가 판정한다:
    /// 1. 스테이지/레이어 컨테이너는 항등 트랜스폼(스트레치 풀)이라 리그를 루트 직속으로
    ///    세워도 좌표가 같다. 컨테이너 스키마는 덤프에 없다 — 어긋나면 덤프에 추가한다.
    /// 2. 커맨드 기본값은 브리지 시그니처에서 옮겨 박았다(각 case의 주석 참조).
    ///    카탈로그가 데이터가 되면 tuning으로 옮긴다.
    ///
    /// 파일 구성 — 이 파일은 공개 API·디스패치·공용 helper만 갖는다.
    /// 폴드 본문은 디스패치의 구획을 그대로 따라 partial로 나뉜다:
    ///   StageReducer.Slot.cs      슬롯 스폰 · 배역/별칭 · 구조 축
    ///   StageReducer.Anchor.cs    역할 앵커(cast) · fade
    ///   StageReducer.Placement.cs 리그의 세 덩어리가 받는 자리 · 크기 — place · focus_* · size
    ///   StageReducer.Staging.cs   move · rotate · 초상 축의 배율 · 회전 · motion
    ///   StageReducer.Shot.cs      shot 계열
    ///   StageReducer.Background.cs bg_* (배경 = 캐스팅하지 않은 슬롯)
    /// </summary>
    public static partial class StageReducer
    {
        public static StageState CreateInitialState(StageReducerTuning tuning)
        {
            Require(tuning);
            return new StageState(new RectSpace(tuning.BaseResolution, Vec2.Half));
        }

        public static StageState Apply(StageState state, in StageCommand command, StageReducerTuning tuning)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Require(tuning);

            StageState next = state.Clone();

            try
            {
                if (!TryApply(next, command, tuning, out string reason))
                    next.AddUnhandled(command, reason);
            }
            catch (Exception ex)
            {
                // 접다가 터진 커맨드도 침묵 대신 기록이다. 예외로 폴드가 멈추면
                // 그 뒤 라인을 하나도 판정할 수 없다. 상태는 클론이라 원본은 무사하다.
                next.AddUnhandled(command, $"폴드 중 예외: {ex.Message}");
            }

            return next;
        }

        public static StageState ApplyAll(
            StageState state, IEnumerable<StageCommand> commands, StageReducerTuning tuning)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            foreach (StageCommand command in commands)
                state = Apply(state, command, tuning);

            return state;
        }

        // ── 디스패치 ─────────────────────────────────────────────────

        private static bool TryApply(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            reason = null;

            switch (cmd.Name)
            {
                // 슬롯 스폰. 파서 표: slot(slotKey, stage="stage00", layer="mid")
                case "slot":   return ApplySlot(state, cmd, tuning, cmd.Arg(1, "stage00"), cmd.Arg(2, "mid"), out reason);

                // 배역·별칭 — 커맨드 대상 해석(TryResolveSlot)의 전제.
                // 브리지: cast(slot, character, variant="a", emotion="1") — 뒤 둘은 초상 축이다.
                case "cast": return ApplyCast(state, cmd, tuning, out reason);
                case "actor": return ApplyActor(state, cmd, out reason);

                // 초상 축. 브리지: pose(target, variant) / face(target, emotion) /
                //                  face_swap(target, emotion, duration)
                case "pose": return ApplyPose(state, cmd, out reason);
                case "face": return ApplyFace(state, cmd, tuning, out reason);
                case "face_swap": return ApplyFace(state, cmd, tuning, out reason);

                // fade — 브리지와 같은 표적(CharacterPortraitSprite_Root).
                case "fade_in": return ApplyFade(state, cmd, visible: true, out reason);
                case "fade_out": return ApplyFade(state, cmd, visible: false, out reason);
                case "fade":     return ApplyFadeTo(state, cmd, out reason);   // fade(slot, alpha="1", dur) — 반투명까지

                // place — 슬롯의 본래 자리(CharSlot_Track)를 절대값으로. 파서 표: place(role, x="0u", y="0u", duration="0.4s", ease).
                // 초점을 화면 지점에 두는 값은 도구가 풀어 적는다(NodeTargets.TrySolveFocusPlacement) — 폴드는 무대를 재지 않는다.
                case "place": return ApplyPlace(state, cmd, tuning, out reason);

                // 슬롯의 움직임 — 본래 자리 위에서 상대값으로 쌓는다(docs/PLAN_move-axes.md). 가로는 Move_X, 세로는 Move_Y라 함께 돈다.
                // 파서 표: left · right · up · down(role, 거리, duration="0.4s", ease) — 거리에는 생략값이 없다.
                case "left":
                case "right":
                case "up":
                case "down": return ApplySlotMove(state, cmd, tuning, out reason);

                // 포커스 — 대화할 때만 서는 자리. 셋 다 절대값이고, focus_reset이 함께 걷는다(docs/PLAN_rig-layers.md L1 · L3).
                // 파서 표: focus_move(role, x="0u", y="0u") · focus_rotate(role, 도="0") · focus_scale(role, 배율="1") · focus_reset(role) — 뒤에 duration="0.4s", ease.
                case "focus_move":   return ApplyFocusMove(state, cmd, tuning, out reason);
                case "focus_rotate": return ApplyFocusRotate(state, cmd, out reason);
                case "focus_scale":  return ApplyFocusScale(state, cmd, out reason);
                case "focus_reset":  return ApplyFocusReset(state, cmd, out reason);

                // size — 그 사람의 크기(DepthOffset의 Y · DepthScale의 배율). 무대를 재지 않는다.
                // 파서 표: size(role, 레벨·라벨·x배율, [keep_y], duration="13fr", ease) / size_reset(role, duration, ease) — mid로(SizeToken).
                case "size": return ApplySize(state, cmd, tuning, cmd.Arg(1), SizeToken.IsKeepY(cmd.Arg(2)), out reason);
                case "size_reset": return ApplySize(state, cmd, tuning, "mid", keepY: false, out reason);

                // move_by · slide_in · slide_out은 걷었다(docs/PLAN_move-axes.md M4 · M9) — 케이스가 없다. 남은 대본은 Unhandled로 드러난다.
                // 들고 나는 움직임은 left · right · up · down으로 적는다(§4.2).

                // staging 이동/회전 · 초상 축의 배율/회전
                case "move_reset":   return ApplyMoveReset(state, cmd, out reason);   // Move_X · _Y만 — 본래 자리는 그대로
                case "rotate_by":    return ApplyRotateBy(state, cmd, out reason);
                case "rotate_reset": return ApplyRotateReset(state, cmd, out reason);
                case "char_scale_to":  return ApplyPortraitScaleTo(state, cmd, out reason);
                case "char_rotate_to": return ApplyPortraitRotateTo(state, cmd, out reason);

                // motion — 채널마다 마지막 키가 타깃이다. 샘플링하지 않는다(MotionMath.TargetFor).
                case "motion": return ApplyMotion(state, cmd, tuning, out reason);

                // shot — 축마다 하나(shot_<축>), 되돌리기 둘.
                case "shot_zoom":  return ApplyShotZoom(state, cmd, out reason);
                case "shot_pan":   return ApplyShotPan(state, cmd, tuning, out reason);
                case "shot_focus": return ApplyShotFocus(state, cmd, tuning, out reason);
                case "shot_track": return ApplyShotTrack(state, cmd, tuning, out reason);
                case "shot_dolly": return ApplyShotDolly(state, cmd, out reason);
                case "shot_roll":  return ApplyShotRoll(state, cmd, out reason);

                // 렌즈만 되돌린다 — 카메라의 몸과 롤은 그대로다.
                case "shot_reset":
                    state.Shot = ShotIntents.Reset(state.Shot);
                    return true;

                case "shot_reset_all":
                    state.Shot = ShotIntents.ResetAll();
                    return true;

                // 흔들림 — 접을 것이 없다(끝나면 0). 클립만 본다.
                case "shot_shake": return ApplyShotShake(state, cmd, tuning, out reason);

                // 배경 — 캐스팅하지 않은 슬롯. StageCommandParser.Background가 캐릭터 Spec으로 펼치는 것을 그대로 접는다.
                // 인자 자리·생략값은 파서 표(StageCommandParser.Table)와 같다:
                //   bg_spawn(rigKey, spriteKey, stage="stage00", layer="far") ·
                //   bg_sprite(rigKey, spriteKey) · bg_fade_in/out(rigKey, dur) · bg_move(rigKey, x, y, dur) · bg_scale(rigKey, scale, dur).
                // bg_size·bg_place·bg_slotNN은 케이스가 없다 — 지운 어휘다. 남은 대본은 Unhandled로 드러난다.
                case "bg_spawn":  return ApplyBackgroundSpawn(state, cmd, tuning, cmd.Arg(2, "stage00"), cmd.Arg(3, "far"), out reason);
                case "bg_sprite": return ApplyBackgroundSprite(state, cmd, tuning, out reason);
                case "bg_fade_in":  return ApplyFade(state, cmd, visible: true, out reason);
                case "bg_fade_out": return ApplyFade(state, cmd, visible: false, out reason);
                case "bg_move":  return ApplyBackgroundMove(state, cmd, tuning, out reason);
                case "bg_scale": return ApplyPortraitScaleTo(state, cmd, out reason);

                // 컷인 — 끝 상태만(StageReducer.CutIn). 파서 표: cutin_open(무대="stage01", 판, x="0u", y="0u", 배율="1") · cutin_close(무대="stage01")
                case "cutin_open":  return ApplyCutInOpen(state, cmd, tuning, out reason);
                case "cutin_close": return ApplyCutInClose(state, cmd, out reason);

                // 그림 팝업 — 끝 상태만(StageReducer.Popup). 파서 표: popup(무대="stage01", 그림, x="0u", y="0u", 배율="1") · popup_close(무대="stage01")
                case "popup":       return ApplyPopupOpen(state, cmd, tuning, out reason);
                case "popup_close": return ApplyPopupClose(state, cmd, out reason);

                // 이모지 — 슬롯에 딸려 서고 내린다(StageReducer.Emoji). 파서 표: emoji(누구, 이름) · emoji_off(누구, 이름="")
                case "emoji":     return ApplyEmoji(state, cmd, tuning, out reason);
                case "emoji_off": return ApplyEmojiOff(state, cmd, out reason);

                default:
                    // 시간만 쓰는 커맨드는 못 접는 게 아니라 접을 것이 없다 — 아래 참조.
                    if (IsWaitCommand(cmd.Name))
                        return true;

                    reason = "아직 코어로 이관되지 않은 커맨드";
                    return false;
            }
        }

        // ── helper ───────────────────────────────────────────────────

        /// <summary>
        /// 시간만 쓰는 커맨드인가 — `&lt;&lt;pause 시간&gt;&gt;`. 프레임 별칭 `&lt;&lt;Nfr&gt;&gt;`은 걷었다(docs/PLAN_line-beat.md D4) —
        /// 남은 대본의 것은 모르는 이름으로 Unhandled에 남는다.
        ///
        /// 정지 프레임에 목표가 **없는 게 아니라 접을 것이 없다** — 무대 상태를 하나도
        /// 건드리지 않는다. duration 인자를 파싱조차 하지 않는 것과 같은 규율이라
        /// Unhandled로 남기지 않는다: 남기면 "아직 이관 안 됨" 작업 목록이 거짓말이 된다.
        /// </summary>
        private static bool IsWaitCommand(string name) => name == YarnCommandTextExtractor.PauseCommand;

        private static bool TryGetSlotKey(in StageCommand cmd, out string slotKey, out string reason)
        {
            slotKey = cmd.Arg(0);

            if (string.IsNullOrEmpty(slotKey))
            {
                reason = "슬롯 키 인자가 없다";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryGetSpawnedSlot(
            StageState state, in StageCommand cmd, out string slotKey, out string reason)
        {
            if (!TryGetSlotKey(cmd, out string roleKey, out reason))
            {
                slotKey = null;
                return false;
            }

            // 별칭("@3")·캐릭터 키("parkeunseol")도 슬롯으로 푼다 (actor/cast 축).
            if (!state.TryResolveSlot(roleKey, out slotKey))
            {
                reason = $"대상 '{roleKey}'를 슬롯으로 풀 수 없다 (slot/cast/actor 선행 필요)";
                return false;
            }

            reason = null;
            return true;
        }

        private static string[] Prefixed(string slotKey, string[] ids)
        {
            string[] keys = new string[ids.Length];

            for (int i = 0; i < ids.Length; i++)
                keys[i] = StageState.NodeKeyOf(slotKey, ids[i]);

            return keys;
        }

        private static void Require(StageReducerTuning tuning)
        {
            if (tuning == null)
                throw new ArgumentNullException(nameof(tuning));

            if (!(tuning.ReferenceStageWidth > 0f))
                throw new ArgumentException("tuning.ReferenceStageWidth가 유효하지 않다.", nameof(tuning));

            if (!(tuning.BaseResolution.X > 0f) || !(tuning.BaseResolution.Y > 0f))
                throw new ArgumentException("tuning.BaseResolution이 유효하지 않다.", nameof(tuning));
        }
    }
}
