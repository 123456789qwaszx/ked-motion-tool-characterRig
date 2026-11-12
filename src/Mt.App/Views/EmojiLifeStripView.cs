using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Mt.Authoring.Model;

namespace Mt.App.Views;

/// <summary>띠 하나 — 구간과 그것이 이어진 수명의 자리(없으면 잇지 않은 구간).</summary>
public readonly record struct LifeBand(string Name, int Start, int End, EmojiLifeRole? Role);

/// <summary>띠의 어디를 잡았는가.</summary>
public enum LifeGrip
{
    Start,
    Body,
    End,
}

/// <summary>
/// 수명의 띠(<c>docs/history/PLAN-ux.md</c> U5) — 타임 슬라이더 위에 등장 · 머묾 · 퇴장을 색 띠로 보이고, 끌어서 고친다.
///
/// - 가로는 타임 슬라이더와 같은 눈금이다(<see cref="RangeStart"/> … <see cref="RangeEnd"/>, 초) — 띠의 끝이 아래 눈금의 프레임과 맞는다.
/// - 띠의 끝을 끌면 시작 · 끝이, 몸통을 끌면 통째로 옮겨진다. 프레임에 붙는다. 놓을 때 한 번 알린다(<see cref="SpanCommitted"/> — 되돌리기 한 칸).
/// - 오른쪽 단추로 누르면 그 띠의 메뉴를 청한다(<see cref="BandMenuRequested"/>).
/// 이 컨트롤은 문서를 모른다 — 띠를 받고 바뀐 프레임을 알릴 뿐이다.
/// </summary>
public sealed class EmojiLifeStripView : Control
{
    public const double StripHeight = 22;
    private const double EdgeGrip = 5;
    private const double MinBandWidth = 8;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly IBrush LooseFill = new SolidColorBrush(Color.FromArgb(70, 0xB4, 0xB4, 0xB4));
    private static readonly Pen LoosePen = new(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 1, DashStyle.Dash);
    private static readonly Pen PickedPen = new(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)), 1.5);
    private static readonly Pen NowPen = new(new SolidColorBrush(Color.FromArgb(200, 0xF2, 0xF2, 0xF2)), 1);

    private static readonly IBrush[] RoleBrushes =
    {
        new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6B)),
        new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x30)),
        new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xE8)),
    };

    private IReadOnlyList<LifeBand> _bands = Array.Empty<LifeBand>();
    private int _lastFrame;
    private double _rangeStart;
    private double _rangeEnd = 1;
    private double _currentTime;
    private string? _picked;
    private (LifeBand Band, LifeGrip Grip, double PressX, int Start, int End)? _drag;

    public EmojiLifeStripView()
    {
        Height = StripHeight;
        ClipToBounds = true;
    }

    /// <summary>띠의 끝이나 몸통을 끌어 놓았다 — 이름과 새 프레임(끝 포함). 안 바뀌었으면 알리지 않는다.</summary>
    public event Action<string, int, int>? SpanCommitted;

    /// <summary>띠를 눌렀다 — 그 구간의 이름.</summary>
    public event Action<string>? BandPicked;

    /// <summary>띠를 오른쪽 단추로 눌렀다 — 그 구간의 이름.</summary>
    public event Action<string>? BandMenuRequested;

    /// <summary>자리의 색 — 타임 슬라이더의 띠와 같은 색을 쓴다.</summary>
    public static IBrush BrushOf(EmojiLifeRole role) => RoleBrushes[(int)role];

    public IReadOnlyList<LifeBand> Bands => _bands;

    /// <summary>고른 띠(구간 이름).</summary>
    public string? Picked
    {
        get => _picked;
        set
        {
            _picked = value;
            InvalidateVisual();
        }
    }

    /// <summary>띠와 타임라인의 마지막 프레임을 준다.</summary>
    public void SetBands(IReadOnlyList<LifeBand> bands, int lastFrame)
    {
        _bands = bands ?? Array.Empty<LifeBand>();
        _lastFrame = Math.Max(0, lastFrame);

        // 끄는 도중에 같은 띠들이 다시 오면(띠를 고른 것이 화면을 새로 고친다) 끌기를 지킨다 — 그 구간이 달라졌을 때만 놓는다.
        if (_drag is { } drag && !_bands.Contains(drag.Band))
        {
            _drag = null;
        }

        if (_picked is not null && !_bands.Any(b => b.Name == _picked))
        {
            _picked = null;
        }

        InvalidateVisual();
    }

    /// <summary>보이는 범위(초) — 타임 슬라이더와 같게 준다.</summary>
    public void SetRange(double start, double end)
    {
        _rangeStart = start;
        _rangeEnd = Math.Max(start + 1e-3, end);
        InvalidateVisual();
    }

    /// <summary>지금 시각(초) — 가는 세로줄.</summary>
    public double CurrentTime
    {
        get => _currentTime;
        set
        {
            if (Math.Abs(_currentTime - value) > 1e-9)
            {
                _currentTime = value;
                InvalidateVisual();
            }
        }
    }

    // 타임 슬라이더의 Track과 같은 셈(양쪽 2px).
    private double XOfSeconds(double seconds) => 2 + (seconds - _rangeStart) / (_rangeEnd - _rangeStart) * Math.Max(1, Bounds.Width - 4);

    private double XOfFrame(int frame) => XOfSeconds(EmojiAsset.SecondsOf(frame));

    private double FramesPerPixel => (_rangeEnd - _rangeStart) * EmojiAsset.Fps / Math.Max(1, Bounds.Width - 4);

    /// <summary>띠의 네모(컨트롤 좌표). 끄는 중이면 끄는 대로.</summary>
    public Rect RectOf(LifeBand band)
    {
        (int start, int end) = _drag is { } d && d.Band.Name == band.Name ? (d.Start, d.End) : (band.Start, band.End);
        double left = XOfFrame(start);
        double right = Math.Max(left + MinBandWidth, XOfFrame(end));
        return band.Role is null ? new Rect(left, 12, right - left, StripHeight - 14) : new Rect(left, 2, right - left, StripHeight - 4);
    }

    /// <summary>그 자리의 띠와 잡은 곳 — 이은 띠가 잇지 않은 구간보다 먼저다.</summary>
    public (LifeBand Band, LifeGrip Grip)? Hit(Point position)
    {
        foreach (LifeBand band in _bands.OrderBy(b => b.Role is null ? 1 : 0))
        {
            Rect rect = RectOf(band).Inflate(new Thickness(EdgeGrip / 2, 2));

            if (!rect.Contains(position))
            {
                continue;
            }

            // 좁은 띠에서도 몸통이 남도록 끝의 손잡이는 너비의 1/3을 넘지 않는다.
            double grip = Math.Min(EdgeGrip, rect.Width / 3);
            LifeGrip where = position.X <= rect.X + grip ? LifeGrip.Start : position.X >= rect.Right - grip ? LifeGrip.End : LifeGrip.Body;
            return (band, where);
        }

        return null;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));

        if (_bands.Count == 0)
        {
            DrawText(context, "구간이 없다 — 타임라인 전체를 한 번 재생하고 사라진다. 위의 + 등장 · + 머묾 · + 퇴장으로 놓는다", new Point(6, 4), HintBrush, 10);
        }

        foreach (LifeBand band in _bands.OrderBy(b => b.Role is null ? 0 : 1))
        {
            Rect rect = RectOf(band);
            (int start, int end) = _drag is { } d && d.Band.Name == band.Name ? (d.Start, d.End) : (band.Start, band.End);
            string range = $"{start.ToString(CultureInfo.InvariantCulture)}–{end.ToString(CultureInfo.InvariantCulture)}";

            if (band.Role is { } role)
            {
                context.DrawRectangle(BrushOf(role), band.Name == _picked ? PickedPen : null, rect, 3, 3);

                using (context.PushClip(rect))
                {
                    DrawText(context, $"{EmojiLife.Label(role)} {range}", new Point(rect.X + 5, rect.Y + 2), LabelBrush, 10);
                }
            }
            else
            {
                context.DrawRectangle(LooseFill, band.Name == _picked ? PickedPen : LoosePen, rect, 2, 2);

                using (context.PushClip(rect))
                {
                    DrawText(context, $"{band.Name} {range}", new Point(rect.X + 4, rect.Y - 3), HintBrush, 9);
                }
            }
        }

        double now = XOfSeconds(_currentTime);

        if (now >= 0 && now <= Bounds.Width)
        {
            context.DrawLine(NowPen, new Point(now, 0), new Point(now, Bounds.Height));
        }
    }

    private static void DrawText(DrawingContext context, string text, Point at, IBrush brush, double size)
        => context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush), at);

    // ── 입력 ──────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            if (Hit(point.Position) is { } menu)
            {
                Picked = menu.Band.Name;
                BandPicked?.Invoke(menu.Band.Name);
                BandMenuRequested?.Invoke(menu.Band.Name);
                e.Handled = true;
            }

            return;
        }

        if (point.Properties.IsLeftButtonPressed && Press(point.Position))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);

        if (_drag is not null)
        {
            Move(position);
            return;
        }

        Cursor = Hit(position) switch
        {
            { Grip: LifeGrip.Body } => new Cursor(StandardCursorType.Hand),
            { } => new Cursor(StandardCursorType.SizeWestEast),
            _ => Cursor.Default,
        };
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_drag is not null)
        {
            e.Pointer.Capture(null);
            Release();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_drag is not null)
        {
            _drag = null;
            InvalidateVisual();
        }
    }

    private bool Press(Point position)
    {
        if (Hit(position) is not { } hit)
        {
            return false;
        }

        _drag = (hit.Band, hit.Grip, position.X, hit.Band.Start, hit.Band.End);
        Picked = hit.Band.Name;
        BandPicked?.Invoke(hit.Band.Name);
        return true;
    }

    private void Move(Point position)
    {
        if (_drag is not { } drag)
        {
            return;
        }

        int delta = (int)Math.Round((position.X - drag.PressX) * FramesPerPixel);
        int start = drag.Band.Start;
        int end = drag.Band.End;

        switch (drag.Grip)
        {
            case LifeGrip.Start:
                start = Math.Clamp(start + delta, 0, end);
                break;
            case LifeGrip.End:
                end = Math.Clamp(end + delta, start, Math.Max(start, _lastFrame));
                break;
            default:
                // 통째로 — 길이를 지키고 타임라인 안에서만 움직인다.
                delta = Math.Clamp(delta, -start, Math.Max(0, _lastFrame - end));
                start += delta;
                end += delta;
                break;
        }

        _drag = drag with { Start = start, End = end };
        InvalidateVisual();
    }

    private void Release()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        InvalidateVisual();

        if (drag.Start != drag.Band.Start || drag.End != drag.Band.End)
        {
            SpanCommitted?.Invoke(drag.Band.Name, drag.Start, drag.End);
        }
    }

    /// <summary>테스트 손잡이 — 컨트롤 좌표에서 누르고 끌어 놓는다(사람의 끌기와 같은 길). 띠를 잡지 못했으면 거짓.</summary>
    internal bool DragForTest(Point from, Point to)
    {
        if (!Press(from))
        {
            return false;
        }

        Move(to);
        Release();
        return true;
    }
}
