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
/// 편집 중 계속 돌리는 이모지 검증(<c>docs/history/PLAN-emoji.md</c> E3). 막는 문제가 있으면 <c>emojis.json</c>을 내지 않는다.
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

    /// <summary>
    /// 그림 파일이 그 경로에 없는 것 — 알림이다(막지 않는다: 게임은 파일 이름으로 그림을 찾고, 내보내기는 파일을 읽지 않는다).
    /// 경로는 문서 기준이라 문서가 놓인 폴더를 받는다. 파일로 만든 문서의 틀린 경로가 툴에서 열어 봐야만 드러났다.
    /// 있기는 한데 문서의 폴더 밖인 것(절대 경로 · <c>../</c>)도 알린다 — 화면에서 다른 곳의 그림을 고르면 그렇게 적힌다.
    /// </summary>
    public static IReadOnlyList<EmojiProblem> MissingImages(MotionDocument document, string documentDirectory)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documentDirectory);
        var problems = new List<EmojiProblem>();

        foreach (EmojiAsset emoji in document.Emojis)
        {
            foreach (EmojiRigAsset rig in emoji.Rigs)
            {
                foreach (string image in rig.Images.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal))
                {
                    string full = Path.IsPathRooted(image) ? image : Path.Combine(documentDirectory, image);

                    if (!File.Exists(full))
                    {
                        problems.Add(new EmojiProblem(emoji.Name, $"그림 '{rig.Name}'의 그림 파일을 찾지 못했다: {image} (문서가 놓인 폴더 기준) — 툴에서 자리표시로 보인다", Blocking: false));
                    }
                    else if (Path.IsPathRooted(image) || image.Replace('\\', '/').StartsWith("../", StringComparison.Ordinal))
                    {
                        // 있는 파일이지만 문서의 폴더 밖이다 — 저작본만 옮기거나 다른 PC에서 열면 그림을 잃는다.
                        problems.Add(new EmojiProblem(emoji.Name, $"그림 '{rig.Name}'의 그림 파일이 문서의 폴더 밖에 있다: {image} — 저작본을 옮기면 그림을 잃는다. 문서 옆(그 아래 폴더)에 두고 다시 고른다", Blocking: false));
                    }
                }
            }
        }

        return problems;
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

        // 막는 문제가 없을 때만 — 구간이 어긋난 채로는 견줄 처음과 끝이 없다.
        if (problems.Count == 0)
        {
            problems.AddRange(HoldSeams(emoji).Select(message => new EmojiProblem(emoji.Name, message, Blocking: false)));
        }

        if (OffFrameKeys(emoji) is { } offFrame)
        {
            problems.Add(new EmojiProblem(emoji.Name, offFrame, Blocking: false));
        }

        return problems;
    }

    /// <summary>
    /// 프레임에 안 맞는 키 — 이모지는 프레임(1/24초)으로 센다. 화면에서 놓은 키는 늘 프레임에 맞지만, 파일로 쓴 초(0.26)는
    /// 프레임 사이(6.24)에 놓일 수 있다. 그대로 나가고 그대로 재생된다(막지 않는다) — 다만 구간의 경계와 어긋나고, 화면에서 그 키를 프레임으로 잡기 어렵다.
    /// </summary>
    private static string? OffFrameKeys(EmojiAsset emoji)
    {
        int count = 0;
        string? first = null;

        foreach (ChannelAsset channel in emoji.Timeline.Channels)
        {
            foreach (Axis axis in ChannelKinds.Axes(channel.Kind))
            {
                foreach (KeyAsset key in channel.Get(axis)?.Keys ?? (IReadOnlyList<KeyAsset>)Array.Empty<KeyAsset>())
                {
                    float frame = key.T * EmojiAsset.Fps;

                    if (float.IsFinite(frame) && Math.Abs(frame - MathF.Round(frame)) > 1e-3f)
                    {
                        count++;
                        first ??= FormattableString.Invariant($"{channel.Node} {ChannelKindLabel.File(channel.Kind)}의 {key.T:0.####}초 = {frame:0.##}프레임");
                    }
                }
            }
        }

        return count == 0
            ? null
            : FormattableString.Invariant($"프레임에 안 맞는 키가 {count}개다(처음: {first}) — 이모지는 1/{EmojiAsset.Fps:0}초 단위로 센다. 초 = 프레임 ÷ {EmojiAsset.Fps:0}로 적는다");
    }

    /// <summary>
    /// 머묾의 이음매 — 머묾은 끝 프레임 다음에 처음 프레임으로 돌아간다. 그 한 걸음이 구간 안의 어느 걸음보다도
    /// 훨씬 크면 돌 때마다 튄다. (처음과 끝이 꼭 같아야 하는 것은 아니다 — 고르게 흐르는 움직임은 끝이 처음의 한 걸음 앞이다.)
    /// 막지 않는다 — 뜻한 것일 수 있다. 그림 바꾸기는 본래 장이 넘어가는 것이라 보지 않고, 회전은 한 바퀴(360°)를 같은 모습으로 친다.
    /// </summary>
    private static IEnumerable<string> HoldSeams(EmojiAsset emoji)
    {
        if (emoji.FindSpan(emoji.Hold) is not { } hold || hold.End <= hold.Start)
        {
            yield break;
        }

        foreach (ChannelAsset channel in emoji.Timeline.Channels.Where(c => c.Kind != ChannelKind.Image))
        {
            foreach (Axis axis in ChannelKinds.Axes(channel.Kind))
            {
                if (channel.Get(axis) is not { Keys.Count: > 0 } curve)
                {
                    continue;
                }

                // 프레임마다의 값 — 구간의 처음부터 끝까지.
                List<Graph.GraphPoint> sampled = Graph.CurveSampler.Sample(curve.Keys, EmojiAsset.SecondsOf(hold.Start), EmojiAsset.SecondsOf(hold.End), hold.End - hold.Start);
                double At(int frame)
                {
                    double t = EmojiAsset.SecondsOf(frame);
                    return sampled.MinBy(p => Math.Abs(p.T - t)).V;
                }

                double Step(double from, double to)
                {
                    double d = Math.Abs(to - from);

                    if (channel.Kind == ChannelKind.Euler)
                    {
                        d %= 360;
                        d = Math.Min(d, 360 - d);
                    }

                    return d;
                }

                double widest = 0;

                for (int f = hold.Start; f < hold.End; f++)
                {
                    widest = Math.Max(widest, Step(At(f), At(f + 1)));
                }

                double seam = Step(At(hold.End), At(hold.Start));

                if (seam > Math.Max(1e-3, widest * 2))
                {
                    yield return $"머묾 '{hold.Name}'이 되풀이할 때 튄다 — {EmojiWords.Target(channel.Node)}의 {EmojiWords.Component(channel.Kind, axis)}이 끝({hold.End}프레임) {Round(At(hold.End))}에서 처음({hold.Start}프레임) {Round(At(hold.Start))}으로 한 걸음에 넘어간다(구간 안의 가장 큰 걸음은 {Round(widest)})";
                }
            }
        }

        static string Round(double v) => Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateRigs(EmojiAsset emoji, Action<string> block)
    {
        if (emoji.Rigs.Count == 0)
        {
            block("그림이 없다 — 이모지는 그림 하나 이상으로 이뤄진다");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiRigAsset rig in emoji.Rigs)
        {
            if (!MotionNames.IsValid(rig.Name))
            {
                block($"그림: {MotionNames.RuleMessage(rig.Name)}");
            }
            else if (!names.Add(rig.Name))
            {
                block($"그림 이름 '{rig.Name}'이 두 번 나온다 — 키가 그림을 이름으로 가리킨다");
            }

            if (rig.Images.Count == 0)
            {
                block($"그림 '{rig.Name}'에 그림 파일이 없다 — 그림을 고르고 '그림 더하기…'로 PNG를 넣는다(첫 장이 기본으로 보인다)");
            }
            else if (rig.Images.Any(i => string.IsNullOrWhiteSpace(EmojiRigAsset.SpriteKeyOf(i))))
            {
                block($"그림 '{rig.Name}'의 그림 파일에 파일 이름이 없다 — 게임이 파일 이름으로 그림을 찾는다");
            }

            if (!float.IsFinite(rig.Scale) || rig.Scale == 0f || !float.IsFinite(rig.Rotation) || !float.IsFinite(rig.PlaceX) || !float.IsFinite(rig.PlaceY))
            {
                block($"그림 '{rig.Name}'의 기본 자리 · 크기 · 회전이 쓸 수 없는 값이다(크기 {rig.Scale})");
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
            string where = $"채널 {channel.Node} {ChannelKindLabel.File(channel.Kind)}";

            if (!targets.Add((channel.Node, channel.Kind)))
            {
                block($"{where}: 같은 대상의 같은 채널이 둘이다");
            }

            if (!frame.TryTarget(channel.Node, out string rigName))
            {
                block($"{where}: 키를 받는 노드가 아니다 — {frame.OffsetNode}(이모지 전체) 또는 {{그림}}{EmojiRigFrame.Separator}{frame.ImageNode}에만 키를 잡는다");
                continue;
            }

            EmojiRigAsset? rig = string.IsNullOrEmpty(rigName) ? null : emoji.FindRig(rigName);

            if (!string.IsNullOrEmpty(rigName) && rig is null)
            {
                block($"{where}: 그림 '{rigName}'이 이 이모지에 없다");
                continue;
            }

            if (channel.Kind == ChannelKind.Image && rig is null)
            {
                block($"{where}: 그림 바꾸기는 그림에만 쓴다 — 이모지 전체에는 넘길 장이 없다");
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
                    block($"{where}: 그림 키 {key.V:0.###}가 그림 '{rig.Name}'의 장(0~{rig.Images.Count - 1}번째)을 가리키지 않는다");
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
