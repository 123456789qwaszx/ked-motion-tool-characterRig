using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 좌우(PLAN-emoji.md E8) — 이모지의 <c>fixedSide</c> · 리그의 <c>upright</c>(런타임 <c>docs/emoji-format.md</c> §6).
/// 모델 · 편집 명령 · 저작본 · 내보내기(E8-2).
/// </summary>
public sealed class EmojiSideTests
{
    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    private static JsonObject Exported(MotionDocument document)
    {
        Assert.True(EmojiExport.TryExport(document, null, out string json, out IReadOnlyList<EmojiProblem> problems), string.Join("\n", problems));
        return JsonNode.Parse(json)!["emojis"]![0]!.AsObject();
    }

    [Fact]
    public void 두_칸은_한_칸씩_기록되고_같은_값은_기록하지_않는다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        long before = editor.Revision;

        editor.SetEmojiFixedSide("star", false); // 이미 거짓
        editor.SetEmojiRigUpright("star", "body", false);
        Assert.Equal(before, editor.Revision);

        editor.SetEmojiFixedSide("star", true);
        Assert.Equal(before + 1, editor.Revision);
        editor.SetEmojiRigUpright("star", "spark", true);
        Assert.Equal(before + 2, editor.Revision);
        editor.SetEmojiRigUpright("star", "spark", true);
        Assert.Equal(before + 2, editor.Revision);

        EmojiAsset star = editor.Document.FindEmoji("star")!;
        Assert.True(star.FixedSide);
        Assert.True(star.FindRig("spark")!.Upright);
        Assert.False(star.FindRig("body")!.Upright);

        editor.Undo();
        Assert.False(editor.Document.FindEmoji("star")!.FindRig("spark")!.Upright);
        editor.Undo();
        Assert.False(editor.Document.FindEmoji("star")!.FixedSide);

        Assert.Throws<KeyNotFoundException>(() => editor.SetEmojiRigUpright("star", "ghost", true));
    }

    [Fact]
    public void 저작본은_참일_때만_적고_다시_읽으며_복제가_가져간다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        string plain = MotionDocumentJson.Write(editor.Document);
        Assert.DoesNotContain("fixedSide", plain); // 거짓이면 글자가 전과 같다
        Assert.DoesNotContain("upright", plain);

        editor.SetEmojiFixedSide("star", true);
        editor.SetEmojiRigUpright("star", "spark", true);
        string text = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"fixedSide\": true", text);
        Assert.Contains("\"upright\": true", text);

        EmojiAsset reread = MotionDocumentJson.Read(text).FindEmoji("star")!;
        Assert.True(reread.FixedSide);
        Assert.True(reread.FindRig("spark")!.Upright);
        Assert.False(reread.FindRig("body")!.Upright);

        EmojiAsset copy = editor.DuplicateEmoji("star", "star_2");
        Assert.True(copy.FixedSide);
        Assert.True(copy.FindRig("spark")!.Upright);
    }

    [Fact]
    public void 내보내기는_참일_때만_적고_런타임_로더가_두_칸을_읽는다()
    {
        MotionEditor editor = EmojiModelTests.StarEditor();
        JsonObject plain = Exported(editor.Document);
        Assert.Null(plain["fixedSide"]);
        Assert.All(plain["rigs"]!.AsArray(), r => Assert.Null(r!["upright"]));

        editor.SetEmojiRigUpright("star", "spark", true);
        JsonObject upright = Exported(editor.Document);
        Assert.Null(upright["fixedSide"]);
        Assert.Null(upright["rigs"]![0]!["upright"]);
        Assert.True(upright["rigs"]![1]!["upright"]!.GetValue<bool>());

        editor.SetEmojiFixedSide("star", true);
        JsonObject sided = Exported(editor.Document);
        Assert.True(sided["fixedSide"]!.GetValue<bool>());

        // 미리보기가 쓰는 정의(코어 로더로 읽은 것)에 두 칸이 실린다.
        EmojiDefinition definition = EmojiPreview.Definition(editor.Document.FindEmoji("star")!, Frame, out string? why)!;
        Assert.Null(why);
        Assert.True(definition.FixedSide);
        Assert.Equal(new[] { false, true }, definition.Rigs.Select(r => r.Upright));
    }
}
