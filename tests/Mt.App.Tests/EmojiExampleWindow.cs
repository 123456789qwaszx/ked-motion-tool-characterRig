using Mt.Authoring.Settings;

namespace Mt.App.Tests;

/// <summary>예시 이모지 문서를 이모지 모드로 연 창 — 이모지 셸 테스트들이 같이 쓴다.</summary>
internal static class EmojiExampleWindow
{
    internal static void Run(Action<MainWindow> body)
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

    internal static string RepoRoot()
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
