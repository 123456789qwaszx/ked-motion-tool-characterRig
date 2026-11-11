using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시 문서 <c>examples/emoji/emoji-examples.motion-authoring.json</c>을 짓는다 — 이모지 22종을 실제 그림으로 세팅해 둔 것(써 보려고).
/// 문서는 편집기의 명령으로만 짓는다(툴에서 손으로 만든 것과 같은 길). <see cref="EmojiExampleTests"/>가 저장된 파일과 맞는지 지킨다.
/// </summary>
internal static class EmojiExamples
{
    public const string Rig = "emoji";
    private const string Body = "Emoji_Body";
    /// <summary>조각 — 모션이 움직인다.</summary>
    private static string Part(int i) => $"Emoji_Part{i}";

    /// <summary>조각의 그림 노드 — 스킨의 그림과 놓임이 들어간다(런타임 이모지 리그: 조각 › 그림).</summary>
    private static string Image(int i) => $"Emoji_Part{i}_Image";

    private static KeyAsset K(float t, float v, TangentMode tangent = TangentMode.Auto) => new() { T = t, V = v, Tangent = tangent };

    private static KeyAsset L(float t, float v) => K(t, v, TangentMode.Linear);

    public static MotionEditor Build()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.SetObjectPlace(null, "s1", "center");
        editor.AddSceneObject(null, "e1", Rig);
        editor.AttachObject(null, "e1", "s1");

        Motions(editor);
        Emojis(editor);
        return editor;
    }

    private static void Curve(MotionEditor editor, string motion, string node, MotionChannelKind kind, Axis axis, params KeyAsset[] keys)
    {
        if (editor.Document.Find(motion)!.Channels.All(c => c.Node != node || c.Kind != kind))
        {
            editor.AddChannel(motion, node, kind);
        }

        editor.SetKeys(motion, node, kind, axis, keys);
    }

    private static void Scale(MotionEditor editor, string motion, string node, params KeyAsset[] keys)
    {
        Curve(editor, motion, node, MotionChannelKind.Scale, Axis.X, keys);
        Curve(editor, motion, node, MotionChannelKind.Scale, Axis.Y, keys.Select(k => K(k.T, k.V, k.Tangent)).ToArray());
    }

    private static void Motions(MotionEditor editor)
    {
        const MotionChannelMode Abs = MotionChannelMode.Absolute;

        // ── 등장(한 번, 유지) ──
        // 톡 튀어나온다: 0 → 1.2 → 1.
        editor.AddMotion("emoji_pop", Rig, 0.25f, MotionEnd.Commit, Abs);
        Scale(editor, "emoji_pop", Body, K(0f, 0f), K(0.15f, 1.2f), K(0.25f, 1f));

        // 위에서 내려앉는다(땀·빗선).
        editor.AddMotion("emoji_drop", Rig, 0.3f, MotionEnd.Commit, Abs);
        Curve(editor, "emoji_drop", Body, MotionChannelKind.Position, Axis.Y, K(0f, 40f), K(0.3f, 0f));

        // 조각 셋이 차례로 핀다 — 어디에 얼마만 하게 놓이는가는 스킨의 놓임이 정한다(꽃이든 별이든 이 모션을 쓴다).
        editor.AddMotion("emoji_bloom", Rig, 0.5f, MotionEnd.Commit, Abs);
        float[] starts = { 0f, 0.12f, 0.24f };

        for (int i = 0; i < starts.Length; i++)
        {
            float at = starts[i];
            KeyAsset[] bloom = at > 0f
                ? new[] { K(0f, 0f, TangentMode.Flat), K(at, 0f, TangentMode.Flat), K(at + 0.14f, 1.25f), K(at + 0.22f, 1f), K(0.5f, 1f, TangentMode.Flat) }
                : new[] { K(0f, 0f, TangentMode.Flat), K(0.16f, 1.25f), K(0.26f, 1f), K(0.5f, 1f, TangentMode.Flat) };
            Scale(editor, "emoji_bloom", Part(i + 1), bloom);
            Curve(editor, "emoji_bloom", Part(i + 1), MotionChannelKind.Euler, Axis.Z, K(0f, -40f), K(0.5f, 0f));
        }

        // ── 머묾(되풀이, 복원) ──
        editor.AddMotion("emoji_bob", Rig, 0.8f); // 둥실
        Curve(editor, "emoji_bob", Body, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.4f, 6f), K(0.8f, 0f));

        editor.AddMotion("emoji_wobble", Rig, 0.6f); // 갸웃
        Curve(editor, "emoji_wobble", Body, MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(0.15f, 8f), K(0.45f, -8f), K(0.6f, 0f));

        editor.AddMotion("emoji_beat", Rig, 0.5f, MotionEnd.Restore, Abs); // 두근
        Scale(editor, "emoji_beat", Body, K(0f, 1f), K(0.12f, 1.14f), K(0.3f, 1f), K(0.5f, 1f));

        editor.AddMotion("emoji_shake", Rig, 0.24f); // 부들
        Curve(editor, "emoji_shake", Body, MotionChannelKind.Position, Axis.X, L(0f, 0f), L(0.06f, 4f), L(0.18f, -4f), L(0.24f, 0f));

        // 되풀이하며 "끝에서 처음으로 튀는" 머묾은 복원 모션일 수 없다(복원은 중립값에서 끝나야 한다) — 유지로 두고, 이모지의 머묾이 바퀴마다 처음 자세에서 다시 돌린다.
        editor.AddMotion("emoji_drip", Rig, 0.9f, MotionEnd.Commit, Abs); // 주르륵 — 흘러내렸다가 다시
        Curve(editor, "emoji_drip", Body, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.9f, -28f));

        editor.AddMotion("emoji_float", Rig, 1.2f, MotionEnd.Commit, Abs); // 떠올랐다가 다시
        Curve(editor, "emoji_float", Body, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1.2f, 18f));
        Curve(editor, "emoji_float", Body, MotionChannelKind.Position, Axis.X, K(0f, 0f), K(0.4f, 5f), K(0.8f, -5f), K(1.2f, 0f));

        editor.AddMotion("emoji_sway", Rig, 1f); // 조각들이 살랑
        for (int i = 1; i <= 3; i++)
        {
            float lean = i == 2 ? -7f : 7f;
            Curve(editor, "emoji_sway", Part(i), MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(0.5f, lean), K(1f, 0f));
        }

        // ── 퇴장(한 번) ── 흐려지는 것은 모션이 아니다(불투명도는 모션 규약 밖 — 런타임 PLAN_emoji §8-4).
        editor.AddMotion("emoji_lift", Rig, 0.25f, MotionEnd.Commit, Abs); // 살짝 떠오르며 작아진다
        Curve(editor, "emoji_lift", Body, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.25f, 16f));
        Scale(editor, "emoji_lift", Body, K(0f, 1f), K(0.25f, 0.6f));

        editor.AddMotion("emoji_pop_out", Rig, 0.15f, MotionEnd.Commit, Abs);
        Scale(editor, "emoji_pop_out", Body, K(0f, 1f), K(0.05f, 1.15f), K(0.15f, 0f));
    }

    /// <summary>스킨 하나 — 조각마다 장들(한 장이면 그림 하나, 여럿이면 그림 넘기기).</summary>
    private static void Skin(MotionEditor editor, string name, params string[][] parts)
    {
        editor.AddSkin(name, Rig);

        for (int i = 0; i < parts.Length; i++)
        {
            editor.SetSkinPartImages(name, Image(i + 1), parts[i].Select(a => $"art/{a}.png").ToArray());
        }
    }

    private static void Emoji(MotionEditor editor, string key, string enter, string? hold, string exit, float? seconds, float x, float y, string locator = "faceAura", string? flipped = null)
        => editor.UpdateEmoji(editor.AddEmoji(key, Rig).Name, new EmojiAsset
        {
            Skin = key, SkinFlipped = flipped, Enter = enter, Hold = hold, Exit = exit, HoldSeconds = seconds, OffsetX = x, OffsetY = y, Locator = locator,
        });

    /// <summary>그림 한 장짜리 이모지 — 스킨과 정의를 같은 이름으로.</summary>
    private static void Simple(MotionEditor editor, string key, string art, string enter, string? hold, string exit, float? seconds, float x, float y, string locator = "faceAura")
    {
        Skin(editor, key, new[] { art });
        Emoji(editor, key, enter, hold, exit, seconds, x, y, locator);
    }

    private static void Emojis(MotionEditor editor)
    {
        const float R = 190f;  // 머리 오른쪽
        const float Up = 20f;  // 머리 옆, 살짝 위

        // 잠깐 뜨는 것(머무는 시간이 있다)
        Simple(editor, "느낌물음표", "exclaim_question", "emoji_pop", "emoji_wobble", "emoji_lift", 1.2f, R, Up);
        Simple(editor, "반짝별", "sparkle", "emoji_pop", "emoji_beat", "emoji_pop_out", 1f, R, Up);
        Simple(editor, "큰땀삐질", "big_sweat", "emoji_drop", "emoji_drip", "emoji_lift", 1.8f, R, 40f);
        Simple(editor, "난감빗선", "gloom_lines", "emoji_drop", null, "emoji_lift", 1.5f, -R, Up);
        Simple(editor, "빠직", "anger", "emoji_pop", "emoji_beat", "emoji_lift", 1.25f, R, Up);
        Simple(editor, "팟놀란왕관", "shock_crown", "emoji_pop", null, "emoji_lift", 0.8f, -R, Up + 40f);
        Simple(editor, "아이디어전구", "idea_bulb", "emoji_pop", "emoji_bob", "emoji_lift", 1.6f, 0f, Up + 110f);
        Simple(editor, "빠밤작대기들", "tada_lines", "emoji_pop", "emoji_beat", "emoji_lift", 1f, R, Up + 40f);
        Simple(editor, "빨간경고막대기", "alert", "emoji_pop", "emoji_shake", "emoji_lift", 0.96f, R, Up);
        Simple(editor, "띠링물음표", "question", "emoji_pop", "emoji_wobble", "emoji_lift", 1.2f, R, Up);
        Simple(editor, "후우하는구름", "sigh_cloud", "emoji_pop", "emoji_float", "emoji_lift", 1.2f, 170f, -60f, "face");
        Simple(editor, "화내는부우구름", "huff_cloud", "emoji_pop", "emoji_float", "emoji_lift", 1.2f, 170f, -60f, "face");

        // 그림을 넘기는 것 — 조각 하나에 장을 여럿 준다(스킨의 그림 넘기기). 모션은 한 장짜리와 같은 것을 쓴다.
        Skin(editor, "난감실타래", new[] { "tangle_1", "tangle_2" });
        editor.SetSkinPartSetup("난감실타래", Image(1), new SkinPartSetup { Frames = { "art/tangle_2.png" }, FrameSeconds = 0.3f });
        Emoji(editor, "난감실타래", "emoji_pop", "emoji_bob", "emoji_lift", 1.8f, -R, Up);

        Skin(editor, "왁자지껄", new[] { "chatter_1", "chatter_2" });
        editor.SetSkinPartSetup("왁자지껄", Image(1), new SkinPartSetup { Frames = { "art/chatter_2.png" }, FrameSeconds = 0.3f });
        Emoji(editor, "왁자지껄", "emoji_pop", "emoji_shake", "emoji_lift", 1.8f, R, Up);

        Skin(editor, "삐질삐질땀방울", new[] { "sweat_1", "sweat_2", "sweat_3" }); // 장당 0.2초(기본값), 되풀이
        Emoji(editor, "삐질삐질땀방울", "emoji_pop", null, "emoji_lift", 1.8f, R, 40f);

        // 끌 때까지 머무는 것(emoji_off)
        Simple(editor, "플러팅하트", "heart", "emoji_pop", "emoji_beat", "emoji_pop_out", null, R, Up);
        Simple(editor, "곰곰생각구름", "think_cloud", "emoji_pop", "emoji_bob", "emoji_lift", null, R, Up + 40f);
        Simple(editor, "ZZZ", "zzz", "emoji_pop", "emoji_float", "emoji_lift", null, R, Up);
        Simple(editor, "신나는음표", "note", "emoji_pop", "emoji_bob", "emoji_lift", null, R, Up);

        // 좌·우 판 — 정의는 하나, 반전 판 스킨만 따로. 캐릭터가 좌우 반전이면 자리는 뒤집히고 그림은 반전 판을 뒤집지 않고 쓴다.
        // 말줄임점: 빈 풍선 → 점 하나 → 둘 → 셋에서 멈춘다(되풀이 안 함).
        foreach (string side in new[] { "r", "l" })
        {
            string name = side == "r" ? "말줄임점풍선" : "말줄임점풍선_반전";
            Skin(editor, name, new[] { $"dots_{side}_1", $"dots_{side}_2", $"dots_{side}_3", $"dots_{side}_4" });
            SkinPartSetup dots = editor.Document.FindSkin(name)!.SetupOf(Image(1)).Clone();
            dots.FrameSeconds = 0.35f;
            dots.Loop = false;
            editor.SetSkinPartSetup(name, Image(1), dots);
        }

        Emoji(editor, "말줄임점풍선", "emoji_pop", "emoji_bob", "emoji_lift", null, R, Up, flipped: "말줄임점풍선_반전");

        Skin(editor, "쑥스럼풍선", new[] { "shy_r" });
        Skin(editor, "쑥스럼풍선_반전", new[] { "shy_l" });
        Emoji(editor, "쑥스럼풍선", "emoji_pop", "emoji_bob", "emoji_lift", 1.6f, R, Up, flipped: "쑥스럼풍선_반전");

        // 조각 셋이 같은 그림 — 어디에 얼마만 하게 놓이는가는 스킨의 놓임. 모션(emoji_bloom · emoji_sway)은 자리를 모른다.
        Skin(editor, "행복한벚꽃", new[] { "blossom" }, new[] { "blossom" }, new[] { "blossom" });
        editor.SetSkinPartSetup("행복한벚꽃", Image(1), new SkinPartSetup { X = -150f, Y = 40f });
        editor.SetSkinPartSetup("행복한벚꽃", Image(2), new SkinPartSetup { X = 150f, Y = -30f, Scale = 0.8f });
        editor.SetSkinPartSetup("행복한벚꽃", Image(3), new SkinPartSetup { X = 120f, Y = 130f, Scale = 0.55f });
        Emoji(editor, "행복한벚꽃", "emoji_bloom", "emoji_sway", "emoji_lift", null, 0f, 0f);
    }
}
