using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.App;

// 스킨의 셸(PLAN-parts.md P1) — "씬" 탭 아래, 조각 노드가 있는 리그의 오브젝트를 골랐을 때만 보인다.
// 스킨 = 조각마다 붙일 그림의 묶음에 이름을 붙인 것. 오브젝트는 스킨을 고르고, 스킨의 조각 줄에서 그림을 정한다.
public partial class MainWindow
{
    private const string NoSkin = "(없음)";

    private readonly StackPanel _skinPanel = new() { Spacing = 4, IsVisible = false };
    private readonly ComboBox _skinCombo = new() { FontSize = 11, MinWidth = 120 };
    private readonly TextBox _skinName = new() { FontSize = 11 };
    private readonly StackPanel _skinParts = new() { Spacing = 3 };

    /// <summary>테스트 손잡이 — "스킨 추가".</summary>
    internal string? AddSkinFromForm() => AddSkin();

    /// <summary>테스트 손잡이 — 스킨 칸이 보이는가, 고른 스킨, 조각 줄 수.</summary>
    internal (bool Visible, string? Selected, int PartRows) SkinFormState
        => (_skinPanel.IsVisible, _skinCombo.SelectedItem as string is { } s && s != NoSkin ? s : null, _skinParts.Children.Count);

    /// <summary>테스트 손잡이 — 조각 줄의 경로 칸(조각 노드 id로 찾는다).</summary>
    internal TextBox? SkinPartBox(string node)
        => _skinParts.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<TextBox>()).FirstOrDefault(b => Equals(b.Tag, node));

    private Control BuildSkinPanel()
    {
        _skinPanel.Children.Add(Header("스킨 (조각 그림)"));
        _skinPanel.Children.Add(Labeled("입은 스킨", _skinCombo));
        ToolTip.SetTip(_skinCombo, "이 오브젝트가 입는 스킨 — 조각마다 어떤 그림을 그릴지의 묶음. 같은 리그의 스킨만 보인다. 그림이 없는 조각은 안 그린다");
        Button add = SmallButton("스킨 추가", (_, _) => Guard("스킨 추가", () => AddSkin()));
        ToolTip.SetTip(add, "이 리그의 새 스킨(skin, skin_2…)을 만들어 이 오브젝트에 입힌다 — 되돌리기 한 번. 이름은 아래 칸에서(한글도 된다)");
        _skinPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { add, SmallButton("스킨 삭제", (_, _) => Guard("스킨 삭제", RemoveSkin)) },
        });
        _skinPanel.Children.Add(Labeled("이름", _skinName));
        _skinPanel.Children.Add(_skinParts);

        _skinCombo.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _skinCombo.SelectedItem is not string choice || _viewport.SelectedObject is not { } o)
            {
                return;
            }

            Guard("스킨 입히기", () => _session.Editor.SetObjectSkin(null, o.Id, choice == NoSkin ? null : choice));
        };
        OnCommit(_skinName, text =>
        {
            if (!_refreshing && _viewport.SelectedObject?.Skin is { } current && text.Trim() != current)
            {
                Guard("스킨 이름", () => _session.Editor.RenameSkin(current, text));
            }
        });
        return _skinPanel;
    }

    /// <summary>고른 오브젝트의 리그에 조각 노드가 있을 때만 보인다 — 캐릭터(초상 한 장)에는 스킨이 없다.</summary>
    private void RefreshSkinPanel()
    {
        _refreshDepth++;

        try
        {
            SceneObject? o = _viewport.SelectedObject;
            IReadOnlyList<string> parts = o is null ? Array.Empty<string>() : RigParts.Of(_session.Rig, o.Rig);
            _skinPanel.IsVisible = parts.Count > 0;
            _skinParts.Children.Clear();
            _skinSetupBoxes.Clear();

            if (o is null || parts.Count == 0)
            {
                return;
            }

            var choices = new List<string> { NoSkin };
            choices.AddRange(_session.Editor.Document.Skins.Where(s => s.Rig == o.Rig).Select(s => s.Name));
            _skinCombo.ItemsSource = choices;
            _skinCombo.SelectedIndex = o.Skin is null ? 0 : Math.Max(0, choices.IndexOf(o.Skin));

            SkinAsset? skin = _session.Editor.Document.FindSkin(o.Skin);
            _skinName.IsEnabled = skin is not null;

            if (!_skinName.IsFocused)
            {
                _skinName.Text = skin?.Name ?? "";
            }

            if (skin is null)
            {
                return;
            }

            foreach (string node in parts)
            {
                _skinParts.Children.Add(SkinPartRow(skin, node));
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>조각 한 줄 — 노드 이름 · 그림 경로(문서 기준, 비우면 그 조각을 안 그린다) · 파일 고르기.</summary>
    private Control SkinPartRow(SkinAsset asset, string node)
    {
        string skin = asset.Name;
        var box = new TextBox { FontSize = 11, Text = string.Join(FrameSeparator, asset.ImagesOf(node)), Tag = node, PlaceholderText = "(그림 없음)" };
        ToolTip.SetTip(box, $"{node}에 그릴 그림 — 문서 기준 상대 경로. 비우면 이 조각은 안 그린다. 여러 장을 ' | '로 이어 적으면 그림 넘기기(아래 칸의 장당 시간마다 넘어간다)");
        OnCommit(box, text =>
        {
            if (!_refreshing)
            {
                Guard("조각 그림", () => _session.Editor.SetSkinPartImages(skin, node, text.Split('|')));
            }
        });

        Button pick = SmallButton("…", async (_, _) =>
        {
            try
            {
                string? path = await PickOpenAsync("조각 그림 PNG", new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } });

                if (path is not null)
                {
                    _session.SetSkinPart(skin, node, path);
                }
            }
            catch (Exception exception)
            {
                _session.SetStatus($"조각 그림 실패: {exception.Message}");
            }
        });

        var row = new DockPanel();

        // 그림이 있는 조각에만: 그림 넘기기(장당 시간 · 되풀이)와 놓임(자리 · 크기 · 회전).
        if (asset.Parts.ContainsKey(node))
        {
            Control setup = SkinSetupRow(asset, node);
            DockPanel.SetDock(setup, Dock.Bottom);
            row.Children.Add(setup);
        }

        DockPanel.SetDock(pick, Dock.Right);
        row.Children.Add(pick);
        var label = new TextBlock { Text = node, FontSize = 11, Width = 96, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(box);
        return row;
    }

    private const string FrameSeparator = " | ";

    /// <summary>테스트 손잡이 — 조각의 덧붙임 칸들(장당 초 · 되풀이 · X · Y · 크기 · 회전). 그림이 없는 조각이면 null.</summary>
    internal (TextBox Seconds, CheckBox Loop, TextBox X, TextBox Y, TextBox Scale, TextBox Rotation)? SkinSetupBoxes(string node)
        => _skinSetupBoxes.TryGetValue(node, out var boxes) ? boxes : null;

    private readonly Dictionary<string, (TextBox Seconds, CheckBox Loop, TextBox X, TextBox Y, TextBox Scale, TextBox Rotation)> _skinSetupBoxes = new(StringComparer.Ordinal);

    private Control SkinSetupRow(SkinAsset asset, string node)
    {
        string skin = asset.Name;
        SkinPartSetup now = asset.SetupOf(node);
        TextBox Box(string text, string hint, string tip)
        {
            var box = new TextBox { FontSize = 10, Text = text, PlaceholderText = hint, Width = 46, MinWidth = 0, Padding = new Avalonia.Thickness(3, 1) };
            ToolTip.SetTip(box, tip);
            return box;
        }

        TextBox seconds = Box(F(now.FrameSeconds), "초", "그림 넘기기 — 한 장이 보이는 시간(초). 장이 여럿일 때만 쓰인다");
        var loop = new CheckBox { Content = "되풀이", FontSize = 10, IsChecked = now.Loop, IsEnabled = now.Frames.Count > 0 };
        ToolTip.SetTip(loop, "마지막 장 다음에 첫 장으로 돌아간다. 끄면 마지막 장에서 멈춘다(점이 하나씩 찍히고 서는 말풍선)");
        seconds.IsEnabled = now.Frames.Count > 0;
        TextBox x = Box(F(now.X), "X", "놓임 — 조각 노드 안에서의 자리 X(px). 모션과는 따로다: 꽃 셋을 흩어 놓는 것은 여기서, 움직임은 모션에서");
        TextBox y = Box(F(now.Y), "Y", "놓임 — 자리 Y(px)");
        TextBox scale = Box(F(now.Scale), "크기", "놓임 — 크기(배). 1이 조각 사각형에 꼭 맞는 크기");
        TextBox rotation = Box(F(now.Rotation), "회전", "놓임 — 회전(도, 반시계가 +)");
        _skinSetupBoxes[node] = (seconds, loop, x, y, scale, rotation);

        void Commit()
        {
            if (_refreshing || _session.Editor.Document.FindSkin(skin) is not { } current || !current.Parts.ContainsKey(node))
            {
                return;
            }

            SkinPartSetup value = current.SetupOf(node).Clone();
            value.FrameSeconds = TryFloat(seconds.Text ?? "", out float s) ? s : value.FrameSeconds;
            value.Loop = loop.IsChecked == true;
            value.X = TryFloat(x.Text ?? "", out float vx) ? vx : value.X;
            value.Y = TryFloat(y.Text ?? "", out float vy) ? vy : value.Y;
            value.Scale = TryFloat(scale.Text ?? "", out float vs) ? vs : value.Scale;
            value.Rotation = TryFloat(rotation.Text ?? "", out float vr) ? vr : value.Rotation;
            Guard("조각 놓임", () => _session.Editor.SetSkinPartSetup(skin, node, value));
        }

        foreach (TextBox box in new[] { seconds, x, y, scale, rotation })
        {
            OnCommit(box, _ => Commit());
        }

        loop.IsCheckedChanged += (_, _) => Commit();

        // 인스펙터가 좁아 두 줄로: 그림 넘기기 / 놓임.
        return new StackPanel
        {
            Spacing = 2,
            Margin = new Avalonia.Thickness(12, 1, 0, 4),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Small("장당"), seconds, Small("초"), loop } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Small("놓임"), x, y, scale, rotation } },
            },
        };
    }

    /// <summary>고른 오브젝트의 리그로 새 스킨을 만들어 입힌다 — 되돌리기 한 번.</summary>
    private string? AddSkin()
    {
        if (_viewport.SelectedObject is not { } o)
        {
            return null;
        }

        MotionEditor editor = _session.Editor;
        string name = "skin";

        for (int n = 2; editor.Document.FindSkin(name) is not null; n++)
        {
            name = $"skin_{n}";
        }

        string id = o.Id;
        string rig = o.Rig;
        editor.Batch(MotionChangeKind.Scene, () =>
        {
            editor.AddSkin(name, rig);
            editor.SetObjectSkin(null, id, name);
        });
        return name;
    }

    private void RemoveSkin()
    {
        if (_viewport.SelectedObject?.Skin is { } skin)
        {
            _session.Editor.RemoveSkin(skin);
        }
    }
}
