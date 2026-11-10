using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 스킨 명령(PLAN-parts.md P1) — 각각 되돌리기 한 칸. 조각 노드가 그 리그에 있는지는 검증·화면의 일이다(편집기는 덤프를 모른다).
public sealed partial class MotionEditor
{
    public SkinAsset AddSkin(string name, string rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        string trimmed = RequireSkinName(name);

        if (rig.Length == 0)
        {
            throw new InvalidOperationException("스킨에는 리그(rigKind)가 있어야 한다.");
        }

        var skin = new SkinAsset { Name = trimmed, Rig = rig };
        Mutate(MotionChangeKind.Scene, () => Document.Skins.Add(skin));
        return skin;
    }

    /// <summary>스킨을 지운다 — 그 스킨을 입은 오브젝트는 같은 칸에서 벗는다(없는 이름을 달고 남지 않게).</summary>
    public void RemoveSkin(string name)
    {
        SkinAsset skin = RequireSkin(name);
        Mutate(MotionChangeKind.Scene, () =>
        {
            Document.Skins.Remove(skin);

            foreach (EmojiAsset emoji in Document.Emojis.Where(e => e.SkinFlipped == name))
            {
                emoji.SkinFlipped = null; // 반전 판만 없어진다 — 그림(Skin)이 없어진 것은 검증이 알린다
            }

            foreach (SceneObject o in Wearers(name))
            {
                o.Skin = null;
            }
        });
    }

    /// <summary>이름 바꾸기 — 입고 있던 오브젝트가 같은 칸에서 따라온다.</summary>
    public void RenameSkin(string name, string newName)
    {
        SkinAsset skin = RequireSkin(name);

        if (string.Equals(name, newName?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string trimmed = RequireSkinName(newName);
        Mutate(MotionChangeKind.Scene, () =>
        {
            foreach (SceneObject o in Wearers(name))
            {
                o.Skin = trimmed;
            }

            skin.Name = trimmed;
            RetargetEmojiSkins(name, trimmed);
        });
    }

    /// <summary>조각의 그림(문서 기준 상대 경로). null·빈 문자열이면 그 조각을 비운다(안 그린다).</summary>
    public void SetSkinPart(string name, string node, string? image)
    {
        ArgumentNullException.ThrowIfNull(node);
        SkinAsset skin = RequireSkin(name);

        if (node.Length == 0)
        {
            throw new InvalidOperationException("조각 노드 id가 비었다.");
        }

        Mutate(MotionChangeKind.Scene, () =>
        {
            if (string.IsNullOrEmpty(image))
            {
                skin.Parts.Remove(node);
                skin.Setups.Remove(node); // 그림이 없는 조각에는 넘길 장도 놓임도 없다
            }
            else
            {
                skin.Parts[node] = image;
            }
        });
    }

    /// <summary>
    /// 조각의 장들을 한 번에 — 첫 장이 그 조각의 그림, 나머지가 넘길 장이다(한 칸). 비면 그 조각을 비운다. 장당 시간·놓임은 그대로 둔다.
    /// </summary>
    public void SetSkinPartImages(string name, string node, IReadOnlyList<string> images)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(images);
        SkinAsset skin = RequireSkin(name);
        List<string> list = images.Select(i => (i ?? "").Trim()).Where(i => i.Length > 0).ToList();

        if (node.Length == 0)
        {
            throw new InvalidOperationException("조각 노드 id가 비었다.");
        }

        if (skin.ImagesOf(node).SequenceEqual(list))
        {
            return;
        }

        Mutate(MotionChangeKind.Scene, () =>
        {
            if (list.Count == 0)
            {
                skin.Parts.Remove(node);
                skin.Setups.Remove(node);
                return;
            }

            skin.Parts[node] = list[0];
            SkinPartSetup setup = skin.SetupOf(node).Clone();
            setup.Frames = list.Skip(1).ToList();

            if (setup.IsDefault)
            {
                skin.Setups.Remove(node);
            }
            else
            {
                skin.Setups[node] = setup;
            }
        });
    }

    /// <summary>
    /// 조각의 덧붙임(그림 넘기기 · 놓임)을 고친다 — 그림이 있는 조각에만. 기본값뿐이면 지운다. 같은 값은 무기록.
    /// </summary>
    public void SetSkinPartSetup(string name, string node, SkinPartSetup value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(value);
        SkinAsset skin = RequireSkin(name);

        if (!skin.Parts.ContainsKey(node))
        {
            throw new InvalidOperationException($"스킨 '{name}'의 조각 '{node}'에 그림이 없다 — 먼저 그림을 준다.");
        }

        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Rotation) || !float.IsFinite(value.Scale) || value.Scale <= 0f)
        {
            throw new InvalidOperationException("놓임의 자리·회전은 수, 크기는 0보다 큰 수다.");
        }

        if (!float.IsFinite(value.FrameSeconds) || value.FrameSeconds <= 0f)
        {
            throw new InvalidOperationException($"장당 시간은 0보다 큰 수다 ('{value.FrameSeconds}')");
        }

        SkinPartSetup next = value.Clone();
        next.Frames = next.Frames.Select(f => (f ?? "").Trim()).Where(f => f.Length > 0).ToList();
        SkinPartSetup now = skin.SetupOf(node);

        if (now.Frames.SequenceEqual(next.Frames) && now.FrameSeconds == next.FrameSeconds && now.Loop == next.Loop
            && now.X == next.X && now.Y == next.Y && now.Scale == next.Scale && now.Rotation == next.Rotation)
        {
            return;
        }

        Mutate(MotionChangeKind.Scene, () =>
        {
            if (next.IsDefault)
            {
                skin.Setups.Remove(node);
            }
            else
            {
                skin.Setups[node] = next;
            }
        });
    }

    /// <summary>오브젝트에 스킨을 입힌다. null이면 벗긴다. 리그가 다른 스킨은 못 입는다.</summary>
    public void SetObjectSkin(string? sceneName, string id, string? skin)
    {
        SceneAsset scene = RequireScene(sceneName);
        SceneObject o = RequireObject(scene, id);

        if (skin is not null)
        {
            SkinAsset asset = RequireSkin(skin);

            if (!string.Equals(asset.Rig, o.Rig, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"스킨 '{skin}'은 리그 '{asset.Rig}'의 것이다 — 오브젝트 '{id}'는 '{o.Rig}'다.");
            }
        }

        Mutate(MotionChangeKind.Scene, () => o.Skin = skin);
    }

    private IEnumerable<SceneObject> Wearers(string skin)
        => Document.Scenes.SelectMany(s => s.Objects).Where(o => string.Equals(o.Skin, skin, StringComparison.Ordinal));

    private string RequireSkinName(string? name)
    {
        string trimmed = (name ?? "").Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("스킨 이름이 비었다.");
        }

        if (Document.FindSkin(trimmed) is not null)
        {
            throw new InvalidOperationException($"스킨 '{trimmed}'이 이미 있다.");
        }

        return trimmed;
    }

    private SkinAsset RequireSkin(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Document.FindSkin(name) ?? throw new KeyNotFoundException($"스킨 '{name}'이 없다.");
    }
}
