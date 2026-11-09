using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 카메라 모션 — 카메라(zoom·pan)를 **키와 커브로** 움직이는 모션. 리그 덤프에는 없는 툴의 가짜 리그(<see cref="RigKind"/>)다.
///
/// 모션의 그릇은 그대로 쓴다: 채널은 "노드 × 종류 × 성분"이고 카메라는 가짜 노드 둘이다 —
/// <see cref="PanNode"/>의 위치 x·y = pan(px), <see cref="ZoomNode"/>의 위치 x = zoom(씬 탭의 그 값, 1당 배율 0.05).
/// 그래서 그래프 에디터·도프 시트·키 연산·되돌리기·검증(코어 규칙)이 캐릭터 모션과 똑같이 돈다.
/// 값을 읽는 것도 같은 함수다: 카메라 값을 가짜 트리에 넣고 <see cref="MotionPreview.Pose(RectNodeTree, MotionAsset, double, MotionChannelMode, Func{Graph.CurveRef, bool}?, string)"/>를 지난다
/// (relative = 지금 값 + 키, absolute = 키 그대로, restore/commit 끝 규칙).
///
/// 런타임의 모션 규약(<c>motions/2</c>)은 리그 노드만 안다 — 카메라 모션은 <c>motions.json</c>에 **나가지 않는다**(<see cref="Validation.MotionExport"/>).
/// 클립 산출물 초안(K6)이 실을 자리다.
/// </summary>
public static class CameraRig
{
    public const string RigKind = "camera";
    public const string PanNode = "Camera_Pan";
    public const string ZoomNode = "Camera_Zoom";

    public static readonly IReadOnlyList<string> NodeIds = new[] { PanNode, ZoomNode };

    public static bool IsCamera(MotionAsset? motion) => motion is not null && string.Equals(motion.Rig, RigKind, StringComparison.Ordinal);

    /// <summary>검증에 넘기는 "rigKind → 노드" 표에 카메라 리그를 더한 것. null이면 null(덤프가 아직 없는 자리).</summary>
    public static IReadOnlyDictionary<string, ICollection<string>>? WithCamera(IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds)
    {
        if (rigNodeIds is null || rigNodeIds.ContainsKey(RigKind))
        {
            return rigNodeIds;
        }

        var merged = new Dictionary<string, ICollection<string>>(rigNodeIds, StringComparer.Ordinal) { [RigKind] = NodeIds.ToList() };
        return merged;
    }

    /// <summary>카메라 값을 담은 가짜 트리 — 모션이 얹힐 자리.</summary>
    public static RectNodeTree TreeOf(CameraSettings camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var tree = new RectNodeTree(RectSpace.Centered(1f, 1f));
        tree.Add(PanNode, null!, RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(camera.PanX, camera.PanY)));
        tree.Add(ZoomNode, null!, RectNodeState.StretchFull.WithAnchoredPosition(new Vec2(camera.Zoom, 0f)));
        return tree;
    }

    public static CameraSettings Read(RectNodeTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Vec2 pan = tree.GetState(PanNode).AnchoredPosition;
        return new CameraSettings { PanX = pan.X, PanY = pan.Y, Zoom = tree.GetState(ZoomNode).AnchoredPosition.X };
    }

    /// <summary>
    /// 시각 t의 카메라 — <paramref name="start"/>(그때의 카메라)에 모션을 얹은 값. 캐릭터 모션과 같은 합성·끝 규칙이다.
    /// </summary>
    public static CameraSettings Pose(CameraSettings start, MotionAsset motion, double seconds, MotionChannelMode mode, Func<Graph.CurveRef, bool>? isMuted = null)
        => Read(MotionPreview.Pose(TreeOf(start), motion, seconds, mode, isMuted));

    /// <summary>지금 카메라 값이 되게 하려면 그 성분의 키 값이 얼마여야 하나 — 재생 기준(relative면 시작값과의 차).</summary>
    public static float KeyValueFor(MotionChannelMode mode, float start, float wanted)
        => PoseEdit.KeyValueFor(MotionChannelKind.Position, mode, start, wanted);
}
