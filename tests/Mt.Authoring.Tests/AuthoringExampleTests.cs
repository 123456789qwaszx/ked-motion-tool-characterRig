using System.Text.Json.Nodes;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>
/// 손으로 쓴 예시(<c>examples/authoring</c> — <c>docs/AUTHORING-FILES.md</c>가 가리킨다)가 설명서대로 통하는가:
/// 검증을 지나고, 내보내지고, 키의 탄젠트 종류만 적어도 읽을 때 값이 선다.
/// </summary>
public class AuthoringExampleTests
{
    private static string Example(string name) => Path.Combine(RepoRoot(), "examples", "authoring", name);

    private static (int Code, JsonNode Json) Run(params string[] args)
    {
        var stdout = new StringWriter();
        int code = MtCli.Run(args, stdout, new StringWriter());
        return (code, JsonNode.Parse(stdout.ToString())!);
    }

    [Theory]
    [InlineData("motions.motion-authoring.json")]
    [InlineData("emojis.motion-authoring.json")]
    public void 예시는_알림_하나_없이_검증을_지난다(string name)
    {
        (int code, JsonNode json) = Run("validate", Example(name));
        Assert.Equal(0, code);
        Assert.Empty(json["problems"]!.AsArray());
    }

    [Fact]
    public void 모션_예시는_내보내지고_클립은_구워진다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"authoring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string document = Example("motions.motion-authoring.json");
            Assert.Equal(0, Run("export", document, Path.Combine(directory, "motions.json")).Code);

            // 가장 깊이 숙인 때 — 설명서의 "mt pose로 값을 읽는다".
            (_, JsonNode pose) = Run("pose", document, "--clip", "greet", "--time", "0.5");
            Assert.Equal(-7.0, pose["objects"]!["s1"]!["CharacterPortrait_SwayPivot"]!["euler"]![2]!.GetValue<double>(), 3);
            Assert.Equal(-24.0, pose["objects"]!["s1"]!["CharacterPortrait_Track_Move"]!["position"]![1]!.GetValue<double>(), 3);

            (int code, JsonNode baked) = Run("bake", document, "--clip", "greet", "--object", "s1", "--name", "greet_s1", "--out", Path.Combine(directory, "baked.motion-authoring.json"));
            Assert.Equal(0, code);
            Assert.Equal("greet_s1", baked["motion"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 이모지_예시는_내보내지고_머무는_이모지다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"authoring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string document = Example("emojis.motion-authoring.json");
            Assert.Equal(0, Run("emojis", document, Path.Combine(directory, "emojis.json")).Code);
            (_, JsonNode pose) = Run("pose", document, "--emoji", "spark", "--time", "0.1");
            Assert.Equal("in", pose["phase"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 이모지의_그림_파일이_그_경로에_없으면_알린다_막지는_않는다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"authoring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            // 예시를 그림 없는 폴더로 옮기면 "art/star.png"가 가리키는 데가 없다.
            string moved = Path.Combine(directory, "emojis.motion-authoring.json");
            File.Copy(Example("emojis.motion-authoring.json"), moved);

            (int code, JsonNode json) = Run("validate", moved);
            Assert.Equal(0, code);
            JsonArray problems = json["problems"]!.AsArray();
            Assert.Equal(2, problems.Count); // 그림마다 한 줄 — big · small
            Assert.All(problems, p => Assert.False(p!["blocking"]!.GetValue<bool>()));
            Assert.Contains("art/star.png", problems[0]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void 탄젠트는_종류만_적어도_읽을_때_값이_서고_다시_쓴_문서는_읽어도_그대로다()
    {
        // 직선으로 가는 두 키 — 기울기를 적지 않았다.
        const string json = """
            { "schema": "motion-authoring/2",
              "motions": [ { "name": "a", "rig": "character", "duration": 1, "end": "commit",
                "channels": [ { "node": "CharacterPortrait_Track_Move", "kind": "position",
                  "x": { "keys": [ { "t": 0, "v": 0, "tangent": "linear" }, { "t": 1, "v": 10, "tangent": "linear" } ] } } ] } ] }
            """;

        MotionDocument read = MotionDocumentJson.Read(json);
        KeyAsset first = read.Find("a")!.Channels[0].X!.Keys[0];
        Assert.Equal(10f, first.OutTangent, 3); // 앱에서 열어도 mt로 읽어도 같은 곡선이다

        string written = MotionDocumentJson.Write(read);
        Assert.Equal(written, MotionDocumentJson.Write(MotionDocumentJson.Read(written)));
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
