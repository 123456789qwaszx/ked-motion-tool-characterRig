using System.Text.Json;
using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// <c>presets/emoji-locator.json</c>(ExportedTuning) — 런타임의 이모지 로케이터 표(<c>EmojiLocatorTuningDBSO</c>): 캐릭터(+ 포즈)별 머리의 자리.
/// **보기에만 쓴다**(<c>docs/PLAN-ux.md</c> U7) — 뷰포트가 이모지를 인물의 머리 위에 세워 보이려고 읽는다. 툴은 이 표를 고치지도 내보내지도 않는다
/// (이모지는 캐릭터를 모른다 — 머리의 자리는 런타임의 표가 든다).
/// 찾는 순서는 런타임과 같다: 캐릭터 + 포즈 → 캐릭터(포즈 칸이 빈 항목) → 없다(<see cref="DefaultPosition"/>).
/// </summary>
public sealed class EmojiLocatorFile
{
    public const string FileName = "emoji-locator.json";

    /// <summary>
    /// 표에 없을 때의 자리 — 발(<c>CharSlot_Scale</c>의 아래 가운데)에서 잰 px, y 위. 런타임 표의 새 항목이 갖는 값과 같다.
    /// 런타임에서 표에 없는 캐릭터의 자리는 게임의 설정(부트스트랩)이 정하므로 이 값은 보기를 위한 어림이다.
    /// </summary>
    public static readonly Vec2 DefaultPosition = new(0f, 800f);

    /// <summary>표가 없을 때 — 모든 캐릭터가 <see cref="DefaultPosition"/>.</summary>
    public static readonly EmojiLocatorFile Empty = new(new List<EmojiLocatorEntryDto>());

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<(string Character, string Variant), Vec2> _map = new();

    private EmojiLocatorFile(List<EmojiLocatorEntryDto> entries)
    {
        // 같은 키가 둘이면 나중 것이 이긴다(런타임과 같다).
        foreach (EmojiLocatorEntryDto entry in entries)
        {
            string character = Normalize(entry.character);

            if (character.Length > 0)
            {
                _map[(character, Normalize(entry.variant))] = new Vec2(entry.position?.x ?? DefaultPosition.X, entry.position?.y ?? DefaultPosition.Y);
            }
        }
    }

    /// <summary>항목의 수.</summary>
    public int Count => _map.Count;

    public static EmojiLocatorFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path));
    }

    public static EmojiLocatorFile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        EmojiLocatorFileDto dto = JsonSerializer.Deserialize<EmojiLocatorFileDto>(json, Options) ?? throw new JsonException("빈 emoji-locator.json이다.");
        EmojiLocatorBodyDto body = dto.MonoBehaviour ?? throw new JsonException("emoji-locator.json에 MonoBehaviour가 없다.");
        return new EmojiLocatorFile(body.entries ?? new List<EmojiLocatorEntryDto>());
    }

    /// <summary>덤프 옆에서 찾는다(<c>presets/</c> 먼저). 없으면 null.</summary>
    public static string? TryFindBeside(string rigSchemasPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(rigSchemasPath));

        if (directory is null)
        {
            return null;
        }

        foreach (string candidate in new[] { Path.Combine(directory, "presets", FileName), Path.Combine(directory, FileName) })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>그 캐릭터(와 포즈)의 머리의 자리. 표에 없으면 <see cref="DefaultPosition"/>과 거짓.</summary>
    public bool TryResolve(string? character, string? variant, out Vec2 position)
    {
        string c = Normalize(character);
        string v = Normalize(variant);

        if (c.Length > 0 && ((v.Length > 0 && _map.TryGetValue((c, v), out position)) || _map.TryGetValue((c, ""), out position)))
        {
            return true;
        }

        position = DefaultPosition;
        return false;
    }

    /// <summary>그 캐릭터의 머리의 자리(표에 없으면 기본값).</summary>
    public Vec2 Resolve(string? character, string? variant = null)
    {
        TryResolve(character, variant, out Vec2 position);
        return position;
    }

    private static string Normalize(string? key) => (key ?? "").Trim().ToLowerInvariant();
}

#pragma warning disable SA1300, IDE1006 // 덤프(유니티 JSON)의 이름 그대로
public sealed class EmojiLocatorFileDto
{
    public EmojiLocatorBodyDto? MonoBehaviour;
}

public sealed class EmojiLocatorBodyDto
{
    public List<EmojiLocatorEntryDto>? entries;
}

public sealed class EmojiLocatorEntryDto
{
    public string? character;
    public string? variant;
    public EmojiLocatorPositionDto? position;
}

public sealed class EmojiLocatorPositionDto
{
    public float x;
    public float y;
}
#pragma warning restore SA1300, IDE1006
