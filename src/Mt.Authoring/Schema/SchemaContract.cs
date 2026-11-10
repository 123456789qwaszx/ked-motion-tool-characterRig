using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Schema;

/// <summary>스키마에 어긋난 것 하나 — 무엇이(모션 · 이모지 · 리그), 어느 것이, 왜.</summary>
public sealed record SchemaViolation(string Kind, string Subject, string Message)
{
    public override string ToString() => $"{Kind} '{Subject}': {Message}";
}

/// <summary>
/// 스키마 계약(<c>docs/PLAN-characterRig.md</c> §3) — **방향을 보이는 코드**다(아직 툴에 물리지 않았다).
///
/// 이 툴의 원칙: **구조는 유니티가 정한다.** 유니티가 덤프한 리그 스키마(<c>rig-schemas.json</c>)가 유일한 진실이고,
/// 문서(모션 · 이모지)는 그 스키마에 있는 리그와 노드만 쓴다. 이 함수는 그 계약을 한 자리에서 센다 —
/// 지금은 같은 검사가 모션 검증 · 이모지 검증 · 리그 합치기에 흩어져 있고, 일부는 알림일 뿐이다(막지 않는다).
/// 단단하게 만든다는 것은 이것을 **막는 문제**로 올리고, 덤프가 바뀌었을 때(유니티 쪽 구조가 달라졌을 때) 무엇이 끊겼는지를 한 번에 보이는 것이다.
/// </summary>
public static class SchemaContract
{
    /// <summary>문서가 스키마(덤프)에 어긋난 곳들 — 없으면 빈 목록.</summary>
    public static IReadOnlyList<SchemaViolation> Check(RigSchemaFile schema, MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(document);
        var violations = new List<SchemaViolation>();
        IReadOnlyDictionary<string, ICollection<string>> nodes = schema.NodeIds;

        // 문서가 정의한 리그 — 구조는 유니티가 정한다. 스키마에 같은 이름이 있으면 덤프를 가리고(어느 쪽이 진실인지 흐려진다), 없으면 런타임에 없는 리그다.
        foreach (RigAsset rig in document.Rigs)
        {
            violations.Add(new SchemaViolation("리그", rig.Kind, nodes.ContainsKey(rig.Kind)
                ? "문서가 스키마의 리그를 다시 정의한다 — 덤프의 것을 가린다"
                : "문서가 정의한 리그다 — 유니티의 스키마에 없다"));
        }

        foreach (MotionAsset motion in document.Motions)
        {
            if (!nodes.TryGetValue(motion.Rig, out ICollection<string>? known))
            {
                violations.Add(new SchemaViolation("모션", motion.Name, $"리그 '{motion.Rig}'가 스키마에 없다"));
                continue;
            }

            foreach (string node in motion.Channels.Select(c => c.Node).Distinct(StringComparer.Ordinal).Where(n => !known.Contains(n)))
            {
                violations.Add(new SchemaViolation("모션", motion.Name, $"노드 '{node}'가 리그 '{motion.Rig}'의 스키마에 없다"));
            }
        }

        foreach (EmojiAsset emoji in document.Emojis)
        {
            if (!nodes.ContainsKey(emoji.Rig))
            {
                violations.Add(new SchemaViolation("이모지", emoji.Name, $"리그 '{emoji.Rig}'가 스키마에 없다"));
            }
        }

        return violations;
    }
}
