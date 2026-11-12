using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Mt.Authoring.Model;

namespace Mt.App;

// 새 이모지의 첫 화면(docs/history/PLAN-ux.md U9) — "이모지 추가"가 여는 작은 창. 셋만 정하면 움직이는 이모지가 생긴다:
//   1. 그림 — PNG를 끌어다 놓거나 "그림 고르기…"(한 장에 그림 하나). ▲ ▼로 순서를 바꾼다(아래가 위)
//   2. 어떻게 뜨는가 — 한 번 재생하고 사라진다 / 머문다
//   3. 움직임 — 등장 · 머묾 · 퇴장(없음도 된다)
// "만들기"가 그것을 한 칸에 짓고(MotionEditor.StartEmoji), "빈 이모지로 시작"은 전처럼 빈 그림 body 하나짜리를 만든다.
public partial class MainWindow
{
    private const string NoMove = "(없음)";

    private Window? _newEmojiWindow;
    private readonly TextBox _newEmojiName = new() { FontSize = 12 };
    private readonly ListBox _newEmojiImages = new() { FontSize = 11, MinHeight = 64, MaxHeight = 130 };
    private readonly RadioButton _newEmojiOnce = new() { Content = "한 번 재생하고 사라진다", GroupName = "newEmojiKind", FontSize = 12, IsChecked = true };
    private readonly RadioButton _newEmojiStays = new() { Content = "머문다 — 내릴 때까지(emoji__off) 되풀이한다", GroupName = "newEmojiKind", FontSize = 12 };
    private readonly ComboBox _newEmojiIn = new() { FontSize = 11, MinWidth = 170 };
    private readonly ComboBox _newEmojiHold = new() { FontSize = 11, MinWidth = 170 };
    private readonly ComboBox _newEmojiOut = new() { FontSize = 11, MinWidth = 170 };
    private readonly TextBlock _newEmojiSummary = new() { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _newEmojiPaths = new();

    /// <summary>테스트 손잡이 — 첫 화면을 연다(이미 열려 있으면 앞으로).</summary>
    internal void OpenNewEmojiFromForm() => OpenNewEmoji();

    /// <summary>테스트 손잡이 — 첫 화면이 열려 있는가.</summary>
    internal bool NewEmojiOpen => _newEmojiWindow is not null;

    /// <summary>테스트 손잡이 — 첫 화면의 창(장면을 그림으로 뽑는다).</summary>
    internal Window? NewEmojiWindow => _newEmojiWindow;

    /// <summary>테스트 손잡이 — 첫 화면의 칸을 채운다(그림은 파일 경로, 움직임은 null = 없음).</summary>
    internal void FillNewEmojiFromForm(string name, IEnumerable<string> images, bool stays, EmojiMove? @in, EmojiMove? hold, EmojiMove? @out)
    {
        _newEmojiName.Text = name;
        _newEmojiPaths.Clear();
        AddNewEmojiImages(images);
        _newEmojiStays.IsChecked = stays;
        _newEmojiOnce.IsChecked = !stays;
        _newEmojiIn.SelectedItem = @in is { } i ? EmojiMoves.Label(i) : NoMove;
        _newEmojiHold.SelectedItem = hold is { } h ? EmojiMoves.Label(h) : NoMove;
        _newEmojiOut.SelectedItem = @out is { } o ? EmojiMoves.Label(o) : NoMove;
        RefreshNewEmojiSummary();
    }

    /// <summary>테스트 손잡이 — 첫 화면의 지금 상태(이름 · 그림 파일 이름들 · 아래의 요약).</summary>
    internal (string Name, IReadOnlyList<string> Images, string Summary) NewEmojiFormState
        => (_newEmojiName.Text ?? "", _newEmojiPaths.Select(Path.GetFileName).ToList()!, _newEmojiSummary.Text ?? "");

    /// <summary>테스트 손잡이 — "만들기" / "빈 이모지로 시작".</summary>
    internal string? CreateNewEmojiFromForm() => CreateNewEmoji();

    internal string StartBlankEmojiFromForm()
    {
        string name = AddEmoji();
        _newEmojiWindow?.Close();
        return name;
    }

    private void OpenNewEmoji()
    {
        if (_newEmojiWindow is not null)
        {
            _newEmojiWindow.Activate();
            return;
        }

        _newEmojiName.Text = FreeEmojiName();
        _newEmojiPaths.Clear();
        _newEmojiImages.ItemsSource = Array.Empty<string>();
        _newEmojiOnce.IsChecked = true;
        _newEmojiStays.IsChecked = false;

        foreach ((ComboBox combo, EmojiLifeRole role, EmojiMove? first) in new[]
                 {
                     (_newEmojiIn, EmojiLifeRole.In, (EmojiMove?)EmojiMove.PopIn),
                     (_newEmojiHold, EmojiLifeRole.Hold, null),
                     (_newEmojiOut, EmojiLifeRole.Out, (EmojiMove?)EmojiMove.FloatOut),
                 })
        {
            combo.ItemsSource = EmojiMoves.All.Where(m => EmojiMoves.Role(m) == role).Select(EmojiMoves.Label).Prepend(NoMove).ToList();
            combo.SelectedItem = first is { } move ? EmojiMoves.Label(move) : NoMove;
        }

        Button pick = SmallButton("그림 고르기…", OnPickNewEmojiImagesClick);
        Button drop = SmallButton("빼기", (_, _) =>
        {
            if (_newEmojiImages.SelectedIndex is int index and >= 0 && index < _newEmojiPaths.Count)
            {
                _newEmojiPaths.RemoveAt(index);
                AddNewEmojiImages(Array.Empty<string>());
            }
        });
        Button up = SmallButton("▲", (_, _) => MoveNewEmojiImage(-1));
        Button down = SmallButton("▼", (_, _) => MoveNewEmojiImage(1));
        ToolTip.SetTip(up, "고른 그림을 위로 — 먼저 그려진다(뒤에 깔린다)");
        ToolTip.SetTip(down, "고른 그림을 아래로 — 나중에 그려진다(앞에 온다)");
        Button create = SmallButton("만들기", (_, _) => CreateNewEmoji());
        create.FontSize = 12;
        create.Padding = new Avalonia.Thickness(16, 5);
        create.FontWeight = FontWeight.SemiBold;
        Button blank = SmallButton("빈 이모지로 시작", (_, _) => StartBlankEmojiFromForm());
        Button cancel = SmallButton("취소", (_, _) => _newEmojiWindow?.Close());
        ToolTip.SetTip(blank, "그림도 움직임도 없이 빈 그림 body 하나짜리 이모지를 만든다 — 전부 손으로 짓는다");
        ToolTip.SetTip(_newEmojiImages, "PNG를 여기에 끌어다 놓는다. 한 장이 그림 하나가 된다(그림의 이름은 파일 이름에서 온다). 순서가 그려지는 순서다(아래가 위) — ▲ ▼로 바꾼다");

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 6,
            Children =
            {
                Labeled("이름", _newEmojiName),
                Header("1. 그림"),
                new TextBlock { Text = "PNG를 끌어다 놓거나 고른다. 한 장이 그림 하나가 된다 — 뒤에 더하거나 뺄 수 있다.", FontSize = 10, Opacity = 0.65, TextWrapping = TextWrapping.Wrap },
                _newEmojiImages,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { pick, drop, up, down } },
                Header("2. 어떻게 뜨는가"),
                _newEmojiOnce,
                _newEmojiStays,
                Header("3. 움직임"),
                Labeled("등장", _newEmojiIn),
                Labeled("머묾", _newEmojiHold),
                Labeled("퇴장", _newEmojiOut),
                new TextBlock { Text = "움직임은 키로 들어간다 — 만든 뒤 그래프와 뷰포트에서 고친다. 없음으로 두고 손으로 지어도 된다.", FontSize = 10, Opacity = 0.65, TextWrapping = TextWrapping.Wrap },
                new Border { Height = 1, Margin = new Avalonia.Thickness(0, 6), Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)) },
                _newEmojiSummary,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Avalonia.Thickness(0, 6, 0, 0), Children = { create, blank, cancel } },
            },
        };

        DragDrop.SetAllowDrop(panel, true);
        panel.AddHandler(DragDrop.DropEvent, OnNewEmojiDrop);

        foreach (RadioButton radio in new[] { _newEmojiOnce, _newEmojiStays })
        {
            radio.IsCheckedChanged += OnNewEmojiChoiceChanged;
        }

        foreach (ComboBox combo in new[] { _newEmojiIn, _newEmojiHold, _newEmojiOut })
        {
            combo.SelectionChanged += OnNewEmojiChoiceChanged;
        }

        RefreshNewEmojiSummary();

        var window = new Window { Title = "새 이모지", Width = 460, Height = 640, CanResize = false, Content = new ScrollViewer { Content = panel } };
        window.Closed += (_, _) =>
        {
            foreach (RadioButton radio in new[] { _newEmojiOnce, _newEmojiStays })
            {
                radio.IsCheckedChanged -= OnNewEmojiChoiceChanged;
            }

            foreach (ComboBox combo in new[] { _newEmojiIn, _newEmojiHold, _newEmojiOut })
            {
                combo.SelectionChanged -= OnNewEmojiChoiceChanged;
            }

            // 칸들은 창보다 오래 산다 — 다음에 열 때 다시 쓰도록 떼어 둔다.
            foreach (Panel host in panel.Children.OfType<Panel>())
            {
                host.Children.Clear();
            }

            panel.Children.Clear();
            _newEmojiWindow = null;
        };
        _newEmojiWindow = window;
        window.Show();
    }

    private void OnNewEmojiChoiceChanged(object? sender, EventArgs e) => RefreshNewEmojiSummary();

    private string FreeEmojiName()
    {
        string name = "emoji";

        for (int i = 2; _session.Editor.Document.FindEmoji(name) is not null; i++)
        {
            name = $"emoji_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        return name;
    }

    /// <summary>테스트 손잡이 — 목록에서 그 줄(0부터)을 고르고 ▲(−1) · ▼(+1).</summary>
    internal void MoveNewEmojiImageFromForm(int index, int step)
    {
        _newEmojiImages.SelectedIndex = index;
        MoveNewEmojiImage(step);
    }

    /// <summary>고른 그림을 한 칸 위(−1) · 아래(+1)로 — 순서가 그려지는 순서다(아래가 위). 끝에서는 아무 일도 없다. 고른 줄은 따라간다.</summary>
    private void MoveNewEmojiImage(int step)
    {
        int from = _newEmojiImages.SelectedIndex;
        int to = from + step;

        if (from < 0 || from >= _newEmojiPaths.Count || to < 0 || to >= _newEmojiPaths.Count)
        {
            return;
        }

        (_newEmojiPaths[from], _newEmojiPaths[to]) = (_newEmojiPaths[to], _newEmojiPaths[from]);
        AddNewEmojiImages(Array.Empty<string>());
        _newEmojiImages.SelectedIndex = to;
    }

    /// <summary>그림을 더한다 — PNG만, 같은 파일은 한 번만.</summary>
    private void AddNewEmojiImages(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) && !_newEmojiPaths.Contains(path))
            {
                _newEmojiPaths.Add(path);
            }
        }

        _newEmojiImages.ItemsSource = _newEmojiPaths.Select(Path.GetFileName).ToList();
        RefreshNewEmojiSummary();
    }

    private void OnNewEmojiDrop(object? sender, DragEventArgs e)
    {
        IStorageItem[]? files = e.DataTransfer.TryGetFiles();

        if (files is not null)
        {
            AddNewEmojiImages(files.Select(f => f.Path.LocalPath));
            e.Handled = true;
        }
    }

    private async void OnPickNewEmojiImagesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageProvider? storage = (_newEmojiWindow ?? GetTopLevel(this))?.StorageProvider;

            if (storage is null || !storage.CanOpen)
            {
                _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없다.");
                return;
            }

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "이모지 그림 PNG(여러 장을 고를 수 있다)",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } },
            });
            AddNewEmojiImages(files.Select(f => f.Path.LocalPath));
        }
        catch (Exception exception)
        {
            _session.SetStatus($"그림 실패: {exception.Message}");
        }
    }

    private static EmojiMove? MoveOf(ComboBox combo)
        => combo.SelectedItem is string label && label != NoMove ? EmojiMoves.All.First(m => EmojiMoves.Label(m) == label) : null;

    private EmojiStart NewEmojiStart()
        => new((_newEmojiName.Text ?? "").Trim(), _newEmojiPaths.Select(_session.DocumentRelative).ToList(), _newEmojiStays.IsChecked == true,
            MoveOf(_newEmojiIn), MoveOf(_newEmojiHold), MoveOf(_newEmojiOut));

    /// <summary>아래의 요약 — "만들기"가 무엇을 짓는가.</summary>
    private void RefreshNewEmojiSummary()
    {
        bool stays = _newEmojiStays.IsChecked == true;
        string pictures = _newEmojiPaths.Count == 0 ? "빈 그림 body 하나(그림은 뒤에 넣는다)" : $"그림 {_newEmojiPaths.Count}장";
        string life = stays ? "머무는 이모지" : "한 번 재생하고 사라지는 이모지";
        var moves = new List<string>();

        foreach ((string role, ComboBox combo) in new[] { ("등장", _newEmojiIn), ("머묾", _newEmojiHold), ("퇴장", _newEmojiOut) })
        {
            if (MoveOf(combo) is { } move)
            {
                moves.Add($"{role} {EmojiMoves.Label(move)}");
            }
        }

        _newEmojiSummary.Text = $"만들기 → {pictures} · {life} · " + (moves.Count == 0 ? "움직임 없음(키 없이)" : string.Join(" · ", moves)) + " · 길이 1초(24프레임 — 뒤에 바꾼다)";
    }

    /// <summary>"만들기" — 칸의 것으로 이모지를 한 칸에 짓고 고른다. 못 지었으면(이름이 겹친다 등) 창은 그대로이고 null.</summary>
    private string? CreateNewEmoji()
    {
        EmojiStart start = NewEmojiStart();
        string? made = null;

        Guard("새 이모지", () =>
        {
            made = _session.Editor.StartEmoji(start, _session.EmojiFrame).Name;
        });

        if (made is null)
        {
            return null;
        }

        _newEmojiWindow?.Close();
        _emojiList.SelectedItem = made;
        _session.SetStatus($"새 이모지 '{made}' — {(_newEmojiSummary.Text ?? "").Replace("만들기 → ", "", StringComparison.Ordinal)}");
        return made;
    }
}
