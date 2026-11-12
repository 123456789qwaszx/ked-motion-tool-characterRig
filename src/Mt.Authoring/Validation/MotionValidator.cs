using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Schema;

namespace Mt.Authoring.Validation;

/// <summary>
/// 검증 문제 하나. 모션 이름이 비어 있으면 문서 수준의 문제다.
/// <see cref="Blocking"/>이 false면 **경고**다 — 내보내기를 막지 않는다(규칙이 아니라 "튄다"는 알림).
/// </summary>
public sealed record MotionProblem(string MotionName, string Message, bool Blocking = true)
{
    public override string ToString()
        => string.IsNullOrEmpty(MotionName) ? Message : $"'{MotionName}': {Message}";
}

/// <summary>
/// 편집 중 계속 돌리는 검증. 규칙은 코어 <see cref="MotionRules"/> 한 벌뿐이다 — 런타임 로더와 Unity 내보내기가
/// 같은 규칙을 쓰므로 "툴에서는 되는데 재생에서 사라지는" 모션이 안 생긴다. 문구도 코어의 것을 그대로 보인다.
///
/// 스키마에 없는 리그 · 노드는 스키마 계약(<see cref="SchemaContract"/>)이 센다. 툴에만 있는 것(불투명도 · 그림 채널 · 계단 키 — <see cref="EmojiOnlyIn"/>)은
/// 여기서 막는다. 그 밖에 더 보는 것은 문서 수준(같은 이름 둘)과 **경고** 하나다:
/// restore인데 마지막 키가 중립값이 아니면 재생이 끝에서 시작값을 되써 튄다 — 규칙(motions/2)은 막지 않으므로
/// 경고로만 보이고 내보내기는 된다. 첫 키가 중립값이 아닌 것은 알리지 않는다 — "지금 값 +140에서 시작"은 뜻이다.
/// </summary>
public static class MotionValidator
{
    /// <summary>컨트롤러 경고의 문구 머리 — 돕는 손이 이것으로 고른다.</summary>
    public const string AbsoluteControllerWarning = "absolute로 재생하는 컨트롤러";

    /// <param name="rigNodeIds">rigKind → 노드 id 목록. null이면 스키마 계약을 생략한다(덤프가 아직 없는 자리).</param>
    /// <param name="controllers">컨트롤러 표. 주면 자리·깊이(absolute) 컨트롤러가 든 relative 미리보기 모션에 경고한다.</param>
    public static IReadOnlyList<MotionProblem> Validate(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null,
        ControllersFile? controllers = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<MotionProblem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (MotionAsset motion in document.Motions)
        {
            if (!string.IsNullOrEmpty(motion.Name) && !seen.Add(motion.Name))
            {
                problems.Add(new MotionProblem(motion.Name, "같은 이름이 둘이다"));
            }

            problems.AddRange(ValidateMotion(motion, rigNodeIds, controllers));
        }

        return problems;
    }

    public static IReadOnlyList<MotionProblem> ValidateMotion(
        MotionAsset motion,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null,
        ControllersFile? controllers = null)
    {
        ArgumentNullException.ThrowIfNull(motion);

        var problems = new List<MotionProblem>();

        // 리그와 노드가 스키마에 있는가 — 스키마 계약 한 곳이 센다.
        if (rigNodeIds is not null)
        {
            problems.AddRange(SchemaContract.Motion(rigNodeIds, motion).Select(v => new MotionProblem(motion.Name, v.Message, v.Blocking)));
        }

        // 불투명도 · 그림 채널과 계단 키는 툴의 것이다(이모지의 타임라인 — PLAN-emoji.md §3). motions/2에는 자리가 없다.
        problems.AddRange(EmojiOnlyIn(motion).Select(message => new MotionProblem(motion.Name, message)));

        MotionClip clip = MotionAssetCodec.ToClip(motion, skipEmojiOnly: true);

        problems.AddRange(ShapeProblems(clip).Select(reason => new MotionProblem(motion.Name, reason)));

        // 경고는 모양 규칙과 무관하게 본다 — 고치는 순서를 작가가 고른다.

        if (motion.End == MotionEnd.Restore)
        {
            for (int i = 0; i < motion.Channels.Count; i++)
            {
                ChannelAsset channel = motion.Channels[i];
                float neutral = ChannelKinds.Neutral(channel.Kind);

                foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
                {
                    if (channel.Get(axis) is { Keys.Count: > 0 } curve && Math.Abs(curve.Keys[^1].V - neutral) > MotionRules.Tolerance)
                    {
                        problems.Add(new MotionProblem(
                            motion.Name,
                            $"채널 {i}: {axis}: restore인데 마지막 키 값이 중립값 {neutral}이 아니다 ({curve.Keys[^1].V}) — 끝에서 시작값으로 튄다. 남기려면 commit",
                            Blocking: false));
                    }
                }
            }
        }

        // 자리·깊이 컨트롤러는 absolute로 재생한다(결정 D1·그룹 mode). relative 미리보기면 자리가 누적돼 보인다 — 경고.
        if (motion.PreviewMode == MotionChannelMode.Relative && controllers?.Rig(motion.Rig) is { } table)
        {
            List<string> absolute = AbsoluteControllersIn(motion, table).Select(c => $"{c.Group.Label} · {c.Label}").Distinct().ToList();

            if (absolute.Count > 0)
            {
                problems.Add(new MotionProblem(
                    motion.Name,
                    $"{AbsoluteControllerWarning}({string.Join(", ", absolute)})가 있다 — 재생 기준이 상대값(relative)이면 자리가 누적돼 보인다. 재생은 <<motion slot {motion.Name} absolute>>로. 연기와 섞였으면 두 모션으로 나눈다",
                    Blocking: false));
            }
        }

        return problems;
    }

    /// <summary>
    /// 캐릭터 모션(<c>motions/2</c>)이 받지 않는 것 — 이모지의 타임라인에만 쓰는 종류(불투명도 · 그림)와 계단 키.
    /// 채널마다 한 줄. 코어 규칙(<see cref="MotionRules"/>)은 이것을 모르므로 여기서 센다.
    /// </summary>
    public static IEnumerable<string> EmojiOnlyIn(MotionAsset motion)
    {
        ArgumentNullException.ThrowIfNull(motion);

        for (int i = 0; i < motion.Channels.Count; i++)
        {
            ChannelAsset channel = motion.Channels[i];
            string where = $"채널 {i}({channel.Node} {ChannelKindLabel.Of(channel.Kind)})";

            if (ChannelKinds.IsEmojiOnly(channel.Kind))
            {
                yield return $"{where}: {ChannelKindLabel.Of(channel.Kind)}은 이모지의 타임라인에만 쓴다 — motions/2는 받지 않는다";
                continue;
            }

            if (new[] { Axis.X, Axis.Y, Axis.Z }.Any(axis => channel.Get(axis)?.Keys.Any(k => k.Tangent == TangentMode.Step) == true))
            {
                yield return $"{where}: 계단 키는 이모지의 타임라인에만 쓴다 — motions/2는 받지 않는다";
            }
        }
    }

    /// <summary>모션의 채널 중 absolute 묶음(자리·깊이)의 컨트롤러로 풀리는 것들.</summary>
    public static IEnumerable<Controller> AbsoluteControllersIn(MotionAsset motion, ControllerRig table)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(table);

        foreach (ChannelAsset channel in motion.Channels)
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null && table.TryResolve(new CurveRef(channel.Node, channel.Kind, axis), out Controller c, out _) && c.Group.Mode == MotionChannelMode.Absolute)
                {
                    yield return c;
                    break;
                }
            }
        }
    }

    /// <summary>채널이 absolute 묶음의 컨트롤러인가 — 두 모션으로 나눌 때의 기준.</summary>
    public static bool IsAbsoluteController(ChannelAsset channel, ControllerRig table)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(table);

        foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            if (table.TryResolve(new CurveRef(channel.Node, channel.Kind, axis), out Controller c, out _))
            {
                return c.Group.Mode == MotionChannelMode.Absolute;
            }
        }

        return false;
    }

    /// <summary>내보내기를 막는 것만.</summary>
    public static IEnumerable<MotionProblem> Blocking(IEnumerable<MotionProblem> problems)
        => problems.Where(p => p.Blocking);

    /// <summary>
    /// 모양 규칙에 걸린 것 **전부**. 코어의 검사(<see cref="MotionRules.TryValidate"/>)는 처음 걸린 하나에서 멈춘다 —
    /// 파일로 만드는 사람은 고치고 다시 돌리기를 되풀이해야 했다. 규칙을 여기에 베끼지 않고, 코어의 검사를
    /// 조각(머리 · 채널 · 성분)마다 따로 불러 조각마다의 첫 문제를 모은다. 문구는 코어의 것 그대로다.
    /// </summary>
    internal static IEnumerable<string> ShapeProblems(MotionClip clip)
    {
        // 머리 — 이름 · 길이. 걸린 것을 멀쩡한 값으로 바꿔 가며 다음 것을 묻는다(채널은 뺀다).
        var head = new MotionClip { Name = clip.Name, Rig = clip.Rig, Duration = clip.Duration, End = clip.End, Channels = Array.Empty<MotionChannel>() };

        for (int guard = 0; guard < 3 && !MotionRules.TryValidate(head, null, out string reason); guard++)
        {
            yield return reason;

            if (string.IsNullOrEmpty(head.Name) || !MotionRules.NameRule.IsMatch(head.Name))
            {
                head.Name = "a";
            }
            else if (!(head.Duration > 0f))
            {
                head.Duration = 1f;
            }
            else
            {
                break;
            }
        }

        MotionChannel[] channels = clip.Channels ?? Array.Empty<MotionChannel>();
        var seen = new HashSet<(string, MotionChannelKind)>();

        for (int i = 0; i < channels.Length; i++)
        {
            MotionChannel channel = channels[i];
            bool whole = true;

            // 성분마다 따로 — 한 성분의 문제가 다른 성분의 문제를 가리지 않게.
            foreach ((CurveKey[]? x, CurveKey[]? y, CurveKey[]? z) in new[] { (channel.X, (CurveKey[]?)null, (CurveKey[]?)null), (null, channel.Y, null), (null, null, channel.Z) })
            {
                if ((x ?? y ?? z) is not { Length: > 0 })
                {
                    continue;
                }

                var one = new MotionChannel { Node = channel.Node, Kind = channel.Kind, X = x!, Y = y!, Z = z! };

                if (MotionRules.TryValidateChannel(one, clip.Duration, clip.End, null, out string why))
                {
                    continue;
                }

                // 성분의 문제가 아니면(노드 · 종류) 채널에 한 번만 적는다.
                bool ofComponent = why.StartsWith("X:", StringComparison.Ordinal) || why.StartsWith("Y:", StringComparison.Ordinal) || why.StartsWith("Z:", StringComparison.Ordinal);

                if (ofComponent || whole)
                {
                    yield return $"채널 {i}: {why}";
                }

                whole &= ofComponent;

                if (!ofComponent)
                {
                    break;
                }
            }

            // 성분이 하나도 없는 채널 — 통째로 묻는다.
            if (!channel.HasX && !channel.HasY && !channel.HasZ && !MotionRules.TryValidateChannel(channel, clip.Duration, clip.End, null, out string empty))
            {
                yield return $"채널 {i}: {empty}";
            }

            if (!seen.Add((channel.Node, channel.Kind)))
            {
                yield return $"채널 {i}: 같은 노드·종류의 채널이 둘이다 ({channel.Node} {channel.Kind})";
            }
        }
    }
}
