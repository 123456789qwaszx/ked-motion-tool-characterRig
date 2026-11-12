using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

/// <summary>트윈머신(PLAN-tween.md T1) — 앞 키와 뒤 키 사이의 값으로 키를 놓는다. 끄는 조작은 끝까지가 한 칸이다.</summary>
public class TweenTests
{
    private const string Node = "CharSlot_Translate";

    private static (MotionEditor Editor, CurveRef X, CurveRef Y) Two()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("m", "character", 2f);
        editor.AddChannel("m", Node, ChannelKind.Position);
        editor.SetKeys("m", Node, ChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(1f, 100f) });
        editor.SetKeys("m", Node, ChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 10f) }); // 뒤에 키가 없다
        return (editor, new CurveRef(Node, ChannelKind.Position, Axis.X), new CurveRef(Node, ChannelKind.Position, Axis.Y));
    }

    private static float At(MotionEditor editor, CurveRef curve, float time)
        => curve.Resolve(editor.Document.Find("m")!)!.Keys.Single(k => Math.Abs(k.T - time) < 1e-4).V;

    [Fact]
    public void 앞_키와_뒤_키_사이의_비율로_키를_놓고_한_칸이다()
    {
        (MotionEditor editor, CurveRef x, CurveRef y) = Two();
        long before = editor.Revision;

        List<CurveRef> done = editor.TweenKeys("m", new[] { x, y }, 0.5f, 0.25, TangentMode.Auto);

        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(new[] { x }, done);          // 뒤에 키가 없는 곡선은 건너뛴다
        Assert.Equal(25f, At(editor, x, 0.5f), 3); // 시간의 한가운데여도 값은 앞 키 쪽 25%
        Assert.Single(y.Resolve(editor.Document.Find("m")!)!.Keys);

        // 그 시각에 키가 있으면 값만 바뀐다 — 키가 늘지 않는다.
        editor.TweenKeys("m", new[] { x }, 0.5f, 0.75, TangentMode.Auto);
        Assert.Equal(75f, At(editor, x, 0.5f), 3);
        Assert.Equal(3, x.Resolve(editor.Document.Find("m")!)!.Keys.Count);

        // 오버슈트 — 뒤 키를 지나서, 앞 키보다 더 뒤로.
        editor.TweenKeys("m", new[] { x }, 0.5f, 1.2, TangentMode.Auto);
        Assert.Equal(120f, At(editor, x, 0.5f), 3);
        editor.TweenKeys("m", new[] { x }, 0.5f, -0.2, TangentMode.Auto);
        Assert.Equal(-20f, At(editor, x, 0.5f), 3);
    }

    [Fact]
    public void 키가_있는_시각에서는_그_키의_앞과_뒤를_본다()
    {
        (MotionEditor editor, CurveRef x, _) = Two();
        editor.TweenKeys("m", new[] { x }, 0.5f, 0.5, TangentMode.Auto);

        // 0.5초의 키는 세지 않는다 — 앞은 0초, 뒤는 1초.
        Assert.True(TweenMath.TryNeighbors(x.Resolve(editor.Document.Find("m")!)!.Keys, 0.5f, out KeyAsset? a, out KeyAsset? b));
        Assert.Equal((0f, 1f), (a!.T, b!.T));

        // 첫 키 · 마지막 키의 시각에서는 한쪽이 없다 — 놓지 않는다(같은 값이라 기록도 없다).
        long before = editor.Revision;
        Assert.Empty(editor.TweenKeys("m", new[] { x }, 0f, 0.5, TangentMode.Auto));
        Assert.Empty(editor.TweenKeys("m", new[] { x }, 1f, 0.5, TangentMode.Auto));
        Assert.Equal(before, editor.Revision);
    }

    [Fact]
    public void 앞_키가_계단이면_새_키도_계단이다()
    {
        (MotionEditor editor, CurveRef x, _) = Two();
        editor.SetTangentMode("m", new[] { new KeyRef(x, 0), new KeyRef(x, 1) }, TangentMode.Step);

        editor.TweenKeys("m", new[] { x }, 0.5f, 0.5, TangentMode.Auto);

        Assert.Equal(TangentMode.Step, x.Resolve(editor.Document.Find("m")!)!.Keys[1].Tangent);
    }

    [Fact]
    public void 끄는_조작은_끄는_동안_문서를_고치고_놓으면_한_칸이다()
    {
        (MotionEditor editor, CurveRef x, _) = Two();
        long before = editor.Revision;
        int notified = 0;
        editor.Changed += (_, _) => notified++;

        editor.BeginGesture(MotionChangeKind.Keys);
        Assert.True(editor.InGesture);

        foreach (double bias in new[] { 0.1, 0.4, 0.9 })
        {
            editor.TweenKeys("m", new[] { x }, 0.5f, bias, TangentMode.Auto);
            editor.GestureChanged();
            Assert.Equal((float)(bias * 100), At(editor, x, 0.5f), 3); // 끄는 동안 문서가 따라온다
        }

        Assert.Equal(before, editor.Revision); // 끄는 동안은 기록이 없다
        Assert.Equal(3, notified);             // 화면은 따라오게 알린다

        editor.EndGesture();
        Assert.False(editor.InGesture);
        Assert.Equal(before + 1, editor.Revision);

        // 되돌리기 한 번에 끌기 전으로.
        editor.Undo();
        Assert.Equal(2, x.Resolve(editor.Document.Find("m")!)!.Keys.Count);
        editor.Redo();
        Assert.Equal(90f, At(editor, x, 0.5f), 3);
    }

    [Fact]
    public void 끄는_조작은_바뀐_것이_없으면_흔적이_없고_버리면_돌아온다()
    {
        (MotionEditor editor, CurveRef x, _) = Two();
        long before = editor.Revision;

        editor.BeginGesture(MotionChangeKind.Keys);
        editor.EndGesture();
        Assert.Equal(before, editor.Revision);
        Assert.False(editor.CanUndo && editor.Revision != before);

        editor.BeginGesture(MotionChangeKind.Keys);
        editor.TweenKeys("m", new[] { x }, 0.5f, 0.5, TangentMode.Auto);
        editor.CancelGesture();
        Assert.False(editor.InGesture);
        Assert.Equal(2, x.Resolve(editor.Document.Find("m")!)!.Keys.Count);

        // 열어 둔 채 되돌리기를 누르면 먼저 한 칸으로 닫고 그 칸을 되돌린다. 둘을 겹쳐 열지 않는다.
        editor.BeginGesture(MotionChangeKind.Keys);
        Assert.Throws<InvalidOperationException>(() => editor.BeginGesture(MotionChangeKind.Keys));
        editor.TweenKeys("m", new[] { x }, 0.5f, 0.5, TangentMode.Auto);
        editor.Undo();
        Assert.False(editor.InGesture);
        Assert.Equal(2, x.Resolve(editor.Document.Find("m")!)!.Keys.Count);
    }
}
