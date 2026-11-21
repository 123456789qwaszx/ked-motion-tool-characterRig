using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 키를 고치는 명령의 속 — 모션 하나를 받아 "무엇을 바꿀지"를 돌려준다(<see cref="IGraphEditor"/>의 명령들).
/// 확인(가리킨 채널 · 키가 있는가)은 여기서 먼저 하고, 바꾸는 일은 돌려준 것을 부를 때 일어난다 —
/// 편집기는 그것을 제 되돌리기 한 칸으로 감싼다. 바꿀 것이 없으면 null이다.
/// 저작본의 편집기(<c>MotionEditor</c>)와 다른 툴의 편집기가 같은 속을 쓴다.
/// </summary>
public static class MotionKeyEdits
{
    /// <summary>키의 사본.</summary>
    public static KeyAsset Clone(KeyAsset key) => new()
    {
        T = key.T,
        V = key.V,
        InTangent = key.InTangent,
        OutTangent = key.OutTangent,
        Tangent = key.Tangent,
        Broken = key.Broken,
    };

    /// <summary>키 목록을 성분 하나로 — 사본을 시각의 차례로 세우고 탄젠트를 다시 계산한다. 비었으면 null("성분 없음").</summary>
    public static ComponentCurve? Normalize(IEnumerable<KeyAsset> keys)
    {
        List<KeyAsset> sorted = keys.Select(Clone).OrderBy(k => k.T).ToList();

        if (sorted.Count == 0)
        {
            return null;
        }

        TangentMath.Recompute(sorted);
        return new ComponentCurve { Keys = sorted };
    }

    /// <summary>
    /// 여러 성분의 키 목록을 한 번에 바꾼다. 빈 목록은 "성분 없음". 가리킨 채널이 없으면 아무것도 바꾸지 않고 예외다.
    /// </summary>
    public static Action? SetKeysMany(MotionAsset motion, IEnumerable<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> edits)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(edits);
        var plan = new List<(ChannelAsset Channel, Axis Axis, ComponentCurve? Curve)>();

        foreach ((CurveRef curveRef, IEnumerable<KeyAsset> keys) in edits)
        {
            ChannelAsset channel = motion.FindChannel(curveRef.Node, curveRef.Kind)
                ?? throw new KeyNotFoundException($"모션 '{motion.Name}'에 채널 {curveRef.Node}/{curveRef.Kind}가 없다.");
            plan.Add((channel, curveRef.Axis, Normalize(keys)));
        }

        if (plan.Count == 0)
        {
            return null;
        }

        return () =>
        {
            foreach ((ChannelAsset channel, Axis axis, ComponentCurve? curve) in plan)
            {
                channel.Set(axis, curve);
            }
        };
    }

    /// <summary>키들의 탄젠트 모드를 바꾼다. free가 아니면 탄젠트가 바로 다시 계산된다.</summary>
    public static Action? SetTangentMode(MotionAsset motion, IEnumerable<KeyRef> keys, TangentMode mode)
    {
        List<(ComponentCurve Curve, int Index)> targets = ResolveKeys(motion, keys);

        if (targets.Count == 0)
        {
            return null;
        }

        return () =>
        {
            foreach ((ComponentCurve curve, int index) in targets)
            {
                curve.Keys[index].Tangent = mode;
            }

            foreach (ComponentCurve curve in targets.Select(t => t.Curve).Distinct())
            {
                TangentMath.Recompute(curve.Keys);
            }
        };
    }

    /// <summary>in·out 탄젠트를 따로 놀게(끊기) 하거나 다시 붙인다. 붙이면 out이 in을 따른다. 모드는 free가 된다.</summary>
    public static Action? SetBroken(MotionAsset motion, IEnumerable<KeyRef> keys, bool broken)
    {
        List<(ComponentCurve Curve, int Index)> targets = ResolveKeys(motion, keys);

        if (targets.Count == 0)
        {
            return null;
        }

        return () =>
        {
            foreach ((ComponentCurve curve, int index) in targets)
            {
                KeyAsset key = curve.Keys[index];
                key.Tangent = TangentMode.Free;
                key.Broken = broken;

                if (!broken)
                {
                    key.OutTangent = key.InTangent;
                }
            }
        };
    }

    /// <summary>핸들을 끈 결과. 모드는 free가 된다. 끊기지 않은 키는 양쪽이 같이 돈다 — 호출부가 같은 값을 넣는다.</summary>
    public static Action SetTangents(MotionAsset motion, KeyRef key, float inTangent, float outTangent)
    {
        ArgumentNullException.ThrowIfNull(motion);
        KeyAsset target = key.Resolve(motion) ?? throw new KeyNotFoundException($"키 {key}가 없다.");

        return () =>
        {
            target.Tangent = TangentMode.Free;
            target.InTangent = inTangent;
            target.OutTangent = outTangent;
            target.Broken = inTangent != outTangent;
        };
    }

    /// <summary>
    /// 클립보드를 그 시각에 붙인다(merge — 같은 시각의 키는 바뀐다). 채널·성분이 없으면 만든다.
    /// 붙은 키는 바꾸는 일이 일어날 때 <paramref name="pasted"/>에 담긴다.
    /// targets를 주면 복사한 커브 대신 그 커브들에 붙인다(마야 paste to selected channels): 복사한 커브가 하나면 모든 대상에,
    /// 여럿이면 순서대로 짝지어(남는 쪽은 버린다). 그래서 scale X의 키를 복사해 scale Y에 붙일 수 있다.
    /// </summary>
    public static Action? PasteKeys(
        MotionAsset motion, KeyClipboard clipboard, float atTime, Func<float, float>? snap, IReadOnlyList<CurveRef>? targets, List<KeyRef> pasted)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(pasted);

        if (clipboard.Count == 0)
        {
            return null;
        }

        float shift = atTime - clipboard.Origin;

        List<(CurveRef Curve, IReadOnlyList<KeyAsset> Keys)> items = targets is { Count: > 0 }
            ? clipboard.Items.Count == 1
                ? targets.Select(t => (t, clipboard.Items[0].Keys)).ToList()
                : clipboard.Items.Zip(targets, (item, target) => (target, item.Keys)).ToList()
            : clipboard.Items.ToList();

        return () =>
        {
            foreach ((CurveRef curveRef, IReadOnlyList<KeyAsset> keys) in items)
            {
                ChannelAsset? channel = motion.FindChannel(curveRef.Node, curveRef.Kind);

                if (channel is null)
                {
                    channel = new ChannelAsset { Node = curveRef.Node, Kind = curveRef.Kind };
                    motion.Channels.Add(channel);
                }

                ComponentCurve curve = channel.Get(curveRef.Axis) ?? new ComponentCurve();
                var pastedTimes = new List<float>();

                foreach (KeyAsset key in keys)
                {
                    float t = Math.Max(0f, snap is null ? key.T + shift : snap(key.T + shift));
                    curve.Keys.RemoveAll(k => Math.Abs(k.T - t) <= MotionRules.Tolerance);
                    curve.Keys.Add(new KeyAsset
                    {
                        T = t, V = key.V, InTangent = key.InTangent, OutTangent = key.OutTangent, Tangent = key.Tangent, Broken = key.Broken,
                    });
                    pastedTimes.Add(t);
                }

                curve.Keys.Sort((a, b) => a.T.CompareTo(b.T));
                TangentMath.Recompute(curve.Keys);
                channel.Set(curveRef.Axis, curve);

                foreach (float t in pastedTimes)
                {
                    int index = curve.Keys.FindIndex(k => Math.Abs(k.T - t) <= MotionRules.Tolerance);

                    if (index >= 0)
                    {
                        pasted.Add(new KeyRef(curveRef, index));
                    }
                }
            }
        };
    }

    /// <summary>그 키들을 뺀 뒤의 키 목록 — 커브마다. <see cref="SetKeysMany"/>에 넘긴다. 뺄 것이 없으면 빈 목록.</summary>
    public static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> WithoutKeys(MotionAsset motion, IEnumerable<KeyRef> keys)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(keys);
        var byCurve = new Dictionary<CurveRef, List<int>>();

        foreach (KeyRef key in keys)
        {
            if (key.Resolve(motion) is null)
            {
                continue;
            }

            if (!byCurve.TryGetValue(key.Curve, out List<int>? indices))
            {
                indices = new List<int>();
                byCurve[key.Curve] = indices;
            }

            indices.Add(key.Index);
        }

        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();

        foreach ((CurveRef curveRef, List<int> indices) in byCurve)
        {
            ComponentCurve curve = curveRef.Resolve(motion)!;
            var remaining = new HashSet<int>(indices);
            edits.Add((curveRef, curve.Keys.Where((_, i) => !remaining.Contains(i)).ToList()));
        }

        return edits;
    }

    private static List<(ComponentCurve Curve, int Index)> ResolveKeys(MotionAsset motion, IEnumerable<KeyRef> keys)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(keys);
        var targets = new List<(ComponentCurve Curve, int Index)>();

        foreach (KeyRef key in keys.Distinct())
        {
            ComponentCurve? curve = key.Curve.Resolve(motion);

            if (curve is not null && key.Index >= 0 && key.Index < curve.Keys.Count)
            {
                targets.Add((curve, key.Index));
            }
        }

        return targets;
    }
}
