using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Ked.Presentation.Core;
using Mt.App.Services;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Presets;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Settings;
using Mt.Authoring.Validation;
using Avalonia.Threading;

namespace Mt.App;

/// <summary>
/// 셸 — 왼쪽 모션·채널 목록, 가운데 그래프, 오른쪽 인스펙터, 아래 상태·문제.
/// 모든 편집은 <see cref="MotionEditor"/> 명령이고, 화면은 <see cref="MotionEditor.Changed"/>를 듣고 다시 읽는다.
/// 파일은 <see cref="DocumentSession"/>을 지난다.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] EndTokens = { "복원 (Restore)", "유지 (Commit)" }; // 차례 = MotionEnd
    private static readonly string[] ModeTokens = { "상대값 (Relative)", "절대값 (Absolute)" }; // 차례 = MotionChannelMode

    private readonly DocumentSession _session = new();
    private readonly GraphEditorView _graph = new();
    private readonly DopeSheetView _dope = new();
    private readonly ViewportView _viewport = new();

    /// <summary>그래프와 도프시트가 같은 선택을 든다 — 한 모델, 두 뷰.</summary>
    private readonly KeySelection _selection = new();

    /// <summary>잠금·뮤트 — 그래프·도프시트·뷰포트가 같은 객체를 든다.</summary>
    private readonly CurveFlags _flags = new();
    private readonly KeyClipboardSlot _clipboard = new();
    private readonly ToolSettings _settings;
    private readonly string _settingsPath;
    private readonly DispatcherTimer _autoSaveTimer = new();
    private readonly Button _recentButton = new() { Content = "최근…", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _snapStepCombo = new() { ItemsSource = new[] { "1/24초 (1프레임)", "1/60초", "1/120초" }, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _autoSaveCombo = new() { ItemsSource = new[] { "끔", "1분", "5분", "10분" }, SelectedIndex = 2, FontSize = 11 };
    private static readonly double[] SnapSteps = { 1.0 / 24.0, 1.0 / 60.0, 1.0 / 120.0 }; // 차례 = _snapStepCombo 항목
    private static readonly int[] AutoSaveMinutes = { 0, 1, 5, 10 };

    // 트랜스포트
    private readonly Button _playButton = new() { Content = "▶", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _playMode = new() { ItemsSource = new[] { "한 번", "루프", "왕복" }, SelectedIndex = 1, FontSize = 11 };
    private readonly ComboBox _playSpeed = new() { ItemsSource = new[] { "×0.25", "×0.5", "×1", "×2" }, SelectedIndex = 2, FontSize = 11 };
    private readonly ComboBox _playStep = new()
    {
        ItemsSource = new[]
        {
            PlayStepItem("연속", "실제 경과 시간에 해당하는 자세를 표시합니다."),
            PlayStepItem($"프레임 맞춤 ({TimeSnap.FramesPerSecond:0} FPS)", "표시 시각을 가장 가까운 프레임에 맞춥니다. 느린 환경에서는 중간 프레임을 건너뛸 수 있습니다."),
        },
        SelectedIndex = 0,
        FontSize = 11,
    };
    private readonly TextBox _rangeStart = new() { Text = "0", Width = 56, FontSize = 11 };
    private readonly TextBox _rangeEnd = new() { Text = "1", Width = 56, FontSize = 11 };
    private readonly TextBox _animEnd = new() { Width = 56, FontSize = 11 };
    private readonly TimeSliderView _timeSlider = new();
    private readonly RangeSliderView _rangeSlider = new();
    private readonly ToggleButton _autoKeyToggle = new() { Content = "● Auto Key", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
    private static readonly IBrush AutoKeyOnBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x3C, 0x3C));
    private bool _syncingTime;

    // 채널 박스(마야) — 선택 노드의 Translate/Rotate/Scale. 칸에 값을 넣으면 매니퓰레이터를 놓은 것과 같다.
    private readonly TextBlock _channelBoxTitle = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Avalonia.Thickness(0, 2) };
    private readonly StackPanel _channelBoxPanel = new() { Spacing = 1 };
    private readonly Dictionary<(ChannelKind Kind, Axis Axis), (Border Row, TextBox Box)> _channelBoxRows = new();
    private readonly Dictionary<(string Id, Axis Axis), (Border Row, TextBox Box, Slider? Slider)> _controllerRows = new();
    private string? _channelBoxLayout;
    private static readonly (ChannelKind Kind, Axis Axis, string Label)[] ChannelBoxComponents =
    {
        (ChannelKind.Position, Axis.X, "Translate X"),
        (ChannelKind.Position, Axis.Y, "Translate Y"),
        (ChannelKind.Euler, Axis.X, "Rotate X"),
        (ChannelKind.Euler, Axis.Y, "Rotate Y"),
        (ChannelKind.Euler, Axis.Z, "Rotate Z"),
        (ChannelKind.Scale, Axis.X, "Scale X"),
        (ChannelKind.Scale, Axis.Y, "Scale Y"),
    };
    private static readonly IBrush ChannelBoxKeyedHere = new SolidColorBrush(Color.FromArgb(150, 0xC8, 0x3C, 0x3C)); // 이 시각에 키(마야 빨강)
    private static readonly IBrush ChannelBoxKeyed = new SolidColorBrush(Color.FromArgb(110, 0xB4, 0x5A, 0x5A)); // 어딘가에 키(마야 연한 빨강)
    private static readonly IBrush ChannelBoxPending = new SolidColorBrush(Color.FromArgb(120, 0xD0, 0x8A, 0x28)); // 키 없는 포즈
    /// <summary>선택 키의 노드 따라가기 — 하이어아키·아웃라이너에서 고르면 꺼지고, 모션을 바꾸면 켜진다(설정 아님).</summary>
    private bool _followSelection = true;
    private readonly TabControl _inspectorTabs = new() { Padding = new Avalonia.Thickness(0) };
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private Window? _settingsWindow;
    private bool _closing; // 본 창이 닫히는 중 — 딸린 창이 닫혀도 "열려 있었다"를 지우지 않는다
    private Window? _shortcutsWindow;
    private readonly List<Panel> _settingsRows = new();
    private readonly TextBlock _pendingText = new() { FontSize = 11, Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
    private Button? _regionButton;
    private Button? _bufferButton;
    private readonly CheckBox _valueSnap = new() { Content = "값 스냅", FontSize = 11 };
    private readonly CheckBox _stackGraph = new() { Content = "종류별 칸", FontSize = 11 };

    /// <summary>그래프를 종류별 칸에 나누는가 — 이모지의 타임라인을 볼 때, 설정이 켜져 있으면.</summary>
    private void SyncGraphStacked()
        => _graph.Stacked = _settings.StackEmojiGraph && _graph.MotionName is { } bound && EmojiAsset.IsTimelineName(bound, out _);

    /// <summary>테스트 손잡이 — 그래프 툴바의 "종류별 칸".</summary>
    internal void SetStackGraphFromForm(bool on) => _stackGraph.IsChecked = on;
    private static readonly string[] InfinityTokens = { "상수", "사이클", "사이클+오프셋", "왕복", "직선" };
    private readonly ComboBox _preInfinity = new() { ItemsSource = InfinityTokens, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _postInfinity = new() { ItemsSource = InfinityTokens, SelectedIndex = 0, FontSize = 11 };
    private static readonly TangentMode[] NewKeyTangentChoices = { TangentMode.Auto, TangentMode.Spline, TangentMode.Plateau, TangentMode.Flat, TangentMode.Linear };
    private readonly ComboBox _newKeyTangent = new() { ItemsSource = new[] { "auto", "spline", "plateau", "flat", "linear" }, SelectedIndex = 0, FontSize = 11 };

    /// <summary>새 키(뷰포트 키·성분 토글)의 탄젠트 — 설정. 곡선 위에 끼우는 I·더블클릭은 모양을 지키는 free다.</summary>
    private TangentMode DefaultTangent => NewKeyTangentChoices[Math.Max(0, _newKeyTangent.SelectedIndex)];

    // 왼쪽 — 씬
    // 높이 제한 없음 — 왼쪽 패널이 스크롤한다. 오브젝트가 없는 가지는 기본 접힘이라 길지 않다.
    private readonly TreeView _hierarchy = new() { MinHeight = 160, FontSize = 11 };

    /// <summary>펼침 상태 기억 — 기본이 펼침인 항목(폴더·노드)의 접힘, 기본이 접힘인 항목(오브젝트)의 펼침.</summary>
    private readonly HashSet<string> _collapsedHierarchy = new();
    private readonly HashSet<string> _expandedHierarchy = new();
    private readonly Dictionary<TreeViewItem, TreeViewItem?> _hierarchyParents = new();
    private readonly ContextMenu _hierarchyMenu = new();
    private object? _menuTarget;
    private static readonly IBrush ActingNodeBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xE0, 0xFF));
    private static readonly IBrush PlacementNodeBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly IBrush ForbiddenNodeBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
    private readonly ComboBox _exportScope = new() { ItemsSource = new[] { "전체", "이 rigKind만", "이 오브젝트가 입는 모션만" }, SelectedIndex = 0, FontSize = 11 };
    private readonly TextBox _camZoom = new() { FontSize = 11, Width = 56 };
    private readonly TextBox _camPanX = new() { FontSize = 11, Width = 56 };
    private readonly TextBox _camPanY = new() { FontSize = 11, Width = 56 };

    // 오른쪽 — 오브젝트(채널 박스)
    private readonly TextBox _objX = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objY = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objRot = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objSx = new() { FontSize = 11, Width = 64 };
    private readonly TextBox _objSy = new() { FontSize = 11, Width = 64 };
    private readonly ComboBox _objLayer = new() { ItemsSource = SceneLayers.Order, FontSize = 11 };
    private readonly ComboBox _objStage = new() { ItemsSource = StageNodes.Stages, FontSize = 11 };
    private readonly ComboBox _objPlace = new() { FontSize = 11, MinWidth = 90 };
    private readonly ComboBox _objPlaceFocus = new() { ItemsSource = ScenePreview.FocusPresets, FontSize = 11, MinWidth = 90 };
    private const string NoPlaceToken = "(place 없음)";

    private readonly CheckBox _objVisible = new() { Content = "보이기", FontSize = 11 };
    private readonly TextBox _objId = new() { FontSize = 11, Width = 90 };
    private readonly TextBox _objCharacter = new() { FontSize = 11, Width = 90, PlaceholderText = "(기본값)" };

    /// <summary>테스트 손잡이 — "씬" 탭의 배역 키 칸.</summary>
    internal TextBox ObjectCharacterBox => _objCharacter;
    private readonly TextBlock _objSummary = new() { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly Expander _placementExpander = new() { Header = "시작 배치", FontSize = 11, IsExpanded = false };
    private readonly Expander _cameraExpander = new() { Header = "카메라(샷)", FontSize = 11, IsExpanded = false };

    // 왼쪽
    private readonly ListBox _motionList = new() { MinHeight = 120 };
    private readonly StackPanel _channelRows = new() { Spacing = 0 };
    private readonly List<string> _outlinerOrder = new();
    private Grid? _centerGrid;
    private readonly TextBox _outlinerSearch = new() { PlaceholderText = "검색…", FontSize = 11, Margin = new Avalonia.Thickness(2) };
    private static readonly IBrush OutlinerBackground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    private static readonly IBrush OutlinerRowSelected = new SolidColorBrush(Color.FromArgb(170, 0x4B, 0x6E, 0xAF));
    private readonly ComboBox _timeDisplayCombo = new() { ItemsSource = new[] { "프레임 (24fps)", "초" }, SelectedIndex = 0, FontSize = 11 };

    /// <summary>시간 글자 단위(설정) — 마야처럼 프레임이 기본.</summary>
    private TimeDisplay Display => _settings.TimeDisplay;

    // 오른쪽 — 모션
    private readonly TextBox _motionName = new() { FontSize = 11 };
    private readonly TextBox _motionDuration = new() { FontSize = 11 };
    private readonly ComboBox _motionEnd = new() { ItemsSource = EndTokens, FontSize = 11 };
    private readonly ComboBox _motionDefaultMode = new() { ItemsSource = ModeTokens, FontSize = 11 };

    // 오른쪽 — 키
    private readonly TextBox _keyValue = new() { FontSize = 11 };

    // 오른쪽 — 보기
    private readonly CheckBox _snap = new() { Content = "시간 스냅", IsChecked = true, FontSize = 11 };
    private readonly CheckBox _showRigNodes = new() { Content = "리그 노드 보기 (컨트롤러 대신 덤프 노드)", FontSize = 11 };
    private readonly CheckBox _showStageSkeleton = new() { Content = "무대 뼈대 보기 (Shot·Pan·Zoom·Mask·Framing… 전부)", FontSize = 11 };
    private readonly ComboBox _presetCombo = new() { ItemsSource = MotionPresets.All.Select(p => p.Name).ToArray(), SelectedIndex = 0, FontSize = 11, MinWidth = 120 };
    private readonly TextBox _currentTime = new() { Text = "0", FontSize = 11, Width = 72 };

    private int _refreshDepth;

    private bool _refreshing => _refreshDepth > 0;

    public MainWindow() : this(ToolSettings.DefaultPath())
    {
        AsksBeforeDiscard = true;
    }

    /// <param name="settingsPath">설정 파일 자리 — 테스트가 임시 경로를 준다.</param>
    internal MainWindow(string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = ToolSettings.Load(settingsPath);
        InitializeComponent();
        BuildToolbar();
        BuildLeft();
        BuildRight();
        _graph.Selection = _selection;
        _dope.Selection = _selection;
        _viewport.Selection = _selection;
        _graph.Flags = _flags;
        _dope.Flags = _flags;
        _viewport.Flags = _flags;
        _graph.Clipboard = _clipboard;
        _dope.Clipboard = _clipboard;
        _timeSlider.Viewport = _viewport;
        _timeSlider.Selection = _selection;
        _timeSlider.Clipboard = _clipboard;
        _rangeSlider.Viewport = _viewport;
        _flags.Changed += (_, _) => RefreshChannels();
        GraphHost.Child = BuildCenter();
        ApplyLayout();

        _session.DocumentReplaced += (_, _) =>
        {
            CloseAutoSavePrompt(); // 다른 문서가 됐다 — 앞 문서의 물음은 거둔다
            OnDocumentReplaced();
        };
        _session.StatusChanged += (_, _) =>
        {
            StatusText.Text = _session.Status;
            RefreshTitle(); // 저장 · 열기 · 새로는 상태 줄을 바꾼다
        };
        _session.AssetsChanged += (_, _) => RefreshAssets();
        _selection.Changed += (_, _) =>
        {
            RefreshKeyInspector();
            RefreshSelectedNode();
        };
        _graph.CurrentTimeChanged += (_, _) =>
        {
            _currentTime.Text = TimeText.Format(_graph.CurrentTime, Display);
            SyncTime(() =>
            {
                // 그래프·도프 시트는 모션의 시각, 뷰포트는 시계의 시각 — 클립 안에서 블록의 모션을 고칠 때는 블록의 시작만큼 어긋난다.
                ScrubViewportFromGraph(_graph.CurrentTime);
                _dope.CurrentTime = _graph.CurrentTime;
            });
        };
        _dope.CurrentTimeChanged += (_, _) => SyncTime(() =>
        {
            _graph.CurrentTime = _dope.CurrentTime;
            ScrubViewportFromGraph(_dope.CurrentTime);
        });
        _graph.CurvesChanged += (_, _) =>
        {
            RefreshChannels();
            RefreshSelectedNode();
        };
        _viewport.TimeChanged += (_, _) =>
        {
            // 반복하는 블록은 바퀴가 넘어갈 때 시각 차가 바뀐다 — 그래프의 시각이 모션 길이 안에서 다시 돈다.
            if (SelectedClip is not null && _graph.MotionName is not null)
            {
                _blockOffset = _viewport.ClipBlockTimeOffset;
            }

            // 이모지의 수명 보기 — 그래프 · 도프 시트는 지금 클립의 프레임이 잘려 나온 타임라인의 자리를 따라간다.
            double graphTime = _viewport.EmojiName is not null && _viewport.EmojiLife
                ? _viewport.EmojiTimelineTime ?? _graph.CurrentTime
                : _viewport.Time - _blockOffset;

            SyncTime(() =>
            {
                _graph.CurrentTime = graphTime;
                _dope.CurrentTime = graphTime;
            });
            RefreshChannelBox();
        };
        _viewport.PlayStateChanged += (_, _) => _playButton.Content = _viewport.IsPlaying ? "❚❚" : "▶";

        OnDocumentReplaced();
        ApplySettings();

        if (_settings.LoadProblem is { } problem)
        {
            _session.SetStatus(problem);
        }

        _autoSaveTimer.Tick += (_, _) => Guard("자동 저장", () => _session.AutoSave());
        Closing += (_, e) =>
        {
            // 저장하지 않은 것이 있으면 닫기를 멈추고 묻는다.
            if (HoldCloseForUnsaved())
            {
                e.Cancel = true;
                return;
            }

            // 딸린 창(트윈머신)을 먼저 닫는다 — 열려 있었다는 것은 설정에 남는다(다음에 다시 뜬다).
            _closing = true;
            _tweenWindow?.Close();
            SaveSettings();
        };
        Opened += (_, _) =>
        {
            if (_settings.TweenWindowOpen)
            {
                OpenTween();
            }
        };
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    internal ToolSettings Settings => _settings;

    internal DocumentSession Session => _session;

    internal GraphEditorView Graph => _graph;

    internal ViewportView Viewport => _viewport;

    internal TimeSliderView TimeSlider => _timeSlider;

    internal RangeSliderView RangeSlider => _rangeSlider;

    internal DopeSheetView Dope => _dope;

    internal KeySelection SharedSelection => _selection;

    internal CurveFlags SharedFlags => _flags;

    internal bool AutoKeyChecked
    {
        get => _autoKeyToggle.IsChecked == true;
        set => _autoKeyToggle.IsChecked = value;
    }

    internal string? SelectedMotion => _motionList.SelectedItem as string;

    /// <summary>테스트 손잡이 — 목록에서 모션을 고른 것과 같다.</summary>
    internal void SelectMotion(string name) => _motionList.SelectedItem = name;

    /// <summary>헤드리스 테스트가 폼을 채우는 손잡이 — 화면과 같은 명령 경로를 지난다.</summary>
    internal void AddMotionFromForm(string name, string rig, string duration, MotionEnd end)
    {
        if (!TryFloat(duration, out float seconds))
        {
            throw new InvalidOperationException("길이는 초 단위 숫자다.");
        }

        AddMotion(name, rig, seconds, end);
    }

    /// <summary>테스트 손잡이 — 채널을 바로 더한다(UI에는 "채널 추가"가 없다: 채널 박스·매니퓰레이터가 첫 키에서 만든다).</summary>
    internal void AddChannelFromForm(string node, ChannelKind kind)
    {
        MotionAsset motion = _graph.Motion ?? throw new InvalidOperationException("먼저 모션을 고른다.");
        _session.Editor.AddChannel(motion.Name, node, kind);
    }

    /// <summary>인스펙터의 값 칸에 적고 Enter 한 것과 같다.</summary>
    internal void CommitKeyValueFromInspector(string text)
    {
        _keyValue.Text = text;
        WithSingleKey((key, k) => { if (TryFloat(text, out float v) && v != k.V) UpdateKey(key, k, value: v); });
    }

    internal IReadOnlyList<MotionProblem> ProblemsShown => _session.Problems;

    // ── 구성 ──────────────────────────────────────────────────────────

    /// <summary>가운데: 위 뷰포트, 트랜스포트 줄, 아래 그래프. 사이에 스플리터.</summary>
    private Control BuildCenter()
    {
        // 위: 뷰포트 + (이모지 모드의 수명의 띠) + 트랜스포트 + 레인지 줄(한 덩어리, 아래 줄들은 늘 제 높이) · 스플리터 · 아래: 그래프/도프 시트.
        // 스플리터는 덩어리와 탭의 별 높이만 바꾼다 — 레인지 줄이 늘어나지 않는다.
        var grid = new Grid { RowDefinitions = new RowDefinitions("2*,4,3*") };
        grid.RowDefinitions[0].MinHeight = 200;
        grid.RowDefinitions[2].MinHeight = 160;
        _centerGrid = grid;

        var scene = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto") };
        Grid.SetRow(scene, 0);
        grid.Children.Add(scene);

        Grid.SetRow(_viewport, 0);
        scene.Children.Add(_viewport);

        // 뷰포트 위에 도구 띠를 얹는다(선택 · 이동 · 회전 · 크기 · Auto Key).
        Control tools = BuildToolStrip();
        Grid.SetRow(tools, 0);
        scene.Children.Add(tools);

        _playButton.Click += (_, _) => _viewport.TogglePlay();
        _playMode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playMode.SelectedIndex >= 0)
            {
                _viewport.Mode = (PlaybackMode)_playMode.SelectedIndex;
            }
        };
        _playStep.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playStep.SelectedIndex >= 0)
            {
                _settings.PlayWholeFrames = _playStep.SelectedIndex == 1;
                PushSettingsToViews();
            }
        };
        ToolTip.SetTip(_playStep, "재생 표시 — 둘 다 실제 시간으로 재생한다. 연속: 실제 경과 시간에 해당하는 자세 · 프레임 맞춤: 표시 시각을 가장 가까운 프레임에 맞춘다");
        _playSpeed.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playSpeed.SelectedIndex >= 0)
            {
                _viewport.Speed = new[] { 0.25, 0.5, 1, 2 }[_playSpeed.SelectedIndex];
            }
        };
        OnCommit(_rangeStart, text => Guard("재생 범위", () => { if (TimeText.TryParse(text, Display, out double v)) _viewport.SetRange(v, _viewport.Clock.RangeEnd); }));
        OnCommit(_rangeEnd, text => Guard("재생 범위", () => { if (TimeText.TryParse(text, Display, out double v)) _viewport.SetRange(_viewport.Clock.RangeStart, v); }));
        _viewport.RangeChanged += (_, _) => RefreshTransport();
        // 마야 트랜스포트: [현재 프레임] ⏮ |◀ ◁ ◀ ▶ ▷ ▶| ⏭  — 타임 슬라이더가 나머지를 채운다.
        _playButton.Content = "▶";
        _playButton.FontSize = 10;
        _playButton.Padding = new Avalonia.Thickness(6, 2);
        ToolTip.SetTip(_playButton, "재생 / 일시정지 (Alt+Q) · 처음부터 재생 (Alt+`)");
        Button playBack = SmallButton("◀", (_, _) =>
        {
            if (_viewport.IsPlaying)
            {
                _viewport.Pause();
            }
            else
            {
                _viewport.PlayBackward();
            }
        });
        ToolTip.SetTip(playBack, "거꾸로 재생");
        OnCommit(_animEnd, text => WithMotion(m =>
        {
            if (TimeText.TryParse(text, Display, out double v) && Math.Abs(v - m.Duration) > 1e-6)
            {
                _session.Editor.SetDuration(m.Name, (float)v);
            }
        }));
        ToolTip.SetTip(_currentTime, "현재 프레임 — 숫자를 넣으면 그 시각으로(초는 0.5s처럼)");
        ToolTip.SetTip(_rangeStart, "재생 범위 시작");
        ToolTip.SetTip(_rangeEnd, "재생 범위 끝");
        ToolTip.SetTip(_animEnd, "모션 길이(애니메이션 끝) — 바꾸면 길이가 바뀐다");
        (string Label, string Tip, EventHandler<RoutedEventArgs> Click)[] transportSpec =
        {
            ("⏮", "범위 처음으로 (`)", (_, _) => _viewport.Stop()),
            ("|◀", "이전 키 (1 · ,)", (_, _) => _viewport.JumpToPreviousKey()),
            ("◁", "이전 프레임 (Alt+1 · ←)", (_, _) => StepFrame(-1)),
        };
        var transportButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
        _currentTime.Width = 56;
        transportButtons.Children.Add(_currentTime);

        foreach ((string label, string tip, EventHandler<RoutedEventArgs> click) in transportSpec)
        {
            Button b = SmallButton(label, click);
            ToolTip.SetTip(b, tip);
            transportButtons.Children.Add(b);
        }

        transportButtons.Children.Add(playBack);
        transportButtons.Children.Add(_playButton);
        (string Label, string Tip, EventHandler<RoutedEventArgs> Click)[] transportTail =
        {
            ("▷", "다음 프레임 (Alt+2 · →)", (_, _) => StepFrame(+1)),
            ("▶|", "다음 키 (2 · .)", (_, _) => _viewport.JumpToNextKey()),
            ("⏭", "범위 끝으로", (_, _) => { _viewport.Pause(); _viewport.ScrubTo(_viewport.Clock.RangeEnd); }),
        };

        foreach ((string label, string tip, EventHandler<RoutedEventArgs> click) in transportTail)
        {
            Button b = SmallButton(label, click);
            ToolTip.SetTip(b, tip);
            transportButtons.Children.Add(b);
        }

        var transportRow = new DockPanel { Margin = new Avalonia.Thickness(4, 2, 4, 0) };
        DockPanel.SetDock(transportButtons, Dock.Right);
        transportRow.Children.Add(transportButtons);
        transportRow.Children.Add(_timeSlider);
        Grid.SetRow(transportRow, 2);
        scene.Children.Add(transportRow);

        // 이모지 모드 — 뷰포트와 타임 슬라이더 사이에 수명의 띠.
        Control life = BuildLifeRows(transportButtons);
        Grid.SetRow(life, 1);
        scene.Children.Add(life);

        // 레인지 슬라이더 줄: [0][범위 시작] ═바═ [범위 끝][길이] · 모드 · 속도 · fps · Auto Key.
        var rangeLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            Children = { new TextBlock { Text = "0", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Width = 20, TextAlignment = TextAlignment.Center }, _rangeStart },
        };
        var rangeRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            Children =
            {
                _rangeEnd,
                _animEnd,
                _playMode,
                _playSpeed,
                _playStep,
                new TextBlock { Text = "24 fps", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 },
                _autoKeyToggle,
            },
        };
        var rangeRow = new DockPanel { Margin = new Avalonia.Thickness(4, 2, 4, 2) };
        DockPanel.SetDock(rangeLeft, Dock.Left);
        DockPanel.SetDock(rangeRight, Dock.Right);
        rangeRow.Children.Add(rangeLeft);
        rangeRow.Children.Add(rangeRight);
        rangeRow.Children.Add(_rangeSlider);
        Grid.SetRow(rangeRow, 3);
        scene.Children.Add(rangeRow);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent };
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);

        _editorTabs.ItemsSource = new[]
        {
            new TabItem { Header = "그래프", Content = BuildGraphTab(), FontSize = 12 },
            new TabItem { Header = "도프 시트", Content = _dope, FontSize = 12 },
        };

        // 타임라인은 탭이 아니라 그래프·도프 시트 **위**에 선다(클립을 고른 동안만) — 블록을 고르면 바로 아래에서 그 모션의 커브를 고친다.
        // 둘 사이의 스플리터를 끌면 타임라인의 높이가 바뀌고 설정에 남는다. 트랙이 높이보다 많으면 타임라인 안에서 세로로 굴린다.
        _editorsGrid.Children.Add(BuildTimeline());
        Grid.SetRow(_timelineSplitter, 1);
        _editorsGrid.Children.Add(_timelineSplitter);
        Grid.SetRow(_editorTabs, 2);
        _editorsGrid.Children.Add(_editorTabs);
        Grid.SetRow(_editorsGrid, 2);
        grid.Children.Add(_editorsGrid);
        return grid;
    }

    /// <summary>마야 그래프 에디터 배치: 왼쪽 아웃라이너(검색 · 행 클릭 = 그 커브만) · 위 툴바 · 플롯.</summary>
    private Control BuildGraphTab()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("210,4,*") };

        var outliner = new DockPanel { Background = OutlinerBackground };
        DockPanel.SetDock(_outlinerSearch, Dock.Top);
        outliner.Children.Add(_outlinerSearch);
        outliner.Children.Add(new ScrollViewer { Content = _channelRows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _outlinerSearch.TextChanged += (_, _) => RefreshChannels();
        Grid.SetColumn(outliner, 0);
        grid.Children.Add(outliner);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var right = new DockPanel();
        Control toolbar = BuildGraphToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        right.Children.Add(toolbar);
        right.Children.Add(_graph);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>마야 그래프 에디터 툴바: 툴 · 맞춤 · 탄젠트 · 버퍼 · 스냅/보기 · Stats.</summary>
    private Control BuildGraphToolbar()
    {
        // 툴 버튼은 리전 하나 — 선택·이동·스케일은 Q/W/R, 끼우기 I, 맞춤 A/F(소유자, 2026-10-28). 리전은 토글: 다시 누르면 선택 툴로.
        _regionButton = SmallButton("리전", (_, _) =>
        {
            _graph.Tool = _graph.Tool == GraphTool.Region ? GraphTool.Select : GraphTool.Region;
            RefreshToolButtons();
        });
        _bufferButton = SmallButton("버퍼 찍기", (_, _) =>
        {
            if (_graph.HasBuffer)
            {
                _graph.SwapBuffer();
            }
            else
            {
                _graph.SnapshotBuffer();
            }

            RefreshToolButtons();
        });
        ToolTip.SetTip(_bufferButton, "버퍼 커브 — 없으면 보이는 커브를 흐린 점선으로 찍어 두고(Ctrl+B), 있으면 찍어 둔 것과 지금을 바꾼다(Ctrl+Shift+B). 새로 찍으려면 먼저 지운다");

        OnCommit(_keyValue, text => ApplyValueToSelectedKeys(text));
        ToolTip.SetTip(_snap, "키 배치·이동 및 타임라인 탐색 시, 시간을 지정한 간격의 배수에 맞춥니다. 간격은 설정 창의 \"시간 스냅 간격\"");
        _valueSnap.IsCheckedChanged += (_, _) =>
        {
            _graph.ValueSnapEnabled = _valueSnap.IsChecked == true;
        };
        _preInfinity.SelectionChanged += (_, _) =>
        {
            if (_preInfinity.SelectedIndex >= 0)
            {
                _graph.PreInfinity = (InfinityMode)_preInfinity.SelectedIndex;
                _graph.InvalidateVisual();
            }
        };
        _postInfinity.SelectionChanged += (_, _) =>
        {
            if (_postInfinity.SelectedIndex >= 0)
            {
                _graph.PostInfinity = (InfinityMode)_postInfinity.SelectedIndex;
                _graph.InvalidateVisual();
            }
        };
        ToolTip.SetTip(_preInfinity, "Pre Infinity — 첫 키 앞을 어떻게 이어 보이나(보기만, 산출물의 끝은 restore/commit)");
        ToolTip.SetTip(_postInfinity, "Post Infinity — 마지막 키 뒤를 어떻게 이어 보이나(보기만)");
        ToolTip.SetTip(_valueSnap, "끌 때 키 값을 정수로(마야 Snap Values)");
        ToolTip.SetTip(_regionButton, "리전 툴(토글) — 상자로 고르고, 안을 끌면 이동, 모서리를 끌면 그 축만 반대쪽 기준 스케일. 다시 누르면 선택 툴(Q)로");
        _keyValue.Width = 64;
        ToolTip.SetTip(_keyValue, "선택 키의 값. 여러 키면 +=3 · *=2 식 상대 입력");

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Avalonia.Thickness(2, 2, 2, 4) };

        void Add(params Control[] controls)
        {
            foreach (Control c in controls)
            {
                c.Margin = new Avalonia.Thickness(1, 1);
                c.VerticalAlignment = VerticalAlignment.Center;
                bar.Children.Add(c);
            }
        }

        Add(SmallButton("재생 범위", (_, _) => _graph.FitRange()), Separator());
        Add(SmallButton("auto", (_, _) => _graph.SetTangentMode(TangentMode.Auto)),
            SmallButton("spline", (_, _) => _graph.SetTangentMode(TangentMode.Spline)),
            SmallButton("linear", (_, _) => _graph.SetTangentMode(TangentMode.Linear)),
            SmallButton("flat", (_, _) => _graph.SetTangentMode(TangentMode.Flat)),
            SmallButton("plateau", (_, _) => _graph.SetTangentMode(TangentMode.Plateau)),
            StepButton(),
            SmallButton("끊기", (_, _) => _graph.SetBroken(true)),
            SmallButton("붙이기", (_, _) => _graph.SetBroken(false)),
            Separator());
        Add(_bufferButton, SmallButton("버퍼 지우기", (_, _) =>
        {
            _graph.ClearBuffer();
            RefreshToolButtons();
        }), Separator());
        Add(_regionButton, Separator());
        Add(_snap, _valueSnap, Separator());
        _stackGraph.IsChecked = _settings.StackEmojiGraph;
        _stackGraph.IsCheckedChanged += (_, _) =>
        {
            _settings.StackEmojiGraph = _stackGraph.IsChecked == true;
            SyncGraphStacked();
        RefreshTweenNote();
        };
        ToolTip.SetTip(_stackGraph, "곡선을 종류별 칸에 나눠 그린다 — 자리 · 크기 · 회전 · 투명도 · 그림이 한 칸씩, 칸마다 제 세로 눈금(값이 작은 곡선이 바닥에 깔리지 않는다). 칸의 눈금은 놓인 키에 저절로 맞는다. 끄면 한 눈금에 전부");
        _graph.LaneName = EmojiWords.Kind;
        Add(OnlyIn(WorkMode.Emoji, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _stackGraph, Separator() } }));
        Add(new TextBlock { Text = "무한", FontSize = 10, Opacity = 0.7 }, _preInfinity, _postInfinity, Separator());
        Add(new TextBlock { Text = "값", FontSize = 10, Opacity = 0.7 }, _keyValue);
        ToolTip.SetTip(bar, "툴 Q 선택 · W 이동 · R 스케일 · I 현재 시각에 키 끼우기 · A 전체 맞춤 · F 선택 맞춤 · 탄젠트 Ctrl+1 auto · 2 flat · 3 linear · 4 free · 5 spline · 6 plateau · step(계단 — 이모지의 타임라인만) · 버퍼 Ctrl+B 찍기 / Ctrl+Shift+B 바꾸기");
        RefreshToolButtons();
        return bar;
    }

    private void RefreshTimeTexts()
    {
        _currentTime.Text = TimeText.Format(_graph.CurrentTime, Display);
        RefreshTransport();
        RefreshKeyInspector();
    }

    /// <summary>리전 버튼의 켜짐 표시와 버퍼 버튼의 라벨(찍기/바꾸기) — 툴·버퍼가 단축키로 바뀌어도 따라가도록 키 인스펙터 갱신 때마다 부른다.</summary>
    private void RefreshToolButtons()
    {
        if (_regionButton is not null)
        {
            bool active = _graph.Tool == GraphTool.Region;
            _regionButton.Opacity = active ? 1 : 0.55;
            _regionButton.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
        }

        if (_bufferButton is not null)
        {
            _bufferButton.Content = _graph.HasBuffer ? "버퍼 바꾸기" : "버퍼 찍기";
        }
    }


    private void RefreshPending()
    {
        _pendingText.Text = _viewport.IsHoldingPose
            ? "든 포즈(MMB 스크럽) — S로 이 프레임에 키를 놓거나 Esc·보통 스크럽으로 버린다."
            : _viewport.HasPending ? "키 없는 포즈 — S로 키를 놓거나 Esc로 버린다. 시각을 옮기면 사라진다." : "";
        _pendingText.IsVisible = _viewport.HasPending;
    }

    private void RefreshTransport()
    {
        _refreshDepth++;

        try
        {
            _playMode.SelectedIndex = (int)_viewport.Mode;
            double[] speeds = { 0.25, 0.5, 1, 2 };
            int speedIndex = Array.FindIndex(speeds, v => Math.Abs(v - _viewport.Speed) < 1e-9);
            _playSpeed.SelectedIndex = speedIndex < 0 ? 2 : speedIndex;
            _rangeStart.Text = TimeText.Format(_viewport.Clock.RangeStart, Display);
            _rangeEnd.Text = TimeText.Format(_viewport.Clock.RangeEnd, Display);
            _animEnd.Text = TimeText.Format(_viewport.Clock.Duration, Display);
            _graph.RangeStart = _viewport.Clock.RangeStart;
            _graph.RangeEnd = _viewport.Clock.RangeEnd;
            _graph.InvalidateVisual();
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>그래프와 뷰포트의 현재 시각은 하나다 — 서로를 되부르지 않게 한쪽만 민다.</summary>
    private void SyncTime(Action push)
    {
        if (_syncingTime)
        {
            return;
        }

        _syncingTime = true;

        try
        {
            push();
        }
        finally
        {
            _syncingTime = false;
        }
    }

    private void BindMotion(string? name)
    {
        _followSelection = true; // 모션을 바꾸면 다시 따라간다
        string? graphName = GraphMotionFor(name); // 모션이 없고 이모지를 골랐으면 그 타임라인
        _graph.Bind(_session.Editor, graphName);
        SyncGraphStacked();
        _dope.Controllers = _session.ControllersOf(graphName is null ? null : _session.Editor.Document.Find(graphName)?.Rig);
        _dope.Bind(_session.Editor, graphName);

        // 모션이 없고 클립을 골랐으면 뷰포트(와 그 시계)는 클립의 것이다 — 그래프·도프 시트·타임 슬라이더의 키 틱은 빈다.
        // 이모지를 골랐으면 뷰포트는 그 이모지를 보인다(시계 = 타임라인, 또는 수명).
        if (name is null && SelectedClip is { } clip)
        {
            _viewport.BindClip(_session.Editor, clip);
        }
        else if (name is null && SelectedEmoji is { } emoji)
        {
            _viewport.BindEmoji(_session.Editor, emoji);
            _viewport.EmojiLife = _emojiLifeToggle.IsChecked == true;
        }
        else
        {
            _viewport.Bind(_session.Editor, name);
        }

        _timeSlider.Bind(_session.Editor, graphName);
        RefreshEmojiMarks();
        _timeline.Bind(_session.Editor);
        _blockOffset = 0;
        SyncBlockContext(); // 클립을 보는 중이면 고른 블록의 모션을 그래프에 다시 묶는다
        RefreshSelectedNode();
    }

    /// <summary>뷰포트가 축을 그릴 노드 — 선택한 키의 노드, 없으면 첫 채널의 노드.</summary>
    /// <summary>
    /// 그래프 · 도프 시트에서 시각을 옮겼다 — 뷰포트도 옮긴다(블록 안이면 블록의 시작만큼 더해서).
    /// 이모지의 수명 보기에서는 타임라인 보기로 돌아간다 — 그래프의 시각은 타임라인의 시각이다.
    /// </summary>
    private void ScrubViewportFromGraph(double graphTime)
    {
        if (_viewport.EmojiName is not null && _viewport.EmojiLife)
        {
            _emojiLifeToggle.IsChecked = false;
        }

        _viewport.ScrubTo(graphTime + _blockOffset);
    }

    private Button StepButton()
    {
        Button button = SmallButton("step", (_, _) => _graph.SetTangentMode(TangentMode.Step));
        ToolTip.SetTip(button, "계단 — 다음 키까지 그 값으로 멈춘다(마야 stepped). 이모지의 타임라인에만 쓴다 — 캐릭터 모션(motions/2)에 있으면 검증이 막는다");
        return button;
    }

    private void RefreshSelectedNode()
    {
        MotionAsset? motion = _graph.Motion;
        string? node = _graph.Selection.Count > 0
            ? _graph.Selection.First().Curve.Node
            : motion?.Channels.FirstOrDefault()?.Node;

        if (_followSelection || _viewport.SelectedNode is null)
        {
            _viewport.SelectedNode = node ?? _viewport.SelectedNode;
        }

        _viewport.InvalidateVisual();
        RefreshChannelBox();
        RefreshChannels();
    }

    /// <summary>테스트 손잡이 — 자산(리그·초상·무대 크기)을 뷰포트에 다시 실은 횟수. 키 편집에서는 오르지 않아야 한다.</summary>
    internal int AssetRefreshCount { get; private set; }

    private void RefreshAssets()
    {
        AssetRefreshCount++;
        _viewport.SetRig(_session.Rig);
        _viewport.SetDepth(_session.Depth);
        _viewport.SetFocus(_session.Focus);
        _viewport.EmojiLocators = _session.EmojiLocators;
        _viewport.StageSize = _session.StageSize;
        _viewport.ClearPortraits();

        foreach (SceneObject o in _viewport.SceneObjects)
        {
            if (_session.PortraitPathOf(o) is not { } portraitPath)
            {
                continue;
            }

            try
            {
                _viewport.SetPortrait(o.Id, new Bitmap(portraitPath));
            }
            catch (Exception exception)
            {
                _session.SetStatus($"{o.Id}의 초상을 읽지 못했다: {exception.Message}");
            }
        }

        LoadEmojiImages();
        _dope.Controllers = _session.ControllersOf(_graph.Motion?.Rig);
        RefreshChannelBox();
        RefreshObjects();
        RefreshProblems();
    }

    private async void OnPickPortraitClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("초상 PNG", new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } });

            if (path is not null)
            {
                string objectId = _viewport.SelectedObjectId ?? throw new InvalidOperationException("먼저 오브젝트를 고른다.");
                _session.SetPortrait(objectId, path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"초상 실패: {exception.Message}");
        }
    }

    private void BuildToolbar()
    {
        BuildWorkModeSwitch();
        Toolbar.Children.Add(ToolButton("새로", OnNewClick));
        Toolbar.Children.Add(ToolButton("열기…", OnOpenClick));
        Toolbar.Children.Add(_recentButton);
        Toolbar.Children.Add(ToolButton("저장", OnSaveClick));
        Toolbar.Children.Add(ToolButton("다른 이름으로…", OnSaveAsClick));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("리그 덤프…", OnLoadRigClick));
        Toolbar.Children.Add(OnlyIn(WorkMode.Motion, ToolButton("motions.json 가져오기…", OnImportClick)));
        Toolbar.Children.Add(OnlyIn(WorkMode.Motion, _exportScope));
        ToolTip.SetTip(_exportScope, "내보낼 범위 — 전체 / 선택 오브젝트의 rigKind 모션만 / 선택 오브젝트가 입을 수 있는 모션만. 모션의 주인은 rigKind다");
        Toolbar.Children.Add(OnlyIn(WorkMode.Motion, ToolButton("motions.json 내보내기…", OnExportClick)));
        Toolbar.Children.Add(OnlyIn(WorkMode.Emoji, EmojiExportButton()));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("되돌리기", (_, _) => UndoCommand()));
        Toolbar.Children.Add(ToolButton("다시하기", (_, _) => RedoCommand()));
        Toolbar.Children.Add(Separator());
        BuildTween();
        Button tween = ToolButton("트윈머신", (_, _) => OpenTween());
        ToolTip.SetTip(tween, "트윈머신 창을 띄운다 — 앞 키와 뒤 키 사이의 값을 슬라이더로 골라 지금 프레임에 키로 놓는다(사이 포즈 · 오버슈트). 캐릭터 모션과 이모지 둘 다. 창은 떠 있는 채로 뷰포트 · 타임라인을 만진다");
        Toolbar.Children.Add(tween);
        Toolbar.Children.Add(ToolButton("설정…", (_, _) => OpenSettings()));
        Toolbar.Children.Add(ToolButton("단축키 (F1)", (_, _) => OpenShortcuts()));
    }

    private void BuildLeft()
    {
        LeftPanel.Children.Add(Header("씬 — 오브젝트"));
        _hierarchy.SelectionChanged += (_, _) => OnHierarchySelected();
        // 더블클릭은 노드 선택과 같다(채널 박스가 그 노드를 보인다). "채널 추가"는 없다.
        _hierarchy.DoubleTapped += (_, _) =>
        {
            switch (_hierarchy.SelectedItem)
            {
                case TreeViewItem { Tag: HierarchyNode node }:
                    SelectNodeInHierarchy(node);
                    break;
                case TreeViewItem { Tag: HierarchyController controller }:
                    SelectControllerInHierarchy(controller);
                    break;
            }
        };
        _hierarchy.ContextMenu = _hierarchyMenu;
        _hierarchyMenu.Opening += (_, _) => RebuildHierarchyMenu();
        _hierarchy.AddHandler(PointerPressedEvent, OnHierarchyPointerPressed, RoutingStrategies.Tunnel);
        _hierarchy.KeyDown += OnHierarchyKeyDown;
        LeftPanel.Children.Add(_hierarchy);
        var plus = SmallButton("+ 캐릭터 리그", (_, _) => Guard("캐릭터 리그 만들기", () =>
            CreateObject(SceneObject.DefaultRig, _hierarchy.SelectedItem is TreeViewItem { Tag: { } tag } ? tag : null)));
        ToolTip.SetTip(plus, "바로 캐릭터 리그를 만든다 — 고른 층(Content)·Stage면 거기에, 오브젝트/노드를 고른 채면 그 자식으로, 아무것도 안 골랐으면 선택 오브젝트의 무대·층(없으면 Stage00 Mid)에. 우클릭 메뉴와 같다 (Ctrl+Shift+N)");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                plus,
                SmallButton("삭제", (_, _) => Guard("오브젝트 삭제", RemoveObject)),
                SmallButton("초상 PNG…", OnPickPortraitClick),
            },
        });
        ToolTip.SetTip(_hierarchy, "PresentataionUIRoot › … › Stage{NN}Depth_{Layer}_Content › 오브젝트 › 자리/깊이/연기 › 컨트롤러 › 자식 오브젝트. 클릭 = 선택, 컨트롤러 클릭 = 채널 박스·매니퓰레이터의 손잡이(값을 넣거나 끌면 그 노드에 키가 생긴다). 우클릭 = 이름/복제/삭제/숨기기/레이어/만들기 메뉴(층 위에서 만들면 그 층에). 오브젝트끼리 부모·자식은 없다. '리그 노드 보기'(보기 설정)면 덤프 노드 26단. Del · Ctrl+D · Ctrl+Shift+N · F2");

        LeftPanel.Children.Add(Header("모션"));
        _motionList.SelectionChanged += (_, _) => OnMotionSelected();
        LeftPanel.Children.Add(_motionList);

        var addMotionButton = SmallButton("모션 추가", (_, _) => Guard("모션 추가", () => AddMotion()));
        ToolTip.SetTip(addMotionButton, "이름(motion, motion_2…)·리그(선택 오브젝트의 rigKind)·길이 1초·끝 restore로 만든다 — 오른쪽 인스펙터에서 바꾼다");
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { addMotionButton, SmallButton("모션 삭제", (_, _) => Guard("모션 삭제", RemoveMotion)) },
        });

        Button addPresetButton = SmallButton("프리셋에서 추가", (_, _) => Guard("프리셋에서 추가", () => AddPreset()));
        ToolTip.SetTip(addPresetButton, "컨트롤러 표 위에 지은 연기 9종(끄덕·젓기·깡충·움찔·떨림·숙임·바운스·호흡·돌아서기)을 선택 오브젝트의 리그로 더한다 — 이름은 id(겹치면 id_2…). 표가 없는 리그는 안 된다");
        _presetCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _presetCombo.SelectedIndex >= 0)
            {
                ToolTip.SetTip(_presetCombo, MotionPresets.All[_presetCombo.SelectedIndex].Note);
            }
        };
        ToolTip.SetTip(_presetCombo, MotionPresets.All[0].Note);
        LeftPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _presetCombo, addPresetButton },
        });

        BuildClipLeft();

        // 여기까지가 캐릭터 모션 모드의 왼쪽이고, 다음부터가 이모지 모드의 왼쪽이다 — 모드가 한쪽만 보인다.
        int emojiFrom = LeftPanel.Children.Count;
        BuildEmojiLeft();
        GroupLeftByWorkMode(emojiFrom);
    }

    /// <summary>
    /// 오른쪽 = 캐릭터 모션 모드의 탭 넷(PLAN-inspector): 채널 박스(기본, 애니메이터) · 씬(무대 세팅) · 모션(속성) · 클립, 그리고 이모지 모드의 칸(탭 밖). 설정·단축키는 툴바의 창으로,
    /// 단축키의 그림자 버튼(Q/W/E/R·S·Esc·복사·붙여넣기…)은 없다. 마지막 탭·접힌 묶음은 설정에 남는다.
    /// </summary>
    private void BuildRight()
    {
        _inspectorTabItems = new[]
        {
            new TabItem { Header = "채널 박스", Content = BuildChannelBoxTab(), FontSize = 12 },
            new TabItem { Header = "씬", Content = BuildSceneTab(), FontSize = 12 },
            new TabItem { Header = "모션", Content = BuildMotionTab(), FontSize = 12 },
            new TabItem { Header = "클립", Content = BuildClipTab(), FontSize = 12 },
        };
        _inspectorTabs.ItemsSource = _inspectorTabItems;
        _inspectorTabs.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _inspectorTabs.SelectedIndex >= 0)
            {
                _settings.InspectorTab = _inspectorTabs.SelectedIndex;
            }
        };
        RightPanel.Children.Add(_inspectorTabs);

        // 이모지 모드의 오른쪽 — 탭이 아니다(하나뿐인 탭 줄은 뜻이 없다). 모드가 탭들과 이것 중 하나만 보인다.
        _emojiPane = BuildEmojiPane();
        RightPanel.Children.Add(_emojiPane);
        WireSettingsControls();
    }

    private Control BuildMotionTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(Labeled("이름", _motionName));
        panel.Children.Add(Labeled("길이(초)", _motionDuration));
        panel.Children.Add(Labeled("종료 처리", _motionEnd));
        ToolTip.SetTip(_motionEnd, "모션이 끝난 뒤 — 복원(restore) = 시작 자세로 돌아간다 · 유지(commit) = 마지막 키 값이 남는다. 산출물(motions/2)의 end");
        panel.Children.Add(Labeled("재생 기준", _motionDefaultMode));
        ToolTip.SetTip(_motionDefaultMode, "키 값을 어떻게 읽나 — 상대값(relative) = 시작값 + 키(배율은 ×) · 절대값(absolute) = 키 그대로. 뷰포트 미리보기와 매니퓰레이터·채널 박스가 포즈를 키 값으로 바꿀 때 쓴다. 파일에는 안 나간다: 재생은 대본의 <<motion slot 이름 relative|absolute>>가 정한다(생략 = relative)");

        OnCommit(_motionName, text => WithMotion(m =>
        {
            if (text != m.Name)
            {
                _session.Editor.RenameMotion(m.Name, text);

                // 클립 안에서 블록의 모션 이름을 바꾼 것이면 목록에서 고르지 않는다 — 고르면 클립이 풀린다(블록의 참조는 편집기가 따라 바꿨다).
                if (SelectedClip is null)
                {
                    _motionList.SelectedItem = text;
                }
            }
        }));
        OnCommit(_motionDuration, text => WithMotion(m => { if (TryFloat(text, out float v) && v != m.Duration) _session.Editor.SetDuration(m.Name, v); }));
        _motionEnd.SelectionChanged += (_, _) => WithMotion(m =>
        {
            if (_motionEnd.SelectedIndex >= 0 && (MotionEnd)_motionEnd.SelectedIndex != m.End)
            {
                _session.Editor.SetEnd(m.Name, (MotionEnd)_motionEnd.SelectedIndex);
            }
        });
        _motionDefaultMode.SelectionChanged += (_, _) => WithMotion(m =>
        {
            if (_motionDefaultMode.SelectedIndex >= 0 && (MotionChannelMode)_motionDefaultMode.SelectedIndex != m.PreviewMode)
            {
                _session.Editor.SetPreviewMode(m.Name, (MotionChannelMode)_motionDefaultMode.SelectedIndex);
            }
        });
        return panel;
    }

    internal bool ShowStageSkeleton
    {
        get => _settings.ShowStageSkeleton;
        set
        {
            _settings.ShowStageSkeleton = value;
            ApplySettings();
            RebuildHierarchy();
        }
    }

    internal IReadOnlyCollection<string> CollapsedChannelBoxGroups => _collapsedGroups;

    // ── 문서·목록 갱신 ─────────────────────────────────────────────────

    private void OnDocumentReplaced()
    {
        _session.Editor.Changed += (_, _) => OnEditorChanged();
        _viewport.ClearClipMutes();
        RefreshTitle();
        BindMotion(null);
        RefreshAll();
        RefreshAssets();
    }

    private void OnEditorChanged()
    {
        RefreshTitle();
        RefreshTweenNote();
        RefreshMotionList();
        RefreshClips();
        RefreshEmojis();
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshProblems();
        RefreshObjects();
        RefreshChannelBox();
    }

    private static TextBlock Small(string text) => new() { Text = text, FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };

    private void RefreshAll()
    {
        RefreshMotionList();
        RefreshClips();
        RefreshEmojis();
        RefreshChannels();
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshProblems();
        StatusText.Text = _session.Status;
    }

    private void RefreshMotionList()
    {
        _refreshDepth++;

        try
        {
            // 클립 안에서 블록의 모션을 고치는 중이면 그래프의 모션은 "고른 모션"이 아니다 — 목록에서 골라 주지 않는다(고르면 클립이 풀린다).
            string? selected = SelectedMotion ?? (SelectedClip is not null ? null : _graph.MotionName);
            // rigKind로 묶어 보인다(모션의 주인은 rigKind) — 같은 리그끼리 이름순.
            List<string> names = _session.Editor.Document.Motions
                .OrderBy(m => m.Rig, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => m.Name).ToList();
            _motionList.ItemsSource = names;
            _motionList.ItemTemplate ??= new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            {
                MotionAsset? motion = _session.Editor.Document.Find(name);
                bool fits = motion is null || _viewport.SelectedObject is not { } o || _session.Rig is null || ScenePreview.CanWear(_session.Rig, o, motion);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Opacity = string.IsNullOrEmpty(name) ? 0.6 : fits ? 1 : 0.5 };
                row.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(name) ? "(이름 없음 — 아래 문제 칸의 '이름 고치기')" : name, FontSize = 11 });
                row.Children.Add(new TextBlock { Text = motion is null ? "" : motion.Rig + (fits ? "" : " · 안 맞음"), FontSize = 10, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
                return row;
            });
            int index = selected is null ? -1 : names.IndexOf(selected);
            _motionList.SelectedIndex = index;

            // 그래프가 이모지의 타임라인을 묶고 있으면 모션 목록에 없는 것이 맞다 — 다시 묶지 않는다
            // (다시 묶으면 시계가 처음으로 돌아가, 키를 놓을 때마다 0프레임으로 튀었다). 이모지가 사라진 것은 RefreshEmojis가 본다.
            if (index < 0 && _graph.MotionName is { } bound && !EmojiAsset.IsTimelineName(bound, out _) && SelectedClip is null)
            {
                BindMotion(null);
            }
            else if (index >= 0 && _graph.MotionName != names[index])
            {
                BindMotion(names[index]);
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void OnMotionSelected()
    {
        if (_refreshing)
        {
            return;
        }

        if (SelectedMotion is not null)
        {
            DeselectClip(); // 시간의 주인은 모션 · 클립 · 이모지 중 하나다
            DeselectEmoji();
            EnterWorkMode(WorkMode.Motion);
        }

        BindMotion(SelectedMotion);
        RefreshMotionInspector();
        RefreshKeyInspector();
    }

    internal int OutlinerRowCount => _channelRows.Children.Count;

    internal string CurrentTimeText => _currentTime.Text ?? "";

    internal string OutlinerQuery
    {
        get => _outlinerSearch.Text ?? "";
        set
        {
            _outlinerSearch.Text = value;
            RefreshChannels();
        }
    }

    private void RefreshMotionInspector()
    {
        _refreshDepth++;

        try
        {
            MotionAsset? motion = _graph.Motion;
            bool has = motion is not null;
            _motionName.IsEnabled = has;
            _motionDuration.IsEnabled = has;
            _motionEnd.IsEnabled = has;
            _motionDefaultMode.IsEnabled = has;

            _motionName.Text = motion?.Name ?? "";
            _motionDuration.Text = motion is null ? "" : motion.Duration.ToString("0.###", CultureInfo.InvariantCulture);
            _motionEnd.SelectedIndex = motion is null ? -1 : (int)motion.End;
            _motionDefaultMode.SelectedIndex = motion is null ? -1 : (int)motion.PreviewMode;
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void RefreshKeyInspector()
    {
        _refreshDepth++;

        try
        {
            MotionAsset? motion = _graph.Motion;
            int count = _graph.Selection.Count;
            KeyAsset? single = count == 1 && motion is not null ? _graph.Selection.First().Resolve(motion) : null;
            _keyValue.IsEnabled = count > 0;

            _keyValue.Text = single is null ? (count > 0 ? "+=0" : "") : F(single.V);
            RefreshToolButtons();
        }
        finally
        {
            _refreshDepth--;
        }
    }

    internal int ProblemRowsShown => ProblemsPanel.Children.Count;

    // ── 명령 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 모션 추가 — 이름은 motion, motion_2…, 리그는 선택 오브젝트의 rigKind(없으면 character), 길이 1초, 끝 restore.
    /// 전부 오른쪽 인스펙터에서 바꾼다. 테스트 손잡이는 값을 준다.
    /// </summary>
    private void AddMotion(string? name = null, string? rig = null, float duration = 1f, MotionEnd end = MotionEnd.Restore)
    {
        name = (name ?? "").Trim();

        // 이름을 비우면 motion, motion_2…로 만든다 — 빈 이름의 모션은 목록에서 안 보여 규칙 위반이 "안 없어지는" 것처럼 보인다.
        if (name.Length == 0)
        {
            name = MotionNames.Unique(_session.Editor.Document, MotionNames.Fallback);
        }

        rig ??= _viewport.SelectedObject?.Rig ?? SceneObject.DefaultRig;
        _session.Editor.AddMotion(name, rig, duration, end);
        _motionList.SelectedItem = name;
    }

    /// <summary>프리셋에서 추가 — 콤보의 프리셋을 선택 오브젝트의 리그(없으면 character)로. 표가 없는 리그는 예외(상태줄).</summary>
    private string AddPreset(string? presetId = null)
    {
        MotionPreset preset = presetId is null
            ? MotionPresets.All[Math.Max(0, _presetCombo.SelectedIndex)]
            : MotionPresets.Find(presetId) ?? throw new InvalidOperationException($"프리셋 '{presetId}'이 없다.");
        string rig = _viewport.SelectedObject?.Rig ?? SceneObject.DefaultRig;
        ControllerRig table = _session.ControllersOf(rig) ?? throw new InvalidOperationException($"리그 '{rig}'에 컨트롤러 표가 없다 — 프리셋은 컨트롤러 위에 지은 것이다(presets/controllers.json).");
        string name = MotionPresets.Add(_session.Editor, preset, rig, table);
        _motionList.SelectedItem = name;
        return name;
    }

    /// <summary>테스트 손잡이 — "프리셋에서 추가".</summary>
    internal string AddPresetFromForm(string presetId) => AddPreset(presetId);

    private void RemoveMotion()
    {
        if (SelectedMotion is { } name)
        {
            _session.Editor.RemoveMotion(name);
        }
    }

    /// <summary>
    /// 시각·값 칸: 숫자는 절대값, <c>+=</c>·<c>-=</c>·<c>*=</c>·<c>/=</c>는 상대. 선택 키 전부에 적용한다.
    /// 시각의 절대값은 키 하나일 때만(여러 키를 한 시각에 겹칠 수 없다).
    /// </summary>
    /// <summary>값 칸 — 선택 키 전부에 적용(숫자는 절대값, +=·-=·*=·/=는 상대). 시각 칸은 없다(소유자, 2026-10-28): 시각은 끌거나 도프 시트로.</summary>
    private void ApplyValueToSelectedKeys(string text)
    {
        if (_refreshing || _graph.Motion is not { } motion || _graph.Selection.Count == 0 || !RelativeInput.TryParse(text, out Func<float, float> apply))
        {
            return;
        }

        List<KeyRef> keys = _graph.Selection.ToList();
        Guard("키 값", () => _session.Editor.SetKeyValues(motion.Name, keys, k => apply(k.V)));
    }

    private void UpdateKey(KeyRef key, KeyAsset current, float? t = null, float? value = null)
    {
        _session.Editor.UpdateKey(_graph.MotionName!, key.Curve.Node, key.Curve.Kind, key.Curve.Axis, key.Index, new KeyAsset
        {
            T = t ?? current.T,
            V = value ?? current.V,
            InTangent = current.InTangent,
            OutTangent = current.OutTangent,
            Tangent = current.Tangent,
            Broken = current.Broken,
        });
        _graph.Select(new[] { key }, additive: false);
    }

    private void WithMotion(Action<MotionAsset> action)
    {
        if (_refreshing || _graph.Motion is not { } motion)
        {
            return;
        }

        Guard("모션 편집", () => action(motion));
    }

    private void WithSingleKey(Action<KeyRef, KeyAsset> action)
    {
        if (_refreshing || _graph.Motion is not { } motion || _graph.Selection.Count != 1)
        {
            return;
        }

        KeyRef key = _graph.Selection.First();

        if (key.Resolve(motion) is { } asset)
        {
            Guard("키 편집", () => action(key, asset));
        }
    }

    /// <summary>잘못된 입력·없는 파일은 상태 줄로 — 창이 죽지 않는다.</summary>
    private void Guard(string context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"{context} 실패: {exception.Message}");
        }
    }

    // ── 작은 도우미 ───────────────────────────────────────────────────

    private static bool TryFloat(string text, out float value)
        => float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// 글 칸 확정 — Enter와 포커스 잃기. 한 번 확정한 글은 포커스가 떠날 때 다시 확정하지 않고, 들어와서 안 바꾸고 나가면 아무 일도 없다
    /// (같은 편집이 두 번 기록되거나 다시하기가 지워지지 않게). Enter로 확정하면 포커스를 뷰포트로 옮긴다 — 그 뒤 Ctrl+Z는 문서 되돌리기다(마야 채널 박스).
    /// </summary>
    private void OnCommit(TextBox box, Action<string> apply)
    {
        string? committed = null;

        void Commit(bool enter)
        {
            string text = box.Text ?? "";

            if (!enter && text == committed)
            {
                return;
            }

            committed = text;
            apply(text);
        }

        box.GotFocus += (_, _) => committed = box.Text ?? "";
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !box.AcceptsReturn)
            {
                Commit(enter: true);
                e.Handled = true;

                if (ReferenceEquals(TopLevel.GetTopLevel(box), this))
                {
                    _viewport.Focus();
                }
            }
        };
        box.LostFocus += (_, _) => Commit(enter: false);
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 6, 0, 0),
    };

    private static Control Labeled(string label, Control control)
    {
        var row = new DockPanel();
        var text = new TextBlock { Text = label, FontSize = 11, Width = 76, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static Button ToolButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
        button.Click += onClick;
        return button;
    }

    private static Button SmallButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
        button.Click += onClick;
        return button;
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Avalonia.Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
    };
}
