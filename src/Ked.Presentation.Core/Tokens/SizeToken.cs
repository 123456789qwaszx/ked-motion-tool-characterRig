using System;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// size 커맨드의 토큰 규약(docs/PLAN_rig-layers.md L6).
    ///
    ///   size 슬롯 14            레벨 — level 커브의 Y와 배율
    ///   size 슬롯 front         라벨 — 커브 위의 눈금(DepthLevelLabels)
    ///   size 슬롯 14 keep_y     배율만 놓고 Y는 지금 값 그대로
    ///   size 슬롯 x1.04         배율을 직접(절대값). 레벨이 없어 Y의 근거가 없으므로 Y는 건드리지 않는다
    ///
    /// 폴드와 런타임 파서가 같은 함수로 읽는다 — 두 곳이 다르게 읽으면 재생과 정지 프레임이 갈라진다.
    /// </summary>
    public static class SizeToken
    {
        /// <summary>셋째 칸의 낱말 — Y를 지금 값 그대로 둔다.</summary>
        public const string KeepY = "keep_y";

        /// <summary>배율을 직접 적는 머리 글자 — "x1.04".</summary>
        public const char ScalePrefix = 'x';

        public static bool IsKeepY(string token)
            => string.Equals((token ?? "").Trim(), KeepY, StringComparison.OrdinalIgnoreCase);

        /// <summary>"x1.04" → 1.04. 머리 글자가 없거나 수가 아니거나 0 이하면 false.</summary>
        public static bool TryParseScale(string token, out float scale)
        {
            scale = 0f;

            string s = (token ?? "").Trim();

            if (s.Length < 2 || char.ToLowerInvariant(s[0]) != ScalePrefix)
                return false;

            if (!float.TryParse(s.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return false;

            if (float.IsNaN(value) || float.IsInfinity(value) || !(value > 0f))
                return false;

            scale = value;
            return true;
        }
    }
}
