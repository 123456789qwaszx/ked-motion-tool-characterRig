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
