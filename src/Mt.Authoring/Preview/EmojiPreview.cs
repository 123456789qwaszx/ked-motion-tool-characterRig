using Ked.Presentation.Core;
using Mt.Authoring.Model;

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
        => seconds >= 0 && seconds <= PhasesOf(document, emoji).Total + 1e-6;

    /// <summary>
    /// 수명의 시각 t 포즈 — <paramref name="actor"/>(이모지 리그의 오브젝트)에 얹는다. 자리(<see cref="EmojiAsset.AnchorNode"/>의 오프셋)도 여기서 넣는다.
    /// 수명 밖이면 template 그대로(호출부가 <see cref="IsAlive"/>로 안 그린다).
    /// </summary>
    public static RectNodeTree Pose(RectNodeTree template, MotionDocument document, EmojiAsset emoji, SceneObject actor, double seconds, IDictionary<string, float>? alphas = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(actor);

        RectNodeTree tree = template.Clone();
        ApplyAnchor(tree, emoji, actor);

        if (!IsAlive(document, emoji, seconds))
        {
            return tree;
        }

        EmojiPhases phases = PhasesOf(document, emoji);
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
    public static void ApplyAnchor(RectNodeTree tree, EmojiAsset emoji, SceneObject actor)
    {
        string key = tree.Contains(actor.KeyOf(emoji.AnchorNode)) ? actor.KeyOf(emoji.AnchorNode) : ScenePreview.RootKeyOf(actor);

        if (tree.Contains(key))
        {
            tree.SetState(key, tree.GetState(key).WithAnchoredPosition(new Vec2(emoji.OffsetX, emoji.OffsetY)));
        }
    }

    private static MotionAsset? Find(MotionDocument document, string? name) => string.IsNullOrEmpty(name) ? null : document.Find(name);
}
