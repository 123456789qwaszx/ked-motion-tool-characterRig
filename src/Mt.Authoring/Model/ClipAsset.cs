using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 연출 클립 — 씬 위의 시간표. 씬이 시작 상태이고, 배역마다 트랙에 모션 블록을 시간차로 놓는다.
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
    public string Role { get; set; } = "";

    /// <summary>시작 시각 오름차순을 편집기가 지킨다.</summary>
    public List<ClipBlock> Blocks { get; set; } = new();
}

/// <summary>
/// 블록 하나 — 모션 하나를 <see cref="At"/>부터 그 모션의 길이(× <see cref="Cycles"/>)만큼 재생한다(자르기 · 속도는 없다).
/// mode는 블록이 정한다: <c>motions/2</c>에서 relative/absolute는 "쓰는 쪽"의 인자이고, 클립에서는 블록이 그 자리다.
/// </summary>
public sealed class ClipBlock
{
    /// <summary>초. 0 이상.</summary>
    public float At { get; set; }

    /// <summary>모션 이름. 문서에 없는 이름이 남을 수 있다(모션을 지운 뒤) — 검증이 알린다.</summary>
    public string Motion { get; set; } = "";

    public MotionChannelMode Mode { get; set; } = MotionChannelMode.Relative;

    /// <summary>
    /// 반복 횟수 — 그 모션을 이어서 몇 번 도는가. null·1이면 한 번. 블록의 길이는 모션 길이 × 횟수다.
    /// 뜻은 **같은 블록을 끝에 이어 그만큼 놓은 것**과 같다: 유지(commit) 모션은 돌 때마다 남긴 값 위에 다시 얹히고(상대값이면 쌓인다),
    /// 복원(restore) 모션은 매번 시작 자세로 돌아와 다시 돈다. 그래서 정지 프레임은 motion 커맨드를 그 횟수만큼 부른 것과 같다.
    /// 끝없이 도는 반복은 클립의 것이 아니라 재생하는 쪽의 인자다 — 클립은 길이가 있다.
    /// </summary>
    public int? Repeat { get; set; }

    /// <summary>도는 횟수 — 1 이상.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Cycles => Math.Max(1, Repeat ?? 1);

    /// <summary>
    /// 끈 블록(true) — 놓인 자리는 그대로 두고 적용만 뺀다: 미리보기·굽기·겹침 검사에서 없는 것처럼 본다. 문서에 남는다(되돌리기 한 칸).
    /// 켜져 있으면 null로 적는다(파일에 안 나온다). 배역 끄기(줄 머리)는 보기 상태이고, 이것은 블록 하나의 저작 상태다.
    /// </summary>
    public bool? Muted { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMuted => Muted == true;

    public ClipBlock Clone() => (ClipBlock)MemberwiseClone();

    /// <summary>타임라인 막대와 문제 문구에 쓰는 짧은 이름.</summary>
    public string Describe() => Cycles > 1 ? $"{Motion} ×{Cycles}" : Motion;
}

/// <summary>클립의 시간 셈 — 블록 길이는 참조하는 모션의 길이다.</summary>
public static class ClipTiming
{
    /// <summary>블록의 길이 — 그 모션의 길이 × 도는 횟수(모션이 문서에 없으면 0).</summary>
    public static float LengthOf(MotionDocument document, ClipBlock block)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        return (document.Find(block.Motion)?.Duration ?? 0f) * block.Cycles;
    }

    /// <summary>블록이 끝나는 시각.</summary>
    public static float EndOf(MotionDocument document, ClipBlock block) => block.At + LengthOf(document, block);

    /// <summary>그 모션을 가리키는 블록의 수 — 문서의 모든 클립에서. 모션은 공유 자산이라 하나를 고치면 이만큼이 같이 바뀐다.</summary>
    public static int UsesOf(MotionDocument document, string motion)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Clips.SelectMany(c => c.Tracks).SelectMany(t => t.Blocks).Count(b => string.Equals(b.Motion, motion, StringComparison.Ordinal));
    }

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
