namespace Ked.Presentation.Core
{
    /// <summary>
    /// 리그의 세 덩어리가 받는 자리 · 크기(docs/PLAN_rig-layers.md §4). 전부 절대값이고 무대를 재지 않는다 —
    /// 결과가 인자에 적혀 있다. 여기는 토큰 해석과 배선만 맡는다.
    ///
    ///   포커스  focus_move · focus_rotate · focus_scale · focus_reset   CharFocus_Track · _Rotation · _Scale
    ///   슬롯    place                                                   CharSlot_Track — 본래 자리(절대)
    ///   슬롯    left · right · up · down · move_reset                   CharSlot_Move_X · _Y — 본래 자리 위의 움직임(상대, 쌓인다)
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

        // left · right · up · down — 본래 자리 위의 움직임(docs/PLAN_move-axes.md §4). 노드와 방향은 NodeTargets.TryGetSlotMove.
        // 거리에는 생략값이 없다 — 빠지면 접지 않는다(파서도 내지 않는다). 음수는 반대 방향이다(left -2u = right 2u).
        private static bool ApplySlotMove(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            if (!NodeTargets.TryGetSlotMove(cmd.Name, out string nodeId, out Vec2 direction))
            {
                reason = $"'{cmd.Name}'은 슬롯의 움직임이 아니다";
                return false;
            }

            string distanceToken = cmd.Arg(1);

            if (distanceToken == null)
            {
                reason = "거리가 없다 — 거리는 꼭 적는다";
                return false;
            }

            if (!UnitToken.TryParseSignedPixels(distanceToken, tuning.ReferenceStageWidth, out float distance))
            {
                reason = $"거리 토큰을 읽지 못했다: '{distanceToken}'";
                return false;
            }

            ApplyMoveTarget(state, slotKey, nodeId, relative: true, direction * distance);
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

        // x · y(단위 토큰). 생략하면 0u.
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
