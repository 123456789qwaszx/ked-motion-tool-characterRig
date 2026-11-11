using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 이모지 하나 — 툴이 정하는 것 전부(docs/emoji-rig.md §5 · docs/emoji-format.md).
    //
    //   구성: 자기 오프셋 + 리그 여러 개(이름 · 스프라이트 · 기본 자리 · 기본 배율 · 기본 회전)
    //   움직임: 클립들 + 어느 클립을 언제 재생하는가(Main / In · Hold · Out)
    //
    // 리그의 틀(노드와 채널)은 런타임의 EmojiRigSchema가 정하고, 여기에는 틀이 없다.
    // 재생(런타임 EmojiLibrary)과 폴드(StageReducer.Emoji)와 툴이 같은 정의를 본다.
    //
    // **머묾(Hold)이 있는가가 이모지의 종류를 정한다**:
    //   · 머묾이 없다 → Main을 한 번 재생하고 사라진다. 건너뛰면 남는 것이 없다.
    //   · 머묾이 있다 → (In을 한 번 →) Hold를 되풀이하며 남는다. 내리면 (Out을 재생하고) 사라진다.
    //     Hold가 한 프레임짜리면 그 모습으로 멈춰 있다.
    //   · 클립이 하나도 없다 → 정의의 모습 그대로 서 있다(머무는 이모지다).
    // ─────────────────────────────────────────────────────────────────

    /// <summary>이모지 안의 리그 하나.</summary>
    public sealed class EmojiRigEntry
    {
        /// <summary>이모지 안에서 리그를 가리키는 이름. 툴이 붙인다 — 키가 이 이름으로 리그를 찾는다.</summary>
        public string Name { get; }

        /// <summary>기본 스프라이트 키 — Resources/Emojis/{키}. 그림 바꾸기 채널이 없으면 늘 이것이다.</summary>
        public string SpriteKey { get; }

        /// <summary>기본 자리(EmojiRig_Place). 오프셋 기준 px, y 위.</summary>
        public Vec2 Place { get; }

        /// <summary>
        /// 기본 배율(EmojiRig_Place) — 그림을 이 이모지에서 얼마만 하게 쓰는가. 키의 배율은 이 위에서 1을 기준으로 잡는다
        /// (클립마다 크기를 다시 적지 않는다).
        /// </summary>
        public float Scale { get; }

        /// <summary>기본 회전(EmojiRig_Place, 도). 키의 회전은 이 위에 얹힌다.</summary>
        public float Rotation { get; }

        public EmojiRigEntry(string name, string spriteKey, Vec2 place, float scale = 1f, float rotation = 0f)
        {
            Name = name;
            SpriteKey = spriteKey;
            Place = place;
            Scale = scale;
            Rotation = rotation;
        }
    }

    /// <summary>이모지 하나.</summary>
    public sealed class EmojiDefinition
    {
        public const float DefaultFps = 24f;

        /// <summary>대본이 부르는 이름.</summary>
        public string Name { get; }

        /// <summary>이모지별 오프셋(Emoji_Offset). 로케이터(RootSpace) 기준 px, y 위.</summary>
        public Vec2 Offset { get; }

        /// <summary>리그들 — 순서가 그려지는 순서다(뒤가 위).</summary>
        public IReadOnlyList<EmojiRigEntry> Rigs { get; }

        /// <summary>초당 프레임 수 — 클립의 프레임을 시간으로 바꾼다.</summary>
        public float Fps { get; }

        public IReadOnlyList<EmojiClip> Clips { get; }

        /// <summary>머묾이 없을 때 한 번 재생하는 클립.</summary>
        public EmojiClip Main { get; }

        /// <summary>등장 — 머묾 앞에 한 번. 없어도 된다.</summary>
        public EmojiClip In { get; }

        /// <summary>머묾 — 되풀이한다. 이것이 있으면 머무는 이모지다.</summary>
        public EmojiClip Hold { get; }

        /// <summary>퇴장 — 내릴 때 한 번. 없으면 바로 사라진다.</summary>
        public EmojiClip Out { get; }

        /// <summary>머무는 이모지인가 — 건너뛴 뒤에도 서 있는가. 머묾이 있거나, 클립이 하나도 없다.</summary>
        public bool Stays => Hold != null || Clips.Count == 0;

        public EmojiDefinition(
            string name,
            Vec2 offset,
            IReadOnlyList<EmojiRigEntry> rigs,
            float fps = DefaultFps,
            IReadOnlyList<EmojiClip> clips = null,
            EmojiClip main = null,
            EmojiClip @in = null,
            EmojiClip hold = null,
            EmojiClip @out = null)
        {
            Name = name;
            Offset = offset;
            Rigs = rigs ?? Array.Empty<EmojiRigEntry>();
            Fps = fps > 0f ? fps : DefaultFps;
            Clips = clips ?? Array.Empty<EmojiClip>();
            Main = main;
            In = @in;
            Hold = hold;
            Out = @out;
        }
    }

    /// <summary>
    /// 이모지 정의의 모양 규칙 — 읽는 쪽(런타임 로더)과 내는 쪽(툴)이 같은 규칙을 쓴다.
    /// 어긋난 정의는 읽을 때 걸러진다("툴에서는 되는데 재생에서 사라지는" 이모지가 안 생기게).
    /// </summary>
    public static class EmojiRules
    {
        public static bool TryValidate(EmojiDefinition emoji, out string why)
        {
            if (emoji == null)
            {
                why = "정의가 비어 있다";
                return false;
            }

            if (string.IsNullOrWhiteSpace(emoji.Name))
            {
                why = "이모지에 이름이 없다";
                return false;
            }

            if (emoji.Rigs.Count == 0)
            {
                why = "리그가 없다 — 리그는 하나 이상이어야 한다";
                return false;
            }

            HashSet<string> rigNames = new(StringComparer.Ordinal);

            for (int i = 0; i < emoji.Rigs.Count; i++)
            {
                EmojiRigEntry rig = emoji.Rigs[i];

                if (rig == null || string.IsNullOrWhiteSpace(rig.Name))
                {
                    why = $"리그 [{i}]에 이름이 없다";
                    return false;
                }

                if (!rigNames.Add(rig.Name))
                {
                    why = $"리그 이름 '{rig.Name}'이 두 번 나온다 — 키가 리그를 이름으로 가리킨다";
                    return false;
                }

                if (float.IsNaN(rig.Scale) || float.IsInfinity(rig.Scale) || rig.Scale == 0f ||
                    float.IsNaN(rig.Rotation) || float.IsInfinity(rig.Rotation))
                {
                    why = $"리그 '{rig.Name}'의 기본 배율 · 회전이 쓸 수 없는 값이다 (배율 {rig.Scale}, 회전 {rig.Rotation})";
                    return false;
                }
            }

            HashSet<string> clipNames = new(StringComparer.Ordinal);

            for (int i = 0; i < emoji.Clips.Count; i++)
            {
                EmojiClip clip = emoji.Clips[i];

                if (clip == null || string.IsNullOrWhiteSpace(clip.Name))
                {
                    why = $"클립 [{i}]에 이름이 없다";
                    return false;
                }

                if (!clipNames.Add(clip.Name))
                {
                    why = $"클립 이름 '{clip.Name}'이 두 번 나온다";
                    return false;
                }

                if (!TryValidateClip(clip, rigNames, out why))
                    return false;
            }

            // 어느 클립을 언제 재생하는가.
            if (emoji.Hold == null && (emoji.In != null || emoji.Out != null))
            {
                why = "등장 · 퇴장은 머묾(hold)이 있을 때만 뜻이 있다";
                return false;
            }

            if (emoji.Hold != null && emoji.Main != null)
            {
                why = "main과 hold를 함께 둘 수 없다 — 머묾이 있으면 in · hold · out으로 재생한다";
                return false;
            }

            if (emoji.Clips.Count > 0 && emoji.Hold == null && emoji.Main == null)
            {
                why = "클립은 있는데 재생할 것(main 또는 hold)이 정해지지 않았다";
                return false;
            }

            why = null;
            return true;
        }

        private static bool TryValidateClip(EmojiClip clip, HashSet<string> rigNames, out string why)
        {
            if (clip.Length < 1)
            {
                why = $"클립 '{clip.Name}': 길이는 1프레임 이상이어야 한다 ({clip.Length})";
                return false;
            }

            float last = clip.Length - 1;
            HashSet<string> seen = new(StringComparer.Ordinal);

            for (int t = 0; t < clip.Tracks.Count; t++)
            {
                EmojiTrack track = clip.Tracks[t];

                if (track == null)
                {
                    why = $"클립 '{clip.Name}': 트랙 [{t}]가 비어 있다";
                    return false;
                }

                string target = track.TargetsWholeEmoji ? "(이모지 전체)" : track.Rig;

                if (!track.TargetsWholeEmoji && !rigNames.Contains(track.Rig))
                {
                    why = $"클립 '{clip.Name}': 트랙이 없는 리그 '{track.Rig}'를 가리킨다";
                    return false;
                }

                if (!seen.Add(track.Rig + "\n" + track.Channel))
                {
                    why = $"클립 '{clip.Name}': {target}의 {track.Channel} 트랙이 둘이다";
                    return false;
                }

                if (track.Channel == EmojiChannel.Sprite)
                {
                    if (track.TargetsWholeEmoji)
                    {
                        why = $"클립 '{clip.Name}': 그림 바꾸기는 리그에만 쓴다(이모지 전체에는 그림이 없다)";
                        return false;
                    }

                    if (track.SpriteKeys.Length == 0)
                    {
                        why = $"클립 '{clip.Name}': {target}의 그림 바꾸기 트랙에 키가 없다";
                        return false;
                    }

                    for (int k = 0; k < track.SpriteKeys.Length; k++)
                    {
                        EmojiSpriteKey key = track.SpriteKeys[k];

                        if (string.IsNullOrWhiteSpace(key.SpriteKey))
                        {
                            why = $"클립 '{clip.Name}': {target}의 그림 키 [{k}]에 스프라이트가 없다";
                            return false;
                        }

                        if (!IsFrameOk(key.Frame, last, k > 0 ? track.SpriteKeys[k - 1].Frame : float.NegativeInfinity))
                        {
                            why = $"클립 '{clip.Name}': {target}의 그림 키 [{k}]의 프레임({key.Frame})이 클립 밖이거나 순서가 어긋난다(0 … {last}, 오름차순)";
                            return false;
                        }
                    }

                    continue;
                }

                if (track.Keys.Length == 0)
                {
                    why = $"클립 '{clip.Name}': {target}의 {track.Channel} 트랙에 키가 없다";
                    return false;
                }

                for (int k = 0; k < track.Keys.Length; k++)
                {
                    CurveKey key = track.Keys[k];

                    if (float.IsNaN(key.Value) || float.IsInfinity(key.Value))
                    {
                        why = $"클립 '{clip.Name}': {target}의 {track.Channel} 키 [{k}]의 값이 수가 아니다";
                        return false;
                    }

                    if (!IsFrameOk(key.Time, last, k > 0 ? track.Keys[k - 1].Time : float.NegativeInfinity))
                    {
                        why = $"클립 '{clip.Name}': {target}의 {track.Channel} 키 [{k}]의 프레임({key.Time})이 클립 밖이거나 순서가 어긋난다(0 … {last}, 오름차순)";
                        return false;
                    }
                }
            }

            why = null;
            return true;
        }

        private static bool IsFrameOk(float frame, float last, float previous)
            => !float.IsNaN(frame) && frame >= 0f && frame <= last && frame > previous;
    }
}
