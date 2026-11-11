namespace Mt.Authoring.Model;

/// <summary>
/// 채널 종류의 화면 이름. 파일 규약(<c>motions/2</c>)은 <c>position · scale · euler</c>를 그대로 쓰고,
/// 화면만 <c>rotate</c>로 부른다(소유자 요청). 저작본·산출물 JSON에는 쓰지 않는다.
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
}
