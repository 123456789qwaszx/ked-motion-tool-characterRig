using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>움직임 가져오기(PLAN-ux.md U8) — 구간에 키를 복사해 넣는다. 한 칸이고, 넣은 것은 그냥 키다.</summary>
public class EmojiMovesTests
{
    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    private static MotionEditor Blank()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddEmoji("pop", 1f); // 0~24fr
        editor.AddEmojiRig("pop", "body", new[] { "a.png" });
        editor.AddEmojiRig("pop", "spark", new[] { "b.png" });
        return editor;
    }

    private static float[] FramesOf(EmojiAsset emoji, string node, ChannelKind kind, Axis axis)
        => emoji.Timeline.FindChannel(node, kind)!.Get(axis)!.Keys.Select(k => k.T * EmojiAsset.Fps).ToArray();

    [Fact]
    public void 여섯_움직임은_등장_머묾_퇴장에_둘씩이다()
    {
        Assert.Equal(6, EmojiMoves.All.Length);

        foreach (EmojiLifeRole role in EmojiLife.Roles)
        {
            Assert.Equal(2, EmojiMoves.All.Count(m => EmojiMoves.Role(m) == role));
        }

        // 머묾의 움직임은 되풀이에 맞게 처음과 끝이 같다.
        foreach (EmojiMove move in EmojiMoves.All.Where(m => EmojiMoves.Role(m) == EmojiLifeRole.Hold))
        {
            Assert.All(EmojiMoves.Curves(move), c => Assert.Equal(c.Keys[0].Value, c.Keys[^1].Value));
        }
    }

    [Fact]
    public void 구간의_프레임에_키를_깔고_한_칸이다()
    {
        MotionEditor editor = Blank();
        long before = editor.Revision;

        int laid = editor.LayEmojiMove("pop", new[] { "body" }, EmojiMove.PopIn, 0, 10);
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;
        string node = Frame.ImageNodeOf("body");

        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(3 + 3 + 2, laid); // 크기 X · Y 셋씩, 투명도 둘
        Assert.Equal(new[] { 0f, 6f, 10f }, FramesOf(pop, node, ChannelKind.Scale, Axis.X));
        Assert.Equal(new[] { 0f, 6f, 10f }, FramesOf(pop, node, ChannelKind.Scale, Axis.Y));
        Assert.Equal(new[] { 0f, 4f }, FramesOf(pop, node, ChannelKind.Alpha, Axis.X));
        Assert.Null(pop.Timeline.FindChannel(Frame.ImageNodeOf("spark"), ChannelKind.Scale)); // 다른 그림은 그대로
        Assert.Empty(EmojiValidator.Validate(editor.Document));

        // 미리보기(코어) — 처음에는 안 보이고 끝에는 제 크기다.
        EmojiClip clip = EmojiPreview.Timeline(pop, Frame)!;
        Assert.Equal(0f, EmojiClipMath.SampleNode(clip, "body", 0f).Scale.X, 3);
        Assert.Equal(1.2f, EmojiClipMath.SampleNode(clip, "body", 6f).Scale.X, 3);
        Assert.Equal(1f, EmojiClipMath.SampleNode(clip, "body", 10f).Scale.X, 3);
        Assert.Equal(1f, EmojiClipMath.SampleNode(clip, "body", 10f).Alpha, 3);

        editor.Undo();
        Assert.Empty(editor.Document.FindEmoji("pop")!.Timeline.Channels);
    }

    [Fact]
    public void 범위_안의_같은_성분만_바뀌고_밖의_키와_다른_성분은_남는다()
    {
        MotionEditor editor = Blank();
        string timeline = EmojiAsset.TimelineNameOf("pop");
        string node = Frame.ImageNodeOf("body");
        editor.AddChannel(timeline, node, ChannelKind.Scale);
        editor.SetKeys(timeline, node, ChannelKind.Scale, Axis.X, new[] { Fixtures.Key(0f, 1f), Fixtures.Key(EmojiAsset.SecondsOf(14), 2f), Fixtures.Key(1f, 1f) });
        editor.AddChannel(timeline, node, ChannelKind.Position);
        editor.SetKeys(timeline, node, ChannelKind.Position, Axis.X, new[] { Fixtures.Key(EmojiAsset.SecondsOf(14), 5f) });

        editor.LayEmojiMove("pop", new[] { "body" }, EmojiMove.Pulse, 12, 20);
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;

        Assert.Equal(new[] { 0f, 12f, 16f, 20f, 24f }, FramesOf(pop, node, ChannelKind.Scale, Axis.X)); // 14fr의 키는 범위 안이라 바뀌었다
        Assert.Equal(new[] { 14f }, FramesOf(pop, node, ChannelKind.Position, Axis.X));                   // 움직임이 쓰지 않는 성분
    }

    [Fact]
    public void 대상이_여럿이면_함께_깔고_빈_이름은_이모지_전체다()
    {
        MotionEditor editor = Blank();
        long before = editor.Revision;

        editor.LayEmojiMove("pop", new[] { "", "body", "spark" }, EmojiMove.FloatOut, 18, 24);
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;

        Assert.Equal(before + 1, editor.Revision);

        foreach (string node in new[] { Frame.OffsetNode, Frame.ImageNodeOf("body"), Frame.ImageNodeOf("spark") })
        {
            Assert.Equal(new[] { 18f, 24f }, FramesOf(pop, node, ChannelKind.Position, Axis.Y));
            Assert.Equal(new[] { 18f, 24f }, FramesOf(pop, node, ChannelKind.Alpha, Axis.X));
        }

        Assert.Empty(EmojiValidator.Validate(editor.Document));
    }

    [Fact]
    public void 짧은_구간에서는_같은_프레임의_키가_하나로_줄고_끝의_모습이_남는다()
    {
        MotionEditor editor = Blank();
        editor.LayEmojiMove("pop", new[] { "body" }, EmojiMove.PopIn, 5, 5);
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;
        ComponentCurve scale = pop.Timeline.FindChannel(Frame.ImageNodeOf("body"), ChannelKind.Scale)!.X!;

        Assert.Equal(1f, Assert.Single(scale.Keys).V);
    }

    [Fact]
    public void 없는_그림과_타임라인_밖은_막는다()
    {
        MotionEditor editor = Blank();
        long before = editor.Revision;

        Assert.ThrowsAny<Exception>(() => editor.LayEmojiMove("pop", new[] { "ghost" }, EmojiMove.Shake, 0, 5));
        Assert.Throws<InvalidOperationException>(() => editor.LayEmojiMove("pop", new[] { "body" }, EmojiMove.Shake, 20, 30));
        Assert.Throws<InvalidOperationException>(() => editor.LayEmojiMove("pop", Array.Empty<string>(), EmojiMove.Shake, 0, 5));
        Assert.Equal(before, editor.Revision);
    }
}
