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
}
