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
    public const string Rig = RigTemplates.EmojiKind;
    private const string Body = "Emoji_Body";
    private const float Flip = 0.02f; // 그림 넘기기의 틈 — 끊김 탄젠트가 없어 짧은 선으로 넘긴다

    private static string Part(int i) => $"Emoji_Part{i}";

    private static KeyAsset K(float t, float v, TangentMode tangent = TangentMode.Auto) => new() { T = t, V = v, Tangent = tangent };

    private static KeyAsset L(float t, float v) => K(t, v, TangentMode.Linear);

    public static MotionEditor Build()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji());
        editor.SetObjectPlace(null, "s1", "center");
        editor.AddSceneObject(null, "e1", Rig);
        editor.AttachObject(null, "e1", "s1");

        Motions(editor);
        Emojis(editor);
        Character(editor);
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

    /// <summary>조각 하나가 [from, to) 동안만 보인다 — 그림 넘기기의 한 장.</summary>
    private static KeyAsset[] Shown(float from, float to, float duration)
    {
        var keys = new List<KeyAsset>();

        if (from > 0f)
        {
            keys.Add(L(0f, 0f));
            keys.Add(L(from - Flip, 0f));
        }

        keys.Add(L(from, 1f));

        if (to < duration)
        {
            keys.Add(L(to - Flip, 1f));
            keys.Add(L(to, 0f));
            keys.Add(L(duration, 0f));
        }
        else
        {
            keys.Add(L(duration, 1f));
        }

        return keys.ToArray();
    }

    private static void Motions(MotionEditor editor)
    {
        const MotionChannelMode Abs = MotionChannelMode.Absolute;

        // ── 등장(한 번, 유지) ──
        // 톡 튀어나온다: 0 → 1.2 → 1, 처음 0.08초에 나타난다.
        editor.AddMotion("emoji_pop", Rig, 0.25f, MotionEnd.Commit, Abs);
        Scale(editor, "emoji_pop", Body, K(0f, 0f), K(0.15f, 1.2f), K(0.25f, 1f));
        Curve(editor, "emoji_pop", Body, MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.08f, 1f), L(0.25f, 1f));

        // 그림을 넘기는 이모지의 등장 — 톡 튀어나오되 첫 장만 보인다(2~4번 조각은 꺼 둔다).
        editor.AddMotion("emoji_pop_frames", Rig, 0.25f, MotionEnd.Commit, Abs);
        Scale(editor, "emoji_pop_frames", Body, K(0f, 0f), K(0.15f, 1.2f), K(0.25f, 1f));
        Curve(editor, "emoji_pop_frames", Body, MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.08f, 1f), L(0.25f, 1f));

        for (int i = 2; i <= 4; i++)
        {
            Curve(editor, "emoji_pop_frames", Part(i), MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.25f, 0f));
        }

        // 위에서 내려앉는다(땀·빗선).
        editor.AddMotion("emoji_drop", Rig, 0.3f, MotionEnd.Commit, Abs);
        Curve(editor, "emoji_drop", Body, MotionChannelKind.Position, Axis.Y, K(0f, 40f), K(0.3f, 0f));
        Curve(editor, "emoji_drop", Body, MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.15f, 1f), L(0.3f, 1f));

        // 꽃 셋이 차례로 피며 제자리로 흩어진다.
        editor.AddMotion("blossom_in", Rig, 0.5f, MotionEnd.Commit, Abs);
        (float X, float Y, float Size, float At)[] petals = { (-150f, 40f, 1f, 0f), (150f, -30f, 0.8f, 0.12f), (120f, 130f, 0.55f, 0.24f) };

        for (int i = 0; i < petals.Length; i++)
        {
            (float x, float y, float size, float at) = petals[i];
            Curve(editor, "blossom_in", Part(i + 1), MotionChannelKind.Position, Axis.X, L(0f, x), L(0.5f, x));
            Curve(editor, "blossom_in", Part(i + 1), MotionChannelKind.Position, Axis.Y, L(0f, y), L(0.5f, y));
            KeyAsset[] bloom = at > 0f
                ? new[] { K(0f, 0f, TangentMode.Flat), K(at, 0f, TangentMode.Flat), K(at + 0.14f, size * 1.25f), K(at + 0.22f, size), K(0.5f, size, TangentMode.Flat) }
                : new[] { K(0f, 0f, TangentMode.Flat), K(0.16f, size * 1.25f), K(0.26f, size), K(0.5f, size, TangentMode.Flat) };
            Scale(editor, "blossom_in", Part(i + 1), bloom);
            Curve(editor, "blossom_in", Part(i + 1), MotionChannelKind.Euler, Axis.Z, K(0f, -40f), K(0.5f, 0f));
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
        editor.AddMotion("emoji_drip", Rig, 0.9f, MotionEnd.Commit, Abs); // 주르륵 — 흘러내리며 사라졌다가 다시
        Curve(editor, "emoji_drip", Body, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.9f, -28f));
        Curve(editor, "emoji_drip", Body, MotionChannelKind.Alpha, Axis.X, L(0f, 1f), L(0.6f, 1f), L(0.9f, 0f));

        editor.AddMotion("emoji_float", Rig, 1.2f, MotionEnd.Commit, Abs); // 떠오르며 흐려졌다가 다시
        Curve(editor, "emoji_float", Body, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1.2f, 18f));
        Curve(editor, "emoji_float", Body, MotionChannelKind.Position, Axis.X, K(0f, 0f), K(0.4f, 5f), K(0.8f, -5f), K(1.2f, 0f));
        Curve(editor, "emoji_float", Body, MotionChannelKind.Alpha, Axis.X, L(0f, 1f), L(0.8f, 1f), L(1.2f, 0.35f));

        // 그림 넘기기 — 조각마다 제 차례에만 보인다.
        Frames(editor, "emoji_frames2", 0.6f, 2);
        Frames(editor, "emoji_frames3", 0.6f, 3);
        editor.AddMotion("dots_frames", Rig, 2f, MotionEnd.Commit, Abs); // 빈 풍선 → 점 하나 → 둘 → 셋(오래)
        float[] turns = { 0f, 0.35f, 0.7f, 1.05f, 2f };

        for (int i = 1; i <= 4; i++)
        {
            Curve(editor, "dots_frames", Part(i), MotionChannelKind.Alpha, Axis.X, Shown(turns[i - 1], turns[i], 2f));
        }

        editor.AddMotion("blossom_sway", Rig, 1f); // 꽃이 살랑
        for (int i = 1; i <= 3; i++)
        {
            float lean = i == 2 ? -7f : 7f;
            Curve(editor, "blossom_sway", Part(i), MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(0.5f, lean), K(1f, 0f));
        }

        // ── 퇴장(한 번) ──
        editor.AddMotion("emoji_fade", Rig, 0.25f, MotionEnd.Commit, Abs);
        Curve(editor, "emoji_fade", Body, MotionChannelKind.Alpha, Axis.X, L(0f, 1f), L(0.25f, 0f));

        editor.AddMotion("emoji_pop_out", Rig, 0.15f, MotionEnd.Commit, Abs);
        Scale(editor, "emoji_pop_out", Body, K(0f, 1f), K(0.05f, 1.15f), K(0.15f, 0f));
    }

    private static void Frames(MotionEditor editor, string name, float duration, int count)
    {
        editor.AddMotion(name, Rig, duration, MotionEnd.Commit, MotionChannelMode.Absolute);

        for (int i = 1; i <= count; i++)
        {
            Curve(editor, name, Part(i), MotionChannelKind.Alpha, Axis.X, Shown(duration * (i - 1) / count, duration * i / count, duration));
        }
    }

    private static void Emoji(
        MotionEditor editor, string key, string[] art, string enter, string? hold, string exit, float? seconds, float x, float y, string locator = "faceAura")
    {
        editor.AddSkin(key, Rig);

        for (int i = 0; i < art.Length; i++)
        {
            editor.SetSkinPart(key, Part(i + 1), $"art/{art[i]}.png");
        }

        editor.AddEmoji(key, Rig);
        editor.UpdateEmoji(key, new EmojiAsset { Skin = key, Enter = enter, Hold = hold, Exit = exit, HoldSeconds = seconds, OffsetX = x, OffsetY = y, Locator = locator });
    }

    private static void Emojis(MotionEditor editor)
    {
        const float R = 190f;  // 머리 오른쪽
        const float Up = 20f;  // 머리 옆, 살짝 위
        string[] One(string name) => new[] { name };

        // 잠깐 뜨는 것(머무는 시간이 있다)
        Emoji(editor, "느낌물음표", One("exclaim_question"), "emoji_pop", "emoji_wobble", "emoji_fade", 1.2f, R, Up);
        Emoji(editor, "반짝별", One("sparkle"), "emoji_pop", "emoji_beat", "emoji_pop_out", 1f, R, Up);
        Emoji(editor, "큰땀삐질", One("big_sweat"), "emoji_drop", "emoji_drip", "emoji_fade", 1.8f, R, 40f);
        Emoji(editor, "난감빗선", One("gloom_lines"), "emoji_drop", null, "emoji_fade", 1.5f, -R, Up);
        Emoji(editor, "빠직", One("anger"), "emoji_pop", "emoji_beat", "emoji_fade", 1.25f, R, Up);
        Emoji(editor, "팟놀란왕관", One("shock_crown"), "emoji_pop", null, "emoji_fade", 0.8f, -R, Up + 40f);
        Emoji(editor, "아이디어전구", One("idea_bulb"), "emoji_pop", "emoji_bob", "emoji_fade", 1.6f, 0f, Up + 110f);
        Emoji(editor, "빠밤작대기들", One("tada_lines"), "emoji_pop", "emoji_beat", "emoji_fade", 1f, R, Up + 40f);
        Emoji(editor, "빨간경고막대기", One("alert"), "emoji_pop", "emoji_shake", "emoji_fade", 0.96f, R, Up);
        Emoji(editor, "띠링물음표", One("question"), "emoji_pop", "emoji_wobble", "emoji_fade", 1.2f, R, Up);
        Emoji(editor, "후우하는구름", One("sigh_cloud"), "emoji_pop", "emoji_float", "emoji_fade", 1.2f, 170f, -60f, "face");
        Emoji(editor, "화내는부우구름", One("huff_cloud"), "emoji_pop", "emoji_float", "emoji_fade", 1.2f, 170f, -60f, "face");
        Emoji(editor, "쑥스럼풍선_우", One("shy_r"), "emoji_pop", "emoji_bob", "emoji_fade", 1.6f, R, Up);
        Emoji(editor, "쑥스럼풍선_좌", One("shy_l"), "emoji_pop", "emoji_bob", "emoji_fade", 1.6f, -R, Up);

        // 그림을 넘기는 것(조각 = 장)
        Emoji(editor, "난감실타래", new[] { "tangle_1", "tangle_2" }, "emoji_pop_frames", "emoji_frames2", "emoji_fade", 1.8f, -R, Up);
        Emoji(editor, "왁자지껄", new[] { "chatter_1", "chatter_2" }, "emoji_pop_frames", "emoji_frames2", "emoji_fade", 1.8f, R, Up);
        Emoji(editor, "삐질삐질땀방울", new[] { "sweat_1", "sweat_2", "sweat_3" }, "emoji_pop_frames", "emoji_frames3", "emoji_fade", 1.8f, R, 40f);

        // 끌 때까지 머무는 것(emoji_off)
        Emoji(editor, "플러팅하트", One("heart"), "emoji_pop", "emoji_beat", "emoji_pop_out", null, R, Up);
        Emoji(editor, "곰곰생각구름", One("think_cloud"), "emoji_pop", "emoji_bob", "emoji_fade", null, R, Up + 40f);
        Emoji(editor, "ZZZ", One("zzz"), "emoji_pop", "emoji_float", "emoji_fade", null, R, Up);
        Emoji(editor, "신나는음표", One("note"), "emoji_pop", "emoji_bob", "emoji_fade", null, R, Up);
        Emoji(editor, "말줄임점풍선_우", new[] { "dots_r_1", "dots_r_2", "dots_r_3", "dots_r_4" }, "emoji_pop_frames", "dots_frames", "emoji_fade", null, R, Up);
        Emoji(editor, "말줄임점풍선_좌", new[] { "dots_l_1", "dots_l_2", "dots_l_3", "dots_l_4" }, "emoji_pop_frames", "dots_frames", "emoji_fade", null, -R, Up);
        Emoji(editor, "행복한벚꽃", new[] { "blossom", "blossom", "blossom" }, "blossom_in", "blossom_sway", "emoji_fade", null, 0f, 0f);
    }

    /// <summary>캐릭터 쪽 — 이벤트 키로 이모지를 띄우는 모션 셋과, 그것을 이어 놓은 클립.</summary>
    private static void Character(MotionEditor editor)
    {
        const string Hop = "CharacterPortrait_Track_Move_Y";

        editor.AddMotion("react_surprise", SceneObject.DefaultRig, 0.5f);
        Curve(editor, "react_surprise", Hop, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.12f, 26f), K(0.3f, 0f), K(0.5f, 0f));
        editor.AddMotionEvent("react_surprise", 0.05f, "emoji", "빨간경고막대기");

        editor.AddMotion("react_happy", SceneObject.DefaultRig, 1.2f);
        Curve(editor, "react_happy", Hop, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.2f, 14f), K(0.4f, 0f), K(0.6f, 14f), K(0.8f, 0f), K(1.2f, 0f));
        editor.AddMotionEvent("react_happy", 0.1f, "emoji", "행복한벚꽃");

        editor.AddMotion("react_think", SceneObject.DefaultRig, 0.8f);
        Curve(editor, "react_think", Hop, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.4f, -10f), K(0.8f, 0f));
        editor.AddMotionEvent("react_think", 0.2f, "emoji", "말줄임점풍선_우");

        editor.AddMotion("react_calm", SceneObject.DefaultRig, 0.4f);
        Curve(editor, "react_calm", Hop, MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.2f, -6f), K(0.4f, 0f));
        editor.AddMotionEvent("react_calm", 0f, "emoji_off");

        // 놀람 → 기쁨(꽃이 머문다) → 진정(꽃이 진다) → 생각(점 풍선이 머문다) → 진정.
        editor.AddClip("emoji_demo");
        editor.AddClipRole("emoji_demo", "a", SceneObject.DefaultRig, "s1");
        editor.AddClipTrack("emoji_demo", "a");
        editor.AddClipBlock("emoji_demo", 0, "react_surprise", 0.3f);
        editor.AddClipBlock("emoji_demo", 0, "react_happy", 2f);
        editor.AddClipBlock("emoji_demo", 0, "react_calm", 4.5f);
        editor.AddClipBlock("emoji_demo", 0, "react_think", 5.5f);
        editor.AddClipBlock("emoji_demo", 0, "react_calm", 9f);
    }
}
