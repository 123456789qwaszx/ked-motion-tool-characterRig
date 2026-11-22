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
            Assert.Equal(File.ReadAllText(Library("export", output)), File.ReadAllText(written));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
