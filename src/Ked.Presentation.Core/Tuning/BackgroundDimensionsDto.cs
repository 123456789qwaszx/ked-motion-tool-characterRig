using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// background-dimensions.json — 배경 스프라이트 키 → 픽셀 치수.
    ///
    /// 배경은 캐스팅하지 않은 캐릭터 슬롯이다(StageReducer.Background.cs). 초상과 같은 사이징 —
    /// **원본 픽셀 크기 그대로** 화면 가운데(2026-11-09) — 을 타고, 다른 것은 크기의 출처 하나다.
    /// 화면보다 큰 배경이 화면 밖까지 걸치고, 카메라는 그 안을 본다.
    /// 초상은 (캐릭터·변형·표정)으로 초상 DB를 찾고, 배경은 `bg_sprite`의 키 하나로 `Resources/Backgrounds/{키}`를 연다.
    ///
    /// 조회 규약은 런타임 해석기(ResourcesSpriteResolver → Resources.Load)를 그대로 따른다:
    ///   · 키는 Backgrounds/ 아래 상대 경로(확장자 없음). 트림하지 않는다.
    ///   · **대소문자를 가리지 않는다** — Resources.Load가 그렇다. 가리면 런타임은 그림을 찾아 크기를 맞추는데
    ///     폴드는 Unhandled로 남겨 sizeDelta가 갈린다.
    ///   · 중복 키(대소문자만 다른 것 포함)는 먼저 들어온 것이 이긴다.
    ///   · 찾지 못하면 false — 짐작으로 잇지 않는다(그 축만 Unhandled로 소리를 낸다).
    /// </summary>
    [Serializable]
    public sealed class BackgroundDimensionsFileDto
    {
        /// <summary>익스포터가 읽은 Resources 하위 폴더. 추적용이다.</summary>
        public string sourceFolder;

        public List<BackgroundDimensionDto> entries;

        [NonSerialized] private Dictionary<string, BackgroundDimensionDto> _index;

        /// <summary>픽셀 크기(가로, 세로). 배경 사이징이 쓰는 값이다 — 캔버스 기준 1px = 원본 1px.</summary>
        public bool TryGetSize(string spriteKey, out Vec2 size, out string reason)
        {
            size = default;

            if (string.IsNullOrEmpty(spriteKey))
            {
                reason = "배경 스프라이트 키가 없다";
                return false;
            }

            EnsureIndex();

            if (!_index.TryGetValue(spriteKey, out BackgroundDimensionDto entry))
            {
                reason = $"배경 치수가 없다: 키='{spriteKey}' (Resources/Backgrounds에 없거나 덤프가 낡았다)";
                return false;
            }

            if (!(entry.width > 0f) || !(entry.height > 0f))
            {
                reason = $"배경 치수가 유효하지 않다: {entry}";
                return false;
            }

            size = new Vec2(entry.width, entry.height);
            reason = null;
            return true;
        }

        private void EnsureIndex()
        {
            if (_index != null)
                return;

            _index = new Dictionary<string, BackgroundDimensionDto>(StringComparer.OrdinalIgnoreCase);

            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                BackgroundDimensionDto entry = entries[i];

                if (entry == null || string.IsNullOrEmpty(entry.key))
                    continue;

                if (!_index.ContainsKey(entry.key))
                    _index.Add(entry.key, entry);
            }
        }
    }

    [Serializable]
    public sealed class BackgroundDimensionDto
    {
        /// <summary>`bg_sprite`가 쓰는 키 = Resources/Backgrounds 아래 상대 경로(확장자 없음).</summary>
        public string key;

        // 스프라이트 rect의 픽셀 크기 = 화면에 그려지는 크기(캔버스 기준 px).
        // 임포터 상한에 걸려 줄어든 그림은 익스포터가 경고한다(원본과 rect가 다르면 화면 크기가 틀린다).
        public float width;
        public float height;

        public override string ToString() => $"{key} = {width}x{height}";
    }
}
