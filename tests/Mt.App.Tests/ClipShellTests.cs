using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

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
        editor.AddChannel("nod", MoveY, ChannelKind.Position);
        editor.SetKeys("nod", MoveY, ChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });

        editor.AddMotion("bow", "character", 0.4f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("bow", Sway, ChannelKind.Euler);
        editor.SetKeys("bow", Sway, ChannelKind.Euler, Axis.Z, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.4f, 8f) });
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
                window.Viewport.SetComponent(MoveY, ChannelKind.Position, Axis.Y, 40f); // 채널 박스 값 입력과 같은 길
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
        Assert.True(window.Timeline.IsVisible); // 클립을 고르면 그래프 위에 타임라인이 선다
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
                Assert.False(window.Timeline.IsVisible); // 모션 층에서는 타임라인이 숨는다

                // 클립으로 돌아오면 다시 타임라인이고 블록은 그대로다.
                window.SelectClip("clip");
                Assert.True(window.Timeline.IsVisible);
                Assert.Equal(new[] { ("nod", 0.5f) }, Blocks(window, 1));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 줄_머리를_누르면_배역이_꺼지고_한_캐릭터에_겹친_블록을_새_모션으로_굽는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;

                // 같은 캐릭터(s1)를 맡는 배역을 하나 더 — 그 위에 모션을 겹친다.
                window.Viewport.SelectedObjectId = "s1";
                Assert.Equal("c", window.AddClipRoleFromForm());
                timeline.PlaceMotion(0, "bow", 0);   // a(s1): Sway 8
                timeline.PlaceMotion(2, "nod", 0);   // c(s1): MoveY
                timeline.PlaceMotion(1, "nod", 0.5); // b(s2)
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                window.Viewport.ScrubTo(0.3);
                Assert.Equal(-18f, MoveYOf(window, "s1"), 3);
                Assert.InRange(SwayZOf(window, "s1"), 4f, 8f);

                // 줄 머리의 🔊 = 배역 끄기. 문서는 안 바뀐다(보기 상태).
                long revision = editor.Revision;
                Rect mute = timeline.MuteRect(2);
                Point click = At(window, timeline, mute.Center.X, mute.Center.Y);
                window.MouseDown(click, MouseButton.Left);
                window.MouseUp(click, MouseButton.Left);
                Assert.True(window.Viewport.IsClipRoleMuted("c"));
                Assert.Equal(revision, editor.Revision);
                Assert.False(editor.CanRedo);
                Assert.Equal(0f, MoveYOf(window, "s1"), 3);       // c의 nod가 빠졌다
                Assert.InRange(SwayZOf(window, "s1"), 4f, 8f);     // a는 그대로
                Assert.Equal(0.3, window.Viewport.Time, 4);        // 누른 자리가 스크럽으로 새지 않는다

                // 끈 채로 구우면 a의 bow만 든다.
                string? onlyBow = window.BakeClipActorFromMenu(0);
                Assert.Equal("clip_s1", onlyBow);
                Assert.Equal(new[] { Sway }, editor.Document.Find("clip_s1")!.Channels.Select(c => c.Node));
                Assert.Equal("clip", window.SelectedClip); // 클립에 그대로 있다
                Assert.Contains("clip_s1", window.Session.Status, StringComparison.Ordinal);

                // 모션을 열었다 돌아와도 끈 배역은 남는다.
                window.SelectMotion("nod");
                window.SelectClip("clip");
                Assert.True(window.Viewport.IsClipRoleMuted("c"));
                window.Viewport.ScrubTo(0.3); // 묶을 때마다 시계는 처음으로 간다

                // 다시 켜고 구우면 둘 다 — 이름은 겹치지 않게.
                timeline.ToggleRoleMuted(2);
                Assert.False(window.Viewport.IsClipRoleMuted("c"));
                Assert.Equal(-18f, MoveYOf(window, "s1"), 3);
                string? both = window.BakeClipActorFromMenu(2); // c의 줄에서 구워도 s1 전부다
                Assert.Equal("clip_s1_2", both);
                MotionAsset baked = editor.Document.Find("clip_s1_2")!;
                Assert.Equal(new[] { Sway, MoveY }, baked.Channels.Select(c => c.Node));
                Assert.Equal(MotionEnd.Commit, baked.End);
                Assert.Equal(0, window.ProblemRowsShown);

                // 구운 모션 하나가 같은 움직임을 낸다.
                window.SelectMotion("clip_s1_2");
                window.Viewport.SelectedObjectId = "s1";
                window.Viewport.ScrubTo(0.3);
                Assert.Equal(-18f, MoveYOf(window, "s1"), 2);
                Assert.InRange(SwayZOf(window, "s1"), 4f, 8f);

                // 굽기는 되돌리기 한 번.
                editor.Undo();
                Assert.Null(editor.Document.Find("clip_s1_2"));
                Assert.NotNull(editor.Document.Find("clip_s1"));

                // 새 문서에는 끈 배역이 안 남는다.
                window.SelectClip("clip");
                timeline.ToggleRoleMuted(0);
                Assert.True(window.Viewport.IsClipRoleMuted("a"));
                window.Session.New();
                Assert.False(window.Viewport.IsClipRoleMuted("a"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ── K4b 1단계 — 클립 안에서 블록의 모션을 고친다 ──────────────────

    [Fact]
    public void 블록을_고르면_클립을_떠나지_않고_그래프가_그_모션을_묶고_시각은_블록_시작만큼_어긋난다()
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

                // 놓은 블록이 골라져 있다 — 그래프가 nod를 묶었고 클립은 그대로다.
                Assert.Equal((1, 0), timeline.SelectedBlock);
                Assert.Equal((true, 0.5), window.BlockContext);
                Assert.Equal("nod", window.Graph.MotionName);
                Assert.Equal("clip", window.SelectedClip);
                Assert.Null(window.SelectedMotion);
                Assert.Equal("clip", window.Viewport.ClipName);
                Assert.True(window.Timeline.IsVisible);

                // 시계는 하나: 클립 0.8초 = nod의 0.3초. 그래프에서 옮기면 뷰포트가 블록 시작만큼 더해 간다.
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(0.3, window.Graph.CurrentTime, 4);
                window.Graph.CurrentTime = 0.1;
                Assert.Equal(0.6, window.Viewport.Time, 4);

                // 그래프에서 키를 끼운다 — 모션이 바뀌고, 클립도 시각도 그대로다(고칠 때마다 처음으로 돌아가지 않는다).
                window.Graph.CurrentTime = 0.15;
                int keysBefore = editor.Document.Find("nod")!.Channels[0].Y!.Keys.Count;
                window.Graph.InsertKeyAtCurrentTime();
                Assert.Equal(keysBefore + 1, editor.Document.Find("nod")!.Channels[0].Y!.Keys.Count);
                Assert.Equal("clip", window.SelectedClip);
                Assert.Equal("clip", window.Viewport.ClipName);
                Assert.Equal(0.65, window.Viewport.Time, 4);
                Assert.Equal("nod", window.Graph.MotionName);
                Assert.Equal((1, 0), timeline.SelectedBlock);

                // 키 값을 고치면 뷰포트의 클립 포즈가 바로 따라온다 — 다른 출연자는 제 블록대로 서 있다.
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(-18f, MoveYOf(window, "s2"), 2);
                editor.SetKeys("nod", MoveY, ChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -40f), TestDocuments.Key(0.6f, 0f) });
                Assert.Equal(-40f, MoveYOf(window, "s2"), 2);
                Assert.Equal(8f, SwayZOf(window, "s1"), 2);
                Assert.Equal(0.8, window.Viewport.Time, 4);

                // 블록을 옮기면 시각 차가 따라간다.
                timeline.MoveSelected(1.0);
                Assert.Equal((true, 1.0), window.BlockContext);
                window.Viewport.ScrubTo(1.25);
                Assert.Equal(0.25, window.Graph.CurrentTime, 4);

                // 다른 블록을 고르면 그 모션으로, 빈 곳을 누르면 그래프가 빈다.
                timeline.Select((0, 0));
                Assert.Equal("bow", window.Graph.MotionName);
                Assert.Equal((true, 0.0), window.BlockContext);
                timeline.Select(null);
                Assert.Null(window.Graph.MotionName);
                Assert.Equal("clip", window.SelectedClip);

                // 모션 이름이 바뀌어도 블록을 따라 그래프가 다시 묶인다 — 클립은 그대로다.
                timeline.Select((1, 0));
                editor.RenameMotion("nod", "nod_deep");
                Assert.Equal("nod_deep", window.Graph.MotionName);
                Assert.Equal("clip", window.SelectedClip);
                Assert.Null(window.SelectedMotion);

                // 목록에서 모션을 고르면 클립을 떠난다 — 시각 차는 없다.
                window.SelectMotion("bow");
                Assert.Null(window.SelectedClip);
                Assert.Equal((false, 0.0), window.BlockContext);
                window.Viewport.ScrubTo(0.2);
                Assert.Equal(0.2, window.Graph.CurrentTime, 4);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이_블록만_따로는_모션을_복제해_그_블록만_새_모션을_가리키게_한다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;
                timeline.PlaceMotion(0, "nod", 0);
                timeline.PlaceMotion(1, "nod", 0.5);
                Assert.Equal(2, ClipTiming.UsesOf(editor.Document, "nod"));

                long revision = editor.Revision;
                string? copy = timeline.MakeSelectedUnique();
                Assert.Equal("nod_clip", copy);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { ("nod", 0f) }, Blocks(window, 0));
                Assert.Equal(new[] { ("nod_clip", 0.5f) }, Blocks(window, 1));
                Assert.Equal("nod_clip", window.Graph.MotionName); // 그래프가 새 모션으로 넘어온다
                Assert.Equal("clip", window.SelectedClip);

                // 이제 이 블록만 다르게 고쳐진다.
                editor.SetKeys("nod_clip", MoveY, ChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -40f), TestDocuments.Key(0.6f, 0f) });
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(-40f, MoveYOf(window, "s2"), 2);
                window.Viewport.ScrubTo(0.3);
                Assert.Equal(-18f, MoveYOf(window, "s1"), 2);

                editor.Undo(); // 키
                editor.Undo(); // 따로 떼기
                Assert.Null(editor.Document.Find("nod_clip"));
                Assert.Equal(new[] { ("nod", 0.5f) }, Blocks(window, 1));
                Assert.Equal("nod", window.Graph.MotionName);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ── K4b 2단계 — 클립 안에서 뷰포트·채널 박스·S로 키를 놓는다 ────────

    [Fact]
    public void 클립_안에서_잡은_포즈는_고른_블록의_모션에_블록_기준으로_키가_된다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;

                // lean: Sway z 0 → 5 (0.5초, commit). s1에 bow(절대값 8) 뒤에 lean(상대값)을 잇는다. s2에는 nod.
                editor.AddMotion("lean", "character", 0.5f, MotionEnd.Commit);
                editor.AddChannel("lean", Sway, ChannelKind.Euler);
                editor.SetKeys("lean", Sway, ChannelKind.Euler, Axis.Z, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.5f, 5f) });
                timeline.PlaceMotion(0, "bow", 0);
                timeline.PlaceMotion(1, "nod", 0.5);
                timeline.PlaceMotion(0, "lean", 1.0);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.AutoKey = false;
                window.Viewport.SnapEnabled = false; // 이 테스트의 시각(0.3초 등)은 프레임 눈금이 아니다

                // 블록을 고르면 뷰포트도 그 블록을 보고, 그 배역의 캐릭터가 골라진다.
                timeline.Select((1, 0)); // nod @ 0.5 (s2)
                Assert.Equal((1, 0), window.Viewport.ClipBlockContext);
                Assert.Equal("s2", window.Viewport.SelectedObjectId);

                // 블록 밖(0.2초)에서는 포즈를 잡지 않는다 — 그 모션이 보이지 않는 자리다.
                window.Viewport.ScrubTo(0.2);
                Assert.Null(window.Viewport.EditMotion);
                long revision = editor.Revision;
                window.Viewport.SetComponent(MoveY, ChannelKind.Position, Axis.Y, -30f);
                window.Viewport.SetKey();
                Assert.Equal(revision, editor.Revision);
                Assert.False(window.Viewport.HasPending);

                // 블록 안(클립 0.8초 = nod의 0.3초): 그 시각에 키가 있으니 값 입력이 곧 키다 — nod의 0.3초 키가 바뀐다.
                window.Viewport.ScrubTo(0.8);
                Assert.Equal("nod", window.Viewport.EditMotion?.Name);
                window.Viewport.SetComponent(MoveY, ChannelKind.Position, Axis.Y, -30f);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { (0f, 0f), (0.3f, -30f), (0.6f, 0f) }, editor.Document.Find("nod")!.Channels[0].Y!.Keys.Select(k => (k.T, k.V)));
                Assert.Equal(-30f, MoveYOf(window, "s2"), 2);
                Assert.Equal("clip", window.SelectedClip);     // 클립을 떠나지 않는다
                Assert.Equal(0.8, window.Viewport.Time, 4);     // 시각도 그대로
                Assert.Equal((1, 0), timeline.SelectedBlock);

                // 키가 없는 시각(클립 0.75초 = 0.25초): 키 없는 포즈로 남고, S가 모션 기준 시각에 키한다 — 한 칸.
                window.Viewport.ScrubTo(0.75);
                window.Viewport.SetComponent(MoveY, ChannelKind.Position, Axis.Y, -10f);
                Assert.True(window.Viewport.HasPending);
                Assert.Equal(3, editor.Document.Find("nod")!.Channels[0].Y!.Keys.Count);
                window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
                Assert.False(window.Viewport.HasPending);
                Assert.Contains(editor.Document.Find("nod")!.Channels[0].Y!.Keys, k => Math.Abs(k.T - 0.25f) < 1e-4f && Math.Abs(k.V + 10f) < 1e-3f);
                editor.Undo();
                Assert.Equal(3, editor.Document.Find("nod")!.Channels[0].Y!.Keys.Count);
                Assert.True(window.Viewport.HasPending); // 키 확정 직전의 포즈로 돌아온다
                window.Viewport.ScrubTo(0.8);            // 스크럽이 버린다

                // 시작값은 "이 블록이 얹히기 전의 상태"다: lean(상대값)은 bow가 남긴 8 위에 얹힌다 — 20이 보이게 하려면 키는 12.
                timeline.Select((0, 1)); // lean @ 1.0 (s1)
                Assert.Equal("s1", window.Viewport.SelectedObjectId);
                window.Viewport.ScrubTo(1.5);
                Assert.Equal(13f, SwayZOf(window, "s1"), 2);
                window.Viewport.SetComponent(Sway, ChannelKind.Euler, Axis.Z, 20f);
                Assert.Equal(12f, editor.Document.Find("lean")!.Channels[0].Z!.Keys[^1].V, 3);
                Assert.Equal(20f, SwayZOf(window, "s1"), 2);

                // 재생 기준은 블록의 것이다: 절대값 블록이면 키가 곧 그 값(모션의 렌즈는 그대로 상대값).
                timeline.SetSelectedMode(MotionChannelMode.Absolute);
                window.Viewport.SetComponent(Sway, ChannelKind.Euler, Axis.Z, 30f);
                Assert.Equal(30f, editor.Document.Find("lean")!.Channels[0].Z!.Keys[^1].V, 3);
                Assert.Equal(MotionChannelMode.Relative, editor.Document.Find("lean")!.PreviewMode);
                Assert.Equal(30f, SwayZOf(window, "s1"), 2);

                // 없던 채널도 클립 안에서 생긴다(Auto Key) — 모션으로 건너갈 일이 없다.
                window.Viewport.AutoKey = true;
                window.Viewport.ScrubTo(1.25);
                window.Viewport.SetComponent(MoveY, ChannelKind.Position, Axis.Y, 15f);
                ChannelAsset added = Assert.Single(editor.Document.Find("lean")!.Channels, c => c.Node == MoveY);
                Assert.Equal((0.25f, 15f), (added.Y!.Keys[0].T, added.Y.Keys[0].V));
                Assert.Equal("clip", window.SelectedClip);

                // 블록을 끄면 그 블록에는 키를 놓지 않고, 미리보기에서도 빠진다.
                window.Viewport.ScrubTo(1.5);
                timeline.SetSelectedMuted(true);
                Assert.Null(window.Viewport.EditMotion);
                Assert.Equal(8f, SwayZOf(window, "s1"), 2); // bow가 남긴 값만
                editor.Undo();
                Assert.Equal(30f, SwayZOf(window, "s1"), 2);

                // 빈 곳을 고르면 다시 포즈를 잡지 않는다.
                timeline.Select(null);
                Assert.Null(window.Viewport.ClipBlockContext);
                Assert.Null(window.Viewport.EditMotion);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 블록은_M으로_끄고_켜고_타임라인과_그래프_사이는_끌어서_조절하면_설정에_남는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;
                timeline.PlaceMotion(0, "nod", 0);
                timeline.PlaceMotion(0, "bow", 0);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 같은 배역 안에서 블록 하나만 끈다 — 다른 블록은 그대로 돈다.
                window.Viewport.ScrubTo(0.3);
                Assert.Equal(-18f, MoveYOf(window, "s1"), 2);
                Assert.InRange(SwayZOf(window, "s1"), 4f, 8f);
                timeline.Select((0, 0));
                string first = editor.Document.FindClip("clip")!.Tracks[0].Blocks[0].Motion;
                long revision = editor.Revision;
                timeline.Focus();
                window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
                Assert.True(editor.Document.FindClip("clip")!.Tracks[0].Blocks[0].IsMuted);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.False(window.Viewport.IsClipRoleMuted("a")); // 배역은 켜져 있다

                if (first == "nod")
                {
                    Assert.Equal(0f, MoveYOf(window, "s1"), 2);
                    Assert.InRange(SwayZOf(window, "s1"), 4f, 8f);
                }
                else
                {
                    Assert.Equal(-18f, MoveYOf(window, "s1"), 2);
                    Assert.Equal(0f, SwayZOf(window, "s1"), 2);
                }

                window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
                Assert.False(editor.Document.FindClip("clip")!.Tracks[0].Blocks[0].IsMuted);
                Assert.Equal(-18f, MoveYOf(window, "s1"), 2);
                editor.Undo();
                Assert.True(editor.Document.FindClip("clip")!.Tracks[0].Blocks[0].IsMuted);
                editor.Undo();

                // 타임라인 줄의 높이: 처음엔 트랙 수에 맞고, 끌어서 바꾸면 설정에 남아 다시 클립을 고를 때 돌아온다.
                double natural = ClipTimelineView.RulerHeight + 2 * ClipTimelineView.RowHeight + 4;
                Assert.Equal(natural, window.TimelineRowHeight, 1);
                Assert.Equal(0, window.Settings.TimelineHeight);
                window.TimelineRowHeight = 150;
                Assert.Equal(150, window.Settings.TimelineHeight, 1);

                window.SelectMotion("nod");
                Assert.Equal(0, window.TimelineRowHeight, 1); // 모션 층에서는 자리를 안 차지한다
                Assert.Equal(150, window.Settings.TimelineHeight, 1);
                window.SelectClip("clip");
                Assert.Equal(150, window.TimelineRowHeight, 1);
                Assert.True(window.Timeline.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 반복하는_블록은_바퀴마다_다시_돌고_그래프의_시각도_모션_길이_안에서_돈다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                ClipTimelineView timeline = PrepareTimeline(window);
                MotionEditor editor = window.Session.Editor;
                window.Viewport.SnapEnabled = false;
                window.Viewport.AutoKey = false;
                timeline.PlaceMotion(1, "nod", 0.5); // s2: 0.5 ~ 1.1
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 인스펙터의 "반복" — 세 번. 블록이 세 배로 길어지고 클립 길이가 따라간다. 한 칸.
                long revision = editor.Revision;
                window.UpdateSelectedClipBlock(b => b.Repeat = 3);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(3, editor.Document.FindClip("clip")!.Tracks[1].Blocks[0].Cycles);
                Assert.Equal(2.3, window.Viewport.Clock.Duration, 4); // 0.5 + 0.6 × 3
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(timeline.XAtTime(2.3) - timeline.XAtTime(0.5), timeline.BlockRect(1, 0).Width, 1); // 막대가 세 바퀴만큼

                // 바퀴마다 같은 움직임이 다시 온다(복원 모션): 0.8 · 1.4 · 2.0초가 모두 가장 깊은 자리.
                foreach (double t in new[] { 0.8, 1.4, 2.0 })
                {
                    window.Viewport.ScrubTo(t);
                    Assert.Equal(-18f, MoveYOf(window, "s2"), 2);
                    Assert.Equal(0.3, window.Graph.CurrentTime, 4); // 그래프는 모션의 0.3초 — 바퀴가 넘어가도 모션 길이 안에서 돈다
                }

                // 그래프에서 시각을 옮기면 지금 바퀴 안에서 간다.
                window.Viewport.ScrubTo(1.4);
                window.Graph.CurrentTime = 0.1;
                Assert.Equal(1.2, window.Viewport.Time, 4); // 둘째 바퀴(1.1 ~ 1.7)의 0.1초

                // 둘째 바퀴에서 잡은 포즈도 그 모션의 키가 된다 — 모든 바퀴가 같이 바뀐다(모션은 하나다).
                window.Viewport.ScrubTo(1.4);
                window.Viewport.SetComponent(MoveY, ChannelKind.Position, Axis.Y, -30f);
                Assert.Contains(editor.Document.Find("nod")!.Channels[0].Y!.Keys, k => Math.Abs(k.T - 0.3f) < 1e-4f && Math.Abs(k.V + 30f) < 1e-3f);
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(-30f, MoveYOf(window, "s2"), 2);

                // 한 번으로 되돌리면 안 적힌다.
                ClipBlock once = editor.Document.FindClip("clip")!.Tracks[1].Blocks[0].Clone();
                once.Repeat = 1;
                editor.UpdateClipBlock("clip", 1, 0, once);
                Assert.Null(editor.Document.FindClip("clip")!.Tracks[1].Blocks[0].Repeat);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
