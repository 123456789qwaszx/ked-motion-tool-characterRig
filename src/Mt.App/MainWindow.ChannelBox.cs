using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.App;

// 채널 박스 탭 — 고른 노드 · 컨트롤러의 값을 보이고, 값을 넣으면 지금 시각에 키가 된다(마야 채널 박스).
public partial class MainWindow
{
    /// <summary>테스트 손잡이 — 채널 박스 칸에 값을 넣고 Enter.</summary>
    internal void SetChannelBoxValue(ChannelKind kind, Axis axis, string text) => CommitChannelBox(kind, axis, text);

    /// <summary>테스트 손잡이 — 채널 박스 칸의 글자.</summary>
    internal string ChannelBoxValue(ChannelKind kind, Axis axis) => _channelBoxRows[(kind, axis)].Box.Text ?? "";

    /// <summary>테스트 손잡이 — 채널 박스 ●(이 성분을 지금 키).</summary>
    internal void KeyChannelBoxComponent(ChannelKind kind, Axis axis) => KeyChannelBox(kind, axis);

    /// <summary>채널 박스가 보여 주는 노드.</summary>
    internal string? ChannelBoxNode => _viewport.SelectedNode;

    /// <summary>테스트 손잡이 — 컨트롤러 칸(자리·깊이·연기)에 값을 넣고 Enter.</summary>
    internal void SetControllerValue(string controllerId, Axis axis, string text)
    {
        Controller c = RequireController(controllerId);
        CommitController(c, axis, text);
    }

    /// <summary>테스트 손잡이 — 컨트롤러 칸의 글자.</summary>
    internal string ControllerValueText(string controllerId, Axis axis) => _controllerRows[(controllerId, axis)].Box.Text ?? "";

    /// <summary>테스트 손잡이 — 컨트롤러 ●.</summary>
    internal void KeyControllerFromBox(string controllerId, Axis axis) => KeyController(RequireController(controllerId), axis);

    /// <summary>테스트 손잡이 — 채널 박스가 컨트롤러 모양인가("ctl:character") 노드 모양인가("node").</summary>
    internal string? ChannelBoxLayout => _channelBoxLayout;

    private Controller RequireController(string id)
        => _session.ControllersOf(_viewport.SelectedObject?.Rig)?.Find(id) ?? throw new InvalidOperationException($"컨트롤러 '{id}'가 없다 — 오브젝트를 고르고 리그에 표가 있어야 한다.");

    /// <summary>채널 행의 성분 토글과 같은 길 — 없는 성분을 현재 시각의 중립값 키 하나로 만든다.</summary>
    internal void EnsureComponent(string node, ChannelKind kind, Axis axis)
    {
        MotionAsset motion = _graph.Motion ?? throw new InvalidOperationException("먼저 모션을 고른다.");

        if (motion.FindChannel(node, kind)?.Get(axis) is not null)
        {
            return;
        }

        _session.Editor.AddKey(motion.Name, node, kind, axis, new KeyAsset
        {
            T = (float)_graph.CurrentTime,
            V = ChannelKinds.Neutral(kind),
            Tangent = DefaultTangent,
        });
    }

    /// <summary>
    /// 채널 박스 — 고른 오브젝트의 리그에 컨트롤러 표가 있으면 **컨트롤러**(자리·깊이·연기)를, 없으면 고른 **노드**의 성분을 보인다.
    /// 칸 색: 이 시각에 키 = 빨강, 어딘가에 키 = 연한 빨강, 키 없는 포즈 = 주황.
    /// </summary>
    private void RefreshChannelBox()
    {
        RefreshTweenNote(); // 고른 컨트롤러가 바뀌면 트윈머신의 대상도 바뀐다
        _refreshDepth++;

        try
        {
            SceneObject? o = _viewport.SelectedObject;
            MotionAsset? motion = _graph.Motion;
            ControllerRig? table = o is null ? null : _session.ControllersOf(o.Rig);
            EnsureChannelBoxLayout(table);
            float time = (float)(_viewport.SnapEnabled ? TimeSnap.Snap(_viewport.Time, _viewport.SnapStep) : _viewport.Time);

            if (table is not null)
            {
                bool has = o is not null && _viewport.HasRig;
                _channelBoxTitle.Text = !has
                    ? "(하이어아키에서 오브젝트를 고른다)"
                    : motion is null ? $"{o!.Id} · {o.Rig} — 모션을 고르면 키를 놓을 수 있다" : $"{o!.Id} · {o.Rig}";

                foreach (((string id, Axis axis), (Border row, TextBox box, Slider? slider)) in _controllerRows)
                {
                    Controller? c = table.Find(id);
                    float? value = has && c is not null ? ControllerValue(c, axis) : null;
                    box.IsEnabled = value is not null && motion is not null;

                    if (slider is not null)
                    {
                        slider.IsEnabled = box.IsEnabled;

                        if (value is not null && !_sliderDragging && Math.Abs(slider.Value - value.Value) > 1e-4)
                        {
                            slider.Value = value.Value;
                        }
                    }

                    string text = value is null ? "" : F(value.Value);

                    if (!box.IsFocused && box.Text != text)
                    {
                        box.Text = text;
                    }

                    bool keyedHere = false;
                    bool keyed = false;
                    bool pending = false;

                    if (has && c is not null)
                    {
                        foreach (CurveRef curve in ControllerCurves(c, axis))
                        {
                            ComponentCurve? component = motion?.FindChannel(curve.Node, curve.Kind)?.Get(curve.Axis);
                            keyed |= component is not null;
                            keyedHere |= component?.Keys.Any(k => Math.Abs(k.T - time) <= MotionRules.Tolerance) == true;
                            pending |= _viewport.IsPending(curve.Node, curve.Kind, curve.Axis);
                        }
                    }

                    row.Background = pending ? ChannelBoxPending : keyedHere ? ChannelBoxKeyedHere : keyed ? ChannelBoxKeyed : Brushes.Transparent;
                }

                return;
            }

            string? node = _viewport.SelectedNode;
            bool hasNode = node is not null && o is not null && _viewport.HasRig;
            _channelBoxTitle.Text = !hasNode
                ? "(하이어아키에서 리그 노드를 고른다)"
                : motion is null ? $"{o!.Id} · {node} — 모션을 고르면 키를 놓을 수 있다" : $"{o!.Id} · {node}";

            foreach (((ChannelKind kind, Axis axis), (Border row, TextBox box)) in _channelBoxRows)
            {
                float? value = hasNode ? _viewport.ComponentValue(node!, kind, axis) : null;
                box.IsEnabled = value is not null && motion is not null;
                string text = value is null ? "" : F(value.Value);

                if (!box.IsFocused && box.Text != text)
                {
                    box.Text = text;
                }

                ComponentCurve? curve = hasNode && motion is not null ? motion.FindChannel(node!, kind)?.Get(axis) : null;
                bool keyedHere = curve?.Keys.Any(k => Math.Abs(k.T - time) <= MotionRules.Tolerance) == true;
                bool pending = hasNode && _viewport.IsPending(node!, kind, axis);
                row.Background = pending ? ChannelBoxPending : keyedHere ? ChannelBoxKeyedHere : curve is not null ? ChannelBoxKeyed : Brushes.Transparent;
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private bool _sliderDragging;

    /// <summary>채널 박스의 줄을 리그의 표에 맞춰 다시 세운다 — 컨트롤러 표가 있으면 세 섹션, 없으면 노드 성분 일곱 줄.</summary>
    private void EnsureChannelBoxLayout(ControllerRig? table)
    {
        string layout = table is null ? "node" : "ctl:" + table.RigKind;

        if (_channelBoxLayout == layout)
        {
            return;
        }

        _channelBoxLayout = layout;
        _channelBoxPanel.Children.Clear();
        _channelBoxRows.Clear();
        _controllerRows.Clear();

        if (table is null)
        {
            foreach ((ChannelKind kind, Axis axis, string label) in ChannelBoxComponents)
            {
                (Border row, TextBox box) = ChannelBoxRow(label, null, null,
                    text => CommitChannelBox(kind, axis, text),
                    () => KeyChannelBox(kind, axis),
                    additive =>
                    {
                        if (_viewport.SelectedNode is { } node && _graph.Motion?.FindChannel(node, kind)?.Get(axis) is not null)
                        {
                            ShowCurves(new[] { new CurveRef(node, kind, axis) }, additive);
                        }
                    });
                _channelBoxRows[(kind, axis)] = (row, box);
            }

            return;
        }

        foreach (ControllerGroup group in table.Groups)
        {
            bool collapsed = _collapsedGroups.Contains(group.Id);
            string groupId = group.Id;
            var header = new TextBlock
            {
                Text = $"{(collapsed ? "▸" : "▾")} {group.Label}   ·   {MotionModeLabel.Of(group.Mode)}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, 6, 0, 1),
                Foreground = group.Mode == MotionChannelMode.Absolute ? PlacementNodeBrush : ActingNodeBrush,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(header, (group.Note ?? "") + $"\n재생: <<motion slot 이름 {MotionModeToken.Of(group.Mode)}>>\n클릭: 접기/펼치기");
            header.PointerPressed += (_, e) =>
            {
                ToggleChannelBoxGroup(groupId);
                e.Handled = true;
            };
            _channelBoxPanel.Children.Add(header);

            foreach (Controller c in table.InGroup(group))
            {
                foreach (Axis axis in c.Axes)
                {
                    // 깊이는 X·Y 배율 둘이 한 손잡이(level)다 — 줄은 하나.
                    if (c.Bake == ControllerBake.DepthLevel && axis != c.Axes[0])
                    {
                        continue;
                    }

                    Controller captured = c;
                    Axis capturedAxis = axis;
                    Slider? slider = null;

                    if (c.Bake == ControllerBake.DepthLevel)
                    {
                        slider = new Slider { Minimum = DepthTuningFile.MinLevel, Maximum = DepthTuningFile.MaxLevel, Margin = new Avalonia.Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
                        ToolTip.SetTip(slider, "깊이 level — 0 far · 10 back · 14 mid · 16 front · 20 close · 40까지(close 너머는 커브 연장). 놓으면 DepthScale 배율과 DepthY(얼굴 제자리, 발은 멀수록 위로)에 굽는다");
                    }

                    string unit = c.Unit.Length == 0 ? "" : $" ({c.Unit})";
                    (Border row, TextBox box) = ChannelBoxRow(c.AttributeLabel(axis), (c.Note ?? "") + unit + $"\n{c.Node} {ChannelKindLabel.Of(c.Kind)} {axis.ToString().ToLowerInvariant()}", slider,
                        text => CommitController(captured, capturedAxis, text),
                        () => KeyController(captured, capturedAxis),
                        additive =>
                        {
                            MotionAsset? motion = _graph.Motion;
                            List<CurveRef> curves = ControllerCurves(captured, capturedAxis).Where(cr => motion?.FindChannel(cr.Node, cr.Kind)?.Get(cr.Axis) is not null).ToList();

                            if (curves.Count > 0)
                            {
                                ShowCurves(curves, additive);
                            }
                        });

                    if (slider is not null)
                    {
                        slider.AddHandler(PointerPressedEvent, (_, _) => _sliderDragging = true, RoutingStrategies.Tunnel, handledEventsToo: true);
                        slider.AddHandler(PointerReleasedEvent, (_, _) =>
                        {
                            _sliderDragging = false;
                            CommitController(captured, capturedAxis, F((float)slider.Value));
                        }, RoutingStrategies.Tunnel, handledEventsToo: true);
                        slider.ValueChanged += (_, e) =>
                        {
                            if (_sliderDragging && !_refreshing)
                            {
                                box.Text = F((float)e.NewValue);
                            }
                        };
                    }

                    row.IsVisible = !collapsed;
                    _controllerRows[(c.Id, axis)] = (row, box, slider);
                }
            }
        }
    }

    /// <summary>채널 박스 묶음 접기/펼치기 — 설정에 남는다. 접힌 묶음의 칸은 있되 안 보인다(값 입력 손잡이는 그대로).</summary>
    internal void ToggleChannelBoxGroup(string groupId)
    {
        if (!_collapsedGroups.Remove(groupId))
        {
            _collapsedGroups.Add(groupId);
        }

        _settings.CollapsedChannelBoxGroups = _collapsedGroups.OrderBy(g => g, StringComparer.Ordinal).ToList();
        _channelBoxLayout = null;
        RefreshChannelBox();
    }

    /// <summary>채널 박스 한 줄: 이름(클릭 = 그 커브를 그래프에) · 값 칸 · ●. <paramref name="extra"/>는 칸 왼쪽(깊이 슬라이더).</summary>
    private (Border Row, TextBox Box) ChannelBoxRow(string label, string? tip, Control? extra, Action<string> commit, Action key, Action<bool> showCurves)
    {
        var box = new TextBox { FontSize = 11, MinWidth = 70, MinHeight = 22, Padding = new Avalonia.Thickness(4, 1), IsEnabled = false };
        var title = new TextBlock { Text = label, FontSize = 11, Width = 76, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };

        if (!string.IsNullOrWhiteSpace(tip))
        {
            ToolTip.SetTip(title, tip);
        }

        title.PointerPressed += (_, e) =>
        {
            showCurves(e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        };
        Button keyButton = SmallButton("●", (_, _) => key());
        ToolTip.SetTip(keyButton, "이 성분을 지금 값으로 지금 시각에 키(마야 Key Selected)");
        var inner = new DockPanel();
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(keyButton, Dock.Right);
        inner.Children.Add(title);
        inner.Children.Add(keyButton);

        if (extra is Slider)
        {
            // 슬라이더 줄(깊이): 숫자 칸은 좁게 오른쪽에, 슬라이더가 남은 폭을 다 쓴다 — 세밀하게 끌 수 있게. 칸은 줄 높이에 늘어나지 않는다.
            box.MinWidth = 0;
            box.Width = 48;
            box.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(box, Dock.Right);
            inner.Children.Add(box);
            extra.VerticalAlignment = VerticalAlignment.Center;
            inner.Children.Add(extra);
        }
        else
        {
            if (extra is not null)
            {
                DockPanel.SetDock(extra, Dock.Left);
                inner.Children.Add(extra);
            }

            inner.Children.Add(box);
        }
        var row = new Border { Padding = new Avalonia.Thickness(2, 1), Child = inner };
        OnCommit(box, commit);
        _channelBoxPanel.Children.Add(row);
        return (row, box);
    }

    /// <summary>컨트롤러 성분으로 읽히는 커브들 — 깊이는 주 노드의 X·Y 둘, 나머지는 주 노드 + 별칭.</summary>
    private static IEnumerable<CurveRef> ControllerCurves(Controller c, Axis axis)
        => c.Bake == ControllerBake.DepthLevel
            ? c.Axes.Select(c.Primary).Concat(c.DepthYCurve is { } y ? new[] { y } : Array.Empty<CurveRef>())
            : c.CurvesOf(axis);

    /// <summary>
    /// 컨트롤러 성분의 지금 값 — 주 노드와 별칭의 합(배율은 곱). 깊이는 배율을 level로 되돌린 값. 포즈가 없으면 null.
    /// </summary>
    private float? ControllerValue(Controller c, Axis axis)
    {
        float? primary = _viewport.ComponentValue(c.Node, c.Kind, axis);

        if (primary is null)
        {
            return null;
        }

        if (c.Bake == ControllerBake.DepthLevel)
        {
            DepthTuningFile depth = _session.Depth ?? BuiltinTuning.Depth;
            return depth.TryLevelOf(primary.Value, out float level) ? level : primary;
        }

        float value = primary.Value;

        foreach (ControllerAlias alias in c.Aliases)
        {
            if (alias.Axis != axis || _viewport.ComponentValue(alias.Node, c.Kind, axis) is not { } aliasValue)
            {
                continue;
            }

            value = c.Kind == ChannelKind.Scale ? value * aliasValue : value + aliasValue;
        }

        return value;
    }

    /// <summary>키를 놓을 노드 — 그 성분에 채널이 있는 노드(별칭이라도), 없으면 주 노드.</summary>
    private static string ControllerTargetNode(MotionAsset? motion, Controller c, Axis axis)
    {
        foreach (CurveRef curve in c.CurvesOf(axis))
        {
            if (motion?.FindChannel(curve.Node, curve.Kind)?.Get(curve.Axis) is not null)
            {
                return curve.Node;
            }
        }

        return c.Node;
    }

    /// <summary>첫 키를 놓는 순간 재생 기준(relative/absolute)를 그 묶음의 mode로 — 자리·깊이는 absolute, 연기는 relative(되돌리기 한 번 더).</summary>
    private void EnsurePreviewModeForGroup(MotionAsset motion, Controller c)
    {
        if (motion.Channels.Count == 0 && motion.PreviewMode != c.Group.Mode)
        {
            _session.Editor.SetPreviewMode(motion.Name, c.Group.Mode);
        }
    }

    /// <summary>컨트롤러 칸 입력: 숫자는 절대값, += 등은 지금 값에 상대. 매니퓰레이터를 놓은 것과 같은 길이다.</summary>
    private void CommitController(Controller c, Axis axis, string text)
    {
        if (_refreshing || _graph.Motion is not { } motion || !RelativeInput.TryParse(text, out Func<float, float> apply))
        {
            RefreshChannelBox();
            return;
        }

        float? current = ControllerValue(c, axis);

        if (current is null)
        {
            return;
        }

        float target = apply(current.Value);

        Guard("채널 박스", () =>
        {
            if (c.Bake == ControllerBake.DepthLevel)
            {
                DepthTuningFile depth = _session.Depth ?? BuiltinTuning.Depth;
                float level = Math.Clamp(target, DepthTuningFile.MinLevel, DepthTuningFile.MaxLevel);

                if (!depth.TryDepthOf(level, DepthTuningFile.FocusHeightOf(_session.Focus), out float depthY, out float scale))
                {
                    return;
                }

                // 배율 X·Y와 깊이 Y(얼굴 제자리·발은 멀수록 위로)가 한 손잡이 — 되돌리기 하나.
                _session.Editor.Batch(MotionChangeKind.Keys, () =>
                {
                    EnsurePreviewModeForGroup(motion, c);
                    _viewport.SetComponents(c.Node, c.Kind, c.Axes.Select(a => (a, scale)).ToList());

                    if (c.DepthYCurve is { } y)
                    {
                        _viewport.SetComponents(y.Node, y.Kind, new[] { (y.Axis, depthY) });
                    }
                });
                return;
            }

            string node = ControllerTargetNode(motion, c, axis);

            if (_viewport.ComponentValue(node, c.Kind, axis) is not { } nodeValue)
            {
                return;
            }

            // 합(곱)으로 보인 값의 차이를 그 노드 하나에 싣는다.
            float next = c.Kind == ChannelKind.Scale
                ? (Math.Abs(current.Value) < 1e-6f ? target : nodeValue * (target / current.Value))
                : nodeValue + (target - current.Value);
            _session.Editor.Batch(MotionChangeKind.Keys, () =>
            {
                EnsurePreviewModeForGroup(motion, c); // 첫 키의 재생 기준 자동 변경은 그 키와 같은 칸
                _viewport.SetComponent(node, c.Kind, axis, next);
            });
        });
        RefreshChannelBox();
    }

    private void KeyController(Controller c, Axis axis)
    {
        if (_graph.Motion is not { } motion)
        {
            return;
        }

        Guard("키 놓기", () => _session.Editor.Batch(MotionChangeKind.Keys, () =>
        {
            EnsurePreviewModeForGroup(motion, c); // 첫 키의 재생 기준 자동 변경은 그 키와 같은 칸

            if (c.Bake == ControllerBake.DepthLevel)
            {
                _viewport.KeyComponents(c.Node, c.Kind, c.Axes);

                if (c.DepthYCurve is { } y)
                {
                    _viewport.KeyComponents(y.Node, y.Kind, new[] { y.Axis });
                }

                return;
            }

            _viewport.KeyComponent(ControllerTargetNode(motion, c, axis), c.Kind, axis);
        }));
    }

    /// <summary>노드 칸 입력(컨트롤러 표가 없는 리그): 숫자는 절대값, <c>+=</c>·<c>-=</c>·<c>*=</c>·<c>/=</c>는 지금 값에 상대.</summary>
    private void CommitChannelBox(ChannelKind kind, Axis axis, string text)
    {
        if (_refreshing || _viewport.SelectedNode is not { } node || _graph.Motion is null || !RelativeInput.TryParse(text, out Func<float, float> apply))
        {
            RefreshChannelBox();
            return;
        }

        float? current = _viewport.ComponentValue(node, kind, axis);

        if (current is null)
        {
            return;
        }

        float value = apply(current.Value);
        Guard("채널 박스", () => _viewport.SetComponent(node, kind, axis, value));
        RefreshChannelBox();
    }

    private void KeyChannelBox(ChannelKind kind, Axis axis)
    {
        if (_viewport.SelectedNode is { } node && _graph.Motion is not null)
        {
            Guard("키 놓기", () => _viewport.KeyComponent(node, kind, axis));
        }
    }

    private Control BuildChannelBoxTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(_channelBoxTitle);
        ToolTip.SetTip(_channelBoxTitle, "고른 오브젝트의 컨트롤러(자리·깊이·연기 — 리거의 표 presets/controllers.json). 표가 없는 리그는 고른 노드의 Translate·Rotate·Scale. 칸에 값을 넣으면(+=·-=·*=·/= 는 지금 값에 상대) 매니퓰레이터를 놓은 것과 같다: Auto Key거나 그 시각에 키가 있으면 키, 아니면 키 없는 포즈(S로 키·Esc로 버림). 묶음 제목 클릭 = 접기/펼치기 · 글자 클릭 = 그 커브를 그래프에(Shift/Ctrl: 더하기) · ● = 이 성분을 지금 키");
        panel.Children.Add(_channelBoxPanel);
        panel.Children.Add(_pendingText);

        _autoKeyToggle.IsCheckedChanged += (_, _) =>
        {
            _viewport.AutoKey = _autoKeyToggle.IsChecked == true;
            _autoKeyToggle.Foreground = _viewport.AutoKey ? AutoKeyOnBrush : null;

            if (!_refreshing)
            {
                _settings.AutoKey = _viewport.AutoKey;
            }
        };
        ToolTip.SetTip(_autoKeyToggle, "Auto Key — 매니퓰레이터를 놓거나 채널 박스에 값을 넣는 순간 키가 들어간다(마야 빨간 키). 꺼져 있으면 키 없는 포즈 → S");
        _viewport.PendingChanged += (_, _) =>
        {
            RefreshPending();
            RefreshChannelBox();
        };
        _viewport.PoseKeyed += (_, _) => RefreshChannelBox();
        return panel;
    }
}
