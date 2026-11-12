using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Avalonia.VisualTree;

namespace Mt.App;

// 씬 — 하이어아키(오브젝트 · 컨트롤러 · 노드의 트리와 그 메뉴 · 단축키)와 오른쪽 씬 탭(배치 · 멈춘 카메라).
public partial class MainWindow
{
    /// <summary>씬 탭 — 고른 오브젝트의 배치(모션 키가 아니다). 자주 쓰는 자리·층은 펼쳐 두고, 시작 배치·붙이기·카메라는 접는다(기본값이 아니면 펼친다).</summary>
    private Control BuildSceneTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(_objSummary);
        ToolTip.SetTip(_objSummary, "고른 오브젝트 · 리그 · 무대 층. 이름 바꾸기는 하이어아키에서 F2");

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("자리"), _objPlace, Small("focus"), _objPlaceFocus },
        });
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Small("배역"), _objCharacter } });
        ToolTip.SetTip(_objCharacter, "배역 키(런타임 cast의 characterKey) — 기준점 표(presets/focus.json)의 그 배역 값으로 자리·깊이를 푼다. 비우면 표의 기본값");
        OnCommit(_objCharacter, text =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o)
            {
                Guard("배역", () => _session.Editor.SetObjectCharacter(null, o.Id, text));
            }
        });
        ToolTip.SetTip(_objPlace, "place — focus 프리셋 점이 이 화면 지점에 오도록 CharSlot_Track_Focus를 푼다(코어 place 폴드, 런타임과 같은 함수). 없으면 Track_Focus는 0");
        ToolTip.SetTip(_objPlaceFocus, "place가 맞추는 focus 프리셋(bust 기본)");
        _objPlace.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o && _objPlace.SelectedItem is string chosen)
            {
                string? point = chosen == NoPlaceToken ? null : chosen;

                if (point != o.Place)
                {
                    Guard("자리", () => _session.Editor.SetObjectPlace(null, o.Id, point, o.PlaceFocus));
                }
            }
        };
        _objPlaceFocus.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { Place: not null } o && _objPlaceFocus.SelectedItem is string chosen && chosen != (o.PlaceFocus ?? SceneObject.DefaultPlaceFocus))
            {
                Guard("focus", () => _session.Editor.SetObjectPlace(null, o.Id, o.Place, chosen));
            }
        };

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("Layer"), _objLayer, Small("Stage"), _objStage, _objVisible },
        });
        ToolTip.SetTip(_objLayer, "층(far·back·mid·front·close) — 깊이의 진실. 하이어아키에서 층 폴더로 끌어다 놓아도 된다");
        _objLayer.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _objLayer.SelectedItem is string layer && _viewport.SelectedObject is { } o && o.Layer != layer)
            {
                Guard("레이어", () => _session.Editor.SetObjectLayer(null, o.Id, layer));
            }
        };
        _objStage.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _objStage.SelectedItem is string stage && _viewport.SelectedObject is { } o && o.Stage != stage)
            {
                Guard("무대", () => _session.Editor.SetObjectStage(null, o.Id, stage));
            }
        };
        _objVisible.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && _viewport.SelectedObject is { } o)
            {
                Guard("보이기", () => _session.Editor.SetObjectVisible(null, o.Id, _objVisible.IsChecked == true));
            }
        };

        _placementExpander.Content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Small("오프셋 X"), _objX, Small("Y"), _objY, Small("Rot Z"), _objRot } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Small("Scale X"), _objSx, Small("Y"), _objSy } },
            },
        };
        ToolTip.SetTip(_placementExpander, "모션이 없을 때의 자리 — CharSlot_Track(move)·SwayPivot(rotate)·Scale에 그대로 들어간다. 채널 박스의 자리 키는 여기서 출발한다. 모션 키가 아니다");
        panel.Children.Add(_placementExpander);

        foreach (TextBox box in new[] { _objX, _objY, _objRot, _objSx, _objSy })
        {
            OnCommit(box, _ => CommitObjectPlacement());
        }

        // id 칸은 화면에 없다(소유자, 2026-10-28): 이름 바꾸기는 하이어아키 F2. 컨트롤은 테스트 손잡이로만 남는다.
        ToolTip.SetTip(_objId, "이름 바꾸기 — [a-z0-9_]+");
        OnCommit(_objId, _ => RenameSelectedObject());
        _cameraExpander.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Small("zoom"), _camZoom, Small("pan X"), _camPanX, Small("Y"), _camPanY },
        };
        ToolTip.SetTip(_cameraExpander, "무대 카메라(샷 커맨드 shot_zoom·shot_track) — 뼈대의 StageZoom/StagePan에 들어간다. 뷰포트 확대(휠·MMB)와는 다르다");
        ToolTip.SetTip(_camZoom, "샷 zoom — 1당 배율 +0.05(코어 규약). 보기만 바뀐다");
        panel.Children.Add(_cameraExpander);

        foreach (TextBox box in new[] { _camZoom, _camPanX, _camPanY })
        {
            OnCommit(box, _ => CommitCamera());
        }

        _viewport.ObjectSelected += (_, _) => RefreshObjects();
        return panel;
    }

    /// <summary>오브젝트 목록·채널 박스·노드 목록을 선택 오브젝트에 맞춘다.</summary>
    private void RefreshObjects()
    {
        _refreshDepth++;

        try
        {
            RebuildHierarchy();

            SceneObject? selected = _viewport.SelectedObject;
            bool has = selected is not null;

            foreach (Control c in new Control[] { _objX, _objY, _objRot, _objSx, _objSy, _objVisible, _objId, _objLayer, _objStage, _objPlace })
            {
                c.IsEnabled = has;
            }

            _objPlaceFocus.IsEnabled = has && selected!.Place is not null;

            _objSummary.Text = selected is null
                ? "오브젝트 없음 — 왼쪽 하이어아키에서 층(Content)을 우클릭해 만든다"
                : $"{selected.Id} · {selected.Rig} · {StageNodes.ContentOf(selected.Stage, selected.Layer)}"
                  + (_viewport.Motion is null || _viewport.CanWear(selected) ? "" : " · 지금 모션은 이 리그에 안 맞는다");
            _objId.Text = selected?.Id ?? "";
            _objCharacter.Text = selected?.Character ?? "";
            _objCharacter.IsEnabled = has;
            _objStage.SelectedIndex = selected is null ? -1 : StageNodes.Stages.ToList().IndexOf(selected.Stage);

            var places = new List<string> { NoPlaceToken };
            places.AddRange(ScenePreview.ScreenPoints);
            _objPlace.ItemsSource = places;
            _objPlace.SelectedIndex = selected?.Place is { } place && places.IndexOf(place) >= 0 ? places.IndexOf(place) : 0;
            _objPlaceFocus.SelectedIndex = selected is null ? -1 : Math.Max(0, ScenePreview.FocusPresets.ToList().IndexOf(selected.PlaceFocus ?? SceneObject.DefaultPlaceFocus));

            _objX.Text = selected is null ? "" : F(selected.PositionX);
            _objY.Text = selected is null ? "" : F(selected.PositionY);
            _objRot.Text = selected is null ? "" : F(selected.RotationZ);
            _objSx.Text = selected is null ? "" : F(selected.ScaleX);
            _objSy.Text = selected is null ? "" : F(selected.ScaleY);
            _objLayer.SelectedIndex = selected is null ? -1 : SceneLayers.IndexOf(selected.Layer);
            _objVisible.IsChecked = selected?.Visible ?? false;
            CameraSettings? camera = _viewport.Scene?.Camera;
            _camZoom.Text = camera is null ? "" : F(camera.Zoom);
            _camPanX.Text = camera is null ? "" : F(camera.PanX);
            _camPanY.Text = camera is null ? "" : F(camera.PanY);

            // 접힌 묶음은 값이 기본이 아닐 때만 스스로 펼친다(닫지는 않는다).
            _placementExpander.IsExpanded |= selected is not null && (selected.PositionX != 0 || selected.PositionY != 0 || selected.RotationZ != 0 || selected.ScaleX != 1 || selected.ScaleY != 1);
            _cameraExpander.IsExpanded |= camera is not null && (camera.Zoom != 0 || camera.PanX != 0 || camera.PanY != 0);
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshChannelBox();
    }

    // ── 하이어아키 ─────────────────────────────────────────────────────

    /// <summary>트리 항목의 꼬리표 — 오브젝트 또는 오브젝트의 리그 노드.</summary>
    private sealed record HierarchyNode(string ObjectId, string? NodeId);

    /// <summary>폴더 항목의 꼬리표 — Scene(둘 다 null) · stage · layer(Content). 만들기·드롭의 자리가 된다.</summary>
    private sealed record HierarchyFolder(string? Stage, string? Layer);

    /// <summary>오브젝트 밑의 컨트롤러 항목(기본 보기) — 표의 컨트롤러 하나. 고르면 그 주 노드가 채널 박스·매니퓰레이터의 노드.</summary>
    private sealed record HierarchyController(string ObjectId, string ControllerId);

    /// <summary>컨트롤러 묶음 폴더(자리·깊이·연기) — 보기만. 만들기·드롭은 그 오브젝트의 자식.</summary>
    private sealed record HierarchyControllerGroup(string ObjectId, string GroupId);

    /// <summary>무대 뼈대의 다른 노드(Shot/Pan/Zoom/Mask/Framing…) — 보기만, 자리가 아니다.</summary>
    private sealed record SkeletonNode(string NodeId);

    /// <summary>
    /// 유니티 <c>PresentataionUIRoot</c> 그대로: 뼈대 노드를 덤프의 parent대로 중첩하고, 오브젝트는 그 무대·층의
    /// <c>Stage{NN}Depth_{Layer}_Content</c> 밑에, 리그 노드는 덤프의 parent 그대로.
    /// 압축 보기는 뼈대를 Stage › 층(Content)만 남긴다(데이터는 같다). 펼침 상태는 키로 기억한다(오브젝트는 기본 접힘).
    /// </summary>
    private void RebuildHierarchy()
    {
        SceneAsset? scene = _viewport.Scene;
        bool nodeWasSelected = _hierarchy.SelectedItem is TreeViewItem { Tag: HierarchyNode { NodeId: not null } or HierarchyController };
        _hierarchy.Items.Clear();
        _hierarchyParents.Clear();

        if (scene is null)
        {
            return;
        }

        TreeViewItem? selectedItem = null;
        var root = Folder($"Scene  {scene.Name}  ·  PresentataionUIRoot", "scene");
        root.Tag = new HierarchyFolder(null, null);
        Attach(root, null);

        // 뼈대: 덤프 순서(부모 먼저)로 중첩 — 유니티 PresentataionUIRoot 그대로. 오브젝트가 선 가지(Content까지의 조상)만 기본 펼침,
        // 나머지 가지는 기본 접힘(유니티처럼 필요할 때 펼친다).
        IReadOnlyList<RigSchemaNodeDto> skeleton = BuiltinTuning.StageRig.nodes;
        var parentOf = skeleton.ToDictionary(n => n.id, n => string.IsNullOrEmpty(n.parent) ? RigSchemaLoader.RootKey : n.parent, StringComparer.Ordinal);
        var occupied = new HashSet<string>(StringComparer.Ordinal);

        foreach (SceneObject o in scene.Objects)
        {
            // Content에서 루트까지 올라가며 표시한다. __root는 자기 자신을 부모로 가리키므로 거기서 멈춘다.
            for (string id = StageNodes.ContentOf(o.Stage, o.Layer); id != RigSchemaLoader.RootKey && parentOf.ContainsKey(id); id = parentOf[id])
            {
                occupied.Add(id);
            }
        }

        var itemsByNode = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal) { [RigSchemaLoader.RootKey] = root };
        var contentItems = new Dictionary<(string Stage, string Layer), TreeViewItem>();

        foreach (RigSchemaNodeDto node in skeleton)
        {
            if (node.id == RigSchemaLoader.RootKey)
            {
                continue;
            }

            bool isStageRoot = StageNodes.Stages.Any(st => StageNodes.StageRootOf(st) == node.id);
            bool isContent = StageNodes.TryParseContent(node.id, out string stage, out string layer);

            // 기본은 Scene › 무대 › 층만 — 뼈대의 다른 노드(Shot/Pan/Zoom/Mask/Framing…)는 "무대 뼈대 보기"를 켜야 보인다.
            if (!_settings.ShowStageSkeleton && !isStageRoot && !isContent)
            {
                continue;
            }

            TreeViewItem parentItem = root;

            for (string up = parentOf[node.id]; up != RigSchemaLoader.RootKey && parentOf.ContainsKey(up); up = parentOf[up])
            {
                if (itemsByNode.TryGetValue(up, out TreeViewItem? found))
                {
                    parentItem = found;
                    break;
                }
            }

            string header = _settings.ShowStageSkeleton ? node.id
                : isStageRoot ? StageNodes.StageName(StageNodes.Stages.First(st => StageNodes.StageRootOf(st) == node.id))
                : layer;

            if (node.id == StageNodes.ZoomRoot)
            {
                header += $"  ·  zoom {scene.Camera.Zoom:0.##}";
            }
            else if (node.id == StageNodes.PanRoot)
            {
                header += $"  ·  pan {scene.Camera.PanX:0},{scene.Camera.PanY:0}";
            }

            TreeViewItem item = Folder(header, "skel:" + node.id, defaultExpanded: occupied.Contains(node.id));
            item.Tag = isContent ? new HierarchyFolder(stage, layer)
                : isStageRoot ? new HierarchyFolder(StageNodes.Stages.First(st => StageNodes.StageRootOf(st) == node.id), null)
                : new SkeletonNode(node.id);
            Attach(item, parentItem);
            itemsByNode[node.id] = item;

            if (isContent)
            {
                contentItems[(stage, layer)] = item;
            }
        }

        foreach (SceneObject o in scene.Objects)
        {
            TreeViewItem under = contentItems.GetValueOrDefault((o.Stage, o.Layer)) ?? root;
            BuildObjectItem(scene, o, under, nodeWasSelected, ref selectedItem);
        }

        _hierarchy.Items.Add(root);

        // 고른 항목이 보이도록 조상을 펼친다(유니티가 선택을 드러내듯).
        for (TreeViewItem? up = selectedItem is null ? null : _hierarchyParents.GetValueOrDefault(selectedItem); up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            up.IsExpanded = true;
        }

        _hierarchy.SelectedItem = selectedItem;
    }

    private void Attach(TreeViewItem item, TreeViewItem? parent)
    {
        _hierarchyParents[item] = parent;
        parent?.Items.Add(item);
    }

    /// <summary>오브젝트 항목 + 컨트롤러(또는 리그 노드) 트리.</summary>
    private void BuildObjectItem(SceneAsset scene, SceneObject o, TreeViewItem parentItem, bool nodeWasSelected, ref TreeViewItem? selectedItem)
    {
        string kind = o.Rig;
        string suffix = o.Visible ? "" : "  ·  숨김";
        TreeViewItem objectItem = Folder($"{o.Id}  ·  {kind}{suffix}", "object:" + o.Id, defaultExpanded: false);
        objectItem.Tag = new HierarchyNode(o.Id, null);
        objectItem.FontWeight = FontWeight.SemiBold;
        objectItem.Opacity = o.Visible ? 1 : 0.55;
        Attach(objectItem, parentItem);

        var itemsByNode = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal);
        ControllerRig? table = _settings.ShowRigNodes ? null : _session.ControllersOf(o.Rig);

        if (table is not null)
        {
            // 기본 보기: 자리 › 깊이 › 연기 폴더, 그 안이 컨트롤러(리거의 표).
            Controller? selectedController = nodeWasSelected && o.Id == _viewport.SelectedObjectId && _viewport.SelectedNode is { } selectedNode ? table.OfNode(selectedNode) : null;

            foreach (ControllerGroup group in table.Groups)
            {
                TreeViewItem groupItem = Folder($"{group.Label}  ·  {MotionModeLabel.Of(group.Mode)}", $"group:{o.Id}/{group.Id}");
                groupItem.Tag = new HierarchyControllerGroup(o.Id, group.Id);
                groupItem.FontWeight = FontWeight.Normal;
                groupItem.Opacity = 0.8;
                ToolTip.SetTip(groupItem, group.Note ?? group.Label);
                Attach(groupItem, objectItem);

                foreach (Controller c in table.InGroup(group))
                {
                    string axes = string.Concat(c.Axes.Select(a => a.ToString()));
                    TreeViewItem item = Folder($"{c.Label}  ·  {ChannelKindLabel.Of(c.Kind)} {axes}", $"controller:{o.Id}/{c.Id}");
                    item.Tag = new HierarchyController(o.Id, c.Id);
                    item.FontWeight = FontWeight.Normal;
                    item.Foreground = group.Mode == MotionChannelMode.Absolute ? PlacementNodeBrush : ActingNodeBrush;
                    ToolTip.SetTip(item, $"{c.Node}  ·  {c.Unit}" + (c.Aliases.Count > 0 ? "\n별칭: " + string.Join(", ", c.Aliases.Select(a => $"{a.Node} {a.Axis}")) : "") + (c.Note is null ? "" : "\n" + c.Note));
                    Attach(item, groupItem);
                    itemsByNode.TryAdd(c.Node, item);

                    foreach (ControllerAlias alias in c.Aliases)
                    {
                        itemsByNode.TryAdd(alias.Node, item);
                    }

                    if (c.DepthYNode is { } depthYNode)
                    {
                        itemsByNode.TryAdd(depthYNode, item);
                    }

                    if (ReferenceEquals(c, selectedController))
                    {
                        selectedItem = item;
                    }
                }
            }
        }
        else if (_session.Rig is { } rig && rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
        {
            foreach (RigSchemaNodeDto node in dto.nodes)
            {
                if (node.id == RigSchemaLoader.RootKey)
                {
                    continue; // 오브젝트 항목이 곧 루트다.
                }

                TreeViewItem nodeParent = string.IsNullOrEmpty(node.parent) || node.parent == RigSchemaLoader.RootKey || !itemsByNode.TryGetValue(node.parent, out TreeViewItem? found)
                    ? objectItem
                    : found;
                bool forbidden = PlacementNodes.IsForbidden(node.id);
                TreeViewItem nodeItem = Folder(node.id, $"node:{o.Id}/{node.id}");
                nodeItem.Tag = new HierarchyNode(o.Id, node.id);
                nodeItem.FontWeight = FontWeight.Normal;
                nodeItem.Foreground = forbidden ? ForbiddenNodeBrush : PlacementNodes.IsPlacementAxis(node.id) ? PlacementNodeBrush : ActingNodeBrush;
                nodeItem.FontStyle = forbidden ? FontStyle.Italic : FontStyle.Normal;
                ToolTip.SetTip(nodeItem, forbidden ? "키잉 금지 노드(코어 규칙) — 채널을 둘 수 없다" : PlacementNodes.IsPlacementAxis(node.id) ? "배치 축 — 런타임 place/size가 움직인다. 모션 채널로 쓰면 겹칠 수 있다" : "연기 축 — 모션 채널의 자리");
                Attach(nodeItem, nodeParent);
                itemsByNode[node.id] = nodeItem;

                if (nodeWasSelected && o.Id == _viewport.SelectedObjectId && node.id == _viewport.SelectedNode)
                {
                    selectedItem = nodeItem;
                }
            }
        }

        if (selectedItem is null && o.Id == _viewport.SelectedObjectId && !nodeWasSelected)
        {
            selectedItem = objectItem;
        }
    }

    private TreeViewItem Folder(string header, string key, bool defaultExpanded = true)
    {
        bool expanded = defaultExpanded ? !_collapsedHierarchy.Contains(key) : _expandedHierarchy.Contains(key);
        var item = new TreeViewItem { Header = header, FontSize = 11, IsExpanded = expanded };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty)
            {
                if (item.IsExpanded == defaultExpanded)
                {
                    _collapsedHierarchy.Remove(key);
                    _expandedHierarchy.Remove(key);
                }
                else if (defaultExpanded)
                {
                    _collapsedHierarchy.Add(key);
                }
                else
                {
                    _expandedHierarchy.Add(key);
                }
            }
        };
        return item;
    }

    /// <summary>오브젝트의 노드 항목을 전부 펼치거나 접는다(유니티 Alt+클릭).</summary>
    private void ExpandObjectNodes(string objectId, bool expand)
    {
        foreach (TreeViewItem item in _hierarchyParents.Keys.Where(i => ObjectIdOf(i.Tag) == objectId).ToList())
        {
            item.IsExpanded = expand;
        }
    }

    // ── 하이어아키 메뉴·단축키 ──────────────────────────────────────────

    private static TreeViewItem? ItemUnder(object? source)
        => (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);

    private void OnHierarchyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(_hierarchy).Properties;
        TreeViewItem? item = ItemUnder(e.Source);

        if (props.IsRightButtonPressed)
        {
            _menuTarget = item?.Tag;

            if (item?.Tag is HierarchyNode or HierarchyController)
            {
                _hierarchy.SelectedItem = item;
            }

            return;
        }
    }

    private void OnHierarchyKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers == KeyModifiers.Control;
        bool ctrlShift = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

        switch (e.PhysicalKey)
        {
            case PhysicalKey.Delete when e.KeyModifiers == KeyModifiers.None:
                Guard("오브젝트 삭제", RemoveObject);
                break;
            case PhysicalKey.D when ctrl:
                Guard("복제", DuplicateSelectedObject);
                break;
            case PhysicalKey.N when ctrlShift:
                Guard("캐릭터 리그 만들기", () => CreateObject(SceneObject.DefaultRig, _hierarchy.SelectedItem is TreeViewItem { Tag: { } tag } ? tag : null));
                break;
            case PhysicalKey.F2 when e.KeyModifiers == KeyModifiers.None:
                _objId.Focus();
                _objId.SelectAll();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>유니티 하이어아키 우클릭 메뉴. 우클릭한 자리(_menuTarget)에 따라 달라진다.</summary>
    private void RebuildHierarchyMenu()
    {
        _hierarchyMenu.Items.Clear();
        SceneAsset? scene = _viewport.Scene;
        object? target = _menuTarget;
        SceneObject? o = ObjectIdOf(target) is { } targetId && scene is not null ? scene.Find(targetId) : null;

        MenuItem Item(string header, Action action, KeyGesture? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, FontSize = 11, InputGesture = gesture, IsEnabled = enabled };
            item.Click += (_, _) => Guard(header, action);
            _hierarchyMenu.Items.Add(item);
            return item;
        }

        if (o is not null)
        {
            Item("이름 바꾸기", () =>
            {
                _objId.Focus();
                _objId.SelectAll();
            }, new KeyGesture(Key.F2));
            Item("복제", DuplicateSelectedObject, new KeyGesture(Key.D, KeyModifiers.Control));
            Item("삭제", RemoveObject, new KeyGesture(Key.Delete));
            _hierarchyMenu.Items.Add(new Separator());
            Item(o.Visible ? "숨기기" : "보이기", () => _session.Editor.SetObjectVisible(null, o.Id, !o.Visible));

            var layer = new MenuItem { Header = "레이어", FontSize = 11 };

            foreach (string l in SceneLayers.Order)
            {
                var pick = new MenuItem { Header = l + (l == o.Layer ? "  ✓" : ""), FontSize = 11 };
                pick.Click += (_, _) => Guard("레이어", () => _session.Editor.SetObjectLayer(null, o.Id, l));
                layer.Items.Add(pick);
            }

            _hierarchyMenu.Items.Add(layer);
            _hierarchyMenu.Items.Add(new Separator());
        }

        string where = target switch
        {
            HierarchyFolder { Stage: { } fs, Layer: { } fl } => $" — {StageNodes.ContentOf(fs, fl)}에",
            HierarchyFolder { Stage: { } fs } => $" — {StageNodes.StageName(fs)} Mid에",
            _ when o is not null => $" — {StageNodes.ContentOf(o.Stage, o.Layer)}에",
            _ => "",
        };
        Item("캐릭터 리그 만들기" + where, () => CreateObject(SceneObject.DefaultRig, target), new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), enabled: target is not SkeletonNode);

        if (o is not null)
        {
            _hierarchyMenu.Items.Add(new Separator());
            Item("노드 전부 펼치기", () => ExpandObjectNodes(o.Id, true));
            Item("노드 전부 접기", () => ExpandObjectNodes(o.Id, false));
        }
    }

    /// <summary>비어 있는 다음 id — character는 s1·s2…(Yarn 슬롯 키 꼴), 그 밖은 rigKind+번호. 채널 박스에서 바꾼다.</summary>
    private string NewObjectId(string rig)
    {
        string stem = rig == SceneObject.DefaultRig ? "s" : MotionNames.Sanitize(rig);
        SceneAsset? scene = _viewport.Scene;
        int n = 1;

        while (scene?.Find($"{stem}{n}") is not null)
        {
            n++;
        }

        return $"{stem}{n}";
    }

    /// <summary>
    /// 만들기 — 자리(target)가 층(Content) 폴더면 그 무대·층에, 오브젝트/노드/컨트롤러면 그 오브젝트의 무대·층에,
    /// 그 밖이면 선택 오브젝트의 무대·층(없으면 Stage00 Mid)에. 만든 것을 고른다.
    /// </summary>
    private string CreateObject(string rig, object? target, string? id = null)
    {
        SceneObject? beside = ObjectIdOf(target) is { } besideId ? _viewport.Scene?.Find(besideId) : _viewport.SelectedObject;
        string layer = beside?.Layer ?? SceneLayers.Mid;
        string stage = beside?.Stage ?? SceneObject.DefaultStage;

        switch (target)
        {
            case HierarchyFolder { Stage: { } fs, Layer: { } l }:
                stage = fs;
                layer = l;
                break;
            case HierarchyFolder { Stage: { } fs }:
                stage = fs;
                layer = SceneLayers.Mid;
                break;
        }

        id ??= NewObjectId(rig);
        _session.Editor.AddSceneObject(null, id, rig, layer, stage);
        _viewport.SelectedObjectId = id;
        RefreshAssets();
        return id;
    }

    private void OnHierarchySelected()
    {
        if (_refreshing)
        {
            return;
        }

        switch (_hierarchy.SelectedItem)
        {
            case TreeViewItem { Tag: HierarchyNode { NodeId: null } node }:
                _viewport.SelectedObjectId = node.ObjectId;
                break;
            case TreeViewItem { Tag: HierarchyNode node }:
                SelectNodeInHierarchy(node);
                break;
            case TreeViewItem { Tag: HierarchyController controller }:
                SelectControllerInHierarchy(controller);
                break;
            case TreeViewItem { Tag: HierarchyControllerGroup group }:
                _viewport.SelectedObjectId = group.ObjectId;
                break;
        }
    }

    /// <summary>항목 꼬리표가 가리키는 오브젝트 — 오브젝트·노드·컨트롤러·묶음. 폴더·뼈대는 null.</summary>
    private static string? ObjectIdOf(object? tag) => tag switch
    {
        HierarchyNode n => n.ObjectId,
        HierarchyController c => c.ObjectId,
        HierarchyControllerGroup g => g.ObjectId,
        _ => null,
    };

    private Controller? ControllerOf(HierarchyController tag)
        => _session.ControllersOf(_viewport.Scene?.Find(tag.ObjectId)?.Rig)?.Find(tag.ControllerId);

    /// <summary>
    /// 컨트롤러 항목: 그 오브젝트를 고르고, 채널 박스·매니퓰레이터의 노드를 컨트롤러의 노드로(모션에 별칭 노드의 채널만 있으면 그 노드).
    /// 매니퓰레이터가 선택(Q)이 아니면 컨트롤러 종류의 것(이동·회전·배율)으로 바꾼다.
    /// </summary>
    private void SelectControllerInHierarchy(HierarchyController tag)
    {
        if (ControllerOf(tag) is not { } c)
        {
            _viewport.SelectedObjectId = tag.ObjectId;
            return;
        }

        _viewport.SelectedObjectId = tag.ObjectId;
        _followSelection = false;
        _viewport.SelectedNode = ControllerNodeFor(_graph.Motion, c);
        ShowControllerCurves(c);
        RefreshChannels();

        if (_viewport.Manipulator != ManipulatorMode.Select)
        {
            _viewport.Manipulator = c.Kind switch
            {
                ChannelKind.Euler => ManipulatorMode.Rotate,
                ChannelKind.Scale => ManipulatorMode.Scale,
                _ => ManipulatorMode.Move,
            };
        }

        _viewport.InvalidateVisual();
        RefreshChannelBox();
    }

    /// <summary>컨트롤러의 노드 — 모션이 주 노드에 채널이 없고 별칭 노드에만 있으면 그 별칭 노드(런타임 견본 nod의 Track_Move_Y).</summary>
    private static string ControllerNodeFor(MotionAsset? motion, Controller c)
    {
        if (motion is null || motion.FindChannel(c.Node, c.Kind) is not null)
        {
            return c.Node;
        }

        return c.Aliases.FirstOrDefault(a => motion.FindChannel(a.Node, c.Kind) is not null)?.Node ?? c.Node;
    }

    /// <summary>노드 항목: 그 오브젝트를 고르고, 채널 박스·매니퓰레이터의 노드를 그 노드로. 키 따라가기는 끈다(사용자가 직접 골랐다).</summary>
    private void SelectNodeInHierarchy(HierarchyNode node)
    {
        _viewport.SelectedObjectId = node.ObjectId;
        _followSelection = false;
        _viewport.SelectedNode = node.NodeId;
        ShowCurves(_graph.Curves.Select(s => s.Curve).Where(cr => cr.Node == node.NodeId).ToList(), additive: false);
        _viewport.InvalidateVisual();
        RefreshChannelBox();
        RefreshChannels();
    }

    /// <summary>테스트 손잡이 — 하이어아키에서 노드(또는 오브젝트)를 고른 것과 같다.</summary>
    internal void SelectInHierarchy(string objectId, string? nodeId)
    {
        var node = new HierarchyNode(objectId, nodeId);

        if (nodeId is null)
        {
            _viewport.SelectedObjectId = objectId;
        }
        else
        {
            SelectNodeInHierarchy(node);
        }
    }

    internal int HierarchyObjectCount => _viewport.SceneObjects.Count;

    /// <summary>테스트 손잡이 — 하이어아키에서 컨트롤러 항목을 고른 것과 같다.</summary>
    internal void SelectControllerInHierarchy(string objectId, string controllerId)
        => SelectControllerInHierarchy(new HierarchyController(objectId, controllerId));

    /// <summary>테스트 손잡이 — "리그 노드 보기" 설정.</summary>
    internal bool ShowRigNodes
    {
        get => _settings.ShowRigNodes;
        set
        {
            _settings.ShowRigNodes = value;
            ApplySettings();
            RebuildHierarchy();
        }
    }

    /// <summary>테스트 손잡이 — 컨트롤러 항목 위에서 "만들기"(그 오브젝트의 무대·층에).</summary>
    internal string CreateObjectAtController(string rig, string objectId, string controllerId)
        => CreateObject(rig, new HierarchyController(objectId, controllerId));

    /// <summary>테스트 손잡이 — 컨트롤러 항목의 경로(Scene부터 헤더 순). 없으면 빈 목록.</summary>
    internal IReadOnlyList<string> HierarchyPathOfController(string objectId, string controllerId)
        => PathOf(_hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyController c && c.ObjectId == objectId && c.ControllerId == controllerId));

    private void CommitCamera()
    {
        if (_refreshing || _viewport.Scene is not { } scene)
        {
            return;
        }

        if (!TryFloat(_camZoom.Text ?? "", out float zoom) || !TryFloat(_camPanX.Text ?? "", out float panX) || !TryFloat(_camPanY.Text ?? "", out float panY))
        {
            return;
        }

        Guard("카메라", () => _session.Editor.SetCamera(scene.Name, zoom, panX, panY));
    }

    internal void CommitCameraFromForm(float zoom, float panX, float panY)
    {
        _camZoom.Text = F(zoom);
        _camPanX.Text = F(panX);
        _camPanY.Text = F(panY);
        CommitCamera();
    }

    private void RemoveObject()
    {
        if (_viewport.SelectedObjectId is { } id)
        {
            _session.Editor.RemoveSceneObject(null, id);
            RefreshAssets();
        }
    }

    private void DuplicateSelectedObject()
    {
        if (_viewport.SelectedObjectId is { } id)
        {
            SceneObject copy = _session.Editor.DuplicateSceneObject(null, id);
            _viewport.SelectedObjectId = copy.Id;
            RefreshAssets();
        }
    }

    private void RenameSelectedObject()
    {
        if (_refreshing || _viewport.SelectedObject is not { } o)
        {
            return;
        }

        string newId = (_objId.Text ?? "").Trim();

        if (newId.Length == 0 || newId == o.Id)
        {
            _objId.Text = o.Id;
            return;
        }

        Guard("이름 바꾸기", () =>
        {
            _session.Editor.RenameSceneObject(null, o.Id, newId);
            _viewport.SelectedObjectId = newId;
            RefreshAssets();
        });
    }

    private void CommitObjectPlacement()
    {
        if (_refreshing || _viewport.SelectedObject is not { } o)
        {
            return;
        }

        if (!TryFloat(_objX.Text ?? "", out float x) || !TryFloat(_objY.Text ?? "", out float y) || !TryFloat(_objRot.Text ?? "", out float rot)
            || !TryFloat(_objSx.Text ?? "", out float sx) || !TryFloat(_objSy.Text ?? "", out float sy))
        {
            return;
        }

        Guard("배치", () => _session.Editor.SetObjectPlacement(null, o.Id, x, y, rot, sx, sy));
    }

    /// <summary>테스트 손잡이 — 오브젝트 추가(최상위, 그 레이어)와 채널 박스.</summary>
    internal void AddObjectFromForm(string id, string rig, string layer, string stage = SceneObject.DefaultStage)
        => CreateObject(rig, new HierarchyFolder(stage, layer), id);

    internal void SetPlaceFromForm(string? screenPoint, string? focus = null)
    {
        _objPlace.SelectedItem = screenPoint ?? NoPlaceToken;

        if (focus is not null)
        {
            _objPlaceFocus.SelectedItem = focus;
        }
    }

    /// <summary>테스트 손잡이 — 우클릭 메뉴 "만들기"(오브젝트/노드 위면 그 오브젝트의 무대·층에). 새 id를 돌려준다.</summary>
    internal string CreateObjectFromMenu(string rig, string? atObjectId, string? atNodeId)
        => CreateObject(rig, atObjectId is null ? null : new HierarchyNode(atObjectId, atNodeId));

    internal void DuplicateSelectedFromMenu() => DuplicateSelectedObject();

    internal void RenameSelectedFromForm(string newId)
    {
        _objId.Text = newId;
        RenameSelectedObject();
    }

    /// <summary>테스트 손잡이 — 하이어아키 항목의 경로(Scene부터 헤더 순). 없으면 빈 목록.</summary>
    internal IReadOnlyList<string> HierarchyPathOf(string objectId, string? nodeId)
        => PathOf(_hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyNode n && n.ObjectId == objectId && n.NodeId == nodeId));

    private IReadOnlyList<string> PathOf(TreeViewItem? item)
    {
        var path = new List<string>();

        for (TreeViewItem? up = item; up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            path.Insert(0, up.Header?.ToString() ?? "");
        }

        return path;
    }

    internal void CommitObjectPlacementFromForm(float x, float y, float rotationZ, float scaleX, float scaleY)
    {
        _objX.Text = F(x);
        _objY.Text = F(y);
        _objRot.Text = F(rotationZ);
        _objSx.Text = F(scaleX);
        _objSy.Text = F(scaleY);
        CommitObjectPlacement();
    }
}
