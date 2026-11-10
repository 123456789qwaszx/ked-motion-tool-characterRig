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

    // ── 붙는 자리(로케이터) — PLAN-parts.md §11 ─────────────────────────

    private static readonly FocusTuningFile Focus = FocusTuningFile.Load(FocusTuningFile.TryFindBeside(Fixtures.PathOf("rig-schemas.json"))!);

    /// <summary>노드의 원점이 루트 공간 어디인가.</summary>
    private static Vec2 WorldOf(RectNodeTree tree, string key) => SettledFocusMath.FocusPointInRigSpace(tree.BuildChainTo(key), tree.RootSpace, Vec2.Zero);

    /// <summary>캐릭터의 기준점이 루트 공간 어디인가(표의 기본값).</summary>
    private static Vec2 FocusOf(RectNodeTree tree, string objectId, string preset)
        => SettledFocusMath.FocusPointInRigSpace(tree.BuildChainTo($"{objectId}/{PlaceFocusStageReduction.MeasureNodeId}"), tree.RootSpace, FocusOffsetMath.Resolve(Focus.Body, "", preset, Vec2.Zero));

    private static void Near(Vec2 expected, Vec2 actual)
    {
        Assert.InRange(actual.X, expected.X - 0.05f, expected.X + 0.05f);
        Assert.InRange(actual.Y, expected.Y - 0.05f, expected.Y + 0.05f);
    }

    [Fact]
    public void 붙는_자리는_붙은_캐릭터의_기준점에서_풀리고_캐릭터가_어디에_서든_따라간다()
    {
        MotionEditor editor = NewEditor();
        EmojiAsset Sweat() => editor.Document.FindEmoji("sweat")!;
        SceneObject Actor() => Scene(editor).Find("e1")!;
        RectNodeTree Rest() => ScenePreview.Build(RigFile, Scene(editor), null, null, null, Focus);
        RectNodeTree Posed(RectNodeTree rest) => EmojiPreview.Pose(rest, editor.Document, Sweat(), Actor(), 0.5, null, EmojiPreview.LocatorPoint(rest, Scene(editor), Sweat(), Actor(), Focus));

        // 붙는 자리가 없으면 풀지 않는다 — 오프셋은 붙는 노드에서부터(전과 같다).
        Assert.Null(EmojiPreview.LocatorPoint(Rest(), Scene(editor), Sweat(), Actor(), Focus));

        // faceAura — 자리 노드의 원점이 캐릭터의 그 점에 선다.
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "faceAura" });
        RectNodeTree rest = Rest();
        Near(FocusOf(rest, "s1", "faceAura"), WorldOf(Posed(rest), "e1/Emoji_Anchor"));

        // 캐릭터를 옮기고 키워도(오프셋·배율·자리) 그 점을 따라간다. 다른 기준점은 다른 자리다.
        editor.SetObjectPlacement(null, "s1", 300f, -50f, 0f, 1.5f, 1.5f);
        editor.SetObjectPlace(null, "s1", "left", "face");
        rest = Rest();
        Near(FocusOf(rest, "s1", "faceAura"), WorldOf(Posed(rest), "e1/Emoji_Anchor"));
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "hand_left" }); // 대본의 표기도 받는다
        Near(FocusOf(rest, "s1", "handLeft"), WorldOf(Posed(rest), "e1/Emoji_Anchor"));
        Assert.NotEqual(FocusOf(rest, "s1", "faceAura"), FocusOf(rest, "s1", "handLeft"));

        // 오프셋은 그 점에서부터 — 자리 노드의 좌표로 더해진다.
        Vec2 origin = EmojiPreview.LocatorPoint(rest, Scene(editor), Sweat(), Actor(), Focus)!.Value;
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "handLeft", OffsetX = 30f, OffsetY = -10f });
        Near(origin + new Vec2(30f, -10f), Posed(rest).GetState("e1/Emoji_Anchor").AnchoredPosition);

        // 모르는 이름·붙은 데가 없음 → 풀지 않고, 모르는 이름은 검증이 알린다(막지 않는다).
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "nose" });
        Assert.Null(EmojiPreview.LocatorPoint(rest, Scene(editor), Sweat(), Actor(), Focus));
        EmojiProblem notice = Assert.Single(EmojiValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Message.Contains("nose", StringComparison.Ordinal));
        Assert.False(notice.Blocking);
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "face" });
        editor.AttachObject(null, "e1", null);
        Assert.Null(EmojiPreview.LocatorPoint(Rest(), Scene(editor), Sweat(), Actor(), Focus));
    }

    [Fact]
    public void 붙는_자리는_가만히_선_자세에서_한_번_풀려_초상_계층의_모션을_따라가지_않는다()
    {
        MotionEditor editor = NewEditor();
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "faceAura" });
        editor.AddMotionEvent("nod", 0f, "emoji", "sweat");
        RectNodeTree rest = ScenePreview.Build(RigFile, Scene(editor), null, null, null, Focus);
        SceneObject s1 = Scene(editor).Find("s1")!;

        // nod의 가운데(0.3초) — 캐릭터의 초상이 움직이는 중이어도 이모지의 자리는 가만히 선 자세의 그 점이다.
        RectNodeTree nodding = ScenePreview.Pose(rest, s1, editor.Document.Find("nod")!, 0.3);
        RectNodeTree posed = EmojiEvents.Pose(nodding, editor.Document, Plays(editor, 0.3), null, play => EmojiPreview.LocatorPoint(rest, Scene(editor), play.Emoji, play.Target, Focus));
        Vec2 anchor = WorldOf(posed, "e1/Emoji_Anchor");
        Near(FocusOf(rest, "s1", "faceAura"), anchor);
    }

    [Fact]
    public void 배역_키를_적으면_자리와_붙는_자리가_기준점_표의_그_배역_값으로_풀린다()
    {
        // 픽스처의 표: bust 820 · faceAura 950, 배역 bandi는 공통 −20 · bust +10 → bust 810 · faceAura 930.
        MotionEditor editor = NewEditor();
        editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Exit = "out", HoldSeconds = 1f, Locator = "faceAura" });
        editor.SetObjectPlace(null, "s1", "center");
        RectNodeTree Rest() => ScenePreview.Build(RigFile, Scene(editor), null, null, null, Focus);
        Vec2 PointOf(RectNodeTree tree, string key, string preset)
            => SettledFocusMath.FocusPointInRigSpace(tree.BuildChainTo($"s1/{PlaceFocusStageReduction.MeasureNodeId}"), tree.RootSpace, FocusOffsetMath.Resolve(Focus.Body, key, preset, Vec2.Zero));
        Vec2 AnchorWorld(RectNodeTree rest)
        {
            EmojiAsset sweat = editor.Document.FindEmoji("sweat")!;
            SceneObject actor = Scene(editor).Find("e1")!;
            return WorldOf(EmojiPreview.Pose(rest, editor.Document, sweat, actor, 0.5, null, EmojiPreview.LocatorPoint(rest, Scene(editor), sweat, actor, Focus)), "e1/Emoji_Anchor");
        }

        // 배역 키 없이: 표의 기본값의 가슴 점이 화면 가운데에 온다.
        RectNodeTree plain = Rest();
        Near(Vec2.Zero, PointOf(plain, "", "bust"));
        Vec2 plainFocus = plain.GetState("s1/CharSlot_Track_Focus").AnchoredPosition;
        Vec2 plainAnchor = AnchorWorld(plain);

        // 배역 키 — 한 칸, 앞뒤 빈칸은 버리고 같은 값은 무기록.
        long revision = editor.Revision;
        editor.SetObjectCharacter(null, "s1", " bandi ");
        editor.SetObjectCharacter(null, "s1", "bandi");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal("bandi", Scene(editor).Find("s1")!.Character);

        // 자리: 이제 bandi의 가슴 점(810)이 가운데에 온다 — 캐릭터가 10만큼 올라가 선다.
        RectNodeTree cast = Rest();
        Near(Vec2.Zero, PointOf(cast, "bandi", "bust"));
        Assert.Equal(plainFocus.Y + 10f, cast.GetState("s1/CharSlot_Track_Focus").AnchoredPosition.Y, 2);

        // 붙는 자리: bandi의 faceAura(930)에 선다 — 가슴에서 120 위(기본값이면 130 위였다).
        Near(PointOf(cast, "bandi", "faceAura"), AnchorWorld(cast));
        Assert.Equal(130f, plainAnchor.Y, 1);
        Assert.Equal(120f, AnchorWorld(cast).Y, 1);

        // 표에 없는 배역은 기본값과 같다. 지우면 돌아온다.
        editor.SetObjectCharacter(null, "s1", "nobody");
        Assert.Equal(plainFocus.Y, Rest().GetState("s1/CharSlot_Track_Focus").AnchoredPosition.Y, 3);
        editor.SetObjectCharacter(null, "s1", "  ");
        Assert.Null(Scene(editor).Find("s1")!.Character);

        // 저작본: 적었을 때만 실린다.
        Assert.DoesNotContain("\"character\":", Mt.Authoring.Serialization.MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);
        editor.SetObjectCharacter(null, "s1", "bandi");
        string json = Mt.Authoring.Serialization.MotionDocumentJson.Write(editor.Document);
        Assert.Equal("bandi", Mt.Authoring.Serialization.MotionDocumentJson.Read(json).ActiveSceneAsset!.Find("s1")!.Character);
    }
}
