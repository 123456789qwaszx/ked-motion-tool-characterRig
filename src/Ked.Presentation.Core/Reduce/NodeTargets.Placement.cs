using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 화면 지점 프리셋의 rig-space 좌표. 초점을 무대 지점에 놓는 값을 도구가 풀 때 쓴다(NodeTargets.TrySolveFocusPlacement).
    ///
    /// 주의: 이 비율은 게임별 값이므로 원칙적으로는 tuning 데이터가 맞다(target-boundary.md 백로그).
    /// 같은 값을 눈금으로 그리는 곳이 하나 있다(Samples의 ScreenFocusGridOverlay) — 함께 고친다.
    /// </summary>
    public static class ScreenPointRatios
    {
        public const float OuterXRatio = 0.24f;
        public const float OuterYRatio = 0.16f;
        public const float InnerXRatio = 0.14f;
        public const float InnerYRatio = 0.09f;

        /// <summary>지점 이름 → rig-space 좌표. 모르면 false.</summary>
        public static bool TryResolve(Vec2 frameSize, string pointName, out Vec2 point)
        {
            float ox = frameSize.X * OuterXRatio;
            float oy = frameSize.Y * OuterYRatio;
            float ix = frameSize.X * InnerXRatio;
            float iy = frameSize.Y * InnerYRatio;

            switch ((pointName ?? "").Trim().ToLowerInvariant())
            {
                case "center": point = Vec2.Zero; return true;

                case "left": point = new Vec2(-ox, 0f); return true;
                case "right": point = new Vec2(ox, 0f); return true;
                case "top": point = new Vec2(0f, oy); return true;
                case "bottom": point = new Vec2(0f, -oy); return true;

                case "tl": point = new Vec2(-ox, oy); return true;
                case "tr": point = new Vec2(ox, oy); return true;
                case "bl": point = new Vec2(-ox, -oy); return true;
                case "br": point = new Vec2(ox, -oy); return true;

                case "inner_tl": point = new Vec2(-ix, iy); return true;
                case "inner_tr": point = new Vec2(ix, iy); return true;
                case "inner_bl": point = new Vec2(-ix, -iy); return true;
                case "inner_br": point = new Vec2(ix, -iy); return true;

                default: point = Vec2.Zero; return false;
            }
        }
    }

    /// <summary>
    /// focus 로컬 오프셋 해석 — 런타임 CharacterFocusTuningDBSO.ResolveOffset과 같은 합:
    ///   base[preset] + entry(character).defaultOffset + entry.offsets[preset] + 커맨드 오프셋
    ///
    /// 주의: 좌우 반전(mirror)은 폴드에 없다 — 뒤집힌 인물의 초점은 호출부가 오프셋의 x를 뒤집어 넘긴다
    /// (NodeTargets.TrySolveFocusPlacement의 오프셋을 받는 꼴).
    /// </summary>
    public static class FocusOffsetMath
    {
        public static Vec2 Resolve(
            FocusTuningBodyDto tuning,
            string characterKey,
            string presetName,
            Vec2 commandOffset)
        {
            Vec2 offset = Vec2.Zero;

            if (tuning?.baseOffsets != null && tuning.baseOffsets.TryGet(presetName, out Vec2 baseOffset))
                offset += baseOffset;

            if (tuning != null && characterKey != null && tuning.TryGetEntry(characterKey, out FocusEntryDto entry))
            {
                if (entry.defaultOffset != null)
                    offset += entry.defaultOffset.ToVec2();

                if (entry.offsets != null && entry.offsets.TryGet(presetName, out Vec2 presetOffset))
                    offset += presetOffset;
            }

            return offset + commandOffset;
        }
    }

    public static partial class NodeTargets
    {
        // ── 리그의 세 덩어리가 받는 커맨드의 표적 노드 (docs/PLAN_rig-layers.md §3 · §4) ──

        /// <summary>포커스 — 대화할 때만 서는 자리. focus_move · focus_rotate · focus_scale이 절대값으로 놓는다.</summary>
        public const string FocusTrackNodeId = "CharFocus_Track";
        public const string FocusRotationNodeId = "CharFocus_Rotation";
        public const string FocusScaleNodeId = "CharFocus_Scale";

        /// <summary>슬롯 — 무대 위의 본래 자리. place(절대) · move_by(상대) · slide가 쓴다.</summary>
        public const string SlotTrackNodeId = "CharSlot_Track";

        /// <summary>초상의 깊이 — 그 사람의 크기. size가 놓는다.</summary>
        public const string DepthOffsetNodeId = "CharacterPortrait_DepthOffset";
        public const string DepthScaleNodeId = "CharacterPortrait_DepthScale";

        /// <summary>초점(가슴 · 얼굴 점)을 재는 노드 — 깊이 아래다. 크기를 바꾸면 초점도 따라온다.</summary>
        public const string FocusMeasureNodeId = "CharacterPortrait_VisualOffset";

        // ── 초점을 무대 지점에 놓는 값 — 도구의 보조 계산 ───────────────────
        //
        // 커맨드가 아니다(docs/PLAN_rig-layers.md L11). 대본에는 풀린 값이 적힌다 — place 슬롯 x y(본래 자리) 이거나
        // focus_move 슬롯 x y(대화할 때만 서는 자리). 그 값을 내는 순수 함수가 이것이고, 부르는 쪽은 저작 도구다.
        // 무대 지점은 카메라가 중립일 때 그 화면 지점과 겹치는 무대 위 자리다. 이 노드 트리에는 카메라 ·
        // 깊이 층 프레이밍 노드가 없다 — 그것이 곧 카메라 중립이다.

        /// <param name="moveNodeId">값을 풀 이동 노드 — SlotTrackNodeId(place) 또는 FocusTrackNodeId(focus_move).</param>
        public static bool TrySolveFocusPlacement(
            StageState state,
            string slotKey,
            string characterKey,
            string focusPresetName,
            string screenPointName,
            FocusTuningBodyDto focusTuning,
            string moveNodeId,
            out Vec2 anchoredPosition,
            out string reason)
        {
            anchoredPosition = Vec2.Zero;

            if (!ScreenPointRatios.TryResolve(state.Nodes.RootSpace.Size, screenPointName, out Vec2 desired))
            {
                reason = $"화면 지점 '{screenPointName}'을 모른다";
                return false;
            }

            Vec2 focusOffset = FocusOffsetMath.Resolve(focusTuning, characterKey, focusPresetName, Vec2.Zero);

            return TrySolveFocusPlacement(state, slotKey, focusOffset, desired, moveNodeId, out anchoredPosition, out reason);
        }

        /// <summary>초점의 로컬 오프셋과 무대 지점을 값으로 받는 꼴 — 좌우 반전처럼 호출부가 오프셋을 손본 뒤 부를 때.</summary>
        public static bool TrySolveFocusPlacement(
            StageState state,
            string slotKey,
            Vec2 focusLocalOffset,
            Vec2 desiredInRigSpace,
            string moveNodeId,
            out Vec2 anchoredPosition,
            out string reason)
        {
            anchoredPosition = Vec2.Zero;

            string measureKey = StageState.NodeKeyOf(slotKey, FocusMeasureNodeId);
            string moveKey = StageState.NodeKeyOf(slotKey, moveNodeId);

            List<string> chainKeys = new();
            RectNodeState[] chain = state.Nodes.BuildChainTo(measureKey, chainKeys);

            int moveIndex = chainKeys.IndexOf(moveKey);

            if (moveIndex < 0)
            {
                reason = $"이동 축 '{moveKey}'가 측정 체인에 없다";
                return false;
            }

            anchoredPosition = SettledFocusMath.SolveFocusPlacement(
                chain,
                state.Nodes.RootSpace,
                moveIndex,
                focusLocalOffset,
                desiredInRigSpace,
                state.Nodes.GetState(moveKey).AnchoredPosition);

            reason = null;
            return true;
        }

        // ── size (depth) ────────────────────────────────────────
        //
        // size — 그 사람의 크기. 깊이의 Y(DepthOffset)와 배율(DepthScale)을 놓는다. 무대를 재지 않는다 —
        // 커브가 낸 값(또는 적힌 배율)이 곧 목표다. 깊이는 초상만 곱한다: 슬롯의 자리(place · move_by)는 그대로다.

        /// <param name="depthToken">레벨(숫자) · 라벨 · x배율(SizeToken).</param>
        /// <param name="keepY">true면 Y를 지금 값 그대로 둔다(타깃을 내지 않는다). x배율은 늘 Y를 건드리지 않는다.</param>
        /// <param name="movesY">Y 타깃을 냈는가 — false면 depthOffsetTarget은 쓰지 않는다.</param>
        public static bool TrySetDepth(
            string slotKey,
            string depthToken,
            bool keepY,
            DepthLevelTuningDto depthLevel,
            out bool movesY,
            out NodeTarget depthOffsetTarget,
            out NodeTarget depthScaleTarget,
            out string reason)
        {
            movesY = false;
            depthOffsetTarget = default;
            depthScaleTarget = default;

            string scaleKey = StageState.NodeKeyOf(slotKey, DepthScaleNodeId);

            // 배율을 직접 — 레벨이 없어 Y의 근거가 없다. 배율만 놓는다.
            if (SizeToken.TryParseScale(depthToken, out float scale))
            {
                depthScaleTarget = NodeTarget.LocalScaleXY(scaleKey, new Vec2(scale, scale));
                reason = null;
                return true;
            }

            // 깊이의 진실은 level 커브 하나다. 토큰은 숫자이거나 라벨이고,
            // 라벨은 커브 위에서 쓸 수치를 고르는 이름표일 뿐이다(DepthLevelLabels).
            // 그래서 재생·정지 프레임·툴 프리뷰가 같은 커브 한 장을 지난다.
            if (!NumberToken.TryParseFloat(depthToken, out float level) &&
                !DepthLevelLabels.TryGetLevel(depthToken, out level))
            {
                reason = $"depth 토큰 '{depthToken}'을 모른다 (숫자 레벨도, 라벨도, x배율도 아니다)";
                return false;
            }

            if (depthLevel == null ||
                !depthLevel.TryResolve(level, out float levelY, out float levelScale))
            {
                reason = $"depth 레벨 {level}을 풀 커브가 tuning에 없다";
                return false;
            }

            if (!keepY)
            {
                movesY = true;
                depthOffsetTarget = NodeTarget.AnchoredPosition(
                    StageState.NodeKeyOf(slotKey, DepthOffsetNodeId), new Vec2(0f, levelY));
            }

            depthScaleTarget = NodeTarget.LocalScaleXY(scaleKey, new Vec2(levelScale, levelScale));
            reason = null;
            return true;
        }
    }
}
