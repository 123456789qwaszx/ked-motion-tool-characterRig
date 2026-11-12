namespace Mt.Authoring.Model;

/// <summary>수명의 세 자리 — 구간을 잇는 곳.</summary>
public enum EmojiLifeRole
{
    In,
    Hold,
    Out,
}

/// <summary>
/// 수명의 띠(<c>docs/PLAN-ux.md</c> U5)가 쓰는 말과 셈 — 자리의 이름, 그 자리에 이은 구간, 새 구간이 놓일 프레임.
/// 문서를 바꾸지 않는다(바꾸는 것은 <c>MotionEditor.AddEmojiLifeSpan</c>).
/// </summary>
public static class EmojiLife
{
    public static readonly EmojiLifeRole[] Roles = { EmojiLifeRole.In, EmojiLifeRole.Hold, EmojiLifeRole.Out };

    /// <summary>화면에 적는 이름.</summary>
    public static string Label(EmojiLifeRole role) => role switch
    {
        EmojiLifeRole.In => "등장",
        EmojiLifeRole.Hold => "머묾",
        _ => "퇴장",
    };

    /// <summary>새 구간에 붙이는 이름의 머리 — 예시 문서의 관습(in · hold · out).</summary>
    public static string SpanStem(EmojiLifeRole role) => role switch
    {
        EmojiLifeRole.In => "in",
        EmojiLifeRole.Hold => "hold",
        _ => "out",
    };

    /// <summary>그 자리에 이은 구간의 이름(없으면 null).</summary>
    public static string? LinkOf(EmojiAsset emoji, EmojiLifeRole role)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        return role switch
        {
            EmojiLifeRole.In => emoji.In,
            EmojiLifeRole.Hold => emoji.Hold,
            _ => emoji.Out,
        };
    }

    /// <summary>구간이 이어진 자리(어디에도 잇지 않았으면 null). 여러 자리에 이었으면 앞의 것.</summary>
    public static EmojiLifeRole? RoleOf(EmojiAsset emoji, string spanName)
    {
        ArgumentNullException.ThrowIfNull(emoji);

        foreach (EmojiLifeRole role in Roles)
        {
            if (string.Equals(LinkOf(emoji, role), spanName, StringComparison.Ordinal))
            {
                return role;
            }
        }

        return null;
    }

    /// <summary>아직 없는 구간 이름 — in, in2, in3 …</summary>
    public static string FreeSpanName(EmojiAsset emoji, EmojiLifeRole role)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        string stem = SpanStem(role);
        string name = stem;

        for (int i = 2; emoji.FindSpan(name) is not null; i++)
        {
            name = stem + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }

    /// <summary>
    /// 새 구간이 놓일 프레임(끝 포함). 이미 이은 이웃이 있으면 그 사이의 빈 곳을 채우고, 없으면 타임라인을 셋으로 나눈 제 몫이다.
    /// 빈 곳이 없으면(이웃이 붙어 있으면) 이웃의 끝 한 프레임에 겹쳐 놓는다 — 끌어서 고친다.
    /// </summary>
    public static (int Start, int End) Suggest(EmojiAsset emoji, EmojiLifeRole role)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        int last = Math.Max(0, emoji.LastFrame);
        int third = Math.Max(1, (last + 1) / 3);
        EmojiSpan? @in = emoji.FindSpan(emoji.In);
        EmojiSpan? hold = emoji.FindSpan(emoji.Hold);
        EmojiSpan? @out = emoji.FindSpan(emoji.Out);

        int start;
        int end;

        switch (role)
        {
            case EmojiLifeRole.In:
                start = 0;
                end = (hold?.Start ?? @out?.Start ?? third) - 1;
                break;
            case EmojiLifeRole.Hold:
                start = @in is not null ? @in.End + 1 : third;
                end = @out is not null ? @out.Start - 1 : Math.Max(start, last - third);
                break;
            default:
                start = hold is not null ? hold.End + 1 : @in is not null ? Math.Max(@in.End + 1, last - third + 1) : last - third + 1;
                end = last;
                break;
        }

        start = Math.Clamp(start, 0, last);
        end = Math.Clamp(end, 0, last);
        return end < start ? (start, start) : (start, end);
    }

    /// <summary>수명을 한 줄로 — 머무는 이모지인가, 한 번 재생하고 사라지는가.</summary>
    public static string Describe(EmojiAsset emoji)
    {
        ArgumentNullException.ThrowIfNull(emoji);

        if (!emoji.Stays)
        {
            return $"한 번 재생하고 사라진다 — 0~{emoji.LastFrame}fr 전체. 머묾을 두면 머무는 이모지가 된다";
        }

        string @in = emoji.In is null ? "등장 없이 바로" : "등장 한 번 →";
        string @out = emoji.Out is null ? "내리면 바로 사라진다" : "내리면(emoji_off) 퇴장";
        return $"머무는 이모지 — {@in} 머묾 되풀이 → {@out}";
    }
}
