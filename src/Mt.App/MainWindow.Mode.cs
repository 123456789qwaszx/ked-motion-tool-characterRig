using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mt.Authoring.Settings;

namespace Mt.App;

// 작업 모드(docs/PLAN-ux.md U1) — 캐릭터 모션 / 이모지. 화면이 그 일에 쓰는 것만 보인다.
//
//   캐릭터 모션: 왼쪽 = 씬 · 모션 · 클립,          오른쪽 = 채널 박스 · 씬 · 모션 · 클립 탭, 툴바 = motions.json 가져오기 · 내보내기
//   이모지:     왼쪽 = 이모지 · 그 이모지의 그림, 오른쪽 = 고른 것의 값(탭이 아니다),        툴바 = emojis.json 내보내기
//
// 가운데(뷰포트 · 타임 슬라이더 · 그래프 · 도프 시트)는 두 모드가 함께 쓴다 — 시간의 주인(모션 · 클립 · 이모지)이 묶는다.
// 모드는 시간의 주인을 따라간다: 이모지를 고르면 이모지 모드, 모션이나 클립을 고르면 캐릭터 모션 모드.
// 툴바에서 모드를 바꾸면 반대쪽의 선택이 풀린다(숨은 목록의 것이 가운데에 남지 않게).
public partial class MainWindow
{
    private readonly ToggleButton _modeMotion = new() { Content = "캐릭터 모션", FontSize = 11, Padding = new Avalonia.Thickness(10, 3) };
    private readonly ToggleButton _modeEmoji = new() { Content = "이모지", FontSize = 11, Padding = new Avalonia.Thickness(10, 3) };
    private readonly StackPanel _motionLeft = new() { Spacing = 6 };
    private readonly StackPanel _emojiLeft = new() { Spacing = 6 };
    private readonly List<(WorkMode Mode, Control Control)> _modeOnly = new();
    private TabItem[] _inspectorTabItems = Array.Empty<TabItem>();
    private Control _emojiPane = new Panel();
    private WorkMode _workMode;

    /// <summary>테스트 손잡이 — 지금의 작업 모드.</summary>
    internal WorkMode Mode => _workMode;

    /// <summary>테스트 손잡이 — 툴바에서 모드를 고른다(단추를 누르는 것과 같은 길).</summary>
    internal void SwitchWorkModeFromForm(WorkMode mode) => SwitchWorkMode(mode);

    /// <summary>테스트 손잡이 — 모드가 보이는 것: 왼쪽의 두 묶음 · 오른쪽의 탭들과 이모지의 칸 · 보이는 툴바의 내보내기.</summary>
    internal (bool MotionLeft, bool EmojiLeft, bool Tabs, bool EmojiPane, IReadOnlyList<string> Exports) WorkModeFormState
        => (_motionLeft.IsVisible,
            _emojiLeft.IsVisible,
            _inspectorTabs.IsVisible,
            _emojiPane.IsVisible,
            _modeOnly.Where(m => m.Control.IsVisible && m.Control is Button).Select(m => ((Button)m.Control).Content as string ?? "").ToList());

    private void BuildWorkModeSwitch()
    {
        _modeMotion.Click += (_, _) => SwitchWorkMode(WorkMode.Motion);
        _modeEmoji.Click += (_, _) => SwitchWorkMode(WorkMode.Emoji);
        ToolTip.SetTip(_modeMotion, "캐릭터 모션 · 클립 · 씬을 만든다 — 이모지의 목록과 탭은 보이지 않는다");
        ToolTip.SetTip(_modeEmoji, "이모지를 만든다 — 씬 · 모션 · 클립의 목록과 탭은 보이지 않는다. 이모지를 고르거나 만들면 저절로 이 모드가 된다");
        // 모드마다 툴바의 내용이 달라도(콤보가 있고 없고) 높이가 같게 — 아래 화면이 튀지 않는다.
        Toolbar.MinHeight = 32;
        Toolbar.Children.Add(_modeMotion);
        Toolbar.Children.Add(_modeEmoji);
        Toolbar.Children.Add(Separator());
    }

    private Button EmojiExportButton()
    {
        Button export = ToolButton("emojis.json 내보내기…", OnExportEmojisClick);
        ToolTip.SetTip(export, "emojis.json(emojis/2) 내보내기 — 구간마다 따로 선 클립. 막는 문제가 있거나 런타임 로더가 다시 읽지 못하면 쓰지 않는다");
        return export;
    }

    /// <summary>그 모드에서만 보이는 컨트롤로 적어 둔다.</summary>
    private T OnlyIn<T>(WorkMode mode, T control)
        where T : Control
    {
        _modeOnly.Add((mode, control));
        return control;
    }

    /// <summary>왼쪽 패널에 쌓은 것을 두 묶음으로 나눈다 — <paramref name="emojiFrom"/>번째부터가 이모지 모드의 것이다.</summary>
    private void GroupLeftByWorkMode(int emojiFrom)
    {
        List<Control> all = LeftPanel.Children.ToList();
        LeftPanel.Children.Clear();

        for (int i = 0; i < all.Count; i++)
        {
            (i < emojiFrom ? _motionLeft : _emojiLeft).Children.Add(all[i]);
        }

        LeftPanel.Children.Add(_motionLeft);
        LeftPanel.Children.Add(_emojiLeft);
    }

    /// <summary>모드가 보이는 것을 맞춘다 — 선택은 건드리지 않는다.</summary>
    private void ApplyWorkMode(WorkMode mode)
    {
        _workMode = mode;
        _settings.WorkMode = mode;
        bool emoji = mode == WorkMode.Emoji;

        _refreshDepth++;

        try
        {
            _modeMotion.IsChecked = !emoji;
            _modeEmoji.IsChecked = emoji;
            _motionLeft.IsVisible = !emoji;
            _emojiLeft.IsVisible = emoji;

            foreach ((WorkMode only, Control control) in _modeOnly)
            {
                control.IsVisible = only == mode;
            }

            _inspectorTabs.IsVisible = !emoji;
            _emojiPane.IsVisible = emoji;
            _inspectorTabs.SelectedIndex = Math.Clamp(_settings.InspectorTab, 0, _inspectorTabItems.Length - 1);
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>시간의 주인을 따라 모드에 들어선다 — 이미 그 모드면 아무 일도 없다.</summary>
    private void EnterWorkMode(WorkMode mode)
    {
        if (_workMode != mode)
        {
            ApplyWorkMode(mode);
        }
    }

    /// <summary>
    /// 툴바에서 모드를 골랐다. 반대쪽의 선택을 푼다 — 이모지 모드로 가면 모션 · 클립을, 캐릭터 모션 모드로 가면 이모지를.
    /// 이모지 모드에 들어섰는데 고른 이모지가 없으면 첫 이모지를 고른다(있을 때).
    /// </summary>
    private void SwitchWorkMode(WorkMode mode)
    {
        // 누른 단추가 이미 켜진 쪽이어도 토글이 꺼지지 않게 다시 맞춘다.
        ApplyWorkMode(mode);

        if (mode == WorkMode.Motion)
        {
            _emojiList.SelectedItem = null;
            return;
        }

        DeselectClip();
        _motionList.SelectedItem = null;

        if (SelectedEmoji is null && _emojiList.ItemsSource?.Cast<string>().FirstOrDefault() is { } first)
        {
            _emojiList.SelectedItem = first;
        }
        RefreshTweenNote();
    }
}
