using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시 문서(<c>examples/emoji</c>)를 짓는다 — 런타임의 견본 셋(<c>Assets/Dialogue/emojis.json</c>, 런타임 <c>32858197</c>)을
/// 편집 명령으로 다시 지은 것(PLAN-emoji.md E7). 런타임의 클립을 한 타임라인에 이어 놓고, 머무는 이모지는 구간으로 다시 자른다.
/// 2026-11-18 30fps로 옮겼다 — 24fps의 프레임 n은 round(n × 1.25)(0.5는 올림, <c>tools/authoring-24to30.py</c>와 같은 규칙).
/// 이 빌더가 낸 <c>emojis.json</c>(fps 30)이 런타임의 새 견본이 된다(<c>Fixtures/runtime-emojis.json</c>).
/// 키는 전부 flat(기울기 0 — 런타임 파일이 기울기를 적지 않았다). 고친 뒤 <c>MT_WRITE_EXAMPLES=1</c>로 예시 파일을 다시 쓴다.
/// </summary>
internal static class EmojiExamples
{
    public const string DocumentFile = "emoji-examples.motion-authoring.json";
    public const string ExportFile = "emojis.json";

    private static readonly EmojiRigFrame Frame = EmojiRigFrame.Builtin;

    public static MotionEditor Build()
    {
        var editor = new MotionEditor(new MotionDocument());
        Star(editor);
        Question(editor);
        Twinkle(editor);
        return editor;
    }

    /// <summary>star — 등장(0~5) · 머묾(6~25, 숨쉬듯 부푼다) · 퇴장(26~33, 작아지며 전체가 사라진다).</summary>
    private static void Star(MotionEditor editor)
    {
        const string name = "star";
        editor.AddEmoji(name, EmojiAsset.SecondsOf(33));
        editor.SetEmojiOffset(name, 170f, 60f);
        editor.AddEmojiRig(name, "body", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "body", 0f, 0f, 0.45f, 0f);

        Keys(editor, name, "body", ChannelKind.Scale, Both, (0, 0.45f), (4, 1.27f), (5, 1f), (6, 1f), (11, 1.08f), (16, 1f), (25, 1f), (26, 1f), (33, 0.67f));
        Keys(editor, name, "body", ChannelKind.Alpha, X, (0, 0f), (3, 1f));
        Keys(editor, name, "", ChannelKind.Alpha, X, (26, 1f), (33, 0f));

        editor.AddEmojiSpan(name, "in", 0, 5);
        editor.AddEmojiSpan(name, "hold", 6, 25);
        editor.AddEmojiSpan(name, "out", 26, 33);
        editor.SetEmojiLinks(name, "in", "hold", "out");
    }

    /// <summary>question — 한 번(main): 튀어나와 흔들리고 떠오르며 사라진다. 물음표는 뒤집히면 안 되는 그림이다(upright — 런타임 32858197).</summary>
    private static void Question(MotionEditor editor)
    {
        const string name = "question";
        editor.AddEmoji(name, EmojiAsset.SecondsOf(26));
        editor.SetEmojiOffset(name, -170f, 40f);
        editor.AddEmojiRig(name, "mark", new[] { "art/question.png" });
        editor.SetEmojiRigPlace(name, "mark", 0f, 0f, 0.45f, 0f);
        editor.SetEmojiRigUpright(name, "mark", true);

        Keys(editor, name, "mark", ChannelKind.Scale, Both, (0, 0.45f), (4, 1.12f), (6, 1f), (26, 1f));
        Keys(editor, name, "mark", ChannelKind.Euler, Z, (0, -20f), (5, 8f), (10, -4f), (15, 0f));
        Keys(editor, name, "mark", ChannelKind.Alpha, X, (0, 0f), (3, 1f), (21, 1f), (26, 0f));
        Keys(editor, name, "", ChannelKind.Position, Y, (0, 0f), (26, 20f));
    }

    /// <summary>twinkle — 별 셋이 차례로 켜졌다 꺼지고(main) 전체가 떠오른다. 같은 그림을 리그 셋이 쓴다.</summary>
    private static void Twinkle(MotionEditor editor)
    {
        const string name = "twinkle";
        editor.AddEmoji(name, EmojiAsset.SecondsOf(29));
        editor.SetEmojiOffset(name, 0f, 150f);
        editor.AddEmojiRig(name, "left", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "left", -150f, -30f, 0.3f, 0f);
        editor.AddEmojiRig(name, "center", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "center", 0f, 40f, 0.3f, 0f);
        editor.AddEmojiRig(name, "right", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "right", 150f, -30f, 0.3f, 15f);

        Keys(editor, name, "left", ChannelKind.Scale, Both, (0, 0.35f), (4, 1f), (29, 1f));
        Keys(editor, name, "left", ChannelKind.Alpha, X, (0, 0f), (4, 1f), (16, 1f), (23, 0f));
        Keys(editor, name, "center", ChannelKind.Scale, Both, (0, 0.35f), (4, 0.35f), (8, 1f), (29, 1f));
        Keys(editor, name, "center", ChannelKind.Alpha, X, (0, 0f), (4, 0f), (8, 1f), (20, 1f), (26, 0f));
        Keys(editor, name, "right", ChannelKind.Scale, Both, (0, 0.35f), (8, 0.35f), (11, 1f), (29, 1f));
        Keys(editor, name, "right", ChannelKind.Alpha, X, (0, 0f), (8, 0f), (11, 1f), (24, 1f), (29, 0f));
        Keys(editor, name, "", ChannelKind.Position, Y, (0, 0f), (29, 30f));
    }

    private static readonly Axis[] X = { Axis.X };
    private static readonly Axis[] Y = { Axis.Y };
    private static readonly Axis[] Z = { Axis.Z };
    private static readonly Axis[] Both = { Axis.X, Axis.Y };

    /// <summary>타임라인의 그 대상(""= 이모지 전체)에 키를 — 프레임과 값, flat.</summary>
    private static void Keys(MotionEditor editor, string emoji, string rig, ChannelKind kind, Axis[] axes, params (int Frame, float Value)[] keys)
    {
        string timeline = EmojiAsset.TimelineNameOf(emoji);
        string node = Frame.TargetNode(rig);
        editor.AddChannel(timeline, node, kind);

        foreach (Axis axis in axes)
        {
            editor.SetKeys(timeline, node, kind, axis, keys.Select(k => new KeyAsset
            {
                T = EmojiAsset.SecondsOf(k.Frame),
                V = k.Value,
                Tangent = TangentMode.Flat,
            }).ToArray());
        }
    }
}
