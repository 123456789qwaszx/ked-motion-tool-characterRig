using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>emojis.json(emojis/2) 내보내기(PLAN-emoji.md E4) — 구간 자르기 · 초 → 프레임 · 채널 글자 · 같은 규칙.</summary>
public sealed class EmojiExportTests
{
    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    private static JsonObject ExportOk(MotionDocument document)
    {
        Assert.True(EmojiExport.TryExport(document, null, out string json, out IReadOnlyList<EmojiProblem> problems), string.Join("\n", problems));
        return JsonNode.Parse(json)!.AsObject();
    }

    [Fact]
    public void 머무는_이모지는_구간마다_따로_선_클립과_잇기로_나간다()
    {
        JsonObject file = ExportOk(EmojiModelTests.StarEditor().Document);

        Assert.Equal("emojis/2", file["schema"]!.GetValue<string>());
        Assert.Equal(24, file["fps"]!.GetValue<double>());
        JsonObject star = file["emojis"]![0]!.AsObject();

        Assert.Equal(new[] { "enter", "loop", "leave" }, star["clips"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()));
        Assert.Equal(new[] { 5, 16, 7 }, star["clips"]!.AsArray().Select(c => c!["length"]!.GetValue<int>()));
        Assert.Equal("enter", star["in"]!.GetValue<string>());
        Assert.Equal("loop", star["hold"]!.GetValue<string>());
        Assert.Equal("leave", star["out"]!.GetValue<string>());
        Assert.Null(star["main"]);

        JsonObject body = star["rigs"]![0]!.AsObject();
        Assert.Equal("star", body["sprite"]!.GetValue<string>()); // 첫 그림의 파일 이름
        Assert.Equal(0.45, body["scale"]!.GetValue<double>(), 4);
        Assert.Null(star["rigs"]![1]!["scale"]); // 1은 적지 않는다

        // 이모지 전체는 빈 리그 이름이다.
        Assert.Contains(star["clips"]![2]!["tracks"]!.AsArray(), t => t!["rig"]!.GetValue<string>() == "" && t["channel"]!.GetValue<string>() == "alpha");

        // 런타임 로더가 받는다.
        EmojiDefinition definition = EmojiExport.ToDefinitions(EmojiModelTests.StarEditor().Document)["star"];
        Assert.True(definition.Stays);
        Assert.Equal(16, definition.Hold.Length);
    }

    [Fact]
    public void 머묾이_없으면_타임라인_전체가_main이다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        editor.SetEmojiLinks("star", null, null, null);

        JsonObject star = ExportOk(editor.Document)["emojis"]![0]!.AsObject();

        Assert.Equal("main", star["main"]!.GetValue<string>());
        JsonObject main = star["clips"]![0]!.AsObject();
        Assert.Equal("main", main["name"]!.GetValue<string>());
        Assert.Equal(25, main["length"]!.GetValue<int>()); // 0 … 24fr
        Assert.Null(star["hold"]);
        Assert.False(EmojiExport.ToDefinitions(editor.Document)["star"].Stays);
    }

    [Fact]
    public void 잘라도_모양이_같다_구간의_프레임마다_코어의_값이_타임라인과_같다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        string timeline = EmojiAsset.TimelineNameOf("star");
        string body = Frame.ImageNodeOf("body");
        editor.AddChannel(timeline, body, ChannelKind.Position);
        editor.SetKeys(timeline, body, ChannelKind.Position, Axis.Y, new[]
        {
            new KeyAsset { T = 0f, V = 0f, Tangent = TangentMode.Auto },
            new KeyAsset { T = 0.3f, V = 40f, Tangent = TangentMode.Spline },
            new KeyAsset { T = 0.55f, V = 10f, Tangent = TangentMode.Step },
            new KeyAsset { T = 0.8f, V = -20f, Tangent = TangentMode.Auto },
            new KeyAsset { T = 1f, V = 0f, Tangent = TangentMode.Flat },
        });
        editor.SetEmojiSpan("star", "loop", 9, 20); // 스플라인 한가운데서 시작해 계단 안에서 끝나지 않게 — 계단 구간(13.2~19.2fr)을 지난다
        editor.AddEmojiSpan("star", "inside", 14, 18); // 계단 안에서 시작한다

        EmojiAsset star = editor.Document.FindEmoji("star")!;
        EmojiDefinition definition = EmojiExport.ToDefinitions(editor.Document)[star.Name];

        foreach (EmojiClip clip in definition.Clips)
        {
            EmojiSpan span = star.FindSpan(clip.Name)!;

            foreach ((ChannelAsset channel, Axis axis, EmojiChannel token) in new[]
            {
                (star.Timeline.FindChannel(body, ChannelKind.Position)!, Axis.Y, EmojiChannel.PositionY),
                (star.Timeline.FindChannel(body, ChannelKind.Scale)!, Axis.X, EmojiChannel.ScaleX),
            })
            {
                CurveKey[] tool = TangentMath.ToCurveKeys(channel.Get(axis)!.Keys);

                for (int f = 0; f < clip.Length; f++)
                {
                    float expected = CurveFunctions.Evaluate(tool, EmojiAsset.SecondsOf(span.Start + f));
                    Assert.Equal(expected, EmojiClipMath.Sample(clip, "body", token, f), 0.001f);
                }
            }
        }
    }

    [Fact]
    public void 계단_키와_그림_키는_계단으로_나가고_그림은_스프라이트_키다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        string timeline = EmojiAsset.TimelineNameOf("star");
        string body = Frame.ImageNodeOf("body");
        editor.AddChannel(timeline, body, ChannelKind.Image);
        editor.SetKeys(timeline, body, ChannelKind.Image, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.25f, 1f), Fixtures.Key(0.5f, 0f) });
        editor.SetTangentMode(timeline, new[] { new KeyRef(new CurveRef(Frame.OffsetNode, ChannelKind.Alpha, Axis.X), 0) }, TangentMode.Step);
        editor.SetEmojiSpan("star", "loop", 8, 20); // 프레임 6에 넘긴 둘째 그림이 보이는 채 시작한다

        JsonObject star = ExportOk(editor.Document)["emojis"]![0]!.AsObject();
        JsonObject loop = star["clips"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "loop")!.AsObject();
        JsonArray sprite = loop["tracks"]!.AsArray().Single(t => t!["channel"]!.GetValue<string>() == "sprite")!["keys"]!.AsArray();

        Assert.Equal("{\"f\":0,\"s\":\"star_2\"}", sprite[0]!.ToJsonString());
        Assert.Equal("{\"f\":4,\"s\":\"star\"}", sprite[1]!.ToJsonString());

        JsonArray alpha = star["clips"]![0]!["tracks"]!.AsArray().Single(t => t!["rig"]!.GetValue<string>() == "")!["keys"]!.AsArray();
        Assert.True(alpha[0]!["step"]!.GetValue<bool>());
    }

    [Fact]
    public void 기울기는_프레임당이다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        string timeline = EmojiAsset.TimelineNameOf("star");
        editor.AddChannel(timeline, Frame.OffsetNode, ChannelKind.Position);
        editor.SetKeys(timeline, Frame.OffsetNode, ChannelKind.Position, Axis.X, new[]
        {
            new KeyAsset { T = 0f, V = 0f, OutTangent = 48f, InTangent = 48f, Tangent = TangentMode.Free },
            new KeyAsset { T = 1f, V = 48f, OutTangent = 48f, InTangent = 48f, Tangent = TangentMode.Free },
        });
        editor.SetEmojiLinks("star", null, null, null);

        JsonObject star = ExportOk(editor.Document)["emojis"]![0]!.AsObject();
        JsonObject first = star["clips"]![0]!["tracks"]!.AsArray().Single(t => t!["channel"]!.GetValue<string>() == "x")!["keys"]![0]!.AsObject();

        Assert.Equal(2.0, first["o"]!.GetValue<double>(), 4); // 초당 48 = 프레임당 2
    }

    [Fact]
    public void 막는_문제가_있으면_내지_않는다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        editor.AddEmoji("bare");

        Assert.False(EmojiExport.TryExport(editor.Document, null, out string json, out IReadOnlyList<EmojiProblem> problems));
        Assert.Equal("", json);
        Assert.Contains(problems, p => p.EmojiName == "bare");
    }

    [Fact]
    public void mt_emojis는_파일을_쓰고_info와_validate가_이모지를_본다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"emoji-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "e.motion-authoring.json");
        File.WriteAllText(path, MotionDocumentJson.Write(EmojiModelTests.StarEditor().Document));
        string target = Path.Combine(directory, "emojis.json");

        var stdout = new StringWriter();
        Assert.Equal(0, MtCli.Run(new[] { "emojis", path, target }, stdout, new StringWriter()));
        Assert.True(EmojiExport.TryExport(EmojiModelTests.StarEditor().Document, null, out string expected, out _));
        Assert.Equal(expected, File.ReadAllText(target));

        stdout = new StringWriter();
        Assert.Equal(0, MtCli.Run(new[] { "info", path }, stdout, new StringWriter()));
        JsonNode info = JsonNode.Parse(stdout.ToString())!;
        Assert.Equal("star", info["emojis"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("loop 5~20", info["emojis"]![0]!["clips"]![1]!.GetValue<string>());

        var broken = new MotionEditor(MotionDocumentJson.Read(File.ReadAllText(path)));
        broken.AddEmoji("bare");
        File.WriteAllText(path, MotionDocumentJson.Write(broken.Document));
        stdout = new StringWriter();
        Assert.Equal(1, MtCli.Run(new[] { "validate", path }, stdout, new StringWriter()));
        Assert.Contains("\"kind\": \"emoji\"", stdout.ToString());
        Assert.Equal(1, MtCli.Run(new[] { "emojis", path, target }, new StringWriter(), new StringWriter()));
    }
}
