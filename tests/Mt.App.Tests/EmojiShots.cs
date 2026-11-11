using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Mt.Authoring.Editing;

namespace Mt.App.Tests;

/// <summary>
/// 이모지 화면의 장면(PLAN-emoji.md E6) — <c>MT_RENDER_DIR</c>이 있으면 그림 파일로 뽑는다(눈으로 보는 확인).
/// 없으면 장면을 세우고 뷰포트가 이모지를 보는지만 본다. 그림은 <c>examples/emoji/art</c>의 것.
/// </summary>
public class EmojiShots
{
    [Fact]
    public void 이모지_탭의_타임라인과_수명_보기()
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
                MotionEditor editor = window.Session.Editor;
                string star = Path.Combine(RepoRoot(), "examples", "emoji", "art", "star.png");

                window.AddEmojiFromForm();
                editor.SetEmojiRigImages("emoji", "body", new[] { star });
                editor.AddEmojiSpan("emoji", "in", 0, 7);
                editor.AddEmojiSpan("emoji", "hold", 8, 23);
                window.LinkEmojiFromForm("in", "hold", null);
                window.KeyEmojiNowFromForm("body", Mt.Authoring.Model.ChannelKind.Alpha);
                window.Viewport.FrameEmoji();

                Assert.NotNull(window.Viewport.EmojiNow());
                Assert.NotNull(window.Viewport.EmojiControlBox());

                Shot(window, "emoji-timeline");
                window.SetEmojiLifeFromForm(true);
                window.Viewport.ScrubTo(0.5);
                Shot(window, "emoji-life");
            }
            finally
            {
                window.Close();
            }
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

        throw new DirectoryNotFoundException("KedMotionTool.sln을 찾지 못했다.");
    }
}
