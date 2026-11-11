using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

/// <summary>
/// 이모지 화면의 장면(PLAN-emoji.md E6) — <c>MT_RENDER_DIR</c>이 있으면 그림 파일로 뽑는다(눈으로 보는 확인).
/// 없으면 장면을 세우고 뷰포트가 이모지를 보는지만 본다. 장면은 예시 문서(<c>examples/emoji</c>)다.
/// </summary>
public class EmojiShots
{
    [Fact]
    public void 예시_문서의_이모지를_타임라인과_수명으로_본다()
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
                window.SelectEmoji("twinkle");

                // 예시(E7)의 그림이 읽혔고, 고르면 보기가 그림에 맞는다.
                Assert.Equal("twinkle", window.Viewport.EmojiName);
                Assert.NotNull(window.Viewport.EmojiControlBox());
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(10));
                Shot(window, "emoji-twinkle");

                window.SelectEmoji("star");
                window.SelectEmojiRigFromForm("body");
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(9));
                Shot(window, "emoji-star-timeline");
                window.SetEmojiLifeFromForm(true);
                window.Viewport.ScrubTo(1.4);
                Assert.Equal(EmojiPhase.Hold, window.Viewport.EmojiCurrentMoment()!.Value.Phase);
                Shot(window, "emoji-star-life");
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
