using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
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
// 타임라인 보기에서는 리그의 그림을 끌어 기본 자리(EmojiRig_Place)를 옮긴다 — 키가 아니다. 놓을 때 한 칸.
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

    // 기본 자리 끌기 — 누른 자리(루트 공간)와 그때의 기본 자리. 끄는 동안은 문서를 바꾸지 않고 보이기만 한다.
    private (string Rig, Vec2 StartRoot, float StartX, float StartY)? _emojiDrag;
    private (float X, float Y)? _emojiDragPlace;

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
                _emojiDrag = null;
                _emojiDragPlace = null;
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
        InvalidateVisual();
    }

    public void ClearEmojiImages() => _emojiImages.Clear();

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

        // 끄는 중이면 그 리그의 기본 자리만 바꾼 사본으로 세운다(문서는 놓을 때 바뀐다).
        if (_emojiDrag is { } drag && _emojiDragPlace is { } place && emoji.FindRig(drag.Rig) is { } dragged)
        {
            emoji = new EmojiAsset
            {
                Name = emoji.Name, OffsetX = emoji.OffsetX, OffsetY = emoji.OffsetY, Timeline = emoji.Timeline, Spans = emoji.Spans, FixedSide = emoji.FixedSide,
                Rigs = emoji.Rigs.Select(r => r == dragged
                    ? new EmojiRigAsset { Name = r.Name, Images = r.Images, PlaceX = place.X, PlaceY = place.Y, Scale = r.Scale, Rotation = r.Rotation, Upright = r.Upright }
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
        DrawLabel(context, now.Label + (_emojiLife ? "" : _emojiFacingLeft ? " — 기본 자리는 보기를 되돌리고 끈다" : " — 그림을 끌면 기본 자리(키가 아니다)"), new Point(stage.X + 8, stage.Y + 6));
    }

    /// <summary>타임라인 보기에서 리그의 그림을 눌렀다 — 고르고 끌기를 시작한다. 맞은 것이 없으면 거짓.</summary>
    private bool TryStartEmojiDrag(Point position)
    {
        if (_emojiLife || ShownEmoji is not { } emoji || EmojiNow() is not { Visible: true } now)
        {
            return false;
        }

        Vec2 root = ToRoot(position);

        // 위에 그려진 것(뒤의 리그)부터.
        foreach (EmojiRigPose rig in now.Rigs.Reverse())
        {
            if (!Contains(EmojiQuad(now.Tree, rig), root) || emoji.FindRig(rig.Rig) is not { } asset)
            {
                continue;
            }

            SelectedEmojiRig = rig.Rig;
            EmojiRigPicked?.Invoke(this, EventArgs.Empty);

            // 거울 속에서는 고르기만 한다 — 기본 자리는 왼쪽을 보는 인물로 보기를 끄고 옮긴다.
            if (_emojiFacingLeft)
            {
                return true;
            }

            _emojiDrag = (rig.Rig, root, asset.PlaceX, asset.PlaceY);
            _emojiDragPlace = (asset.PlaceX, asset.PlaceY);
            return true;
        }

        return false;
    }

    /// <summary>끄는 중 — 루트 공간의 이동을 오프셋 노드(기본 자리의 부모)의 공간으로 되돌려 기본 자리에 더한다.</summary>
    private void DragEmojiPlace(Point position)
    {
        if (_emojiDrag is not { } drag || EmojiNow() is not { } now)
        {
            return;
        }

        string parent = EmojiKeyPrefix + (EmojiSource()?.Frame.OffsetNode ?? "");
        Vec2 origin = now.Tree.TransformPoint(parent, Vec3.Zero).XY;
        Vec2 ex = now.Tree.TransformPoint(parent, new Vec3(1f, 0f, 0f)).XY - origin;
        Vec2 ey = now.Tree.TransformPoint(parent, new Vec3(0f, 1f, 0f)).XY - origin;
        Vec2 delta = ToRoot(position) - drag.StartRoot;
        float det = ex.X * ey.Y - ex.Y * ey.X;

        if (Math.Abs(det) < 1e-9f)
        {
            return;
        }

        float lx = (delta.X * ey.Y - delta.Y * ey.X) / det;
        float ly = (ex.X * delta.Y - ex.Y * delta.X) / det;
        _emojiDragPlace = ((float)Math.Round(drag.StartX + lx, 1), (float)Math.Round(drag.StartY + ly, 1));
        InvalidateVisual();
    }

    /// <summary>놓았다 — 기본 자리를 문서에 적는다(한 칸, 같은 자리면 흔적이 없다).</summary>
    private void EndEmojiDrag()
    {
        if (_emojiDrag is { } drag && _emojiDragPlace is { } place && _editor is not null && ShownEmoji is { } emoji && emoji.FindRig(drag.Rig) is { } rig)
        {
            _emojiDrag = null;
            _emojiDragPlace = null;
            _editor.SetEmojiRigPlace(emoji.Name, rig.Name, place.X, place.Y, rig.Scale, rig.Rotation);
        }

        _emojiDrag = null;
        _emojiDragPlace = null;
        InvalidateVisual();
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
