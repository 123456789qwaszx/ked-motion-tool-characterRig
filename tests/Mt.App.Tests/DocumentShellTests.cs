using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Mt.Authoring.Editing;

namespace Mt.App.Tests;

/// <summary>문서의 얼굴 — 창 제목(이름 · 고쳤다는 표시), 저장하지 않은 것을 버리기 전에 묻기, 파일 단축키.</summary>
public class DocumentShellTests
{
    private static void Run(Action<MainWindow, string> body)
    {
        HeadlessUi.Run(() =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"document-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string settings = Path.Combine(directory, "settings.json");
            File.WriteAllText(settings, "{}");
            var window = new MainWindow(settings) { Width = 1280, Height = 800, AsksBeforeDiscard = true };
            window.Show();

            try
            {
                body(window, directory);
            }
            finally
            {
                window.AsksBeforeDiscard = false;
                window.Close();
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void 창_제목이_문서의_이름과_고쳤다는_표시를_보인다()
    {
        Run((window, directory) =>
        {
            MotionEditor editor = window.Session.Editor;
            Assert.Equal("새 문서 — ked-motion-tool-characterRig", window.Title);

            editor.AddMotion("a", "character", 1f);
            Assert.Equal("새 문서 • — ked-motion-tool-characterRig", window.Title);

            string path = Path.Combine(directory, "walk.motion-authoring.json");
            window.Session.Save(path);
            Assert.Equal("walk.motion-authoring.json — ked-motion-tool-characterRig", window.Title);

            editor.SetDuration("a", 2f);
            Assert.EndsWith("• — ked-motion-tool-characterRig", window.Title, StringComparison.Ordinal);

            // 저장하면 표시가 사라진다.
            window.Session.Save();
            Assert.Equal("walk.motion-authoring.json — ked-motion-tool-characterRig", window.Title);
        });
    }

    [Fact]
    public void 고친_것이_없으면_묻지_않고_새_문서가_된다()
    {
        Run((window, _) =>
        {
            window.NewDocumentFromForm();
            Assert.False(window.UnsavedPromptOpen);
        });
    }

    [Fact]
    public void 고친_채로_새로를_누르면_묻고_답대로_한다()
    {
        Run((window, directory) =>
        {
            string path = Path.Combine(directory, "a.motion-authoring.json");
            window.Session.Editor.AddMotion("a", "character", 1f);
            window.Session.Save(path);
            window.Session.Editor.AddMotion("b", "character", 1f);

            // 그만두기 — 문서는 그대로다.
            window.NewDocumentFromForm();
            Assert.True(window.UnsavedPromptOpen);
            window.AnswerUnsavedFromForm("cancel");
            Assert.False(window.UnsavedPromptOpen);
            Assert.NotNull(window.Session.Editor.Document.Find("b"));
            Assert.Equal(path, window.Session.DocumentPath);

            // 저장하고 계속 — 파일에 b가 들어가고 새 문서가 된다.
            window.NewDocumentFromForm();
            window.AnswerUnsavedFromForm("save");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("\"b\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Null(window.Session.DocumentPath);
            Assert.Empty(window.Session.Editor.Document.Motions);
        });
    }

    [Fact]
    public void 저장하지_않고_다른_문서를_열면_고친_것은_버려진다()
    {
        Run((window, directory) =>
        {
            string first = Path.Combine(directory, "first.motion-authoring.json");
            string second = Path.Combine(directory, "second.motion-authoring.json");
            window.Session.Editor.AddMotion("a", "character", 1f);
            window.Session.Save(first);
            window.Session.Save(second);
            window.Session.Editor.AddMotion("lost", "character", 1f);

            window.OpenDocumentAskingFromForm(first);
            Assert.True(window.UnsavedPromptOpen);
            Assert.Equal(second, window.Session.DocumentPath); // 답하기 전에는 그대로

            window.AnswerUnsavedFromForm("discard");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(first, window.Session.DocumentPath);
            Assert.DoesNotContain("lost", File.ReadAllText(second), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void 고친_채로_닫으면_닫기를_멈추고_묻는다()
    {
        Run((window, _) =>
        {
            window.Session.Editor.AddMotion("a", "character", 1f);

            window.Close();
            Assert.True(window.IsVisible);
            Assert.True(window.UnsavedPromptOpen);

            window.AnswerUnsavedFromForm("cancel");
            Assert.True(window.IsVisible);

            window.Close();
            window.AnswerUnsavedFromForm("discard");
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Ctrl_S가_저장하고_Ctrl_N이_새_문서를_묻는다()
    {
        Run((window, directory) =>
        {
            string path = Path.Combine(directory, "a.motion-authoring.json");
            window.Session.Editor.AddMotion("a", "character", 1f);
            window.Session.Save(path);
            window.Session.Editor.AddMotion("b", "character", 1f);
            Assert.True(window.Session.IsDirty);

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.Session.IsDirty);
            Assert.Contains("\"b\"", File.ReadAllText(path), StringComparison.Ordinal);

            window.Session.Editor.AddMotion("c", "character", 1f);
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);
            Assert.True(window.UnsavedPromptOpen);
            window.AnswerUnsavedFromForm("cancel");
        });
    }
}
