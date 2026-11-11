using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // emojis.json의 전송 타입 — schema "emojis/2". 형식과 뜻의 규범은 docs/emoji-format.md다.
    //
    //   {
    //     "schema": "emojis/2",
    //     "fps": 24,
    //     "emojis": [
    //       { "name": "star", "offset": { "x": 170, "y": 60 },
    //         "rigs":  [ { "name": "body", "sprite": "star", "place": { "x": 0, "y": 0 }, "scale": 0.45 } ],
    //         "clips": [ { "name": "hold", "length": 12,
    //                      "tracks": [ { "rig": "body", "channel": "scaleX",
    //                                    "keys": [ { "f": 0, "v": 1 }, { "f": 6, "v": 1.08 }, { "f": 11, "v": 1 } ] } ] } ],
    //         "hold": "hold" }
    //     ]
    //   }
    //
    // 필드 이름이 JSON 키와 1:1이다. 코어는 JSON 파서를 갖지 않는다 — 역직렬화는 호스트가 한다
    // (유니티: JsonUtility, 툴: System.Text.Json + IncludeFields). 해석은 EmojiDefinitionLoader 한 곳이다.
    // "emojis/1"(클립이 없는 최소형)도 그대로 읽힌다 — 클립 없는 이모지는 정의의 모습으로 서 있다.
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class EmojisFileDto
    {
        public string schema;

        /// <summary>초당 프레임 수. 0이거나 없으면 24.</summary>
        public float fps;

        public List<EmojiDto> emojis;
    }

    [Serializable]
    public sealed class EmojiDto
    {
        public string name;
        public Float2Dto offset;
        public List<EmojiRigDto> rigs;

        public List<EmojiClipDto> clips;

        /// <summary>머묾이 없을 때 한 번 재생할 클립의 이름.</summary>
        public string main;

        /// <summary>등장 · 머묾 · 퇴장에 이은 클립의 이름. 머묾(hold)이 있으면 머무는 이모지다.</summary>
        public string @in;
        public string hold;
        public string @out;
    }

    [Serializable]
    public sealed class EmojiRigDto
    {
        public string name;
        public string sprite;
        public Float2Dto place;

        /// <summary>기본 배율. 0이거나 없으면 1.</summary>
        public float scale;

        /// <summary>기본 회전(도). 없으면 0.</summary>
        public float rotation;
    }

    [Serializable]
    public sealed class EmojiClipDto
    {
        public string name;

        /// <summary>프레임 수 — 프레임 0 … length-1.</summary>
        public int length;

        public List<EmojiTrackDto> tracks;
    }

    [Serializable]
    public sealed class EmojiTrackDto
    {
        /// <summary>대상 리그의 이름. 비어 있으면 이모지 전체(Emoji_Offset).</summary>
        public string rig;

        /// <summary>x · y · scaleX · scaleY · rotation · alpha · sprite</summary>
        public string channel;

        public List<EmojiKeyDto> keys;
    }

    [Serializable]
    public sealed class EmojiKeyDto
    {
        /// <summary>프레임.</summary>
        public float f;

        /// <summary>값(수치 채널).</summary>
        public float v;

        /// <summary>들어오는 · 나가는 기울기(프레임당). 생략하면 0 — 키에서 평평하다.</summary>
        public float i;
        public float o;

        /// <summary>계단 키 — 다음 키까지 이 값으로 멈춰 있다.</summary>
        public bool step;

        /// <summary>스프라이트 키(sprite 채널). 그림 바꾸기는 언제나 계단이다.</summary>
        public string s;
    }

    /// <summary>DTO → 정의. 어긋난 이모지는 건너뛰고 까닭을 warn으로 넘긴다(이모지 이름, 까닭).</summary>
    public static class EmojiDefinitionLoader
    {
        public const string Schema = "emojis/2";

        public static Dictionary<string, EmojiDefinition> Build(EmojisFileDto file, Action<string, string> warn = null)
        {
            Dictionary<string, EmojiDefinition> result = new(StringComparer.Ordinal);

            if (file?.emojis == null)
                return result;

            float fps = file.fps > 0f ? file.fps : EmojiDefinition.DefaultFps;

            for (int i = 0; i < file.emojis.Count; i++)
            {
                EmojiDto dto = file.emojis[i];

                if (dto == null)
                {
                    warn?.Invoke($"[{i}]", "항목이 비어 있다");
                    continue;
                }

                string label = string.IsNullOrEmpty(dto.name) ? $"[{i}]" : dto.name;

                if (!TryConvert(dto, fps, out EmojiDefinition emoji, out string why) ||
                    !EmojiRules.TryValidate(emoji, out why))
                {
                    warn?.Invoke(label, why);
                    continue;
                }

                if (result.ContainsKey(emoji.Name))
                {
                    warn?.Invoke(label, "같은 이름이 둘이다 — 뒤의 것을 무시한다");
                    continue;
                }

                result[emoji.Name] = emoji;
            }

            return result;
        }

        private static bool TryConvert(EmojiDto dto, float fps, out EmojiDefinition emoji, out string why)
        {
            emoji = null;

            List<EmojiRigEntry> rigs = new();

            if (dto.rigs != null)
            {
                foreach (EmojiRigDto rig in dto.rigs)
                    rigs.Add(rig == null
                        ? null
                        : new EmojiRigEntry(rig.name, rig.sprite, ToVec2(rig.place), rig.scale == 0f ? 1f : rig.scale, rig.rotation));
            }

            List<EmojiClip> clips = new();
            Dictionary<string, EmojiClip> byName = new(StringComparer.Ordinal);

            if (dto.clips != null)
            {
                for (int c = 0; c < dto.clips.Count; c++)
                {
                    EmojiClipDto clipDto = dto.clips[c];

                    if (clipDto == null)
                    {
                        why = $"클립 [{c}]가 비어 있다";
                        return false;
                    }

                    if (!TryConvertClip(clipDto, out EmojiClip clip, out why))
                        return false;

                    clips.Add(clip);

                    // 이름이 겹치는 것은 규칙(EmojiRules)이 거른다 — 여기서는 첫 것만 잇는다.
                    if (!string.IsNullOrEmpty(clip.Name) && !byName.ContainsKey(clip.Name))
                        byName[clip.Name] = clip;
                }
            }

            if (!TryLink(dto.main, "main", byName, out EmojiClip main, out why) ||
                !TryLink(dto.@in, "in", byName, out EmojiClip @in, out why) ||
                !TryLink(dto.hold, "hold", byName, out EmojiClip hold, out why) ||
                !TryLink(dto.@out, "out", byName, out EmojiClip @out, out why))
            {
                return false;
            }

            emoji = new EmojiDefinition(dto.name, ToVec2(dto.offset), rigs, fps, clips, main, @in, hold, @out);
            why = null;
            return true;
        }

        private static bool TryLink(
            string clipName, string role, Dictionary<string, EmojiClip> byName, out EmojiClip clip, out string why)
        {
            clip = null;
            why = null;

            if (string.IsNullOrEmpty(clipName))
                return true;

            if (byName.TryGetValue(clipName, out clip))
                return true;

            why = $"{role}이 가리키는 클립 '{clipName}'이 없다";
            return false;
        }

        private static bool TryConvertClip(EmojiClipDto dto, out EmojiClip clip, out string why)
        {
            clip = null;

            List<EmojiTrack> tracks = new();

            if (dto.tracks != null)
            {
                for (int t = 0; t < dto.tracks.Count; t++)
                {
                    EmojiTrackDto trackDto = dto.tracks[t];

                    if (trackDto == null)
                    {
                        tracks.Add(null);
                        continue;
                    }

                    if (!TryParseChannel(trackDto.channel, out EmojiChannel channel))
                    {
                        why = $"클립 '{dto.name}': 트랙 [{t}]의 채널 '{trackDto.channel}'을 모른다 " +
                              "(x · y · scaleX · scaleY · rotation · alpha · sprite)";
                        return false;
                    }

                    int count = trackDto.keys?.Count ?? 0;

                    if (channel == EmojiChannel.Sprite)
                    {
                        EmojiSpriteKey[] spriteKeys = new EmojiSpriteKey[count];

                        for (int k = 0; k < count; k++)
                        {
                            EmojiKeyDto key = trackDto.keys[k];
                            spriteKeys[k] = key == null ? new EmojiSpriteKey(float.NaN, null) : new EmojiSpriteKey(key.f, key.s);
                        }

                        tracks.Add(new EmojiTrack(trackDto.rig, channel, null, spriteKeys));
                        continue;
                    }

                    CurveKey[] keys = new CurveKey[count];

                    for (int k = 0; k < count; k++)
                    {
                        EmojiKeyDto key = trackDto.keys[k];

                        if (key == null)
                        {
                            keys[k] = new CurveKey(float.NaN, float.NaN, 0f, 0f);
                            continue;
                        }

                        // 계단 키 = 나가는 기울기가 무한(CurveFunctions의 규칙 — 앞 키 값으로 멈춘다).
                        keys[k] = new CurveKey(key.f, key.v, key.i, key.step ? float.PositiveInfinity : key.o);
                    }

                    tracks.Add(new EmojiTrack(trackDto.rig, channel, keys));
                }
            }

            clip = new EmojiClip(dto.name, dto.length, tracks);
            why = null;
            return true;
        }

        public static bool TryParseChannel(string token, out EmojiChannel channel)
        {
            switch (token)
            {
                case "x": channel = EmojiChannel.PositionX; return true;
                case "y": channel = EmojiChannel.PositionY; return true;
                case "scaleX": channel = EmojiChannel.ScaleX; return true;
                case "scaleY": channel = EmojiChannel.ScaleY; return true;
                case "rotation": channel = EmojiChannel.Rotation; return true;
                case "alpha": channel = EmojiChannel.Alpha; return true;
                case "sprite": channel = EmojiChannel.Sprite; return true;
                default: channel = default; return false;
            }
        }

        public static string ChannelToken(EmojiChannel channel)
        {
            switch (channel)
            {
                case EmojiChannel.PositionX: return "x";
                case EmojiChannel.PositionY: return "y";
                case EmojiChannel.ScaleX: return "scaleX";
                case EmojiChannel.ScaleY: return "scaleY";
                case EmojiChannel.Rotation: return "rotation";
                case EmojiChannel.Alpha: return "alpha";
                default: return "sprite";
            }
        }

        private static Vec2 ToVec2(Float2Dto value) => value == null ? Vec2.Zero : value.ToVec2();
    }
}
