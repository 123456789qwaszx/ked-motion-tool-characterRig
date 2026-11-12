using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.App.Tests;

/// <summary>트윈머신(PLAN-tween.md T1 · T4) — 이모지와 캐릭터 모션.</summary>
public class TweenShellTests
{
    private static void Run(Action<MainWindow> body) => EmojiExampleWindow.Run(body);

    [Fact]
    public void 트윈머신_창은_고른_것과_지금_프레임을_따라가고_끌면_보이고_놓으면_한_칸이다()
    {
        Run(window =>
        {
            window.SelectEmoji("star");
            MotionEditor editor = window.Session.Editor;
            EmojiRigFrame frame = window.Session.EmojiFrame!;
            var scaleX = new Mt.Authoring.Graph.CurveRef(frame.ImageNodeOf("body"), ChannelKind.Scale, Axis.X);
            KeyAsset[] keys = scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys.ToArray();
            (KeyAsset a, KeyAsset b) = (keys[0], keys[1]);
            int between = (EmojiAsset.FrameOf(a.T) + EmojiAsset.FrameOf(b.T)) / 2;
            Assert.True(between > EmojiAsset.FrameOf(a.T) && between < EmojiAsset.FrameOf(b.T), "예시의 첫 두 키 사이에 프레임이 있어야 한다");

            window.OpenTweenFromForm();
            Assert.True(window.TweenOpen);

            // 그림을 고르고 두 키 사이로 간다 — 창이 무엇에, 어느 키 사이에 놓이는지 적는다.
            window.SelectEmojiRigFromForm("body");
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(between));
            Assert.StartsWith($"body  |  {between}fr에 — 앞 키 ", window.TweenNote);

            // 끈다 — 문서가 따라오지만(뷰포트 · 그래프에 보인다) 기록은 아직 없다.
            long before = editor.Revision;
            window.DragTweenFromForm(20);
            window.DragTweenFromForm(80);
            Assert.Equal(before, editor.Revision);
            float shown = scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys.Single(k => EmojiAsset.FrameOf(k.T) == between).V;
            Assert.Equal(a.V + (b.V - a.V) * 0.8f, shown, 3);
            Assert.Equal(EmojiAsset.SecondsOf(between), window.Viewport.Clock.Time, 4); // 보고 있던 프레임 그대로

            // 놓는다 — 한 칸. 되돌리면 끌기 전으로.
            window.ReleaseTweenFromForm();
            Assert.Equal(before + 1, editor.Revision);
            editor.Undo();
            Assert.DoesNotContain(scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys, k => EmojiAsset.FrameOf(k.T) == between);

            // 값 단추 — 바로 한 칸. 고른 그림의 것만 건드린다(이모지 전체의 채널은 그대로).
            string wholeBefore = string.Join(",", editor.Document.FindEmoji("star")!.Timeline.Channels.Where(c => c.Node == frame.OffsetNode).SelectMany(c => c.X?.Keys ?? new List<KeyAsset>()).Select(k => k.T));
            before = editor.Revision;
            window.PressTweenButtonFromForm(50);
            Assert.Equal(before + 1, editor.Revision);
            Assert.Equal((a.V + b.V) / 2, scaleX.Resolve(editor.Document.FindEmoji("star")!.Timeline)!.Keys.Single(k => EmojiAsset.FrameOf(k.T) == between).V, 3);
            Assert.Equal(wholeBefore, string.Join(",", editor.Document.FindEmoji("star")!.Timeline.Channels.Where(c => c.Node == frame.OffsetNode).SelectMany(c => c.X?.Keys ?? new List<KeyAsset>()).Select(k => k.T)));
            Assert.Equal(50, window.TweenSliderState.Value);

            // 오버슈트 — 범위가 넓어진다.
            Assert.Equal((0d, 100d), (window.TweenSliderState.Min, window.TweenSliderState.Max));
            window.SetTweenOvershootFromForm(true);
            Assert.Equal((-50d, 150d), (window.TweenSliderState.Min, window.TweenSliderState.Max));

            // 첫 프레임 — 앞에 키가 없다. 놓일 곳이 없다고 적고 아무것도 하지 않는다.
            window.Viewport.ScrubTo(0);
            Assert.Contains("앞과 뒤에 키가 있는 곡선이 없다", window.TweenNote);
            before = editor.Revision;
            window.PressTweenButtonFromForm(50);
            Assert.Equal(before, editor.Revision);

            window.CloseTweenFromForm();
            Assert.False(window.TweenOpen);
        });
    }

    [Fact]
    public void 트윈머신은_캐릭터_모션에도_먹고_키_없는_포즈는_버린다()
    {
        HeadlessUi.Run(() =>
        {
            const string moveY = "CharacterPortrait_Track_Move_Y";
            string settings = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, "{}");
            var window = new MainWindow(settings) { Width = 1600, Height = 960 };
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                MotionEditor editor = window.Session.Editor;
                editor.AddMotion("nod", "character", 1f);
                editor.AddChannel("nod", moveY, ChannelKind.Position);
                editor.SetKeys("nod", moveY, ChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(1f, -40f) });
                window.SelectMotion("nod");
                window.OpenTweenFromForm();

                // 대상은 마야처럼 고른 컨트롤러를 따라간다 — 몸을 고르면 몸의 곡선만이다(숙임에도 키가 있지만 세지 않는다).
                editor.AddChannel("nod", "CharacterPortrait_Track_Sway", ChannelKind.Euler);
                editor.SetKeys("nod", "CharacterPortrait_Track_Sway", ChannelKind.Euler, Axis.Z, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(1f, 10f) });
                window.Viewport.ScrubTo(0.5);
                window.Viewport.SelectedNode = moveY;
                window.CloseTweenFromForm();
                window.OpenTweenFromForm();
                Assert.StartsWith("모션 nod · 몸  |  12fr에 — 앞 키 0fr ↔ 뒤 키 24fr  |  곡선 1개", window.TweenNote);

                // "모션 전체에"를 켜면 모션의 곡선 전부 — 몸과 숙임 둘 다.
                long whole = editor.Revision;
                window.SetTweenWholeFromForm(true);
                Assert.StartsWith("모션 nod · 전체  |  12fr에 — 앞 키 0fr ↔ 뒤 키 24fr  |  곡선 2개", window.TweenNote);
                window.PressTweenButtonFromForm(50);
                Assert.Equal(whole + 1, editor.Revision);
                Assert.Equal(5f, editor.Document.Find("nod")!.Channels[1].Z!.Keys[1].V, 3);
                editor.Undo();

                // 끄면 고른 컨트롤러(몸)의 것만 — 숙임은 그대로다.
                window.SetTweenWholeFromForm(false);

                // 키 없는 포즈를 들고 있다가 트윈을 놓으면 — 포즈는 버려지고 그 프레임의 키가 포즈다. 한 칸.
                window.Viewport.SetComponent(moveY, ChannelKind.Position, Axis.Y, -5f);
                Assert.True(window.Viewport.HasPending);
                long before = editor.Revision;
                window.DragTweenFromForm(25);
                window.DragTweenFromForm(75);
                Assert.Equal(before, editor.Revision);
                Assert.False(window.Viewport.HasPending);
                window.ReleaseTweenFromForm();
                Assert.Equal(before + 1, editor.Revision);
                Assert.Equal(new[] { (0f, 0f), (0.5f, -30f), (1f, -40f) }, editor.Document.Find("nod")!.Channels[0].Y!.Keys.Select(k => (k.T, k.V)));
                Assert.Equal(2, editor.Document.Find("nod")!.Channels[1].Z!.Keys.Count); // 고르지 않은 숙임에는 키가 놓이지 않았다

                // 되돌리면 키가 사라지고 들고 있던 포즈가 돌아온다.
                editor.Undo();
                Assert.Equal(2, editor.Document.Find("nod")!.Channels[0].Y!.Keys.Count);
                Assert.True(window.Viewport.HasPending);

                // 값 단추도 같다 — 한 칸.
                before = editor.Revision;
                window.PressTweenButtonFromForm(50);
                Assert.Equal(before + 1, editor.Revision);
                Assert.Equal(-20f, editor.Document.Find("nod")!.Channels[0].Y!.Keys[1].V, 3);
                Assert.False(window.Viewport.HasPending);

                // 모션을 놓으면(아무것도 고르지 않았다) 쓸 곳이 없다.
                window.SelectMotion(null!);
                Assert.StartsWith("모션을 고른다", window.TweenNote);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
