using Avalonia;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

/// <summary>수명의 띠(PLAN-ux.md U5) — 띠가 구간과 이어진 자리를 보이고, 끌어서 고치고, 없는 자리를 더한다.</summary>
public class EmojiLifeStripShellTests
{
    private static void Run(Action<MainWindow> body) => EmojiExampleWindow.Run(body);

    [Fact]
    public void 띠는_구간과_이어진_자리를_보이고_없는_자리만_더하게_한다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            Assert.Equal(
                new[] { new LifeBand("in", 0, 4, EmojiLifeRole.In), new LifeBand("hold", 5, 20, EmojiLifeRole.Hold), new LifeBand("out", 21, 26, EmojiLifeRole.Out) },
                window.LifeBands);
            Assert.StartsWith("머무는 이모지", window.LifeFormState.Note);
            Assert.Empty(window.LifeFormState.Addable);

            window.SelectEmoji("twinkle"); // 구간이 없다
            Assert.Empty(window.LifeBands);
            Assert.StartsWith("한 번 재생하고 사라진다", window.LifeFormState.Note);
            Assert.Equal(EmojiLife.Roles, window.LifeFormState.Addable);
        });
    }

    [Fact]
    public void 띠의_끝과_몸통을_끌면_구간이_프레임에_붙어_바뀌고_한_칸이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            MotionEditor editor = window.Session.Editor;
            EmojiAsset star = editor.Document.FindEmoji("star")!;
            EmojiLifeStripView strip = window.LifeStrip;
            double frame = (strip.RectOf(window.LifeBands[1]).Right - strip.RectOf(window.LifeBands[1]).X) / 15; // 머묾 5~20 = 15프레임 너비

            // 머묾의 오른쪽 끝을 두 프레임 왼쪽으로.
            long before = editor.Revision;
            Rect hold = strip.RectOf(window.LifeBands[1]);
            Assert.True(strip.DragForTest(new Point(hold.Right - 1, hold.Center.Y), new Point(hold.Right - 1 - frame * 2.2, hold.Center.Y)));
            Assert.Equal((5, 18), (star.FindSpan("hold")!.Start, star.FindSpan("hold")!.End));
            Assert.Equal(before + 1, editor.Revision);

            // 등장의 몸통을 오른쪽으로 — 길이를 지킨다.
            Rect enter = strip.RectOf(window.LifeBands[0]);
            Assert.True(strip.DragForTest(enter.Center, new Point(enter.Center.X + frame * 3, enter.Center.Y)));
            Assert.Equal((3, 7), (star.FindSpan("in")!.Start, star.FindSpan("in")!.End));
            Assert.Equal(before + 2, editor.Revision);

            // 퇴장을 타임라인 밖으로 끌어도 마지막 프레임에서 멈춘다. 제자리에 놓으면 기록하지 않는다.
            Rect leave = strip.RectOf(window.LifeBands[2]);
            Assert.True(strip.DragForTest(leave.Center, new Point(leave.Center.X + frame * 40, leave.Center.Y)));
            Assert.Equal((21, 26), (star.FindSpan("out")!.Start, star.FindSpan("out")!.End));
            Assert.Equal(before + 2, editor.Revision);

            // 빈 곳은 잡히지 않는다.
            Assert.False(strip.DragForTest(new Point(hold.Right - frame * 0.7, hold.Center.Y), new Point(10, hold.Center.Y))); // 머묾이 줄어 비운 19~20fr 사이

            editor.Undo();
            editor.Undo();
            Assert.Equal(new LifeBand("hold", 5, 20, EmojiLifeRole.Hold), window.LifeBands[1]);
        });
    }

    [Fact]
    public void 띠는_타임_슬라이더와_같은_눈금이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");

            // 타임 슬라이더의 띠(구간)와 수명의 띠가 같은 x에서 시작하고 끝난다 — 너비가 같고 같은 범위를 쓴다.
            Assert.Equal(window.TimeSliderBounds.Width, window.LifeStrip.Bounds.Width, 1);
            Rect hold = window.LifeStrip.RectOf(window.LifeBands[1]);
            Assert.Equal(window.TimeSliderXOf(EmojiAsset.SecondsOf(5)), hold.X, 1);
            Assert.Equal(window.TimeSliderXOf(EmojiAsset.SecondsOf(20)), hold.Right, 1);
        });
    }

    [Fact]
    public void 없는_자리를_더하고_띠의_메뉴로_풀고_지운다()
    {
        Run(window =>
        {
            window.SelectEmoji("twinkle");
            MotionEditor editor = window.Session.Editor;
            EmojiAsset twinkle = editor.Document.FindEmoji("twinkle")!;
            long before = editor.Revision;

            window.AddLifeSpanFromForm(EmojiLifeRole.Hold);
            Assert.Equal(before + 1, editor.Revision);
            Assert.True(twinkle.Stays);
            Assert.Equal(new[] { EmojiLifeRole.In, EmojiLifeRole.Out }, window.LifeFormState.Addable);
            Assert.Equal("머무는 이모지", window.EmojiRowNote("twinkle"));

            window.UnlinkLifeSpanFromForm("hold");
            Assert.False(twinkle.Stays);
            Assert.Equal(new LifeBand("hold", twinkle.Spans[0].Start, twinkle.Spans[0].End, null), Assert.Single(window.LifeBands)); // 구간은 남는다(점선)

            window.RemoveLifeSpanFromForm("hold");
            Assert.Empty(window.LifeBands);
            Assert.Equal(before + 3, editor.Revision);
        });
    }
}
