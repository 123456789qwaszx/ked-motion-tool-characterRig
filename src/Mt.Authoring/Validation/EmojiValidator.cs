using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Validation;

/// <summary>이모지 검증 문제 하나. 이모지 이름이 비어 있으면 문서 수준의 문제다. <see cref="Blocking"/>이 false면 경고다.</summary>
public sealed record EmojiProblem(string EmojiName, string Message, bool Blocking = true)
{
    public override string ToString()
        => string.IsNullOrEmpty(EmojiName) ? Message : $"이모지 '{EmojiName}': {Message}";
}

/// <summary>
/// 편집 중 계속 돌리는 이모지 검증(<c>docs/PLAN-emoji.md</c> E3). 막는 문제가 있으면 <c>emojis.json</c>을 내지 않는다.
///
/// 보는 것: 이름 · 리그(이름 · 그림 · 기본 배율) · 타임라인의 채널(대상이 틀의 키 받는 노드인가 · 종류와 성분이 맞는가 · 키가 타임라인 안인가 ·
/// 그림 키가 그 리그의 그림을 가리키는가) · 구간(타임라인 안인가) · 잇기(있는 구간인가 · 등장/퇴장은 머묾이 있을 때만).
/// 런타임이 읽을 때의 규칙(코어 <see cref="EmojiRules"/>)은 내보낼 정의에 한 번 더 건다(<see cref="EmojiExport"/>) — 이 검증은 그 앞에서 툴의 말로 알린다.
/// </summary>
public static class EmojiValidator
{
    /// <summary>실린 리그 덤프로 — 덤프에 이모지 리그가 없거나 모양이 다르면 이모지가 있는 문서에 문서 수준의 문제 하나다.</summary>
    public static IReadOnlyList<EmojiProblem> Validate(MotionDocument document, RigSchemaFile rigs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rigs);

        if (EmojiRigFrame.TryRead(rigs, out EmojiRigFrame frame, out string why))
        {
            return Validate(document, frame);
        }

        return document.Emojis.Count == 0 ? Array.Empty<EmojiProblem>() : new[] { new EmojiProblem("", why) };
    }

    /// <param name="frame">이모지 리그의 틀. null이면 내장 덤프의 것.</param>
    public static IReadOnlyList<EmojiProblem> Validate(MotionDocument document, EmojiRigFrame? frame = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<EmojiProblem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiAsset emoji in document.Emojis)
        {
            if (!string.IsNullOrEmpty(emoji.Name) && !seen.Add(emoji.Name))
            {
                problems.Add(new EmojiProblem(emoji.Name, "같은 이름이 둘이다"));
            }

            problems.AddRange(ValidateEmoji(emoji, frame ?? EmojiRigFrame.Builtin));
        }

        return problems;
    }

    public static IReadOnlyList<EmojiProblem> ValidateEmoji(EmojiAsset emoji, EmojiRigFrame frame)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(frame);
        var problems = new List<EmojiProblem>();
        void Block(string message) => problems.Add(new EmojiProblem(emoji.Name, message));

        if (!MotionNames.IsValid(emoji.Name))
        {
            Block(MotionNames.RuleMessage(emoji.Name));
        }

        if (!float.IsFinite(emoji.OffsetX) || !float.IsFinite(emoji.OffsetY))
        {
            Block("오프셋이 수가 아니다");
        }

        ValidateRigs(emoji, Block);
        ValidateTimeline(emoji, frame, Block);
        ValidateSpans(emoji, Block);
        return problems;
    }

    private static void ValidateRigs(EmojiAsset emoji, Action<string> block)
    {
        if (emoji.Rigs.Count == 0)
        {
            block("리그가 없다 — 그림 하나에 리그 하나, 하나 이상이어야 한다");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiRigAsset rig in emoji.Rigs)
        {
            if (!MotionNames.IsValid(rig.Name))
            {
                block($"리그: {MotionNames.RuleMessage(rig.Name)}");
            }
            else if (!names.Add(rig.Name))
            {
                block($"리그 이름 '{rig.Name}'이 두 번 나온다 — 키가 리그를 이름으로 가리킨다");
            }

            if (rig.Images.Count == 0)
            {
                block($"리그 '{rig.Name}'에 그림이 없다 — 첫 장이 기본 스프라이트다");
            }
            else if (rig.Images.Any(i => string.IsNullOrWhiteSpace(EmojiRigAsset.SpriteKeyOf(i))))
            {
                block($"리그 '{rig.Name}'의 그림에 파일 이름이 없다 — 스프라이트 키는 파일 이름이다");
            }

            if (!float.IsFinite(rig.Scale) || rig.Scale == 0f || !float.IsFinite(rig.Rotation) || !float.IsFinite(rig.PlaceX) || !float.IsFinite(rig.PlaceY))
            {
                block($"리그 '{rig.Name}'의 기본 자리 · 배율 · 회전이 쓸 수 없는 값이다(배율 {rig.Scale})");
            }
        }
    }

    private static void ValidateTimeline(EmojiAsset emoji, EmojiRigFrame frame, Action<string> block)
    {
        MotionAsset timeline = emoji.Timeline;

        if (!float.IsFinite(timeline.Duration) || timeline.Duration < 0f)
        {
            block($"타임라인의 길이가 쓸 수 없는 값이다({timeline.Duration})");
            return;
        }

        var targets = new HashSet<(string Node, ChannelKind Kind)>();

        for (int i = 0; i < timeline.Channels.Count; i++)
        {
            ChannelAsset channel = timeline.Channels[i];
            string where = $"채널 {channel.Node} {ChannelKindLabel.Of(channel.Kind)}";

            if (!targets.Add((channel.Node, channel.Kind)))
            {
                block($"{where}: 같은 대상의 같은 채널이 둘이다");
            }

            if (!frame.TryTarget(channel.Node, out string rigName))
            {
                block($"{where}: 키를 받는 노드가 아니다 — {frame.OffsetNode}(이모지 전체) 또는 {{리그}}{EmojiRigFrame.Separator}{frame.ImageNode}에만 키를 잡는다");
                continue;
            }

            EmojiRigAsset? rig = string.IsNullOrEmpty(rigName) ? null : emoji.FindRig(rigName);

            if (!string.IsNullOrEmpty(rigName) && rig is null)
            {
                block($"{where}: 리그 '{rigName}'이 이 이모지에 없다");
                continue;
            }

            if (channel.Kind == ChannelKind.Image && rig is null)
            {
                block($"{where}: 그림 바꾸기는 리그에만 쓴다 — 이모지 전체에는 그림이 없다");
            }

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not { } curve)
                {
                    continue;
                }

                // 회전은 z 하나다(emojis/2의 rotation).
                if (!ChannelKinds.HasAxis(channel.Kind, axis) || (channel.Kind == ChannelKind.Euler && axis != Axis.Z))
                {
                    block($"{where}: {axis} 성분은 이모지에 없다");
                    continue;
                }

                ValidateKeys(emoji, rig, channel, axis, curve, where, block);
            }
        }
    }

    private static void ValidateKeys(EmojiAsset emoji, EmojiRigAsset? rig, ChannelAsset channel, Axis axis, ComponentCurve curve, string where, Action<string> block)
    {
        float end = emoji.Timeline.Duration;

        foreach (KeyAsset key in curve.Keys)
        {
            if (!float.IsFinite(key.T) || !float.IsFinite(key.V) || key.T < -MotionRules.Tolerance || key.T > end + MotionRules.Tolerance)
            {
                block($"{where} {axis}: 키 {key.T:0.###}초가 타임라인(0~{end:0.###}초) 밖이다");
                break;
            }
        }

        if (channel.Kind == ChannelKind.Image && rig is not null)
        {
            foreach (KeyAsset key in curve.Keys)
            {
                int index = (int)Math.Round(key.V);

                if (Math.Abs(key.V - index) > MotionRules.Tolerance || index < 0 || index >= rig.Images.Count)
                {
                    block($"{where}: 그림 키 {key.V:0.###}가 리그 '{rig.Name}'의 그림(0~{rig.Images.Count - 1}번째)을 가리키지 않는다");
                    break;
                }
            }
        }
    }

    private static void ValidateSpans(EmojiAsset emoji, Action<string> block)
    {
        int last = emoji.LastFrame;
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiSpan span in emoji.Spans)
        {
            if (!MotionNames.IsValid(span.Name))
            {
                block($"구간: {MotionNames.RuleMessage(span.Name)}");
            }
            else if (string.Equals(span.Name, EmojiAsset.MainClip, StringComparison.Ordinal))
            {
                block($"구간 이름 '{EmojiAsset.MainClip}'은 타임라인 전체의 클립 이름이다");
            }
            else if (!names.Add(span.Name))
            {
                block($"구간 이름 '{span.Name}'이 두 번 나온다");
            }

            if (span.Start < 0 || span.End < span.Start || span.End > last)
            {
                block($"구간 '{span.Name}'({span.Start}~{span.End}fr)이 타임라인(0~{last}fr) 밖이거나 거꾸로다");
            }
        }

        foreach ((string role, string? link) in new[] { ("등장", emoji.In), ("머묾", emoji.Hold), ("퇴장", emoji.Out) })
        {
            if (link is not null && emoji.FindSpan(link) is null)
            {
                block($"{role}에 이은 구간 '{link}'이 없다");
            }
        }

        if (emoji.Hold is null && (emoji.In is not null || emoji.Out is not null))
        {
            block("등장 · 퇴장은 머묾을 이었을 때만 뜻이 있다 — 머묾이 없으면 타임라인 전체를 한 번 재생하고 사라진다");
        }
    }
}
