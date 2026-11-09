using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Validation;

namespace Mt.App.Views;

/// <summary>
/// 연출 클립의 타임라인(<c>PLAN-clip.md</c> K4) — 트랙마다 한 줄, 모션 블록은 그 모션 길이만큼의 막대.
///
/// 조작은 도프 시트와 같다: LMB = 블록 선택 · **블록 드래그(LMB·MMB) = 이동**(가로 = 시각, 세로 = 트랙) · 빈 곳/눈금자 LMB = 스크럽 ·
/// 더블클릭 = 그 모션을 연다 · RMB = 메뉴(모션 놓기 · mode · 삭제 · 트랙) · Del · Ctrl+D.
/// 끄는 동안은 그리기만 하고, 놓는 순간이 편집기 명령 하나(되돌리기 한 칸)다. 겹친 블록(같은 배역·같은 노드)은 빨갛게.
/// 무엇을 보일지는 뷰포트가 정한다 — 뷰포트가 클립을 묶고 있을 때만 그린다.
/// </summary>
public sealed class ClipTimelineView : Control
{
    public const double RulerHeight = 18;
    public const double RowHeight = 24;
    public const double Gutter = 120;
    private const double RightPad = 10;
    private const double DragThreshold = 3;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    private static readonly IBrush RulerBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
    private static readonly IBrush RowBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly IBrush RowAltBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x2F, 0x2F));
    private static readonly IBrush GutterBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly IBrush OutsideBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x10, 0x10, 0x10));
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x3A, 0x3A));
    private static readonly Pen TickPen = new(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);
    private static readonly Pen SelectedPen = new(Brushes.White, 1.5);
    private static readonly Pen OverlapPen = new(Brushes.OrangeRed, 1.5);
    private static readonly Pen TimePen = new(new SolidColorBrush(Color.FromRgb(0xF9, 0xD7, 0x1C)), 1.5);

    /// <summary>배역마다 다른 색 — 배역 순서로 돈다.</summary>
    private static readonly IBrush[] RoleBrushes =
    {
        new SolidColorBrush(Color.FromRgb(0x4F, 0x7C, 0xAC)),
        new SolidColorBrush(Color.FromRgb(0x5E, 0x94, 0x5E)),
        new SolidColorBrush(Color.FromRgb(0xA8, 0x74, 0x4A)),
        new SolidColorBrush(Color.FromRgb(0x8A, 0x62, 0xA8)),
        new SolidColorBrush(Color.FromRgb(0xA8, 0x5A, 0x6E)),
    };

    private MotionEditor? _editor;
    private ViewportView? _viewport;
    private (int Track, int Index)? _selected;
    private ContextMenu? _menu;

    private enum Drag { None, Scrub, Move }

    private Drag _drag = Drag.None;
    private Point _dragStart;
    private (int Track, float At)? _ghost;

    public ClipTimelineView()
    {
        Focusable = true;
        ClipToBounds = true;
        MinHeight = RulerHeight + RowHeight * 2;
    }

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = 1.0 / 24.0;

    public TimeDisplay TimeDisplay { get; set; } = TimeDisplay.Frames;

    /// <summary>블록을 더블클릭했다(또는 메뉴의 "모션 편집") — 셸이 그 모션을 연다.</summary>
    public event EventHandler<(int Track, int Index)>? BlockOpened;

    public event EventHandler? SelectionChanged;

    /// <summary>메뉴의 "새 모션으로 굽기" — 그 트랙의 배역을 맡은 캐릭터. 셸이 굽는다.</summary>
    public event EventHandler<int>? BakeRequested;

    public ViewportView? Viewport
    {
        get => _viewport;
        set
        {
            if (ReferenceEquals(_viewport, value))
            {
                return;
            }

            if (_viewport is not null)
            {
                _viewport.TimeChanged -= OnViewportChanged;
                _viewport.RangeChanged -= OnViewportChanged;
                _viewport.ClipMuteChanged -= OnViewportChanged;
            }

            _viewport = value;

            if (_viewport is not null)
            {
                _viewport.TimeChanged += OnViewportChanged;
                _viewport.RangeChanged += OnViewportChanged;
                _viewport.ClipMuteChanged += OnViewportChanged;
            }

            InvalidateVisual();
        }
    }

    public void Bind(MotionEditor? editor)
    {
        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
        }

        _editor = editor;

        if (_editor is not null)
        {
            _editor.Changed += OnEditorChanged;
        }

        Select(null);
        InvalidateVisual();
    }

    /// <summary>보이는 클립 — 뷰포트가 묶은 것.</summary>
    public ClipAsset? ShownClip => _viewport?.ShownClip;

    public (int Track, int Index)? SelectedBlock => _selected;

    private ClipBlock? Resolve((int Track, int Index)? at)
        => at is { } a && ShownClip is { } clip && a.Track >= 0 && a.Track < clip.Tracks.Count && a.Index >= 0 && a.Index < clip.Tracks[a.Track].Blocks.Count
            ? clip.Tracks[a.Track].Blocks[a.Index]
            : null;

    public void Select((int Track, int Index)? block)
    {
        if (block is not null && Resolve(block) is null)
        {
            block = null;
        }

        if (_selected == block)
        {
            return;
        }

        _selected = block;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void OnViewportChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        if (Resolve(_selected) is null)
        {
            Select(null); // 고른 블록이 사라졌다(삭제·되돌리기·클립 전환)
        }

        InvalidateVisual();
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    /// <summary>보이는 시간 폭 — 클립 길이와 마지막 블록 끝 중 긴 쪽에 여유를 둔다(길이를 넘친 블록도 보인다).</summary>
    public double Span
    {
        get
        {
            double duration = _viewport?.Clock.Duration ?? 1;
            double blocks = _editor is not null && ShownClip is { } clip ? ClipTiming.EndOfBlocks(_editor.Document, clip) : 0;
            return Math.Max(0.5, Math.Max(duration, blocks) * 1.15);
        }
    }

    private double PlotWidth => Math.Max(1, Bounds.Width - Gutter - RightPad);

    public double XAtTime(double seconds) => Gutter + seconds / Span * PlotWidth;

    public double TimeAtX(double x) => Math.Max(0, (x - Gutter) / PlotWidth * Span);

    public Rect RowRect(int track) => new(0, RulerHeight + track * RowHeight, Bounds.Width, RowHeight);

    /// <summary>y 자리의 트랙 번호. 눈금자 위·트랙 밖이면 null.</summary>
    public int? TrackAtY(double y)
    {
        if (ShownClip is not { } clip || y < RulerHeight)
        {
            return null;
        }

        int track = (int)((y - RulerHeight) / RowHeight);
        return track < clip.Tracks.Count ? track : null;
    }

    /// <summary>블록의 사각형. 모션이 문서에 없으면 길이를 모른다 — 좁은 막대로 그린다.</summary>
    public Rect BlockRect(int track, int index)
    {
        if (_editor is null || Resolve((track, index)) is not { } block)
        {
            return default;
        }

        return BlockRect(track, block.At, _editor.Document.Find(block.Motion)?.Duration ?? 0f);
    }

    private Rect BlockRect(int track, float at, float duration)
    {
        Rect row = RowRect(track);
        double x0 = XAtTime(at);
        double x1 = Math.Max(x0 + 6, XAtTime(at + duration));
        return new Rect(x0, row.Y + 3, x1 - x0, row.Height - 6);
    }

    private (int Track, int Index)? HitBlock(Point position)
    {
        if (ShownClip is not { } clip || TrackAtY(position.Y) is not { } track)
        {
            return null;
        }

        // 뒤에 놓인(늦게 시작하는) 블록이 위에 그려진다 — 맞은 것도 그 순서로.
        for (int i = clip.Tracks[track].Blocks.Count - 1; i >= 0; i--)
        {
            if (BlockRect(track, i).Contains(position))
            {
                return (track, i);
            }
        }

        return null;
    }

    private float SnapTime(double seconds) => (float)Math.Max(0, SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds);

    /// <summary>줄 머리의 끄기 표시 자리 — 누르면 그 트랙의 배역이 꺼지고 켜진다.</summary>
    public Rect MuteRect(int track)
    {
        Rect row = RowRect(track);
        return new Rect(Gutter - 24, row.Y, 22, row.Height);
    }

    /// <summary>그 트랙의 배역을 끄거나 켠다(같은 배역의 트랙이 다 같이). 보기 상태 — 문서는 안 바뀐다.</summary>
    public void ToggleRoleMuted(int track)
    {
        if (_viewport is null || ShownClip is not { } clip || track < 0 || track >= clip.Tracks.Count)
        {
            return;
        }

        string role = clip.Tracks[track].Role;
        _viewport.SetClipRoleMuted(role, !_viewport.IsClipRoleMuted(role));
    }

    /// <summary>그 트랙의 배역을 맡은 캐릭터를 새 모션으로 굽자고 셸에 알린다.</summary>
    public void RequestBake(int track) => BakeRequested?.Invoke(this, track);

    // ── 명령 — 전부 편집기 명령 하나 ──────────────────────────────────

    /// <summary>모션 블록을 놓는다(메뉴의 "모션 놓기"). 놓은 블록이 골라진다.</summary>
    public void PlaceMotion(int track, string motion, double seconds)
    {
        if (_editor is null || ShownClip is not { } clip)
        {
            return;
        }

        int index = _editor.AddClipBlock(clip.Name, track, motion, SnapTime(seconds));
        Select((track, index));
    }

    public void RemoveSelected()
    {
        if (_editor is null || ShownClip is not { } clip || _selected is not { } s)
        {
            return;
        }

        _editor.RemoveClipBlock(clip.Name, s.Track, s.Index);
    }

    /// <summary>고른 블록을 그 끝에 바로 이어 한 번 더 놓는다(Ctrl+D). 새 블록이 골라진다.</summary>
    public void DuplicateSelected()
    {
        if (_editor is null || ShownClip is not { } clip || _selected is not { } s || Resolve(s) is not { } block)
        {
            return;
        }

        int index = _editor.AddClipBlock(clip.Name, s.Track, block.Motion, ClipTiming.EndOf(_editor.Document, block), block.Mode);
        Select((s.Track, index));
    }

    public void SetSelectedMode(MotionChannelMode mode)
    {
        if (_editor is null || ShownClip is not { } clip || _selected is not { } s)
        {
            return;
        }

        _editor.SetClipBlockMode(clip.Name, s.Track, s.Index, mode);
    }

    /// <summary>고른 블록을 옮긴다 — 끌어 놓은 것과 같은 길.</summary>
    public void MoveSelected(double seconds, int? toTrack = null)
    {
        if (_editor is null || ShownClip is not { } clip || _selected is not { } s)
        {
            return;
        }

        (int Track, int Index) moved = _editor.MoveClipBlock(clip.Name, s.Track, s.Index, SnapTime(seconds), toTrack);
        _selected = moved; // Select의 "같으면 무시"를 지나 번호만 따라간다
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_viewport is null || ShownClip is null)
        {
            return;
        }

        Focus();
        Point position = e.GetPosition(this);
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;
        (int Track, int Index)? hit = HitBlock(position);

        if (pressed.IsRightButtonPressed)
        {
            if (hit is not null)
            {
                Select(hit);
            }

            OpenMenu(position, hit);
            e.Handled = true;
            return;
        }

        if (pressed.IsMiddleButtonPressed)
        {
            if (hit is not null)
            {
                Select(hit); // 마야와 달리 맞은 블록을 바로 잡는다 — 블록은 키보다 크고 하나씩 옮긴다
            }

            if (Resolve(_selected) is not null)
            {
                _drag = Drag.Move;
                _dragStart = position;
                _ghost = null;
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (!pressed.IsLeftButtonPressed)
        {
            return;
        }

        if (hit is not null)
        {
            Select(hit);

            if (e.ClickCount == 2)
            {
                BlockOpened?.Invoke(this, hit.Value);
            }
            else
            {
                // 도프 시트처럼 블록을 바로 끈다 — 조금이라도 움직여야 이동이다(클릭은 선택뿐).
                _drag = Drag.Move;
                _dragStart = position;
                _ghost = null;
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (TrackAtY(position.Y) is { } muteTrack && MuteRect(muteTrack).Contains(position))
        {
            ToggleRoleMuted(muteTrack);
            e.Handled = true;
            return;
        }

        if (position.Y >= RulerHeight)
        {
            Select(null);
        }

        if (position.X >= Gutter)
        {
            _drag = Drag.Scrub;
            _viewport.Pause();
            _viewport.ScrubTo(SnapTime(TimeAtX(position.X)));
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);

        switch (_drag)
        {
            case Drag.Scrub:
                _viewport?.ScrubTo(SnapTime(TimeAtX(position.X)));
                break;

            case Drag.Move when _selected is { } s && Resolve(s) is { } block && ShownClip is { } clip:
                if (_ghost is null && Math.Abs(position.X - _dragStart.X) < DragThreshold && Math.Abs(position.Y - _dragStart.Y) < DragThreshold)
                {
                    break;
                }

                double delta = (position.X - _dragStart.X) / PlotWidth * Span;
                int track = Math.Clamp(s.Track + (int)Math.Round((position.Y - _dragStart.Y) / RowHeight), 0, clip.Tracks.Count - 1);
                _ghost = (track, SnapTime(block.At + delta));
                InvalidateVisual();
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Drag drag = _drag;
        (int Track, float At)? ghost = _ghost;
        _drag = Drag.None;
        _ghost = null;
        e.Pointer.Capture(null);

        if (drag == Drag.Move && ghost is { } g && _selected is { } s && Resolve(s) is { } block
            && (g.Track != s.Track || Math.Abs(g.At - block.At) > 1e-6f))
        {
            try
            {
                MoveSelected(g.At, g.Track);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException)
            {
                // 놓는 사이 문서가 바뀌었다 — 옮기지 않는다.
            }
        }

        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_selected is null)
        {
            return;
        }

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            DuplicateSelected();
            e.Handled = true;
        }
    }

    private void OpenMenu(Point position, (int Track, int Index)? hit)
    {
        if (_editor is null || ShownClip is not { } clip)
        {
            return;
        }

        var menu = new ContextMenu();

        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, FontSize = 11, IsEnabled = enabled };
            item.Click += (_, _) => Guarded(action);
            return item;
        }

        if (hit is { } h && Resolve(h) is { } block)
        {
            menu.Items.Add(Item((block.Mode == MotionChannelMode.Relative ? "● " : "○ ") + "상대값(relative)", () => SetSelectedMode(MotionChannelMode.Relative)));
            menu.Items.Add(Item((block.Mode == MotionChannelMode.Absolute ? "● " : "○ ") + "절대값(absolute)", () => SetSelectedMode(MotionChannelMode.Absolute)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("모션 편집 (더블클릭)", () => BlockOpened?.Invoke(this, h)));
            menu.Items.Add(Item("이어서 한 번 더 (Ctrl+D)", DuplicateSelected));
            menu.Items.Add(Item("블록 삭제 (Del)", RemoveSelected));
        }
        else if (TrackAtY(position.Y) is { } track)
        {
            string roleName = clip.Tracks[track].Role;
            string? rig = clip.FindRole(roleName)?.Rig;
            double at = TimeAtX(Math.Max(Gutter, position.X));
            var place = new MenuItem { Header = $"모션 놓기 ({TimeText.Format(SnapTime(at), TimeDisplay)})", FontSize = 11 };

            foreach (MotionAsset motion in _editor.Document.Motions.Where(m => rig is null || ClipValidator.Wears(rig, m, null)).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                string name = motion.Name;
                place.Items.Add(Item($"{name}  ·  {motion.Duration:0.###}s", () => PlaceMotion(track, name, at)));
            }

            place.IsEnabled = place.Items.Count > 0;
            menu.Items.Add(place);
            menu.Items.Add(new Separator());
            bool muted = _viewport?.IsClipRoleMuted(roleName) == true;
            string? actor = clip.FindRole(roleName)?.Object;
            menu.Items.Add(Item(muted ? $"배역 {roleName} 켜기" : $"배역 {roleName} 끄기", () => ToggleRoleMuted(track)));
            menu.Items.Add(Item(actor is null ? "새 모션으로 굽기 (배역이 안 묶였다)" : $"{actor}의 블록 전부를 새 모션으로 굽기", () => RequestBake(track), actor is not null));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item($"트랙 추가 (배역 {roleName})", () => _editor.AddClipTrack(clip.Name, roleName)));
            menu.Items.Add(Item("트랙 삭제", () => _editor.RemoveClipTrack(clip.Name, track)));
        }
        else
        {
            return;
        }

        _menu?.Close();
        _menu = menu;
        menu.Open(this);
    }

    /// <summary>메뉴의 명령이 던져도 창이 죽지 않게 — 편집기는 흔적을 남기지 않는다.</summary>
    private static void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException)
        {
        }
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Background, null, bounds);

        if (_editor is null || _viewport is null || ShownClip is not { } clip)
        {
            Draw(context, "클립을 고르면 여기에 타임라인이 선다 — 왼쪽 '클립' 목록.", new Point(10, 8), DimBrush);
            return;
        }

        double duration = _viewport.Clock.Duration;
        context.DrawRectangle(RulerBrush, null, new Rect(0, 0, bounds.Width, RulerHeight));
        DrawRuler(context, bounds);

        HashSet<(int Track, int Index)> overlapping = ClipValidator.OverlappingBlocks(_editor.Document, clip);

        for (int t = 0; t < clip.Tracks.Count; t++)
        {
            ClipTrack track = clip.Tracks[t];
            Rect row = RowRect(t);
            context.DrawRectangle(t % 2 == 0 ? RowBrush : RowAltBrush, null, row);
            context.DrawRectangle(GutterBrush, null, new Rect(0, row.Y, Gutter - 1, row.Height));

            ClipRole? role = clip.FindRole(track.Role);
            int roleIndex = role is null ? -1 : clip.Roles.IndexOf(role);
            string label = role is null ? $"{track.Role} (배역 없음)" : role.Object is null ? $"{role.Name} · 안 묶음" : $"{role.Name} · {role.Object}";
            bool muted = _viewport.IsClipRoleMuted(track.Role);
            Draw(context, label, new Point(8, row.Y + 6), role?.Object is null || muted ? DimBrush : LabelBrush);
            Draw(context, muted ? "🔇" : "🔊", new Point(MuteRect(t).X + 3, row.Y + 5), muted ? DimBrush : LabelBrush);
            using IDisposable? dim = muted ? context.PushOpacity(0.35) : null; // 끈 배역의 블록은 흐리게

            for (int i = 0; i < track.Blocks.Count; i++)
            {
                ClipBlock block = track.Blocks[i];
                MotionAsset? motion = _editor.Document.Find(block.Motion);
                Rect rect = BlockRect(t, i);
                IBrush fill = motion is null || roleIndex < 0 ? MissingBrush : RoleBrushes[roleIndex % RoleBrushes.Length];
                Pen? pen = _selected == (t, i) ? SelectedPen : overlapping.Contains((t, i)) ? OverlapPen : null;
                context.DrawRectangle(fill, pen, rect, 3, 3);

                string text = motion is null
                    ? $"{block.Motion} (없음)"
                    : $"{block.Motion} · {(block.Mode == MotionChannelMode.Relative ? "rel" : "abs")} · {(motion.End == MotionEnd.Restore ? "restore" : "commit")}";

                using (context.PushClip(rect))
                {
                    Draw(context, text, new Point(rect.X + 4, rect.Y + 3), LabelBrush);
                }
            }
        }

        // 클립 길이 밖은 어둡게 — 넘친 블록이 한눈에 보인다.
        double endX = XAtTime(duration);

        if (endX < bounds.Width)
        {
            context.DrawRectangle(OutsideBrush, null, new Rect(endX, RulerHeight, bounds.Width - endX, bounds.Height - RulerHeight));
        }

        if (_drag == Drag.Move && _ghost is { } ghost && Resolve(_selected) is { } moving)
        {
            context.DrawRectangle(GhostBrush, SelectedPen, BlockRect(ghost.Track, ghost.At, _editor.Document.Find(moving.Motion)?.Duration ?? 0f), 3, 3);
        }

        double timeX = XAtTime(_viewport.Time);
        context.DrawLine(TimePen, new Point(timeX, 0), new Point(timeX, bounds.Height));

        if (clip.Tracks.Count == 0)
        {
            Draw(context, "트랙이 없다 — 오른쪽 '클립' 탭에서 배역을 더하면 트랙이 선다.", new Point(Gutter + 6, RulerHeight + 6), DimBrush);
        }
    }

    private void DrawRuler(DrawingContext context, Rect bounds)
    {
        double pixelsPerSecond = PlotWidth / Span;
        double step = TimeSnap.TickStep(pixelsPerSecond);

        for (double t = 0; XAtTime(t) <= bounds.Width; t += step)
        {
            double x = XAtTime(t);
            context.DrawLine(TickPen, new Point(x, RulerHeight - 5), new Point(x, bounds.Height));
            Draw(context, TimeText.Format(t, TimeDisplay), new Point(x + 3, 3), DimBrush);
        }
    }

    private static void Draw(DrawingContext context, string text, Point at, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 10, brush);
        context.DrawText(formatted, at);
    }
}
