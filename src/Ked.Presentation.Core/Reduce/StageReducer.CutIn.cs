namespace Ked.Presentation.Core
{
    /// <summary>
    /// cutin_open · cutin_close — 덮는 무대의 컷인 끝 상태(docs/PLAN_cutin.md §6).
    ///
    /// 런타임(CutInOpenCommand · CutInCloseCommand)과 같은 규칙으로 접는다:
    ///   · 본 무대(stage00)에는 쓰지 않는다 — 런타임은 경고하고 건너뛰고, 여기서는 Unhandled로 소리를 낸다.
    ///   · 열지 않은 무대를 닫으면 런타임은 경고하고 건너뛴다 — 여기서도 Unhandled.
    ///   · 창 안의 리그는 그대로 접힌다 — 창은 무대 루트 바깥이라 리그 로컬 값이 바뀌지 않는다.
    /// 마스크(stage_mask · stage_mask_reset)는 컷인을 건드리지 않는다. 폴드는 마스크를 모른다(target-boundary) — 그 축은 아직 없다.
    /// </summary>
    public static partial class StageReducer
    {
        private const string DefaultCutInStage = "stage01";

        private static bool ApplyCutInOpen(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetCutInStage(cmd, out string stageKey, out reason))
                return false;

            string panelKey = cmd.Arg(1);

            if (string.IsNullOrWhiteSpace(panelKey))
            {
                reason = "판 키 인자가 없다";
                return false;
            }

            if (!UnitToken.TryParseSignedPixels(cmd.Arg(2, "0u"), tuning.ReferenceStageWidth, out float x) ||
                !UnitToken.TryParseSignedPixels(cmd.Arg(3, "0u"), tuning.ReferenceStageWidth, out float y))
            {
                reason = $"자리 인자가 단위 토큰이 아니다 ('{cmd.Arg(2)}', '{cmd.Arg(3)}')";
                return false;
            }

            if (!NumberToken.TryParseFloat(cmd.Arg(4, "1"), out float scale) || !(scale > 0f))
            {
                reason = $"배율은 0보다 큰 수여야 한다 ('{cmd.Arg(4)}')";
                return false;
            }

            state.SetCutIn(stageKey, new CutInState(true, panelKey.Trim(), new Vec2(x, y), scale));
            reason = null;
            return true;
        }

        private static bool ApplyCutInClose(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetCutInStage(cmd, out string stageKey, out reason))
                return false;

            if (!state.TryGetCutIn(stageKey, out CutInState cutIn) || !cutIn.IsOpen)
            {
                reason = $"{stageKey}에 열린 컷인이 없다";
                return false;
            }

            state.SetCutIn(stageKey, cutIn.Closed());
            reason = null;
            return true;
        }

        private static bool TryGetCutInStage(in StageCommand cmd, out string stageKey, out string reason)
        {
            string raw = (cmd.Arg(0, DefaultCutInStage) ?? "").Trim().ToLowerInvariant();
            stageKey = raw.Length == 0 ? DefaultCutInStage : raw;

            if (StageRoles.Of(stageKey) == StageRole.Main)
            {
                reason = $"본 무대({stageKey})에는 컷인을 열고 닫을 수 없다 — 덮는 무대에 쓴다";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
