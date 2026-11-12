using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>수명의 띠가 쓰는 셈(PLAN-ux.md U5) — 새 구간이 놓일 자리 · 한 칸에 놓고 잇기. 그리고 보기에만 읽는 로케이터 표(U7).</summary>
public class EmojiLifeTests
{
    private static MotionEditor Blank(float seconds = 1f)
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddEmoji("pop", seconds); // 0~24fr
        editor.AddEmojiRig("pop", "body", new[] { "a.png" });
        return editor;
    }

    [Fact]
    public void 이웃이_없으면_타임라인을_셋으로_나눈_제_몫에_놓는다()
    {
        EmojiAsset pop = Blank().Document.FindEmoji("pop")!;

        Assert.Equal(24, pop.LastFrame);
        Assert.Equal((0, 7), EmojiLife.Suggest(pop, EmojiLifeRole.In));
        Assert.Equal((8, 16), EmojiLife.Suggest(pop, EmojiLifeRole.Hold));
        Assert.Equal((17, 24), EmojiLife.Suggest(pop, EmojiLifeRole.Out));
    }

    [Fact]
    public void 이웃이_있으면_그_사이의_빈_곳을_채운다()
    {
        MotionEditor editor = Blank();
        editor.AddEmojiSpan("pop", "loop", 6, 15);
        editor.SetEmojiLinks("pop", null, "loop", null);
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;

        Assert.Equal((0, 5), EmojiLife.Suggest(pop, EmojiLifeRole.In));
        Assert.Equal((16, 24), EmojiLife.Suggest(pop, EmojiLifeRole.Out));

        // 빈 곳이 없으면 타임라인 안의 한 프레임 — 막지 않는다(끌어서 고친다).
        editor.SetEmojiSpan("pop", "loop", 0, 24);
        Assert.Equal((0, 0), EmojiLife.Suggest(pop, EmojiLifeRole.In));
        Assert.Equal((24, 24), EmojiLife.Suggest(pop, EmojiLifeRole.Out));
    }

    [Fact]
    public void 자리에_구간을_놓고_잇는_것은_한_칸이다()
    {
        MotionEditor editor = Blank();
        long before = editor.Revision;

        EmojiSpan hold = editor.AddEmojiLifeSpan("pop", EmojiLifeRole.Hold);
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;

        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal("hold", hold.Name);
        Assert.Equal("hold", pop.Hold);
        Assert.True(pop.Stays);
        Assert.Equal(EmojiLifeRole.Hold, EmojiLife.RoleOf(pop, "hold"));

        editor.AddEmojiLifeSpan("pop", EmojiLifeRole.In);
        editor.AddEmojiLifeSpan("pop", EmojiLifeRole.Out);
        Assert.Equal((0, 7), (pop.FindSpan(pop.In)!.Start, pop.FindSpan(pop.In)!.End));
        Assert.Equal((17, 24), (pop.FindSpan(pop.Out)!.Start, pop.FindSpan(pop.Out)!.End));
        Assert.Empty(Mt.Authoring.Validation.EmojiValidator.Validate(editor.Document));

        editor.Undo();
        editor.Undo();
        editor.Undo();
        pop = editor.Document.FindEmoji("pop")!; // 되돌리기는 문서를 갈아 끼운다
        Assert.Empty(pop.Spans);
        Assert.Null(pop.Hold);
    }

    [Fact]
    public void 이미_이은_자리에는_놓지_않고_이름이_겹치면_번호를_붙인다()
    {
        MotionEditor editor = Blank();
        editor.AddEmojiSpan("pop", "hold", 0, 3); // 잇지 않은 구간이 이름을 먼저 썼다
        EmojiSpan added = editor.AddEmojiLifeSpan("pop", EmojiLifeRole.Hold);

        Assert.Equal("hold2", added.Name);
        Assert.Throws<InvalidOperationException>(() => editor.AddEmojiLifeSpan("pop", EmojiLifeRole.Hold));
    }

    [Fact]
    public void 수명을_한_줄로_적는다()
    {
        MotionEditor editor = Blank();
        EmojiAsset pop = editor.Document.FindEmoji("pop")!;
        Assert.StartsWith("한 번 재생하고 사라진다 — 0~24fr", EmojiLife.Describe(pop));

        editor.AddEmojiLifeSpan("pop", EmojiLifeRole.Hold);
        Assert.Equal("머무는 이모지 — 등장 없이 바로 머묾 되풀이 → 내리면 바로 사라진다", EmojiLife.Describe(pop));

        editor.AddEmojiLifeSpan("pop", EmojiLifeRole.In);
        editor.AddEmojiLifeSpan("pop", EmojiLifeRole.Out);
        Assert.Equal("머무는 이모지 — 등장 한 번 → 머묾 되풀이 → 내리면(emoji_off) 퇴장", EmojiLife.Describe(pop));
    }

    [Fact]
    public void 로케이터_표는_런타임과_같은_순서로_찾는다()
    {
        EmojiLocatorFile table = EmojiLocatorFile.Parse("""
            { "MonoBehaviour": { "m_Name": "EmojiLocatorTuningDB", "entries": [
                { "character": "Seina", "variant": "", "position": { "x": 20.0, "y": 760.0 } },
                { "character": "seina", "variant": "sit", "position": { "x": 0.0, "y": 520.0 } }
            ] } }
            """);

        Assert.Equal(2, table.Count);
        Assert.Equal((20f, 760f), (table.Resolve("seina").X, table.Resolve("seina").Y));        // 대소문자를 가리지 않는다
        Assert.Equal(520f, table.Resolve("SEINA", "sit").Y);                                      // 포즈 항목이 이긴다
        Assert.Equal(760f, table.Resolve("seina", "stand").Y);                                    // 포즈 항목이 없으면 캐릭터 항목
        Assert.False(table.TryResolve("harin", null, out var fallback));                          // 없으면 기본 자리
        Assert.Equal(EmojiLocatorFile.DefaultPosition, fallback);
        Assert.Equal(EmojiLocatorFile.DefaultPosition, EmojiLocatorFile.Empty.Resolve(null));
        Assert.Equal(0, EmojiLocatorFile.Parse("""{ "MonoBehaviour": { "entries": [] } }""").Count); // 런타임이 지금 내는 빈 표
    }
}
