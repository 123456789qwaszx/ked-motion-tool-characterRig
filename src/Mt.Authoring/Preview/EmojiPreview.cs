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
/// - 좌우(E8): "왼쪽을 보는 인물로 보기"면 로케이터를 뒤집고(런타임 <c>EmojiLocatorSystem</c>), 예외의 부호는 코어 <see cref="EmojiSide"/>가 낸다
///   (런타임 <c>EmojiPlaybackSystem.Apply</c>와 같은 곱셈). 부호를 여기서 따로 계산하지 않는다.
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
    /// <remarks>
    /// <paramref name="locatorFlipped"/>면 왼쪽을 보는 인물 밑으로 본다 — 로케이터를 뒤집고 <paramref name="definition"/>(코어 정의)로 예외의 부호를 곱한다.
    /// 정의가 없으면(런타임이 거르는 이모지) 로케이터만 뒤집는다.
    /// </remarks>
    public static EmojiPose Pose(EmojiAsset emoji, EmojiRigFrame frame, EmojiClip? clip, float clipFrame, RectSpace? space = null, EmojiDefinition? definition = null, bool locatorFlipped = false)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(frame);

        RectNodeTree tree = RigSchemaLoader.BuildTree(frame.Assemble(emoji), space ?? new RectSpace(BuiltinTuning.BaseResolution, Vec2.Half));

        if (locatorFlipped)
        {
            FlipLocator(tree, "", frame);
        }

        (float alpha, IReadOnlyList<EmojiRigPose> rigs) = Apply(tree, "", emoji, frame, clip, clipFrame, definition, locatorFlipped);
        return new EmojiPose(tree, alpha, rigs);
    }

    /// <summary>
    /// 이미 있는 트리(씬)에 조립한 리그를 세운다 — 런타임처럼 캐릭터의 <c>CharSlot_Scale</c> 밑(로케이터 자리)에.
    /// <paramref name="parentKey"/>가 null이면 트리의 루트 공간 직속. 노드의 키는 <paramref name="keyPrefix"/>를 머리로 단다.
    /// </summary>
    /// <remarks><paramref name="locatorFlipped"/>면 세운 로케이터를 뒤집는다(<see cref="FlipLocator"/>) — 왼쪽을 보는 인물.</remarks>
    /// <param name="locatorPosition">
    /// 로케이터의 자리(인물의 발에서 잰 px) — 런타임에서는 캐릭터 + 포즈별 표(<c>EmojiLocatorSystem</c>)가 넣는 값이다.
    /// 툴은 그 표를 보기에만 읽는다(<see cref="EmojiLocatorFile"/>). null이면 덤프의 값 그대로.
    /// </param>
    public static void Attach(RectNodeTree tree, EmojiAsset emoji, EmojiRigFrame frame, string keyPrefix, string? parentKey, bool locatorFlipped = false, Vec2? locatorPosition = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(frame);
        RigSchemaLoader.AddRigTo(tree, frame.Assemble(emoji), keyPrefix ?? "", parentKey);

        if (locatorPosition is { } position)
        {
            string locator = tree.GetParentKey((keyPrefix ?? "") + frame.OffsetNode);
            tree.SetState(locator, tree.GetState(locator).WithAnchoredPosition(position));
        }

        if (locatorFlipped)
        {
            FlipLocator(tree, keyPrefix ?? "", frame);
        }
    }

    /// <summary>
    /// 로케이터(<c>Emoji_Offset</c>의 부모)를 뒤집는다 — 런타임 <c>EmojiLocatorSystem</c>: 인물이 왼쪽을 보면 로케이터의 자리 x와 가로 배율을 뒤집는다.
    /// 로케이터에 지금 든 자리(덤프의 값이거나 <see cref="Attach"/>가 넣은 표의 값)의 x를 뒤집는다. 갓 세운 로케이터에 한 번 쓴다.
    /// </summary>
    public static void FlipLocator(RectNodeTree tree, string keyPrefix, EmojiRigFrame frame)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(frame);
        string locator = tree.GetParentKey((keyPrefix ?? "") + frame.OffsetNode);
        RectNodeState state = tree.GetState(locator);
        tree.SetState(locator, state
            .WithAnchoredPosition(new Vec2(-state.AnchoredPosition.X, state.AnchoredPosition.Y))
            .WithLocalScale(new Vec3(-state.LocalScale.X, state.LocalScale.Y, state.LocalScale.Z)));
    }

    /// <summary>
    /// 세운 리그(<paramref name="keyPrefix"/>를 머리로 단 노드)에 그 프레임의 값을 옮긴다 — 런타임 <c>EmojiPlaybackSystem.Apply</c>와 같은 법.
    /// 이모지 전체의 불투명도와 리그마다(그리는 순서)의 불투명도 · 그림을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 좌우 — <paramref name="locatorFlipped"/>(로케이터가 뒤집혔는가)와 <paramref name="definition"/>(코어 정의)으로 예외 둘만 한 번 더 뒤집는다:
    /// <see cref="EmojiSide.OffsetSign"/>은 <c>Emoji_Offset</c>의 자리 x · 가로 배율 · 회전에, <see cref="EmojiSide.PlaceSign"/>은 <c>upright</c> 리그의
    /// <c>EmojiRig_Place</c> 가로 배율 · 회전에 곱한다(자리는 부모를 따라 거울 쪽). 리그는 런타임처럼 순번으로 맞춘다. 정의가 없으면 부호는 1이다.
    /// </remarks>
    public static (float Alpha, IReadOnlyList<EmojiRigPose> Rigs) Apply(RectNodeTree tree, string keyPrefix, EmojiAsset emoji, EmojiRigFrame frame, EmojiClip? clip, float clipFrame, EmojiDefinition? definition = null, bool locatorFlipped = false)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(frame);
        keyPrefix ??= "";
        float offsetSign = definition is null ? 1f : EmojiSide.OffsetSign(definition, locatorFlipped);

        // 이모지 전체 — 자리의 키는 오프셋 값에 더한다.
        EmojiNodePose whole = EmojiClipMath.SampleNode(clip, "", clipFrame);
        string offsetKey = keyPrefix + frame.OffsetNode;
        tree.SetState(offsetKey, tree.GetState(offsetKey)
            .WithAnchoredPosition(new Vec2(offsetSign * (emoji.OffsetX + whole.Position.X), emoji.OffsetY + whole.Position.Y))
            .WithLocalScale(new Vec3(offsetSign * whole.Scale.X, whole.Scale.Y, 1f))
            .WithLocalEuler(new Vec3(0f, 0f, offsetSign * whole.Rotation)));

        var rigs = new List<EmojiRigPose>(emoji.Rigs.Count);

        for (int i = 0; i < emoji.Rigs.Count; i++)
        {
            EmojiRigAsset rig = emoji.Rigs[i];

            // 바로 서야 하는 그림 — 기본 자리의 가로 배율과 회전에 부호를 곱한다(런타임처럼 Upright인 리그만 다시 쓴다).
            if (definition is not null && i < definition.Rigs.Count && definition.Rigs[i] is { Upright: true } entry)
            {
                float placeSign = EmojiSide.PlaceSign(definition, entry, locatorFlipped);
                string place = keyPrefix + frame.PlaceNodeOf(rig.Name);
                tree.SetState(place, tree.GetState(place)
                    .WithLocalScale(new Vec3(placeSign * entry.Scale, entry.Scale, 1f))
                    .WithLocalEuler(new Vec3(0f, 0f, placeSign * entry.Rotation)));
            }

            EmojiNodePose pose = EmojiClipMath.SampleNode(clip, rig.Name, clipFrame);
            string node = keyPrefix + frame.ImageNodeOf(rig.Name);
            tree.SetState(node, tree.GetState(node)
                .WithAnchoredPosition(pose.Position)
                .WithLocalScale(new Vec3(pose.Scale.X, pose.Scale.Y, 1f))
                .WithLocalEuler(new Vec3(0f, 0f, pose.Rotation)));

            string? sprite = pose.SpriteKey ?? rig.DefaultSprite;
            rigs.Add(new EmojiRigPose(rig.Name, node, pose.Alpha, sprite, ImageOf(rig, sprite)));
        }

        return (whole.Alpha, rigs);
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
