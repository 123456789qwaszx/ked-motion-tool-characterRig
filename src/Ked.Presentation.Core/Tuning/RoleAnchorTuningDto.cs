using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // ExportedTuning/presets/role-anchor.json의 전송 타입.
    // EditorJsonUtility 직렬화 모양 그대로다: {"MonoBehaviour": { "entries": [...] }}.
    // m_* 메타 필드는 담지 않는다(역직렬화가 무시한다).
    //
    // 이 파일을 tuning에 배선하는 것은 재작성이 원본과 다르게 간 지점이다.
    // 원본은 "엔트리가 전부 기본값(0/1)"이라 가정하고 Default로 접었는데,
    // 실측 결과 11건 중 2건이 기본값이 아니다(tyrant: (-30,-800)/5.0 · Amber: (330,0)/10.0).
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class RoleAnchorTuningFileDto
    {
        public RoleAnchorTuningBodyDto MonoBehaviour;
    }

    [Serializable]
    public sealed class RoleAnchorTuningBodyDto
    {
        public List<RoleAnchorEntryDto> entries = new();

        /// <summary>
        /// 캐릭터 키 → 앵커 튜닝. 런타임 RoleAnchorTuningDBSO.TryGet과 같은 규약:
        /// Trim 후 **대소문자를 가리지 않는** 일치(OrdinalIgnoreCase), 같은 키가 둘이면 **나중 것이 이긴다**
        /// (런타임 Build가 `_map[key] = entry`로 덮는다). 없으면 false — 호출자가 Default를 쓴다
        /// (엔트리 부재는 오류가 아니라 "기본값 캐릭터"라는 데이터 의미다).
        /// </summary>
        public bool TryGet(string characterKey, out NodeTargets.RoleAnchorTuning tuning)
        {
            tuning = NodeTargets.RoleAnchorTuning.Default;

            if (string.IsNullOrWhiteSpace(characterKey))
                return false;

            string key = characterKey.Trim();
            bool found = false;

            for (int i = 0; i < entries.Count; i++)
            {
                RoleAnchorEntryDto entry = entries[i];

                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                    continue;

                if (!string.Equals(entry.key.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                tuning = new NodeTargets.RoleAnchorTuning(
                    entry.offset?.ToVec2() ?? Vec2.Zero,
                    entry.visualScale);

                found = true;
            }

            return found;
        }
    }

    [Serializable]
    public sealed class RoleAnchorEntryDto
    {
        public string key;
        public Float2Dto offset;
        public float visualScale;
    }
}