using Ked.Presentation.Core;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.App.Tests;

/// <summary>이모지의 셸(PLAN-emoji.md E6) — 목록 · 탭의 칸 · 지금 키 · 구간과 잇기 · 그림 넘기기 · 뷰포트(캐릭터의 발에 선다 · 기본 자리 끌기 · 수명 재생).</summary>
public class EmojiShellTests
{
    private static MainWindow NewWindow()
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new(path);
    }

    private static void Run(Action<MainWindow> body)
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(TestDocuments.FixturePath("rig-schemas.json"));
                body(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지를_더하고_고르면_그래프와_뷰포트가_그_타임라인을_본다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            editor.AddMotion("nod", "character", 0.6f);
            window.SelectMotion("nod");

            string name = window.AddEmojiFromForm();

            Assert.Equal("emoji", name);
            Assert.Equal("emoji", window.SelectedEmoji);
            Assert.Equal(new[] { "body" }, window.EmojiLists.Rigs);
            Assert.Null(window.SelectedMotion); // 시간의 주인은 하나
            Assert.Equal("emoji", window.Viewport.EmojiName);
            Assert.Equal("emoji:emoji", window.Graph.MotionName);
            Assert.Equal(1.0, window.Viewport.Clock.Duration, 6);
            Assert.True(window.EmojiFormState.Enabled);
            Assert.Equal("24", window.EmojiFormState.Frames);

            // 이모지 추가는 한 칸이다(이모지 + 리그 body).
            editor.Undo();
            Assert.Empty(editor.Document.Emojis);
            Assert.Null(window.Viewport.EmojiName);

            // 모션을 고르면 이모지가 풀린다.
            editor.Redo();
            window.SelectEmoji("emoji");
            window.SelectMotion("nod");
            Assert.Null(window.SelectedEmoji);
            Assert.Null(window.Viewport.EmojiName);
            Assert.Equal("nod", window.Graph.MotionName);
        });
    }

    [Fact]
    public void 탭의_칸은_각각_한_칸이고_이름이_바뀌면_따라온다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            long before = editor.Revision;

            window.CommitEmojiField("name", "star");
            window.CommitEmojiField("offsetX", "170");
            window.CommitEmojiField("frames", "12");
            window.SelectEmojiRigFromForm("body");
            window.CommitEmojiField("rigImages", "art/star.png\nart/star_2.png");
            window.CommitEmojiField("rigScale", "0.45");
            window.CommitEmojiField("rigName", "core");

            Assert.Equal(before + 6, editor.Revision);
            EmojiAsset star = editor.Document.FindEmoji("star")!;
            Assert.Equal("star", window.SelectedEmoji);
            Assert.Equal("emoji:star", window.Graph.MotionName);
            Assert.Equal(170f, star.OffsetX);
            Assert.Equal(12, star.LastFrame);
            Assert.Equal(new[] { "art/star.png", "art/star_2.png" }, star.Rigs[0].Images);
            Assert.Equal(0.45f, star.Rigs[0].Scale);
            Assert.Equal("core", star.Rigs[0].Name);
            Assert.Equal(new[] { "core" }, window.EmojiLists.Rigs);

            // 못 읽는 수는 무시하고 칸이 문서의 값으로 돌아간다.
            window.CommitEmojiField("rigScale", "big");
            Assert.Equal(before + 6, editor.Revision);
            Assert.Equal("0.45", window.EmojiFormState.RigScale);
        });
    }

    [Fact]
    public void 지금_키와_그림_넘기기와_계단이_타임라인에_키를_놓는다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");
            window.CommitEmojiField("rigImages", "a.png\nb.png\nc.png");

            window.Viewport.ScrubTo(0.25);
            window.KeyEmojiNowFromForm("body", ChannelKind.Scale);
            window.KeyEmojiNowFromForm(null, ChannelKind.Alpha);

            MotionAsset timeline = editor.Document.Find("emoji:emoji")!;
            ChannelAsset scale = timeline.FindChannel("body/EmojiRig_Image", ChannelKind.Scale)!;
            Assert.Equal(0.25f, scale.X!.Keys.Single().T, 4);
            Assert.Equal(1f, scale.X.Keys.Single().V); // 지금 값 = 중립
            Assert.NotNull(timeline.FindChannel("Emoji_Offset", ChannelKind.Alpha));

            // 그림은 리그에만 — 이모지 전체에는 막힌다(상태 줄).
            window.KeyEmojiNowFromForm(null, ChannelKind.Image);
            Assert.Null(timeline.FindChannel("Emoji_Offset", ChannelKind.Image));
            Assert.Contains("그림 바꾸기는 그림 파일이 있는 그림에만", window.Session.Status);

            window.SelectEmojiRigFromForm("body"); // 키 놓기의 대상은 고른 것을 따른다 — 그림 넘기기는 그림의 칸이다
            long before = editor.Revision;
            window.LayFlipsFromForm(0, 3, 5);
            Assert.Equal(before + 1, editor.Revision);
            ComponentCurve flips = timeline.FindChannel("body/EmojiRig_Image", ChannelKind.Image)!.X!;
            Assert.Equal(new[] { 0f, 1f, 2f, 0f, 1f }, flips.Keys.Select(k => k.V));
            Assert.Equal(new[] { 0, 3, 6, 9, 12 }, flips.Keys.Select(k => EmojiAsset.FrameOf(k.T)));
            Assert.All(flips.Keys, k => Assert.Equal(TangentMode.Step, k.Tangent));

            // 뷰포트는 그 프레임의 그림을 보인다(6fr → c).
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(7));
            Assert.Equal("c", window.Viewport.EmojiNow()!.Rigs[0].Sprite);
        });
    }

    [Fact]
    public void 구간과_잇기가_종류를_정하고_타임_슬라이더에_띠가_선다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();

            window.Viewport.ScrubTo(0);
            window.AddEmojiSpanFromForm();
            window.CommitEmojiField("spanName", "enter");
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(6));
            window.AddEmojiSpanFromForm();
            window.CommitEmojiField("spanName", "loop");
            window.CommitEmojiField("spanEnd", "20");

            Assert.Equal(new[] { "enter 0~5", "loop 6~20" }, window.EmojiLists.Spans);
            Assert.False(editor.Document.FindEmoji("emoji")!.Stays);

            window.LinkEmojiFromForm("enter", "loop", null);
            EmojiAsset emoji = editor.Document.FindEmoji("emoji")!;
            Assert.Equal("loop", emoji.Hold);
            Assert.Equal("enter", emoji.In);
            Assert.True(emoji.Stays);
            // 타임라인 보기의 구간은 수명의 띠가 보인다 — 타임 슬라이더에는 깔지 않는다. 숫자 칸은 접혀 있다.
            Assert.Empty(window.TimeSliderMarks);
            Assert.False(window.EmojiSpanSectionOpen);

            // 수명 재생 — 시계는 등장 6fr + 머묾 15fr × 2바퀴, 띠는 단계.
            window.SetEmojiLifeFromForm(true);
            Assert.True(window.Viewport.EmojiLife);
            Assert.Equal((6 + 15 * 2) / 24.0, window.Viewport.Clock.Duration, 6);
            Assert.Equal(new[] { "등장", "머묾 × 2" }, window.TimeSliderMarks.Select(m => m.Label));

            // 머묾 안의 한 순간 — 그래프는 그 프레임이 잘려 나온 타임라인의 자리를 따라간다.
            window.Viewport.ScrubTo(EmojiAsset.SecondsOf(6 + 2));
            Assert.Equal(EmojiPhase.Hold, window.Viewport.EmojiCurrentMoment()!.Value.Phase);
            Assert.Equal(EmojiAsset.SecondsOf(6 + 2), window.Graph.CurrentTime, 4);

            // 그래프에서 시각을 옮기면 타임라인 보기로 돌아간다.
            window.Graph.CurrentTime = 0.1;
            Assert.False(window.Viewport.EmojiLife);
        });
    }

    [Fact]
    public void 뷰포트는_캐릭터의_발에_이모지를_세우고_그림을_끌면_기본_자리가_한_칸으로_바뀐다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            window.CommitEmojiField("offsetY", "300");

            EmojiFrameView now = window.Viewport.EmojiNow()!;
            Assert.True(now.Tree.Contains("emoji/Emoji_Offset"));
            Assert.True(now.Visible);
            Assert.Contains("s1의 발", now.Label);

            // 캐릭터의 CharSlot_Scale 밑에 섰다 — 로케이터(덤프의 __root)의 부모가 그 노드다.
            Assert.Equal("s1/CharSlot_Scale", now.Tree.GetParentKey("emoji/__root"));

            Vec2 center = window.Viewport.EmojiRigCenter("body")!.Value;
            long before = editor.Revision;
            Assert.True(window.Viewport.DragEmojiRigForTest(center, center + new Vec2(40f, 0f)));

            Assert.Equal(before + 1, editor.Revision);
            EmojiRigAsset body = editor.Document.FindEmoji("emoji")!.Rigs[0];
            Assert.True(body.PlaceX > 0f, $"기본 자리 X {body.PlaceX}");
            Assert.Equal(0f, body.PlaceY, 0);
            Assert.Equal("body", window.Viewport.SelectedEmojiRig);
        });
    }

    [Fact]
    public void 이모지를_고르면_보기가_그림에_맞고_놓으면_무대로_돌아온다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            editor.AddMotion("nod", "character", 0.6f);
            window.AddEmojiFromForm();
            ViewportView viewport = window.Viewport;
            Assert.True(viewport.Bounds.Width > 0 && viewport.Bounds.Height > 0);

            // 맞춘 상자는 뷰포트 가운데에 있고 짧은 변 쪽의 절반을 채운다.
            Assert.True(viewport.FrameEmoji());
            Avalonia.Rect box = viewport.EmojiControlBox()!.Value;
            Assert.Equal(viewport.Bounds.Width / 2, box.Center.X, 1);
            Assert.Equal(viewport.Bounds.Height / 2, box.Center.Y, 1);
            double fill = Math.Max(box.Width / viewport.Bounds.Width, box.Height / viewport.Bounds.Height);
            Assert.True(fill > 0.49 && fill < 0.51 || viewport.ViewZoom == ViewportView.MaxViewZoom, $"채움 {fill}, 배율 {viewport.ViewZoom}");

            // 보기만 바뀐다 — 되돌리기 칸이 없다.
            long revision = editor.Revision;
            viewport.FrameEmoji();
            Assert.Equal(revision, editor.Revision);

            // 모션을 고르면(이모지를 놓으면) 무대 맞춤으로 돌아온다.
            window.SelectMotion("nod");
            Assert.Equal(1, viewport.ViewZoom, 6);
            Assert.False(viewport.FrameEmoji());
        });
    }

    [Fact]
    public void 그래프의_키_없음_줄은_키를_받는_노드와_이모지의_종류만_낸다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            string image = "body/EmojiRig_Image";

            var rows = window.UnkeyedRows;
            Assert.Contains(("Emoji_Offset", ChannelKind.Alpha), rows);
            Assert.Contains((image, ChannelKind.Position), rows);
            Assert.Contains((image, ChannelKind.Alpha), rows);
            Assert.DoesNotContain(rows, r => r.Node.Contains("Place") || r.Kind == ChannelKind.Image); // 기본 자리는 키를 안 받고, 그림이 없으면 그림 바꾸기도 없다
            Assert.DoesNotContain(rows, r => r.Node == "Emoji_Offset" && r.Kind == ChannelKind.Image);

            editor.SetEmojiRigImages("emoji", "body", new[] { "art/a.png", "art/b.png" });
            Assert.Contains((image, ChannelKind.Image), window.UnkeyedRows);

            // ＋는 지금 값에 키 — 한 칸, 그 줄은 키 있는 채널로 옮겨 간다.
            long before = editor.Revision;
            window.KeyUnkeyedRow(image, ChannelKind.Alpha);
            Assert.Equal(before + 1, editor.Revision);
            Assert.NotNull(editor.Document.Find("emoji:emoji")!.FindChannel(image, ChannelKind.Alpha));
            Assert.DoesNotContain((image, ChannelKind.Alpha), window.UnkeyedRows);
        });
    }

    [Fact]
    public void 왼쪽을_보는_인물로_보기는_보기만_바꾸고_거울에_비추며_끌기를_막는다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            editor.SetEmojiOffset("emoji", 120f, 300f);
            ViewportView viewport = window.Viewport;

            EmojiFrameView right = viewport.EmojiNow()!;
            string locator = right.Tree.GetParentKey("emoji/Emoji_Offset");
            float axis = right.Tree.TransformPoint(locator, Vec3.Zero).X;
            Vec2 before = viewport.EmojiRigCenter("body")!.Value;

            long revision = editor.Revision;
            viewport.EmojiFacingLeft = true;
            Assert.Equal(revision, editor.Revision); // 보기의 상태 — 문서에 적지 않는다

            EmojiFrameView left = viewport.EmojiNow()!;
            Assert.Equal(-1f, left.Tree.GetState(locator).LocalScale.X, 4);
            Assert.Contains("왼쪽을 보는 인물", left.Label);
            Vec2 mirrored = viewport.EmojiRigCenter("body")!.Value;
            Assert.Equal(axis - (before.X - axis), mirrored.X, 1); // 로케이터의 세로축을 두고 반대쪽
            Assert.Equal(before.Y, mirrored.Y, 1);

            // 거울 속에서는 그림을 끌어도 기본 자리가 바뀌지 않는다(고르기만 한다).
            viewport.DragEmojiRigForTest(mirrored, mirrored + new Vec2(40f, 0f));
            Assert.Equal(revision, editor.Revision);
            Assert.Equal("body", viewport.SelectedEmojiRig);

            // 방향을 따르지 않는 이모지는 뒤집힌 인물에게도 만든 쪽에 선다.
            editor.SetEmojiFixedSide("emoji", true);
            Vec2 fixedSide = viewport.EmojiRigCenter("body")!.Value;
            Assert.Equal(before.X, fixedSide.X, 1);

            viewport.EmojiFacingLeft = false;
            editor.SetEmojiFixedSide("emoji", false);
            Assert.Equal(before.X, viewport.EmojiRigCenter("body")!.Value.X, 1);
        });
    }

    [Fact]
    public void 좌우의_두_칸은_한_칸씩이고_방향을_따르지_않으면_upright가_흐려진다()
    {
        Run(window =>
        {
            MotionEditor editor = window.Session.Editor;
            window.AddEmojiFromForm();
            window.SelectEmojiRigFromForm("body");
            Assert.Equal((false, false, false, false, false), window.EmojiSideFormState);

            long before = editor.Revision;
            window.SetEmojiRigUprightFromForm(true);
            Assert.Equal(before + 1, editor.Revision);
            Assert.True(editor.Document.FindEmoji("emoji")!.FindRig("body")!.Upright);

            window.SetEmojiFixedSideFromForm(true);
            Assert.Equal(before + 2, editor.Revision);
            Assert.True(editor.Document.FindEmoji("emoji")!.FixedSide);
            Assert.Equal((true, true, true, true, false), window.EmojiSideFormState); // 막지 않고 흐리게 · 한 줄 알림

            // 되돌리면 칸이 따라온다.
            editor.Undo();
            Assert.Equal((false, true, false, false, false), window.EmojiSideFormState);
            editor.Undo();
            Assert.False(window.EmojiSideFormState.Upright);
            Assert.False(editor.Document.FindEmoji("emoji")!.FindRig("body")!.Upright);

            // 보기의 칸 — 뷰포트만 바뀐다.
            long revision = editor.Revision;
            window.SetEmojiFacingLeftFromForm(true);
            Assert.True(window.Viewport.EmojiFacingLeft);
            Assert.Equal(revision, editor.Revision);
            window.SetEmojiFacingLeftFromForm(false);
            Assert.False(window.Viewport.EmojiFacingLeft);
        });
    }

    [Fact]
    public void 이모지의_문제는_문제_칸에_나온다()
    {
        Run(window =>
        {
            window.AddEmojiFromForm(); // 리그 body에 그림이 없다
            Assert.Contains(window.Session.EmojiProblems, p => p.Message.Contains("그림 파일이 없다"));
            Assert.False(window.Session.ExportEmojis(Path.Combine(Path.GetTempPath(), $"emojis-{Guid.NewGuid():N}.json")));
            Assert.Contains("이모지 내보내기 막힘", window.Session.Status);
        });
    }
}
