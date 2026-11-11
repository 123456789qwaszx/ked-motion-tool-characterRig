using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>카메라 모션 — 카메라(zoom·pan)를 키와 커브로. 가짜 리그 <c>camera</c> 위의 보통 모션이다.</summary>
public class CameraMotionTests
{
    private static readonly RigSchemaFile RigFile = BuiltinTuning.RigSchemas;

    private static RectNodeTree BuildTree(SceneAsset scene)
        => ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.Depth, BuiltinTuning.Focus, BuiltinTuning.StageRig);

    /// <summary>
    /// push(1초 commit): zoom 0 → 4, pan x 0 → 100. shake(0.5초 restore): pan y 0 → 20 → 0. nod(캐릭터, 0.6초).
    /// clip: 배역 a(s1) 트랙 0, 카메라 트랙 1.
    /// </summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());

        editor.AddMotion("push", CameraRig.RigKind, 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("push", CameraRig.ZoomNode, ChannelKind.Position);
        editor.SetKeys("push", CameraRig.ZoomNode, ChannelKind.Position, Axis.X, new[] { Linear(0f, 0f), Linear(1f, 4f) });
        editor.AddChannel("push", CameraRig.PanNode, ChannelKind.Position);
        editor.SetKeys("push", CameraRig.PanNode, ChannelKind.Position, Axis.X, new[] { Linear(0f, 0f), Linear(1f, 100f) });

        editor.AddMotion("shake", CameraRig.RigKind, 0.5f);
        editor.AddChannel("shake", CameraRig.PanNode, ChannelKind.Position);
        editor.SetKeys("shake", CameraRig.PanNode, ChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.25f, 20f), Linear(0.5f, 0f) });

        editor.AddMotion("nod", "character", 0.6f);
        editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", ChannelKind.Position);
        editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", ChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.3f, -18f), Fixtures.Key(0.6f, 0f) });

        editor.AddClip("clip");
        editor.AddClipRole("clip", "a", "character", "s1");
        editor.AddClipTrack("clip", "a");
        editor.AddClipTrack("clip", ClipTrack.CameraRole);
        return editor;
    }

    private static KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    private static ClipAsset Clip(MotionEditor editor) => editor.Document.FindClip("clip")!;

    private static void Near(float expected, float actual, float tolerance = 1e-3f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Fact]
    public void 카메라_모션은_보통_모션의_규칙으로_카메라_값을_낸다()
    {
        MotionEditor editor = NewEditor();
        MotionAsset push = editor.Document.Find("push")!;
        var start = new CameraSettings { Zoom = 2f, PanX = 10f, PanY = -5f };

        // absolute: 키 그대로. 안 건드린 성분(pan y)은 시작값.
        CameraSettings half = CameraRig.Pose(start, push, 0.5, MotionChannelMode.Absolute);
        Near(2f, half.Zoom);
        Near(50f, half.PanX);
        Near(-5f, half.PanY);

        // relative: 지금 값 + 키.
        CameraSettings relative = CameraRig.Pose(start, push, 1.0, MotionChannelMode.Relative);
        Near(6f, relative.Zoom);
        Near(110f, relative.PanX);

        // 끝 규칙: commit은 남고 restore는 돌아간다.
        Near(4f, CameraRig.Pose(start, push, 9.0, MotionChannelMode.Absolute).Zoom);
        MotionAsset shake = editor.Document.Find("shake")!;
        Near(15f, CameraRig.Pose(start, shake, 0.25, MotionChannelMode.Relative).PanY);
        Near(-5f, CameraRig.Pose(start, shake, 9.0, MotionChannelMode.Relative).PanY);

        // 코어 규칙으로 검증된다 — 가짜 리그의 노드 표로.
        Assert.Empty(MotionValidator.Validate(editor.Document, CameraRig.WithCamera(RigFile.NodeIds)));
        Assert.Contains(MotionValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.MotionName == "push"); // 표에 없으면 모르는 리그다
        Assert.Null(CameraRig.WithCamera(null));
    }

    [Fact]
    public void 카메라_모션은_motions_json에_나가지_않고_저작본에는_왕복한다()
    {
        MotionEditor editor = NewEditor();
        Assert.True(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out string json, out IReadOnlyList<MotionProblem> problems));
        Assert.Empty(problems);
        Assert.Contains("\"nod\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("push", json, StringComparison.Ordinal);
        Assert.DoesNotContain("camera", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "nod" }, MotionExport.ToClips(editor.Document).Select(c => c.Name));

        string authoring = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(authoring, MotionDocumentJson.Write(MotionDocumentJson.Read(authoring)));

        // 카메라 모션이 규칙을 어겨도 motions.json은 나간다(거기 실리지 않는 모션이다) — 문제 칸은 계속 알린다.
        editor.SetDuration("push", 0.5f);
        Assert.True(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out string again, out _));
        Assert.Equal(json, again);
        Assert.Contains(MotionValidator.Validate(editor.Document, CameraRig.WithCamera(RigFile.NodeIds)), p => p.Blocking && p.MotionName == "push");
    }

    [Fact]
    public void 클립의_카메라_트랙에서_카메라_모션이_무대를_움직인다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("clip", 1, "push", 0.5f); // absolute(모션의 렌즈)
        editor.AddClipBlock("clip", 0, "nod", 0f);
        ClipAsset clip = Clip(editor);
        ClipStage stage = ClipStage.Build(editor.Document, clip, BuildTree)!;

        RectNodeTree PoseAt(double t) => ClipPreview.Pose(stage.TreeAt(t), editor.Document, clip, t, null, stage.CameraAt(t));

        string panKey = StageNodes.KeyOf(StageNodes.PanRoot);
        string zoomKey = StageNodes.KeyOf(StageNodes.ZoomRoot);

        Near(0f, PoseAt(0.3).GetState(panKey).AnchoredPosition.X);  // 아직 시작 전
        Near(-18f, PoseAt(0.3).GetState("s1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y); // 캐릭터는 제 블록대로
        Near(50f, PoseAt(1.0).GetState(panKey).AnchoredPosition.X);
        Near(ShotIntentMath.EvaluateCameraScale(2f), PoseAt(1.0).GetState(zoomKey).LocalScale.X);

        // 끝난 뒤의 무대 = 카메라를 그 값으로 둔 씬(층 패럴랙스까지).
        SceneAsset expectedScene = ClipPreview.SceneOf(editor.Document, clip)!.Clone();
        expectedScene.Camera.Zoom = 4f;
        expectedScene.Camera.PanX = 100f;
        RectNodeTree expected = BuildTree(expectedScene);
        RectNodeTree after = PoseAt(5.0);

        foreach (string key in expected.Keys)
        {
            RectNodeState e = expected.GetState(key);
            RectNodeState a = after.GetState(key);
            Assert.True(
                Math.Abs(e.AnchoredPosition.X - a.AnchoredPosition.X) < 1e-3f && Math.Abs(e.AnchoredPosition.Y - a.AnchoredPosition.Y) < 1e-3f && Math.Abs(e.LocalScale.X - a.LocalScale.X) < 1e-5f,
                $"노드 {key}가 다르다");
        }

        Assert.Equal(1.5f, ClipTiming.DurationOf(editor.Document, clip), 4);
        Assert.Empty(ClipValidator.ValidateClip(editor.Document, clip, RigFile.NodeIds));
    }

    [Fact]
    public void 카메라_모션은_카메라_블록이_둔_값에서_시작하고_앞_블록이_남긴_값을_잇는다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipStateBlock("clip", 1, new ClipBlock { Kind = ClipBlockKind.Camera, At = 0f, Duration = 1f, PanY = 40f });
        editor.AddClipBlock("clip", 1, "shake", 0.25f, MotionChannelMode.Relative); // 0.25 ~ 0.75: 가던 값 위에 흔들림
        editor.AddClipBlock("clip", 1, "push", 1f, MotionChannelMode.Relative);     // 1 ~ 2: zoom +4, pan x +100
        editor.AddClipBlock("clip", 1, "push", 2f, MotionChannelMode.Relative);     // 2 ~ 3: 이어서 한 번 더
        ClipAsset clip = Clip(editor);
        ClipStage stage = ClipStage.Build(editor.Document, clip, BuildTree)!;

        CameraSettings At(double t) => ClipPreview.CameraAt(editor.Document, clip, stage.CameraAt(t), t)!;

        Near(20f, stage.CameraAt(0.5).PanY);   // 카메라 블록의 절반
        Near(40f, At(0.5).PanY);               // + shake의 꼭대기 20
        Near(30f, At(0.75).PanY);              // shake가 끝난 자리(restore) — 블록이 간 값만
        Near(8f, At(9.0).Zoom);                // push 두 번
        Near(200f, At(9.0).PanX);
        Near(40f, At(9.0).PanY);
        Assert.Null(ClipPreview.CameraAt(editor.Document, clip, stage.CameraAt(0.1), 0.1)); // 시작한 카메라 모션이 없다

        // 카메라 트랙을 끄면 모션도 블록도 빠진다.
        Assert.Null(ClipPreview.CameraAt(editor.Document, clip, null, 9.0, track => track.IsCamera));
    }

    [Fact]
    public void 카메라_모션은_카메라_트랙에만_캐릭터_모션은_배역_트랙에만_놓인다()
    {
        MotionEditor editor = NewEditor();
        long revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.AddClipBlock("clip", 0, "push", 0f));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipBlock("clip", 1, "nod", 0f));
        Assert.Equal(revision, editor.Revision);

        editor.AddClipBlock("clip", 1, "push", 0f);
        editor.AddClipBlock("clip", 0, "nod", 0f);
        Assert.Throws<InvalidOperationException>(() => editor.MoveClipBlock("clip", 1, 0, 0f, toTrack: 0));
        Assert.Throws<InvalidOperationException>(() => editor.MoveClipBlock("clip", 0, 0, 0f, toTrack: 1));
        Assert.Throws<InvalidOperationException>(() => editor.SetClipBlockMotion("clip", 1, 0, "nod"));
        editor.SetClipBlockMotion("clip", 1, 0, "shake");
        Assert.Equal("shake", Clip(editor).Tracks[1].Blocks[0].Motion);

        // 같은 카메라 노드를 같은 때에 쓰는 두 모션은 겹칠 수 없다(다른 노드면 된다 — shake는 pan y, push는 zoom·pan x... 가 아니라 pan 노드를 같이 쓴다).
        editor.AddClipBlock("clip", 1, "push", 0.25f);
        ClipProblem overlap = Assert.Single(ClipValidator.ValidateClip(editor.Document, Clip(editor), RigFile.NodeIds), p => p.Blocking);
        Assert.Contains("카메라의 Camera_Pan", overlap.Message, StringComparison.Ordinal);
        Assert.Equal(2, ClipValidator.OverlappingBlocks(editor.Document, Clip(editor)).Count);

        // 손으로 쓴 파일: 카메라 트랙의 캐릭터 모션.
        Clip(editor).Tracks[1].Blocks.Clear();
        Clip(editor).Tracks[1].Blocks.Add(new ClipBlock { At = 0f, Motion = "nod" });
        Assert.Contains(ClipValidator.ValidateClip(editor.Document, Clip(editor), RigFile.NodeIds), p => p.Blocking && p.Message.Contains("카메라 모션만", StringComparison.Ordinal));
    }
}
