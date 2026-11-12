using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.App;

// 그래프 왼쪽의 채널 목록(아웃라이너) — 컨트롤러 · 노드별 곡선 줄, "키 없음" 줄, 줄을 눌러 곡선을 보이고 고르는 일.
public partial class MainWindow
{
    /// <summary>
    /// 마야 그래프 에디터 아웃라이너 — 노드 › 채널 › 성분. 행 클릭 = 그 커브만 보인다, Shift/Ctrl+클릭 = 더하기/빼기.
    /// 없는 성분은 흐리고, 클릭하면 현재 시각의 중립값 키 하나로 생긴다.
    /// </summary>
    /// <summary>
    /// 아웃라이너(그래프 왼쪽) — 모션의 리그에 컨트롤러 표가 있으면 **컨트롤러 전부**(10개)를 표 순서 그대로 보인다(순서는 바뀌지 않는다):
    /// 고른 컨트롤러는 굵게, 키가 없는 것은 흐리게. 컨트롤러 밑은 표가 쓰는 성분만(B). 흐린 성분을 클릭해도 성분을 만들지 않는다(A2, 마야식) —
    /// 성분은 사용자가 키를 놓을 때만 생긴다. 표가 없는 리그·"리그 노드 보기"는 모션의 채널을 노드로 보인다.
    /// </summary>
    private void RefreshChannels()
    {
        _channelRows.Children.Clear();
        _outlinerOrder.Clear();
        MotionAsset? motion = _graph.Motion;

        if (motion is null)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(모션 없음)", FontSize = 11, Opacity = 0.6, Margin = new Avalonia.Thickness(6, 4) });
            return;
        }

        string query = (_outlinerSearch.Text ?? "").Trim();
        ControllerRig? table = _settings.ShowRigNodes ? null : _session.ControllersOf(motion.Rig);

        if (table is null)
        {
            RefreshChannelsByNode(motion, query);
            return;
        }

        Controller? selected = SelectedControllerOf(motion, table);

        foreach (Controller c in table.Controllers)
        {
            List<CurveRef> curves = OutlinerCurvesOf(motion, c);
            List<CurveRef> existing = curves.Where(cr => cr.Resolve(motion) is not null).ToList();
            bool keyed = existing.Count > 0;
            bool isSelected = ReferenceEquals(c, selected);
            string title = $"{c.Group.Label} · {c.Label}";

            if (query.Length > 0 && !title.Contains(query, StringComparison.OrdinalIgnoreCase) && !c.Id.Contains(query, StringComparison.OrdinalIgnoreCase) && !c.Node.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _outlinerOrder.Add(c.Id);
            Controller captured = c;
            List<CurveRef> capturedExisting = existing;
            bool anyLocked = existing.Any(_flags.IsLocked);
            bool anyMuted = existing.Any(_flags.IsMuted);

            var header = new DockPanel { Margin = new Avalonia.Thickness(2, 3, 2, 0), Opacity = keyed || isSelected ? 1 : 0.5 };
            Button remove = SmallButton("×", (_, _) => Guard("컨트롤러 키 삭제", () =>
            {
                MotionAsset? m = _graph.Motion;

                if (m is null)
                {
                    return;
                }

                _session.Editor.Batch(MotionChangeKind.Structure, () =>
                {
                    foreach (CurveRef cr in capturedExisting)
                    {
                        RemoveComponent(m, cr);
                    }
                });
            }));
            remove.IsEnabled = keyed;
            ToolTip.SetTip(remove, "이 컨트롤러의 키 전부 삭제(채널이 없어진다)");
            Button lockButton = SmallButton(anyLocked ? "🔒" : "🔓", (_, _) =>
            {
                foreach (CurveRef cr in capturedExisting)
                {
                    _flags.SetLocked(cr, !anyLocked);
                }
            });
            lockButton.IsEnabled = keyed;
            ToolTip.SetTip(lockButton, anyLocked ? "잠금 풀기 — 다시 고르고 끌 수 있다" : "잠금 — 보이되 고르거나 끌 수 없다");
            Button muteButton = SmallButton(anyMuted ? "🔇" : "🔊", (_, _) =>
            {
                foreach (CurveRef cr in capturedExisting)
                {
                    _flags.SetMuted(cr, !anyMuted);
                }
            });
            muteButton.IsEnabled = keyed;
            ToolTip.SetTip(muteButton, anyMuted ? "뮤트 풀기" : "뮤트 — 뷰포트 재생에서 이 컨트롤러를 뺀다(저장되지 않는다)");

            foreach (Button b in new[] { remove, muteButton, lockButton })
            {
                DockPanel.SetDock(b, Dock.Right);
                header.Children.Add(b);
            }

            var titleBlock = new TextBlock
            {
                Text = title + (keyed ? "" : "  (키 없음)"),
                FontSize = 11,
                FontWeight = isSelected ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = c.Group.Mode == MotionChannelMode.Absolute ? PlacementNodeBrush : ActingNodeBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(titleBlock, $"{c.Node}" + (c.Note is null ? "" : "\n" + c.Note) + "\n클릭: 이 컨트롤러를 고르고 그 커브만 · Shift/Ctrl: 더하기 · S: 지금 값을 현재 시각에 키");
            titleBlock.PointerPressed += (_, e) =>
            {
                bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

                if (additive)
                {
                    ShowCurves(capturedExisting, additive: true);
                }

                SelectController(captured, showCurves: !additive);
                e.Handled = true;
            };
            header.Children.Add(titleBlock);
            _channelRows.Children.Add(header);

            // 키가 없는 컨트롤러는 이름 한 줄만 — 성분 줄은 키가 있거나 고른 것에만 편다(순서는 표 그대로다. 키가 있는 줄을 찾느라 내리지 않게).
            if (!keyed && !isSelected)
            {
                continue;
            }

            foreach (CurveRef curveRef in curves)
            {
                string label = $"{ChannelKindLabel.Of(curveRef.Kind)} {curveRef.Axis.ToString().ToUpperInvariant()}" + (curveRef.Node == c.Node ? "" : $"  · {curveRef.Node}");
                _channelRows.Children.Add(ComponentRow(motion, curveRef, label, () => SelectController(captured)));
            }
        }
    }

    /// <summary>표가 없는 리그·리그 노드 보기 — 모션의 채널을 노드 · 종류로, 성분은 종류의 축 전부(없는 것은 흐리게).</summary>
    private void RefreshChannelsByNode(MotionAsset motion, string query)
    {
        ControllerRig? labels = _session.ControllersOf(motion.Rig);

        // 이모지의 타임라인은 만드는 사람의 말로 적는다(EmojiWords) — 노드 이름은 툴팁에 남는다.
        bool emoji = EmojiAsset.IsTimelineName(motion.Name, out _);

        // 이모지에서 그림을 골랐으면 그 그림의 것만 적는다(고르지 않았으면 전부).
        HashSet<string>? scope = emoji ? EmojiGraphNodes() : null;

        foreach (ChannelAsset channel in motion.Channels)
        {
            string node = channel.Node;

            if (scope is not null && !scope.Contains(node))
            {
                continue;
            }
            ChannelKind kind = channel.Kind;
            string nodeLabel = emoji ? EmojiWords.Target(node) : DopeSheetRows.NodeLabel(node, labels);
            string kindLabel = emoji ? EmojiWords.Kind(kind) : ChannelKindLabel.Of(kind);

            if (query.Length > 0 && !node.Contains(query, StringComparison.OrdinalIgnoreCase) && !nodeLabel.Contains(query, StringComparison.OrdinalIgnoreCase) && !kindLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<Axis> possibleAxes = ChannelKinds.Axes(kind);
            CurveRef[] axesOfChannel = possibleAxes.Select(a => new CurveRef(node, kind, a)).ToArray();
            CurveRef[] existing = axesOfChannel.Where(cr => cr.Resolve(motion) is not null).ToArray();
            bool anyLocked = existing.Any(_flags.IsLocked);
            bool anyMuted = existing.Any(_flags.IsMuted);

            var header = new DockPanel { Margin = new Avalonia.Thickness(2, 3, 2, 0) };
            Button remove = SmallButton("×", (_, _) => Guard("채널 삭제", () => _session.Editor.RemoveChannel(motion.Name, node, kind)));
            ToolTip.SetTip(remove, "채널 삭제(성분 전부)");
            Button lockButton = SmallButton(anyLocked ? "🔒" : "🔓", (_, _) =>
            {
                foreach (CurveRef cr in existing)
                {
                    _flags.SetLocked(cr, !anyLocked);
                }
            });
            ToolTip.SetTip(lockButton, anyLocked ? "잠금 풀기 — 다시 고르고 끌 수 있다" : "잠금 — 보이되 고르거나 끌 수 없다");
            Button muteButton = SmallButton(anyMuted ? "🔇" : "🔊", (_, _) =>
            {
                foreach (CurveRef cr in existing)
                {
                    _flags.SetMuted(cr, !anyMuted);
                }
            });
            ToolTip.SetTip(muteButton, anyMuted ? "뮤트 풀기" : "뮤트 — 뷰포트 재생에서 이 채널을 뺀다(저장되지 않는다)");

            foreach (Button b in new[] { remove, muteButton, lockButton })
            {
                DockPanel.SetDock(b, Dock.Right);
                header.Children.Add(b);
            }

            var title = new TextBlock
            {
                Text = $"{nodeLabel} · {kindLabel}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(title, $"{node}\n{kind}\n클릭: 이 채널의 커브만 · Shift/Ctrl: 더하기");
            title.PointerPressed += (_, e) => ShowCurves(existing, additive: e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
            header.Children.Add(title);
            _channelRows.Children.Add(header);

            foreach (CurveRef curveRef in axesOfChannel)
            {
                _channelRows.Children.Add(ComponentRow(motion, curveRef, emoji ? EmojiWords.Component(kind, curveRef.Axis) : $"{kindLabel} {curveRef.Axis.ToString().ToUpperInvariant()}", null));
            }
        }

        AddUnkeyedRows(motion, query, labels, scope);

        if (motion.Channels.Count == 0)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(채널 없음 — 하이어아키에서 노드를 고르고 채널 박스에 값을 넣거나 매니퓰레이터로 끌면 생긴다)", FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(6, 4) });
        }
    }

    /// <summary>작은 리그에서만 키 없는 채널을 늘어놓는다 — 캐릭터 리그는 노드가 많아 목록이 묻힌다(그쪽은 컨트롤러 표가 그 일을 한다).</summary>
    private const int UnkeyedRowsMaxNodes = 16;

    /// <summary>테스트 손잡이 — 그래프 목록에 적힌 글자들(채널의 머리글 · 성분 · "키 없음" 줄), 위에서 아래로.</summary>
    internal IReadOnlyList<string> ChannelRowTexts
    {
        get
        {
            var texts = new List<string>();

            void Walk(Control control)
            {
                switch (control)
                {
                    case TextBlock { Text: { Length: > 0 } text }:
                        texts.Add(text);
                        break;
                    case Panel panel:
                        foreach (Control child in panel.Children)
                        {
                            Walk(child);
                        }

                        break;
                    case Decorator { Child: { } child }:
                        Walk(child);
                        break;
                    case ContentControl { Content: Control content }:
                        Walk(content);
                        break;
                }
            }

            Walk(_channelRows);
            return texts;
        }
    }

    /// <summary>테스트 손잡이 — 그래프 목록의 "키 없음" 줄들(노드 · 종류).</summary>
    internal IReadOnlyList<(string Node, ChannelKind Kind)> UnkeyedRows => _unkeyedRows.Select(t => (t.Node, t.Kind)).ToList();

    internal void KeyUnkeyedRow(string node, ChannelKind kind) => _unkeyedRows.First(t => t.Node == node && t.Kind == kind).Key();

    /// <summary>"키 없음" 줄 하나 — 노드 · 종류 · 보이는 이름, ＋(지금 값에 키)와 이름 클릭(고르기)의 손.</summary>
    private sealed record UnkeyedTarget(string Node, ChannelKind Kind, string Label, Action Key, Action Pick);

    private readonly List<UnkeyedTarget> _unkeyedRows = new();

    /// <summary>
    /// 아직 키가 없는 채널들을 흐리게 — 이 모션이 안 건드리는 노드·종류(위치·배율·회전)도 보이고, 눌러서 그 노드를 고르거나 ＋로 지금 값에 키를 놓는다.
    /// "배율만 있는 모션에 살짝 위치도" 같은 손을 목록에서 바로 간다. 이모지의 타임라인이면 키를 받는 노드와 이모지의 종류만.
    /// </summary>
    private void AddUnkeyedRows(MotionAsset motion, string query, ControllerRig? labels, HashSet<string>? scope = null)
    {
        _unkeyedRows.Clear();
        IReadOnlyList<UnkeyedTarget>? targets = EmojiAsset.IsTimelineName(motion.Name, out string emojiName)
            ? EmojiUnkeyedTargets(emojiName)
            : RigUnkeyedTargets(motion, labels);

        if (targets is null)
        {
            return;
        }

        bool headed = false;

        foreach (UnkeyedTarget target in targets)
        {
            if (motion.FindChannel(target.Node, target.Kind) is not null || (scope is not null && !scope.Contains(target.Node)))
            {
                continue;
            }

            string kindLabel = EmojiAsset.IsTimelineName(motion.Name, out _) ? EmojiWords.Kind(target.Kind) : ChannelKindLabel.Of(target.Kind);

            if (query.Length > 0 && !target.Node.Contains(query, StringComparison.OrdinalIgnoreCase) && !target.Label.Contains(query, StringComparison.OrdinalIgnoreCase) && !kindLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!headed)
            {
                headed = true;
                _channelRows.Children.Add(new TextBlock { Text = "키 없음", FontSize = 10, Opacity = 0.55, Margin = new Avalonia.Thickness(4, 8, 2, 0) });
            }

            _unkeyedRows.Add(target);
            var row = new DockPanel { Margin = new Avalonia.Thickness(2, 1, 2, 0), Opacity = 0.5 };
            Button add = SmallButton("＋", (_, _) => Guard("키 놓기", target.Key));
            ToolTip.SetTip(add, "지금 값을 현재 시각에 키 — 채널이 생긴다. 그 뒤 그래프·뷰포트에서 고친다");
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(add);
            var title = new TextBlock
            {
                Text = $"{target.Label} · {kindLabel}",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(title, $"{target.Node}\n{target.Kind} — 이 모션에 아직 키가 없다\n클릭: 이 노드를 고른다 · ＋: 지금 값에 키");
            title.PointerPressed += (_, e) =>
            {
                target.Pick();
                e.Handled = true;
            };
            row.Children.Add(title);
            _channelRows.Children.Add(row);
        }
    }

    /// <summary>캐릭터 모션의 "키 없음" 줄들 — 리그 덤프의 노드마다 위치 · 배율 · 회전. 노드가 너무 많으면 null.</summary>
    private IReadOnlyList<UnkeyedTarget>? RigUnkeyedTargets(MotionAsset motion, ControllerRig? labels)
    {
        if (_session.Rig is not { } rig || !rig.NodeIds.TryGetValue(motion.Rig, out ICollection<string>? nodes) || nodes.Count > UnkeyedRowsMaxNodes)
        {
            return null;
        }

        var targets = new List<UnkeyedTarget>();

        foreach (string node in nodes)
        {
            if (MotionRules.ForbiddenNodes.Contains(node) || node == RigSchemaLoader.RootKey)
            {
                continue;
            }

            string nodeLabel = DopeSheetRows.NodeLabel(node, labels);

            foreach (ChannelKind kind in new[] { ChannelKind.Position, ChannelKind.Scale, ChannelKind.Euler })
            {
                string capturedNode = node;
                ChannelKind capturedKind = kind;
                targets.Add(new UnkeyedTarget(
                    node,
                    kind,
                    nodeLabel,
                    () => KeyUnkeyed(capturedNode, capturedKind),
                    () =>
                    {
                        _viewport.SelectedNode = capturedNode;
                        RefreshChannelBox();
                    }));
            }
        }

        return targets;
    }

    /// <summary>키 없는 채널에 지금 값을 지금 시각의 키로 — 채널이 생긴다. 뷰포트의 길(클립 안이면 그 문맥의 시각과 시작값)로 놓는다.</summary>
    private void KeyUnkeyed(string node, ChannelKind kind)
    {
        _viewport.SelectedNode = node;
        _viewport.KeyComponents(node, kind, kind == ChannelKind.Euler ? new[] { Axis.Z } : new[] { Axis.X, Axis.Y });
    }

    /// <summary>성분 행: 색 네모 + 이름 + (있으면) ×. 보이는 행은 파랗게(마야 선택). 없는 성분은 흐리고, 클릭해도 만들지 않는다 — <paramref name="onMissingClick"/>만.</summary>
    private Border ComponentRow(MotionAsset motion, CurveRef curveRef, string label, Action? onMissingClick)
    {
        CurveSlot? slot = _graph.Curves.FirstOrDefault(s => s.Curve == curveRef);
        bool exists = curveRef.Resolve(motion) is not null;
        bool visible = slot?.Visible == true;
        Color color = slot?.Color ?? Color.FromRgb(0x80, 0x80, 0x80);
        var content = new DockPanel { Opacity = exists ? 1 : 0.45 };

        if (exists)
        {
            Button remove = SmallButton("×", (_, e) =>
            {
                Guard("성분 삭제", () => RemoveComponent(_graph.Motion ?? motion, curveRef));
                e.Handled = true;
            });
            ToolTip.SetTip(remove, "이 성분의 키만 삭제 — 다른 성분은 남는다(마지막 성분이면 채널이 없어진다)");
            DockPanel.SetDock(remove, Dock.Right);
            content.Children.Add(remove);
        }

        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new Border { Width = 8, Height = 8, Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
            },
        });

        var row = new Border
        {
            Padding = new Avalonia.Thickness(18, 1, 4, 1),
            Background = visible ? OutlinerRowSelected : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = curveRef,
            Child = content,
        };
        ToolTip.SetTip(row, exists
            ? "클릭: 이 커브만 · Shift/Ctrl+클릭: 더하기/빼기 · ×: 이 성분의 키 삭제"
            : "성분이 없다 — 채널 박스에 값을 넣거나 매니퓰레이터로 끌거나 S로 키를 놓으면 생긴다(클릭은 만들지 않는다)");
        row.PointerPressed += (_, e) =>
        {
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            OutlinerClick(curveRef, additive, onMissingClick);
            e.Handled = true;
        };
        return row;
    }

    /// <summary>컨트롤러의 아웃라이너 성분들 — 표가 쓰는 성분만(B): 성분마다 모션에 있는 커브(주 노드·별칭), 없으면 주 커브 하나(흐림). 깊이는 배율 X·Y + 깊이 Y.</summary>
    private static List<CurveRef> OutlinerCurvesOf(MotionAsset motion, Controller c)
    {
        var curves = new List<CurveRef>();

        if (c.Bake == ControllerBake.DepthLevel)
        {
            curves.AddRange(ControllerCurves(c, Axis.X));
            return curves;
        }

        foreach (Axis axis in c.Axes)
        {
            List<CurveRef> existing = c.CurvesOf(axis).Where(cr => cr.Resolve(motion) is not null).ToList();
            curves.AddRange(existing.Count > 0 ? existing : new[] { c.Primary(axis) });
        }

        return curves;
    }

    /// <summary>고른 컨트롤러 — 뷰포트의 선택 노드(하이어아키·아웃라이너·W/E/R이 정한다)가 속한 컨트롤러.</summary>
    private Controller? SelectedControllerOf(MotionAsset motion, ControllerRig? table = null)
    {
        table ??= _session.ControllersOf(motion.Rig);
        return table is not null && _viewport.SelectedNode is { } node ? table.OfNode(node) : null;
    }

    /// <summary>
    /// 아웃라이너에서 컨트롤러를 고른다 — 하이어아키에서 고른 것과 같다(선택 노드·채널 박스·하이어아키 항목). 그래프는 그 컨트롤러의 커브만 보이고,
    /// 키가 없으면 **빈 그래프**다(전에 보던 커브를 남기지 않는다).
    /// </summary>
    private void SelectController(Controller c, bool showCurves = true)
    {
        _followSelection = false;
        _viewport.SelectedNode = ControllerNodeFor(_graph.Motion, c);
        SyncHierarchySelection(c);

        if (showCurves)
        {
            ShowControllerCurves(c);
        }

        _viewport.InvalidateVisual();
        RefreshChannelBox();
        RefreshChannels();
    }

    /// <summary>그래프에 이 컨트롤러의 커브만 — 모션에 없으면 전부 숨긴다(빈 그래프).</summary>
    private void ShowControllerCurves(Controller c)
    {
        MotionAsset? motion = _graph.Motion;
        List<CurveRef> existing = motion is null ? new List<CurveRef>() : OutlinerCurvesOf(motion, c).Where(cr => cr.Resolve(motion) is not null).ToList();
        ShowCurves(existing, additive: false);
    }

    private void SyncHierarchySelection(Controller c)
    {
        string? objectId = _viewport.SelectedObjectId;
        TreeViewItem? item = _hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyController hc && hc.ObjectId == objectId && hc.ControllerId == c.Id);

        if (item is null)
        {
            return;
        }

        _refreshDepth++;

        try
        {
            _hierarchy.SelectedItem = item;
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>성분 하나의 키를 지운다 — 다른 성분은 남고, 마지막 성분이면 채널이 없어진다(빈 채널은 규약에 없다).</summary>
    private void RemoveComponent(MotionAsset motion, CurveRef curve)
    {
        if (motion.FindChannel(curve.Node, curve.Kind) is not { } channel || channel.Get(curve.Axis) is null)
        {
            return;
        }

        int remaining = new[] { Axis.X, Axis.Y, Axis.Z }.Count(a => channel.Get(a) is not null);

        if (remaining <= 1)
        {
            _session.Editor.RemoveChannel(motion.Name, curve.Node, curve.Kind);
            return;
        }

        _session.Editor.SetKeys(motion.Name, curve.Node, curve.Kind, curve.Axis, Array.Empty<KeyAsset>());
    }

    /// <summary>
    /// 아웃라이너 성분 행 클릭 — 있는 성분은 그 커브만(additive면 토글). 없는 성분은 **만들지 않는다**(A2, 마야식):
    /// 컨트롤러 보기면 그 컨트롤러를 고르고, 노드 보기면 아무 일도 없다.
    /// </summary>
    internal void OutlinerClick(CurveRef curve, bool additive, Action? onMissingClick = null)
    {
        MotionAsset? motion = _graph.Motion;

        if (motion is null)
        {
            return;
        }

        if (curve.Resolve(motion) is null)
        {
            if (onMissingClick is not null)
            {
                onMissingClick();
            }
            else if (_session.ControllersOf(motion.Rig)?.OfNode(curve.Node) is { } c && !_settings.ShowRigNodes)
            {
                SelectController(c);
            }

            return;
        }

        if (additive)
        {
            bool wasVisible = _graph.Curves.FirstOrDefault(s => s.Curve == curve)?.Visible == true;
            _graph.SetVisible(curve, !wasVisible);
            return;
        }

        _graph.SetVisibleAll(c => c == curve);
    }

    /// <summary>
    /// S(Set Key) — 키 없는 포즈(드래그·채널 박스·MMB 스크럽)가 있으면 그것을 키로(뷰포트). 없으면 고른 컨트롤러의 성분 전부를
    /// 지금 값으로 현재 시각에 키(마야 Set Key on selected). 컨트롤러가 없으면 뷰포트의 선택 노드 키.
    /// </summary>
    private void SetKeySelected()
    {
        if (_viewport.HasPending || _graph.Motion is not { } motion || SelectedControllerOf(motion) is not { } c)
        {
            Guard("키 놓기", () => _session.Editor.Batch(MotionChangeKind.Keys, _viewport.SetKey)); // 키 없는 포즈 전부가 되돌리기 한 칸
            return;
        }

        Guard("키 놓기", () => _session.Editor.Batch(MotionChangeKind.Keys, () =>
        {
            if (c.Bake == ControllerBake.DepthLevel)
            {
                KeyController(c, c.Axes[0]);
                return;
            }

            foreach (Axis axis in c.Axes)
            {
                KeyController(c, axis);
            }
        }));
    }

    /// <summary>테스트 손잡이 — 아웃라이너의 컨트롤러 목록(표 순서; 컨트롤러 보기일 때; 검색으로 걸러진 것은 빠진다).</summary>
    internal IReadOnlyList<string> OutlinerOrder => _outlinerOrder;

    /// <summary>테스트 손잡이 — 성분 행의 ×.</summary>
    internal void RemoveComponentFromOutliner(CurveRef curve)
    {
        if (_graph.Motion is { } motion)
        {
            RemoveComponent(motion, curve);
        }
    }

    /// <summary>테스트 손잡이 — S.</summary>
    internal void SetKeyFromShell() => SetKeySelected();

    private void ShowCurves(IReadOnlyCollection<CurveRef> curves, bool additive)
    {
        if (additive)
        {
            _graph.SetVisibleAll(c => curves.Contains(c) || (_graph.Curves.FirstOrDefault(s => s.Curve == c)?.Visible == true));
            return;
        }

        _graph.SetVisibleAll(curves.Contains);
    }
}
