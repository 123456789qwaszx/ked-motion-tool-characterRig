using Avalonia;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

/// <summary>
/// 뷰포트에서 이모지를 직접 만진다(PLAN-ux.md U4) — 매니퓰레이터(W 이동 · E 회전 · R 크기), Auto Key가 기본값과 키를 가른다,
/// 그림의 빈 곳은 눌러도 맞지 않는다, 겹친 그림은 다시 누르면 그 밑의 것으로 넘어간다, 도구 띠.
/// </summary>
public class EmojiManipulateShellTests
{
    private static void Run(Action<MainWindow> body)
    {
        HeadlessUi.Run(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");
            var window = new MainWindow(path) { Width = 1600, Height = 960 };
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                body(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static EmojiRigAsset Body(MainWindow window) => window.Session.Editor.Document.FindEmoji("emoji")!.Rigs[0];

    private static (Point Center, Point XEnd, Point YEnd) Gizmo(MainWindow window) => window.Viewport.EmojiGizmoForTest()!.Value;

    [Fact]
    public void 자동_키가_꺼져_있으면_끈_것이_그림의_기본값이_된다_이동_회전_크기()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");
            Assert.False(window.Viewport.AutoKey);

            // 이동 — x 화살표를 끌면 가로만.
            window.PickToolFromStrip(ManipulatorMode.Move);
            long before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(Gizmo(window).XEnd, Gizmo(window).XEnd + new Vector(50, 30)));
            Assert.Equal(before + 1, editor.Revision);
            Assert.True(Body(window).PlaceX > 0f, $"자리 X {Body(window).PlaceX}");
            Assert.Equal(0f, Body(window).PlaceY, 3);

            // 회전 — 고리의 오른쪽에서 위로 4분의 1바퀴(반시계가 양수).
            window.PickToolFromStrip(ManipulatorMode.Rotate);
            Point center = Gizmo(window).Center;
            double arm = Gizmo(window).XEnd.X - center.X;
            before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(new Point(center.X + arm, center.Y), new Point(center.X, center.Y - arm)));
            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal(90f, Body(window).Rotation, 0);

            // 크기 — 그림의 기본 크기는 값 하나다. 어느 손잡이를 끌어도 가로 세로가 같이 간다.
            window.PickToolFromStrip(ManipulatorMode.Scale);
            (Point c, Point xEnd, _) = Gizmo(window);
            before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(xEnd, new Point(xEnd.X + (xEnd.X - c.X), xEnd.Y + (xEnd.Y - c.Y))));
            Assert.Equal(before + 1, editor.Revision);
            Assert.True(Body(window).Scale > 1.5f, $"크기 {Body(window).Scale}");

            // 키는 하나도 놓이지 않았다.
            Assert.Empty(editor.Document.Find(EmojiAsset.TimelineNameOf("emoji"))!.Channels);
        });
    }

    [Fact]
    public void 자동_키가_켜져_있으면_끈_것이_지금_프레임의_키가_된다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");
            window.SetAutoKeyFromStrip(true);
            Assert.True(window.Viewport.AutoKey);
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(6));

            window.PickToolFromStrip(ManipulatorMode.Move);
            long before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(Gizmo(window).Center, Gizmo(window).Center + new Vector(40, 0)));
            Assert.Equal(before + 1, editor.Revision);

            // 기본값은 그대로이고, 그림 노드의 자리에 6프레임의 키가 놓였다.
            Assert.Equal(0f, Body(window).PlaceX, 3);
            MotionAsset timeline = editor.Document.Find(EmojiAsset.TimelineNameOf("emoji"))!;
            ChannelAsset position = timeline.FindChannel(frame.TargetNode("body"), ChannelKind.Position)!;
            KeyAsset key = Assert.Single(position.Get(Axis.X)!.Keys);
            Assert.Equal(EmojiAsset.SecondsOf(6), key.T, 4);
            Assert.True(key.V > 0f, $"키 X {key.V}");

            // 크기 — 키는 축마다 따로다(x 손잡이는 가로만).
            window.PickToolFromStrip(ManipulatorMode.Scale);
            (Point c, Point xEnd, _) = Gizmo(window);
            Assert.True(window.Viewport.DragEmojiForTest(xEnd, new Point(xEnd.X + (xEnd.X - c.X), xEnd.Y + (xEnd.Y - c.Y))));
            ChannelAsset scale = timeline.FindChannel(frame.TargetNode("body"), ChannelKind.Scale)!;
            Assert.True(scale.Get(Axis.X)!.Keys[0].V > 1.5f);
            Assert.Equal(1f, scale.Get(Axis.Y)!.Keys[0].V, 3);
            Assert.Equal(1f, Body(window).Scale, 3);
        });
    }

    [Fact]
    public void 그림을_고르지_않았으면_대상은_이모지_전체다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            window.AddEmojiFromForm();
            Assert.Null(window.Viewport.SelectedEmojiRig);

            // 기본값 — 팔을 끌면 머리에서의 자리가 바뀐다.
            window.PickToolFromStrip(ManipulatorMode.Move);
            Assert.True(window.Viewport.DragEmojiForTest(Gizmo(window).XEnd, Gizmo(window).XEnd + new Vector(40, 0)));
            Assert.True(editor.Document.FindEmoji("emoji")!.OffsetX > 0f);
            Assert.Null(window.Viewport.SelectedEmojiRig);

            // 이모지 전체에는 기본 회전 · 크기가 없다 — 키로만 잡는다.
            window.PickToolFromStrip(ManipulatorMode.Rotate);
            Assert.Null(window.Viewport.EmojiGizmoForTest());
            Assert.Contains("키로만", window.Viewport.EmojiEditNoteForTest());

            window.SetAutoKeyFromStrip(true);
            Point center = Gizmo(window).Center;
            double arm = Gizmo(window).XEnd.X - center.X;
            Assert.True(window.Viewport.DragEmojiForTest(new Point(center.X + arm, center.Y), new Point(center.X, center.Y - arm)));
            ChannelAsset euler = editor.Document.Find(EmojiAsset.TimelineNameOf("emoji"))!.FindChannel(frame.TargetNode(""), ChannelKind.Euler)!;
            Assert.Equal(90f, euler.Get(Axis.Z)!.Keys[0].V, 0);
        });
    }

    [Fact]
    public void 이동_도구는_그림을_눌러_고르면서_바로_끌고_선택_도구는_고르기만_한다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            Vec2 center = window.Viewport.EmojiRigCenter("body")!.Value;

            window.PickToolFromStrip(ManipulatorMode.Select);
            long before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiRigForTest(center, center + new Vec2(40f, 0f)));
            Assert.Equal(before, editor.Revision);
            Assert.Equal("body", window.Viewport.SelectedEmojiRig);
            Assert.Equal("body", window.EmojiLists.Rigs.Single());
            Assert.True(window.EmojiInspectorState.RigFields); // 뷰포트에서 고르면 왼쪽 목록과 오른쪽이 따라온다

            window.SelectEmojiRigFromForm(null);
            window.PickToolFromStrip(ManipulatorMode.Move);
            Assert.True(window.Viewport.DragEmojiRigForTest(center, center + new Vec2(40f, 0f)));
            Assert.Equal(before + 1, editor.Revision);
            Assert.True(Body(window).PlaceX > 0f);
        });
    }

    [Fact]
    public void S는_지금_대상의_지금_값을_키한다_한_칸()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(3));

            window.PickToolFromStrip(ManipulatorMode.Rotate);
            long before = editor.Revision;
            window.Viewport.SetKey();
            Assert.Equal(before + 1, editor.Revision);
            MotionAsset timeline = editor.Document.Find(EmojiAsset.TimelineNameOf("emoji"))!;
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("body"), ChannelKind.Euler));
            Assert.Null(timeline.FindChannel(frame.TargetNode("body"), ChannelKind.Position));

            // 선택 도구면 자리 · 회전 · 크기를 한꺼번에(그래도 한 칸).
            window.PickToolFromStrip(ManipulatorMode.Select);
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(9));
            before = editor.Revision;
            window.Viewport.SetKey();
            Assert.Equal(before + 1, editor.Revision);
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("body"), ChannelKind.Position));
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("body"), ChannelKind.Scale));
        });
    }

    [Fact]
    public void 그림의_빈_곳은_맞지_않고_겹친_그림은_다시_누르면_밑의_것으로_넘어간다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();                       // 그림 body(아래)
            editor.AddEmojiRig("emoji", "top");               // 그림 top(위) — 같은 자리
            editor.SetEmojiRigImages("emoji", "body", new[] { "a.png" });
            editor.SetEmojiRigImages("emoji", "top", new[] { "b.png" });
            window.PickToolFromStrip(ManipulatorMode.Select);

            // body는 꽉 찬 그림, top은 왼쪽 절반만 그려진 그림(오른쪽 절반은 비었다).
            const int size = 100;
            byte[] full = Enumerable.Repeat((byte)255, size * size).ToArray();
            byte[] leftHalf = Enumerable.Range(0, size * size).Select(i => i % size < size / 2 ? (byte)255 : (byte)0).ToArray();
            window.Viewport.SetEmojiAlphaForTest("a.png", size, size, full);
            window.Viewport.SetEmojiAlphaForTest("b.png", size, size, leftHalf);

            Rect box = window.Viewport.EmojiControlBox()!.Value;
            var left = new Point(box.X + box.Width * 0.25, box.Center.Y);
            var right = new Point(box.X + box.Width * 0.75, box.Center.Y);
            var outside = new Point(box.Right + 40, box.Center.Y);

            // 오른쪽 — top의 사각형 안이지만 그림이 비었다. 밑의 body가 골라진다.
            Assert.True(window.Viewport.DragEmojiForTest(right, right));
            Assert.Equal("body", window.Viewport.SelectedEmojiRig);

            // 왼쪽 — 둘 다 그려져 있다. 이미 고른 body를 지킨다(겹친 그림을 끌 수 있게), 끌지 않고 놓았으니 다음 것(top)으로 넘어간다.
            Assert.True(window.Viewport.DragEmojiForTest(left, left));
            Assert.Equal("top", window.Viewport.SelectedEmojiRig);
            Assert.True(window.Viewport.DragEmojiForTest(left, left));
            Assert.Equal("body", window.Viewport.SelectedEmojiRig);

            // 아무것도 고르지 않은 채 왼쪽을 누르면 위에 그려진 것부터.
            window.SelectEmojiRigFromForm(null);
            Assert.True(window.Viewport.DragEmojiForTest(left, left));
            Assert.Equal("top", window.Viewport.SelectedEmojiRig);

            // 빈 곳 — 그림 선택이 풀린다(대상이 이모지 전체가 된다). 왼쪽 목록과 오른쪽도 따라온다.
            Assert.False(window.Viewport.DragEmojiForTest(outside, outside));
            Assert.Null(window.Viewport.SelectedEmojiRig);
            Assert.True(window.EmojiInspectorState.EmojiFields);
        });
    }

    [Fact]
    public void 도구_띠는_지금_도구와_자동_키를_보이고_단축키를_따라온다()
    {
        Run(window =>
        {
            Assert.Equal(ManipulatorMode.Move, window.ToolStripChecked);

            window.PickToolFromStrip(ManipulatorMode.Rotate);
            Assert.Equal(ManipulatorMode.Rotate, window.Viewport.Manipulator);
            Assert.Equal(ManipulatorMode.Rotate, window.ToolStripChecked);

            // 뷰포트 쪽에서 바뀌어도(단축키) 띠가 따라온다.
            window.Viewport.Manipulator = ManipulatorMode.Scale;
            Assert.Equal(ManipulatorMode.Scale, window.ToolStripChecked);

            // Auto Key는 아래 줄의 것과 같은 상태다.
            Assert.False(window.ToolStripAutoKey);
            window.SetAutoKeyFromStrip(true);
            Assert.True(window.AutoKeyChecked);
            Assert.True(window.Viewport.AutoKey);
            window.AutoKeyChecked = false;
            Assert.False(window.ToolStripAutoKey);
        });
    }
}
