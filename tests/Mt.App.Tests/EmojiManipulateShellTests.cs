using Avalonia;
using Ked.Presentation.Core;
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

            // 키를 놓은 뒤에도 그 프레임을 보고 있다(처음 프레임으로 돌아가지 않는다) — 뷰포트도 그래프도.
            Assert.Equal(EmojiAsset.SecondsOf(6), window.Viewport.Clock.Time, 4);
            Assert.Equal(EmojiAsset.SecondsOf(6), window.Graph.CurrentTime, 4);

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
            Assert.True(window.Viewport.DragEmojiForTest(outside, outside));
            Assert.Null(window.Viewport.SelectedEmojiRig);
            Assert.True(window.EmojiInspectorState.EmojiFields);
        });
    }

    // ── 여럿을 고른다 ────────────────────────────────────────────

    /// <summary>그림 셋이 옆으로 늘어선 이모지 — a(왼쪽) · b(가운데) · c(오른쪽).</summary>
    private static void ThreeInARow(MainWindow window)
    {
        MotionEditor editor = window.Session.Editor;
        window.AddEmojiFromForm();
        editor.RenameEmojiRig("emoji", "body", "a");
        editor.AddEmojiRig("emoji", "b");
        editor.AddEmojiRig("emoji", "c");
        editor.SetEmojiRigPlace("emoji", "a", -150f, 0f, 1f, 0f);
        editor.SetEmojiRigPlace("emoji", "c", 150f, 0f, 1f, 0f);
        window.Viewport.FrameEmoji();
    }

    private static Point CenterOf(MainWindow window, string rig) => window.Viewport.ToScreen(window.Viewport.EmojiRigCenter(rig)!.Value);

    private static EmojiRigAsset Rig(MainWindow window, string rig) => window.Session.Editor.Document.FindEmoji("emoji")!.FindRig(rig)!;

    [Fact]
    public void Shift로_더하고_빼고_상자로_고르고_왼쪽_목록이_따라온다()
    {
        Run(window =>
        {
            ThreeInARow(window);
            window.PickToolFromStrip(ManipulatorMode.Select);

            window.Viewport.DragEmojiForTest(CenterOf(window, "a"), CenterOf(window, "a"));
            window.Viewport.DragEmojiForTest(CenterOf(window, "c"), CenterOf(window, "c"), additive: true);
            Assert.Equal(new[] { "a", "c" }, window.Viewport.SelectedEmojiRigs);
            Assert.Equal(new[] { "a", "c" }, window.SelectedEmojiRigsInList);

            // 여럿을 골랐으면 오른쪽은 값의 칸을 내지 않는다.
            var state = window.EmojiInspectorState;
            Assert.False(state.EmojiFields);
            Assert.False(state.RigFields);
            Assert.True(state.Back);
            Assert.StartsWith("그림 2개", state.Title);

            // Shift로 다시 누르면 빠진다.
            window.Viewport.DragEmojiForTest(CenterOf(window, "a"), CenterOf(window, "a"), additive: true);
            Assert.Equal(new[] { "c" }, window.Viewport.SelectedEmojiRigs);
            Assert.True(window.EmojiInspectorState.RigFields);

            // 빈 데서 상자를 끌면 닿은 그림들이 골라진다 — a와 b를 감싼다.
            Rect box = window.Viewport.EmojiControlBox()!.Value;
            Point from = new(box.X - 10, box.Y - 10);
            Point to = new(CenterOf(window, "b").X + 5, box.Bottom + 10);
            Assert.True(window.Viewport.DragEmojiForTest(from, to));
            Assert.Equal(new[] { "a", "b" }, window.Viewport.SelectedEmojiRigs.OrderBy(r => r));
            Assert.Equal(new[] { "a", "b" }, window.SelectedEmojiRigsInList);

            // 왼쪽 목록에서 여럿을 골라도 뷰포트가 따라온다.
            window.SelectEmojiRigsFromForm("b", "c");
            Assert.Equal(new[] { "b", "c" }, window.Viewport.SelectedEmojiRigs.OrderBy(r => r));
        });
    }

    [Fact]
    public void 여럿을_함께_끌면_함께_움직이고_한_칸이다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            ThreeInARow(window);
            window.SelectEmojiRigsFromForm("a", "c");
            window.PickToolFromStrip(ManipulatorMode.Move);

            // 고른 그림(c)을 눌러 끈다 — a도 같이 간다. b는 그대로다.
            long before = editor.Revision;
            Point c = CenterOf(window, "c");
            Assert.True(window.Viewport.DragEmojiForTest(c, c + new Vector(60, 0)));
            Assert.Equal(before + 1, editor.Revision);
            float moved = Rig(window, "c").PlaceX - 150f;
            Assert.True(moved > 0f, $"c가 움직인 거리 {moved}");
            Assert.Equal(-150f + moved, Rig(window, "a").PlaceX, 0);
            Assert.Equal(0f, Rig(window, "b").PlaceX, 3);
            Assert.Equal(new[] { "a", "c" }, window.Viewport.SelectedEmojiRigs.OrderBy(r => r)); // 끈 뒤에도 그대로 골라져 있다

            // 되돌리기 한 번에 둘 다 돌아온다.
            editor.Undo();
            Assert.Equal(150f, Rig(window, "c").PlaceX, 3);
            Assert.Equal(-150f, Rig(window, "a").PlaceX, 3);

            // 회전은 무리째 — 매니퓰레이터가 둘의 가운데 점에 서고, 그 점을 축으로 각도 자리도 함께 돈다.
            window.PickToolFromStrip(ManipulatorMode.Rotate);
            Point center = Gizmo(window).Center;
            Point middle = new((CenterOf(window, "a").X + CenterOf(window, "c").X) / 2, (CenterOf(window, "a").Y + CenterOf(window, "c").Y) / 2);
            Assert.Equal(middle.X, center.X, 1);
            Assert.Equal(middle.Y, center.Y, 1);
            double arm = Gizmo(window).XEnd.X - center.X;
            before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(new Point(center.X + arm, center.Y), new Point(center.X, center.Y - arm))); // 오른쪽에서 위로 = 반시계 90°
            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal(90f, Rig(window, "a").Rotation, 0);
            Assert.Equal(90f, Rig(window, "c").Rotation, 0);
            Assert.Equal(0f, Rig(window, "b").Rotation, 3);

            // 오른쪽에 있던 c는 위로, 왼쪽에 있던 a는 아래로 갔다 — 가운데 점은 그대로다.
            Assert.Equal(0f, Rig(window, "c").PlaceX, 0);
            Assert.Equal(150f, Rig(window, "c").PlaceY, 0);
            Assert.Equal(0f, Rig(window, "a").PlaceX, 0);
            Assert.Equal(-150f, Rig(window, "a").PlaceY, 0);
            Assert.Equal((0f, 0f), (Rig(window, "b").PlaceX, Rig(window, "b").PlaceY));
            Point after = Gizmo(window).Center;
            Assert.Equal(center.X, after.X, 1);
            Assert.Equal(center.Y, after.Y, 1);

            // 하나만 고르면 전처럼 제 가운데에서 돈다 — 자리는 그대로.
            editor.Undo();
            window.SelectEmojiRigFromForm("c");
            Point own = Gizmo(window).Center;
            Assert.True(window.Viewport.DragEmojiForTest(new Point(own.X + arm, own.Y), new Point(own.X, own.Y - arm)));
            Assert.Equal(90f, Rig(window, "c").Rotation, 0);
            Assert.Equal(150f, Rig(window, "c").PlaceX, 3);
        });
    }

    [Fact]
    public void 여럿의_크기는_가운데_점에서_멀어지며_커지고_자동_키면_자리의_키도_놓인다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            ThreeInARow(window);
            window.SelectEmojiRigsFromForm("a", "c");
            window.PickToolFromStrip(ManipulatorMode.Scale);

            // 가운데 손잡이를 팔 하나만큼 밖으로 = ×2. 그림도 두 배, 가운데 점까지의 거리도 두 배.
            Point center = Gizmo(window).Center;
            double arm = Gizmo(window).XEnd.X - center.X;
            long before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(center, center + new Vector(arm, 0)));
            Assert.Equal(before + 1, editor.Revision);
            float grew = Rig(window, "c").Scale;
            Assert.True(grew > 1.5f, $"배율 {grew}");
            Assert.Equal(150f * grew, Rig(window, "c").PlaceX, 0);
            Assert.Equal(-150f * grew, Rig(window, "a").PlaceX, 0);
            Assert.Equal(1f, Rig(window, "b").Scale, 3);
            editor.Undo();

            // Auto Key — 무리째 돌리면 회전과 함께 자리의 키도 놓인다(한 칸).
            window.SetAutoKeyFromStrip(true);
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(6));
            window.PickToolFromStrip(ManipulatorMode.Rotate);
            center = Gizmo(window).Center;
            before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiForTest(new Point(center.X + arm, center.Y), new Point(center.X, center.Y - arm)));
            Assert.Equal(before + 1, editor.Revision);
            MotionAsset timeline = editor.Document.Find(EmojiAsset.TimelineNameOf("emoji"))!;

            foreach (string rig in new[] { "a", "c" })
            {
                Assert.Equal(90f, timeline.FindChannel(frame.TargetNode(rig), ChannelKind.Euler)!.Z!.Keys.Single().V, 0);
                Assert.NotNull(timeline.FindChannel(frame.TargetNode(rig), ChannelKind.Position));
            }

            // 키의 자리는 기본 자리에 얹히는 값이다 — c는 (150, 0)에서 (0, 150)으로 가야 하니 (−150, +150).
            ChannelAsset moved = timeline.FindChannel(frame.TargetNode("c"), ChannelKind.Position)!;
            Assert.Equal(-150f, moved.X!.Keys.Single().V, 0);
            Assert.Equal(150f, moved.Y!.Keys.Single().V, 0);
            Assert.Equal(150f, Rig(window, "c").PlaceX, 3); // 기본 자리는 그대로
        });
    }

    [Fact]
    public void 여럿을_고른_채_자동_키와_S는_고른_것_전부에_키를_놓는다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            ThreeInARow(window);
            window.SelectEmojiRigsFromForm("a", "c");
            window.SetAutoKeyFromStrip(true);
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(4));
            window.PickToolFromStrip(ManipulatorMode.Move);

            long before = editor.Revision;
            Point c = CenterOf(window, "c");
            Assert.True(window.Viewport.DragEmojiForTest(c, c + new Vector(0, -40)));
            Assert.Equal(before + 1, editor.Revision);

            MotionAsset timeline = editor.Document.Find(EmojiAsset.TimelineNameOf("emoji"))!;
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("a"), ChannelKind.Position));
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("c"), ChannelKind.Position));
            Assert.Null(timeline.FindChannel(frame.TargetNode("b"), ChannelKind.Position));
            Assert.Equal(EmojiAsset.SecondsOf(4), window.Viewport.Clock.Time, 4);

            // S — 회전 도구면 고른 둘의 회전에 키(한 칸).
            window.PickToolFromStrip(ManipulatorMode.Rotate);
            before = editor.Revision;
            window.Viewport.SetKey();
            Assert.Equal(before + 1, editor.Revision);
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("a"), ChannelKind.Euler));
            Assert.NotNull(timeline.FindChannel(frame.TargetNode("c"), ChannelKind.Euler));
        });
    }

    [Fact]
    public void 여럿을_고른_채_하나를_끌지_않고_누르면_그것만_남는다()
    {
        Run(window =>
        {
            ThreeInARow(window);
            window.SelectEmojiRigsFromForm("a", "b", "c");
            window.PickToolFromStrip(ManipulatorMode.Move);
            long before = window.Session.Editor.Revision;

            Point b = CenterOf(window, "b");
            Assert.True(window.Viewport.DragEmojiForTest(b, b));

            Assert.Equal(new[] { "b" }, window.Viewport.SelectedEmojiRigs);
            Assert.Equal(before, window.Session.Editor.Revision);
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
