namespace Mt.Authoring.Model;

/// <summary>
/// 채널 종류의 화면 이름. 파일 규약(<c>motions/2</c>)은 <c>position · scale · euler</c>를 그대로 쓰고,
/// 화면만 <c>rotate</c>로 부른다(소유자 요청). 저작본·산출물 JSON에는 쓰지 않는다.
/// 파일을 가리키는 말(검증 문구 · <c>mt</c>의 출력)은 <see cref="File"/>을 쓴다 — 읽은 사람이 그대로 파일에 적기 때문이다.
/// </summary>
public static class ChannelKindLabel
{
    public static string Of(ChannelKind kind) => kind switch
    {
        ChannelKind.Position => "position",
        ChannelKind.Scale => "scale",
        ChannelKind.Euler => "rotate",
        ChannelKind.Alpha => "alpha",
        ChannelKind.Image => "image",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>파일에 적는 이름 — <c>"kind": "euler"</c>.</summary>
    public static string File(ChannelKind kind) => kind.ToString().ToLowerInvariant();
}
