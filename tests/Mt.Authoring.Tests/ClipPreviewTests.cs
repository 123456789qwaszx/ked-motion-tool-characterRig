using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>연출 클립 K2(<c>PLAN-clip.md</c>) — 시각 t의 포즈, 리듀서와의 끝 상태 등가, 검증.</summary>
public class ClipPreviewTests
{
    private const string Sway = "CharacterPortrait_SwayPivot";
    private const string MoveY = "CharacterPortrait_Track_Move_Y";
    private const string TrackX = "CharSlot_Track_X";

    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    /// <summary>
    /// s1·s2 두 캐릭터, 모션 넷:
    /// nod(0.6초 restore, MoveY y 0 → -18 → 0) · bow(0.4초 commit, Sway z 0 → 8) ·
    /// lean(0.5초 commit, Sway z 0 → 5) · step(0.4초 commit, TrackX x 10 → 80).
    /// </summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character");

        editor.AddMotion("nod", "character", 0.6f);
        editor.AddChannel("nod", MoveY, MotionChannelKind.Position);
        editor.SetKeys("nod", MoveY, MotionChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.3f, -18f), Fixtures.Key(0.6f, 0f) });

        editor.AddMotion("bow", "character", 0.4f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("bow", Sway, MotionChannelKind.Euler);
        editor.SetKeys("bow", Sway, MotionChannelKind.Euler, Axis.Z, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.4f, 8f) });

        editor.AddMotion("lean", "character", 0.5f, MotionEnd.Commit);
        editor.AddChannel("lean", Sway, MotionChannelKind.Euler);
        editor.SetKeys("lean", Sway, MotionChannelKind.Euler, Axis.Z, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.5f, 5f) });

        editor.AddMotion("step", "character", 0.4f, MotionEnd.Commit);
        editor.AddChannel("step", TrackX, MotionChannelKind.Position);
        editor.SetKeys("step", TrackX, MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 10f), Fixtures.Key(0.4f, 80f) });

        editor.AddClip("greet");
        editor.AddClipRole("greet", "a", "character", "s1");
        editor.AddClipRole("greet", "b", "character", "s2");
        editor.AddClipTrack("greet", "a");
        editor.AddClipTrack("greet", "b");
        return editor;
    }

    private static ClipAsset Greet(MotionEditor editor) => editor.Document.FindClip("greet")!;

    private static RectNodeTree Template(MotionEditor editor)
        => ScenePreview.Build(RigFile, ClipPreview.SceneOf(editor.Document, Greet(editor))!);

    private static RectNodeTree Pose(MotionEditor editor, double t)
        => ClipPreview.Pose(Template(editor), editor.Document, Greet(editor), t);

    private static float SwayZ(RectNodeTree tree, string slot) => tree.GetState($"{slot}/{Sway}").LocalEulerAngles.Z;

    private static float MoveYOf(RectNodeTree tree, string slot) => tree.GetState($"{slot}/{MoveY}").AnchoredPosition.Y;

    private static float TrackXOf(RectNodeTree tree, string slot) => tree.GetState($"{slot}/{TrackX}").AnchoredPosition.X;

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-3f, expected + 1e-3f);

    [Fact]
    public void 두_출연자가_시간차로_움직인다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);   // a(s1): 0 → 0.4
        editor.AddClipBlock("greet", 1, "nod", 0.5f); // b(s2): 0.5 → 1.1

        // 0.2초: s1은 bow 한가운데 — 모션 하나를 그 오브젝트에 얹은 것과 같은 값. s2는 아직이다.
        RectNodeTree at02 = Pose(editor, 0.2);
        RectNodeTree alone = MotionPreview.Pose(Template(editor), editor.Document.Find("bow")!, 0.2, null, "s1/");
        Near(SwayZ(alone, "s1"), SwayZ(at02, "s1"));
        Assert.InRange(SwayZ(at02, "s1"), 0.5f, 7.5f);
        Near(0f, MoveYOf(at02, "s2"));
        Near(0f, SwayZ(at02, "s2")); // 남의 블록은 안 묻는다

        // 0.8초: s1의 bow는 끝나 8이 남았고(commit), s2는 nod의 0.3초 — 가장 깊은 자리.
        RectNodeTree at08 = Pose(editor, 0.8);
        Near(8f, SwayZ(at08, "s1"));
        Near(-18f, MoveYOf(at08, "s2"));

        // 끝난 뒤: commit은 남고 restore는 시작값이다.
        RectNodeTree after = Pose(editor, 5.0);
        Near(8f, SwayZ(after, "s1"));
        Near(0f, MoveYOf(after, "s2"));
    }

    [Fact]
    public void 앞_블록이_남긴_값이_뒤_블록의_시작값이다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);                                // absolute → 8
        editor.AddClipBlock("greet", 0, "lean", 1f, MotionChannelMode.Relative);   // 8 위에 +5

        Near(8f, SwayZ(Pose(editor, 1.0), "s1"));     // lean의 첫 키(0)를 8에 더한 자리
        float mid = SwayZ(Pose(editor, 1.25), "s1");
        Assert.InRange(mid, 8.5f, 12.5f);
        Near(13f, SwayZ(Pose(editor, 1.5), "s1"));
        Near(13f, SwayZ(Pose(editor, 9.0), "s1"));

        // 같은 lean을 absolute로 쓰면 8을 버리고 5로 간다 — mode는 블록이 정한다.
        editor.SetClipBlockMode("greet", 0, 1, MotionChannelMode.Absolute);
        Near(5f, SwayZ(Pose(editor, 9.0), "s1"));
    }

    [Fact]
    public void mode는_모션의_렌즈가_아니라_블록의_것이다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "step", 0f);   // step의 렌즈는 relative
        editor.AddClipBlock("greet", 0, "step", 0.5f);

        Near(160f, TrackXOf(Pose(editor, 2.0), "s1")); // 0 + 80 + 80

        editor.SetPreviewMode("step", MotionChannelMode.Absolute); // 렌즈를 바꿔도 이미 놓은 블록은 그대로다
        Near(160f, TrackXOf(Pose(editor, 2.0), "s1"));

        editor.SetClipBlockMode("greet", 0, 1, MotionChannelMode.Absolute);
        Near(80f, TrackXOf(Pose(editor, 2.0), "s1"));
    }

    [Fact]
    public void 순서는_시작_시각_그다음_트랙_그다음_트랙_안_순이다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipTrack("greet", "a"); // 트랙 2도 a
        editor.AddClipBlock("greet", 2, "step", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0f);
        editor.AddClipBlock("greet", 0, "bow", 0.5f);
        editor.AddClipBlock("greet", 0, "lean", 0f);

        Assert.Equal(
            new[] { (0, 0, "lean"), (1, 0, "nod"), (2, 0, "step"), (0, 1, "bow") },
            ClipPreview.Ordered(Greet(editor)).Select(p => (p.Track, p.Index, p.Block.Motion)));
    }

    [Fact]
    public void 그릴_수_없는_블록은_건너뛰고_받은_트리는_안_건드린다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0f);
        RectNodeTree template = Template(editor);

        editor.BindClipRole("greet", "b", "nobody"); // 씬에 없는 오브젝트
        editor.RemoveMotion("bow");                  // 블록은 남는다
        RectNodeTree pose = ClipPreview.Pose(template, editor.Document, Greet(editor), 0.3);
        Near(0f, SwayZ(pose, "s1"));
        Near(0f, MoveYOf(pose, "s2"));

        // 아무것도 안 얹어도 새 트리다.
        Assert.NotSame(template, pose);
        Assert.NotSame(template, ClipPreview.Pose(template, editor.Document, Greet(editor), -1.0));

        editor.BindClipRole("greet", "b", null);
        Near(0f, MoveYOf(ClipPreview.Pose(template, editor.Document, Greet(editor), 0.3), "s2"));
    }

    [Fact]
    public void 끝난_뒤의_포즈는_블록을_커맨드_열로_풀어_리듀서가_접은_상태와_같다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipTrack("greet", "a");
        editor.AddClipBlock("greet", 0, "bow", 0f);                               // s1 Sway: 8
        editor.AddClipBlock("greet", 0, "lean", 1f, MotionChannelMode.Relative);  // s1 Sway: 13
        editor.AddClipBlock("greet", 2, "step", 0.25f);                           // s1 TrackX: +80 (bow와 겹치지만 다른 노드)
        editor.AddClipBlock("greet", 1, "nod", 0.5f);                             // s2 restore — 무변
        editor.AddClipBlock("greet", 1, "step", 1.5f, MotionChannelMode.Absolute);// s2 TrackX: 80
        editor.AddClipBlock("greet", 1, "lean", 1.5f);                            // s2 Sway: +5 (같은 시각, 다른 노드)

        ClipAsset clip = Greet(editor);
        Assert.Empty(ClipValidator.Blocking(ClipValidator.ValidateClip(editor.Document, clip, RigFile.NodeIds)));

        // 툴: 클립이 끝난 뒤의 포즈.
        RectNodeTree pose = Pose(editor, ClipTiming.DurationOf(editor.Document, clip) + 1.0);

        // 런타임의 정지 프레임: slot 둘을 세우고, 블록을 같은 순서의 motion(slot, name, mode)로.
        var tuning = new StageReducerTuning
        {
            RigSchemas = RigFile.Dto,
            ReferenceStageWidth = 1920f,
            BaseResolution = new Vec2(1920f, 1080f),
            RoleAnchors = new RoleAnchorTuningBodyDto(),
            Motions = MotionExport.ToClips(editor.Document).ToDictionary(c => c.Name, c => c),
        };

        var commands = new List<StageCommand> { Cmd("slot", "s1"), Cmd("slot", "s2") };

        foreach (PlacedBlock placed in ClipPreview.Ordered(clip))
        {
            string slot = clip.FindRole(clip.Tracks[placed.Track].Role)!.Object!;
            commands.Add(Cmd("motion", slot, placed.Block.Motion, MotionModeToken.Of(placed.Block.Mode)));
        }

        StageState folded = StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);
        Assert.Empty(folded.Unhandled);

        foreach (string slot in new[] { "s1", "s2" })
        {
            Near(SwayZ(folded.Nodes, slot), SwayZ(pose, slot));
            Near(TrackXOf(folded.Nodes, slot), TrackXOf(pose, slot));
            Near(MoveYOf(folded.Nodes, slot), MoveYOf(pose, slot));
        }

        // 표로도 못 박는다 — 둘이 같이 틀리는 일을 막는다.
        Near(13f, SwayZ(pose, "s1"));
        Near(80f, TrackXOf(pose, "s1"));
        Near(5f, SwayZ(pose, "s2"));
        Near(80f, TrackXOf(pose, "s2"));
        Near(0f, MoveYOf(pose, "s2"));
    }

    private static StageCommand Cmd(string name, params string[] args) => new(name, args, "clip-preview:1");

    // ── 검증 ──────────────────────────────────────────────────────────

    private static List<ClipProblem> Problems(MotionEditor editor)
        => ClipValidator.ValidateClip(editor.Document, Greet(editor), RigFile.NodeIds).ToList();

    [Fact]
    public void 빈_클립은_알림뿐이고_맞는_클립은_조용하다()
    {
        MotionEditor editor = NewEditor();
        ClipProblem only = Assert.Single(Problems(editor));
        Assert.False(only.Blocking);
        Assert.Contains("블록이 없다", only.Message, StringComparison.Ordinal);

        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0.5f);
        Assert.Empty(Problems(editor));
        Assert.Empty(ClipValidator.Validate(editor.Document, RigFile.NodeIds));
    }

    [Fact]
    public void 같은_배역의_같은_노드가_겹치면_막고_맞닿거나_다른_노드면_된다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipTrack("greet", "a");
        editor.AddClipBlock("greet", 0, "bow", 0f);    // Sway 0 → 0.4
        editor.AddClipBlock("greet", 2, "lean", 0.4f); // Sway 0.4 → 0.9 — 맞닿는다
        editor.AddClipBlock("greet", 2, "step", 0.1f); // 다른 노드
        editor.AddClipBlock("greet", 1, "bow", 0.1f);  // 다른 배역
        Assert.Empty(Problems(editor));

        editor.MoveClipBlock("greet", 2, 1, 0.3f);     // lean을 bow 위로
        ClipProblem overlap = Assert.Single(Problems(editor));
        Assert.True(overlap.Blocking);
        Assert.Contains("겹칠 수 없다", overlap.Message, StringComparison.Ordinal);
        Assert.Contains(Sway, overlap.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 없는_모션과_없는_배역과_길이_밖을_막는다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0.5f); // 끝 1.1
        editor.SetClipDuration("greet", 1f);

        ClipProblem over = Assert.Single(Problems(editor));
        Assert.True(over.Blocking);
        Assert.Contains("넘는다", over.Message, StringComparison.Ordinal);

        // 돕는 손: 길이를 마지막 블록 끝으로 — 되돌리기 한 칸.
        ClipFixes.DurationToLastBlock(editor, "greet");
        Assert.Equal(1.1f, Greet(editor).Duration!.Value, 4);
        Assert.Empty(Problems(editor));
        editor.Undo();
        Assert.Equal(1f, Greet(editor).Duration);
        editor.SetClipDuration("greet", null);
        long revision = editor.Revision;
        ClipFixes.DurationToLastBlock(editor, "greet"); // 안 적은 길이는 이미 블록을 따라간다
        Assert.Equal(revision, editor.Revision);

        editor.RemoveMotion("nod");
        Assert.Contains(Problems(editor), p => p.Blocking && p.Message.Contains("모션 'nod'이 없다", StringComparison.Ordinal));

        // 손으로 쓴 파일처럼 배역 없는 트랙.
        Greet(editor).Tracks[0].Role = "ghost";
        Assert.Contains(Problems(editor), p => p.Blocking && p.Message.Contains("배역 'ghost'이 없다", StringComparison.Ordinal));
    }

    [Fact]
    public void 리그가_안_맞는_모션을_막고_노드가_전부_있으면_나눠_쓴다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);

        editor.SetClipRoleRig("greet", "a", "screenEffect");
        List<ClipProblem> problems = Problems(editor);
        Assert.Contains(problems, p => p.Blocking && p.Message.Contains("안 맞는다", StringComparison.Ordinal));
        Assert.Contains(problems, p => !p.Blocking && p.Message.Contains("오브젝트 's1'는 'character'", StringComparison.Ordinal));

        editor.SetClipRoleRig("greet", "a", "nope");
        Assert.Contains(Problems(editor), p => p.Blocking && p.Message.Contains("rigKind 'nope'가 없다", StringComparison.Ordinal));

        // rigKind 이름이 달라도 채널의 노드가 전부 그 리그에 있으면 입는다(CanWear와 같은 판정).
        var shared = new Dictionary<string, ICollection<string>> { ["character"] = RigFile.NodeIds["character"], ["twin"] = RigFile.NodeIds["character"] };
        Assert.True(ClipValidator.Wears("twin", editor.Document.Find("bow")!, shared));
        Assert.False(ClipValidator.Wears("twin", editor.Document.Find("bow")!, null));
        Assert.False(ClipValidator.Wears("screenEffect", editor.Document.Find("bow")!, RigFile.NodeIds));
    }

    [Fact]
    public void 참조한_모션의_규칙_위반은_클립도_막고_안_묶인_배역은_알린다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 0, "bow", 1f);
        editor.SetDuration("bow", 0.3f); // 마지막 키(0.4)가 길이 밖 — 코어 규칙 위반

        List<ClipProblem> problems = Problems(editor);
        ClipProblem broken = Assert.Single(problems, p => p.Blocking); // 두 번 써도 한 번만 알린다
        Assert.StartsWith("모션 'bow':", broken.Message, StringComparison.Ordinal);

        editor.SetDuration("bow", 0.4f);
        editor.BindClipRole("greet", "a", null);
        editor.BindClipRole("greet", "b", "nobody");
        problems = Problems(editor);
        Assert.DoesNotContain(problems, p => p.Blocking);
        Assert.Contains(problems, p => p.Message.Contains("배역 'a'을 맡은 오브젝트가 없다", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Message.Contains("오브젝트 'nobody'가 없다", StringComparison.Ordinal));
    }

    [Fact]
    public void 손으로_쓴_문서의_이름_어긋남을_막는다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);
        MotionDocument document = editor.Document;
        document.Clips.Add(new ClipAsset { Name = "greet" });
        document.Clips.Add(new ClipAsset { Name = "Bad Name", Roles = { new ClipRole { Name = "x", Rig = "character" }, new ClipRole { Name = "x", Rig = "" } } });

        IReadOnlyList<ClipProblem> problems = ClipValidator.Validate(document, RigFile.NodeIds);
        Assert.Contains(problems, p => p.Blocking && p.ClipName == "greet" && p.Message.Contains("같은 이름이 둘", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Blocking && p.ClipName == "Bad Name" && p.Message.Contains("소문자", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Blocking && p.Message.Contains("배역 'x'이 둘", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Blocking && p.Message.Contains("리그(rigKind)가 없다", StringComparison.Ordinal));
        Assert.Equal("클립 'greet': 같은 이름이 둘이다", problems.First(p => p.Message.Contains("같은 이름", StringComparison.Ordinal)).ToString());
    }

    // ── 배역 끄기 · 굽기 ──────────────────────────────────────────────

    [Fact]
    public void 끈_트랙의_블록은_없는_것처럼_건너뛴다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0.5f);
        ClipAsset clip = Greet(editor);

        RectNodeTree pose = ClipPreview.Pose(Template(editor), editor.Document, clip, 0.8, track => track.Role == "a");
        Near(0f, SwayZ(pose, "s1"));     // a는 꺼졌다
        Near(-18f, MoveYOf(pose, "s2")); // b는 그대로
    }

    /// <summary>한 캐릭터(s1)에 배역 둘(a·c)을 묶어 겹쳐 놓은 클립 — bow(Sway 8) 위에 lean(+5)이 겹치고 nod가 같이 돈다.</summary>
    private static MotionEditor Layered()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipRole("greet", "c", "character", "s1");
        editor.AddClipTrack("greet", "c");                                        // 트랙 2 = c(s1)
        editor.AddClipBlock("greet", 0, "bow", 0f);                               // a: Sway 0 → 8 (0 ~ 0.4, commit)
        editor.AddClipBlock("greet", 2, "lean", 0.2f, MotionChannelMode.Relative);// c: Sway +5 (0.2 ~ 0.7, commit) — 같은 노드에 겹친다
        editor.AddClipBlock("greet", 2, "nod", 0.7f);                             // c: MoveY (0.7 ~ 1.3, restore)
        editor.AddClipBlock("greet", 1, "step", 0f);                              // b(s2): 다른 캐릭터
        return editor;
    }

    [Fact]
    public void 구운_모션은_한_캐릭터에_겹친_블록의_합성과_프레임마다_같다()
    {
        MotionEditor editor = Layered();
        RectNodeTree template = Template(editor);
        long revision = editor.Revision;

        MotionAsset baked = ClipBake.Bake(editor, template, "greet", "s1", "greet_s1");

        Assert.Equal("character", baked.Rig);
        Assert.Equal(1.3f, baked.Duration, 4);
        Assert.Equal(MotionEnd.Commit, baked.End); // Sway가 남는다
        Assert.Equal(MotionChannelMode.Relative, baked.PreviewMode);
        Assert.Equal(new[] { Sway, MoveY }, baked.Channels.Select(c => c.Node)); // s2의 TrackX는 없다
        Assert.Null(baked.Channels[0].X);                                        // 안 움직인 성분은 싣지 않는다
        Assert.NotNull(baked.Channels[0].Z);
        Assert.DoesNotContain(MotionValidator.ValidateMotion(baked, RigFile.NodeIds), p => p.Blocking);

        // 키는 덜어 냈다 — 프레임(32개)보다 훨씬 적다.
        Assert.InRange(baked.Channels[0].Z!.Keys.Count, 3, 28);

        // 프레임마다: 구운 모션 하나를 s1에 얹은 포즈 = 클립의 포즈.
        ClipAsset clip = Greet(editor);

        for (int k = 0; k <= 31; k++)
        {
            double t = k / 24.0;
            RectNodeTree expected = ClipPreview.Pose(template, editor.Document, clip, t);
            RectNodeTree actual = MotionPreview.Pose(template, baked, t, null, "s1/");
            Assert.InRange(SwayZ(actual, "s1"), SwayZ(expected, "s1") - 0.01f, SwayZ(expected, "s1") + 0.01f);
            Assert.InRange(MoveYOf(actual, "s1"), MoveYOf(expected, "s1") - 0.01f, MoveYOf(expected, "s1") + 0.01f);
        }

        // 끝난 뒤에도 같다(13이 남고 nod는 돌아왔다).
        RectNodeTree after = MotionPreview.Pose(template, baked, 9.0, null, "s1/");
        Near(13f, SwayZ(after, "s1"));
        Near(0f, MoveYOf(after, "s1"));

        // 한 칸이다.
        Assert.Equal(revision + 1, editor.Revision);
        editor.Undo();
        Assert.Null(editor.Document.Find("greet_s1"));
    }

    [Fact]
    public void 끈_배역은_굽지_않고_남는_것이_없으면_restore다()
    {
        MotionEditor editor = Layered();
        RectNodeTree template = Template(editor);

        // a(bow)를 끄면 Sway에는 lean(+5)만 남는다.
        MotionAsset withoutA = ClipBake.Bake(editor, template, "greet", "s1", "no_a", track => track.Role == "a");
        Near(5f, SwayZ(MotionPreview.Pose(template, withoutA, 9.0, null, "s1/"), "s1"));

        // commit 블록을 다 끄면 nod만 — 남는 것이 없다.
        editor.RemoveClipBlock("greet", 2, 0); // lean
        MotionAsset nodOnly = ClipBake.Bake(editor, template, "greet", "s1", "nod_only", track => track.Role == "a");
        Assert.Equal(MotionEnd.Restore, nodOnly.End);
        Assert.Equal(new[] { MoveY }, nodOnly.Channels.Select(c => c.Node));
        Assert.Empty(MotionValidator.ValidateMotion(nodOnly, RigFile.NodeIds)); // 경고도 없다(마지막 키가 중립값)
    }

    [Fact]
    public void 구울_것이_없으면_던지고_흔적이_없다()
    {
        MotionEditor editor = Layered();
        RectNodeTree template = Template(editor);
        long revision = editor.Revision;

        Assert.Throws<KeyNotFoundException>(() => ClipBake.Bake(editor, template, "greet", "nobody", "x"));
        Assert.Throws<KeyNotFoundException>(() => ClipBake.Bake(editor, template, "nope", "s1", "x"));
        Assert.Throws<InvalidOperationException>(() => ClipBake.Bake(editor, template, "greet", "s1", "x", _ => true));
        Assert.Throws<InvalidOperationException>(() => ClipBake.Bake(editor, template, "greet", "s1", "bow")); // 이름이 이미 있다
        Assert.Throws<InvalidOperationException>(() => ClipBake.Bake(editor, template, "greet", "s1", "Bad Name"));
        Assert.Equal(revision, editor.Revision);
        Assert.Equal(4, editor.Document.Motions.Count);
    }

    [Fact]
    public void 직선_위의_표본은_덜어_낸다()
    {
        float[] times = { 0f, 1f, 2f, 3f, 4f };
        Assert.Equal(new[] { 0, 4 }, ClipBake.Reduce(times, new[] { 0f, 1f, 2f, 3f, 4f }));
        Assert.Equal(new[] { 0, 2, 4 }, ClipBake.Reduce(times, new[] { 0f, 1f, 2f, 1f, 0f }));
        Assert.Equal(new[] { 0, 1, 2, 4 }, ClipBake.Reduce(times, new[] { 0f, 0f, 5f, 5f, 5f }));
    }

    [Fact]
    public void 끈_블록은_자리는_남고_적용만_빠진다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);                               // Sway 8
        editor.AddClipBlock("greet", 0, "lean", 0.2f, MotionChannelMode.Relative); // 같은 노드에 겹친다 — 막힌다
        editor.AddClipBlock("greet", 0, "step", 0f);
        Assert.Contains(ClipValidator.ValidateClip(editor.Document, Greet(editor), RigFile.NodeIds), p => p.Blocking);
        Assert.Equal(2, ClipValidator.OverlappingBlocks(editor.Document, Greet(editor)).Count);

        // lean을 끈다 — 한 칸. 겹침이 풀리고, 미리보기에서 빠지고, 길이는 그대로(자리가 남는다).
        int lean = Greet(editor).Tracks[0].Blocks.FindIndex(b => b.Motion == "lean");
        long revision = editor.Revision;
        editor.SetClipBlockMuted("greet", 0, lean, true);
        Assert.Equal(revision + 1, editor.Revision);
        Assert.DoesNotContain(ClipValidator.ValidateClip(editor.Document, Greet(editor), RigFile.NodeIds), p => p.Blocking);
        Assert.Empty(ClipValidator.OverlappingBlocks(editor.Document, Greet(editor)));
        Near(8f, SwayZ(Pose(editor, 9.0), "s1"));
        Near(80f, TrackXOf(Pose(editor, 9.0), "s1"));
        Assert.Equal(0.7f, ClipTiming.DurationOf(editor.Document, Greet(editor)), 4);

        // 굽기에도 안 든다.
        MotionAsset baked = ClipBake.Bake(editor, Template(editor), "greet", "s1", "baked");
        Near(8f, SwayZ(MotionPreview.Pose(Template(editor), baked, 9.0, null, "s1/"), "s1"));
        editor.Undo();

        // 저작본: 끈 블록만 muted가 적힌다. 값을 고쳐도 끈 채로 남는다.
        string json = Mt.Authoring.Serialization.MotionDocumentJson.Write(editor.Document);
        Assert.Equal(1, json.Split("\"muted\": true").Length - 1);
        Assert.DoesNotContain("\"muted\": false", json, StringComparison.Ordinal);
        Assert.True(Mt.Authoring.Serialization.MotionDocumentJson.Read(json).FindClip("greet")!.Tracks[0].Blocks[lean].IsMuted);
        int moved = editor.UpdateClipBlock("greet", 0, lean, new ClipBlock { At = 0.3f, Motion = "lean", Mode = MotionChannelMode.Relative, Muted = true });
        Assert.True(Greet(editor).Tracks[0].Blocks[moved].IsMuted);

        // 다시 켜면 돌아온다 — 같은 값은 흔적이 없다.
        editor.SetClipBlockMuted("greet", 0, moved, false);
        Assert.Null(Greet(editor).Tracks[0].Blocks[moved].Muted);
        Near(13f, SwayZ(Pose(editor, 9.0), "s1"));
        revision = editor.Revision;
        editor.SetClipBlockMuted("greet", 0, moved, false);
        Assert.Equal(revision, editor.Revision);
    }

    [Fact]
    public void except로_뺀_블록_없이_그_시각의_상태를_낸다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 0, "lean", 1f, MotionChannelMode.Relative);
        ClipBlock lean = Greet(editor).Tracks[0].Blocks[1];

        Near(13f, SwayZ(Pose(editor, 1.5), "s1"));
        Near(8f, SwayZ(ClipPreview.Pose(Template(editor), editor.Document, Greet(editor), 1.5, null, null, lean), "s1")); // lean의 시작값
    }

    [Fact]
    public void 반복은_같은_블록을_끝에_이어_놓은_것과_같다_리듀서로도()
    {
        MotionEditor editor = NewEditor();
        editor.AddClipBlock("greet", 0, "lean", 0f, MotionChannelMode.Relative); // Sway +5 (0.5초, commit)
        editor.SetClipBlockRepeat("greet", 0, 0, 3);
        editor.AddClipBlock("greet", 1, "nod", 0f);                              // s2: 복원 모션
        editor.SetClipBlockRepeat("greet", 1, 0, 2);
        ClipAsset clip = Greet(editor);

        Assert.Equal(1.5f, ClipTiming.EndOf(editor.Document, clip.Tracks[0].Blocks[0]), 4);
        Assert.Equal(1.5f, ClipTiming.DurationOf(editor.Document, clip), 4);
        Assert.Equal("lean ×3", clip.Tracks[0].Blocks[0].Describe());

        // 유지 모션은 돌 때마다 쌓인다: 5 · 10 · 15, 가운데는 그 사이.
        Near(5f, SwayZ(Pose(editor, 0.5), "s1"));
        Assert.InRange(SwayZ(Pose(editor, 0.75), "s1"), 6f, 9f);
        Near(10f, SwayZ(Pose(editor, 1.0), "s1"));
        Near(15f, SwayZ(Pose(editor, 9.0), "s1"));

        // 복원 모션은 매번 처음 자세에서 다시 돈다.
        Near(-18f, MoveYOf(Pose(editor, 0.3), "s2"));
        Near(-18f, MoveYOf(Pose(editor, 0.9), "s2"));
        Near(0f, MoveYOf(Pose(editor, 9.0), "s2"));

        // 똑같은 블록을 이어 놓은 클립과 프레임마다 같다.
        MotionEditor chained = NewEditor();
        for (int i = 0; i < 3; i++)
        {
            chained.AddClipBlock("greet", 0, "lean", 0.5f * i, MotionChannelMode.Relative);
        }

        for (int k = 0; k <= 40; k++)
        {
            double t = k / 24.0;
            Near(SwayZ(ClipPreview.Pose(Template(chained), chained.Document, Greet(chained), t), "s1"), SwayZ(Pose(editor, t), "s1"));
        }

        // 정지 프레임 = motion 커맨드를 그 횟수만큼.
        var tuning = new StageReducerTuning
        {
            RigSchemas = RigFile.Dto,
            ReferenceStageWidth = 1920f,
            BaseResolution = new Vec2(1920f, 1080f),
            RoleAnchors = new RoleAnchorTuningBodyDto(),
            Motions = MotionExport.ToClips(editor.Document).ToDictionary(c => c.Name, c => c),
        };
        var commands = new List<StageCommand> { Cmd("slot", "s1") };
        commands.AddRange(Enumerable.Repeat(Cmd("motion", "s1", "lean", "relative"), 3));
        Near(15f, SwayZ(StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning).Nodes, "s1"));

        // except: 둘째 바퀴(번호 1)부터 빼면 첫 바퀴가 남긴 5가 시작값이다.
        Near(5f, SwayZ(ClipPreview.Pose(Template(editor), editor.Document, clip, 0.75, null, null, clip.Tracks[0].Blocks[0], null, 1), "s1"));

        // 겹침·길이 검사는 늘어난 길이로 본다. 굽기는 바퀴를 다 담는다.
        editor.AddClipBlock("greet", 0, "bow", 1.2f); // lean ×3(0 ~ 1.5)과 같은 노드
        Assert.Contains(ClipValidator.ValidateClip(editor.Document, Greet(editor), RigFile.NodeIds), p => p.Blocking && p.Message.Contains("겹칠 수 없다", StringComparison.Ordinal));
        editor.Undo();
        MotionAsset baked = ClipBake.Bake(editor, Template(editor), "greet", "s1", "baked");
        Near(15f, SwayZ(MotionPreview.Pose(Template(editor), baked, 9.0, null, "s1/"), "s1"));
        Near(10f, SwayZ(MotionPreview.Pose(Template(editor), baked, 1.0, null, "s1/"), "s1"));

        // 저작본: 한 번이면 안 적힌다. 터무니없는 값은 검증이 막는다.
        Assert.Equal(2, Mt.Authoring.Serialization.MotionDocumentJson.Write(editor.Document).Split("\"repeat\"").Length - 1);
        Greet(editor).Tracks[0].Blocks[0].Repeat = 100000;
        Assert.Contains(ClipValidator.ValidateClip(editor.Document, Greet(editor), RigFile.NodeIds), p => p.Blocking && p.Message.Contains("반복은", StringComparison.Ordinal));
    }
}
