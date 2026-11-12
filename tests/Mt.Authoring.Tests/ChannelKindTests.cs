using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>툴 쪽 채널 종류(PLAN-emoji.md E2) — 코어의 셋 + 불투명도 · 그림, 그리고 계단 키.</summary>
public sealed class ChannelKindTests
{
    private const string Node = "CharacterPortrait_Track_Move_Y";

    [Fact]
    public void 종류마다_성분과_중립값과_쓰는_곳이_정해져_있다()
    {
        Assert.Equal(new[] { Axis.X, Axis.Y }, ChannelKinds.Axes(ChannelKind.Position));
        Assert.Equal(new[] { Axis.X, Axis.Y }, ChannelKinds.Axes(ChannelKind.Scale));
        Assert.Equal(new[] { Axis.X, Axis.Y, Axis.Z }, ChannelKinds.Axes(ChannelKind.Euler));
        Assert.Equal(new[] { Axis.X }, ChannelKinds.Axes(ChannelKind.Alpha));
        Assert.Equal(new[] { Axis.X }, ChannelKinds.Axes(ChannelKind.Image));

        // 중립값은 코어의 두 표와 같다 — 모션(자리 · 배율 · 회전)과 이모지(불투명도 · 그림).
        foreach (MotionChannelKind core in Enum.GetValues<MotionChannelKind>())
        {
            Assert.Equal(MotionMath.Neutral(core), ChannelKinds.Neutral(ChannelKinds.FromCore(core)));
        }

        Assert.Equal(EmojiClipMath.Neutral(EmojiChannel.Alpha), ChannelKinds.Neutral(ChannelKind.Alpha));
        Assert.Equal(0f, ChannelKinds.Neutral(ChannelKind.Image)); // 0번째 그림 = 기본 스프라이트

        Assert.False(ChannelKinds.IsEmojiOnly(ChannelKind.Position));
        Assert.True(ChannelKinds.IsEmojiOnly(ChannelKind.Alpha));
        Assert.True(ChannelKinds.IsEmojiOnly(ChannelKind.Image));
        Assert.True(ChannelKinds.IsAlwaysStepped(ChannelKind.Image));
        Assert.False(ChannelKinds.IsAlwaysStepped(ChannelKind.Alpha));
    }

    [Fact]
    public void 코어로는_앞의_셋만_건너간다()
    {
        foreach (MotionChannelKind core in Enum.GetValues<MotionChannelKind>())
        {
            Assert.Equal(core, ChannelKinds.ToCore(ChannelKinds.FromCore(core)));
        }

        Assert.False(ChannelKinds.TryToCore(ChannelKind.Alpha, out _));
        Assert.Throws<InvalidOperationException>(() => ChannelKinds.ToCore(ChannelKind.Image));
    }

    [Fact]
    public void 저작본에는_종류가_소문자_이름으로_적히고_그대로_읽힌다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.AddChannel("nod", "Emoji_Offset", ChannelKind.Alpha);
        editor.SetKeys("nod", "Emoji_Offset", ChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.6f, 1f) });

        string json = MotionDocumentJson.Write(editor.Document);

        Assert.Contains("\"kind\": \"position\"", json);
        Assert.Contains("\"kind\": \"euler\"", json);
        Assert.Contains("\"kind\": \"alpha\"", json);

        MotionDocument back = MotionDocumentJson.Read(json);
        Assert.Equal(ChannelKind.Alpha, back.Find("nod")!.Channels[1].Kind);
        Assert.Equal(json, MotionDocumentJson.Write(back));
    }

    [Fact]
    public void 계단_키는_다음_키까지_값을_지키고_저작본에는_무한이_실리지_않는다()
    {
        var keys = new List<KeyAsset>
        {
            new() { T = 0f, V = 0f, Tangent = TangentMode.Step },
            new() { T = 1f, V = 10f, Tangent = TangentMode.Flat },
        };
        TangentMath.Recompute(keys);

        CurveKey[] core = TangentMath.ToCurveKeys(keys);
        Assert.True(float.IsPositiveInfinity(core[0].OutTangent));
        Assert.Equal(0f, CurveFunctions.Evaluate(core, 0.999f));
        Assert.Equal(10f, CurveFunctions.Evaluate(core, 1f));

        // 그리는 점도 계단이다 — 다음 키 바로 앞에 앞 키의 값이 있다.
        List<GraphPoint> points = CurveSampler.Sample(keys, 0, 1, 4);
        Assert.Contains(points, p => p.T > 0.99 && p.T < 1 && p.V == 0);

        // 저장된 기울기는 유한하다(JSON에 무한을 쓸 수 없다). 코어 키에서 다시 올리면 계단이다.
        Assert.Equal(0f, keys[0].OutTangent);
        Assert.Equal(TangentMode.Step, KeyAsset.FromCurveKey(core[0]).Tangent);
    }

    [Fact]
    public void 그림_채널의_키는_어느_길로_넣어도_계단이고_한_칸이다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.AddChannel("nod", "Emoji_Offset", ChannelKind.Image);
        long before = editor.Revision;

        editor.SetKeys("nod", "Emoji_Offset", ChannelKind.Image, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.3f, 1f) });
        editor.AddKey("nod", "Emoji_Offset", ChannelKind.Image, Axis.X, Fixtures.Key(0.5f, 2f));

        ComponentCurve curve = editor.Document.Find("nod")!.FindChannel("Emoji_Offset", ChannelKind.Image)!.X!;
        Assert.All(curve.Keys, k => Assert.Equal(TangentMode.Step, k.Tangent));
        Assert.Equal(before + 2, editor.Revision); // 조작 둘 = 칸 둘(바로잡기는 같은 칸)

        // 다른 모드로 바꿔도 그림 채널은 계단으로 남는다 — 같은 값이라 기록도 없다.
        var key = new KeyRef(new CurveRef("Emoji_Offset", ChannelKind.Image, Axis.X), 1);
        editor.SetTangentMode("nod", new[] { key }, TangentMode.Auto);
        Assert.Equal(TangentMode.Step, curve.Keys[1].Tangent);
        Assert.Equal(before + 2, editor.Revision);

        editor.Undo();
        Assert.Equal(2, editor.Document.Find("nod")!.FindChannel("Emoji_Offset", ChannelKind.Image)!.X!.Keys.Count);
    }

    [Fact]
    public void 계단으로_바꾸기는_한_칸이고_되돌릴_수_있다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        var key = new KeyRef(new CurveRef(Node, ChannelKind.Position, Axis.Y), 1);
        long before = editor.Revision;

        editor.SetTangentMode("nod", new[] { key }, TangentMode.Step);

        KeyAsset stepped = editor.Document.Find("nod")!.FindChannel(Node, ChannelKind.Position)!.Y!.Keys[1];
        Assert.Equal(TangentMode.Step, stepped.Tangent);
        Assert.Equal(before + 1, editor.Revision);

        editor.Undo();
        Assert.Equal(TangentMode.Flat, editor.Document.Find("nod")!.FindChannel(Node, ChannelKind.Position)!.Y!.Keys[1].Tangent);
    }

    [Fact]
    public void 캐릭터_모션에_불투명도_그림_계단_키가_있으면_검증이_막고_내보내지_않는다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.AddChannel("nod", Node, ChannelKind.Alpha);
        editor.SetKeys("nod", Node, ChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 1f), Fixtures.Key(0.6f, 1f) });
        editor.SetTangentMode("bow", new[] { new KeyRef(new CurveRef("CharacterPortrait_SwayPivot", ChannelKind.Euler, Axis.Z), 0) }, TangentMode.Step);

        IReadOnlyList<MotionProblem> problems = MotionValidator.Validate(editor.Document);

        Assert.Contains(problems, p => p.MotionName == "nod" && p.Blocking && p.Message.Contains("alpha은 이모지의 타임라인에만"));
        Assert.Contains(problems, p => p.MotionName == "bow" && p.Blocking && p.Message.Contains("계단 키는 이모지의 타임라인에만"));
        Assert.False(MotionExport.TryExport(editor.Document, null, out _, out _));

        // 코어 클립으로 바로 바꾸면 던진다 — 검증이 앞에서 막는 길이다.
        Assert.Throws<InvalidOperationException>(() => MotionAssetCodec.ToClip(editor.Document.Find("nod")!));
    }

    [Fact]
    public void 미리보기는_불투명도_그림_채널을_빼고_얹는다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.AddChannel("nod", Node, ChannelKind.Alpha);
        editor.SetKeys("nod", Node, ChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.6f, 1f) });
        MotionAsset nod = editor.Document.Find("nod")!;

        RectNodeTree template = Rig.BuiltinTuning.RigSchemas.BuildTree("character");
        RectNodeTree pose = MotionPreview.Pose(template, nod, 0.3);

        Assert.Equal(-18f, pose.GetState(Node).AnchoredPosition.Y - template.GetState(Node).AnchoredPosition.Y, 3);
        Assert.Single(MotionAssetCodec.ToClip(nod, skipEmojiOnly: true).Channels);
    }
}
