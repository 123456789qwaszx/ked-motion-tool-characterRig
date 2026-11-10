using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Schema;
using Xunit;

namespace Mt.Authoring.Tests;

/// <summary>스키마 계약의 방향을 보이는 코드(<c>docs/PLAN-characterRig.md</c> §3) — 문서는 유니티가 덤프한 스키마의 리그와 노드만 쓴다.</summary>
public class SchemaContractTests
{
    private static readonly RigSchemaFile Schema = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    [Fact]
    public void 스키마의_리그와_노드만_쓰는_문서는_어긋난_곳이_없다()
        => Assert.Empty(SchemaContract.Check(Schema, Fixtures.NodBowDocument()));

    [Fact]
    public void 스키마에_없는_리그나_노드를_쓰면_어느_것이_왜_어긋났는지_나온다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        MotionAsset nod = document.Motions[0];

        // 스키마에 없는 노드 — 유니티 쪽에서 이름이 바뀌었거나 사라진 노드를 모션이 아직 쓰고 있다.
        nod.Channels.Add(new ChannelAsset { Node = "Renamed_In_Unity", Kind = Ked.Presentation.Core.MotionChannelKind.Position });
        SchemaViolation missingNode = Assert.Single(SchemaContract.Check(Schema, document));
        Assert.Equal(("모션", nod.Name), (missingNode.Kind, missingNode.Subject));
        Assert.Contains("Renamed_In_Unity", missingNode.Message, StringComparison.Ordinal);

        // 스키마에 없는 리그 — 모션과 이모지 둘 다.
        document.Motions.Add(new MotionAsset { Name = "ghost", Rig = "ghost_rig", Duration = 1f });
        document.Emojis.Add(new EmojiAsset { Name = "땀", Rig = "ghost_rig" });
        IReadOnlyList<SchemaViolation> violations = SchemaContract.Check(Schema, document);
        Assert.Contains(violations, v => v.Kind == "모션" && v.Subject == "ghost" && v.Message.Contains("ghost_rig", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Kind == "이모지" && v.Subject == "땀");

        // 문서가 정의한 리그 — 구조는 유니티가 정한다. 스키마에 없는 이름이든, 스키마의 것을 가리는 이름이든 어긋난 것이다.
        document.Rigs.Add(new RigAsset { Kind = "ghost_rig" });
        document.Rigs.Add(new RigAsset { Kind = nod.Rig });
        violations = SchemaContract.Check(Schema, document);
        Assert.Contains(violations, v => v.Kind == "리그" && v.Subject == "ghost_rig" && v.Message.Contains("스키마에 없다", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Kind == "리그" && v.Subject == nod.Rig && v.Message.Contains("가린다", StringComparison.Ordinal));
    }
}
