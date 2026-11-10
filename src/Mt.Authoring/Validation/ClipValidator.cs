using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.Authoring.Validation;

/// <summary>
/// 클립 검증 문제 하나. <see cref="Blocking"/>이 false면 알림이다 — 내보내기를 막지 않는다.
/// </summary>
public sealed record ClipProblem(string ClipName, string Message, bool Blocking = true)
{
    public override string ToString()
        => string.IsNullOrEmpty(ClipName) ? Message : $"클립 '{ClipName}': {Message}";
}

/// <summary>
/// 연출 클립의 규칙(<c>PLAN-clip.md</c> §3, 초안). 편집기는 중간 상태를 막지 않으므로 여기서 계속 알린다.
/// 막는 것: 이름·배역·참조의 어긋남, 리그가 안 맞는 모션, 길이 밖의 블록, 같은 배역·같은 노드의 겹침, 참조한 모션의 규칙 위반.
/// 알리는 것: 빈 클립, 미리보기에 안 묶인 배역.
/// 상태 블록(K5)은 줄에 맞는 종류·아는 값·바꾸는 값이 있는지를 보고, 같은 줄의 같은 종류가 겹치면 막는다.
/// </summary>
public static class ClipValidator
{
    /// <summary>시각 비교의 여유 — 프레임(1/24초)보다 훨씬 작다.</summary>
    public const float Tolerance = 1e-4f;

    /// <param name="rigNodeIds">rigKind → 노드 id 목록. null이면 리그 맞춤은 rigKind 이름만 견준다.</param>
    public static IReadOnlyList<ClipProblem> Validate(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<ClipProblem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ClipAsset clip in document.Clips)
        {
            if (!string.IsNullOrEmpty(clip.Name) && !seen.Add(clip.Name))
            {
                problems.Add(new ClipProblem(clip.Name, "같은 이름이 둘이다"));
            }

            problems.AddRange(ValidateClip(document, clip, rigNodeIds));
        }

        return problems;
    }

    public static IReadOnlyList<ClipProblem> ValidateClip(
        MotionDocument document,
        ClipAsset clip,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);
        var problems = new List<ClipProblem>();

        void Block(string message) => problems.Add(new ClipProblem(clip.Name, message));
        void Notice(string message) => problems.Add(new ClipProblem(clip.Name, message, Blocking: false));

        if (!MotionNames.IsValid(clip.Name))
        {
            Block(MotionNames.RuleMessage(clip.Name));
        }

        if (clip.Duration is { } given && (!float.IsFinite(given) || given < 0f))
        {
            Block($"길이는 0 이상의 수다 ({given})");
        }

        // ── 배역 ──
        var roleNames = new HashSet<string>(StringComparer.Ordinal);
        SceneAsset? scene = ClipPreview.SceneOf(document, clip);

        if (scene is null)
        {
            Notice($"씬 '{clip.Scene}'이 없다 — 미리보기에 세울 시작 상태가 없다");
        }

        foreach (ClipRole role in clip.Roles)
        {
            if (!MotionNames.IsValid(role.Name))
            {
                Block($"배역 이름: {MotionNames.RuleMessage(role.Name)}");
            }
            else if (!roleNames.Add(role.Name))
            {
                Block($"배역 '{role.Name}'이 둘이다");
            }

            if (string.IsNullOrEmpty(role.Rig))
            {
                Block($"배역 '{role.Name}'에 리그(rigKind)가 없다");
            }
            else if (rigNodeIds is not null && !rigNodeIds.ContainsKey(role.Rig))
            {
                Block($"배역 '{role.Name}': 리그 덤프에 rigKind '{role.Rig}'가 없다. 있는 것: [{string.Join(", ", rigNodeIds.Keys)}]");
            }

            if (scene is null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(role.Object))
            {
                Notice($"배역 '{role.Name}'을 맡은 오브젝트가 없다 — 미리보기에 안 보인다");
            }
            else if (scene.Find(role.Object) is not { } o)
            {
                Notice($"배역 '{role.Name}': 씬 '{scene.Name}'에 오브젝트 '{role.Object}'가 없다 — 미리보기에 안 보인다");
            }
            else if (!string.Equals(o.Rig, role.Rig, StringComparison.Ordinal))
            {
                Notice($"배역 '{role.Name}'의 리그는 '{role.Rig}'인데 오브젝트 '{o.Id}'는 '{o.Rig}'다");
            }
        }

        // ── 블록 ──
        IReadOnlyList<PlacedBlock> ordered = ClipPreview.Ordered(clip);

        if (ordered.Count == 0)
        {
            Notice("블록이 없다");
        }

        float? duration = clip.Duration;
        var checkedMotions = new HashSet<string>(StringComparer.Ordinal);
        // 배역 → 그 배역이 쓰는 (노드, 종류)의 구간들. 겹침을 본다.
        var claims = new Dictionary<(string Role, string Node, MotionChannelKind Kind), List<(float Start, float End, PlacedBlock Block)>>();
        var stateClaims = new Dictionary<(string Role, ClipBlockKind Kind), List<(float Start, float End, PlacedBlock Block)>>();

        foreach (PlacedBlock placed in ordered)
        {
            ClipBlock block = placed.Block;
            ClipTrack track = clip.Tracks[placed.Track];
            string where = $"트랙 {placed.Track + 1} 블록 {placed.Index + 1}('{block.Describe()}' @ {block.At:0.###}s)";
            ClipRole? role = clip.FindRole(track.Role);

            if (role is null && !track.IsCamera)
            {
                Block($"{where}: 배역 '{track.Role}'이 없다");
            }

            if (!float.IsFinite(block.At) || block.At < 0f)
            {
                Block($"{where}: 시작 시각은 0 이상이다");
            }

            if (!block.IsMotion)
            {
                ValidateState(block, track, where, Block);
                float stateEnd = block.At + Math.Max(0f, block.Duration ?? 0f);

                if (duration is { } limit && stateEnd > limit + Tolerance)
                {
                    Block($"{where}: 클립 길이 {limit:0.###}s를 넘는다(끝 {stateEnd:0.###}s)");
                }

                if (block.IsMuted)
                {
                    continue; // 끈 블록은 겹침을 다투지 않는다
                }

                // 같은 줄(배역·카메라)의 같은 종류가 같은 때에 겹치면 어느 값으로 가는지 흐려진다.
                (string, ClipBlockKind) stateKey = (track.Role, block.Kind);

                if (!stateClaims.TryGetValue(stateKey, out List<(float Start, float End, PlacedBlock Block)>? taken))
                {
                    stateClaims[stateKey] = taken = new List<(float, float, PlacedBlock)>();
                }

                foreach ((float start, float otherEnd, PlacedBlock other) in taken)
                {
                    if (block.At < otherEnd - Tolerance && start < stateEnd - Tolerance)
                    {
                        Block($"{where}: 트랙 {other.Track + 1} 블록 {other.Index + 1}('{other.Block.Describe()}' @ {other.Block.At:0.###}s)과 같은 값을 같은 때에 바꾼다 — 겹칠 수 없다");
                        break;
                    }
                }

                taken.Add((block.At, stateEnd, placed));
                continue;
            }


            if (document.Find(block.Motion) is not { } motion)
            {
                Block($"{where}: 모션 '{block.Motion}'이 없다");
                continue;
            }

            float end = block.At + motion.Duration;

            if (duration is { } d && end > d + Tolerance)
            {
                Block($"{where}: 클립 길이 {d:0.###}s를 넘는다(끝 {end:0.###}s)");
            }

            if (checkedMotions.Add(motion.Name))
            {
                foreach (MotionProblem p in MotionValidator.ValidateMotion(motion, CameraRig.WithCamera(rigNodeIds)).Where(p => p.Blocking))
                {
                    Block($"모션 '{motion.Name}': {p.Message}");
                }
            }

            if (track.IsCamera)
            {
                // 카메라 트랙의 모션은 카메라 모션이어야 한다 — 겹침은 캐릭터와 같은 규칙(같은 가짜 노드를 같은 때에).
                if (!CameraRig.IsCamera(motion))
                {
                    Block($"{where}: 카메라 트랙에는 카메라 블록과 카메라 모션만 놓는다");
                    continue;
                }
            }
            else if (role is null)
            {
                continue;
            }
            else if (!Wears(role.Rig, motion, rigNodeIds))
            {
                Block($"{where}: 모션의 리그 '{motion.Rig}'가 배역 '{role.Name}'의 리그 '{role.Rig}'에 안 맞는다");
            }

            foreach (ChannelAsset channel in block.IsMuted ? Enumerable.Empty<ChannelAsset>() : motion.Channels)
            {
                var key = (track.Role, channel.Node, channel.Kind);

                if (!claims.TryGetValue(key, out List<(float Start, float End, PlacedBlock Block)>? spans))
                {
                    claims[key] = spans = new List<(float, float, PlacedBlock)>();
                }

                foreach ((float start, float otherEnd, PlacedBlock other) in spans)
                {
                    // 맞닿는 것(앞의 끝 = 뒤의 시작)은 겹침이 아니다.
                    if (block.At < otherEnd - Tolerance && start < end - Tolerance)
                    {
                        Block(
                            $"{where}: {(track.IsCamera ? "카메라" : $"배역 '{track.Role}'")}의 {channel.Node} {ChannelKindLabel.Of(channel.Kind)}를 " +
                            $"트랙 {other.Track + 1} 블록 {other.Index + 1}('{other.Block.Motion}' @ {other.Block.At:0.###}s)과 같은 때에 쓴다 — 겹칠 수 없다");
                        break;
                    }
                }

                spans.Add((block.At, end, placed));
            }
        }

        return problems;
    }

    /// <summary>상태 블록의 값 — 줄에 맞는 종류인가, 아는 층·지점인가, 바꾸는 값이 하나라도 있는가.</summary>
    private static void ValidateState(ClipBlock block, ClipTrack track, string where, Action<string> report)
    {
        if (track.IsCamera != (block.Kind == ClipBlockKind.Camera))
        {
            report(track.IsCamera ? $"{where}: 카메라 트랙에는 카메라 블록만 놓는다" : $"{where}: 카메라 블록은 카메라 트랙에만 놓는다");
            return;
        }

        if (block.Duration is { } d && (!float.IsFinite(d) || d < 0f))
        {
            report($"{where}: 길이는 0 이상의 수다");
        }

        switch (block.Kind)
        {
            case ClipBlockKind.Visible when block.Visible is null:
                report($"{where}: 보이기/숨기기가 정해지지 않았다");
                break;

            case ClipBlockKind.Place:
                if (block.Place is null && block.PlaceFocus is null)
                {
                    report($"{where}: 자리도 focus도 없다 — 바꾸는 값이 없다");
                }

                if (!string.IsNullOrEmpty(block.Place) && !ScenePreview.ScreenPoints.Contains(block.Place))
                {
                    report($"{where}: 화면 지점 '{block.Place}'를 모른다. 있는 것: [{string.Join(", ", ScenePreview.ScreenPoints)}]");
                }

                if (block.PlaceFocus is not null && !ScenePreview.FocusPresets.Contains(block.PlaceFocus))
                {
                    report($"{where}: focus '{block.PlaceFocus}'를 모른다. 있는 것: [{string.Join(", ", ScenePreview.FocusPresets)}]");
                }

                break;

            case ClipBlockKind.Layer when !SceneLayers.IsKnown(block.Layer):
                report($"{where}: 층은 {string.Join("·", SceneLayers.Order)} 중 하나다 ('{block.Layer}')");
                break;

            case ClipBlockKind.Offset when block.X is null && block.Y is null && block.Rotation is null && block.ScaleX is null && block.ScaleY is null:
                report($"{where}: 오프셋·회전·배율이 전부 비었다 — 바꾸는 값이 없다");
                break;

            case ClipBlockKind.Camera when block.Zoom is null && block.PanX is null && block.PanY is null:
                report($"{where}: zoom·pan이 전부 비었다 — 바꾸는 값이 없다");
                break;
        }
    }

    /// <summary>
    /// 그 리그가 모션을 입을 수 있나 — <see cref="ScenePreview.CanWear"/>와 같은 판정: rigKind가 같거나, 채널의 노드가 전부 그 리그에 있다.
    /// </summary>
    public static bool Wears(string rig, MotionAsset motion, IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds)
    {
        ArgumentNullException.ThrowIfNull(motion);

        if (string.Equals(rig, motion.Rig, StringComparison.Ordinal))
        {
            return true;
        }

        return motion.Channels.Count > 0
            && rigNodeIds is not null
            && rigNodeIds.TryGetValue(rig ?? "", out ICollection<string>? nodes)
            && motion.Channels.All(c => nodes.Contains(c.Node));
    }

    /// <summary>
    /// 겹치는 블록들의 (트랙, 번호) — 타임라인이 빨갛게 그린다. <see cref="ValidateClip"/>의 겹침 규칙과 같다:
    /// 같은 배역의 같은 (노드, 종류)를 쓰는 두 블록의 시간이 겹친다(맞닿는 것은 아니다). 겹친 양쪽이 다 든다.
    /// </summary>
    public static HashSet<(int Track, int Index)> OverlappingBlocks(MotionDocument document, ClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);
        var overlapping = new HashSet<(int, int)>();
        var claims = new Dictionary<(string Role, string Node, MotionChannelKind Kind), List<(float Start, float End, PlacedBlock Block)>>();

        foreach (PlacedBlock placed in ClipPreview.Ordered(clip))
        {
            if (!placed.Block.IsMotion || placed.Block.IsMuted || document.Find(placed.Block.Motion) is not { } motion)
            {
                continue;
            }

            string role = clip.Tracks[placed.Track].Role;
            float start = placed.Block.At;
            float end = start + motion.Duration;

            foreach (ChannelAsset channel in motion.Channels)
            {
                var key = (role, channel.Node, channel.Kind);

                if (!claims.TryGetValue(key, out List<(float Start, float End, PlacedBlock Block)>? spans))
                {
                    claims[key] = spans = new List<(float, float, PlacedBlock)>();
                }

                foreach ((float otherStart, float otherEnd, PlacedBlock other) in spans)
                {
                    if (start < otherEnd - Tolerance && otherStart < end - Tolerance)
                    {
                        overlapping.Add((placed.Track, placed.Index));
                        overlapping.Add((other.Track, other.Index));
                    }
                }

                spans.Add((start, end, placed));
            }
        }

        return overlapping;
    }

    /// <summary>내보내기를 막는 것만.</summary>
    public static IEnumerable<ClipProblem> Blocking(IEnumerable<ClipProblem> problems)
        => problems.Where(p => p.Blocking);
}

/// <summary>클립의 돕는 손 — 각각 편집기 명령 하나 = 되돌리기 하나.</summary>
public static class ClipFixes
{
    /// <summary>길이를 마지막 블록의 끝에 맞춘다(넘치는 블록이 있을 때). 길이를 안 적은 클립은 이미 그렇다 — 아무 일도 없다.</summary>
    public static void DurationToLastBlock(MotionEditor editor, string clipName)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ClipAsset clip = editor.Document.FindClip(clipName) ?? throw new KeyNotFoundException($"클립 '{clipName}'이 없다.");

        if (clip.Duration is not null)
        {
            editor.SetClipDuration(clipName, ClipTiming.EndOfBlocks(editor.Document, clip));
        }
    }
}
