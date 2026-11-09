using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 연출 클립 — 씬 위의 시간표(<c>PLAN-clip.md</c>). 씬이 시작 상태이고, 배역마다 트랙에 모션 블록을 시간차로 놓는다.
///
/// 모션은 **참조**한다: 블록은 이름·시작 시각·mode만 들고 키는 <see cref="MotionDocument.Motions"/>의 모션 하나에 있다.
/// 규칙(겹침·리그 맞춤·길이)은 검증이 알린다 — 편집기는 모션과 마찬가지로 중간 상태를 막지 않는다.
/// </summary>
public sealed class ClipAsset
{
    /// <summary>모션과 같은 이름 규칙(<c>[a-z0-9_]+</c>). 클립끼리 하나뿐이다.</summary>
    public string Name { get; set; } = "";

    /// <summary>초. null이면 마지막 블록의 끝(<see cref="ClipTiming.DurationOf"/>).</summary>
    public float? Duration { get; set; }

    /// <summary>미리보기 시작 상태인 씬 이름 — 편집기는 늘 적는다(null은 손으로 쓴 파일뿐이고 활성 씬으로 읽는다). 산출물에는 안 나간다.</summary>
    public string? Scene { get; set; }

    public List<ClipRole> Roles { get; set; } = new();

    public List<ClipTrack> Tracks { get; set; } = new();

    public ClipRole? FindRole(string? name)
        => name is null ? null : Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// 배역 — 클립의 출연자 자리. 모션이 rigKind에 묶이고 슬롯에는 재생 때 묶이듯, 클립도 특정 슬롯이 아니라 배역에 묶인다.
/// </summary>
public sealed class ClipRole
{
    /// <summary>클립 안의 이름(<c>[a-z0-9_]+</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>이 배역이 받는 리그(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>미리보기에서 이 배역을 맡는 씬 오브젝트 id. null이면 아직 안 묶였다. 산출물에는 안 나간다.</summary>
    public string? Object { get; set; }
}

/// <summary>트랙 — 배역 하나의 블록 한 줄. 한 배역이 트랙을 여럿 가질 수 있다(다른 노드를 쓰는 모션을 겹쳐 놓을 때).</summary>
public sealed class ClipTrack
{
    /// <summary>카메라 트랙의 배역 자리 — 배역 목록에는 없다. 카메라 블록만 놓인다.</summary>
    public const string CameraRole = "@camera";

    public string Role { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCamera => string.Equals(Role, CameraRole, StringComparison.Ordinal);

    /// <summary>시작 시각 오름차순을 편집기가 지킨다.</summary>
    public List<ClipBlock> Blocks { get; set; } = new();
}

/// <summary>블록의 종류 — 모션, 또는 씬 값을 바꾸는 상태 블록 다섯(<c>PLAN-clip.md</c> §2.4).</summary>
public enum ClipBlockKind
{
    /// <summary>모션 하나를 재생한다.</summary>
    Motion,

    /// <summary>보이기/숨기기(<see cref="SceneObject.Visible"/>) — 길이가 있으면 페이드.</summary>
    Visible,

    /// <summary>자리(<see cref="SceneObject.Place"/> · <see cref="SceneObject.PlaceFocus"/>) — 화면 지점으로.</summary>
    Place,

    /// <summary>깊이 층(<see cref="SceneObject.Layer"/>).</summary>
    Layer,

    /// <summary>오프셋·회전·배율(<see cref="SceneObject.PositionX"/> …) — 적은 값만 바뀐다.</summary>
    Offset,

    /// <summary>카메라 zoom·pan(<see cref="SceneAsset.Camera"/>) — <see cref="ClipTrack.CameraRole"/> 트랙에만.</summary>
    Camera,
}

/// <summary>
/// 블록 하나. <see cref="Kind"/>가 Motion이면 모션 하나를 <see cref="At"/>부터 그 모션의 길이만큼 재생한다(자르기·속도·반복은 없다).
/// mode는 블록이 정한다: <c>motions/2</c>에서 relative/absolute는 "쓰는 쪽"의 인자이고, 클립에서는 블록이 그 자리다.
///
/// 그 밖의 종류는 **상태 블록**이다 — 씬 오브젝트(또는 카메라)가 이미 가진 값을 <see cref="At"/>부터 <see cref="Duration"/> 동안
/// <see cref="Ease"/>로 **그 값이 되게** 바꾼다. 끝난 뒤의 무대는 그 값으로 세운 씬과 같다(코어 폴드). 값 칸은 종류에 맞는 것만 쓰고, 비운(null) 칸은 안 바뀐다.
/// </summary>
public sealed class ClipBlock
{
    /// <summary>초. 0 이상.</summary>
    public float At { get; set; }

    public ClipBlockKind Kind { get; set; } = ClipBlockKind.Motion;

    /// <summary>모션 이름(모션 블록). 문서에 없는 이름이 남을 수 있다(모션을 지운 뒤) — 검증이 알린다.</summary>
    public string Motion { get; set; } = "";

    public MotionChannelMode Mode { get; set; } = MotionChannelMode.Relative;

    /// <summary>상태 블록의 길이(초). 0·null이면 그 순간에 바뀐다. 모션 블록은 쓰지 않는다(길이 = 모션의 길이).</summary>
    public float? Duration { get; set; }

    /// <summary>상태 블록의 이징. null이면 linear.</summary>
    public EaseKind? Ease { get; set; }

    public bool? Visible { get; set; }

    /// <summary>화면 지점 이름. 빈 문자열이면 자리를 푼다(place 없음).</summary>
    public string? Place { get; set; }

    public string? PlaceFocus { get; set; }

    public string? Layer { get; set; }

    public float? X { get; set; }
    public float? Y { get; set; }
    public float? Rotation { get; set; }
    public float? ScaleX { get; set; }
    public float? ScaleY { get; set; }

    public float? Zoom { get; set; }
    public float? PanX { get; set; }
    public float? PanY { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMotion => Kind == ClipBlockKind.Motion;

    public ClipBlock Clone() => (ClipBlock)MemberwiseClone();

    /// <summary>타임라인 막대와 문제 문구에 쓰는 짧은 이름.</summary>
    public string Describe() => Kind switch
    {
        ClipBlockKind.Motion => Motion,
        ClipBlockKind.Visible => Visible == false ? "숨기기" : "보이기",
        ClipBlockKind.Place => string.IsNullOrEmpty(Place) ? "자리 풀기" : $"자리 {Place}",
        ClipBlockKind.Layer => $"층 {Layer}",
        ClipBlockKind.Offset => "오프셋",
        ClipBlockKind.Camera => "카메라",
        _ => Kind.ToString(),
    };
}

/// <summary>클립의 시간 셈 — 블록 길이는 참조하는 모션의 길이다.</summary>
public static class ClipTiming
{
    /// <summary>블록의 길이 — 모션 블록은 그 모션의 길이(문서에 없으면 0), 상태 블록은 적어 둔 길이.</summary>
    public static float LengthOf(MotionDocument document, ClipBlock block)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        return block.IsMotion ? document.Find(block.Motion)?.Duration ?? 0f : Math.Max(0f, block.Duration ?? 0f);
    }

    /// <summary>블록이 끝나는 시각.</summary>
    public static float EndOf(MotionDocument document, ClipBlock block) => block.At + LengthOf(document, block);

    /// <summary>마지막 블록의 끝. 블록이 없으면 0.</summary>
    public static float EndOfBlocks(MotionDocument document, ClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);
        float end = 0f;

        foreach (ClipTrack track in clip.Tracks)
        {
            foreach (ClipBlock block in track.Blocks)
            {
                end = Math.Max(end, EndOf(document, block));
            }
        }

        return end;
    }

    /// <summary>클립 길이 — 적어 둔 값, 없으면 마지막 블록의 끝.</summary>
    public static float DurationOf(MotionDocument document, ClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return clip.Duration ?? EndOfBlocks(document, clip);
    }
}
