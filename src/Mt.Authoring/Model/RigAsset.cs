using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 툴에서 정의한 리그(<c>PLAN-parts.md</c> §9) — 캐릭터에 붙는 작은 리그(이모지 등)의 뼈대. Spine처럼 **툴이 원본**이고 런타임은 내보낸 것으로 세운다.
/// 캐릭터 리그는 여전히 유니티의 덤프가 원본이다(프리팹·깊이·자리가 그 구조에 맞물려 있다) — 여기서 정의하는 것은 새로 만드는 붙는 리그뿐이다.
///
/// 노드는 단순하다: 부모의 가운데에 앵커·피벗을 둔 사각형(위치 · 크기) + 그림을 그리는가 + 불투명도를 받는가.
/// 루트(<c>__root</c>)는 적지 않는다 — 내보낼 때 붙는다(부모를 꽉 채운다). 부모가 빈 노드는 루트 밑이다.
/// </summary>
public sealed class RigAsset
{
    /// <summary>rigKind — 모션·스킨·이모지가 이 이름으로 가리킨다. 영문자로 시작하는 영문·숫자·_.</summary>
    public string Kind { get; set; } = "";

    /// <summary>부모가 자식보다 먼저 온다. 그리는 순서도 이 순서다(뒤의 것이 위).</summary>
    public List<RigNodeAsset> Nodes { get; set; } = new();

    public RigNodeAsset? Find(string id) => Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
}

public sealed class RigNodeAsset
{
    public string Id { get; set; } = "";

    /// <summary>부모 노드의 id. 비면 루트 밑.</summary>
    public string Parent { get; set; } = "";

    /// <summary>부모 가운데에서의 위치(px).</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>사각형의 크기(px). 그림 노드면 그림이 이 안에 종횡비로 맞춰진다. 0이면 점(묶는 노드).</summary>
    public float Width { get; set; }

    public float Height { get; set; }

    /// <summary>그림을 그리는 노드(조각)인가 — 스킨이 여기에 그림을 준다.</summary>
    public bool Image { get; set; }

    /// <summary>불투명도를 받는가(CanvasGroup) — 알파 채널을 놓을 수 있다.</summary>
    public bool Alpha { get; set; }
}

/// <summary>툴의 리그 ↔ 코어의 리그 스키마. 해석(트리 세우기)은 코어가 한다.</summary>
public static class RigAssetCodec
{
    public const string RootId = RigSchemaLoader.RootKey;

    /// <summary>코어 스키마로 — 루트를 붙이고, 노드는 가운데 앵커·가운데 피벗으로.</summary>
    public static RigSchemaRigDto ToDto(RigAsset rig, Vec2 stageSize)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var nodes = new List<RigSchemaNodeDto>
        {
            new()
            {
                id = RootId,
                parent = "",
                anchoredPosition = F2(0f, 0f),
                anchorMin = F2(0f, 0f),
                anchorMax = F2(1f, 1f),
                pivot = F2(0.5f, 0.5f),
                sizeDelta = F2(0f, 0f),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(stageSize.X, stageSize.Y),
                canvasGroupAlpha = 1f,
            },
        };

        foreach (RigNodeAsset node in rig.Nodes)
        {
            nodes.Add(new RigSchemaNodeDto
            {
                id = node.Id,
                parent = string.IsNullOrEmpty(node.Parent) ? RootId : node.Parent,
                anchoredPosition = F2(node.X, node.Y),
                anchorMin = F2(0.5f, 0.5f),
                anchorMax = F2(0.5f, 0.5f),
                pivot = F2(0.5f, 0.5f),
                sizeDelta = F2(node.Width, node.Height),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(node.Width, node.Height),
                hasCanvasGroup = node.Alpha,
                canvasGroupAlpha = 1f,
                hasImage = node.Image,
            });
        }

        return new RigSchemaRigDto { rigKind = rig.Kind, sourcePrefab = "", nodes = nodes };
    }

    private static Float2Dto F2(float x, float y) => new() { x = x, y = y };

    private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };
}

/// <summary>리그의 본 — 처음부터 노드를 하나씩 짓지 않게.</summary>
public static class RigTemplates
{
    public const string EmojiKind = "emoji";
    public const int DefaultParts = 4;
    public const float DefaultPartSize = 160f;

    /// <summary>
    /// 이모지 리그: <c>Emoji_Anchor</c>(자리) › <c>Emoji_Body</c>(묶음 전체의 움직임·불투명도) › <c>Emoji_Part1…N</c>(조각 그림 — 따로 움직이고 따로 흐려진다).
    /// </summary>
    public static RigAsset Emoji(string kind = EmojiKind, int parts = DefaultParts, float partSize = DefaultPartSize)
    {
        var rig = new RigAsset
        {
            Kind = kind,
            Nodes =
            {
                new RigNodeAsset { Id = "Emoji_Anchor" },
                new RigNodeAsset { Id = "Emoji_Body", Parent = "Emoji_Anchor", Alpha = true },
            },
        };

        for (int i = 1; i <= parts; i++)
        {
            rig.Nodes.Add(new RigNodeAsset { Id = $"Emoji_Part{i}", Parent = "Emoji_Body", Width = partSize, Height = partSize, Image = true, Alpha = true });
        }

        return rig;
    }
}
