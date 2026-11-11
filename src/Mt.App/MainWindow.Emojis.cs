using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Mt.Authoring.Validation;

namespace Mt.App;

// 이모지의 셸(docs/PLAN-emoji.md E6) — 왼쪽 "이모지" 목록 + 오른쪽 "이모지" 탭.
// 목록에서 고르면 그 이모지가 시간의 주인이 된다: 그래프 · 도프 시트 · 타임 슬라이더가 그 타임라인(emoji:이름)을 묶고,
// 뷰포트는 그 이모지를 캐릭터의 발(로케이터 자리)에 세워 보인다. 모션 · 클립 선택은 풀린다.
// 값은 탭의 칸에서 고친다(각각 되돌리기 한 칸). 키는 그래프 · 도프 시트와 탭의 "지금 키"로 잡는다.
public partial class MainWindow
{
    private const string NoneChoice = "(없음)";
    private const string WholeEmojiChoice = "이모지 전체";

    private static readonly ChannelKind[] EmojiKinds = { ChannelKind.Position, ChannelKind.Scale, ChannelKind.Euler, ChannelKind.Alpha, ChannelKind.Image };
    private static readonly IBrush[] SpanBrushes =
    {
        new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6B)),
        new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x30)),
        new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xE8)),
        new SolidColorBrush(Color.FromRgb(0xC0, 0x6C, 0xE0)),
    };

    private readonly ListBox _emojiList = new() { MinHeight = 50, MaxHeight = 140, FontSize = 11 };
    private readonly StackPanel _emojiFields = new() { Spacing = 4 };
    private readonly TextBlock _emojiTitle = new() { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _emojiName = new() { FontSize = 11 };
    private readonly TextBox _emojiOffsetX = new() { FontSize = 11 };
    private readonly TextBox _emojiOffsetY = new() { FontSize = 11 };
    private readonly TextBox _emojiFrames = new() { FontSize = 11 };

    private readonly ListBox _emojiRigList = new() { MinHeight = 40, MaxHeight = 110, FontSize = 11 };
    private readonly StackPanel _emojiRigFields = new() { Spacing = 4 };
    private readonly TextBox _emojiRigName = new() { FontSize = 11 };
    private readonly TextBox _emojiRigImages = new() { FontSize = 11, AcceptsReturn = true, MinHeight = 38, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBox _emojiRigX = new() { FontSize = 11 };
    private readonly TextBox _emojiRigY = new() { FontSize = 11 };
    private readonly TextBox _emojiRigScale = new() { FontSize = 11 };
    private readonly TextBox _emojiRigRotation = new() { FontSize = 11 };
    private readonly TextBox _flipStart = new() { FontSize = 11, Text = "0", Width = 34 };
    private readonly TextBox _flipEvery = new() { FontSize = 11, Text = "3", Width = 34 };
    private readonly TextBox _flipCount = new() { FontSize = 11, Text = "4", Width = 34 };

    private readonly ComboBox _emojiKeyTarget = new() { FontSize = 11, MinWidth = 90 };
    private readonly ComboBox _emojiKeyKind = new() { FontSize = 11, MinWidth = 70 };

    private readonly ListBox _emojiSpanList = new() { MinHeight = 40, MaxHeight = 110, FontSize = 11 };
    private readonly StackPanel _emojiSpanFields = new() { Spacing = 4 };
    private readonly TextBox _emojiSpanName = new() { FontSize = 11 };
    private readonly TextBox _emojiSpanStart = new() { FontSize = 11 };
    private readonly TextBox _emojiSpanEnd = new() { FontSize = 11 };
    private readonly ComboBox _emojiIn = new() { FontSize = 11 };
    private readonly ComboBox _emojiHold = new() { FontSize = 11 };
    private readonly ComboBox _emojiOut = new() { FontSize = 11 };
    private readonly TextBlock _emojiKindNote = new() { FontSize = 10, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };

    private readonly ToggleButton _emojiLifeToggle = new() { Content = "수명 재생", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
    private readonly TextBox _emojiHoldCycles = new() { FontSize = 11, Text = "2", Width = 34 };

    internal string? SelectedEmoji => _emojiList.SelectedItem as string;

    private EmojiAsset? CurrentEmoji => SelectedEmoji is { } name ? _session.Editor.Document.FindEmoji(name) : null;

    private string? SelectedEmojiRig => _emojiRigList.SelectedItem as string;

    private string? SelectedEmojiSpan => _emojiSpanList.SelectedItem is string row ? row.Split(' ')[0] : null;

    // ── 테스트 손잡이 ─────────────────────────────────────────────────

    /// <summary>테스트 손잡이 — 이모지 목록에서 고른다(null이면 푼다).</summary>
    internal void SelectEmoji(string? name) => _emojiList.SelectedItem = name;

    /// <summary>테스트 손잡이 — "이모지 추가"(이름 emoji, emoji_2 … · 리그 body 하나).</summary>
    internal string AddEmojiFromForm() => AddEmoji();

    /// <summary>테스트 손잡이 — 리그를 고른다.</summary>
    internal void SelectEmojiRigFromForm(string? rig) => _emojiRigList.SelectedItem = rig;

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

    internal void KeyEmojiNowFromForm(string? rig, ChannelKind kind)
    {
        _emojiKeyTarget.SelectedItem = rig ?? WholeEmojiChoice;
        _emojiKeyKind.SelectedIndex = Array.IndexOf(EmojiKinds, kind);
        Guard("지금 키", KeyEmojiNow);
    }

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

        Button add = SmallButton("이모지 추가", (_, _) => Guard("이모지 추가", () => AddEmoji()));
        ToolTip.SetTip(add, "이름 emoji, emoji_2…로 만든다 — 리그 body 하나, 타임라인 24프레임. 오른쪽 '이모지' 탭에서 그림 · 구간을 준다");
        Button duplicate = SmallButton("복제", (_, _) => Guard("이모지 복제", DuplicateEmoji));
        ToolTip.SetTip(duplicate, "고른 이모지를 키까지 통째로 복제한다 — 재사용은 가져와서 고치는 것이다");
        Button export = SmallButton("emojis.json…", OnExportEmojisClick);
        ToolTip.SetTip(export, "emojis.json(emojis/2) 내보내기 — 구간마다 따로 선 클립. 막는 문제가 있거나 런타임 로더가 다시 읽지 못하면 쓰지 않는다");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { add, SmallButton("삭제", (_, _) => Guard("이모지 삭제", RemoveEmoji)), duplicate, export },
        });
    }

    private Control BuildEmojiTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(_emojiTitle);

        _emojiFields.Children.Add(Labeled("이름", _emojiName));
        _emojiFields.Children.Add(Labeled("오프셋 X", _emojiOffsetX));
        _emojiFields.Children.Add(Labeled("오프셋 Y", _emojiOffsetY));
        _emojiFields.Children.Add(Labeled("길이(프레임)", _emojiFrames));
        ToolTip.SetTip(_emojiName, "대본이 부르는 이름(<<emoji 누구 이름>>) — 영문 소문자 · 숫자 · _. 한 번 내보낸 이름은 바꾸지 않는다(대본이 가리킨다)");
        ToolTip.SetTip(_emojiOffsetX, "이모지별 오프셋(Emoji_Offset) — 로케이터(캐릭터 + 바리에이션의 자리) 기준 px. 이모지 전체를 움직이는 키는 이 값에 더해진다");
        ToolTip.SetTip(_emojiFrames, "타임라인의 마지막 프레임(24fps) — 프레임 0 … 이것까지. 키는 옮기지 않는다");

        // 리그
        _emojiFields.Children.Add(Header("리그 — 그림 하나에 리그 하나"));
        _emojiRigList.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _viewport.SelectedEmojiRig = SelectedEmojiRig;
                RefreshEmojiInspector();
            }
        };
        ToolTip.SetTip(_emojiRigList, "리그 — 순서가 그려지는 순서다(아래가 위). 뷰포트에서 그림을 눌러도 고른다");
        _emojiFields.Children.Add(_emojiRigList);
        _emojiFields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                SmallButton("리그 추가", (_, _) => AddEmojiRigFromForm()),
                SmallButton("삭제", (_, _) => Guard("리그 삭제", RemoveEmojiRig)),
                SmallButton("▲", (_, _) => Guard("리그 순서", () => MoveEmojiRig(-1))),
                SmallButton("▼", (_, _) => Guard("리그 순서", () => MoveEmojiRig(+1))),
            },
        });

        _emojiRigFields.Children.Add(Labeled("리그 이름", _emojiRigName));
        _emojiRigFields.Children.Add(Labeled("그림", _emojiRigImages));
        Button pick = SmallButton("그림 더하기…", OnPickEmojiImageClick);
        ToolTip.SetTip(pick, "PNG를 골라 그림 목록 끝에 더한다 — 문서 기준 경로로 적힌다. 스프라이트 키는 파일 이름이다");
        _emojiRigFields.Children.Add(pick);
        _emojiRigFields.Children.Add(Labeled("자리 X", _emojiRigX));
        _emojiRigFields.Children.Add(Labeled("자리 Y", _emojiRigY));
        _emojiRigFields.Children.Add(Labeled("배율", _emojiRigScale));
        _emojiRigFields.Children.Add(Labeled("회전", _emojiRigRotation));
        ToolTip.SetTip(_emojiRigName, "이모지 안에서 리그를 가리키는 이름 — 키가 이 이름으로 리그를 찾는다. 바꾸면 그 리그의 채널이 따라온다");
        ToolTip.SetTip(_emojiRigImages, "그림 경로 — 한 줄에 하나. 첫 장이 기본 스프라이트, 나머지는 그림 바꾸기(image) 채널이 몇 번째인가로 고른다");
        ToolTip.SetTip(_emojiRigX, "기본 자리(EmojiRig_Place) — 키가 아니다. 뷰포트에서 그림을 끌어도 된다");
        ToolTip.SetTip(_emojiRigScale, "기본 배율 — 그림을 이 이모지에서 얼마만 하게 쓰는가. 키의 배율은 이 위에서 1을 기준으로 잡는다");

        var flips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        flips.Children.Add(Small("넘기기"));
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
        _emojiFields.Children.Add(_emojiRigFields);

        // 키
        _emojiFields.Children.Add(Header("키 — 지금 시각에"));
        _emojiKeyKind.ItemsSource = EmojiKinds.Select(ChannelKindLabel.Of).ToList();
        _emojiKeyKind.SelectedIndex = 0;
        Button keyNow = SmallButton("지금 키", (_, _) => Guard("지금 키", KeyEmojiNow));
        ToolTip.SetTip(keyNow, "고른 대상(이모지 전체 · 리그)의 그 채널을 지금 시각의 값으로 키한다 — 채널이 없으면 생긴다. 그다음은 그래프 · 도프 시트에서 고친다. 회전은 z 하나, 그림은 리그에만");
        Button step = SmallButton("계단", (_, _) => _graph.SetTangentMode(TangentMode.Step));
        ToolTip.SetTip(step, "그래프에서 고른 키를 계단으로 — 다음 키까지 그 값으로 멈춘다(emojis/2의 step). 그림 채널의 키는 언제나 계단이다");
        _emojiFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _emojiKeyTarget, _emojiKeyKind, keyNow, step } });

        // 구간과 잇기
        _emojiFields.Children.Add(Header("구간과 잇기"));
        _emojiSpanList.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                RefreshEmojiInspector();
            }
        };
        ToolTip.SetTip(_emojiSpanList, "이름 붙은 구간(프레임, 끝 포함) — 겹쳐도 된다. 내보낼 때 구간마다 따로 선 클립이 된다(구간의 첫 프레임이 클립의 프레임 0)");
        _emojiFields.Children.Add(_emojiSpanList);
        Button addSpan = SmallButton("구간 추가", (_, _) => AddEmojiSpanFromForm());
        ToolTip.SetTip(addSpan, "지금 프레임부터 6프레임(타임라인 안으로)의 구간을 더한다 — 이름 span, span_2…");
        _emojiFields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { addSpan, SmallButton("삭제", (_, _) => Guard("구간 삭제", RemoveEmojiSpan)) },
        });
        _emojiSpanFields.Children.Add(Labeled("구간 이름", _emojiSpanName));
        _emojiSpanFields.Children.Add(Labeled("시작(fr)", _emojiSpanStart));
        _emojiSpanFields.Children.Add(Labeled("끝(fr)", _emojiSpanEnd));
        _emojiFields.Children.Add(_emojiSpanFields);
        _emojiFields.Children.Add(Labeled("등장", _emojiIn));
        _emojiFields.Children.Add(Labeled("머묾(되풀이)", _emojiHold));
        _emojiFields.Children.Add(Labeled("퇴장", _emojiOut));
        _emojiFields.Children.Add(_emojiKindNote);
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

        // 보기
        _emojiFields.Children.Add(Header("보기"));
        _emojiLifeToggle.IsCheckedChanged += (_, _) =>
        {
            _viewport.EmojiLife = _emojiLifeToggle.IsChecked == true;
            RefreshEmojiMarks();
        };
        ToolTip.SetTip(_emojiLifeToggle, "켜면 뷰포트 · 타임 슬라이더가 수명을 재생한다 — 등장 → 머묾 × 바퀴 → 퇴장(머묾이 없으면 main 한 번). 끄면 타임라인(그래프와 같은 시각)");
        ToolTip.SetTip(_emojiHoldCycles, "수명 재생에서 머묾을 몇 바퀴 돈 뒤 내리는가");
        OnCommit(_emojiHoldCycles, text =>
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycles))
            {
                _viewport.EmojiHoldCycles = cycles;
                RefreshEmojiMarks();
            }
        });
        Button frame = SmallButton("이모지에 맞춤", (_, _) => _viewport.FrameEmoji());
        ToolTip.SetTip(frame, "리그 그림들이 뷰포트 가운데를 채우게 보기를 옮긴다(F와 같다 — 보기만). 로케이터는 배역의 발치라 그대로는 무대 아래 작게 보인다");
        _emojiFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _emojiLifeToggle, Small("머묾"), _emojiHoldCycles, Small("바퀴"), frame } });

        panel.Children.Add(_emojiFields);

        foreach (TextBox box in new[] { _emojiName, _emojiOffsetX, _emojiOffsetY, _emojiFrames, _emojiRigName, _emojiRigImages, _emojiRigX, _emojiRigY, _emojiRigScale, _emojiRigRotation, _emojiSpanName, _emojiSpanStart, _emojiSpanEnd })
        {
            TextBox captured = box;
            OnCommit(captured, _ => CommitEmojiBox(captured));
        }

        _viewport.EmojiRigPicked += (_, _) =>
        {
            if (_viewport.SelectedEmojiRig is { } rig)
            {
                _emojiRigList.SelectedItem = rig;
            }
        };

        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

    private void RemoveEmojiRig()
    {
        if (CurrentEmoji is { } emoji && SelectedEmojiRig is { } rig)
        {
            _session.Editor.RemoveEmojiRig(emoji.Name, rig);
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

    /// <summary>"지금 키" — 고른 대상 · 채널의 지금 값(그 프레임의 미리보기 값, 그림은 지금 보이는 장)을 지금 시각에 키한다. 한 칸.</summary>
    private void KeyEmojiNow()
    {
        if (CurrentEmoji is not { } emoji || _emojiKeyKind.SelectedIndex < 0)
        {
            return;
        }

        string target = _emojiKeyTarget.SelectedItem as string ?? WholeEmojiChoice;
        KeyEmojiAt(emoji, target == WholeEmojiChoice ? "" : target, EmojiKinds[_emojiKeyKind.SelectedIndex]);
    }

    /// <summary>지금 시각(프레임에 붙여)에 그 대상(""= 이모지 전체)의 지금 값을 키로 — "지금 키"와 그래프 목록의 "키 없음 ＋"가 같이 쓴다.</summary>
    private void KeyEmojiAt(EmojiAsset emoji, string rig, ChannelKind kind)
    {
        if (_session.EmojiFrame is not { } frame)
        {
            return;
        }

        if (kind == ChannelKind.Image && rig.Length == 0)
        {
            throw new InvalidOperationException("그림 바꾸기는 리그에만 쓴다 — 대상에서 리그를 고른다.");
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
            throw new InvalidOperationException("리그를 먼저 고른다.");
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
                _session.SetStatus("리그를 먼저 고른다.");
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
            _emojiTitle.Text = emoji is null
                ? "왼쪽 '이모지'에서 고르거나 더한다."
                : $"{emoji.Name} — 리그 {emoji.Rigs.Count} · {emoji.LastFrame + 1}프레임 · {(emoji.Stays ? "머무는 이모지(in · hold · out)" : "한 번 재생하고 사라지는 이모지(main)")}";

            string? rigBefore = SelectedEmojiRig ?? _viewport.SelectedEmojiRig;
            string? spanBefore = selectSpan ?? SelectedEmojiSpan;
            List<string> rigs = emoji?.Rigs.Select(r => r.Name).ToList() ?? new List<string>();
            _emojiRigList.ItemsSource = rigs;
            _emojiRigList.SelectedIndex = rigBefore is not null && rigs.Contains(rigBefore) ? rigs.IndexOf(rigBefore) : rigs.Count > 0 ? 0 : -1;
            _viewport.SelectedEmojiRig = SelectedEmojiRig;

            List<string> spans = emoji?.Spans.Select(s => $"{s.Name} {s.Start}~{s.End}").ToList() ?? new List<string>();
            _emojiSpanList.ItemsSource = spans;
            int spanIndex = emoji?.Spans.FindIndex(s => s.Name == spanBefore) ?? -1;
            _emojiSpanList.SelectedIndex = spanIndex >= 0 ? spanIndex : spans.Count > 0 ? 0 : -1;

            EmojiRigAsset? rig = emoji?.FindRig(SelectedEmojiRig);
            EmojiSpan? span = emoji?.FindSpan(SelectedEmojiSpan);
            _emojiRigFields.IsEnabled = rig is not null;
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
            Write(_emojiSpanName, span?.Name ?? "");
            Write(_emojiSpanStart, span?.Start.ToString(CultureInfo.InvariantCulture) ?? "");
            Write(_emojiSpanEnd, span?.End.ToString(CultureInfo.InvariantCulture) ?? "");

            var targets = new List<string> { WholeEmojiChoice };
            targets.AddRange(rigs);
            string? targetBefore = _emojiKeyTarget.SelectedItem as string;
            _emojiKeyTarget.ItemsSource = targets;
            _emojiKeyTarget.SelectedItem = targetBefore is not null && targets.Contains(targetBefore) ? targetBefore : WholeEmojiChoice;

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
                marks.Add(new SliderMark(span.Name + role, EmojiAsset.SecondsOf(span.Start), EmojiAsset.SecondsOf(span.End), SpanBrushes[i % SpanBrushes.Length]));
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
        _viewport.ClearEmojiImages();

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
        }
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
