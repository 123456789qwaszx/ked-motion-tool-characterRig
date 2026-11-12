namespace Mt.Authoring.Model;

/// <summary>
/// 이모지의 타임라인을 화면에 적는 말(docs/history/PLAN-ux.md U3) — 리그 덤프의 노드 이름과 파일의 채널 이름 대신 만드는 사람의 말로.
/// 화면에만 쓴다: 저작본 · 산출물의 이름(<c>Emoji_Offset</c> · <c>body/EmojiRig_Image</c> · <c>alpha</c>)은 그대로다.
///
/// 이모지의 타임라인에서 키를 받는 노드는 둘뿐이다 — 이모지 전체(오프셋 노드)와 리그마다의 그림 노드(<c>리그/노드</c>).
/// 그래서 노드 이름만으로 대상을 읽는다: 구분자가 있으면 그 앞이 리그(그림)의 이름, 없으면 이모지 전체.
/// </summary>
public static class EmojiWords
{
    /// <summary>이모지 전체(<c>Emoji_Offset</c>)를 가리키는 말.</summary>
    public const string WholeEmoji = "이모지 전체";

    /// <summary>되풀이되는 노드와 리그 이름 사이의 구분자 — <c>EmojiRigFrame.Separator</c>와 같다.</summary>
    private const char Separator = '/';

    /// <summary>키를 받는 대상 — 그림(리그)의 이름, 또는 이모지 전체.</summary>
    public static string Target(string node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int cut = node.IndexOf(Separator);
        return cut > 0 ? node[..cut] : WholeEmoji;
    }

    /// <summary>움직이는 것 — 자리 · 크기 · 회전 · 투명도 · 그림.</summary>
    public static string Kind(ChannelKind kind) => kind switch
    {
        ChannelKind.Position => "자리",
        ChannelKind.Scale => "크기",
        ChannelKind.Euler => "회전",
        ChannelKind.Alpha => "투명도",
        ChannelKind.Image => "그림",
        _ => ChannelKindLabel.Of(kind),
    };

    /// <summary>성분 하나 — 자리와 크기는 축을 붙이고(자리 X), 성분이 하나뿐인 것은 그대로(투명도).</summary>
    public static string Component(ChannelKind kind, Axis axis)
        => kind is ChannelKind.Position or ChannelKind.Scale ? $"{Kind(kind)} {axis.ToString().ToUpperInvariant()}" : Kind(kind);
}
