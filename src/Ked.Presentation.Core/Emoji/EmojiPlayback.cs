namespace Ked.Presentation.Core
{
    public enum EmojiPhase
    {
        /// <summary>머묾 없는 이모지 — 한 번 재생하는 중.</summary>
        Main,

        /// <summary>등장 — 한 번.</summary>
        In,

        /// <summary>머묾 — 되풀이. 클립이 없는 이모지도 여기 머문다(정의의 모습 그대로).</summary>
        Hold,

        /// <summary>퇴장 — 한 번.</summary>
        Out,

        /// <summary>끝났다 — 사라질 차례다.</summary>
        Done,
    }

    /// <summary>
    /// 이모지 하나의 재생 — 지금 어느 클립의 몇 프레임인가. 순수한 상태 기계다:
    /// 시간을 넘기는 것(Advance)과 내리는 것(RequestOff)만 받는다. 런타임의 재생과 툴의 미리보기가 같이 쓴다.
    ///
    ///   머묾이 없다:  Main ─(끝)→ Done
    ///   머묾이 있다:  [In ─(끝)→] Hold ⟲ ─(내린다)→ [Out ─(끝)→] Done
    ///
    /// 구간은 따로 선 클립이다 — 등장이 끝나면 머묾의 프레임 0에서 잇는다(넘친 시간만큼 앞서서).
    /// 등장 도중에 내리면 바로 퇴장으로 넘어간다.
    /// </summary>
    public sealed class EmojiPlayback
    {
        private readonly EmojiDefinition _definition;

        public EmojiPhase Phase { get; private set; }

        /// <summary>지금 클립 안의 프레임(소수 — 화면의 프레임이 클립의 프레임보다 잦다).</summary>
        public float Frame { get; private set; }

        public bool IsDone => Phase == EmojiPhase.Done;

        /// <summary>지금 재생하는 클립. 클립 없는 이모지와 끝난 뒤에는 null이다.</summary>
        public EmojiClip Clip
        {
            get
            {
                switch (Phase)
                {
                    case EmojiPhase.Main: return _definition.Main;
                    case EmojiPhase.In: return _definition.In;
                    case EmojiPhase.Hold: return _definition.Hold;
                    case EmojiPhase.Out: return _definition.Out;
                    default: return null;
                }
            }
        }

        public EmojiPlayback(EmojiDefinition definition)
        {
            _definition = definition ?? throw new System.ArgumentNullException(nameof(definition));

            if (!definition.Stays)
                Phase = EmojiPhase.Main;
            else
                Phase = definition.In != null ? EmojiPhase.In : EmojiPhase.Hold;

            Frame = 0f;
        }

        /// <summary>
        /// 건너뛴 뒤의 모습으로 간다 — 머무는 이모지는 머묾의 처음, 머묾 없는 이모지는 끝(사라진다).
        /// </summary>
        public void Settle()
        {
            Phase = _definition.Stays ? EmojiPhase.Hold : EmojiPhase.Done;
            Frame = 0f;
        }

        /// <summary>내린다 — 퇴장이 있으면 그것을 처음부터, 없으면 바로 끝난다. 이미 퇴장 중이면 그대로 둔다.</summary>
        public void RequestOff()
        {
            if (Phase == EmojiPhase.Done || Phase == EmojiPhase.Out)
                return;

            if (_definition.Stays && _definition.Out != null)
            {
                Phase = EmojiPhase.Out;
                Frame = 0f;
                return;
            }

            Phase = EmojiPhase.Done;
            Frame = 0f;
        }

        /// <summary>시간을 넘긴다(초). 클립의 끝을 넘으면 다음 단계로 간다 — 넘친 시간은 다음 클립에서 이어 쓴다.</summary>
        public void Advance(float seconds)
        {
            if (seconds <= 0f || IsDone)
                return;

            float frames = seconds * _definition.Fps;

            // 한 번에 여러 클립을 넘을 수 있다(긴 프레임 · 짧은 클립). 되풀이는 나머지로 접는다.
            for (int guard = 0; guard < 8 && !IsDone; guard++)
            {
                EmojiClip clip = Clip;

                // 클립 없는 머묾 — 정의의 모습 그대로 서 있다. 넘길 것이 없다.
                if (clip == null)
                    return;

                float length = clip.Length;
                float next = Frame + frames;

                if (next < length)
                {
                    Frame = next;
                    return;
                }

                float overflow = next - length;

                switch (Phase)
                {
                    case EmojiPhase.Hold:
                        Frame = overflow % length;
                        return;

                    case EmojiPhase.In:
                        Phase = EmojiPhase.Hold;
                        Frame = 0f;
                        frames = overflow;
                        break;

                    default: // Main · Out
                        Phase = EmojiPhase.Done;
                        Frame = 0f;
                        return;
                }
            }
        }
    }
}
