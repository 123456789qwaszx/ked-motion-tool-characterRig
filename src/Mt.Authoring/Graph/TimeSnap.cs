using Ked.Presentation.Core;

namespace Mt.Authoring.Graph;

/// <summary>
/// 시간 스냅. 기본 1프레임(1/30초 — 2026-11-18 24fps에서 30fps로)이고 눈금은 30fps 프레임을 병기한다(<see cref="DurationToken.FramesPerSecond"/>와 같은 규약).
/// 값 스냅은 없다.
/// </summary>
public static class TimeSnap
{
    public const double DefaultStep = 1.0 / 30.0;

    public static double FramesPerSecond => DurationToken.FramesPerSecond;

    public static double Snap(double seconds, double step)
    {
        if (step <= 0)
        {
            return seconds;
        }

        return Math.Round(seconds / step) * step;
    }

    /// <summary>초 → 프레임 수(소수, 30fps).</summary>
    public static double ToFrames(double seconds) => seconds * FramesPerSecond;

    /// <summary>
    /// 눈금 간격(초) — 픽셀 밀도에 맞춰 1·2·5 배수가 아니라 프레임 친화적인 단계에서 고른다:
    /// 1프레임, 2, 3, 5, 10, 15, 30(=1초), 60, 150, 300.
    /// </summary>
    public static double TickStep(double pixelsPerSecond, double minPixelsPerTick = 48)
    {
        double[] frames = { 1, 2, 3, 5, 10, 15, 30, 60, 150, 300, 600, 1500 };

        foreach (double f in frames)
        {
            double seconds = f / FramesPerSecond;

            if (seconds * pixelsPerSecond >= minPixelsPerTick)
            {
                return seconds;
            }
        }

        return frames[^1] / FramesPerSecond;
    }

    /// <summary>값 눈금 간격 — 1·2·5 배수.</summary>
    public static double ValueTickStep(double pixelsPerValue, double minPixelsPerTick = 40)
    {
        double raw = minPixelsPerTick / Math.Max(pixelsPerValue, 1e-9);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));

        foreach (double m in new[] { 1, 2, 5, 10 })
        {
            if (magnitude * m >= raw)
            {
                return magnitude * m;
            }
        }

        return magnitude * 10;
    }
}
