using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Mt.Authoring.Model;

namespace Mt.App;

// 리그 편집기(PLAN-parts.md §12) — 툴이 정의한 리그의 노드를 하나씩 짓고 옮긴다. "이모지" 탭의 리그 칸 아래.
// 덤프의 리그(캐릭터 등)는 런타임이 원본이라 여기서 못 고친다 — 목록만 비어 있다.
public partial class MainWindow
{
    private const string FollowRigChoice = "(고른 이모지·오브젝트의 리그)";
    private const string RootParentChoice = "(루트)";

    private readonly ComboBox _rigPick = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _rigNodeList = new() { FontSize = 11, MaxHeight = 170 };
    private readonly StackPanel _rigNodeFields = new() { Spacing = 4 };
    private readonly TextBox _rigNodeId = new() { FontSize = 11 };
    private readonly ComboBox _rigNodeParent = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _rigNodeX = new() { FontSize = 11 };
    private readonly TextBox _rigNodeY = new() { FontSize = 11 };
    private readonly TextBox _rigNodeW = new() { FontSize = 11 };
    private readonly TextBox _rigNodeH = new() { FontSize = 11 };
    private readonly CheckBox _rigNodeImage = new() { Content = "그림을 그린다(조각)", FontSize = 11 };
    private readonly CheckBox _rigNodeAlpha = new() { Content = "불투명도를 받는다", FontSize = 11 };

    /// <summary>리그 칸에서 직접 고른 리그 — null이면 고른 이모지·오브젝트의 리그를 따라간다.</summary>
    private string? _rigPicked;

    private string? _rigNodeSelected;

    /// <summary>테스트 손잡이 — 리그 편집기가 다루는 리그와 고른 노드, 목록의 줄.</summary>
    internal (string? Rig, string? Node, IReadOnlyList<string> Rows, bool Editable) RigEditorState
        => (RigInFocus, _rigNodeSelected, (_rigNodeList.ItemsSource as IEnumerable<string> ?? Array.Empty<string>()).ToList(), _rigNodeFields.IsEnabled);

    /// <summary>테스트 손잡이 — 노드 칸들.</summary>
    internal (TextBox Id, ComboBox Parent, TextBox X, TextBox Y, TextBox W, TextBox H, CheckBox Image, CheckBox Alpha) RigNodeBoxes
        => (_rigNodeId, _rigNodeParent, _rigNodeX, _rigNodeY, _rigNodeW, _rigNodeH, _rigNodeImage, _rigNodeAlpha);

    internal void SelectRigNode(string? id)
    {
        _rigNodeSelected = id;
        RefreshRigEditor();
    }

    internal string NewRigFromForm() => NewRig();

    internal string? AddRigNodeFromForm(bool image) => AddRigNode(image);

    internal void RemoveRigNodeFromForm() => RemoveSelectedRigNode();

    internal void MoveRigNodeFromForm(int direction) => MoveSelectedRigNode(direction);

    private RigAsset? EditedRig => _session.Editor.Document.FindRig(RigInFocus);

    private RigNodeAsset? EditedRigNode => _rigNodeSelected is { } id ? EditedRig?.Find(id) : null;

    private void BuildRigEditor(StackPanel into)
    {
        into.Children.Add(Labeled("다루는 리그", _rigPick));
        ToolTip.SetTip(_rigPick, "노드를 고칠 리그 — 툴이 정의한 리그만. 비워 두면 고른 이모지·오브젝트의 리그를 따라간다");
        _rigPick.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _rigPick.SelectedIndex >= 0)
            {
                _rigPicked = _rigPick.SelectedItem as string is { } s && s != FollowRigChoice ? s : null;
                _rigNodeSelected = null;
                RefreshRigFields();
            }
        };

        var rigButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        Button newRig = SmallButton("새 리그", (_, _) => Guard("새 리그", () => NewRig()));
        ToolTip.SetTip(newRig, "빈 리그(노드 Body 하나)를 정의한다 — 이름 rig, rig_2…. 하이어아키에서 캐릭터 우클릭 → 붙는 리그 만들기로 세운다");
        rigButtons.Children.Add(newRig);
        into.Children.Add(rigButtons);

        into.Children.Add(Header("노드"));
        ToolTip.SetTip(_rigNodeList, "리그의 노드 — 부모가 자식보다 먼저, 뒤의 것이 위에 그려진다. ▣ 그림 노드(조각) · α 불투명도를 받는다");
        _rigNodeList.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && EditedRig is { } rig && _rigNodeList.SelectedIndex >= 0 && _rigNodeList.SelectedIndex < rig.Nodes.Count)
            {
                _rigNodeSelected = rig.Nodes[_rigNodeList.SelectedIndex].Id;
                RefreshRigEditor();
            }
        };
        into.Children.Add(_rigNodeList);

        var nodeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        Button add = SmallButton("노드", (_, _) => Guard("노드 추가", () => AddRigNode(image: false)));
        Button addImage = SmallButton("그림 노드", (_, _) => Guard("그림 노드 추가", () => AddRigNode(image: true)));
        Button remove = SmallButton("삭제", (_, _) => Guard("노드 삭제", RemoveSelectedRigNode));
        Button up = SmallButton("▲", (_, _) => Guard("노드 순서", () => MoveSelectedRigNode(-1)));
        Button down = SmallButton("▼", (_, _) => Guard("노드 순서", () => MoveSelectedRigNode(+1)));
        ToolTip.SetTip(add, "고른 노드 밑에(없으면 루트 밑에) 묶는 노드를 더한다 — 움직임을 따로 주는 자리");
        ToolTip.SetTip(addImage, "고른 노드 밑에 그림 노드(조각)를 더한다 — 스킨이 여기에 그림을 준다");
        ToolTip.SetTip(remove, "고른 노드와 그 밑의 노드를 지운다. 그 노드를 쓰던 채널·스킨 칸은 남고 문제 목록이 알린다");
        ToolTip.SetTip(up, "형제 사이에서 앞으로 — 아래에 그려진다");
        ToolTip.SetTip(down, "형제 사이에서 뒤로 — 위에 그려진다");

        foreach (Button b in new[] { add, addImage, remove, up, down })
        {
            nodeButtons.Children.Add(b);
        }

        into.Children.Add(nodeButtons);

        _rigNodeFields.Children.Add(Labeled("이름", _rigNodeId));
        _rigNodeFields.Children.Add(Labeled("부모", _rigNodeParent));
        _rigNodeFields.Children.Add(Labeled("X", _rigNodeX));
        _rigNodeFields.Children.Add(Labeled("Y", _rigNodeY));
        _rigNodeFields.Children.Add(Labeled("너비", _rigNodeW));
        _rigNodeFields.Children.Add(Labeled("높이", _rigNodeH));
        _rigNodeFields.Children.Add(_rigNodeImage);
        _rigNodeFields.Children.Add(_rigNodeAlpha);
        into.Children.Add(_rigNodeFields);
        ToolTip.SetTip(_rigNodeId, "노드 이름(영문자로 시작하는 영문·숫자·_) — 바꾸면 그 리그의 모션 채널·스킨 칸·이모지의 자리 노드가 따라온다");
        ToolTip.SetTip(_rigNodeParent, "부모 노드 — 밑의 노드도 함께 옮겨진다");
        ToolTip.SetTip(_rigNodeX, "부모 가운데에서의 위치(px) — 가만히 선 자세. 모션은 여기에 얹힌다");
        ToolTip.SetTip(_rigNodeW, "사각형의 크기(px) — 그림 노드면 그림이 이 안에 종횡비로 맞춰진다. 묶는 노드는 0");

        OnCommit(_rigNodeId, text =>
        {
            if (!_refreshing && RigInFocus is { } kind && EditedRigNode is { } node && text.Trim() != node.Id)
            {
                Guard("노드 이름", () =>
                {
                    _session.Editor.RenameRigNode(kind, node.Id, text);
                    _rigNodeSelected = text.Trim();
                    RefreshRigEditor();
                });
            }
        });
        _rigNodeParent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && RigInFocus is { } kind && EditedRigNode is { } node && _rigNodeParent.SelectedItem is string chosen)
            {
                Guard("노드 부모", () => _session.Editor.SetRigNodeParent(kind, node.Id, chosen == RootParentChoice ? null : chosen));
            }
        };

        foreach (TextBox box in new[] { _rigNodeX, _rigNodeY, _rigNodeW, _rigNodeH })
        {
            OnCommit(box, _ => CommitRigNode());
        }

        _rigNodeImage.IsCheckedChanged += (_, _) => CommitRigNode();
        _rigNodeAlpha.IsCheckedChanged += (_, _) => CommitRigNode();
    }

    private void CommitRigNode()
    {
        if (_refreshing || RigInFocus is not { } kind || EditedRigNode is not { } node)
        {
            return;
        }

        var value = new RigNodeAsset
        {
            X = TryFloat(_rigNodeX.Text ?? "", out float x) ? x : node.X,
            Y = TryFloat(_rigNodeY.Text ?? "", out float y) ? y : node.Y,
            Width = TryFloat(_rigNodeW.Text ?? "", out float w) ? w : node.Width,
            Height = TryFloat(_rigNodeH.Text ?? "", out float h) ? h : node.Height,
            Image = _rigNodeImage.IsChecked == true,
            Alpha = _rigNodeAlpha.IsChecked == true,
        };
        Guard("노드 값", () => _session.Editor.UpdateRigNode(kind, node.Id, value));
    }

    private string NewRig()
    {
        string kind = "rig";

        for (int i = 2; _session.Rig?.RigKinds.Contains(kind) == true || _session.Editor.Document.FindRig(kind) is not null; i++)
        {
            kind = $"rig_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.AddRig(new RigAsset { Kind = kind, Nodes = { new RigNodeAsset { Id = "Body", Alpha = true } } });
        _rigPicked = kind;
        _rigNodeSelected = "Body";
        RefreshRigFields();
        return kind;
    }

    private string? AddRigNode(bool image)
    {
        if (RigInFocus is not { } kind || EditedRig is not { } rig)
        {
            return null;
        }

        string stem = image ? "Part" : "Node";
        string id = stem + "1";

        for (int i = 2; rig.Find(id) is not null; i++)
        {
            id = stem + i.ToString(CultureInfo.InvariantCulture);
        }

        _session.Editor.AddRigNode(kind, id, EditedRigNode?.Id, image);
        _rigNodeSelected = id;
        RefreshRigEditor();
        return id;
    }

    private void RemoveSelectedRigNode()
    {
        if (RigInFocus is { } kind && EditedRigNode is { } node)
        {
            string? parent = node.Parent.Length > 0 ? node.Parent : null;
            _session.Editor.RemoveRigNode(kind, node.Id);
            _rigNodeSelected = parent;
            RefreshRigEditor();
        }
    }

    private void MoveSelectedRigNode(int direction)
    {
        if (RigInFocus is { } kind && EditedRigNode is { } node)
        {
            _session.Editor.MoveRigNode(kind, node.Id, direction);
        }
    }

    private static int DepthOf(RigAsset rig, RigNodeAsset node)
    {
        int depth = 0;

        for (RigNodeAsset? at = node; at is { Parent.Length: > 0 } && depth < 64; at = rig.Find(at.Parent))
        {
            depth++;
        }

        return depth;
    }

    private void RefreshRigEditor()
    {
        _refreshDepth++;

        try
        {
            var picks = new List<string> { FollowRigChoice };
            picks.AddRange(_session.Editor.Document.Rigs.Select(r => r.Kind).OrderBy(k => k, StringComparer.Ordinal));

            if (_rigPicked is not null && !picks.Contains(_rigPicked))
            {
                _rigPicked = null; // 지워졌다(되돌리기 등)
            }

            _rigPick.ItemsSource = picks;
            _rigPick.SelectedIndex = _rigPicked is null ? 0 : picks.IndexOf(_rigPicked);

            RigAsset? rig = EditedRig;

            if (rig?.Find(_rigNodeSelected ?? "") is null)
            {
                _rigNodeSelected = null;
            }

            _rigNodeList.ItemsSource = rig?.Nodes
                .Select(n => $"{new string(' ', DepthOf(rig, n) * 3)}{(n.Image ? "▣ " : "· ")}{n.Id}{(n.Alpha ? "  α" : "")}")
                .ToList() ?? new List<string>();
            _rigNodeList.SelectedIndex = rig is null || _rigNodeSelected is null ? -1 : rig.Nodes.FindIndex(n => n.Id == _rigNodeSelected);
            _rigNodeList.IsEnabled = rig is not null;

            RigNodeAsset? node = EditedRigNode;
            _rigNodeFields.IsEnabled = node is not null;

            void Write(TextBox box, string text)
            {
                if (!box.IsFocused)
                {
                    box.Text = text;
                }
            }

            Write(_rigNodeId, node?.Id ?? "");
            Write(_rigNodeX, node is null ? "" : F(node.X));
            Write(_rigNodeY, node is null ? "" : F(node.Y));
            Write(_rigNodeW, node is null ? "" : F(node.Width));
            Write(_rigNodeH, node is null ? "" : F(node.Height));
            _rigNodeImage.IsChecked = node?.Image == true;
            _rigNodeAlpha.IsChecked = node?.Alpha == true;

            // 부모로 고를 수 있는 것 — 제 식구(밑의 노드)가 아닌 것.
            var parents = new List<string> { RootParentChoice };

            if (rig is not null && node is not null)
            {
                var family = new HashSet<string>(StringComparer.Ordinal) { node.Id };

                foreach (RigNodeAsset n in rig.Nodes)
                {
                    if (family.Contains(n.Parent))
                    {
                        family.Add(n.Id);
                    }
                    else if (n.Id != node.Id)
                    {
                        parents.Add(n.Id);
                    }
                }
            }

            _rigNodeParent.ItemsSource = parents;
            _rigNodeParent.SelectedIndex = node is null ? -1 : node.Parent.Length == 0 ? 0 : parents.IndexOf(node.Parent);
        }
        finally
        {
            _refreshDepth--;
        }
    }
}
