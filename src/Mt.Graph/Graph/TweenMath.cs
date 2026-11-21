using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>사이 포즈(트윈머신)의 셈 — 한 시각의 앞 키와 뒤 키, 그 사이의 값.</summary>
public static class TweenMath
{
    /// <summary>그 시각보다 앞의 마지막 키와 뒤의 첫 키(그 시각의 키는 세지 않는다). 둘 다 있어야 참.</summary>
    public static bool TryNeighbors(IReadOnlyList<KeyAsset> keys, float time, out KeyAsset? before, out KeyAsset? after)
    {
        ArgumentNullException.ThrowIfNull(keys);
        before = null;
        after = null;

        foreach (KeyAsset key in keys)
        {
            if (key.T < time - MotionRules.Tolerance)
            {
                if (before is null || key.T > before.T)
                {
                    before = key;
                }
            }
            else if (key.T > time + MotionRules.Tolerance && (after is null || key.T < after.T))
            {
                after = key;
            }
        }

        return before is not null && after is not null;
    }

    /// <summary>앞 키와 뒤 키 사이 <paramref name="bias"/>의 값(0 = 앞 · 1 = 뒤 · 밖은 오버슈트). 앞이나 뒤에 키가 없으면 거짓.</summary>
    public static bool TryValue(IReadOnlyList<KeyAsset> keys, float time, double bias, out float value, out KeyAsset? before)
    {
        value = 0f;

        if (!TryNeighbors(keys, time, out before, out KeyAsset? after))
        {
            return false;
        }

        value = (float)(before!.V + (after!.V - before.V) * bias);
        return true;
    }
}
