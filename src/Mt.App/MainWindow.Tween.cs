using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Settings;

namespace Mt.App;

// 트윈머신(docs/PLAN-tween.md T1) — 따로 뜨는 작은 창. 본 창 위에 떠 있고, 띄운 채 뷰포트 · 타임라인 · 그래프를 만진다.
//   대상의 한 줄 — "body · spark  |  4fr ↔ 12fr  |  곡선 5개" (창은 지금 고른 것과 지금 프레임을 따라간다)
//   슬라이더 — 앞 키(0%) ↔ 뒤 키(100%). 끄는 동안 뷰포트 · 그래프에 보이고(편집기의 끄는 조작), 놓으면 되돌리기 한 칸.
//   단추 — 0 · 25 · 33 · 50 · 67 · 75 · 100(오버슈트를 켜면 −25 · 125). 누르면 바로 한 칸.
// 대상
//   이모지 — 고른 그림들(없으면 이모지 전체)의 자리 · 크기 · 회전 · 투명도. 그래프에서 곡선을 골랐으면 그 가운데 그것만.
//   캐릭터 모션(T4) — 고른 컨트롤러(뷰포트 · 하이어아키 · 아웃라이너)의 곡선. "모션 전체에"를 켜면 모션의 곡선 전부(포즈 전체).
//     그래프에서 곡선을 골랐으면 그것만.
//     키 없는 포즈가 있으면 버린다 — 트윈이 그 프레임에 키를 놓으니 그 키가 포즈다(되돌리면 포즈도 돌아온다).
// 앞이나 뒤에 키가 없는 곡선은 건너뛴다. 그림 바꾸기는 계단 값이라 뺀다.
public partial class MainWindow
{
    private static readonly ChannelKind[] TweenKinds = { ChannelKind.Position, ChannelKind.Scale, ChannelKind.Euler, ChannelKind.Alpha };

    private Window? _tweenWindow;
    private readonly Slider _tweenSlider = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 25, TickPlacement = TickPlacement.BottomRight, MinWidth = 280 };
    private readonly TextBlock _tweenValue = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _tweenNote = new() { FontSize = 11, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _tweenOvershoot = new() { Content = "오버슈트 (−50 ~ 150%)", FontSize = 11 };
    private readonly CheckBox _tweenWhole = new() { Content = "고른 컨트롤러만이 아니라 모션 전체에", FontSize = 11 };
    private readonly StackPanel _tweenButtons = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private bool _tweenSetting; // 슬라이더를 코드로 옮기는 중 — 적용하지 않는다

    /// <summary>테스트 손잡이 — 창을 열고 닫는다 · 열려 있는가.</summary>
    internal void OpenTweenFromForm() => OpenTween();

    internal void CloseTweenFromForm() => _tweenWindow?.Close();

    internal bool TweenOpen => _tweenWindow is not null;

    internal Window? TweenWindow => _tweenWindow;

    /// <summary>테스트 손잡이 — 대상의 한 줄.</summary>
    internal string TweenNote => _tweenNote.Text ?? "";

    /// <summary>테스트 손잡이 — 슬라이더를 그 값(%)으로 끈다(놓지 않았다 — 미리보기).</summary>
    internal void DragTweenFromForm(double percent) => _tweenSlider.Value = percent;

    /// <summary>테스트 손잡이 — 슬라이더를 놓는다.</summary>
    internal void ReleaseTweenFromForm() => EndTweenDrag();

    /// <summary>테스트 손잡이 — 값 단추(%)를 누른다.</summary>
    internal void PressTweenButtonFromForm(double percent) => ApplyTweenOnce(percent);

    /// <summary>테스트 손잡이 — 오버슈트를 켜고 끈다 · 슬라이더의 범위.</summary>
    internal void SetTweenOvershootFromForm(bool on) => _tweenOvershoot.IsChecked = on;

    /// <summary>테스트 손잡이 — "모션 전체에"를 켜고 끈다.</summary>
    internal void SetTweenWholeFromForm(bool on) => _tweenWhole.IsChecked = on;

    internal (double Min, double Max, double Value) TweenSliderState => (_tweenSlider.Minimum, _tweenSlider.Maximum, _tweenSlider.Value);

    private void BuildTween()
    {
        _tweenSlider.ValueChanged += (_, _) =>
        {
            _tweenValue.Text = $"{_tweenSlider.Value.ToString("0", CultureInfo.InvariantCulture)}%";

            if (!_tweenSetting)
            {
                PreviewTween(_tweenSlider.Value);
            }
        };

        // 놓는 순간 — 손잡이를 놓았다 · 눈금을 눌렀다 뗐다 · 화살표 키를 뗐다 · 다른 데로 갔다.
        _tweenSlider.AddHandler(PointerReleasedEvent, (_, _) => EndTweenDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        _tweenSlider.AddHandler(Thumb.DragCompletedEvent, (_, _) => EndTweenDrag());
        _tweenSlider.PointerCaptureLost += (_, _) => EndTweenDrag();
        _tweenSlider.KeyUp += (_, _) => EndTweenDrag();
        _tweenSlider.LostFocus += (_, _) => EndTweenDrag();

        _tweenOvershoot.IsCheckedChanged += (_, _) =>
        {
            _settings.TweenOvershoot = _tweenOvershoot.IsChecked == true;
            ApplyTweenRange();
        };
        _tweenWhole.IsCheckedChanged += (_, _) => RefreshTweenNote();
        ToolTip.SetTip(_tweenWhole, "캐릭터 모션 — 끄면 고른 컨트롤러(몸 · 숙임 …)의 곡선에만(마야처럼 고른 것을 따라간다), 켜면 모션의 곡선 전부에(포즈 전체의 사이 포즈). 그래프에서 곡선을 골랐으면 어느 쪽이든 그것만");
        ToolTip.SetTip(_tweenOvershoot, "슬라이더가 앞 키 · 뒤 키의 값을 넘어간다 — 100% 위는 지나침(오버슈트), 0% 아래는 예비 동작(반대쪽으로 먼저)");
        ToolTip.SetTip(_tweenSlider, "앞 키(0%) ↔ 뒤 키(100%) 사이의 값을 지금 프레임에 키로 놓는다. 끄는 동안 뷰포트 · 그래프에 보이고, 놓으면 되돌리기 한 칸");

        // 지금 고른 것 · 지금 프레임 · 그래프에서 고른 곡선을 따라간다.
        _viewport.TimeChanged += (_, _) => RefreshTweenNote();
        _viewport.EmojiRigPicked += (_, _) => RefreshTweenNote();
        _graph.SelectionChanged += (_, _) => RefreshTweenNote();
    }

    private void OpenTween()
    {
        if (_tweenWindow is not null)
        {
            _tweenWindow.Activate();
            return;
        }

        _tweenOvershoot.IsChecked = _settings.TweenOvershoot;
        ApplyTweenRange();

        var head = new DockPanel();
        DockPanel.SetDock(_tweenValue, Dock.Right);
        head.Children.Add(_tweenValue);
        head.Children.Add(_tweenSlider);

        var ends = new DockPanel { Margin = new Thickness(0, -4, 46, 0) };
        var right = new TextBlock { Text = "뒤 키", FontSize = 10, Opacity = 0.6 };
        DockPanel.SetDock(right, Dock.Right);
        ends.Children.Add(right);
        ends.Children.Add(new TextBlock { Text = "앞 키", FontSize = 10, Opacity = 0.6 });

        var panel = new StackPanel { Margin = new Thickness(12, 10), Spacing = 6, Children = { _tweenNote, head, ends, _tweenButtons, _tweenOvershoot, _tweenWhole } };
        var window = new Window
        {
            Title = "트윈머신",
            Width = 380,
            Height = 216,
            CanResize = false,
            ShowInTaskbar = false,
            Content = panel,
        };

        if (_settings.TweenWindowX is { } x && _settings.TweenWindowY is { } y)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(x, y);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        // 창에 손이 가 있어도 본 창의 단축키(S · W · E · R · 재생 …)가 먹는다.
        window.AddHandler(KeyDownEvent, (sender, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Tab or Key.Home or Key.End or Key.Space or Key.Enter))
            {
                OnShellKeyDown(sender, e);
            }
        }, RoutingStrategies.Tunnel);

        // 고른 컨트롤러는 여러 길로 바뀐다 — 창으로 손이 올 때 한 줄을 다시 읽는다(놓을 때는 늘 그때의 대상을 다시 본다).
        window.Activated += (_, _) => RefreshTweenNote();
        window.PointerEntered += (_, _) => RefreshTweenNote();

        window.Closing += (_, _) =>
        {
            EndTweenDrag();
            (_settings.TweenWindowX, _settings.TweenWindowY) = (window.Position.X, window.Position.Y);
        };
        window.Closed += (_, _) =>
        {
            // 칸들은 창보다 오래 산다 — 다음에 열 때 다시 쓰도록 떼어 둔다.
            head.Children.Clear();
            panel.Children.Clear();
            _tweenWindow = null;

            if (!_closing)
            {
                _settings.TweenWindowOpen = false;
            }

            SaveSettings();
        };

        _tweenWindow = window;
        _settings.TweenWindowOpen = true;
        RefreshTweenNote();

        // 본 창에 딸린 창 — 본 창 위에 늘 떠 있고, 본 창을 내리면 같이 내려간다.
        if (IsVisible)
        {
            window.Show(this);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>오버슈트에 따라 슬라이더의 범위와 값 단추를 다시 놓는다.</summary>
    private void ApplyTweenRange()
    {
        bool overshoot = _tweenOvershoot.IsChecked == true;
        _tweenSetting = true;

        try
        {
            (_tweenSlider.Minimum, _tweenSlider.Maximum) = overshoot ? (-50, 150) : (0, 100);
            _tweenSlider.Value = Math.Clamp(_tweenSlider.Value, _tweenSlider.Minimum, _tweenSlider.Maximum);
        }
        finally
        {
            _tweenSetting = false;
        }

        _tweenValue.Text = $"{_tweenSlider.Value.ToString("0", CultureInfo.InvariantCulture)}%";
        _tweenButtons.Children.Clear();

        foreach (int percent in overshoot ? new[] { -25, 0, 25, 33, 50, 67, 75, 100, 125 } : new[] { 0, 25, 33, 50, 67, 75, 100 })
        {
            int captured = percent;
            Button button = SmallButton(percent.ToString(CultureInfo.InvariantCulture), (_, _) => ApplyTweenOnce(captured));
            button.MinWidth = 34;
            ToolTip.SetTip(button, percent switch
            {
                0 => "앞 키의 값 그대로",
                100 => "뒤 키의 값 그대로",
                50 => "한가운데",
                < 0 => "앞 키보다 더 뒤로 — 예비 동작",
                > 100 => "뒤 키를 지나서 — 오버슈트",
                _ => $"앞 키에서 뒤 키 쪽으로 {percent}%",
            });
            _tweenButtons.Children.Add(button);
        }
    }

    /// <summary>트윈이 놓일 곳 — 어느 모션(이모지면 그 타임라인)의 어느 곡선들에, 어느 시각에. 대상을 한 줄로 적은 글도.</summary>
    private sealed record TweenTarget(MotionAsset Motion, IReadOnlyList<CurveRef> Curves, float Time, string Label, bool Picked);

    /// <summary>
    /// 지금 트윈이 닿는 것. 없으면 null과 그 까닭.
    ///   이모지 — 고른 그림들(없으면 이모지 전체)의 자리 · 크기 · 회전 · 투명도 가운데 키가 있는 곡선.
    ///   캐릭터 모션 — 그래프가 보는 모션에서, 고른 컨트롤러의 키가 있는 곡선(고른 것이 없으면 전부. 그림 바꾸기는 뺀다).
    /// 그래프에서 곡선을 골랐으면 그 가운데 그것만.
    /// </summary>
    private TweenTarget? TweenTargetNow(out string why)
    {
        why = "";
        IEnumerable<CurveRef> CurvesOf(MotionAsset motion, Func<ChannelAsset, bool> take) => motion.Channels
            .Where(take)
            .SelectMany(c => new[] { Axis.X, Axis.Y, Axis.Z }.Where(a => c.Get(a) is { Keys.Count: > 0 }).Select(a => new CurveRef(c.Node, c.Kind, a)));

        TweenTarget Make(MotionAsset motion, List<CurveRef> curves, double seconds, string label)
        {
            List<CurveRef> picked = curves.Where(_graph.SelectedCurves.Contains).ToList();
            float time = (float)TimeSnap.Snap(Math.Clamp(seconds, 0, motion.Duration), 1.0 / TimeSnap.FramesPerSecond);
            return new TweenTarget(motion, picked.Count > 0 ? picked : curves, time, label, picked.Count > 0);
        }

        if (_workMode == WorkMode.Emoji)
        {
            if (CurrentEmoji is not { } emoji || _session.EmojiFrame is not { } frame)
            {
                why = "이모지를 고른다.";
                return null;
            }

            if (_viewport.EmojiLife)
            {
                why = "'게임처럼 재생'을 끄고 쓴다 — 키는 타임라인의 프레임에 놓인다.";
                return null;
            }

            IReadOnlyList<string> targets = EmojiKeyTargets();
            var nodes = targets.Select(frame.TargetNode).ToHashSet(StringComparer.Ordinal);
            return Make(
                emoji.Timeline,
                CurvesOf(emoji.Timeline, c => nodes.Contains(c.Node) && TweenKinds.Contains(c.Kind)).ToList(),
                _viewport.EmojiTimelineTime ?? 0,
                string.Join(" · ", targets.Select(t => t.Length == 0 ? EmojiWords.WholeEmoji : t)));
        }

        if (_graph.MotionName is not { } name || EmojiAsset.IsTimelineName(name, out _) || _session.Editor.Document.Find(name) is not { } bound)
        {
            why = "모션을 고른다 — 클립을 보는 중에는 쓰지 않는다(키는 모션에 놓인다).";
            return null;
        }

        // 마야처럼 고른 컨트롤러를 따라간다 — 뷰포트 · 하이어아키 · 아웃라이너에서 고른 컨트롤러(표가 없는 리그면 고른 노드)의 곡선만.
        // "모션 전체에"를 켰거나 고른 것이 없으면 모션의 곡선 전부(포즈 전체).
        List<CurveRef> all = CurvesOf(bound, c => c.Kind != ChannelKind.Image).ToList();

        if (_tweenWhole.IsChecked == true)
        {
            return Make(bound, all, _viewport.Time, $"모션 {name} · 전체");
        }

        if (SelectedControllerOf(bound) is { } controller)
        {
            var own = controller.Axes.SelectMany(controller.CurvesOf).ToHashSet();

            if (controller.DepthYCurve is { } depthY)
            {
                own.Add(depthY);
            }

            return Make(bound, all.Where(own.Contains).ToList(), _viewport.Time, $"모션 {name} · {controller.Label}");
        }

        if (_viewport.SelectedNode is { } node)
        {
            return Make(bound, all.Where(c => c.Node == node).ToList(), _viewport.Time, $"모션 {name} · {node}");
        }

        return Make(bound, all, _viewport.Time, $"모션 {name} · 전체");
    }

    /// <summary>대상의 한 줄 — 무엇에, 어느 키와 어느 키 사이에 놓이는가. 놓일 곳이 없으면 그 까닭.</summary>
    private void RefreshTweenNote()
    {
        if (_tweenWindow is null)
        {
            return;
        }

        bool ready = false;

        if (TweenTargetNow(out string why) is not { } target)
        {
            _tweenNote.Text = why;
        }
        else
        {
            var between = new List<(double Before, double After)>();

            foreach (CurveRef curve in target.Curves)
            {
                if (curve.Resolve(target.Motion) is { } resolved && TweenMath.TryNeighbors(resolved.Keys, target.Time, out KeyAsset? before, out KeyAsset? after))
                {
                    between.Add((before!.T, after!.T));
                }
            }

            static string Frame(double seconds) => Math.Round(TimeSnap.ToFrames(seconds)).ToString(CultureInfo.InvariantCulture);
            string scope = target.Picked ? "그래프에서 고른 곡선 " : "곡선 ";
            ready = between.Count > 0;
            _tweenNote.Text = ready
                ? $"{target.Label}  |  {Frame(target.Time)}fr에 — 앞 키 {Frame(between.Max(b => b.Before))}fr ↔ 뒤 키 {Frame(between.Min(b => b.After))}fr  |  {scope}{between.Count}개"
                : $"{target.Label}  |  {Frame(target.Time)}fr의 앞과 뒤에 키가 있는 곡선이 없다 — 두 키 사이의 프레임으로 간다";
        }

        _tweenSlider.IsEnabled = ready;
        _tweenButtons.IsEnabled = ready;
        _tweenWhole.IsVisible = _workMode != WorkMode.Emoji;
    }

    /// <summary>트윈을 놓는다 — 캐릭터 모션에서 키 없는 포즈가 있으면 먼저 버린다(같은 칸에 붙는다).</summary>
    private void TweenAt(TweenTarget target, double percent)
    {
        if (_workMode != WorkMode.Emoji && _viewport.HasPending)
        {
            _viewport.DiscardPending();
        }

        _session.Editor.TweenKeys(target.Motion.Name, target.Curves, target.Time, percent / 100.0, DefaultTangent);
    }

    /// <summary>슬라이더를 끄는 중 — 끄는 조작을 열고(처음에만) 그 값으로 키를 놓아 보인다.</summary>
    private void PreviewTween(double percent)
    {
        if (TweenTargetNow(out _) is not { Curves.Count: > 0 } target)
        {
            return;
        }

        MotionEditor editor = _session.Editor;

        Guard("트윈", () =>
        {
            if (!editor.InGesture)
            {
                editor.BeginGesture(MotionChangeKind.Keys);
            }

            TweenAt(target, percent);
            editor.GestureChanged();
        });
    }

    /// <summary>슬라이더를 놓았다 — 끈 것이 한 칸이 된다.</summary>
    private void EndTweenDrag()
    {
        if (_session.Editor.InGesture)
        {
            _session.Editor.EndGesture();
            RefreshTweenNote();
        }
    }

    /// <summary>값 단추 — 그 값으로 바로 한 칸. 슬라이더도 그 자리로 간다.</summary>
    private void ApplyTweenOnce(double percent)
    {
        EndTweenDrag();
        _tweenSetting = true;

        try
        {
            _tweenSlider.Value = Math.Clamp(percent, _tweenSlider.Minimum, _tweenSlider.Maximum);
        }
        finally
        {
            _tweenSetting = false;
        }

        if (TweenTargetNow(out _) is { Curves.Count: > 0 } target)
        {
            Guard("트윈", () => _session.Editor.Batch(MotionChangeKind.Keys, () => TweenAt(target, percent)));
            RefreshTweenNote();
        }
    }
}
