using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>툴에서 정의한 리그(<c>PLAN-parts.md</c> §9) — 덤프를 기다리지 않고 이모지 리그를 만든다. Spine처럼 툴이 원본.</summary>
public class RigAssetTests
{
    private static readonly RigSchemaFile Dump = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json")); // character · screenEffect뿐

    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji());
        editor.AddSceneObject(null, "e1", RigTemplates.EmojiKind);
        editor.AttachObject(null, "e1", "s1");
        return editor;
    }

    [Fact]
    public void 문서의_리그는_덤프와_한_벌로_서고_조각과_불투명도_노드가_표시대로_나온다()
    {
        MotionEditor editor = NewEditor();
        Assert.DoesNotContain(RigTemplates.EmojiKind, Dump.RigKinds);
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Contains(RigTemplates.EmojiKind, rig.RigKinds);
        Assert.Contains("character", rig.RigKinds);
        Assert.Equal(Dump.Sha256, rig.Sha256); // 문서가 기억하는 것은 덤프의 해시다
        Assert.Same(Dump, RigAssets.Compose(Dump, new MotionDocument())); // 더할 것이 없으면 그대로

        Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, RigParts.Of(rig, RigTemplates.EmojiKind));
        Assert.Equal(new[] { "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, rig.AlphaNodeIds[RigTemplates.EmojiKind].OrderBy(n => n, StringComparer.Ordinal));
        Assert.Contains("Emoji_Anchor", rig.NodeIds[RigTemplates.EmojiKind]);

        // 씬에 서고(캐릭터의 슬롯 끝에 붙어), 조각은 정의한 크기다.
        RectNodeTree tree = ScenePreview.Build(rig, editor.Document.ActiveSceneAsset!);
        Assert.Equal("s1/CharSlot_Scale", tree.GetParentKey("e1/__root"));
        Assert.Equal("e1/Emoji_Body", tree.GetParentKey("e1/Emoji_Part2"));
        Assert.Equal(RigTemplates.DefaultPartSize, tree.GetRectSize("e1/Emoji_Part1").X, 3);

        // 조각은 이름이 아니라 표시(Image)로 가린다 — 이름 규칙에 안 맞아도 된다.
        editor.AddRig(new RigAsset { Kind = "prop", Nodes = { new RigNodeAsset { Id = "Hand" }, new RigNodeAsset { Id = "Leaf", Parent = "Hand", Width = 80, Height = 40, Image = true } } });
        RigSchemaFile both = RigAssets.Compose(Dump, editor.Document);
        Assert.Equal(new[] { "Leaf" }, RigParts.Of(both, "prop"));
        Assert.Empty(both.AlphaNodeIds["prop"]);

        // 모션·스킨·이모지 검증이 이 리그의 노드로 선다.
        editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f);
        editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Alpha);
        editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.2f, 1f) });
        Assert.Empty(MotionValidator.Validate(editor.Document, both.NodeIds, null, both.AlphaNodeIds));
        Assert.NotEmpty(MotionValidator.Validate(editor.Document, Dump.NodeIds)); // 덤프만으로는 모르는 리그다
    }

    [Fact]
    public void 같은_이름이면_문서의_리그가_덤프의_것을_이긴다()
    {
        // 런타임이 나중에 이모지 리그를 덤프에 넣어도, 문서가 정의한 것이 쓰인다(툴이 원본).
        RigSchemaFile dumpWithEmoji = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 2, partSize: 64f));
        RigSchemaFile rig = RigAssets.Compose(dumpWithEmoji, editor.Document);
        Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part2" }, RigParts.Of(rig, RigTemplates.EmojiKind));
        Assert.Single(rig.Dto.rigs, r => r.rigKind == RigTemplates.EmojiKind);
    }

    [Fact]
    public void 편집기는_세울_수_있는_리그만_받고_조각을_다시_세운다()
    {
        MotionEditor editor = NewEditor();
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji()));                       // 이미 있다
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji("character")));            // 덤프가 원본인 리그
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji("camera")));
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji("1bad")));
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(new RigAsset { Kind = "x", Nodes = { new RigNodeAsset { Id = "A", Parent = "B" }, new RigNodeAsset { Id = "B" } } })); // 부모가 뒤에
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(new RigAsset { Kind = "x", Nodes = { new RigNodeAsset { Id = "A" }, new RigNodeAsset { Id = "A" } } }));
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(new RigAsset { Kind = "x", Nodes = { new RigNodeAsset { Id = "a/b" } } }));
        Assert.Throws<InvalidOperationException>(() => editor.SetRigParts(RigTemplates.EmojiKind, 0, 100f));
        Assert.Throws<InvalidOperationException>(() => editor.SetRigParts(RigTemplates.EmojiKind, 3, 0f));
        Assert.Throws<KeyNotFoundException>(() => editor.SetRigParts("character", 3, 100f)); // 덤프의 리그는 못 고친다
        Assert.Equal(revision, editor.Revision);

        // 조각 여섯, 크기 96 — 한 칸. 다른 노드는 그대로다.
        editor.SetRigParts(RigTemplates.EmojiKind, 6, 96f);
        Assert.Equal(revision + 1, editor.Revision);
        RigAsset rig = editor.Document.FindRig(RigTemplates.EmojiKind)!;
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4", "Emoji_Part5", "Emoji_Part6" }, rig.Nodes.Select(n => n.Id));
        Assert.All(rig.Nodes.Where(n => n.Image), n => Assert.Equal((96f, "Emoji_Body", true), (n.Width, n.Parent, n.Alpha)));
        editor.Undo();
        Assert.Equal(4, editor.Document.FindRig(RigTemplates.EmojiKind)!.Nodes.Count(n => n.Image));

        // 받은 객체를 문서에 꽂지 않는다 — 밖에서 고쳐도 문서는 그대로.
        RigAsset given = RigTemplates.Emoji("prop");
        editor.AddRig(given);
        given.Nodes.Clear();
        Assert.Equal(6, editor.Document.FindRig("prop")!.Nodes.Count);

        // 저작본 왕복. 리그가 없는 문서에는 안 적힌다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
        Assert.DoesNotContain("\"rigs\"", MotionDocumentJson.Write(new MotionDocument()), StringComparison.Ordinal);

        // 지우면 그 리그의 오브젝트는 남고(검증이 알린다) 씬은 던지지 않고 선다.
        editor.RemoveRig(RigTemplates.EmojiKind);
        var missing = new List<SceneObject>();
        ScenePreview.Build(RigAssets.Compose(Dump, editor.Document), editor.Document.ActiveSceneAsset!, null, missing);
        Assert.Equal("e1", Assert.Single(missing).Id);
    }

    [Fact]
    public void 내보낸_리그는_덤프와_같은_모양이라_같은_로더로_읽힌다()
    {
        MotionEditor editor = NewEditor();
        string json = RigAssets.WriteSchemas(editor.Document, new Vec2(1920f, 1080f));
        Assert.Equal(json, RigAssets.WriteSchemas(editor.Document, new Vec2(1920f, 1080f))); // 결정적

        RigSchemaFile read = RigSchemaFile.Parse(System.Text.Encoding.UTF8.GetBytes(json)); // 캐릭터 덤프를 읽는 그 길
        Assert.Equal(new[] { RigTemplates.EmojiKind }, read.RigKinds);
        Assert.Equal(4, RigParts.Of(read, RigTemplates.EmojiKind).Count);
        Assert.Equal(5, read.AlphaNodeIds[RigTemplates.EmojiKind].Count);
        Assert.Equal(RigTemplates.DefaultPartSize, read.BuildTree(RigTemplates.EmojiKind).GetRectSize("Emoji_Part1").X, 3);

        JsonNode root = JsonNode.Parse(json)!;
        JsonNode part = root["rigs"]![0]!["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "Emoji_Part1")!;
        Assert.True(part["hasImage"]!.GetValue<bool>());
        Assert.True(part["hasCanvasGroup"]!.GetValue<bool>());
        Assert.Equal("__root", root["rigs"]![0]!["nodes"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void 명령줄도_문서의_리그로_검증하고_내보낸다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
        editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.2f, 40f) });

        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"rig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string document = Path.Combine(directory, "a.motion-authoring.json");
        File.WriteAllText(document, MotionDocumentJson.Write(editor.Document));

        int Run(out JsonNode? json, params string[] args)
        {
            var stdout = new StringWriter();
            int code = MtCli.Run(args, stdout, new StringWriter());
            json = JsonNode.Parse(stdout.ToString());
            return code;
        }

        Assert.Equal(0, Run(out JsonNode? validated, "validate", document)); // 내장 덤프에는 emoji가 없다 — 문서의 리그로 통과한다
        Assert.Empty(validated!["problems"]!.AsArray());
        Assert.Equal(0, Run(out JsonNode? pose, "pose", document, "--motion", "pop", "--time", "0.2"));
        Assert.Equal(40.0, pose!["nodes"]!["Emoji_Body"]!["position"]![1]!.GetValue<double>(), 3);

        string target = Path.Combine(directory, "rigs.json");
        Assert.Equal(0, Run(out JsonNode? exported, "rigs", document, target));
        Assert.Equal(4, exported!["rigs"]![0]!["parts"]!.GetValue<int>());
        Assert.Contains(RigTemplates.EmojiKind, RigSchemaFile.Load(target).RigKinds);
    }

    // ── 리그 편집기(PLAN-parts.md §12) ─────────────────────────────────

    private static string[] Ids(MotionEditor editor, string kind = RigTemplates.EmojiKind) => editor.Document.FindRig(kind)!.Nodes.Select(n => n.Id).ToArray();

    [Fact]
    public void 노드를_짓고_지우고_옮기면_부모가_늘_자식보다_먼저_온다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 2));
        long revision = editor.Revision;

        // 더하기 — 부모의 식구들 맨 뒤. 그림 노드는 본의 크기와 불투명도를 갖고 선다.
        RigNodeAsset glow = editor.AddRigNode(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Anchor", image: true);
        Assert.Equal((160f, 160f, true, true), (glow.Width, glow.Height, glow.Image, glow.Alpha));
        editor.AddRigNode(RigTemplates.EmojiKind, "Emoji_Spark", "Emoji_Glow");
        editor.AddRigNode(RigTemplates.EmojiKind, "Extra"); // 루트 밑
        Assert.Equal(revision + 3, editor.Revision);
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark", "Extra" }, Ids(editor));

        // 막는 것 — 기록이 안 남는다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "Emoji_Glow"));       // 이미 있다
        Assert.Throws<InvalidOperationException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "1bad"));
        Assert.Throws<InvalidOperationException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "a/b"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "X", "Nope"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddRigNode("character", "X"));                                 // 덤프의 리그는 못 고친다
        Assert.Throws<InvalidOperationException>(() => editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Spark")); // 제 밑으로
        Assert.Throws<InvalidOperationException>(() => editor.UpdateRigNode(RigTemplates.EmojiKind, "Extra", new RigNodeAsset { Width = -1f }));
        Assert.Equal(revision, editor.Revision);

        // 형제 사이의 순서 — 식구째 옮긴다. 끝에서는 아무 일도 없다.
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Glow", -1);
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Glow", "Emoji_Spark", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Extra" }, Ids(editor));
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Glow", -1);
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Part2", +1);
        Assert.Equal(revision + 1, editor.Revision);
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Anchor", +1);
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Glow", "Emoji_Spark", "Emoji_Body", "Emoji_Part1", "Emoji_Part2" }, Ids(editor));

        // 부모 바꾸기 — 식구째, 새 부모의 맨 뒤로. 같은 부모면 무기록.
        revision = editor.Revision;
        editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Body");
        editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Body");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark" }, Ids(editor));
        Assert.Equal("Emoji_Body", editor.Document.FindRig(RigTemplates.EmojiKind)!.Find("Emoji_Glow")!.Parent);
        editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Body", null); // 루트로
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark" }, Ids(editor));
        Assert.Equal("", editor.Document.FindRig(RigTemplates.EmojiKind)!.Find("Emoji_Body")!.Parent);

        // 값 — 한 칸, 같은 값은 무기록. 세운 나무에 그대로 선다.
        revision = editor.Revision;
        var moved = new RigNodeAsset { X = 30f, Y = -12f, Width = 80f, Height = 40f, Image = true, Alpha = false };
        editor.UpdateRigNode(RigTemplates.EmojiKind, "Emoji_Glow", moved);
        editor.UpdateRigNode(RigTemplates.EmojiKind, "Emoji_Glow", moved);
        Assert.Equal(revision + 1, editor.Revision);
        RigSchemaFile composed = RigAssets.Compose(Dump, editor.Document);
        Assert.True(composed.TryGetRig(RigTemplates.EmojiKind, out RigSchemaRigDto dto));
        RigSchemaNodeDto node = dto.nodes.Single(n => n.id == "Emoji_Glow");
        Assert.Equal((30f, -12f, 80f, 40f, true, false), (node.anchoredPosition.x, node.anchoredPosition.y, node.sizeDelta.x, node.sizeDelta.y, node.hasImage, node.hasCanvasGroup));
        Assert.Contains("Emoji_Glow", RigParts.Of(composed, RigTemplates.EmojiKind));

        // 지우기 — 밑의 노드도 함께. 되돌리면 그 자리에 돌아온다.
        editor.RemoveRigNode(RigTemplates.EmojiKind, "Emoji_Body");
        Assert.Equal(new[] { "Extra", "Emoji_Anchor" }, Ids(editor));
        editor.Undo();
        Assert.Equal(7, Ids(editor).Length);
        RigAssets.Compose(Dump, editor.Document); // 여전히 세울 수 있다
    }

    [Fact]
    public void 노드의_이름을_바꾸면_채널과_스킨과_이모지와_붙는_노드가_따라온다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 2));
        editor.AddSceneObject(null, "e1", RigTemplates.EmojiKind);
        editor.AddSkin("땀", RigTemplates.EmojiKind);
        editor.SetSkinPart("땀", "Emoji_Part1", "art/sweat.png");
        editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f);
        editor.AddChannel("pop", "Emoji_Part1", MotionChannelKind.Position);
        editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
        editor.AddEmoji("sweat", RigTemplates.EmojiKind);

        long revision = editor.Revision;
        editor.RenameRigNode(RigTemplates.EmojiKind, "Emoji_Part1", "Drop");
        editor.RenameRigNode(RigTemplates.EmojiKind, "Emoji_Anchor", "Pin");
        editor.RenameRigNode(RigTemplates.EmojiKind, "Pin", "Pin"); // 같은 이름은 무기록
        Assert.Equal(revision + 2, editor.Revision);

        Assert.Equal(new[] { "Pin", "Emoji_Body", "Drop", "Emoji_Part2" }, Ids(editor));
        Assert.Equal("Pin", editor.Document.FindRig(RigTemplates.EmojiKind)!.Find("Emoji_Body")!.Parent);
        Assert.Equal(new[] { "Drop", "Emoji_Body" }, editor.Document.Find("pop")!.Channels.Select(c => c.Node));
        Assert.Equal("art/sweat.png", editor.Document.FindSkin("땀")!.Parts["Drop"]);
        Assert.False(editor.Document.FindSkin("땀")!.Parts.ContainsKey("Emoji_Part1"));
        Assert.Equal("Pin", editor.Document.FindEmoji("sweat")!.AnchorNode);

        // 이름이 겹치거나 규칙에 안 맞으면 막는다. 되돌리면 전부 함께 돌아온다.
        Assert.Throws<InvalidOperationException>(() => editor.RenameRigNode(RigTemplates.EmojiKind, "Drop", "Emoji_Body"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameRigNode(RigTemplates.EmojiKind, "Drop", "물방울"));
        editor.Undo();
        editor.Undo();
        Assert.Equal("Emoji_Part1", editor.Document.Find("pop")!.Channels[0].Node);
        Assert.Equal("Emoji_Anchor", editor.Document.FindEmoji("sweat")!.AnchorNode);
        Assert.True(editor.Document.FindSkin("땀")!.Parts.ContainsKey("Emoji_Part1"));
    }
}
