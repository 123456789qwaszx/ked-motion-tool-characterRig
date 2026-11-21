namespace Ked.Presentation.Core
{
    /// <summary>
    /// 샷 의도 — 카메라의 축 값 한 벌. 무대의 트랜스폼은 전부 이 값의 함수다(ShotLayoutMath).
    ///
    ///   렌즈  Zoom · PanInRigSpace · FocusPointInRigSpace
    ///   헤드  Roll
    ///   몸    Track · Dolly
    ///
    /// 흔들림은 여기 없다 — 끝값이 없는 연기라 의도가 아니다.
    /// </summary>
    public readonly struct ShotIntentState
    {
        // 저작된 zoom 의도. 실제 카메라 배율은 ShotIntentMath.EvaluateCameraScale.
        public readonly float Zoom;

        // 화면 밀기 — 줌 바깥에서 화면을 미는 값(rig space px). 화면이 가는 쪽이 +다.
        public readonly Vec2 PanInRigSpace;

        // rig-space 논리 focus 지점. 프레이밍·대상 산개가 이 점 기준으로 풀린다.
        public readonly Vec2 FocusPointInRigSpace;

        // 카메라가 서는 자리 — mid 면의 이 무대 점이 화면 가운데에 온다. 카메라가 가는 쪽이 +라, 화면은 반대로 흐른다.
        public readonly Vec2 Track;

        // 카메라의 앞뒤 자리. 줌과 같은 눈금이고(ShotIntentMath.EvaluateDollyScale) +면 다가간다.
        public readonly float Dolly;

        // 화면의 기울기(도). +면 그림이 반시계로 돈다.
        public readonly float Roll;

        public ShotIntentState(float zoom, Vec2 panInRigSpace, Vec2 focusPointInRigSpace)
            : this(zoom, panInRigSpace, focusPointInRigSpace, Vec2.Zero, 0f, 0f)
        {
        }

        public ShotIntentState(
            float zoom, Vec2 panInRigSpace, Vec2 focusPointInRigSpace, Vec2 track, float dolly, float roll)
        {
            Zoom = zoom;
            PanInRigSpace = panInRigSpace;
            FocusPointInRigSpace = focusPointInRigSpace;
            Track = track;
            Dolly = dolly;
            Roll = roll;
        }

        public static readonly ShotIntentState Default = new(0f, Vec2.Zero, Vec2.Zero);

        /// <summary>렌즈만 바꾼 의도 — 몸 · 헤드는 그대로다.</summary>
        public ShotIntentState WithLens(float zoom, Vec2 panInRigSpace, Vec2 focusPointInRigSpace)
            => new(zoom, panInRigSpace, focusPointInRigSpace, Track, Dolly, Roll);

        public override string ToString()
            => $"zoom={Zoom} pan={PanInRigSpace} focus={FocusPointInRigSpace} track={Track} dolly={Dolly} roll={Roll}";
    }

    public static class ShotIntentMath
    {
        // 규약 상수: zoom 1당 배율 +0.05.
        public const float ZoomToScaleFactor = 0.05f;

        public static float EvaluateCameraScale(float zoom)
            => 1f + zoom * ZoomToScaleFactor;

        // 달리는 줌과 눈금이 같다 — 달리 n과 줌 n은 mid 면에서 같은 크기다. 다른 것은 다른 층이다.
        public static float EvaluateDollyScale(float dolly)
            => 1f + dolly * ZoomToScaleFactor;
    }
}
