using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Settings;
using Mt.Authoring.Preview;
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
    private readonly StackPanel _clipBlock = new() { Spacing = 3 };
    private readonly ClipTimelineView _timeline = new();

    /// <summary>가운데 아래 탭 — 그래프 · 도프 시트 · 타임라인.</summary>
    private readonly TabControl _editorTabs = new() { Padding = new Avalonia.Thickness(0) };

    /// <summary>
    /// 클립 안에서 블록의 모션을 고치는 중일 때 그 블록의 시작 시각 — 그래프·도프 시트의 시각(모션 기준, 0부터)과 뷰포트 시계(클립 시각)의 차.
    /// 그 밖에는 0.
    /// </summary>
    private double _blockOffset;

    /// <summary>테스트 손잡이 — 지금 그래프가 클립의 블록 문맥에서 모션을 고치는 중인가, 그 시각 차.</summary>
    internal (bool Active, double Offset) BlockContext => (SelectedClip is not null && _graph.MotionName is not null, _blockOffset);

    internal ClipTimelineView Timeline => _timeline;

    /// <summary>테스트 손잡이 — 가운데 아래 탭 번호.</summary>
    internal int EditorTab => _editorTabs.SelectedIndex;

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

    private readonly Grid _editorsGrid = new() { RowDefinitions = new RowDefinitions("0,Auto,*") };
    private readonly GridSplitter _timelineSplitter = new() { ResizeDirection = GridResizeDirection.Rows, Height = 5, Background = Avalonia.Media.Brushes.Transparent, IsVisible = false };
    private readonly ScrollViewer _timelineScroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, IsVisible = false };

    /// <summary>테스트 손잡이 — 타임라인 줄(그래프 위)의 높이. 끌어서 바꾼 것처럼 넣을 수도 있다.</summary>
    internal double TimelineRowHeight
    {
        get => _editorsGrid.RowDefinitions[0].Height.Value;
        set
        {
            _editorsGrid.RowDefinitions[0].Height = new GridLength(value, GridUnitType.Pixel);
            RememberTimelineHeight();
        }
    }

    /// <summary>스플리터를 놓았다 — 그 높이를 설정에 남긴다(다음부터 트랙 수 대신 이 높이).</summary>
    private void RememberTimelineHeight()
    {
        if (_timelineScroll.IsVisible)
        {
            _settings.TimelineHeight = Math.Max(MinTimelineHeight, _editorsGrid.RowDefinitions[0].Height.Value);
        }
    }

    private const double MinTimelineHeight = ClipTimelineView.RulerHeight + ClipTimelineView.RowHeight + 4;

    private Control BuildTimeline()
    {
        _timelineScroll.Content = _timeline;
        _timelineSplitter.DragCompleted += (_, _) => RememberTimelineHeight();
        ToolTip.SetTip(_timelineSplitter, "끌어서 타임라인과 그래프 사이를 조절한다 — 높이는 설정에 남는다");
        _timeline.Viewport = _viewport;
        _timeline.SelectionChanged += (_, _) => SyncBlockContext();
        _timeline.BlockOpened += (_, at) => Guard("모션 편집", () => OpenClipBlock(at.Track, at.Index));
        _timeline.BakeRequested += (_, track) => Guard("굽기", () => BakeClipActor(track));
        _timeline.SelectionChanged += (_, _) => RefreshClipBlockInspector();
        return _timelineScroll;
    }

    /// <summary>
    /// 클립 안에서 고치기(<c>PLAN-clip.md</c> K4b 1단계) — 타임라인에서 고른 모션 블록의 모션을 그래프·도프 시트에 묶는다. 클립은 고른 채로 남고
    /// 뷰포트는 계속 클립 전체를 보이므로, 커브를 고치면 다른 출연자와 함께 바로 보인다. 그래프의 시각은 모션 기준(0부터)이고
    /// 뷰포트 시계와는 블록의 시작 시각만큼 어긋난다(<see cref="_blockOffset"/>). 블록을 안 골랐거나 상태 블록이면 그래프는 빈다.
    /// 문서가 바뀔 때마다 다시 맞춘다 — 블록을 옮기면 시각 차가, 모션을 바꾸면 묶인 모션이 따라간다.
    /// </summary>
    private void SyncBlockContext()
    {
        if (SelectedClip is null || _viewport.ClipName is null)
        {
            return; // 모션 층 — 그래프는 고른 모션의 것이다(BindMotion)
        }

        ClipBlock? block = CurrentClip is { } clip && _timeline.SelectedBlock is { } at
            && at.Track < clip.Tracks.Count && at.Index < clip.Tracks[at.Track].Blocks.Count
                ? clip.Tracks[at.Track].Blocks[at.Index]
                : null;
        string? motion = block is { IsMotion: true } && _session.Editor.Document.Find(block.Motion) is not null ? block.Motion : null;
        // 뷰포트도 같은 블록을 본다 — 끌기·채널 박스·S가 그 블록의 모션에 키를 놓는다(2단계).
        _viewport.ClipBlockContext = motion is null ? null : _timeline.SelectedBlock;
        _blockOffset = motion is null ? 0 : _viewport.ClipBlockTimeOffset; // 블록의 시작 + 지나온 바퀴(반복하는 블록)

        if (_graph.MotionName != motion)
        {
            _graph.Bind(_session.Editor, motion);
            _dope.Controllers = _session.ControllersOf(motion is null ? null : _session.Editor.Document.Find(motion)?.Rig);
            _dope.Bind(_session.Editor, motion);
            RefreshChannels();
            RefreshMotionInspector();
            RefreshKeyInspector();
        }

        RefreshCameraPanel(); // 카메라 모션 블록이면 '모션' 탭의 카메라 값 칸이 그 블록을 본다
        _timeSlider.InvalidateVisual(); // 고치는 블록의 모션의 키 틱

        // 시계는 하나다 — 그래프의 현재 시각을 클립 시각에서 블록 시작을 뺀 자리로.
        SyncTime(() =>
        {
            _graph.CurrentTime = _viewport.Time - _blockOffset;
            _dope.CurrentTime = _viewport.Time - _blockOffset;
        });
    }

    /// <summary>
    /// 블록의 모션을 연다 — 그 모션을 고르고(클립 선택은 풀린다), 배역을 맡은 오브젝트를 고르고, 클립에서 보던 시각을 모션의 시각으로 옮긴다.
    /// 그래프 탭으로 간다. 클립으로 돌아오려면 왼쪽 목록에서 클립을 다시 고른다.
    /// </summary>
    private void OpenClipBlock(int track, int index)
    {
        if (CurrentClip is not { } clip || track < 0 || track >= clip.Tracks.Count || index < 0 || index >= clip.Tracks[track].Blocks.Count)
        {
            return;
        }

        ClipBlock block = clip.Tracks[track].Blocks[index];

        if (_session.Editor.Document.Find(block.Motion) is not { } motion)
        {
            _session.SetStatus($"모션 '{block.Motion}'이 없다 — 블록을 지우거나 다른 모션을 놓는다.");
            return;
        }

        double local = Math.Clamp(_viewport.Time - block.At, 0, motion.Duration);
        string? actor = clip.FindRole(clip.Tracks[track].Role)?.Object;
        _motionList.SelectedItem = motion.Name;

        if (actor is not null && _viewport.Scene?.Find(actor) is not null)
        {
            _viewport.SelectedObjectId = actor;
        }

        _viewport.ScrubTo(local);
    }

    /// <summary>테스트 손잡이 — 타임라인 메뉴의 "새 모션으로 굽기".</summary>
    internal string? BakeClipActorFromMenu(int track) => BakeClipActor(track);

    /// <summary>
    /// 그 트랙의 배역을 맡은 캐릭터에게 얹힌 블록 전부(같은 캐릭터를 맡은 다른 배역 것까지, 끈 배역은 빼고)를 새 모션 하나로 굽는다.
    /// 이름은 <c>클립_오브젝트</c>(겹치면 _2…). 클립과 블록은 그대로 둔다 — 구운 모션은 모션 목록에 생긴다. 되돌리기 한 번.
    /// </summary>
    private string? BakeClipActor(int track)
    {
        if (CurrentClip is not { } clip || track < 0 || track >= clip.Tracks.Count)
        {
            return null;
        }

        string role = clip.Tracks[track].Role;

        if (clip.FindRole(role)?.Object is not { } actor)
        {
            throw new InvalidOperationException($"배역 '{role}'을 맡은 오브젝트가 없다 — '클립' 탭에서 묶는다.");
        }

        RectNodeTree template = _viewport.TemplateTree ?? throw new InvalidOperationException("리그가 없다.");
        string name = MotionNames.Unique(_session.Editor.Document, MotionNames.Sanitize($"{clip.Name}_{actor}"));
        MotionAsset baked = ClipBake.Bake(_session.Editor, template, clip.Name, actor, name, _viewport.IsClipTrackMuted);
        int keys = baked.Channels.Sum(c => (c.X?.Keys.Count ?? 0) + (c.Y?.Keys.Count ?? 0) + (c.Z?.Keys.Count ?? 0));
        _session.SetStatus($"구웠다: 모션 '{name}' — {actor}의 블록을 합친 {baked.Duration:0.###}초, 키 {keys}개, 상대값·{(baked.End == MotionEnd.Commit ? "유지(commit)" : "복원(restore)")}. 왼쪽 모션 목록에 있다.");
        return name;
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
        panel.Children.Add(Header("블록"));
        panel.Children.Add(_clipBlock);

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

    /// <summary>타임라인은 클립을 고른 동안만 보이고, 높이는 트랙 수를 따른다(여덟 줄까지).</summary>
    private void RefreshTimelineLayout()
    {
        ClipAsset? clip = CurrentClip;
        bool show = clip is not null;
        _timeline.IsVisible = show;
        _timelineScroll.IsVisible = show;
        _timelineSplitter.IsVisible = show;
        RowDefinition row = _editorsGrid.RowDefinitions[0];

        if (!show)
        {
            row.MinHeight = 0;
            row.Height = new GridLength(0, GridUnitType.Pixel);
            return;
        }

        // 타임라인 자체는 트랙을 다 담는 높이다 — 줄이 그보다 낮으면 스크롤 뷰어가 굴린다.
        int tracks = Math.Max(2, clip!.Tracks.Count);
        double natural = ClipTimelineView.RulerHeight + tracks * ClipTimelineView.RowHeight + 4;
        _timeline.Height = natural;
        row.MinHeight = MinTimelineHeight;

        // 끌어서 정한 높이가 있으면 그것, 없으면 트랙 수에 맞춘다(여덟 줄까지).
        double wanted = _settings.TimelineHeight > 0
            ? _settings.TimelineHeight
            : Math.Min(natural, ClipTimelineView.RulerHeight + 8 * ClipTimelineView.RowHeight + 4);

        if (Math.Abs(row.Height.Value - wanted) > 0.5)
        {
            row.Height = new GridLength(wanted, GridUnitType.Pixel);
        }
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

        if (SelectedClip is not null)
        {
            DeselectEmoji(); // 시간의 주인은 하나
            EnterWorkMode(WorkMode.Motion);
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

        RefreshTimelineLayout();
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

        RefreshTimelineLayout();
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

        RefreshTimelineLayout();
        SyncBlockContext();

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
            RefreshClipBlockInspector();
        }
    }

    // ── 블록 인스펙터 — 타임라인에서 고른 블록의 값 ─────────────────────

    /// <summary>테스트 손잡이 — 블록 인스펙터의 숫자 칸(라벨로 찾는다). 없으면 null.</summary>
    internal TextBox? ClipBlockBox(string label)
        => _clipBlock.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<TextBox>()).FirstOrDefault(b => Equals(b.Tag, label));

    /// <summary>테스트 손잡이 — 블록 인스펙터의 줄 수.</summary>
    internal int ClipBlockRows => _clipBlock.Children.Count;

    /// <summary>
    /// 고른 블록을 고친다 — 인스펙터의 모든 칸이 지나는 한 길(되돌리기 한 칸). 시작 시각이 바뀌어 번호가 달라져도 선택이 따라간다.
    /// </summary>
    internal void UpdateSelectedClipBlock(Action<ClipBlock> change)
    {
        if (CurrentClip is not { } clip || _timeline.SelectedBlock is not { } at
            || at.Track >= clip.Tracks.Count || at.Index >= clip.Tracks[at.Track].Blocks.Count)
        {
            return;
        }

        ClipBlock next = clip.Tracks[at.Track].Blocks[at.Index].Clone();
        change(next);
        int index = _session.Editor.UpdateClipBlock(clip.Name, at.Track, at.Index, next);
        _timeline.Select((at.Track, index));
        RefreshClipBlockInspector();
    }

    private void RefreshClipBlockInspector()
    {
        _refreshDepth++;

        try
        {
            _clipBlock.Children.Clear();

            if (CurrentClip is not { } clip || _timeline.SelectedBlock is not { } at
                || at.Track >= clip.Tracks.Count || at.Index >= clip.Tracks[at.Track].Blocks.Count)
            {
                _clipBlock.Children.Add(new TextBlock { Text = "(타임라인에서 블록을 고른다)", FontSize = 11, Opacity = 0.6 });
                return;
            }

            ClipBlock block = clip.Tracks[at.Track].Blocks[at.Index];
            int uses = block.IsMotion ? ClipTiming.UsesOf(_session.Editor.Document, block.Motion) : 0;
            _clipBlock.Children.Add(new TextBlock
            {
                Text = !block.IsMotion ? $"상태 · {block.Describe()}" : uses > 1 ? $"모션 · {block.Motion} — {uses}곳에서 쓰인다(고치면 다 바뀐다)" : $"모션 · {block.Motion}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            if (block.IsMotion)
            {
                _clipBlock.Children.Add(BlockNumber("반복(번)", block.Cycles, v => b => b.Repeat = v is null ? null : (int)Math.Round(v.Value),
                    "그 모션을 이어서 몇 번 도는가 — 같은 블록을 끝에 이어 놓은 것과 같다(유지 모션은 돌 때마다 쌓이고, 복원 모션은 매번 처음 자세에서 다시 돈다). 비우면 한 번"));
            }

            var off = new CheckBox { Content = "이 블록 끄기 (M)", IsChecked = block.IsMuted, FontSize = 11 };
            ToolTip.SetTip(off, "자리는 그대로 두고 적용만 뺀다 — 미리보기·굽기·겹침 검사에서 없는 것처럼. 파일에 남는다(되돌리기 한 번)");
            off.IsCheckedChanged += (_, _) => FromBlockControl(() => _timeline.SetSelectedMuted(off.IsChecked == true));
            _clipBlock.Children.Add(off);
            _clipBlock.Children.Add(BlockNumber("시작(초)", block.At, v => b => b.At = v ?? 0f, "블록이 시작하는 시각. 타임라인에서 끌어도 된다"));

            if (block.IsMotion)
            {
                var mode = new ComboBox { ItemsSource = new[] { "상대값(relative)", "절대값(absolute)" }, SelectedIndex = (int)block.Mode, FontSize = 11 };
                mode.SelectionChanged += (_, _) => FromBlockControl(() => UpdateSelectedClipBlock(b => b.Mode = (MotionChannelMode)mode.SelectedIndex), mode.SelectedIndex >= 0);
                _clipBlock.Children.Add(Labeled("재생 기준", mode));
                return;
            }

            _clipBlock.Children.Add(BlockNumber("길이(초)", block.Duration, v => b => b.Duration = v, "비우거나 0이면 그 순간에 바뀐다"));
            string[] eases = Enum.GetNames<EaseKind>();
            var ease = new ComboBox { ItemsSource = eases, SelectedIndex = (int)(block.Ease ?? EaseKind.Linear), FontSize = 11 };
            ease.SelectionChanged += (_, _) => FromBlockControl(() => UpdateSelectedClipBlock(b => b.Ease = ease.SelectedIndex == 0 ? null : (EaseKind)ease.SelectedIndex), ease.SelectedIndex >= 0);
            ToolTip.SetTip(ease, "길이 동안 값이 가는 모양(DOTween 이징). 중간 프레임은 런타임 트윈을 따라 그린 근사다 — 끝 상태만 런타임과 같다");
            _clipBlock.Children.Add(Labeled("이징", ease));

            switch (block.Kind)
            {
                case ClipBlockKind.Visible:
                    var visible = new CheckBox { Content = "보이기", IsChecked = block.Visible ?? true, FontSize = 11 };
                    visible.IsCheckedChanged += (_, _) => FromBlockControl(() => UpdateSelectedClipBlock(b => b.Visible = visible.IsChecked == true));
                    _clipBlock.Children.Add(visible);
                    break;

                case ClipBlockKind.Place:
                    var points = new List<string> { "(자리 풀기)" };
                    points.AddRange(ScenePreview.ScreenPoints);
                    var point = new ComboBox { ItemsSource = points, SelectedIndex = string.IsNullOrEmpty(block.Place) ? 0 : Math.Max(0, points.IndexOf(block.Place)), FontSize = 11 };
                    point.SelectionChanged += (_, _) => FromBlockControl(() => UpdateSelectedClipBlock(b => b.Place = point.SelectedIndex == 0 ? "" : points[point.SelectedIndex]), point.SelectedIndex >= 0);
                    _clipBlock.Children.Add(Labeled("화면 지점", point));
                    var focuses = new List<string> { "(그대로)" };
                    focuses.AddRange(ScenePreview.FocusPresets);
                    var focus = new ComboBox { ItemsSource = focuses, SelectedIndex = block.PlaceFocus is null ? 0 : Math.Max(0, focuses.IndexOf(block.PlaceFocus)), FontSize = 11 };
                    focus.SelectionChanged += (_, _) => FromBlockControl(() => UpdateSelectedClipBlock(b => b.PlaceFocus = focus.SelectedIndex == 0 ? null : focuses[focus.SelectedIndex]), focus.SelectedIndex >= 0);
                    _clipBlock.Children.Add(Labeled("focus", focus));
                    break;

                case ClipBlockKind.Layer:
                    var layers = SceneLayers.Order.ToList();
                    var layer = new ComboBox { ItemsSource = layers, SelectedIndex = layers.IndexOf(block.Layer ?? ""), FontSize = 11 };
                    layer.SelectionChanged += (_, _) => FromBlockControl(() => UpdateSelectedClipBlock(b => b.Layer = layers[layer.SelectedIndex]), layer.SelectedIndex >= 0);
                    _clipBlock.Children.Add(Labeled("층", layer));
                    break;

                case ClipBlockKind.Offset:
                    const string keep = "비우면 그 값은 안 바뀐다";
                    _clipBlock.Children.Add(BlockNumber("오프셋 X", block.X, v => b => b.X = v, keep));
                    _clipBlock.Children.Add(BlockNumber("오프셋 Y", block.Y, v => b => b.Y = v, keep));
                    _clipBlock.Children.Add(BlockNumber("회전(도)", block.Rotation, v => b => b.Rotation = v, keep));
                    _clipBlock.Children.Add(BlockNumber("배율 X", block.ScaleX, v => b => b.ScaleX = v, keep));
                    _clipBlock.Children.Add(BlockNumber("배율 Y", block.ScaleY, v => b => b.ScaleY = v, keep));
                    break;

                case ClipBlockKind.Camera:
                    const string keepCamera = "비우면 그 값은 안 바뀐다. zoom 1당 배율 0.05, pan은 px";
                    _clipBlock.Children.Add(BlockNumber("zoom", block.Zoom, v => b => b.Zoom = v, keepCamera));
                    _clipBlock.Children.Add(BlockNumber("pan X", block.PanX, v => b => b.PanX = v, keepCamera));
                    _clipBlock.Children.Add(BlockNumber("pan Y", block.PanY, v => b => b.PanY = v, keepCamera));
                    break;
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>블록 인스펙터의 컨트롤이 바뀌었다 — 다시 그리는 중이 아닐 때만 편집으로 본다.</summary>
    private void FromBlockControl(Action apply, bool valid = true)
    {
        if (!_refreshing && valid)
        {
            Guard("블록", apply);
        }
    }

    /// <summary>숫자 칸 한 줄 — 빈 칸은 null(그 값은 안 바뀐다). Enter·포커스 잃기로 확정.</summary>
    private Control BlockNumber(string label, float? value, Func<float?, Action<ClipBlock>> setter, string tip)
    {
        var box = new TextBox { FontSize = 11, Text = value is { } v ? v.ToString("0.###", CultureInfo.InvariantCulture) : "", Tag = label };
        ToolTip.SetTip(box, tip);
        OnCommit(box, text =>
        {
            if (_refreshing)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Guard("블록", () => UpdateSelectedClipBlock(setter(null)));
            }
            else if (TryFloat(text, out float parsed))
            {
                Guard("블록", () => UpdateSelectedClipBlock(setter(parsed)));
            }
        });
        return Labeled(label, box);
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
