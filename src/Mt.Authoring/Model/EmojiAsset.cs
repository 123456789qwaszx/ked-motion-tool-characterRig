using System.Text.Json.Serialization;
using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 이모지 하나의 저작본(<c>docs/PLAN-emoji.md</c> §1 · §2 · §4). 런타임 정의(<c>docs/emoji-rig.md</c>)에서 **툴이 정하는 것** 전부다:
/// 오프셋 · 리그들(이름 · 그림 · 기본 자리 · 배율 · 회전) · 자기 타임라인 하나 · 이름 붙은 구간 · 구간을 등장/머묾/퇴장에 잇기.
///
/// 리그의 **틀**(노드와 채널)은 런타임 덤프의 것이다(<see cref="Rig.EmojiRigFrame"/>). 툴은 틀에 맞는 리그를 몇 개 놓을지만 정한다.
/// 타임라인은 이 이모지가 소유한 모션이다(<see cref="Timeline"/>) — 편집 기반(그래프 · 도프 시트 · 키 연산 · 되돌리기)을 그대로 쓰고,
/// <c>motions.json</c>에는 나가지 않는다. 내보내기는 <c>emojis.json</c>(<c>emojis/2</c>)이다.
/// </summary>
public sealed class EmojiAsset
{
    /// <summary>타임라인 모션의 이름 머리 — 모션 이름 규칙(<c>[a-z0-9_]+</c>) 밖의 글자라 모션과 겹치지 않는다.</summary>
    public const string TimelinePrefix = "emoji:";

    /// <summary>머묾을 잇지 않은 이모지가 내보내는 클립 — 타임라인 전체.</summary>
    public const string MainClip = "main";

    /// <summary>초당 프레임 수 — 저작본의 초를 내보낼 때 프레임으로 바꾼다(코어 <see cref="EmojiDefinition.DefaultFps"/>).</summary>
    public const float Fps = EmojiDefinition.DefaultFps;

    /// <summary>대본이 부르는 이름(<c>&lt;&lt;emoji 누구 이름&gt;&gt;</c>). 한 번 내보낸 뒤에는 바꾸지 않는다 — 이름이 계약이다.</summary>
    public string Name { get; set; } = "";

    /// <summary>이모지별 오프셋(<c>Emoji_Offset</c>) — 로케이터 기준 px, y 위.</summary>
    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    /// <summary>리그들 — 순서가 그려지는 순서다(뒤가 위).</summary>
    public List<EmojiRigAsset> Rigs { get; set; } = new();

    /// <summary>
    /// 인물의 방향을 따르지 않는다(<c>fixedSide</c>, 런타임 <c>docs/emoji-format.md</c> §6) — 인물이 뒤집혀도 만든 쪽 그대로 선다
    /// (로케이터의 자리만 인물을 따라간다). 이모지는 오른쪽을 보는 인물을 기준으로 만들고, 보통은 인물이 뒤집히면 통째로 거울에 비친다.
    /// 거짓이면 저작본 · <c>emojis.json</c>에 적지 않는다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FixedSide { get; set; }

    /// <summary>
    /// 이모지의 타임라인 — 이 이모지가 소유한 모션 하나. 이름은 <see cref="TimelineNameOf"/>로 이모지 이름을 따라간다.
    /// 채널의 대상은 <c>Emoji_Offset</c>(이모지 전체)과 리그마다의 그림 노드다(<see cref="Rig.EmojiRigFrame"/>). 시각은 초(24fps).
    /// </summary>
    public MotionAsset Timeline { get; set; } = new();

    /// <summary>이름 붙은 구간 — 프레임. 겹쳐도 된다. 내보낼 때 구간마다 따로 선 클립이 된다.</summary>
    public List<EmojiSpan> Spans { get; set; } = new();

    /// <summary>등장 — 머묾 앞에 한 번. 구간의 이름. 머묾이 있을 때만 뜻이 있다.</summary>
    public string? In { get; set; }

    /// <summary>머묾 — 되풀이한다. 이것을 이으면 머무는 이모지, 잇지 않으면 타임라인 전체(<see cref="MainClip"/>)를 한 번 재생하고 사라진다.</summary>
    public string? Hold { get; set; }

    /// <summary>퇴장 — 내릴 때 한 번. 구간의 이름. 머묾이 있을 때만 뜻이 있다.</summary>
    public string? Out { get; set; }

    /// <summary>타임라인의 마지막 프레임 — 길이(초) × fps. 프레임 0 … 이것까지가 있다.</summary>
    [JsonIgnore]
    public int LastFrame => FrameOf(Timeline.Duration);

    /// <summary>머무는 이모지인가 — 머묾을 이었는가.</summary>
    [JsonIgnore]
    public bool Stays => Hold is not null;

    public EmojiRigAsset? FindRig(string? name)
        => name is null ? null : Rigs.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));

    public EmojiSpan? FindSpan(string? name)
        => name is null ? null : Spans.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    public static string TimelineNameOf(string emojiName) => TimelinePrefix + emojiName;

    /// <summary>모션 이름이 이모지의 타임라인 이름인가 — 그렇다면 이모지 이름을 낸다.</summary>
    public static bool IsTimelineName(string? motionName, out string emojiName)
    {
        if (motionName is not null && motionName.StartsWith(TimelinePrefix, StringComparison.Ordinal))
        {
            emojiName = motionName[TimelinePrefix.Length..];
            return true;
        }

        emojiName = "";
        return false;
    }

    /// <summary>초 → 프레임(가장 가까운 정수).</summary>
    public static int FrameOf(float seconds) => (int)Math.Round(seconds * Fps, MidpointRounding.AwayFromZero);

    /// <summary>프레임 → 초.</summary>
    public static float SecondsOf(float frame) => frame / Fps;
}

/// <summary>이모지 안의 리그 하나 — 스프라이트 하나의 뼈대(두 단: 기본 자리 · 그림). 이름으로 가리킨다(순번이 아니다).</summary>
public sealed class EmojiRigAsset
{
    /// <summary>이모지 안에서 리그를 가리키는 이름. 키가 이 이름으로 리그를 찾는다. 한 번 내보낸 뒤에는 바꾸지 않는다.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 그림들 — 문서 기준 경로. 첫 장이 기본 스프라이트이고, 나머지는 그림 바꾸기 채널이 몇 번째인가로 고른다.
    /// 내보낼 때 스프라이트 키는 파일 이름(확장자 없이)이다 — 런타임 <c>Resources/Emojis/{키}</c>.
    /// </summary>
    public List<string> Images { get; set; } = new();

    /// <summary>기본 자리(<c>EmojiRig_Place</c>) — 오프셋 기준 px, y 위. 키를 받지 않는다.</summary>
    public float PlaceX { get; set; }

    public float PlaceY { get; set; }

    /// <summary>기본 배율 — 그림을 이 이모지에서 얼마만 하게 쓰는가. 키의 배율은 이 위에서 1을 기준으로 잡는다.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>기본 회전(도). 키의 회전은 이 위에 얹힌다.</summary>
    public float Rotation { get; set; }

    /// <summary>
    /// 뒤집히면 안 되는 그림(<c>upright</c>, 런타임 <c>docs/emoji-format.md</c> §6 — 물음표 · 글자). 이모지가 거울에 비칠 때
    /// 이 리그의 그림과 키는 만든 그대로 보이고 기본 자리만 거울 쪽으로 간다. 방향을 따르지 않는 이모지(<see cref="EmojiAsset.FixedSide"/>)에서는 뜻이 없다.
    /// 거짓이면 저작본 · <c>emojis.json</c>에 적지 않는다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Upright { get; set; }

    /// <summary>그림 하나의 스프라이트 키 — 파일 이름(확장자 없이).</summary>
    public static string SpriteKeyOf(string imagePath) => Path.GetFileNameWithoutExtension(imagePath.Replace('\\', '/'));

    /// <summary>기본 스프라이트 키 — 첫 그림. 그림이 없으면 null.</summary>
    [JsonIgnore]
    public string? DefaultSprite => Images.Count > 0 ? SpriteKeyOf(Images[0]) : null;
}

/// <summary>타임라인의 이름 붙은 구간 — 시작 · 끝 프레임(끝 포함). 내보낼 때 따로 선 클립이 된다(구간의 첫 프레임이 클립의 프레임 0).</summary>
public sealed class EmojiSpan
{
    public string Name { get; set; } = "";

    public int Start { get; set; }

    /// <summary>끝 프레임 — 포함한다. 0~12fr 구간은 13프레임짜리 클립이다.</summary>
    public int End { get; set; }

    /// <summary>프레임 수.</summary>
    [JsonIgnore]
    public int Length => End - Start + 1;
}
