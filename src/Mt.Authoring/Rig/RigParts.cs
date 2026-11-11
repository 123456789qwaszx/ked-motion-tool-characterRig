using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// 리그의 조각 — 스킨이 그림을 주는 노드들(<c>PLAN-parts.md</c> P1). 덤프가 그림 노드를 표시한다(<c>hasImage</c>).
/// 캐릭터 리그의 그림 노드(초상 · 겹침)는 조각이 아니다 — 그 한 장은 <c>SceneObject.Portrait</c>와 초상 사이징이 맡는다.
/// </summary>
public static class RigParts
{
    /// <summary>리그의 조각 노드 id — 덤프에 적힌 순서(= 그리는 순서: 뒤의 것이 위).</summary>
    public static IReadOnlyList<string> Of(RigSchemaRigDto rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return rig.rigKind == Model.SceneObject.DefaultRig ? Array.Empty<string>() : rig.nodes.Where(n => n.hasImage).Select(n => n.id).ToList();
    }

    /// <summary>그 rigKind의 조각 노드 id. 덤프에 없는 리그면 빈 목록.</summary>
    public static IReadOnlyList<string> Of(RigSchemaFile? file, string rigKind)
        => file is not null && file.TryGetRig(rigKind, out RigSchemaRigDto dto) ? Of(dto) : Array.Empty<string>();
}
