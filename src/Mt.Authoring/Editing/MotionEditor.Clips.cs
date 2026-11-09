using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 연출 클립 명령(PLAN-clip.md K1). 각각 되돌리기 한 칸이고, 같은 값이면 흔적이 없다(Mutate가 지킨다).
// 구조(이름·참조하는 배역·모션의 존재)는 여기서 지키고, 규칙(겹침·리그 맞춤·길이 안)은 검증이 알린다.
public sealed partial class MotionEditor
{
    // ── 클립 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 빈 클립을 더한다. 씬을 안 주면 **지금의** 활성 씬이 시작 상태로 적힌다 — 나중에 활성 씬이 바뀌어도
    /// 클립의 시작 상태(와 배역이 묶인 오브젝트)는 그대로다.
    /// </summary>
    public ClipAsset AddClip(string name, string? sceneName = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        RequireClipName(name);

        var clip = new ClipAsset { Name = name, Scene = RequireScene(sceneName).Name };
        Mutate(MotionChangeKind.Clip, () => Document.Clips.Add(clip));
        return clip;
    }

    public void RemoveClip(string name)
    {
        ClipAsset clip = RequireClip(name);
        Mutate(MotionChangeKind.Clip, () => Document.Clips.Remove(clip));
    }

    public void RenameClip(string name, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        ClipAsset clip = RequireClip(name);

        if (string.Equals(name, newName, StringComparison.Ordinal))
        {
            return;
        }

        RequireClipName(newName);
        Mutate(MotionChangeKind.Clip, () => clip.Name = newName);
    }

    /// <summary>길이(초). null이면 마지막 블록의 끝을 따라간다. 블록은 옮기지 않는다 — 넘치는 블록은 검증이 알린다.</summary>
    public void SetClipDuration(string name, float? seconds)
    {
        ClipAsset clip = RequireClip(name);

        if (seconds is < 0f || seconds is { } s && !float.IsFinite(s))
        {
            throw new InvalidOperationException($"클립 길이는 0 이상의 수다 ('{seconds}')");
        }

        Mutate(MotionChangeKind.Clip, () => clip.Duration = seconds);
    }

    /// <summary>미리보기 시작 상태인 씬. 배역의 묶임은 그대로 둔다 — 새 씬에 그 id가 없으면 검증이 알린다.</summary>
    public void SetClipScene(string name, string sceneName)
    {
        ClipAsset clip = RequireClip(name);
        string scene = RequireScene(sceneName ?? throw new ArgumentNullException(nameof(sceneName))).Name;
        Mutate(MotionChangeKind.Clip, () => clip.Scene = scene);
    }

    // ── 배역 ──────────────────────────────────────────────────────────

    /// <summary>배역을 더한다. 오브젝트를 주면 미리보기에서 그 오브젝트가 맡는다(없는 id도 받는다 — 검증이 알린다).</summary>
    public ClipRole AddClipRole(string clipName, string role, string rig, string? objectId = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(rig);
        ClipAsset clip = RequireClip(clipName);
        RequireRoleName(clip, role);

        if (rig.Length == 0)
        {
            throw new InvalidOperationException("배역에는 리그(rigKind)가 있어야 한다.");
        }

        var added = new ClipRole { Name = role, Rig = rig, Object = objectId };
        Mutate(MotionChangeKind.Clip, () => clip.Roles.Add(added));
        return added;
    }

    /// <summary>배역을 지운다 — 그 배역의 트랙(과 블록)도 같은 칸에서 사라진다.</summary>
    public void RemoveClipRole(string clipName, string role)
    {
        ClipAsset clip = RequireClip(clipName);
        ClipRole target = RequireRole(clip, role);
        Mutate(MotionChangeKind.Clip, () =>
        {
            clip.Roles.Remove(target);
            clip.Tracks.RemoveAll(t => string.Equals(t.Role, role, StringComparison.Ordinal));
        });
    }

    /// <summary>배역 이름 바꾸기 — 그 배역의 트랙이 따라온다.</summary>
    public void RenameClipRole(string clipName, string role, string newRole)
    {
        ArgumentNullException.ThrowIfNull(newRole);
        ClipAsset clip = RequireClip(clipName);
        ClipRole target = RequireRole(clip, role);

        if (string.Equals(role, newRole, StringComparison.Ordinal))
        {
            return;
        }

        RequireRoleName(clip, newRole);
        Mutate(MotionChangeKind.Clip, () =>
        {
            target.Name = newRole;

            foreach (ClipTrack track in clip.Tracks.Where(t => string.Equals(t.Role, role, StringComparison.Ordinal)))
            {
                track.Role = newRole;
            }
        });
    }

    public void SetClipRoleRig(string clipName, string role, string rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ClipRole target = RequireRole(RequireClip(clipName), role);

        if (rig.Length == 0)
        {
            throw new InvalidOperationException("배역에는 리그(rigKind)가 있어야 한다.");
        }

        Mutate(MotionChangeKind.Clip, () => target.Rig = rig);
    }

    /// <summary>미리보기에서 이 배역을 맡을 씬 오브젝트. null이면 푼다.</summary>
    public void BindClipRole(string clipName, string role, string? objectId)
    {
        ClipRole target = RequireRole(RequireClip(clipName), role);
        Mutate(MotionChangeKind.Clip, () => target.Object = objectId);
    }

    // ── 트랙 ──────────────────────────────────────────────────────────

    /// <summary>배역의 트랙을 끝에 더하고 그 번호를 돌려준다.</summary>
    public int AddClipTrack(string clipName, string role)
    {
        ClipAsset clip = RequireClip(clipName);
        RequireRole(clip, role);
        Mutate(MotionChangeKind.Clip, () => clip.Tracks.Add(new ClipTrack { Role = role }));
        return clip.Tracks.Count - 1;
    }

    /// <summary>트랙을 지운다(블록째).</summary>
    public void RemoveClipTrack(string clipName, int track)
    {
        ClipAsset clip = RequireClip(clipName);
        ClipTrack target = RequireTrack(clip, track);
        Mutate(MotionChangeKind.Clip, () => clip.Tracks.Remove(target));
    }

    // ── 블록 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 모션 블록을 놓고 트랙 안 번호를 돌려준다. mode를 안 주면 그 모션의 재생 기준(뷰포트 렌즈)을 물려받는다.
    /// 시작 시각은 0 아래로 못 간다(0으로 붙는다). 겹침·리그 맞춤은 막지 않는다 — 검증의 일이다.
    /// </summary>
    public int AddClipBlock(string clipName, int track, string motion, float at, MotionChannelMode? mode = null)
    {
        ClipAsset clip = RequireClip(clipName);
        ClipTrack target = RequireTrack(clip, track);
        MotionAsset source = Require(motion);
        var block = new ClipBlock { At = ClampAt(at), Motion = motion, Mode = mode ?? source.PreviewMode };
        Mutate(MotionChangeKind.Clip, () => Insert(target, block));
        return target.Blocks.IndexOf(block);
    }

    /// <summary>
    /// 블록을 옮긴다 — 시각, 그리고 (주면) 다른 트랙으로. 옮긴 뒤의 (트랙, 번호)를 돌려준다(시각 순이라 번호가 바뀔 수 있다).
    /// </summary>
    public (int Track, int Index) MoveClipBlock(string clipName, int track, int index, float at, int? toTrack = null)
    {
        ClipAsset clip = RequireClip(clipName);
        ClipTrack from = RequireTrack(clip, track);
        ClipBlock block = RequireBlock(from, index);
        int destinationIndex = toTrack ?? track;
        ClipTrack to = RequireTrack(clip, destinationIndex);

        Mutate(MotionChangeKind.Clip, () =>
        {
            from.Blocks.Remove(block);
            block.At = ClampAt(at);
            Insert(to, block);
        });

        return (destinationIndex, to.Blocks.IndexOf(block));
    }

    public void SetClipBlockMode(string clipName, int track, int index, MotionChannelMode mode)
    {
        ClipBlock block = RequireBlock(RequireTrack(RequireClip(clipName), track), index);
        Mutate(MotionChangeKind.Clip, () => block.Mode = mode);
    }

    /// <summary>블록이 가리키는 모션을 바꾼다(자리는 그대로).</summary>
    public void SetClipBlockMotion(string clipName, int track, int index, string motion)
    {
        ClipBlock block = RequireBlock(RequireTrack(RequireClip(clipName), track), index);
        Require(motion);
        Mutate(MotionChangeKind.Clip, () => block.Motion = motion);
    }

    public void RemoveClipBlock(string clipName, int track, int index)
    {
        ClipTrack target = RequireTrack(RequireClip(clipName), track);
        ClipBlock block = RequireBlock(target, index);
        Mutate(MotionChangeKind.Clip, () => target.Blocks.Remove(block));
    }

    // ── 따라 바꾸기 — 이름을 바꾼 명령의 Mutate 안에서 부른다(같은 되돌리기 칸) ────────

    private void RetargetClipMotions(string name, string newName)
    {
        foreach (ClipBlock block in Document.Clips.SelectMany(c => c.Tracks).SelectMany(t => t.Blocks))
        {
            if (string.Equals(block.Motion, name, StringComparison.Ordinal))
            {
                block.Motion = newName;
            }
        }
    }

    /// <summary><paramref name="scene"/>을 시작 상태로 쓰는 클립의 배역 묶임을 새 id로(씬을 안 적은 손글씨 클립은 활성 씬).</summary>
    private void RetargetClipObjects(SceneAsset scene, string id, string newId)
    {
        foreach (ClipAsset clip in Document.Clips)
        {
            SceneAsset? start = clip.Scene is null ? Document.ActiveSceneAsset : Document.FindScene(clip.Scene);

            if (!ReferenceEquals(start, scene))
            {
                continue;
            }

            foreach (ClipRole role in clip.Roles)
            {
                if (string.Equals(role.Object, id, StringComparison.Ordinal))
                {
                    role.Object = newId;
                }
            }
        }
    }

    private void RetargetClipScenes(string name, string newName)
    {
        foreach (ClipAsset clip in Document.Clips)
        {
            if (string.Equals(clip.Scene, name, StringComparison.Ordinal))
            {
                clip.Scene = newName;
            }
        }
    }

    // ── helper ────────────────────────────────────────────────────────

    private static float ClampAt(float at)
    {
        if (!float.IsFinite(at))
        {
            throw new InvalidOperationException($"블록의 시작 시각은 수여야 한다 ('{at}')");
        }

        return Math.Max(0f, at);
    }

    /// <summary>시각 순 자리에 넣는다 — 같은 시각이면 먼저 있던 것 뒤.</summary>
    private static void Insert(ClipTrack track, ClipBlock block)
    {
        int at = track.Blocks.Count;

        for (int i = 0; i < track.Blocks.Count; i++)
        {
            if (track.Blocks[i].At > block.At)
            {
                at = i;
                break;
            }
        }

        track.Blocks.Insert(at, block);
    }

    private void RequireClipName(string name)
    {
        if (!MotionNames.IsValid(name))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(name));
        }

        if (Document.FindClip(name) is not null)
        {
            throw new InvalidOperationException($"클립 '{name}'이 이미 있다 — 이름은 하나뿐이어야 한다.");
        }
    }

    private static void RequireRoleName(ClipAsset clip, string role)
    {
        if (!MotionNames.IsValid(role))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(role));
        }

        if (clip.FindRole(role) is not null)
        {
            throw new InvalidOperationException($"클립 '{clip.Name}'에 배역 '{role}'이 이미 있다.");
        }
    }

    private ClipAsset RequireClip(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Document.FindClip(name) ?? throw new KeyNotFoundException($"클립 '{name}'이 없다.");
    }

    private static ClipRole RequireRole(ClipAsset clip, string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return clip.FindRole(role) ?? throw new KeyNotFoundException($"클립 '{clip.Name}'에 배역 '{role}'이 없다.");
    }

    private static ClipTrack RequireTrack(ClipAsset clip, int track)
    {
        if (track < 0 || track >= clip.Tracks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(track), track, $"클립 '{clip.Name}'의 트랙이 {clip.Tracks.Count}개다.");
        }

        return clip.Tracks[track];
    }

    private static ClipBlock RequireBlock(ClipTrack track, int index)
    {
        if (index < 0 || index >= track.Blocks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"블록이 {track.Blocks.Count}개다.");
        }

        return track.Blocks[index];
    }
}
