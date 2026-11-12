using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Settings;
using Mt.Authoring.Validation;

namespace Mt.App;

// 이모지의 셸(docs/PLAN-emoji.md E6 · docs/PLAN-ux.md U1~U3) — 이모지 모드의 왼쪽(이모지 목록 + 그 이모지의 그림 목록)과 오른쪽(고른 것의 값).
// 화면의 말은 만드는 사람의 것이다: 리그 → 그림, 오프셋 → 머리에서의 자리, 배율 → 크기, alpha → 투명도. 파일과 코드의 이름은 그대로다.
// 목록에서 고르면 그 이모지가 시간의 주인이 된다: 그래프 · 도프 시트 · 타임 슬라이더가 그 타임라인(emoji:이름)을 묶고,
// 뷰포트는 그 이모지를 캐릭터의 발(로케이터 자리)에 세워 보인다. 모션 · 클립 선택은 풀린다.
// 값은 탭의 칸에서 고친다(각각 되돌리기 한 칸). 키는 그래프 · 도프 시트와 탭의 "지금 키"로 잡는다.
public partial class MainWindow
{
    private const string NoneChoice = "(없음)";
    private const string WholeEmojiChoice = EmojiWords.WholeEmoji;

    private static readonly ChannelKind[] EmojiKinds = { ChannelKind.Position, ChannelKind.Scale, ChannelKind.Euler, ChannelKind.Alpha, ChannelKind.Image };
    private const double ThumbSize = 30;

    /// <summary>이모지의 그림들(문서 기준 경로 → 비트맵) — 뷰포트에 실은 것과 같은 것. 목록의 작은 그림이 쓴다.</summary>
    private readonly Dictionary<string, Bitmap> _emojiBitmaps = new(StringComparer.Ordinal);

    private static readonly IBrush[] SpanBrushes =
    {
        new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6B)),
        new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x30)),
        new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xE8)),
        new SolidColorBrush(Color.FromRgb(0xC0, 0x6C, 0xE0)),
    };

    private readonly ListBox _emojiList = new() { MinHeight = 50, MaxHeight = 260, FontSize = 11 };
    private readonly StackPanel _emojiFields = new() { Spacing = 4 };
    private readonly StackPanel _emojiOwnFields = new() { Spacing = 4 };
    private readonly StackPanel _emojiRigSection = new() { Spacing = 6 };
    private readonly TextBlock _emojiTitle = new() { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly Button _emojiBack = new() { FontSize = 10, Padding = new Avalonia.Thickness(6, 2), IsVisible = false };
    private readonly TextBox _emojiName = new() { FontSize = 11 };
    private readonly TextBox _emojiOffsetX = new() { FontSize = 11 };
    private readonly TextBox _emojiOffsetY = new() { FontSize = 11 };
    private readonly TextBox _emojiFrames = new() { FontSize = 11 };
    private readonly CheckBox _emojiFixedSide = new() { Content = "인물의 방향을 따르지 않기", FontSize = 11 };

    private readonly ListBox _emojiRigList = new() { MinHeight = 40, MaxHeight = 220, FontSize = 11, SelectionMode = SelectionMode.Multiple };

    /// <summary>리그 선택의 주인 — 다른 이모지로 옮기면 리그 선택을 이어받지 않는다(이름이 같은 리그가 있어도).</summary>
    private string? _emojiRigOwner;

    /// <summary>리그 목록에 실어 둔 줄들(이름과 그림) — 달라졌을 때만 목록을 다시 싣는다.</summary>
    private string? _emojiRigRows;
    private readonly StackPanel _emojiRigFields = new() { Spacing = 4 };
    private readonly TextBox _emojiRigName = new() { FontSize = 11 };
    private readonly TextBox _emojiRigImages = new() { FontSize = 11, AcceptsReturn = true, MinHeight = 38, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBox _emojiRigX = new() { FontSize = 11 };
    private readonly TextBox _emojiRigY = new() { FontSize = 11 };
    private readonly TextBox _emojiRigScale = new() { FontSize = 11 };
    private readonly TextBox _emojiRigRotation = new() { FontSize = 11 };
    private readonly CheckBox _emojiRigUpright = new() { Content = "인물이 돌아서도 이 그림은 뒤집지 않기", FontSize = 11 };
    private readonly TextBlock _emojiUprightNote = new() { FontSize = 10, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, IsVisible = false, Text = "이 이모지는 인물의 방향을 따르지 않아 거울에 비치지 않는다 — 이 칸은 뜻이 없다." };
    private readonly TextBox _flipStart = new() { FontSize = 11, Text = "0", Width = 34 };
    private readonly TextBox _flipEvery = new() { FontSize = 11, Text = "3", Width = 34 };
    private readonly TextBox _flipCount = new() { FontSize = 11, Text = "4", Width = 34 };

    private readonly TextBlock _emojiKeyTargetNote = new() { FontSize = 11, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<ChannelKind, Button> _emojiKeyButtons = new();

    private readonly ListBox _emojiSpanList = new() { MinHeight = 40, MaxHeight = 110, FontSize = 11 };
    private readonly StackPanel _emojiSpanFields = new() { Spacing = 4 };
    private readonly TextBox _emojiSpanName = new() { FontSize = 11 };
    private readonly TextBox _emojiSpanStart = new() { FontSize = 11 };
    private readonly TextBox _emojiSpanEnd = new() { FontSize = 11 };
    private readonly ComboBox _emojiIn = new() { FontSize = 11 };
    private readonly ComboBox _emojiHold = new() { FontSize = 11 };
    private readonly ComboBox _emojiOut = new() { FontSize = 11 };
    private readonly TextBlock _emojiKindNote = new() { FontSize = 10, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };

    private readonly ToggleButton _emojiLifeToggle = new() { Content = "게임처럼 재생", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
    private readonly TextBox _emojiHoldCycles = new() { FontSize = 11, Text = "2", Width = 34 };
    private readonly ToggleButton _emojiFacingLeft = new() { Content = "◀ 왼쪽 보기", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ToggleButton _emojiShowHost = new() { Content = "인물", FontSize = 11, Padding = new Avalonia.Thickness(8, 3), IsChecked = true };

    internal string? SelectedEmoji => _emojiList.SelectedItem as string;

    private EmojiAsset? CurrentEmoji => SelectedEmoji is { } name ? _session.Editor.Document.FindEmoji(name) : null;

    /// <summary>고른 그림들(목록의 순서). 여럿이면 뷰포트에서 함께 끌고 함께 키한다.</summary>
    private IReadOnlyList<string> SelectedEmojiRigs => _emojiRigList.SelectedItems?.Cast<string>().ToList() ?? new List<string>();

    /// <summary>값을 고칠 그림 — 하나만 골랐을 때의 그것. 여럿이면 null(오른쪽은 값의 칸을 내지 않는다).</summary>
    private string? SelectedEmojiRig => SelectedEmojiRigs is { Count: 1 } one ? one[0] : null;

    private string? SelectedEmojiSpan => _emojiSpanList.SelectedItem is string row ? row.Split(' ')[0] : null;

    // ── 테스트 손잡이 ─────────────────────────────────────────────────

    /// <summary>테스트 손잡이 — 이모지 목록에서 고른다(null이면 푼다).</summary>
    internal void SelectEmoji(string? name) => _emojiList.SelectedItem = name;

    /// <summary>테스트 손잡이 — "이모지 추가"(이름 emoji, emoji_2 … · 리그 body 하나).</summary>
    internal string AddEmojiFromForm() => AddEmoji();

    /// <summary>테스트 손잡이 — 리그를 고른다.</summary>
    internal void SelectEmojiRigFromForm(string? rig) => SelectEmojiRigsInList(rig is null ? Array.Empty<string>() : new[] { rig });

    /// <summary>테스트 손잡이 — 왼쪽 목록에서 그림 여럿을 고른다 / 고른 것들.</summary>
    internal void SelectEmojiRigsFromForm(params string[] rigs) => SelectEmojiRigsInList(rigs);

    internal IReadOnlyList<string> SelectedEmojiRigsInList => SelectedEmojiRigs;

    /// <summary>테스트 손잡이 — 칸 하나에 글을 넣고 확정한다(Enter와 같은 길).</summary>
    internal void CommitEmojiField(string field, string text)
    {
        TextBox box = field switch
        {
            "name" => _emojiName,
            "offsetX" => _emojiOffsetX,
            "offsetY" => _emojiOffsetY,
            "frames" => _emojiFrames,
            "rigName" => _emojiRigName,
            "rigImages" => _emojiRigImages,
            "rigX" => _emojiRigX,
            "rigY" => _emojiRigY,
            "rigScale" => _emojiRigScale,
            "rigRotation" => _emojiRigRotation,
            "spanName" => _emojiSpanName,
            "spanStart" => _emojiSpanStart,
            "spanEnd" => _emojiSpanEnd,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "모르는 칸"),
        };

        box.Text = text;
        CommitEmojiBox(box);
    }

    /// <summary>테스트 손잡이 — 칸들의 지금 글.</summary>
    internal (string Name, string OffsetX, string Frames, string RigName, string RigImages, string RigScale, string SpanName, string? In, string? Hold, string? Out, bool Enabled) EmojiFormState
        => (_emojiName.Text ?? "", _emojiOffsetX.Text ?? "", _emojiFrames.Text ?? "", _emojiRigName.Text ?? "", _emojiRigImages.Text ?? "", _emojiRigScale.Text ?? "",
            _emojiSpanName.Text ?? "", Choice(_emojiIn), Choice(_emojiHold), Choice(_emojiOut), _emojiFields.IsEnabled);

    /// <summary>테스트 손잡이 — 리그 · 구간 목록의 줄.</summary>
    internal (IReadOnlyList<string> Rigs, IReadOnlyList<string> Spans) EmojiLists
        => (_emojiRigList.ItemsSource?.Cast<string>().ToList() ?? new List<string>(), _emojiSpanList.ItemsSource?.Cast<string>().ToList() ?? new List<string>());

    /// <summary>테스트 손잡이 — 리그 · 구간 · 지금 키 · 그림 넘기기 단추를 누른다.</summary>
    internal void AddEmojiRigFromForm() => Guard("리그 추가", () => AddEmojiRig());

    internal void AddEmojiSpanFromForm() => Guard("구간 추가", () => AddEmojiSpan());

    /// <summary>테스트 손잡이 — 그 그림(null = 이모지 전체)을 고르고 그 종류의 "키 놓기" 단추를 누른다.</summary>
    internal void KeyEmojiNowFromForm(string? rig, ChannelKind kind)
    {
        SelectEmojiRigsInList(rig is null ? Array.Empty<string>() : new[] { rig });
        KeyEmojiKindFromForm(kind);
    }

    /// <summary>테스트 손잡이 — 지금 고른 것에 그 종류의 "키 놓기" 단추를 누른다.</summary>
    internal void KeyEmojiKindFromForm(ChannelKind kind) => Guard("키 놓기", () => KeyEmojiKind(kind));

    /// <summary>테스트 손잡이 — "키 놓기" 단추들 중 누를 수 있는 종류.</summary>
    internal IReadOnlyList<ChannelKind> EmojiKeyKindsEnabled => EmojiKinds.Where(k => _emojiKeyButtons[k].IsEnabled).ToList();

    internal void LayFlipsFromForm(int start, int every, int count)
    {
        _flipStart.Text = start.ToString(CultureInfo.InvariantCulture);
        _flipEvery.Text = every.ToString(CultureInfo.InvariantCulture);
        _flipCount.Text = count.ToString(CultureInfo.InvariantCulture);
        Guard("그림 넘기기", LayFlips);
    }

    /// <summary>테스트 손잡이 — 잇기 칸을 고른다(null = 없음).</summary>
    internal void LinkEmojiFromForm(string? @in, string? hold, string? @out)
    {
        _emojiIn.SelectedItem = @in ?? NoneChoice;
        _emojiHold.SelectedItem = hold ?? NoneChoice;
        _emojiOut.SelectedItem = @out ?? NoneChoice;
    }

    /// <summary>테스트 손잡이 — 수명 재생을 켜고 끈다.</summary>
    internal void SetEmojiLifeFromForm(bool life) => _emojiLifeToggle.IsChecked = life;

    /// <summary>테스트 손잡이 — 좌우의 두 칸(이모지의 fixedSide · 고른 리그의 upright)을 누른다.</summary>
    internal void SetEmojiFixedSideFromForm(bool fixedSide) => _emojiFixedSide.IsChecked = fixedSide;

    internal void SetEmojiRigUprightFromForm(bool upright) => _emojiRigUpright.IsChecked = upright;

    /// <summary>테스트 손잡이 — "왼쪽을 보는 인물로" 보기를 켜고 끈다.</summary>
    internal void SetEmojiFacingLeftFromForm(bool left) => _emojiFacingLeft.IsChecked = left;

    /// <summary>테스트 손잡이 — 도구 띠의 "인물"을 켜고 끈다.</summary>
    internal void SetEmojiShowHostFromForm(bool show) => _emojiShowHost.IsChecked = show;

    /// <summary>테스트 손잡이 — 좌우 칸의 지금 상태(체크 · upright 칸이 흐린가 · 알림이 보이는가).</summary>
    internal (bool FixedSide, bool Upright, bool UprightDimmed, bool UprightNote, bool FacingLeft) EmojiSideFormState
        => (_emojiFixedSide.IsChecked == true, _emojiRigUpright.IsChecked == true, _emojiRigUpright.Opacity < 1, _emojiUprightNote.IsVisible, _emojiFacingLeft.IsChecked == true);

    /// <summary>테스트 손잡이 — 오른쪽이 보이는 것(고른 것의 값만): 이모지의 칸 · 리그의 칸 · 머리글 · 돌아가기 단추.</summary>
    internal (bool EmojiFields, bool RigFields, string Title, bool Back) EmojiInspectorState
        => (_emojiOwnFields.IsVisible, _emojiRigFields.IsVisible, _emojiTitle.Text ?? "", _emojiBack.IsVisible);

    /// <summary>테스트 손잡이 — 왼쪽 목록의 줄에 적힌 둘째 줄(이모지의 종류 · 리그의 첫 그림).</summary>
    internal string EmojiRowNote(string emoji) => EmojiNoteOf(emoji);

    internal string EmojiRigRowNote(string rig) => EmojiRigNoteOf(rig);

    /// <summary>테스트 손잡이 — "지금 키"의 대상 칸(이모지 전체 또는 리그 이름).</summary>
    internal string? EmojiKeyTargetChoice => (_emojiKeyTargetNote.Text ?? "").Replace("대상: ", "", StringComparison.Ordinal);

    /// <summary>테스트 손잡이 — 왼쪽의 리그 묶음이 보이는가(이모지를 골랐을 때만).</summary>
    internal bool EmojiRigSectionVisible => _emojiRigSection.IsVisible;

    /// <summary>테스트 손잡이 — 타임 슬라이더의 이름 붙은 띠.</summary>
    internal IReadOnlyList<SliderMark> TimeSliderMarks => _timeSlider.Marks;

    private static string? Choice(ComboBox combo) => combo.SelectedItem as string is { } s && s != NoneChoice ? s : null;

    // ── 짓기 ──────────────────────────────────────────────────────────

    private void BuildEmojiLeft()
    {
        LeftPanel.Children.Add(Header("이모지"));
        _emojiList.SelectionChanged += (_, _) => OnEmojiSelected();
        ToolTip.SetTip(_emojiList, "이모지 — 리그 여럿(그림 하나에 리그 하나) + 자기 타임라인 + 구간. 고르면 그래프 · 도프 시트가 그 타임라인을, 뷰포트가 캐릭터의 발에 선 그 이모지를 보인다(모션 · 클립 선택은 풀린다)");
        LeftPanel.Children.Add(_emojiList);

        Button add = SmallButton("이모지 추가", (_, _) => OpenNewEmoji());
        ToolTip.SetTip(add, "새 이모지의 첫 화면을 연다 — 그림(PNG를 끌어다 놓는다) · 어떻게 뜨는가 · 움직임을 정하면 움직이는 이모지가 생긴다. '빈 이모지로 시작'도 거기 있다");
        Button duplicate = SmallButton("복제", (_, _) => Guard("이모지 복제", DuplicateEmoji));
        ToolTip.SetTip(duplicate, "고른 이모지를 키까지 통째로 복제한다 — 재사용은 가져와서 고치는 것이다");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { add, SmallButton("삭제", (_, _) => Guard("이모지 삭제", RemoveEmoji)), duplicate },
        });

        // 줄마다 이름 + 둘째 줄(종류). 목록의 값은 이름 그대로다 — 고르기와 손잡이는 이름으로 한다.
        _emojiList.ItemTemplate = new FuncDataTemplate<string>((name, _) => TwoLineRow(name, EmojiNoteOf(name), EmojiThumbOf(name)));

        // 이미 고른 이모지를 다시 누르면 리그 선택이 풀려 오른쪽이 이모지의 값으로 돌아온다.
        _emojiList.Tapped += (_, _) => SelectWholeEmoji();

        // 그 이모지의 리그 — 고른 것이 오른쪽을 정한다(리그를 고르면 리그의 값, 아니면 이모지의 값).
        _emojiRigSection.Children.Add(Header("그림 — 이 이모지를 이루는 조각"));
        _emojiRigList.ItemTemplate = new FuncDataTemplate<string>((name, _) => TwoLineRow(name, EmojiRigNoteOf(name), EmojiRigThumbOf(name)));
        _emojiRigList.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                // 목록에서 고른 것을 뷰포트로 — 뷰포트가 이미 같은 것들을 골랐으면(뷰포트에서 고른 것이 여기로 온 것) 순서를 지킨다.
                IReadOnlyList<string> picked = SelectedEmojiRigs;

                if (!picked.OrderBy(r => r, StringComparer.Ordinal).SequenceEqual(_viewport.SelectedEmojiRigs.OrderBy(r => r, StringComparer.Ordinal)))
                {
                    _viewport.SetSelectedEmojiRigs(picked);
                }

                RefreshEmojiInspector();
            }
        };
        ToolTip.SetTip(_emojiRigList, "이 이모지를 이루는 그림들(런타임의 말로는 리그 — 그림 하나에 리그 하나). 순서가 그려지는 순서다(아래가 위). 고르면 오른쪽이 그 그림의 값을 보인다. Shift · Ctrl로 여럿을 고르면 뷰포트에서 함께 끌고 함께 키한다. 뷰포트에서 그림을 눌러도(Shift · Ctrl로 더하고, 빈 데서 상자를 끌어도) 고른다");
        _emojiRigSection.Children.Add(_emojiRigList);
        _emojiRigSection.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                SmallButton("그림 추가", (_, _) => AddEmojiRigFromForm()),
                SmallButton("삭제", (_, _) => Guard("그림 삭제", RemoveEmojiRig)),
                SmallButton("▲", (_, _) => Guard("그림 순서", () => MoveEmojiRig(-1))),
                SmallButton("▼", (_, _) => Guard("그림 순서", () => MoveEmojiRig(+1))),
            },
        });
        LeftPanel.Children.Add(_emojiRigSection);
    }

    /// <summary>목록의 줄 — 왼쪽에 작은 그림(없으면 빈 칸), 오른쪽에 이름과 둘째 줄.</summary>
    private static Control TwoLineRow(string? name, string note, Bitmap? thumb)
    {
        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = name ?? "", FontSize = 12 },
                new TextBlock { Text = note, FontSize = 10, Opacity = 0.65, IsVisible = note.Length > 0, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        var picture = new Border
        {
            Width = ThumbSize,
            Height = ThumbSize,
            CornerRadius = new Avalonia.CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            Child = thumb is null ? null : new Image { Source = thumb, Stretch = Stretch.Uniform, Margin = new Avalonia.Thickness(2) },
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { picture, text } };
    }

    /// <summary>이모지 목록의 작은 그림 — 맨 위에 그려지는 그림(마지막 리그)의 첫 장. 못 읽었으면 null.</summary>
    private Bitmap? EmojiThumbOf(string? name)
        => _session.Editor.Document.FindEmoji(name ?? "")?.Rigs.LastOrDefault(r => r.Images.Count > 0) is { } rig ? ThumbOfImage(rig.Images[0]) : null;

    /// <summary>그림 목록의 작은 그림 — 그 리그의 첫 장.</summary>
    private Bitmap? EmojiRigThumbOf(string? name)
        => CurrentEmoji?.FindRig(name) is { Images.Count: > 0 } rig ? ThumbOfImage(rig.Images[0]) : null;

    private Bitmap? ThumbOfImage(string image) => _emojiBitmaps.TryGetValue(image, out Bitmap? bitmap) ? bitmap : null;

    /// <summary>테스트 손잡이 — 목록의 줄에 작은 그림이 붙는가(이모지 · 지금 이모지의 그림).</summary>
    internal bool EmojiRowHasThumb(string emoji) => EmojiThumbOf(emoji) is not null;

    internal bool EmojiRigRowHasThumb(string rig) => EmojiRigThumbOf(rig) is not null;

    /// <summary>이모지 목록의 둘째 줄 — 종류.</summary>
    private string EmojiNoteOf(string? name)
        => _session.Editor.Document.FindEmoji(name ?? "") is { } emoji
            ? emoji.Stays ? "머무는 이모지" : "한 번 재생하고 사라진다"
            : "";

    /// <summary>리그 목록의 둘째 줄 — 첫 그림(여러 장이면 장수).</summary>
    private string EmojiRigNoteOf(string? name)
    {
        if (CurrentEmoji?.FindRig(name) is not { } rig)
        {
            return "";
        }

        if (rig.Images.Count == 0)
        {
            return "그림 없음";
        }

        string first = Path.GetFileName(rig.Images[0]);
        return rig.Images.Count == 1 ? first : $"{first} 외 {rig.Images.Count - 1}장";
    }

    private Control BuildEmojiPane()
    {
        var panel = new StackPanel { Spacing = 4 };
        // 머리글 — 지금 오른쪽이 무엇의 값인가. 리그를 보는 중이면 이모지로 돌아가는 단추가 선다.
        _emojiBack.Click += (_, _) => SelectWholeEmoji();
        ToolTip.SetTip(_emojiBack, "그림 선택을 풀고 이모지의 값(이름 · 머리에서의 자리 · 길이 · 구간과 잇기)으로 돌아간다 — 왼쪽 목록에서 이모지를 다시 눌러도 된다");
        panel.Children.Add(_emojiBack);
        panel.Children.Add(_emojiTitle);

        // 이모지의 값 — 리그를 고르지 않았을 때만 보인다.
        _emojiOwnFields.Children.Add(Labeled("이름", _emojiName));
        _emojiOwnFields.Children.Add(Labeled("머리에서 X", _emojiOffsetX));
        _emojiOwnFields.Children.Add(Labeled("머리에서 Y", _emojiOffsetY));
        _emojiOwnFields.Children.Add(Labeled("길이(프레임)", _emojiFrames));
        ToolTip.SetTip(_emojiName, "대본이 부르는 이름(<<emoji 누구 이름>>) — 영문 소문자 · 숫자 · _. 한 번 내보낸 이름은 바꾸지 않는다(대본이 가리킨다)");
        ToolTip.SetTip(_emojiOffsetX, "머리 기준점에서 이모지가 뜨는 자리(px) — 런타임의 말로는 이모지별 오프셋(Emoji_Offset, 로케이터 기준). 이모지 전체를 움직이는 키는 이 값에 더해진다");
        ToolTip.SetTip(_emojiFrames, "타임라인의 마지막 프레임(24fps) — 프레임 0 … 이것까지. 키는 옮기지 않는다");
        _emojiOwnFields.Children.Add(_emojiFixedSide);
        ToolTip.SetTip(_emojiFixedSide, "이모지는 오른쪽을 보는 인물을 기준으로 만들고, 인물이 뒤집히면 런타임이 통째로 거울에 비춘다. 켜면 인물의 방향을 따르지 않고 만든 쪽 그대로 선다(로케이터의 자리만 인물을 따라간다) — 머리 위 한가운데에 뜨는 것, 화면 기준으로 흐르는 것. emojis.json의 fixedSide");
        _emojiFixedSide.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && CurrentEmoji is { } emoji)
            {
                Guard("방향", () => _session.Editor.SetEmojiFixedSide(emoji.Name, _emojiFixedSide.IsChecked == true));
            }
        };

        // 리그의 값 — 왼쪽에서 리그를 골랐을 때만 보인다.
        _emojiRigFields.Children.Add(Labeled("이름", _emojiRigName));
        _emojiRigFields.Children.Add(Labeled("그림 파일", _emojiRigImages));
        Button pick = SmallButton("그림 더하기…", OnPickEmojiImageClick);
        ToolTip.SetTip(pick, "PNG를 골라 그림 목록 끝에 더한다 — 문서 기준 경로로 적힌다. 스프라이트 키는 파일 이름이다");
        _emojiRigFields.Children.Add(pick);
        _emojiRigFields.Children.Add(Labeled("자리 X", _emojiRigX));
        _emojiRigFields.Children.Add(Labeled("자리 Y", _emojiRigY));
        _emojiRigFields.Children.Add(Labeled("크기", _emojiRigScale));
        _emojiRigFields.Children.Add(Labeled("회전", _emojiRigRotation));
        ToolTip.SetTip(_emojiRigName, "이모지 안에서 이 그림(리그)을 가리키는 이름 — 키가 이 이름으로 찾는다. 바꾸면 그 채널이 따라온다");
        ToolTip.SetTip(_emojiRigImages, "그림 경로 — 한 줄에 하나. 첫 장이 기본 스프라이트, 나머지는 그림 바꾸기(image) 채널이 몇 번째인가로 고른다");
        ToolTip.SetTip(_emojiRigX, "기본 자리(EmojiRig_Place) — 키가 아니다. 뷰포트에서 그림을 끌어도 된다");
        ToolTip.SetTip(_emojiRigScale, "기본 크기(배율) — 그림을 이 이모지에서 얼마만 하게 쓰는가. 키의 크기는 이 위에서 1을 기준으로 잡는다");
        _emojiRigFields.Children.Add(_emojiRigUpright);
        _emojiRigFields.Children.Add(_emojiUprightNote);
        ToolTip.SetTip(_emojiRigUpright, "뒤집히면 안 되는 그림(물음표 · 느낌표 · 글자). 이모지가 거울에 비쳐도 이 리그의 그림과 키는 만든 그대로 보이고 기본 자리만 거울 쪽으로 간다. 그래서 바깥으로 흐르는 x 키는 뒤집힌 인물에게는 안쪽으로 흐른다 — 그런 움직임은 이모지 전체의 키로 잡는다. emojis.json의 upright");
        _emojiRigUpright.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing && CurrentEmoji is { } emoji && SelectedEmojiRig is { } rig)
            {
                Guard("뒤집히지 않는 그림", () => _session.Editor.SetEmojiRigUpright(emoji.Name, rig, _emojiRigUpright.IsChecked == true));
            }
        };

        // 패널이 좁으면 다음 줄로 넘긴다 — 한 줄로 두면 오른쪽 끝(깔기)이 잘렸다.
        var flips = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        flips.Children.Add(Small("그림 넘기기"));
        flips.Children.Add(_flipStart);
        flips.Children.Add(Small("fr부터"));
        flips.Children.Add(_flipEvery);
        flips.Children.Add(Small("fr마다"));
        flips.Children.Add(_flipCount);
        flips.Children.Add(Small("번"));
        Button lay = SmallButton("깔기", (_, _) => Guard("그림 넘기기", LayFlips));
        ToolTip.SetTip(lay, "그림 바꾸기 키를 한 번에 깐다 — 그 프레임부터 몇 프레임마다 다음 장(끝나면 처음으로). 그 범위의 그림 키는 바뀐다. 되돌리기 한 번");
        flips.Children.Add(lay);
        _emojiRigFields.Children.Add(flips);

        // 구간과 잇기
        _emojiOwnFields.Children.Add(Header("구간과 잇기"));
        _emojiSpanList.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                RefreshEmojiInspector();
            }
        };
        ToolTip.SetTip(_emojiSpanList, "이름 붙은 구간(프레임, 끝 포함) — 겹쳐도 된다. 내보낼 때 구간마다 따로 선 클립이 된다(구간의 첫 프레임이 클립의 프레임 0)");
        _emojiOwnFields.Children.Add(_emojiSpanList);
        Button addSpan = SmallButton("구간 추가", (_, _) => AddEmojiSpanFromForm());
        ToolTip.SetTip(addSpan, "지금 프레임부터 6프레임(타임라인 안으로)의 구간을 더한다 — 이름 span, span_2…");
        _emojiOwnFields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { addSpan, SmallButton("삭제", (_, _) => Guard("구간 삭제", RemoveEmojiSpan)) },
        });
        _emojiSpanFields.Children.Add(Labeled("구간 이름", _emojiSpanName));
        _emojiSpanFields.Children.Add(Labeled("시작(fr)", _emojiSpanStart));
        _emojiSpanFields.Children.Add(Labeled("끝(fr)", _emojiSpanEnd));
        _emojiOwnFields.Children.Add(_emojiSpanFields);
        _emojiOwnFields.Children.Add(Labeled("등장", _emojiIn));
        _emojiOwnFields.Children.Add(Labeled("머묾(되풀이)", _emojiHold));
        _emojiOwnFields.Children.Add(Labeled("퇴장", _emojiOut));
        _emojiOwnFields.Children.Add(_emojiKindNote);
        ToolTip.SetTip(_emojiHold, "머묾을 이으면 머무는 이모지(등장 한 번 → 머묾 되풀이 → emoji_off에 퇴장), 잇지 않으면 타임라인 전체(main)를 한 번 재생하고 사라진다");

        foreach (ComboBox combo in new[] { _emojiIn, _emojiHold, _emojiOut })
        {
            combo.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && combo.SelectedIndex >= 0 && CurrentEmoji is { } emoji)
                {
                    Guard("잇기", () => _session.Editor.SetEmojiLinks(emoji.Name, Choice(_emojiIn), Choice(_emojiHold), Choice(_emojiOut)));
                }
            };
        }


        _emojiFields.Children.Add(_emojiOwnFields);
        _emojiFields.Children.Add(_emojiRigFields);

        // 키 놓기 — 종류마다 단추 하나. 대상은 고른 그림들(없으면 이모지 전체)이다.
        _emojiFields.Children.Add(Header("키 놓기 — 지금 프레임에"));
        _emojiFields.Children.Add(_emojiKeyTargetNote);
        var keyRow = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };

        foreach (ChannelKind kind in EmojiKinds)
        {
            ChannelKind captured = kind;
            Button key = SmallButton(EmojiWords.Kind(kind), (_, _) => Guard("키 놓기", () => KeyEmojiKind(captured)));
            key.FontSize = 11;
            key.Padding = new Avalonia.Thickness(9, 4);
            ToolTip.SetTip(key, kind switch
            {
                ChannelKind.Position => "자리의 키 — 고른 것의 지금 자리(X · Y)를 지금 프레임에 놓는다",
                ChannelKind.Scale => "크기의 키 — 지금 크기(X · Y)를 지금 프레임에 놓는다",
                ChannelKind.Euler => "회전의 키 — 지금 회전을 지금 프레임에 놓는다",
                ChannelKind.Alpha => "투명도의 키 — 지금 투명도(0 안 보임 … 1 다 보임)를 지금 프레임에 놓는다. 놓은 뒤 그래프에서 값을 바꾼다",
                _ => "그림 바꾸기의 키 — 지금 보이는 장을 지금 프레임에 놓는다(늘 계단). 그림 파일이 있는 그림을 골랐을 때만. 놓은 뒤 그래프에서 몇 번째 장인지 바꾼다",
            });
            _emojiKeyButtons[kind] = key;
            keyRow.Children.Add(key);
        }

        Button step = SmallButton("계단", (_, _) => _graph.SetTangentMode(TangentMode.Step));
        ToolTip.SetTip(step, "그래프에서 고른 키를 계단으로 — 다음 키까지 그 값으로 멈춘다(emojis/2의 step). 그림 바꾸기의 키는 언제나 계단이다");
        keyRow.Children.Add(new Border { Width = 1, Margin = new Avalonia.Thickness(3, 2), Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)) });
        keyRow.Children.Add(step);
        _emojiFields.Children.Add(keyRow);
        _emojiFields.Children.Add(new TextBlock { Text = "여러 그림을 골랐으면 전부에 놓인다(한 칸). 채널이 없으면 생긴다.", FontSize = 10, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });

        // 보기 — 단추는 수명의 띠(게임처럼 재생 · 머묾 바퀴)와 뷰포트의 도구 띠(인물 · 왼쪽 보기 · 맞춤)에 있다.
        _emojiLifeToggle.IsCheckedChanged += (_, _) =>
        {
            _viewport.EmojiLife = _emojiLifeToggle.IsChecked == true;
            RefreshEmojiMarks();
            SyncLifeStripTime();
        };
        ToolTip.SetTip(_emojiLifeToggle, "켜면 뷰포트 · 타임 슬라이더가 수명을 재생한다 — 등장 → 머묾 × 바퀴 → 퇴장(머묾이 없으면 main 한 번). 끄면 타임라인(그래프와 같은 시각)");
        ToolTip.SetTip(_emojiHoldCycles, "게임처럼 재생할 때 머묾을 몇 바퀴 돈 뒤 내리는가");
        OnCommit(_emojiHoldCycles, text =>
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycles))
            {
                _viewport.EmojiHoldCycles = cycles;
                RefreshEmojiMarks();
            }
        });

        panel.Children.Add(_emojiFields);

        foreach (TextBox box in new[] { _emojiName, _emojiOffsetX, _emojiOffsetY, _emojiFrames, _emojiRigName, _emojiRigImages, _emojiRigX, _emojiRigY, _emojiRigScale, _emojiRigRotation, _emojiSpanName, _emojiSpanStart, _emojiSpanEnd })
        {
            TextBox captured = box;
            OnCommit(captured, _ => CommitEmojiBox(captured));
        }

        // 뷰포트에서 그림을 골랐거나 빈 곳을 눌러 풀었다 — 왼쪽 목록과 오른쪽이 따라온다.
        _viewport.EmojiRigPicked += (_, _) => SelectEmojiRigsInList(_viewport.SelectedEmojiRigs);

        return panel;
    }

    /// <summary>칸 하나의 확정 — 글을 그 칸의 명령으로(되돌리기 한 칸). 못 읽는 수는 무시하고 칸을 되돌린다.</summary>
    private void CommitEmojiBox(TextBox box)
    {
        if (_refreshing || CurrentEmoji is not { } emoji)
        {
            return;
        }

        MotionEditor editor = _session.Editor;
        string text = (box.Text ?? "").Trim();
        EmojiRigAsset? rig = emoji.FindRig(SelectedEmojiRig);
        EmojiSpan? span = emoji.FindSpan(SelectedEmojiSpan);

        Guard("이모지", () =>
        {
            if (box == _emojiName && text != emoji.Name)
            {
                editor.RenameEmoji(emoji.Name, text);
                _emojiList.SelectedItem = text;
            }
            else if (box == _emojiOffsetX && TryFloat(text, out float x))
            {
                editor.SetEmojiOffset(emoji.Name, x, emoji.OffsetY);
            }
            else if (box == _emojiOffsetY && TryFloat(text, out float y))
            {
                editor.SetEmojiOffset(emoji.Name, emoji.OffsetX, y);
            }
            else if (box == _emojiFrames && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames) && frames >= 0)
            {
                editor.SetDuration(emoji.Timeline.Name, EmojiAsset.SecondsOf(frames));
            }
            else if (rig is not null && box == _emojiRigName && text != rig.Name)
            {
                editor.RenameEmojiRig(emoji.Name, rig.Name, text);
                _emojiRigList.SelectedItem = text;
            }
            else if (rig is not null && box == _emojiRigImages)
            {
                List<string> images = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                editor.SetEmojiRigImages(emoji.Name, rig.Name, images);
            }
            else if (rig is not null && (box == _emojiRigX || box == _emojiRigY || box == _emojiRigScale || box == _emojiRigRotation) && TryFloat(text, out float v))
            {
                editor.SetEmojiRigPlace(
                    emoji.Name, rig.Name,
                    box == _emojiRigX ? v : rig.PlaceX,
                    box == _emojiRigY ? v : rig.PlaceY,
                    box == _emojiRigScale ? v : rig.Scale,
                    box == _emojiRigRotation ? v : rig.Rotation);
            }
            else if (span is not null && box == _emojiSpanName && text != span.Name)
            {
                editor.RenameEmojiSpan(emoji.Name, span.Name, text);
                RefreshEmojiInspector(selectSpan: text);
            }
            else if (span is not null && (box == _emojiSpanStart || box == _emojiSpanEnd)
                     && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int f))
            {
                editor.SetEmojiSpan(emoji.Name, span.Name, box == _emojiSpanStart ? f : span.Start, box == _emojiSpanEnd ? f : span.End);
            }
        });

        RefreshEmojiInspector(); // 못 읽은 글은 문서의 값으로 되돌린다
    }

    // ── 명령 ──────────────────────────────────────────────────────────

    private string AddEmoji()
    {
        MotionDocument document = _session.Editor.Document;
        string name = "emoji";

        for (int i = 2; document.FindEmoji(name) is not null; i++)
        {
            name = $"emoji_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.Batch(MotionChangeKind.Emoji, () =>
        {
            _session.Editor.AddEmoji(name);
            _session.Editor.AddEmojiRig(name, "body");
        });

        _emojiList.SelectedItem = name;
        return name;
    }

    private void RemoveEmoji()
    {
        if (CurrentEmoji is { } emoji)
        {
            _session.Editor.RemoveEmoji(emoji.Name);
        }
    }

    private void DuplicateEmoji()
    {
        if (CurrentEmoji is not { } emoji)
        {
            return;
        }

        string name = emoji.Name + "_2";

        for (int i = 3; _session.Editor.Document.FindEmoji(name) is not null; i++)
        {
            name = $"{emoji.Name}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.DuplicateEmoji(emoji.Name, name);
        _emojiList.SelectedItem = name;
    }

    private void AddEmojiRig()
    {
        if (CurrentEmoji is not { } emoji)
        {
            return;
        }

        string name = "rig";

        for (int i = 2; emoji.FindRig(name) is not null; i++)
        {
            name = $"rig_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.AddEmojiRig(emoji.Name, name);
        _emojiRigList.SelectedItem = name;
    }

    /// <summary>고른 그림들을 지운다 — 여럿이어도 한 칸.</summary>
    private void RemoveEmojiRig()
    {
        if (CurrentEmoji is not { } emoji || SelectedEmojiRigs is not { Count: > 0 } rigs)
        {
            return;
        }

        _session.Editor.Batch(MotionChangeKind.Emoji, () =>
        {
            foreach (string rig in rigs)
            {
                _session.Editor.RemoveEmojiRig(emoji.Name, rig);
            }
        });
    }

    /// <summary>
    /// 이모지를 눌렀다 — 그림의 선택을 풀고 대상을 "이모지 전체"로 한다(뷰포트의 매니퓰레이터가 이모지 전체에 선다 — 머리에서의 자리를 끈다).
    /// 뷰포트의 빈 곳을 누르면 이것도 풀린다.
    /// </summary>
    private void SelectWholeEmoji()
    {
        SelectEmojiRigsInList(Array.Empty<string>());
        _viewport.EmojiWholeSelected = true;
    }

    /// <summary>테스트 손잡이 — 왼쪽 목록에서 이모지를 누른다.</summary>
    internal void TapEmojiFromForm() => SelectWholeEmoji();

    /// <summary>목록의 선택을 그 그림들로 맞춘다 — 같으면 건드리지 않는다. 알림(SelectionChanged)은 한 번만 간다.</summary>
    private void SelectEmojiRigsInList(IReadOnlyList<string> rigs)
    {
        List<string> rows = _emojiRigList.ItemsSource?.Cast<string>().ToList() ?? new List<string>();
        List<int> want = rigs.Select(r => rows.IndexOf(r)).Where(i => i >= 0).OrderBy(i => i).ToList();

        if (want.SequenceEqual(_emojiRigList.Selection.SelectedIndexes.OrderBy(i => i)))
        {
            return;
        }

        _emojiRigList.Selection.BeginBatchUpdate();

        try
        {
            _emojiRigList.Selection.Clear();

            foreach (int index in want)
            {
                _emojiRigList.Selection.Select(index);
            }
        }
        finally
        {
            _emojiRigList.Selection.EndBatchUpdate();
        }
    }

    private void MoveEmojiRig(int step)
    {
        if (CurrentEmoji is { } emoji && SelectedEmojiRig is { } rig)
        {
            int index = emoji.Rigs.FindIndex(r => r.Name == rig);
            _session.Editor.MoveEmojiRig(emoji.Name, rig, index + step);
            _emojiRigList.SelectedItem = rig;
        }
    }

    private void AddEmojiSpan()
    {
        if (CurrentEmoji is not { } emoji)
        {
            return;
        }

        string name = "span";

        for (int i = 2; emoji.FindSpan(name) is not null; i++)
        {
            name = $"span_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        int start = Math.Clamp(EmojiAsset.FrameOf((float)(_viewport.EmojiTimelineTime ?? 0)), 0, emoji.LastFrame);
        int end = Math.Min(emoji.LastFrame, start + 5);
        _session.Editor.AddEmojiSpan(emoji.Name, name, start, end);
        RefreshEmojiInspector(selectSpan: name);
    }

    private void RemoveEmojiSpan()
    {
        if (CurrentEmoji is { } emoji && SelectedEmojiSpan is { } span)
        {
            _session.Editor.RemoveEmojiSpan(emoji.Name, span);
        }
    }

    /// <summary>키를 놓을 대상 — 고른 그림들, 없으면 이모지 전체(빈 이름).</summary>
    private IReadOnlyList<string> EmojiKeyTargets() => SelectedEmojiRigs.Count > 0 ? SelectedEmojiRigs.ToList() : new[] { "" };

    /// <summary>
    /// "키 놓기"의 단추 — 고른 것 전부의 그 종류를 지금 값(그 프레임의 미리보기 값, 그림 바꾸기는 지금 보이는 장)으로 지금 프레임에 키한다. 한 칸.
    /// 그림 바꾸기는 그림 파일이 있는 그림에만 놓인다.
    /// </summary>
    private void KeyEmojiKind(ChannelKind kind)
    {
        if (CurrentEmoji is not { } emoji)
        {
            return;
        }

        IReadOnlyList<string> targets = EmojiKeyTargets();

        if (kind == ChannelKind.Image)
        {
            targets = targets.Where(t => emoji.FindRig(t) is { Images.Count: > 0 }).ToList();

            if (targets.Count == 0)
            {
                throw new InvalidOperationException("그림 바꾸기는 그림 파일이 있는 그림에만 쓴다 — 왼쪽에서 그림을 고른다(이모지 전체에는 넘길 장이 없다).");
            }
        }

        _session.Editor.Batch(MotionChangeKind.Structure, () =>
        {
            foreach (string target in targets)
            {
                KeyEmojiAt(emoji, target, kind);
            }
        });
    }

    /// <summary>지금 시각(프레임에 붙여)에 그 대상(""= 이모지 전체)의 지금 값을 키로 — "키 놓기"의 단추와 그래프 목록의 "키 없음 ＋"가 같이 쓴다.</summary>
    private void KeyEmojiAt(EmojiAsset emoji, string rig, ChannelKind kind)
    {
        if (_session.EmojiFrame is not { } frame)
        {
            return;
        }

        if (kind == ChannelKind.Image && rig.Length == 0)
        {
            throw new InvalidOperationException("그림 바꾸기는 그림에만 쓴다 — 대상에서 그림을 고른다(이모지 전체에는 그림이 없다).");
        }

        double seconds = Math.Clamp(_viewport.EmojiTimelineTime ?? 0, 0, emoji.Timeline.Duration);
        float time = (float)TimeSnap.Snap(seconds, 1.0 / EmojiAsset.Fps);
        EmojiClip? clip = EmojiPreview.Timeline(emoji, frame);
        EmojiNodePose pose = EmojiClipMath.SampleNode(clip, rig, time * EmojiAsset.Fps);
        IReadOnlyList<(Axis, float)> values = kind switch
        {
            ChannelKind.Position => new[] { (Axis.X, pose.Position.X), (Axis.Y, pose.Position.Y) },
            ChannelKind.Scale => new[] { (Axis.X, pose.Scale.X), (Axis.Y, pose.Scale.Y) },
            ChannelKind.Euler => new[] { (Axis.Z, pose.Rotation) },
            ChannelKind.Alpha => new[] { (Axis.X, pose.Alpha) },
            _ => new[] { (Axis.X, (float)Math.Max(0, emoji.FindRig(rig)?.Images.FindIndex(i => EmojiRigAsset.SpriteKeyOf(i) == pose.SpriteKey) ?? 0)) },
        };

        _session.Editor.KeyComponents(emoji.Timeline.Name, frame.TargetNode(rig), kind, time, values, kind == ChannelKind.Image ? TangentMode.Step : DefaultTangent);
    }

    /// <summary>
    /// 이모지 타임라인의 "키 없음" 줄들 — 키를 받는 노드만(이모지 전체 · 리그마다 그림 노드), 종류는 이모지의 다섯(그림 바꾸기는 그림이 있는 리그에만).
    /// 리그 덤프의 노드(EmojiRig_Place 같은 기본 자리)는 키를 받지 않으니 내놓지 않는다. 문서에 이모지 리그가 없으면 null.
    /// </summary>
    private IReadOnlyList<UnkeyedTarget>? EmojiUnkeyedTargets(string emojiName)
    {
        if (_session.Editor.Document.FindEmoji(emojiName) is not { } emoji || _session.EmojiFrame is not { } frame)
        {
            return null;
        }

        var targets = new List<UnkeyedTarget>();

        foreach (string rig in emoji.Rigs.Select(r => r.Name).Prepend(""))
        {
            string label = rig.Length == 0 ? WholeEmojiChoice : rig;

            foreach (ChannelKind kind in EmojiKinds)
            {
                if (kind == ChannelKind.Image && (rig.Length == 0 || emoji.FindRig(rig) is not { Images.Count: > 0 }))
                {
                    continue;
                }

                string capturedRig = rig;
                ChannelKind capturedKind = kind;
                targets.Add(new UnkeyedTarget(
                    frame.TargetNode(rig),
                    kind,
                    label,
                    () => KeyEmojiAt(emoji, capturedRig, capturedKind),
                    () =>
                    {
                        if (capturedRig.Length > 0)
                        {
                            _emojiRigList.SelectedItem = capturedRig;
                        }
                    }));
            }
        }

        return targets;
    }

    private void LayFlips()
    {
        if (CurrentEmoji is not { } emoji || SelectedEmojiRig is not { } rig)
        {
            throw new InvalidOperationException("왼쪽에서 그림을 먼저 고른다.");
        }

        if (!int.TryParse(_flipStart.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
            || !int.TryParse(_flipEvery.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int every)
            || !int.TryParse(_flipCount.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            throw new InvalidOperationException("넘기기의 시작 · 간격 · 횟수는 정수다.");
        }

        int laid = _session.Editor.LayEmojiImageFlips(emoji.Name, rig, start, every, count, _session.EmojiFrame);
        _session.SetStatus($"그림 넘기기: {rig}에 키 {laid}개");
    }

    private async void OnPickEmojiImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (CurrentEmoji is not { } emoji || emoji.FindRig(SelectedEmojiRig) is not { } rig)
            {
                _session.SetStatus("왼쪽에서 그림을 먼저 고른다.");
                return;
            }

            string? path = await PickOpenAsync("이모지 그림 PNG", new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } });

            if (path is not null)
            {
                _session.Editor.SetEmojiRigImages(emoji.Name, rig.Name, rig.Images.Append(_session.DocumentRelative(path)));
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"그림 실패: {exception.Message}");
        }
    }

    private async void OnExportEmojisClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("emojis.json 내보내기", "emojis.json", new FilePickerFileType("emojis.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                _session.ExportEmojis(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"이모지 내보내기 실패: {exception.Message}");
        }
    }

    // ── 고르기 · 묶기 ─────────────────────────────────────────────────

    private void OnEmojiSelected()
    {
        if (_refreshing)
        {
            return;
        }

        if (SelectedEmoji is not null)
        {
            EnterWorkMode(WorkMode.Emoji);

            // 시간의 주인은 하나 — 클립과 모션을 푼다. 모션 선택을 풀면 그 알림이 다시 묶는다(BindMotion → 이모지).
            DeselectClip();

            if (SelectedMotion is not null)
            {
                _motionList.SelectedItem = null;
                RefreshEmojiInspector();
                return;
            }
        }

        BindMotion(SelectedMotion);
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshEmojiInspector();
    }

    /// <summary>모션이나 클립을 골랐다 — 이모지 선택을 푼다(다시 묶는 일은 고른 쪽이 한다).</summary>
    private void DeselectEmoji()
    {
        if (SelectedEmoji is null)
        {
            return;
        }

        _refreshDepth++;

        try
        {
            _emojiList.SelectedItem = null;
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshEmojiInspector();
    }

    /// <summary>그래프 · 도프 시트 · 타임 슬라이더가 묶을 모션 — 고른 모션, 없고 이모지를 골랐으면 그 타임라인.</summary>
    private string? GraphMotionFor(string? motionName)
        => motionName ?? (SelectedClip is null && SelectedEmoji is { } emoji ? EmojiAsset.TimelineNameOf(emoji) : null);

    private void RefreshEmojis()
    {
        _refreshDepth++;

        try
        {
            string? selected = SelectedEmoji ?? _viewport.EmojiName;
            List<string> names = _session.Editor.Document.Emojis.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            _emojiList.ItemsSource = names;
            _emojiList.SelectedIndex = selected is null || SelectedMotion is not null || SelectedClip is not null ? -1 : names.IndexOf(selected);
        }
        finally
        {
            _refreshDepth--;
        }

        // 고른 이모지가 사라졌거나 이름이 바뀌었으면(삭제 · 되돌리기) 뷰포트를 다시 묶는다.
        if (_viewport.EmojiName != (SelectedMotion is null && SelectedClip is null ? SelectedEmoji : null))
        {
            BindMotion(SelectedMotion);
        }

        RefreshEmojiInspector();
    }

    private void RefreshEmojiInspector(string? selectSpan = null)
    {
        _refreshDepth++;

        try
        {
            EmojiAsset? emoji = CurrentEmoji;
            _emojiFields.IsEnabled = emoji is not null;
            _emojiRigSection.IsVisible = emoji is not null;

            // 리그 선택은 그 이모지의 것이다 — 다른 이모지로 옮기면 풀린다. 고르지 않은 채면 오른쪽은 이모지의 값이다.
            IReadOnlyList<string> rigsBefore = emoji is not null && _emojiRigOwner == emoji.Name
                ? (SelectedEmojiRigs.Count > 0 ? SelectedEmojiRigs : _viewport.SelectedEmojiRigs.ToList())
                : new List<string>();
            _emojiRigOwner = emoji?.Name;
            string? spanBefore = selectSpan ?? SelectedEmojiSpan;
            List<string> rigs = emoji?.Rigs.Select(r => r.Name).ToList() ?? new List<string>();

            // 목록은 줄(이름 · 둘째 줄)이 달라졌을 때만 다시 싣는다 — 고르는 도중(목록의 SelectionChanged 안)에 다시 실으면 목록이 거부한다.
            string rigRows = string.Join("\n", emoji?.Rigs.Select(r => $"{r.Name}\t{string.Join("|", r.Images)}") ?? Enumerable.Empty<string>());

            if (rigRows != _emojiRigRows || _emojiRigList.ItemsSource is null)
            {
                _emojiRigRows = rigRows;
                _emojiRigList.ItemsSource = rigs;
            }

            SelectEmojiRigsInList(rigsBefore);

            // 사라진 그림은 뷰포트의 선택에서도 뺀다(순서는 뷰포트의 것을 지킨다 — 마지막이 매니퓰레이터의 자리다).
            IReadOnlyList<string> selectedNow = SelectedEmojiRigs;
            _viewport.SetSelectedEmojiRigs(_viewport.SelectedEmojiRigs.Where(selectedNow.Contains).Concat(selectedNow).Distinct(StringComparer.Ordinal));

            List<string> spans = emoji?.Spans.Select(s => $"{s.Name} {s.Start}~{s.End}").ToList() ?? new List<string>();
            _emojiSpanList.ItemsSource = spans;
            int spanIndex = emoji?.Spans.FindIndex(s => s.Name == spanBefore) ?? -1;
            _emojiSpanList.SelectedIndex = spanIndex >= 0 ? spanIndex : spans.Count > 0 ? 0 : -1;

            EmojiRigAsset? rig = emoji?.FindRig(SelectedEmojiRig);
            EmojiSpan? span = emoji?.FindSpan(SelectedEmojiSpan);
            // 여럿을 골랐으면 값의 칸을 내지 않는다 — 뷰포트에서 함께 끌고 함께 키한다. 값은 하나만 골라 고친다.
            int picked = SelectedEmojiRigs.Count;
            _emojiOwnFields.IsVisible = picked == 0;
            _emojiRigFields.IsVisible = rig is not null;
            _emojiBack.IsVisible = picked > 0;
            _emojiBack.Content = emoji is null ? "" : $"← {emoji.Name}";
            _emojiTitle.Text = emoji is null
                ? "왼쪽 '이모지'에서 고르거나 더한다."
                : picked > 1
                    ? $"그림 {picked}개 — {emoji.Name}. 뷰포트에서 함께 끌고(W · E · R) 함께 키한다(S). 값은 하나만 골라 고친다."
                : rig is not null
                    ? $"그림 {rig.Name} — {emoji.Name}"
                    : $"{emoji.Name} — 그림 {emoji.Rigs.Count}장 · {emoji.LastFrame + 1}프레임 · {(emoji.Stays ? "머무는 이모지" : "한 번 재생하고 사라진다")}";
            _emojiSpanFields.IsEnabled = span is not null;

            void Write(TextBox box, string text)
            {
                if (!box.IsFocused)
                {
                    box.Text = text;
                }
            }

            Write(_emojiName, emoji?.Name ?? "");
            Write(_emojiOffsetX, emoji is null ? "" : F(emoji.OffsetX));
            Write(_emojiOffsetY, emoji is null ? "" : F(emoji.OffsetY));
            Write(_emojiFrames, emoji is null ? "" : emoji.LastFrame.ToString(CultureInfo.InvariantCulture));
            Write(_emojiRigName, rig?.Name ?? "");
            Write(_emojiRigImages, rig is null ? "" : string.Join("\n", rig.Images));
            Write(_emojiRigX, rig is null ? "" : F(rig.PlaceX));
            Write(_emojiRigY, rig is null ? "" : F(rig.PlaceY));
            Write(_emojiRigScale, rig is null ? "" : F(rig.Scale));
            Write(_emojiRigRotation, rig is null ? "" : F(rig.Rotation));
            _emojiFixedSide.IsChecked = emoji?.FixedSide == true;
            _emojiRigUpright.IsChecked = rig?.Upright == true;

            // 방향을 따르지 않는 이모지는 거울에 비치지 않는다 — upright 칸은 뜻이 없으니 흐리게 두고 한 줄로 알린다(막지는 않는다).
            bool meaningless = emoji?.FixedSide == true;
            _emojiRigUpright.Opacity = meaningless ? 0.5 : 1;
            _emojiUprightNote.IsVisible = meaningless;
            Write(_emojiSpanName, span?.Name ?? "");
            Write(_emojiSpanStart, span?.Start.ToString(CultureInfo.InvariantCulture) ?? "");
            Write(_emojiSpanEnd, span?.End.ToString(CultureInfo.InvariantCulture) ?? "");

            IReadOnlyList<string> keyTargets = EmojiKeyTargets();
            _emojiKeyTargetNote.Text = "대상: " + string.Join(" · ", keyTargets.Select(t => t.Length == 0 ? WholeEmojiChoice : t));
            _emojiKeyButtons[ChannelKind.Image].IsEnabled = keyTargets.Any(t => emoji?.FindRig(t) is { Images.Count: > 0 });

            void Fill(ComboBox combo, string? current)
            {
                var choices = new List<string> { NoneChoice };
                choices.AddRange(emoji?.Spans.Select(s => s.Name) ?? Enumerable.Empty<string>());

                // 문서에 없는 이름이 적혀 있으면 그대로 보인다 — 조용히 지우지 않는다(검증이 알린다).
                if (current is not null && !choices.Contains(current))
                {
                    choices.Add(current);
                }

                combo.ItemsSource = choices;
                combo.SelectedIndex = current is null ? 0 : choices.IndexOf(current);
            }

            Fill(_emojiIn, emoji?.In);
            Fill(_emojiHold, emoji?.Hold);
            Fill(_emojiOut, emoji?.Out);
            _emojiKindNote.Text = emoji is null
                ? ""
                : emoji.Stays
                    ? "머묾을 이었다 — 등장을 한 번, 머묾을 되풀이하며 남는다. emoji_off에 퇴장을 재생하고 사라진다."
                    : "머묾을 잇지 않았다 — 타임라인 전체(main)를 한 번 재생하고 스스로 사라진다.";
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshEmojiMarks();
        RefreshLifeStrip();
        SyncEmojiGraphScope();
        SyncEmojiImages();
    }

    private string _emojiImageSet = "";

    /// <summary>문서의 그림 파일들이 달라졌으면(그림을 더했다 · 새 이모지 · 되돌리기) 비트맵을 다시 싣는다 — 뷰포트와 목록의 작은 그림이 따라온다.</summary>
    private void SyncEmojiImages()
    {
        string set = string.Join("\n", _session.Editor.Document.Emojis.SelectMany(e => e.Rigs).SelectMany(r => r.Images).Distinct(StringComparer.Ordinal));

        if (set != _emojiImageSet)
        {
            LoadEmojiImages();
        }
    }

    // ── 그래프의 범위 ─────────────────────────────────────────────────
    //
    // 그림을 골랐으면 그래프의 왼쪽 목록과 곡선은 그 그림(들)의 것만 보인다. 고르지 않았으면(이모지를 고른 것) 전부 — 이모지 전체와 모든 그림.

    private string _emojiGraphScope = "";

    /// <summary>테스트 손잡이 — 그래프에 보이는 곡선들의 노드.</summary>
    internal IReadOnlyList<string> VisibleGraphNodes => _graph.Curves.Where(c => c.Visible).Select(c => c.Curve.Node).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>그래프가 보일 노드들 — 고른 그림들의 키를 받는 노드. 그림을 고르지 않았거나 이모지의 타임라인이 아니면 null(전부).</summary>
    private HashSet<string>? EmojiGraphNodes()
    {
        if (_graph.MotionName is not { } bound || !EmojiAsset.IsTimelineName(bound, out string name)
            || CurrentEmoji is not { } emoji || emoji.Name != name || _session.EmojiFrame is not { } frame)
        {
            return null;
        }

        IReadOnlyList<string> picked = SelectedEmojiRigs;
        return picked.Count == 0 ? null : picked.Select(frame.TargetNode).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>고른 것이 달라졌으면 그래프의 목록을 다시 짓고 곡선도 그 범위만 보인다. 같으면 건드리지 않는다(곡선을 따로 골라 본 것을 지킨다).</summary>
    private void SyncEmojiGraphScope()
    {
        HashSet<string>? nodes = EmojiGraphNodes();
        string scope = nodes is null ? "" : string.Join("\n", nodes.OrderBy(n => n, StringComparer.Ordinal));

        if (scope == _emojiGraphScope)
        {
            return;
        }

        _emojiGraphScope = scope;
        RefreshChannels();
        _graph.SetVisibleAll(c => nodes is null || nodes.Contains(c.Node));
        _graph.FitAll(); // 보이는 곡선에 눈금을 맞춘다 — 값이 작은 곡선이 바닥에 깔려 보이지 않게
    }

    /// <summary>
    /// 타임 슬라이더의 띠 — 타임라인 보기면 구간들, 수명 보기면 단계들(등장 · 머묾 × 바퀴 · 퇴장, 또는 main).
    /// </summary>
    private void RefreshEmojiMarks()
    {
        if (CurrentEmoji is not { } emoji || _viewport.EmojiName != emoji.Name || _session.EmojiFrame is not { } frame)
        {
            _timeSlider.Marks = Array.Empty<SliderMark>();
            return;
        }

        var marks = new List<SliderMark>();

        if (!_viewport.EmojiLife)
        {
            for (int i = 0; i < emoji.Spans.Count; i++)
            {
                EmojiSpan span = emoji.Spans[i];
                string role = span.Name == emoji.In ? " · 등장" : span.Name == emoji.Hold ? " · 머묾" : span.Name == emoji.Out ? " · 퇴장" : "";
                marks.Add(new SliderMark(span.Name + role, EmojiAsset.SecondsOf(span.Start), EmojiAsset.SecondsOf(span.End), EmojiLife.RoleOf(emoji, span.Name) is { } linked ? EmojiLifeStripView.BrushOf(linked) : SpanBrushes[3]));
            }
        }
        else if (EmojiPreview.Definition(emoji, frame, out _) is { } definition)
        {
            double fps = definition.Fps;

            if (!definition.Stays)
            {
                marks.Add(new SliderMark("main", 0, (definition.Main?.Length ?? 0) / fps, SpanBrushes[0]));
            }
            else
            {
                double at = 0;

                if (definition.In is { } @in)
                {
                    marks.Add(new SliderMark("등장", 0, @in.Length / fps, SpanBrushes[0]));
                    at = @in.Length / fps;
                }

                double off = EmojiPreview.OffAt(definition, _viewport.EmojiHoldCycles) ?? at;
                marks.Add(new SliderMark($"머묾 × {_viewport.EmojiHoldCycles}", at, off, SpanBrushes[1]));

                if (definition.Out is { } @out)
                {
                    marks.Add(new SliderMark("퇴장", off, off + @out.Length / fps, SpanBrushes[2]));
                }
            }
        }

        _timeSlider.Marks = marks;
    }

    /// <summary>이모지의 그림들을 뷰포트에 싣는다 — 문서 기준 경로, 못 읽으면 자리표시.</summary>
    private void LoadEmojiImages()
    {
        _emojiImageSet = string.Join("\n", _session.Editor.Document.Emojis.SelectMany(e => e.Rigs).SelectMany(r => r.Images).Distinct(StringComparer.Ordinal));
        _viewport.ClearEmojiImages();
        _emojiBitmaps.Clear();

        foreach (string image in _session.Editor.Document.Emojis.SelectMany(e => e.Rigs).SelectMany(r => r.Images).Distinct(StringComparer.Ordinal))
        {
            Bitmap? bitmap = null;

            try
            {
                if (_session.ExistingFileOf(image) is { } path)
                {
                    bitmap = new Bitmap(path);
                }
            }
            catch (Exception exception)
            {
                _session.SetStatus($"이모지 그림을 읽지 못했다({image}): {exception.Message}");
            }

            _viewport.SetEmojiImage(image, bitmap);

            if (bitmap is not null)
            {
                _emojiBitmaps[image] = bitmap;
            }
        }

        // 목록의 작은 그림이 새 비트맵을 쓰도록 줄을 다시 짓는다.
        _emojiRigRows = null;
        RefreshEmojis();
    }

    private Control EmojiProblemRow(EmojiProblem problem) => new TextBlock
    {
        Text = (problem.Blocking ? "⚠ " : "△ ") + problem,
        FontSize = 11,
        Foreground = problem.Blocking ? Brushes.OrangeRed : Brushes.Goldenrod,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 700,
    };
}
