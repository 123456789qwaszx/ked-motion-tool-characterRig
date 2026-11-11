using Mt.Authoring.Model;
using Mt.Authoring.Schema;

namespace Mt.Authoring.Editing;

// 스키마 지문과 달라진 것을 따라가는 명령(docs/PLAN-characterRig.md §3의 2) — 각각 되돌리기 한 칸.
public sealed partial class MotionEditor
{
    /// <summary>문서를 지을 때의 스키마를 적는다. 같은 지문이면 기록하지 않는다.</summary>
    public void SetBuiltOn(SchemaPrint print)
    {
        ArgumentNullException.ThrowIfNull(print);
        Mutate(MotionChangeKind.Document, () => Document.BuiltOn = print);
    }

    /// <summary>
    /// 이름이 바뀐 것으로 보이는 노드를 따라간다 — 그 리그의 모션 채널이 옛 이름으로 가리키던 곳을 새 이름으로.
    /// 한 칸이다. 바꾼 곳의 수를 돌려준다(0이면 기록도 없다).
    /// </summary>
    public int FollowSchemaRenames(SchemaDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        int changed = 0;

        Batch(MotionChangeKind.Document, () =>
        {
            foreach (SchemaRigDiff rig in diff.Rigs)
            {
                foreach (SchemaRename rename in rig.Renamed)
                {
                    changed += Rename(rig.Kind, rename.From, rename.To);
                }
            }
        });

        return changed;
    }

    private int Rename(string rig, string from, string to)
    {
        int changed = 0;

        foreach (ChannelAsset channel in Document.Motions.Where(m => Same(m.Rig, rig)).SelectMany(m => m.Channels).Where(c => Same(c.Node, from)))
        {
            channel.Node = to;
            changed++;
        }

        return changed;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
}
