using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Preview;

/// <summary>리그 하나의 그 프레임 — 그림 노드 · 불투명도 · 보이는 그림(스프라이트 키와 그 그림 파일).</summary>
public readonly record struct EmojiRigPose(string Rig, string ImageNode, float Alpha, string? Sprite, string? ImagePath);

/// <summary>이모지 하나의 그 프레임 — 조립한 리그의 트리(자리 · 배율 · 회전) + 이모지 전체의 불투명도 + 리그마다(그리는 순서).</summary>
public sealed record EmojiPose(RectNodeTree Tree, float Alpha, IReadOnlyList<EmojiRigPose> Rigs);

/// <summary>수명의 한 순간 — 어느 단계의 어느 클립의 몇 프레임인가(코어 <see cref="EmojiPlayback"/>).</summary>
public readonly record struct EmojiMoment(EmojiPhase Phase, EmojiClip? Clip, float Frame);

/// <summary>
/// 이모지의 미리보기(<c>docs/PLAN-emoji.md</c> E5) — **내보낸 정의를 코어로 돌린다.** 그래서 툴이 보이는 것 = 게임이 재생하는 것이다.
///
/// - 정의: <see cref="EmojiExport"/>가 낸 글자를 코어 로더(<see cref="EmojiDefinitionLoader"/>)로 읽은 것 — 구간 자르기 · 초 → 프레임까지 같은 길.
/// - 그 프레임의 값: 코어 <see cref="EmojiClipMath.SampleNode"/>. 노드에 옮기는 법은 런타임 <c>EmojiPlaybackSystem.Apply</c>와 같다 —
///   이모지 전체의 자리는 오프셋에 더하고, 리그의 키는 그림 노드가 받고, 기본 자리는 건드리지 않는다.
/// - 수명: 코어 <see cref="EmojiPlayback"/>(시간을 넘기는 것과 내리는 것만 받는 상태 기계)를 처음부터 다시 돌린다.
/// </summary>
public static class EmojiPreview
{
    /// <summary>타임라인 전체를 한 클립으로 읽을 때의 이름(편집 중의 미리보기 — 내보내지 않는다).</summary>
    public const string TimelineClip = "timeline";

    /// <summary>이모지 하나의 정의 — 내보낼 글자를 코어 로더로 읽은 것. 런타임이 거르는 이모지면 null과 까닭.</summary>
    public static EmojiDefinition? Definition(EmojiAsset emoji, EmojiRigFrame frame, out string? why)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(frame);
        string? reason = null;
        Dictionary<string, EmojiDefinition> loaded = EmojiExport.ToDefinitions(Holding(emoji), frame, (_, w) => reason ??= w);
        why = reason;
        return loaded.TryGetValue(emoji.Name, out EmojiDefinition? definition) ? definition : null;
    }

    /// <summary>
    /// 타임라인 전체를 코어 클립 하나로 — 그래프에서 고치는 동안 보이는 것. 머묾의 잇기와 상관없이 0 … 마지막 프레임이다.
    /// 내보내는 길(구간 자르기)을 그대로 지난다. 런타임이 읽지 못하는 모양이면 null.
    /// </summary>
    public static EmojiClip? Timeline(EmojiAsset emoji, EmojiRigFrame frame)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        var whole = new EmojiAsset
        {
            Name = string.IsNullOrEmpty(emoji.Name) ? TimelineClip : emoji.Name,
            OffsetX = emoji.OffsetX,
            OffsetY = emoji.OffsetY,
            Rigs = emoji.Rigs,
            Timeline = emoji.Timeline,
        };

        return Definition(whole, frame, out _)?.Main;
    }

    /// <summary>
    /// 그 프레임의 모습. <paramref name="clip"/>이 null이면 정의의 모습 그대로(키가 없는 채널은 중립값).
    /// 트리는 조립한 리그 — <paramref name="space"/>(없으면 기준 해상도 · 가운데) 밑에 선다.
    /// </summary>
    public static EmojiPose Pose(EmojiAsset emoji, EmojiRigFrame frame, EmojiClip? clip, float clipFrame, RectSpace? space = null)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(frame);

        RectNodeTree tree = RigSchemaLoader.BuildTree(frame.Assemble(emoji), space ?? new RectSpace(BuiltinTuning.BaseResolution, Vec2.Half));

        // 이모지 전체 — 자리의 키는 오프셋 값에 더한다.
        EmojiNodePose whole = EmojiClipMath.SampleNode(clip, "", clipFrame);
        RectNodeState offset = tree.GetState(frame.OffsetNode);
        tree.SetState(frame.OffsetNode, offset
            .WithAnchoredPosition(new Vec2(emoji.OffsetX + whole.Position.X, emoji.OffsetY + whole.Position.Y))
            .WithLocalScale(new Vec3(whole.Scale.X, whole.Scale.Y, 1f))
            .WithLocalEuler(new Vec3(0f, 0f, whole.Rotation)));

        var rigs = new List<EmojiRigPose>(emoji.Rigs.Count);

        foreach (EmojiRigAsset rig in emoji.Rigs)
        {
            EmojiNodePose pose = EmojiClipMath.SampleNode(clip, rig.Name, clipFrame);
            string node = frame.ImageNodeOf(rig.Name);
            RectNodeState image = tree.GetState(node);
            tree.SetState(node, image
                .WithAnchoredPosition(pose.Position)
                .WithLocalScale(new Vec3(pose.Scale.X, pose.Scale.Y, 1f))
                .WithLocalEuler(new Vec3(0f, 0f, pose.Rotation)));

            string? sprite = pose.SpriteKey ?? rig.DefaultSprite;
            rigs.Add(new EmojiRigPose(rig.Name, node, pose.Alpha, sprite, ImageOf(rig, sprite)));
        }

        return new EmojiPose(tree, whole.Alpha, rigs);
    }

    /// <summary>
    /// 수명의 한 순간 — 처음부터 <paramref name="seconds"/>초 재생한 뒤. <paramref name="offAt"/>초에 내린다(null이면 내리지 않는다).
    /// 런타임과 같은 상태 기계를 돌린다: 머무는 이모지는 등장 → 머묾 ⟲ → (내리면) 퇴장, 아니면 main 한 번.
    /// </summary>
    public static EmojiMoment Moment(EmojiDefinition definition, double seconds, double? offAt = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var playback = new EmojiPlayback(definition);

        if (offAt is { } off && seconds > off)
        {
            playback.Advance((float)Math.Max(0, off));
            playback.RequestOff();
            playback.Advance((float)(seconds - Math.Max(0, off)));
        }
        else
        {
            playback.Advance((float)Math.Max(0, seconds));
        }

        return new EmojiMoment(playback.Phase, playback.Clip, playback.Frame);
    }

    /// <summary>
    /// 미리보기 한 바퀴의 길이(초) — 등장 + 머묾 × <paramref name="holdCycles"/> + 퇴장, 머묾이 없으면 main 하나.
    /// 내리는 시각은 <see cref="OffAt"/>.
    /// </summary>
    public static double LifeSeconds(EmojiDefinition definition, int holdCycles)
    {
        ArgumentNullException.ThrowIfNull(definition);
        double fps = definition.Fps;

        if (!definition.Stays)
        {
            return (definition.Main?.Length ?? 0) / fps;
        }

        return OffAt(definition, holdCycles)!.Value + (definition.Out?.Length ?? 0) / fps;
    }

    /// <summary>미리보기에서 내리는 시각 — 등장이 끝나고 머묾을 <paramref name="holdCycles"/>번 돈 뒤. 머묾이 없으면 null(스스로 사라진다).</summary>
    public static double? OffAt(EmojiDefinition definition, int holdCycles)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.Stays)
        {
            return null;
        }

        double fps = definition.Fps;
        return ((definition.In?.Length ?? 0) + (definition.Hold?.Length ?? 1) * Math.Max(1, holdCycles)) / fps;
    }

    /// <summary>스프라이트 키의 그림 파일 — 그 리그의 그림 중 파일 이름이 키인 첫 장. 없으면 null.</summary>
    public static string? ImageOf(EmojiRigAsset rig, string? sprite)
        => sprite is null ? null : rig.Images.FirstOrDefault(i => string.Equals(EmojiRigAsset.SpriteKeyOf(i), sprite, StringComparison.Ordinal));

    private static MotionDocument Holding(EmojiAsset emoji)
    {
        var document = new MotionDocument();
        document.Emojis.Add(emoji);
        return document;
    }
}
