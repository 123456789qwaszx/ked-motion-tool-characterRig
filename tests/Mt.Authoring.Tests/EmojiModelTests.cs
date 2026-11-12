using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>이모지의 모델(PLAN-emoji.md E3) — 틀 · 조립한 리그 · 편집 명령 · 저작본 · 검증.</summary>
public sealed class EmojiModelTests
{
    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    /// <summary>리그 둘(body · spark), 몸에 등장 키 · 이모지 전체에 퇴장 키, 구간 셋을 등장 · 머묾 · 퇴장에 이은 이모지.</summary>
    internal static MotionEditor StarEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddEmoji("star");
        editor.AddEmojiRig("star", "body", new[] { "art/star.png", "art/star_2.png" });
        editor.AddEmojiRig("star", "spark", new[] { "art/spark.png" });
        editor.SetEmojiRigPlace("star", "body", 0f, 0f, 0.45f, 0f);

        string timeline = EmojiAsset.TimelineNameOf("star");
        string body = Frame.ImageNodeOf("body");
        editor.AddChannel(timeline, body, ChannelKind.Scale);
        editor.SetKeys(timeline, body, ChannelKind.Scale, Axis.X, new[] { Fixtures.Key(0f, 0.45f), Fixtures.Key(0.125f, 1.27f), Fixtures.Key(1f, 1f) });
        editor.AddChannel(timeline, Frame.OffsetNode, ChannelKind.Alpha);
        editor.SetKeys(timeline, Frame.OffsetNode, ChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 1f), Fixtures.Key(1f, 0f) });

        editor.AddEmojiSpan("star", "enter", 0, 4);
        editor.AddEmojiSpan("star", "loop", 5, 20);
        editor.AddEmojiSpan("star", "leave", 18, 24);
        editor.SetEmojiLinks("star", "enter", "loop", "leave");
        return editor;
    }

    [Fact]
    public void 틀은_덤프의_모양에서_읽는다()
    {
        Assert.Equal("Emoji_Offset", Frame.OffsetNode);
        Assert.Equal("EmojiRig_Place", Frame.PlaceNode);
        Assert.Equal("EmojiRig_Image", Frame.ImageNode);

        Assert.True(Frame.TryTarget("Emoji_Offset", out string whole));
        Assert.Equal("", whole);
        Assert.True(Frame.TryTarget("body/EmojiRig_Image", out string rig));
        Assert.Equal("body", rig);
        Assert.False(Frame.TryTarget("body/EmojiRig_Place", out _)); // 기본 자리는 키를 받지 않는다
        Assert.False(Frame.TryTarget("__root", out _));
    }

    [Fact]
    public void 조립한_리그는_리그마다_두_단을_세우고_기본값을_얹는다()
    {
        EmojiAsset star = StarEditor().Document.FindEmoji("star")!;
        star.OffsetX = 170f;

        RigSchemaRigDto assembled = Frame.Assemble(star);

        Assert.Equal(
            new[] { "__root", "Emoji_Offset", "body/EmojiRig_Place", "body/EmojiRig_Image", "spark/EmojiRig_Place", "spark/EmojiRig_Image" },
            assembled.nodes.Select(n => n.id));
        Assert.Equal("body/EmojiRig_Place", assembled.nodes.Single(n => n.id == "body/EmojiRig_Image").parent);
        Assert.Equal("Emoji_Offset", assembled.nodes.Single(n => n.id == "spark/EmojiRig_Place").parent);

        RectNodeTree tree = RigSchemaLoader.BuildTree(assembled, new RectSpace(new Vec2(1920, 1080), Vec2.Half));
        Assert.Equal(170f, tree.GetState("Emoji_Offset").AnchoredPosition.X);
        Assert.Equal(0.45f, tree.GetState("body/EmojiRig_Place").LocalScale.X);
        Assert.Equal(1f, tree.GetState("body/EmojiRig_Image").LocalScale.X); // 키는 1을 기준으로 잡는다

        // 덤프의 틀 자체는 그대로다.
        Assert.Equal(4, Frame.Dto.nodes.Count);
    }

    [Fact]
    public void 타임라인은_이모지의_것이고_이름으로_찾되_모션_목록에는_없다()
    {
        MotionEditor editor = StarEditor();
        MotionDocument document = editor.Document;

        Assert.Empty(document.Motions);
        MotionAsset timeline = Assert.IsType<MotionAsset>(document.Find("emoji:star"));
        Assert.Same(document.FindEmoji("star")!.Timeline, timeline);
        Assert.Contains(timeline, document.AllMotions);
        Assert.Equal(24, document.FindEmoji("star")!.LastFrame);

        // 모션의 이름 · 지우기 명령은 타임라인을 건드리지 않는다 — 이모지와 함께다.
        Assert.Throws<InvalidOperationException>(() => editor.RemoveMotion("emoji:star"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameMotion("emoji:star", "other"));

        // 캐릭터 모션의 검증은 타임라인을 보지 않는다(불투명도가 있어도 막지 않는다).
        Assert.Empty(MotionValidator.Validate(document));
    }

    [Fact]
    public void 명령은_하나가_한_칸이고_이름을_바꾸면_따라온다()
    {
        MotionEditor editor = StarEditor();
        long before = editor.Revision;

        editor.RenameEmoji("star", "star2");
        Assert.Equal(before + 1, editor.Revision);
        Assert.NotNull(editor.Document.Find("emoji:star2"));
        Assert.Null(editor.Document.Find("emoji:star"));
        Assert.Equal("emoji:star2", editor.Document.FindEmoji("star2")!.Timeline.Name);

        editor.RenameEmojiRig("star2", "body", "core");
        Assert.NotNull(editor.Document.Find("emoji:star2")!.FindChannel("core/EmojiRig_Image", ChannelKind.Scale));

        editor.RenameEmojiSpan("star2", "loop", "idle");
        Assert.Equal("idle", editor.Document.FindEmoji("star2")!.Hold);

        editor.RemoveEmojiSpan("star2", "enter");
        Assert.Null(editor.Document.FindEmoji("star2")!.In);

        editor.RemoveEmojiRig("star2", "core");
        Assert.DoesNotContain(editor.Document.Find("emoji:star2")!.Channels, c => c.Node.StartsWith("core/", StringComparison.Ordinal));
        Assert.Equal(before + 5, editor.Revision);

        // 같은 값은 흔적이 없다.
        editor.SetEmojiOffset("star2", 0f, 0f);
        Assert.Equal(before + 5, editor.Revision);

        for (int i = 0; i < 5; i++)
        {
            editor.Undo();
        }

        Assert.NotNull(editor.Document.FindEmoji("star"));
        Assert.Equal("loop", editor.Document.FindEmoji("star")!.Hold);
        Assert.NotNull(editor.Document.Find("emoji:star")!.FindChannel("body/EmojiRig_Image", ChannelKind.Scale));
    }

    [Fact]
    public void 이름의_규칙과_겹침은_명령이_막는다()
    {
        MotionEditor editor = StarEditor();

        Assert.Throws<InvalidOperationException>(() => editor.AddEmoji("star"));
        Assert.Throws<InvalidOperationException>(() => editor.AddEmoji("Star"));
        Assert.Throws<InvalidOperationException>(() => editor.AddEmojiRig("star", "body"));
        Assert.Throws<InvalidOperationException>(() => editor.AddEmojiSpan("star", "main", 0, 1));
        Assert.Throws<InvalidOperationException>(() => editor.AddEmojiSpan("star", "late", 5, 4));
        Assert.Throws<KeyNotFoundException>(() => editor.SetEmojiLinks("star", null, "nothing", null));
        Assert.Throws<InvalidOperationException>(() => editor.SetEmojiRigPlace("star", "body", 0f, 0f, 0f, 0f));
    }

    [Fact]
    public void 복제는_키까지_가져온다()
    {
        MotionEditor editor = StarEditor();

        EmojiAsset copy = editor.DuplicateEmoji("star", "star_b");

        Assert.Equal("emoji:star_b", copy.Timeline.Name);
        Assert.Equal(2, copy.Rigs.Count);
        Assert.Equal(3, copy.Timeline.FindChannel("body/EmojiRig_Image", ChannelKind.Scale)!.X!.Keys.Count);
        Assert.NotSame(editor.Document.FindEmoji("star")!.Timeline, copy.Timeline);
    }

    [Fact]
    public void 저작본에_실리고_그대로_읽힌다_이모지가_없으면_글자가_같다()
    {
        Assert.DoesNotContain("emojis", MotionDocumentJson.Write(Fixtures.NodBowDocument()));

        MotionEditor editor = StarEditor();
        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument back = MotionDocumentJson.Read(json);

        Assert.Equal(json, MotionDocumentJson.Write(back));
        Assert.Equal("emoji:star", back.FindEmoji("star")!.Timeline.Name);
        Assert.Equal("emoji", back.FindEmoji("star")!.Timeline.Rig);
    }

    [Fact]
    public void 맞는_이모지는_문제가_없다()
    {
        Assert.Empty(EmojiValidator.Validate(StarEditor().Document));
    }

    [Fact]
    public void 검증은_리그_그림_채널_구간_잇기를_본다()
    {
        MotionEditor editor = StarEditor();
        string timeline = EmojiAsset.TimelineNameOf("star");

        editor.AddEmoji("bare"); // 리그가 없다
        editor.SetEmojiRigImages("star", "spark", Array.Empty<string>()); // 그림이 없다
        editor.AddChannel(timeline, "body/EmojiRig_Place", ChannelKind.Position); // 키를 받지 않는 노드
        editor.AddChannel(timeline, Frame.OffsetNode, ChannelKind.Image); // 그림은 리그에만
        editor.AddChannel(timeline, Frame.ImageNodeOf("body"), ChannelKind.Euler);
        editor.SetKeys(timeline, Frame.ImageNodeOf("body"), ChannelKind.Euler, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(1f, 5f) }); // 회전은 z 하나
        editor.AddChannel(timeline, Frame.ImageNodeOf("body"), ChannelKind.Image);
        editor.SetKeys(timeline, Frame.ImageNodeOf("body"), ChannelKind.Image, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.5f, 2f) }); // 그림은 둘(0 · 1)
        editor.AddChannel(timeline, Frame.ImageNodeOf("ghost"), ChannelKind.Alpha); // 없는 리그
        editor.SetEmojiSpan("star", "leave", 18, 30); // 타임라인(0~24fr) 밖
        editor.AddEmoji("lone");
        editor.AddEmojiRig("lone", "body", new[] { "a.png" });
        editor.AddEmojiSpan("lone", "a", 0, 3);
        editor.SetEmojiLinks("lone", "a", null, null); // 머묾 없이 등장

        IReadOnlyList<EmojiProblem> problems = EmojiValidator.Validate(editor.Document);
        string all = string.Join("\n", problems);

        Assert.All(problems, p => Assert.True(p.Blocking));
        Assert.Contains("이모지 'bare': 그림이 없다", all);
        Assert.Contains("그림 'spark'에 그림 파일이 없다", all);
        Assert.Contains("body/EmojiRig_Place position: 키를 받는 노드가 아니다", all);
        Assert.Contains("그림 바꾸기는 그림에만", all);
        Assert.Contains("X 성분은 이모지에 없다", all);
        Assert.Contains("그림 키 2가 그림 'body'의 장(0~1번째)을 가리키지 않는다", all);
        Assert.Contains("그림 'ghost'이 이 이모지에 없다", all);
        Assert.Contains("구간 'leave'(18~30fr)이 타임라인(0~24fr) 밖", all);
        Assert.Contains("이모지 'lone': 등장 · 퇴장은 머묾을 이었을 때만", all);
    }

    [Fact]
    public void 덤프에_이모지_틀이_없으면_이모지가_있는_문서에_알린다()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Fixtures.BytesOf("rig-schemas.json"))!;
        System.Text.Json.Nodes.JsonArray rigs = json["rigs"]!.AsArray();
        rigs.Remove(rigs.First(r => (string?)r!["rigKind"] == "emoji"));
        RigSchemaFile noEmoji = RigSchemaFile.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Empty(EmojiValidator.Validate(Fixtures.NodBowDocument(), noEmoji));
        EmojiProblem problem = Assert.Single(EmojiValidator.Validate(StarEditor().Document, noEmoji));
        Assert.Contains("'emoji' 리그가 없다", problem.Message);
    }
}
