using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>이모지의 타임라인을 화면에 적는 말(PLAN-ux.md U3) — 노드 이름 대신 그림의 이름, 채널 이름 대신 자리 · 크기 · 투명도.</summary>
public class EmojiWordsTests
{
    [Fact]
    public void 대상은_그림의_이름이거나_이모지_전체다()
    {
        EmojiRigFrame frame = EmojiRigFrame.Builtin;

        Assert.Equal("이모지 전체", EmojiWords.Target(frame.TargetNode("")));
        Assert.Equal("body", EmojiWords.Target(frame.TargetNode("body")));
        Assert.Equal("left", EmojiWords.Target(frame.ImageNodeOf("left")));
    }

    [Fact]
    public void 종류와_성분의_말()
    {
        Assert.Equal("자리", EmojiWords.Kind(ChannelKind.Position));
        Assert.Equal("크기", EmojiWords.Kind(ChannelKind.Scale));
        Assert.Equal("회전", EmojiWords.Kind(ChannelKind.Euler));
        Assert.Equal("투명도", EmojiWords.Kind(ChannelKind.Alpha));
        Assert.Equal("그림", EmojiWords.Kind(ChannelKind.Image));

        // 자리와 크기는 축을 붙이고, 성분이 하나뿐인 것은 그대로다(alpha X가 아니다).
        Assert.Equal("크기 X", EmojiWords.Component(ChannelKind.Scale, Axis.X));
        Assert.Equal("자리 Y", EmojiWords.Component(ChannelKind.Position, Axis.Y));
        Assert.Equal("투명도", EmojiWords.Component(ChannelKind.Alpha, Axis.X));
        Assert.Equal("회전", EmojiWords.Component(ChannelKind.Euler, Axis.Z));
    }

    [Fact]
    public void 도프_시트의_줄은_이모지의_타임라인이면_그_말로_적는다()
    {
        var editor = new MotionEditor(new MotionDocument());
        EmojiRigFrame frame = EmojiRigFrame.Builtin;
        editor.AddEmoji("star");
        editor.AddEmojiRig("star", "body");
        string timeline = EmojiAsset.TimelineNameOf("star");
        editor.KeyComponents(timeline, frame.TargetNode("body"), ChannelKind.Scale, 0f, new[] { (Axis.X, 1f), (Axis.Y, 1f) }, TangentMode.Flat);
        editor.KeyComponents(timeline, frame.TargetNode(""), ChannelKind.Alpha, 0f, new[] { (Axis.X, 1f) }, TangentMode.Flat);

        List<DopeRow> rows = DopeSheetRows.Build(editor.Document.Find(timeline)!);
        string[] labels = rows.Select(r => r.Label).ToArray();

        Assert.Contains("body", labels);
        Assert.Contains("크기", labels);
        Assert.Contains("크기 X", labels);
        Assert.Contains("이모지 전체", labels);
        Assert.Contains("투명도", labels);
        Assert.DoesNotContain(labels, l => l.Contains("EmojiRig_Image") || l.Contains("Emoji_Offset") || l == "alpha" || l == "scale");

        // 줄의 노드와 종류는 그대로다 — 바꾼 것은 글자뿐이다.
        Assert.Contains(rows, r => r.Node == frame.TargetNode("body") && r.Kind == ChannelKind.Scale);
    }

    [Fact]
    public void 캐릭터_모션의_줄은_전과_같다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("nod", "character", 0.5f);
        editor.KeyComponents("nod", "CharacterPortrait_Track_Y", ChannelKind.Position, 0f, new[] { (Axis.Y, 1f) }, TangentMode.Flat);

        string[] labels = DopeSheetRows.Build(editor.Document.Find("nod")!).Select(r => r.Label).ToArray();

        Assert.Equal(new[] { "CharacterPortrait_Track_Y", "position", "y" }, labels);
    }
}
