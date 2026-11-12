using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Views;

/// <summary>뷰포트가 그리는 이모지의 한 순간 — 씬의 트리(캐릭터 + 그 밑에 선 이모지) · 이모지 전체의 불투명도 · 리그마다 · 글.</summary>
internal sealed record EmojiFrameView(RectNodeTree Tree, float Alpha, IReadOnlyList<EmojiRigPose> Rigs, string Label, bool Visible);

// 이모지의 미리보기(docs/history/PLAN-emoji.md E6). 이모지를 고르면 뷰포트의 시간은 그 이모지의 것이다:
//   타임라인 보기 — 시계 = 타임라인(초). 그래프 · 도프 시트와 같은 시각이다. 그 프레임을 내보내는 길 그대로 읽어(EmojiPreview.Timeline) 보인다.
//   수명 보기     — 시계 = 등장 → 머묾 × 바퀴 → 퇴장(머묾이 없으면 main 한 번). 코어 재생(EmojiPlayback)을 그대로 돌린다.
// 이모지는 런타임처럼 캐릭터의 CharSlot_Scale 밑(로케이터 자리 — 발)에 선다. 씬에 캐릭터가 없으면 무대 가운데.
// 타임라인 보기에서는 뷰포트에서 직접 만진다(docs/history/PLAN-ux.md U4) — 그림을 눌러 고르고, 매니퓰레이터(W 이동 · E 회전 · R 크기)로 끈다.
//   Auto Key가 꺼져 있으면 기본값(그림의 자리 · 크기 · 회전 = EmojiRig_Place, 이모지 전체는 머리에서의 자리)이 바뀐다 — 키가 아니다.
//   Auto Key가 켜져 있으면 지금 프레임에 키가 놓인다(그림의 키 = EmojiRig_Image, 이모지 전체의 키 = Emoji_Offset).
// 끄는 동안은 보이기만 하고 놓을 때 한 칸. 그림을 고르지 않았으면 대상은 이모지 전체다.
public sealed partial class ViewportView
{
    private const string EmojiKeyPrefix = "emoji/";
    private const string HostNode = "CharSlot_Scale";

    private static readonly IPen EmojiRigPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0xF0, 0xC0, 0x40)), 1, DashStyle.Dash);
    private static readonly IPen SelectedEmojiRigPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x40)), 2);
    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(28, 0xF0, 0xC0, 0x40));
    private static readonly IPen LocatorPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0x40, 0xC0, 0xF0)), 1);

    private string? _emojiName;
    private bool _emojiLife;
    private bool _emojiFacingLeft;
    private int _emojiHoldCycles = 2;
    private readonly List<string> _selectedEmojiRigs = new();
    private bool _emojiWholeSelected;
    private readonly Dictionary<string, Bitmap?> _emojiImages = new(StringComparer.Ordinal);
    private EmojiLocatorFile _emojiLocators = EmojiLocatorFile.Empty;
    private bool _emojiShowHost = true;

    /// <summary>이모지를 그리는 동안의 인물(오브젝트 id) — <c>DrawObject</c>가 이 인물을 감추거나 뒤집어 그린다.</summary>
    private string? _emojiHostId;
    private (long Revision, string Name, EmojiRigFrame Frame, EmojiDefinition? Definition, EmojiClip? Timeline, string? Why)? _emojiCache;

    /// <summary>끄는 조작에 든 대상 하나 — 그림(빈 이름은 이모지 전체)과 값이 들어가는 노드, 시작 값과 지금 값.</summary>
    private sealed class EmojiManipItem
    {
        public string Rig = "";

        /// <summary>값이 들어가는 노드(트리의 키) — 끈 거리를 이 노드의 부모 공간으로 되돌린다.</summary>
        public string Node = "";

        public float X, Y, Sx = 1f, Sy = 1f, Rotation;
        public float NowX, NowY, NowSx = 1f, NowSy = 1f, NowRotation;

        /// <summary>끌기 시작할 때 이 그림의 가운데(루트 공간) — 무리째 돌리거나 늘일 때 여기서 가운데 점까지의 팔이 돈다.</summary>
        public Vec2 OriginRoot;
    }

    /// <summary>
    /// 끄는 중인 조작 하나 — 고른 그림 전부(없으면 이모지 전체)를 함께 끈다. 기본값인가 키인가, 어느 손잡이인가.
    /// 끄는 동안은 문서를 바꾸지 않고 보이기만 한다. 이동은 화면의 같은 거리만큼. 회전 · 크기는 하나면 제 가운데에서,
    /// 여럿이면 **무리의 가운데 점**(매니퓰레이터가 선 곳)을 축으로 — 그림마다 돌고(커지고) 자리도 그 점을 돌아(그 점에서 멀어져) 옮겨 간다.
    /// </summary>
    private sealed class EmojiManip
    {
        /// <summary>참이면 지금 프레임의 키, 거짓이면 기본값.</summary>
        public bool Key;

        public Drag Drag;
        public List<EmojiManipItem> Items = new();
        public Vec2 StartRoot;
        public Point StartControl;
        public Vec2 PivotRoot;
        public Vec2 ScaleDirection;

        /// <summary>그림 여럿을 함께 끄는가 — 회전 · 크기가 가운데 점을 축으로 자리까지 옮긴다.</summary>
        public bool Group => Items.Count > 1;

        /// <summary>축 화살표를 끌 때 그 축의 방향(루트 공간, 길이 1) — 끈 거리를 이 방향으로만 남긴다.</summary>
        public Vec2? AxisRoot;

        /// <summary>크기의 축 손잡이를 끌 때 그 팔의 방향(루트 공간, 길이 1) — 무리의 자리를 이 방향으로만 늘인다.</summary>
        public Vec2? ScaleAxisRoot;

        public bool Moved;
    }

    /// <summary>빈 곳에서 끌기 시작한 상자 — 놓으면 상자에 닿은 그림들이 골라진다.</summary>
    private sealed class EmojiBox
    {
        public Point Start;
        public Point Now;
        public bool Additive;
        public bool Moved;
    }

    /// <summary>누른 자리에 겹쳐 있던 그림들(위에서 아래로)과 고른 것 — 끌지 않고 놓으면 그 밑의 것으로 넘긴다.</summary>
    private sealed record EmojiClick(IReadOnlyList<string> Candidates, string Chosen, bool WasSelected, Point At);

    private EmojiManip? _emojiManip;
    private EmojiClick? _emojiClick;
    private EmojiBox? _emojiBox;
    private bool _emojiPress;

    /// <summary>그림의 불투명도(알파) — 그림의 빈 곳은 눌러도 맞지 않는다. 못 읽은 그림은 없다(사각형 전체가 맞는다).</summary>
    private readonly Dictionary<string, (int Width, int Height, byte[] Alpha)> _emojiAlpha = new(StringComparer.Ordinal);

    /// <summary>끌었다고 치는 거리(px) — 이보다 덜 움직이고 놓으면 누르기다.</summary>
    private const double EmojiClickSlop = 3;

    /// <summary>이 값보다 진해야 그림의 속이다(0~255).</summary>
    private const byte EmojiAlphaThreshold = 16;

    /// <summary>보고 있는 이모지의 이름. null이면 이모지를 보지 않는다.</summary>
    public string? EmojiName => _emojiName;

    public EmojiAsset? ShownEmoji => _editor is not null && _emojiName is not null ? _editor.Document.FindEmoji(_emojiName) : null;

    /// <summary>수명 보기인가(거짓이면 타임라인 보기). 바꾸면 시계의 길이가 바뀐다.</summary>
    public bool EmojiLife
    {
        get => _emojiLife;
        set
        {
            if (_emojiLife == value)
            {
                return;
            }

            _emojiLife = value;
            _clock.Stop();
            _timer.Stop();
            SyncDuration();
            ScrubTo(0);
            PlayStateChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>수명 보기에서 머묾을 몇 바퀴 돈 뒤 내리는가.</summary>
    public int EmojiHoldCycles
    {
        get => _emojiHoldCycles;
        set
        {
            _emojiHoldCycles = Math.Clamp(value, 1, 20);
            SyncDuration();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 왼쪽을 보는 인물로 보기(E8) — 보기의 상태다(문서에 적지 않고 되돌리기에 들지 않는다). 켜면 미리보기의 로케이터를 뒤집고
    /// 예외(<c>fixedSide</c> · <c>upright</c>)의 부호를 코어 <see cref="EmojiSide"/>로 곱한다 — 런타임에서 뒤집힌 인물에게 뜨는 모습.
    /// 켠 동안 그림을 끌어 기본 자리를 옮기지 않는다(거울 속에서 끄는 값이 헷갈린다 — 끄고 옮긴다).
    /// </summary>
    public bool EmojiFacingLeft
    {
        get => _emojiFacingLeft;
        set
        {
            if (_emojiFacingLeft != value)
            {
                _emojiFacingLeft = value;
                CancelEmojiPress();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// 캐릭터별 머리의 자리(런타임의 로케이터 표) — 이모지를 인물의 머리에 세워 보인다. 보기만 바뀐다(문서 · 내보내기와 무관).
    /// </summary>
    public EmojiLocatorFile EmojiLocators
    {
        get => _emojiLocators;
        set
        {
            _emojiLocators = value ?? EmojiLocatorFile.Empty;
            InvalidateVisual();
        }
    }

    /// <summary>이모지가 선 인물을 보이는가(초상, 없으면 실루엣). 꺼도 이모지의 자리는 그대로다 — 인물의 그림만 감춘다.</summary>
    public bool EmojiShowHost
    {
        get => _emojiShowHost;
        set
        {
            if (_emojiShowHost != value)
            {
                _emojiShowHost = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// 고른 그림(리그) — 여럿이면 마지막에 고른 것(매니퓰레이터가 서는 자리). 넣으면 그것 하나만 고른다(null이면 푼다).
    /// </summary>
    public string? SelectedEmojiRig
    {
        get => _selectedEmojiRigs.Count > 0 ? _selectedEmojiRigs[^1] : null;
        set => SetSelectedEmojiRigs(value is null ? Array.Empty<string>() : new[] { value });
    }

    /// <summary>고른 그림들 — 고른 순서. 함께 끌고 함께 키한다.</summary>
    public IReadOnlyList<string> SelectedEmojiRigs => _selectedEmojiRigs;

    /// <summary>
    /// "이모지 전체"를 골랐는가 — 왼쪽 목록에서 이모지를 누르면 골라진다(이모지를 처음 고를 때도). 매니퓰레이터가 이모지 전체에 서고
    /// 끌면 그림 전부가 함께 간다(머리에서의 자리 · 이모지 전체의 키). 그림을 고르면 풀리고, 뷰포트의 빈 곳을 누르면 풀린다 —
    /// 그때는 아무것도 고르지 않은 것이라 끌 것도 매니퓰레이터도 없다.
    /// </summary>
    public bool EmojiWholeSelected
    {
        get => _emojiWholeSelected;
        set
        {
            if (_emojiWholeSelected == value)
            {
                return;
            }

            _emojiWholeSelected = value;
            CancelEmojiPress();

            if (value)
            {
                _selectedEmojiRigs.Clear();
            }

            InvalidateVisual();
        }
    }

    /// <summary>지금 대상들 — 고른 그림들, "이모지 전체"를 골랐으면 그것(빈 이름), 아무것도 고르지 않았으면 없다.</summary>
    private IReadOnlyList<string> EmojiTargets()
        => _selectedEmojiRigs.Count > 0 ? _selectedEmojiRigs.ToList() : _emojiWholeSelected ? new[] { "" } : Array.Empty<string>();

    /// <summary>고른 그림들을 바꾼다 — 마지막 것이 매니퓰레이터의 자리다.</summary>
    public void SetSelectedEmojiRigs(IEnumerable<string> rigs)
    {
        List<string> next = rigs.Distinct(StringComparer.Ordinal).ToList();

        if (next.Count > 0)
        {
            _emojiWholeSelected = false; // 그림을 고르면 "이모지 전체"는 풀린다
        }

        if (!next.SequenceEqual(_selectedEmojiRigs))
        {
            _selectedEmojiRigs.Clear();
            _selectedEmojiRigs.AddRange(next);
        }

        InvalidateVisual();
    }

    /// <summary>뷰포트에서 고른 그림들이 바뀌었다(누르기 · 상자 · 빈 곳).</summary>
    public event EventHandler? EmojiRigPicked;

    /// <summary>
    /// 이모지를 본다 — 그래프 · 도프 시트처럼 그 타임라인(<c>emoji:이름</c>)이 묶인다. null이면 이모지를 놓는다.
    /// </summary>
    public void BindEmoji(MotionEditor? editor, string? emojiName)
    {
        string? before = _emojiName;
        Bind(editor, emojiName is null ? null : EmojiAsset.TimelineNameOf(emojiName));
        _emojiName = emojiName;
        _emojiCache = null;
        SyncDuration();
        TimeChanged?.Invoke(this, EventArgs.Empty);

        // 다른 이모지로 오면 그 그림과 인물의 머리에 맞춘다(보기만). 이모지를 놓을 때 무대로 돌아오는 것은 Bind가 한다.
        if (emojiName is not null && emojiName != before)
        {
            _emojiWholeSelected = true; // 이모지를 고른 것 — 대상은 이모지 전체다
            FrameEmoji(withHost: true);
        }

        InvalidateVisual();
    }

    /// <summary>그림 파일의 비트맵(문서 기준 경로로). 못 읽었으면 null — 자리표시로 보인다.</summary>
    public void SetEmojiImage(string documentPath, Bitmap? bitmap)
    {
        _emojiImages[documentPath] = bitmap;
        _emojiAlpha.Remove(documentPath);

        if (bitmap is not null && TryReadAlpha(bitmap) is { } alpha)
        {
            _emojiAlpha[documentPath] = alpha;
        }

        InvalidateVisual();
    }

    public void ClearEmojiImages()
    {
        _emojiImages.Clear();
        _emojiAlpha.Clear();
    }

    /// <summary>테스트 손잡이 — 그림의 알파를 직접 준다(헤드리스에서는 비트맵의 픽셀을 읽지 못한다). 행 우선, 위에서 아래로.</summary>
    internal void SetEmojiAlphaForTest(string documentPath, int width, int height, byte[] alpha) => _emojiAlpha[documentPath] = (width, height, alpha);

    /// <summary>비트맵의 알파만 읽는다(픽셀 넷째 바이트). 32비트가 아니거나 읽지 못하면 null — 그때는 사각형 전체가 맞는다.</summary>
    private static (int Width, int Height, byte[] Alpha)? TryReadAlpha(Bitmap bitmap)
    {
        try
        {
            int width = bitmap.PixelSize.Width;
            int height = bitmap.PixelSize.Height;

            if (width <= 0 || height <= 0 || bitmap.Format is not { BitsPerPixel: 32 })
            {
                return null;
            }

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                handle.Free();
            }

            byte[] alpha = new byte[width * height];

            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = pixels[i * 4 + 3];
            }

            return (width, height, alpha);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>이 이모지의 정의 · 타임라인 클립 — 문서의 판이 바뀔 때만 다시 읽는다(프레임마다 내보내기를 다시 하지 않는다).</summary>
    private (EmojiRigFrame Frame, EmojiDefinition? Definition, EmojiClip? Timeline, string? Why)? EmojiSource()
    {
        if (_editor is null || ShownEmoji is not { } emoji || !EmojiRigFrame.TryRead(_rig ?? BuiltinTuning.RigSchemas, out EmojiRigFrame frame, out string frameWhy))
        {
            return ShownEmoji is null ? null : (EmojiRigFrame.Builtin, null, null, "리그 덤프에 이모지 리그가 없다");
        }

        if (_emojiCache is { } cache && cache.Revision == _editor.Revision && cache.Name == emoji.Name && ReferenceEquals(cache.Frame.Dto, frame.Dto))
        {
            return (cache.Frame, cache.Definition, cache.Timeline, cache.Why);
        }

        EmojiDefinition? definition = EmojiPreview.Definition(emoji, frame, out string? why);
        EmojiClip? timeline = EmojiPreview.Timeline(emoji, frame);
        _emojiCache = (_editor.Revision, emoji.Name, frame, definition, timeline, why ?? (timeline is null ? frameWhy : null));
        return (frame, definition, timeline, _emojiCache.Value.Why);
    }

    /// <summary>수명 보기의 시계 길이. 이모지를 보지 않거나 타임라인 보기면 null.</summary>
    private double? EmojiLifeDuration()
        => _emojiName is not null && _emojiLife && EmojiSource() is { Definition: { } definition } ? EmojiPreview.LifeSeconds(definition, _emojiHoldCycles) : null;

    /// <summary>
    /// 지금 시각이 타임라인의 어디인가 — 그래프 · 도프 시트가 따라갈 시각. 수명 보기면 지금 클립의 프레임을 그 클립이 잘려 나온 구간의 자리로 되돌린다
    /// (main은 0부터). 퇴장이 끝났거나 이모지를 보지 않으면 null.
    /// </summary>
    public double? EmojiTimelineTime
    {
        get
        {
            if (ShownEmoji is not { } emoji)
            {
                return null;
            }

            if (!_emojiLife)
            {
                return _clock.Time;
            }

            if (EmojiCurrentMoment() is not { Clip: { } clip } moment)
            {
                return null;
            }

            int start = emoji.FindSpan(clip.Name)?.Start ?? 0;
            return EmojiAsset.SecondsOf(start + moment.Frame);
        }
    }

    /// <summary>수명 보기의 지금 순간. 타임라인 보기거나 정의가 없으면 null.</summary>
    public EmojiMoment? EmojiCurrentMoment()
    {
        if (!_emojiLife || EmojiSource() is not { Definition: { } definition })
        {
            return null;
        }

        return EmojiPreview.Moment(definition, _clock.Time, EmojiPreview.OffAt(definition, _emojiHoldCycles));
    }

    /// <summary>이모지가 설 캐릭터 — 고른 오브젝트가 CharSlot_Scale을 가졌으면 그것, 아니면 그런 첫 오브젝트. 없으면 null(무대 가운데).</summary>
    private SceneObject? EmojiHost(RectNodeTree tree)
    {
        if (SelectedObject is { } selected && tree.Contains(selected.KeyOf(HostNode)))
        {
            return selected;
        }

        return SceneObjects.FirstOrDefault(o => o.Visible && tree.Contains(o.KeyOf(HostNode)));
    }

    /// <summary>지금 시각의 이모지 — 씬 위에 세우고 값을 옮긴 것. 이모지를 보지 않으면 null.</summary>
    internal EmojiFrameView? EmojiNow()
    {
        if (ShownEmoji is not { } shown || EmojiSource() is not { } source)
        {
            return null;
        }

        EmojiAsset emoji = shown;

        // 기본값을 끄는 중이면 그 값만 바꾼 사본으로 세운다(문서는 놓을 때 바뀐다).
        if (_emojiManip is { Key: false, Moved: true } basing)
        {
            EmojiManipItem? whole = basing.Items.FirstOrDefault(i => i.Rig.Length == 0);
            emoji = new EmojiAsset
            {
                Name = emoji.Name,
                OffsetX = whole?.NowX ?? emoji.OffsetX,
                OffsetY = whole?.NowY ?? emoji.OffsetY,
                Timeline = emoji.Timeline, Spans = emoji.Spans, FixedSide = emoji.FixedSide,
                Rigs = emoji.Rigs.Select(r => basing.Items.FirstOrDefault(i => i.Rig == r.Name) is { } item
                    ? new EmojiRigAsset { Name = r.Name, Images = r.Images, PlaceX = item.NowX, PlaceY = item.NowY, Scale = item.NowSx, Rotation = item.NowRotation, Upright = r.Upright }
                    : r).ToList(),
            };
        }

        RectNodeTree tree = _template?.Clone() ?? new RectNodeTree(new RectSpace(_stageSize, Vec2.Half));
        SceneObject? host = EmojiHost(tree);
        // 인물이 있으면 그 캐릭터의 머리에 세운다(런타임의 로케이터 표 — 보기에만 읽는다). 없으면 무대 가운데.
        EmojiPreview.Attach(tree, emoji, source.Frame, EmojiKeyPrefix, host?.KeyOf(HostNode), _emojiFacingLeft, host is null ? null : _emojiLocators.Resolve(host.Character));

        EmojiClip? clip;
        float frame;
        string label;
        bool visible = true;

        if (_emojiLife)
        {
            if (source.Definition is not { } definition)
            {
                return new EmojiFrameView(tree, 1f, Array.Empty<EmojiRigPose>(), $"이모지 {emoji.Name} — 런타임이 읽지 못한다: {source.Why}", false);
            }

            EmojiMoment moment = EmojiPreview.Moment(definition, _clock.Time, EmojiPreview.OffAt(definition, _emojiHoldCycles));
            clip = moment.Clip;
            frame = moment.Frame;
            visible = moment.Phase != EmojiPhase.Done;
            label = $"이모지 {emoji.Name} · 수명 — {PhaseLabel(moment.Phase)}{(clip is null ? "" : $" {clip.Name} {frame:0.#}/{clip.Length}fr")}";
        }
        else
        {
            clip = source.Timeline;
            frame = (float)Math.Clamp(_clock.Time * EmojiAsset.Fps, 0, emoji.LastFrame);
            label = clip is null
                ? $"이모지 {emoji.Name} · 타임라인 — 런타임이 읽지 못한다: {source.Why}"
                : $"이모지 {emoji.Name} · 타임라인 {frame:0.#}/{emoji.LastFrame}fr";
        }

        (float alpha, IReadOnlyList<EmojiRigPose> rigs) = EmojiPreview.Apply(tree, EmojiKeyPrefix, emoji, source.Frame, clip, frame, source.Definition, _emojiFacingLeft);

        // 키를 끄는 중이면 그 노드들의 지금 값만 덮는다(키는 놓을 때 들어간다). 이모지 전체의 자리는 머리에서의 자리에 더한 것이다.
        if (_emojiManip is { Key: true, Moved: true } keying)
        {
            foreach (EmojiManipItem item in keying.Items)
            {
                if (!tree.Contains(item.Node))
                {
                    continue;
                }

                bool whole = item.Rig.Length == 0;
                RectNodeState state = tree.GetState(item.Node)
                    .WithAnchoredPosition(new Vec2((whole ? emoji.OffsetX : 0f) + item.NowX, (whole ? emoji.OffsetY : 0f) + item.NowY))
                    .WithLocalScale(new Vec3(item.NowSx, item.NowSy, 1f))
                    .WithLocalEuler(new Vec3(0f, 0f, item.NowRotation));
                tree.SetState(item.Node, state);
            }
        }

        string side = !_emojiFacingLeft ? "" : emoji.FixedSide ? " · 왼쪽을 보는 인물(방향을 따르지 않는다)" : " · 왼쪽을 보는 인물 — 거울";
        return new EmojiFrameView(tree, alpha, rigs, label + (host is null ? " · 캐릭터가 없어 무대 가운데" : $" · {host.Id}의 발") + side, visible);
    }

    private static string PhaseLabel(EmojiPhase phase) => phase switch
    {
        EmojiPhase.Main => "한 번",
        EmojiPhase.In => "등장",
        EmojiPhase.Hold => "머묾",
        EmojiPhase.Out => "퇴장",
        _ => "끝 — 사라졌다",
    };

    /// <summary>리그의 그림이 보이는 사각형(루트 공간) — 그림의 원본 픽셀 크기, 그림이 없으면 덤프의 크기.</summary>
    private Quad EmojiQuad(RectNodeTree tree, EmojiRigPose rig)
    {
        if (rig.ImagePath is { } path && _emojiImages.TryGetValue(path, out Bitmap? bitmap) && bitmap is not null)
        {
            return PortraitLayout.CenteredQuad(tree, rig.ImageNode, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }

        Vec2 size = tree.GetRectSize(rig.ImageNode);
        return PortraitLayout.CenteredQuad(tree, rig.ImageNode, size.X, size.Y);
    }

    private void RenderEmoji(DrawingContext context, Rect stage)
    {
        if (EmojiNow() is not { } now)
        {
            return;
        }

        SceneObject? host = EmojiHost(now.Tree);
        _emojiHostId = host?.Id;

        try
        {
            DrawObjects(context, now.Tree);
        }
        finally
        {
            _emojiHostId = null;
        }

        if (host is not null && _emojiShowHost && !HasPortrait(host))
        {
            DrawEmojiHostSilhouette(context, now.Tree, _emojiLocators.Resolve(host.Character));
        }

        // 로케이터(이모지가 서는 자리 — 인물의 머리) — 작은 십자.
        Point locator = ToControl(now.Tree.TransformPoint(EmojiKeyPrefix + RigSchemaLoader.RootKey, Vec3.Zero).XY);
        context.DrawLine(LocatorPen, new Point(locator.X - 6, locator.Y), new Point(locator.X + 6, locator.Y));
        context.DrawLine(LocatorPen, new Point(locator.X, locator.Y - 6), new Point(locator.X, locator.Y + 6));

        if (now.Visible)
        {
            foreach (EmojiRigPose rig in now.Rigs)
            {
                Quad quad = EmojiQuad(now.Tree, rig);
                Point topLeft = ToControl(quad.TopLeft);
                Point topRight = ToControl(quad.TopRight);
                Point bottomLeft = ToControl(quad.BottomLeft);
                Point bottomRight = ToControl(quad.BottomRight);
                var outline = new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true);
                Bitmap? bitmap = rig.ImagePath is { } path && _emojiImages.TryGetValue(path, out Bitmap? b) ? b : null;

                using (context.PushOpacity(Math.Clamp(now.Alpha * rig.Alpha, 0f, 1f)))
                {
                    if (bitmap is null)
                    {
                        context.DrawGeometry(PlaceholderFill, PlaceholderPen, outline);
                        context.DrawLine(PlaceholderPen, topLeft, bottomRight);
                        context.DrawLine(PlaceholderPen, topRight, bottomLeft);
                    }
                    else
                    {
                        double w = bitmap.PixelSize.Width;
                        double h = bitmap.PixelSize.Height;
                        var matrix = new Matrix(
                            (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
                            (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
                            topLeft.X, topLeft.Y);

                        using (context.PushTransform(matrix))
                        {
                            context.DrawImage(bitmap, new Rect(0, 0, w, h));
                        }
                    }
                }

                if (!_emojiLife)
                {
                    context.DrawGeometry(null, _selectedEmojiRigs.Contains(rig.Rig) ? SelectedEmojiRigPen : EmojiRigPen, outline);
                    DrawLabel(context, rig.Rig + (rig.Sprite is null ? "" : $" · {rig.Sprite}"), new Point(Math.Min(topLeft.X, bottomLeft.X) + 2, Math.Max(bottomLeft.Y, bottomRight.Y) + 2));
                }
            }
        }

        DrawOutside(context, stage);
        context.DrawRectangle(StagePen, stage);
        DrawEmojiGizmo(context, now);

        // 고르는 상자.
        if (_emojiBox is { Moved: true } box)
        {
            context.DrawRectangle(BoxFill, EmojiRigPen, new Rect(box.Start, box.Now).Normalize());
        }
        DrawLabel(context, now.Label, new Point(stage.X + 8, stage.Y + 6));

        // 지금 도구로 무엇을 끌면 무엇이 바뀌는가 — 보기를 이모지에 맞추면 무대의 글은 화면 밖이라, 뷰포트의 왼쪽 아래에 붙박아 적는다.
        if (EmojiEditNote() is { Length: > 0 } note)
        {
            DrawLabel(context, note, new Point(8, Math.Max(0, Bounds.Height - 20)));
        }
    }

    /// <summary>뷰포트의 글 끝에 붙는 말 — 지금 도구로 무엇을 끌면 무엇이 바뀌는가.</summary>
    private string EmojiEditNote()
    {
        if (_emojiLife)
        {
            return "";
        }

        if (_emojiFacingLeft)
        {
            return "거울 속에서는 고르기만 한다(보기를 되돌리고 끈다)";
        }

        if (EmojiTargets().Count == 0)
        {
            return "고른 것 없음 — 그림을 누르거나 끌어서 고른다. 이모지 전체는 왼쪽 목록에서 이모지를 누른다";
        }

        string target = _selectedEmojiRigs.Count switch
        {
            0 => EmojiWords.WholeEmoji,
            1 => _selectedEmojiRigs[0],
            _ => $"그림 {_selectedEmojiRigs.Count}개",
        };
        string tool = Manipulator switch
        {
            ManipulatorMode.Move => "이동(W)",
            ManipulatorMode.Rotate => "회전(E)",
            ManipulatorMode.Scale => "크기(R)",
            _ => "선택(Q)",
        };

        if (Manipulator == ManipulatorMode.Select)
        {
            return $"{tool} · {target}";
        }

        if (!AutoKey && _selectedEmojiRigs.Count == 0 && Manipulator != ManipulatorMode.Move)
        {
            return $"{tool} · {target}: 회전 · 크기는 키로만 잡는다(Auto Key를 켠다)";
        }

        return AutoKey
            ? $"{tool} · {target}: 끌면 {EmojiAsset.FrameOf((float)EmojiKeyTime())}프레임에 키(Auto Key)"
            : $"{tool} · {target}: 끌면 기본값(키가 아니다 — Auto Key를 켜면 키)";
    }

    /// <summary>테스트 손잡이 — 뷰포트의 글 끝에 붙는 말.</summary>
    internal string EmojiEditNoteForTest() => EmojiEditNote();

    // ── 매니퓰레이터 ─────────────────────────────────────────────────

    /// <summary>지금 대상을 지금 도구로 끌 수 있는가 — 수명 보기 · 거울 보기 · 선택 도구가 아니고, 기본값에 없는 것(이모지 전체의 회전 · 크기)이 아니다.</summary>
    private bool EmojiCanManipulate()
        => !_emojiLife && !_emojiFacingLeft && Manipulator != ManipulatorMode.Select && ShownEmoji is not null
           && (_selectedEmojiRigs.Count > 0 || (_emojiWholeSelected && (AutoKey || Manipulator == ManipulatorMode.Move)));

    /// <summary>대상이 보이는 노드(트리의 키) — 그림이면 그 그림 노드, 이모지 전체면 오프셋 노드. 매니퓰레이터가 여기에 선다.</summary>
    private string? EmojiTargetNode(EmojiFrameView now, string rig)
    {
        if (EmojiSource() is not { } source)
        {
            return null;
        }

        string key = EmojiKeyPrefix + source.Frame.TargetNode(rig);
        return now.Tree.Contains(key) ? key : null;
    }

    /// <summary>매니퓰레이터의 중심과 x · y 팔 끝(컨트롤 좌표, 팔 길이는 화면 고정). 끌 수 없으면 null.</summary>
    private (Point Center, Point XEnd, Point YEnd)? EmojiGizmoPoints(EmojiFrameView now)
    {
        if (!EmojiCanManipulate() || EmojiTargetNode(now, SelectedEmojiRig ?? "") is not { } key)
        {
            return null;
        }

        (Vec2 origin, Vec2 xAxis, Vec2 yAxis) = PortraitLayout.Axes(now.Tree, key, 100f);

        // 여럿을 골랐으면 매니퓰레이터는 그 그림들의 가운데 점에 선다(축의 방향은 마지막에 고른 그림의 것) — 회전 · 크기가 그 점을 축으로 한다.
        if (_selectedEmojiRigs.Count > 1)
        {
            List<Vec2> origins = _selectedEmojiRigs
                .Select(rig => EmojiTargetNode(now, rig))
                .Where(node => node is not null)
                .Select(node => now.Tree.TransformPoint(node!, Vec3.Zero).XY)
                .ToList();

            if (origins.Count > 1)
            {
                var middle = new Vec2(origins.Average(o => o.X), origins.Average(o => o.Y));
                Vec2 shift = middle - origin;
                (origin, xAxis, yAxis) = (middle, xAxis + shift, yAxis + shift);
            }
        }

        Point center = ToControl(origin);
        return (center, Along(center, ToControl(xAxis)), Along(center, ToControl(yAxis)));
    }

    /// <summary>테스트 손잡이 — 매니퓰레이터의 세 점(컨트롤 좌표). 없으면 null.</summary>
    internal (Point Center, Point XEnd, Point YEnd)? EmojiGizmoForTest() => EmojiNow() is { } now ? EmojiGizmoPoints(now) : null;

    private void DrawEmojiGizmo(DrawingContext context, EmojiFrameView now)
    {
        if (EmojiGizmoPoints(now) is not { } gizmo)
        {
            return;
        }

        (Point center, Point xEnd, Point yEnd) = gizmo;

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
                context.DrawLine(XAxisPen, center, xEnd);
                context.DrawLine(YAxisPen, center, yEnd);
                context.DrawGeometry(GizmoXBrush, null, ArrowHead(center, xEnd));
                context.DrawGeometry(GizmoYBrush, null, ArrowHead(center, yEnd));
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case ManipulatorMode.Scale:
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

    /// <summary>키가 놓일 시각 — 지금 시각을 프레임에 붙인 것(타임라인 안으로).</summary>
    private double EmojiKeyTime()
        => ShownEmoji is { } emoji ? TimeSnap.Snap(Math.Clamp(_clock.Time, 0, emoji.Timeline.Duration), 1.0 / EmojiAsset.Fps) : 0;

    /// <summary>지금 프레임에서 그 대상의 키 값(그 프레임의 미리보기 값 — 키가 없는 채널은 중립).</summary>
    private EmojiNodePose EmojiKeyPose(string rig)
        => EmojiClipMath.SampleNode(EmojiSource()?.Timeline, rig, (float)(EmojiKeyTime() * EmojiAsset.Fps));

    /// <summary>
    /// 타임라인 보기에서 눌렀다.
    ///   매니퓰레이터의 손잡이 — 고른 것 전부(이모지 전체를 골랐으면 그것)를 끌기 시작한다.
    ///   그림 — 고른다. Shift · Ctrl이면 더하거나 뺀다. 이동 도구면 고르면서 바로 끈다(고른 것 전부).
    ///   빈 곳 — 상자를 끌어 고른다. 끌지 않고 놓으면 선택이 전부 풀린다(이모지 전체까지 — 끌 것이 없어진다).
    /// </summary>
    private bool TryStartEmojiDrag(Point position, bool additive = false)
    {
        if (_emojiLife || ShownEmoji is not { } emoji || EmojiNow() is not { Visible: true } now)
        {
            return false;
        }

        CancelEmojiPress();

        // 1. 매니퓰레이터의 손잡이 — 그 밑에 다른 그림이 있어도 고르기가 넘어가지 않는다.
        //    대상이 이모지 전체일 때 가운데 손잡이는 그림 위에 놓이기 쉽다 — 그 자리에 그림이 있으면 그림 고르기가 이긴다(팔은 늘 잡힌다).
        //    여럿을 골랐을 때 이동의 가운데 손잡이(무리의 가운데 점)가 고른 그림 위에 놓였으면 그림을 누른 것으로 본다 —
        //    끌면 똑같이 함께 가고, 끌지 않고 놓으면 그것만 남는다.
        if (!additive && EmojiGizmoPoints(now) is { } gizmo && GizmoDragAt(position, gizmo) is var handle && handle != Drag.None
            && !(_selectedEmojiRigs.Count == 0 && handle is Drag.MoveFree or Drag.ScaleFree && EmojiRigsAt(now, ToRoot(position)).Count > 0)
            && !(_selectedEmojiRigs.Count > 1 && handle == Drag.MoveFree && EmojiRigsAt(now, ToRoot(position)).Any(_selectedEmojiRigs.Contains)))
        {
            _emojiPress = BeginEmojiManip(emoji, now, handle, position, gizmo);
            return _emojiPress;
        }

        IReadOnlyList<string> hits = EmojiRigsAt(now, ToRoot(position));
        _emojiPress = true;

        // 2. 빈 곳 — 상자.
        if (hits.Count == 0)
        {
            _emojiBox = new EmojiBox { Start = position, Now = position, Additive = additive };
            return true;
        }

        // 3. Shift · Ctrl — 위에 그려진 것을 더하거나 뺀다. 끌지 않는다.
        if (additive)
        {
            List<string> next = _selectedEmojiRigs.ToList();

            if (!next.Remove(hits[0]))
            {
                next.Add(hits[0]);
            }

            PickEmojiRigs(next);
            return true;
        }

        // 4. 그림 — 맞은 것들 가운데 이미 고른 것이 있으면 그것을 지킨다(겹친 그림 · 여럿을 끌 수 있게).
        string? kept = hits.FirstOrDefault(h => _selectedEmojiRigs.Contains(h));
        string chosen = kept ?? hits[0];
        _emojiClick = new EmojiClick(hits, chosen, kept is not null, position);

        // 고른 것이 매니퓰레이터의 자리가 된다 — 여럿을 골랐으면 그 가운데 누른 것이 마지막으로 온다.
        PickEmojiRigs(kept is null ? new[] { chosen } : _selectedEmojiRigs.Where(r => r != chosen).Append(chosen));

        // 이동 도구면 고르면서 바로 끈다(손잡이를 따로 잡지 않아도 된다).
        if (Manipulator == ManipulatorMode.Move && EmojiCanManipulate() && EmojiGizmoPoints(EmojiNow() ?? now) is { } picked)
        {
            BeginEmojiManip(emoji, now, Drag.MoveFree, position, picked);
        }

        return true;
    }

    /// <summary>고른 그림들을 바꾸고 알린다(왼쪽 목록과 오른쪽이 따라온다). 같으면 알리지 않는다.</summary>
    private void PickEmojiRigs(IEnumerable<string> rigs)
    {
        List<string> next = rigs.ToList();

        if (!next.SequenceEqual(_selectedEmojiRigs))
        {
            SetSelectedEmojiRigs(next);
            EmojiRigPicked?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>매니퓰레이터의 어느 손잡이를 눌렀는가.</summary>
    private Drag GizmoDragAt(Point position, (Point Center, Point XEnd, Point YEnd) gizmo)
    {
        double toCenter = Distance(position, gizmo.Center);

        return Manipulator switch
        {
            ManipulatorMode.Move => Distance(position, gizmo.XEnd) <= GizmoHit ? Drag.MoveX
                : Distance(position, gizmo.YEnd) <= GizmoHit ? Drag.MoveY
                : toCenter <= GizmoHit + 2 ? Drag.MoveFree : Drag.None,
            ManipulatorMode.Scale => Distance(position, gizmo.XEnd) <= GizmoHit ? Drag.ScaleX
                : Distance(position, gizmo.YEnd) <= GizmoHit ? Drag.ScaleY
                : toCenter <= GizmoHit + 2 ? Drag.ScaleFree : Drag.None,
            ManipulatorMode.Rotate => Math.Abs(toCenter - GizmoArm) <= GizmoHit ? Drag.Rotate : Drag.None,
            _ => Drag.None,
        };
    }

    /// <summary>끌기를 시작한다 — 고른 그림 전부(없으면 이모지 전체)의 시작 값을 읽는다(Auto Key면 지금 프레임의 키 값, 아니면 기본값).</summary>
    private bool BeginEmojiManip(EmojiAsset emoji, EmojiFrameView now, Drag drag, Point position, (Point Center, Point XEnd, Point YEnd) gizmo)
    {
        if (EmojiSource() is not { } source)
        {
            return false;
        }

        var manip = new EmojiManip { Key = AutoKey, Drag = drag, StartRoot = ToRoot(position), StartControl = position, PivotRoot = ToRoot(gizmo.Center) };
        IEnumerable<string> targets = _selectedEmojiRigs.Count > 0 ? _selectedEmojiRigs : new[] { "" };

        foreach (string rig in targets)
        {
            var item = new EmojiManipItem { Rig = rig };

            if (manip.Key)
            {
                EmojiNodePose pose = EmojiKeyPose(rig);
                (item.X, item.Y, item.Sx, item.Sy, item.Rotation) = (pose.Position.X, pose.Position.Y, pose.Scale.X, pose.Scale.Y, pose.Rotation);
                item.Node = EmojiKeyPrefix + source.Frame.TargetNode(rig);
            }
            else if (emoji.FindRig(rig) is { } asset)
            {
                (item.X, item.Y, item.Sx, item.Sy, item.Rotation) = (asset.PlaceX, asset.PlaceY, asset.Scale, asset.Scale, asset.Rotation);
                item.Node = EmojiKeyPrefix + source.Frame.PlaceNodeOf(rig);
            }
            else
            {
                (item.X, item.Y) = (emoji.OffsetX, emoji.OffsetY);
                item.Node = EmojiKeyPrefix + source.Frame.OffsetNode;
            }

            if (!now.Tree.Contains(item.Node))
            {
                continue;
            }

            (item.NowX, item.NowY, item.NowSx, item.NowSy, item.NowRotation) = (item.X, item.Y, item.Sx, item.Sy, item.Rotation);
            item.OriginRoot = now.Tree.TransformPoint(item.Node, Vec3.Zero).XY;
            manip.Items.Add(item);
        }

        if (manip.Items.Count == 0)
        {
            return false;
        }

        Point arm = drag == Drag.ScaleY ? gizmo.YEnd : gizmo.XEnd;
        manip.ScaleDirection = new Vec2((float)(arm.X - gizmo.Center.X), (float)(arm.Y - gizmo.Center.Y));

        if (drag is Drag.ScaleX or Drag.ScaleY)
        {
            Vec2 scaleAxis = ToRoot(arm) - manip.PivotRoot;
            float scaleLength = (float)Math.Sqrt(scaleAxis.X * scaleAxis.X + scaleAxis.Y * scaleAxis.Y);
            manip.ScaleAxisRoot = scaleLength < 1e-6f ? null : new Vec2(scaleAxis.X / scaleLength, scaleAxis.Y / scaleLength);
        }

        // 축 화살표 — 그 화살표가 가리키는 방향(매니퓰레이터가 선 그림의 축)으로만 움직인다. 여럿이면 다 같은 방향으로 간다.
        if (drag is Drag.MoveX or Drag.MoveY)
        {
            Vec2 axis = ToRoot(drag == Drag.MoveX ? gizmo.XEnd : gizmo.YEnd) - manip.PivotRoot;
            float length = (float)Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
            manip.AxisRoot = length < 1e-6f ? null : new Vec2(axis.X / length, axis.Y / length);
        }

        _emojiManip = manip;
        return true;
    }

    /// <summary>
    /// 끄는 중 — 끈 만큼 지금 값을 바꾼다(보이기만). 이동은 화면의 같은 거리를 그림마다 제 부모 공간으로 되돌리고,
    /// 회전은 매니퓰레이터의 가운데를 도는 각을 그림마다 더하고, 크기는 팔 방향으로 잰 배를 그림마다 곱한다. 상자를 끄는 중이면 상자를 넓힌다.
    /// </summary>
    private void DragEmojiPlace(Point position)
    {
        if (_emojiBox is { } box)
        {
            box.Now = position;
            box.Moved |= Distance(position, box.Start) >= EmojiClickSlop;
            InvalidateVisual();
            return;
        }

        if (_emojiManip is not { } manip || EmojiNow() is not { } now)
        {
            return;
        }

        if (!manip.Moved && Distance(position, manip.StartControl) < EmojiClickSlop)
        {
            return;
        }

        manip.Moved = true;
        Vec2 root = ToRoot(position);

        switch (manip.Drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            {
                Vec2 moved = root - manip.StartRoot;

                if (manip.AxisRoot is { } axis)
                {
                    float along = moved.X * axis.X + moved.Y * axis.Y;
                    moved = new Vec2(axis.X * along, axis.Y * along);
                }

                foreach (EmojiManipItem item in manip.Items.Where(i => now.Tree.Contains(i.Node)))
                {
                    Vec2 delta = PoseEdit.RootDeltaToLocal(now.Tree, item.Node, manip.StartRoot, manip.StartRoot + moved);
                    item.NowX = (float)Math.Round(item.X + delta.X, 1);
                    item.NowY = (float)Math.Round(item.Y + delta.Y, 1);
                }

                break;
            }

            case Drag.Rotate:
            {
                float angle = PoseEdit.AngleDelta(manip.PivotRoot, manip.StartRoot, root);
                double radians = angle * Math.PI / 180.0;
                (float cos, float sin) = ((float)Math.Cos(radians), (float)Math.Sin(radians));

                foreach (EmojiManipItem item in manip.Items)
                {
                    item.NowRotation = (float)Math.Round(item.Rotation + angle, 1);

                    // 여럿이면 무리째 돈다 — 가운데 점에서 이 그림까지의 팔을 같은 각만큼 돌린 곳으로 자리를 옮긴다.
                    if (manip.Group && now.Tree.Contains(item.Node))
                    {
                        Vec2 arm = item.OriginRoot - manip.PivotRoot;
                        MoveGroupItem(now.Tree, item, manip.PivotRoot + new Vec2(arm.X * cos - arm.Y * sin, arm.X * sin + arm.Y * cos));
                    }
                }

                break;
            }

            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
            {
                var from = new Vec2((float)manip.StartControl.X, (float)manip.StartControl.Y);
                var to = new Vec2((float)position.X, (float)position.Y);
                float factor = PoseEdit.ScaleAlong(manip.ScaleDirection, from, to, (float)GizmoArm);

                // 그림의 기본 크기는 값 하나다(가로 세로가 같이 간다). 키는 축마다 따로.
                bool both = !manip.Key || manip.Drag == Drag.ScaleFree;

                foreach (EmojiManipItem item in manip.Items)
                {
                    item.NowSx = both || manip.Drag == Drag.ScaleX ? (float)Math.Round(item.Sx * factor, 3) : item.Sx;
                    item.NowSy = both || manip.Drag == Drag.ScaleY ? (float)Math.Round(item.Sy * factor, 3) : item.Sy;

                    // 여럿이면 무리째 커진다 — 가운데 점에서 이 그림까지의 팔도 같은 배로(축 손잡이면 그 축의 방향으로만) 늘인다.
                    if (manip.Group && now.Tree.Contains(item.Node))
                    {
                        Vec2 arm = item.OriginRoot - manip.PivotRoot;

                        if (manip.Drag == Drag.ScaleFree || manip.ScaleAxisRoot is not { } axis)
                        {
                            arm = new Vec2(arm.X * factor, arm.Y * factor);
                        }
                        else
                        {
                            float along = arm.X * axis.X + arm.Y * axis.Y;
                            arm = new Vec2(arm.X + axis.X * along * (factor - 1f), arm.Y + axis.Y * along * (factor - 1f));
                        }

                        MoveGroupItem(now.Tree, item, manip.PivotRoot + arm);
                    }
                }

                break;
            }
        }

        InvalidateVisual();
    }

    /// <summary>무리째 돌리거나 늘이는 중 — 그 그림의 가운데가 루트 공간의 <paramref name="toRoot"/>로 가도록 자리를 바꾼다(보이기만).</summary>
    private static void MoveGroupItem(RectNodeTree tree, EmojiManipItem item, Vec2 toRoot)
    {
        Vec2 delta = PoseEdit.RootDeltaToLocal(tree, item.Node, item.OriginRoot, toRoot);
        item.NowX = (float)Math.Round(item.X + delta.X, 1);
        item.NowY = (float)Math.Round(item.Y + delta.Y, 1);
    }

    /// <summary>
    /// 놓았다.
    ///   끌었으면 문서에 적는다 — 고른 것이 여럿이어도 한 칸(기본값이면 그 값들, 키면 지금 프레임의 키들).
    ///   상자를 끌었으면 상자에 닿은 그림들을 고른다(Shift · Ctrl이면 더한다). 빈 곳을 그냥 눌렀으면 선택을 푼다.
    ///   그림을 끌지 않고 눌렀으면 — 여럿을 고른 채였으면 그것 하나만 남기고, 하나였고 그 자리에 다른 그림이 겹쳐 있으면 그 밑의 것으로 넘긴다.
    /// </summary>
    private void EndEmojiDrag()
    {
        EmojiManip? manip = _emojiManip;
        EmojiClick? click = _emojiClick;
        EmojiBox? box = _emojiBox;
        int selectedBefore = _selectedEmojiRigs.Count;
        CancelEmojiPress();

        if (manip is { Moved: true } && _editor is not null && ShownEmoji is { } emoji)
        {
            CommitEmojiManip(emoji, manip);
        }
        else if (box is not null)
        {
            if (box.Moved)
            {
                IEnumerable<string> touched = EmojiRigsIn(new Rect(box.Start, box.Now).Normalize());
                PickEmojiRigs(box.Additive ? _selectedEmojiRigs.Concat(touched).Distinct(StringComparer.Ordinal) : touched);
            }
            else if (!box.Additive)
            {
                // 빈 곳을 눌렀다 — 전부 푼다("이모지 전체"까지). 고른 것이 없으면 매니퓰레이터도 없다.
                bool wasWhole = _emojiWholeSelected;
                _emojiWholeSelected = false;
                PickEmojiRigs(Array.Empty<string>());

                if (wasWhole)
                {
                    EmojiRigPicked?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        else if (click is { WasSelected: true })
        {
            if (selectedBefore > 1)
            {
                PickEmojiRigs(new[] { click.Chosen });
            }
            else if (click.Candidates.Count > 1)
            {
                int next = (click.Candidates.ToList().IndexOf(click.Chosen) + 1) % click.Candidates.Count;
                PickEmojiRigs(new[] { click.Candidates[next] });
            }
        }

        InvalidateVisual();
    }

    private void CancelEmojiPress()
    {
        _emojiManip = null;
        _emojiClick = null;
        _emojiBox = null;
        _emojiPress = false;
    }

    /// <summary>상자(컨트롤 좌표)에 닿은 그림들 — 그려지는 순서. 지금 프레임에 거의 안 보이는 그림은 빼고, 다 안 보이면 다 넣는다.</summary>
    private IReadOnlyList<string> EmojiRigsIn(Rect box)
    {
        if (EmojiNow() is not { } now)
        {
            return Array.Empty<string>();
        }

        var seen = new List<string>();
        var faint = new List<string>();

        foreach (EmojiRigPose rig in now.Rigs)
        {
            Quad quad = EmojiQuad(now.Tree, rig);
            (Point min, Point max) = ControlBox(new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft });

            if (box.Intersects(new Rect(min, max)))
            {
                (now.Alpha * rig.Alpha >= 0.05f ? seen : faint).Add(rig.Rig);
            }
        }

        return seen.Count > 0 ? seen : faint;
    }

    /// <summary>끈 것을 문서에 적는다 — 한 칸.</summary>
    private void CommitEmojiManip(EmojiAsset emoji, EmojiManip manip)
    {
        if (!manip.Key)
        {
            _editor!.Batch(MotionChangeKind.Emoji, () =>
            {
                foreach (EmojiManipItem item in manip.Items)
                {
                    if (emoji.FindRig(item.Rig) is { } rig)
                    {
                        _editor.SetEmojiRigPlace(emoji.Name, rig.Name, item.NowX, item.NowY, item.NowSx, item.NowRotation);
                    }
                    else
                    {
                        _editor.SetEmojiOffset(emoji.Name, item.NowX, item.NowY);
                    }
                }
            });
            return;
        }

        // 첫 키면 채널이 생긴다 — 목록을 다시 만들게 구조의 변화로 알린다.
        _editor!.Batch(MotionChangeKind.Structure, () =>
        {
            foreach (EmojiManipItem item in manip.Items)
            {
                (ChannelKind kind, (Axis, float)[] values) = manip.Drag switch
                {
                    Drag.Rotate => (ChannelKind.Euler, new[] { (Axis.Z, item.NowRotation) }),
                    Drag.ScaleFree or Drag.ScaleX or Drag.ScaleY => (ChannelKind.Scale, new[] { (Axis.X, item.NowSx), (Axis.Y, item.NowSy) }),
                    _ => (ChannelKind.Position, new[] { (Axis.X, item.NowX), (Axis.Y, item.NowY) }),
                };

                KeyEmoji(emoji, item.Rig, kind, values);

                // 무리째 돌리거나 늘였으면 자리도 옮겨 갔다 — 자리의 키도 함께 놓는다.
                if (manip.Group && kind != ChannelKind.Position)
                {
                    KeyEmoji(emoji, item.Rig, ChannelKind.Position, new[] { (Axis.X, item.NowX), (Axis.Y, item.NowY) });
                }
            }
        });
        PoseKeyed?.Invoke(this, EventArgs.Empty);
    }

    private void KeyEmoji(EmojiAsset emoji, string rig, ChannelKind kind, IReadOnlyList<(Axis, float)> values)
    {
        if (_editor is null || EmojiSource() is not { } source)
        {
            return;
        }

        _editor.KeyComponents(emoji.Timeline.Name, source.Frame.TargetNode(rig), kind, (float)EmojiKeyTime(), values, DefaultTangent);
    }

    /// <summary>
    /// S — 고른 그림들(이모지 전체를 골랐으면 그것)의 지금 값을 지금 프레임에 키한다. 도구의 종류만(이동 = 자리 · 회전 · 크기), 선택 도구면 셋 다. 한 칸.
    /// </summary>
    private void KeyEmojiTarget()
    {
        if (_editor is null || _emojiLife || ShownEmoji is not { } emoji)
        {
            return;
        }

        IReadOnlyList<string> targets = EmojiTargets();

        if (targets.Count == 0)
        {
            return;
        }

        _editor.Batch(MotionChangeKind.Structure, () =>
        {
            foreach (string rig in targets)
            {
                EmojiNodePose pose = EmojiKeyPose(rig);

                if (Manipulator is ManipulatorMode.Move or ManipulatorMode.Select)
                {
                    KeyEmoji(emoji, rig, ChannelKind.Position, new[] { (Axis.X, pose.Position.X), (Axis.Y, pose.Position.Y) });
                }

                if (Manipulator is ManipulatorMode.Rotate or ManipulatorMode.Select)
                {
                    KeyEmoji(emoji, rig, ChannelKind.Euler, new[] { (Axis.Z, pose.Rotation) });
                }

                if (Manipulator is ManipulatorMode.Scale or ManipulatorMode.Select)
                {
                    KeyEmoji(emoji, rig, ChannelKind.Scale, new[] { (Axis.X, pose.Scale.X), (Axis.Y, pose.Scale.Y) });
                }
            }
        });
        PoseKeyed?.Invoke(this, EventArgs.Empty);
    }

    // ── 고르기 ───────────────────────────────────────────────────────

    /// <summary>
    /// 그 자리(루트 공간)에 맞은 그림들 — 위에 그려진 것부터. 그림의 빈 곳(알파)은 맞지 않는다: 사각형이 겹쳐도 보이는 그림을 누른 것이 골라진다.
    /// 지금 프레임에 거의 안 보이는 그림(투명도)은 뒤로 미룬다. 알파를 모르는 그림(못 읽었거나 자리표시)은 사각형 전체가 맞는다.
    /// </summary>
    private IReadOnlyList<string> EmojiRigsAt(EmojiFrameView now, Vec2 root)
    {
        var seen = new List<string>();
        var faint = new List<string>();

        foreach (EmojiRigPose rig in now.Rigs.Reverse())
        {
            Quad quad = EmojiQuad(now.Tree, rig);

            if (!Contains(quad, root) || !EmojiPixelHit(rig, quad, root))
            {
                continue;
            }

            (now.Alpha * rig.Alpha >= 0.05f ? seen : faint).Add(rig.Rig);
        }

        seen.AddRange(faint);
        return seen;
    }

    /// <summary>테스트 손잡이 — 그 자리에 맞은 그림들(위에서 아래로).</summary>
    internal IReadOnlyList<string> EmojiRigsAtForTest(Vec2 root) => EmojiNow() is { } now ? EmojiRigsAt(now, root) : Array.Empty<string>();

    /// <summary>테스트 손잡이 — 컨트롤 좌표의 그 자리에 맞은 그림들.</summary>
    internal IReadOnlyList<string> EmojiRigsAtControlForTest(Point control) => EmojiRigsAtForTest(ToRoot(control));

    /// <summary>사각형 안의 그 점이 그림의 속인가 — 점 둘레 몇 픽셀 안에 진한 픽셀이 있으면 속이다(가는 선도 잡히게). 알파를 모르면 속으로 친다.</summary>
    private bool EmojiPixelHit(EmojiRigPose rig, Quad quad, Vec2 root)
    {
        if (rig.ImagePath is not { } path || !_emojiAlpha.TryGetValue(path, out (int Width, int Height, byte[] Alpha) mask))
        {
            return true;
        }

        // p = TopLeft + u·(TopRight − TopLeft) + v·(BottomLeft − TopLeft)
        Vec2 ex = quad.TopRight - quad.TopLeft;
        Vec2 ey = quad.BottomLeft - quad.TopLeft;
        Vec2 d = root - quad.TopLeft;
        float det = ex.X * ey.Y - ex.Y * ey.X;

        if (Math.Abs(det) < 1e-9f)
        {
            return true;
        }

        float u = (d.X * ey.Y - d.Y * ey.X) / det;
        float v = (ex.X * d.Y - ex.Y * d.X) / det;
        int px = (int)(u * mask.Width);
        int py = (int)(v * mask.Height);

        // 화면의 4px쯤을 그림의 픽셀로 — 작게 보일수록 넓게 본다.
        double shown = Distance(ToControl(quad.TopLeft), ToControl(quad.TopRight));
        int reach = shown < 1e-6 ? 2 : (int)Math.Clamp(Math.Round(4 * mask.Width / shown), 1, 16);

        for (int y = Math.Max(0, py - reach); y <= Math.Min(mask.Height - 1, py + reach); y++)
        {
            for (int x = Math.Max(0, px - reach); x <= Math.Min(mask.Width - 1, px + reach); x++)
            {
                if (mask.Alpha[y * mask.Width + x] > EmojiAlphaThreshold)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ── 인물(실루엣) ─────────────────────────────────────────────────
    //
    // 초상이 없는 인물은 실루엣으로 보인다 — 머리가 로케이터(머리의 자리)에, 몸이 거기서 발까지. 이모지가 "머리에서 얼마나"인지가 보인다.
    // 로케이터의 좌표로 그리므로 왼쪽을 보는 인물이면(로케이터가 뒤집혔다) 코가 왼쪽을 향한다.

    private const float HeadHalfWidth = 90f;
    private const float HeadHalfHeight = 105f;

    private static readonly IBrush HostFill = new SolidColorBrush(Color.FromArgb(46, 0x9A, 0xB4, 0xD0));
    private static readonly Pen HostPen = new(new SolidColorBrush(Color.FromArgb(120, 0x9A, 0xB4, 0xD0)), 1);

    /// <summary>실루엣의 윤곽들(로케이터의 좌표, y 위) — 머리 · 코(보는 쪽) · 몸. <paramref name="locator"/>는 발에서 잰 머리의 자리.</summary>
    internal static IReadOnlyList<Vec2[]> HostSilhouette(Vec2 locator)
    {
        var shapes = new List<Vec2[]>();
        var head = new Vec2[24];

        for (int i = 0; i < head.Length; i++)
        {
            double a = Math.PI * 2 * i / head.Length;
            head[i] = new Vec2((float)(Math.Cos(a) * HeadHalfWidth), (float)(Math.Sin(a) * HeadHalfHeight));
        }

        shapes.Add(head);
        shapes.Add(new[] { new Vec2(HeadHalfWidth - 4f, 14f), new Vec2(HeadHalfWidth + 34f, -8f), new Vec2(HeadHalfWidth - 6f, -26f) });

        // 몸 — 머리가 발보다 충분히 위일 때만. 발은 로케이터에서 (−x, −y)다.
        if (locator.Y > HeadHalfHeight * 3f)
        {
            float footX = -locator.X;
            float footY = -locator.Y;
            float neck = -HeadHalfHeight + 6f;
            float shoulder = Math.Max(footY, neck - 95f);
            shapes.Add(new[]
            {
                new Vec2(-36f, neck), new Vec2(36f, neck), new Vec2(190f, shoulder), new Vec2(footX + 150f, footY),
                new Vec2(footX - 150f, footY), new Vec2(-190f, shoulder),
            });
        }

        return shapes;
    }

    private void DrawEmojiHostSilhouette(DrawingContext context, RectNodeTree tree, Vec2 locator)
    {
        string key = EmojiKeyPrefix + RigSchemaLoader.RootKey;

        foreach (Vec2[] shape in HostSilhouette(locator))
        {
            Point[] points = shape.Select(p => ToControl(tree.TransformPoint(key, new Vec3(p.X, p.Y, 0f)).XY)).ToArray();
            context.DrawGeometry(HostFill, HostPen, new PolylineGeometry(points.Append(points[0]), true));
        }
    }

    /// <summary>테스트 손잡이 — 로케이터(머리의 자리)가 선 곳(컨트롤 좌표).</summary>
    internal Point? EmojiLocatorControlPoint()
        => EmojiNow() is { } now ? ToControl(now.Tree.TransformPoint(EmojiKeyPrefix + RigSchemaLoader.RootKey, Vec3.Zero).XY) : null;

    /// <summary>테스트 손잡이 — 인물의 발(슬롯의 아래 가운데, 컨트롤 좌표). 인물이 없으면 null.</summary>
    internal Point? EmojiHostFeetControlPoint()
    {
        if (EmojiNow() is not { } now || EmojiHost(now.Tree) is not { } host)
        {
            return null;
        }

        Vec2 locator = _emojiLocators.Resolve(host.Character);
        return ToControl(now.Tree.TransformPoint(EmojiKeyPrefix + RigSchemaLoader.RootKey, new Vec3(-locator.X, -locator.Y, 0f)).XY);
    }

    /// <summary>
    /// 이모지에 맞춤 — 리그 그림들(지금 시각, 투명해도)을 감싼 상자가 뷰포트의 절반을 채우고 가운데에 오도록 보기 배율 · 이동을 바꾼다.
    /// <paramref name="withHost"/>면 인물의 머리까지 감싼다(이모지가 머리에서 얼마나 떨어졌는지가 보이게 — 이모지를 처음 열 때의 보기).
    /// 보기만 바뀐다(데이터 · 되돌리기 없음). 이모지가 없으면 false.
    /// </summary>
    public bool FrameEmoji(bool withHost = false)
    {
        if (EmojiNow() is not { } now || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return false;
        }

        var corners = new List<Vec2>();

        foreach (EmojiRigPose rig in now.Rigs)
        {
            Quad quad = EmojiQuad(now.Tree, rig);
            corners.AddRange(new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft });
        }

        if (corners.Count == 0)
        {
            return false;
        }

        if (withHost && _emojiShowHost && EmojiHost(now.Tree) is not null)
        {
            string locator = EmojiKeyPrefix + RigSchemaLoader.RootKey;

            foreach ((float x, float y) in new[] { (-HeadHalfWidth, -HeadHalfHeight * 2.2f), (HeadHalfWidth, -HeadHalfHeight * 2.2f), (HeadHalfWidth, HeadHalfHeight), (-HeadHalfWidth, HeadHalfHeight) })
            {
                corners.Add(now.Tree.TransformPoint(locator, new Vec3(x, y, 0f)).XY);
            }
        }

        (Point min, Point max) = ControlBox(corners);
        double width = max.X - min.X;
        double height = max.Y - min.Y;

        if (width < 1e-6 && height < 1e-6)
        {
            return false;
        }

        Vec2 center = ToRoot(new Point((min.X + max.X) / 2, (min.Y + max.Y) / 2));
        double factor = Math.Min(width < 1e-6 ? double.MaxValue : Bounds.Width * 0.5 / width, height < 1e-6 ? double.MaxValue : Bounds.Height * 0.5 / height);
        _viewZoom = Math.Clamp(_viewZoom * factor, MinViewZoom, MaxViewZoom);
        Point moved = ToControl(center);
        _viewPan = new Vector(_viewPan.X + Bounds.Width / 2 - moved.X, _viewPan.Y + Bounds.Height / 2 - moved.Y);
        InvalidateVisual();
        return true;
    }

    /// <summary>테스트 손잡이 — 리그 그림들을 감싼 상자(컨트롤 좌표, 지금 보기).</summary>
    internal Rect? EmojiControlBox()
    {
        if (EmojiNow() is not { } now || now.Rigs.Count == 0)
        {
            return null;
        }

        var corners = new List<Vec2>();

        foreach (EmojiRigPose rig in now.Rigs)
        {
            Quad quad = EmojiQuad(now.Tree, rig);
            corners.AddRange(new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft });
        }

        (Point min, Point max) = ControlBox(corners);
        return new Rect(min, max);
    }

    private (Point Min, Point Max) ControlBox(IEnumerable<Vec2> roots)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        foreach (Vec2 root in roots)
        {
            Point p = ToControl(root);
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return (new Point(minX, minY), new Point(maxX, maxY));
    }

    /// <summary>테스트 손잡이 — 루트 공간의 점에서 리그를 눌러 끌고 놓는다(마우스 대신).</summary>
    internal bool DragEmojiRigForTest(Vec2 fromRoot, Vec2 toRoot)
    {
        if (!TryStartEmojiDrag(ToScreen(fromRoot)))
        {
            return false;
        }

        DragEmojiPlace(ToScreen(toRoot));
        EndEmojiDrag();
        return true;
    }

    /// <summary>테스트 손잡이 — 컨트롤 좌표에서 누르고 끌고 놓는다(매니퓰레이터의 손잡이를 잡을 때). 잡은 것이 없으면 거짓.</summary>
    internal bool DragEmojiForTest(Point from, Point to, bool additive = false)
    {
        if (!TryStartEmojiDrag(from, additive))
        {
            return false;
        }

        DragEmojiPlace(to);
        EndEmojiDrag();
        return true;
    }

    /// <summary>테스트 손잡이 — 그 자리(루트 공간)를 끌지 않고 누른다.</summary>
    internal bool ClickEmojiForTest(Vec2 root)
    {
        bool pressed = TryStartEmojiDrag(ToScreen(root));
        EndEmojiDrag();
        return pressed;
    }

    /// <summary>테스트 손잡이 — 리그의 그림이 보이는 사각형의 가운데(루트 공간).</summary>
    internal Vec2? EmojiRigCenter(string rig)
    {
        if (EmojiNow() is not { } now || now.Rigs.FirstOrDefault(r => r.Rig == rig) is not { Rig: not null } pose)
        {
            return null;
        }

        Quad quad = EmojiQuad(now.Tree, pose);
        return (quad.BottomLeft + quad.TopRight) * 0.5f;
    }

    private static bool Contains(Quad quad, Vec2 p)
    {
        // 볼록 사각형 — 네 변의 같은 쪽.
        Vec2[] corners = { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft };
        float sign = 0f;

        for (int i = 0; i < 4; i++)
        {
            Vec2 a = corners[i];
            Vec2 b = corners[(i + 1) % 4];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

            if (Math.Abs(cross) < 1e-9f)
            {
                continue;
            }

            if (sign == 0f)
            {
                sign = Math.Sign(cross);
            }
            else if (Math.Sign(cross) != sign)
            {
                return false;
            }
        }

        return true;
    }
}
