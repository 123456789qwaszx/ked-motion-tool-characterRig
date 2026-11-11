using Mt.Authoring.Model;

namespace Mt.Authoring.Schema;

/// <summary>
/// 스키마에 어긋난 것 하나 — 무엇이(모션 · 이모지 · 배역 · 리그), 어느 것이, 왜.
/// <see cref="Blocking"/>이 false면 알림이다 — 내보내기를 막지 않는다.
/// </summary>
public sealed record SchemaViolation(string Kind, string Subject, string Message, bool Blocking = true)
{
    public override string ToString() => $"{Kind} '{Subject}': {Message}";
}

/// <summary>
/// 스키마 계약(<c>docs/PLAN-characterRig.md</c> §3의 1) — **구조는 유니티가 정한다.**
/// 문서(모션 · 이모지 · 클립의 배역)는 스키마에 있는 리그와 노드만 쓴다. "스키마에 없는 리그 · 노드"를 보는 곳은 여기 하나다 —
/// 모션 · 이모지 · 클립 검증이 이것을 불러 **막는 문제**로 올린다(어긋난 문서는 내보내지 못한다).
///
/// 스키마는 rigKind → 노드 id 목록으로 받는다. 툴은 덤프에 카메라의 가짜 리그를 더한 것을 넘긴다.
/// </summary>
public static class SchemaContract
{
    /// <summary>모션의 리그와 채널의 노드.</summary>
    public static IReadOnlyList<SchemaViolation> Motion(IReadOnlyDictionary<string, ICollection<string>> schema, MotionAsset motion)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(motion);
        var violations = new List<SchemaViolation>();

        if (!schema.TryGetValue(motion.Rig ?? "", out ICollection<string>? known))
        {
            violations.Add(new SchemaViolation("모션", motion.Name, MissingRig(schema, motion.Rig)));
            return violations;
        }

        // 빈 노드는 모양 규칙(코어 MotionRules)이 말한다 — 여기서는 이름이 있는데 스키마에 없는 것만.
        foreach (string node in motion.Channels.Select(c => c.Node).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).Where(n => !known.Contains(n)))
        {
            violations.Add(new SchemaViolation("모션", motion.Name, MissingNode("노드", node, motion.Rig!)));
        }

        return violations;
    }

    /// <summary>이모지의 리그 · 자리 노드, 그리고 그 리그의 스킨(그림 · 반전 판)이 그림을 준 조각.</summary>
    public static IReadOnlyList<SchemaViolation> Emoji(IReadOnlyDictionary<string, ICollection<string>> schema, MotionDocument document, EmojiAsset emoji)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(emoji);
        var violations = new List<SchemaViolation>();

        if (!schema.TryGetValue(emoji.Rig ?? "", out ICollection<string>? known))
        {
            violations.Add(new SchemaViolation("이모지", emoji.Name, MissingRig(schema, emoji.Rig)));
            return violations;
        }

        if (!known.Contains(emoji.AnchorNode))
        {
            violations.Add(new SchemaViolation("이모지", emoji.Name, MissingNode("자리 노드", emoji.AnchorNode, emoji.Rig!)));
        }

        // 리그가 다른 스킨은 이모지 검증이 따로 막는다 — 그 노드는 이 리그의 것이 아니다.
        foreach (SkinAsset skin in new[] { emoji.Skin, emoji.SkinFlipped }.Distinct(StringComparer.Ordinal).Select(document.FindSkin).OfType<SkinAsset>()
                     .Where(s => string.Equals(s.Rig, emoji.Rig, StringComparison.Ordinal)))
        {
            foreach (string node in skin.Parts.Keys.Where(n => !known.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
            {
                violations.Add(new SchemaViolation("이모지", emoji.Name, MissingNode($"스킨 '{skin.Name}'의 조각", node, emoji.Rig!)));
            }
        }

        return violations;
    }

    /// <summary>클립의 배역이 입는 리그. 리그를 안 적은 배역은 클립 검증이 말한다.</summary>
    public static IReadOnlyList<SchemaViolation> Clip(IReadOnlyDictionary<string, ICollection<string>> schema, ClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(clip);
        return clip.Roles
            .Where(r => !string.IsNullOrEmpty(r.Rig) && !schema.ContainsKey(r.Rig))
            .Select(r => new SchemaViolation("배역", r.Name, MissingRig(schema, r.Rig)))
            .ToList();
    }

    private static string MissingRig(IReadOnlyDictionary<string, ICollection<string>> schema, string? rig)
        => $"리그 '{rig}'가 스키마에 없다. 있는 것: [{string.Join(", ", schema.Keys)}]";

    private static string MissingNode(string what, string node, string rig)
        => $"{what} '{node}'가 리그 '{rig}'의 스키마에 없다";
}
