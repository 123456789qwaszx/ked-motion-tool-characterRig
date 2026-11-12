using Avalonia;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

/// <summary>뷰포트의 인물과 고르기(PLAN-ux.md U7) — 이모지가 인물의 머리에 서고, 고른 그림이 그래프와 조작의 대상을 정한다.</summary>
public class EmojiHostShellTests
{
    private static void Run(Action<MainWindow> body) => EmojiExampleWindow.Run(body);

    [Fact]
    public void 이모지는_인물의_머리에_서고_표가_있으면_그_캐릭터의_자리다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            ViewportView viewport = window.Viewport;
            Point feet = viewport.EmojiHostFeetControlPoint()!.Value;
            Point head = viewport.EmojiLocatorControlPoint()!.Value;

            // 표가 없다 — 기본 자리(발에서 800px 위). 화면에서는 위쪽이다.
            Assert.Equal(feet.X, head.X, 1);
            Assert.True(head.Y < feet.Y - 10);
            double perPixel = (feet.Y - head.Y) / EmojiLocatorFile.DefaultPosition.Y;

            // 캐릭터의 항목이 있으면 그 자리 — 보기만 바뀐다(문서는 그대로).
            long before = window.Session.Editor.Revision;
            viewport.SelectedObject!.Character = "seina";
            viewport.EmojiLocators = EmojiLocatorFile.Parse("""{ "MonoBehaviour": { "entries": [ { "character": "seina", "variant": "", "position": { "x": 100.0, "y": 400.0 } } ] } }""");
            Point moved = viewport.EmojiLocatorControlPoint()!.Value;
            Assert.Equal(400 * perPixel, viewport.EmojiHostFeetControlPoint()!.Value.Y - moved.Y, 1);
            Assert.Equal(100 * perPixel, moved.X - viewport.EmojiHostFeetControlPoint()!.Value.X, 1);
            Assert.Equal(before, window.Session.Editor.Revision);

            // 왼쪽을 보는 인물 — 머리의 자리가 발을 축으로 거울에 비친다.
            window.SetEmojiFacingLeftFromForm(true);
            Assert.Equal(-100 * perPixel, viewport.EmojiLocatorControlPoint()!.Value.X - viewport.EmojiHostFeetControlPoint()!.Value.X, 1);
        });
    }

    [Fact]
    public void 인물을_감춰도_이모지의_자리는_그대로이고_목록에_작은_그림이_붙는다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            Point head = window.Viewport.EmojiLocatorControlPoint()!.Value;

            Assert.True(window.Viewport.EmojiShowHost);
            window.SetEmojiShowHostFromForm(false);
            Assert.False(window.Viewport.EmojiShowHost);
            Assert.Equal(head, window.Viewport.EmojiLocatorControlPoint()!.Value);

            Assert.True(window.EmojiRowHasThumb("star"));
            Assert.True(window.EmojiRigRowHasThumb("body"));
        });
    }

    [Fact]
    public void 그림을_고르면_그래프는_그_그림의_것만_보이고_풀면_전부다()
    {
        Run(window =>
        {
            window.SelectEmoji("twinkle"); // 그림 셋(left · center · right) + 이모지 전체의 자리
            Assert.Contains("left · 크기", window.ChannelRowTexts);
            Assert.Contains("right · 크기", window.ChannelRowTexts);
            Assert.Contains("이모지 전체 · 자리", window.ChannelRowTexts);

            window.SelectEmojiRigFromForm("center");
            IReadOnlyList<string> one = window.ChannelRowTexts;
            Assert.Contains("center · 크기", one);
            Assert.Contains("center · 자리", one); // "키 없음" 줄도 그 그림의 것만
            Assert.DoesNotContain(one, t => t.StartsWith("left", StringComparison.Ordinal) || t.StartsWith("right", StringComparison.Ordinal) || t.StartsWith("이모지 전체", StringComparison.Ordinal));
            Assert.All(window.VisibleGraphNodes, n => Assert.StartsWith("center/", n)); // 곡선도 그 그림의 것만
            Assert.NotEmpty(window.VisibleGraphNodes);

            window.SelectEmojiRigsFromForm("left", "right");
            Assert.Contains("left · 크기", window.ChannelRowTexts);
            Assert.Contains("right · 크기", window.ChannelRowTexts);
            Assert.DoesNotContain(window.ChannelRowTexts, t => t.StartsWith("center", StringComparison.Ordinal));

            // 고른 것을 풀면(이모지를 고른 것) 전부로 돌아온다.
            window.SelectEmojiRigFromForm(null);
            Assert.Contains("center · 크기", window.ChannelRowTexts);
            Assert.Contains("이모지 전체 · 자리", window.ChannelRowTexts);
            Assert.Contains(window.VisibleGraphNodes, n => n.StartsWith("left/", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void 뷰포트의_빈_곳을_누르면_전부_풀리고_이모지를_누르면_이모지_전체가_대상이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            ViewportView viewport = window.Viewport;
            MotionEditor editor = window.Session.Editor;

            // 이모지를 고른 것 — 대상은 이모지 전체다(매니퓰레이터가 선다 · 끌면 머리에서의 자리).
            Assert.True(viewport.EmojiWholeSelected);
            Assert.NotNull(viewport.EmojiGizmoForTest());
            Assert.Contains("이모지 전체", viewport.EmojiEditNoteForTest());

            // 빈 곳을 누른다 — 고른 것이 없다. 매니퓰레이터도 없고 S도 아무것도 하지 않는다.
            long before = editor.Revision;
            Assert.True(viewport.DragEmojiForTest(new Point(4, viewport.Bounds.Height - 40), new Point(4, viewport.Bounds.Height - 40)));
            Assert.False(viewport.EmojiWholeSelected);
            Assert.Empty(viewport.SelectedEmojiRigs);
            Assert.Null(viewport.EmojiGizmoForTest());
            Assert.StartsWith("고른 것 없음", viewport.EmojiEditNoteForTest());
            window.SetKeyFromShell();
            Assert.Equal(before, editor.Revision);

            // 그림을 고르면 그 그림, 다시 빈 곳을 누르면 그림도 풀린다 — 이모지 전체로 넘어가지 않는다.
            window.SelectEmojiRigFromForm("body");
            Assert.NotNull(viewport.EmojiGizmoForTest());
            viewport.DragEmojiForTest(new Point(4, viewport.Bounds.Height - 40), new Point(4, viewport.Bounds.Height - 40));
            Assert.Empty(viewport.SelectedEmojiRigs);
            Assert.False(viewport.EmojiWholeSelected);
            Assert.Null(viewport.EmojiGizmoForTest());

            // 왼쪽 목록에서 이모지를 누르면 다시 이모지 전체다.
            window.TapEmojiFromForm();
            Assert.True(viewport.EmojiWholeSelected);
            Assert.NotNull(viewport.EmojiGizmoForTest());
        });
    }

    [Fact]
    public void 그림_파일이_그_경로에_없으면_문제_칸이_알린다_막지는_않는다()
    {
        Run(window =>
        {
            Assert.DoesNotContain(window.Session.EmojiProblems, p => p.Message.Contains("찾지 못했다", StringComparison.Ordinal));

            window.Session.Editor.SetEmojiRigImages("star", "body", new[] { "art/nowhere.png" });
            Mt.Authoring.Validation.EmojiProblem missing = Assert.Single(window.Session.EmojiProblems, p => p.Message.Contains("찾지 못했다", StringComparison.Ordinal));
            Assert.False(missing.Blocking);
            Assert.Contains("art/nowhere.png", missing.Message, StringComparison.Ordinal);
        });
    }
}
