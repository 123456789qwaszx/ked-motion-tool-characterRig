using Avalonia.Controls;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App;

// 붙는 리그(PLAN-parts.md P2·P3) — 하이어아키의 오브젝트 우클릭 메뉴에서 만든다.
// 캐릭터가 아닌 리그(이모지 등)를 고른 오브젝트의 노드에 붙여 세운다: 부모가 움직이면 따라가고, 하이어아키에서는 부모 밑에 보인다.
public partial class MainWindow
{
    /// <summary>테스트 손잡이 — "붙는 리그 만들기 ▸ rigKind". 만든 오브젝트의 id.</summary>
    internal string CreateAttachedFromMenu(string rig, string parentId) => CreateAttached(rig, parentId);

    /// <summary>테스트 손잡이 — 붙여 만들 수 있는 rigKind들(메뉴에 나오는 것).</summary>
    internal IReadOnlyList<string> AttachableRigs => AttachableRigKinds();

    /// <summary>덤프의 리그 중 붙여 만들 것 — 캐릭터(제 층에 서는 것)와 카메라의 가짜 리그를 뺀 나머지.</summary>
    private List<string> AttachableRigKinds()
        => (_session.Rig?.RigKinds ?? Enumerable.Empty<string>())
            .Where(k => k != SceneObject.DefaultRig && k != CameraRig.RigKind)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    private void AddAttachMenu(SceneAsset scene, SceneObject o)
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header, FontSize = 11 };
            item.Click += (_, _) => Guard(header, action);
            return item;
        }

        if (scene.ParentOf(o) is { } parent)
        {
            _hierarchyMenu.Items.Add(Item($"떼기 ({parent.Id}에서)", () => _session.Editor.AttachObject(null, o.Id, null)));
            _hierarchyMenu.Items.Add(new Separator());
            return;
        }

        List<string> rigs = AttachableRigKinds();
        var create = new MenuItem { Header = $"붙는 리그 만들기 — {o.Id}에", FontSize = 11, IsEnabled = rigs.Count > 0 };
        ToolTip.SetTip(create, "이 오브젝트를 따라가는 리그(이모지 등)를 만들어 붙인다 — 슬롯 계층의 끝(CharSlot_Scale)에. 덤프에 캐릭터가 아닌 리그가 있어야 한다");

        foreach (string rig in rigs)
        {
            string captured = rig;
            create.Items.Add(Item(rig, () => CreateAttached(captured, o.Id)));
        }

        _hierarchyMenu.Items.Add(create);
        _hierarchyMenu.Items.Add(new Separator());
    }

    /// <summary>
    /// 리그를 만들어 부모에 붙인다 — 되돌리기 한 번. 붙는 노드는 부모 리그에 <see cref="SceneObject.DefaultAttachNode"/>가 있으면 그것, 없으면 루트.
    /// 만든 것을 고른다(바로 스킨을 입히고 모션을 만든다).
    /// </summary>
    private string CreateAttached(string rig, string parentId)
    {
        MotionEditor editor = _session.Editor;
        SceneObject parent = _viewport.Scene?.Find(parentId) ?? throw new KeyNotFoundException($"오브젝트 '{parentId}'가 없다.");
        string id = NewObjectId(rig);
        string? node = _session.Rig is { } dump && dump.NodeIds.TryGetValue(parent.Rig, out ICollection<string>? nodes) && nodes.Contains(SceneObject.DefaultAttachNode)
            ? SceneObject.DefaultAttachNode
            : null;

        editor.Batch(MotionChangeKind.Scene, () =>
        {
            editor.AddSceneObject(null, id, rig, parent.Layer, parent.Stage);
            editor.AttachObject(null, id, parentId, node);
        });

        _viewport.SelectedObjectId = id;
        RefreshAssets();
        return id;
    }
}
