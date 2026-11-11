using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>트랙 안의 블록 하나와 그 자리 — 클립 전체를 시간 순으로 돌 때 쓴다.</summary>
public readonly record struct PlacedBlock(int Track, int Index, ClipBlock Block);

/// <summary>
/// 연출 클립의 시각 t 포즈(<c>PLAN-clip.md</c> §2.2). 새 수학은 없다 — 씬이 세운 트리에 블록마다
/// <see cref="MotionPreview.Pose(RectNodeTree, MotionAsset, double, MotionChannelMode, Func{Graph.CurveRef, bool}?, string)"/>를
/// **시작 시각 순으로** 얹을 뿐이다. 앞 블록이 남긴 트리가 뒤 블록의 시작값이다(런타임에서 motion의 시작값 = 그때의 노드 상태).
///
/// 그래서 클립이 끝난 뒤의 포즈는 블록을 같은 순서의 <c>motion(slot, name, mode)</c> 열로 풀어 코어 리듀서가 접은 상태와 같다
/// — 테스트가 그것을 지킨다.
/// </summary>
public static class ClipPreview
{
    /// <summary>
    /// 재생·폴드의 순서 — 시작 시각, 같으면 트랙 순, 같으면 트랙 안 순. 산출물(초안)의 <c>events[]</c>도 이 순서다.
    /// </summary>
    public static IReadOnlyList<PlacedBlock> Ordered(ClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var blocks = new List<PlacedBlock>();

        for (int t = 0; t < clip.Tracks.Count; t++)
        {
            for (int i = 0; i < clip.Tracks[t].Blocks.Count; i++)
            {
                blocks.Add(new PlacedBlock(t, i, clip.Tracks[t].Blocks[i]));
            }
        }

        // OrderBy는 안정 정렬이다 — 같은 시각이면 위에서 넣은 (트랙, 번호) 순서가 남는다.
        return blocks.OrderBy(b => b.Block.At).ToList();
    }

    /// <summary>클립의 시작 상태인 씬 — 적어 둔 이름, 안 적었으면 활성 씬.</summary>
    public static SceneAsset? SceneOf(MotionDocument document, ClipAsset clip)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);
        return clip.Scene is null ? document.ActiveSceneAsset : document.FindScene(clip.Scene);
    }

    /// <summary>이 트랙의 배역을 맡은 씬 오브젝트. 배역이 없거나 안 묶였거나 씬에 그 id가 없으면 null.</summary>
    public static SceneObject? ObjectOf(MotionDocument document, ClipAsset clip, ClipTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return clip.FindRole(track.Role)?.Object is { } id ? SceneOf(document, clip)?.Find(id) : null;
    }

    /// <summary>
    /// 시각 t의 카메라 — 카메라 트랙에 놓인 카메라 모션 블록을 시작 시각 순으로 얹은 값(캐릭터 블록과 같은 규칙: 앞 블록이 남긴 값이 뒤 블록의 시작값).
    /// 시작한 카메라 모션 블록이 하나도 없으면 null — 무대의 카메라를 건드릴 일이 없다.
    /// </summary>
    /// <param name="except">이 블록은 얹지 않는다 — 클립 안에서 그 카메라 모션을 고칠 때의 시작값을 낸다.</param>
    public static CameraSettings? CameraAt(MotionDocument document, ClipAsset clip, CameraSettings? start, double seconds, Func<ClipTrack, bool>? isMuted = null, ClipBlock? except = null, int exceptFromCycle = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);
        CameraSettings? camera = null;

        foreach (PlacedBlock placed in Ordered(clip))
        {
            ClipBlock block = placed.Block;

            if (block.At > seconds)
            {
                break;
            }

            ClipTrack track = clip.Tracks[placed.Track];

            if (!track.IsCamera || !block.IsMotion || block.IsMuted || isMuted?.Invoke(track) == true
                || document.Find(block.Motion) is not { } motion || !CameraRig.IsCamera(motion))
            {
                continue;
            }

            int cycles = ReferenceEquals(block, except) ? Math.Min(block.Cycles, exceptFromCycle) : block.Cycles;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                double from = block.At + cycle * (double)motion.Duration;

                if (from > seconds)
                {
                    break;
                }

                camera ??= start ?? SceneOf(document, clip)?.Camera ?? new CameraSettings();
                camera = CameraRig.Pose(camera, motion, seconds - from, block.Mode);
            }
        }

        return camera;
    }

    /// <summary>
    /// 시각 t의 포즈. <paramref name="template"/>은 클립의 씬으로 세운 트리다(<see cref="ScenePreview.Build"/>).
    /// 그릴 수 없는 블록(모션이 없다 · 배역이 안 묶였다 · 오브젝트가 씬에 없다)은 건너뛴다 — 검증이 이미 소리를 낸다.
    /// 아직 시작하지 않은 블록은 얹지 않는다.
    /// </summary>
    /// <param name="isMuted">끈 트랙(배역 끄기) — 그 블록은 없는 것처럼 건너뛴다. 보기 상태라 문서에는 없다. null이면 없음.</param>
    /// <param name="startCamera">그 시각의 카메라 값(<see cref="ClipStage.CameraAt"/>). null이면 씬의 카메라 — 카메라 모션이 여기서 시작한다.</param>
    /// <param name="except">이 블록은 얹지 않는다 — 클립 안에서 그 블록의 모션을 고칠 때 "이 블록이 얹히기 전의 상태"(키 값의 시작값)를 낸다.</param>
    public static RectNodeTree Pose(RectNodeTree template, MotionDocument document, ClipAsset clip, double seconds, Func<ClipTrack, bool>? isMuted = null, CameraSettings? startCamera = null, ClipBlock? except = null, int exceptFromCycle = 0)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);

        RectNodeTree tree = template;

        foreach (PlacedBlock placed in Ordered(clip))
        {
            ClipBlock block = placed.Block;

            if (block.At > seconds)
            {
                break; // 시각 순이라 뒤는 전부 아직이다
            }

            ClipTrack track = clip.Tracks[placed.Track];

            if (block.IsMuted || isMuted?.Invoke(track) == true)
            {
                continue;
            }

            // 상태 블록은 여기서 얹지 않는다 — 무대(ClipStage)가 먼저 세우고 모션은 그 위에 얹힌다.
            if (!block.IsMotion || document.Find(block.Motion) is not { } motion || ObjectOf(document, clip, track) is not { } target)
            {
                continue;
            }

            // 반복은 같은 모션을 끝에 이어 다시 얹는 것이다 — 끝난 바퀴가 남긴 값이 다음 바퀴의 시작값.
            // except면 그 바퀴부터는 얹지 않는다(그 앞 바퀴가 남긴 것은 시작값의 일부다).
            int cycles = ReferenceEquals(block, except) ? Math.Min(block.Cycles, exceptFromCycle) : block.Cycles;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                double start = block.At + cycle * (double)motion.Duration;

                if (start > seconds)
                {
                    break;
                }

                tree = MotionPreview.Pose(tree, motion, seconds - start, block.Mode, null, target.Prefix);
            }
        }

        // 카메라 모션 — 카메라 트랙의 모션 블록. 그때의 카메라(상태 블록까지 반영한 값)에서 시작해 순서대로 얹고, 무대의 카메라 노드를 그 값으로 다시 세운다.
        if (CameraAt(document, clip, startCamera, seconds, isMuted) is { } camera)
        {
            if (ReferenceEquals(tree, template))
            {
                tree = template.Clone();
            }

            StageShotResponse.Apply(tree, camera);
        }

        // 얹은 블록이 없으면 호출자의 트리를 그대로 돌려주지 않는다 — 받은 쪽이 고쳐도 template이 안 변하게.
        return ReferenceEquals(tree, template) ? template.Clone() : tree;
    }
}
