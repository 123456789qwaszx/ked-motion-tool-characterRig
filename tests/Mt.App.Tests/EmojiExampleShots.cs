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

                // 그림 넘기기 — 점이 하나씩 찍힌다.
                window.SelectEmoji("말줄임점풍선");

                foreach (double t in new[] { 0.2, 0.5, 0.9, 1.4 })
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.Viewport.ScrubTo(t);
                    Shot($"frames-{t:0.0}");
                }

                // 반전 판 — 캐릭터를 좌우로 뒤집으면 자리가 뒤집히고 반전 판 그림이 뜬다(화면에만: 저장하지 않는다).
                window.Session.Editor.SetObjectPlacement(null, "s1", 0f, 0f, 0f, -1f, 1f);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(1.4);
                Shot("flipped-dots");
                window.SelectEmoji("느낌물음표");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.7);
                Shot("flipped-plain");
                window.Session.Editor.Undo();

                // 스킨 칸 — 이모지 오브젝트에 꽃 스킨을 입혀 "씬" 탭에서 본다.
                window.Session.Editor.SetObjectSkin(null, "e1", "행복한벚꽃");
                window.SelectEmoji(null);
                window.Viewport.SelectedObjectId = "e1";
                window.InspectorTab = 1;
                Shot("skin-panel");
                window.Session.Editor.Undo();
                window.InspectorTab = 4;

                // "이모지" 탭 — 그 이모지의 스킨을 그 자리에서 고친다.
                window.SelectEmoji("말줄임점풍선");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(1.4);
                Shot("emoji-tab");

                // 이모지 안에서 머묾의 모션을 고친다 — 그래프에 그 모션이 뜨고 손잡이가 선다.
                window.SelectEmoji("느낌물음표");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.EditEmojiPhase(Mt.Authoring.Preview.EmojiPhase.Hold);
                window.Viewport.ScrubTo(0.4);
                Shot("phase-edit");
                window.EditEmojiPhase(null);

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
