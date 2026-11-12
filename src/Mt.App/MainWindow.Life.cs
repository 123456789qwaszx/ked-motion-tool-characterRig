using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Settings;

namespace Mt.App;

// 수명의 띠(docs/PLAN-ux.md U5) — 뷰포트와 타임 슬라이더 사이, 이모지 모드에서만.
//   윗줄: 이 이모지가 어떻게 사는가 한 줄 · [+ 등장] [+ 머묾] [+ 퇴장](아직 없는 것만) · [게임처럼 재생] 머묾 n 바퀴
//   아랫줄: 등장 · 머묾 · 퇴장의 색 띠 — 타임 슬라이더와 같은 눈금. 끝을 끌어 길이를, 몸통을 끌어 자리를 고친다.
// 이름과 프레임을 숫자로 적는 칸(오른쪽 "구간과 잇기")은 그대로다 — 띠는 그 위의 빠른 길이다.
public partial class MainWindow
{
    private readonly EmojiLifeStripView _lifeStrip = new();
    private readonly TextBlock _lifeNote = new() { FontSize = 11, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<EmojiLifeRole, Button> _lifeAdd = new();

    /// <summary>테스트 손잡이 — 띠의 구간들(이름 · 시작 · 끝 · 자리).</summary>
    internal IReadOnlyList<LifeBand> LifeBands => _lifeStrip.Bands;

    /// <summary>테스트 손잡이 — 띠 자체(끌기는 <c>DragForTest</c>).</summary>
    internal EmojiLifeStripView LifeStrip => _lifeStrip;

    /// <summary>테스트 손잡이 — 윗줄의 글과 보이는 "+ 자리" 단추들.</summary>
    internal (string Note, IReadOnlyList<EmojiLifeRole> Addable) LifeFormState
        => (_lifeNote.Text ?? "", EmojiLife.Roles.Where(r => _lifeAdd[r].IsVisible).ToList());

    /// <summary>테스트 손잡이 — 타임 슬라이더의 자리와 그 눈금(띠와 맞는지 본다).</summary>
    internal Rect TimeSliderBounds => _timeSlider.Bounds;

    internal double TimeSliderXOf(double seconds) => _timeSlider.XAtTime(seconds);

    /// <summary>테스트 손잡이 — "+ 등장 / + 머묾 / + 퇴장".</summary>
    internal void AddLifeSpanFromForm(EmojiLifeRole role) => AddLifeSpan(role);

    /// <summary>테스트 손잡이 — 띠의 메뉴: 잇기 풀기 / 구간 지우기.</summary>
    internal void UnlinkLifeSpanFromForm(string span) => UnlinkLifeSpan(span);

    internal void RemoveLifeSpanFromForm(string span) => RemoveLifeSpan(span);

    /// <summary>뷰포트 아래에 놓는 두 줄. <paramref name="transportButtons"/>의 너비만큼 오른쪽을 비워 타임 슬라이더와 눈금을 맞춘다.</summary>
    private Control BuildLifeRows(Control transportButtons)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        foreach (EmojiLifeRole role in EmojiLife.Roles)
        {
            EmojiLifeRole captured = role;
            Button add = SmallButton($"+ {EmojiLife.Label(role)}", (_, _) => AddLifeSpan(captured));
            add.Foreground = EmojiLifeStripView.BrushOf(role);
            ToolTip.SetTip(add, role switch
            {
                EmojiLifeRole.In => "등장 — 뜰 때 한 번 재생하는 구간을 놓는다(머묾이 있을 때 쓰인다)",
                EmojiLifeRole.Hold => "머묾 — 떠 있는 동안 되풀이하는 구간을 놓는다. 이것을 두면 머무는 이모지가 된다(emoji_off로 내린다)",
                _ => "퇴장 — 내릴 때 한 번 재생하는 구간을 놓는다(머묾이 있을 때 쓰인다)",
            });
            _lifeAdd[role] = add;
            buttons.Children.Add(add);
        }

        buttons.Children.Add(_emojiLifeToggle);
        buttons.Children.Add(Small("머묾"));
        buttons.Children.Add(_emojiHoldCycles);
        buttons.Children.Add(Small("바퀴"));

        var head = new DockPanel { Margin = new Thickness(6, 3, 4, 2) };
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(_lifeNote);

        // 타임 슬라이더는 트랜스포트 단추들의 왼쪽을 채운다 — 띠도 같은 너비여야 눈금이 맞는다.
        var spacer = new Border();
        transportButtons.GetObservable(BoundsProperty).Subscribe(new BoundsWatcher(b => spacer.Width = b.Width + transportButtons.Margin.Left + transportButtons.Margin.Right));
        var body = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        DockPanel.SetDock(spacer, Dock.Right);
        body.Children.Add(spacer);
        body.Children.Add(_lifeStrip);

        ToolTip.SetTip(_lifeStrip, "수명의 띠 — 끝을 끌면 길이가, 몸통을 끌면 자리가 바뀐다(프레임에 붙는다 · 놓을 때 되돌리기 한 칸). 오른쪽 단추: 잇기 풀기 · 구간 지우기. 점선은 어디에도 잇지 않은 구간");
        _lifeStrip.SpanCommitted += (span, start, end) =>
        {
            if (CurrentEmoji is { } emoji)
            {
                Guard("구간", () => _session.Editor.SetEmojiSpan(emoji.Name, span, start, end));
            }
        };
        _lifeStrip.BandPicked += span =>
        {
            // 오른쪽의 "구간" 목록도 같은 구간을 가리킨다 — 숫자로 고치려면 거기서.
            if (CurrentEmoji?.Spans.FindIndex(s => s.Name == span) is int index and >= 0)
            {
                _emojiSpanList.SelectedIndex = index;
            }
        };
        _lifeStrip.BandMenuRequested += span =>
        {
            var menu = new ContextMenu();

            if (CurrentEmoji is { } emoji && EmojiLife.RoleOf(emoji, span) is { } role)
            {
                var unlink = new MenuItem { Header = $"{EmojiLife.Label(role)}에서 풀기 (구간은 남는다)" };
                unlink.Click += (_, _) => UnlinkLifeSpan(span);
                menu.Items.Add(unlink);
            }

            var remove = new MenuItem { Header = $"구간 '{span}' 지우기" };
            remove.Click += (_, _) => RemoveLifeSpan(span);
            menu.Items.Add(remove);
            menu.Open(_lifeStrip);
        };

        _viewport.TimeChanged += (_, _) => SyncLifeStripTime();
        _viewport.RangeChanged += (_, _) => SyncLifeStripTime();

        var rows = new StackPanel { Children = { head, body } };
        return OnlyIn(WorkMode.Emoji, rows);
    }

    private void AddLifeSpan(EmojiLifeRole role)
    {
        if (CurrentEmoji is { } emoji)
        {
            Guard(EmojiLife.Label(role), () => _lifeStrip.Picked = _session.Editor.AddEmojiLifeSpan(emoji.Name, role).Name);
        }
    }

    private void UnlinkLifeSpan(string span)
    {
        if (CurrentEmoji is { } emoji)
        {
            string? Keep(string? link) => link == span ? null : link;
            Guard("잇기", () => _session.Editor.SetEmojiLinks(emoji.Name, Keep(emoji.In), Keep(emoji.Hold), Keep(emoji.Out)));
        }
    }

    private void RemoveLifeSpan(string span)
    {
        if (CurrentEmoji is { } emoji)
        {
            Guard("구간", () => _session.Editor.RemoveEmojiSpan(emoji.Name, span));
        }
    }

    /// <summary>띠를 지금 이모지로 다시 채운다 — 이모지의 다른 화면과 같은 때(문서가 바뀌었거나 다른 이모지를 골랐다).</summary>
    private void RefreshLifeStrip()
    {
        EmojiAsset? emoji = CurrentEmoji;
        _lifeStrip.SetBands(
            emoji?.Spans.Select(s => new LifeBand(s.Name, s.Start, s.End, EmojiLife.RoleOf(emoji, s.Name))).ToList() ?? new List<LifeBand>(),
            emoji?.LastFrame ?? 0);
        _lifeNote.Text = emoji is null ? "" : EmojiLife.Describe(emoji);

        foreach (EmojiLifeRole role in EmojiLife.Roles)
        {
            _lifeAdd[role].IsVisible = emoji is not null && EmojiLife.LinkOf(emoji, role) is null;
        }

        SyncLifeStripTime();
    }

    /// <summary>
    /// 띠의 눈금과 지금 시각 — 타임라인 보기면 타임 슬라이더와 같은 범위, 수명 보기("게임처럼 재생")면 타임라인 전체
    /// (그때 슬라이더는 수명의 시간을 보인다 — 띠는 타임라인의 어디가 재생되는지를 보인다).
    /// </summary>
    private void SyncLifeStripTime()
    {
        if (CurrentEmoji is not { } emoji || _viewport.EmojiName != emoji.Name)
        {
            return;
        }

        if (_viewport.EmojiLife)
        {
            _lifeStrip.SetRange(0, Math.Max(EmojiAsset.SecondsOf(1), emoji.Timeline.Duration));
            _lifeStrip.CurrentTime = _viewport.EmojiTimelineTime ?? 0;
        }
        else
        {
            _lifeStrip.SetRange(_viewport.Clock.RangeStart, _viewport.Clock.RangeEnd);
            _lifeStrip.CurrentTime = _viewport.Time;
        }
    }

    private sealed class BoundsWatcher : IObserver<Rect>
    {
        private readonly Action<Rect> _onNext;

        public BoundsWatcher(Action<Rect> onNext) => _onNext = onNext;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(Rect value) => _onNext(value);
    }
}
