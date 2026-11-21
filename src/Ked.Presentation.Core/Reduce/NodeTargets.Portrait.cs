namespace Ked.Presentation.Core
{
    public static partial class NodeTargets
    {
        // ── 원본 픽셀 사이징 (CharRigImageSizingPolicy.SourcePixels) ──
        //
        // 초상과 배경은 **원본 픽셀 크기 그대로** 그린다 — 캔버스 기준 1px = 원본 1px, 피벗(가운데) 자리에.
        // 높이 맞춤은 2026-11-09에 걷었다. 배경이 먼저였고(화면보다 큰 배경이 화면 밖까지 걸치고 카메라가 그 안을 본다),
        // 초상이 뒤따랐다 — 원본 게임 실영상에서 인물은 그림 픽셀에 배율을 곱한 크기로 선다
        // (캔버스 px / 그림 px = 0.815~0.962, ch05 ep1 실측). 그 배율과 자리는 역할 앵커가 쥔다
        // (visualScale · offset → CharacterPortrait_VisualOffset, NodeTargets.SetAnchor).
        //
        // 노드는 앵커와 상관없이 같은 식으로 쓴다:
        //   rect 크기 = 부모 크기 × (anchorMax − anchorMin) + sizeDelta
        //   → sizeDelta = 그림 크기 − 부모 크기 × (anchorMax − anchorMin)
        // 리그의 그림 노드는 늘림 앵커(0,0)–(1,1)이라 sizeDelta = 그림 크기 − 부모 크기다.
        // 런타임 CharRigImageSizingPolicy.ApplySourcePixels가 같은 식을 쓴다 — 식은 여기 하나다.
        //
        // 정렬(pivot · anchoredPosition)은 접지 않는다 — 이 방식은 건드리지 않으므로 리그 초기값 그대로다.

        public const string PortraitImageNodeId = "CharacterPortraitSprite_Image";
        public const string PortraitParentNodeId = "CharacterPortraitSprite_Root";

        public static NodeTarget SourcePixelsSizing(
            string imageNodeKey, Vec2 parentSize, Vec2 anchorMin, Vec2 anchorMax, Vec2 spriteSize)
            => NodeTarget.SizeDelta(imageNodeKey, new Vec2(
                spriteSize.X - parentSize.X * (anchorMax.X - anchorMin.X),
                spriteSize.Y - parentSize.Y * (anchorMax.Y - anchorMin.Y)));

        /// <summary>
        /// 무대 상태에서 초상 사이징 타깃 하나를 뽑는다 —
        /// 부모 크기와 앵커는 리그 트리에서, 그림 크기는 초상 치수 덤프에서 온다.
        /// </summary>
        public static bool TryPortraitSizing(
            StageState state,
            string slotKey,
            string emotionKey,
            PortraitDimensionsFileDto dimensions,
            out NodeTarget target,
            out string reason)
        {
            target = default;

            if (dimensions == null)
            {
                reason = "초상 치수 덤프가 tuning에 없다 (portrait-dimensions.json)";
                return false;
            }

            if (!state.TryGetCharacter(slotKey, out string characterKey))
            {
                reason = $"슬롯 '{slotKey}'에 배역이 없다 — 초상 크기를 정할 수 없다 (cast 선행 필요)";
                return false;
            }

            if (!dimensions.TryGetSize(characterKey, state.GetVariant(slotKey), emotionKey,
                    out Vec2 spriteSize, out reason))
            {
                return false;
            }

            string imageKey = StageState.NodeKeyOf(slotKey, PortraitImageNodeId);
            Vec2 parentSize = state.Nodes
                .GetRectSize(StageState.NodeKeyOf(slotKey, PortraitParentNodeId));
            RectNodeState image = state.Nodes.GetState(imageKey);

            target = SourcePixelsSizing(
                imageKey, parentSize, image.AnchorMin, image.AnchorMax, spriteSize);

            reason = null;
            return true;
        }
    }
}