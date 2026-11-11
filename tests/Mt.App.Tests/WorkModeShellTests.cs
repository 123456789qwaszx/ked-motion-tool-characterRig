using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Mt.Authoring.Editing;
using Mt.Authoring.Settings;

namespace Mt.App.Tests;

/// <summary>작업 모드(PLAN-ux.md U1) — 캐릭터 모션 / 이모지. 화면이 그 일에 쓰는 것만 보이고, 모드는 시간의 주인을 따라간다.</summary>
public class WorkModeShellTests
{
    private static readonly string[] MotionTabs = { "채널 박스", "씬", "모션", "클립" };
    private static readonly string[] EmojiTabs = { "이모지" };

    private static string NewSettings(string json = "{}")
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static void Run(Action<MainWindow> body, string settingsJson = "{}")
    {
        HeadlessUi.Run(() =>
        {
            var window = new MainWindow(NewSettings(settingsJson)) { Width = 1600, Height = 960 };
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                body(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 처음은_캐릭터_모션_모드이고_이모지의_것은_보이지_않는다()
    {
        Run(window =>
        {
            Assert.Equal(WorkMode.Motion, window.Mode);

            var state = window.WorkModeFormState;
            Assert.True(state.MotionLeft);
            Assert.False(state.EmojiLeft);
            Assert.Equal(MotionTabs, state.Tabs);
            Assert.Equal(new[] { "motions.json 가져오기…", "motions.json 내보내기…" }, state.Exports);
        });
    }

    [Fact]
    public void 이모지_모드에서는_이모지의_것만_보인다()
    {
        Run(window =>
        {
            window.SwitchWorkModeFromForm(WorkMode.Emoji);

            Assert.Equal(WorkMode.Emoji, window.Mode);

            var state = window.WorkModeFormState;
            Assert.False(state.MotionLeft);
            Assert.True(state.EmojiLeft);
            Assert.Equal(EmojiTabs, state.Tabs);
            Assert.Equal(new[] { "emojis.json 내보내기…" }, state.Exports);
            Assert.Equal(4, window.InspectorTab);
        });
    }

    [Fact]
    public void 이모지를_고르거나_만들면_이모지_모드가_되고_모션을_고르면_돌아온다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            editor.AddMotion("nod", "character", 0.6f);

            window.AddEmojiFromForm();
            Assert.Equal(WorkMode.Emoji, window.Mode);

            window.SelectMotion("nod");
            Assert.Equal(WorkMode.Motion, window.Mode);
            Assert.Null(window.SelectedEmoji);

            window.SelectEmoji("emoji");
            Assert.Equal(WorkMode.Emoji, window.Mode);

            string clip = window.AddClipFromForm();
            window.SelectClip(clip);
            Assert.Equal(WorkMode.Motion, window.Mode);
        });
    }

    [Fact]
    public void 툴바에서_모드를_바꾸면_반대쪽의_선택이_풀린다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            editor.AddMotion("nod", "character", 0.6f);
            window.AddEmojiFromForm();
            window.SelectMotion("nod");

            // 이모지 모드로 — 모션이 풀리고, 고른 이모지가 없으니 첫 이모지를 고른다.
            window.SwitchWorkModeFromForm(WorkMode.Emoji);
            Assert.Null(window.SelectedMotion);
            Assert.Equal("emoji", window.SelectedEmoji);
            Assert.Equal("emoji:emoji", window.Graph.MotionName);

            // 캐릭터 모션 모드로 — 이모지가 풀린다. 가운데에 숨은 목록의 것이 남지 않는다.
            window.SwitchWorkModeFromForm(WorkMode.Motion);
            Assert.Null(window.SelectedEmoji);
            Assert.Null(window.Viewport.EmojiName);
        });
    }

    [Fact]
    public void 모드를_바꾸는_것은_문서를_고치지_않는다()
    {
        Run(window =>
        {
            window.AddEmojiFromForm();
            long revision = window.Session.Editor.Revision;

            window.SwitchWorkModeFromForm(WorkMode.Motion);
            window.SwitchWorkModeFromForm(WorkMode.Emoji);
            window.SwitchWorkModeFromForm(WorkMode.Emoji);

            Assert.Equal(revision, window.Session.Editor.Revision);
        });
    }

    [Fact]
    public void 캐릭터_모션_모드의_마지막_탭은_이모지_모드를_다녀와도_남는다()
    {
        Run(window =>
        {
            window.InspectorTab = 2;
            Assert.Equal(2, window.Settings.InspectorTab);

            window.SwitchWorkModeFromForm(WorkMode.Emoji);
            Assert.Equal(4, window.InspectorTab);
            Assert.Equal(2, window.Settings.InspectorTab); // 이모지 모드의 탭은 남기지 않는다

            window.SwitchWorkModeFromForm(WorkMode.Motion);
            Assert.Equal(2, window.InspectorTab);
        });
    }

    [Fact]
    public void 모드는_설정에_남아_다음에_그_모드로_열린다()
    {
        Run(window =>
        {
            Assert.Equal(WorkMode.Emoji, window.Mode);
            Assert.Equal(EmojiTabs, window.WorkModeFormState.Tabs);

            window.SwitchWorkModeFromForm(WorkMode.Motion);
            Assert.Equal(WorkMode.Motion, window.Settings.WorkMode);
        }, "{ \"workMode\": \"emoji\" }");
    }

    [Fact]
    public void 예시_문서를_두_모드로_본다()
    {
        Run(window =>
        {
            window.Session.Open(Path.Combine(RepoRoot(), "examples", "emoji", "emoji-examples.motion-authoring.json"));
            Shot(window, "mode-motion");

            window.SwitchWorkModeFromForm(WorkMode.Emoji);
            Assert.Equal("question", window.SelectedEmoji);
            window.SelectEmoji("star");
            window.Viewport.ScrubTo(Mt.Authoring.Model.EmojiAsset.SecondsOf(9));
            Shot(window, "mode-emoji");
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
