using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 이모지 정의 명령(PLAN-parts.md §8) — 각각 되돌리기 한 칸. 참조(스킨·모션)의 옳고 그름은 검증이 알린다.
public sealed partial class MotionEditor
{
    public EmojiAsset AddEmoji(string name, string rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        string key = RequireEmojiName(name);

        if (rig.Length == 0)
        {
            throw new InvalidOperationException("이모지에는 리그(rigKind)가 있어야 한다.");
        }

        var emoji = new EmojiAsset { Name = key, Rig = rig };
        Mutate(MotionChangeKind.Scene, () => Document.Emojis.Add(emoji));
        return emoji;
    }

    public void RemoveEmoji(string name)
    {
        EmojiAsset emoji = RequireEmoji(name);
        Mutate(MotionChangeKind.Scene, () => Document.Emojis.Remove(emoji));
    }

    public void RenameEmoji(string name, string newName)
    {
        EmojiAsset emoji = RequireEmoji(name);

        if (string.Equals(name, newName?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string key = RequireEmojiName(newName);
        Mutate(MotionChangeKind.Scene, () => emoji.Name = key);
    }

    /// <summary>
    /// 정의의 값을 통째로 바꾼다(인스펙터의 한 길) — 이름과 리그는 여기서 못 바꾼다. 빈 글은 "없음"(null)으로 적는다.
    /// </summary>
    public void UpdateEmoji(string name, EmojiAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EmojiAsset emoji = RequireEmoji(name);

        if (value.HoldSeconds is { } seconds && (!float.IsFinite(seconds) || seconds < 0f))
        {
            throw new InvalidOperationException($"머무는 시간은 0 이상의 수다 ('{seconds}')");
        }

        if (!float.IsFinite(value.OffsetX) || !float.IsFinite(value.OffsetY))
        {
            throw new InvalidOperationException("오프셋은 수여야 한다.");
        }

        Mutate(MotionChangeKind.Scene, () =>
        {
            emoji.Skin = Blank(value.Skin);
            emoji.Enter = Blank(value.Enter);
            emoji.Hold = Blank(value.Hold);
            emoji.Exit = Blank(value.Exit);
            emoji.HoldSeconds = value.HoldSeconds;
            emoji.AnchorNode = string.IsNullOrWhiteSpace(value.AnchorNode) ? EmojiAsset.DefaultAnchorNode : value.AnchorNode.Trim();
            emoji.OffsetX = value.OffsetX;
            emoji.OffsetY = value.OffsetY;
            emoji.Locator = Blank(value.Locator);
        });
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>모션 이름이 바뀌면 이모지의 등장·머묾·퇴장 참조가 같은 칸에서 따라온다.</summary>
    private void RetargetEmojiMotions(string name, string newName)
    {
        foreach (EmojiAsset emoji in Document.Emojis)
        {
            emoji.Enter = emoji.Enter == name ? newName : emoji.Enter;
            emoji.Hold = emoji.Hold == name ? newName : emoji.Hold;
            emoji.Exit = emoji.Exit == name ? newName : emoji.Exit;
        }
    }

    private void RetargetEmojiSkins(string name, string newName)
    {
        foreach (EmojiAsset emoji in Document.Emojis.Where(e => e.Skin == name))
        {
            emoji.Skin = newName;
        }
    }

    private string RequireEmojiName(string? name)
    {
        string key = (name ?? "").Trim();

        if (key.Length == 0 || key.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"이모지 키는 빈칸 없는 글이어야 한다 ('{name}') — 대본이 이 이름으로 부른다.");
        }

        if (Document.FindEmoji(key) is not null)
        {
            throw new InvalidOperationException($"이모지 '{key}'가 이미 있다.");
        }

        return key;
    }

    private EmojiAsset RequireEmoji(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Document.FindEmoji(name) ?? throw new KeyNotFoundException($"이모지 '{name}'가 없다.");
    }
}
