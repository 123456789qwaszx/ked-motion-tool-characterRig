namespace Ked.Presentation.Core
{
    /// <summary>
    /// 덮는 무대 하나의 그림 팝업 끝 상태 — docs/PLAN_popup.md §7.
    /// 페이드(등장 · 퇴장) 도중은 접지 않는다. 다른 커맨드처럼 끝값만이다:
    /// 열림 = 뒷막과 그림이 불투명도 1로 보인다, 닫힘 = 둘 다 안 보인다.
    /// 그림 배율 · 뒷막 색과 세기 · 페이드는 게임 프리셋(런타임 PopupPresetDBSO)이라 여기 없다.
    /// </summary>
    public readonly struct PopupState
    {
        public readonly bool IsOpen;

        /// <summary>그림 키 — Resources/Popups/{키}.</summary>
        public readonly string PictureKey;

        /// <summary>그림 중심. 무대 중심 기준 px, y 위.</summary>
        public readonly Vec2 Center;

        /// <summary>프리셋 배율에 곱하는 배율.</summary>
        public readonly float Scale;

        public PopupState(bool isOpen, string pictureKey, Vec2 center, float scale)
        {
            IsOpen = isOpen;
            PictureKey = pictureKey;
            Center = center;
            Scale = scale;
        }

        public PopupState Closed() => new(false, PictureKey, Center, Scale);

        public override string ToString()
            => $"{(IsOpen ? "열림" : "닫힘")} {PictureKey} @({Center.X:0.##}, {Center.Y:0.##}) ×{Scale:0.###}";
    }
}
