using System;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 카메라의 새 축(트랙 · 달리 · 롤)의 산수.
    ///
    /// 맨 앞의 것이 가장 무겁다: **새 축이 0이면 종전 식과 비트 단위로 같다.** 종전 식으로 잰 대본이
    /// 화면을 그대로 갖는 근거라서, 종전 식을 이 파일에 오라클로 적어 두고 허용 오차 없이 견준다.
    /// 나머지는 축마다의 성질이다 — 노드 값을 계층대로 합성하면 식이 된다, 달리는 mid에서 줌과 같다,
    /// 트랙의 자리가 화면 가운데에 온다, 롤은 강체다, 팬 역산이 기운 화면에서도 맞는다.
    /// </summary>
    public sealed class ShotCameraAxesTests
    {
        private const float Eps = 1e-3f;

        private static readonly LayerProfileCurve Curve = LayerProfileCurve.Default;

        private static readonly float[] Depths = { -1f, -0.75f, -0.5f, 0f, 0.25f, 0.5f, 1f };

        private static void AssertVec2(Vec2 actual, Vec2 expected, string what, float eps = Eps)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(eps), $"{what} X — actual={actual} expected={expected}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(eps), $"{what} Y — actual={actual} expected={expected}");
        }

        private static float Range(Random random, float min, float max)
            => min + (float)random.NextDouble() * (max - min);

        private static Vec2 Range2(Random random, float x, float y)
            => new(Range(random, -x, x), Range(random, -y, y));

        private static ShotIntentState RandomIntent(Random random)
            => new(
                Range(random, -6f, 8f),
                Range2(random, 300f, 150f),
                Range2(random, 500f, 300f),
                Range2(random, 400f, 200f),
                Range(random, -6f, 8f),
                Range(random, -40f, 40f));

        private static Vec2 Seen(in ShotIntentState intent, Vec2 pointInLayer, float depth)
        {
            CameraPose camera = ShotLayoutMath.SolveCamera(in intent);
            LayerResponse layer = ShotLayoutMath.SolveLayer(in intent, depth, Curve);

            return ShotLayoutMath.ProjectToRigSpace(pointInLayer, in camera, in layer);
        }

        private static Vec2 Rotate(Vec2 v, float degrees)
        {
            double radians = degrees * (Math.PI / 180.0);

            return new Vec2(
                (float)(v.X * Math.Cos(radians) - v.Y * Math.Sin(radians)),
                (float)(v.X * Math.Sin(radians) + v.Y * Math.Cos(radians)));
        }

        // ── 종전 식 — 오라클 ─────────────────────────────────────────
        // 2026-11-17, 카메라 리그 전의 ShotLayoutMath.SolveLayer · ProjectToRigSpace를 글자 그대로 옮긴 것이다. 고치지 않는다.

        private static void LegacySolveLayer(
            float zoom, Vec2 pan, Vec2 focus, float depth, out Vec2 offset, out Vec2 scale, out float cameraScale)
        {
            LayerProfile profile = Curve.Evaluate(depth);

            cameraScale = 1f + zoom * 0.05f;
            float zoomAmount = cameraScale - 1f;

            Vec2 fromFocus = Vec2.Zero - focus;

            float signX = fromFocus.X == 0f ? 0f : Math.Sign(fromFocus.X);
            float signY = fromFocus.Y == 0f ? 0f : Math.Sign(fromFocus.Y);

            Vec2 focusSpread = new(
                signX * profile.FocusSpreadPerZoom.X * zoomAmount,
                signY * profile.FocusSpreadPerZoom.Y * zoomAmount);

            Vec2 panFollow = Vec2.Scale(pan, profile.PanResponse);

            float s = (float)Math.Pow(cameraScale, profile.ZoomScaleResponse);

            offset = focusSpread + panFollow;
            scale = new Vec2(s, s);
        }

        private static Vec2 LegacyProject(Vec2 pointInLayer, float cameraScale, Vec2 pan, Vec2 offset, Vec2 scale)
            => pan + offset + Vec2.Scale(pointInLayer, scale) * cameraScale;

        [Test]
        public void 새_축이_0이면_종전_식과_비트_단위로_같다()
        {
            Random random = new(20261117);

            for (int i = 0; i < 2000; i++)
            {
                float zoom = Range(random, -6f, 8f);
                Vec2 pan = Range2(random, 300f, 150f);
                Vec2 focus = i % 5 == 0 ? Vec2.Zero : Range2(random, 500f, 300f);
                Vec2 p = Range2(random, 960f, 540f);
                float depth = i % 3 == 0 ? Depths[i % Depths.Length] : Range(random, -1.2f, 1.2f);

                LegacySolveLayer(zoom, pan, focus, depth, out Vec2 offset, out Vec2 scale, out float cameraScale);
                Vec2 expected = LegacyProject(p, cameraScale, pan, offset, scale);

                ShotIntentState intent = new(zoom, pan, focus);
                CameraPose camera = ShotLayoutMath.SolveCamera(in intent);
                LayerResponse layer = ShotLayoutMath.SolveLayer(in intent, depth, Curve);
                Vec2 actual = ShotLayoutMath.ProjectToRigSpace(p, in camera, in layer);

                string where = $"zoom={zoom} pan={pan} focus={focus} d={depth} p={p}";

                // 허용 오차가 없다 — Is.EqualTo는 float를 정확히 견준다.
                Assert.That(actual.X, Is.EqualTo(expected.X), $"보이는 자리 X — {where}");
                Assert.That(actual.Y, Is.EqualTo(expected.Y), $"보이는 자리 Y — {where}");
                Assert.That(layer.OffsetInRigSpace.X, Is.EqualTo(offset.X), $"층 오프셋 X — {where}");
                Assert.That(layer.OffsetInRigSpace.Y, Is.EqualTo(offset.Y), $"층 오프셋 Y — {where}");
                Assert.That(layer.Scale.X, Is.EqualTo(scale.X), $"층 배율 — {where}");
                Assert.That(camera.Scale, Is.EqualTo(cameraScale), $"카메라 배율 — {where}");
                Assert.That(camera.Pan.X, Is.EqualTo(pan.X), $"팬 X — {where}");
                Assert.That(camera.Pan.Y, Is.EqualTo(pan.Y), $"팬 Y — {where}");

                // 종전에 런타임이 층 노드에 쓰던 값은 층 오프셋 ÷ 카메라 배율이다.
                Assert.That(layer.LocalPosition.X, Is.EqualTo(offset.X / cameraScale), $"층 위치 X — {where}");
                Assert.That(layer.LocalPosition.Y, Is.EqualTo(offset.Y / cameraScale), $"층 위치 Y — {where}");
            }
        }

        [Test]
        public void Default_의도는_카메라_노드가_전부_항등이다()
        {
            CameraPose camera = ShotLayoutMath.SolveCamera(ShotIntentState.Default);

            AssertVec2(camera.Pan, Vec2.Zero, "팬");
            Assert.That(camera.ZoomScale, Is.EqualTo(1f));
            Assert.That(camera.Roll, Is.EqualTo(0f));
            Assert.That(camera.DollyScale, Is.EqualTo(1f));
            AssertVec2(camera.TrackPosition, Vec2.Zero, "트랙");

            foreach (float d in Depths)
            {
                LayerResponse layer = ShotLayoutMath.SolveLayer(ShotIntentState.Default, d, Curve);

                AssertVec2(layer.LocalPosition, Vec2.Zero, $"d={d} 층 위치");
                AssertVec2(layer.Scale, Vec2.One, $"d={d} 층 배율");
            }
        }

        // ── 노드 값 = 식 ─────────────────────────────────────────────

        [Test]
        public void 노드_값을_계층대로_합성하면_보이는_자리가_된다()
        {
            // 런타임은 CameraPose · LayerResponse의 노드 로컬 값을 받아 쓰기만 한다. 그 계층
            // Pan → Zoom → Roll → Dolly → Track → Layer를 손으로 합성한 값이 식(ProjectToRigSpace)과 같아야 한다.
            Random random = new(7);

            for (int i = 0; i < 500; i++)
            {
                ShotIntentState intent = RandomIntent(random);
                float depth = Range(random, -1.2f, 1.2f);
                Vec2 p = Range2(random, 960f, 540f);

                CameraPose camera = ShotLayoutMath.SolveCamera(in intent);
                LayerResponse layer = ShotLayoutMath.SolveLayer(in intent, depth, Curve);

                Vec2 x = layer.LocalPosition + Vec2.Scale(p, layer.Scale);   // Layer
                x = camera.TrackPosition + x;                                // Camera_Track
                x = x * camera.DollyScale;                                   // Camera_Dolly
                x = Rotate(x, camera.Roll);                                  // Camera_Roll
                x = x * camera.ZoomScale;                                    // Camera_Zoom
                x = camera.Pan + x;                                          // Camera_Pan

                AssertVec2(x, ShotLayoutMath.ProjectToRigSpace(p, in camera, in layer), $"{intent} d={depth} p={p}", 2e-2f);
            }
        }

        [Test]
        public void Mid_층은_어떤_카메라에서도_항등으로_남는다()
        {
            // 공통 몫은 카메라 노드가 갖고 층 노드에는 d에 따른 차이만 남는다 — mid는 차이가 없다.
            Random random = new(11);

            for (int i = 0; i < 200; i++)
            {
                ShotIntentState intent = RandomIntent(random);
                LayerResponse mid = ShotLayoutMath.SolveLayer(in intent, StageLayerLabels.Mid, Curve);

                AssertVec2(mid.LocalPosition, Vec2.Zero, $"{intent} 위치");
                AssertVec2(mid.Scale, Vec2.One, $"{intent} 배율", 1e-6f);
            }
        }

        // ── 달리 ─────────────────────────────────────────────────────

        [Test]
        public void 달리는_mid_면에서_줌과_같은_크기다()
        {
            Vec2 p = new(320f, -140f);

            foreach (float n in new[] { -4f, 2f, 6f })
            {
                Vec2 zoomed = Seen(new ShotIntentState(n, Vec2.Zero, Vec2.Zero), p, StageLayerLabels.Mid);
                Vec2 dollied = Seen(new ShotIntentState(0f, Vec2.Zero, Vec2.Zero, Vec2.Zero, n, 0f), p, StageLayerLabels.Mid);

                AssertVec2(dollied, zoomed, $"n={n}");
            }
        }

        [Test]
        public void 달리는_먼_층을_덜_가까운_층을_더_키운다()
        {
            ShotIntentState intent = new(0f, Vec2.Zero, Vec2.Zero, Vec2.Zero, 4f, 0f);
            CameraPose camera = ShotLayoutMath.SolveCamera(in intent);

            float previous = 0f;

            foreach (float d in StageLayerLabels.Depths)
            {
                float total = camera.Scale * ShotLayoutMath.SolveLayer(in intent, d, Curve).Scale.X;
                float expected = (float)Math.Pow(1.2, 1.0 + Curve.Evaluate(d).DollyScaleResponse);

                Assert.That(total, Is.EqualTo(expected).Within(1e-5f), $"d={d} — 1.2 ^ (1 + dollyScaleResponse)");
                Assert.That(total, Is.GreaterThan(previous), $"d={d} — 가까울수록 크다");

                previous = total;
            }
        }

        [Test]
        public void 달리와_줌을_반대로_걸면_mid는_그대로이고_다른_층만_변한다()
        {
            // 달리 줌 — 인물(mid)은 그대로인데 배경만 커지거나 작아진다. 축을 가른 값이다.
            // mid 면의 배율이 정확히 1이 되게 줌을 맞춘다: s × g = 1.
            float dolly = 5f;
            float zoom = (1f / ShotIntentMath.EvaluateDollyScale(dolly) - 1f) / ShotIntentMath.ZoomToScaleFactor;

            ShotIntentState intent = new(zoom, Vec2.Zero, Vec2.Zero, Vec2.Zero, dolly, 0f);
            Vec2 p = new(400f, 100f);

            AssertVec2(Seen(in intent, p, StageLayerLabels.Mid), p, "mid는 제자리");

            Vec2 far = Seen(in intent, p, StageLayerLabels.Far);
            Assert.That(far.X, Is.LessThan(p.X - 1f), "먼 층은 다가간 만큼 덜 커져서 줌 아웃에 진다 — 작아진다");
        }

        // ── 트랙 ─────────────────────────────────────────────────────

        [Test]
        public void 트랙의_자리가_화면_가운데에_온다()
        {
            // 트랙 값 = mid 면에서 화면 가운데에 놓이는 무대 점. 줌 · 달리 · 롤을 걸어도 그 점은 가운데에 남는다 —
            // 배율과 회전이 카메라의 축을 중심으로 걸리기 때문이다.
            Vec2 track = new(260f, -80f);

            foreach (float zoom in new[] { 0f, 4f })
            foreach (float dolly in new[] { 0f, -3f, 6f })
            foreach (float roll in new[] { 0f, 12f })
            {
                ShotIntentState intent = new(zoom, Vec2.Zero, Vec2.Zero, track, dolly, roll);

                AssertVec2(Seen(in intent, track, StageLayerLabels.Mid), Vec2.Zero, $"zoom={zoom} dolly={dolly} roll={roll}");
            }
        }

        [Test]
        public void 트랙은_층을_반대로_밀고_먼_층을_덜_민다()
        {
            Vec2 track = new(200f, 50f);
            ShotIntentState intent = new(0f, Vec2.Zero, Vec2.Zero, track, 0f, 0f);

            foreach (float d in StageLayerLabels.Depths)
            {
                Vec2 response = Curve.Evaluate(d).TrackResponse;
                Vec2 expected = new(-track.X * (1f + response.X), -track.Y * (1f + response.Y));

                AssertVec2(Seen(in intent, Vec2.Zero, d), expected, $"d={d} — −track × (1 + trackResponse)");
            }

            float far = Math.Abs(Seen(in intent, Vec2.Zero, StageLayerLabels.Far).X);
            float mid = Math.Abs(Seen(in intent, Vec2.Zero, StageLayerLabels.Mid).X);
            float close = Math.Abs(Seen(in intent, Vec2.Zero, StageLayerLabels.Close).X);

            Assert.That(mid, Is.EqualTo(200f).Within(Eps), "mid는 트랙만큼");
            Assert.That(far, Is.LessThan(mid), "먼 층은 덜");
            Assert.That(close, Is.GreaterThan(mid), "가까운 층은 더");
        }

        // ── 롤 ───────────────────────────────────────────────────────

        [Test]
        public void 롤은_팬_안쪽을_강체로_돌린다()
        {
            // 같은 샷에 롤만 얹으면, 모든 층의 모든 점이 팬 자리를 중심으로 같은 각도만큼 돈다.
            Random random = new(3);

            for (int i = 0; i < 200; i++)
            {
                ShotIntentState rolled = RandomIntent(random);
                ShotIntentState level = new(
                    rolled.Zoom, rolled.PanInRigSpace, rolled.FocusPointInRigSpace, rolled.Track, rolled.Dolly, 0f);

                float depth = Range(random, -1.2f, 1.2f);
                Vec2 p = Range2(random, 960f, 540f);

                Vec2 expected = rolled.PanInRigSpace + Rotate(Seen(in level, p, depth) - rolled.PanInRigSpace, rolled.Roll);

                AssertVec2(Seen(in rolled, p, depth), expected, $"{rolled} d={depth} p={p}", 2e-2f);
            }
        }

        [Test]
        public void 롤_양수는_그림을_반시계로_돌린다()
        {
            ShotIntentState intent = new(0f, Vec2.Zero, Vec2.Zero, Vec2.Zero, 0f, 90f);

            AssertVec2(Seen(in intent, new Vec2(100f, 0f), StageLayerLabels.Mid), new Vec2(0f, 100f), "오른쪽 점이 위로 간다");
        }

        // ── 팬 역산 ──────────────────────────────────────────────────

        [Test]
        public void 팬_역산은_카메라가_옮겨_서고_기울어도_맞는다()
        {
            // FocusTo의 식 — 지금 카메라의 몸 · 헤드가 입력이다. 푼 팬을 정방향 식에 넣으면 원하는 자리가 나온다.
            Random random = new(5);

            for (int i = 0; i < 500; i++)
            {
                ShotIntentState basis = RandomIntent(random);

                if (i % 4 == 0)
                    basis = new(basis.Zoom, basis.PanInRigSpace, basis.FocusPointInRigSpace, basis.Track, basis.Dolly, 0f);

                float depth = Range(random, -1.2f, 1.2f);
                Vec2 p = Range2(random, 960f, 540f);
                Vec2 desired = Range2(random, 600f, 300f);

                Vec2 pan = ShotLayoutMath.SolvePanToPlace(in basis, p, desired, depth, Curve);
                ShotIntentState solved = basis.WithLens(basis.Zoom, pan, basis.FocusPointInRigSpace);

                AssertVec2(Seen(in solved, p, depth), desired, $"{basis} d={depth} p={p}", 5e-2f);
            }
        }

        [Test]
        public void 팬_역산은_basis의_팬을_읽지_않는다()
        {
            ShotIntentState a = new(3f, new Vec2(500f, -200f), new Vec2(40f, 0f), new Vec2(100f, 0f), 2f, 8f);
            ShotIntentState b = a.WithLens(a.Zoom, Vec2.Zero, a.FocusPointInRigSpace);

            Vec2 p = new(120f, 60f);
            Vec2 desired = new(-200f, 30f);

            Assert.That(
                ShotLayoutMath.SolvePanToPlace(in a, p, desired, 0.5f, Curve),
                Is.EqualTo(ShotLayoutMath.SolvePanToPlace(in b, p, desired, 0.5f, Curve)));
        }

        // ── 프로필 ───────────────────────────────────────────────────

        [Test]
        public void panResponse는_축마다_1보다_작아야_한다()
        {
            // 화면이 기울면 팬을 푸는 식이 2×2가 된다. 두 축이 (−1, 1) 안이어야 어떤 기울기에서도 풀린다.
            Assert.Throws<ArgumentException>(() => new LayerProfileCurve(
                new[] { 0f },
                new[] { new LayerProfile(Vec2.Zero, new Vec2(1f, 0f), 0f) }));

            Assert.DoesNotThrow(() => new LayerProfileCurve(
                new[] { 0f },
                new[] { new LayerProfile(Vec2.Zero, new Vec2(0.99f, -0.99f), 0f) }));
        }

        [Test]
        public void 옛_프로필_파일은_트랙_달리_반응이_0이다()
        {
            // trackResponse · dollyScaleResponse가 없던 때의 layer-profile.json — 없으면 0으로 읽는다.
            LayerProfileFileDto old = new()
            {
                samples = new[]
                {
                    new LayerProfileSampleDto
                    {
                        depth = -1f,
                        focusSpreadPerZoom = new Float2Dto { x = -8f, y = 0f },
                        panResponse = new Float2Dto { x = -0.04f, y = 0f },
                        zoomScaleResponse = -0.15f,
                    },
                },
            };

            LayerProfile profile = LayerProfileCurve.FromDto(old).Evaluate(-1f);

            AssertVec2(profile.TrackResponse, Vec2.Zero, "trackResponse");
            Assert.That(profile.DollyScaleResponse, Is.EqualTo(0f));
        }

        [Test]
        public void DTO_왕복은_트랙_달리_반응을_잃지_않는다()
        {
            LayerProfileCurve round = LayerProfileCurve.FromDto(Curve.ToDto());

            foreach (float d in Depths)
            {
                LayerProfile a = Curve.Evaluate(d);
                LayerProfile b = round.Evaluate(d);

                AssertVec2(b.TrackResponse, a.TrackResponse, $"d={d} trackResponse", 1e-6f);
                Assert.That(b.DollyScaleResponse, Is.EqualTo(a.DollyScaleResponse).Within(1e-6f), $"d={d} dollyScaleResponse");
            }
        }

        [Test]
        public void 기본_곡선의_mid는_트랙_달리에도_항등이다()
        {
            LayerProfile mid = Curve.Evaluate(StageLayerLabels.Mid);

            AssertVec2(mid.TrackResponse, Vec2.Zero, "trackResponse");
            Assert.That(mid.DollyScaleResponse, Is.EqualTo(0f));
        }
    }
}
