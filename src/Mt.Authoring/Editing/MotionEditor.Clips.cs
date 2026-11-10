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

    /// <summary>배역의 트랙을 끝에 더하고 그 번호를 돌려준다. <see cref="ClipTrack.CameraRole"/>이면 카메라 트랙이다(하나뿐).</summary>
    public int AddClipTrack(string clipName, string role)
    {
        ClipAsset clip = RequireClip(clipName);

        if (string.Equals(role, ClipTrack.CameraRole, StringComparison.Ordinal))
        {
            if (clip.Tracks.Any(t => t.IsCamera))
            {
                throw new InvalidOperationException($"클립 '{clip.Name}'에 카메라 트랙이 이미 있다.");
            }
        }
        else
        {
            RequireRole(clip, role);
        }

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
        RequireFits(target, source);
        var block = new ClipBlock { At = ClampAt(at), Motion = motion, Mode = mode ?? source.PreviewMode };
        Mutate(MotionChangeKind.Clip, () => Insert(target, block));
        return target.Blocks.IndexOf(block);
    }

    /// <summary>
    /// 상태 블록을 놓고 트랙 안 번호를 돌려준다(<c>PLAN-clip.md</c> K5). 값은 사본으로 들어간다. 카메라 블록은 카메라 트랙에만, 나머지는 배역 트랙에만 —
    /// 줄에 안 맞는 종류는 여기서 막는다(놓을 자리가 없다). 값의 옳고 그름(모르는 층·지점)은 검증이 알린다.
    /// </summary>
    public int AddClipStateBlock(string clipName, int track, ClipBlock state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ClipAsset clip = RequireClip(clipName);
        ClipTrack target = RequireTrack(clip, track);
        ClipBlock block = NormalizeState(target, state);
        Mutate(MotionChangeKind.Clip, () => Insert(target, block));
        return target.Blocks.IndexOf(block);
    }

    /// <summary>
    /// 블록의 값을 통째로 바꾼다(인스펙터) — 종류는 못 바꾼다. 시작 시각이 바뀌면 시각 순 자리로 옮겨지고 새 번호를 돌려준다.
    /// </summary>
    public int UpdateClipBlock(string clipName, int track, int index, ClipBlock value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ClipTrack target = RequireTrack(RequireClip(clipName), track);
        ClipBlock block = RequireBlock(target, index);

        if (value.Kind != block.Kind)
        {
            throw new InvalidOperationException("블록의 종류는 바꿀 수 없다 — 지우고 새로 놓는다.");
        }

        ClipBlock next;

        if (block.IsMotion)
        {
            RequireFits(target, Require(value.Motion));
            next = new ClipBlock { At = ClampAt(value.At), Motion = value.Motion, Mode = value.Mode, Muted = value.IsMuted ? true : null };
        }
        else
        {
            next = NormalizeState(target, value);
        }

        Mutate(MotionChangeKind.Clip, () =>
        {
            target.Blocks.Remove(block);
            Insert(target, next);
        });
        return target.Blocks.IndexOf(next);
    }

    /// <summary>카메라 모션은 카메라 트랙에만, 캐릭터 모션은 배역 트랙에만 — 놓을 자리가 없는 것은 여기서 막는다.</summary>
    private static void RequireFits(ClipTrack track, MotionAsset motion)
    {
        bool camera = Preview.CameraRig.IsCamera(motion);

        if (track.IsCamera && !camera)
        {
            throw new InvalidOperationException($"카메라 트랙에는 카메라 블록과 카메라 모션만 놓는다 ('{motion.Name}'은 {motion.Rig} 모션).");
        }

        if (!track.IsCamera && camera)
        {
            throw new InvalidOperationException($"카메라 모션 '{motion.Name}'은 카메라 트랙에만 놓는다.");
        }
    }

    private static ClipBlock NormalizeState(ClipTrack track, ClipBlock state)
    {
        if (state.IsMotion)
        {
            throw new InvalidOperationException("모션 블록은 AddClipBlock으로 놓는다.");
        }

        if (track.IsCamera != (state.Kind == ClipBlockKind.Camera))
        {
            throw new InvalidOperationException(track.IsCamera ? "카메라 트랙에는 카메라 블록만 놓는다." : "카메라 블록은 카메라 트랙에만 놓는다.");
        }

        if (state.Duration is { } d && (!float.IsFinite(d) || d < 0f))
        {
            throw new InvalidOperationException($"블록의 길이는 0 이상의 수다 ('{d}')");
        }

        ClipBlock block = state.Clone();
        block.At = ClampAt(state.At);
        block.Motion = "";
        block.Mode = MotionChannelMode.Relative;
        block.Duration = state.Duration is > 0f ? state.Duration : null; // 0과 빈 칸은 같은 뜻 — 한 가지로 적는다
        block.Muted = state.IsMuted ? true : null;
        return block;
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

        if (!block.IsMotion && to.IsCamera != from.IsCamera)
        {
            throw new InvalidOperationException("상태 블록은 카메라 트랙과 배역 트랙 사이를 오갈 수 없다.");
        }

        if (block.IsMotion && Document.Find(block.Motion) is { } moved)
        {
            RequireFits(to, moved);
        }
        else if (block.IsMotion && to.IsCamera != from.IsCamera)
        {
            throw new InvalidOperationException("없는 모션의 블록은 카메라 트랙과 배역 트랙 사이를 오갈 수 없다.");
        }

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

        if (!block.IsMotion)
        {
            throw new InvalidOperationException("상태 블록에는 재생 기준이 없다.");
        }

        Mutate(MotionChangeKind.Clip, () => block.Mode = mode);
    }

    /// <summary>블록을 끄거나 켠다 — 자리는 그대로, 적용만 빠진다(미리보기·굽기·겹침 검사). 되돌리기 한 칸.</summary>
    public void SetClipBlockMuted(string clipName, int track, int index, bool muted)
    {
        ClipBlock block = RequireBlock(RequireTrack(RequireClip(clipName), track), index);
        Mutate(MotionChangeKind.Clip, () => block.Muted = muted ? true : null);
    }

    /// <summary>블록이 가리키는 모션을 바꾼다(자리는 그대로).</summary>
    public void SetClipBlockMotion(string clipName, int track, int index, string motion)
    {
        ClipBlock block = RequireBlock(RequireTrack(RequireClip(clipName), track), index);

        if (!block.IsMotion)
        {
            throw new InvalidOperationException("상태 블록에는 모션이 없다.");
        }

        RequireFits(RequireTrack(RequireClip(clipName), track), Require(motion));
        Mutate(MotionChangeKind.Clip, () => block.Motion = motion);
    }

    /// <summary>
    /// "이 블록만 따로" — 블록의 모션을 복제해 이 블록만 새 모션을 가리키게 한다(되돌리기 한 칸). 모션은 공유 자산이라, 한 블록에서만 다르게 고치려면
    /// 먼저 떼어 내야 한다. 새 모션의 이름을 돌려준다. 그 모션을 이 블록만 쓰고 있어도 복제한다(원본을 남겨 두려는 뜻일 수 있다).
    /// </summary>
    public string MakeClipBlockUnique(string clipName, int track, int index, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        ClipBlock block = RequireBlock(RequireTrack(RequireClip(clipName), track), index);

        if (!block.IsMotion)
        {
            throw new InvalidOperationException("상태 블록에는 모션이 없다.");
        }

        MotionAsset source = Require(block.Motion);

        if (!MotionNames.IsValid(newName))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(newName));
        }

        if (Document.Find(newName) is not null)
        {
            throw new InvalidOperationException($"모션 '{newName}'이 이미 있다.");
        }

        // 문서의 사본에서 떼어 온다 — 원본과 객체를 나눠 갖지 않는다.
        MotionAsset copy = Serialization.MotionDocumentJson.Read(Serialization.MotionDocumentJson.Write(Document)).Find(source.Name)!;
        copy.Name = newName;

        Mutate(MotionChangeKind.Structure, () =>
        {
            Document.Motions.Insert(Document.Motions.IndexOf(source) + 1, copy);
            block.Motion = newName;
        });
        return newName;
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
            if (block.IsMotion && string.Equals(block.Motion, name, StringComparison.Ordinal))
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
