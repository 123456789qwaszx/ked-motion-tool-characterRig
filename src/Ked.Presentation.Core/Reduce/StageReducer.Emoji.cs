namespace Ked.Presentation.Core
{
    /// <summary>
    /// emoji · emoji_off — 슬롯에 딸린 이모지가 서 있는가(docs/PLAN_emoji.md §4).
    ///
    /// 런타임(SetEmojiCommand · ClearEmojiCommand)과 같은 규칙으로 접는다:
    ///   · 대상은 슬롯으로 풀려야 한다(slot/cast/actor 선행).
    ///   · 정의(emojis.json)에 없는 이모지는 런타임이 경고하고 건너뛴다 — 여기서는 Unhandled.
    ///   · **머무는 이모지(EmojiDefinition.Stays)만 남는다.** 머묾 없는 이모지는 한 번 재생하고 사라진다 —
    ///     건너뛰면 런타임도 세우지 않는다. 접을 것이 없으므로 Unhandled도 아니다.
    ///   · 서 있는 이모지를 다시 부르면 부수고 새로 세운다 — 접으면 "서 있다" 그대로다(맨 뒤로 간다).
    ///   · 퇴장의 움직임은 접지 않는다 — emoji_off는 내려간 것으로 접는다(다른 커맨드처럼 끝값이다).
    ///   · emoji_off에 이름이 없으면 그 슬롯의 이모지를 전부 내린다(없어도 된다).
    ///     이름이 있는데 서 있지 않으면 런타임은 경고하고 건너뛴다 — 여기서는 Unhandled.
    ///   · 슬롯을 다시 선언하면 함께 사라진다(StageReducer.Slot.SpawnSlot).
    ///   · 인물의 fade와는 묶지 않는다 — 내리는 것은 emoji_off와 슬롯의 재선언뿐이다.
    ///   · 쪽(셋째 인자 — 생략 · flip)은 접지 않는다 — 서 있는가만 끝 상태다. 되감기 · 이어하기는 커맨드를 다시 실행해 세우므로
    ///     쪽이 따라온다(docs/PLAN_emoji-side.md Q3). 모르는 낱말은 런타임이 세우지 않는 줄이라 Unhandled.
    ///
    /// 이모지의 노드는 리그 노드 트리에 넣지 않는다 — 머무는 이모지는 되풀이하는 중이라 "끝값"이 없다.
    /// 서 있는가만이 끝 상태다.
    /// </summary>
    public static partial class StageReducer
    {
        private static bool ApplyEmoji(
            StageState state, in StageCommand cmd, StageReducerTuning tuning, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string emojiName = (cmd.Arg(1) ?? "").Trim();

            if (emojiName.Length == 0)
            {
                reason = "이모지 이름 인자가 없다";
                return false;
            }

            if (tuning.Emojis == null || !tuning.Emojis.TryGetValue(emojiName, out EmojiDefinition definition))
            {
                reason = $"이모지 '{emojiName}'이 정의(emojis.json)에 없다";
                return false;
            }

            if (!EmojiSideToken.TryParse(cmd.Arg(2), out _))
            {
                reason = $"이모지의 쪽은 생략하거나 '{EmojiSideToken.Flip}'이어야 한다 ('{cmd.Arg(2)}')";
                return false;
            }

            // 끝 상태만 접는다: 머묾 없는 이모지는 한 번 재생하고 사라지므로 남는 것이 없다.
            // 다만 같은 이름이 서 있었다면 런타임이 부수고 새로 세우므로, 그것은 내려간 것이다.
            if (definition.Stays)
                state.AddEmoji(slotKey, emojiName);
            else
                state.RemoveEmoji(slotKey, emojiName);

            reason = null;
            return true;
        }

        private static bool ApplyEmojiOff(StageState state, in StageCommand cmd, out string reason)
        {
            if (!TryGetSpawnedSlot(state, cmd, out string slotKey, out reason))
                return false;

            string emojiName = (cmd.Arg(1) ?? "").Trim();

            if (emojiName.Length == 0)
            {
                state.ClearEmojis(slotKey);
                reason = null;
                return true;
            }

            if (!state.RemoveEmoji(slotKey, emojiName))
            {
                reason = $"슬롯 '{slotKey}'에 이모지 '{emojiName}'이 서 있지 않다";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
