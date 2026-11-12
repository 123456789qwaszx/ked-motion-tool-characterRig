using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

/// <summary>연출 클립 K1(<c>PLAN-clip.md</c>) — 모델·편집기 명령·저작본 왕복.</summary>
public class ClipEditorTests
{
    /// <summary>두 캐릭터(s1·s2)와 모션 둘(nod 0.5초 restore · bow 1초 commit)이 든 문서.</summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character");
        editor.AddMotion("nod", "character", 0.5f);
        editor.AddMotion("bow", "character", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        return editor;
    }

    /// <summary>greet: 배역 a(s1)·b(s2), 트랙 하나씩. a에 bow@0, b에 nod@0.5.</summary>
    private static MotionEditor NewGreet()
    {
        MotionEditor editor = NewEditor();
        editor.AddClip("greet");
        editor.AddClipRole("greet", "a", "character", "s1");
        editor.AddClipRole("greet", "b", "character", "s2");
        editor.AddClipTrack("greet", "a");
        editor.AddClipTrack("greet", "b");
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0.5f);
        return editor;
    }

    private static ClipAsset Greet(MotionEditor editor) => editor.Document.FindClip("greet")!;

    [Fact]
    public void 클립이_없는_문서는_저작본에_clips를_쓰지_않는다()
    {
        MotionEditor editor = NewEditor();
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.DoesNotContain("clips", json, StringComparison.Ordinal);
        Assert.Empty(MotionDocumentJson.Read(json).Clips);
    }

    [Fact]
    public void 클립은_저작본에_왕복한다()
    {
        MotionEditor editor = NewGreet();
        editor.SetClipDuration("greet", 2.5f);

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal(json, MotionDocumentJson.Write(read));
        Assert.Contains("\"schema\": \"motion-authoring/2\"", json, StringComparison.Ordinal);

        ClipAsset clip = Assert.Single(read.Clips);
        Assert.Equal("greet", clip.Name);
        Assert.Equal(2.5f, clip.Duration);
        Assert.Equal("scene", clip.Scene); // 만들 때의 활성 씬이 적힌다
        Assert.Equal(new[] { "a", "b" }, clip.Roles.Select(r => r.Name));
        Assert.Equal(new[] { "s1", "s2" }, clip.Roles.Select(r => r.Object));
        Assert.All(clip.Roles, r => Assert.Equal("character", r.Rig));
        Assert.Equal(new[] { "a", "b" }, clip.Tracks.Select(t => t.Role));

        ClipBlock bow = Assert.Single(clip.Tracks[0].Blocks);
        Assert.Equal(("bow", 0f, MotionChannelMode.Absolute), (bow.Motion, bow.At, bow.Mode));
        ClipBlock nod = Assert.Single(clip.Tracks[1].Blocks);
        Assert.Equal(("nod", 0.5f, MotionChannelMode.Relative), (nod.Motion, nod.At, nod.Mode));
    }

    [Fact]
    public void 예전_판의_카메라_모션과_카메라_줄과_상태_블록은_읽을_때_버린다()
    {
        MotionDocument read = MotionDocumentJson.Read(
            """
            {
              "schema": "motion-authoring/2",
              "motions": [ { "name": "nod", "rig": "character", "duration": 1 }, { "name": "push_in", "rig": "camera", "duration": 1 } ],
              "clips": [ { "name": "t", "roles": [ { "name": "a", "rig": "character" } ], "tracks": [
                { "role": "a", "blocks": [ { "at": 0, "motion": "nod" }, { "at": 0.5, "kind": "visible", "visible": false, "duration": 0.5 } ] },
                { "role": "@camera", "blocks": [ { "at": 0, "motion": "push_in" }, { "at": 1, "kind": "camera", "zoom": 4 } ] } ] } ]
            }
            """);

        Assert.Equal(new[] { "nod" }, read.Motions.Select(m => m.Name));
        ClipTrack track = Assert.Single(read.Clips[0].Tracks);
        Assert.Equal("nod", Assert.Single(track.Blocks).Motion);

        // 다시 쓴 문서에는 그 흔적이 없다.
        string json = MotionDocumentJson.Write(read);
        Assert.DoesNotContain("@camera", json, StringComparison.Ordinal);
        Assert.DoesNotContain("push_in", json, StringComparison.Ordinal);
        Assert.DoesNotContain("visible\": false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 목록이_빠진_클립도_읽힌다()
    {
        MotionDocument read = MotionDocumentJson.Read(
            """{ "schema": "motion-authoring/2", "motions": [], "clips": [ { "name": "empty" }, { "name": "t", "tracks": [ { "role": "a" } ] } ] }""");
        Assert.Equal(2, read.Clips.Count);
        Assert.Empty(read.Clips[0].Roles);
        Assert.Empty(read.Clips[0].Tracks);
        Assert.Empty(read.Clips[1].Tracks[0].Blocks);
    }

    [Fact]
    public void 명령_하나는_되돌리기_한_칸이다()
    {
        MotionEditor editor = NewGreet();
        string full = MotionDocumentJson.Write(editor.Document);

        // 문서 준비(오브젝트 1 + 모션 2) 3칸 + 클립 7칸.
        for (int i = 0; i < 7; i++)
        {
            Assert.True(editor.CanUndo);
            editor.Undo();
        }

        Assert.Empty(editor.Document.Clips);
        Assert.Equal(2, editor.Document.Motions.Count);

        for (int i = 0; i < 7; i++)
        {
            editor.Redo();
        }

        Assert.False(editor.CanRedo);
        Assert.Equal(full, MotionDocumentJson.Write(editor.Document));
    }

    [Fact]
    public void 같은_값은_기록하지_않고_Clip_변화를_알린다()
    {
        MotionEditor editor = NewGreet();
        var kinds = new List<MotionChangeKind>();
        editor.Changed += (_, e) => kinds.Add(e.Kind);
        long revision = editor.Revision;

        editor.SetClipBlockMode("greet", 0, 0, MotionChannelMode.Absolute); // 이미 absolute
        editor.BindClipRole("greet", "a", "s1");
        editor.SetClipDuration("greet", null);
        editor.MoveClipBlock("greet", 1, 0, 0.5f);
        editor.RenameClip("greet", "greet");
        Assert.Equal(revision, editor.Revision);
        Assert.Empty(kinds);

        editor.SetClipBlockMode("greet", 0, 0, MotionChannelMode.Relative);
        Assert.Equal(new[] { MotionChangeKind.Clip }, kinds);
    }

    [Fact]
    public void 이름_규칙과_겹침을_막는다()
    {
        MotionEditor editor = NewGreet();
        Assert.Throws<InvalidOperationException>(() => editor.AddClip("Greet!"));
        Assert.Throws<InvalidOperationException>(() => editor.AddClip("greet"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddClip("other", "no_such_scene"));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipRole("greet", "a", "character"));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipRole("greet", "C", "character"));
        Assert.Throws<InvalidOperationException>(() => editor.AddClipRole("greet", "c", ""));
        Assert.Throws<KeyNotFoundException>(() => editor.AddClipTrack("greet", "nobody"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddClipBlock("greet", 0, "no_such_motion", 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddClipBlock("greet", 9, "nod", 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.RemoveClipBlock("greet", 0, 5));
        Assert.Throws<InvalidOperationException>(() => editor.SetClipDuration("greet", -1f));
        Assert.Throws<InvalidOperationException>(() => editor.MoveClipBlock("greet", 0, 0, float.NaN));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveClip("nope"));

        // 던진 명령은 흔적이 없다.
        Assert.Single(editor.Document.Clips);
        Assert.Equal(2, Greet(editor).Roles.Count);
    }

    [Fact]
    public void 블록은_시각_순으로_서고_0_아래로_못_간다()
    {
        MotionEditor editor = NewGreet();

        Assert.Equal(1, editor.AddClipBlock("greet", 0, "nod", 1.5f));
        Assert.Equal(1, editor.AddClipBlock("greet", 0, "nod", 1f));
        Assert.Equal(1, editor.AddClipBlock("greet", 0, "nod", -3f)); // 0으로 붙는다 — 같은 시각의 bow 뒤
        Assert.Equal(new[] { 0f, 0f, 1f, 1.5f }, Greet(editor).Tracks[0].Blocks.Select(b => b.At));
    }

    [Fact]
    public void 같은_시각이면_먼저_있던_블록_뒤에_선다()
    {
        MotionEditor editor = NewGreet();
        Assert.Equal(1, editor.AddClipBlock("greet", 0, "nod", 0f));
        Assert.Equal(new[] { "bow", "nod" }, Greet(editor).Tracks[0].Blocks.Select(b => b.Motion));
    }

    [Fact]
    public void 블록을_옮기면_새_자리를_돌려주고_한_칸이다()
    {
        MotionEditor editor = NewGreet();
        editor.AddClipBlock("greet", 0, "nod", 1f); // a: bow@0, nod@1

        Assert.Equal((0, 1), editor.MoveClipBlock("greet", 0, 0, 2f)); // bow를 nod 뒤로
        Assert.Equal(new[] { "nod", "bow" }, Greet(editor).Tracks[0].Blocks.Select(b => b.Motion));

        Assert.Equal((1, 0), editor.MoveClipBlock("greet", 0, 1, 0.25f, toTrack: 1)); // bow를 b 트랙으로
        Assert.Equal(new[] { "nod" }, Greet(editor).Tracks[0].Blocks.Select(b => b.Motion));
        Assert.Equal(new[] { ("bow", 0.25f), ("nod", 0.5f) }, Greet(editor).Tracks[1].Blocks.Select(b => (b.Motion, b.At)));

        editor.Undo();
        Assert.Equal(new[] { "nod", "bow" }, Greet(editor).Tracks[0].Blocks.Select(b => b.Motion));
        Assert.Single(Greet(editor).Tracks[1].Blocks);
    }

    [Fact]
    public void 새_블록의_mode는_모션의_재생_기준을_물려받고_따로_줄_수_있다()
    {
        MotionEditor editor = NewGreet();
        Assert.Equal(MotionChannelMode.Absolute, Greet(editor).Tracks[0].Blocks[0].Mode); // bow의 렌즈

        int index = editor.AddClipBlock("greet", 1, "bow", 1f, MotionChannelMode.Relative);
        Assert.Equal(MotionChannelMode.Relative, Greet(editor).Tracks[1].Blocks[index].Mode);

        // 블록의 mode는 모션의 렌즈를 건드리지 않는다.
        editor.SetClipBlockMode("greet", 0, 0, MotionChannelMode.Relative);
        Assert.Equal(MotionChannelMode.Absolute, editor.Document.Find("bow")!.PreviewMode);
    }

    [Fact]
    public void 모션_이름을_바꾸면_블록이_같은_칸에서_따라온다()
    {
        MotionEditor editor = NewGreet();
        editor.RenameMotion("bow", "deep_bow");
        Assert.Equal("deep_bow", Greet(editor).Tracks[0].Blocks[0].Motion);
        Assert.Equal("nod", Greet(editor).Tracks[1].Blocks[0].Motion);

        editor.Undo();
        Assert.Equal("bow", Greet(editor).Tracks[0].Blocks[0].Motion);
        Assert.NotNull(editor.Document.Find("bow"));
    }

    [Fact]
    public void 모션을_지우면_블록은_남는다()
    {
        MotionEditor editor = NewGreet();
        editor.RemoveMotion("nod");

        // 조용히 같이 지우지 않는다 — 검증이 알린다(K2).
        Assert.Equal("nod", Greet(editor).Tracks[1].Blocks[0].Motion);
        Assert.Equal(0.5f, ClipTiming.EndOf(editor.Document, Greet(editor).Tracks[1].Blocks[0])); // 길이 0으로 본다
    }

    [Fact]
    public void 오브젝트와_씬의_이름을_바꾸면_클립의_참조가_따라온다()
    {
        MotionEditor editor = NewGreet();
        editor.RenameSceneObject(null, "s2", "guest");
        Assert.Equal(new[] { "s1", "guest" }, Greet(editor).Roles.Select(r => r.Object));

        editor.Undo();
        Assert.Equal(new[] { "s1", "s2" }, Greet(editor).Roles.Select(r => r.Object));
    }

    [Fact]
    public void 배역을_지우면_트랙이_따라온다()
    {
        MotionEditor editor = NewGreet();
        editor.AddClipTrack("greet", "a"); // a: 트랙 0·2

        editor.RemoveClipRole("greet", "a");
        Assert.Equal(new[] { "b" }, Greet(editor).Roles.Select(r => r.Name));
        Assert.Equal(new[] { "b" }, Greet(editor).Tracks.Select(t => t.Role));

        editor.Undo();
        Assert.Equal(3, Greet(editor).Tracks.Count);
        Assert.Single(Greet(editor).Tracks[0].Blocks);

        editor.BindClipRole("greet", "b", null);
        Assert.Null(Greet(editor).Roles[1].Object);
    }

    [Fact]
    public void 길이는_적어_둔_값이고_없으면_마지막_블록의_끝이다()
    {
        MotionEditor editor = NewGreet();
        ClipAsset clip = Greet(editor);
        Assert.Equal(1f, ClipTiming.EndOfBlocks(editor.Document, clip)); // bow 0→1, nod 0.5→1
        Assert.Equal(1f, ClipTiming.DurationOf(editor.Document, clip));

        editor.MoveClipBlock("greet", 1, 0, 2f);
        Assert.Equal(2.5f, ClipTiming.DurationOf(editor.Document, Greet(editor)));

        editor.SetDuration("nod", 1f); // 모션 길이가 바뀌면 블록 길이도 따라온다
        Assert.Equal(3f, ClipTiming.DurationOf(editor.Document, Greet(editor)));

        editor.SetClipDuration("greet", 4f);
        Assert.Equal(4f, ClipTiming.DurationOf(editor.Document, Greet(editor)));
        Assert.Equal(3f, ClipTiming.EndOfBlocks(editor.Document, Greet(editor)));
    }

    [Fact]
    public void 클립은_motions_json에_나가지_않는다()
    {
        MotionEditor plain = NewEditor();
        MotionEditor withClip = NewGreet();
        Assert.Equal(
            MotionsJson.Write(Validation.MotionExport.ToClips(plain.Document)),
            MotionsJson.Write(Validation.MotionExport.ToClips(withClip.Document)));
    }

    [Fact]
    public void 클립과_블록_지우기()
    {
        MotionEditor editor = NewGreet();
        editor.RemoveClipBlock("greet", 1, 0);
        Assert.Empty(Greet(editor).Tracks[1].Blocks);

        editor.RemoveClipTrack("greet", 1);
        Assert.Single(Greet(editor).Tracks);

        editor.RenameClip("greet", "hello");
        Assert.Null(editor.Document.FindClip("greet"));
        editor.RemoveClip("hello");
        Assert.Empty(editor.Document.Clips);

        editor.Undo();
        Assert.Single(editor.Document.Clips);
    }

    [Fact]
    public void 이_블록만_따로는_모션을_복제하고_그_블록만_옮긴다()
    {
        MotionEditor editor = NewGreet();
        editor.AddClipBlock("greet", 0, "nod", 2f); // nod를 두 곳에서 쓴다
        Assert.Equal(2, ClipTiming.UsesOf(editor.Document, "nod"));
        Assert.Equal(1, ClipTiming.UsesOf(editor.Document, "bow"));

        Assert.Equal("nod_b", editor.MakeClipBlockUnique("greet", 1, 0, "nod_b"));
        Assert.Equal("nod_b", Greet(editor).Tracks[1].Blocks[0].Motion);
        Assert.Equal("nod", Greet(editor).Tracks[0].Blocks[1].Motion);
        Assert.Equal(new[] { "nod", "nod_b", "bow" }, editor.Document.Motions.Select(m => m.Name)); // 원본 바로 뒤
        MotionAsset copy = editor.Document.Find("nod_b")!;
        Assert.NotSame(editor.Document.Find("nod"), copy);
        Assert.Equal((0.5f, MotionEnd.Restore, "character"), (copy.Duration, copy.End, copy.Rig));
        Assert.Equal((1, 1), (ClipTiming.UsesOf(editor.Document, "nod"), ClipTiming.UsesOf(editor.Document, "nod_b")));

        long revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.MakeClipBlockUnique("greet", 1, 0, "bow"));
        Assert.Throws<InvalidOperationException>(() => editor.MakeClipBlockUnique("greet", 1, 0, "Bad"));
        Assert.Equal(revision, editor.Revision);

        editor.Undo();
        Assert.Null(editor.Document.Find("nod_b"));
        Assert.Equal("nod", Greet(editor).Tracks[1].Blocks[0].Motion);
    }
}
