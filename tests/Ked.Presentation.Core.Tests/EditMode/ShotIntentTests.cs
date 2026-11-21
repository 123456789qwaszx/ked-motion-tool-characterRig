using System;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 샷 목표 계산. 기대값은 종전 BuildTargetState 본문에서 온다.
    ///
    /// FocusTo(초점을 화면 지점에 오게 하는 렌즈)는 값보다 **명중**이 정의다: 접은 샷을 정방향 식(SolveLayer → ProjectToRigSpace)에 넣어
    /// 그 점이 원하는 화면 지점에 보이는지 본다. 층마다 반응이 달라도 명중해야 한다.
    /// </summary>
    public sealed class ShotIntentTests
    {
        private const float Eps = 1e-4f;

        private static readonly ShotIntentState From =
            new(2f, new Vec2(30f, -10f), new Vec2(100f, 200f));

        private static readonly LayerProfileCurve Curve = LayerProfileCurve.Default;

        private static void AssertVec2(Vec2 actual, Vec2 expected, string what, float eps = Eps)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(eps), $"{what} X — actual={actual}");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(eps), $"{what} Y — actual={actual}");
        }

        // ── 규약 상수 ────────────────────────────────────────────────

        [Test]
        public void zoom은_1당_배율_0_05다()
        {
            // 커맨드(목표 계산)와 적용측(카메라 루트·response)이 같이 보는 규약이다.
            Assert.That(ShotIntentMath.ZoomToScaleFactor, Is.EqualTo(0.05f));

            Assert.That(ShotIntentMath.EvaluateCameraScale(0f), Is.EqualTo(1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(2f), Is.EqualTo(1.1f).Within(Eps));
            Assert.That(ShotIntentMath.EvaluateCameraScale(-4f), Is.EqualTo(0.8f).Within(Eps));
        }

        // ── 단순 목표 계산 — shot_<축>은 그 축 하나만 바꾼다 ───────────

        [Test]
        public void shot_zoom은_zoom만_바꾼다()
        {
            ShotIntentState to = ShotIntents.Zoom(From, 5f);

            Assert.That(to.Zoom, Is.EqualTo(5f));
            AssertVec2(to.PanInRigSpace, From.PanInRigSpace, "pan 유지");
            AssertVec2(to.FocusPointInRigSpace, From.FocusPointInRigSpace, "focus 유지");
        }

        // 여섯 축이 모두 0이 아닌 출발점 — 어느 축이 "그대로"인지 드러난다.
        private static readonly ShotIntentState Full = new(
            2f, new Vec2(30f, -10f), new Vec2(100f, 50f), new Vec2(240f, 60f), 4f, 12f);

        private static void AssertOnlyChanged(in ShotIntentState to, in ShotIntentState from, string changed)
        {
            if (changed != "zoom") Assert.That(to.Zoom, Is.EqualTo(from.Zoom), "zoom 유지");
            if (changed != "pan") AssertVec2(to.PanInRigSpace, from.PanInRigSpace, "pan 유지");
            if (changed != "focus") AssertVec2(to.FocusPointInRigSpace, from.FocusPointInRigSpace, "focus 유지");
            if (changed != "track") AssertVec2(to.Track, from.Track, "track 유지");
            if (changed != "dolly") Assert.That(to.Dolly, Is.EqualTo(from.Dolly), "dolly 유지");
            if (changed != "roll") Assert.That(to.Roll, Is.EqualTo(from.Roll), "roll 유지");
        }

        [Test]
        public void shot_축_명령은_그_축만_절대값으로_바꾼다()
        {
            ShotIntentState zoom = ShotIntents.Zoom(Full, 5f);
            Assert.That(zoom.Zoom, Is.EqualTo(5f));
            AssertOnlyChanged(in zoom, Full, "zoom");

            ShotIntentState pan = ShotIntents.Pan(Full, new Vec2(-80f, 60f));
            AssertVec2(pan.PanInRigSpace, new Vec2(-80f, 60f), "pan 절대");
            AssertOnlyChanged(in pan, Full, "pan");

            ShotIntentState focus = ShotIntents.Focus(Full, new Vec2(240f, -40f));
            AssertVec2(focus.FocusPointInRigSpace, new Vec2(240f, -40f), "focus 절대");
            AssertOnlyChanged(in focus, Full, "focus");

            ShotIntentState track = ShotIntents.Track(Full, new Vec2(-120f, 0f));
            AssertVec2(track.Track, new Vec2(-120f, 0f), "track 절대 — 지금 값에 더하지 않는다");
            AssertOnlyChanged(in track, Full, "track");

            ShotIntentState dolly = ShotIntents.Dolly(Full, -3f);
            Assert.That(dolly.Dolly, Is.EqualTo(-3f));
            AssertOnlyChanged(in dolly, Full, "dolly");

            ShotIntentState roll = ShotIntents.Roll(Full, -8f);
            Assert.That(roll.Roll, Is.EqualTo(-8f));
            AssertOnlyChanged(in roll, Full, "roll");
        }

        [Test]
        public void 줌과_팬을_나란히_적으면_둘_다_남는다()
        {
            // 줌 + 팬의 묶음 명령은 없다 — 둘을 나란히 적는다. 어느 쪽을 먼저 적어도 같은 의도다.
            ShotIntentState zoomThenPan = ShotIntents.Pan(ShotIntents.Zoom(Full, 4f), new Vec2(-80f, 60f));
            ShotIntentState panThenZoom = ShotIntents.Zoom(ShotIntents.Pan(Full, new Vec2(-80f, 60f)), 4f);

            Assert.That(zoomThenPan.Zoom, Is.EqualTo(4f));
            AssertVec2(zoomThenPan.PanInRigSpace, new Vec2(-80f, 60f), "pan 절대");
            Assert.That(panThenZoom.ToString(), Is.EqualTo(zoomThenPan.ToString()));

            // 초점 · 몸 · 롤은 건드리지 않는다.
            AssertVec2(zoomThenPan.FocusPointInRigSpace, Full.FocusPointInRigSpace, "focus 유지");
            AssertVec2(zoomThenPan.Track, Full.Track, "track 유지");
            Assert.That(zoomThenPan.Dolly, Is.EqualTo(Full.Dolly));
            Assert.That(zoomThenPan.Roll, Is.EqualTo(Full.Roll));
        }

        [Test]
        public void shot_reset은_렌즈만_중립으로_돌린다()
        {
            // 장면의 구도(몸)와 기울기는 두고, 그 자리의 중립 렌즈로 돌아온다.
            ShotIntentState to = ShotIntents.Reset(Full);

            Assert.That(to.Zoom, Is.EqualTo(0f));
            AssertVec2(to.PanInRigSpace, Vec2.Zero, "pan 0");
            AssertVec2(to.FocusPointInRigSpace, Vec2.Zero, "focus 0");
            AssertVec2(to.Track, Full.Track, "track 유지");
            Assert.That(to.Dolly, Is.EqualTo(Full.Dolly), "dolly 유지");
            Assert.That(to.Roll, Is.EqualTo(Full.Roll), "roll 유지");

            // 새 축을 쓰지 않는 대본에서는 종전 그대로 — 기본 샷이다.
            Assert.That(ShotIntents.Reset(From).ToString(), Is.EqualTo(ShotIntentState.Default.ToString()));
        }

        [Test]
        public void shot_reset_all은_카메라를_통째로_중립으로_돌린다()
        {
            Assert.That(ShotIntents.ResetAll().ToString(), Is.EqualTo(ShotIntentState.Default.ToString()));
        }

        // ── FocusTo(초점을 화면 지점에 오게 하는 줌 · 팬): 명중 ──────

        // 접은 샷에서 층 d 안의 점이 보이는 자리 — 런타임 계층(카메라 리그 → Layer)과 같은 정방향 식.
        private static Vec2 SeenAt(in ShotIntentState shot, Vec2 pointInLayer, float depth)
        {
            CameraPose camera = ShotLayoutMath.SolveCamera(in shot);
            LayerResponse layer = ShotLayoutMath.SolveLayer(in shot, depth, Curve);
            return ShotLayoutMath.ProjectToRigSpace(pointInLayer, in camera, in layer);
        }

        private static readonly float[] Depths =
        {
            StageLayerLabels.Far, StageLayerLabels.Back, StageLayerLabels.Mid,
            StageLayerLabels.Front, StageLayerLabels.Close, 0.25f, -0.8f,
        };

        [Test]
        public void 접은_샷에서_그_점은_어느_층에_서도_원하는_지점에_보인다()
        {
            // 이것이 FocusTo의 정의다. 초점이 왼쪽 · 오른쪽 · 정확히 0인 축을 모두 지나게 한다(산개 부호).
            Vec2[] focuses = { new(-460.8f, 300f), new(250f, -380f), new(0f, 820f) };
            Vec2[] desireds = { Vec2.Zero, new(-460.8f, 0f), new(300f, 200f) };

            foreach (float depth in Depths)
            foreach (Vec2 focus in focuses)
            foreach (Vec2 desired in desireds)
            foreach (float zoom in new[] { 2.5f, 6f, -4f })
            {
                ShotIntentState to = ShotIntents.FocusTo(ShotIntentState.Default, zoom, focus, desired, depth, Curve);

                // 값이 수백 px이라 float 왕복 오차를 1e-3까지 둔다.
                AssertVec2(SeenAt(in to, focus, depth), desired, $"d={depth} focus={focus} desired={desired} zoom={zoom}", 1e-3f);
                Assert.That(to.Zoom, Is.EqualTo(zoom));
            }
        }

        [Test]
        public void 논리_focus는_무대_위_자리_그대로다()
        {
            // 층 산개의 기준이 되는 점이다. 카메라를 벗길 것이 없다 — 입력이 이미 무대 좌표다.
            Vec2 focus = new(250f, 380f);

            ShotIntentState to = ShotIntents.FocusTo(ShotIntentState.Default, 6f, focus, Vec2.Zero, StageLayerLabels.Close, Curve);

            AssertVec2(to.FocusPointInRigSpace, focus, "논리 focus");
        }

        [Test]
        public void mid에서는_종전_식_desired_빼기_focus_곱하기_배율이다()
        {
            // mid의 반응은 항등이라 카메라만 남는다 — 카메라만 본 팬과 같다.
            Vec2 focus = new(250f, 380f);
            Vec2 desired = new(-460.8f, 0f);
            float scale = ShotIntentMath.EvaluateCameraScale(6f);

            ShotIntentState to = ShotIntents.FocusTo(ShotIntentState.Default, 6f, focus, desired, StageLayerLabels.Mid, Curve);

            AssertVec2(to.PanInRigSpace, desired - focus * scale, "mid 팬");
        }

        [Test]
        public void mid가_아닌_층에서는_카메라만으로_푼_팬과_다르다()
        {
            // 층 반응을 빼고 풀면 빗나간다 — 이 목표 계산이 층을 보는 이유.
            Vec2 focus = new(-460.8f, 300f);
            float scale = ShotIntentMath.EvaluateCameraScale(2.5f);
            Vec2 cameraOnly = Vec2.Zero - focus * scale;

            ShotIntentState to = ShotIntents.FocusTo(ShotIntentState.Default, 2.5f, focus, Vec2.Zero, StageLayerLabels.Close, Curve);

            Assert.That(Math.Abs(to.PanInRigSpace.X - cameraOnly.X), Is.GreaterThan(1f), $"close 팬={to.PanInRigSpace} 카메라만={cameraOnly}");
        }

        [Test]
        public void 카메라가_옮겨_서고_기울어_있어도_그_점은_원하는_지점에_보인다()
        {
            // 몸과 롤은 입력이다 — 같은 대상이라도 카메라가 선 자리에 따라 팬이 달라지고, 몸과 롤은 그대로 남는다.
            Vec2 focus = new(-460.8f, 300f);
            Vec2 desired = new(300f, 200f);

            foreach (float depth in Depths)
            foreach (float roll in new[] { 0f, 10f, -25f })
            {
                ShotIntentState from = new(1f, new Vec2(999f, 999f), new Vec2(5f, 5f), new Vec2(240f, -60f), 4f, roll);
                ShotIntentState to = ShotIntents.FocusTo(from, 2.5f, focus, desired, depth, Curve);

                AssertVec2(SeenAt(in to, focus, depth), desired, $"d={depth} roll={roll}", 5e-2f);
                AssertVec2(to.Track, from.Track, "track 유지");
                Assert.That(to.Dolly, Is.EqualTo(from.Dolly), "dolly 유지");
                Assert.That(to.Roll, Is.EqualTo(from.Roll), "roll 유지");
                Assert.That(to.Zoom, Is.EqualTo(2.5f));
                AssertVec2(to.FocusPointInRigSpace, focus, "논리 focus");
            }
        }

        [Test]
        public void 렌즈는_입력이_아니다_지금_줌과_팬이_달라도_같은_결과다()
        {
            Vec2 focus = new(120f, 90f);
            Vec2 desired = new(50f, -20f);

            ShotIntentState a = new(0f, Vec2.Zero, Vec2.Zero, new Vec2(100f, 0f), 2f, 5f);
            ShotIntentState b = new(7f, new Vec2(-300f, 80f), new Vec2(40f, 40f), new Vec2(100f, 0f), 2f, 5f);

            Assert.That(
                ShotIntents.FocusTo(a, 3f, focus, desired, StageLayerLabels.Front, Curve).ToString(),
                Is.EqualTo(ShotIntents.FocusTo(b, 3f, focus, desired, StageLayerLabels.Front, Curve).ToString()));
        }

        [Test]
        public void 같은_지점을_다시_요청하면_같은_결과다()
        {
            // 순수 함수 — 같은 입력은 언제나 같은 출력.
            Vec2 focus = new(120f, 90f);
            Vec2 desired = new(50f, -20f);

            ShotIntentState a = ShotIntents.FocusTo(ShotIntentState.Default, 3f, focus, desired, StageLayerLabels.Front, Curve);
            ShotIntentState b = ShotIntents.FocusTo(ShotIntentState.Default, 3f, focus, desired, StageLayerLabels.Front, Curve);

            Assert.That(b.Zoom, Is.EqualTo(a.Zoom));
            AssertVec2(b.PanInRigSpace, a.PanInRigSpace, "pan");
            AssertVec2(b.FocusPointInRigSpace, a.FocusPointInRigSpace, "focus");
        }
    }
}
