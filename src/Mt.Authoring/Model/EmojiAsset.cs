namespace Mt.Authoring.Model;

/// <summary>
/// 이모지 정의 — 키 하나가 "붙는 리그 + 그림(스킨) + 등장·머묾·퇴장 모션 + 머무는 시간 + 자리"를 가리킨다(<c>PLAN-parts.md</c> §8).
/// 런타임은 <c>emoji 슬롯 키</c> 같은 커맨드(또는 모션의 이벤트 키)로 이 정의를 불러 한 번의 수명을 돌린다:
/// 등장 모션 → 머무는 동안 머묾 모션을 되풀이 → 퇴장 모션 → 사라짐. 머무는 시간이 없으면(null) 끄라고 할 때까지 머문다.
///
/// 모션과 스킨은 **참조**한다 — 같은 "튀어나옴" 모션을 여러 이모지가 나눠 쓴다. 산출물은 <c>emojis.json</c>(초안)으로 따로 나간다.
/// </summary>
public sealed class EmojiAsset
{
    /// <summary>문서에서 하나뿐인 키. 빈칸 없는 글(한글도 된다) — 대본이 이 이름으로 부른다.</summary>
    public string Name { get; set; } = "";

    /// <summary>이 이모지가 서는 리그(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>그림 — 스킨의 이름. null이면 그림이 없다(검증이 알린다).</summary>
    public string? Skin { get; set; }

    /// <summary>
    /// 반전 판 그림 — 붙은 캐릭터가 좌우 반전일 때 입는 스킨(말풍선의 꼬리가 반대쪽인 판). 자리와 움직임은 계층이 뒤집어 주고, 이 판의 그림은 **뒤집지 않고** 그린다
    /// (글자·점의 순서가 읽히게 따로 그린 그림이다). null이면 같은 그림이 계층을 따라 뒤집혀 보인다.
    /// </summary>
    public string? SkinFlipped { get; set; }

    /// <summary>등장 모션(한 번). null이면 바로 선다.</summary>
    public string? Enter { get; set; }

    /// <summary>머묾 모션 — 머무는 동안 처음부터 되풀이한다(바퀴마다 등장이 끝난 자세에서 다시). null이면 가만히 머문다.</summary>
    public string? Hold { get; set; }

    /// <summary>퇴장 모션(한 번). null이면 바로 사라진다.</summary>
    public string? Exit { get; set; }

    /// <summary>
    /// 머무는 시간(초) — 등장이 끝난 뒤 퇴장이 시작하기까지. null이면 **끄라고 할 때까지** 머문다(머무는 이모지: 상태다 — 되감기에서 살아 있어야 한다).
    /// 값이 있으면 잠깐 뜨고 사라지는 이모지다(이벤트 — 끝 상태가 없다).
    /// </summary>
    public float? HoldSeconds { get; set; }

    /// <summary>자리를 주는 노드 — 이 노드의 위치에 <see cref="OffsetX"/>·<see cref="OffsetY"/>가 들어간다. 리그에 없으면 루트.</summary>
    public string AnchorNode { get; set; } = DefaultAnchorNode;

    /// <summary>붙는 자리에서의 오프셋(px) — 이모지마다 머리 옆·머리 위·입 옆이 다르다.</summary>
    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    /// <summary>
    /// 붙는 자리의 이름(런타임의 기준점 — <c>faceAura</c>·<c>face</c>…). 툴은 값을 풀지 않고 그대로 싣는다:
    /// 캐릭터마다 다른 머리 자리는 런타임의 기준점 표가 푼다. null이면 런타임의 기본.
    /// </summary>
    public string? Locator { get; set; }

    public const string DefaultAnchorNode = "Emoji_Anchor";

    public EmojiAsset Clone() => (EmojiAsset)MemberwiseClone();
}
