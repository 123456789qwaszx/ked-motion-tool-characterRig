using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

/// <summary>스킨과 조각(<c>PLAN-parts.md</c> P1) — 리그의 조각마다 붙일 그림의 묶음.</summary>
public class SkinTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));

    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);
        return editor;
    }

    [Fact]
    public void 조각은_이름_규칙으로_가리고_캐릭터에는_없다()
    {
        Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, RigParts.Of(RigFile, EmojiRigFixture.RigKind));
        Assert.Empty(RigParts.Of(RigFile, "character"));
        Assert.Empty(RigParts.Of(RigFile, "nope"));
        Assert.Empty(RigParts.Of(null, EmojiRigFixture.RigKind));
        Assert.True(RigParts.IsPartNode("Emoji_Part12"));
        Assert.False(RigParts.IsPartNode("Emoji_Body"));
        Assert.False(RigParts.IsPartNode("Part1_Root"));

        // 조각 노드는 리그의 보통 노드다 — 씬에 서고, 모션의 채널이 된다.
        RectNodeTree tree = ScenePreview.Build(RigFile, NewEditor().Document.ActiveSceneAsset!);
        Assert.True(tree.Contains("e1/Emoji_Part3"));
        Assert.Equal(EmojiRigFixture.PartSize, tree.GetRectSize("e1/Emoji_Part1").X, 3);
        Assert.Contains("Emoji_Part1", RigFile.NodeIds[EmojiRigFixture.RigKind]);
    }

    [Fact]
    public void 스킨은_저작본에_왕복하고_없으면_적지_않는다()
    {
        MotionEditor editor = NewEditor();
        Assert.DoesNotContain("skins", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);

        editor.AddSkin("꽃", EmojiRigFixture.RigKind);
        editor.SetSkinPart("꽃", "Emoji_Part2", "art/꽃-작은.png");
        editor.SetSkinPart("꽃", "Emoji_Part1", "art/꽃-큰.png");
        editor.SetObjectSkin(null, "e1", "꽃");

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal(json, MotionDocumentJson.Write(read));
        SkinAsset skin = Assert.Single(read.Skins);
        Assert.Equal(("꽃", EmojiRigFixture.RigKind), (skin.Name, skin.Rig));
        Assert.Equal(new[] { ("Emoji_Part1", "art/꽃-큰.png"), ("Emoji_Part2", "art/꽃-작은.png") }, skin.Parts.Select(p => (p.Key, p.Value))); // 노드 이름순
        Assert.Equal("꽃", read.ActiveSceneAsset!.Find("e1")!.Skin);
        Assert.Null(read.ActiveSceneAsset.Find("s1")!.Skin);
    }

    [Fact]
    public void 명령은_한_칸이고_이름을_바꾸거나_지우면_입은_오브젝트가_따라온다()
    {
        MotionEditor editor = NewEditor();
        editor.AddSceneObject(null, "e2", EmojiRigFixture.RigKind);
        editor.AddSkin("  sweat ", EmojiRigFixture.RigKind); // 앞뒤 빈칸은 버린다
        editor.SetObjectSkin(null, "e1", "sweat");
        editor.SetObjectSkin(null, "e2", "sweat");
        SceneAsset Scene() => editor.Document.ActiveSceneAsset!;

        long revision = editor.Revision;
        editor.RenameSkin("sweat", "땀");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { "땀", "땀" }, new[] { Scene().Find("e1")!.Skin, Scene().Find("e2")!.Skin });
        editor.Undo();
        Assert.Equal("sweat", Scene().Find("e1")!.Skin);
        Assert.NotNull(editor.Document.FindSkin("sweat"));
        editor.Redo();

        // 조각 그림: 넣고, 같은 값은 흔적이 없고, 비우면 빠진다.
        editor.SetSkinPart("땀", "Emoji_Part1", "a.png");
        revision = editor.Revision;
        editor.SetSkinPart("땀", "Emoji_Part1", "a.png");
        editor.SetSkinPart("땀", "Emoji_Part3", "");
        Assert.Equal(revision, editor.Revision);
        editor.SetSkinPart("땀", "Emoji_Part1", null);
        Assert.Empty(editor.Document.FindSkin("땀")!.Parts);

        // 복제하면 스킨도 같이 입는다.
        Assert.Equal("땀", editor.DuplicateSceneObject(null, "e1").Skin);

        // 지우면 입은 오브젝트가 같은 칸에서 벗는다 — 되돌리면 다시 입는다.
        editor.RemoveSkin("땀");
        Assert.All(Scene().Objects, o => Assert.Null(o.Skin));
        editor.Undo();
        Assert.Equal("땀", Scene().Find("e2")!.Skin);
    }

    [Fact]
    public void 이름과_리그가_안_맞으면_막는다()
    {
        MotionEditor editor = NewEditor();
        editor.AddSkin("a", EmojiRigFixture.RigKind);
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.AddSkin("a", EmojiRigFixture.RigKind));
        Assert.Throws<InvalidOperationException>(() => editor.AddSkin("  ", EmojiRigFixture.RigKind));
        Assert.Throws<InvalidOperationException>(() => editor.AddSkin("b", ""));
        Assert.Throws<KeyNotFoundException>(() => editor.SetObjectSkin(null, "e1", "nope"));
        Assert.Throws<InvalidOperationException>(() => editor.SetObjectSkin(null, "s1", "a")); // 캐릭터에 이모지 스킨
        Assert.Throws<KeyNotFoundException>(() => editor.SetSkinPart("nope", "Emoji_Part1", "x.png"));
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPart("a", "", "x.png"));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveSkin("nope"));
        Assert.Equal(revision, editor.Revision);

        editor.RenameSkin("a", " a "); // 같은 이름 — 아무 일도 없다
        Assert.Equal(revision, editor.Revision);
    }
}
