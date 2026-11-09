using Ked.Presentation.Core;
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
}
