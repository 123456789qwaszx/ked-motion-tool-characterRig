using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Views;

/// <summary>
/// 뷰포트 — 리그 덤프로 세운 트리 위에 초상 한 장을 올리고 모션을 재생한다.
///
/// 포즈는 <see cref="MotionPreview"/>(코어 수학), 자리는 <see cref="PortraitLayout"/>(런타임 사이징)이다 —
/// 여기에는 그리기·시계·입력만 있다. 무대는 기준 해상도 크기이고 컨트롤 안에 레터박스로 맞춘다.
/// 깊이는 mid(DepthScale 1) 고정이고 카메라는 없다 — v1 범위.
///
/// 타임 슬라이더는 따로(<see cref="TimeSliderView"/>)다. 재생 토글은 전역 Alt+`(셸), Home/End 처음/끝.
/// <b>포즈 들고 스크럽</b>(마야 MMB 드래그): <see cref="HoldScrubTo"/>로 시각을 옮겨도 보이는 포즈는 그대로라 S로 그 포즈를 다른 프레임에 키한다.
/// </summary>
public sealed class ViewportView : Control
{
    private const double Pad = 8;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(255, 16, 16, 22));
    private static readonly IBrush StageBrush = new SolidColorBrush(Color.FromArgb(255, 44, 44, 58));
    private static readonly Pen StagePen = new(new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)), 1);
    private static readonly Pen PlaceholderPen = new(new SolidColorBrush(Color.FromArgb(200, 226, 232, 240)), 1.5, DashStyle.Dash);
    private static readonly IBrush PlaceholderFill = new SolidColorBrush(Color.FromArgb(40, 226, 232, 240));
    private static readonly Pen XAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 248, 113, 113)), 2);
    private static readonly Pen YAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 74, 222, 128)), 2);
    private static readonly IBrush PivotBrush = new SolidColorBrush(Color.FromArgb(240, 250, 204, 21));
    private static readonly Pen GridMinorPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
    private static readonly Pen GridMajorPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
    private static readonly Pen WorldXPen = new(new SolidColorBrush(Color.FromArgb(200, 0xE2, 0x4C, 0x3F)), 1.5);
    private static readonly Pen WorldYPen = new(new SolidColorBrush(Color.FromArgb(200, 0x5E, 0xC6, 0x5A)), 1.5);
    private static readonly Pen ObjectOutlinePen = new(new SolidColorBrush(Color.FromArgb(180, 0x9E, 0xB4, 0xFF)), 1, DashStyle.Dash);
    private static readonly Pen SelectedObjectPen = new(new SolidColorBrush(Color.FromArgb(230, 0xF9, 0xD7, 0x1C)), 1.5);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(200, 203, 213, 225));
    private static readonly IBrush GizmoCenterBrush = new SolidColorBrush(Color.FromArgb(230, 250, 204, 21));
    private static readonly IBrush GizmoXBrush = new SolidColorBrush(Color.FromArgb(240, 248, 113, 113));
    private static readonly IBrush GizmoYBrush = new SolidColorBrush(Color.FromArgb(240, 74, 222, 128));
    private static readonly Pen GizmoRingPen = new(new SolidColorBrush(Color.FromArgb(230, 96, 165, 250)), 2);
    private static readonly Pen PendingPen = new(new SolidColorBrush(Color.FromArgb(220, 251, 146, 60)), 1.5, DashStyle.Dash);
    private static readonly IBrush OutsideBrush = new SolidColorBrush(Color.FromArgb(185, 58, 58, 66));

    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();

    private MotionEditor? _editor;
    private string? _motionName;
    private string? _clipName;
    private ClipStage? _clipStage;
    private string? _lastClipName; // 모션을 열었다 돌아와도 같은 클립이면 끈 배역이 남는다
    private RigSchemaFile? _rig;
    private DepthTuningFile? _depth;
    private FocusTuningFile? _focus;
    // 무대 뼈대(PresentataionUIRoot). 리그가 있으면 내장 뼈대를 쓴다 — 카메라·패럴랙스가 이 트리에 들어간다.
    private RigSchemaRigDto? _stage;
    // 씬 전체의 시작 상태(오브젝트마다 슬롯 접두사). 모션은 선택 오브젝트에만 얹는다.
    private RectNodeTree? _template;
    private readonly Dictionary<string, Bitmap?> _portraits = new();

    /// <summary>조각 그림 — (오브젝트 id, 조각 노드 id) → 비트맵. 스킨이 그림을 준 조각만 든다(파일을 못 읽었으면 null — 자리표시).</summary>
    private readonly Dictionary<(string Object, string Node), Bitmap?> _partImages = new();
    private string? _selectedObjectId;
    public bool ShowGrid { get; set; } = true;
    private Vec2 _stageSize = BaseResolutionFile.Default;
    private KeySelection _selection = new();
    private CurveFlags _flags = new();

    /// <summary>뮤트된 성분은 재생에서 뺀다(시작값 그대로). 그래프·도프시트와 같은 객체.</summary>
    public CurveFlags Flags
    {
        get => _flags;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_flags, value))
            {
                return;
            }

            _flags.Changed -= OnFlagsChanged;
            _flags = value;
            _flags.Changed += OnFlagsChanged;
            InvalidateVisual();
        }
    }

    private void OnFlagsChanged(object? sender, EventArgs e) => InvalidateVisual();

    private enum Drag { None, MoveFree, MoveX, MoveY, Rotate, ScaleFree, ScaleX, ScaleY }

    private Drag _drag = Drag.None;

    private const double GizmoArm = 60;
    private const double GizmoHit = 9;

    // 키로 아직 안 들어간 포즈(마야: 키 없이 바꾼 값). 시각이 바뀌면 버린다.
    private readonly Dictionary<string, RectNodeState> _overrides = new();
    private readonly Dictionary<(string Node, MotionChannelKind Kind), HashSet<Axis>> _pending = new();

    // 들고 있는 포즈(마야 MMB 스크럽). 시각이 바뀌어도 이것을 보여 주고, S가 이것을 키한다.
    private RectNodeTree? _held;
    private bool _holdScrub;

    // 드래그 시작 때의 키 없는 포즈 — 놓을 때 이력에 올릴 차이의 기준.
    private PendingSnapshot? _dragPendingBefore;
    private Vec2 _dragStartRoot;
    private Vec2 _dragPivotRoot;
    private Point _dragStartControl;
    private Vec2 _dragScaleDirection;
    private RectNodeState _dragStartState;

    public ViewportView()
    {
        MinHeight = 120;
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => OnTick();
        _selection.Changed += OnSelectionChanged;
        _flags.Changed += OnFlagsChanged;
    }

    public event EventHandler? TimeChanged;

    public event EventHandler? PlayStateChanged;

    /// <summary>재생 범위·모드·속도가 바뀌었다.</summary>
    public event EventHandler? RangeChanged;

    /// <summary>키 없이 바꾼 포즈가 생기거나 사라졌다(S로 키를 놓거나 시각이 바뀌면 사라진다).</summary>
    public event EventHandler? PendingChanged;

    /// <summary>매니퓰레이터·S·Auto Key로 키가 들어갔다.</summary>
    public event EventHandler? PoseKeyed;

    public ManipulatorMode Manipulator { get; set; } = ManipulatorMode.Move;

    // 보기 변환(네비게이션): 무대를 컨트롤에 맞춘 배율 × ViewZoom, 가운데에서 ViewPan만큼. 씬 데이터가 아니라 화면뿐이다.
    private double _viewZoom = 1;
    private Vector _viewPan;
    private bool _panning;
    private Point _panPointerStart;
    private Vector _panStart;

    public const double MinViewZoom = 0.2;
    public const double MaxViewZoom = 10;

    /// <summary>보기 배율 — 1 = 무대가 컨트롤에 맞는다(레터박스). 휠이 커서를 축으로 바꾼다.</summary>
    public double ViewZoom => _viewZoom;

    /// <summary>보기 이동(컨트롤 픽셀) — MMB 드래그.</summary>
    public Vector ViewPan => _viewPan;

    /// <summary>보기 맞춤(F) — 배율 1, 이동 0.</summary>
    public void ResetView()
    {
        _viewZoom = 1;
        _viewPan = default;
        InvalidateVisual();
    }

    /// <summary>커서 아래의 루트 점이 제자리에 있도록 배율을 바꾼다(그래프 편집기 휠과 같은 관습).</summary>
    public void ZoomViewAt(Point anchor, double factor)
    {
        double next = Math.Clamp(_viewZoom * factor, MinViewZoom, MaxViewZoom);

        if (Math.Abs(next - _viewZoom) < 1e-9)
        {
            return;
        }

        Vec2 under = ToRoot(anchor);
        _viewZoom = next;
        Point moved = ToControl(under);
        _viewPan = new Vector(_viewPan.X + anchor.X - moved.X, _viewPan.Y + anchor.Y - moved.Y);
        InvalidateVisual();
    }

    /// <summary>켜져 있으면 매니퓰레이터를 놓는 순간 키가 들어간다. 꺼져 있으면 S를 누를 때까지 "키 없는 포즈"다.</summary>
    public bool AutoKey { get; set; }

    /// <summary>새 키의 탄젠트 모드(설정).</summary>
    public TangentMode DefaultTangent { get; set; } = TangentMode.Auto;

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    public bool HasPending => _pending.Count > 0 || _held is not null;

    /// <summary>포즈를 들고 있는가(MMB 스크럽 뒤). S가 모든 채널에 그 포즈를 키한다.</summary>
    public bool IsHoldingPose => _held is not null;

    /// <summary>매니퓰레이터를 둘 수 있는 노드들 — 덤프의 노드 중 __root·키잉 금지 노드를 뺀 것.</summary>
    public IReadOnlyList<string> NodeChoices
    {
        get
        {
            if (_rig is null || SelectedObject is not { } o || !_rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
            {
                return Array.Empty<string>();
            }

            return dto.nodes.Select(n => n.id).Where(k => k != RigSchemaLoader.RootKey && !MotionRules.ForbiddenNodes.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    // ── 씬·오브젝트 ───────────────────────────────────────────────────

    /// <summary>뷰포트가 보여 주는 씬 — 문서의 활성 씬. 클립을 보는 중이면 그 클립의 시작 씬.</summary>
    public SceneAsset? Scene => _editor is null
        ? null
        : ShownClip is { } clip ? ClipPreview.SceneOf(_editor.Document, clip) ?? _editor.Document.ActiveSceneAsset : _editor.Document.ActiveSceneAsset;

    public IReadOnlyList<SceneObject> SceneObjects => Scene?.Objects ?? (IReadOnlyList<SceneObject>)Array.Empty<SceneObject>();

    /// <summary>선택 오브젝트 — 매니퓰레이터·모션 미리보기·초상 바꾸기의 대상.</summary>
    public string? SelectedObjectId
    {
        get => _selectedObjectId;
        set
        {
            if (_selectedObjectId == value)
            {
                return;
            }

            _selectedObjectId = value;
            DiscardPending();
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public SceneObject? SelectedObject => _selectedObjectId is null ? null : Scene?.Find(_selectedObjectId);

    /// <summary>
    /// 포즈를 잡는 오브젝트의 노드 키 접두사("c1/"). 보통은 선택 오브젝트이고, 클립 안에서 블록을 고치는 중이면 그 블록의 배역을 맡은 오브젝트다
    /// (뷰포트에서 다른 오브젝트를 눌러도 키는 그 블록의 캐릭터에 놓인다). 오브젝트가 없으면 빈 문자열.
    /// </summary>
    private string Prefix => ContextActor?.Prefix ?? SelectedObject?.Prefix ?? "";

    // ── 클립 안에서 블록의 모션을 고친다(PLAN-clip.md K4b 2단계) ─────────

    private (int Track, int Index)? _contextBlock;

    /// <summary>
    /// 클립을 보는 중에 포즈를 잡을 블록(타임라인에서 고른 모션 블록). 끌기·채널 박스·S가 그 블록의 모션에 키를 놓는다 —
    /// 시각은 클립 시각 − 블록의 시작, 재생 기준은 블록의 것, 시작값은 "이 블록이 얹히기 전의 상태"다. null이면 클립 층에서는 포즈를 잡지 않는다.
    /// </summary>
    public (int Track, int Index)? ClipBlockContext
    {
        get => _contextBlock;
        set
        {
            if (_contextBlock == value)
            {
                return;
            }

            DiscardPending(); // 다른 블록의 키 없는 포즈를 들고 넘어가지 않는다
            _contextBlock = value;

            if (ContextActor is { } actor)
            {
                SelectedObjectId = actor.Id;
            }

            PendingChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>문맥의 블록 — 클립을 보는 중이고, 그 자리에 캐릭터 모션 블록이 있고, 꺼지지 않았을 때만.</summary>
    private (ClipTrack Track, ClipBlock Block)? ContextEntry => AnyContextEntry is { } entry && !entry.Track.IsCamera ? entry : null;

    /// <summary>
    /// 문맥이 카메라 모션 블록일 때 — 그 블록과 모션. 지금 시각이 블록 안일 때만(캐릭터와 같은 까닭: 밖에서는 그 모션이 보이지 않는다).
    /// 캐릭터의 포즈는 안 잡히고, 카메라 값 칸(<see cref="SetCameraValue"/>)만 그 블록의 모션에 키를 놓는다.
    /// </summary>
    private (ClipBlock Block, MotionAsset Motion)? CameraContext
    {
        get
        {
            if (_editor is null || AnyContextEntry is not { } entry || !entry.Track.IsCamera
                || _editor.Document.Find(entry.Block.Motion) is not { } motion || !CameraRig.IsCamera(motion))
            {
                return null;
            }

            double local = _clock.Time - entry.Block.At;
            return local >= -1e-6 && local <= motion.Duration + 1e-6 ? (entry.Block, motion) : null;
        }
    }

    /// <summary>그 시각의 클립 카메라 — 카메라 블록(상태)까지 반영한 값에 카메라 모션을 얹은 것. <paramref name="except"/>는 빼고.</summary>
    private CameraSettings ClipCameraAt(ClipAsset clip, double seconds, ClipBlock? except)
    {
        CameraSettings stage = _clipStage?.CameraAt(seconds) ?? Scene?.Camera ?? new CameraSettings();
        return ClipPreview.CameraAt(_editor!.Document, clip, stage, seconds, IsClipTrackMuted, except) ?? stage;
    }

    private (ClipTrack Track, ClipBlock Block)? AnyContextEntry
    {
        get
        {
            if (_motionName is not null || ShownClip is not { } clip || _contextBlock is not { } at
                || at.Track < 0 || at.Track >= clip.Tracks.Count || at.Index < 0 || at.Index >= clip.Tracks[at.Track].Blocks.Count)
            {
                return null;
            }

            ClipTrack track = clip.Tracks[at.Track];
            ClipBlock block = track.Blocks[at.Index];
            return block.IsMotion && !block.IsMuted && !IsClipTrackMuted(track) ? (track, block) : null;
        }
    }

    private SceneObject? ContextActor
        => _editor is not null && ShownClip is { } clip && ContextEntry is { } entry ? ClipPreview.ObjectOf(_editor.Document, clip, entry.Track) : null;

    /// <summary>
    /// 포즈를 키로 바꿀 모션 — 묶인 모션, 또는 클립 안에서 고치는 블록의 모션. 블록은 **지금 시각이 그 블록 안일 때만** 고친다:
    /// 블록 밖에서는 그 모션이 보이지 않아, 잡은 포즈가 어디로 가는지 알 수 없다.
    /// </summary>
    public MotionAsset? EditMotion
    {
        get
        {
            if (Motion is { } bound)
            {
                return bound;
            }

            if (_editor is null || ContextEntry is not { } entry || ContextActor is null || _editor.Document.Find(entry.Block.Motion) is not { } motion)
            {
                return null;
            }

            double local = _clock.Time - entry.Block.At;
            return local >= -1e-6 && local <= motion.Duration + 1e-6 ? motion : null;
        }
    }

    /// <summary>키를 놓는 시각(모션 기준) — 클립 안에서는 시계에서 블록의 시작을 뺀다.</summary>
    private double EditTime => _clock.Time - (ContextEntry?.Block.At ?? 0f);

    /// <summary>키 값을 읽는 기준 — 클립 안에서는 블록의 재생 기준(모션의 렌즈가 아니다).</summary>
    private MotionChannelMode EditModeOf(MotionAsset motion) => ContextEntry?.Block.Mode ?? motion.PreviewMode;

    /// <summary>
    /// 키 값의 시작값 — 보통은 씬으로 세운 트리. 클립 안에서는 지금 시각의 클립 포즈에서 **이 블록만 뺀** 것이다
    /// (앞 블록이 남긴 값·상태 블록이 옮긴 자리 위에 이 블록이 얹힌다 — 미리보기와 같은 함수라 놓은 키가 보이는 자리에 그대로 선다).
    /// </summary>
    private RectNodeState StartStateOf(string key)
    {
        if (_editor is not null && _template is not null && ShownClip is { } clip && ContextEntry is { } entry)
        {
            RectNodeTree start = ClipPreview.Pose(_clipStage?.TreeAt(_clock.Time) ?? _template, _editor.Document, clip, _clock.Time, IsClipTrackMuted, _clipStage?.CameraAt(_clock.Time), entry.Block);
            return start.GetState(key);
        }

        return _template!.GetState(key);
    }

    /// <summary>선택 노드의 트리 키(접두사 포함).</summary>
    private string? SelectedKey => SelectedNode is null ? null : Prefix + SelectedNode;

    /// <summary>오브젝트를 골랐다.</summary>
    public event EventHandler? ObjectSelected;

    /// <summary>이 오브젝트에 지금 모션을 입혀 볼 수 있나(rigKind가 같거나 채널 노드가 전부 있으면).</summary>
    public bool CanWear(SceneObject o) => _rig is not null && Motion is { } motion && ScenePreview.CanWear(_rig, o, motion);

    /// <summary>오브젝트의 그림(초상) 사각형 — 루트 공간. 그림 노드가 없는 리그면 루트 rect. 트리가 없으면 null.</summary>
    public Quad? ObjectQuad(string objectId)
    {
        RectNodeTree? pose = PoseAt(_clock.Time);
        SceneObject? o = Scene?.Find(objectId);

        if (pose is null || o is null)
        {
            return null;
        }

        return QuadOf(pose, o);
    }

    /// <summary>포즈 위의 오브젝트 사각형(루트 공간) — 초상이 있으면 보이는 그림, 없으면 루트 rect.</summary>
    private Quad? QuadOf(RectNodeTree pose, SceneObject o)
    {
        // 조각으로 그려지는 오브젝트는 첫 조각의 사각형이 고르는 자리다.
        if (DrawnParts(o) is { Count: > 0 } parts && PartQuadOf(pose, o, parts[0]) is { } first)
        {
            return first;
        }

        string? image = ScenePreview.ImageKeyOf(pose, o);
        return image is not null
            ? PortraitLayout.VisualQuad(pose, image, AspectOf(o))
            : pose.Contains(ScenePreview.RootKeyOf(o)) ? PortraitLayout.RectQuad(pose, ScenePreview.RootKeyOf(o)) : null;
    }

    private float AspectOf(SceneObject o)
        => _portraits.TryGetValue(o.Id, out Bitmap? bitmap) && bitmap is { PixelSize: { Width: > 0, Height: > 0 } }
            ? bitmap.PixelSize.Width / (float)bitmap.PixelSize.Height
            : PortraitLayout.PlaceholderAspect;

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    /// <summary>지금 보이는 포즈 — 시각의 포즈에 키 없는 포즈를 얹은 것.</summary>
    public RectNodeTree? CurrentPose()
    {
        if (_held is not null)
        {
            return _held;
        }

        RectNodeTree? pose = PoseAt(_clock.Time);

        if (pose is null)
        {
            return null;
        }

        foreach ((string node, RectNodeState state) in _overrides)
        {
            if (pose.Contains(node))
            {
                pose.SetState(node, state);
            }
        }

        return pose;
    }

    /// <summary>매니퓰레이터 중심(컨트롤 좌표). 테스트가 쓴다.</summary>
    public Point? GizmoCenter()
    {
        RectNodeTree? pose = CurrentPose();

        if (pose is null || SelectedKey is not { } key || !pose.Contains(key))
        {
            return null;
        }

        return ToControl(GizmoAnchor(pose, key));
    }

    /// <summary>
    /// 매니퓰레이터가 서는 점(루트 공간) — 노드의 원점. 원점이 오브젝트 그림 밖이면(화면 크기 스트레치 노드의 pivot —
    /// 발 기준 회전의 SwayPivot은 rect 아래 끝이라 무대 밖으로 나간다) 그림 가운데에 둔다. 손잡이는 늘 보이는 곳에 있어야 한다.
    /// </summary>
    private Vec2 GizmoAnchor(RectNodeTree pose, string key)
    {
        Vec2 origin = pose.TransformPoint(key, Vec3.Zero).XY;

        if (SelectedObject is not { } o || QuadOf(pose, o) is not { } quad)
        {
            return origin;
        }

        float minX = Math.Min(Math.Min(quad.BottomLeft.X, quad.BottomRight.X), Math.Min(quad.TopLeft.X, quad.TopRight.X));
        float maxX = Math.Max(Math.Max(quad.BottomLeft.X, quad.BottomRight.X), Math.Max(quad.TopLeft.X, quad.TopRight.X));
        float minY = Math.Min(Math.Min(quad.BottomLeft.Y, quad.BottomRight.Y), Math.Min(quad.TopLeft.Y, quad.TopRight.Y));
        float maxY = Math.Max(Math.Max(quad.BottomLeft.Y, quad.BottomRight.Y), Math.Max(quad.TopLeft.Y, quad.TopRight.Y));
        bool inside = origin.X >= minX && origin.X <= maxX && origin.Y >= minY && origin.Y <= maxY;
        return inside ? origin : quad.Center;
    }

    /// <summary>키 없는 포즈를 버린다(시각이 바뀌거나 Esc·선택 변경·재생). 버린 것도 이력에 올라 Ctrl+Z로 돌아온다.</summary>
    public void DiscardPending() => WithPendingHistory(ClearPending);

    private void ClearPending()
    {
        if (_overrides.Count == 0 && _pending.Count == 0 && _held is null)
        {
            return;
        }

        _overrides.Clear();
        _pending.Clear();
        _held = null;
        _holdScrub = false;
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ── 키 없는 포즈의 이력 ────────────────────────────────────────────
    //
    // 키 없는 포즈(_overrides·_pending·_held)는 문서 밖이라 스냅샷에 없다. 그래서 조작의 경계(드래그 끝·값 확정·S·Esc·스크럽·선택 변경·재생)마다
    // 앞뒤 상태를 재어 편집기 이력에 손(RecordExternal)으로 올린다 — 문서 조작과 한 줄에서 실제 순서대로 되돌아가고, S로 키한 것을 되돌리면
    // 키 확정 직전의 포즈로 돌아온다(키 칸에 손이 붙는다). 저장 파일·산출물에는 아무것도 안 들어간다. 복원 중의 반응은 편집기가 거른다.

    private sealed record PendingSnapshot(
        IReadOnlyDictionary<string, RectNodeState> Overrides,
        IReadOnlyDictionary<(string Node, MotionChannelKind Kind), IReadOnlyList<Axis>> Pending,
        RectNodeTree? Held,
        bool HoldScrub);

    private PendingSnapshot CapturePending()
        => new(
            new Dictionary<string, RectNodeState>(_overrides),
            _pending.ToDictionary(p => p.Key, p => (IReadOnlyList<Axis>)p.Value.ToList()),
            _held,
            _holdScrub);

    private void ApplyPending(PendingSnapshot snapshot)
    {
        _overrides.Clear();

        foreach ((string key, RectNodeState state) in snapshot.Overrides)
        {
            _overrides[key] = state;
        }

        _pending.Clear();

        foreach (((string Node, MotionChannelKind Kind) key, IReadOnlyList<Axis> axes) in snapshot.Pending)
        {
            _pending[key] = new HashSet<Axis>(axes);
        }

        _held = snapshot.Held;
        _holdScrub = snapshot.HoldScrub;
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static bool SamePending(PendingSnapshot a, PendingSnapshot b)
    {
        if (!ReferenceEquals(a.Held, b.Held) || a.HoldScrub != b.HoldScrub || a.Overrides.Count != b.Overrides.Count || a.Pending.Count != b.Pending.Count)
        {
            return false;
        }

        foreach ((string key, RectNodeState state) in a.Overrides)
        {
            if (!b.Overrides.TryGetValue(key, out RectNodeState other) || !state.Equals(other))
            {
                return false;
            }
        }

        foreach (((string Node, MotionChannelKind Kind) key, IReadOnlyList<Axis> axes) in a.Pending)
        {
            if (!b.Pending.TryGetValue(key, out IReadOnlyList<Axis>? others) || !new HashSet<Axis>(axes).SetEquals(others))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>경계 시작 때의 상태와 지금을 견줘 달라졌으면 이력에 올린다. 묶음 안이면 그 칸에 붙는다.</summary>
    private void RecordPendingChange(PendingSnapshot before)
    {
        if (_editor is null)
        {
            return;
        }

        PendingSnapshot after = CapturePending();

        if (SamePending(before, after))
        {
            return;
        }

        _editor.RecordExternal(() => ApplyPending(before), () => ApplyPending(after));
    }

    private void WithPendingHistory(Action operation)
    {
        PendingSnapshot before = CapturePending();
        operation();
        RecordPendingChange(before);
    }

    /// <summary>조작 하나 = 되돌리기 한 칸: 문서 변화와 키 없는 포즈 변화를 같은 칸에.</summary>
    private void AsOneEdit(Action operation)
    {
        if (_editor is null)
        {
            operation();
            return;
        }

        _editor.Batch(MotionChangeKind.Keys, () => WithPendingHistory(operation));
    }

    /// <summary>
    /// Set Key(S). 키 없는 포즈가 있으면 그것을 키로. 없으면 선택 노드의 있는 채널·성분을 지금 포즈 값으로 키(홀드 키).
    /// </summary>
    public void SetKey()
    {
        MotionAsset? motion = EditMotion;

        // 카메라 모션에는 캐릭터의 포즈를 키하지 않는다 — 카메라 값은 SetCameraValue로.
        if (_editor is null || motion is null || CameraRig.IsCamera(motion))
        {
            return;
        }

        // 조작 하나 — 되돌리기 한 칸(성분·노드가 여럿이어도). 되돌리면 키 확정 직전의 키 없는 포즈로.
        AsOneEdit(() => SetKeyCore(motion));
    }

    private void SetKeyCore(MotionAsset motion)
    {
        if (_held is not null)
        {
            KeyHeldPose(motion);
            return;
        }

        if (_pending.Count > 0)
        {
            foreach (((string node, MotionChannelKind kind), HashSet<Axis> axes) in _pending.ToList())
            {
                KeyNow(node, kind, axes.ToList());
            }

            ClearPending();
            return;
        }

        if (SelectedNode is null)
        {
            return;
        }

        foreach (ChannelAsset channel in motion.Channels.Where(c => c.Node == SelectedNode).ToList())
        {
            var axes = new List<Axis>();

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null)
                {
                    axes.Add(axis);
                }
            }

            if (axes.Count > 0)
            {
                KeyNow(channel.Node, channel.Kind, axes);
            }
        }
    }

    /// <summary>들고 있는 포즈를 모든 채널의 있는 성분에 한 조작으로 키(마야: MMB로 옮긴 뒤 S).</summary>
    private void KeyHeldPose(MotionAsset motion)
    {
        RectNodeTree? pose = _held;

        if (_editor is null || _template is null || pose is null)
        {
            ClearPending();
            return;
        }

        var items = new List<(string, MotionChannelKind, IReadOnlyList<(Axis, float)>)>();

        foreach (ChannelAsset channel in motion.Channels)
        {
            string key = Prefix + channel.Node;

            if (!pose.Contains(key))
            {
                continue;
            }

            RectNodeState start = StartStateOf(key);
            RectNodeState posed = pose.GetState(key);
            var values = new List<(Axis, float)>();

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null)
                {
                    values.Add((axis, PoseEdit.KeyValueFor(channel.Kind, EditModeOf(motion), PoseEdit.Component(channel.Kind, start, axis), PoseEdit.Component(channel.Kind, posed, axis))));
                }
            }

            items.Add((channel.Node, channel.Kind, values));
        }

        float time = (float)Math.Max(0, SnapTime(EditTime));
        ClearPending();
        _editor.KeyPose(motion.Name, time, items, DefaultTangent);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// 마야 MMB 스크럽 — 지금 보이는 포즈를 든 채 시각만 옮긴다. 처음 부르면 포즈를 들고, 놓을 때 <see cref="EndHoldScrub"/>.
    /// 든 포즈는 S로 키하거나 Esc·보통 스크럽으로 버린다.
    /// </summary>
    public void HoldScrubTo(double seconds)
    {
        if (_held is null)
        {
            _held = CurrentPose();
            _holdScrub = _held is not null;
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_held is null)
        {
            ScrubTo(seconds);
            return;
        }

        Pause();
        double before = _clock.Time;
        _clock.ScrubTo(seconds);

        if (Math.Abs(before - _clock.Time) > 1e-12)
        {
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    public void EndHoldScrub() => _holdScrub = false;

    /// <summary>지금 포즈의 값으로 키를 놓는다 — 시작값은 덤프(템플릿), 키 값은 모션의 재생 기준(relative/absolute)를 따른다.</summary>
    private void KeyNow(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        MotionAsset? motion = EditMotion;
        RectNodeTree? pose = CurrentPose();

        string key = Prefix + node;

        if (_editor is null || motion is null || CameraRig.IsCamera(motion) || _template is null || pose is null || !pose.Contains(key) || axes.Count == 0)
        {
            return;
        }

        MotionChannelMode mode = EditModeOf(motion);
        RectNodeState start = StartStateOf(key);
        RectNodeState posed = pose.GetState(key);
        var values = new List<(Axis, float)>();

        foreach (Axis axis in axes)
        {
            values.Add((axis, PoseEdit.KeyValueFor(kind, mode, PoseEdit.Component(kind, start, axis), PoseEdit.Component(kind, posed, axis))));
        }

        _overrides.Remove(key);
        _pending.Remove((node, kind));
        _editor.KeyComponents(motion.Name, node, kind, (float)Math.Max(0, SnapTime(EditTime)), values, DefaultTangent);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>선택 키를 띠에 노랗게 보이려고 든다. 그래프·도프시트와 같은 객체를 꽂는다.</summary>
    public KeySelection Selection
    {
        get => _selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_selection, value))
            {
                return;
            }

            _selection.Changed -= OnSelectionChanged;
            _selection = value;
            _selection.Changed += OnSelectionChanged;
            InvalidateVisual();
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    public PlaybackMode Mode
    {
        get => _clock.Mode;
        set
        {
            _clock.Mode = value;
            RangeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>1프레임 단위 재생 — 보이는 시각이 정수 프레임이다. 꺼지면 연속.</summary>
    public bool PlayWholeFrames
    {
        get => _clock.WholeFrames;
        set
        {
            _clock.WholeFrames = value;
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Speed
    {
        get => _clock.Speed;
        set
        {
            _clock.Speed = Math.Clamp(value, 0.05, 8);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetRange(double start, double end)
    {
        _clock.SetRange(start, end);
        RangeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ResetRange()
    {
        _clock.ResetRange();
        RangeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>다음 키 시각으로(`.`). 없으면 그대로.</summary>
    public bool JumpToNextKey()
    {
        if (EditMotion is not { } motion)
        {
            return false;
        }

        float? next = KeyTimes.Next(KeyTimes.All(motion), EditTime);

        if (next is null)
        {
            return false;
        }

        Pause();
        ScrubTo(next.Value + (_clock.Time - EditTime));
        return true;
    }

    /// <summary>이전 키 시각으로(`,`). 없으면 그대로.</summary>
    public bool JumpToPreviousKey()
    {
        if (EditMotion is not { } motion)
        {
            return false;
        }

        float? previous = KeyTimes.Previous(KeyTimes.All(motion), EditTime);

        if (previous is null)
        {
            return false;
        }

        Pause();
        ScrubTo(previous.Value + (_clock.Time - EditTime));
        return true;
    }

    public PlaybackClock Clock => _clock;

    public double Time => _clock.Time;

    public bool IsPlaying => _clock.IsPlaying;

    public bool Loop
    {
        get => _clock.Loop;
        set => _clock.Loop = value;
    }

    /// <summary>축을 그릴 노드. null이면 안 그린다.</summary>
    public string? SelectedNode { get; set; }

    public Vec2 StageSize
    {
        get => _stageSize;
        set
        {
            _stageSize = value.X > 0 && value.Y > 0 ? value : BaseResolutionFile.Default;
            InvalidateVisual();
        }
    }

    public string? MotionName => _motionName;

    public MotionAsset? Motion => _editor is not null && _motionName is not null ? _editor.Document.Find(_motionName) : null;

    /// <summary>보고 있는 연출 클립의 이름(<see cref="BindClip"/>). 모션을 묶으면 비워진다 — 시간의 주인은 둘 중 하나다.</summary>
    public string? ClipName => _clipName;

    private readonly HashSet<string> _mutedClipRoles = new(StringComparer.Ordinal);

    /// <summary>배역 끄기가 바뀌었다 — 타임라인이 다시 그린다.</summary>
    public event EventHandler? ClipMuteChanged;

    /// <summary>
    /// 끈 배역인가 — 그 배역의 블록은 미리보기에서 빠진다(마야의 뮤트). 보기 상태라 문서·되돌리기·산출물에 없고,
    /// 다른 클립을 묶으면 비워진다.
    /// </summary>
    public bool IsClipRoleMuted(string role) => _mutedClipRoles.Contains(role);

    public bool IsClipTrackMuted(ClipTrack track) => track is not null && _mutedClipRoles.Contains(track.Role);

    public void SetClipRoleMuted(string role, bool muted)
    {
        if (muted ? _mutedClipRoles.Add(role) : _mutedClipRoles.Remove(role))
        {
            RebuildTemplate(); // 끈 배역의 상태 블록은 무대에서도 빠진다
            ClipMuteChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>끈 배역을 다 켠다 — 문서를 갈아 끼웠을 때.</summary>
    public void ClearClipMutes()
    {
        _lastClipName = null;

        if (_mutedClipRoles.Count > 0)
        {
            _mutedClipRoles.Clear();
            ClipMuteChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>클립을 보는 중일 때 그 시각의 오브젝트 불투명도(보이기 블록) — 클립이 아니면 씬의 보이기 그대로.</summary>
    public float OpacityOf(string objectId)
        => ShownClip is not null && _clipStage is { } stage
            ? stage.OpacityOf(objectId, _clock.Time)
            : Scene?.Find(objectId) is { Visible: true } ? 1f : 0f;

    /// <summary>카메라 모션을 보는 중일 때 지금 시각의 카메라 값(zoom·pan). 카메라 모션이 아니면 null.</summary>
    public CameraSettings? CameraValue()
    {
        if (Motion is { } motion && CameraRig.IsCamera(motion) && Scene is { } scene)
        {
            return CameraRig.Pose(scene.Camera, motion, _clock.Time, motion.PreviewMode, _flags.IsMuted);
        }

        // 클립 안에서 카메라 모션 블록을 고쳤으면 — 그 시각의 클립 카메라(보이는 그 값).
        return ShownClip is { } clip && CameraContext is not null ? ClipCameraAt(clip, _clock.Time, null) : null;
    }

    /// <summary>
    /// 카메라 값을 지금 시각에 키한다(마야 채널 박스에 값을 넣은 것) — 그 성분의 키 하나, 되돌리기 한 칸.
    /// <paramref name="node"/>는 <see cref="CameraRig.PanNode"/>(x·y) 또는 <see cref="CameraRig.ZoomNode"/>(x). 재생 기준이 상대값이면 씬의 카메라와의 차가 키 값이다.
    /// </summary>
    public void SetCameraValue(string node, Axis axis, float value)
    {
        // 클립 안: 고른 카메라 모션 블록에 키를 놓는다 — 시각은 시계 − 블록의 시작, 재생 기준은 블록의 것,
        // 시작값은 그 시각의 클립 카메라에서 이 블록만 뺀 값(카메라 블록이 보낸 값·앞 카메라 모션이 남긴 값 위에 이 블록이 얹힌다).
        if (_editor is not null && ShownClip is { } clip && CameraContext is { } context)
        {
            CameraSettings before = ClipCameraAt(clip, _clock.Time, context.Block);
            float from = node == CameraRig.ZoomNode ? before.Zoom : axis == Axis.X ? before.PanX : before.PanY;
            float at = (float)Math.Max(0, SnapTime(_clock.Time - context.Block.At));
            _editor.KeyComponents(context.Motion.Name, node, MotionChannelKind.Position, at, new[] { (axis, CameraRig.KeyValueFor(context.Block.Mode, from, value)) }, DefaultTangent);
            return;
        }

        if (_editor is null || Motion is not { } motion || !CameraRig.IsCamera(motion) || Scene is not { } scene)
        {
            return;
        }

        float start = node == CameraRig.ZoomNode ? scene.Camera.Zoom : axis == Axis.X ? scene.Camera.PanX : scene.Camera.PanY;
        float time = (float)Math.Max(0, SnapTime(_clock.Time));
        _editor.KeyComponents(motion.Name, node, MotionChannelKind.Position, time, new[] { (axis, CameraRig.KeyValueFor(motion.PreviewMode, start, value)) }, DefaultTangent);
    }

    /// <summary>씬으로 세운 시작 상태의 트리(읽기 전용으로 쓴다) — 굽기가 표본을 뜨는 바탕.</summary>
    public RectNodeTree? TemplateTree => _template;

    public ClipAsset? ShownClip => _editor is not null && _clipName is not null ? _editor.Document.FindClip(_clipName) : null;

    public bool HasRig => _template is not null;

    /// <summary>선택 오브젝트의 초상 종횡비(없으면 자리표시).</summary>
    public float Aspect => SelectedObject is { } o ? AspectOf(o) : PortraitLayout.PlaceholderAspect;

    // ── 바인딩 ────────────────────────────────────────────────────────

    public void Bind(MotionEditor? editor, string? motionName)
    {
        if (ReferenceEquals(_editor, editor))
        {
            DiscardPending(); // 같은 문서에서 모션을 바꾸는 것 — 버린 포즈도 이력에
        }
        else
        {
            ClearPending(); // 문서가 갈렸다 — 이전 문서의 포즈·이력은 남지 않는다
        }

        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
        }

        _editor = editor;
        _motionName = motionName;
        _clipName = null;
        _contextBlock = null;

        if (_editor is not null)
        {
            _editor.Changed += OnEditorChanged;
        }

        _clock.Stop();
        _timer.Stop();
        SyncDuration();
        PickSelectedObject();
        RebuildTemplate();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// 연출 클립을 본다(<c>PLAN-clip.md</c> K3) — 시계의 길이는 클립 길이, 포즈는 <see cref="ClipPreview.Pose"/>(출연자 전부).
    /// 모션은 묶이지 않는다. 포즈는 <see cref="ClipBlockContext"/>로 고른 블록 안에서만 잡는다 — 그 밖에서는 매니퓰레이터·축을 그리지 않고 끌기도 받지 않는다.
    /// </summary>
    public void BindClip(MotionEditor? editor, string? clipName)
    {
        bool same = ReferenceEquals(_editor, editor) && _lastClipName == clipName;
        Bind(editor, null);
        _clipName = clipName;

        if (!same && _mutedClipRoles.Count > 0)
        {
            _mutedClipRoles.Clear();
            ClipMuteChanged?.Invoke(this, EventArgs.Empty);
        }

        _lastClipName = clipName ?? _lastClipName;
        SyncDuration();
        PickSelectedObject();
        RebuildTemplate();
        TimeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetRig(RigSchemaFile? rig)
    {
        _rig = rig;
        _stage = rig is null ? null : BuiltinTuning.StageRig;
        PickSelectedObject();
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>카메라가 트리(뼈대의 Zoom/Pan 노드)에 들어가 있나. 뼈대가 없으면 보기 변환으로 입힌다(같은 식).</summary>
    public bool CameraInTree => _stage is not null;

    /// <summary>presets/depth.json — 레이어를 깊이(DepthY·DepthScale)로. null이면 레이어는 그리는 순서만.</summary>
    public void SetDepth(DepthTuningFile? depth)
    {
        _depth = depth;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>presets/focus.json — focus 오프셋(base + bust)이 깊이·place 폴드에 들어간다.</summary>
    public void SetFocus(FocusTuningFile? focus)
    {
        _focus = focus;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>씬 카메라(보기 전용) 배율 — 코어 규약 zoom 1당 +0.05.</summary>
    public float CameraScale => ShotIntentMath.EvaluateCameraScale(Scene?.Camera.Zoom ?? 0f);

    private Vec2 CameraPan => Scene is { } scene ? new Vec2(scene.Camera.PanX, scene.Camera.PanY) : Vec2.Zero;

    /// <summary>루트 공간 → 컨트롤 좌표(카메라 적용). 테스트가 쓴다.</summary>
    public Point ToScreen(Vec2 root) => ToControl(root);

    /// <summary>선택 오브젝트의 초상 비트맵(호환용). null이면 자리표시 사각형.</summary>
    public void SetPortrait(Bitmap? portrait)
    {
        if (SelectedObject is { } o)
        {
            SetPortrait(o.Id, portrait);
        }
    }

    /// <summary>오브젝트의 초상 비트맵. 종횡비는 비트맵에서 온다.</summary>
    public void SetPortrait(string objectId, Bitmap? portrait)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        _portraits[objectId] = portrait;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>조각의 그림 비트맵(스킨이 준 것). 못 읽었으면 null을 넣는다 — 그 조각은 자리표시로 그려진다.</summary>
    public void SetPartImage(string objectId, string node, Bitmap? image)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        ArgumentNullException.ThrowIfNull(node);
        _partImages[(objectId, node)] = image;
        InvalidateVisual();
    }

    /// <summary>
    /// 그 오브젝트에서 그려지는 조각 노드들(그리는 순서) — 리그의 조각 가운데 입은 스킨이 그림을 준 것. 조각이 없는 리그나 스킨을 안 입었으면 빈 목록.
    /// </summary>
    public IReadOnlyList<string> DrawnParts(SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(o);

        if (_editor?.Document.FindSkin(o.Skin) is not { } skin || !string.Equals(skin.Rig, o.Rig, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return RigParts.Of(_rig, o.Rig).Where(skin.Parts.ContainsKey).ToList();
    }

    /// <summary>조각이 지금 그려지는 사각형(루트 공간) — 노드의 rect 안에 그림의 종횡비로 맞춘 것(비트맵이 없으면 rect 그대로). 없으면 null.</summary>
    public Quad? PartQuad(string objectId, string node)
        => CurrentPose() is { } pose && Scene?.Find(objectId) is { } o ? PartQuadOf(pose, o, node) : null;

    private Quad? PartQuadOf(RectNodeTree pose, SceneObject o, string node)
    {
        string key = o.KeyOf(node);

        if (!pose.Contains(key))
        {
            return null;
        }

        Quad rect = PortraitLayout.RectQuad(pose, key);

        if (!_partImages.TryGetValue((o.Id, node), out Bitmap? image) || image is not { PixelSize: { Width: > 0, Height: > 0 } })
        {
            return rect;
        }

        // 노드 rect 안에 그림을 찌그러뜨리지 않고 넣는다(가운데, contain) — rect의 두 변을 따라 줄인다.
        float w = Norm(rect.BottomRight - rect.BottomLeft);
        float h = Norm(rect.TopLeft - rect.BottomLeft);

        static float Norm(Vec2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

        if (w <= 1e-6f || h <= 1e-6f)
        {
            return rect;
        }

        float aspect = image.PixelSize.Width / (float)image.PixelSize.Height;
        float fitW = Math.Min(w, h * aspect);
        float fitH = fitW / aspect;
        Vec2 ux = (rect.BottomRight - rect.BottomLeft) * (1f / w);
        Vec2 uy = (rect.TopLeft - rect.BottomLeft) * (1f / h);
        Vec2 origin = rect.BottomLeft + ux * ((w - fitW) / 2f) + uy * ((h - fitH) / 2f);
        return new Quad(origin, origin + ux * fitW, origin + ux * fitW + uy * fitH, origin + uy * fitH);
    }

    public void ClearPortraits()
    {
        _partImages.Clear();
        _portraits.Clear();
        RebuildTemplate();
        InvalidateVisual();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        SyncDuration();

        if (e.Kind != MotionChangeKind.Keys)
        {
            PickSelectedObject();
            RebuildTemplate();
        }

        InvalidateVisual();
    }

    /// <summary>선택 오브젝트가 없거나 씬에서 사라졌으면 — 모션을 입을 수 있는 첫 오브젝트, 없으면 첫 오브젝트.</summary>
    private void PickSelectedObject()
    {
        SceneAsset? scene = Scene;

        if (scene is null || scene.Objects.Count == 0)
        {
            SelectedObjectId = null;
            return;
        }

        if (_selectedObjectId is not null && scene.Find(_selectedObjectId) is not null)
        {
            return;
        }

        SceneObject pick = scene.Objects.FirstOrDefault(CanWear) ?? scene.Objects[0];
        SelectedObjectId = pick.Id;
    }

    private void SyncDuration()
    {
        if (Motion is { } motion)
        {
            _clock.SetDuration(motion.Duration);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (_editor is not null && ShownClip is { } clip)
        {
            _clock.SetDuration(ClipTiming.DurationOf(_editor.Document, clip));
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildTemplate()
    {
        _template = null;
        _clipStage = null;
        SceneAsset? scene = Scene;

        if (_rig is null || scene is null)
        {
            return;
        }

        _template = ScenePreview.Build(_rig, scene, AspectOf, null, _depth, _focus, _stage);

        // 클립을 보는 중이면 상태 블록의 단계도 같이 세운다 — 문서·자산·끈 배역이 바뀔 때만 다시 세우고 프레임마다는 섞기만 한다.
        if (_editor is not null && ShownClip is { } clip)
        {
            RigSchemaFile rig = _rig;
            _clipStage = ClipStage.Build(_editor.Document, clip, s => ScenePreview.Build(rig, s, AspectOf, null, _depth, _focus, _stage), IsClipTrackMuted);
        }
    }

    // ── 시계 ──────────────────────────────────────────────────────────

    public void Play()
    {
        _clock.Play();
        _stopwatch.Restart();
        _timer.Start();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _clock.Pause();
        _timer.Stop();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>거꾸로 재생(마야 ◀).</summary>
    public void PlayBackward()
    {
        _clock.PlayBackward();
        _stopwatch.Restart();
        _timer.Start();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>처음부터 재생(Alt+`) — 범위 시작으로 간 뒤 재생. 이미 재생 중이면 처음으로 되감는다.</summary>
    public void PlayFromStart()
    {
        Stop();
        Play();
    }

    public void TogglePlay()
    {
        if (_clock.IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        _clock.Stop();
        _timer.Stop();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ScrubTo(double seconds)
    {
        double before = _clock.Time;
        _clock.ScrubTo(seconds);

        if (Math.Abs(before - _clock.Time) > 1e-12)
        {
            // 포즈 들고 스크럽 중(MMB)이면 든 포즈를 지키고, 보통 스크럽은 키 없는 포즈를 버린다(마야).
            if (!_holdScrub)
            {
                DiscardPending();
            }

            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    /// <summary>시계를 손으로 민다 — 타이머 대신 테스트가 부른다.</summary>
    public void Advance(double seconds)
    {
        if (_clock.Advance(seconds))
        {
            DiscardPending();
            TimeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        if (!_clock.IsPlaying)
        {
            _timer.Stop();
            PlayStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTick()
    {
        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();
        Advance(elapsed);
    }

    // ── 포즈 조회 (테스트·인스펙터) ───────────────────────────────────

    public RectNodeTree? PoseAt(double seconds)
    {
        MotionAsset? motion = Motion;
        if (_template is null)
        {
            return null;
        }

        if (_editor is not null && ShownClip is { } clip)
        {
            // 무대(상태 블록)가 먼저 서고 모션이 그 위에 얹힌다.
            return ClipPreview.Pose(_clipStage?.TreeAt(seconds) ?? _template, _editor.Document, clip, seconds, IsClipTrackMuted, _clipStage?.CameraAt(seconds));
        }

        // 카메라 모션 — 씬의 카메라 값에 모션을 얹어 무대의 카메라 노드를 다시 세운다. 캐릭터는 시작 상태로 선다.
        if (CameraRig.IsCamera(motion) && Scene is { } cameraScene)
        {
            RectNodeTree tree = _template.Clone();
            StageShotResponse.Apply(tree, CameraRig.Pose(cameraScene.Camera, motion!, seconds, motion!.PreviewMode, _flags.IsMuted));
            return tree;
        }

        return motion is not null && SelectedObject is { } o && CanWear(o)
            ? ScenePreview.Pose(_template, o, motion, seconds, _flags.IsMuted)
            : _template.Clone();
    }

    /// <summary>지금 시각의 초상 사각형(루트 공간). 리그가 없으면 null.</summary>
    public Quad? CurrentPortraitQuad()
    {
        return SelectedObject is { } o ? ObjectQuad(o.Id) : null;
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    /// <summary>무대가 놓이는 컨트롤 영역(레터박스).</summary>
    public Rect StageRect
    {
        get
        {
            double availableWidth = Math.Max(1, Bounds.Width - Pad * 2);
            double availableHeight = Math.Max(1, Bounds.Height - Pad * 2);
            double scale = Math.Min(availableWidth / _stageSize.X, availableHeight / _stageSize.Y) * _viewZoom;
            double width = _stageSize.X * scale;
            double height = _stageSize.Y * scale;
            double centerX = Bounds.Width / 2 + _viewPan.X;
            double centerY = Bounds.Height / 2 + _viewPan.Y;
            return new Rect(centerX - width / 2, centerY - height / 2, width, height);
        }
    }

    // 카메라(샷): 보이는 좌표 = 루트 × 배율 + pan (코어 ShotIntentMath와 같은 규약). 보기만 바뀐다.
    private Point ToControl(Vec2 root)
    {
        Rect stage = StageRect;
        Vec2 shot = CameraInTree ? root : root * CameraScale + CameraPan;
        Vec2 canvas = PortraitLayout.ToCanvas(shot, _stageSize);
        double scale = stage.Width / _stageSize.X;
        return new Point(stage.X + canvas.X * scale, stage.Y + canvas.Y * scale);
    }

    /// <summary>컨트롤 좌표 → 루트 공간(가운데 원점·y 위). 카메라를 벗긴다.</summary>
    public Vec2 ToRoot(Point control)
    {
        Rect stage = StageRect;
        double scale = stage.Width / _stageSize.X;
        double canvasX = (control.X - stage.X) / scale;
        double canvasY = (control.Y - stage.Y) / scale;
        var shot = new Vec2((float)(canvasX - _stageSize.X / 2), (float)(_stageSize.Y / 2 - canvasY));
        return CameraInTree ? shot : (shot - CameraPan) * (1f / CameraScale);
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        Rect stage = StageRect;
        context.FillRectangle(StageBrush, stage);
        context.DrawRectangle(StagePen, stage);

        MotionAsset? motion = Motion;

        if (ShowGrid)
        {
            using (context.PushClip(stage))
            {
                DrawWorld(context, stage);
            }
        }

        if (_template is null)
        {
            DrawLabel(context, _rig is null ? "리그 덤프(rig-schemas.json)를 실으면 오브젝트가 선다." : "씬에 오브젝트가 없다 — 왼쪽 '씬'에서 추가한다.", new Point(stage.X + 10, stage.Y + 10));
            return;
        }

        // 오브젝트는 무대 밖도 그대로 그리고, 카메라에 안 잡히는 바깥을 회색으로 덮어 가른다. 축·손잡이는 그 위에.
        RectNodeTree pose = CurrentPose() ?? _template;
        DrawObjects(context, pose);
        DrawOutside(context, stage);
        context.DrawRectangle(StagePen, stage);
        if (CameraRig.IsCamera(motion) && CameraValue() is { } shot)
        {
            DrawLabel(context, $"카메라 모션 {motion!.Name} · zoom {shot.Zoom:0.##} · pan {shot.PanX:0.#}, {shot.PanY:0.#}", new Point(stage.X + 8, stage.Y + 6));
            return;
        }

        if (ShownClip is { } shown)
        {
            // 고른 블록 안이면 그 캐릭터에 손잡이가 선다 — 클립 안에서 바로 포즈를 잡는다.
            if (EditMotion is { } editing && ContextEntry is { } entry)
            {
                DrawAxes(context, pose);
                DrawGizmo(context, pose);
                DrawLabel(context, $"클립 {shown.Name} › {editing.Name} @ {entry.Block.At:0.###}s · {MotionModeLabel.Of(entry.Block.Mode)} — 이 블록의 모션에 키가 놓인다", new Point(stage.X + 8, stage.Y + 6));
                return;
            }

            if (CameraContext is { } cameraEdit)
            {
                DrawLabel(context, $"클립 {shown.Name} › 카메라 모션 {cameraEdit.Motion.Name} @ {cameraEdit.Block.At:0.###}s · {MotionModeLabel.Of(cameraEdit.Block.Mode)} — '모션' 탭의 카메라 칸이 이 블록에 키를 놓는다", new Point(stage.X + 8, stage.Y + 6));
                return;
            }

            DrawLabel(context, $"클립 {shown.Name} · {_clock.Duration:0.###}s · 출연 {shown.Roles.Count}", new Point(stage.X + 8, stage.Y + 6));
            return;
        }

        DrawAxes(context, pose);
        DrawGizmo(context, pose);

        string objectLabel = SelectedObject is { } so ? $"{so.Id} ({so.Rig} · {so.Layer})" : "오브젝트 없음";
        string motionLabel = motion is null
            ? "모션 없음"
            : $"{motion.Name} · {(motion.End == MotionEnd.Restore ? "restore — 끝에서 시작 상태로" : "commit — 마지막 키가 남는다")}"
              + (SelectedObject is { } wearer && !CanWear(wearer) ? " · 이 오브젝트의 리그에는 안 맞는다" : "");
        DrawLabel(context, $"{objectLabel} · {motionLabel}", new Point(stage.X + 8, stage.Y + 6));
    }

    /// <summary>무대 프레임 바깥(카메라가 안 잡는 곳)을 회색으로 덮는다 — 네 띠.</summary>
    private void DrawOutside(DrawingContext context, Rect stage)
    {
        var bounds = new Rect(Bounds.Size);
        double left = Math.Max(bounds.X, stage.X);
        double right = Math.Min(bounds.Right, stage.Right);
        double top = Math.Max(bounds.Y, stage.Y);
        double bottom = Math.Min(bounds.Bottom, stage.Bottom);

        if (right <= left || bottom <= top)
        {
            context.FillRectangle(OutsideBrush, bounds);
            return;
        }

        context.FillRectangle(OutsideBrush, new Rect(bounds.X, bounds.Y, bounds.Width, top - bounds.Y));
        context.FillRectangle(OutsideBrush, new Rect(bounds.X, bottom, bounds.Width, bounds.Bottom - bottom));
        context.FillRectangle(OutsideBrush, new Rect(bounds.X, top, left - bounds.X, bottom - top));
        context.FillRectangle(OutsideBrush, new Rect(right, top, bounds.Right - right, bottom - top));
    }

    /// <summary>월드 축·격자·무대 프레임 — 루트 공간(가운데 원점·y 위, px). 가는 선 1u(기준 폭/48), 굵은 선 5u.</summary>
    private void DrawWorld(DrawingContext context, Rect stage)
    {
        double unit = _stageSize.X / 48.0;
        double scale = stage.Width / _stageSize.X;
        double unitPx = unit * scale;

        if (unitPx >= 4)
        {
            Point origin = ToControl(Vec2.Zero);
            int countX = (int)Math.Ceiling(_stageSize.X / 2 / unit) + 1;
            int countY = (int)Math.Ceiling(_stageSize.Y / 2 / unit) + 1;

            for (int i = -countX; i <= countX; i++)
            {
                double x = origin.X + i * unitPx;
                context.DrawLine(i % 5 == 0 ? GridMajorPen : GridMinorPen, new Point(x, stage.Y), new Point(x, stage.Bottom));
            }

            for (int j = -countY; j <= countY; j++)
            {
                double y = origin.Y - j * unitPx;
                context.DrawLine(j % 5 == 0 ? GridMajorPen : GridMinorPen, new Point(stage.X, y), new Point(stage.Right, y));
            }

            context.DrawLine(WorldXPen, new Point(stage.X, origin.Y), new Point(stage.Right, origin.Y));
            context.DrawLine(WorldYPen, new Point(origin.X, stage.Y), new Point(origin.X, stage.Bottom));
            context.DrawEllipse(null, new Pen(PivotBrush, 1), origin, 5, 5);
            DrawLabel(context, $"0,0 · 1u = {unit:0}px", new Point(origin.X + 6, origin.Y + 4));
        }

        context.DrawRectangle(StagePen, stage);
    }

    private void DrawObjects(DrawingContext context, RectNodeTree pose)
    {
        SceneAsset? scene = Scene;

        if (scene is null)
        {
            return;
        }

        // 클립의 상태 블록이 있으면 그 시각의 씬(층·그리는 순서)과 불투명도(보이기 블록의 페이드)로 그린다.
        if (ShownClip is not null && _clipStage is { } stage)
        {
            foreach (SceneObject o in stage.SceneAt(_clock.Time).DrawOrder())
            {
                float opacity = stage.OpacityOf(o.Id, _clock.Time);

                if (opacity <= 0.001f)
                {
                    continue;
                }

                using (context.PushOpacity(opacity))
                {
                    DrawObject(context, pose, o, selected: o.Id == _selectedObjectId);
                }
            }

            return;
        }

        foreach (SceneObject o in scene.DrawOrder())
        {
            if (!o.Visible)
            {
                continue;
            }

            DrawObject(context, pose, o, selected: o.Id == _selectedObjectId);
        }
    }

    /// <summary>사각형 하나에 그림을 그린다 — 비트맵이 없으면 자리표시(X 표 사각형).</summary>
    private void DrawImageQuad(DrawingContext context, Quad quad, Bitmap? image)
    {
        Point topLeft = ToControl(quad.TopLeft);
        Point topRight = ToControl(quad.TopRight);
        Point bottomLeft = ToControl(quad.BottomLeft);
        Point bottomRight = ToControl(quad.BottomRight);

        if (image is null)
        {
            context.DrawGeometry(PlaceholderFill, PlaceholderPen, new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true));
            context.DrawLine(PlaceholderPen, topLeft, bottomRight);
            context.DrawLine(PlaceholderPen, topRight, bottomLeft);
            return;
        }

        double w = image.PixelSize.Width;
        double h = image.PixelSize.Height;
        var matrix = new Matrix(
            (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
            (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
            topLeft.X, topLeft.Y);

        using (context.PushTransform(matrix))
        {
            context.DrawImage(image, new Rect(0, 0, w, h));
        }
    }

    private void DrawObject(DrawingContext context, RectNodeTree pose, SceneObject o, bool selected)
    {
        // 조각이 있는 리그(이모지 등) — 스킨이 그림을 준 조각만, 덤프 순서로 그린다. 스킨을 안 입었으면 아래의 윤곽으로 떨어진다.
        if (_rig is not null && RigParts.Of(_rig, o.Rig).Count > 0 && DrawnParts(o) is { Count: > 0 } parts)
        {
            foreach (string node in parts)
            {
                if (PartQuadOf(pose, o, node) is { } part)
                {
                    _partImages.TryGetValue((o.Id, node), out Bitmap? image);
                    DrawImageQuad(context, part, image);
                }
            }

            if (selected && PartQuadOf(pose, o, parts[0]) is { } outline)
            {
                context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(
                    new[] { ToControl(outline.TopLeft), ToControl(outline.TopRight), ToControl(outline.BottomRight), ToControl(outline.BottomLeft), ToControl(outline.TopLeft) }, true));
            }

            return;
        }

        string? imageKey = ScenePreview.ImageKeyOf(pose, o);

        if (imageKey is null)
        {
            // 그림 노드가 없는 리그(배경 등): 루트 rect 윤곽과 이름.
            string rootKey = ScenePreview.RootKeyOf(o);

            if (!pose.Contains(rootKey))
            {
                return;
            }

            Quad root = PortraitLayout.RectQuad(pose, rootKey);
            Point a = ToControl(root.BottomLeft);
            Point b = ToControl(root.BottomRight);
            Point c = ToControl(root.TopRight);
            Point d = ToControl(root.TopLeft);
            context.DrawGeometry(null, selected ? SelectedObjectPen : ObjectOutlinePen, new PolylineGeometry(new[] { a, b, c, d, a }, true));
            DrawLabel(context, $"{o.Id} ({o.Rig})", new Point(Math.Min(a.X, d.X) + 4, Math.Min(c.Y, d.Y) + 4));
            return;
        }

        Quad quad = PortraitLayout.VisualQuad(pose, imageKey, AspectOf(o));
        Point topLeft = ToControl(quad.TopLeft);
        Point topRight = ToControl(quad.TopRight);
        Point bottomLeft = ToControl(quad.BottomLeft);
        Point bottomRight = ToControl(quad.BottomRight);
        _portraits.TryGetValue(o.Id, out Bitmap? portrait);

        if (portrait is null)
        {
            var outline = new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true);
            context.DrawGeometry(PlaceholderFill, PlaceholderPen, outline);
            context.DrawLine(PlaceholderPen, topLeft, bottomRight);
            context.DrawLine(PlaceholderPen, topRight, bottomLeft);
        }
        else
        {
            // 이미지 로컬 (0,0)-(w,h) → 화면 사각형 네 점. 직교 투영 아래서 평면 사각형의 변환은 아핀이라 세 점이면 정확하다.
            double w = portrait.PixelSize.Width;
            double h = portrait.PixelSize.Height;
            var matrix = new Matrix(
                (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
                (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
                topLeft.X, topLeft.Y);

            using (context.PushTransform(matrix))
            {
                context.DrawImage(portrait, new Rect(0, 0, w, h));
            }
        }

        if (selected)
        {
            context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true));
        }

        DrawLabel(context, o.Id, new Point(Math.Min(topLeft.X, bottomLeft.X) + 4, Math.Min(topLeft.Y, topRight.Y) + 4));
    }

    private void DrawAxes(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedKey is not { } key || !pose.Contains(key))
        {
            return;
        }

        (Vec2 origin, Vec2 xEnd, Vec2 yEnd) = PortraitLayout.Axes(pose, key, 80f);
        Point o = ToControl(origin);
        context.DrawLine(XAxisPen, o, ToControl(xEnd));
        context.DrawLine(YAxisPen, o, ToControl(yEnd));
        context.DrawEllipse(PivotBrush, null, o, 4, 4);
        DrawLabel(context, key, new Point(o.X + 6, o.Y + 6));
    }

    private void DrawGizmo(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedKey is not { } gizmoKey || !pose.Contains(gizmoKey) || Manipulator == ManipulatorMode.Select)
        {
            return;
        }

        (Point center, Point xEnd, Point yEnd) = GizmoPoints(pose);

        if (_pending.Count > 0)
        {
            context.DrawEllipse(null, PendingPen, center, GizmoArm + 8, GizmoArm + 8);
        }

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
                // 이동 = 화살표(마야). 가운데 네모는 자유 이동.
                context.DrawLine(XAxisPen, center, xEnd);
                context.DrawLine(YAxisPen, center, yEnd);
                context.DrawGeometry(GizmoXBrush, null, ArrowHead(center, xEnd));
                context.DrawGeometry(GizmoYBrush, null, ArrowHead(center, yEnd));
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case ManipulatorMode.Scale:
                // 배율 = 끝에 상자(마야). 가운데 상자는 양쪽 같이.
                context.DrawLine(XAxisPen, center, xEnd);
                context.DrawLine(YAxisPen, center, yEnd);
                context.FillRectangle(GizmoXBrush, new Rect(xEnd.X - 5, xEnd.Y - 5, 10, 10));
                context.FillRectangle(GizmoYBrush, new Rect(yEnd.X - 5, yEnd.Y - 5, 10, 10));
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case ManipulatorMode.Rotate:
                context.DrawEllipse(null, GizmoRingPen, center, GizmoArm, GizmoArm);
                context.DrawEllipse(GizmoCenterBrush, null, center, 4, 4);
                break;
        }
    }

    /// <summary>팔 끝의 화살촉(삼각형) — 길이 14, 폭 10, 끝이 팔 끝에 온다.</summary>
    private static PolylineGeometry ArrowHead(Point from, Point tip)
    {
        double dx = tip.X - from.X;
        double dy = tip.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-6)
        {
            dx = 1;
            dy = 0;
            length = 1;
        }

        double ux = dx / length;
        double uy = dy / length;
        var back = new Point(tip.X - ux * 14, tip.Y - uy * 14);
        var left = new Point(back.X - uy * 5, back.Y + ux * 5);
        var right = new Point(back.X + uy * 5, back.Y - ux * 5);
        return new PolylineGeometry(new[] { tip, left, right, tip }, true);
    }

    /// <summary>매니퓰레이터의 중심과 x·y 팔 끝(컨트롤 좌표, 팔 길이는 화면 고정). 팔 방향은 노드의 축, 중심은 <see cref="GizmoAnchor"/>.</summary>
    private (Point Center, Point XEnd, Point YEnd) GizmoPoints(RectNodeTree pose)
    {
        (Vec2 origin, Vec2 xAxis, Vec2 yAxis) = PortraitLayout.Axes(pose, SelectedKey!, 100f);
        Vec2 anchor = GizmoAnchor(pose, SelectedKey!);
        Point center = ToControl(anchor);
        return (center, Along(center, ToControl(anchor + (xAxis - origin))), Along(center, ToControl(anchor + (yAxis - origin))));
    }

    private static Point Along(Point from, Point towards)
    {
        double dx = towards.X - from.X;
        double dy = towards.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-6)
        {
            return new Point(from.X + GizmoArm, from.Y);
        }

        return new Point(from.X + dx / length * GizmoArm, from.Y + dy / length * GizmoArm);
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
        context.DrawText(formatted, at);
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point position = e.GetPosition(this);

        // MMB·RMB 드래그 = 보기 이동(팬). 씬·키는 안 건드린다. (소유자: 그래프와 같이 둘 다)
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;

        if (pressed.IsMiddleButtonPressed || pressed.IsRightButtonPressed)
        {
            _panning = true;
            _panPointerStart = position;
            _panStart = _viewPan;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!pressed.IsLeftButtonPressed)
        {
            return;
        }

        // 클립 층에서는 고른 블록 안에서만 포즈를 잡는다(EditMotion). 카메라 모션에는 캐릭터의 포즈를 키하지 않는다.
        bool canPose = ShownClip is null ? !CameraRig.IsCamera(Motion) : EditMotion is not null && ContextEntry is not null;

        if (canPose && TryStartManipulation(position))
        {
            Pause();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (TryPickObject(position, out string picked))
        {
            SelectedObjectId = picked;
            e.Handled = true;
        }
    }

    /// <summary>클릭 자리의 오브젝트(앞 레이어 우선). 초상 사각형 안이면 맞은 것.</summary>
    private bool TryPickObject(Point position, out string objectId)
    {
        objectId = "";
        RectNodeTree? pose = CurrentPose();
        SceneAsset? scene = Scene;

        if (pose is null || scene is null)
        {
            return false;
        }

        var at = new Pixel(position.X, position.Y);

        foreach (SceneObject o in scene.DrawOrder().Reverse())
        {
            if (OpacityOf(o.Id) <= 0.001f)
            {
                continue;
            }

            if (ObjectQuad(o.Id) is not { } quad)
            {
                continue;
            }

            var polygon = new List<Pixel>();

            foreach (Vec2 corner in new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft })
            {
                Point p = ToControl(corner);
                polygon.Add(new Pixel(p.X, p.Y));
            }

            if (GraphHitTest.Contains(polygon, at))
            {
                objectId = o.Id;
                return true;
            }
        }

        return false;
    }

    private bool TryStartManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (Manipulator == ManipulatorMode.Select || SelectedKey is not { } key || pose is null || !pose.Contains(key))
        {
            return false;
        }

        (Point center, Point xEnd, Point yEnd) = GizmoPoints(pose);
        double toCenter = Distance(position, center);
        Drag drag = Drag.None;

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
                drag = Distance(position, xEnd) <= GizmoHit ? Drag.MoveX
                    : Distance(position, yEnd) <= GizmoHit ? Drag.MoveY
                    : toCenter <= GizmoHit + 2 ? Drag.MoveFree : Drag.None;
                break;
            case ManipulatorMode.Scale:
                drag = Distance(position, xEnd) <= GizmoHit ? Drag.ScaleX
                    : Distance(position, yEnd) <= GizmoHit ? Drag.ScaleY
                    : toCenter <= GizmoHit + 2 ? Drag.ScaleFree : Drag.None;
                break;
            case ManipulatorMode.Rotate:
                drag = Math.Abs(toCenter - GizmoArm) <= GizmoHit ? Drag.Rotate : Drag.None;
                break;
        }

        if (drag == Drag.None)
        {
            return false;
        }

        _drag = drag;
        _dragStartRoot = ToRoot(position);
        _dragStartControl = position;
        _dragPivotRoot = GizmoAnchor(pose, key);
        // 배율은 팔 방향(화면)으로 잰다 — 자유(가운데)는 x 팔 방향.
        Point arm = drag == Drag.ScaleY ? yEnd : xEnd;
        _dragScaleDirection = new Vec2((float)(arm.X - center.X), (float)(arm.Y - center.Y));
        _dragStartState = pose.GetState(key);
        _dragPendingBefore = CapturePending();
        return true;
    }

    private void DragManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (SelectedKey is not { } key || pose is null || !pose.Contains(key))
        {
            return;
        }

        Vec2 now = ToRoot(position);
        RectNodeState start = _dragStartState;
        RectNodeState next;

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            {
                Vec2 delta = PoseEdit.RootDeltaToLocal(pose, key, _dragStartRoot, now);
                float dx = _drag == Drag.MoveY ? 0f : delta.X;
                float dy = _drag == Drag.MoveX ? 0f : delta.Y;
                next = start.WithAnchoredPosition(new Vec2(start.AnchoredPosition.X + dx, start.AnchoredPosition.Y + dy));
                break;
            }

            case Drag.Rotate:
            {
                float angle = PoseEdit.AngleDelta(_dragPivotRoot, _dragStartRoot, now);
                Vec3 euler = start.LocalEulerAngles;
                next = start.WithLocalEuler(new Vec3(euler.X, euler.Y, euler.Z + angle));
                break;
            }

            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
            {
                var fromControl = new Vec2((float)_dragStartControl.X, (float)_dragStartControl.Y);
                var toControl = new Vec2((float)position.X, (float)position.Y);
                float factor = PoseEdit.ScaleAlong(_dragScaleDirection, fromControl, toControl, (float)GizmoArm);
                float sx = _drag == Drag.ScaleY ? start.LocalScale.X : start.LocalScale.X * factor;
                float sy = _drag == Drag.ScaleX ? start.LocalScale.Y : start.LocalScale.Y * factor;
                next = start.WithLocalScale(new Vec3(sx, sy, start.LocalScale.Z));
                break;
            }

            default:
                return;
        }

        _overrides[key] = next;
        InvalidateVisual();
    }

    /// <summary>놓는 순간: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void EndManipulation()
    {
        MotionAsset? motion = EditMotion;
        string? node = SelectedNode;

        if (motion is null || node is null || !_overrides.TryGetValue(Prefix + node, out RectNodeState posed))
        {
            return;
        }

        (MotionChannelKind kind, List<Axis> axes) = ChangedAxes(_dragStartState, posed);

        if (axes.Count == 0)
        {
            _overrides.Remove(Prefix + node);
            InvalidateVisual();
            return;
        }

        CommitPose(motion, node, kind, axes);
    }

    /// <summary>바뀐 포즈의 처리: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void CommitPose(MotionAsset motion, string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        ChannelAsset? channel = motion.FindChannel(node, kind);
        float time = (float)SnapTime(EditTime);
        bool keyExists = channel is not null && axes.Any(a => channel.Get(a)?.Keys.Any(k => Math.Abs(k.T - time) <= MotionRules.Tolerance) == true);

        if (AutoKey || keyExists)
        {
            KeyNow(node, kind, axes);
            return;
        }

        if (!_pending.TryGetValue((node, kind), out HashSet<Axis>? pendingAxes))
        {
            pendingAxes = new HashSet<Axis>();
            _pending[(node, kind)] = pendingAxes;
        }

        pendingAxes.UnionWith(axes);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ── 채널 박스(마야) ───────────────────────────────────────────────

    /// <summary>선택 오브젝트의 노드 성분의 지금 포즈 값(키 없는 포즈 포함). 노드·리그가 없으면 null.</summary>
    public float? ComponentValue(string node, MotionChannelKind kind, Axis axis)
    {
        RectNodeTree? pose = CurrentPose();
        string key = Prefix + node;

        if (pose is null || !pose.Contains(key))
        {
            return null;
        }

        return PoseEdit.Component(kind, pose.GetState(key), axis);
    }

    /// <summary>이 성분이 키 없는 포즈로 남아 있는가(채널 박스가 주황으로 보인다).</summary>
    public bool IsPending(string node, MotionChannelKind kind, Axis axis)
        => _pending.TryGetValue((node, kind), out HashSet<Axis>? axes) && axes.Contains(axis);

    /// <summary>
    /// 채널 박스 입력 — 노드 성분 하나를 이 값으로. 매니퓰레이터를 놓은 것과 같다:
    /// Auto Key거나 그 시각에 키가 있으면 키로, 아니면 키 없는 포즈(S로 키·Esc로 버림). 값이 같으면 아무것도 안 한다.
    /// </summary>
    public void SetComponent(string node, MotionChannelKind kind, Axis axis, float value)
        => SetComponents(node, kind, new[] { (axis, value) });

    /// <summary>여러 성분을 한 번에(깊이 컨트롤러의 X·Y 배율처럼). 바뀐 성분만 키·키 없는 포즈가 된다.</summary>
    public void SetComponents(string node, MotionChannelKind kind, IReadOnlyList<(Axis Axis, float Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        MotionAsset? motion = EditMotion;
        RectNodeTree? pose = CurrentPose();
        string key = Prefix + node;

        if (motion is null || CameraRig.IsCamera(motion) || pose is null || !pose.Contains(key) || MotionRules.ForbiddenNodes.Contains(node))
        {
            return;
        }

        RectNodeState state = pose.GetState(key);
        var changed = new List<Axis>();

        foreach ((Axis axis, float value) in values)
        {
            if (Math.Abs(PoseEdit.Component(kind, state, axis) - value) < 1e-6f)
            {
                continue;
            }

            state = PoseEdit.WithComponent(kind, state, axis, value);
            changed.Add(axis);
        }

        if (changed.Count == 0)
        {
            return;
        }

        AsOneEdit(() =>
        {
            _overrides[key] = state;
            InvalidateVisual();
            CommitPose(motion, node, kind, changed);
        });
    }

    /// <summary>채널 박스의 ● — 성분 하나를 지금 포즈 값으로 지금 키(마야 Key Selected). 없는 채널·성분은 생긴다.</summary>
    public void KeyComponent(string node, MotionChannelKind kind, Axis axis) => KeyComponents(node, kind, new[] { axis });

    public void KeyComponents(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        if (MotionRules.ForbiddenNodes.Contains(node) || axes.Count == 0)
        {
            return;
        }

        AsOneEdit(() => KeyNow(node, kind, axes));
    }

    /// <summary>드래그 종류의 채널 종류와, 실제로 값이 바뀐 성분만(마야 Auto Key의 "바뀐 속성만").</summary>
    private (MotionChannelKind Kind, List<Axis> Axes) ChangedAxes(in RectNodeState start, in RectNodeState posed)
    {
        const float epsilon = 1e-4f;
        var axes = new List<Axis>();

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
                if (Math.Abs(posed.AnchoredPosition.X - start.AnchoredPosition.X) > epsilon) axes.Add(Axis.X);
                if (Math.Abs(posed.AnchoredPosition.Y - start.AnchoredPosition.Y) > epsilon) axes.Add(Axis.Y);
                return (MotionChannelKind.Position, axes);
            case Drag.Rotate:
                if (Math.Abs(posed.LocalEulerAngles.Z - start.LocalEulerAngles.Z) > epsilon) axes.Add(Axis.Z);
                return (MotionChannelKind.Euler, axes);
            default:
                if (Math.Abs(posed.LocalScale.X - start.LocalScale.X) > epsilon) axes.Add(Axis.X);
                if (Math.Abs(posed.LocalScale.Y - start.LocalScale.Y) > epsilon) axes.Add(Axis.Y);
                return (MotionChannelKind.Scale, axes);
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panning)
        {
            Point now = e.GetPosition(this);
            _viewPan = new Vector(_panStart.X + now.X - _panPointerStart.X, _panStart.Y + now.Y - _panPointerStart.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            case Drag.Rotate:
            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
                DragManipulation(e.GetPosition(this));
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_drag != Drag.None)
        {
            Drag drag = _drag;

            if (drag is Drag.MoveFree or Drag.MoveX or Drag.MoveY or Drag.Rotate or Drag.ScaleFree or Drag.ScaleX or Drag.ScaleY)
            {
                // 드래그 하나 = 되돌리기 한 칸: 키가 됐으면 문서 변화, 키 없는 포즈로 남았으면 포즈 변화(드래그 시작 때와 견줘)
                PendingSnapshot before = _dragPendingBefore ?? CapturePending();
                _dragPendingBefore = null;

                if (_editor is null)
                {
                    EndManipulation();
                }
                else
                {
                    _editor.Batch(MotionChangeKind.Keys, () =>
                    {
                        EndManipulation();
                        RecordPendingChange(before);
                    });
                }
            }

            _drag = Drag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>휠 = 커서를 축으로 확대·축소(한 칸 = ×1.15).</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomViewAt(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.F when e.KeyModifiers == KeyModifiers.None:
                ResetView();
                break;
            case Key.Home:
                Pause();
                ScrubTo(0);
                break;
            case Key.End:
                Pause();
                ScrubTo(_clock.Duration);
                break;
            case Key.Left:
                Pause();
                ScrubTo(_clock.Time - 1.0 / TimeSnap.FramesPerSecond);
                break;
            case Key.Right:
                Pause();
                ScrubTo(_clock.Time + 1.0 / TimeSnap.FramesPerSecond);
                break;
            case Key.OemPeriod:
                JumpToNextKey();
                break;
            case Key.OemComma:
                JumpToPreviousKey();
                break;
            case Key.Q:
                Manipulator = ManipulatorMode.Select;
                InvalidateVisual();
                break;
            case Key.W:
                Manipulator = ManipulatorMode.Move;
                InvalidateVisual();
                break;
            case Key.E:
                Manipulator = ManipulatorMode.Rotate;
                InvalidateVisual();
                break;
            case Key.R:
                Manipulator = ManipulatorMode.Scale;
                InvalidateVisual();
                break;
            case Key.S:
                SetKey();
                break;
            case Key.Escape:
                DiscardPending();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
