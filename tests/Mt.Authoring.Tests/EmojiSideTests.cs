using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 좌우(PLAN-emoji.md E8) — 이모지의 <c>fixedSide</c> · 리그의 <c>upright</c>(런타임 <c>docs/emoji-format.md</c> §6).
/// 모델 · 편집 명령 · 저작본 · 내보내기(E8-2) · 왼쪽을 보는 인물로 보기(E8-3 — 런타임 <c>EmojiPlaybackTests</c>의 뒤집힌 로케이터 둘과 같은 견본 · 같은 값).
/// </summary>
public sealed class EmojiSideTests
{
    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    private static JsonObject Exported(MotionDocument document)
    {
        Assert.True(EmojiExport.TryExport(document, null, out string json, out IReadOnlyList<EmojiProblem> problems), string.Join("\n", problems));
        return JsonNode.Parse(json)!["emojis"]![0]!.AsObject();
    }

    [Fact]
    public void 두_칸은_한_칸씩_기록되고_같은_값은_기록하지_않는다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        long before = editor.Revision;

        editor.SetEmojiFixedSide("star", false); // 이미 거짓
        editor.SetEmojiRigUpright("star", "body", false);
        Assert.Equal(before, editor.Revision);

        editor.SetEmojiFixedSide("star", true);
        Assert.Equal(before + 1, editor.Revision);
        editor.SetEmojiRigUpright("star", "spark", true);
        Assert.Equal(before + 2, editor.Revision);
        editor.SetEmojiRigUpright("star", "spark", true);
        Assert.Equal(before + 2, editor.Revision);

        EmojiAsset star = editor.Document.FindEmoji("star")!;
        Assert.True(star.FixedSide);
        Assert.True(star.FindRig("spark")!.Upright);
        Assert.False(star.FindRig("body")!.Upright);

        editor.Undo();
        Assert.False(editor.Document.FindEmoji("star")!.FindRig("spark")!.Upright);
        editor.Undo();
        Assert.False(editor.Document.FindEmoji("star")!.FixedSide);

        Assert.Throws<KeyNotFoundException>(() => editor.SetEmojiRigUpright("star", "ghost", true));
    }

    [Fact]
    public void 저작본은_참일_때만_적고_다시_읽으며_복제가_가져간다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        string plain = MotionDocumentJson.Write(editor.Document);
        Assert.DoesNotContain("fixedSide", plain); // 거짓이면 글자가 전과 같다
        Assert.DoesNotContain("upright", plain);

        editor.SetEmojiFixedSide("star", true);
        editor.SetEmojiRigUpright("star", "spark", true);
        string text = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"fixedSide\": true", text);
        Assert.Contains("\"upright\": true", text);

        EmojiAsset reread = MotionDocumentJson.Read(text).FindEmoji("star")!;
        Assert.True(reread.FixedSide);
        Assert.True(reread.FindRig("spark")!.Upright);
        Assert.False(reread.FindRig("body")!.Upright);

        EmojiAsset copy = editor.DuplicateEmoji("star", "star_2");
        Assert.True(copy.FixedSide);
        Assert.True(copy.FindRig("spark")!.Upright);
    }

    [Fact]
    public void 내보내기는_참일_때만_적고_런타임_로더가_두_칸을_읽는다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        JsonObject plain = Exported(editor.Document);
        Assert.Null(plain["fixedSide"]);
        Assert.All(plain["rigs"]!.AsArray(), r => Assert.Null(r!["upright"]));

        editor.SetEmojiRigUpright("star", "spark", true);
        JsonObject upright = Exported(editor.Document);
        Assert.Null(upright["fixedSide"]);
        Assert.Null(upright["rigs"]![0]!["upright"]);
        Assert.True(upright["rigs"]![1]!["upright"]!.GetValue<bool>());

        editor.SetEmojiFixedSide("star", true);
        JsonObject sided = Exported(editor.Document);
        Assert.True(sided["fixedSide"]!.GetValue<bool>());

        // 미리보기가 쓰는 정의(코어 로더로 읽은 것)에 두 칸이 실린다.
        EmojiDefinition definition = EmojiPreview.Definition(editor.Document.FindEmoji("star")!, Frame, out string? why)!;
        Assert.Null(why);
        Assert.True(definition.FixedSide);
        Assert.Equal(new[] { false, true }, definition.Rigs.Select(r => r.Upright));
    }

    // ── 왼쪽을 보는 인물로 보기(E8-3) ─────────────────────────────────

    /// <summary>런타임 견본 Sided와 같다 — 오프셋 (100, 50) · 리그 둘: a는 보통, q는 바로 서야 하는 그림(기본 배율 0.5 · 회전 10°). 키는 없다.</summary>
    private static MotionEditor Sided(bool fixedSide)
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddEmoji("sided");
        editor.SetEmojiOffset("sided", 100f, 50f);
        editor.AddEmojiRig("sided", "a", new[] { "art/s.png" });
        editor.SetEmojiRigPlace("sided", "a", 0f, 0f, 0.5f, 10f);
        editor.AddEmojiRig("sided", "q", new[] { "art/s.png" });
        editor.SetEmojiRigPlace("sided", "q", 30f, 20f, 0.5f, 10f);
        editor.SetEmojiRigUpright("sided", "q", true);
        editor.SetEmojiFixedSide("sided", fixedSide);
        return editor;
    }

    private static EmojiPose PoseOf(MotionEditor editor, bool facingLeft)
    {
        EmojiAsset emoji = editor.Document.FindEmoji("sided")!;
        EmojiDefinition definition = EmojiPreview.Definition(emoji, Frame, out _)!;
        return EmojiPreview.Pose(emoji, Frame, EmojiPreview.Timeline(emoji, Frame), 0f, null, definition, facingLeft);
    }

    private static Vec2 At(RectNodeTree tree, string node, float x = 0f, float y = 0f) => tree.TransformPoint(node, new Vec3(x, y, 0f)).XY;

    [Fact]
    public void 뒤집힌_로케이터_밑의_보통_이모지는_그대로이고_바로_서야_하는_리그만_되뒤집힌다()
    {
        MotionEditor editor = Sided(fixedSide: false);
        RectNodeTree right = PoseOf(editor, false).Tree;
        RectNodeTree left = PoseOf(editor, true).Tree;

        // 로케이터가 뒤집힌다(가로 배율 −1). 이모지의 값은 그대로다.
        string locator = left.GetParentKey(Frame.OffsetNode);
        Assert.Equal(-1f, left.GetState(locator).LocalScale.X, 4);
        Assert.Equal(new Vec2(100f, 50f), left.GetState(Frame.OffsetNode).AnchoredPosition);
        Assert.Equal(1f, left.GetState(Frame.OffsetNode).LocalScale.X, 4);

        // 보통 리그는 그대로, 바로 서야 하는 리그는 가로 배율과 회전의 부호가 바뀐다.
        Assert.Equal(0.5f, left.GetState(Frame.PlaceNodeOf("a")).LocalScale.X, 4);
        Assert.Equal(10f, left.GetState(Frame.PlaceNodeOf("a")).LocalEulerAngles.Z, 2);
        Assert.Equal(-0.5f, left.GetState(Frame.PlaceNodeOf("q")).LocalScale.X, 4);
        Assert.Equal(0.5f, left.GetState(Frame.PlaceNodeOf("q")).LocalScale.Y, 4);
        Assert.Equal(-10f, left.GetState(Frame.PlaceNodeOf("q")).LocalEulerAngles.Z, 2);
        Assert.Equal(0.5f, right.GetState(Frame.PlaceNodeOf("q")).LocalScale.X, 4); // 바로 보면 그대로

        // 화면에서: 자리는 거울 쪽(x가 반대), 보통 그림은 거울에 비치고 바로 서야 하는 그림은 만든 그대로 선다.
        foreach (string rig in new[] { "a", "q" })
        {
            string image = Frame.ImageNodeOf(rig);
            Assert.Equal(-At(right, image).X, At(left, image).X, 2);
            Assert.Equal(At(right, image).Y, At(left, image).Y, 2);
        }

        string upright = Frame.ImageNodeOf("q");
        Vec2 axisRight = At(right, upright, 1f) - At(right, upright);
        Vec2 axisLeft = At(left, upright, 1f) - At(left, upright);
        Assert.Equal(axisRight.X, axisLeft.X, 3); // 그림의 가로축 — 뒤집히지 않았고 기울기도 같다
        Assert.Equal(axisRight.Y, axisLeft.Y, 3);

        string plain = Frame.ImageNodeOf("a");
        Vec2 plainRight = At(right, plain, 1f) - At(right, plain);
        Vec2 plainLeft = At(left, plain, 1f) - At(left, plain);
        Assert.Equal(-plainRight.X, plainLeft.X, 3); // 보통 그림은 거울에 비친다
        Assert.Equal(plainRight.Y, plainLeft.Y, 3);
    }

    [Fact]
    public void 방향을_따르지_않는_이모지는_뒤집힌_로케이터_밑에서_오프셋이_되뒤집어_만든_쪽에_선다()
    {
        MotionEditor editor = Sided(fixedSide: true);
        RectNodeTree right = PoseOf(editor, false).Tree;
        RectNodeTree left = PoseOf(editor, true).Tree;

        Assert.Equal(new Vec2(-100f, 50f), left.GetState(Frame.OffsetNode).AnchoredPosition);
        Assert.Equal(-1f, left.GetState(Frame.OffsetNode).LocalScale.X, 4);
        Assert.Equal(1f, left.GetState(Frame.OffsetNode).LocalScale.Y, 4);
        Assert.Equal(0.5f, left.GetState(Frame.PlaceNodeOf("q")).LocalScale.X, 4); // 거울에 비치지 않으니 리그도 더 뒤집지 않는다

        // 화면에서는 바로 볼 때와 같다(로케이터의 자리가 기본값 — 가운데라서).
        foreach (string rig in new[] { "a", "q" })
        {
            string image = Frame.ImageNodeOf(rig);
            Assert.Equal(At(right, image).X, At(left, image).X, 2);
            Assert.Equal(At(right, image).Y, At(left, image).Y, 2);
            Assert.Equal((At(right, image, 1f) - At(right, image)).X, (At(left, image, 1f) - At(left, image)).X, 3);
        }

        Assert.Equal(new Vec2(100f, 50f), right.GetState(Frame.OffsetNode).AnchoredPosition);
    }

    [Fact]
    public void 이모지_전체의_x_키는_뒤집히고_바로_서야_하는_리그의_x_키는_만든_그대로다()
    {
        MotionEditor editor = Sided(fixedSide: false);
        string timeline = EmojiAsset.TimelineNameOf("sided");
        editor.AddChannel(timeline, Frame.OffsetNode, ChannelKind.Position);
        editor.SetKeys(timeline, Frame.OffsetNode, ChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 40f), Fixtures.Key(1f, 40f) });
        editor.AddChannel(timeline, Frame.ImageNodeOf("q"), ChannelKind.Position);
        editor.SetKeys(timeline, Frame.ImageNodeOf("q"), ChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 25f), Fixtures.Key(1f, 25f) });

        RectNodeTree right = PoseOf(editor, false).Tree;
        RectNodeTree left = PoseOf(editor, true).Tree;
        string offset = Frame.OffsetNode;
        string q = Frame.ImageNodeOf("q");

        // 이모지 전체는 통째로 거울에 — 키로 간 40도 반대쪽.
        Assert.Equal(-At(right, offset).X, At(left, offset).X, 2);

        // upright 리그의 키(그림 노드의 x 25)는 만든 그대로 오른쪽으로 간다 — 바깥으로 흐르던 것이 안쪽으로(규범 §6).
        float keyRight = (At(right, q) - At(right, Frame.PlaceNodeOf("q"))).X;
        float keyLeft = (At(left, q) - At(left, Frame.PlaceNodeOf("q"))).X;
        Assert.True(keyRight > 0f && keyLeft > 0f, $"{keyRight} · {keyLeft}");
        Assert.Equal(keyRight, keyLeft, 2);
    }

    [Fact]
    public void mt_pose의_facing_left는_왼쪽을_보는_인물_밑의_모습이다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"emoji-side-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "e.motion-authoring.json");
        File.WriteAllText(path, MotionDocumentJson.Write(Sided(fixedSide: false).Document));

        JsonNode Pose(params string[] extra)
        {
            var stdout = new StringWriter();
            Assert.Equal(0, Mt.Cli.MtCli.Run(new[] { "pose", path, "--emoji", "sided", "--time", "0", "--view", "timeline" }.Concat(extra).ToArray(), stdout, new StringWriter()));
            return JsonNode.Parse(stdout.ToString())!;
        }

        JsonNode right = Pose();
        JsonNode left = Pose("--facing", "left");
        Assert.Equal("right", right["facing"]!.GetValue<string>());
        Assert.Equal("left", left["facing"]!.GetValue<string>());

        for (int i = 0; i < 2; i++)
        {
            double x = right["rigs"]![i]!["at"]![0]!.GetValue<double>();
            Assert.Equal(-x, left["rigs"]![i]!["at"]![0]!.GetValue<double>(), 2);
        }

        Assert.Equal(2, Mt.Cli.MtCli.Run(new[] { "pose", path, "--emoji", "sided", "--time", "0", "--facing", "up" }, new StringWriter(), new StringWriter()));
    }
}
