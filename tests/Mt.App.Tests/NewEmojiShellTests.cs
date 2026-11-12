using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

/// <summary>새 이모지의 첫 화면(PLAN-ux.md U9).</summary>
public class NewEmojiShellTests
{
    private static void Run(Action<MainWindow> body) => EmojiExampleWindow.Run(body);

    [Fact]
    public void 첫_화면에서_그림과_수명과_움직임을_정하면_움직이는_이모지가_한_칸에_생긴다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            string art = Path.Combine(EmojiExampleWindow.RepoRoot(), "examples", "emoji", "art");

            window.OpenNewEmojiFromForm();
            Assert.True(window.NewEmojiOpen);
            Assert.Equal("emoji", window.NewEmojiFormState.Name);
            Assert.Contains("빈 그림 body 하나", window.NewEmojiFormState.Summary);
            Assert.Contains("등장 튀어나오기", window.NewEmojiFormState.Summary); // 처음 골라져 있는 움직임

            // PNG만 받는다 — 같은 파일은 한 번.
            window.FillNewEmojiFromForm("wow", new[] { Path.Combine(art, "star.png"), Path.Combine(art, "star.png"), Path.Combine(art, "notes.txt") }, true, EmojiMove.PopIn, EmojiMove.Pulse, null);
            Assert.Equal(new[] { "star.png" }, window.NewEmojiFormState.Images);
            Assert.Equal("만들기 → 그림 1장 · 머무는 이모지 · 등장 튀어나오기 · 머묾 두근거리기 · 길이 1초(24프레임 — 뒤에 바꾼다)", window.NewEmojiFormState.Summary);

            long before = editor.Revision;
            Assert.Equal("wow", window.CreateNewEmojiFromForm());
            Assert.Equal(before + 1, editor.Revision);
            Assert.False(window.NewEmojiOpen);

            // 만든 이모지가 골라지고, 띠 · 목록 · 그림이 따라온다.
            Assert.Equal("wow", window.SelectedEmoji);
            EmojiAsset wow = editor.Document.FindEmoji("wow")!;
            Assert.Equal("art/star.png", Assert.Single(Assert.Single(wow.Rigs).Images)); // 문서 기준 경로
            Assert.Equal(new[] { new LifeBand("in", 0, 7, EmojiLifeRole.In), new LifeBand("hold", 8, 24, EmojiLifeRole.Hold) }, window.LifeBands);
            Assert.Equal("머무는 이모지", window.EmojiRowNote("wow"));
            Assert.True(window.EmojiRowHasThumb("wow"));
            Assert.DoesNotContain(Mt.Authoring.Validation.EmojiValidator.Validate(editor.Document), p => p.ToString().Contains("wow"));

            // 이름이 겹치면 만들지 않고 창이 남는다.
            window.OpenNewEmojiFromForm();
            window.FillNewEmojiFromForm("wow", Array.Empty<string>(), false, null, null, null);
            Assert.Null(window.CreateNewEmojiFromForm());
            Assert.True(window.NewEmojiOpen);
            Assert.Equal(before + 1, editor.Revision);

            // 그림의 순서 — ▲ ▼로 바꾸고, 그 순서로 그림이 생긴다(아래가 위). 끝에서는 그대로다.
            window.FillNewEmojiFromForm("stack", new[] { Path.Combine(art, "star.png"), Path.Combine(art, "question.png"), Path.Combine(art, "spark.png") }, false, null, null, null);
            Assert.Equal(new[] { "star.png", "question.png", "spark.png" }, window.NewEmojiFormState.Images);
            window.MoveNewEmojiImageFromForm(2, -1);
            window.MoveNewEmojiImageFromForm(0, -1); // 맨 위에서 위로 — 그대로
            window.MoveNewEmojiImageFromForm(0, 1);
            Assert.Equal(new[] { "spark.png", "star.png", "question.png" }, window.NewEmojiFormState.Images);
            Assert.Equal("stack", window.CreateNewEmojiFromForm());
            Assert.Equal(new[] { "spark", "star", "question" }, editor.Document.FindEmoji("stack")!.Rigs.Select(r => r.Name));
            Assert.Equal(before + 2, editor.Revision);
            window.OpenNewEmojiFromForm();

            // 빈 이모지로 시작 — 전과 같은 것(빈 그림 body 하나).
            string blank = window.StartBlankEmojiFromForm();
            Assert.False(window.NewEmojiOpen);
            Assert.Equal("body", Assert.Single(editor.Document.FindEmoji(blank)!.Rigs).Name);
            Assert.Empty(editor.Document.FindEmoji(blank)!.Timeline.Channels);
        });
    }
}
