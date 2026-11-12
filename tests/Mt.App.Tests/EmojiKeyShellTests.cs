using Avalonia;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Settings;

namespace Mt.App.Tests;

/// <summary>키 놓기의 단추와 그래프의 종류별 칸(PLAN-ux.md U6의 나머지).</summary>
public class EmojiKeyShellTests
{
    private static void Run(Action<MainWindow> body) => EmojiExampleWindow.Run(body);

    [Fact]
    public void 키_놓기의_단추는_고른_것_전부에_그_종류의_키를_한_칸에_놓는다()
    {
        Run(window =>
        {
            window.SelectEmoji("twinkle"); // 그림 셋
            MotionEditor editor = window.Session.Editor;
            EmojiAsset twinkle = editor.Document.FindEmoji("twinkle")!;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(7));

            // 고른 것이 없다 — 대상은 이모지 전체. 그림 바꾸기는 누를 수 없다(이모지 전체에는 넘길 장이 없다).
            Assert.Equal("이모지 전체", window.EmojiKeyTargetChoice);
            Assert.DoesNotContain(ChannelKind.Image, window.EmojiKeyKindsEnabled);
            long before = editor.Revision;
            window.KeyEmojiKindFromForm(ChannelKind.Alpha);
            Assert.Equal(before + 1, editor.Revision);
            Assert.Contains(twinkle.Timeline.FindChannel(frame.OffsetNode, ChannelKind.Alpha)!.X!.Keys, k => Math.Abs(k.T - EmojiAsset.SecondsOf(7)) < 1e-4);

            // 둘을 골랐다 — 둘 다에, 한 칸에.
            window.SelectEmojiRigsFromForm("left", "right");
            Assert.Equal("left · right", window.EmojiKeyTargetChoice);
            Assert.Contains(ChannelKind.Image, window.EmojiKeyKindsEnabled);
            window.KeyEmojiKindFromForm(ChannelKind.Euler);
            Assert.Equal(before + 2, editor.Revision);

            foreach (string rig in new[] { "left", "right" })
            {
                Assert.Contains(twinkle.Timeline.FindChannel(frame.ImageNodeOf(rig), ChannelKind.Euler)!.Z!.Keys, k => Math.Abs(k.T - EmojiAsset.SecondsOf(7)) < 1e-4);
            }

            Assert.Null(twinkle.Timeline.FindChannel(frame.ImageNodeOf("center"), ChannelKind.Euler));
        });
    }

    [Fact]
    public void 종류별_칸은_종류마다_제_눈금으로_나눠_그리고_끄면_한_눈금이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            window.SelectEmojiRigFromForm("body"); // 크기 X · Y와 투명도에 키가 있다
            GraphEditorView graph = window.Graph;
            Assert.True(graph.Stacked);

            IReadOnlyList<GraphEditorView.Lane> lanes = graph.Lanes();
            Assert.Equal(new[] { ChannelKind.Scale, ChannelKind.Alpha }, lanes.Select(l => l.Kind));
            Assert.True(lanes[0].Band.Bottom <= lanes[1].Band.Y + 0.5); // 위아래로 겹치지 않는다
            Assert.Equal(lanes[0].Band.Height, lanes[1].Band.Height, 1);

            // 곡선은 제 칸 안에 그려진다 — 키마다.
            MotionAsset timeline = window.Session.Editor.Document.FindEmoji("star")!.Timeline;
            string node = window.Session.EmojiFrame!.ImageNodeOf("body");

            foreach ((ChannelKind kind, int lane) in new[] { (ChannelKind.Scale, 0), (ChannelKind.Alpha, 1) })
            {
                var curve = new Mt.Authoring.Graph.CurveRef(node, kind, Axis.X);

                foreach (KeyAsset key in curve.Resolve(timeline)!.Keys)
                {
                    Point at = graph.ScreenOfForTest(curve, key.T, key.V);
                    Assert.InRange(at.Y, lanes[lane].Band.Y, lanes[lane].Band.Bottom);
                }
            }

            // 시간은 칸이 달라도 같은 x다.
            Assert.Equal(
                graph.ScreenOfForTest(new Mt.Authoring.Graph.CurveRef(node, ChannelKind.Scale, Axis.X), 0.5, 0).X,
                graph.ScreenOfForTest(new Mt.Authoring.Graph.CurveRef(node, ChannelKind.Alpha, Axis.X), 0.5, 0).X, 3);

            // 끄면 한 눈금 — 칸이 없다. 캐릭터 모션은 켜 둬도 나누지 않는다.
            window.SetStackGraphFromForm(false);
            Assert.False(graph.Stacked);
            Assert.Empty(graph.Lanes());
            window.SetStackGraphFromForm(true);
            Assert.True(graph.Stacked);
            window.SwitchWorkModeFromForm(WorkMode.Motion);
            Assert.False(graph.Stacked);
        });
    }
}
