using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Validation;

namespace Mt.App;

// 연출 클립의 셸(PLAN-clip.md K3) — 왼쪽 "클립" 목록과 오른쪽 "클립" 탭.
// 시간의 주인은 모션이나 클립 중 하나다: 클립을 고르면 모션 선택이 풀리고 뷰포트·시계·타임 슬라이더·레인지가 클립을 본다.
// 블록을 놓고 끄는 타임라인은 K4다 — 여기서는 클립과 배역(출연자 ↔ 씬 오브젝트)까지.
public partial class MainWindow
{
    private readonly ListBox _clipList = new() { MinHeight = 60 };
    private readonly TextBox _clipName = new() { FontSize = 11 };
    private readonly TextBox _clipDuration = new() { FontSize = 11, PlaceholderText = "마지막 블록의 끝" };
    private readonly StackPanel _clipRoles = new() { Spacing = 3 };

    internal string? SelectedClip => _clipList.SelectedItem as string;

    /// <summary>테스트 손잡이 — 클립 목록에서 고른다(null이면 푼다).</summary>
    internal void SelectClip(string? name) => _clipList.SelectedItem = name;

    /// <summary>테스트 손잡이 — "클립 추가".</summary>
    internal string AddClipFromForm() => AddClip();

    /// <summary>테스트 손잡이 — "배역 추가".</summary>
    internal string? AddClipRoleFromForm() => AddClipRole();

    /// <summary>테스트 손잡이 — 클립 탭의 글 칸과 배역 줄 수.</summary>
    internal (string Name, string Duration, int Roles, bool Enabled) ClipFormState
        => (_clipName.Text ?? "", _clipDuration.Text ?? "", _clipRoles.Children.Count, _clipName.IsEnabled);

    private ClipAsset? CurrentClip => SelectedClip is { } name ? _session.Editor.Document.FindClip(name) : null;

    private void BuildClipLeft()
    {
        LeftPanel.Children.Add(Header("클립"));
        _clipList.SelectionChanged += (_, _) => OnClipSelected();
        ToolTip.SetTip(_clipList, "연출 클립 — 여러 출연자의 모션을 시간차로 놓은 시간표. 고르면 뷰포트·타임 슬라이더가 클립 전체를 재생한다(모션 선택은 풀린다). 키는 모션을 골라 고친다");
        LeftPanel.Children.Add(_clipList);

        Button add = SmallButton("클립 추가", (_, _) => Guard("클립 추가", () => AddClip()));
        ToolTip.SetTip(add, "이름 clip, clip_2…로 만든다 — 지금 씬이 시작 상태로 적힌다. 오른쪽 '클립' 탭에서 이름·길이·배역을 바꾼다");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { add, SmallButton("클립 삭제", (_, _) => Guard("클립 삭제", RemoveClip)) },
        });
    }

    private Control BuildClipTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(Labeled("이름", _clipName));
        panel.Children.Add(Labeled("길이(초)", _clipDuration));
        ToolTip.SetTip(_clipDuration, "비우면 마지막 블록의 끝을 따라간다. 적으면 그 길이로 고정 — 넘치는 블록은 아래 문제 칸이 알린다");
        panel.Children.Add(Header("배역"));
        panel.Children.Add(_clipRoles);
        Button addRole = SmallButton("배역 추가", (_, _) => Guard("배역 추가", () => AddClipRole()));
        ToolTip.SetTip(addRole, "출연자 자리를 하나 더한다(a, b, c…) — 리그와 맡을 오브젝트는 뷰포트에서 고른 오브젝트. 트랙도 하나 같이 생긴다(되돌리기 한 번)");
        panel.Children.Add(addRole);

        OnCommit(_clipName, text => WithClip(c =>
        {
            if (text != c.Name)
            {
                _session.Editor.RenameClip(c.Name, text);
                _clipList.SelectedItem = text;
            }
        }));
        OnCommit(_clipDuration, text => WithClip(c =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _session.Editor.SetClipDuration(c.Name, null);
            }
            else if (TryFloat(text, out float seconds))
            {
                _session.Editor.SetClipDuration(c.Name, seconds);
            }
        }));
        return panel;
    }

    private void WithClip(Action<ClipAsset> action)
    {
        if (_refreshing || CurrentClip is not { } clip)
        {
            return;
        }

        Guard("클립", () => action(clip));
    }

    private void OnClipSelected()
    {
        if (_refreshing)
        {
            return;
        }

        if (SelectedClip is not null && SelectedMotion is not null)
        {
            // 모션 선택을 푼다 — 그 알림(OnMotionSelected)이 BindMotion(null)을 불러 뷰포트가 클립을 묶는다.
            _motionList.SelectedItem = null;
        }
        else
        {
            BindMotion(SelectedMotion);
            RefreshMotionInspector();
            RefreshKeyInspector();
        }

        RefreshClipInspector();
    }

    /// <summary>모션을 골랐다 — 클립 선택을 푼다(다시 묶는 일은 모션 쪽이 한다).</summary>
    private void DeselectClip()
    {
        if (SelectedClip is null)
        {
            return;
        }

        _refreshDepth++;

        try
        {
            _clipList.SelectedItem = null;
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshClipInspector();
    }

    /// <summary>문서가 바뀌었다 — 목록을 다시 읽고, 고른 클립이 사라졌으면(삭제·되돌리기) 뷰포트를 풀어 준다.</summary>
    private void RefreshClips()
    {
        _refreshDepth++;

        try
        {
            string? selected = SelectedClip ?? _viewport.ClipName;
            List<string> names = _session.Editor.Document.Clips.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            _clipList.ItemsSource = names;
            _clipList.SelectedIndex = selected is null || SelectedMotion is not null ? -1 : names.IndexOf(selected);
        }
        finally
        {
            _refreshDepth--;
        }

        if (_viewport.ClipName != (SelectedMotion is null ? SelectedClip : null))
        {
            BindMotion(SelectedMotion);
        }

        RefreshClipInspector();
    }

    private void RefreshClipInspector()
    {
        _refreshDepth++;

        try
        {
            ClipAsset? clip = CurrentClip;
            _clipName.IsEnabled = clip is not null;
            _clipDuration.IsEnabled = clip is not null;
            _clipName.Text = clip?.Name ?? "";
            _clipDuration.Text = clip?.Duration is { } d ? d.ToString("0.###", CultureInfo.InvariantCulture) : "";
            _clipRoles.Children.Clear();

            if (clip is null)
            {
                return;
            }

            List<string> objects = (_viewport.Scene?.Objects.Select(o => o.Id) ?? Enumerable.Empty<string>()).ToList();

            foreach (ClipRole role in clip.Roles)
            {
                _clipRoles.Children.Add(ClipRoleRow(clip.Name, role, objects));
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>배역 한 줄 — 이름 · 리그 · 맡은 오브젝트(콤보, 첫 칸은 "안 묶음") · ×.</summary>
    private Control ClipRoleRow(string clipName, ClipRole role, List<string> objects)
    {
        string roleName = role.Name;
        var choices = new List<string> { "(안 묶음)" };
        choices.AddRange(objects);

        // 씬에 없는 id가 적혀 있으면 그대로 보인다 — 조용히 지우지 않는다(검증이 알린다).
        if (role.Object is { } bound && !objects.Contains(bound))
        {
            choices.Add(bound);
        }

        var combo = new ComboBox { ItemsSource = choices, FontSize = 11, MinWidth = 90, SelectedIndex = role.Object is null ? 0 : choices.IndexOf(role.Object) };
        combo.SelectionChanged += (_, _) =>
        {
            if (_refreshing || combo.SelectedIndex < 0)
            {
                return;
            }

            string? next = combo.SelectedIndex == 0 ? null : choices[combo.SelectedIndex];
            Guard("배역 묶기", () => _session.Editor.BindClipRole(clipName, roleName, next));
        };

        var row = new DockPanel();
        Button remove = SmallButton("×", (_, _) => Guard("배역 삭제", () => _session.Editor.RemoveClipRole(clipName, roleName)));
        ToolTip.SetTip(remove, "배역을 지운다 — 그 배역의 트랙과 블록도 같이(되돌리기 한 번)");
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        var label = new TextBlock { Text = $"{role.Name} · {role.Rig}", FontSize = 11, Width = 96, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(combo);
        return row;
    }

    private Control ClipProblemRow(ClipProblem problem)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock
        {
            Text = (problem.Blocking ? "⚠ " : "△ ") + problem,
            FontSize = 11,
            Foreground = problem.Blocking ? Brushes.OrangeRed : Brushes.Goldenrod,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 700,
        });

        if (problem.Blocking && problem.Message.Contains("넘는다", StringComparison.Ordinal))
        {
            string clipName = problem.ClipName;
            row.Children.Add(SmallButton("길이를 마지막 블록에 맞추기", (_, _) => Guard("돕는 손", () => ClipFixes.DurationToLastBlock(_session.Editor, clipName))));
        }

        return row;
    }

    // ── 명령 ──────────────────────────────────────────────────────────

    private string AddClip()
    {
        MotionDocument document = _session.Editor.Document;
        string name = "clip";

        for (int n = 2; document.FindClip(name) is not null; n++)
        {
            name = $"clip_{n}";
        }

        _session.Editor.AddClip(name);
        _clipList.SelectedItem = name;
        return name;
    }

    private void RemoveClip()
    {
        if (SelectedClip is { } name)
        {
            _session.Editor.RemoveClip(name);
        }
    }

    /// <summary>배역 a, b, c…(다 쓰면 role_27…)를 고른 오브젝트의 리그·묶임으로 더하고 트랙 하나를 같이 만든다 — 되돌리기 한 번.</summary>
    private string? AddClipRole()
    {
        if (CurrentClip is not { } clip)
        {
            return null;
        }

        string role = Enumerable.Range(0, 26).Select(i => ((char)('a' + i)).ToString()).FirstOrDefault(n => clip.FindRole(n) is null)
            ?? $"role_{clip.Roles.Count + 1}";
        SceneObject? o = _viewport.SelectedObject;
        string clipName = clip.Name;

        _session.Editor.Batch(MotionChangeKind.Clip, () =>
        {
            _session.Editor.AddClipRole(clipName, role, o?.Rig ?? SceneObject.DefaultRig, o?.Id);
            _session.Editor.AddClipTrack(clipName, role);
        });
        return role;
    }
}
