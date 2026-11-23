namespace Ked.Presentation.Core
{
    /// <summary>
    /// 역할 앵커 · fade — 인물이 서는 자리와 가시성.
    ///
    /// 축 리셋 + 역할 앵커(ApplyRoleAnchor)는 cast가 쓴다 — 런타임 파서가 cast를 SetAnchor까지 팬아웃한다.
    /// 리셋 목록은 리그 스키마 지식이라 목표 계산이 아니라 여기가 갖는다.
    /// </summary>
    public static partial class StageReducer
    {
        // SetAnchorCommandCharR의 리셋 목록(두 플래그 모두 켜진 경로 — cast가 쓴다). 순서도 런타임과 같다.
        // 슬롯의 움직임(Move_X · _Y)은 본래 자리와 함께 되돌린다(docs/PLAN_move-axes.md M7).
        private static readonly string[] SetAnchorResetPositionIds =
        {
            "CharSlot_Track", "CharSlot_Move_X", "CharSlot_Move_Y",
            "CharacterPortrait_Track", "CharacterPortrait_Track_Move",
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] SetAnchorResetEulerIds =
        {
            // CharSlot_SwayPivot은 목록에 없다 — rotate_by의 표적인데 cast가 되돌리지
            // 않는 것이 런타임 실동작이다(StagingBundleTargetTests가 고정).
            "CharSlot_Rotation",
            "CharacterPortrait_Rotation", "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
        };

        private static readonly string[] SetAnchorResetScaleIds =
        {
            // 포커스(CharFocus_*)와 깊이(CharacterPortrait_Depth*)는 목록에 없다 — cast는 그 둘을 건드리지 않는다.
            "CharacterPortrait_SwayPivot", "CharacterPortrait_Shake",
            "CharacterPortrait_ActingScale", "CharacterPortrait_Mirror",
        };

        /// <summary>
        /// 축 리셋 + 역할 앵커 — 런타임 SetAnchorCommandCharR(두 리셋 플래그 모두 켜짐) 그대로.
        /// cast가 쓴다: 런타임 파서가 cast를 배역 → pose → face → SetAnchor로 팬아웃한다
        /// (StageCommandParser.CharRigSetup.EnqueueCastCharacterSpec).
        /// </summary>
        private static void ApplyRoleAnchor(
            StageState state, in StageCommand cmd, string slotKey, StageReducerTuning tuning)
        {
            // 역할 앵커: cast된 캐릭터의 튜닝. 배역이 없거나 엔트리가 없으면 Default —
            // 그게 데이터의 의미다. 튜닝 파일 자체가 없으면 침묵하지 않는다.
            NodeTargets.RoleAnchorTuning anchorTuning = NodeTargets.RoleAnchorTuning.Default;

            if (tuning.RoleAnchors == null)
            {
                state.AddUnhandled(cmd, "role-anchor 튜닝이 tuning에 없다 — 앵커를 기본값으로 접었다");
            }
            else if (state.TryGetCharacter(slotKey, out string characterKey))
            {
                tuning.RoleAnchors.TryGet(characterKey, out anchorTuning);
            }

            NodeTarget[] targets = NodeTargets.SetAnchor(
                StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset"),
                anchorTuning,
                Prefixed(slotKey, SetAnchorResetPositionIds),
                Prefixed(slotKey, SetAnchorResetEulerIds),
                Prefixed(slotKey, SetAnchorResetScaleIds));

            foreach (NodeTarget target in targets)
                state.Apply(target);
        }

        private static bool ApplyFade(StageState state, in StageCommand cmd, bool visible, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string nodeKey = StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root");

            state.Apply(visible
                ? NodeTargets.FadeIn(nodeKey)
                : NodeTargets.FadeOut(nodeKey));

            return true;
        }

        /// <summary>fade(slot, alpha, dur) — 같은 표적, 목표 alpha만 인자에서. 생략값은 파서 표와 같은 1이다.</summary>
        private static bool ApplyFadeTo(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1, "1"), out float alpha))
            {
                reason = $"목표 alpha를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            state.Apply(NodeTargets.Fade(StageState.NodeKeyOf(slotKey, "CharacterPortraitSprite_Root"), alpha));

            return true;
        }
    }
}
