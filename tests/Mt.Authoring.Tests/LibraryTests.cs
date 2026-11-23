using System.Text.Json.Nodes;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>
/// 실제로 쓸 세트(<c>library/cards.motion-authoring.json</c> — 연출 카드의 몸짓 · 감정 아이콘)가 막는 문제 없이 검증을 지나고,
/// 저작본에서 내보낸 것이 <c>library/export/</c>에 둔 산출물과 같은가. 그림(<c>library/art</c>)은 git 밖이라
/// 그림이 없다는 알림은 둔다 — 막는 문제만 본다.
/// </summary>
public class LibraryTests
{
    private const string Document = "cards.motion-authoring.json";

    private static string Library(params string[] parts) => Path.Combine([RepoRoot(), "library", .. parts]);

    private static (int Code, JsonNode Json) Run(params string[] args)
    {
        var stdout = new StringWriter();
        int code = MtCli.Run(args, stdout, new StringWriter());
        return (code, JsonNode.Parse(stdout.ToString())!);
    }

    [Fact]
    public void 세트는_막는_문제_없이_검증을_지난다()
    {
        (int code, JsonNode json) = Run("validate", Library(Document));
        Assert.Equal(0, code);
        Assert.All(json["problems"]!.AsArray(), p => Assert.False(p!["blocking"]!.GetValue<bool>(), p!["message"]!.GetValue<string>()));
    }

    [Fact]
    public void 세트는_몸짓_열일곱과_감정_아이콘_스물둘이다()
    {
        // 몸짓 카드 G01~G16 + G12의 좌우 반대, 감정 아이콘 카드 I01~I22 — 한 저작본에 함께 둔다.
        (_, JsonNode info) = Run("info", Library(Document));
        Assert.Equal(17, info["motions"]!.AsArray().Count);
        Assert.Equal(22, info["emojis"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("export", "motions.json")]
    [InlineData("emojis", "emojis.json")]
    public void 내보낸_것이_export에_둔_산출물과_같다(string command, string output)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string written = Path.Combine(directory, output);
            Assert.Equal(0, Run(command, Library(Document), written).Code);
            string committed = File.ReadAllText(Library("export", output));
            string exported = File.ReadAllText(written);

            if (committed != exported)
            {
                Assert.Fail(StaleExportMessage(output, committed, exported));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 산출물이_어긋나면_다른_이름과_다시_내보내는_길을_말한다()
    {
        const string committed = """{ "schema": "emojis/2", "emojis": [ { "name": "a", "v": 1 }, { "name": "b", "v": 1 }, { "name": "gone", "v": 1 } ] }""";
        const string exported = """{ "schema": "emojis/2", "emojis": [ { "name": "a", "v": 1 }, { "name": "b", "v": 2 }, { "name": "new", "v": 1 } ] }""";

        Assert.Equal(new[] { "b", "new(새로)", "gone(빠짐)" }, DifferingNames(committed, exported));
        string message = StaleExportMessage("emojis.json", committed, exported);
        Assert.Contains("b, new(새로), gone(빠짐)", message, StringComparison.Ordinal);
        Assert.Contains(@"library\export.cmd", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 몸짓은_잰_꼭짓점에서_그_값이다()
    {
        // 다시 지은 곡선이 참고집 클립의 꼭짓점을 지키는가 — 가장 크게 움직인 프레임의 값(꼭짓점의 키는 덜어 내지 않는다).
        Assert.Equal(60.04, Pose("g01_hop", 5, "y"), 2);
        Assert.Equal(-59.86, Pose("g08_bow", 11, "y"), 2);
        Assert.Equal(73.61, Pose("g13_heave", 4, "y"), 2);
        Assert.Equal(48.04, Pose("g12_lean", 4, "x"), 2);
        Assert.Equal(-48.04, Pose("g12_lean_m", 4, "x"), 2);
    }

    [Fact]
    public void 졸음의_Z는_사라진_동안_제자리에_멈춰_있다()
    {
        // 정의(emojis/2)의 계단 키는 들어오는 기울기를 가진다 — 저작본에서는 free 키 + 한 프레임 뒤의 step 키로 옮겼다.
        // 그래도 사라진 51 ~ 59프레임에 z1은 멈춰 있고 보이지 않는다.
        JsonNode at51 = EmojiRig("sleepy", 51, "z1");
        JsonNode at59 = EmojiRig("sleepy", 59, "z1");
        Assert.Equal(at51["position"]!.ToJsonString(), at59["position"]!.ToJsonString());
        Assert.Equal(0, at51["alpha"]!.GetValue<double>(), 3);
        Assert.Equal(0, at59["alpha"]!.GetValue<double>(), 3);
    }

    [Theory]
    [InlineData("talk")]
    [InlineData("sigh")]
    public void 반대쪽에_맞게_그려진_그림은_가로로_뒤집어_쓴다(string emoji)
    {
        // 원본에서 이 그림들은 PNG와 좌우가 반대로 섰다 — 그림의 가로 배율 키가 음수다(런타임 형식에 그림을 뒤집는 칸이 없다).
        JsonNode rig = EmojiRig(emoji, 10, "a");
        Assert.True(rig["scale"]![0]!.GetValue<double>() < 0, rig.ToJsonString());
        Assert.True(rig["scale"]![1]!.GetValue<double>() > 0, rig.ToJsonString());
    }

    [Fact]
    public void 분함은_작은_김이_먼저_뜨고_생각은_거품_뒤에_구름이_뜬다()
    {
        // 원본(소유자 11-22): 분함은 김 둘 — 작은 김 a가 먼저, 큰 김 b가 2프레임 뒤. 생각은 거품 b1 → b2 → 구름.
        Assert.True(EmojiRig("resentful", 1, "a")["alpha"]!.GetValue<double>() > 0.5);
        Assert.Equal(0, EmojiRig("resentful", 1, "b")["alpha"]!.GetValue<double>(), 3);
        Assert.True(EmojiRig("think", 2, "b1")["alpha"]!.GetValue<double>() > 0.5);
        Assert.Equal(0, EmojiRig("think", 2, "cloud")["alpha"]!.GetValue<double>(), 3);
        Assert.True(EmojiRig("think", 10, "cloud")["alpha"]!.GetValue<double>() > 0.9);
    }

    /// <summary>
    /// 산출물이 저작본과 어긋났을 때의 말 — 무엇이 다른지(항목의 이름)와 무엇을 하면 되는지.
    /// 앱에서 저작본을 고쳐 저장만 하면 산출물은 옛것 그대로라 여기서 깨진다(2026-11-23 — 그때는 글자의 자리만 말했다).
    /// </summary>
    private static string StaleExportMessage(string output, string committed, string exported)
    {
        IReadOnlyList<string> names = DifferingNames(committed, exported);
        string what = names.Count > 0 ? $"다른 것: {string.Join(", ", names)}" : "항목은 같고 그 밖의 글자가 다르다";
        return $"저작본(library/{Document})에서 내보낸 것이 커밋된 library/export/{output}과 다르다 — {what}. "
            + @"앱에서 저작본을 고쳤다면 library\export.cmd(bash: library/export.sh)로 다시 내보내 함께 커밋한다. "
            + "고친 것을 버리려면 저작본을 git으로 되돌린다.";
    }

    /// <summary>두 산출물(<c>motions/2</c> · <c>emojis/2</c>)에서 내용이 다른 항목의 이름 — 내보낸 쪽의 차례, 한쪽에만 있는 것은 표시를 붙인다.</summary>
    private static IReadOnlyList<string> DifferingNames(string committed, string exported)
    {
        static List<(string Name, string Text)> Entries(string json)
        {
            JsonNode? root = JsonNode.Parse(json);
            return (root?["motions"] ?? root?["emojis"]) is JsonArray items
                ? items.Select(i => (Name: i?["name"]?.GetValue<string>() ?? "", Text: i?.ToJsonString() ?? "")).ToList()
                : new List<(string Name, string Text)>();
        }

        List<(string Name, string Text)> before = Entries(committed);
        List<(string Name, string Text)> after = Entries(exported);
        var names = new List<string>();

        foreach ((string name, string text) in after)
        {
            int i = before.FindIndex(b => b.Name == name);

            if (i < 0)
            {
                names.Add($"{name}(새로)");
            }
            else if (before[i].Text != text)
            {
                names.Add(name);
            }
        }

        names.AddRange(before.Where(b => after.All(a => a.Name != b.Name)).Select(b => $"{b.Name}(빠짐)"));
        return names;
    }

    private static double Pose(string motion, int frame, string axis)
    {
        string time = (frame / 30.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        (_, JsonNode pose) = Run("pose", Library(Document), "--motion", motion, "--time", time);
        return pose["nodes"]!["CharacterPortrait_Track_Move"]!["position"]![axis == "x" ? 0 : 1]!.GetValue<double>();
    }

    private static JsonNode EmojiRig(string emoji, int frame, string rig)
    {
        string time = (frame / 30.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        (_, JsonNode pose) = Run("pose", Library(Document), "--emoji", emoji, "--time", time, "--view", "timeline");
        return pose["rigs"]!.AsArray().First(r => r!["name"]!.GetValue<string>() == rig)!;
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "KedMotionTool.sln")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("저장소 루트를 찾지 못했다.");
    }
}
