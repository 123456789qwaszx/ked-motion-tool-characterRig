using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>이벤트로 떠서 지금 돌고 있는 이모지 하나 — 연기하는 오브젝트, 정의, 뜬 뒤 흐른 시간, 구간.</summary>
public readonly record struct EmojiPlay(SceneObject Target, EmojiAsset Emoji, double Local, EmojiPhases Phases);

/// <summary>
/// 이벤트 키로 이모지 띄우기(<c>PLAN-parts.md</c> §10) — 캐릭터 모션의 이벤트 키 <c>emoji</c>(값 = 이모지 키)가 울리면 그 캐릭터에 붙은 이모지 리그가
/// 그 정의의 수명을 돈다. "표정 변화와 함께 머리 옆에 뜬다"를 툴에서 본다. <c>emoji_off</c>가 울리면 퇴장이 시작한다(머무는 이모지를 끈다).
///
/// 규약(초안): 한 캐릭터에 이모지는 하나 — 새 <c>emoji</c>가 울리면 앞의 것을 갈아 치운다(퇴장 없이). 모르는 키면 아무것도 안 뜬다(검증이 알린다).
/// 연기하는 오브젝트는 그 캐릭터에 **붙어 있는**, 정의와 같은 리그의 첫 오브젝트다 — 없으면 미리볼 데가 없다(런타임은 그때 리그를 세운다).
/// </summary>
public static class EmojiEvents
{
    /// <summary>이모지를 띄우는 이벤트 키의 이름 — 값이 이모지 키다.</summary>
    public const string Show = "emoji";

    /// <summary>머무는 이모지를 끄는 이벤트 키의 이름(값은 안 본다 — 그 캐릭터의 지금 이모지를 끈다).</summary>
    public const string Hide = "emoji_off";

    /// <summary>
    /// 그 시각에 돌고 있는 이모지들. <paramref name="fired"/>는 처음부터 그 시각까지 울린 이벤트(시각 순) —
    /// 모션 하나면 그 모션의 것을 연기하는 캐릭터의 id와 함께, 클립이면 <see cref="ClipPreview.EventsBetween(MotionDocument, ClipAsset, double, double, Func{ClipTrack, bool}?)"/>.
    /// </summary>
    public static List<EmojiPlay> Resolve(
        MotionDocument document,
        SceneAsset scene,
        IEnumerable<(double Time, string? ObjectId, string Name, string? Value)> fired,
        double seconds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(fired);

        // 캐릭터마다 지금의 이모지 하나: 정의, 뜬 시각, 끄라는 말이 온 시각.
        var current = new Dictionary<string, (EmojiAsset Emoji, double At, double? Off)>(StringComparer.Ordinal);

        foreach ((double time, string? objectId, string name, string? value) in fired)
        {
            if (objectId is null || time > seconds + 1e-6)
            {
                continue;
            }

            if (name == Show)
            {
                if (document.FindEmoji(value) is { } emoji)
                {
                    current[objectId] = (emoji, time, null); // 앞의 것을 갈아 치운다
                }
                else
                {
                    current.Remove(objectId);
                }
            }
            else if (name == Hide && current.TryGetValue(objectId, out (EmojiAsset Emoji, double At, double? Off) playing) && playing.Off is null)
            {
                current[objectId] = (playing.Emoji, playing.At, time);
            }
        }

        var plays = new List<EmojiPlay>();

        foreach ((string characterId, (EmojiAsset emoji, double at, double? off)) in current)
        {
            if (scene.AttachedTo(characterId).FirstOrDefault(o => o.Rig == emoji.Rig) is not { } target)
            {
                continue; // 붙은 이모지 리그가 없다
            }

            EmojiPhases phases = EmojiPreview.PlayPhases(document, emoji, off - at);
            double local = seconds - at;

            if (EmojiPreview.IsAlive(phases, local))
            {
                plays.Add(new EmojiPlay(target, emoji, local, phases));
            }
        }

        return plays;
    }

    /// <summary>도는 이모지들을 포즈에 얹는다 — 캐릭터의 모션이 다 얹힌 나무 위에(붙은 리그라 캐릭터를 따라간다).</summary>
    public static RectNodeTree Pose(RectNodeTree tree, MotionDocument document, IEnumerable<EmojiPlay> plays, IDictionary<string, float>? alphas = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(plays);

        foreach (EmojiPlay play in plays)
        {
            tree = EmojiPreview.Pose(tree, document, play.Emoji, play.Target, play.Local, alphas, play.Phases);
        }

        return tree;
    }

    /// <summary>
    /// 이벤트가 부리는 오브젝트들의 id — 울릴 <c>emoji</c> 이벤트(<paramref name="all"/>: 처음부터 끝까지)가 띄울 붙은 이모지 리그.
    /// 이들은 떠 있을 때만 보인다(뜨기 전·사라진 뒤에는 없다).
    /// </summary>
    public static HashSet<string> Targets(MotionDocument document, SceneAsset scene, IEnumerable<(double Time, string? ObjectId, string Name, string? Value)> all)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(all);
        var targets = new HashSet<string>(StringComparer.Ordinal);

        foreach ((_, string? objectId, string name, string? value) in all)
        {
            if (objectId is not null && name == Show && document.FindEmoji(value) is { } emoji
                && scene.AttachedTo(objectId).FirstOrDefault(o => o.Rig == emoji.Rig) is { } target)
            {
                targets.Add(target.Id);
            }
        }

        return targets;
    }
}
