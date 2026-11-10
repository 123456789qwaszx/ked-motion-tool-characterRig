using System.Text.Json.Nodes;

namespace Mt.Authoring.Tests;

/// <summary>
/// 시험용 이모지 리그 — 런타임 문서의 모양(<c>Emoji_Anchor › Emoji_Body › Emoji_Part1…4</c>)을 내장 덤프에 더한 파일을 임시 폴더에 만든다.
/// 실제 덤프에 이모지 리그가 들어오면 이 픽스처는 그것으로 바꾼다(<c>PLAN-parts.md</c> §2.3).
/// </summary>
internal static class EmojiRigFixture
{
    public const string RigKind = "emoji";
    public const float PartSize = 160f;

    /// <summary>이모지 리그가 든 <c>rig-schemas.json</c>의 경로(옆에 presets는 없다 — 깊이·focus는 내장본이 쓰인다).</summary>
    public static string WriteDump(string sourceDumpPath)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(sourceDumpPath))!;
        var nodes = new JsonArray
        {
            Node("__root", "", stretch: true, size: 0f),
            Node("Emoji_Anchor", "__root", stretch: false, size: 0f),
            Node("Emoji_Body", "Emoji_Anchor", stretch: false, size: 0f),
        };

        for (int i = 1; i <= 4; i++)
        {
            nodes.Add(Node($"Emoji_Part{i}", "Emoji_Body", stretch: false, size: PartSize));
        }

        root["rigs"]!.AsArray().Add(new JsonObject { ["rigKind"] = RigKind, ["sourcePrefab"] = "", ["nodes"] = nodes });

        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"emoji-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rig-schemas.json");
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    private static JsonObject Node(string id, string parent, bool stretch, float size) => new()
    {
        ["id"] = id,
        ["parent"] = parent,
        ["anchoredPosition"] = V2(0f, 0f),
        ["anchorMin"] = stretch ? V2(0f, 0f) : V2(0.5f, 0.5f),
        ["anchorMax"] = stretch ? V2(1f, 1f) : V2(0.5f, 0.5f),
        ["pivot"] = V2(0.5f, 0.5f),
        ["sizeDelta"] = V2(size, size),
        ["localScale"] = new JsonObject { ["x"] = 1f, ["y"] = 1f, ["z"] = 1f },
        ["localEulerAngles"] = new JsonObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f },
        ["measuredRectSize"] = stretch ? V2(1920f, 1080f) : V2(size, size),
        ["hasCanvasGroup"] = false,
        ["canvasGroupAlpha"] = 1f,
    };

    private static JsonObject V2(float x, float y) => new() { ["x"] = x, ["y"] = y };
}
