using System.Text.Json.Nodes;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>
/// 실제로 쓸 세트(<c>library/</c> — 캐릭터 모션 · 이모지)가 막는 문제 없이 검증을 지나고,
/// 저작본에서 내보낸 것이 <c>library/export/</c>에 둔 산출물과 같은가. 그림(<c>library/art</c>)은 git 밖이라
/// 그림이 없다는 알림은 둔다 — 막는 문제만 본다.
/// </summary>
public class LibraryTests
{
    private static string Library(params string[] parts) => Path.Combine([RepoRoot(), "library", .. parts]);

    private static (int Code, JsonNode Json) Run(params string[] args)
    {
        var stdout = new StringWriter();
        int code = MtCli.Run(args, stdout, new StringWriter());
        return (code, JsonNode.Parse(stdout.ToString())!);
    }

    [Theory]
    [InlineData("motions.motion-authoring.json")]
    [InlineData("emojis.motion-authoring.json")]
    public void 세트는_막는_문제_없이_검증을_지난다(string name)
    {
        (int code, JsonNode json) = Run("validate", Library(name));
        Assert.Equal(0, code);
        Assert.All(json["problems"]!.AsArray(), p => Assert.False(p!["blocking"]!.GetValue<bool>(), p!["message"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("export", "motions.motion-authoring.json", "motions.json")]
    [InlineData("emojis", "emojis.motion-authoring.json", "emojis.json")]
    public void 내보낸_것이_export에_둔_산출물과_같다(string command, string document, string output)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string written = Path.Combine(directory, output);
            Assert.Equal(0, Run(command, Library(document), written).Code);
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
        // 다시 지은 곡선이 참고집 카드의 크기를 지키는가 — 가장 크게 움직인 때의 값.
        Assert.Equal(60.85, Pose("g01_hop", 0.16666667, "y"), 2);
        Assert.Equal(-60.72, Pose("g08_bow", 0.33333334, "y"), 2);
        Assert.Equal(-48.76, Pose("g12_lean", 0.14166667, "x"), 2);
        Assert.Equal(48.76, Pose("g12_lean_m", 0.14166667, "x"), 2);
    }

    private static double Pose(string motion, double time, string axis)
    {
        (_, JsonNode pose) = Run("pose", Library("motions.motion-authoring.json"), "--motion", motion, "--time", time.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return pose["nodes"]!["CharacterPortrait_Track_Move"]!["position"]![axis == "x" ? 0 : 1]!.GetValue<double>();
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
