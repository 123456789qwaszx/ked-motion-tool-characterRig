using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>불투명도 채널(<c>PLAN-parts.md</c> P4, <c>motions/3</c>) — 알파를 키와 커브로.</summary>
public class AlphaChannelTests
{
    private const string Part = "Emoji_Part1";
    private const string Body = "Emoji_Body";
    private const string SpriteRoot = "CharacterPortraitSprite_Root";

    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));

    private static KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    /// <summary>e1(이모지). fade(1초 commit): Part1 알파 1 → 0. dim(1초 commit): Body 알파 1 → 0.5.</summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);

        editor.AddMotion("fade", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit);
        editor.AddChannel("fade", Part, MotionChannelKind.Alpha);
        editor.SetKeys("fade", Part, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0f) });

        editor.AddMotion("dim", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit);
        editor.AddChannel("dim", Body, MotionChannelKind.Alpha);
        editor.SetKeys("dim", Body, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0.5f) });
        return editor;
    }

    private static RectNodeTree Template(MotionEditor editor) => ScenePreview.Build(RigFile, editor.Document.ActiveSceneAsset!);

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-3f, expected + 1e-3f);

    [Fact]
    public void 알파_채널은_트리가_아니라_불투명도_표에_얹힌다()
    {
        MotionEditor editor = NewEditor();
        RectNodeTree template = Template(editor);
        MotionAsset fade = editor.Document.Find("fade")!;
        var alphas = new Dictionary<string, float>();

        RectNodeTree pose = MotionPreview.Pose(template, fade, 0.25, MotionChannelMode.Relative, null, "e1/", alphas);
        Near(0.75f, alphas["e1/" + Part]);
        Assert.Equal(template.GetState("e1/" + Part).AnchoredPosition, pose.GetState("e1/" + Part).AnchoredPosition); // 트리는 그대로

        // relative는 곱이다 — 이미 0.5인 불투명도에서 흐려지면 0.5 × 0.75.
        alphas = new Dictionary<string, float> { ["e1/" + Part] = 0.5f };
        MotionPreview.Pose(template, fade, 0.25, MotionChannelMode.Relative, null, "e1/", alphas);
        Near(0.375f, alphas["e1/" + Part]);

        // absolute는 키 그대로.
        alphas = new Dictionary<string, float> { ["e1/" + Part] = 0.5f };
        MotionPreview.Pose(template, fade, 0.25, MotionChannelMode.Absolute, null, "e1/", alphas);
        Near(0.75f, alphas["e1/" + Part]);

        // 끝: commit은 남고(0), restore는 시작값 그대로다(표를 안 건드린다).
        alphas = new Dictionary<string, float>();
        MotionPreview.Pose(template, fade, 9.0, MotionChannelMode.Relative, null, "e1/", alphas);
        Near(0f, alphas["e1/" + Part]);
        editor.SetEnd("fade", MotionEnd.Restore);
        alphas = new Dictionary<string, float>();
        MotionPreview.Pose(template, editor.Document.Find("fade")!, 9.0, MotionChannelMode.Relative, null, "e1/", alphas);
        Assert.Empty(alphas);

        // 표를 안 주면 알파 채널은 건너뛴다(던지지 않는다).
        MotionPreview.Pose(template, fade, 0.5, MotionChannelMode.Relative, null, "e1/");
    }

    [Fact]
    public void 알파가_있으면_motions_3으로_없으면_전과_같은_글자로_나간다()
    {
        MotionEditor editor = NewEditor();
        string json = MotionsJson.Write(MotionExport.ToClips(editor.Document));
        Assert.Contains("\"schema\": \"motions/3\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"alpha\"", json, StringComparison.Ordinal);

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems), string.Join("\n", problems));
        Assert.Empty(problems);
        Assert.Equal(MotionChannelKind.Alpha, clips[0].Channels[0].Kind);
        Assert.Equal(json, MotionsJson.Write(clips)); // 왕복

        // 알파가 없는 문서는 motions/2 그대로 — 기존 골든이 안 바뀐다.
        Assert.Equal(Fixtures.TextOf("motions.nod-bow.json"), MotionsJson.Write(MotionExport.ToClips(Fixtures.NodBowDocument())));
        Assert.Contains("\"schema\": \"motions/2\"", MotionsJson.Write(MotionExport.ToClips(Fixtures.NodBowDocument())), StringComparison.Ordinal);

        // 저작본 왕복.
        string authoring = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"kind\": \"alpha\"", authoring, StringComparison.Ordinal);
        Assert.Equal(authoring, MotionDocumentJson.Write(MotionDocumentJson.Read(authoring)));
    }

    [Fact]
    public void 규칙은_성분_하나와_0에서_1과_받을_수_있는_노드를_본다()
    {
        MotionEditor editor = NewEditor();
        Assert.Empty(MotionValidator.Validate(editor.Document, RigFile.NodeIds, null, RigFile.AlphaNodeIds));
        Assert.True(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out _, out _, null, RigFile.AlphaNodeIds));

        // 코어 규칙: Y 성분은 없다 · 키 값은 0~1.
        editor.SetKeys("fade", Part, MotionChannelKind.Alpha, Axis.Y, new[] { Linear(0f, 1f), Linear(1f, 1f) });
        Assert.Contains(MotionValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.Message.Contains("X 성분만", StringComparison.Ordinal));
        editor.Undo();
        editor.SetKeys("fade", Part, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1.5f), Linear(1f, 0f) });
        Assert.Contains(MotionValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.Message.Contains("0~1", StringComparison.Ordinal));
        editor.Undo();

        // 툴 규칙: CanvasGroup이 없는 노드에는 못 놓는다 — 내보내기도 막는다.
        editor.AddChannel("fade", "Emoji_Anchor", MotionChannelKind.Alpha);
        editor.SetKeys("fade", "Emoji_Anchor", MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0f) });
        MotionProblem problem = Assert.Single(MotionValidator.Validate(editor.Document, RigFile.NodeIds, null, RigFile.AlphaNodeIds));
        Assert.True(problem.Blocking);
        Assert.Contains("불투명도를 받을 수 없는 노드", problem.Message, StringComparison.Ordinal);
        Assert.False(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out _, out _, null, RigFile.AlphaNodeIds));

        Assert.Equal(new[] { "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, RigFile.AlphaNodeIds[EmojiRigFixture.RigKind].OrderBy(n => n, StringComparer.Ordinal));
        Assert.Contains(SpriteRoot, RigFile.AlphaNodeIds["character"]);
    }

    [Fact]
    public void 리듀서는_commit_알파를_fade와_같은_축으로_접는다()
    {
        // 캐릭터의 초상 뿌리(CanvasGroup이 있다)에 알파 모션 — 절반으로 흐려진 채 남는다.
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("half", "character", 0.5f, MotionEnd.Commit);
        editor.AddChannel("half", SpriteRoot, MotionChannelKind.Alpha);
        editor.SetKeys("half", SpriteRoot, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(0.5f, 0.5f) });

        var tuning = new StageReducerTuning
        {
            RigSchemas = RigFile.Dto,
            ReferenceStageWidth = 1920f,
            BaseResolution = new Vec2(1920f, 1080f),
            RoleAnchors = new RoleAnchorTuningBodyDto(),
            Motions = MotionExport.ToClips(editor.Document).ToDictionary(c => c.Name, c => c),
        };

        StageState state = StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), new[]
        {
            new StageCommand("slot", new[] { "s1" }),
            new StageCommand("fade_in", new[] { "s1" }),            // 알파 1
            new StageCommand("motion", new[] { "s1", "half" }),      // relative: 1 × 0.5
            new StageCommand("motion", new[] { "s1", "half" }),      // 한 번 더: 0.5 × 0.5
        }, tuning);
        Assert.Empty(state.Unhandled);
        Near(0.25f, state.GetAlpha("s1/" + SpriteRoot));

        // 툴의 미리보기도 같은 값(같은 함수) — 두 번 얹은 표.
        RectNodeTree template = ScenePreview.Build(RigFile, editor.Document.ActiveSceneAsset!);
        var alphas = new Dictionary<string, float>();
        MotionPreview.Pose(template, editor.Document.Find("half")!, 9.0, MotionChannelMode.Relative, null, "s1/", alphas);
        MotionPreview.Pose(template, editor.Document.Find("half")!, 9.0, MotionChannelMode.Relative, null, "s1/", alphas);
        Near(0.25f, alphas["s1/" + SpriteRoot]);
    }

    [Fact]
    public void 클립에서_알파는_블록을_따라_이어지고_굽기에도_든다()
    {
        MotionEditor editor = NewEditor();
        editor.AddClip("clip");
        editor.AddClipRole("clip", "e", EmojiRigFixture.RigKind, "e1");
        editor.AddClipTrack("clip", "e");
        editor.AddClipTrack("clip", "e");
        editor.AddClipBlock("clip", 0, "dim", 0f);  // Body 1 → 0.5
        editor.AddClipBlock("clip", 1, "fade", 0.5f); // Part1 1 → 0 (0.5 ~ 1.5)
        ClipAsset clip = editor.Document.FindClip("clip")!;
        RectNodeTree template = Template(editor);

        var alphas = new Dictionary<string, float>();
        ClipPreview.Pose(template, editor.Document, clip, 1.0, null, null, null, alphas);
        Near(0.5f, alphas["e1/" + Body]);
        Near(0.5f, alphas["e1/" + Part]);

        // 끈 블록의 알파는 빠진다.
        editor.SetClipBlockMuted("clip", 0, 0, true);
        alphas = new Dictionary<string, float>();
        ClipPreview.Pose(template, editor.Document, clip, 1.0, null, null, null, alphas);
        Assert.False(alphas.ContainsKey("e1/" + Body));
        editor.Undo();

        // 굽기: 알파 채널 둘이 든 모션 하나 — 프레임마다 클립과 같다.
        MotionAsset baked = ClipBake.Bake(editor, template, "clip", "e1", "baked");
        Assert.Equal(new[] { (Body, MotionChannelKind.Alpha), (Part, MotionChannelKind.Alpha) }, baked.Channels.Select(c => (c.Node, c.Kind)));
        Assert.Equal(MotionEnd.Commit, baked.End);
        Assert.Empty(MotionValidator.ValidateMotion(baked, RigFile.NodeIds, null, RigFile.AlphaNodeIds));

        for (int k = 0; k <= 36; k++)
        {
            double t = k / 24.0;
            var expected = new Dictionary<string, float>();
            ClipPreview.Pose(template, editor.Document, editor.Document.FindClip("clip")!, t, null, null, null, expected);
            var actual = new Dictionary<string, float>();
            MotionPreview.Pose(template, baked, t, MotionChannelMode.Relative, null, "e1/", actual);

            foreach (string node in new[] { Body, Part })
            {
                float want = expected.TryGetValue("e1/" + node, out float e) ? e : 1f;
                float got = actual.TryGetValue("e1/" + node, out float a) ? a : 1f;
                Assert.InRange(got, want - 0.01f, want + 0.01f);
            }
        }
    }
}
