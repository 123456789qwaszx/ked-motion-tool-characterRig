namespace Ked.Presentation.Core
{
    /// <summary>
    /// 덮는 무대 하나의 컷인 끝 상태 — docs/PLAN_cutin.md §6.
    /// 창의 모션(팝 인 · 팝 아웃) 도중은 접지 않는다. 다른 커맨드처럼 끝값만이다:
    /// 열림 = 창 항등 + 판이 이 자리 · 배율로 보인다, 닫힘 = 창 항등 + 마스크 숨김(무대 내용도 안 보인다).
    /// 판 모양 · 기본 크기는 게임 프리셋(런타임 CutInPresetDBSO)이라 여기 없다 — 툴 미리보기는 프리셋과 이 값으로 그린다.
    /// </summary>
    public readonly struct CutInState
    {
        public readonly bool IsOpen;

        /// <summary>판 그림 키 — Resources/CutInPanels/{키}.</summary>
        public readonly string PanelKey;

        /// <summary>판 중심. 무대 중심 기준 px, y 위.</summary>
        public readonly Vec2 Center;

        /// <summary>판 기본 크기에 곱하는 배율.</summary>
        public readonly float Scale;

        public CutInState(bool isOpen, string panelKey, Vec2 center, float scale)
        {
            IsOpen = isOpen;
            PanelKey = panelKey;
            Center = center;
            Scale = scale;
        }

        public CutInState Closed() => new(false, PanelKey, Center, Scale);

        public override string ToString()
            => $"{(IsOpen ? "열림" : "닫힘")} {PanelKey} @({Center.X:0.##}, {Center.Y:0.##}) ×{Scale:0.###}";
    }
}
