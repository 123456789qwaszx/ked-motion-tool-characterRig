using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 저작 원본 한 벌 (<c>*.motion-authoring.json</c>, 스키마 <see cref="AuthoringFormat.AuthoringSchema"/>).
///
/// 산출물(<c>motions/2</c>)에 들어가지 않는 제작 정보 — 탄젠트 모드·끊김·재생 기준(relative/absolute)·씬 —을
/// 함께 든다. 내보내기는 그것을 벗기고 <see cref="MotionAsset"/>을 코어 <see cref="MotionClip"/>으로 바꾼다.
///
/// 전부 가변 클래스다. 편집은 <see cref="Editing.MotionEditor"/>만 하고, 되돌리기는 문서 전체 스냅샷이다
/// — 명령마다 역연산을 짓지 않는다.
/// </summary>
public sealed class MotionDocument
{
    public string Schema { get; set; } = AuthoringFormat.AuthoringSchema;

    /// <summary>리그 덤프의 출처. 열 때 해시가 다르면 "리그가 바뀌었다"를 알린다 — 조용히 맞는 척하지 않는다.</summary>
    public RigSchemasReference? RigSchemas { get; set; }

    /// <summary>
    /// 문서를 지을 때의 스키마(<c>docs/PLAN-characterRig.md</c> §3의 2). 지금 실린 덤프와 견주어 무엇이 달라졌는지 보인다(<c>Schema.SchemaDiff</c>).
    /// 없으면 아직 적은 적이 없다 — 덤프를 싣거나 저장할 때 적힌다.
    /// </summary>
    public SchemaPrint? BuiltOn { get; set; }

    /// <summary>기준 해상도 덤프(<c>base-resolution.json</c>)의 경로. 뷰포트가 쓴다. v1에서는 없어도 된다.</summary>
    public FileReference? BaseResolution { get; set; }

    public List<MotionAsset> Motions { get; set; } = new();

    /// <summary>미리보기 씬들(<c>PLAN-scene.md</c>). 내보내지 않는다. 비어 있으면 <see cref="EnsureDefaultScene"/>가 기본 씬을 둔다.</summary>
    public List<SceneAsset> Scenes { get; set; } = new();

    /// <summary>뷰포트가 보여 주는 씬 이름. 없으면 첫 씬.</summary>
    public string? ActiveScene { get; set; }

    public SceneAsset? FindScene(string? name)
        => name is null ? null : Scenes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    /// <summary>보여 줄 씬 — ActiveScene, 없으면 첫 씬, 그것도 없으면 null.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SceneAsset? ActiveSceneAsset => FindScene(ActiveScene) ?? Scenes.FirstOrDefault();

    /// <summary>씬이 없으면 캐릭터 하나짜리 기본 씬을 둔다. 있으면 아무것도 안 한다.</summary>
    public void EnsureDefaultScene()
    {
        if (Scenes.Count == 0)
        {
            Scenes.Add(SceneAsset.Default());
        }

        ActiveScene ??= Scenes[0].Name;
    }

    /// <summary>
    /// 연출 클립들(<c>PLAN-clip.md</c>) — 씬 위의 시간표. <c>motions.json</c>에는 안 나간다.
    /// 저작본에는 <see cref="ClipsOrNull"/>로 실린다: 클립이 없는 문서는 전과 같은 글자다.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ClipAsset> Clips { get; set; } = new();

    /// <summary>저작본의 <c>clips</c> 자리 — 비면 쓰지 않는다. 코드는 <see cref="Clips"/>를 쓴다.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("clips")]
    public List<ClipAsset>? ClipsOrNull
    {
        get => Clips.Count == 0 ? null : Clips;
        set => Clips = value ?? new List<ClipAsset>();
    }

    public ClipAsset? FindClip(string? name)
        => name is null ? null : Clips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 이모지들(<c>docs/history/PLAN-emoji.md</c>). <c>motions.json</c>에는 안 나가고 <c>emojis.json</c>으로 나간다.
    /// 저작본에는 <see cref="EmojisOrNull"/>로 실린다: 이모지가 없는 문서는 전과 같은 글자다.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<EmojiAsset> Emojis { get; set; } = new();

    /// <summary>저작본의 <c>emojis</c> 자리 — 비면 쓰지 않는다. 코드는 <see cref="Emojis"/>를 쓴다.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("emojis")]
    public List<EmojiAsset>? EmojisOrNull
    {
        get => Emojis.Count == 0 ? null : Emojis;
        set => Emojis = value ?? new List<EmojiAsset>();
    }

    public EmojiAsset? FindEmoji(string? name)
        => name is null ? null : Emojis.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 이름으로 모션을 찾는다 — 내보내는 모션(<see cref="Motions"/>)과 이모지의 타임라인(<c>emoji:이름</c>) 둘 다.
    /// 편집 기반(그래프 · 도프 시트 · 키 연산)이 이름 하나로 둘을 같이 다룬다.
    /// </summary>
    public MotionAsset? Find(string name)
        => EmojiAsset.IsTimelineName(name, out string emoji)
            ? FindEmoji(emoji)?.Timeline
            : Motions.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));

    /// <summary>문서의 모션 전부 — 내보내는 모션과 이모지의 타임라인.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<MotionAsset> AllMotions => Motions.Concat(Emojis.Select(e => e.Timeline));
}

public sealed class RigSchemasReference
{
    /// <summary>문서 파일 기준 상대 경로 또는 절대 경로. 구분자는 <c>/</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>파일 바이트의 SHA-256, 소문자 16진수. 비어 있으면 아직 검증한 적이 없다.</summary>
    public string Sha256 { get; set; } = "";
}

public sealed class FileReference
{
    public string Path { get; set; } = "";
}
