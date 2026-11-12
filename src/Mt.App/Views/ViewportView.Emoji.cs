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

// 이모지의 미리보기(docs/PLAN-emoji.md E6). 이모지를 고르면 뷰포트의 시간은 그 이모지의 것이다:
//   타임라인 보기 — 시계 = 타임라인(초). 그래프 · 도프 시트와 같은 시각이다. 그 프레임을 내보내는 길 그대로 읽어(EmojiPreview.Timeline) 보인다.
//   수명 보기     — 시계 = 등장 → 머묾 × 바퀴 → 퇴장(머묾이 없으면 main 한 번). 코어 재생(EmojiPlayback)을 그대로 돌린다.
// 이모지는 런타임처럼 캐릭터의 CharSlot_Scale 밑(로케이터 자리 — 발)에 선다. 씬에 캐릭터가 없으면 무대 가운데.
// 타임라인 보기에서는 뷰포트에서 직접 만진다(docs/PLAN-ux.md U4) — 그림을 눌러 고르고, 매니퓰레이터(W 이동 · E 회전 · R 크기)로 끈다.
//   Auto Key가 꺼져 있으면 기본값(그림의 자리 · 크기 · 회전 = EmojiRig_Place, 이모지 전체는 머리에서의 자리)이 바뀐다 — 키가 아니다.
//   Auto Key가 켜져 있으면 지금 프레임에 키가 놓인다(그림의 키 = EmojiRig_Image, 이모지 전체의 키 = Emoji_Offset).
// 끄는 동안은 보이기만 하고 놓을 때 한 칸. 그림을 고르지 않았으면 대상은 이모지 전체다.
public sealed partial class ViewportView
{
    private const string EmojiKeyPrefix = "emoji/";
    private const string HostNode = "CharSlot_Scale";

    private static readonly IPen EmojiRigPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 0xF0, 0xC0, 0x40)), 1, DashStyle.Dash);
    private static readonly IPen SelectedEmojiRigPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x40)), 2);
    private static readonly IPen LocatorPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0x40, 0xC0, 0xF0)), 1);

    private string? _emojiName;
    private bool _emojiLife;
    private bool _emojiFacingLeft;
    private int _emojiHoldCycles = 2;
    private string? _selectedEmojiRig;
    private readonly Dictionary<string, Bitmap?> _emojiImages = new(StringComparer.Ordinal);
    private (long Revision, string Name, EmojiRigFrame Frame, EmojiDefinition? Definition, EmojiClip? Timeline, string? Why)? _emojiCache;

    /// <summary>끄는 중인 조작 하나 — 대상 · 기본값인가 키인가 · 시작 값과 지금 값. 끄는 동안은 문서를 바꾸지 않고 보이기만 한다.</summary>
    private sealed class EmojiManip
    {
        /// <summary>대상 그림(리그)의 이름. 빈 이름은 이모지 전체.</summary>
        public string Rig = "";

        /// <summary>참이면 지금 프레임의 키, 거짓이면 기본값.</summary>
        public bool Key;

        public Drag Drag;

        /// <summary>값이 들어가는 노드(트리의 키) — 끈 거리를 이 노드의 부모 공간으로 되돌린다.</summary>
        public string Node = "";

        public Vec2 StartRoot;
        public Point StartControl;
        public Vec2 PivotRoot;
        public Vec2 ScaleDirection;
        public float X, Y, Sx = 1f, Sy = 1f, Rotation;
        public float NowX, NowY, NowSx = 1f, NowSy = 1f, NowRotation;
        public bool Moved;
    }

    /// <summary>누른 자리에 겹쳐 있던 그림들(위에서 아래로)과 고른 것 — 끌지 않고 놓으면 그 밑의 것으로 넘긴다.</summary>
    private sealed record EmojiClick(IReadOnlyList<string> Candidates, string Chosen, bool WasSelected, Point At);

    private EmojiManip? _emojiManip;
    private EmojiClick? _emojiClick;
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

    /// <summary>고른 리그 — 끌기의 대상이고 테두리가 진하다.</summary>
    public string? SelectedEmojiRig
    {
        get => _selectedEmojiRig;
        set
        {
            if (_selectedEmojiRig != value)
            {
                _selectedEmojiRig = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>뷰포트에서 리그를 눌러 골랐다.</summary>
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

        // 다른 이모지로 오면 그 그림에 맞춘다(보기만). 이모지를 놓을 때 무대로 돌아오는 것은 Bind가 한다.
        if (emojiName is not null && emojiName != before)
        {
            FrameEmoji();
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
            EmojiRigAsset? dragged = emoji.FindRig(basing.Rig);
            emoji = new EmojiAsset
            {
                Name = emoji.Name,
                OffsetX = basing.Rig.Length == 0 ? basing.NowX : emoji.OffsetX,
                OffsetY = basing.Rig.Length == 0 ? basing.NowY : emoji.OffsetY,
                Timeline = emoji.Timeline, Spans = emoji.Spans, FixedSide = emoji.FixedSide,
                Rigs = emoji.Rigs.Select(r => r == dragged
                    ? new EmojiRigAsset { Name = r.Name, Images = r.Images, PlaceX = basing.NowX, PlaceY = basing.NowY, Scale = basing.NowSx, Rotation = basing.NowRotation, Upright = r.Upright }
                    : r).ToList(),
            };
        }

        RectNodeTree tree = _template?.Clone() ?? new RectNodeTree(new RectSpace(_stageSize, Vec2.Half));
        SceneObject? host = EmojiHost(tree);
        EmojiPreview.Attach(tree, emoji, source.Frame, EmojiKeyPrefix, host?.KeyOf(HostNode), _emojiFacingLeft);

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

        // 키를 끄는 중이면 그 노드의 지금 값만 덮는다(키는 놓을 때 들어간다). 이모지 전체의 자리는 머리에서의 자리에 더한 것이다.
        if (_emojiManip is { Key: true, Moved: true } keying && tree.Contains(keying.Node))
        {
            bool whole = keying.Rig.Length == 0;
            RectNodeState state = tree.GetState(keying.Node)
                .WithAnchoredPosition(new Vec2((whole ? emoji.OffsetX : 0f) + keying.NowX, (whole ? emoji.OffsetY : 0f) + keying.NowY))
                .WithLocalScale(new Vec3(keying.NowSx, keying.NowSy, 1f))
                .WithLocalEuler(new Vec3(0f, 0f, keying.NowRotation));
            tree.SetState(keying.Node, state);
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

        DrawObjects(context, now.Tree);

        // 로케이터(이모지가 서는 자리) — 작은 십자.
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
                    context.DrawGeometry(null, rig.Rig == _selectedEmojiRig ? SelectedEmojiRigPen : EmojiRigPen, outline);
                    DrawLabel(context, rig.Rig + (rig.Sprite is null ? "" : $" · {rig.Sprite}"), new Point(Math.Min(topLeft.X, bottomLeft.X) + 2, Math.Max(bottomLeft.Y, bottomRight.Y) + 2));
                }
            }
        }

        DrawOutside(context, stage);
        context.DrawRectangle(StagePen, stage);
        DrawEmojiGizmo(context, now);
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

        string target = _selectedEmojiRig ?? EmojiWords.WholeEmoji;
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

        if (!AutoKey && _selectedEmojiRig is null && Manipulator != ManipulatorMode.Move)
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
           && (AutoKey || _selectedEmojiRig is not null || Manipulator == ManipulatorMode.Move);

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
        if (!EmojiCanManipulate() || EmojiTargetNode(now, _selectedEmojiRig ?? "") is not { } key)
        {
            return null;
        }

        (Vec2 origin, Vec2 xAxis, Vec2 yAxis) = PortraitLayout.Axes(now.Tree, key, 100f);
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
    /// 타임라인 보기에서 눌렀다. 매니퓰레이터의 손잡이면 지금 대상을 끌기 시작하고, 그림이면 고른다(이동 도구면 고르면서 바로 끈다).
    /// 빈 곳이면 그림 선택을 푼다(대상이 이모지 전체가 된다). 무엇인가 잡았으면 참.
    /// </summary>
    private bool TryStartEmojiDrag(Point position)
    {
        if (_emojiLife || ShownEmoji is not { } emoji || EmojiNow() is not { Visible: true } now)
        {
            return false;
        }

        _emojiManip = null;
        _emojiClick = null;

        // 1. 매니퓰레이터의 손잡이 — 고른 것을 끈다. 그 밑에 다른 그림이 있어도 고르기가 넘어가지 않는다.
        //    대상이 이모지 전체일 때 가운데 손잡이는 그림 위에 놓이기 쉽다 — 그 자리에 그림이 있으면 그림 고르기가 이긴다(팔은 늘 잡힌다).
        if (EmojiGizmoPoints(now) is { } gizmo && GizmoDragAt(position, gizmo) is var handle && handle != Drag.None
            && !(_selectedEmojiRig is null && handle is Drag.MoveFree or Drag.ScaleFree && EmojiRigsAt(now, ToRoot(position)).Count > 0))
        {
            _emojiPress = BeginEmojiManip(emoji, now, _selectedEmojiRig ?? "", handle, position, gizmo);
            return _emojiPress;
        }

        // 2. 그림 — 맞은 것들 가운데 이미 고른 것이 있으면 그것을 지킨다(겹친 그림을 끌 수 있게).
        IReadOnlyList<string> hits = EmojiRigsAt(now, ToRoot(position));

        if (hits.Count == 0)
        {
            if (_selectedEmojiRig is not null)
            {
                SelectedEmojiRig = null;
                EmojiRigPicked?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }

        string? before = _selectedEmojiRig;
        string chosen = before is not null && hits.Contains(before) ? before : hits[0];
        _emojiClick = new EmojiClick(hits, chosen, chosen == before, position);

        if (chosen != before)
        {
            SelectedEmojiRig = chosen;
            EmojiRigPicked?.Invoke(this, EventArgs.Empty);
        }

        // 이동 도구면 고르면서 바로 끈다(손잡이를 따로 잡지 않아도 된다).
        if (Manipulator == ManipulatorMode.Move && EmojiCanManipulate() && EmojiGizmoPoints(EmojiNow() ?? now) is { } picked)
        {
            BeginEmojiManip(emoji, now, chosen, Drag.MoveFree, position, picked);
        }

        _emojiPress = true;
        return true;
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

    /// <summary>끌기를 시작한다 — 대상의 시작 값을 읽는다(Auto Key면 지금 프레임의 키 값, 아니면 기본값).</summary>
    private bool BeginEmojiManip(EmojiAsset emoji, EmojiFrameView now, string rig, Drag drag, Point position, (Point Center, Point XEnd, Point YEnd) gizmo)
    {
        if (EmojiSource() is not { } source)
        {
            return false;
        }

        var manip = new EmojiManip { Rig = rig, Key = AutoKey, Drag = drag, StartRoot = ToRoot(position), StartControl = position };

        if (manip.Key)
        {
            EmojiNodePose pose = EmojiKeyPose(rig);
            (manip.X, manip.Y, manip.Sx, manip.Sy, manip.Rotation) = (pose.Position.X, pose.Position.Y, pose.Scale.X, pose.Scale.Y, pose.Rotation);
            manip.Node = EmojiKeyPrefix + source.Frame.TargetNode(rig);
        }
        else if (emoji.FindRig(rig) is { } asset)
        {
            (manip.X, manip.Y, manip.Sx, manip.Sy, manip.Rotation) = (asset.PlaceX, asset.PlaceY, asset.Scale, asset.Scale, asset.Rotation);
            manip.Node = EmojiKeyPrefix + source.Frame.PlaceNodeOf(rig);
        }
        else
        {
            (manip.X, manip.Y) = (emoji.OffsetX, emoji.OffsetY);
            manip.Node = EmojiKeyPrefix + source.Frame.OffsetNode;
        }

        if (!now.Tree.Contains(manip.Node))
        {
            return false;
        }

        (manip.NowX, manip.NowY, manip.NowSx, manip.NowSy, manip.NowRotation) = (manip.X, manip.Y, manip.Sx, manip.Sy, manip.Rotation);
        manip.PivotRoot = ToRoot(gizmo.Center);
        Point arm = drag == Drag.ScaleY ? gizmo.YEnd : gizmo.XEnd;
        manip.ScaleDirection = new Vec2((float)(arm.X - gizmo.Center.X), (float)(arm.Y - gizmo.Center.Y));
        _emojiManip = manip;
        return true;
    }

    /// <summary>끄는 중 — 끈 만큼 지금 값을 바꾼다(보이기만). 이동은 그 노드의 부모 공간으로 되돌린 거리, 회전은 중심을 도는 각, 크기는 팔 방향으로 잰 배.</summary>
    private void DragEmojiPlace(Point position)
    {
        if (_emojiManip is not { } manip || EmojiNow() is not { } now || !now.Tree.Contains(manip.Node))
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
                Vec2 delta = PoseEdit.RootDeltaToLocal(now.Tree, manip.Node, manip.StartRoot, root);
                manip.NowX = (float)Math.Round(manip.X + (manip.Drag == Drag.MoveY ? 0f : delta.X), 1);
                manip.NowY = (float)Math.Round(manip.Y + (manip.Drag == Drag.MoveX ? 0f : delta.Y), 1);
                break;
            }

            case Drag.Rotate:
                manip.NowRotation = (float)Math.Round(manip.Rotation + PoseEdit.AngleDelta(manip.PivotRoot, manip.StartRoot, root), 1);
                break;

            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
            {
                var from = new Vec2((float)manip.StartControl.X, (float)manip.StartControl.Y);
                var to = new Vec2((float)position.X, (float)position.Y);
                float factor = PoseEdit.ScaleAlong(manip.ScaleDirection, from, to, (float)GizmoArm);

                // 그림의 기본 크기는 값 하나다(가로 세로가 같이 간다). 키는 축마다 따로.
                bool both = !manip.Key || manip.Drag == Drag.ScaleFree;
                manip.NowSx = both || manip.Drag == Drag.ScaleX ? (float)Math.Round(manip.Sx * factor, 3) : manip.Sx;
                manip.NowSy = both || manip.Drag == Drag.ScaleY ? (float)Math.Round(manip.Sy * factor, 3) : manip.Sy;
                break;
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// 놓았다. 끌었으면 문서에 적는다(한 칸 — 기본값이면 그 값, 키면 지금 프레임의 키).
    /// 끌지 않았으면 누르기다 — 이미 고른 그림을 다시 눌렀고 그 자리에 다른 그림이 겹쳐 있으면 그 밑의 것으로 넘긴다.
    /// </summary>
    private void EndEmojiDrag()
    {
        EmojiManip? manip = _emojiManip;
        EmojiClick? click = _emojiClick;
        CancelEmojiPress();

        if (manip is { Moved: true } && _editor is not null && ShownEmoji is { } emoji)
        {
            CommitEmojiManip(emoji, manip);
        }
        else if (click is { WasSelected: true, Candidates.Count: > 1 })
        {
            int next = (click.Candidates.ToList().IndexOf(click.Chosen) + 1) % click.Candidates.Count;
            SelectedEmojiRig = click.Candidates[next];
            EmojiRigPicked?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    private void CancelEmojiPress()
    {
        _emojiManip = null;
        _emojiClick = null;
        _emojiPress = false;
    }

    private void CommitEmojiManip(EmojiAsset emoji, EmojiManip manip)
    {
        if (!manip.Key)
        {
            if (emoji.FindRig(manip.Rig) is { } rig)
            {
                _editor!.SetEmojiRigPlace(emoji.Name, rig.Name, manip.NowX, manip.NowY, manip.NowSx, manip.NowRotation);
            }
            else
            {
                _editor!.SetEmojiOffset(emoji.Name, manip.NowX, manip.NowY);
            }

            return;
        }

        (ChannelKind kind, (Axis, float)[] values) = manip.Drag switch
        {
            Drag.Rotate => (ChannelKind.Euler, new[] { (Axis.Z, manip.NowRotation) }),
            Drag.ScaleFree or Drag.ScaleX or Drag.ScaleY => (ChannelKind.Scale, new[] { (Axis.X, manip.NowSx), (Axis.Y, manip.NowSy) }),
            _ => (ChannelKind.Position, new[] { (Axis.X, manip.NowX), (Axis.Y, manip.NowY) }),
        };

        KeyEmoji(emoji, manip.Rig, kind, values);
    }

    private void KeyEmoji(EmojiAsset emoji, string rig, ChannelKind kind, IReadOnlyList<(Axis, float)> values)
    {
        if (_editor is null || EmojiSource() is not { } source)
        {
            return;
        }

        _editor.KeyComponents(emoji.Timeline.Name, source.Frame.TargetNode(rig), kind, (float)EmojiKeyTime(), values, DefaultTangent);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// S — 지금 대상(고른 그림, 없으면 이모지 전체)의 지금 값을 지금 프레임에 키한다. 도구의 종류만(이동 = 자리 · 회전 · 크기), 선택 도구면 셋 다. 한 칸.
    /// </summary>
    private void KeyEmojiTarget()
    {
        if (_editor is null || _emojiLife || ShownEmoji is not { } emoji)
        {
            return;
        }

        string rig = _selectedEmojiRig ?? "";
        EmojiNodePose pose = EmojiKeyPose(rig);
        var kinds = new List<(ChannelKind, (Axis, float)[])>();

        if (Manipulator is ManipulatorMode.Move or ManipulatorMode.Select)
        {
            kinds.Add((ChannelKind.Position, new[] { (Axis.X, pose.Position.X), (Axis.Y, pose.Position.Y) }));
        }

        if (Manipulator is ManipulatorMode.Rotate or ManipulatorMode.Select)
        {
            kinds.Add((ChannelKind.Euler, new[] { (Axis.Z, pose.Rotation) }));
        }

        if (Manipulator is ManipulatorMode.Scale or ManipulatorMode.Select)
        {
            kinds.Add((ChannelKind.Scale, new[] { (Axis.X, pose.Scale.X), (Axis.Y, pose.Scale.Y) }));
        }

        _editor.Batch(MotionChangeKind.Keys, () =>
        {
            foreach ((ChannelKind kind, (Axis, float)[] values) in kinds)
            {
                KeyEmoji(emoji, rig, kind, values);
            }
        });
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

    /// <summary>
    /// 이모지에 맞춤 — 리그 그림들(지금 시각, 투명해도)을 감싼 상자가 뷰포트의 절반을 채우고 가운데에 오도록 보기 배율 · 이동을 바꾼다.
    /// 로케이터가 배역의 발치라 그대로는 무대 아래 작게 보인다. 보기만 바뀐다(데이터 · 되돌리기 없음). 이모지가 없으면 false.
    /// </summary>
    public bool FrameEmoji()
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
    internal bool DragEmojiForTest(Point from, Point to)
    {
        if (!TryStartEmojiDrag(from))
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
