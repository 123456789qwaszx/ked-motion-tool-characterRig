using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>모션 하나의 저작본. 수치는 코어 <see cref="MotionClip"/>과 같고, 제작 정보만 더 든다.</summary>
public sealed class MotionAsset
{
    public string Name { get; set; } = "";

    /// <summary>리그 덤프의 rigKind (예: character).</summary>
    public string Rig { get; set; } = "";

    /// <summary>초.</summary>
    public float Duration { get; set; }

    public MotionEnd End { get; set; } = MotionEnd.Restore;

    /// <summary>
    /// 뷰포트가 키 값을 어떻게 보여 줄지 — relative(시작값 + 키, 배율은 ×) 또는 absolute(키 그대로).
    /// 산출물에는 **안 나간다**: motions/2에서 mode는 재생하는 커맨드(<c>motion slot 이름 relative|absolute</c>)가 정한다.
    /// 매니퓰레이터·채널 박스가 포즈를 키 값으로 바꿀 때도 이 렌즈를 쓴다(PoseEdit.KeyValueFor).
    /// </summary>
    public MotionChannelMode PreviewMode { get; set; } = MotionChannelMode.Relative;

    public List<ChannelAsset> Channels { get; set; } = new();

    public ChannelAsset? FindChannel(string node, ChannelKind kind)
        => Channels.FirstOrDefault(c => string.Equals(c.Node, node, StringComparison.Ordinal) && c.Kind == kind);
}

/// <summary>노드 하나의 속성 하나. 성분이 null이면 "건드리지 않는다"(코어와 같은 뜻).</summary>
public sealed class ChannelAsset
{
    public string Node { get; set; } = "";
    public ChannelKind Kind { get; set; }

    public ComponentCurve? X { get; set; }
    public ComponentCurve? Y { get; set; }
    public ComponentCurve? Z { get; set; }

    public ComponentCurve? Get(Axis axis) => axis switch
    {
        Axis.X => X,
        Axis.Y => Y,
        Axis.Z => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "모르는 축"),
    };

    public void Set(Axis axis, ComponentCurve? curve)
    {
        switch (axis)
        {
            case Axis.X: X = curve; break;
            case Axis.Y: Y = curve; break;
            case Axis.Z: Z = curve; break;
            default: throw new ArgumentOutOfRangeException(nameof(axis), axis, "모르는 축");
        }
    }
}

public enum Axis
{
    X,
    Y,
    Z,
}

/// <summary>성분 하나의 키 목록. 시각 오름차순을 편집기가 지킨다.</summary>
public sealed class ComponentCurve
{
    public List<KeyAsset> Keys { get; set; } = new();
}

/// <summary>
/// 키 하나. 수치 넷은 코어 <see cref="CurveKey"/>와 1:1이고, <see cref="Tangent"/>·<see cref="Broken"/>은
/// 그래프 편집기의 손잡이 상태다 — 산출물에는 탄젠트 **값**만 나간다.
/// 계단 키(<see cref="TangentMode.Step"/>)는 저장된 나가는 기울기가 0이고, 코어로 건널 때 무한이 된다(<see cref="ToCurveKey"/>) —
/// 저작본 JSON에 무한을 싣지 않는다.
/// </summary>
public sealed class KeyAsset
{
    /// <summary>초.</summary>
    public float T { get; set; }

    public float V { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }

    /// <summary>탄젠트를 누가 정하는가. 가져온 키는 <see cref="TangentMode.Free"/>다 — 값을 그대로 지킨다.</summary>
    public TangentMode Tangent { get; set; } = TangentMode.Free;

    /// <summary>in·out 탄젠트가 따로 노는가(마야의 break tangents).</summary>
    public bool Broken { get; set; }

    /// <summary>코어 키로. 계단 키는 나가는 기울기가 무한이다 — 다음 키까지 이 값으로 멈춘다(코어 <c>CurveFunctions</c>의 규칙).</summary>
    public CurveKey ToCurveKey() => new(T, V, InTangent, Tangent == TangentMode.Step ? float.PositiveInfinity : OutTangent);

    /// <summary>코어 키에서. 나가는 기울기가 무한이면 계단 키, 아니면 <see cref="TangentMode.Free"/>다 — 값을 그대로 지킨다.</summary>
    public static KeyAsset FromCurveKey(in CurveKey key) => float.IsInfinity(key.OutTangent)
        ? new KeyAsset { T = key.Time, V = key.Value, InTangent = 0f, OutTangent = 0f, Tangent = TangentMode.Step }
        : new KeyAsset
        {
            T = key.Time,
            V = key.Value,
            InTangent = key.InTangent,
            OutTangent = key.OutTangent,
            Tangent = TangentMode.Free,
            Broken = key.InTangent != key.OutTangent,
        };
}

/// <summary>
/// 마야의 탄젠트 종류 중 툴이 쓰는 것(가중은 없다). 산수는 <c>TangentMath</c>.
/// 계단(<see cref="Step"/>)은 이모지의 타임라인에만 쓴다 — <c>motions/2</c>는 받지 않아 검증이 막는다.
/// 이름이 곧 저작 원본의 글자(소문자)다 — 바꾸면 옛 파일을 못 읽는다.
/// </summary>
public enum TangentMode
{
    /// <summary>이웃 할선, 극값·끝은 평평, 가파른 쪽의 3배 안으로(마야 auto에 가깝다).</summary>
    Auto,

    Flat,

    /// <summary>앞·뒤 키와의 할선(끊김).</summary>
    Linear,

    /// <summary>손으로 둔 값 그대로.</summary>
    Free,

    /// <summary>이웃을 잇는 할선 그대로(Catmull-Rom). 부드럽지만 넘칠 수 있다 — 마야 spline.</summary>
    Spline,

    /// <summary>auto보다 더 눌러 이웃 값을 넘지 않게. 이웃과 같은 값이면 평평 — 마야 plateau.</summary>
    Plateau,

    /// <summary>
    /// 계단 — 다음 키까지 이 값으로 멈춘다(마야 stepped, <c>emojis/2</c>의 <c>step</c>). 들어오는 기울기는 평평(0)이다.
    /// 그림 채널의 키는 언제나 이것이다(<see cref="ChannelKinds.IsAlwaysStepped"/>).
    /// </summary>
    Step,
}
