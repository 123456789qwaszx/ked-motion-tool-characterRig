using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Rig;

/// <summary>
/// 이모지 리그의 **틀** — 런타임 덤프(<c>rig-schemas.json</c>의 <c>emoji</c>)에서 읽는다. 툴은 틀을 만들지 않는다(<c>docs/emoji-rig.md</c> §5).
///
/// 노드의 역할은 이름이 아니라 덤프의 모양에서 읽는다:
/// 리그마다 되풀이되는 노드(<c>repeatsPerRig</c>)의 맨 위가 <b>기본 자리</b>(<see cref="PlaceNode"/>), 맨 아래가 키를 받는 <b>그림</b>(<see cref="ImageNode"/>),
/// 그 위의 되풀이되지 않는 노드가 이모지 전체를 움직이는 <b>오프셋</b>(<see cref="OffsetNode"/>)이다.
///
/// 이모지마다 조립한 리그(<see cref="Assemble"/>): 되풀이되는 노드는 리그 이름을 머리로 단다 — <c>body/EmojiRig_Image</c>.
/// 타임라인의 채널은 오프셋 노드(이모지 전체)와 리그마다의 그림 노드만 가리킨다(<see cref="TryTarget"/>).
/// </summary>
public sealed class EmojiRigFrame
{
    /// <summary>덤프의 rigKind.</summary>
    public const string RigKind = "emoji";

    /// <summary>되풀이되는 노드와 리그 이름 사이의 구분자.</summary>
    public const char Separator = '/';

    private EmojiRigFrame(RigSchemaRigDto dto, string offset, string place, string image)
    {
        Dto = dto;
        OffsetNode = offset;
        PlaceNode = place;
        ImageNode = image;
    }

    public RigSchemaRigDto Dto { get; }

    /// <summary>이모지 전체를 움직이는 노드(<c>Emoji_Offset</c>) — 오프셋 값에 키를 더한다.</summary>
    public string OffsetNode { get; }

    /// <summary>리그의 기본 자리 · 배율 · 회전을 드는 노드(<c>EmojiRig_Place</c>) — 키를 받지 않는다.</summary>
    public string PlaceNode { get; }

    /// <summary>리그의 그림을 들고 키를 받는 노드(<c>EmojiRig_Image</c>).</summary>
    public string ImageNode { get; }

    /// <summary>내장 덤프의 틀.</summary>
    public static EmojiRigFrame Builtin => BuiltinLazy.Value;

    private static readonly Lazy<EmojiRigFrame> BuiltinLazy = new(() =>
        TryRead(BuiltinTuning.RigSchemas, out EmojiRigFrame frame, out string why) ? frame : throw new InvalidOperationException(why));

    /// <summary>덤프에서 틀을 읽는다. 이모지 리그가 없거나 모양이 기대와 다르면 거짓과 까닭.</summary>
    public static bool TryRead(RigSchemaFile file, out EmojiRigFrame frame, out string why)
    {
        ArgumentNullException.ThrowIfNull(file);
        frame = null!;

        if (!file.TryGetRig(RigKind, out RigSchemaRigDto dto) || dto.nodes is not { Count: > 0 })
        {
            why = $"리그 덤프에 '{RigKind}' 리그가 없다 — 런타임의 덤프(EmojiRigSchema)를 다시 받는다";
            return false;
        }

        var byId = dto.nodes.Where(n => n is not null && !string.IsNullOrEmpty(n.id)).ToDictionary(n => n.id, StringComparer.Ordinal);
        List<RigSchemaNodeDto> repeated = dto.nodes.Where(n => n.repeatsPerRig).ToList();
        List<RigSchemaNodeDto> tops = repeated.Where(n => !(byId.TryGetValue(n.parent ?? "", out RigSchemaNodeDto? p) && p.repeatsPerRig)).ToList();
        List<RigSchemaNodeDto> leaves = repeated.Where(n => !repeated.Any(c => string.Equals(c.parent, n.id, StringComparison.Ordinal))).ToList();

        if (tops.Count != 1 || leaves.Count != 1 || ReferenceEquals(tops[0], leaves[0]))
        {
            why = $"'{RigKind}' 리그의 모양이 기대와 다르다 — 리그마다 되풀이되는 노드가 한 줄기(기본 자리 › 그림)여야 한다 " +
                  $"(맨 위 {tops.Count}개 · 맨 아래 {leaves.Count}개)";
            return false;
        }

        string offset = tops[0].parent ?? "";

        if (!byId.TryGetValue(offset, out RigSchemaNodeDto? offsetNode) || offsetNode.repeatsPerRig || offset == RigSchemaLoader.RootKey)
        {
            why = $"'{RigKind}' 리그에 리그들을 담는 오프셋 노드가 없다(기본 자리 '{tops[0].id}'의 부모 '{offset}')";
            return false;
        }

        frame = new EmojiRigFrame(dto, offset, tops[0].id, leaves[0].id);
        why = "";
        return true;
    }

    /// <summary>리그 하나의 되풀이되는 노드 이름 — <c>body/EmojiRig_Image</c>.</summary>
    public static string RigNode(string rig, string node) => rig + Separator + node;

    /// <summary>리그의 그림 노드(키를 받는 곳).</summary>
    public string ImageNodeOf(string rig) => RigNode(rig, ImageNode);

    /// <summary>리그의 기본 자리 노드.</summary>
    public string PlaceNodeOf(string rig) => RigNode(rig, PlaceNode);

    /// <summary>
    /// 채널의 노드가 키를 받는 곳인가 — 오프셋 노드면 이모지 전체(빈 리그 이름), <c>{리그}/{그림 노드}</c>면 그 리그.
    /// 리그가 있는지는 보지 않는다(이름만 가른다).
    /// </summary>
    public bool TryTarget(string? node, out string rig)
    {
        rig = "";

        if (string.Equals(node, OffsetNode, StringComparison.Ordinal))
        {
            return true;
        }

        string suffix = Separator + ImageNode;

        if (node is not null && node.Length > suffix.Length && node.EndsWith(suffix, StringComparison.Ordinal))
        {
            rig = node[..^suffix.Length];
            return true;
        }

        return false;
    }

    /// <summary>대상(빈 이름 = 이모지 전체)의 노드.</summary>
    public string TargetNode(string rig) => string.IsNullOrEmpty(rig) ? OffsetNode : ImageNodeOf(rig);

    /// <summary>
    /// 이모지마다 조립한 리그 — 되풀이되는 노드를 리그마다 이름을 달아 세운다. 기본값(오프셋 · 리그의 기본 자리 · 배율 · 회전)을 얹는다.
    /// 그 밖의 값은 덤프 그대로다. 노드는 덤프 순서대로(부모 먼저), 리그는 이모지의 순서대로.
    /// </summary>
    public RigSchemaRigDto Assemble(EmojiAsset emoji)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        var nodes = new List<RigSchemaNodeDto>();

        foreach (RigSchemaNodeDto node in Dto.nodes.Where(n => !n.repeatsPerRig))
        {
            RigSchemaNodeDto copy = Copy(node, node.id, node.parent);

            if (node.id == OffsetNode)
            {
                copy.anchoredPosition = new Float2Dto { x = emoji.OffsetX, y = emoji.OffsetY };
            }

            nodes.Add(copy);
        }

        foreach (EmojiRigAsset rig in emoji.Rigs)
        {
            foreach (RigSchemaNodeDto node in Dto.nodes.Where(n => n.repeatsPerRig))
            {
                bool parentRepeats = Dto.nodes.Any(p => p.repeatsPerRig && string.Equals(p.id, node.parent, StringComparison.Ordinal));
                RigSchemaNodeDto copy = Copy(node, RigNode(rig.Name, node.id), parentRepeats ? RigNode(rig.Name, node.parent) : node.parent);

                if (node.id == PlaceNode)
                {
                    copy.anchoredPosition = new Float2Dto { x = rig.PlaceX, y = rig.PlaceY };
                    copy.localScale = new Float3Dto { x = rig.Scale, y = rig.Scale, z = 1f };
                    copy.localEulerAngles = new Float3Dto { x = 0f, y = 0f, z = rig.Rotation };
                }

                nodes.Add(copy);
            }
        }

        return new RigSchemaRigDto { rigKind = RigKind, sourcePrefab = Dto.sourcePrefab, nodes = nodes };
    }

    /// <summary>조립한 리그의 노드 id 전부.</summary>
    public HashSet<string> NodeIdsOf(EmojiAsset emoji)
        => new(Assemble(emoji).nodes.Select(n => n.id), StringComparer.Ordinal);

    private static RigSchemaNodeDto Copy(RigSchemaNodeDto n, string id, string? parent) => new()
    {
        id = id,
        parent = parent ?? "",
        anchoredPosition = n.anchoredPosition,
        anchorMin = n.anchorMin,
        anchorMax = n.anchorMax,
        pivot = n.pivot,
        sizeDelta = n.sizeDelta,
        localScale = n.localScale,
        localEulerAngles = n.localEulerAngles,
        measuredRectSize = n.measuredRectSize,
        hasCanvasGroup = n.hasCanvasGroup,
        canvasGroupAlpha = n.canvasGroupAlpha,
        repeatsPerRig = n.repeatsPerRig,
    };
}
