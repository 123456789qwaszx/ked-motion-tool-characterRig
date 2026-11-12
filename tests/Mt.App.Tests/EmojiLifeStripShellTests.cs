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
        });
    }

    private static void Shot(MainWindow window, string name)
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
