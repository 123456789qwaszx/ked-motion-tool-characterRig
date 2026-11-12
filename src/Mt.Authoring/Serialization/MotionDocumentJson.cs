using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using Mt.Authoring.Model;

namespace Mt.Authoring.Serialization;

/// <summary>
/// 저작 원본(<c>*.motion-authoring.json</c>)의 읽기·쓰기. 산출물이 아니라 툴의 살림이므로
/// 바이트 정규성은 요구하지 않는다 — 다만 같은 문서면 같은 글자가 나와야 되돌리기 스냅샷과
/// "바뀌었나" 비교가 선다(System.Text.Json은 결정적이다).
///
/// 산출물(<c>motions/2</c>)은 <see cref="MotionsJson"/>이 따로 쓴다 — 두 포맷을 섞지 않는다.
/// 읽는 것은 지금 판(<c>motion-authoring/2</c>)뿐이다 — 옛 판을 옮겨 읽는 길은 두지 않는다.
/// </summary>
public static class MotionDocumentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 한글 메모가 \uXXXX로 깨져 보이지 않게.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options) + "\n";
    }

    /// <summary>
    /// 문서가 모르는 키들 — <c>motions[0].ednn</c> 같은 자리로 낸다. 읽을 때 모르는 키는 말없이 버려지므로
    /// 키 이름의 오타(<c>keyz</c> · <c>placex</c>)가 뜻한 값을 빼먹은 채 지나간다. 검증이 이것으로 알린다.
    /// 깨진 JSON이면 빈 목록이다(읽기가 따로 알린다).
    /// </summary>
    public static IReadOnlyList<string> UnknownKeys(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var unknown = new List<string>();

        try
        {
            JsonNode? root = JsonNode.Parse(json, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            CollectUnknown(root, typeof(MotionDocument), "", unknown);
        }
        catch (JsonException)
        {
        }

        return unknown;
    }

    private static void CollectUnknown(JsonNode? node, Type type, string path, List<string> unknown)
    {
        JsonTypeInfo info = Options.GetTypeInfo(Nullable.GetUnderlyingType(type) ?? type);

        if (node is JsonArray array && info.Kind == JsonTypeInfoKind.Enumerable && info.ElementType is { } element)
        {
            for (int i = 0; i < array.Count; i++)
            {
                CollectUnknown(array[i], element, $"{path}[{i}]", unknown);
            }
        }
        else if (node is JsonObject obj && info.Kind == JsonTypeInfoKind.Object)
        {
            foreach ((string key, JsonNode? value) in obj)
            {
                JsonPropertyInfo? property = info.Properties.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                string here = path.Length == 0 ? key : $"{path}.{key}";

                if (property is null)
                {
                    unknown.Add(here);
                }
                else
                {
                    CollectUnknown(value, property.PropertyType, here, unknown);
                }
            }
        }
    }

    /// <summary>
    /// 스키마가 다르면 거부한다 — 모르는 판을 읽는 척하지 않는다.
    /// 깨진 JSON은 <see cref="JsonException"/>으로 올라간다; 호출부가 사람에게 보인다.
    /// </summary>
    public static MotionDocument Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        MotionDocument document = JsonSerializer.Deserialize<MotionDocument>(json, Options)
            ?? throw new JsonException("빈 문서다.");

        if (!string.Equals(document.Schema, AuthoringFormat.AuthoringSchema, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"schema '{document.Schema}'는 읽을 수 없다 (기대 '{AuthoringFormat.AuthoringSchema}').");
        }

        // 역직렬화가 null을 남길 수 있는 목록은 빈 목록으로 — 모델의 불변 조건(목록은 null이 아니다)을 지킨다.
        document.Motions ??= new List<MotionAsset>();
        document.Scenes ??= new List<SceneAsset>();

        foreach (SceneAsset scene in document.Scenes)
        {
            scene.Objects ??= new List<SceneObject>();
            scene.Camera ??= new CameraSettings();
        }

        document.EnsureDefaultScene();

        foreach (ClipAsset clip in document.Clips)
        {
            clip.Roles ??= new List<ClipRole>();
            clip.Tracks ??= new List<ClipTrack>();

            foreach (ClipTrack track in clip.Tracks)
            {
                track.Blocks ??= new List<ClipBlock>();
            }
        }

        foreach (EmojiAsset emoji in document.Emojis)
        {
            emoji.Rigs ??= new List<EmojiRigAsset>();
            emoji.Spans ??= new List<EmojiSpan>();
            emoji.Timeline ??= new MotionAsset();

            foreach (EmojiRigAsset rig in emoji.Rigs)
            {
                rig.Images ??= new List<string>();
            }

            // 타임라인의 이름 · 리그는 이모지가 정한다 — 손으로 쓴 문서가 비워 두어도 된다.
            emoji.Timeline.Name = EmojiAsset.TimelineNameOf(emoji.Name);
            emoji.Timeline.Rig = Rig.EmojiRigFrame.RigKind;
        }

        foreach (MotionAsset motion in document.AllMotions)
        {
            motion.Channels ??= new List<ChannelAsset>();

            foreach (ChannelAsset channel in motion.Channels)
            {
                Normalize(channel);

                // 손으로(또는 AI가) 쓴 문서는 탄젠트 값을 모른다 — 키의 탄젠트 종류(auto · flat · linear …)대로 값을 세운다.
                // 편집기가 조작마다 하는 그 계산이라 앱이 저장한 문서는 바뀌지 않는다. free는 적힌 값 그대로다.
                foreach (ComponentCurve curve in new[] { channel.X, channel.Y, channel.Z }.OfType<ComponentCurve>())
                {
                    Graph.TangentMath.Recompute(curve.Keys);
                }
            }
        }

        return document;
    }

    private static void Normalize(ChannelAsset channel)
    {
        channel.X = NormalizeCurve(channel.X);
        channel.Y = NormalizeCurve(channel.Y);
        channel.Z = NormalizeCurve(channel.Z);

        // 그림 채널의 키는 언제나 계단이다 — 손으로 쓴 문서가 다른 종류를 적어도 읽을 때 바로잡는다(편집기는 조작마다 지킨다).
        if (ChannelKinds.IsAlwaysStepped(channel.Kind))
        {
            foreach (KeyAsset key in new[] { channel.X, channel.Y, channel.Z }.OfType<ComponentCurve>().SelectMany(c => c.Keys))
            {
                key.Tangent = TangentMode.Step;
                key.InTangent = 0f;
                key.OutTangent = 0f;
                key.Broken = false;
            }
        }
    }

    // 키가 없는 성분은 "건드리지 않는다"(null)로 — 빈 목록과 null을 두 가지 뜻으로 두지 않는다.
    private static ComponentCurve? NormalizeCurve(ComponentCurve? curve)
    {
        if (curve is null)
        {
            return null;
        }

        curve.Keys ??= new List<KeyAsset>();
        return curve.Keys.Count == 0 ? null : curve;
    }
}
