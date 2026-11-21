using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 그래프 에디터가 편집기에 바라는 것 전부 — 모션을 읽고, 키를 고치고, 바뀌면 알림을 받는다.
/// 화면(<c>GraphEditorView</c>)은 이것만 안다: 저작본의 편집기(<c>MotionEditor</c>)도, 다른 툴의 편집기도 이것을 내놓으면 같은 그래프를 쓴다.
/// 고치는 메서드 하나가 되돌리기 한 칸이다 — 여럿을 한 칸으로 묶으려면 <see cref="Batch"/>.
/// </summary>
public interface IGraphEditor
{
    /// <summary>그 이름의 모션. 없으면 null.</summary>
    MotionAsset? FindMotion(string name);

    /// <summary>모션이 바뀌었다 — 그래프가 커브를 다시 읽는다.</summary>
    event EventHandler? GraphChanged;

    /// <summary>여러 커브의 키를 한 번에 갈아 끼운다.</summary>
    void SetKeysMany(string name, IEnumerable<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> edits);

    void SetTangentMode(string name, IEnumerable<KeyRef> keys, TangentMode mode);

    void SetBroken(string name, IEnumerable<KeyRef> keys, bool broken);

    void SetTangents(string name, KeyRef key, float inTangent, float outTangent);

    /// <summary>복사해 둔 키를 그 시각에 붙이고, 붙은 키를 돌려준다.</summary>
    List<KeyRef> PasteKeys(string name, KeyClipboard clipboard, float atTime, Func<float, float>? snap = null, IReadOnlyList<CurveRef>? targets = null);

    void RemoveKeys(string name, IEnumerable<KeyRef> keys);

    /// <summary>키를 고치는 일 여럿을 되돌리기 한 칸으로 묶는다.</summary>
    void Batch(Action change);
}
