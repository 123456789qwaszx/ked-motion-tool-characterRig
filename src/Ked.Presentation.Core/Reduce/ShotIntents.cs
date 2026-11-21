namespace Ked.Presentation.Core
{
    // ShotIntents — 샷 커맨드가 끝나면 샷 의도가 어떤 값이어야 하는가(목표 계산).
    // 샷 축의 끝값은 노드 타깃이 아니라 ShotIntentState 그 자체.
    // 의도를 실제 트랜스폼으로 푸는 것(카메라 리그 · 층 반응)은 적용측의 일.
    // 메서드 이름은 대본 커맨드 이름을 따른다: shot_zoom → Zoom.
    //
    // 어휘의 규칙: shot_<축>은 그 축 하나를 절대값으로 놓는다. 적지 않은 축은 건드리지 않는다 —
    // 그래서 전부 from을 받아 제 축만 바꾼 의도를 낸다. 상대값(지금 값에 더하기)은 없다.
    public static class ShotIntents
    {
        // shot_zoom - 줌만 바꾼다.
        public static ShotIntentState Zoom(in ShotIntentState from, float zoom)
            => from.WithLens(zoom, from.PanInRigSpace, from.FocusPointInRigSpace);

        // shot_pan - 팬(화면 밀기)만 바꾼다.
        public static ShotIntentState Pan(in ShotIntentState from, Vec2 pan)
            => from.WithLens(from.Zoom, pan, from.FocusPointInRigSpace);


        // shot_focus - 초점만 바꾼다. 초점은 줌할 때 앞 · 뒤 층이 벌어지는 쪽의 기준이다(무대 좌표) — 줌 · 팬은 그대로다.
        public static ShotIntentState Focus(in ShotIntentState from, Vec2 focus)
            => from.WithLens(from.Zoom, from.PanInRigSpace, focus);

        // shot_track - 카메라가 서는 자리를 절대값으로. mid 면의 그 무대 점이 화면 가운데에 온다.
        public static ShotIntentState Track(in ShotIntentState from, Vec2 track)
            => new(from.Zoom, from.PanInRigSpace, from.FocusPointInRigSpace, track, from.Dolly, from.Roll);

        // shot_dolly - 카메라의 앞뒤 자리를 절대값으로. +면 다가간다.
        public static ShotIntentState Dolly(in ShotIntentState from, float dolly)
            => new(from.Zoom, from.PanInRigSpace, from.FocusPointInRigSpace, from.Track, dolly, from.Roll);

        // shot_roll - 화면의 기울기(도)를 절대값으로. +면 그림이 반시계로 돈다.
        public static ShotIntentState Roll(in ShotIntentState from, float roll)
            => new(from.Zoom, from.PanInRigSpace, from.FocusPointInRigSpace, from.Track, from.Dolly, roll);

        // shot_reset - 렌즈(줌 · 팬 · 초점)를 중립으로. 카메라의 몸과 롤은 그대로다 —
        // 장면의 구도(몸)는 두고 그 자리의 중립 렌즈로 돌아온다.
        public static ShotIntentState Reset(in ShotIntentState from)
            => from.WithLens(0f, Vec2.Zero, Vec2.Zero);

        // shot_reset_all - 카메라를 통째로 중립으로.
        public static ShotIntentState ResetAll()
            => ShotIntentState.Default;

        // 캐릭터 focus 지점이 화면의 지정 지점에 오는 zoom/pan. 커맨드가 아니다 — 도구의 보조 버튼이 부르는 계산이다
        // (docs/PLAN_rig-layers.md L11. 버튼은 풀린 초점 · 줌 · 팬을 shot_focus · shot_zoom · shot_pan으로 적는다).
        //
        // focus는 무대 좌표다(카메라 중립). 런타임은 정착 사슬이 샷 노드를 중립으로 재고, 폴드는 상태가 곧 무대 좌표다.
        // 그 점이 선 층(d)의 반응까지 넣어, 목표 배율에서 원하는 지점에 보이는 팬을 푼다.
        // 논리 focus는 그 점 자체다 — 층 산개의 기준이 된다.
        //
        // 렌즈는 입력이 아니다(지금 줌 · 팬 · 초점과 무관하게 풀린다). 카메라의 몸과 롤은 입력이다 —
        // 같은 대상이라도 카메라가 옮겨 서 있거나 기울어 있으면 팬이 달라진다. 몸과 롤은 바꾸지 않는다.
        public static ShotIntentState FocusTo(
            in ShotIntentState from,
            float targetZoom,
            Vec2 focusInStageSpace,
            Vec2 desiredPointInRigSpace,
            float layerDepth,
            LayerProfileCurve curve)
        {
            ShotIntentState basis = from.WithLens(targetZoom, Vec2.Zero, focusInStageSpace);

            Vec2 pan = ShotLayoutMath.SolvePanToPlace(
                in basis,
                focusInStageSpace,
                desiredPointInRigSpace,
                layerDepth,
                curve);

            return basis.WithLens(targetZoom, pan, focusInStageSpace);
        }
    }
}
