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

                // 좌우(E8) — 물음표는 뒤집히지 않는 그림이다. 왼쪽을 보는 인물에게는 반대쪽에 뜨되 글자는 바로 선다.
                window.SetEmojiLifeFromForm(false);
                window.SelectEmoji("question");
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(10));
                window.Viewport.FrameEmoji();
                Shot(window, "emoji-question-right");
                float right = window.Viewport.EmojiRigCenter("mark")!.Value.X;
                window.SetEmojiFacingLeftFromForm(true);
                Assert.Contains("왼쪽을 보는 인물", window.Viewport.EmojiNow()!.Label);
                Assert.NotEqual(right, window.Viewport.EmojiRigCenter("mark")!.Value.X);
                window.Viewport.FrameEmoji();
                Shot(window, "emoji-question-left");
                window.SetEmojiFacingLeftFromForm(false);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>작가의 화면(PLAN-ux.md U1 ~ U9) — 이모지 모드 · 수명의 띠 · 인물 · 그림 하나의 그래프 · 움직임 가져오기 · 새 이모지의 첫 화면.</summary>
    [Fact]
    public void 이모지_모드의_화면을_그림으로_본다()
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
                window.SwitchWorkModeFromForm(Mt.Authoring.Settings.WorkMode.Emoji);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 머무는 이모지 — 띠 셋과 머리에 선 이모지.
                window.SelectEmoji("star");
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(9));
                Assert.Equal(3, window.LifeBands.Count);
                Shot(window, "ux-star");

                // 왼쪽 보기 — 인물까지 뒤집힌다.
                window.SetEmojiFacingLeftFromForm(true);
                Shot(window, "ux-star-left");
                window.SetEmojiFacingLeftFromForm(false);

                // 종류별 칸 — 그림 하나의 크기와 투명도가 제 눈금으로.
                window.SelectEmojiRigFromForm("body");
                Assert.Equal(2, window.Graph.Lanes().Count);
                Shot(window, "ux-star-lanes");
                window.SelectEmojiRigFromForm(null);
                Shot(window, "ux-star-lanes-all");

                // 트윈머신 — 따로 뜨는 창. 두 키 사이에서 30%로 끌어 둔 모습.
                window.SelectEmojiRigFromForm("body");
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(3));
                window.OpenTweenFromForm();
                window.DragTweenFromForm(30);
                Shot(window.TweenWindow!, "ux-tween-window");
                Shot(window, "ux-tween-dragging");
                window.ReleaseTweenFromForm();
                window.CloseTweenFromForm();
                window.Session.Editor.Undo();
                window.SelectEmojiRigFromForm(null);

                // 그림 하나를 고르면 그래프는 그 그림의 것만.
                window.SelectEmoji("twinkle");
                window.SelectEmojiRigFromForm("center");
                Shot(window, "ux-twinkle-one-picture");
                window.SelectEmojiRigFromForm(null);

                // 새 이모지의 첫 화면 → 만들기.
                window.OpenNewEmojiFromForm();
                window.FillNewEmojiFromForm("wow", new[] { Path.Combine(RepoRoot(), "examples", "emoji", "art", "star.png") }, true, EmojiMove.PopIn, EmojiMove.Pulse, EmojiMove.ShrinkOut);
                Shot(window.NewEmojiWindow!, "ux-new-emoji-form");
                Assert.Equal("wow", window.CreateNewEmojiFromForm());
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(5));
                Shot(window, "ux-new-emoji-made");

                // 움직임 가져오기 — 그림을 골라 머묾에 흔들기를 더한다.
                window.SelectEmojiRigFromForm("star");
                window.ImportMoveFromForm(EmojiMove.Shake);
                window.Viewport.ScrubTo(EmojiAsset.SecondsOf(11));
                Shot(window, "ux-move-imported");
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
