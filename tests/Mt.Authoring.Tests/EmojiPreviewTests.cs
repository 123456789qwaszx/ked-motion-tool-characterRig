using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>이모지의 미리보기와 대조(PLAN-emoji.md E5) — 툴이 낸 파일을 코어로 읽어 프레임마다 그래프의 값과 잰다. 툴 = 게임.</summary>
public sealed class EmojiPreviewTests
{
    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    /// <summary>모든 채널 종류가 든 이모지 — 별 이모지에 자리 · 회전 · 그림(몸)과 자리 · 배율 · 회전(전체)을 더한다.</summary>
    private static MotionEditor Busy()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        string timeline = EmojiAsset.TimelineNameOf("star");
        string body = Frame.ImageNodeOf("body");
        string spark = Frame.ImageNodeOf("spark");
        editor.SetEmojiOffset("star", 170f, 60f);

        editor.AddChannel(timeline, body, ChannelKind.Position);
        editor.SetKeys(timeline, body, ChannelKind.Position, Axis.X, new[]
        {
            new KeyAsset { T = 0f, V = -10f, Tangent = TangentMode.Auto }, new KeyAsset { T = 0.4f, V = 30f, Tangent = TangentMode.Spline }, new KeyAsset { T = 1f, V = 0f, Tangent = TangentMode.Auto },
        });
        editor.AddChannel(timeline, body, ChannelKind.Euler);
        editor.SetKeys(timeline, body, ChannelKind.Euler, Axis.Z, new[]
        {
            new KeyAsset { T = 0f, V = -20f, Tangent = TangentMode.Plateau }, new KeyAsset { T = 0.3f, V = 8f, Tangent = TangentMode.Step }, new KeyAsset { T = 0.6f, V = 0f, Tangent = TangentMode.Linear },
        });
        editor.AddChannel(timeline, body, ChannelKind.Image);
        editor.SetKeys(timeline, body, ChannelKind.Image, Axis.X, new[] { Fixtures.Key(0.1f, 1f), Fixtures.Key(0.7f, 0f) });
        editor.AddChannel(timeline, spark, ChannelKind.Alpha);
        editor.SetKeys(timeline, spark, ChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.5f, 1f), Fixtures.Key(1f, 0.2f) });
        editor.AddChannel(timeline, Frame.OffsetNode, ChannelKind.Position);
        editor.SetKeys(timeline, Frame.OffsetNode, ChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(1f, 20f) });
        editor.AddChannel(timeline, Frame.OffsetNode, ChannelKind.Euler);
        editor.SetKeys(timeline, Frame.OffsetNode, ChannelKind.Euler, Axis.Z, new[] { Fixtures.Key(0f, 5f), Fixtures.Key(0.5f, -5f) });
        return editor;
    }

    /// <summary>타임라인의 (대상, 코어 채널) → 그 성분의 키. 그림 채널은 빼고.</summary>
    private static IEnumerable<(string Rig, EmojiChannel Channel, IReadOnlyList<KeyAsset> Keys)> NumberCurves(EmojiAsset emoji)
    {
        foreach (ChannelAsset channel in emoji.Timeline.Channels)
        {
            Assert.True(Frame.TryTarget(channel.Node, out string rig));
            (Axis, EmojiChannel)[] map = channel.Kind switch
            {
                ChannelKind.Position => new[] { (Axis.X, EmojiChannel.PositionX), (Axis.Y, EmojiChannel.PositionY) },
                ChannelKind.Scale => new[] { (Axis.X, EmojiChannel.ScaleX), (Axis.Y, EmojiChannel.ScaleY) },
                ChannelKind.Euler => new[] { (Axis.Z, EmojiChannel.Rotation) },
                ChannelKind.Alpha => new[] { (Axis.X, EmojiChannel.Alpha) },
                _ => Array.Empty<(Axis, EmojiChannel)>(),
            };

            foreach ((Axis axis, EmojiChannel token) in map)
            {
                if (channel.Get(axis) is { } curve)
                {
                    yield return (rig, token, curve.Keys);
                }
            }
        }
    }

    [Fact]
    public void 툴이_낸_파일을_코어로_읽으면_클립의_프레임마다_그래프의_값과_같다()
    {
        MotionEditor editor = Busy();
        Assert.True(EmojiExport.TryExport(editor.Document, null, out string json, out IReadOnlyList<EmojiProblem> problems), string.Join("\n", problems));
        EmojiAsset star = editor.Document.FindEmoji("star")!;
        EmojiDefinition definition = EmojiDefinitionLoader.Build(EmojiExport.Read(json))["star"];

        foreach (EmojiClip clip in definition.Clips)
        {
            EmojiSpan span = star.FindSpan(clip.Name)!;

            for (int f = 0; f < clip.Length; f++)
            {
                float seconds = EmojiAsset.SecondsOf(span.Start + f);

                foreach ((string rig, EmojiChannel token, IReadOnlyList<KeyAsset> keys) in NumberCurves(star))
                {
                    float graph = CurveFunctions.Evaluate(TangentMath.ToCurveKeys(keys), seconds);
                    Assert.True(Math.Abs(graph - EmojiClipMath.Sample(clip, rig, token, f)) < 1e-3f, $"{clip.Name} {f}fr {rig} {token}: 그래프 {graph}, 코어 {EmojiClipMath.Sample(clip, rig, token, f)}");
                }

                // 그림 — 그 시각에 보이는 장(첫 키 앞은 기본 그림).
                KeyAsset? shown = star.Timeline.FindChannel(Frame.ImageNodeOf("body"), ChannelKind.Image)!.X!.Keys.LastOrDefault(k => k.T <= seconds + 1e-6f);
                string expected = EmojiRigAsset.SpriteKeyOf(star.FindRig("body")!.Images[shown is null ? 0 : (int)shown.V]);
                Assert.Equal(expected, EmojiClipMath.SampleSprite(clip, "body", f) ?? star.FindRig("body")!.DefaultSprite);
            }
        }
    }

    [Fact]
    public void 편집_중의_타임라인도_같은_길로_읽는다()
    {
        EmojiAsset star = Busy().Document.FindEmoji("star")!;
        EmojiClip timeline = EmojiPreview.Timeline(star, Frame)!;

        Assert.Equal(star.LastFrame + 1, timeline.Length);

        for (int f = 0; f < timeline.Length; f++)
        {
            foreach ((string rig, EmojiChannel token, IReadOnlyList<KeyAsset> keys) in NumberCurves(star))
            {
                float graph = CurveFunctions.Evaluate(TangentMath.ToCurveKeys(keys), EmojiAsset.SecondsOf(f));
                Assert.True(Math.Abs(graph - EmojiClipMath.Sample(timeline, rig, token, f)) < 1e-3f, $"{f}fr {rig} {token}");
            }
        }
    }

    [Fact]
    public void 그_프레임의_모습은_런타임이_노드에_옮기는_법과_같다()
    {
        EmojiAsset star = Busy().Document.FindEmoji("star")!;
        EmojiClip timeline = EmojiPreview.Timeline(star, Frame)!;

        EmojiPose pose = EmojiPreview.Pose(star, Frame, timeline, 12f);

        // 이모지 전체: 자리의 키는 오프셋에 더한다.
        RectNodeState offset = pose.Tree.GetState("Emoji_Offset");
        Assert.Equal(170f, offset.AnchoredPosition.X, 3);
        Assert.Equal(60f + EmojiClipMath.Sample(timeline, "", EmojiChannel.PositionY, 12f), offset.AnchoredPosition.Y, 3);
        Assert.Equal(EmojiClipMath.Sample(timeline, "", EmojiChannel.Rotation, 12f), offset.LocalEulerAngles.Z, 3);

        // 리그: 기본 자리는 그대로, 키는 그림 노드가 받는다.
        Assert.Equal(0.45f, pose.Tree.GetState("body/EmojiRig_Place").LocalScale.X, 4);
        EmojiNodePose body = EmojiClipMath.SampleNode(timeline, "body", 12f);
        RectNodeState image = pose.Tree.GetState("body/EmojiRig_Image");
        Assert.Equal(body.Position.X, image.AnchoredPosition.X, 3);
        Assert.Equal(body.Scale.X, image.LocalScale.X, 3);
        Assert.Equal(body.Rotation, image.LocalEulerAngles.Z, 3);

        EmojiRigPose bodyPose = pose.Rigs[0];
        Assert.Equal("star_2", bodyPose.Sprite); // 프레임 2.4에 둘째 장, 16.8에 첫 장으로 돌아간다
        Assert.Equal("art/star_2.png", bodyPose.ImagePath);
        Assert.Equal(EmojiClipMath.Sample(timeline, "spark", EmojiChannel.Alpha, 12f), pose.Rigs[1].Alpha, 4);
        Assert.Equal("spark", pose.Rigs[1].Sprite); // 그림 채널이 없으면 기본 그림
    }

    [Fact]
    public void 수명은_코어의_상태_기계를_돌린다()
    {
        EmojiDefinition definition = EmojiPreview.Definition(EmojiModelTests.StarEditor().Document.FindEmoji("star")!, Frame, out string? why)!;
        Assert.Null(why);

        // 등장 5fr → 머묾 16fr ⟲ → 내리면 퇴장 7fr.
        Assert.Equal(EmojiPhase.In, EmojiPreview.Moment(definition, 0).Phase);
        EmojiMoment hold = EmojiPreview.Moment(definition, 6 / 24.0);
        Assert.Equal(EmojiPhase.Hold, hold.Phase);
        Assert.Equal(1f, hold.Frame, 3);
        Assert.Equal(EmojiPhase.Hold, EmojiPreview.Moment(definition, 100).Phase); // 내리지 않으면 머문다

        double off = EmojiPreview.OffAt(definition, 2)!.Value;
        Assert.Equal((5 + 32) / 24.0, off, 6);
        EmojiMoment leaving = EmojiPreview.Moment(definition, off + 3 / 24.0, off);
        Assert.Equal(EmojiPhase.Out, leaving.Phase);
        Assert.Equal(3f, leaving.Frame, 3);
        Assert.Equal(EmojiPhase.Done, EmojiPreview.Moment(definition, EmojiPreview.LifeSeconds(definition, 2) + 0.01, off).Phase);

        // 머묾이 없으면 main을 한 번 재생하고 사라진다.
        MotionEditor once = EmojiModelTests.StarEditor();
        once.SetEmojiLinks("star", null, null, null);
        EmojiDefinition main = EmojiPreview.Definition(once.Document.FindEmoji("star")!, Frame, out _)!;
        Assert.Null(EmojiPreview.OffAt(main, 2));
        Assert.Equal(25 / 24.0, EmojiPreview.LifeSeconds(main, 2), 6);
        Assert.Equal(EmojiPhase.Main, EmojiPreview.Moment(main, 0.5).Phase);
        Assert.Equal(EmojiPhase.Done, EmojiPreview.Moment(main, 1.1).Phase);
    }

    [Fact]
    public void mt_pose는_이모지의_수명과_타임라인을_읽는다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"emoji-pose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "e.motion-authoring.json");
        File.WriteAllText(path, MotionDocumentJson.Write(Busy().Document));

        var stdout = new StringWriter();
        Assert.Equal(0, MtCli.Run(new[] { "pose", path, "--emoji", "star", "--time", "1.6", "--off", "1.5" }, stdout, new StringWriter()));
        JsonNode life = JsonNode.Parse(stdout.ToString())!;
        Assert.Equal("out", life["phase"]!.GetValue<string>());
        Assert.Equal("leave", life["clip"]!.GetValue<string>());
        Assert.Equal(2, life["rigs"]!.AsArray().Count);

        stdout = new StringWriter();
        Assert.Equal(0, MtCli.Run(new[] { "pose", path, "--emoji", "star", "--time", "0.5", "--view", "timeline" }, stdout, new StringWriter()));
        JsonNode timeline = JsonNode.Parse(stdout.ToString())!;
        Assert.Equal(12, timeline["frame"]!.GetValue<double>(), 4);
        Assert.Equal(170, timeline["offset"]!["position"]![0]!.GetValue<double>(), 3);
        Assert.Equal("star_2", timeline["rigs"]![0]!["sprite"]!.GetValue<string>());
    }
}
