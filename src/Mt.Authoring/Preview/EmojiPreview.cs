using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Preview;

/// <summary>이모지 수명의 구간들(초) — 등장 · 머묾 · 퇴장. 머무는 이모지(끝이 없다)는 미리보기용으로 머묾을 얼마간만 보인다.</summary>
public readonly record struct EmojiPhases(float Enter, float Hold, float Exit, bool Staying)
{
    public float HoldStart => Enter;
    public float ExitStart => Enter + Hold;
    public float Total => Enter + Hold + Exit;
}

/// <summary>
/// 이모지 정의의 미리보기 — 수명 한 번의 시각 t 포즈. 새 수학은 없다: 등장·머묾·퇴장 모션을 차례로 <see cref="MotionPreview"/>에 얹는다.
///
/// 뜻(런타임에 요청하는 규약, <c>PLAN-parts.md</c> §8):
/// 등장은 한 번(끝 규칙 그대로 — commit이면 그 자세가 남는다). 머묾은 등장이 끝난 자세에서 시작해 **처음부터 되풀이**하고(바퀴가 쌓이지 않는다),
/// 머무는 시간이 다하면 그 순간의 자세에서 퇴장이 시작한다. 퇴장이 끝나면 사라진다.
/// </summary>
public static class EmojiPreview
{
    /// <summary>머무는 이모지를 미리볼 때 머묾을 보이는 길이 — 머묾 모션 세 바퀴, 적어도 이만큼(초).</summary>
    public const float StayingPreviewSeconds = 2f;

    public static EmojiPhases PhasesOf(MotionDocument document, EmojiAsset emoji)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(emoji);
        float enter = Find(document, emoji.Enter)?.Duration ?? 0f;
        float exit = Find(document, emoji.Exit)?.Duration ?? 0f;
        float loop = Find(document, emoji.Hold)?.Duration ?? 0f;
        float hold = emoji.HoldSeconds is { } seconds ? Math.Max(0f, seconds) : Math.Max(StayingPreviewSeconds, loop * 3f);
        return new EmojiPhases(enter, hold, exit, emoji.HoldSeconds is null);
    }

    /// <summary>그 시각에 떠 있는가 — 수명 안(0 ≤ t ≤ 끝).</summary>
    public static bool IsAlive(MotionDocument document, EmojiAsset emoji, double seconds)
        => IsAlive(PhasesOf(document, emoji), seconds);

    public static bool IsAlive(in EmojiPhases phases, double seconds) => seconds >= 0 && seconds <= (double)phases.Total + 1e-6;

    /// <summary>머무는 이모지가 "아직 끄라는 말이 없다"일 때의 머묾 길이 — 사실상 끝없음.</summary>
    public const float Forever = 1e9f;

    /// <summary>
    /// 이벤트로 뜬 이모지의 구간들 — 미리보기용 어림(<see cref="PhasesOf"/>)이 아니라 **실제로 도는 길이**다.
    /// 머무는 시간이 있으면 그만큼, 없으면 끄라고 할 때까지(<see cref="Forever"/>). <paramref name="offAfter"/>는 뜬 뒤 몇 초에 끄라는 말(<c>emoji_off</c>)이 왔는가 —
    /// 오면 그때 퇴장이 시작한다(등장은 끝까지 돈다. 제 수명이 먼저 다했으면 그것이 이긴다).
    /// </summary>
    public static EmojiPhases PlayPhases(MotionDocument document, EmojiAsset emoji, double? offAfter)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(emoji);
        float enter = Find(document, emoji.Enter)?.Duration ?? 0f;
        float exit = Find(document, emoji.Exit)?.Duration ?? 0f;
        float hold = emoji.HoldSeconds is { } seconds ? Math.Max(0f, seconds) : Forever;

        if (offAfter is { } off)
        {
            hold = Math.Min(hold, (float)Math.Max(0, off - enter));
        }

        return new EmojiPhases(enter, hold, exit, emoji.HoldSeconds is null && offAfter is null);
    }

    /// <summary>
    /// 수명의 시각 t 포즈 — <paramref name="actor"/>(이모지 리그의 오브젝트)에 얹는다. 자리(<see cref="EmojiAsset.AnchorNode"/>의 오프셋)도 여기서 넣는다.
    /// 수명 밖이면 template 그대로(호출부가 <see cref="IsAlive"/>로 안 그린다).
    /// </summary>
    /// <param name="origin">붙는 자리(<see cref="LocatorPoint"/>)가 푼 점 — 오프셋이 여기서부터다. null이면 붙는 노드에서부터.</param>
    public static RectNodeTree Pose(RectNodeTree template, MotionDocument document, EmojiAsset emoji, SceneObject actor, double seconds, IDictionary<string, float>? alphas = null, Vec2? origin = null)
        => Pose(template, document, emoji, actor, seconds, alphas, PhasesOf(document, emoji), origin);

    /// <summary>구간을 정해 준 포즈 — 이벤트로 뜬 이모지(<see cref="PlayPhases"/>)가 쓴다.</summary>
    public static RectNodeTree Pose(RectNodeTree template, MotionDocument document, EmojiAsset emoji, SceneObject actor, double seconds, IDictionary<string, float>? alphas, EmojiPhases phases, Vec2? origin = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(actor);

        RectNodeTree tree = template.Clone();
        ApplyAnchor(tree, emoji, actor, origin);

        if (!IsAlive(phases, seconds))
        {
            return tree;
        }

        string prefix = actor.Prefix;

        // 등장 — 한 번. 끝났으면 끝 규칙대로(commit이면 남고 restore면 처음 자세).
        if (Find(document, emoji.Enter) is { } enter)
        {
            tree = MotionPreview.Pose(tree, enter, seconds, enter.PreviewMode, null, prefix, alphas);
        }

        if (seconds <= phases.HoldStart)
        {
            return tree;
        }

        // 머묾 — 등장이 끝난 자세에서 처음부터 되풀이. 머무는 시간이 다한 자리에서 멈춘다(퇴장이 거기서 시작한다).
        if (Find(document, emoji.Hold) is { Duration: > 0f } hold)
        {
            double local = Math.Min(seconds, phases.ExitStart) - phases.HoldStart;
            double phase = local % hold.Duration;

            // 바퀴의 끝과 다음 바퀴의 처음이 만나는 자리는 끝으로 본다 — 딱 떨어지는 길이면 마지막 키의 자세에서 퇴장한다.
            if (phase < 1e-6 && local > 1e-6)
            {
                phase = hold.Duration;
            }

            tree = MotionPreview.Pose(tree, hold, phase, hold.PreviewMode, null, prefix, alphas);
        }

        if (seconds <= phases.ExitStart)
        {
            return tree;
        }

        // 퇴장 — 한 번.
        if (Find(document, emoji.Exit) is { } exit)
        {
            tree = MotionPreview.Pose(tree, exit, Math.Min(seconds - phases.ExitStart, exit.Duration), exit.PreviewMode, null, prefix, alphas);
        }

        return tree;
    }

    /// <summary>자리 — 정의의 오프셋을 그 노드의 위치로. 노드가 리그에 없으면 루트에.</summary>
    public static void ApplyAnchor(RectNodeTree tree, EmojiAsset emoji, SceneObject actor, Vec2? origin = null)
    {
        string key = AnchorKeyOf(tree, emoji, actor);

        if (tree.Contains(key))
        {
            tree.SetState(key, tree.GetState(key).WithAnchoredPosition((origin ?? Vec2.Zero) + new Vec2(emoji.OffsetX, emoji.OffsetY)));
        }
    }

    private static string AnchorKeyOf(RectNodeTree tree, EmojiAsset emoji, SceneObject actor)
        => tree.Contains(actor.KeyOf(emoji.AnchorNode)) ? actor.KeyOf(emoji.AnchorNode) : ScenePreview.RootKeyOf(actor);

    /// <summary>
    /// 붙는 자리(<see cref="EmojiAsset.Locator"/>)를 푼다 — 붙은 캐릭터의 그 기준점(<c>faceAura</c>·<c>face</c>…)이 자리 노드의 좌표로 어디인가.
    /// 점은 코어의 focus 오프셋 합(<see cref="FocusOffsetMath"/> — place가 쓰는 그 표)을 초상 계층의 측정 노드에서 잰 것이고,
    /// 자리 노드를 그리로 보내는 값은 place와 같은 풀이(<see cref="SettledFocusMath.SolveFocusPlacement"/>)다.
    ///
    /// <paramref name="rest"/>는 **가만히 선 무대**(모션을 얹기 전) — 뜨는 순간 한 번 푸는 값이라 캐릭터의 초상 계층 모션(끄덕임)을 따라가지 않는다.
    /// 붙는 자리가 없거나 모르는 이름이거나, 붙은 데가 없거나 캐릭터 리그가 아니면 null(오프셋은 붙는 노드에서부터).
    /// </summary>
    public static Vec2? LocatorPoint(RectNodeTree rest, SceneAsset scene, EmojiAsset emoji, SceneObject actor, FocusTuningFile? focus)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(actor);

        if (!FocusPresetName.TryNormalizeToken(emoji.Locator, out string preset) || scene.ParentOf(actor) is not { } parent)
        {
            return null;
        }

        string measureKey = parent.KeyOf(PlaceFocusStageReduction.MeasureNodeId);
        string anchorKey = AnchorKeyOf(rest, emoji, actor);

        if (!rest.Contains(measureKey) || !rest.Contains(anchorKey))
        {
            return null;
        }

        // 툴은 배역 키를 두지 않는다 — place와 같이 표의 기본값(base)만 쓴다.
        Vec2 offset = FocusOffsetMath.Resolve(focus?.Body, "", preset, Vec2.Zero);
        Vec2 point = SettledFocusMath.FocusPointInRigSpace(rest.BuildChainTo(measureKey), rest.RootSpace, offset);

        RectNodeState[] chain = rest.BuildChainTo(anchorKey);
        return SettledFocusMath.SolveFocusPlacement(chain, rest.RootSpace, chain.Length - 1, Vec2.Zero, point, chain[^1].AnchoredPosition);
    }

    private static MotionAsset? Find(MotionDocument document, string? name) => string.IsNullOrEmpty(name) ? null : document.Find(name);
}
