using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // 이모지의 클립 — 이모지 하나의 움직임 한 덩어리(docs/emoji-format.md).
    //
    // 툴에서는 타임라인 하나로 잡고, 내보낼 때 구간마다 **따로 선 클립**으로 나간다:
    // 0~12fr 구간은 0에서 시작하는 13프레임짜리 클립이다(타임라인을 가리키는 창이 아니다).
    //
    // 시각은 프레임이다. 클립의 길이 Length는 프레임 수 — 프레임 0 … Length-1이 있고,
    // 되풀이의 주기가 Length다(마지막 프레임도 한 프레임만큼 보인다).
    //
    // 트랙 하나 = 대상 하나의 채널 하나. 대상은 리그의 이름이고, 빈 이름은 이모지 전체(Emoji_Offset)다.
    // 키가 없는 채널은 중립값이다(자리 0 · 배율 1 · 회전 0 · 불투명도 1 · 그림은 리그의 기본 스프라이트).
    // ─────────────────────────────────────────────────────────────────

    public enum EmojiChannel
    {
        /// <summary>자리 x(px). 리그는 기본 자리(EmojiRig_Place) 기준, 이모지 전체는 오프셋 값에 더한다.</summary>
        PositionX,

        /// <summary>자리 y(px, 위가 +).</summary>
        PositionY,

        ScaleX,
        ScaleY,

        /// <summary>회전(도, z).</summary>
        Rotation,

        /// <summary>불투명도 0~1.</summary>
        Alpha,

        /// <summary>그림 바꾸기 — 계단 키. 그 프레임부터 스프라이트가 바뀐다. 리그에만 쓴다.</summary>
        Sprite,
    }

    /// <summary>그림 바꾸기의 키 — 그 프레임부터 이 스프라이트다.</summary>
    public readonly struct EmojiSpriteKey
    {
        public readonly float Frame;

        /// <summary>스프라이트 키 — Resources/Emojis/{키}.</summary>
        public readonly string SpriteKey;

        public EmojiSpriteKey(float frame, string spriteKey)
        {
            Frame = frame;
            SpriteKey = spriteKey;
        }
    }

    public sealed class EmojiTrack
    {
        /// <summary>대상 리그의 이름. 비어 있으면 이모지 전체(Emoji_Offset).</summary>
        public string Rig { get; }

        public EmojiChannel Channel { get; }

        /// <summary>수치 채널의 키 — Time은 프레임, 탄젠트는 프레임당. 계단 키는 OutTangent가 +∞다.</summary>
        public CurveKey[] Keys { get; }

        /// <summary>그림 바꾸기 채널의 키(Channel == Sprite일 때만).</summary>
        public EmojiSpriteKey[] SpriteKeys { get; }

        public bool TargetsWholeEmoji => string.IsNullOrEmpty(Rig);

        public EmojiTrack(string rig, EmojiChannel channel, CurveKey[] keys, EmojiSpriteKey[] spriteKeys = null)
        {
            Rig = rig ?? "";
            Channel = channel;
            Keys = keys ?? Array.Empty<CurveKey>();
            SpriteKeys = spriteKeys ?? Array.Empty<EmojiSpriteKey>();
        }
    }

    public sealed class EmojiClip
    {
        public string Name { get; }

        /// <summary>프레임 수. 프레임 0 … Length-1.</summary>
        public int Length { get; }

        public IReadOnlyList<EmojiTrack> Tracks { get; }

        public EmojiClip(string name, int length, IReadOnlyList<EmojiTrack> tracks)
        {
            Name = name;
            Length = length;
            Tracks = tracks ?? Array.Empty<EmojiTrack>();
        }

        /// <summary>대상(리그 이름, 빈 이름 = 이모지 전체)의 채널 하나를 찾는다. 없으면 null.</summary>
        public EmojiTrack Find(string rig, EmojiChannel channel)
        {
            rig ??= "";

            for (int i = 0; i < Tracks.Count; i++)
            {
                EmojiTrack track = Tracks[i];

                if (track.Channel == channel && string.Equals(track.Rig, rig, StringComparison.Ordinal))
                    return track;
            }

            return null;
        }
    }

    /// <summary>대상 하나의 그 프레임의 모습. 툴의 미리보기와 런타임의 재생이 같은 값을 쓴다.</summary>
    public readonly struct EmojiNodePose
    {
        public readonly Vec2 Position;
        public readonly Vec2 Scale;
        public readonly float Rotation;
        public readonly float Alpha;

        /// <summary>그림 바꾸기 채널이 정한 스프라이트. 채널이 없거나 첫 키 앞이면 null — 리그의 기본 스프라이트다.</summary>
        public readonly string SpriteKey;

        public EmojiNodePose(Vec2 position, Vec2 scale, float rotation, float alpha, string spriteKey)
        {
            Position = position;
            Scale = scale;
            Rotation = rotation;
            Alpha = alpha;
            SpriteKey = spriteKey;
        }

        /// <summary>중립 — 키가 하나도 없는 대상의 모습.</summary>
        public static readonly EmojiNodePose Neutral = new(Vec2.Zero, new Vec2(1f, 1f), 0f, 1f, null);
    }

    public static class EmojiClipMath
    {
        public static float Neutral(EmojiChannel channel)
        {
            switch (channel)
            {
                case EmojiChannel.ScaleX:
                case EmojiChannel.ScaleY:
                case EmojiChannel.Alpha:
                    return 1f;

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 수치 채널의 그 프레임 값. 트랙이 없거나 키가 없으면 중립값이다.
        /// 프레임은 클립 안으로 조인다(0 … Length-1) — 첫 키 앞과 마지막 키 뒤는 끝값이다(CurveFunctions).
        /// </summary>
        public static float Sample(EmojiClip clip, string rig, EmojiChannel channel, float frame)
        {
            EmojiTrack track = clip?.Find(rig, channel);

            if (track == null || track.Keys.Length == 0)
                return Neutral(channel);

            return CurveFunctions.Evaluate(track.Keys, ClampFrame(clip, frame));
        }

        /// <summary>그림 바꾸기 채널의 그 프레임 스프라이트 — 프레임이 지난 마지막 키. 없거나 첫 키 앞이면 null.</summary>
        public static string SampleSprite(EmojiClip clip, string rig, float frame)
        {
            EmojiTrack track = clip?.Find(rig, EmojiChannel.Sprite);

            if (track == null || track.SpriteKeys.Length == 0)
                return null;

            float f = ClampFrame(clip, frame);
            string current = null;

            for (int i = 0; i < track.SpriteKeys.Length; i++)
            {
                if (track.SpriteKeys[i].Frame > f)
                    break;

                current = track.SpriteKeys[i].SpriteKey;
            }

            return current;
        }

        /// <summary>대상 하나(리그 이름, 빈 이름 = 이모지 전체)의 그 프레임의 모습. clip이 null이면 중립이다.</summary>
        public static EmojiNodePose SampleNode(EmojiClip clip, string rig, float frame)
        {
            if (clip == null)
                return EmojiNodePose.Neutral;

            return new EmojiNodePose(
                new Vec2(
                    Sample(clip, rig, EmojiChannel.PositionX, frame),
                    Sample(clip, rig, EmojiChannel.PositionY, frame)),
                new Vec2(
                    Sample(clip, rig, EmojiChannel.ScaleX, frame),
                    Sample(clip, rig, EmojiChannel.ScaleY, frame)),
                Sample(clip, rig, EmojiChannel.Rotation, frame),
                Sample(clip, rig, EmojiChannel.Alpha, frame),
                SampleSprite(clip, rig, frame));
        }

        private static float ClampFrame(EmojiClip clip, float frame)
        {
            float last = clip.Length - 1;

            if (frame < 0f)
                return 0f;

            return frame > last ? last : frame;
        }
    }
}
