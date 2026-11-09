using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App.Tests;

/// <summary>연출 클립 K3(<c>PLAN-clip.md</c>) — 셸에서 클립을 고르면 뷰포트·시계가 클립의 것이 된다.</summary>
public class ClipShellTests
{
    private const string MoveY = "CharacterPortrait_Track_Move_Y";
    private const string Sway = "CharacterPortrait_SwayPivot";

    private static MainWindow NewWindow()
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new(path);
    }

    /// <summary>s1·s2, nod(0.6초 restore, MoveY −18) · bow(0.4초 commit absolute, Sway z 8).</summary>
    private static void Prepare(MainWindow window)
    {
        window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
        MotionEditor editor = window.Session.Editor;
        editor.AddSceneObject(null, "s2", "character");

        editor.AddMotion("nod", "character", 0.6f);
        editor.AddChannel("nod", MoveY, MotionChannelKind.Position);
        editor.SetKeys("nod", MoveY, MotionChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });

        editor.AddMotion("bow", "character", 0.4f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("bow", Sway, MotionChannelKind.Euler);
        editor.SetKeys("bow", Sway, MotionChannelKind.Euler, Axis.Z, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.4f, 8f) });
    }

    private static float MoveYOf(MainWindow window, string slot) => window.Viewport.CurrentPose()!.GetState($"{slot}/{MoveY}").AnchoredPosition.Y;

    private static float SwayZOf(MainWindow window, string slot) => window.Viewport.CurrentPose()!.GetState($"{slot}/{Sway}").LocalEulerAngles.Z;

    [Fact]
    public void 클립을_고르면_모션_선택이_풀리고_뷰포트와_시계가_클립_전체를_본다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Prepare(window);
                MotionEditor editor = window.Session.Editor;
                window.SelectMotion("nod");
                Assert.Equal("nod", window.Viewport.MotionName);

                // 클립 추가 — 목록에서 골라지고, 모션 선택이 풀리고, 뷰포트는 클립을 묶는다.
                string clip = window.AddClipFromForm();
                Assert.Equal("clip", clip);
                Assert.Equal("clip", window.SelectedClip);
                Assert.Null(window.SelectedMotion);
                Assert.Null(window.Viewport.MotionName);
                Assert.Null(window.Graph.MotionName);
                Assert.Equal("clip", window.Viewport.ClipName);
                Assert.Equal("scene", editor.Document.FindClip("clip")!.Scene);

                // 배역 둘: 뷰포트에서 고른 오브젝트가 맡는다. 배역 + 트랙이 되돌리기 한 칸.
                window.Viewport.SelectedObjectId = "s1";
                Assert.Equal("a", window.AddClipRoleFromForm());
                window.Viewport.SelectedObjectId = "s2";
                Assert.Equal("b", window.AddClipRoleFromForm());
                ClipAsset asset = editor.Document.FindClip("clip")!;
                Assert.Equal(new[] { ("a", "s1"), ("b", "s2") }, asset.Roles.Select(r => (r.Name, r.Object!)));
                Assert.Equal(new[] { "a", "b" }, asset.Tracks.Select(t => t.Role));
                editor.Undo();
                Assert.Single(editor.Document.FindClip("clip")!.Roles);
                Assert.Single(editor.Document.FindClip("clip")!.Tracks);
                editor.Redo();
                Assert.Equal(2, window.ClipFormState.Roles);

                // 블록(타임라인은 K4 — 여기서는 편집기 명령으로): s1 bow@0, s2 nod@0.5 → 길이 1.1.
                editor.AddClipBlock("clip", 0, "bow", 0f);
                editor.AddClipBlock("clip", 1, "nod", 0.5f);
                Assert.Equal(1.1, window.Viewport.Clock.Duration, 4);
                Assert.Equal("clip", window.Viewport.ClipName); // 편집 뒤에도 묶임이 남는다

                // 스크럽 — 두 출연자가 각자의 블록대로.
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(8f, SwayZOf(window, "s1"), 3);   // bow는 끝나 남았다
                Assert.Equal(-18f, MoveYOf(window, "s2"), 3); // nod의 0.3초
                Assert.Equal(0f, MoveYOf(window, "s1"), 3);

                window.Viewport.ScrubTo(0.2);
                Assert.InRange(SwayZOf(window, "s1"), 0.5f, 7.5f);
                Assert.Equal(0f, MoveYOf(window, "s2"), 3);   // 아직 시작 전

                // 클립 탭: 길이를 적으면 시계가 따라가고, 비우면 다시 블록 끝.
                Assert.Equal(("clip", "", 2, true), window.ClipFormState);
                editor.SetClipDuration("clip", 3f);
                Assert.Equal(3.0, window.Viewport.Clock.Duration, 4);
                Assert.Equal("3", window.ClipFormState.Duration);
                editor.SetClipDuration("clip", null);
                Assert.Equal(1.1, window.Viewport.Clock.Duration, 4);

                // 모션을 고르면 클립이 풀리고 시계는 다시 모션의 것이다.
                window.SelectMotion("nod");
                Assert.Null(window.SelectedClip);
                Assert.Null(window.Viewport.ClipName);
                Assert.Equal("nod", window.Viewport.MotionName);
                Assert.Equal(0.6, window.Viewport.Clock.Duration, 4);
                Assert.False(window.ClipFormState.Enabled);

                // 다시 클립으로.
                window.SelectClip("clip");
                Assert.Null(window.SelectedMotion);
                Assert.Equal("clip", window.Viewport.ClipName);
                Assert.Equal(1.1, window.Viewport.Clock.Duration, 4);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 고른_클립이_사라지면_뷰포트가_풀리고_클립_문제는_아래에_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Prepare(window);
                MotionEditor editor = window.Session.Editor;
                Assert.Equal(0, window.ProblemRowsShown);

                window.AddClipFromForm();
                Assert.Equal(1, window.ProblemRowsShown); // 빈 클립 알림
                window.AddClipRoleFromForm();
                editor.AddClipBlock("clip", 0, "nod", 0f);
                Assert.Equal(0, window.ProblemRowsShown);

                editor.AddClipBlock("clip", 0, "nod", 0.3f); // 같은 배역·같은 노드 겹침
                Assert.Equal(1, window.ProblemRowsShown);
                editor.Undo();
                Assert.Equal(0, window.ProblemRowsShown);

                // 고른 클립을 지우면 뷰포트가 풀린다.
                editor.RemoveClip("clip");
                Assert.Null(window.SelectedClip);
                Assert.Null(window.Viewport.ClipName);
                Assert.False(window.ClipFormState.Enabled);

                // 되돌리면 클립이 돌아오지만 다시 고르지는 않는다 — 뷰포트는 비어 있다.
                editor.Undo();
                Assert.NotNull(editor.Document.FindClip("clip"));
                Assert.Null(window.Viewport.ClipName);

                // 새 문서 — 목록이 빈다.
                window.SelectClip("clip");
                Assert.Equal("clip", window.Viewport.ClipName);
                window.Session.New();
                Assert.Null(window.SelectedClip);
                Assert.Null(window.Viewport.ClipName);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 클립_층에서는_뷰포트_끌기가_포즈를_만들지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Prepare(window);
                window.AddClipFromForm();
                window.AddClipRoleFromForm();
                window.Session.Editor.AddClipBlock("clip", 0, "nod", 0f);
                long revision = window.Session.Editor.Revision;

                Assert.False(window.Viewport.HasPending);
                window.Viewport.SetComponent(MoveY, MotionChannelKind.Position, Axis.Y, 40f); // 채널 박스 값 입력과 같은 길
                window.Viewport.SetKey();                                                     // S
                Assert.False(window.Viewport.HasPending);
                Assert.Equal(revision, window.Session.Editor.Revision);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ── K4 — 타임라인 ─────────────────────────────────────────────────

    private const float Frame = 1f / 24f;

    /// <summary>clip: 배역 a(s1)·b(s2), 트랙 하나씩. 타임라인 탭이 보이는 채로 돌려준다.</summary>
    private static ClipTimelineView PrepareTimeline(MainWindow window)
    {
        Prepare(window);
        window.AddClipFromForm();
        window.Viewport.SelectedObjectId = "s1";
        window.AddClipRoleFromForm();
        window.Viewport.SelectedObjectId = "s2";
        window.AddClipRoleFromForm();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, window.EditorTab); // 클립을 고르면 타임라인 탭
        Assert.True(window.Timeline.Bounds.Width > ClipTimelineView.Gutter + 100, "타임라인이 배치되지 않았다");
        return window.Timeline;
    }

    private static Point At(MainWindow window, ClipTimelineView timeline, double x, double y)
        => timeline.TranslatePoint(new Point(x, y), window) ?? throw new InvalidOperationException("타임라인이 창에 없다");

    private static Point Center(MainWindow window, ClipTimelineView timeline, int track, int index)
    {
        Rect rect = timeline.BlockRect(track, index);
        return At(window, timeline, rect.Center.X, rect.Center.Y);
    }

    private static List<(string Motion, float At)> Blocks(MainWindow window, int track)
        => window.Session.Editor.Document.FindClip("clip")!.Tracks[track].Blocks.Select(b => (b.Motion, b.At)).ToList();

    [Fact]
    public void 타임라인에_블록을_놓고_고르고_MMB로_끌면_되돌리기_한_칸이다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;

                // 놓기(메뉴의 "모션 놓기"와 같은 길) — 스냅되고, 놓은 블록이 골라진다.
                timeline.PlaceMotion(0, "bow", 0.01);
                timeline.PlaceMotion(1, "nod", 0.51);
                Assert.Equal(new[] { ("bow", 0f) }, Blocks(window, 0));
                Assert.Equal(new[] { ("nod", 12 * Frame) }, Blocks(window, 1));
                Assert.Equal((1, 0), timeline.SelectedBlock);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 막대는 시작 시각에 서고 폭은 모션 길이다.
                Rect bow = timeline.BlockRect(0, 0);
                Assert.Equal(timeline.XAtTime(0), bow.X, 3);
                Assert.Equal(timeline.XAtTime(0.4), bow.Right, 3);
                Assert.True(timeline.RowRect(0).Contains(bow.Center));

                // LMB = 선택.
                window.MouseDown(Center(window, timeline, 0, 0), MouseButton.Left);
                window.MouseUp(Center(window, timeline, 0, 0), MouseButton.Left);
                Assert.Equal((0, 0), timeline.SelectedBlock);
                Assert.Equal(new[] { ("bow", 0f) }, Blocks(window, 0)); // 클릭은 안 옮긴다

                // MMB 드래그 = 이동. 끄는 동안은 문서가 그대로, 놓으면 명령 하나.
                long revision = editor.Revision;
                Point grab = Center(window, timeline, 0, 0);
                double dx = timeline.XAtTime(0.25) - timeline.XAtTime(0);
                var mid = new Point(grab.X + dx / 2, grab.Y);
                var drop = new Point(grab.X + dx, grab.Y + ClipTimelineView.RowHeight); // 아래 트랙(b)으로
                window.MouseDown(grab, MouseButton.Middle);
                window.MouseMove(mid, RawInputModifiers.MiddleMouseButton);
                Assert.Equal(revision, editor.Revision);
                window.MouseMove(drop, RawInputModifiers.MiddleMouseButton);
                window.MouseUp(drop, MouseButton.Middle);

                Assert.Equal(revision + 1, editor.Revision);
                Assert.Empty(Blocks(window, 0));
                Assert.Equal(new[] { ("bow", 6 * Frame), ("nod", 12 * Frame) }, Blocks(window, 1));
                Assert.Equal((1, 0), timeline.SelectedBlock); // 선택이 옮긴 블록을 따라간다

                editor.Undo();
                Assert.Equal(new[] { ("bow", 0f) }, Blocks(window, 0));
                Assert.Equal(new[] { ("nod", 12 * Frame) }, Blocks(window, 1));

                // LMB로 끌어도 옮겨진다(도프 시트와 같다) — 이것도 한 칸.
                grab = Center(window, timeline, 1, 0);
                drop = new Point(grab.X + dx, grab.Y);
                window.MouseDown(grab, MouseButton.Left);
                window.MouseMove(drop, RawInputModifiers.LeftMouseButton);
                window.MouseUp(drop, MouseButton.Left);
                Assert.Equal(new[] { ("nod", 18 * Frame) }, Blocks(window, 1));
                editor.Undo();
                Assert.Equal(new[] { ("nod", 12 * Frame) }, Blocks(window, 1));

                // 제자리에서 놓으면 아무 일도 없다.
                revision = editor.Revision;
                window.MouseDown(Center(window, timeline, 0, 0), MouseButton.Middle);
                window.MouseUp(Center(window, timeline, 0, 0), MouseButton.Middle);
                Assert.Equal(revision, editor.Revision);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 타임라인의_빈_곳은_스크럽이고_Del과_Ctrl_D와_mode가_블록을_고친다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;
                timeline.PlaceMotion(0, "bow", 0);
                timeline.PlaceMotion(1, "nod", 0.5);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 눈금자 LMB = 스크럽(스냅). 선택은 그대로다.
                Point ruler = At(window, timeline, timeline.XAtTime(0.8), 5);
                window.MouseDown(ruler, MouseButton.Left);
                window.MouseUp(ruler, MouseButton.Left);
                Assert.Equal(19 * Frame, window.Viewport.Time, 4);
                Assert.Equal((1, 0), timeline.SelectedBlock);
                Assert.Equal(8f, SwayZOf(window, "s1"), 3);

                // 빈 줄 LMB = 스크럽 + 선택 풀기.
                Point empty = At(window, timeline, timeline.XAtTime(0.2), timeline.RowRect(1).Center.Y);
                window.MouseDown(empty, MouseButton.Left);
                window.MouseUp(empty, MouseButton.Left);
                Assert.Null(timeline.SelectedBlock);
                Assert.Equal(5 * Frame, window.Viewport.Time, 4);

                // Ctrl+D = 끝에 이어서 한 번 더(같은 mode), 새 블록이 골라진다.
                timeline.Select((0, 0));
                timeline.Focus();
                window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
                Assert.Equal(new[] { ("bow", 0f), ("bow", 0.4f) }, Blocks(window, 0));
                Assert.Equal((0, 1), timeline.SelectedBlock);
                Assert.Equal(MotionChannelMode.Absolute, editor.Document.FindClip("clip")!.Tracks[0].Blocks[1].Mode);

                // mode(메뉴와 같은 길).
                timeline.SetSelectedMode(MotionChannelMode.Relative);
                Assert.Equal(MotionChannelMode.Relative, editor.Document.FindClip("clip")!.Tracks[0].Blocks[1].Mode);
                window.Viewport.ScrubTo(5);
                Assert.Equal(16f, SwayZOf(window, "s1"), 3); // 8 위에 +8

                // 겹치게 옮기면 문제 칸이 알리고, Del로 지우면 사라진다.
                timeline.MoveSelected(0.2);
                Assert.Equal(1, window.ProblemRowsShown);
                Assert.Contains((0, 1), Mt.Authoring.Validation.ClipValidator.OverlappingBlocks(editor.Document, editor.Document.FindClip("clip")!));
                window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
                Assert.Equal(new[] { ("bow", 0f) }, Blocks(window, 0));
                Assert.Null(timeline.SelectedBlock);
                Assert.Equal(0, window.ProblemRowsShown);

                // 고른 블록이 되돌리기로 사라져도 선택은 따라 풀린다.
                timeline.PlaceMotion(0, "nod", 1);
                Assert.Equal((0, 1), timeline.SelectedBlock);
                editor.Undo();
                Assert.Null(timeline.SelectedBlock);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 블록을_더블클릭하면_그_모션이_열리고_맡은_오브젝트와_시각이_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                timeline.PlaceMotion(0, "bow", 0);
                timeline.PlaceMotion(1, "nod", 0.5);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.SelectedObjectId = "s1";
                window.Viewport.ScrubTo(0.75); // nod 블록의 0.25초

                Point nod = Center(window, timeline, 1, 0);
                window.MouseDown(nod, MouseButton.Left);
                window.MouseUp(nod, MouseButton.Left);
                window.MouseDown(nod, MouseButton.Left); // 같은 자리의 두 번째 누름 = 더블클릭
                window.MouseUp(nod, MouseButton.Left);

                Assert.Equal("nod", window.SelectedMotion);
                Assert.Null(window.SelectedClip);
                Assert.Equal("nod", window.Viewport.MotionName);
                Assert.Null(window.Viewport.ClipName);
                Assert.Equal("s2", window.Viewport.SelectedObjectId); // 배역 b를 맡은 오브젝트
                Assert.Equal(0.25, window.Viewport.Time, 4);
                Assert.Equal(0, window.EditorTab); // 그래프

                // 클립으로 돌아오면 다시 타임라인이고 블록은 그대로다.
                window.SelectClip("clip");
                Assert.Equal(2, window.EditorTab);
                Assert.Equal(new[] { ("nod", 0.5f) }, Blocks(window, 1));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
