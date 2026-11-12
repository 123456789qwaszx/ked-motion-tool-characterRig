namespace Mt.Authoring.Model;

/// <summary>가져올 수 있는 움직임(<c>docs/history/PLAN-ux.md</c> U8) — 등장 둘 · 머묾 둘 · 퇴장 둘.</summary>
public enum EmojiMove
{
    PopIn,
    FadeIn,
    Pulse,
    Shake,
    FloatOut,
    ShrinkOut,
}

/// <summary>움직임의 곡선 하나 — 어느 종류의 어느 성분에, 구간 안의 어디(0…1)에 어떤 값을.</summary>
public sealed record EmojiMoveCurve(ChannelKind Kind, IReadOnlyList<Axis> Axes, IReadOnlyList<(float At, float Value, TangentMode Tangent)> Keys);

/// <summary>
/// 움직임 가져오기의 내용 — **키를 복사해 넣는 본**이다(프리셋을 가리키지 않는다. 넣은 뒤에는 그냥 키라서 그래프에서 고친다).
/// 값은 기본값에 얹히는 것이다: 자리는 더해지는 px, 크기 · 투명도는 1이 그대로, 회전은 도.
/// 런타임의 견본 셋(star · question · twinkle)의 움직임에서 뽑았다.
/// </summary>
public static class EmojiMoves
{
    public static readonly EmojiMove[] All =
    {
        EmojiMove.PopIn, EmojiMove.FadeIn, EmojiMove.Pulse, EmojiMove.Shake, EmojiMove.FloatOut, EmojiMove.ShrinkOut,
    };

    private static readonly Axis[] Xy = { Axis.X, Axis.Y };
    private static readonly Axis[] X = { Axis.X };
    private static readonly Axis[] Y = { Axis.Y };
    private static readonly Axis[] Z = { Axis.Z };

    /// <summary>화면에 적는 이름.</summary>
    public static string Label(EmojiMove move) => move switch
    {
        EmojiMove.PopIn => "튀어나오기",
        EmojiMove.FadeIn => "서서히 나타나기",
        EmojiMove.Pulse => "두근거리기",
        EmojiMove.Shake => "흔들기",
        EmojiMove.FloatOut => "위로 사라지기",
        _ => "줄어들며 사라지기",
    };

    /// <summary>한 줄 설명 — 무엇에 키가 놓이는가.</summary>
    public static string Describe(EmojiMove move) => move switch
    {
        EmojiMove.PopIn => "크기 0 → 조금 크게 → 제 크기, 투명도 0 → 1",
        EmojiMove.FadeIn => "투명도 0 → 1, 조금 아래에서 제자리로",
        EmojiMove.Pulse => "크기가 조금 커졌다 돌아온다(되풀이에 맞게 처음과 끝이 같다)",
        EmojiMove.Shake => "좌우로 기울었다 돌아온다(되풀이에 맞게 처음과 끝이 같다)",
        EmojiMove.FloatOut => "위로 올라가며 투명도 1 → 0",
        _ => "크기 1 → 0, 투명도 1 → 0",
    };

    /// <summary>이 움직임이 어울리는 수명의 자리.</summary>
    public static EmojiLifeRole Role(EmojiMove move) => move switch
    {
        EmojiMove.PopIn or EmojiMove.FadeIn => EmojiLifeRole.In,
        EmojiMove.Pulse or EmojiMove.Shake => EmojiLifeRole.Hold,
        _ => EmojiLifeRole.Out,
    };

    /// <summary>움직임의 곡선들.</summary>
    public static IReadOnlyList<EmojiMoveCurve> Curves(EmojiMove move) => move switch
    {
        EmojiMove.PopIn => new[]
        {
            new EmojiMoveCurve(ChannelKind.Scale, Xy, new[] { (0f, 0f, TangentMode.Flat), (0.6f, 1.2f, TangentMode.Flat), (1f, 1f, TangentMode.Flat) }),
            new EmojiMoveCurve(ChannelKind.Alpha, X, new[] { (0f, 0f, TangentMode.Flat), (0.4f, 1f, TangentMode.Flat) }),
        },
        EmojiMove.FadeIn => new[]
        {
            new EmojiMoveCurve(ChannelKind.Alpha, X, new[] { (0f, 0f, TangentMode.Flat), (1f, 1f, TangentMode.Flat) }),
            new EmojiMoveCurve(ChannelKind.Position, Y, new[] { (0f, -30f, TangentMode.Flat), (1f, 0f, TangentMode.Flat) }),
        },
        EmojiMove.Pulse => new[]
        {
            new EmojiMoveCurve(ChannelKind.Scale, Xy, new[] { (0f, 1f, TangentMode.Flat), (0.5f, 1.12f, TangentMode.Flat), (1f, 1f, TangentMode.Flat) }),
        },
        EmojiMove.Shake => new[]
        {
            new EmojiMoveCurve(ChannelKind.Euler, Z, new[] { (0f, 0f, TangentMode.Auto), (0.25f, 8f, TangentMode.Flat), (0.75f, -8f, TangentMode.Flat), (1f, 0f, TangentMode.Auto) }),
        },
        EmojiMove.FloatOut => new[]
        {
            new EmojiMoveCurve(ChannelKind.Position, Y, new[] { (0f, 0f, TangentMode.Flat), (1f, 60f, TangentMode.Flat) }),
            new EmojiMoveCurve(ChannelKind.Alpha, X, new[] { (0f, 1f, TangentMode.Flat), (1f, 0f, TangentMode.Flat) }),
        },
        _ => new[]
        {
            new EmojiMoveCurve(ChannelKind.Scale, Xy, new[] { (0f, 1f, TangentMode.Flat), (1f, 0f, TangentMode.Flat) }),
            new EmojiMoveCurve(ChannelKind.Alpha, X, new[] { (0.4f, 1f, TangentMode.Flat), (1f, 0f, TangentMode.Flat) }),
        },
    };

    /// <summary>
    /// 곡선의 키를 구간의 프레임으로 옮긴다(끝 포함). 같은 프레임에 둘이 떨어지면(짧은 구간) 뒤의 것이 남는다 — 끝의 모습을 지킨다.
    /// </summary>
    public static IReadOnlyList<(int Frame, float Value, TangentMode Tangent)> Place(EmojiMoveCurve curve, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var placed = new SortedDictionary<int, (float Value, TangentMode Tangent)>();

        foreach ((float at, float value, TangentMode tangent) in curve.Keys)
        {
            int frame = start + (int)Math.Round(at * (end - start), MidpointRounding.AwayFromZero);
            placed[frame] = (value, tangent);
        }

        return placed.Select(p => (p.Key, p.Value.Value, p.Value.Tangent)).ToList();
    }
}

/// <summary>
/// 새 이모지의 첫 화면(<c>docs/history/PLAN-ux.md</c> U9)이 정하는 것 — 이름 · 그림들(문서 기준 경로, 한 장에 그림 하나) ·
/// 머무는가 · 어떻게 움직이는가(없으면 null). <c>MotionEditor.StartEmoji</c>가 이것으로 이모지를 한 칸에 짓는다.
/// </summary>
public sealed record EmojiStart(string Name, IReadOnlyList<string> Images, bool Stays, EmojiMove? In, EmojiMove? Hold, EmojiMove? Out);
