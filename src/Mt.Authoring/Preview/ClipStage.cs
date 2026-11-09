using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 연출 클립의 **무대** — 상태 블록(보이기·자리·층·오프셋·카메라)이 시간에 따라 바꾸는 씬(<c>PLAN-clip.md</c> §2.4, K5).
///
/// 상태 블록은 씬이 이미 가진 값을 바꾼다. 그래서 끝난 뒤의 무대는 "그 값으로 고친 씬"을 <see cref="ScenePreview.Build"/>(깊이·자리·카메라 전부 코어 폴드)로
/// 세운 것과 **같은 트리**다 — 새 수학이 없다. 블록을 시작 시각 순으로 하나씩 적용한 씬마다 트리를 미리 세워 두고(단계),
/// 진행 중인 블록은 앞 단계와 그 단계의 차이를 이징한 만큼만 반영한다:
/// <c>상태 = 마지막으로 시작한 단계 − Σ(1 − u)·(그 단계 − 앞 단계)</c>. 서로 다른 값을 바꾸는 블록이 겹쳐도 차이가 따로 더해진다.
///
/// 중간 프레임은 런타임의 트윈을 따라 그린 **근사**다(자리는 커맨드 순간 한 번 풀리는 값이고, 런타임 이징은 DOTween이 돈다). 등가를 약속하는 것은 끝 상태뿐이다.
/// 모션 블록은 이 트리 위에 <see cref="ClipPreview.Pose"/>가 얹는다 — 모션은 언제나 상태 위다.
/// </summary>
public sealed class ClipStage
{
    private sealed record Step(PlacedBlock Placed, float At, float Duration, EaseKind Ease, SceneAsset Scene, RectNodeTree Tree);

    private readonly SceneAsset _baseScene;
    private readonly RectNodeTree _baseTree;
    private readonly List<Step> _steps = new();

    private ClipStage(SceneAsset baseScene, RectNodeTree baseTree)
    {
        _baseScene = baseScene;
        _baseTree = baseTree;
    }

    /// <summary>상태 블록의 수(끄지 않았고 적용할 수 있는 것).</summary>
    public int StepCount => _steps.Count;

    /// <summary>
    /// 클립의 상태 블록으로 단계를 세운다. 씬이 없으면 null.
    /// </summary>
    /// <param name="build">씬 → 트리. 뷰포트는 초상 종횡비·깊이·focus·무대 뼈대를 넣은 <see cref="ScenePreview.Build"/>를 준다.</param>
    /// <param name="isMuted">끈 트랙의 상태 블록은 없는 것처럼.</param>
    public static ClipStage? Build(MotionDocument document, ClipAsset clip, Func<SceneAsset, RectNodeTree> build, Func<ClipTrack, bool>? isMuted = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(build);

        if (ClipPreview.SceneOf(document, clip) is not { } scene)
        {
            return null;
        }

        var stage = new ClipStage(scene, build(scene));
        SceneAsset current = scene;

        foreach (PlacedBlock placed in ClipPreview.Ordered(clip))
        {
            ClipBlock block = placed.Block;
            ClipTrack track = clip.Tracks[placed.Track];

            if (block.IsMotion || isMuted?.Invoke(track) == true)
            {
                continue;
            }

            SceneAsset next = current.Clone();

            if (!Apply(next, block, track.IsCamera ? null : clip.FindRole(track.Role)?.Object, track.IsCamera))
            {
                continue; // 적용할 데가 없다(안 묶인 배역 · 씬에 없는 오브젝트 · 줄에 안 맞는 종류) — 검증이 알린다
            }

            stage._steps.Add(new Step(placed, block.At, Math.Max(0f, block.Duration ?? 0f), block.Ease ?? EaseKind.Linear, next, build(next)));
            current = next;
        }

        return stage;
    }

    /// <summary>
    /// 상태 블록의 값을 씬에 넣는다 — 비운 칸은 그대로. 넣을 데가 없으면 false.
    /// </summary>
    public static bool Apply(SceneAsset scene, ClipBlock block, string? objectId, bool cameraTrack)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(block);

        if (block.Kind == ClipBlockKind.Camera)
        {
            if (!cameraTrack)
            {
                return false;
            }

            scene.Camera.Zoom = block.Zoom ?? scene.Camera.Zoom;
            scene.Camera.PanX = block.PanX ?? scene.Camera.PanX;
            scene.Camera.PanY = block.PanY ?? scene.Camera.PanY;
            return true;
        }

        if (cameraTrack || objectId is null || scene.Find(objectId) is not { } o)
        {
            return false;
        }

        switch (block.Kind)
        {
            case ClipBlockKind.Visible:
                o.Visible = block.Visible ?? o.Visible;
                return true;

            case ClipBlockKind.Place:
                if (block.Place is not null)
                {
                    o.Place = block.Place.Length == 0 ? null : block.Place;
                }

                o.PlaceFocus = block.PlaceFocus ?? o.PlaceFocus;
                return true;

            case ClipBlockKind.Layer:
                o.Layer = block.Layer ?? o.Layer;
                return true;

            case ClipBlockKind.Offset:
                o.PositionX = block.X ?? o.PositionX;
                o.PositionY = block.Y ?? o.PositionY;
                o.RotationZ = block.Rotation ?? o.RotationZ;
                o.ScaleX = block.ScaleX ?? o.ScaleX;
                o.ScaleY = block.ScaleY ?? o.ScaleY;
                return true;

            default:
                return false;
        }
    }

    /// <summary>블록의 진행도(이징 뒤) — 시작 전 0, 끝난 뒤 1. Back·Elastic은 사이에서 0~1을 벗어날 수 있다.</summary>
    private static float Progress(Step step, double seconds)
    {
        if (seconds < step.At)
        {
            return 0f;
        }

        if (step.Duration <= 0f || seconds >= step.At + step.Duration)
        {
            return 1f;
        }

        return EaseFunctions.Evaluate(step.Ease, (float)((seconds - step.At) / step.Duration));
    }

    /// <summary>시작한 단계의 수 — 단계는 시작 시각 순이다.</summary>
    private int Started(double seconds)
    {
        int count = 0;

        while (count < _steps.Count && _steps[count].At <= seconds)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// 그 시각의 씬 — 마지막으로 시작한 단계의 것(그리는 순서·층·부모는 블록이 시작하는 순간 바뀐다). 읽기 전용으로 쓴다.
    /// </summary>
    public SceneAsset SceneAt(double seconds)
    {
        int started = Started(seconds);
        return started == 0 ? _baseScene : _steps[started - 1].Scene;
    }

    /// <summary>그 시각의 카메라 값 — 카메라 블록이 진행 중이면 그 사이. 카메라 모션이 이 값에서 시작한다.</summary>
    public CameraSettings CameraAt(double seconds)
    {
        int started = Started(seconds);
        CameraSettings last = (started == 0 ? _baseScene : _steps[started - 1].Scene).Camera;
        var camera = new CameraSettings { Zoom = last.Zoom, PanX = last.PanX, PanY = last.PanY };

        for (int k = 0; k < started; k++)
        {
            float remaining = 1f - Progress(_steps[k], seconds);

            if (Math.Abs(remaining) < 1e-6f)
            {
                continue;
            }

            CameraSettings before = (k == 0 ? _baseScene : _steps[k - 1].Scene).Camera;
            CameraSettings after = _steps[k].Scene.Camera;
            camera.Zoom -= remaining * (after.Zoom - before.Zoom);
            camera.PanX -= remaining * (after.PanX - before.PanX);
            camera.PanY -= remaining * (after.PanY - before.PanY);
        }

        return camera;
    }

    /// <summary>그 시각의 무대 트리 — 새 사본이라 받은 쪽이 고쳐도 된다.</summary>
    public RectNodeTree TreeAt(double seconds)
    {
        int started = Started(seconds);
        RectNodeTree tree = (started == 0 ? _baseTree : _steps[started - 1].Tree).Clone();

        for (int k = 0; k < started; k++)
        {
            float remaining = 1f - Progress(_steps[k], seconds);

            if (Math.Abs(remaining) < 1e-6f)
            {
                continue;
            }

            RectNodeTree before = k == 0 ? _baseTree : _steps[k - 1].Tree;
            RectNodeTree after = _steps[k].Tree;

            foreach (string key in after.Keys)
            {
                if (!before.TryGetState(key, out RectNodeState a) || !tree.Contains(key))
                {
                    continue;
                }

                RectNodeState b = after.GetState(key);
                Vec2 position = b.AnchoredPosition - a.AnchoredPosition;
                Vec3 scale = b.LocalScale - a.LocalScale;
                Vec3 euler = b.LocalEulerAngles - a.LocalEulerAngles;

                if (IsZero(position.X, position.Y, 0f) && IsZero(scale.X, scale.Y, scale.Z) && IsZero(euler.X, euler.Y, euler.Z))
                {
                    continue;
                }

                RectNodeState s = tree.GetState(key);
                tree.SetState(key, s
                    .WithAnchoredPosition(s.AnchoredPosition - position * remaining)
                    .WithLocalScale(s.LocalScale - scale * remaining)
                    .WithLocalEuler(s.LocalEulerAngles - euler * remaining));
            }
        }

        return tree;
    }

    /// <summary>오브젝트의 불투명도(0~1) — 보이기 블록이 진행 중이면 그 사이. 씬에 없는 id는 0.</summary>
    public float OpacityOf(string objectId, double seconds)
    {
        int started = Started(seconds);
        float opacity = Alpha(started == 0 ? _baseScene : _steps[started - 1].Scene, objectId);

        for (int k = 0; k < started; k++)
        {
            float remaining = 1f - Progress(_steps[k], seconds);

            if (Math.Abs(remaining) >= 1e-6f)
            {
                opacity -= remaining * (Alpha(_steps[k].Scene, objectId) - Alpha(k == 0 ? _baseScene : _steps[k - 1].Scene, objectId));
            }
        }

        return Math.Clamp(opacity, 0f, 1f);
    }

    private static float Alpha(SceneAsset scene, string objectId) => scene.Find(objectId) is { Visible: true } ? 1f : 0f;

    private static bool IsZero(float x, float y, float z) => Math.Abs(x) < 1e-9f && Math.Abs(y) < 1e-9f && Math.Abs(z) < 1e-9f;
}
