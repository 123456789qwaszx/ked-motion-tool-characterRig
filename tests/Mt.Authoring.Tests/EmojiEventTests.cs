using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>이벤트 키로 이모지 띄우기(<c>PLAN-parts.md</c> §10) — 캐릭터 모션의 <c>emoji</c> 이벤트가 붙은 이모지 리그에 정의의 수명을 돌린다.</summary>
public class EmojiEventTests
{
    private const string Body = "Emoji_Body";

    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));

    private static KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    /// <summary>
    /// nod(0.6초, 캐릭터) · e1(이모지, s1에 붙음) · pop(0.2초 commit absolute: Body y 0 → 40) · out(0.3초 commit absolute: Body y → 0)
    /// 이모지 sweat: pop → 1초 → out. 이모지 stay: pop → 끄라고 할 때까지 → out.
    /// </summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);
        editor.AttachObject(null, "e1", "s1");

        editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("pop", Body, MotionChannelKind.Position);
        editor.SetKeys("pop", Body, MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });

        editor.AddMotion("out", EmojiRigFixture.RigKind, 0.3f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("out", Body, MotionChannelKind.Position);
        editor.SetKeys("out", Body, MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 40f), Linear(0.3f, 10f) });

        editor.AddEmoji("sweat", EmojiRigFixture.RigKind);
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f });
        editor.AddEmoji("stay", EmojiRigFixture.RigKind);
        editor.UpdateEmoji("stay", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = null });
        return editor;
    }

    private static SceneAsset Scene(MotionEditor editor) => editor.Document.ActiveSceneAsset!;

    /// <summary>nod를 s1이 연기할 때 그 시각까지 울린 이벤트.</summary>
    private static List<(double, string?, string, string?)> Fired(MotionEditor editor, double until)
        => ClipPreview.EventsBetween(editor.Document.Find("nod")!, double.NegativeInfinity, until).Select(e => ((double)e.T, (string?)"s1", e.Name, e.Value)).ToList();

    private static List<EmojiPlay> Plays(MotionEditor editor, double t) => EmojiEvents.Resolve(editor.Document, Scene(editor), Fired(editor, t), t);

    private static float BodyY(MotionEditor editor, double t)
        => EmojiEvents.Pose(ScenePreview.Build(RigFile, Scene(editor)), editor.Document, Plays(editor, t)).GetState("e1/" + Body).AnchoredPosition.Y;

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-2f, expected + 1e-2f);

    [Fact]
    public void 이벤트가_울리면_붙은_이모지가_그_시각부터_수명을_돈다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotionEvent("nod", 0.1f, "emoji", "sweat");

        Assert.Empty(Plays(editor, 0.05)); // 아직 안 울렸다
        EmojiPlay play = Assert.Single(Plays(editor, 0.2));
        Assert.Equal(("e1", "sweat"), (play.Target.Id, play.Emoji.Name));
        Assert.Equal(0.1, play.Local, 4);
        Assert.Equal(1.5f, play.Phases.Total, 4);

        Near(20f, BodyY(editor, 0.2));  // 등장의 가운데
        Near(40f, BodyY(editor, 0.8));  // 머묾
        Near(25f, BodyY(editor, 1.45)); // 퇴장의 가운데(뜬 뒤 1.35초)
        Assert.Single(Plays(editor, 1.6));
        Assert.Empty(Plays(editor, 1.7)); // 수명(1.5초)이 다했다 — 모션보다 오래 가도 된다

        // 부리는 오브젝트: 떠 있을 때만 보인다.
        Assert.Equal(new[] { "e1" }, EmojiEvents.Targets(editor.Document, Scene(editor), Fired(editor, double.PositiveInfinity)));
    }

    [Fact]
    public void 머무는_이모지는_끄라고_할_때까지_떠_있고_새_이모지는_앞의_것을_갈아_치운다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotionEvent("nod", 0.1f, "emoji", "stay");
        EmojiPlay staying = Assert.Single(Plays(editor, 500));
        Assert.True(staying.Phases.Staying);
        Near(40f, BodyY(editor, 500));

        // emoji_off — 그때 퇴장이 시작한다(값은 안 본다).
        editor.AddMotionEvent("nod", 0.5f, "emoji_off");
        Assert.Equal(0.7f, Assert.Single(Plays(editor, 0.6)).Phases.Total, 4); // 0.2 + 0.2 + 0.3
        Near(40f, BodyY(editor, 0.5));
        Near(25f, BodyY(editor, 0.65));
        Assert.Empty(Plays(editor, 0.9));

        // 등장이 끝나기 전에 끄면 등장을 마치고 퇴장한다.
        editor.UpdateMotionEvent("nod", 1, 0.15f, "emoji_off", null);
        Assert.Equal(0.5f, Assert.Single(Plays(editor, 0.2)).Phases.Total, 4);

        // 머무는 시간이 정해진 이모지도 일찍 끌 수 있다(1초 머물 것을 0.3초에 끈다). 제 수명이 먼저 다하면 그것이 이긴다.
        editor.UpdateMotionEvent("nod", 0, 0.1f, "emoji", "sweat");
        editor.UpdateMotionEvent("nod", 1, 0.6f, "emoji_off", null);
        Assert.Equal(0.8f, Assert.Single(Plays(editor, 0.6)).Phases.Total, 4);
        Assert.Equal(1.5f, EmojiPreview.PlayPhases(editor.Document, editor.Document.FindEmoji("sweat")!, 5.0).Total, 4);

        // 새 emoji가 울리면 갈아 치운다 — 한 캐릭터에 하나.
        editor.UpdateMotionEvent("nod", 1, 0.4f, "emoji", "stay");
        EmojiPlay replaced = Assert.Single(Plays(editor, 0.5));
        Assert.Equal("stay", replaced.Emoji.Name);
        Assert.Equal(0.1, replaced.Local, 4);
    }

    [Fact]
    public void 모르는_키나_붙은_리그가_없으면_안_뜨고_검증이_알린다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotionEvent("nod", 0.1f, "emoji", "ghost");
        Assert.Empty(Plays(editor, 0.3));
        Assert.Empty(EmojiEvents.Targets(editor.Document, Scene(editor), Fired(editor, 1)));

        MotionProblem notice = Assert.Single(MotionValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Message.Contains("ghost", StringComparison.Ordinal));
        Assert.False(notice.Blocking);

        // 모르는 키는 떠 있던 것도 치운다(런타임과 같은 눈: 그 캐릭터의 이모지를 바꾸려 했다).
        editor.AddMotionEvent("nod", 0f, "emoji", "sweat");
        Assert.Single(Plays(editor, 0.05));
        Assert.Empty(Plays(editor, 0.3));

        // 붙은 이모지 리그가 없으면 미리볼 데가 없다.
        editor.UpdateMotionEvent("nod", 1, 0.1f, "emoji", "sweat");
        editor.AttachObject(null, "e1", null);
        Assert.Empty(Plays(editor, 0.3));
    }

    [Fact]
    public void 클립에서는_블록의_바퀴마다_뜨고_끈_블록은_안_띄운다()
    {
        MotionEditor editor = NewEditor();
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 0.1f }); // 수명 0.6초
        editor.AddMotionEvent("nod", 0.1f, "emoji", "sweat");
        editor.AddClip("clip");
        editor.AddClipRole("clip", "a", "character", "s1");
        editor.AddClipTrack("clip", "a");
        editor.AddClipBlock("clip", 0, "nod", 1f);
        editor.SetClipBlockRepeat("clip", 0, 0, 2); // 1.0~1.6, 1.6~2.2 — 1.1초와 1.7초에 울린다
        ClipAsset clip = editor.Document.FindClip("clip")!;

        List<EmojiPlay> At(double t) => EmojiEvents.Resolve(editor.Document, Scene(editor), ClipPreview.EventsBetween(editor.Document, clip, double.NegativeInfinity, t), t);

        Assert.Empty(At(1.0));
        Assert.Equal(0.2, Assert.Single(At(1.3)).Local, 4);
        Assert.Equal(0.1, Assert.Single(At(1.8)).Local, 4); // 둘째 바퀴가 다시 띄웠다
        Assert.Empty(At(2.4));

        editor.SetClipBlockMuted("clip", 0, 0, true);
        Assert.Empty(At(1.3));
    }
}
