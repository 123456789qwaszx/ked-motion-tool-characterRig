using System.Collections.Generic;
using System.Linq;
using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 이모지의 움직임(docs/emoji-format.md) — 형식의 규칙 · 그 프레임의 모습 · 재생의 단계.
    /// 런타임 Tests/EditMode/EmojiPlaybackTests.cs에서 유니티를 쓰지 않는 절만 반입했다(접기 · 재생 시스템은 저쪽에 남는다).
    /// </summary>
    public sealed class EmojiPlaybackTests
    {
        private const float Eps = 1e-4f;

        // ── 견본 ─────────────────────────────────────────────────────

        private static EmojiKeyDto K(float f, float v, bool step = false) => new() { f = f, v = v, step = step };
        private static EmojiKeyDto S(float f, string sprite) => new() { f = f, s = sprite };

        private static EmojiTrackDto Tr(string rig, string channel, params EmojiKeyDto[] keys)
            => new() { rig = rig, channel = channel, keys = keys.ToList() };

        private static EmojiClipDto Clip(string name, int length, params EmojiTrackDto[] tracks)
            => new() { name = name, length = length, tracks = tracks.ToList() };

        private static List<EmojiRigDto> Rigs(params string[] names)
            => names.Select(n => new EmojiRigDto { name = n, sprite = "s" }).ToList();

        private static EmojiDto Static() => new() { name = "static", rigs = Rigs("a") };

        // 머묾 없는 이모지 — 13프레임을 한 번.
        private static EmojiDto Shot() => new()
        {
            name = "shot",
            rigs = Rigs("a"),
            clips = new List<EmojiClipDto>
            {
                Clip("m", 13,
                    Tr("a", "scaleX", K(0, 0.5f), K(12, 1f)),
                    Tr("", "y", K(0, 0f), K(12, 24f)),
                    Tr("a", "sprite", S(0, "s1"), S(6, "s2")),
                    Tr("a", "alpha", K(0, 1f, step: true), K(6, 0.5f))),
            },
            main = "m",
        };

        // 머무는 이모지 — 등장 4 · 머묾 12 · 퇴장 6.
        private static EmojiDto Hold() => new()
        {
            name = "hold",
            offset = new Float2Dto { x = 100f, y = 50f },
            rigs = Rigs("a"),
            clips = new List<EmojiClipDto>
            {
                Clip("i", 4, Tr("a", "scaleX", K(0, 1.27f), K(3, 1f))),
                Clip("h", 12, Tr("a", "scaleX", K(0, 1f), K(6, 1.08f), K(11, 1f)), Tr("", "x", K(0, 0f), K(11, 22f))),
                Clip("o", 6, Tr("", "alpha", K(0, 1f), K(5, 0f))),
            },
            @in = "i",
            hold = "h",
            @out = "o",
        };

        private static Dictionary<string, EmojiDefinition> Build(List<string> warned, params EmojiDto[] emojis)
        {
            EmojisFileDto file = new() { schema = EmojiDefinitionLoader.Schema, fps = 24f, emojis = emojis.ToList() };
            return EmojiDefinitionLoader.Build(file, (name, why) => warned?.Add($"{name}: {why}"));
        }

        private static EmojiDefinition One(EmojiDto dto)
        {
            List<string> warned = new();
            Dictionary<string, EmojiDefinition> emojis = Build(warned, dto);
            Assert.That(warned, Is.Empty);
            return emojis[dto.name];
        }

        // ── 형식의 규칙 ──────────────────────────────────────────────

        [Test]
        public void 머묾이_있는가가_종류를_정한다()
        {
            Assert.That(One(Static()).Stays, Is.True, "클립이 없으면 정의의 모습으로 서 있다");
            Assert.That(One(Hold()).Stays, Is.True);
            Assert.That(One(Shot()).Stays, Is.False);
        }

        [Test]
        public void 어긋난_이모지는_걸러지고_까닭이_남는다()
        {
            List<string> warned = new();

            Dictionary<string, EmojiDefinition> emojis = Build(warned,
                new EmojiDto { name = "badlink", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5) }, main = "zzz" },
                new EmojiDto { name = "nolink", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5) } },
                new EmojiDto { name = "inwithouthold", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5) }, main = "m", @in = "m" },
                new EmojiDto { name = "mainandhold", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5), Clip("h", 5) }, main = "m", hold = "h" },
                new EmojiDto { name = "badrig", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5, Tr("zz", "x", K(0, 0))) }, main = "m" },
                new EmojiDto { name = "badchannel", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5, Tr("a", "foo", K(0, 0))) }, main = "m" },
                new EmojiDto { name = "outofrange", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5, Tr("a", "x", K(0, 0), K(5, 1))) }, main = "m" },
                new EmojiDto { name = "unsorted", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5, Tr("a", "x", K(2, 0), K(1, 1))) }, main = "m" },
                new EmojiDto { name = "spriteonwhole", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 5, Tr("", "sprite", S(0, "x"))) }, main = "m" },
                new EmojiDto { name = "zerolength", rigs = Rigs("a"), clips = new List<EmojiClipDto> { Clip("m", 0) }, main = "m" },
                Static());

            Assert.That(emojis.Keys, Is.EquivalentTo(new[] { "static" }));
            Assert.That(warned, Has.Count.EqualTo(10), string.Join("\n", warned));
        }

        // ── 그 프레임의 모습 ─────────────────────────────────────────

        [Test]
        public void 키_사이는_곡선으로_잇고_키가_없는_채널은_중립값이다()
        {
            EmojiClip clip = One(Shot()).Main;

            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.ScaleX, 0f), Is.EqualTo(0.5f).Within(Eps));
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.ScaleX, 12f), Is.EqualTo(1f).Within(Eps));

            // 기울기를 생략하면 키에서 평평하다 — 가운데는 두 값의 가운데다.
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.ScaleX, 6f), Is.EqualTo(0.75f).Within(Eps));

            // 클립 밖은 끝값.
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.ScaleX, 99f), Is.EqualTo(1f).Within(Eps));

            // 키가 없는 채널.
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.ScaleY, 3f), Is.EqualTo(1f));
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.Rotation, 3f), Is.EqualTo(0f));
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.PositionX, 3f), Is.EqualTo(0f));

            // 빈 이름 = 이모지 전체.
            Assert.That(EmojiClipMath.Sample(clip, "", EmojiChannel.PositionY, 12f), Is.EqualTo(24f).Within(Eps));

            // 클립이 없으면 대상 전체가 중립이다.
            EmojiNodePose neutral = EmojiClipMath.SampleNode(null, "a", 3f);
            Assert.That(neutral.Scale.X, Is.EqualTo(1f));
            Assert.That(neutral.Alpha, Is.EqualTo(1f));
            Assert.That(neutral.SpriteKey, Is.Null);
        }

        [Test]
        public void 계단_키는_다음_키까지_멈춰_있다()
        {
            EmojiClip clip = One(Shot()).Main;

            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.Alpha, 0f), Is.EqualTo(1f));
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.Alpha, 5.9f), Is.EqualTo(1f));
            Assert.That(EmojiClipMath.Sample(clip, "a", EmojiChannel.Alpha, 6f), Is.EqualTo(0.5f));
        }

        [Test]
        public void 그림_바꾸기는_그_프레임부터_바뀐다()
        {
            EmojiClip clip = One(Shot()).Main;

            Assert.That(EmojiClipMath.SampleSprite(clip, "a", 0f), Is.EqualTo("s1"));
            Assert.That(EmojiClipMath.SampleSprite(clip, "a", 5.99f), Is.EqualTo("s1"));
            Assert.That(EmojiClipMath.SampleSprite(clip, "a", 6f), Is.EqualTo("s2"));
            Assert.That(EmojiClipMath.SampleSprite(clip, "a", 99f), Is.EqualTo("s2"));

            // 그림 바꾸기 채널이 없으면 null — 리그의 기본 스프라이트다.
            Assert.That(EmojiClipMath.SampleSprite(One(Hold()).Hold, "a", 0f), Is.Null);
        }

        // ── 재생의 단계 ──────────────────────────────────────────────

        [Test]
        public void 머묾_없는_이모지는_한_번_재생하고_끝난다()
        {
            EmojiPlayback playback = new(One(Shot()));

            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Main));

            playback.Advance(6f / 24f);
            Assert.That(playback.Frame, Is.EqualTo(6f).Within(1e-3f));

            // 마지막 프레임(12)도 한 프레임만큼 보인다 — 13프레임짜리다.
            playback.Advance(6.9f / 24f);
            Assert.That(playback.IsDone, Is.False);

            playback.Advance(0.2f / 24f);
            Assert.That(playback.IsDone, Is.True);
        }

        [Test]
        public void 머무는_이모지는_등장_뒤에_머묾을_되풀이한다()
        {
            EmojiPlayback playback = new(One(Hold()));

            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.In));

            // 등장(4프레임)을 넘으면 머묾의 프레임 0에서 잇는다 — 넘친 한 프레임만큼 앞서서.
            playback.Advance(5f / 24f);
            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Hold));
            Assert.That(playback.Frame, Is.EqualTo(1f).Within(1e-3f));

            // 머묾의 주기는 12프레임이다.
            playback.Advance(12f / 24f);
            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Hold));
            Assert.That(playback.Frame, Is.EqualTo(1f).Within(1e-2f));

            playback.Advance(100f);
            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Hold));
            Assert.That(playback.Frame, Is.InRange(0f, 12f));
        }

        [Test]
        public void 내리면_퇴장을_재생하고_끝난다_등장_도중이어도_바로_넘어간다()
        {
            EmojiPlayback playback = new(One(Hold()));
            playback.Advance(1f / 24f);
            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.In));

            playback.RequestOff();
            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Out));
            Assert.That(playback.Frame, Is.EqualTo(0f));

            // 퇴장 중에 다시 내려도 처음으로 돌아가지 않는다.
            playback.Advance(3f / 24f);
            playback.RequestOff();
            Assert.That(playback.Frame, Is.EqualTo(3f).Within(1e-3f));

            playback.Advance(3f / 24f);
            Assert.That(playback.IsDone, Is.True);
        }

        [Test]
        public void 퇴장이_없으면_내리는_순간_끝난다()
        {
            EmojiPlayback playback = new(One(Static()));

            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Hold));
            Assert.That(playback.Clip, Is.Null);

            playback.Advance(5f);
            Assert.That(playback.Phase, Is.EqualTo(EmojiPhase.Hold));

            playback.RequestOff();
            Assert.That(playback.IsDone, Is.True);
        }

        [Test]
        public void 건너뛴_뒤의_모습_머무는_것은_머묾의_처음_아닌_것은_없다()
        {
            EmojiPlayback hold = new(One(Hold()));
            hold.Settle();
            Assert.That(hold.Phase, Is.EqualTo(EmojiPhase.Hold));
            Assert.That(hold.Frame, Is.EqualTo(0f));

            EmojiPlayback shot = new(One(Shot()));
            shot.Settle();
            Assert.That(shot.IsDone, Is.True);
        }
    }
}
