using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Mt.Authoring.Editing;
using Mt.Authoring.Settings;

namespace Mt.App.Tests;

/// <summary>작업 모드(PLAN-ux.md U1) — 캐릭터 모션 / 이모지. 화면이 그 일에 쓰는 것만 보이고, 모드는 시간의 주인을 따라간다.</summary>
public class WorkModeShellTests
{
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
            Assert.True(state.Tabs);
            Assert.False(state.EmojiPane);
            Assert.Equal(4, window.InspectorTabCount); // 채널 박스 · 씬 · 모션 · 클립 — 이모지의 칸은 탭이 아니다
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
            Assert.False(state.Tabs);
            Assert.True(state.EmojiPane);
            Assert.Equal(new[] { "emojis.json 내보내기…" }, state.Exports);
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
            Assert.False(window.WorkModeFormState.Tabs);
            Assert.Equal(2, window.Settings.InspectorTab);

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
            Assert.True(window.WorkModeFormState.EmojiPane);

            window.SwitchWorkModeFromForm(WorkMode.Motion);
            Assert.Equal(WorkMode.Motion, window.Settings.WorkMode);
        }, "{ \"workMode\": \"emoji\" }");
    }

    // ── 고른 것이 오른쪽을 정한다 (PLAN-ux.md U2) ───────────────────

    [Fact]
    public void 이모지를_고르면_이모지의_값을_리그를_고르면_리그의_값을_보인다()
    {
        Run(window =>
        {
            window.AddEmojiFromForm();

            // 이모지를 골랐다 — 리그는 저절로 골라지지 않는다.
            var state = window.EmojiInspectorState;
            Assert.True(state.EmojiFields);
            Assert.False(state.RigFields);
            Assert.False(state.Back);
            Assert.Equal("emoji — 그림 1장 · 25프레임 · 한 번 재생하고 사라진다", state.Title);
            Assert.Null(window.Viewport.SelectedEmojiRig);
            Assert.Equal("이모지 전체", window.EmojiKeyTargetChoice);

            window.SelectEmojiRigFromForm("body");

            state = window.EmojiInspectorState;
            Assert.False(state.EmojiFields);
            Assert.True(state.RigFields);
            Assert.True(state.Back);
            Assert.Equal("그림 body — emoji", state.Title);
            Assert.Equal("body", window.Viewport.SelectedEmojiRig);
            Assert.Equal("body", window.EmojiKeyTargetChoice); // 키를 놓을 대상이 고른 리그를 따라온다

            // 리그 선택을 풀면 이모지의 값으로 돌아온다.
            window.SelectEmojiRigFromForm(null);
            Assert.True(window.EmojiInspectorState.EmojiFields);
            Assert.Equal("이모지 전체", window.EmojiKeyTargetChoice);
        });
    }

    [Fact]
    public void 다른_이모지로_옮기면_리그_선택을_이어받지_않는다()
    {
        Run(window =>
        {
            window.AddEmojiFromForm();   // emoji — 리그 body
            window.AddEmojiFromForm();   // emoji_2 — 리그 body(이름이 같다)
            window.SelectEmojiRigFromForm("body");
            Assert.True(window.EmojiInspectorState.RigFields);

            window.SelectEmoji("emoji");

            Assert.True(window.EmojiInspectorState.EmojiFields);
            Assert.Null(window.Viewport.SelectedEmojiRig);
        });
    }

    [Fact]
    public void 리그를_고친_뒤에도_리그의_값을_보이고_리그를_지우면_이모지로_돌아온다()
    {
        Run(window =>
        {
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");

            window.CommitEmojiField("rigScale", "0.5");
            Assert.True(window.EmojiInspectorState.RigFields);

            window.CommitEmojiField("rigName", "mark");
            Assert.Equal("그림 mark — emoji", window.EmojiInspectorState.Title);

            window.Session.Editor.RemoveEmojiRig("emoji", "mark");
            Assert.True(window.EmojiInspectorState.EmojiFields);
        });
    }

    [Fact]
    public void 왼쪽의_리그_묶음은_이모지를_골랐을_때만_보이고_줄마다_둘째_줄이_있다()
    {
        Run(window =>
        {
            window.SwitchWorkModeFromForm(WorkMode.Emoji);
            Assert.False(window.EmojiRigSectionVisible);

            window.Session.Open(Path.Combine(RepoRoot(), "examples", "emoji", "emoji-examples.motion-authoring.json"));
            window.SelectEmoji("star");

            Assert.True(window.EmojiRigSectionVisible);
            Assert.Equal("머무는 이모지", window.EmojiRowNote("star"));
            Assert.Equal("한 번 재생하고 사라진다", window.EmojiRowNote("question"));
            Assert.Equal("star.png", window.EmojiRigRowNote("body"));
        });
    }

    // ── 화면의 말 (PLAN-ux.md U3) ────────────────────────────────

    [Fact]
    public void 그래프_목록은_이모지의_타임라인을_만드는_사람의_말로_적는다()
    {
        Run(window =>
        {
            window.Session.Open(Path.Combine(RepoRoot(), "examples", "emoji", "emoji-examples.motion-authoring.json"));
            window.SelectEmoji("star");

            IReadOnlyList<string> texts = window.ChannelRowTexts;

            Assert.Contains("body · 크기", texts);
            Assert.Contains("크기 X", texts);
            Assert.Contains("body · 투명도", texts);
            Assert.Contains("이모지 전체 · 투명도", texts);
            Assert.Contains("투명도", texts);               // 성분이 하나뿐이면 축을 붙이지 않는다
            Assert.Contains("이모지 전체 · 자리", texts);    // "키 없음" 줄
            Assert.Contains("body · 그림", texts);
            Assert.DoesNotContain(texts, t => t.Contains("EmojiRig_Image") || t.Contains("Emoji_Offset") || t.Contains("alpha") || t.Contains("scale"));
        });
    }

    [Fact]
    public void 캐릭터_모션의_그래프_목록은_전과_같다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            editor.AddMotion("nod", "character", 0.6f);
            editor.KeyComponents("nod", "CharacterPortrait_Track_Y", Mt.Authoring.Model.ChannelKind.Position, 0f,
                new[] { (Mt.Authoring.Model.Axis.Y, 1f) }, Mt.Authoring.Model.TangentMode.Flat);
            window.SelectMotion("nod");

            Assert.Contains(window.ChannelRowTexts, t => t.StartsWith("position", StringComparison.Ordinal));
        });
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

            window.SelectEmojiRigFromForm("body");
            Shot(window, "mode-emoji-rig");

            // 도구와 매니퓰레이터(PLAN-ux U4) — 회전 고리와 Auto Key.
            window.PickToolFromStrip(Mt.Authoring.Preview.ManipulatorMode.Rotate);
            window.SetAutoKeyFromStrip(true);
            Shot(window, "mode-emoji-rotate");
            window.PickToolFromStrip(Mt.Authoring.Preview.ManipulatorMode.Move);
            window.SetAutoKeyFromStrip(false);
            Shot(window, "mode-emoji-move");

            // 실제로 그릴 때만(Skia) 그림의 픽셀을 읽을 수 있다 — 별의 귀퉁이는 비어 있어 눌러도 맞지 않고, 가운데는 맞는다.
            if (RenderShots.Directory is not null)
            {
                Avalonia.Rect box = window.Viewport.EmojiControlBox()!.Value;
                Assert.Empty(window.Viewport.EmojiRigsAtControlForTest(new Avalonia.Point(box.X + box.Width * 0.06, box.Y + box.Height * 0.06)));
                Assert.Equal(new[] { "body" }, window.Viewport.EmojiRigsAtControlForTest(box.Center));
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
