using System;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 줄 태그 #auto · #auto=길이 — 자동으로 넘어가는 줄과 그 머무름(docs/PLAN_line-beat.md R9).
    ///   auto        다 보이면 입력 없이 넘긴다
    ///   auto=1.8s   줄이 들어온 때부터 1.8초가 지나고 글도 다 보이면 넘긴다. 길이 표기는 pause와 같다(DurationToken — 1.8s · 54fr)
    /// 글 카드(장소 카드 · 장제목)가 머무는 시간이다. 전에는 줄 머리의 마지막 pause가 흉내 냈다 — 대사 줄 머리에는 이제 시간이 없다.
    ///
    /// 입력은 정규화된 태그다(재생의 DialogueBoxTagNormalizer — 소문자, '#' · '_' · '-' · 공백을 빼고 ':'를 '='로).
    /// 실패를 로그로 흘리지 않는다 — 읽지 못한 길이는 머무름 없는 auto로 보고 error를 채운다. 무엇을 할지는 호출자가 정한다.
    /// </summary>
    public static class AutoAdvanceTag
    {
        public const string Name = "auto";

        /// <summary>auto 태그면 true. 머무름은 holdSeconds(없으면 0). 길이를 읽지 못하면 true · 0 · error.</summary>
        public static bool TryRead(string normalizedTag, out float holdSeconds, out string error)
        {
            holdSeconds = 0f;
            error = null;

            if (string.IsNullOrEmpty(normalizedTag))
                return false;

            if (normalizedTag == Name)
                return true;

            if (!normalizedTag.StartsWith(Name + "=", StringComparison.Ordinal))
                return false;

            string value = normalizedTag.Substring(Name.Length + 1);

            if (DurationToken.TryParseSeconds(value, out float seconds))
            {
                holdSeconds = seconds;
                return true;
            }

            error = $"#auto의 머무름 '{value}'를 읽지 못했다 — 머무름 없이 넘긴다(표기: 1.8s · 54fr)";
            return true;
        }
    }
}
