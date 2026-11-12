using Avalonia;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Settings;

namespace Mt.App.Tests;

/// <summary>수명의 띠(PLAN-ux.md U5)와 뷰포트의 인물(U7) — 띠를 끌어 구간을 고치고, 이모지가 인물의 머리에 선다.</summary>
public class EmojiLifeStripShellTests
{
    private static void Run(Action<MainWindow> body)
    {
        HeadlessUi.Run(() =>
        {
            string settings = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, "{}");
            var window = new MainWindow(settings) { Width = 1600, Height = 960 };
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                window.Session.Open(Path.Combine(RepoRoot(), "examples", "emoji", "emoji-examples.motion-authoring.json"));
                window.SwitchWorkModeFromForm(WorkMode.Emoji);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // 모드가 띠를 보이게 했다 — 자리를 잡게 한다
                body(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

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

    [Fact]
    public void 이모지는_인물의_머리에_서고_표가_있으면_그_캐릭터의_자리다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            ViewportView viewport = window.Viewport;
            Point feet = viewport.EmojiHostFeetControlPoint()!.Value;
            Point head = viewport.EmojiLocatorControlPoint()!.Value;

            // 표가 없다 — 기본 자리(발에서 800px 위). 화면에서는 위쪽이다.
            Assert.Equal(feet.X, head.X, 1);
            Assert.True(head.Y < feet.Y - 10);
            double perPixel = (feet.Y - head.Y) / EmojiLocatorFile.DefaultPosition.Y;

            // 캐릭터의 항목이 있으면 그 자리 — 보기만 바뀐다(문서는 그대로).
            long before = window.Session.Editor.Revision;
            viewport.SelectedObject!.Character = "seina";
            viewport.EmojiLocators = EmojiLocatorFile.Parse("""{ "MonoBehaviour": { "entries": [ { "character": "seina", "variant": "", "position": { "x": 100.0, "y": 400.0 } } ] } }""");
            Point moved = viewport.EmojiLocatorControlPoint()!.Value;
            Assert.Equal(400 * perPixel, viewport.EmojiHostFeetControlPoint()!.Value.Y - moved.Y, 1);
            Assert.Equal(100 * perPixel, moved.X - viewport.EmojiHostFeetControlPoint()!.Value.X, 1);
            Assert.Equal(before, window.Session.Editor.Revision);

            // 왼쪽을 보는 인물 — 머리의 자리가 발을 축으로 거울에 비친다.
            window.SetEmojiFacingLeftFromForm(true);
            Assert.Equal(-100 * perPixel, viewport.EmojiLocatorControlPoint()!.Value.X - viewport.EmojiHostFeetControlPoint()!.Value.X, 1);
        });
    }

    [Fact]
    public void 인물을_감춰도_이모지의_자리는_그대로이고_목록에_작은_그림이_붙는다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            Point head = window.Viewport.EmojiLocatorControlPoint()!.Value;

            Assert.True(window.Viewport.EmojiShowHost);
            window.SetEmojiShowHostFromForm(false);
            Assert.False(window.Viewport.EmojiShowHost);
            Assert.Equal(head, window.Viewport.EmojiLocatorControlPoint()!.Value);

            Assert.True(window.EmojiRowHasThumb("star"));
            Assert.True(window.EmojiRigRowHasThumb("body"));
        });
    }

    [Fact]
    public void 그림을_고르면_그래프는_그_그림의_것만_보이고_풀면_전부다()
    {
        Run(window =>
        {
            window.SelectEmoji("twinkle"); // 그림 셋(left · center · right) + 이모지 전체의 자리
            Assert.Contains("left · 크기", window.ChannelRowTexts);
            Assert.Contains("right · 크기", window.ChannelRowTexts);
            Assert.Contains("이모지 전체 · 자리", window.ChannelRowTexts);

            window.SelectEmojiRigFromForm("center");
            IReadOnlyList<string> one = window.ChannelRowTexts;
            Assert.Contains("center · 크기", one);
            Assert.Contains("center · 자리", one); // "키 없음" 줄도 그 그림의 것만
            Assert.DoesNotContain(one, t => t.StartsWith("left", StringComparison.Ordinal) || t.StartsWith("right", StringComparison.Ordinal) || t.StartsWith("이모지 전체", StringComparison.Ordinal));
            Assert.All(window.VisibleGraphNodes, n => Assert.StartsWith("center/", n)); // 곡선도 그 그림의 것만
            Assert.NotEmpty(window.VisibleGraphNodes);

            window.SelectEmojiRigsFromForm("left", "right");
            Assert.Contains("left · 크기", window.ChannelRowTexts);
            Assert.Contains("right · 크기", window.ChannelRowTexts);
            Assert.DoesNotContain(window.ChannelRowTexts, t => t.StartsWith("center", StringComparison.Ordinal));

            // 고른 것을 풀면(이모지를 고른 것) 전부로 돌아온다.
            window.SelectEmojiRigFromForm(null);
            Assert.Contains("center · 크기", window.ChannelRowTexts);
            Assert.Contains("이모지 전체 · 자리", window.ChannelRowTexts);
            Assert.Contains(window.VisibleGraphNodes, n => n.StartsWith("left/", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void 뷰포트의_빈_곳을_누르면_전부_풀리고_이모지를_누르면_이모지_전체가_대상이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            ViewportView viewport = window.Viewport;
            MotionEditor editor = window.Session.Editor;

            // 이모지를 고른 것 — 대상은 이모지 전체다(매니퓰레이터가 선다 · 끌면 머리에서의 자리).
            Assert.True(viewport.EmojiWholeSelected);
            Assert.NotNull(viewport.EmojiGizmoForTest());
            Assert.Contains("이모지 전체", viewport.EmojiEditNoteForTest());

            // 빈 곳을 누른다 — 고른 것이 없다. 매니퓰레이터도 없고 S도 아무것도 하지 않는다.
            long before = editor.Revision;
            Assert.True(viewport.DragEmojiForTest(new Point(4, viewport.Bounds.Height - 40), new Point(4, viewport.Bounds.Height - 40)));
            Assert.False(viewport.EmojiWholeSelected);
            Assert.Empty(viewport.SelectedEmojiRigs);
            Assert.Null(viewport.EmojiGizmoForTest());
            Assert.StartsWith("고른 것 없음", viewport.EmojiEditNoteForTest());
            window.SetKeyFromShell();
            Assert.Equal(before, editor.Revision);

            // 그림을 고르면 그 그림, 다시 빈 곳을 누르면 그림도 풀린다 — 이모지 전체로 넘어가지 않는다.
            window.SelectEmojiRigFromForm("body");
            Assert.NotNull(viewport.EmojiGizmoForTest());
            viewport.DragEmojiForTest(new Point(4, viewport.Bounds.Height - 40), new Point(4, viewport.Bounds.Height - 40));
            Assert.Empty(viewport.SelectedEmojiRigs);
            Assert.False(viewport.EmojiWholeSelected);
            Assert.Null(viewport.EmojiGizmoForTest());

            // 왼쪽 목록에서 이모지를 누르면 다시 이모지 전체다.
            window.TapEmojiFromForm();
            Assert.True(viewport.EmojiWholeSelected);
            Assert.NotNull(viewport.EmojiGizmoForTest());
        });
    }

    // ── 움직임 가져오기 (PLAN-ux.md U8) ──────────────────────────

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

    // ── 새 이모지의 첫 화면 (PLAN-ux.md U9) ──────────────────────

    [Fact]
    public void 첫_화면에서_그림과_수명과_움직임을_정하면_움직이는_이모지가_한_칸에_생긴다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            string art = Path.Combine(RepoRoot(), "examples", "emoji", "art");

            window.OpenNewEmojiFromForm();
            Assert.True(window.NewEmojiOpen);
            Assert.Equal("emoji", window.NewEmojiFormState.Name);
            Assert.Contains("빈 그림 body 하나", window.NewEmojiFormState.Summary);
            Assert.Contains("등장 튀어나오기", window.NewEmojiFormState.Summary); // 처음 골라져 있는 움직임

            // PNG만 받는다 — 같은 파일은 한 번.
            window.FillNewEmojiFromForm("wow", new[] { Path.Combine(art, "star.png"), Path.Combine(art, "star.png"), Path.Combine(art, "notes.txt") }, true, EmojiMove.PopIn, EmojiMove.Pulse, null);
            Assert.Equal(new[] { "star.png" }, window.NewEmojiFormState.Images);
            Assert.Equal("만들기 → 그림 1장 · 머무는 이모지 · 등장 튀어나오기 · 머묾 두근거리기 · 길이 1초(24프레임 — 뒤에 바꾼다)", window.NewEmojiFormState.Summary);

            long before = editor.Revision;
            Assert.Equal("wow", window.CreateNewEmojiFromForm());
            Assert.Equal(before + 1, editor.Revision);
            Assert.False(window.NewEmojiOpen);

            // 만든 이모지가 골라지고, 띠 · 목록 · 그림이 따라온다.
            Assert.Equal("wow", window.SelectedEmoji);
            EmojiAsset wow = editor.Document.FindEmoji("wow")!;
            Assert.Equal("art/star.png", Assert.Single(Assert.Single(wow.Rigs).Images)); // 문서 기준 경로
            Assert.Equal(new[] { new LifeBand("in", 0, 7, EmojiLifeRole.In), new LifeBand("hold", 8, 24, EmojiLifeRole.Hold) }, window.LifeBands);
            Assert.Equal("머무는 이모지", window.EmojiRowNote("wow"));
            Assert.True(window.EmojiRowHasThumb("wow"));
            Assert.DoesNotContain(Mt.Authoring.Validation.EmojiValidator.Validate(editor.Document), p => p.ToString().Contains("wow"));

            // 이름이 겹치면 만들지 않고 창이 남는다.
            window.OpenNewEmojiFromForm();
            window.FillNewEmojiFromForm("wow", Array.Empty<string>(), false, null, null, null);
            Assert.Null(window.CreateNewEmojiFromForm());
            Assert.True(window.NewEmojiOpen);
            Assert.Equal(before + 1, editor.Revision);

            // 그림의 순서 — ▲ ▼로 바꾸고, 그 순서로 그림이 생긴다(아래가 위). 끝에서는 그대로다.
            window.FillNewEmojiFromForm("stack", new[] { Path.Combine(art, "star.png"), Path.Combine(art, "question.png"), Path.Combine(art, "spark.png") }, false, null, null, null);
            Assert.Equal(new[] { "star.png", "question.png", "spark.png" }, window.NewEmojiFormState.Images);
            window.MoveNewEmojiImageFromForm(2, -1);
            window.MoveNewEmojiImageFromForm(0, -1); // 맨 위에서 위로 — 그대로
            window.MoveNewEmojiImageFromForm(0, 1);
            Assert.Equal(new[] { "spark.png", "star.png", "question.png" }, window.NewEmojiFormState.Images);
            Assert.Equal("stack", window.CreateNewEmojiFromForm());
            Assert.Equal(new[] { "spark", "star", "question" }, editor.Document.FindEmoji("stack")!.Rigs.Select(r => r.Name));
            Assert.Equal(before + 2, editor.Revision);
            window.OpenNewEmojiFromForm();

            // 빈 이모지로 시작 — 전과 같은 것(빈 그림 body 하나).
            string blank = window.StartBlankEmojiFromForm();
            Assert.False(window.NewEmojiOpen);
            Assert.Equal("body", Assert.Single(editor.Document.FindEmoji(blank)!.Rigs).Name);
            Assert.Empty(editor.Document.FindEmoji(blank)!.Timeline.Channels);
        });
    }

    // ── 키 놓기의 단추 · 종류별 칸 (PLAN-ux.md U6의 나머지) ──────────

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

    // ── 트윈머신 (PLAN-tween.md T1) ──────────────────────────────

    [Fact]
    public void 트윈머신_창은_고른_것과_지금_프레임을_따라가고_끌면_보이고_놓으면_한_칸이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            var scaleX = new Mt.Authoring.Graph.CurveRef(frame.ImageNodeOf("body"), ChannelKind.Scale, Axis.X);
            KeyAsset[] keys = scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys.ToArray();
            (KeyAsset a, KeyAsset b) = (keys[0], keys[1]);
            int between = (EmojiAsset.FrameOf(a.T) + EmojiAsset.FrameOf(b.T)) / 2;
            Assert.True(between > EmojiAsset.FrameOf(a.T) && between < EmojiAsset.FrameOf(b.T), "예시의 첫 두 키 사이에 프레임이 있어야 한다");

            window.OpenTweenFromForm();
            Assert.True(window.TweenOpen);

            // 그림을 고르고 두 키 사이로 간다 — 창이 무엇에, 어느 키 사이에 놓이는지 적는다.
            window.SelectEmojiRigFromForm("body");
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(between));
            Assert.StartsWith($"body  |  {between}fr에 — 앞 키 ", window.TweenNote);

            // 끈다 — 문서가 따라오지만(뷰포트 · 그래프에 보인다) 기록은 아직 없다.
            long before = editor.Revision;
            window.DragTweenFromForm(20);
            window.DragTweenFromForm(80);
            Assert.Equal(before, editor.Revision);
            float shown = scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys.Single(k => EmojiAsset.FrameOf(k.T) == between).V;
            Assert.Equal(a.V + (b.V - a.V) * 0.8f, shown, 3);
            Assert.Equal(EmojiAsset.SecondsOf(between), window.Viewport.Clock.Time, 4); // 보고 있던 프레임 그대로

            // 놓는다 — 한 칸. 되돌리면 끌기 전으로.
            window.ReleaseTweenFromForm();
            Assert.Equal(before + 1, editor.Revision);
            editor.Undo();
            Assert.DoesNotContain(scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys, k => EmojiAsset.FrameOf(k.T) == between);

            // 값 단추 — 바로 한 칸. 고른 그림의 것만 건드린다(이모지 전체의 채널은 그대로).
            string wholeBefore = string.Join(",", editor.Document.FindEmoji("star")!.Timeline.Channels.Where(c => c.Node == frame.OffsetNode).SelectMany(c => c.X?.Keys ?? new List<KeyAsset>()).Select(k => k.T));
            before = editor.Revision;
            window.PressTweenButtonFromForm(50);
            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal((a.V + b.V) / 2, scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys.Single(k => EmojiAsset.FrameOf(k.T) == between).V, 3);
            Assert.Equal(wholeBefore, string.Join(",", editor.Document.FindEmoji("star")!.Timeline.Channels.Where(c => c.Node == frame.OffsetNode).SelectMany(c => c.X?.Keys ?? new List<KeyAsset>()).Select(k => k.T)));
            Assert.Equal(50, window.TweenSliderState.Value);

            // 오버슈트 — 범위가 넓어진다.
            Assert.Equal((0d, 100d), (window.TweenSliderState.Min, window.TweenSliderState.Max));
            window.SetTweenOvershootFromForm(true);
            Assert.Equal((-50d, 150d), (window.TweenSliderState.Min, window.TweenSliderState.Max));

            // 첫 프레임 — 앞에 키가 없다. 놓일 곳이 없다고 적고 아무것도 하지 않는다.
            window.Viewport.ScrubTo(0);
            Assert.Contains("앞과 뒤에 키가 있는 곡선이 없다", window.TweenNote);
            before = editor.Revision;
            window.PressTweenButtonFromForm(50);
            Assert.Equal(before, editor.Revision);

            window.CloseTweenFromForm();
            Assert.False(window.TweenOpen);
        });
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "KedMotionTool.sln")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("저장소 루트를 찾지 못했다.");
    }
}
