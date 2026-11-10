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

    // ── 조각의 덧붙임(PLAN-parts.md §13) — 그림 넘기기 · 놓임 · 반전 판 ────

    [Fact]
    public void 조각에_장을_여럿_주면_장당_시간마다_넘어가고_되풀이하거나_마지막에서_멈춘다()
    {
        MotionEditor editor = NewEditor();
        editor.AddSkin("땀", EmojiRigFixture.RigKind);
        long revision = editor.Revision;

        // 장들을 한 번에 — 한 칸. 첫 장이 그 조각의 그림이다. 빈 것은 버린다.
        editor.SetSkinPartImages("땀", "Emoji_Part1", new[] { " a.png ", "b.png", "", "c.png" });
        editor.SetSkinPartImages("땀", "Emoji_Part1", new[] { "a.png", "b.png", "c.png" }); // 같은 값은 무기록
        Assert.Equal(revision + 1, editor.Revision);
        SkinAsset skin = editor.Document.FindSkin("땀")!;
        Assert.Equal("a.png", skin.Parts["Emoji_Part1"]);
        Assert.Equal(new[] { "a.png", "b.png", "c.png" }, skin.ImagesOf("Emoji_Part1"));
        Assert.Equal(new[] { "a.png", "b.png", "c.png" }, skin.AllImages());

        // 기본: 장당 0.2초, 되풀이.
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", 0));
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", 0.19));
        Assert.Equal("b.png", skin.ImageAt("Emoji_Part1", 0.2));
        Assert.Equal("c.png", skin.ImageAt("Emoji_Part1", 0.45));
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", 0.6));
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", -1)); // 뜨기 전
        Assert.Null(skin.ImageAt("Emoji_Part2", 0));

        // 장당 시간과 되풀이 — 끄면 마지막 장에서 멈춘다.
        SkinPartSetup setup = skin.SetupOf("Emoji_Part1").Clone();
        setup.FrameSeconds = 0.5f;
        setup.Loop = false;
        editor.SetSkinPartSetup("땀", "Emoji_Part1", setup);
        Assert.Equal("b.png", skin.ImageAt("Emoji_Part1", 0.7));
        Assert.Equal("c.png", skin.ImageAt("Emoji_Part1", 99));

        // 막는 것.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPartSetup("땀", "Emoji_Part2", new SkinPartSetup { X = 1f })); // 그림이 없는 조각
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup { Scale = 0f }));
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup { FrameSeconds = 0f }));
        Assert.Equal(revision, editor.Revision);

        // 저작본 왕복. 덧붙임이 없는 스킨에는 안 적힌다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"setups\"", json, StringComparison.Ordinal);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
        Assert.Equal("c.png", MotionDocumentJson.Read(json).FindSkin("땀")!.ImageAt("Emoji_Part1", 99));

        // 한 장으로 줄이고 기본값으로 돌리면 덧붙임이 사라진다. 그림을 비우면 덧붙임도 함께.
        editor.SetSkinPartImages("땀", "Emoji_Part1", new[] { "a.png" });
        editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup());
        Assert.Empty(skin.Setups);
        Assert.DoesNotContain("\"setups\"", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);
        editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup { X = 30f });
        editor.SetSkinPart("땀", "Emoji_Part1", null);
        Assert.Empty(skin.Setups);
    }

    [Fact]
    public void 놓임과_반전_판은_이모지_산출물에_풀려_나간다()
    {
        MotionEditor editor = NewEditor();
        editor.AddSkin("꽃", EmojiRigFixture.RigKind);
        editor.SetSkinPartImages("꽃", "Emoji_Part1", new[] { "art/f.png" });
        editor.SetSkinPartImages("꽃", "Emoji_Part2", new[] { "art/f.png", "art/g.png" });
        editor.SetSkinPartImages("꽃", "Emoji_Part3", new[] { "art/f.png" });
        editor.SetSkinPartSetup("꽃", "Emoji_Part1", new SkinPartSetup { X = -150f, Y = 40f, Scale = 0.8f, Rotation = 15f });
        editor.AddSkin("꽃_반전", EmojiRigFixture.RigKind);
        editor.SetSkinPartImages("꽃_반전", "Emoji_Part1", new[] { "art/f_l.png" });
        editor.AddEmoji("flower", EmojiRigFixture.RigKind);
        editor.UpdateEmoji("flower", new EmojiAsset { Skin = "꽃", SkinFlipped = "꽃_반전", HoldSeconds = 1f });
        Assert.DoesNotContain(Mt.Authoring.Validation.EmojiValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking);

        var emoji = System.Text.Json.Nodes.JsonNode.Parse(Mt.Authoring.Validation.EmojisJson.Write(editor.Document))!["emojis"]![0]!;
        var parts = emoji["parts"]!;
        Assert.Equal(("art/f.png", -150f, 40f, 0.8f, 15f),
            (parts["Emoji_Part1"]!["image"]!.GetValue<string>(), parts["Emoji_Part1"]!["x"]!.GetValue<float>(), parts["Emoji_Part1"]!["y"]!.GetValue<float>(),
             parts["Emoji_Part1"]!["scale"]!.GetValue<float>(), parts["Emoji_Part1"]!["rotation"]!.GetValue<float>()));
        Assert.Null(parts["Emoji_Part1"]!["frames"]);
        Assert.Equal(new[] { "art/g.png" }, parts["Emoji_Part2"]!["frames"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.Equal((0.2f, true), (parts["Emoji_Part2"]!["frameSeconds"]!.GetValue<float>(), parts["Emoji_Part2"]!["loop"]!.GetValue<bool>()));
        Assert.Equal("art/f.png", parts["Emoji_Part3"]!.GetValue<string>()); // 한 장이고 제자리면 글 하나 — 전과 같은 모양
        Assert.Equal("art/f_l.png", emoji["partsFlipped"]!["Emoji_Part1"]!.GetValue<string>());

        // 반전 판 스킨이 이름을 바꾸면 따라오고, 지워지면 반전 판만 없어진다. 없는 스킨을 가리키면 막는다.
        editor.RenameSkin("꽃_반전", "꽃_좌");
        Assert.Equal("꽃_좌", editor.Document.FindEmoji("flower")!.SkinFlipped);
        editor.RemoveSkin("꽃_좌");
        Assert.Null(editor.Document.FindEmoji("flower")!.SkinFlipped);
        editor.UpdateEmoji("flower", new EmojiAsset { Skin = "꽃", SkinFlipped = "ghost", HoldSeconds = 1f });
        Assert.Contains(Mt.Authoring.Validation.EmojiValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.Message.Contains("ghost", StringComparison.Ordinal));
    }
}
