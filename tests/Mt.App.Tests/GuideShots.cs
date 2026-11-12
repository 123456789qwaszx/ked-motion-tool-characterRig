using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

/// <summary>
/// 그림 설명서(<c>docs/guide</c>)의 캐릭터 모션 장면 — <c>MT_RENDER_DIR</c>이 있으면 그림 파일(<c>guide-*</c>)로 뽑는다.
/// 없으면 장면을 세우고 설명서가 말하는 상태인지만 본다. 이모지의 장면은 <see cref="EmojiShots"/>가 낸다.
/// </summary>
public class GuideShots
{
    [Fact]
    public void 캐릭터_모션의_화면을_그림으로_본다()
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

                // 그림을 뽑을 때만 초상을 세운다(설명서용으로 그린 그림) — 보통의 실행은 PNG를 읽지 못한다.
                if (RenderShots.Enabled)
                {
                    window.Session.SetPortrait("s1", Path.Combine(EmojiExampleWindow.RepoRoot(), "docs", "guide", "art", "portrait.png"));
                }

                // 프리셋 둘 — 숙임과 끄덕. 끄덕을 고르고 몸 컨트롤러를 잡은 모습.
                Assert.Equal("bow", window.AddPresetFromForm("bow"));
                Assert.Equal("nod", window.AddPresetFromForm("nod"));
                window.SelectMotion("nod");
                window.SelectControllerInHierarchy("s1", "body");
                window.PickToolFromStrip(Mt.Authoring.Preview.ManipulatorMode.Move);
                window.Viewport.ScrubTo(6 / 24.0);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("nod", window.Viewport.MotionName);
                Shot(window, "guide-motion");

                // 도프 시트 · 씬 탭.
                window.ShowTabsFromForm(editor: 1, inspector: 1);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Shot(window, "guide-dope-scene");
                window.ShowTabsFromForm(editor: 0, inspector: 0);

                // 트윈머신 — 두 키 사이에서 30%.
                window.Viewport.ScrubTo(3 / 24.0);
                window.OpenTweenFromForm();
                window.DragTweenFromForm(30);
                Shot(window.TweenWindow!, "guide-tween-window");
                Shot(window, "guide-tween");
                window.ReleaseTweenFromForm();
                window.CloseTweenFromForm();
                editor.Undo();

                // 문제 칸 — 길이를 줄여 마지막 키가 길이를 넘었다.
                editor.SetDuration("nod", 0.4f);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.ProblemRowsShown > 0);
                Shot(window, "guide-problems");
                editor.Undo();

                // 클립 — 한 캐릭터에 숙임과 끄덕을 겹친다(배역 둘이 같은 s1).
                window.AddClipFromForm();
                window.Viewport.SelectedObjectId = "s1";
                window.AddClipRoleFromForm();
                window.AddClipRoleFromForm();
                editor.AddClipBlock("clip", 0, "bow", 0f);
                editor.AddClipBlock("clip", 1, "nod", 0.25f);
                window.ShowTabsFromForm(inspector: 3);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Timeline.Select((1, 0));
                window.Viewport.ScrubTo(0.5);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("nod", window.Graph.MotionName);
                Shot(window, "guide-clip");
            }
            finally
            {
                window.Close();
            }
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
}
