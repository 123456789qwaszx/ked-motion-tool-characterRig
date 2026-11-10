namespace Mt.Authoring.Model;

/// <summary>
/// 스킨 — 한 리그의 조각(그림 노드)마다 어떤 그림을 붙일지의 묶음에 이름을 붙인 것(<c>PLAN-parts.md</c> P1, Spine의 skin과 같은 자리).
/// 이모지 리그 하나에 "땀"·"꽃"·"느낌표"가 각각 스킨이고, 오브젝트는 스킨을 고른다. 그림이 없는 조각은 안 그린다.
/// 캐릭터의 초상 한 장(<see cref="SceneObject.Portrait"/>)은 스킨이 아니다 — 조각 노드가 없는 리그의 그 한 장이다.
/// 미리보기 전용이라 <c>motions.json</c>에는 안 나간다.
/// </summary>
public sealed class SkinAsset
{
    /// <summary>문서에서 하나뿐인 이름. 한글도 된다(파일 규약의 이름이 아니다).</summary>
    public string Name { get; set; } = "";

    /// <summary>이 스킨이 입히는 리그(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>조각 노드 id → 그림(문서 기준 상대 경로). 노드 이름순으로 적힌다.</summary>
    public SortedDictionary<string, string> Parts { get; set; } = new(StringComparer.Ordinal);
}
