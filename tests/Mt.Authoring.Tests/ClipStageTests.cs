using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>연출 클립 K5(<c>PLAN-clip.md</c> §2.4) — 상태 블록: 씬 값이 시간에 따라 바뀐다.</summary>
public class ClipStageTests
{
    private const string Track = "CharSlot_Track";
    private const string MoveY = "CharacterPortrait_Track_Move_Y";

    private static readonly RigSchemaFile RigFile = BuiltinTuning.RigSchemas;

    /// <summary>뷰포트와 같은 조립 — 깊이·focus·무대 뼈대(카메라가 트리에 든다)까지.</summary>
    private static RectNodeTree BuildTree(SceneAsset scene)
        => ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.Depth, BuiltinTuning.Focus, BuiltinTuning.StageRig);

    /// <summary>s1·s2, nod(0.6초). clip: 배역 a(s1)·b(s2) 트랙 0·1, 카메라 트랙 2.</summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character");
        editor.AddMotion("nod", "character", 0.6f);
        editor.AddChannel("nod", MoveY, ChannelKind.Position);
        editor.SetKeys("nod", MoveY, ChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.3f, -18f), Fixtures.Key(0.6f, 0f) });

        editor.AddClip("clip");
        editor.AddClipRole("clip", "a", "character", "s1");
        editor.AddClipRole("clip", "b", "character", "s2");
        editor.AddClipTrack("clip", "a");
        editor.AddClipTrack("clip", "b");
        editor.AddClipTrack("clip", ClipTrack.CameraRole);
        return editor;
    }

    private static ClipAsset Clip(MotionEditor editor) => editor.Document.FindClip("clip")!;

    private static ClipStage Stage(MotionEditor editor, Func<ClipTrack, bool>? isMuted = null)
        => ClipStage.Build(editor.Document, Clip(editor), BuildTree, isMuted)!;

    private static void Near(float expected, float actual, float tolerance = 1e-3f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    /// <summary>두 트리의 모든 노드가 같은 상태인가 — "끝난 뒤의 무대 = 그 값으로 세운 씬".</summary>
    private static void SameTree(RectNodeTree expected, RectNodeTree actual)
    {
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (string key in expected.Keys)
        {
            RectNodeState e = expected.GetState(key);
            RectNodeState a = actual.GetState(key);
            Assert.True(
                Math.Abs(e.AnchoredPosition.X - a.AnchoredPosition.X) < 1e-3f && Math.Abs(e.AnchoredPosition.Y - a.AnchoredPosition.Y) < 1e-3f
                && Math.Abs(e.LocalScale.X - a.LocalScale.X) < 1e-5f && Math.Abs(e.LocalScale.Y - a.LocalScale.Y) < 1e-5f
                && Math.Abs(e.LocalEulerAngles.Z - a.LocalEulerAngles.Z) < 1e-3f,
                $"노드 {key}가 다르다");
            Assert.Equal(expected.GetParentKey(key), actual.GetParentKey(key));
        }
    }

    /// <summary>기본 씬에 손을 댄 사본으로 세운 트리 — 기대값.</summary>
    private static RectNodeTree Built(MotionEditor editor, Action<SceneAsset> change)
    {
        SceneAsset scene = ClipPreview.SceneOf(editor.Document, Clip(editor))!.Clone();
        change(scene);
        return BuildTree(scene);
    }

    [Fact]
    public void 상태_블록이_없으면_무대는_씬_그대로다()
    {
        MotionEditor editor = NewEditor();
        ClipStage stage = Stage(editor);
        Assert.Equal(0, stage.StepCount);
        SameTree(Built(editor, _ => { }), stage.TreeAt(3));
        Assert.Equal(1f, stage.OpacityOf("s1", 0));
        Assert.Equal(0f, stage.OpacityOf("nobody", 0));
    }

    [Fact]
    public void 오프셋은_길이_동안_이징으로_가고_끝나면_그_값으로_세운_씬과_같다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 1f, Duration = 1f, X = 200f, Rotation = 10f });
        ClipStage stage = Stage(editor);

        Near(0f, stage.TreeAt(0.5).GetState($"s1/{Track}").AnchoredPosition.X);   // 시작 전
        Near(0f, stage.TreeAt(1.0).GetState($"s1/{Track}").AnchoredPosition.X);   // 시작 순간
        Near(100f, stage.TreeAt(1.5).GetState($"s1/{Track}").AnchoredPosition.X); // linear 절반
        Near(5f, stage.TreeAt(1.5).GetState("s1/CharSlot_SwayPivot").LocalEulerAngles.Z);
        Near(0f, stage.TreeAt(1.5).GetState($"s2/{Track}").AnchoredPosition.X);   // 남은 그대로

        RectNodeTree expected = Built(editor, s => { s.Find("s1")!.PositionX = 200f; s.Find("s1")!.RotationZ = 10f; });
        SameTree(expected, stage.TreeAt(2.0));
        SameTree(expected, stage.TreeAt(9.0));

        // 이징: inQuad의 절반은 1/4.
        editor.UpdateClipBlock("clip", 0, 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 1f, Duration = 1f, X = 200f, Ease = EaseKind.InQuad });
        Near(50f, Stage(editor).TreeAt(1.5).GetState($"s1/{Track}").AnchoredPosition.X);

        // 길이 없는 블록은 그 순간에 바뀐다.
        editor.UpdateClipBlock("clip", 0, 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 1f, X = 200f });
        Near(0f, Stage(editor).TreeAt(0.99).GetState($"s1/{Track}").AnchoredPosition.X);
        Near(200f, Stage(editor).TreeAt(1.0).GetState($"s1/{Track}").AnchoredPosition.X);
    }

    [Fact]
    public void 보이기는_불투명도로_나온다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Visible, At = 0.5f, Duration = 0.5f, Visible = false });
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Visible, At = 2f, Visible = true });
        ClipStage stage = Stage(editor);

        Assert.Equal(1f, stage.OpacityOf("s1", 0.4));
        Near(0.5f, stage.OpacityOf("s1", 0.75));
        Assert.Equal(0f, stage.OpacityOf("s1", 1.0));
        Assert.False(stage.SceneAt(1.5).Find("s1")!.Visible);
        Assert.Equal(1f, stage.OpacityOf("s1", 2.0)); // 길이 없이 바로
        Assert.Equal(1f, stage.OpacityOf("s2", 0.75));

        // 원본 씬은 안 바뀐다.
        Assert.True(ClipPreview.SceneOf(editor.Document, Clip(editor))!.Find("s1")!.Visible);
    }

    [Fact]
    public void 층과_자리는_코어_폴드로_세운_씬에_닿는다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Layer, At = 0f, Duration = 1f, Layer = SceneLayers.Close });
        editor.AddClipStateBlock("clip", 1, new ClipBlock { Kind = ClipBlockKind.Place, At = 0.5f, Duration = 1f, Place = "left", PlaceFocus = "face" });
        ClipStage stage = Stage(editor);

        RectNodeTree expected = Built(editor, s =>
        {
            s.Find("s1")!.Layer = SceneLayers.Close;
            s.Find("s2")!.Place = "left";
            s.Find("s2")!.PlaceFocus = "face";
        });
        SameTree(expected, stage.TreeAt(1.5));

        // 층은 깊이 배율이 실제로 달라진다(close는 mid보다 크다) — 중간은 그 사이.
        float mid = Built(editor, _ => { }).GetState($"s1/{ScenePreview.DepthScaleNode}").LocalScale.X;
        float close = expected.GetState($"s1/{ScenePreview.DepthScaleNode}").LocalScale.X;
        Assert.True(close > mid + 0.01f, $"close {close} · mid {mid}");
        Near((mid + close) / 2f, stage.TreeAt(0.5).GetState($"s1/{ScenePreview.DepthScaleNode}").LocalScale.X);

        // 그리는 순서·부모는 블록이 시작하는 순간 새 층의 것이다.
        Assert.Equal(SceneLayers.Close, stage.SceneAt(0.1).Find("s1")!.Layer);
        Assert.Equal(expected.GetParentKey("s1/__root"), stage.TreeAt(0.1).GetParentKey("s1/__root"));

        // 자리는 Track_Focus가 움직인다.
        float focusX = expected.GetState("s2/CharSlot_Track_Focus").AnchoredPosition.X;
        Assert.True(focusX < -100f, $"left 자리의 Track_Focus x = {focusX}");
        Near(focusX / 2f, stage.TreeAt(1.0).GetState("s2/CharSlot_Track_Focus").AnchoredPosition.X, 0.5f);
    }

    [Fact]
    public void 카메라는_카메라_트랙에서_무대째_움직인다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 2, new ClipBlock { Kind = ClipBlockKind.Camera, At = 0f, Duration = 1f, Zoom = 4f, PanX = 120f });
        ClipStage stage = Stage(editor);

        RectNodeTree expected = Built(editor, s => { s.Camera.Zoom = 4f; s.Camera.PanX = 120f; });
        SameTree(expected, stage.TreeAt(1.0));

        string zoomKey = StageNodes.KeyOf(StageNodes.ZoomRoot);
        float zoomed = expected.GetState(zoomKey).LocalScale.X;
        Assert.True(zoomed > 1.01f);
        Near((1f + zoomed) / 2f, stage.TreeAt(0.5).GetState(zoomKey).LocalScale.X);
        Near(60f, stage.TreeAt(0.5).GetState(StageNodes.KeyOf(StageNodes.PanRoot)).AnchoredPosition.X);
    }

    [Fact]
    public void 서로_다른_값을_바꾸는_블록은_겹쳐도_따로_간다_그리고_끈_배역은_빠진다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipTrack("clip", "a"); // 트랙 3도 a
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 0f, Duration = 2f, X = 200f });
        editor.AddClipStateBlock("clip", 3, new ClipBlock { Kind = ClipBlockKind.Offset, At = 1f, Duration = 2f, Y = 80f });
        editor.AddClipStateBlock("clip", 1, new ClipBlock { Kind = ClipBlockKind.Offset, At = 0f, X = -50f });
        ClipStage stage = Stage(editor);

        Vec2 at15 = stage.TreeAt(1.5).GetState($"s1/{Track}").AnchoredPosition;
        Near(150f, at15.X); // 0 → 200의 3/4
        Near(20f, at15.Y);  // 0 → 80의 1/4
        Vec2 end = stage.TreeAt(3.0).GetState($"s1/{Track}").AnchoredPosition;
        Near(200f, end.X);
        Near(80f, end.Y);
        Near(-50f, stage.TreeAt(0).GetState($"s2/{Track}").AnchoredPosition.X);

        ClipStage withoutA = Stage(editor, track => track.Role == "a");
        Assert.Equal(1, withoutA.StepCount);
        Near(0f, withoutA.TreeAt(3.0).GetState($"s1/{Track}").AnchoredPosition.X);
        Near(-50f, withoutA.TreeAt(3.0).GetState($"s2/{Track}").AnchoredPosition.X);
    }

    [Fact]
    public void 모션은_상태_위에_얹힌다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 0f, Duration = 0.6f, X = 120f });
        editor.AddClipBlock("clip", 0, "nod", 0f);

        RectNodeTree pose = ClipPreview.Pose(Stage(editor).TreeAt(0.3), editor.Document, Clip(editor), 0.3);
        Near(60f, pose.GetState($"s1/{Track}").AnchoredPosition.X);
        Near(-18f, pose.GetState($"s1/{MoveY}").AnchoredPosition.Y);
        Assert.Equal(0.6f, ClipTiming.DurationOf(editor.Document, Clip(editor)), 4);
    }

    [Fact]
    public void 상태_블록은_저작본에_왕복하고_편집기가_줄과_종류를_지킨다()
    {
        MotionEditor editor = NewEditor();
        int index = editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Place, At = -1f, Duration = 0.4f, Ease = EaseKind.OutCubic, Place = "right", Motion = "nod" });
        editor.AddClipStateBlock("clip", 2, new ClipBlock { Kind = ClipBlockKind.Camera, At = 0.5f, Duration = 0f, Zoom = 2f });
        ClipBlock placed = Clip(editor).Tracks[0].Blocks[index];
        Assert.Equal(0f, placed.At);       // 0 아래로 못 간다
        Assert.Equal("", placed.Motion);   // 상태 블록에 모션은 없다
        Assert.Null(Clip(editor).Tracks[2].Blocks[0].Duration); // 길이 0은 빈 칸으로 적는다
        Assert.Equal(0.4f, ClipTiming.EndOf(editor.Document, placed), 4);

        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"kind\": \"place\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ease\": \"outCubic\"", json, StringComparison.Ordinal);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal(json, MotionDocumentJson.Write(read));
        ClipBlock back = read.FindClip("clip")!.Tracks[0].Blocks[0];
        Assert.Equal((ClipBlockKind.Place, "right", EaseKind.OutCubic, 0.4f), (back.Kind, back.Place, back.Ease, back.Duration));
        Assert.Equal(2f, read.FindClip("clip")!.Tracks[2].Blocks[0].Zoom);

        // kind가 없는 예전 블록은 모션이다.
        MotionDocument old = MotionDocumentJson.Read(
            """{ "schema": "motion-authoring/2", "motions": [], "clips": [ { "name": "c", "tracks": [ { "role": "a", "blocks": [ { "at": 0.5, "motion": "nod", "mode": "absolute" } ] } ] } ] }""");
        Assert.True(old.FindClip("c")!.Tracks[0].Blocks[0].IsMotion);

        // 줄에 안 맞는 종류는 놓을 자리가 없다.
        long revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Camera, Zoom = 1f }));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipStateBlock("clip", 2, new ClipBlock { Kind = ClipBlockKind.Visible, Visible = false }));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Motion, Motion = "nod" }));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Offset, X = 1f, Duration = -1f }));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipBlock("clip", 2, "nod", 0f));
        Assert.Throws<InvalidOperationException>(() => editor.MoveClipBlock("clip", 0, 0, 0f, toTrack: 2));
        Assert.Throws<InvalidOperationException>(() => editor.MoveClipBlock("clip", 2, 0, 0f, toTrack: 0));
        Assert.Throws<InvalidOperationException>(() => editor.SetClipBlockMode("clip", 0, 0, MotionChannelMode.Absolute));
        Assert.Throws<InvalidOperationException>(() => editor.SetClipBlockMotion("clip", 0, 0, "nod"));
        Assert.Throws<InvalidOperationException>(() => editor.UpdateClipBlock("clip", 0, 0, new ClipBlock { Kind = ClipBlockKind.Layer, Layer = "far" }));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipTrack("clip", ClipTrack.CameraRole)); // 카메라 트랙은 하나
        Assert.Equal(revision, editor.Revision);

        // 배역 트랙끼리는 옮겨진다. 값 바꾸기는 한 칸이고 시각이 바뀌면 자리도 따라간다.
        editor.AddClipBlock("clip", 0, "nod", 1f);
        Assert.Equal(1, editor.UpdateClipBlock("clip", 0, 0, new ClipBlock { Kind = ClipBlockKind.Place, At = 2f, Place = "center" }));
        Assert.Equal(new[] { "nod", "자리 center" }, Clip(editor).Tracks[0].Blocks.Select(b => b.Describe()));
        editor.Undo();
        Assert.Equal(new[] { "자리 right", "nod" }, Clip(editor).Tracks[0].Blocks.Select(b => b.Describe()));
        Assert.Equal((1, 0), editor.MoveClipBlock("clip", 0, 0, 0.25f, toTrack: 1));

        // 모션 블록도 같은 길로 고친다.
        Assert.Equal(0, editor.UpdateClipBlock("clip", 0, 0, new ClipBlock { At = 0.5f, Motion = "nod", Mode = MotionChannelMode.Absolute }));
        Assert.Equal((0.5f, MotionChannelMode.Absolute), (Clip(editor).Tracks[0].Blocks[0].At, Clip(editor).Tracks[0].Blocks[0].Mode));

        // 모션 이름을 바꿔도 상태 블록은 그대로다.
        editor.RenameMotion("nod", "nod2");
        Assert.Equal("", Clip(editor).Tracks[1].Blocks[0].Motion);
    }

    [Fact]
    public void 검증은_상태_블록의_값과_겹침을_본다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Layer, At = 0f, Duration = 1f, Layer = "front" });
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 0.5f, Duration = 1f, X = 10f }); // 다른 종류 — 겹쳐도 된다
        editor.AddClipStateBlock("clip", 1, new ClipBlock { Kind = ClipBlockKind.Layer, At = 0.5f, Duration = 1f, Layer = "far" }); // 다른 배역
        editor.AddClipStateBlock("clip", 2, new ClipBlock { Kind = ClipBlockKind.Camera, At = 0f, Zoom = 1f });
        Assert.Empty(ClipValidator.ValidateClip(editor.Document, Clip(editor), RigFile.NodeIds)); // 카메라 트랙은 배역 없이도 된다

        List<string> Messages() => ClipValidator.ValidateClip(editor.Document, Clip(editor), RigFile.NodeIds).Where(p => p.Blocking).Select(p => p.Message).ToList();

        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Layer, At = 0.9f, Duration = 0.5f, Layer = "mid" });
        Assert.Contains(Messages(), m => m.Contains("같은 값을 같은 때에 바꾼다", StringComparison.Ordinal));
        editor.Undo();

        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Layer, At = 2f, Layer = "nowhere" });
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Place, At = 2f, Place = "moon", PlaceFocus = "elbow" });
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Offset, At = 3f });
        editor.AddClipStateBlock("clip", 0, new ClipBlock { Kind = ClipBlockKind.Visible, At = 3f });
        editor.AddClipStateBlock("clip", 2, new ClipBlock { Kind = ClipBlockKind.Camera, At = 3f });
        List<string> messages = Messages();
        Assert.Contains(messages, m => m.Contains("층은", StringComparison.Ordinal) && m.Contains("nowhere", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("화면 지점 'moon'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("focus 'elbow'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("오프셋·회전·배율이 전부 비었다", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("보이기/숨기기가 정해지지 않았다", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("zoom·pan이 전부 비었다", StringComparison.Ordinal));
        Assert.Equal(6, messages.Count);

        // 길이를 적은 클립에서는 상태 블록도 넘치면 막는다.
        editor.SetClipDuration("clip", 1.2f);
        Assert.Contains(Messages(), m => m.Contains("넘는다", StringComparison.Ordinal));

        // 손으로 쓴 파일: 줄에 안 맞는 종류.
        Clip(editor).Tracks[2].Blocks.Add(new ClipBlock { Kind = ClipBlockKind.Visible, At = 0.2f, Visible = true });
        Clip(editor).Tracks[2].Blocks.Add(new ClipBlock { At = 0.3f, Motion = "nod" });
        messages = Messages();
        Assert.Equal(2, messages.Count(m => m.Contains("카메라 트랙에는 카메라 블록", StringComparison.Ordinal)));
        Assert.Equal(1, ClipStage.Build(editor.Document, Clip(editor), BuildTree, t => !t.IsCamera)!.StepCount - 1); // 안 맞는 블록은 무대에 안 든다(카메라 블록 둘만)
    }
}
