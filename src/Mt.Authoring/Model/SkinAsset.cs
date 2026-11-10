namespace Mt.Authoring.Model;

/// <summary>
/// 스킨 — 한 리그의 조각(그림 노드)마다 어떤 그림을 붙일지의 묶음에 이름을 붙인 것(<c>PLAN-parts.md</c> P1, Spine의 skin과 같은 자리).
/// 이모지 리그 하나에 "땀"·"꽃"·"느낌표"가 각각 스킨이고, 오브젝트는 스킨을 고른다. 그림이 없는 조각은 안 그린다.
/// 캐릭터의 초상 한 장(<see cref="SceneObject.Portrait"/>)은 스킨이 아니다 — 조각 노드가 없는 리그의 그 한 장이다.
/// 미리보기 전용이라 <c>motions.json</c>에는 안 나간다.
/// </summary>
public sealed class SkinAsset
{
    /// <summary>문서에서 하나뿐인 이름. 한글도 된다(파일 규약의 이름이 아니다).</summary>
    public string Name { get; set; } = "";

    /// <summary>이 스킨이 입히는 리그(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>조각 노드 id → 그림(문서 기준 상대 경로). 노드 이름순으로 적힌다.</summary>
    public SortedDictionary<string, string> Parts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 조각마다의 덧붙임(<c>PLAN-parts.md</c> §13) — 그림 넘기기(뒤따르는 장들)와 놓임(자리·크기·회전). 기본값뿐인 조각은 없다.
    /// 저작본에는 <see cref="SetupsOrNull"/>로 실린다(없으면 안 적는다).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SortedDictionary<string, SkinPartSetup> Setups { get; set; } = new(StringComparer.Ordinal);

    [System.Text.Json.Serialization.JsonPropertyName("setups")]
    public SortedDictionary<string, SkinPartSetup>? SetupsOrNull
    {
        get => Setups.Count == 0 ? null : Setups;
        set => Setups = value is null ? new(StringComparer.Ordinal) : new(value, StringComparer.Ordinal);
    }

    /// <summary>그 조각의 덧붙임 — 없으면 기본값(한 장, 제자리).</summary>
    public SkinPartSetup SetupOf(string node) => Setups.TryGetValue(node, out SkinPartSetup? setup) ? setup : SkinPartSetup.Default;

    /// <summary>그 조각의 장들(첫 장 + 뒤따르는 장) — 그림이 없는 조각이면 빈 목록.</summary>
    public IReadOnlyList<string> ImagesOf(string node)
    {
        if (!Parts.TryGetValue(node, out string? first))
        {
            return Array.Empty<string>();
        }

        var images = new List<string> { first };
        images.AddRange(SetupOf(node).Frames);
        return images;
    }

    /// <summary>뜬 뒤 <paramref name="seconds"/>초에 보이는 장 — 장당 시간마다 넘어가고, 되풀이하거나 마지막 장에서 멈춘다. 그림이 없으면 null.</summary>
    public string? ImageAt(string node, double seconds)
    {
        IReadOnlyList<string> images = ImagesOf(node);

        if (images.Count == 0)
        {
            return null;
        }

        SkinPartSetup setup = SetupOf(node);

        if (images.Count == 1 || setup.FrameSeconds <= 0f || seconds <= 0)
        {
            return images[0];
        }

        long frame = (long)Math.Floor((seconds + 1e-6) / setup.FrameSeconds);
        return images[(int)(setup.Loop ? frame % images.Count : Math.Min(frame, images.Count - 1))];
    }

    /// <summary>이 스킨이 쓰는 그림 전부(겹치지 않게).</summary>
    public IEnumerable<string> AllImages() => Parts.Keys.SelectMany(ImagesOf).Distinct(StringComparer.Ordinal);
}

/// <summary>
/// 스킨의 조각 하나의 덧붙임 — Spine의 어태치먼트가 뼈에 대해 갖는 자리와 sequence(그림 묶음)에 해당한다.
/// 리그의 조각은 전부 한가운데에 같은 크기로 선다. 이모지마다 다른 "조각이 어디에 얼마만 하게 놓이는가"와 "몇 장을 넘기는가"는 그림의 일이라 스킨에 둔다 —
/// 모션은 그 위에 얹힌다(같은 '톡 튀어나옴'을 꽃 셋에도, 물음표 하나에도 쓴다).
/// </summary>
public sealed class SkinPartSetup
{
    public const float DefaultFrameSeconds = 0.2f;

    public static readonly SkinPartSetup Default = new();

    /// <summary>뒤따르는 장들(문서 기준 상대 경로) — 첫 장은 <see cref="SkinAsset.Parts"/>의 그림이다. 비면 한 장.</summary>
    public List<string> Frames { get; set; } = new();

    /// <summary>한 장이 보이는 시간(초).</summary>
    public float FrameSeconds { get; set; } = DefaultFrameSeconds;

    /// <summary>마지막 장 다음에 첫 장으로 돌아가는가. 아니면 마지막 장에서 멈춘다(말줄임점이 하나씩 찍히고 선다).</summary>
    public bool Loop { get; set; } = true;

    /// <summary>놓임 — 조각 노드 안에서의 자리(px), 크기(배), 회전(도). 노드의 값(모션이 움직이는 것)과는 따로다.</summary>
    public float X { get; set; }

    public float Y { get; set; }

    public float Scale { get; set; } = 1f;

    public float Rotation { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPlaced => X != 0f || Y != 0f || Scale != 1f || Rotation != 0f;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault => Frames.Count == 0 && !IsPlaced;

    public SkinPartSetup Clone() => new() { Frames = new List<string>(Frames), FrameSeconds = FrameSeconds, Loop = Loop, X = X, Y = Y, Scale = Scale, Rotation = Rotation };
}
