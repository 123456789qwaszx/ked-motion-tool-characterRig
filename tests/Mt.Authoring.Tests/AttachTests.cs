using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

/// <summary>붙이기(<c>PLAN-parts.md</c> P2) — 오브젝트가 다른 오브젝트의 노드 밑에 서서 따라간다.</summary>
public class AttachTests
{
    private const string Part = "Emoji_Part1";

    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));

    /// <summary>s1 · s2(캐릭터) · e1(이모지, s1의 CharSlot_Scale에 붙음).</summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);
        editor.AddSceneObject(null, "s2", "character");
        editor.AttachObject(null, "e1", "s1");
        return editor;
    }

    private static SceneAsset Scene(MotionEditor editor) => editor.Document.ActiveSceneAsset!;

    private static RectNodeTree Build(MotionEditor editor) => ScenePreview.Build(RigFile, Scene(editor));

    private static Quad PartQuad(RectNodeTree tree) => PortraitLayout.RectQuad(tree, "e1/" + Part);

    private static void Near(float expected, float actual, float tolerance = 1e-2f) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Fact]
    public void 붙은_리그는_부모의_그_노드_밑에_서고_부모를_따라간다()
    {
        MotionEditor editor = NewEditor();
        RectNodeTree tree = Build(editor);
        Assert.Equal("s1/CharSlot_Scale", tree.GetParentKey("e1/__root"));
        Assert.Equal((SceneObject.DefaultAttachNode, "s1"), (Scene(editor).Find("e1")!.AttachNode, Scene(editor).Find("e1")!.AttachTo));
        Vec2 before = PartQuad(tree).Center;

        // 부모를 옮기면 같은 양만큼 간다.
        editor.SetObjectPlacement(null, "s1", 200f, -50f, 0f, 1f, 1f);
        Vec2 moved = PartQuad(Build(editor)).Center;
        Near(before.X + 200f, moved.X);
        Near(before.Y - 50f, moved.Y);

        // 부모의 배율도 따라간다(슬롯 계층) — 조각이 두 배가 된다.
        editor.SetObjectPlacement(null, "s1", 0f, 0f, 0f, 2f, 2f);
        Quad scaled = PartQuad(Build(editor));
        Near(EmojiRigFixture.PartSize * 2f, scaled.BottomRight.X - scaled.BottomLeft.X);

        // 제 오프셋은 붙는 자리에 대한 값이다. 배율 x = −1이 좌우 반전의 미리보기 — 사각형이 뒤집힌다.
        editor.SetObjectPlacement(null, "s1", 0f, 0f, 0f, 1f, 1f);
        editor.SetObjectPlacement(null, "e1", 30f, 0f, 0f, -1f, 1f);
        Quad flipped = PartQuad(Build(editor));
        Near(before.X + 30f, flipped.Center.X);
        Assert.True(flipped.BottomRight.X < flipped.BottomLeft.X, "뒤집히지 않았다");
    }

    [Fact]
    public void 슬롯_계층의_모션은_따라가고_초상_계층의_모션은_안_따라간다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotion("step", "character", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("step", "CharSlot_Track_X", MotionChannelKind.Position);
        editor.SetKeys("step", "CharSlot_Track_X", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(1f, 120f) });
        editor.AddMotion("nod", "character", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(1f, -40f) });

        RectNodeTree template = Build(editor);
        Vec2 rest = PartQuad(template).Center;
        SceneObject s1 = Scene(editor).Find("s1")!;

        Vec2 stepped = PartQuad(ScenePreview.Pose(template, s1, editor.Document.Find("step")!, 1.0)).Center;
        Near(rest.X + 120f, stepped.X); // 슬롯이 가면 이모지도 간다

        Vec2 nodded = PartQuad(ScenePreview.Pose(template, s1, editor.Document.Find("nod")!, 1.0)).Center;
        Near(rest.X, nodded.X);
        Near(rest.Y, nodded.Y);         // 초상이 끄덕여도 이모지는 제자리다
    }

    [Fact]
    public void 붙은_것은_부모_바로_뒤에_그리고_없는_부모면_보통_오브젝트로_선다()
    {
        MotionEditor editor = NewEditor();
        Assert.Equal(new[] { "s1", "e1", "s2" }, Scene(editor).DrawOrder().Select(o => o.Id)); // 목록은 s1·e1·s2 — 부모 뒤

        editor.SetObjectLayer(null, "s1", SceneLayers.Front); // 부모가 앞 층으로 가면 같이 간다
        Assert.Equal(new[] { "s2", "s1", "e1" }, Scene(editor).DrawOrder().Select(o => o.Id));
        Assert.Equal(new[] { "e1" }, Scene(editor).AttachedTo("s1").Select(o => o.Id));

        // 손으로 쓴 파일: 없는 부모 · 제 자신 — 붙지 않은 것처럼 선다(던지지 않는다).
        Scene(editor).Find("e1")!.AttachTo = "ghost";
        Assert.Null(Scene(editor).ParentOf(Scene(editor).Find("e1")!));
        Assert.NotEqual("s1/CharSlot_Scale", Build(editor).GetParentKey("e1/__root"));
        Scene(editor).Find("e1")!.AttachTo = "e1";
        Assert.True(Build(editor).Contains("e1/" + Part));

        // 붙는 노드가 그 리그에 없으면 부모의 루트.
        Scene(editor).Find("e1")!.AttachTo = "s1";
        Scene(editor).Find("e1")!.AttachNode = "Nope";
        Assert.Equal("s1/__root", Build(editor).GetParentKey("e1/__root"));
    }

    [Fact]
    public void 편집기는_한_단계만_붙이고_부모를_지우거나_이름을_바꾸면_따라온다()
    {
        MotionEditor editor = NewEditor();
        long revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.AttachObject(null, "s2", "e1")); // 붙은 것에는 못 붙인다
        Assert.Throws<InvalidOperationException>(() => editor.AttachObject(null, "s1", "s2")); // 무언가 붙어 있는 것은 못 붙는다
        Assert.Throws<InvalidOperationException>(() => editor.AttachObject(null, "s2", "s2"));
        Assert.Throws<KeyNotFoundException>(() => editor.AttachObject(null, "s2", "nope"));
        Assert.Equal(revision, editor.Revision);

        // 저작본 왕복. 안 붙은 오브젝트에는 아무것도 안 적힌다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(1, json.Split("\"attachTo\"").Length - 1);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));

        // 이름을 바꾸면 붙은 것이 같은 칸에서 따라온다.
        editor.RenameSceneObject(null, "s1", "hero");
        Assert.Equal("hero", Scene(editor).Find("e1")!.AttachTo);
        editor.Undo();
        Assert.Equal("s1", Scene(editor).Find("e1")!.AttachTo);

        // 붙은 것을 복제하면 같은 데 붙는다.
        SceneObject copy = editor.DuplicateSceneObject(null, "e1");
        Assert.Equal(("s1", SceneObject.DefaultAttachNode), (copy.AttachTo, copy.AttachNode));

        // 부모를 지우면 붙은 것도 같은 칸에서 사라진다.
        editor.RemoveSceneObject(null, "s1");
        Assert.Equal(new[] { "s2" }, Scene(editor).Objects.Select(o => o.Id));
        editor.Undo();
        Assert.Equal(4, Scene(editor).Objects.Count);

        // 떼면 다시 제 층에 선다. 노드를 비우면 루트에 붙는다.
        editor.AttachObject(null, "e1", null);
        Assert.Null(Scene(editor).Find("e1")!.AttachTo);
        Assert.Null(Scene(editor).Find("e1")!.AttachNode);
        editor.AttachObject(null, "e1", "s2", "");
        Assert.Null(Scene(editor).Find("e1")!.AttachNode);
        Assert.Equal("s2/__root", Build(editor).GetParentKey("e1/__root"));
    }
}
