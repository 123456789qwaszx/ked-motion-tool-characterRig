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
            }
            else
            {
                skin.Parts[node] = image;
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
