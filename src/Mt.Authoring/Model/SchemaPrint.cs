namespace Mt.Authoring.Model;

/// <summary>
/// 문서를 지을 때의 스키마(<c>docs/PLAN-characterRig.md</c> §3의 2) — 덤프의 리그마다 노드와 그 부모.
/// 해시(<see cref="RigSchemasReference.Sha256"/>)는 "달라졌다"만 말한다. 이것이 있어야 **무엇이** 달라졌는지 말할 수 있다
/// (<c>Schema.SchemaDiff</c>). 덤프의 리그만 적는다 — 문서가 정의한 리그는 문서 안에 이미 있다.
/// </summary>
public sealed class SchemaPrint
{
    public List<SchemaPrintRig> Rigs { get; set; } = new();
}

public sealed class SchemaPrintRig
{
    public string Kind { get; set; } = "";

    /// <summary>
    /// 덤프의 순서 그대로(부모가 자식보다 먼저). 한 줄이 노드 하나 — <c>"id &lt; parent"</c>, 부모가 없으면 <c>"id"</c>.
    /// 글 하나로 적는 것은 저작본에서 노드 하나가 한 줄이게 하려는 것이다.
    /// </summary>
    public List<string> Nodes { get; set; } = new();

    private const string Separator = " < ";

    public static string Entry(string id, string? parent) => string.IsNullOrEmpty(parent) ? id : id + Separator + parent;

    /// <summary>(id, 부모) — 부모가 없으면 빈 글.</summary>
    public IEnumerable<(string Id, string Parent)> Parsed()
    {
        foreach (string entry in Nodes)
        {
            int at = entry.IndexOf(Separator, StringComparison.Ordinal);
            yield return at < 0 ? (entry, "") : (entry[..at], entry[(at + Separator.Length)..]);
        }
    }
}
