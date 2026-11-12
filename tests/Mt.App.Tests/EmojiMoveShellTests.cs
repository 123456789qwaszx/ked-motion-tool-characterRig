using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

/// <summary>움직임 가져오기(PLAN-ux.md U8).</summary>
public class EmojiMoveShellTests
{
    private static void Run(Action<MainWindow> body) => EmojiExampleWindow.Run(body);

    [Fact]
    public void 움직임을_가져오면_그_자리의_구간에_키가_놓이고_구간이_없으면_함께_놓인다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            string name = window.AddEmojiFromForm(); // 그림 body 하나, 구간 없음, 0~24fr
            EmojiAsset pop = editor.Document.FindEmoji(name)!;
            Assert.Equal(24, pop.LastFrame);
            EmojiRigFrame frame = window.Session.EmojiFrame!;

            // 그림을 고르지 않았다 — 대상은 이모지 전체. 머묾이 없으니 구간을 새로 놓는다(그것까지 한 칸).
            Assert.Equal(("대상: 이모지 전체", "머묾 8~16fr(구간을 새로 놓는다)"), window.MoveMenuNotes(EmojiMove.Pulse));
            long before = editor.Revision;
            window.ImportMoveFromForm(EmojiMove.Pulse);
            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal("hold", pop.Hold);
            Assert.Equal(new LifeBand("hold", 8, 16, EmojiLifeRole.Hold), Assert.Single(window.LifeBands));
            Assert.Equal(3, pop.Timeline.FindChannel(frame.OffsetNode, ChannelKind.Scale)!.X!.Keys.Count);
            Assert.Contains("이모지 전체 · 크기", window.ChannelRowTexts);

            // 그림을 고르면 그 그림에. 등장의 움직임은 등장의 자리에(없으니 놓는다 — 머묾 앞의 빈 곳).
            window.SelectEmojiRigFromForm("body");
            Assert.Equal(("대상: body", "등장 0~7fr(구간을 새로 놓는다)"), window.MoveMenuNotes(EmojiMove.PopIn));
            window.ImportMoveFromForm(EmojiMove.PopIn);
            Assert.Equal(before + 2, editor.Revision);
            Assert.Equal((0, 7), (pop.FindSpan(pop.In)!.Start, pop.FindSpan(pop.In)!.End));
            Assert.Equal(new[] { 0f, EmojiAsset.SecondsOf(4), EmojiAsset.SecondsOf(7) }, pop.Timeline.FindChannel(frame.ImageNodeOf("body"), ChannelKind.Scale)!.X!.Keys.Select(k => k.T));

            // 이미 있는 자리면 그 구간에 넣는다(구간은 그대로).
            Assert.Equal("등장 0~7fr", window.MoveMenuNotes(EmojiMove.FadeIn).Where);
            window.ImportMoveFromForm(EmojiMove.FadeIn);
            Assert.Equal(2, window.LifeBands.Count);

            // 띠에서 구간을 골랐으면 거기에 넣는다 — 자리가 달라도.
            window.LifeStrip.Picked = "hold";
            Assert.Equal("머묾 8~16fr(띠에서 고른 구간)", window.MoveMenuNotes(EmojiMove.FloatOut).Where);

            editor.Undo();
            editor.Undo();
            editor.Undo();
            Assert.Empty(editor.Document.FindEmoji(name)!.Spans);
            Assert.Empty(editor.Document.FindEmoji(name)!.Timeline.Channels);
        });
    }
}
