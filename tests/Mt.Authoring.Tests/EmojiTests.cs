using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>이모지 정의(<c>PLAN-parts.md</c> §8) — 스킨 + 등장·머묾·퇴장 모션의 묶음과 그 수명.</summary>
public class EmojiTests
{
    private const string Body = "Emoji_Body";
    private const string Part = "Emoji_Part1";

    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));

    private static KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    /// <summary>
    /// e1(이모지, s1에 붙음) · 스킨 땀 · 모션 셋:
    /// pop(0.2초 commit, absolute): Body y 0 → 40 · bob(0.4초 restore, relative): Body y 0 → 10 → 0 · fade(0.3초 commit): Part1 알파 1 → 0.
    /// 이모지 sweat: pop → bob을 1초 → fade.
    /// </summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);
        editor.AttachObject(null, "e1", "s1");
        editor.AddSkin("땀", EmojiRigFixture.RigKind);
        editor.SetSkinPart("땀", Part, "art/sweat.png");

        editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("pop", Body, MotionChannelKind.Position);
        editor.SetKeys("pop", Body, MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });

        editor.AddMotion("bob", EmojiRigFixture.RigKind, 0.4f);
        editor.AddChannel("bob", Body, MotionChannelKind.Position);
        editor.SetKeys("bob", Body, MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 10f), Linear(0.4f, 0f) });

        editor.AddMotion("fade", EmojiRigFixture.RigKind, 0.3f, MotionEnd.Commit);
        editor.AddChannel("fade", Part, MotionChannelKind.Alpha);
        editor.SetKeys("fade", Part, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(0.3f, 0f) });

        editor.AddEmoji("sweat", EmojiRigFixture.RigKind);
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Enter = "pop", Hold = "bob", Exit = "fade", HoldSeconds = 1f, OffsetX = 120f, OffsetY = 300f, Locator = "faceAura" });
        return editor;
    }

    private static EmojiAsset Sweat(MotionEditor editor) => editor.Document.FindEmoji("sweat")!;

    private static SceneObject Actor(MotionEditor editor) => editor.Document.ActiveSceneAsset!.Find("e1")!;

    private static RectNodeTree Template(MotionEditor editor) => ScenePreview.Build(RigFile, editor.Document.ActiveSceneAsset!);

    private static float BodyY(MotionEditor editor, double t, IDictionary<string, float>? alphas = null)
        => EmojiPreview.Pose(Template(editor), editor.Document, Sweat(editor), Actor(editor), t, alphas).GetState("e1/" + Body).AnchoredPosition.Y;

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-2f, expected + 1e-2f);

    [Fact]
    public void 수명은_등장_머묾_퇴장이고_머묾은_등장이_끝난_자세에서_되풀이한다()
    {
        MotionEditor editor = NewEditor();
        EmojiPhases phases = EmojiPreview.PhasesOf(editor.Document, Sweat(editor));
        Assert.Equal((0.2f, 1f, 0.3f, false), (phases.Enter, phases.Hold, phases.Exit, phases.Staying));
        Assert.Equal(1.5f, phases.Total, 4);

        // 등장: 0 → 40.
        Near(20f, BodyY(editor, 0.1));
        Near(40f, BodyY(editor, 0.2));

        // 머묾: 40 위에서 bob이 0.4초마다 다시 돈다(쌓이지 않는다) — 0.4 · 0.8초가 꼭대기(50), 0.6 · 1.0초가 바닥(40).
        Near(50f, BodyY(editor, 0.4));
        Near(40f, BodyY(editor, 0.6));
        Near(50f, BodyY(editor, 0.8));
        Near(40f, BodyY(editor, 1.0));

        // 머무는 시간(1초)이 딱 두 바퀴 반 — 꼭대기(50)에서 멈춘 채 퇴장이 시작한다. 퇴장은 조각을 흐린다.
        Near(50f, BodyY(editor, 1.2));
        var alphas = new Dictionary<string, float>();
        Near(50f, BodyY(editor, 1.35, alphas));
        Near(0.5f, alphas["e1/" + Part]);
        alphas = new Dictionary<string, float>();
        BodyY(editor, 1.5, alphas);
        Near(0f, alphas["e1/" + Part]);

        // 수명 밖에서는 떠 있지 않다.
        Assert.True(EmojiPreview.IsAlive(editor.Document, Sweat(editor), 0));
        Assert.True(EmojiPreview.IsAlive(editor.Document, Sweat(editor), 1.5));
        Assert.False(EmojiPreview.IsAlive(editor.Document, Sweat(editor), 1.6));
        Assert.False(EmojiPreview.IsAlive(editor.Document, Sweat(editor), -0.1));

        // 자리: 정의의 오프셋이 자리 노드에 들어간다.
        Vec2 anchor = EmojiPreview.Pose(Template(editor), editor.Document, Sweat(editor), Actor(editor), 0.5).GetState("e1/Emoji_Anchor").AnchoredPosition;
        Assert.Equal((120f, 300f), (anchor.X, anchor.Y));
    }

    [Fact]
    public void 머무는_이모지와_모션이_빠진_이모지도_선다()
    {
        MotionEditor editor = NewEditor();

        // 머무는 시간이 없으면 머무는 이모지 — 미리보기는 머묾을 얼마간(세 바퀴, 적어도 2초) 보이고 퇴장한다.
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Enter = "pop", Hold = "bob", Exit = "fade", HoldSeconds = null });
        EmojiPhases staying = EmojiPreview.PhasesOf(editor.Document, Sweat(editor));
        Assert.True(staying.Staying);
        Assert.Equal(2f, staying.Hold, 3);
        Near(50f, BodyY(editor, 1.6)); // 여전히 돈다(머묾의 1.4초 = 넷째 바퀴의 꼭대기)

        // 등장·퇴장 없이 머묾만.
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Hold = "bob", HoldSeconds = 0.8f });
        Assert.Equal(0.8f, EmojiPreview.PhasesOf(editor.Document, Sweat(editor)).Total, 4);
        Near(10f, BodyY(editor, 0.2));
        Near(0f, BodyY(editor, 0.8)); // 두 바퀴가 딱 끝난 자리 — 마지막 키

        // 모션이 하나도 없으면 머무는 시간만큼 가만히 떠 있다. 없는 모션을 가리켜도 던지지 않는다.
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Enter = "ghost", HoldSeconds = 0.5f });
        Assert.Equal(0.5f, EmojiPreview.PhasesOf(editor.Document, Sweat(editor)).Total, 4);
        Near(0f, BodyY(editor, 0.3));
    }

    [Fact]
    public void 편집기_명령은_한_칸이고_모션과_스킨의_이름이_바뀌면_따라온다()
    {
        MotionEditor editor = NewEditor();
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.AddEmoji("sweat", EmojiRigFixture.RigKind));
        Assert.Throws<InvalidOperationException>(() => editor.AddEmoji("땀 방울", EmojiRigFixture.RigKind)); // 빈칸
        Assert.Throws<InvalidOperationException>(() => editor.AddEmoji("x", ""));
        Assert.Throws<InvalidOperationException>(() => editor.UpdateEmoji("sweat", new EmojiAsset { HoldSeconds = -1f }));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveEmoji("nope"));
        Assert.Equal(revision, editor.Revision);

        editor.RenameMotion("pop", "pop_in");
        editor.RenameSkin("땀", "sweat_drop");
        Assert.Equal(("pop_in", "sweat_drop"), (Sweat(editor).Enter, Sweat(editor).Skin));
        editor.Undo();
        Assert.Equal("땀", Sweat(editor).Skin);

        // 한글 키도 된다. 빈 글은 "없음"으로 적힌다.
        editor.RenameEmoji("sweat", "땀");
        editor.UpdateEmoji("땀", new EmojiAsset { Skin = "  ", Enter = "", AnchorNode = " " });
        EmojiAsset emoji = editor.Document.FindEmoji("땀")!;
        Assert.Null(emoji.Skin);
        Assert.Null(emoji.Enter);
        Assert.Equal(EmojiAsset.DefaultAnchorNode, emoji.AnchorNode);

        // 저작본 왕복. 이모지가 없는 문서에는 안 적힌다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
        editor.RemoveEmoji("땀");
        Assert.DoesNotContain("emojis", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);
    }

    [Fact]
    public void 검증은_참조와_리그를_보고_내보내기는_스킨을_풀어_싣는다()
    {
        MotionEditor editor = NewEditor();
        Assert.Empty(EmojiValidator.Validate(editor.Document, RigFile.NodeIds));

        Assert.True(EmojisJson.TryExport(editor.Document, RigFile.NodeIds, out string json, out IReadOnlyList<string> problems, RigFile.AlphaNodeIds), string.Join("\n", problems));
        JsonNode root = JsonNode.Parse(json)!;
        Assert.Equal("emojis/1-draft", root["schema"]!.GetValue<string>());
        JsonNode sweat = Assert.Single(root["emojis"]!.AsArray())!;
        Assert.Equal(("sweat", "emoji", "pop", "bob", "fade"), (sweat["name"]!.GetValue<string>(), sweat["rig"]!.GetValue<string>(), sweat["enter"]!.GetValue<string>(), sweat["hold"]!.GetValue<string>(), sweat["exit"]!.GetValue<string>()));
        Assert.Equal("art/sweat.png", sweat["parts"]![Part]!.GetValue<string>()); // 스킨 이름이 아니라 조각 → 그림
        Assert.Equal(1.0, sweat["holdSeconds"]!.GetValue<double>(), 3);
        Assert.Null(sweat["stay"]);
        Assert.Equal(new[] { 120.0, 300.0 }, sweat["offset"]!.AsArray().Select(v => v!.GetValue<double>()));
        Assert.Equal("faceAura", sweat["locator"]!.GetValue<string>());
        Assert.Equal(json, EmojisJson.Write(editor.Document)); // 결정적

        // 머무는 이모지는 holdSeconds 대신 stay.
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Hold = "bob" });
        JsonNode staying = JsonNode.Parse(EmojisJson.Write(editor.Document))!["emojis"]![0]!;
        Assert.True(staying["stay"]!.GetValue<bool>());
        Assert.Null(staying["holdSeconds"]);
        Assert.Null(staying["enter"]);

        // 어긋남: 없는 스킨 · 없는 모션 · 캐릭터 모션 · 캐릭터 스킨 — 막고, 내보내기도 안 된다.
        editor.AddMotion("nod", "character", 0.5f);
        editor.AddSkin("face", "character");
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "ghost", Enter = "nope", Hold = "nod" });
        List<string> messages = EmojiValidator.Validate(editor.Document, RigFile.NodeIds).Where(p => p.Blocking).Select(p => p.Message).ToList();
        Assert.Contains(messages, m => m.Contains("스킨 'ghost'이 없다", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("등장 모션 'nope'이 없다", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("머묾 모션 'nod'의 리그", StringComparison.Ordinal));
        Assert.False(EmojisJson.TryExport(editor.Document, RigFile.NodeIds, out _, out _));
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "face" });
        Assert.Contains(EmojiValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.Message.Contains("리그 'character'의 것", StringComparison.Ordinal));

        // 그림 없음은 알림, 스키마에 없는 자리 노드는 막는다(스키마 계약).
        editor.UpdateEmoji("sweat", new EmojiAsset { AnchorNode = "Nowhere" });
        IReadOnlyList<EmojiProblem> anchorProblems = EmojiValidator.Validate(editor.Document, RigFile.NodeIds);
        Assert.Equal(2, anchorProblems.Count);
        Assert.Contains(anchorProblems, p => !p.Blocking && p.Message.Contains("그림(스킨)이 없다", StringComparison.Ordinal));
        Assert.Contains(anchorProblems, p => p.Blocking && p.Message.Contains("자리 노드 'Nowhere'", StringComparison.Ordinal));

        // 이모지가 쓰는 모션이 규칙을 어기면 그것도 막는다.
        editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Enter = "pop" });
        editor.SetDuration("pop", 0.1f);
        Assert.False(EmojisJson.TryExport(editor.Document, RigFile.NodeIds, out _, out problems));
        Assert.Contains(problems, p => p.Contains("pop", StringComparison.Ordinal));
    }

    // ── 이모지 안에서 구간의 모션을 고친다(PLAN-parts.md §15) ───────────

    [Fact]
    public void 구간의_창은_그_모션의_0초가_수명의_몇_초인지_내고_머묾은_지금_바퀴의_처음이다()
    {
        MotionEditor editor = NewEditor(); // pop 0.2 › bob 0.4를 1초 › fade 0.3
        (bool, double) Window(EmojiPhase phase, double t) => EmojiPreview.PhaseWindow(editor.Document, Sweat(editor), phase, t);
        void Same((bool InRange, double Offset) expected, (bool InRange, double Offset) actual)
        {
            Assert.Equal(expected.InRange, actual.InRange);
            Assert.Equal(expected.Offset, actual.Offset, 4);
        }

        Same((true, 0), Window(EmojiPhase.Enter, 0));
        Same((true, 0), Window(EmojiPhase.Enter, 0.2));   // 경계는 앞 구간의 것
        Same((false, 0), Window(EmojiPhase.Enter, 0.3));

        Same((false, 0.2), Window(EmojiPhase.Hold, 0.2));
        Same((true, 0.2), Window(EmojiPhase.Hold, 0.3));  // 첫 바퀴
        Same((true, 0.2), Window(EmojiPhase.Hold, 0.6));  // 첫 바퀴의 끝(다음 바퀴의 처음이 아니다)
        Same((true, 0.6), Window(EmojiPhase.Hold, 0.7));  // 둘째 바퀴
        Same((true, 1.0), Window(EmojiPhase.Hold, 1.2));  // 셋째 바퀴 — 머무는 시간이 여기서 다한다
        Same((false, 1.0), Window(EmojiPhase.Hold, 1.3));

        Same((false, 1.2), Window(EmojiPhase.Exit, 1.2));
        Same((true, 1.2), Window(EmojiPhase.Exit, 1.35));
        Same((false, 1.2), Window(EmojiPhase.Exit, 1.6));

        // 구간이 얹히기 전의 자세: 등장 전 0, 머묾 전 40(등장이 남긴 것 — 바퀴마다 같다), 퇴장 전 50(머묾이 멈춘 자리).
        float Before(EmojiPhase phase) => EmojiPreview.PoseBefore(Template(editor), editor.Document, Sweat(editor), Actor(editor), phase).GetState("e1/" + Body).AnchoredPosition.Y;
        Near(0f, Before(EmojiPhase.Enter));
        Near(40f, Before(EmojiPhase.Hold));
        Near(50f, Before(EmojiPhase.Exit));

        // 이 이모지만의 모션으로 — 복제해 꽂는다, 한 칸. 다른 이모지는 원래 것을 쓴다.
        editor.AddEmoji("other", EmojiRigFixture.RigKind);
        editor.UpdateEmoji("other", new EmojiAsset { Hold = "bob", HoldSeconds = 1f });
        Assert.Equal(2, EmojiPreview.UsesOf(editor.Document, "bob"));
        long revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.MakeEmojiPhaseUnique("sweat", EmojiPhase.Hold, "pop"));      // 이미 있는 이름
        Assert.Throws<InvalidOperationException>(() => editor.MakeEmojiPhaseUnique("sweat", EmojiPhase.Hold, "땀 까딱"));  // 이름 규칙
        Assert.Throws<InvalidOperationException>(() => editor.MakeEmojiPhaseUnique("other", EmojiPhase.Enter, "x"));       // 그 구간에 모션이 없다
        Assert.Equal(revision, editor.Revision);
        Assert.Equal("bob_sweat", editor.MakeEmojiPhaseUnique("sweat", EmojiPhase.Hold, "bob_sweat"));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(("bob_sweat", "bob"), (Sweat(editor).Hold, editor.Document.FindEmoji("other")!.Hold));
        Assert.Equal(1, EmojiPreview.UsesOf(editor.Document, "bob"));
        editor.SetKeys("bob_sweat", Body, MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 30f), Linear(0.4f, 0f) });
        Assert.Equal(10f, editor.Document.Find("bob")!.Channels[0].Y!.Keys[1].V); // 원본은 그대로
        Near(70f, BodyY(editor, 0.4));
        editor.Undo();
        editor.Undo();
        Assert.Equal("bob", Sweat(editor).Hold);
        Assert.Null(editor.Document.Find("bob_sweat"));
    }
}
