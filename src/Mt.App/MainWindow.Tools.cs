using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.Authoring.Preview;

namespace Mt.App;

// 뷰포트 위의 도구 띠(docs/PLAN-ux.md U4) — 지금 도구가 화면에 보이고 눌러서도 바꾼다.
//   선택 Q · 이동 W · 회전 E · 크기 R  |  ● Auto Key
// 단축키로 바꿔도 띠가 따라온다. Auto Key는 아래 줄의 것과 같은 상태다(여기서도 켜고 끈다) —
// 이모지에서는 끈 것이 기본값이 되는가 키가 되는가를 이것이 정하므로 뷰포트 곁에서 보여야 한다.
public partial class MainWindow
{
    private readonly (ManipulatorMode Mode, ToggleButton Button)[] _toolButtons =
    {
        (ManipulatorMode.Select, ToolToggle("선택 Q")),
        (ManipulatorMode.Move, ToolToggle("이동 W")),
        (ManipulatorMode.Rotate, ToolToggle("회전 E")),
        (ManipulatorMode.Scale, ToolToggle("크기 R")),
    };

    private readonly ToggleButton _toolAutoKey = ToolToggle("● Auto Key");

    /// <summary>테스트 손잡이 — 도구 띠에서 켜진 도구.</summary>
    internal ManipulatorMode? ToolStripChecked => _toolButtons.Where(t => t.Button.IsChecked == true).Select(t => (ManipulatorMode?)t.Mode).SingleOrDefault();

    /// <summary>테스트 손잡이 — 도구 띠의 단추를 누른다.</summary>
    internal void PickToolFromStrip(ManipulatorMode mode) => SetManipulator(mode);

    /// <summary>테스트 손잡이 — 도구 띠의 Auto Key를 누른다 / 켜져 있는가.</summary>
    internal void SetAutoKeyFromStrip(bool on) => _toolAutoKey.IsChecked = on;

    internal bool ToolStripAutoKey => _toolAutoKey.IsChecked == true;

    private static ToggleButton ToolToggle(string text) => new()
    {
        Content = text,
        FontSize = 11,
        Padding = new Avalonia.Thickness(8, 3),
    };

    /// <summary>뷰포트의 오른쪽 위에 얹는 띠.</summary>
    private Control BuildToolStrip()
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 6, 8, 0),
        };

        foreach ((ManipulatorMode mode, ToggleButton button) in _toolButtons)
        {
            ManipulatorMode captured = mode;
            button.Click += (_, _) =>
            {
                SetManipulator(captured);
                SyncToolStrip(); // 켜진 것을 다시 눌러도 꺼지지 않는다
                _viewport.Focus();
            };
            strip.Children.Add(button);
        }

        ToolTip.SetTip(_toolButtons[0].Button, "선택(Q) — 누르면 고르기만 한다. 끌어도 값이 바뀌지 않는다");
        ToolTip.SetTip(_toolButtons[1].Button, "이동(W) — 화살표를 끌면 그 축으로, 가운데 네모나 그림을 끌면 자유롭게");
        ToolTip.SetTip(_toolButtons[2].Button, "회전(E) — 고리를 끈다");
        ToolTip.SetTip(_toolButtons[3].Button, "크기(R) — 끝의 상자를 끌면 그 축으로, 가운데 상자를 끌면 가로 세로 같이");

        strip.Children.Add(new Border { Width = 1, Margin = new Avalonia.Thickness(4, 2), Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)) });
        ToolTip.SetTip(_toolAutoKey, "Auto Key — 켜면 끈 것이 지금 프레임의 키가 된다. 이모지에서 꺼져 있으면 끈 것은 기본값(그림의 자리 · 크기 · 회전, 이모지의 머리에서의 자리)이 된다 — 키가 아니다. 아래 줄의 Auto Key와 같은 것이다");
        _toolAutoKey.IsCheckedChanged += (_, _) =>
        {
            if (_autoKeyToggle.IsChecked != _toolAutoKey.IsChecked)
            {
                _autoKeyToggle.IsChecked = _toolAutoKey.IsChecked;
            }
        };
        _autoKeyToggle.IsCheckedChanged += (_, _) =>
        {
            _toolAutoKey.IsChecked = _autoKeyToggle.IsChecked;
            _toolAutoKey.Foreground = _autoKeyToggle.IsChecked == true ? AutoKeyOnBrush : null;
            _viewport.InvalidateVisual(); // 뷰포트의 글("끌면 키 / 기본값")이 따라온다
        };
        strip.Children.Add(_toolAutoKey);

        _viewport.ManipulatorChanged += (_, _) => SyncToolStrip();
        SyncToolStrip();
        return strip;
    }

    private void SyncToolStrip()
    {
        foreach ((ManipulatorMode mode, ToggleButton button) in _toolButtons)
        {
            button.IsChecked = mode == _viewport.Manipulator;
        }
    }
}
