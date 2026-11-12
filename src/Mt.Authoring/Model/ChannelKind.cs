using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 툴이 저작하는 채널 종류(<c>docs/history/PLAN-emoji.md</c> §3). 코어의 <see cref="MotionChannelKind"/> 셋(자리 · 배율 · 회전)에
/// 이모지의 타임라인만 쓰는 둘(불투명도 · 그림)을 더한 것이다. 코어 사본은 고치지 않는다 — 넷째 · 다섯째는 툴의 것이고,
/// <c>motions/2</c>로 나갈 때는 <see cref="ChannelKinds.ToCore"/>로 앞의 셋만 건너간다.
/// 이름이 곧 저작 원본의 글자(소문자)다 — 앞의 셋은 코어와 같은 이름이라 옛 저작본이 그대로 읽힌다.
/// </summary>
public enum ChannelKind
{
    /// <summary>자리(X · Y). 리그 로컬 px.</summary>
    Position,

    /// <summary>배율(X · Y).</summary>
    Scale,

    /// <summary>회전(X · Y · Z). 도. 화면은 rotate로 부른다.</summary>
    Euler,

    /// <summary>불투명도 0~1(X 하나). 이모지의 타임라인에만 쓴다 — 캐릭터 모션(<c>motions/2</c>)은 받지 않는다.</summary>
    Alpha,

    /// <summary>
    /// 그림 바꾸기(X 하나) — 값은 리그의 그림 목록에서 몇 번째인가(0이 기본 스프라이트). 키는 언제나 계단이다.
    /// 이모지의 타임라인에만 쓴다.
    /// </summary>
    Image,
}

/// <summary>채널 종류마다 다른 것 — 성분 · 중립값 · 어디에 쓰는가 · 코어로 건너가는 길. 종류의 규칙은 여기 한 곳이다.</summary>
public static class ChannelKinds
{
    private static readonly Axis[] Xy = { Axis.X, Axis.Y };
    private static readonly Axis[] Xyz = { Axis.X, Axis.Y, Axis.Z };
    private static readonly Axis[] XOnly = { Axis.X };

    /// <summary>그 종류가 가질 수 있는 성분. 회전은 셋, 자리 · 배율은 둘, 불투명도 · 그림은 X 하나.</summary>
    public static IReadOnlyList<Axis> Axes(ChannelKind kind) => kind switch
    {
        ChannelKind.Euler => Xyz,
        ChannelKind.Alpha or ChannelKind.Image => XOnly,
        _ => Xy,
    };

    public static bool HasAxis(ChannelKind kind, Axis axis) => Axes(kind).Contains(axis);

    /// <summary>키가 없을 때의 값이자 relative의 기준 — 자리 · 회전 · 그림 0, 배율 · 불투명도 1(코어 <c>MotionMath.Neutral</c> · <c>EmojiClipMath.Neutral</c>과 같다).</summary>
    public static float Neutral(ChannelKind kind) => kind is ChannelKind.Scale or ChannelKind.Alpha ? 1f : 0f;

    /// <summary>이모지의 타임라인에만 쓰는 종류인가(불투명도 · 그림). 캐릭터 모션에 있으면 검증이 막는다.</summary>
    public static bool IsEmojiOnly(ChannelKind kind) => kind is ChannelKind.Alpha or ChannelKind.Image;

    /// <summary>키가 언제나 계단인 종류인가(그림). 편집기가 그 채널의 키를 계단으로 둔다.</summary>
    public static bool IsAlwaysStepped(ChannelKind kind) => kind == ChannelKind.Image;

    /// <summary>코어 종류로. 불투명도 · 그림은 코어에 없다 — 검증이 앞에서 막으므로 여기 오면 잘못 부른 것이다.</summary>
    public static MotionChannelKind ToCore(ChannelKind kind) => TryToCore(kind, out MotionChannelKind core)
        ? core
        : throw new InvalidOperationException($"채널 종류 {ChannelKindLabel.Of(kind)}는 motions/2에 없다 — 이모지의 타임라인에만 쓴다.");

    public static bool TryToCore(ChannelKind kind, out MotionChannelKind core)
    {
        switch (kind)
        {
            case ChannelKind.Position: core = MotionChannelKind.Position; return true;
            case ChannelKind.Scale: core = MotionChannelKind.Scale; return true;
            case ChannelKind.Euler: core = MotionChannelKind.Euler; return true;
            default: core = default; return false;
        }
    }

    public static ChannelKind FromCore(MotionChannelKind kind) => kind switch
    {
        MotionChannelKind.Position => ChannelKind.Position,
        MotionChannelKind.Scale => ChannelKind.Scale,
        MotionChannelKind.Euler => ChannelKind.Euler,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "모르는 코어 채널 종류"),
    };
}
