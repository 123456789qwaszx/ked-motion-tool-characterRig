using System.Text.RegularExpressions;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 툴에서 정의하는 리그의 명령(PLAN-parts.md §9) — 각각 되돌리기 한 칸.
public sealed partial class MotionEditor
{
    /// <summary>조각 수의 상한 — 터무니없는 값이 화면을 메우지 않게.</summary>
    public const int MaxRigParts = 16;

    /// <summary>캐릭터 리그·카메라의 가짜 리그·무대 뼈대는 툴에서 정의하지 않는다.</summary>
    private static readonly string[] ReservedRigKinds = { SceneObject.DefaultRig, Preview.CameraRig.RigKind, "stage" };

    private static readonly Regex RigKindRule = new("^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>리그를 문서에 더한다. 노드는 사본으로 들어간다. id가 겹치거나 부모가 앞에 없으면 막는다(세울 수 없는 리그다).</summary>
    public RigAsset AddRig(RigAsset rig)
    {
        ArgumentNullException.ThrowIfNull(rig);

        if (!RigKindRule.IsMatch(rig.Kind ?? ""))
        {
            throw new InvalidOperationException($"리그 이름은 영문자로 시작하는 영문·숫자·_ 다 ('{rig.Kind}')");
        }

        if (ReservedRigKinds.Contains(rig.Kind))
        {
            throw new InvalidOperationException($"'{rig.Kind}'는 툴에서 정의하지 않는 리그다.");
        }

        if (Document.FindRig(rig.Kind) is not null)
        {
            throw new InvalidOperationException($"리그 '{rig.Kind}'가 이미 있다.");
        }

        RigAsset copy = CloneRig(rig);
        RequireBuildable(copy);
        Mutate(MotionChangeKind.Document, () => Document.Rigs.Add(copy));
        return copy;
    }

    /// <summary>리그를 지운다. 그 리그의 오브젝트·모션·스킨·이모지는 남는다 — 검증이 "덤프에 없는 리그"로 알린다.</summary>
    public void RemoveRig(string kind)
    {
        RigAsset rig = RequireRig(kind);
        Mutate(MotionChangeKind.Document, () => Document.Rigs.Remove(rig));
    }

    /// <summary>
    /// 조각(그림 노드)의 수와 크기를 바꾼다 — 조각을 1번부터 다시 세운다. 다른 노드는 그대로다. 줄이면 없어진 조각을 쓰던 채널·스킨 칸은 남고 검증이 알린다.
    /// </summary>
    public void SetRigParts(string kind, int count, float size)
    {
        RigAsset rig = RequireRig(kind);

        if (count < 1 || count > MaxRigParts)
        {
            throw new InvalidOperationException($"조각은 1~{MaxRigParts}개다 ('{count}')");
        }

        if (!float.IsFinite(size) || size <= 0f)
        {
            throw new InvalidOperationException($"조각 크기는 0보다 큰 수다 ('{size}')");
        }

        // 지금 조각의 이름 머리와 부모를 잇는다(Emoji_Part1 → "Emoji_Part"). 조각이 하나도 없던 리그면 마지막 노드 밑에 새로.
        RigNodeAsset? first = rig.Nodes.FirstOrDefault(n => n.Image);
        string stem = first is null ? "Part" : first.Id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        string parent = first?.Parent ?? rig.Nodes.LastOrDefault()?.Id ?? "";
        bool alpha = first?.Alpha ?? true;

        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Nodes.RemoveAll(n => n.Image);

            for (int i = 1; i <= count; i++)
            {
                rig.Nodes.Add(new RigNodeAsset { Id = $"{stem}{i}", Parent = parent, Width = size, Height = size, Image = true, Alpha = alpha });
            }
        });
    }

    // ── 리그 편집기(PLAN-parts.md §12) — 노드를 하나씩 짓고 옮긴다 ────────

    /// <summary>
    /// 노드를 더한다 — <paramref name="parent"/>(비면 루트) 밑의 맨 뒤(그 부모의 식구들 뒤 = 가장 위에 그려진다). id는 그 리그에서 하나뿐이어야 한다.
    /// </summary>
    public RigNodeAsset AddRigNode(string kind, string id, string? parent = null, bool image = false)
    {
        RigAsset rig = RequireRig(kind);
        string name = RequireNodeId(rig, id);
        string under = parent ?? "";

        if (under.Length > 0 && rig.Find(under) is null)
        {
            throw new KeyNotFoundException($"부모 노드 '{under}'가 리그 '{kind}'에 없다.");
        }

        var node = new RigNodeAsset { Id = name, Parent = under, Image = image, Alpha = image, Width = image ? RigTemplates.DefaultPartSize : 0f, Height = image ? RigTemplates.DefaultPartSize : 0f };
        int at = under.Length == 0 ? rig.Nodes.Count : SubtreeEnd(rig, rig.Nodes.FindIndex(n => n.Id == under));
        Mutate(MotionChangeKind.Document, () => rig.Nodes.Insert(at, node));
        return node;
    }

    /// <summary>노드를 지운다 — 그 밑의 노드도 함께. 그 노드를 쓰던 채널·스킨 칸은 남고 검증이 알린다(되살리면 다시 맞는다).</summary>
    public void RemoveRigNode(string kind, string id)
    {
        RigAsset rig = RequireRig(kind);
        int index = RequireNodeIndex(rig, id);
        int end = SubtreeEnd(rig, index);
        Mutate(MotionChangeKind.Document, () => rig.Nodes.RemoveRange(index, end - index));
    }

    /// <summary>
    /// 노드의 이름을 바꾼다 — 그 이름을 가리키던 것이 같은 칸에서 따라온다: 자식의 부모, 그 리그의 모션 채널, 스킨의 조각 칸, 이모지의 자리 노드, 그 리그에 붙은 오브젝트의 붙는 노드.
    /// </summary>
    public void RenameRigNode(string kind, string id, string newId)
    {
        RigAsset rig = RequireRig(kind);
        RigNodeAsset node = rig.Nodes[RequireNodeIndex(rig, id)];

        if (string.Equals(id, newId?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string name = RequireNodeId(rig, newId);

        // 그 리그의 모션에 새 이름의 채널이 이미 있으면(지운 노드의 흔적) 합칠 수 없다.
        if (Document.Motions.Any(m => m.Rig == kind && m.Channels.Any(c => c.Node == name)))
        {
            throw new InvalidOperationException($"리그 '{kind}'의 모션에 노드 '{name}'의 채널이 이미 있다 — 먼저 그 채널을 지운다.");
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            node.Id = name;

            foreach (RigNodeAsset child in rig.Nodes.Where(n => n.Parent == id))
            {
                child.Parent = name;
            }

            foreach (ChannelAsset channel in Document.Motions.Where(m => m.Rig == kind).SelectMany(m => m.Channels).Where(c => c.Node == id))
            {
                channel.Node = name;
            }

            foreach (SkinAsset skin in Document.Skins.Where(s => s.Rig == kind))
            {
                if (skin.Parts.Remove(id, out string? image))
                {
                    skin.Parts[name] = image;
                }

                if (skin.Setups.Remove(id, out SkinPartSetup? setup))
                {
                    skin.Setups[name] = setup;
                }
            }

            foreach (EmojiAsset emoji in Document.Emojis.Where(e => e.Rig == kind && e.AnchorNode == id))
            {
                emoji.AnchorNode = name;
            }

            foreach (SceneAsset scene in Document.Scenes)
            {
                foreach (SceneObject o in scene.Objects.Where(o => o.AttachNode == id && scene.ParentOf(o)?.Rig == kind))
                {
                    o.AttachNode = name;
                }
            }
        });
    }

    /// <summary>노드의 값(위치 · 크기 · 그림을 그리는가 · 불투명도를 받는가)을 고친다. id와 부모는 따로(<see cref="RenameRigNode"/> · <see cref="SetRigNodeParent"/>).</summary>
    public void UpdateRigNode(string kind, string id, RigNodeAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RigAsset rig = RequireRig(kind);
        RigNodeAsset node = rig.Nodes[RequireNodeIndex(rig, id)];

        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Width) || !float.IsFinite(value.Height) || value.Width < 0f || value.Height < 0f)
        {
            throw new InvalidOperationException("노드의 위치는 수, 크기는 0 이상의 수다.");
        }

        if (node.X == value.X && node.Y == value.Y && node.Width == value.Width && node.Height == value.Height && node.Image == value.Image && node.Alpha == value.Alpha)
        {
            return;
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            node.X = value.X;
            node.Y = value.Y;
            node.Width = value.Width;
            node.Height = value.Height;
            node.Image = value.Image;
            node.Alpha = value.Alpha;
        });
    }

    /// <summary>노드를 다른 부모 밑으로 옮긴다(비면 루트) — 밑의 노드도 함께, 새 부모의 식구들 맨 뒤로. 제 밑으로는 못 옮긴다.</summary>
    public void SetRigNodeParent(string kind, string id, string? parent)
    {
        RigAsset rig = RequireRig(kind);
        int index = RequireNodeIndex(rig, id);
        string under = parent ?? "";

        if (rig.Nodes[index].Parent == under)
        {
            return;
        }

        int end = SubtreeEnd(rig, index);
        List<RigNodeAsset> subtree = rig.Nodes.GetRange(index, end - index);

        if (under.Length > 0 && rig.Find(under) is null)
        {
            throw new KeyNotFoundException($"부모 노드 '{under}'가 리그 '{kind}'에 없다.");
        }

        if (subtree.Any(n => n.Id == under))
        {
            throw new InvalidOperationException($"노드 '{id}'를 제 밑('{under}')으로 옮길 수 없다.");
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Nodes.RemoveRange(index, subtree.Count);
            subtree[0].Parent = under;
            int at = under.Length == 0 ? rig.Nodes.Count : SubtreeEnd(rig, rig.Nodes.FindIndex(n => n.Id == under));
            rig.Nodes.InsertRange(at, subtree);
        });
    }

    /// <summary>형제 사이에서 순서를 옮긴다(−1 = 앞으로 = 아래에 그려진다, +1 = 뒤로 = 위에) — 밑의 노드도 함께. 끝이면 아무 일도 없다.</summary>
    public void MoveRigNode(string kind, string id, int direction)
    {
        RigAsset rig = RequireRig(kind);
        int index = RequireNodeIndex(rig, id);
        string parent = rig.Nodes[index].Parent;
        List<int> siblings = Enumerable.Range(0, rig.Nodes.Count).Where(i => rig.Nodes[i].Parent == parent).ToList();
        int place = siblings.IndexOf(index) + Math.Sign(direction);

        if (direction == 0 || place < 0 || place >= siblings.Count)
        {
            return;
        }

        // 앞쪽 식구와 뒤쪽 식구(각각 밑의 노드까지)를 맞바꾼다 — 형제는 이어져 있다.
        int first = Math.Min(index, siblings[place]);
        int second = Math.Max(index, siblings[place]);
        int secondEnd = SubtreeEnd(rig, second);
        List<RigNodeAsset> front = rig.Nodes.GetRange(first, second - first);
        List<RigNodeAsset> back = rig.Nodes.GetRange(second, secondEnd - second);

        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Nodes.RemoveRange(first, secondEnd - first);
            rig.Nodes.InsertRange(first, back.Concat(front));
        });
    }

    /// <summary>그 노드의 식구(밑의 노드 전부)가 끝나는 자리 — 부모가 자식보다 먼저 오므로 식구는 이어져 있다.</summary>
    private static int SubtreeEnd(RigAsset rig, int index)
    {
        var family = new HashSet<string>(StringComparer.Ordinal) { rig.Nodes[index].Id };
        int end = index + 1;

        while (end < rig.Nodes.Count && family.Contains(rig.Nodes[end].Parent))
        {
            family.Add(rig.Nodes[end].Id);
            end++;
        }

        return end;
    }

    private static int RequireNodeIndex(RigAsset rig, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int index = rig.Nodes.FindIndex(n => string.Equals(n.Id, id, StringComparison.Ordinal));
        return index >= 0 ? index : throw new KeyNotFoundException($"노드 '{id}'가 리그 '{rig.Kind}'에 없다.");
    }

    private static readonly Regex NodeIdRule = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static string RequireNodeId(RigAsset rig, string? id)
    {
        string name = (id ?? "").Trim();

        if (!NodeIdRule.IsMatch(name))
        {
            throw new InvalidOperationException($"노드 이름은 영문자로 시작하는 영문·숫자·_ 다 ('{id}')");
        }

        if (rig.Find(name) is not null)
        {
            throw new InvalidOperationException($"노드 '{name}'가 리그 '{rig.Kind}'에 이미 있다.");
        }

        return name;
    }

    private static void RequireBuildable(RigAsset rig)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { RigAssetCodec.RootId };

        foreach (RigNodeAsset node in rig.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id) || node.Id.Contains('/', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"노드 id가 비었거나 '/'가 들었다 ('{node.Id}')");
            }

            if (!string.IsNullOrEmpty(node.Parent) && !seen.Contains(node.Parent))
            {
                throw new InvalidOperationException($"노드 '{node.Id}'의 부모 '{node.Parent}'가 앞에 없다 — 부모가 자식보다 먼저 와야 한다.");
            }

            if (!seen.Add(node.Id))
            {
                throw new InvalidOperationException($"노드 '{node.Id}'가 둘이다.");
            }
        }
    }

    private static RigAsset CloneRig(RigAsset rig) => new()
    {
        Kind = rig.Kind,
        Nodes = rig.Nodes.Select(n => new RigNodeAsset { Id = n.Id, Parent = n.Parent ?? "", X = n.X, Y = n.Y, Width = n.Width, Height = n.Height, Image = n.Image, Alpha = n.Alpha }).ToList(),
    };

    private RigAsset RequireRig(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Document.FindRig(kind) ?? throw new KeyNotFoundException($"리그 '{kind}'가 문서에 없다(덤프의 리그는 툴에서 고치지 않는다).");
    }
}
