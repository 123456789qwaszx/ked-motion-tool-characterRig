using System.Text.RegularExpressions;
using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// 리그의 조각 — 그림을 하나씩 그리는 노드들(<c>PLAN-parts.md</c> P1).
///
/// 덤프는 아직 "어느 노드가 그림인가"를 말하지 않는다(런타임에 요청한 <c>hasImage</c>, §2.2). 그때까지는 **이름 규칙**으로 본다:
/// id가 <c>_Part&lt;숫자&gt;</c>로 끝나는 노드가 조각이다(<c>Emoji_Part1</c>…). 덤프에 표시가 생기면 이 자리 하나만 바꾼다.
/// 캐릭터의 초상 노드는 조각이 아니다 — 그 한 장은 <c>SceneObject.Portrait</c>와 초상 사이징이 맡는다.
/// </summary>
public static partial class RigParts
{
    [GeneratedRegex(@"_Part\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex PartName();

    public static bool IsPartNode(string? nodeId) => !string.IsNullOrEmpty(nodeId) && PartName().IsMatch(nodeId);

    /// <summary>리그의 조각 노드 id — 덤프에 적힌 순서(= 그리는 순서: 뒤의 것이 위).</summary>
    public static IReadOnlyList<string> Of(RigSchemaRigDto rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        // 덤프(또는 툴이 정의한 리그)가 그림 노드를 표시했으면 그것, 표시가 없는 옛 덤프는 이름 규칙으로.
        return rig.nodes.Where(n => n.hasImage || IsPartNode(n.id)).Select(n => n.id).ToList();
    }

    /// <summary>그 rigKind의 조각 노드 id. 덤프에 없는 리그면 빈 목록.</summary>
    public static IReadOnlyList<string> Of(RigSchemaFile? file, string rigKind)
        => file is not null && file.TryGetRig(rigKind, out RigSchemaRigDto dto) ? Of(dto) : Array.Empty<string>();
}
