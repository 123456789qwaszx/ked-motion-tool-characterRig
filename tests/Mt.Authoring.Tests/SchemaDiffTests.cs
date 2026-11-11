using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Schema;
using Mt.Authoring.Serialization;
using Xunit;

namespace Mt.Authoring.Tests;

/// <summary>스키마 지문과 달라진 것(<c>docs/PLAN-characterRig.md</c> §3의 2) — 문서를 지을 때의 스키마와 지금의 덤프를 견주어 무엇이 달라졌는지, 무엇이 그것을 쓰는지 말한다.</summary>
public class SchemaDiffTests
{
    private static readonly RigSchemaFile Schema = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    private static SchemaPrint Print(params (string Kind, string[] Nodes)[] rigs)
        => new() { Rigs = rigs.Select(r => new SchemaPrintRig { Kind = r.Kind, Nodes = r.Nodes.ToList() }).ToList() };

    [Fact]
    public void 같은_덤프면_달라진_것이_없고_지문은_덤프의_리그와_노드를_순서대로_적는다()
    {
        SchemaPrint print = SchemaDiff.PrintOf(Schema);

        Assert.Equal(Schema.Dto.rigs.Select(r => r.rigKind), print.Rigs.Select(r => r.Kind));
        SchemaPrintRig character = print.Rigs.Single(r => r.Kind == "character");
        Assert.Equal(Schema.Dto.rigs.Single(r => r.rigKind == "character").nodes.Select(n => n.id), character.Parsed().Select(n => n.Id));
        Assert.Contains(("CharSlot_DepthY", "CharSlot_Track_Focus"), character.Parsed());

        Assert.True(SchemaDiff.Compare(print, SchemaDiff.PrintOf(Schema)).IsEmpty);
    }

    [Fact]
    public void 사라진_노드_새_노드_이름이_바뀐_노드_부모가_바뀐_노드를_가른다()
    {
        SchemaPrint before = Print(("r", new[] { "Root", "A < Root", "B < Root", "A1 < A", "Gone < B", "Mover < A" }));
        SchemaPrint after = Print(("r", new[] { "Root", "Alpha < Root", "B < Root", "A1 < Alpha", "Mover < B", "Fresh < B" }));

        SchemaRigDiff diff = Assert.Single(SchemaDiff.Compare(before, after).Rigs);

        // A는 같은 부모의 같은 자리에서 Alpha가 됐다 — 이름이 바뀐 것으로 본다. 그 자식 A1은 부모가 바뀐 것이 아니다.
        Assert.Equal(new[] { new SchemaRename("A", "Alpha") }, diff.Renamed);
        Assert.Equal(new[] { "Gone" }, diff.Removed);
        Assert.Equal(new[] { "Fresh" }, diff.Added);
        Assert.Equal(new[] { new SchemaMove("Mover", "A", "B") }, diff.Moved);
    }

    [Fact]
    public void 자리가_다르면_이름이_바뀐_것으로_보지_않는다()
    {
        // 첫 자식이 사라지고 끝에 새 자식이 생겼다 — 같은 자리가 아니다.
        SchemaPrint before = Print(("r", new[] { "Root", "A < Root", "B < Root" }));
        SchemaPrint after = Print(("r", new[] { "Root", "B < Root", "C < Root" }));

        SchemaRigDiff diff = Assert.Single(SchemaDiff.Compare(before, after).Rigs);

        Assert.Empty(diff.Renamed);
        Assert.Equal(new[] { "A" }, diff.Removed);
        Assert.Equal(new[] { "C" }, diff.Added);
    }

    [Fact]
    public void 사라진_리그와_새_리그를_말하고_그것을_쓰는_것을_찾는다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        string used = document.Motions[0].Channels[0].Node;
        SchemaPrint before = SchemaDiff.PrintOf(Schema);
        SchemaPrint after = SchemaDiff.PrintOf(Schema);
        after.Rigs.RemoveAll(r => r.Kind == "screenEffect");
        after.Rigs.Add(new SchemaPrintRig { Kind = "prop", Nodes = { "__root", "Prop_Hand < __root" } });
        SchemaPrintRig character = after.Rigs.Single(r => r.Kind == "character");
        character.Nodes = character.Nodes.Select(n => n.Replace(used, used + "_Renamed", StringComparison.Ordinal)).ToList();
        document.Clips.Add(new ClipAsset { Name = "clip", Roles = { new ClipRole { Name = "fx", Rig = "screenEffect" } } });

        SchemaDiff diff = SchemaDiff.Compare(before, after);

        Assert.Equal(new[] { "screenEffect" }, diff.RemovedRigs);
        Assert.Equal(new[] { "prop" }, diff.AddedRigs);
        Assert.True(diff.HasRenames);

        IReadOnlyList<SchemaUse> uses = diff.Users(document);
        Assert.Contains(new SchemaUse("모션", document.Motions[0].Name, "character", used, used + "_Renamed"), uses);
        Assert.Contains(new SchemaUse("배역", "fx", "screenEffect", null, null), uses);

        // 알림은 막지 않는다 — 막는 것은 계약(R1)이고, 이것은 왜 어긋났는지를 말한다.
        IReadOnlyList<SchemaViolation> notices = diff.Notices(document);
        Assert.All(notices, n => Assert.False(n.Blocking));
        Assert.Contains(notices, n => n.Subject == "character" && n.Message.Contains($"{used} → {used}_Renamed", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.Kind == "모션" && n.Message.Contains("바뀐 것으로 보인다", StringComparison.Ordinal));
    }

    [Fact]
    public void 이름_바꾸기를_따라가는_것은_한_칸이고_바꿀_곳이_없으면_기록이_없다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        MotionAsset motion = editor.Document.Motions[0];
        string used = motion.Channels[0].Node;
        SchemaPrint before = SchemaDiff.PrintOf(Schema);
        SchemaPrint after = SchemaDiff.PrintOf(Schema);
        SchemaPrintRig character = after.Rigs.Single(r => r.Kind == "character");
        character.Nodes = character.Nodes.Select(n => n.Replace(used, used + "_Renamed", StringComparison.Ordinal)).ToList();
        SchemaDiff diff = SchemaDiff.Compare(before, after);
        int expected = editor.Document.Motions.SelectMany(m => m.Channels).Count(c => c.Node == used);
        long revision = editor.Revision;

        Assert.Equal(expected, editor.FollowSchemaRenames(diff));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.DoesNotContain(editor.Document.Motions.SelectMany(m => m.Channels), c => c.Node == used);
        Assert.Empty(diff.Users(editor.Document));

        // 한 번 더 — 이미 따라갔으니 바꿀 곳이 없다.
        Assert.Equal(0, editor.FollowSchemaRenames(diff));
        Assert.Equal(revision + 1, editor.Revision);

        editor.Undo();
        Assert.Equal(expected, editor.Document.Motions.SelectMany(m => m.Channels).Count(c => c.Node == used));
    }

    [Fact]
    public void 지문은_저작본에_실리고_같은_지문을_다시_적으면_기록이_없고_없는_문서는_전과_같은_글자다()
    {
        var editor = new MotionEditor(new MotionDocument());
        Assert.DoesNotContain("builtOn", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);
        long revision = editor.Revision;

        editor.SetBuiltOn(SchemaDiff.PrintOf(Schema));
        Assert.Equal(revision + 1, editor.Revision);
        editor.SetBuiltOn(SchemaDiff.PrintOf(Schema));
        Assert.Equal(revision + 1, editor.Revision);

        MotionDocument read = MotionDocumentJson.Read(MotionDocumentJson.Write(editor.Document));
        Assert.True(SchemaDiff.Compare(read.BuiltOn!, SchemaDiff.PrintOf(Schema)).IsEmpty);
        Assert.Contains("\"CharSlot_DepthY < CharSlot_Track_Focus\"", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);
    }
}
