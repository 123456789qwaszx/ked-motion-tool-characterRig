using Avalonia.Headless;
using Mt.App;

namespace Mt.App.Tests;

/// <summary>
/// 예시 문서(<c>examples/emoji</c>)를 앱에서 열어 장면을 그림으로 뽑는다 — <c>MT_RENDER_DIR=폴더 dotnet test --filter EmojiExampleShots</c>.
/// 폴더가 없으면 아무것도 하지 않는다(보통의 테스트 실행).
/// </summary>
public class EmojiExampleShots
{
    [Fact]
    public void 예시의_이모지를_하나씩_그림으로_뽑는다()
    {
        if (RenderShots.Directory is not { } directory)
        {
            return;
        }

        HeadlessUi.Run(() =>
        {
            string settings = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, "{}");
            var window = new MainWindow(settings) { Width = 1500, Height = 950 };
            window.Show();

            try
            {
                DirectoryInfo? root = new(AppContext.BaseDirectory);

                while (root is not null && !File.Exists(Path.Combine(root.FullName, "KedMotionTool.sln")))
                {
                    root = root.Parent;
                }

                window.Session.Open(Path.Combine(root!.FullName, "examples", "emoji", "emoji-examples.motion-authoring.json"));
                Directory.CreateDirectory(directory);
                window.InspectorTab = 4;

                void Shot(string name)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using FileStream stream = File.Create(Path.Combine(directory, name + ".png"));
                    window.CaptureRenderedFrame()?.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }

                int index = 0;

                foreach (string emoji in window.Session.Editor.Document.Emojis.Select(e => e.Name).ToList())
                {
                    window.SelectEmoji(emoji);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.Viewport.ScrubTo(0.7);
                    Shot($"emoji-{index++:00}");
                }

                window.SelectClip("emoji_demo");

                foreach (double t in new[] { 0.6, 3.0, 7.5 })
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.Viewport.ScrubTo(t);
                    Shot($"clip-{t:0.0}");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }
}
