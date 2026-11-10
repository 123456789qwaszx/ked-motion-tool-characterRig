using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Schema;

/// <summary>이름이 바뀐 것으로 보이는 노드 — 같은 부모의 같은 자리에서 하나가 사라지고 하나가 생겼다.</summary>
public sealed record SchemaRename(string From, string To);

/// <summary>부모가 바뀐 노드. 부모가 없으면 빈 글.</summary>
public sealed record SchemaMove(string Node, string FromParent, string ToParent);

/// <summary>리그 하나에서 달라진 것.</summary>
public sealed record SchemaRigDiff(string Kind, IReadOnlyList<string> Removed, IReadOnlyList<SchemaRename> Renamed, IReadOnlyList<string> Added, IReadOnlyList<SchemaMove> Moved)
{
    public bool IsEmpty => Removed.Count == 0 && Renamed.Count == 0 && Added.Count == 0 && Moved.Count == 0;
}

/// <summary>달라진 스키마를 쓰는 것 — 무엇이(모션 · 이모지 · 스킨 · 배역), 어느 것이, 어느 리그의 어느 노드를. 이름이 바뀐 것으로 보이면 새 이름.</summary>
public sealed record SchemaUse(string Kind, string Subject, string Rig, string? Node, string? RenamedTo);

/// <summary>
/// 스키마가 달라진 것(<c>docs/PLAN-characterRig.md</c> §3의 2) — 문서를 지을 때의 스키마(<see cref="SchemaPrint"/>)와 지금 실린 덤프를 견준다.
/// 사라진 리그 · 새 리그, 리그마다 사라진 노드 · 이름이 바뀐 것으로 보이는 노드 · 새 노드 · 부모가 바뀐 노드.
/// 구조만 본다 — 노드의 자리 · 크기 값이 달라진 것은 말하지 않는다(문서는 노드를 이름으로 가리킨다).
/// </summary>
public sealed class SchemaDiff
{
    public static readonly SchemaDiff None = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SchemaRigDiff>());

    private SchemaDiff(IReadOnlyList<string> removedRigs, IReadOnlyList<string> addedRigs, IReadOnlyList<SchemaRigDiff> rigs)
    {
        RemovedRigs = removedRigs;
        AddedRigs = addedRigs;
        Rigs = rigs;
    }

    public IReadOnlyList<string> RemovedRigs { get; }

    public IReadOnlyList<string> AddedRigs { get; }

    /// <summary>두 쪽에 다 있고 노드가 달라진 리그만.</summary>
    public IReadOnlyList<SchemaRigDiff> Rigs { get; }

    public bool IsEmpty => RemovedRigs.Count == 0 && AddedRigs.Count == 0 && Rigs.Count == 0;

    public bool HasRenames => Rigs.Any(r => r.Renamed.Count > 0);

    /// <summary>덤프의 지문 — 리그와 노드를 덤프의 순서 그대로.</summary>
    public static SchemaPrint PrintOf(RigSchemaFile dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var print = new SchemaPrint();

        foreach (RigSchemaRigDto rig in dump.Dto.rigs)
        {
            print.Rigs.Add(new SchemaPrintRig { Kind = rig.rigKind, Nodes = rig.nodes.Select(n => SchemaPrintRig.Entry(n.id, n.parent)).ToList() });
        }

        return print;
    }

    public static SchemaDiff Compare(SchemaPrint before, SchemaPrint after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var removedRigs = before.Rigs.Select(r => r.Kind).Where(k => after.Rigs.All(r => !Same(r.Kind, k))).ToList();
        var addedRigs = after.Rigs.Select(r => r.Kind).Where(k => before.Rigs.All(r => !Same(r.Kind, k))).ToList();
        var rigs = new List<SchemaRigDiff>();

        foreach (SchemaPrintRig old in before.Rigs)
        {
            if (after.Rigs.FirstOrDefault(r => Same(r.Kind, old.Kind)) is { } now && CompareRig(old, now) is { IsEmpty: false } diff)
            {
                rigs.Add(diff);
            }
        }

        return removedRigs.Count == 0 && addedRigs.Count == 0 && rigs.Count == 0 ? None : new SchemaDiff(removedRigs, addedRigs, rigs);
    }

    private static SchemaRigDiff CompareRig(SchemaPrintRig before, SchemaPrintRig after)
    {
        List<(string Id, string Parent)> old = before.Parsed().ToList();
        List<(string Id, string Parent)> now = after.Parsed().ToList();
        var oldIds = new HashSet<string>(old.Select(n => n.Id), StringComparer.Ordinal);
        var nowIds = new HashSet<string>(now.Select(n => n.Id), StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal); // 옛 이름 → 새 이름
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // 부모가 먼저 오므로, 부모의 이름이 바뀐 것을 안 뒤에 자식을 본다.
        foreach ((string id, string parent) in old.Where(n => !nowIds.Contains(n.Id)))
        {
            string parentNow = renamed.TryGetValue(parent, out string? p) ? p : parent;
            int place = old.Where(n => Same(n.Parent, parent)).Select(n => n.Id).ToList().IndexOf(id);
            List<string> siblingsNow = now.Where(n => Same(n.Parent, parentNow)).Select(n => n.Id).ToList();

            // 같은 부모의 같은 자리에 새 노드가 있으면 그것이다.
            if (place < siblingsNow.Count && !oldIds.Contains(siblingsNow[place]) && taken.Add(siblingsNow[place]))
            {
                renamed[id] = siblingsNow[place];
            }
        }

        string Now(string id) => renamed.TryGetValue(id, out string? to) ? to : id;

        return new SchemaRigDiff(
            before.Kind,
            old.Select(n => n.Id).Where(id => !nowIds.Contains(id) && !renamed.ContainsKey(id)).ToList(),
            old.Where(n => renamed.ContainsKey(n.Id)).Select(n => new SchemaRename(n.Id, renamed[n.Id])).ToList(),
            now.Select(n => n.Id).Where(id => !oldIds.Contains(id) && !taken.Contains(id)).ToList(),
            old.Where(n => nowIds.Contains(n.Id))
                .Select(n => new SchemaMove(n.Id, n.Parent, now.First(m => Same(m.Id, n.Id)).Parent))
                .Where(m => !Same(Now(m.FromParent), m.ToParent))
                .ToList());
    }

    /// <summary>달라진 것을 쓰는 것들 — 사라진 리그를 입은 것, 사라졌거나 이름이 바뀐 것으로 보이는 노드를 가리키는 것.</summary>
    public IReadOnlyList<SchemaUse> Users(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var uses = new List<SchemaUse>();

        if (IsEmpty)
        {
            return uses;
        }

        void Rig(string kind, string subject, string? rig)
        {
            if (rig is not null && RemovedRigs.Contains(rig, StringComparer.Ordinal))
            {
                uses.Add(new SchemaUse(kind, subject, rig, null, null));
            }
        }

        void Node(string kind, string subject, string rig, string? node)
        {
            if (node is null || Rigs.FirstOrDefault(r => Same(r.Kind, rig)) is not { } diff)
            {
                return;
            }

            if (diff.Renamed.FirstOrDefault(r => Same(r.From, node)) is { } rename)
            {
                uses.Add(new SchemaUse(kind, subject, rig, node, rename.To));
            }
            else if (diff.Removed.Contains(node, StringComparer.Ordinal))
            {
                uses.Add(new SchemaUse(kind, subject, rig, node, null));
            }
        }

        foreach (MotionAsset motion in document.Motions)
        {
            Rig("모션", motion.Name, motion.Rig);

            foreach (string node in motion.Channels.Select(c => c.Node).Distinct(StringComparer.Ordinal))
            {
                Node("모션", motion.Name, motion.Rig ?? "", node);
            }
        }

        foreach (EmojiAsset emoji in document.Emojis)
        {
            Rig("이모지", emoji.Name, emoji.Rig);
            Node("이모지", emoji.Name, emoji.Rig, emoji.AnchorNode);
        }

        foreach (SkinAsset skin in document.Skins)
        {
            Rig("스킨", skin.Name, skin.Rig);

            foreach (string node in skin.Parts.Keys)
            {
                Node("스킨", skin.Name, skin.Rig, node);
            }
        }

        foreach (ClipAsset clip in document.Clips)
        {
            foreach (ClipRole role in clip.Roles)
            {
                Rig("배역", role.Name, role.Rig);
            }
        }

        return uses;
    }

    /// <summary>
    /// 문제 줄에 올리는 알림 — 리그마다 한 줄(무엇이 달라졌나)과 그것을 쓰는 것마다 한 줄. 막지 않는다:
    /// 사라진 노드를 쓰는 문서를 막는 것은 계약(<see cref="SchemaContract"/>)이고, 이것은 **왜** 어긋났는지를 말한다.
    /// </summary>
    public IReadOnlyList<SchemaViolation> Notices(MotionDocument document)
    {
        var notices = new List<SchemaViolation>();

        foreach (string rig in RemovedRigs)
        {
            notices.Add(Notice("리그", rig, "문서를 지을 때는 스키마에 있었는데 지금은 없다"));
        }

        foreach (string rig in AddedRigs)
        {
            notices.Add(Notice("리그", rig, "문서를 지은 뒤 스키마에 새로 생겼다"));
        }

        foreach (SchemaRigDiff diff in Rigs)
        {
            var parts = new List<string>();

            if (diff.Removed.Count > 0)
            {
                parts.Add($"사라진 노드 [{string.Join(", ", diff.Removed)}]");
            }

            if (diff.Renamed.Count > 0)
            {
                parts.Add($"이름이 바뀐 것으로 보이는 노드 [{string.Join(", ", diff.Renamed.Select(r => $"{r.From} → {r.To}"))}]");
            }

            if (diff.Added.Count > 0)
            {
                parts.Add($"새 노드 [{string.Join(", ", diff.Added)}]");
            }

            if (diff.Moved.Count > 0)
            {
                parts.Add($"부모가 바뀐 노드 [{string.Join(", ", diff.Moved.Select(m => $"{m.Node}: {Label(m.FromParent)} → {Label(m.ToParent)}"))}]");
            }

            notices.Add(Notice("리그", diff.Kind, "문서를 지을 때와 스키마가 다르다 — " + string.Join(" · ", parts)));
        }

        foreach (SchemaUse use in Users(document))
        {
            notices.Add(Notice(use.Kind, use.Subject, use switch
            {
                { Node: null } => $"리그 '{use.Rig}'가 스키마에서 사라졌다",
                { RenamedTo: { } to } => $"노드 '{use.Node}'의 이름이 '{to}'로 바뀐 것으로 보인다(리그 '{use.Rig}')",
                _ => $"노드 '{use.Node}'가 스키마에서 사라졌다(리그 '{use.Rig}')",
            }));
        }

        return notices;
    }

    private static SchemaViolation Notice(string kind, string subject, string message) => new(kind, subject, message, Blocking: false);

    private static string Label(string parent) => parent.Length == 0 ? "(없음)" : parent;

    private static bool Same(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);
}
