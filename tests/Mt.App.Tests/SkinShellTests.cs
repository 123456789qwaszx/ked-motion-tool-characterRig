using Avalonia;
using Mt.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App.Tests;

/// <summary>스킨과 조각의 화면(<c>PLAN-parts.md</c> P1) — 조각마다 그림을 주면 그 조각만 그려진다.</summary>
public class SkinShellTests
{
    private static MainWindow NewWindow()
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new(path);
    }

    [Fact]
    public void 조각이_있는_리그는_스킨이_그림을_준_조각만_그리고_캐릭터는_전과_같다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);
                window.InspectorTab = 1; // 씬 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                SceneObject Emoji() => editor.Document.ActiveSceneAsset!.Find("e1")!;

                // 캐릭터를 고르면 스킨 칸이 없다 — 초상 한 장의 리그다.
                window.Viewport.SelectedObjectId = "s1";
                Assert.False(window.SkinFormState.Visible);
                Assert.NotNull(window.Viewport.CurrentPortraitQuad());
                Quad character = window.Viewport.ObjectQuad("s1")!.Value;

                // 이모지를 고르면 스킨 칸이 선다. 스킨이 없으면 조각은 하나도 안 그려진다.
                window.Viewport.SelectedObjectId = "e1";
                Assert.Equal((true, (string?)null, 0), window.SkinFormState);
                Assert.Empty(window.Viewport.DrawnParts(Emoji()));

                // 스킨 추가 — 만들어서 입힌다, 한 칸. 조각 줄 넷이 선다.
                long revision = editor.Revision;
                Assert.Equal("skin", window.AddSkinFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("skin", Emoji().Skin);
                Assert.Equal((true, "skin", 4), window.SkinFormState);
                Assert.Empty(window.Viewport.DrawnParts(Emoji())); // 그림을 준 조각이 아직 없다

                // 조각 둘에 그림(경로 칸에 넣고 Enter) — 그 둘만 그려진다. 파일이 없어도 자리표시로 그린다.
                TextBox part1 = window.SkinPartBox("Emoji_Part1")!;
                part1.Focus();
                part1.Text = "art/flower-big.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                editor.SetSkinPart("skin", "Emoji_Part3", "art/flower-small.png");
                Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part3" }, window.Viewport.DrawnParts(Emoji()));
                Assert.Equal("art/flower-big.png", editor.Document.FindSkin("skin")!.Parts["Emoji_Part1"]);
                Assert.Equal("art/flower-big.png", window.SkinPartBox("Emoji_Part1")!.Text);

                // 조각은 노드의 rect(160×160)로, 무대 가운데에 선다.
                Quad quad = window.Viewport.PartQuad("e1", "Emoji_Part1")!.Value;
                Assert.Equal(EmojiRigFixture.PartSize, quad.BottomRight.X - quad.BottomLeft.X, 2);
                Assert.Equal(EmojiRigFixture.PartSize, quad.TopLeft.Y - quad.BottomLeft.Y, 2);
                Assert.Equal(0f, quad.Center.X, 2);
                Assert.Null(window.Viewport.PartQuad("e1", "Nope"));

                // 조각마다 따로 움직인다 — 모션은 그 리그의 보통 모션이다.
                editor.AddMotion("float", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("float", "Emoji_Part1", MotionChannelKind.Position);
                editor.SetKeys("float", "Emoji_Part1", MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(1f, 100f) });
                window.SelectMotion("float");
                window.Viewport.SelectedObjectId = "e1";
                window.Viewport.ScrubTo(1.0);
                Assert.Equal(100f, window.Viewport.PartQuad("e1", "Emoji_Part1")!.Value.Center.X, 2);
                Assert.Equal(0f, window.Viewport.PartQuad("e1", "Emoji_Part3")!.Value.Center.X, 2);
                Assert.Equal(0, window.ProblemRowsShown); // 이모지 리그의 노드로 검증된다

                // 고르는 자리(오브젝트 사각형)는 첫 조각이다. 캐릭터의 사각형은 그대로다.
                Assert.Equal(100f, window.Viewport.ObjectQuad("e1")!.Value.Center.X, 2);
                Assert.Equal(character, window.Viewport.ObjectQuad("s1")!.Value);

                // 스킨을 벗기면 다시 안 그려지고, 스킨을 지우면 입은 오브젝트가 벗는다.
                editor.SetObjectSkin(null, "e1", null);
                Assert.Empty(window.Viewport.DrawnParts(Emoji()));
                editor.Undo();
                Assert.Equal(2, window.Viewport.DrawnParts(Emoji()).Count);
                editor.RemoveSkin("skin");
                Assert.Null(Emoji().Skin);
                Assert.Equal((true, (string?)null, 0), window.SkinFormState);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 붙는_리그를_만들면_부모_밑에_서서_따라가고_클립에서_캐릭터와_같이_돈다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                SceneAsset Scene() => editor.Document.ActiveSceneAsset!;
                Assert.Contains(EmojiRigFixture.RigKind, window.AttachableRigs);
                Assert.DoesNotContain("character", window.AttachableRigs);

                // 붙는 리그 만들기(하이어아키 메뉴와 같은 길) — 만들고 붙이는 것이 한 칸, 만든 것이 골라진다.
                long revision = editor.Revision;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                Assert.Equal("emoji1", id);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(("s1", SceneObject.DefaultAttachNode), (Scene().Find(id)!.AttachTo, Scene().Find(id)!.AttachNode));
                Assert.Equal(id, window.Viewport.SelectedObjectId);
                Assert.Equal(2, window.HierarchyObjectCount);
                Assert.True(window.SkinFormState.Visible);

                // 스킨을 입히고 조각 하나에 그림 — 캐릭터 머리 근처가 아니라 붙는 노드의 가운데에 선다(자리는 오프셋으로 옮긴다).
                window.AddSkinFromForm();
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                Quad rest = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;

                // 부모를 옮기면 따라간다 — 캐릭터의 그림이 간 만큼(깊이 배율이 곱해진 화면 거리) 그대로.
                float characterRest = window.Viewport.ObjectQuad("s1")!.Value.Center.X;
                editor.SetObjectPlacement(null, "s1", 150f, 0f, 0f, 1f, 1f);
                float characterMoved = window.Viewport.ObjectQuad("s1")!.Value.Center.X - characterRest;
                Assert.True(characterMoved > 100f);
                Assert.Equal(rest.Center.X + characterMoved, window.Viewport.PartQuad(id, "Emoji_Part1")!.Value.Center.X, 1);
                float depthScale = characterMoved / 150f; // 슬롯 계층의 깊이 배율 — 붙은 리그도 그 아래라 같이 곱해진다
                editor.Undo();

                // 이모지의 모션(튀어나옴)과 캐릭터의 모션(슬롯 이동)을 클립에서 같이 돌린다 — 배역은 리그를 가리지 않는다.
                editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.5f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.5f, 60f) });
                editor.AddMotion("step", "character", 0.5f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("step", "CharSlot_Track_X", MotionChannelKind.Position);
                editor.SetKeys("step", "CharSlot_Track_X", MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.5f, 100f) });

                window.AddClipFromForm();
                window.Viewport.SelectedObjectId = "s1";
                window.AddClipRoleFromForm();   // a = s1 (character)
                window.Viewport.SelectedObjectId = id;
                window.AddClipRoleFromForm();   // b = emoji1 (emoji)
                Assert.Equal(new[] { "character", EmojiRigFixture.RigKind }, editor.Document.FindClip("clip")!.Roles.Select(r => r.Rig));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Timeline.PlaceMotion(0, "step", 0);
                window.Timeline.PlaceMotion(1, "pop", 0);
                Assert.Equal(0, window.ProblemRowsShown);

                window.Viewport.ScrubTo(0.5);
                Quad end = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.Equal(rest.Center.X + 100f * depthScale, end.Center.X, 1); // 캐릭터가 간 만큼
                Assert.Equal(rest.Center.Y + 60f * depthScale, end.Center.Y, 1);  // 제 모션만큼(깊이 배율 아래)

                // 캐릭터 모션은 이모지 줄에 못 놓는다(리그가 다르다) — 문제 칸이 알린다.
                window.Timeline.PlaceMotion(1, "step", 1);
                Assert.Equal(1, window.ProblemRowsShown);
                editor.Undo();

                // 부모를 지우면 붙은 것도 같이 — 한 칸.
                window.SelectMotion("step");
                editor.RemoveSceneObject(null, "s1");
                Assert.Empty(Scene().Objects);
                editor.Undo();
                Assert.Equal(2, Scene().Objects.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 불투명도_칸에_값을_넣으면_알파_채널의_키가_되고_조각이_그만큼_흐려진다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.AddSkinFromForm();
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                editor.AddMotion("vanish", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit);
                window.SelectMotion("vanish");
                window.Viewport.SelectedObjectId = id;
                window.Viewport.SnapEnabled = false;
                window.InspectorTab = 2; // 모션 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 불투명도를 받을 수 없는 노드(Anchor)를 고르면 칸이 없다. 조각을 고르면 선다.
                window.Viewport.SelectedNode = "Emoji_Anchor";
                window.Viewport.ScrubTo(0.0);
                Assert.False(window.AlphaBox.Visible);
                window.Viewport.SelectedNode = "Emoji_Part1";
                window.Viewport.ScrubTo(0.0001);
                window.Viewport.ScrubTo(0.0);
                Assert.True(window.AlphaBox.Visible);
                Assert.Equal("1", window.AlphaBox.Box.Text);

                // 처음에 1, 끝에 0 — 값을 넣고 Enter가 그 시각의 키(한 칸). 없던 알파 채널이 생긴다.
                window.Viewport.SetAlphaValue("Emoji_Part1", 1f);
                window.Viewport.ScrubTo(1.0);
                long revision = editor.Revision;
                TextBox box = window.AlphaBox.Box;
                box.Focus();
                box.Text = "0";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                ChannelAsset channel = Assert.Single(editor.Document.Find("vanish")!.Channels);
                Assert.Equal(("Emoji_Part1", MotionChannelKind.Alpha), (channel.Node, channel.Kind));
                Assert.Equal(new[] { (0f, 1f), (1f, 0f) }, channel.X!.Keys.Select(k => (k.T, k.V)));
                Assert.Equal(0, window.ProblemRowsShown);

                // 뷰포트: 가운데에서 절반, 끝에서 0(안 그려진다). 다른 조각은 그대로 1.
                window.Viewport.ScrubTo(0.5);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 2);
                Assert.Equal(1f, window.Viewport.NodeOpacity(id, "Emoji_Part2")!.Value, 3);
                Assert.Equal("0.5", window.AlphaBox.Box.Text);
                window.Viewport.ScrubTo(1.0);
                Assert.Equal(0f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 3);

                // 묶음(Body)의 불투명도는 조각의 것과 곱해진다.
                window.Viewport.SelectedNode = "Emoji_Body";
                window.Viewport.ScrubTo(0.0);
                window.Viewport.SetAlphaValue("Emoji_Body", 0.5f);
                window.Viewport.ScrubTo(1.0);
                window.Viewport.SetAlphaValue("Emoji_Body", 0.5f);
                window.Viewport.ScrubTo(0.5);
                Assert.Equal(0.25f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 2);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity(id, "Emoji_Part2")!.Value, 2);

                // 범위 밖 값은 0~1로 죈다. 캐릭터의 노드(받을 수 없는 것)에는 안 놓인다.
                window.Viewport.SetAlphaValue("Emoji_Body", 7f);
                Assert.All(editor.Document.Find("vanish")!.FindChannel("Emoji_Body", MotionChannelKind.Alpha)!.X!.Keys, k => Assert.InRange(k.V, 0f, 1f));
                revision = editor.Revision;
                window.Viewport.SetAlphaValue("Emoji_Anchor", 0.3f);
                Assert.Equal(revision, editor.Revision);

                // 알파가 든 모션은 motions/3으로 나간다.
                string export = Path.Combine(Path.GetTempPath(), $"ked-motion-tool-{Guid.NewGuid():N}.json");

                try
                {
                    Assert.True(window.Session.Export(export), window.Session.Status);
                    string json = File.ReadAllText(export);
                    Assert.Contains("\"schema\": \"motions/3\"", json, StringComparison.Ordinal);
                    Assert.Contains("\"kind\": \"alpha\"", json, StringComparison.Ordinal);
                }
                finally
                {
                    File.Delete(export);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지_정의를_만들어_고르면_수명_한_번이_돌고_값을_고치면_바로_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.AddSkinFromForm();
                editor.RenameSkin("skin", "땀");
                editor.SetSkinPart("땀", "Emoji_Part1", "art/sweat.png");

                // 모션 셋: pop(0.2초, Body y → 40, 유지·절대값) · bob(0.4초, Body y 0 → 10 → 0, 복원) · fade(0.3초, Part1 알파 → 0, 유지).
                KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
                editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });
                editor.AddMotion("bob", EmojiRigFixture.RigKind, 0.4f);
                editor.AddChannel("bob", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("bob", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 10f), Linear(0.4f, 0f) });
                editor.AddMotion("fade", EmojiRigFixture.RigKind, 0.3f, MotionEnd.Commit);
                editor.AddChannel("fade", "Emoji_Part1", MotionChannelKind.Alpha);
                editor.SetKeys("fade", "Emoji_Part1", MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(0.3f, 0f) });
                window.SelectMotion("pop");
                window.Viewport.SelectedObjectId = id;
                window.InspectorTab = 4; // 이모지 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 이모지 추가 — 고른 오브젝트의 리그와 입은 스킨으로, 한 칸. 골라지고 모션 선택이 풀린다.
                long revision = editor.Revision;
                Assert.Equal("emoji", window.AddEmojiFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("emoji", window.SelectedEmoji);
                Assert.Null(window.SelectedMotion);
                Assert.Equal("emoji", window.Viewport.EmojiName);
                Assert.Equal(id, window.Viewport.EmojiActor?.Id);
                Assert.Equal(("emoji", "땀", (string?)null, (string?)null, (string?)null, "", true), window.EmojiFormState);

                // 값을 채운다 — 칸마다 한 칸. 시계가 수명(0.2 + 1 + 0.3)을 따라간다.
                window.UpdateSelectedEmoji(e => { e.Enter = "pop"; e.Hold = "bob"; e.Exit = "fade"; e.HoldSeconds = 1f; e.OffsetX = 120f; e.OffsetY = 300f; });
                Assert.Equal(1.5, window.Viewport.Clock.Duration, 4);
                Assert.Equal(("pop", "bob", "fade", "1"), (window.EmojiFormState.Enter, window.EmojiFormState.Hold, window.EmojiFormState.Exit, window.EmojiFormState.HoldSeconds));
                Assert.Equal(0, window.ProblemRowsShown);

                // 수명: 등장(0 → 40) · 머묾(40 위에서 되풀이) · 퇴장(흐려짐). 자리는 정의의 오프셋.
                float BodyY() => window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Body").AnchoredPosition.Y;
                window.Viewport.ScrubTo(0.1);
                Assert.Equal(20f, BodyY(), 2);
                window.Viewport.ScrubTo(0.4);
                Assert.Equal(50f, BodyY(), 2);
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(50f, BodyY(), 2);
                window.Viewport.ScrubTo(1.35);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 2);
                Assert.Equal(120f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.X, 2);
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));

                // 머무는 시간을 비우면 머무는 이모지 — 미리보기는 머묾을 2초 보인다.
                window.UpdateSelectedEmoji(e => e.HoldSeconds = null);
                Assert.Equal(2.5, window.Viewport.Clock.Duration, 4);
                Assert.Equal("", window.EmojiFormState.HoldSeconds);
                editor.Undo();
                Assert.Equal(1.5, window.Viewport.Clock.Duration, 4);

                // 정의의 스킨이 그림이다 — 오브젝트가 스킨을 벗어도 미리보기에서는 보인다.
                editor.SetObjectSkin(null, id, null);
                Assert.Single(window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));

                // 없는 모션을 가리키면 문제 칸이 알린다. 내보내기는 막힌다.
                window.UpdateSelectedEmoji(e => e.Enter = "ghost");
                Assert.Equal(1, window.ProblemRowsShown);
                string target = Path.Combine(Path.GetTempPath(), $"ked-emojis-{Guid.NewGuid():N}.json");
                Assert.False(window.Session.ExportEmojis(target));
                editor.Undo();
                Assert.True(window.Session.ExportEmojis(target), window.Session.Status);
                Assert.Contains("\"schema\": \"emojis/1-draft\"", File.ReadAllText(target), StringComparison.Ordinal);
                File.Delete(target);

                // 모션을 고르면 이모지가 풀리고(오브젝트는 다시 제 스킨 — 벗었으니 안 그려진다), 다시 고르면 돌아온다.
                window.SelectMotion("bob");
                Assert.Null(window.SelectedEmoji);
                Assert.Null(window.Viewport.EmojiName);
                Assert.Empty(window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));
                window.SelectEmoji("emoji");
                Assert.Null(window.SelectedMotion);
                Assert.Equal("emoji", window.Viewport.EmojiName);

                // 지우면 뷰포트가 풀린다.
                editor.RemoveEmoji("emoji");
                Assert.Null(window.Viewport.EmojiName);
                Assert.False(window.EmojiFormState.Enabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 덤프에_이모지_리그가_없어도_툴이_리그를_정의해_바로_이모지를_만든다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow(); // 내장 덤프 — character · screenEffect뿐
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                Assert.DoesNotContain(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);
                Assert.Contains(RigTemplates.EmojiKind, window.AttachableRigs); // 그래도 메뉴에는 있다 — 만들 때 정의한다

                // 붙는 리그 만들기 ▸ emoji — 리그 정의 + 오브젝트 + 붙이기가 되돌리기 한 칸.
                long revision = editor.Revision;
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1");
                Assert.Equal(revision + 1, editor.Revision);
                Assert.NotNull(editor.Document.FindRig(RigTemplates.EmojiKind));
                Assert.Contains(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);          // 세션의 리그에 합쳐졌다
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part1"));       // 뷰포트에 섰다
                Assert.Equal("s1/CharSlot_Scale", window.Viewport.CurrentPose()!.GetParentKey($"{id}/__root"));
                Assert.Equal(0, window.ProblemRowsShown);

                // 스킨 칸이 서고(조각 넷), 스킨을 입혀 조각을 그린다.
                window.InspectorTab = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.SkinFormState.Visible);
                window.AddSkinFromForm();
                Assert.Equal(4, window.SkinFormState.PartRows);
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));

                // 모션 — 이 리그의 노드에, 불투명도도.
                editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.2f, 40f) });
                window.SelectMotion("pop");
                window.Viewport.SelectedObjectId = id;
                window.Viewport.SelectedNode = "Emoji_Part1";
                Assert.True(window.Viewport.CanTakeAlpha("Emoji_Part1"));
                Assert.Equal(0, window.ProblemRowsShown);

                // 이모지 정의 — 리그가 이미 있으니 그대로 쓴다.
                window.InspectorTab = 4;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.AddEmojiFromForm();
                window.UpdateSelectedEmoji(e => { e.Enter = "pop"; e.HoldSeconds = 0.5f; });
                Assert.Equal(RigTemplates.EmojiKind, editor.Document.FindEmoji("emoji")!.Rig);
                Assert.Equal(0.7, window.Viewport.Clock.Duration, 4);
                Assert.Equal(0, window.ProblemRowsShown);

                // 리그 칸: 조각 수를 둘로 — 한 칸. 스킨 칸의 조각 줄도 따라 준다.
                Assert.Equal(("4", "160", true), (window.RigPartBoxes.Count.Text, window.RigPartBoxes.Size.Text, window.RigPartBoxes.Editable));
                TextBox count = window.RigPartBoxes.Count;
                count.Focus();
                count.Text = "2";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(2, editor.Document.FindRig(RigTemplates.EmojiKind)!.Nodes.Count(n => n.Image));
                Assert.False(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part3"));
                editor.Undo();
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part3"));

                // 내보내기 셋: 리그(덤프와 같은 모양) · 모션 · 이모지.
                string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"emoji-out-{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                Assert.True(window.Session.ExportRigs(Path.Combine(directory, "rigs.json")), window.Session.Status);
                Assert.True(window.Session.Export(Path.Combine(directory, "motions.json")), window.Session.Status);
                Assert.True(window.Session.ExportEmojis(Path.Combine(directory, "emojis.json")), window.Session.Status);
                Assert.Contains("\"hasImage\": true", File.ReadAllText(Path.Combine(directory, "rigs.json")), StringComparison.Ordinal);
                Assert.Contains("\"rig\": \"emoji\"", File.ReadAllText(Path.Combine(directory, "motions.json")), StringComparison.Ordinal);

                // 저장하고 다시 열어도 리그가 선다.
                string saved = Path.Combine(directory, "a.motion-authoring.json");
                window.Session.Save(saved);
                window.Session.New();
                Assert.DoesNotContain(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);
                window.Session.Open(saved);
                Assert.Contains(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part1"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 캐릭터_모션의_emoji_이벤트가_울리면_붙은_이모지가_정의의_스킨을_입고_뜬다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                SceneObject Emoji() => editor.Document.ActiveSceneAsset!.Find(id)!;
                editor.AddSkin("땀", EmojiRigFixture.RigKind);
                editor.SetSkinPart("땀", "Emoji_Part1", "art/sweat.png");

                KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
                editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });
                editor.AddEmoji("sweat", EmojiRigFixture.RigKind);
                editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Enter = "pop", HoldSeconds = 0.2f, OffsetX = 120f });

                // 캐릭터 모션 nod(0.6초)의 0.1초에 emoji sweat.
                editor.AddMotion("nod", "character", 0.6f, MotionEnd.Restore, MotionChannelMode.Relative);
                editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
                editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.3f, -18f), Linear(0.6f, 0f) });
                editor.AddMotionEvent("nod", 0.1f, "emoji", "sweat");
                window.SelectMotion("nod");
                window.Viewport.SelectedObjectId = "s1";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                float BodyY() => window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Body").AnchoredPosition.Y;

                // 울리기 전: 부리는 오브젝트는 안 보인다(스킨도 안 입었다).
                window.Viewport.ScrubTo(0.05);
                Assert.Empty(window.Viewport.EmojiPlaysAt(0.05));
                Assert.True(window.Viewport.IsDormantEmoji(id));
                Assert.Empty(window.Viewport.DrawnParts(Emoji()));

                // 울린 뒤: 정의의 스킨을 입고 등장이 돈다. 자리는 정의의 오프셋.
                window.Viewport.ScrubTo(0.2);
                Assert.False(window.Viewport.IsDormantEmoji(id));
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(Emoji()));
                Assert.Equal(20f, BodyY(), 2);
                Assert.Equal(120f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.X, 2);
                window.Viewport.ScrubTo(0.4);
                Assert.Equal(40f, BodyY(), 2);

                // 붙는 자리(faceAura)를 주면 자리 노드가 캐릭터의 그 점으로 간다 — 오프셋은 거기서부터(머리 쪽이라 위로 올라간다).
                float before = window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.Y;
                EmojiAsset located = editor.Document.FindEmoji("sweat")!.Clone();
                located.Locator = "faceAura";
                editor.UpdateEmoji("sweat", located);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.Y > before + 100f);
                Assert.Equal(40f, BodyY(), 2);

                // 수명(0.4초)이 다하면 다시 사라진다.
                window.Viewport.ScrubTo(0.55);
                Assert.True(window.Viewport.IsDormantEmoji(id));
                Assert.False(window.Viewport.IsDormantEmoji("s1"));

                // 클립에서도 같다 — 블록이 놓인 시각부터 센다.
                editor.AddClip("clip");
                editor.AddClipRole("clip", "a", "character", "s1");
                editor.AddClipTrack("clip", "a");
                editor.AddClipBlock("clip", 0, "nod", 1f);
                window.SelectClip("clip");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.5);
                Assert.True(window.Viewport.IsDormantEmoji(id));
                window.Viewport.ScrubTo(1.2);
                Assert.Equal(20f, BodyY(), 2);
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(Emoji()));

                // 모르는 이모지 키는 문제 목록이 알린다(막지는 않는다).
                Assert.Equal(0, window.ProblemRowsShown);
                editor.UpdateMotionEvent("nod", 0, 0.1f, "emoji", "ghost");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, window.ProblemRowsShown);
                Assert.False(window.Viewport.IsDormantEmoji(id)); // 부리는 이벤트가 없으니 보통 오브젝트다
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 씬_탭의_배역_칸에_키를_적으면_한_칸으로_들어가고_붙은_오브젝트에서는_잠긴다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.InspectorTab = 1; // 씬 탭
                window.Viewport.SelectedObjectId = "s1";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.ObjectCharacterBox.IsEnabled);
                Assert.Equal("", window.ObjectCharacterBox.Text);

                long revision = editor.Revision;
                window.ObjectCharacterBox.Focus();
                window.ObjectCharacterBox.Text = "bandi";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("bandi", editor.Document.ActiveSceneAsset!.Find("s1")!.Character);
                Assert.Equal(revision + 1, editor.Revision);

                editor.Undo();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(editor.Document.ActiveSceneAsset!.Find("s1")!.Character);
                Assert.Equal("", window.ObjectCharacterBox.Text);

                // 붙은 것은 부모의 배역을 따른다 — 칸이 잠긴다.
                window.Viewport.SelectedObjectId = id;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(window.ObjectCharacterBox.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 리그_편집기에서_노드를_짓고_고치면_뷰포트의_리그가_바로_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                window.InspectorTab = 4; // 이모지 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 덤프의 리그(캐릭터)는 못 고친다 — 목록이 비고 칸이 잠긴다.
                Assert.Equal(("character", (string?)null, false), (window.RigEditorState.Rig, window.RigEditorState.Node, window.RigEditorState.Editable));
                Assert.Empty(window.RigEditorState.Rows);
                Assert.Null(window.AddRigNodeFromForm(image: true));

                // 새 리그 — 한 칸, 그 리그가 골라지고 노드 Body가 선다.
                long revision = editor.Revision;
                Assert.Equal("rig", window.NewRigFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(("rig", "Body", true), (window.RigEditorState.Rig, window.RigEditorState.Node, window.RigEditorState.Editable));
                Assert.Equal(new[] { "· Body  α" }, window.RigEditorState.Rows);

                // 그림 노드 둘 — 고른 노드 밑에 선다. 목록은 들여쓰기로 계층을 보인다.
                Assert.Equal("Part1", window.AddRigNodeFromForm(image: true));
                window.SelectRigNode("Body");
                Assert.Equal("Part2", window.AddRigNodeFromForm(image: true));
                Assert.Equal(new[] { "· Body  α", "   ▣ Part1  α", "   ▣ Part2  α" }, window.RigEditorState.Rows);

                // 캐릭터에 붙여 세우면 뷰포트의 리그에 그 노드들이 선다.
                string id = window.CreateAttachedFromMenu("rig", "s1");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Part2"));

                // 칸에서 위치·크기를 고친다(Enter) — 한 칸, 뷰포트가 바로 따라온다.
                window.SelectRigNode("Part2");
                revision = editor.Revision;
                window.RigNodeBoxes.X.Focus();
                window.RigNodeBoxes.X.Text = "40";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(40f, editor.Document.FindRig("rig")!.Find("Part2")!.X);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(40f, window.Viewport.CurrentPose()!.GetState($"{id}/Part2").AnchoredPosition.X, 3);

                // 이름 바꾸기 — 고른 노드가 새 이름을 따라간다.
                window.RigNodeBoxes.Id.Focus();
                window.RigNodeBoxes.Id.Text = "Spark";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("Spark", window.RigEditorState.Node);
                Assert.NotNull(editor.Document.FindRig("rig")!.Find("Spark"));

                // 순서와 삭제.
                window.MoveRigNodeFromForm(-1);
                Assert.Equal(new[] { "Body", "Spark", "Part1" }, editor.Document.FindRig("rig")!.Nodes.Select(n => n.Id));
                window.RemoveRigNodeFromForm();
                Assert.Equal(new[] { "Body", "Part1" }, editor.Document.FindRig("rig")!.Nodes.Select(n => n.Id));
                Assert.Equal("Body", window.RigEditorState.Node);

                // 리그를 만든 것까지 되돌리면 고른 리그가 풀린다.
                while (editor.Document.FindRig("rig") is not null)
                {
                    editor.Undo();
                }

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.NotEqual("rig", window.RigEditorState.Rig);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 스킨의_그림_넘기기와_놓임과_반전_판이_뷰포트에_그대로_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.AddSkinFromForm();
                window.InspectorTab = 1; // 씬 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 그림 칸에 ' | '로 이어 적으면 장이 여럿 — 한 칸.
                long revision = editor.Revision;
                TextBox images = window.SkinPartBox("Emoji_Part1")!;
                images.Focus();
                images.Text = "art/a.png | art/b.png | art/c.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { "art/a.png", "art/b.png", "art/c.png" }, editor.Document.FindSkin("skin")!.ImagesOf("Emoji_Part1"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("art/a.png | art/b.png | art/c.png", window.SkinPartBox("Emoji_Part1")!.Text);
                Assert.Null(window.SkinSetupBoxes("Emoji_Part2")); // 그림이 없는 조각에는 덧붙임 칸이 없다

                // 시계를 따라 넘어간다(장당 0.2초).
                editor.AddMotion("still", EmojiRigFixture.RigKind, 1f);
                window.SelectMotion("still");
                window.Viewport.SelectedObjectId = id;
                window.Viewport.ScrubTo(0.0);
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                window.Viewport.ScrubTo(0.25);
                Assert.Equal("art/b.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                window.Viewport.ScrubTo(0.5);
                Assert.Equal("art/c.png", window.Viewport.PartImage(id, "Emoji_Part1"));

                // 놓임 칸(X) — 한 칸, 조각이 그만큼 옮겨 그려진다. 노드의 값은 그대로다(모션과 따로).
                Quad before = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                var boxes = window.SkinSetupBoxes("Emoji_Part1")!;
                revision = editor.Revision;
                boxes.X.Focus();
                boxes.X.Text = "50";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(50f, editor.Document.FindSkin("skin")!.SetupOf("Emoji_Part1").X);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Quad placed = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                // 놓임은 조각 노드의 px이다 — 화면에서는 부모들의 배율(캐릭터의 깊이 배율 등)이 곱해진다: 조각의 폭이 곱해진 그만큼.
                float perPixel = (before.BottomRight.X - before.BottomLeft.X) / EmojiRigFixture.PartSize;
                Assert.Equal(before.Center.X + 50f * perPixel, placed.Center.X, 2);
                Assert.Equal(0f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Part1").AnchoredPosition.X, 3);

                // 크기 0.5 — 사각형이 반으로.
                SkinPartSetup half = editor.Document.FindSkin("skin")!.SetupOf("Emoji_Part1").Clone();
                half.Scale = 0.5f;
                editor.SetSkinPartSetup("skin", "Emoji_Part1", half);
                Quad small = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.Equal((before.BottomRight.X - before.BottomLeft.X) / 2f, small.BottomRight.X - small.BottomLeft.X, 2);

                // 반전 판 — 캐릭터가 좌우 반전이면 이모지는 반전 판 스킨을 입고, 그림은 뒤집지 않는다(사각형의 왼쪽 아래가 여전히 왼쪽).
                editor.AddSkin("skin_flip", EmojiRigFixture.RigKind);
                editor.SetSkinPartImages("skin_flip", "Emoji_Part1", new[] { "art/left.png" });
                editor.AddEmoji("e", EmojiRigFixture.RigKind);
                editor.UpdateEmoji("e", new EmojiAsset { Skin = "skin", SkinFlipped = "skin_flip", HoldSeconds = 1f, OffsetX = 100f });
                window.SelectEmoji("e");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.1);
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                float right = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value.Center.X;

                editor.SetObjectPlacement(null, "s1", 0f, 0f, 0f, -1f, 1f);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.1);
                Assert.Equal("art/left.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                Quad flipped = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.True(flipped.Center.X < 0f && right > 0f, $"{right} → {flipped.Center.X}"); // 자리는 뒤집혔다
                Assert.True(flipped.BottomRight.X > flipped.BottomLeft.X);                          // 그림은 안 뒤집혔다

                // 반전 판이 없으면 같은 그림이 뒤집혀 보인다.
                editor.UpdateEmoji("e", new EmojiAsset { Skin = "skin", HoldSeconds = 1f, OffsetX = 100f });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.1);
                Quad mirrored = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                Assert.True(mirrored.BottomRight.X < mirrored.BottomLeft.X);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지_탭에서_그_이모지의_스킨을_바로_만들고_고친다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1"); // 리그도 그 자리에서 정의된다
                window.InspectorTab = 4;
                Assert.Equal("emoji", window.AddEmojiFromForm());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(window.EmojiSkinPartBox("Emoji_Part1")); // 스킨이 아직 없다

                // 새 스킨 — 이모지의 이름으로 만들어 그림으로 삼는다, 한 칸. 조각 줄이 선다(오브젝트에 입히지 않아도).
                long revision = editor.Revision;
                Assert.Equal("emoji", window.NewEmojiSkinFromForm(flipped: false));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("emoji", editor.Document.FindEmoji("emoji")!.Skin);
                Assert.Null(editor.Document.ActiveSceneAsset!.Find(id)!.Skin);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                TextBox part = window.EmojiSkinPartBox("Emoji_Part1")!;

                // 그 자리에서 그림을 준다 — 뷰포트의 이모지가 바로 그 그림을 그린다.
                part.Focus();
                part.Text = "art/a.png | art/b.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(new[] { "art/a.png", "art/b.png" }, editor.Document.FindSkin("emoji")!.ImagesOf("Emoji_Part1"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.05);
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));

                // 놓임도 여기서.
                var boxes = window.EmojiSkinSetupBoxes("Emoji_Part1")!;
                boxes.Y.Focus();
                boxes.Y.Text = "30";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(30f, editor.Document.FindSkin("emoji")!.SetupOf("Emoji_Part1").Y);

                // 반전 판 — 따로 한 묶음의 줄이 선다.
                Assert.Equal("emoji_반전", window.NewEmojiSkinFromForm(flipped: true));
                Assert.Equal("emoji_반전", editor.Document.FindEmoji("emoji")!.SkinFlipped);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                TextBox flipped = window.EmojiSkinPartBox("Emoji_Part1", flipped: true)!;
                flipped.Focus();
                flipped.Text = "art/left.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("art/left.png", editor.Document.FindSkin("emoji_반전")!.Parts["Emoji_Part1"]);
                Assert.Equal("art/a.png", editor.Document.FindSkin("emoji")!.Parts["Emoji_Part1"]); // 본판은 그대로

                // 되돌리면 스킨과 이모지의 참조가 함께 돌아간다.
                editor.Undo(); // 그림
                editor.Undo(); // 새 반전 판
                Assert.Null(editor.Document.FindEmoji("emoji")!.SkinFlipped);
                Assert.Null(editor.Document.FindSkin("emoji_반전"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지_정의를_보며_조각을_끌면_스킨의_놓임이_한_칸으로_바뀐다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1");
                window.InspectorTab = 4;
                window.AddEmojiFromForm();
                window.NewEmojiSkinFromForm(flipped: false);
                editor.SetSkinPartImages("emoji", "Emoji_Part1", new[] { "art/a.png" });
                editor.SetSkinPartImages("emoji", "Emoji_Part2", new[] { "art/b.png" });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.05);
                ViewportView view = window.Viewport;
                SkinAsset skin = editor.Document.FindSkin("emoji")!;

                Point At(Vec2 root) => view.TranslatePoint(view.ToScreen(root), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Quad Part(string node) => view.PartQuad(id, node)!.Value;

                // 두 조각이 겹쳐 있다 — 위에 그려진 것(Part2)이 집힌다. 끌면 그만큼 놓임이 바뀐다: 한 칸, 모션의 키는 안 생긴다.
                long revision = editor.Revision;
                Quad before = Part("Emoji_Part2");
                float perPixel = (before.BottomRight.X - before.BottomLeft.X) / RigTemplates.DefaultPartSize; // 노드의 px → 루트 공간
                Vec2 grab = before.Center;
                var drop = new Vec2(grab.X + 60f * perPixel, grab.Y + 30f * perPixel);
                window.MouseDown(At(grab), MouseButton.Left);
                window.MouseMove(At(new Vec2(grab.X + 20f, grab.Y)), RawInputModifiers.LeftMouseButton);
                Assert.Equal(revision, editor.Revision); // 끄는 동안에는 기록이 없다(그림만 따라온다)
                Assert.True(Part("Emoji_Part2").Center.X > before.Center.X);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal((60f, 30f), (skin.SetupOf("Emoji_Part2").X, skin.SetupOf("Emoji_Part2").Y));
                Assert.Equal(0f, skin.SetupOf("Emoji_Part1").X);
                Assert.Equal(("emoji", "Emoji_Part2"), view.PlacedPart);
                Assert.Empty(editor.Document.Motions);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("60", window.EmojiSkinSetupBoxes("Emoji_Part2")!.X.Text); // 칸이 따라온다

                // 옮겨 간 Part2가 안 덮는 쪽(왼쪽)에서 집으면 Part1이다. Shift+끌기 = 크기(가운데에서의 거리 비).
                Quad one = Part("Emoji_Part1");
                var edge = new Vec2(one.Center.X - 60f * perPixel, one.Center.Y);
                var half = new Vec2(one.Center.X - 30f * perPixel, one.Center.Y);
                window.MouseDown(At(edge), MouseButton.Left, RawInputModifiers.Shift);
                window.MouseMove(At(half), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
                window.MouseUp(At(half), MouseButton.Left, RawInputModifiers.Shift);
                Assert.Equal(0.5f, skin.SetupOf("Emoji_Part1").Scale, 2);
                Assert.Equal(revision + 2, editor.Revision);

                // Ctrl+끌기 = 회전(가운데 둘레) — 왼쪽에서 아래로 4분의 1바퀴 = 반시계 +90도.
                one = Part("Emoji_Part1");
                var right = new Vec2(one.Center.X - 30f * perPixel, one.Center.Y);
                var up = new Vec2(one.Center.X, one.Center.Y - 30f * perPixel);
                window.MouseDown(At(right), MouseButton.Left, RawInputModifiers.Control);
                window.MouseMove(At(up), RawInputModifiers.LeftMouseButton | RawInputModifiers.Control);
                window.MouseUp(At(up), MouseButton.Left, RawInputModifiers.Control);
                Assert.Equal(90f, skin.SetupOf("Emoji_Part1").Rotation, 0);

                // 제자리에서 눌렀다 떼면 기록이 없다. 빈 자리를 누르면 집은 것이 풀린다.
                revision = editor.Revision;
                Vec2 two = Part("Emoji_Part2").Center;
                window.MouseDown(At(two), MouseButton.Left);
                window.MouseUp(At(two), MouseButton.Left);
                Assert.Equal(revision, editor.Revision);
                var empty = new Vec2(two.X - 900f, two.Y - 400f);
                window.MouseDown(At(empty), MouseButton.Left);
                window.MouseUp(At(empty), MouseButton.Left);
                Assert.Null(view.PlacedPart);

                // 되돌리면 놓임이 돌아간다.
                editor.Undo();
                editor.Undo();
                editor.Undo();
                Assert.Empty(editor.Document.FindSkin("emoji")!.Setups); // 되돌리기는 문서를 갈아 끼운다 — 다시 찾는다
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지를_보면서_구간의_모션을_고치면_그래프가_그_모션을_보이고_키가_그_모션에_놓인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1");
                KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
                editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });
                editor.AddMotion("bob", RigTemplates.EmojiKind, 0.4f);
                editor.AddChannel("bob", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("bob", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 10f), Linear(0.4f, 0f) });
                editor.AddEmoji("sweat", RigTemplates.EmojiKind);
                editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Hold = "bob", HoldSeconds = 1f });
                editor.AddEmoji("other", RigTemplates.EmojiKind);
                editor.UpdateEmoji("other", new EmojiAsset { Hold = "bob", HoldSeconds = 1f });
                window.InspectorTab = 4;
                window.SelectEmoji("sweat");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.SnapEnabled = false;
                Vec2 Body() => window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Body").AnchoredPosition;

                // 구간을 안 골랐으면 타임 슬라이더에 세 구간의 키가 다 보인다(구간마다 색이 다르다): 등장 pop의 0 · 0.2, 머묾 bob이 바퀴마다. 퇴장은 없다.
                Assert.Equal(new[] { (EmojiPhase.Enter, 0.0), (EmojiPhase.Enter, 0.2) }, window.Viewport.EmojiKeyTimes().Where(k => k.Phase == EmojiPhase.Enter).Select(k => (k.Phase, Math.Round(k.Time, 3))));
                Assert.Equal(8, window.Viewport.EmojiKeyTimes().Count(k => k.Phase == EmojiPhase.Hold));
                Assert.DoesNotContain(window.Viewport.EmojiKeyTimes(), k => k.Phase == EmojiPhase.Exit);
                Assert.Empty(window.Viewport.ContextKeyTimes());

                // 구간을 안 골랐으면 그래프는 비고 포즈를 안 잡는다(조각을 끌어 놓는 쪽).
                Assert.Null(window.Graph.MotionName);
                Assert.Null(window.Viewport.EditMotion);
                Assert.Equal((EmojiPhase?)null, window.EmojiPhaseState.Phase);

                // 머묾을 고친다 — 이모지는 고른 채, 그래프에 bob이 뜨고 시계가 그 구간의 처음으로 간다.
                window.EditEmojiPhase(EmojiPhase.Hold);
                Assert.Equal("sweat", window.SelectedEmoji);
                Assert.Null(window.SelectedMotion);
                Assert.Equal("bob", window.Graph.MotionName);
                Assert.Equal("bob", window.Viewport.EditMotion?.Name);
                Assert.InRange(window.Viewport.Time, 0.2, 0.21);
                Assert.Contains("함께 쓴다: other", window.EmojiPhaseState.Note, StringComparison.Ordinal);

                // 타임 슬라이더의 키 틱 — bob의 키(0 · 0.2 · 0.4)가 머묾의 바퀴마다 시계의 자리에: 0.2부터 0.4씩, 머무는 시간(1초)이 다하는 1.2까지.
                Assert.Equal(new[] { 0.2, 0.4, 0.6, 0.6, 0.8, 1.0, 1.0, 1.2 }, window.Viewport.ContextKeyTimes().Select(t => Math.Round(t, 3)));
                Assert.True(window.EmojiPhaseState.CanMakeUnique);

                Assert.Empty(window.Viewport.EmojiKeyTimes()); // 구간을 고르면 그 구간만(빨간 틱)

                // 그래프 목록 — bob이 건드리는 것은 Body의 위치뿐이다. 나머지(다른 노드 · 배율 · 회전 · 불투명도)가 "키 없음"으로 흐리게 선다.
                Assert.DoesNotContain(("Emoji_Body", MotionChannelKind.Position), window.UnkeyedRows);
                Assert.Contains(("Emoji_Body", MotionChannelKind.Scale), window.UnkeyedRows);
                Assert.Contains(("Emoji_Body", MotionChannelKind.Alpha), window.UnkeyedRows);
                Assert.Contains(("Emoji_Part1", MotionChannelKind.Euler), window.UnkeyedRows);
                Assert.DoesNotContain(("Emoji_Anchor", MotionChannelKind.Alpha), window.UnkeyedRows); // 불투명도를 못 받는 노드

                // ＋ — 지금 값이 지금 시각(bob의 0초)의 키가 되고 채널이 생긴다: 한 칸. 줄은 "키 없음"에서 빠진다.
                long before = editor.Revision;
                window.KeyUnkeyedRow("Emoji_Body", MotionChannelKind.Scale);
                Assert.Equal(before + 1, editor.Revision);
                ChannelAsset scale = editor.Document.Find("bob")!.FindChannel("Emoji_Body", MotionChannelKind.Scale)!;
                Assert.Equal((1f, 1f), (scale.X!.Keys.Single().V, scale.Y!.Keys.Single().V));
                Assert.DoesNotContain(("Emoji_Body", MotionChannelKind.Scale), window.UnkeyedRows);
                Assert.Equal("Emoji_Body", window.Viewport.SelectedNode);
                editor.Undo();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Contains(("Emoji_Body", MotionChannelKind.Scale), window.UnkeyedRows);

                // 둘째 바퀴의 0.1초(수명 0.7초) — 그래프의 시각은 모션 기준이다.
                window.Viewport.ScrubTo(0.7);
                Assert.Equal(0.1, window.Graph.CurrentTime, 4);
                Assert.Equal(45f, Body().Y, 2); // 등장이 남긴 40 + bob의 5

                // 값을 넣고 S — 키가 bob의 0.1초에 놓인다. 시작값은 등장이 끝난 자세라 상대값 20이 들어간다(60 − 40).
                window.Viewport.SelectedNode = "Emoji_Body";
                long revision = editor.Revision;
                window.Viewport.SetComponent("Emoji_Body", MotionChannelKind.Position, Axis.Y, 60f);
                window.Viewport.SetKey();
                KeyAsset key = editor.Document.Find("bob")!.FindChannel("Emoji_Body", MotionChannelKind.Position)!.Y!.Keys.Single(k => Math.Abs(k.T - 0.1f) < 1e-4f);
                Assert.Equal(20f, key.V, 2);
                Assert.True(editor.Revision > revision);
                Assert.Equal(60f, Body().Y, 2);
                window.Viewport.ScrubTo(0.3); // 첫 바퀴의 같은 자리에도 보인다(같은 모션이 되풀이된다)
                Assert.Equal(60f, Body().Y, 2);
                Assert.Equal("sweat", window.SelectedEmoji); // 고쳐도 이모지가 풀리지 않는다
                Assert.Equal("bob", window.Graph.MotionName);

                // 그래프에서 시각을 옮기면 뷰포트가 그 바퀴 안에서 따라온다.
                window.Graph.CurrentTime = 0.2;
                Assert.Equal(0.4, window.Viewport.Time, 4);

                // 구간 밖(등장 중)에서는 손이 안 간다.
                window.Viewport.ScrubTo(0.1);
                Assert.Null(window.Viewport.EditMotion);
                window.Viewport.ScrubTo(0.7);

                // 이 이모지만의 모션으로 — 복제해 꽂고 그래프가 그것을 따라간다. 다른 이모지는 원래 것을 쓴다.
                Assert.Equal("bob_2", window.MakeEmojiPhaseUniqueFromForm());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(("bob_2", "bob"), (editor.Document.FindEmoji("sweat")!.Hold, editor.Document.FindEmoji("other")!.Hold));
                Assert.Equal("bob_2", window.Graph.MotionName);
                Assert.Contains("이 이모지만 쓴다", window.EmojiPhaseState.Note, StringComparison.Ordinal);
                Assert.False(window.EmojiPhaseState.CanMakeUnique);

                // 등장으로 바꾸면 그래프도 바뀌고, 풀면 그래프가 빈다.
                window.EditEmojiPhase(EmojiPhase.Enter);
                Assert.Equal("pop", window.Graph.MotionName);
                Assert.InRange(window.Viewport.Time, 0.0, 0.2);
                Assert.Equal(new[] { 0.0, 0.2 }, window.Viewport.ContextKeyTimes().Select(t => Math.Round(t, 3)));
                window.EditEmojiPhase(null);
                Assert.Null(window.Graph.MotionName);
                Assert.Null(window.Viewport.EditMotion);
                Assert.Empty(window.Viewport.ContextKeyTimes());

                // 모션 목록에서 모션을 고르면 이모지를 떠난다(보통의 모션 편집).
                window.SelectMotion("pop");
                Assert.Null(window.SelectedEmoji);
                Assert.Equal("pop", window.Graph.MotionName);
                Assert.Equal("pop", window.Viewport.EditMotion?.Name);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
