using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// portrait-dimensions.json — (캐릭터 · 변형 · 표정) → 초상 스프라이트 픽셀 치수.
    ///
    /// 초상은 **원본 픽셀 크기 그대로** 그린다(2026-11-09, NodeTargets.SourcePixelsSizing) — 그래서 쓰는 값은 픽셀 크기다.
    ///
    /// 조회 규약은 런타임 PortraitResolver를 그대로 따른다. 그게 요점이다 —
    /// 폴드가 다른 스프라이트를 고르면 크기가 달라지고, 그건 곧 불일치다.
    ///   · 키 정규화: 캐릭터 소문자 · 변형은 **문자열 전체**(`school` ≠ `casual`) · 표정 2자리
    ///   · 중복 키는 **먼저 들어온 것이 이긴다** (PortraitResolver의 TryAdd와 같다)
    ///   · 정확 일치 실패 시 (캐릭터, "a", "01")로 한 번 물러선다
    /// </summary>
    [Serializable]
    public sealed class PortraitDimensionsFileDto
    {
        /// <summary>PortraitResolver.DefaultVariant. 변형 인자가 없을 때 서는 자리.</summary>
        public const string DefaultVariantKey = PortraitKeyNormalizer.DefaultVariantKey;

        public string sourceAsset;
        public List<PortraitDimensionDto> entries;

        [NonSerialized] private Dictionary<(string, string, string), PortraitDimensionDto> _index;

        /// <summary>
        /// 픽셀 크기(가로, 세로). 초상 사이징이 쓰는 값이다 — 캔버스 기준 1px = 원본 1px.
        /// 찾지 못하면 false — 짐작으로 잇지 않는다(Unhandled로 소리를 낸다).
        /// </summary>
        public bool TryGetSize(
            string characterKey, string variantKey, string emotionKey,
            out Vec2 size, out string reason)
        {
            size = default;

            // 비어 있는 인자만 기본값으로 채운다 — PortraitResolver.Resolve와 같은 자리다.
            // 값이 있는데 규격 밖인 표정("abc")은 여기서 손대지 않는다:
            // 정규화가 빈 코드를 내고, 그 키로 조회했다가 폴백으로 물러서는 것이 런타임 동작이다.
            if (string.IsNullOrEmpty(variantKey))
                variantKey = DefaultVariantKey;

            if (string.IsNullOrEmpty(emotionKey))
                emotionKey = PortraitKeyNormalizer.DefaultEmotionCode;

            string variant = PortraitKeyNormalizer.VariantKey(variantKey);
            string emotionCode = PortraitKeyNormalizer.EmotionCode(emotionKey);
            string character = PortraitKeyNormalizer.CharacterKey(characterKey);

            if (string.IsNullOrEmpty(character))
            {
                reason = "초상 치수를 찾을 캐릭터 키가 없다 (cast 선행 필요)";
                return false;
            }

            EnsureIndex();

            if (!_index.TryGetValue((character, variant, emotionCode), out PortraitDimensionDto entry) &&
                !_index.TryGetValue((character, PortraitKeyNormalizer.DefaultVariantKey,
                    PortraitKeyNormalizer.DefaultEmotionCode), out entry))
            {
                reason =
                    $"초상 치수가 없다: 캐릭터='{character}', 변형='{variant}', 표정='{emotionCode}' " +
                    $"(폴백 '{PortraitKeyNormalizer.DefaultVariantKey}'/" +
                    $"'{PortraitKeyNormalizer.DefaultEmotionCode}'도 없다)";

                return false;
            }

            if (!(entry.width > 0f) || !(entry.height > 0f))
            {
                reason = $"초상 치수가 유효하지 않다: {entry}";
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

            _index = new Dictionary<(string, string, string), PortraitDimensionDto>();

            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                PortraitDimensionDto entry = entries[i];

                if (entry == null)
                    continue;

                var key = (
                    PortraitKeyNormalizer.CharacterKey(entry.character),
                    PortraitKeyNormalizer.VariantKey(entry.variant),
                    PortraitKeyNormalizer.EmotionCode(entry.emotion));

                // 먼저 들어온 것이 이긴다 — PortraitResolver가 중복 키를 버리는 방향과 같다.
                if (!_index.ContainsKey(key))
                    _index.Add(key, entry);
            }
        }
    }

    [Serializable]
    public sealed class PortraitDimensionDto
    {
        public string character;
        public string variant;
        public string emotion;

        // 스프라이트 rect의 픽셀 크기 = 그림 노드의 크기(캔버스 기준 px, 역할 앵커 배율 전).
        // 임포터 상한에 걸려 줄어든 그림은 익스포터가 경고한다(원본과 rect가 다르면 화면 크기가 틀린다).
        public float width;
        public float height;

        public override string ToString() => $"{character}|{variant}|{emotion} = {width}x{height}";
    }

    /// <summary>
    /// 초상 키 정규화 — 런타임 PresentationKeyNormalizer · PortraitResolver의 규칙을 옮긴 것.
    /// 두 곳이 갈라지면 폴드가 다른 스프라이트를 고르므로, 바꿀 때는 반드시 함께 바꾼다.
    /// </summary>
    public static class PortraitKeyNormalizer
    {
        public const string DefaultVariantKey = "a";
        public const string DefaultEmotionCode = "01";

        public static string CharacterKey(string key) => (key ?? "").Trim().ToLowerInvariant();

        /// <summary>
        /// 변형 키는 **문자열 전체**가 키다 (`a` → "a", `school` → "school").
        /// 초상 에셋의 폴더 규약(&lt;캐릭터&gt;/&lt;변형&gt;/&lt;표정&gt;.png)에서 변형은 폴더 이름 그 자체다.
        /// 마지막 글자만 보면 `school`과 `casual`이 조용히 같은 키가 된다.
        /// </summary>
        public static string VariantKey(string variantKey)
            => (variantKey ?? "").Trim().ToLowerInvariant();

        /// <summary>
        /// 표정 코드를 두 자리로 (`2` → `02`, `02` → `02`).
        /// 숫자가 아니면 빈 문자열 — 런타임도 그렇게 두고 그 키로 조회한다(조용히 기본값으로 잇지 않는다).
        /// </summary>
        public static string EmotionCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = input.Trim();

            if (input.Length == 2 && IsAsciiDigit(input[0]) && IsAsciiDigit(input[1]))
                return input;

            if (input.Length == 1 && IsAsciiDigit(input[0]))
                return "0" + input;

            return "";
        }

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
