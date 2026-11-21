namespace Ked.Presentation.Core
{
    /// <summary>
    /// 리그의 세 덩어리가 받는 자리 · 크기(docs/PLAN_rig-layers.md §4). 전부 절대값이고 무대를 재지 않는다 —
    /// 결과가 인자에 적혀 있다. 여기는 토큰 해석과 배선만 맡는다.
    ///
    ///   포커스  focus_move · focus_rotate · focus_scale · focus_reset   CharFocus_Track · _Rotation · _Scale
    ///   슬롯    place                                                   CharSlot_Track (move_by · slide와 같은 노드)
    ///   초상    size · size_reset                                       CharacterPortrait_DepthOffset · _DepthScale
    /// </summary>
    public static partial class StageReducer
    {
        // ── 슬롯 ──────────────────────────────────────────────

        private static bool ApplyPlace(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!TryReadPosition(cmd, tuning, out Vec2 position, out reason))
                return false;

            ApplyMoveTarget(state, slotKey, NodeTargets.SlotTrackNodeId, relative: false, position);
            return true;
        }

        // ── 포커스 ────────────────────────────────────────────

        private static bool ApplyFocusMove(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!TryReadPosition(cmd, tuning, out Vec2 position, out reason))
                return false;

            ApplyMoveTarget(state, slotKey, NodeTargets.FocusTrackNodeId, relative: false, position);
            return true;
        }

        private static bool ApplyFocusRotate(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1, "0"), out float degree))
            {
                reason = $"각도를 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            ApplyEulerTarget(state, slotKey, NodeTargets.FocusRotationNodeId, new Vec3(0f, 0f, degree));
            return true;
        }

        private static bool ApplyFocusScale(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NumberToken.TryParseFloat(cmd.Arg(1, "1"), out float scale))
            {
                reason = $"배율을 읽지 못했다: '{cmd.Arg(1)}'";
                return false;
            }

            ApplyScaleTarget(state, slotKey, NodeTargets.FocusScaleNodeId, new Vec2(scale, scale));
            return true;
        }

        // 셋을 함께 걷는다 — 대화가 끝나면 본래 자리로 돌아간다.
        private static bool ApplyFocusReset(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            ApplyMoveTarget(state, slotKey, NodeTargets.FocusTrackNodeId, relative: false, Vec2.Zero);
            ApplyEulerTarget(state, slotKey, NodeTargets.FocusRotationNodeId, Vec3.Zero);
            ApplyScaleTarget(state, slotKey, NodeTargets.FocusScaleNodeId, Vec2.One);
            return true;
        }

        // ── 초상의 깊이 ───────────────────────────────────────

        private static bool ApplySize(
            StageState state, in StageCommand cmd, StageReducerTuning tuning,
            string depthToken, bool keepY, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NodeTargets.TrySetDepth(
                    slotKey, depthToken, keepY, tuning.DepthLevel,
                    out bool movesY, out NodeTarget depthOffsetTarget, out NodeTarget depthScaleTarget,
                    out reason))
            {
                return false;
            }

            if (movesY)
                state.Apply(depthOffsetTarget);

            state.Apply(depthScaleTarget);
            return true;
        }

        // ── 공용 ──────────────────────────────────────────────

        // x · y(단위 토큰). 생략하면 0u — move_by와 같은 생략값이다.
        private static bool TryReadPosition(
            in StageCommand cmd, StageReducerTuning tuning, out Vec2 position, out string reason)
        {
            position = Vec2.Zero;

            if (!UnitToken.TryParseSignedPixels(cmd.Arg(1, "0u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"거리 토큰을 읽지 못했다: '{cmd.Arg(1)}', '{cmd.Arg(2)}'";
                return false;
            }

            position = new Vec2(x, y);
            reason = null;
            return true;
        }

        private static void ApplyEulerTarget(StageState state, string slotKey, string nodeId, Vec3 euler)
        {
            string nodeKey = StageState.NodeKeyOf(slotKey, nodeId);

            state.Apply(NodeTargets.RotateTo(
                nodeKey,
                new NodeTargets.RotateToArgs(false, euler),
                state.Nodes.GetState(nodeKey).LocalEulerAngles));
        }

        private static void ApplyScaleTarget(StageState state, string slotKey, string nodeId, Vec2 scale)
        {
            string nodeKey = StageState.NodeKeyOf(slotKey, nodeId);

            state.Apply(NodeTargets.ScaleTo(
                nodeKey,
                new NodeTargets.ScaleToArgs(false, scale),
                state.Nodes.GetState(nodeKey).LocalScale.XY));
        }
    }
}
