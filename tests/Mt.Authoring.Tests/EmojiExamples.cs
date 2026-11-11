using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시 문서(<c>examples/emoji</c>)를 짓는다 — 런타임의 견본 셋(<c>Assets/Dialogue/emojis.json</c>, 런타임 <c>007d4c16</c>)을
/// 편집 명령으로 다시 지은 것(PLAN-emoji.md E7). 런타임의 클립을 한 타임라인에 이어 놓고, 머무는 이모지는 구간으로 다시 자른다.
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

    /// <summary>star — 등장(0~4) · 머묾(5~20, 숨쉬듯 부푼다) · 퇴장(21~26, 작아지며 전체가 사라진다).</summary>
    private static void Star(MotionEditor editor)
    {
        const string name = "star";
        editor.AddEmoji(name, EmojiAsset.SecondsOf(26));
        editor.SetEmojiOffset(name, 170f, 60f);
        editor.AddEmojiRig(name, "body", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "body", 0f, 0f, 0.45f, 0f);

        Keys(editor, name, "body", ChannelKind.Scale, Both, (0, 0.45f), (3, 1.27f), (4, 1f), (5, 1f), (9, 1.08f), (13, 1f), (20, 1f), (21, 1f), (26, 0.67f));
        Keys(editor, name, "body", ChannelKind.Alpha, X, (0, 0f), (2, 1f));
        Keys(editor, name, "", ChannelKind.Alpha, X, (21, 1f), (26, 0f));

        editor.AddEmojiSpan(name, "in", 0, 4);
        editor.AddEmojiSpan(name, "hold", 5, 20);
        editor.AddEmojiSpan(name, "out", 21, 26);
        editor.SetEmojiLinks(name, "in", "hold", "out");
    }

    /// <summary>question — 한 번(main): 튀어나와 흔들리고 떠오르며 사라진다.</summary>
    private static void Question(MotionEditor editor)
    {
        const string name = "question";
        editor.AddEmoji(name, EmojiAsset.SecondsOf(21));
        editor.SetEmojiOffset(name, -170f, 40f);
        editor.AddEmojiRig(name, "mark", new[] { "art/question.png" });
        editor.SetEmojiRigPlace(name, "mark", 0f, 0f, 0.45f, 0f);

        Keys(editor, name, "mark", ChannelKind.Scale, Both, (0, 0.45f), (3, 1.12f), (5, 1f), (21, 1f));
        Keys(editor, name, "mark", ChannelKind.Euler, Z, (0, -20f), (4, 8f), (8, -4f), (12, 0f));
        Keys(editor, name, "mark", ChannelKind.Alpha, X, (0, 0f), (2, 1f), (17, 1f), (21, 0f));
        Keys(editor, name, "", ChannelKind.Position, Y, (0, 0f), (21, 20f));
    }

    /// <summary>twinkle — 별 셋이 차례로 켜졌다 꺼지고(main) 전체가 떠오른다. 같은 그림을 리그 셋이 쓴다.</summary>
    private static void Twinkle(MotionEditor editor)
    {
        const string name = "twinkle";
        editor.AddEmoji(name, EmojiAsset.SecondsOf(23));
        editor.SetEmojiOffset(name, 0f, 150f);
        editor.AddEmojiRig(name, "left", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "left", -150f, -30f, 0.3f, 0f);
        editor.AddEmojiRig(name, "center", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "center", 0f, 40f, 0.3f, 0f);
        editor.AddEmojiRig(name, "right", new[] { "art/star.png" });
        editor.SetEmojiRigPlace(name, "right", 150f, -30f, 0.3f, 15f);

        Keys(editor, name, "left", ChannelKind.Scale, Both, (0, 0.35f), (3, 1f), (23, 1f));
        Keys(editor, name, "left", ChannelKind.Alpha, X, (0, 0f), (3, 1f), (13, 1f), (18, 0f));
        Keys(editor, name, "center", ChannelKind.Scale, Both, (0, 0.35f), (3, 0.35f), (6, 1f), (23, 1f));
        Keys(editor, name, "center", ChannelKind.Alpha, X, (0, 0f), (3, 0f), (6, 1f), (16, 1f), (21, 0f));
        Keys(editor, name, "right", ChannelKind.Scale, Both, (0, 0.35f), (6, 0.35f), (9, 1f), (23, 1f));
        Keys(editor, name, "right", ChannelKind.Alpha, X, (0, 0f), (6, 0f), (9, 1f), (19, 1f), (23, 0f));
        Keys(editor, name, "", ChannelKind.Position, Y, (0, 0f), (23, 30f));
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
