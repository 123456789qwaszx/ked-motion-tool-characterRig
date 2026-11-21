using System;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 샷 = 카메라의 축 값 한 벌(ShotIntentState)이고, 무대의 트랜스폼은 전부 그 값의 함수다.
    // 이 파일이 그 함수의 유일한 자리다 — 런타임은 출력을 노드에 쓰고, 폴드는 상태에 접고,
    // 중립은 Solve(Default)다.
    //
    // 이 런타임에 카메라는 없다. 카메라 변환의 역을 무대에 걸어 카메라처럼 보이게 한다 — 그래서 카메라에서
    // 부모인 몸이 무대 쪽에서는 가장 안쪽이다. 바깥 → 안:
    //
    //   Pan(렌즈 · 화면 밀기) → Zoom(렌즈) → Roll(헤드) → Dolly(몸) → Track(몸) → 층
    //
    // 공통 몫은 카메라 노드에, d에 따른 차이는 층 노드에 쓴다. 층 안의 점 p가 보이는 자리:
    //
    //   화면 = pan + Rot(roll) · [ 층 오프셋 + M(d) × ( p − track × (1 + trackResponse(d)) ) ]
    //   M(d)      = s ^ (1 + zoomScaleResponse(d)) × g ^ (1 + dollyScaleResponse(d))
    //   층 오프셋 = 초점 산개(줌에 비례) + pan × panResponse(d)
    //   s = 1 + 0.05 × zoom,  g = 1 + 0.05 × dolly
    //
    // 트랙 · 달리 · 롤이 0이면 종전 식(pan + 층 오프셋 + s × 층 배율 × p)과 비트 단위로 같다 —
    // 그 식으로 잰 대본이 화면을 그대로 갖는 근거다. 줌 · 팬의 식은 종전 PresentationResponseMath ·
    // PresentationCameraRootApplier에서 글자 그대로 옮긴 것이다(2026-11-05).
    //
    // 층의 기준값은 항등이다(위치 0 · 배율 1). 무대 리그 빌더가 보장하므로 입력에 없다.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>한 깊이에서의 층 반응 계수. d의 곡선(LayerProfileCurve) 위의 한 점.</summary>
    public readonly struct LayerProfile
    {
        /// <summary>카메라 배율 변화량 1.0당, 초점에서 멀어지는 방향으로 밀리는 거리(px). 축별 독립.</summary>
        public readonly Vec2 FocusSpreadPerZoom;

        /// <summary>팬(px)을 따라가는 비율. 축별 독립.</summary>
        public readonly Vec2 PanResponse;

        /// <summary>카메라 배율을 얼마나 상쇄/강조할지. 층 배율 = cameraScale ^ 이 값.</summary>
        public readonly float ZoomScaleResponse;

        /// <summary>
        /// 트랙에 mid보다 얼마나 더 밀리는가. 층이 밀리는 양 = track × (1 + 이 값). 축별 독립.
        /// 먼 층은 음수(−1이면 무한히 멀어 안 밀린다), 가까운 층은 양수.
        /// </summary>
        public readonly Vec2 TrackResponse;

        /// <summary>
        /// 달리에 mid보다 얼마나 더 커지는가. 층 배율 = dollyScale ^ (1 + 이 값) — 그중 공통 몫(dollyScale)은
        /// 카메라 노드가 갖고 층 노드에는 dollyScale ^ 이 값이 남는다. 먼 층은 음수, 가까운 층은 양수.
        /// </summary>
        public readonly float DollyScaleResponse;

        public LayerProfile(Vec2 focusSpreadPerZoom, Vec2 panResponse, float zoomScaleResponse)
            : this(focusSpreadPerZoom, panResponse, zoomScaleResponse, Vec2.Zero, 0f)
        {
        }

        public LayerProfile(
            Vec2 focusSpreadPerZoom, Vec2 panResponse, float zoomScaleResponse, Vec2 trackResponse, float dollyScaleResponse)
        {
            FocusSpreadPerZoom = focusSpreadPerZoom;
            PanResponse = panResponse;
            ZoomScaleResponse = zoomScaleResponse;
            TrackResponse = trackResponse;
            DollyScaleResponse = dollyScaleResponse;
        }

        public static readonly LayerProfile Identity = new(Vec2.Zero, Vec2.Zero, 0f);

        public static LayerProfile Lerp(in LayerProfile a, in LayerProfile b, float t)
            => new(
                a.FocusSpreadPerZoom + (b.FocusSpreadPerZoom - a.FocusSpreadPerZoom) * t,
                a.PanResponse + (b.PanResponse - a.PanResponse) * t,
                a.ZoomScaleResponse + (b.ZoomScaleResponse - a.ZoomScaleResponse) * t,
                a.TrackResponse + (b.TrackResponse - a.TrackResponse) * t,
                a.DollyScaleResponse + (b.DollyScaleResponse - a.DollyScaleResponse) * t);
    }

    /// <summary>
    /// d → LayerProfile. 표본 사이는 선형, 바깥은 끝 표본으로 고정.
    /// 기본 표본 다섯(Default)은 종전 PresentationResponseProfile의 Far…Close 값 그대로라,
    /// 눈금 위에서는 종전과 정확히 같은 값이 나온다.
    ///
    /// panResponse는 축마다 −1보다 크고 1보다 작아야 한다. 층이 화면에서 움직이는 양은 pan × (1 + panResponse)라,
    /// −1이면 팬을 전혀 따라가지 않고(무한히 먼 층) 그보다 작으면 거꾸로 간다. 그런 층의 점은 팬으로
    /// 화면 지점에 놓을 수 없다(SolvePanToPlace). 위쪽 한계는 롤 때문이다 — 화면이 기울면 팬을 푸는 식이
    /// 2×2가 되고, 두 축이 모두 (−1, 1) 안이면 어떤 기울기에서도 풀린다. 표본이 그 안이면 선형 보간도 그렇다.
    /// </summary>
    public sealed class LayerProfileCurve
    {
        private readonly float[] _depths;
        private readonly LayerProfile[] _profiles;

        public int SampleCount => _depths.Length;

        public LayerProfileCurve(float[] depths, LayerProfile[] profiles)
        {
            if (depths == null || profiles == null || depths.Length == 0 || depths.Length != profiles.Length)
                throw new ArgumentException("층 프로필 표본이 비었거나 길이가 맞지 않는다.");

            for (int i = 1; i < depths.Length; i++)
                if (!(depths[i] > depths[i - 1]))
                    throw new ArgumentException($"층 프로필 표본은 d 오름차순이어야 한다 — [{i - 1}]={depths[i - 1]}, [{i}]={depths[i]}");

            for (int i = 0; i < profiles.Length; i++)
                if (!IsInsideUnit(profiles[i].PanResponse.X) || !IsInsideUnit(profiles[i].PanResponse.Y))
                    throw new ArgumentException($"층 프로필 panResponse는 축마다 −1보다 크고 1보다 작아야 한다 — [{i}] d={depths[i]} panResponse={profiles[i].PanResponse}");

            _depths = depths;
            _profiles = profiles;
        }

        private static bool IsInsideUnit(float value) => value > -1f && value < 1f;

        /// <summary>
        /// 다섯 층의 값. Far(-1) · Back(-0.5) · Mid(0) · Front(0.5) · Close(1).
        /// 줌 · 팬의 셋(산개 · panResponse · zoomScaleResponse)은 종전 그대로다.
        /// 트랙 · 달리의 둘(trackResponse · dollyScaleResponse)은 출발값이다 — 화면을 보고 잡는다.
        /// </summary>
        public static LayerProfileCurve Default { get; } = new(
            new[] { StageLayerLabels.Far, StageLayerLabels.Back, StageLayerLabels.Mid, StageLayerLabels.Front, StageLayerLabels.Close },
            new[]
            {
                new LayerProfile(new Vec2(-8f, 0f), new Vec2(-0.04f, 0f), -0.15f, new Vec2(-0.5f, -0.5f), -0.6f),
                new LayerProfile(new Vec2(-4f, 0f), new Vec2(-0.02f, 0f), -0.08f, new Vec2(-0.25f, -0.25f), -0.3f),
                LayerProfile.Identity,
                new LayerProfile(new Vec2(4f, 0f), new Vec2(0.02f, 0f), 0.04f, new Vec2(0.25f, 0.25f), 0.3f),
                new LayerProfile(new Vec2(16f, 0f), new Vec2(0.2f, 0f), 0.16f, new Vec2(0.6f, 0.6f), 0.8f),
            });

        public LayerProfile Evaluate(float depth)
        {
            if (depth <= _depths[0])
                return _profiles[0];

            int last = _depths.Length - 1;

            if (depth >= _depths[last])
                return _profiles[last];

            int hi = 1;
            while (_depths[hi] < depth)
                hi++;

            float t = (depth - _depths[hi - 1]) / (_depths[hi] - _depths[hi - 1]);

            return LayerProfile.Lerp(_profiles[hi - 1], _profiles[hi], t);
        }

        public static LayerProfileCurve FromDto(LayerProfileFileDto dto)
        {
            if (dto?.samples == null)
                throw new ArgumentException("layer-profile.json에 samples가 없다.");

            float[] depths = new float[dto.samples.Length];
            LayerProfile[] profiles = new LayerProfile[dto.samples.Length];

            for (int i = 0; i < dto.samples.Length; i++)
            {
                LayerProfileSampleDto s = dto.samples[i];
                depths[i] = s.depth;
                profiles[i] = new LayerProfile(
                    s.focusSpreadPerZoom.ToVec2(),
                    s.panResponse.ToVec2(),
                    s.zoomScaleResponse,
                    s.trackResponse?.ToVec2() ?? Vec2.Zero,
                    s.dollyScaleResponse);
            }

            return new LayerProfileCurve(depths, profiles);
        }

        public LayerProfileFileDto ToDto()
        {
            LayerProfileSampleDto[] samples = new LayerProfileSampleDto[_depths.Length];

            for (int i = 0; i < _depths.Length; i++)
            {
                samples[i] = new LayerProfileSampleDto
                {
                    depth = _depths[i],
                    focusSpreadPerZoom = new Float2Dto { x = _profiles[i].FocusSpreadPerZoom.X, y = _profiles[i].FocusSpreadPerZoom.Y },
                    panResponse = new Float2Dto { x = _profiles[i].PanResponse.X, y = _profiles[i].PanResponse.Y },
                    zoomScaleResponse = _profiles[i].ZoomScaleResponse,
                    trackResponse = new Float2Dto { x = _profiles[i].TrackResponse.X, y = _profiles[i].TrackResponse.Y },
                    dollyScaleResponse = _profiles[i].DollyScaleResponse,
                };
            }

            return new LayerProfileFileDto { samples = samples };
        }
    }

    /// <summary>
    /// 카메라 노드 다섯에 쓰는 값. 전부 노드 로컬 값이다 — 런타임은 받아 쓰기만 한다.
    ///
    ///   Camera_Pan.anchoredPosition   = Pan
    ///   Camera_Zoom.localScale        = ZoomScale
    ///   Camera_Roll.localEulerAngles.z = Roll
    ///   Camera_Dolly.localScale       = DollyScale
    ///   Camera_Track.anchoredPosition = TrackPosition (= −track)
    /// </summary>
    public readonly struct CameraPose
    {
        public readonly Vec2 Pan;
        public readonly float ZoomScale;
        public readonly float Roll;
        public readonly float DollyScale;
        public readonly Vec2 TrackPosition;

        public CameraPose(Vec2 pan, float zoomScale, float roll, float dollyScale, Vec2 trackPosition)
        {
            Pan = pan;
            ZoomScale = zoomScale;
            Roll = roll;
            DollyScale = dollyScale;
            TrackPosition = trackPosition;
        }

        /// <summary>카메라 노드를 다 지난 배율 — mid 면의 배율이다. 줌과 달리의 공통 몫.</summary>
        public float Scale => ZoomScale * DollyScale;

        public static readonly CameraPose Identity = new(Vec2.Zero, 1f, 0f, 1f, Vec2.Zero);
    }

    /// <summary>
    /// 한 층이 샷에 반응한 결과. 기준(항등)에서의 편차다.
    ///
    /// 노드에 쓰는 값은 LocalPosition · Scale 둘이다(층 노드의 anchoredPosition · localScale).
    /// OffsetInRigSpace · AxisInLayer는 식의 항이다 — 보이는 자리(ProjectToRigSpace)가 이 둘로 적힌다.
    /// </summary>
    public readonly struct LayerResponse
    {
        /// <summary>층 오프셋 — 초점 산개 + 팬 추종. rig space 벡터이고 롤과 함께 돈다.</summary>
        public readonly Vec2 OffsetInRigSpace;

        /// <summary>층 노드의 배율 — 카메라 노드의 공통 몫(CameraPose.Scale)을 뺀, 이 층만의 몫.</summary>
        public readonly Vec2 Scale;

        /// <summary>카메라의 축이 이 층을 뚫는 자리(층 좌표) = track × (1 + trackResponse). 배율은 이 점을 중심으로 걸린다.</summary>
        public readonly Vec2 AxisInLayer;

        /// <summary>층 노드의 위치(부모 = 카메라 리그의 가장 안쪽 노드 공간).</summary>
        public readonly Vec2 LocalPosition;

        public LayerResponse(Vec2 offsetInRigSpace, Vec2 scale, Vec2 axisInLayer, Vec2 localPosition)
        {
            OffsetInRigSpace = offsetInRigSpace;
            Scale = scale;
            AxisInLayer = axisInLayer;
            LocalPosition = localPosition;
        }

        public static readonly LayerResponse Identity = new(Vec2.Zero, Vec2.One, Vec2.Zero, Vec2.Zero);
    }

    /// <summary>샷 하나가 무대에 쓰는 값 전부. 카메라 노드 + 층마다 하나.</summary>
    public readonly struct ShotLayout
    {
        public readonly CameraPose Camera;
        public readonly LayerResponse[] Layers;

        public ShotLayout(CameraPose camera, LayerResponse[] layers)
        {
            Camera = camera;
            Layers = layers;
        }
    }

    public static class ShotLayoutMath
    {
        /// <summary>카메라 노드 다섯.</summary>
        public static CameraPose SolveCamera(in ShotIntentState intent)
            => new(
                intent.PanInRigSpace,
                ShotIntentMath.EvaluateCameraScale(intent.Zoom),
                intent.Roll,
                ShotIntentMath.EvaluateDollyScale(intent.Dolly),
                -intent.Track);

        /// <summary>
        /// 깊이 d인 층 하나의 반응.
        ///
        /// 층 오프셋 = sign(0 − focus) × focusSpread × (s − 1) + pan × panResponse
        /// 층 배율   = s ^ zoomScaleResponse × g ^ dollyScaleResponse
        /// 축의 자리 = track × (1 + trackResponse)
        /// 층 위치   = 층 오프셋 ÷ (s × g) + track − 층 배율 × 축의 자리
        ///
        /// 초점 산개의 부호는 층 기준위치(항등 = 0)와 초점의 차이에서 온다. 축별로 독립이고,
        /// 차이가 정확히 0이면 0이다(종전 Mathf.Approximately(x, 0)과 같다).
        ///
        /// 층 위치는 카메라 노드를 지나면 식의 꼴이 되게 푼 것이다: Camera_Track(−track)과 합쳐
        /// 층 오프셋 ÷ (s × g) + 층 배율 × (p − 축의 자리)가 되고, 그 위의 달리 · 롤 · 줌 · 팬이 나머지를 건다.
        /// mid 층(반응 항등)은 어떤 카메라에서도 항등으로 남는다.
        /// </summary>
        public static LayerResponse SolveLayer(in ShotIntentState intent, float depth, LayerProfileCurve curve)
        {
            LayerProfile profile = curve.Evaluate(depth);

            float cameraScale = ShotIntentMath.EvaluateCameraScale(intent.Zoom);
            float dollyScale = ShotIntentMath.EvaluateDollyScale(intent.Dolly);
            float zoomAmount = cameraScale - 1f;

            Vec2 fromFocus = Vec2.Zero - intent.FocusPointInRigSpace;

            float signX = fromFocus.X == 0f ? 0f : Math.Sign(fromFocus.X);
            float signY = fromFocus.Y == 0f ? 0f : Math.Sign(fromFocus.Y);

            Vec2 focusSpread = new(
                signX * profile.FocusSpreadPerZoom.X * zoomAmount,
                signY * profile.FocusSpreadPerZoom.Y * zoomAmount);

            Vec2 panFollow = Vec2.Scale(intent.PanInRigSpace, profile.PanResponse);
            Vec2 offset = focusSpread + panFollow;

            // 달리가 0이면 뒤쪽 거듭제곱이 정확히 1이라 종전 값(s ^ zoomScaleResponse)과 비트 단위로 같다.
            float scale = (float)(Math.Pow(cameraScale, profile.ZoomScaleResponse) * Math.Pow(dollyScale, profile.DollyScaleResponse));

            Vec2 axis = new(
                intent.Track.X * (1f + profile.TrackResponse.X),
                intent.Track.Y * (1f + profile.TrackResponse.Y));

            float commonScale = cameraScale * dollyScale;

            Vec2 localPosition = new(
                offset.X / commonScale + intent.Track.X - scale * axis.X,
                offset.Y / commonScale + intent.Track.Y - scale * axis.Y);

            return new LayerResponse(offset, new Vec2(scale, scale), axis, localPosition);
        }

        /// <summary>
        /// 층 안의 한 점이 무대 루트(rig space)의 어디에 보이는가 — 계층 Pan → Zoom → Roll → Dolly → Track → Layer를 합성한 결과.
        ///
        /// 화면 = pan + Rot(roll) · [ 층 오프셋 + M × (p − 축의 자리) ],   M = 카메라 배율(s × g) × 층 배율
        ///
        /// 무대 노드가 전부 stretch · 피벗 중앙이라 각 노드의 원점이 부모의 중심이다. 정지 프레임을 그리는 쪽(툴 프리뷰)과
        /// 하네스가 이 한 줄을 공유한다. 롤이 0이면 종전 식을 글자 그대로 탄다 — 더하는 순서까지 같아야 비트가 같다.
        /// </summary>
        public static Vec2 ProjectToRigSpace(Vec2 pointInLayer, in CameraPose camera, in LayerResponse layer)
        {
            Vec2 scaled = Vec2.Scale(pointInLayer - layer.AxisInLayer, layer.Scale) * camera.Scale;

            if (camera.Roll == 0f)
                return camera.Pan + layer.OffsetInRigSpace + scaled;

            return camera.Pan + Rotate(layer.OffsetInRigSpace + scaled, camera.Roll);
        }

        /// <summary>
        /// 층 d 안의 점이 화면(rig space)의 desired에 보이게 하는 팬 — ProjectToRigSpace의 팬에 대한 역.
        /// basis의 팬은 읽지 않는다. 나머지 축(줌 · 초점 · 트랙 · 달리 · 롤)은 그대로 입력이다 — 같은 점이라도
        /// 카메라가 옮겨 서 있거나 기울어 있으면 팬이 달라진다.
        ///
        /// 다른 축이 정해지면 층 반응이 팬에 기대는 항은 pan × panResponse 하나라, 보이는 자리는 팬에 대해 선형이다.
        ///
        /// 보이는 자리 = pan + Rot(roll) · (pan × panResponse) + [팬 0일 때 보이는 자리]
        ///
        /// 롤이 0이면 축별로 풀린다(pan × (1 + panResponse) — 종전 식). 아니면 2×2다:
        /// (I + Rot · diag(panResponse)) · pan = desired − [팬 0일 때 보이는 자리]. panResponse가 축마다 (−1, 1) 안이라
        /// 어떤 기울기에서도 풀린다(LayerProfileCurve가 보장한다). 팬 0일 때의 자리는 정방향 식을 불러 얻는다 — 식을 다시 적지 않는다.
        /// </summary>
        public static Vec2 SolvePanToPlace(
            in ShotIntentState basis,
            Vec2 pointInLayer,
            Vec2 desiredInRigSpace,
            float depth,
            LayerProfileCurve curve)
        {
            ShotIntentState unpanned = basis.WithLens(basis.Zoom, Vec2.Zero, basis.FocusPointInRigSpace);

            CameraPose camera = SolveCamera(in unpanned);
            LayerResponse layer = SolveLayer(in unpanned, depth, curve);

            Vec2 seenUnpanned = ProjectToRigSpace(pointInLayer, in camera, in layer);
            Vec2 response = curve.Evaluate(depth).PanResponse;
            Vec2 remaining = desiredInRigSpace - seenUnpanned;

            if (basis.Roll == 0f)
            {
                Vec2 follow = Vec2.One + response;
                return new Vec2(remaining.X / follow.X, remaining.Y / follow.Y);
            }

            double radians = basis.Roll * (Math.PI / 180.0);
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            // A = I + Rot · diag(a, b)
            double a11 = 1.0 + cos * response.X;
            double a12 = -sin * response.Y;
            double a21 = sin * response.X;
            double a22 = 1.0 + cos * response.Y;
            double det = a11 * a22 - a12 * a21;

            return new Vec2(
                (float)((a22 * remaining.X - a12 * remaining.Y) / det),
                (float)((a11 * remaining.Y - a21 * remaining.X) / det));
        }

        /// <summary>무대 전체. 층 배열은 depths와 같은 순서.</summary>
        public static ShotLayout Solve(in ShotIntentState intent, ReadOnlySpan<float> depths, LayerProfileCurve curve)
        {
            LayerResponse[] layers = new LayerResponse[depths.Length];

            for (int i = 0; i < depths.Length; i++)
                layers[i] = SolveLayer(in intent, depths[i], curve);

            return new ShotLayout(SolveCamera(in intent), layers);
        }

        // 화면 가운데(원점) 기준 회전. +면 반시계.
        private static Vec2 Rotate(Vec2 v, float degrees)
        {
            double radians = degrees * (Math.PI / 180.0);
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            return new Vec2(
                (float)(v.X * cos - v.Y * sin),
                (float)(v.X * sin + v.Y * cos));
        }
    }
}
