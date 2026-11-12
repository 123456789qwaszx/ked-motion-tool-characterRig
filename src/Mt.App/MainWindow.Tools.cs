using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.Authoring.Preview;
using Mt.Authoring.Settings;

namespace Mt.App;

// 뷰포트 위의 도구 띠(docs/history/PLAN-ux.md U4) — 지금 도구가 화면에 보이고 눌러서도 바꾼다.
//   선택 Q · 이동 W · 회전 E · 크기 R  |  ● Auto Key  |  (이모지 모드) 인물 · ◀ 왼쪽 보기 · 맞춤 F · 인물과 함께
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

        // 이모지 모드 — 보기의 단추들: 인물을 보이는가 · 왼쪽을 보는 인물로 · 보기 맞춤.
        _emojiShowHost.IsCheckedChanged += (_, _) => _viewport.EmojiShowHost = _emojiShowHost.IsChecked == true;
        ToolTip.SetTip(_emojiShowHost, "이모지가 뜨는 인물을 보인다 — 초상(씬 탭에서 넣은 PNG)이 있으면 그것, 없으면 실루엣. 십자가 머리의 자리(이모지의 '머리에서'가 재는 곳)다. 덤프 옆 presets/emoji-locator.json이 있으면 그 캐릭터의 자리를 읽는다(보기만 — 게임의 값은 런타임의 표가 정한다)");
        _emojiFacingLeft.IsCheckedChanged += (_, _) => _viewport.EmojiFacingLeft = _emojiFacingLeft.IsChecked == true;
        ToolTip.SetTip(_emojiFacingLeft, "켜면 왼쪽을 보는(뒤집힌) 인물에게 뜨는 모습으로 본다 — 인물과 이모지가 거울에 비치고, '인물의 방향을 따르지 않기' · '뒤집지 않기'를 켠 것만 그대로 남는다(런타임과 같은 계산). 보기만 바뀐다. 켠 동안 그림을 끌어 기본 자리를 옮기지 않는다");
        Button fit = SmallButton("맞춤 F", (_, _) => _viewport.FrameEmoji());
        fit.FontSize = 11;
        fit.Padding = new Avalonia.Thickness(8, 3);
        ToolTip.SetTip(fit, "이모지에 맞춤(F) — 그림들이 뷰포트 가운데를 채우게 보기를 옮긴다(보기만)");
        Button fitHost = SmallButton("인물과 함께", (_, _) => _viewport.FrameEmoji(withHost: true));
        fitHost.FontSize = 11;
        fitHost.Padding = new Avalonia.Thickness(8, 3);
        ToolTip.SetTip(fitHost, "이모지와 인물의 머리가 함께 보이게 보기를 옮긴다 — 이모지를 처음 열 때의 보기(보기만)");
        var emojiView = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new Border { Width = 1, Margin = new Avalonia.Thickness(4, 2), Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)) },
                _emojiShowHost, _emojiFacingLeft, fit, fitHost,
            },
        };
        strip.Children.Add(OnlyIn(WorkMode.Emoji, emojiView));

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
