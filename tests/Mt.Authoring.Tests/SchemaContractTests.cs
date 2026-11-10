using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Schema;
using Mt.Authoring.Validation;
using Xunit;

namespace Mt.Authoring.Tests;

/// <summary>스키마 계약(<c>docs/PLAN-characterRig.md</c> §3의 1) — 문서는 유니티가 덤프한 스키마의 리그와 노드만 쓰고, 어긋난 문서는 내보내지 못한다.</summary>
public class SchemaContractTests
{
    private static readonly RigSchemaFile Schema = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    [Fact]
    public void 스키마의_리그와_노드만_쓰는_문서는_어긋난_곳이_없다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        Assert.All(document.Motions, m => Assert.Empty(SchemaContract.Motion(Schema.NodeIds, m)));
        Assert.Empty(SchemaContract.DefinedRigs(document));
        Assert.True(MotionExport.TryExport(document, Schema.NodeIds, out _, out _));
    }

    [Fact]
    public void 모션이_스키마에_없는_리그나_노드를_쓰면_어느_것이_왜_어긋났는지_나오고_내보내기가_막힌다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        MotionAsset nod = document.Motions[0];

        // 스키마에 없는 노드 — 유니티 쪽에서 이름이 바뀌었거나 사라진 노드를 모션이 아직 쓰고 있다.
        nod.Channels.Add(new ChannelAsset { Node = "Renamed_In_Unity", Kind = MotionChannelKind.Position });
        SchemaViolation missingNode = Assert.Single(SchemaContract.Motion(Schema.NodeIds, nod));
        Assert.Equal(("모션", nod.Name, true), (missingNode.Kind, missingNode.Subject, missingNode.Blocking));
        Assert.Contains("Renamed_In_Unity", missingNode.Message, StringComparison.Ordinal);

        // 스키마에 없는 리그 — 있는 리그를 함께 보인다.
        var ghost = new MotionAsset { Name = "ghost", Rig = "ghost_rig", Duration = 1f };
        document.Motions.Add(ghost);
        SchemaViolation missingRig = Assert.Single(SchemaContract.Motion(Schema.NodeIds, ghost));
        Assert.Contains("ghost_rig", missingRig.Message, StringComparison.Ordinal);
        Assert.Contains(nod.Rig, missingRig.Message, StringComparison.Ordinal);

        // 검증이 그것을 막는 문제로 올린다 — 내보내기가 안 된다.
        Assert.False(MotionExport.TryExport(document, Schema.NodeIds, out _, out IReadOnlyList<MotionProblem> problems));
        Assert.Contains(problems, p => p.MotionName == nod.Name && p.Message == missingNode.Message);
        Assert.Contains(problems, p => p.MotionName == "ghost" && p.Message.Contains("리그 'ghost_rig'가 스키마에 없다", StringComparison.Ordinal));
    }

    [Fact]
    public void 이모지의_리그와_자리_노드와_스킨의_조각이_스키마에_없으면_막는다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        string rig = document.Motions[0].Rig;
        string node = Schema.NodeIds[rig].First();
        document.Skins.Add(new SkinAsset { Name = "그림", Rig = rig, Parts = { [node] = "art/a.png", ["Gone_Part"] = "art/b.png" } });
        var emoji = new EmojiAsset { Name = "땀", Rig = rig, Skin = "그림", AnchorNode = "Gone_Anchor" };
        document.Emojis.Add(emoji);

        IReadOnlyList<SchemaViolation> violations = SchemaContract.Emoji(Schema.NodeIds, document, emoji);
        Assert.Equal(2, violations.Count);
        Assert.All(violations, v => Assert.Equal(("이모지", "땀", true), (v.Kind, v.Subject, v.Blocking)));
        Assert.Contains(violations, v => v.Message.Contains("자리 노드 'Gone_Anchor'", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Message.Contains("스킨 '그림'의 조각 'Gone_Part'", StringComparison.Ordinal));

        Assert.False(EmojisJson.TryExport(document, Schema.NodeIds, out _, out IReadOnlyList<string> problems));
        Assert.Equal(2, problems.Count);

        // 스키마에 없는 리그 — 노드는 더 묻지 않는다.
        emoji.Rig = "ghost_rig";
        Assert.Contains("ghost_rig", Assert.Single(SchemaContract.Emoji(Schema.NodeIds, document, emoji)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 클립의_배역이_스키마에_없는_리그를_입으면_막는다()
    {
        string rig = Schema.NodeIds.Keys.First();
        var clip = new ClipAsset { Name = "scene_1", Roles = { new ClipRole { Name = "a", Rig = rig }, new ClipRole { Name = "b", Rig = "ghost_rig" } } };

        SchemaViolation violation = Assert.Single(SchemaContract.Clip(Schema.NodeIds, clip));
        Assert.Equal(("배역", "b", true), (violation.Kind, violation.Subject, violation.Blocking));

        var document = new MotionDocument { Clips = { clip } };
        Assert.Contains(ClipValidator.Validate(document, Schema.NodeIds), p => p.Blocking && p.Message.Contains("배역 'b'", StringComparison.Ordinal) && p.Message.Contains("ghost_rig", StringComparison.Ordinal));
    }

    [Fact]
    public void 문서가_정의한_리그는_알린다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        document.Rigs.Add(new RigAsset { Kind = "ghost_rig" });

        SchemaViolation violation = Assert.Single(SchemaContract.DefinedRigs(document));
        Assert.Equal(("리그", "ghost_rig", false), (violation.Kind, violation.Subject, violation.Blocking));
    }
}
