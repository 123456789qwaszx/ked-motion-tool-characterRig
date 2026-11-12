using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
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
            Shot(window.NewEmojiWindow!, "new-emoji-form");

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

            // 빈 이모지로 시작 — 전과 같은 것(빈 그림 body 하나).
            string blank = window.StartBlankEmojiFromForm();
            Assert.False(window.NewEmojiOpen);
            Assert.Equal("body", Assert.Single(editor.Document.FindEmoji(blank)!.Rigs).Name);
            Assert.Empty(editor.Document.FindEmoji(blank)!.Timeline.Channels);

            window.SelectEmoji("wow");
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(5));
            Shot(window, "new-emoji-made");
        });
    }

    [Fact]
    public void 띠와_인물을_그림으로_본다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(9));
            Shot(window, "life-star");

            window.SetEmojiFacingLeftFromForm(true);
            Shot(window, "life-star-left");
            window.SetEmojiFacingLeftFromForm(false);

            window.SelectEmoji("twinkle");
            window.SelectEmojiRigFromForm("center");
            Shot(window, "life-twinkle-one-picture");
            window.SelectEmojiRigFromForm(null);
            Shot(window, "life-twinkle-empty");
            window.AddLifeSpanFromForm(EmojiLifeRole.Hold);
            window.AddLifeSpanFromForm(EmojiLifeRole.In);
            Shot(window, "life-twinkle-added");

            // 움직임 가져오기(U8) — 빈 이모지에 등장 · 머묾 · 퇴장을 넣은 모습.
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");
            window.ImportMoveFromForm(EmojiMove.PopIn);
            window.ImportMoveFromForm(EmojiMove.Pulse);
            window.ImportMoveFromForm(EmojiMove.ShrinkOut);
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(12));
            Shot(window, "move-imported");
        });
    }

    private static void Shot(Avalonia.Controls.TopLevel window, string name)
    {
        if (RenderShots.Directory is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Bitmap? frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
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
