using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 살아 있는 무대에서 읽은 "샷이 쓴 것" — 무대마다 카메라 노드의 값, 층마다 보이는 자리 · 배율 · 기울기.
    ///
    /// 정지 프레임(StageState)에는 이 값이 없다. 샷 의도(StageState.Shot)와 층 d만 있으면
    /// ShotLayoutMath로 풀리는 값이라, 접는 쪽은 저장하지 않고 비교할 때 푼다(StageStateComparer.CompareStageView).
    ///
    /// 층은 분해된 트랜스폼(위치 / 배율)이 아니라 **합성된 결과**로 담는다 — 무대 루트에서 본 층 중심과 전체 배율.
    /// 런타임이 값을 어떻게 나눠 쓰든(카메라에 얼마, 층에 얼마) 보이는 것이 같으면 등가다.
    /// </summary>
    public sealed class StageViewSnapshot
    {
        /// <summary>
        /// 카메라 리그의 노드 값. 의도가 쓰는 다섯(Pose)과, 의도가 쓰지 않는 흔들림 노드.
        /// 흔들림은 끝값이 없는 연기라 정착한 프레임에서는 항등이어야 한다 — 그래서 따로 읽는다.
        /// </summary>
        public readonly struct CameraEntry
        {
            public readonly string StageKey;
            public readonly CameraPose Pose;
            public readonly Vec2 ShakePosition;
            public readonly float ShakeRotation;

            public CameraEntry(string stageKey, CameraPose pose, Vec2 shakePosition, float shakeRotation)
            {
                StageKey = stageKey;
                Pose = pose;
                ShakePosition = shakePosition;
                ShakeRotation = shakeRotation;
            }
        }

        public readonly struct LayerEntry
        {
            public readonly string StageKey;
            public readonly float Depth;

            /// <summary>무대 루트(rig space)에서 본 층 중심. = ProjectToRigSpace(0).</summary>
            public readonly Vec2 CenterInRigSpace;

            /// <summary>무대 루트에서 본 층의 전체 배율. = 카메라 배율(줌 × 달리) × 층 배율. 기울기와 무관한 크기다.</summary>
            public readonly Vec2 TotalScale;

            /// <summary>무대 루트에서 본 층의 기울기(도). = 롤.</summary>
            public readonly float Rotation;

            public LayerEntry(string stageKey, float depth, Vec2 centerInRigSpace, Vec2 totalScale, float rotation)
            {
                StageKey = stageKey;
                Depth = depth;
                CenterInRigSpace = centerInRigSpace;
                TotalScale = totalScale;
                Rotation = rotation;
            }
        }

        public readonly List<CameraEntry> Cameras = new();
        public readonly List<LayerEntry> Layers = new();
    }
}
