using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 이모지의 좌우 부호(EmojiSide)와 대본의 쪽 낱말(EmojiSideToken) — 순수 산수.
    ///
    /// 화면에서 보이는 것은 노드 셋의 가로 부호를 곱한 것이다: 로케이터(인물이 뒤집혔는가) × Emoji_Offset × EmojiRig_Place.
    /// 여기서는 그 곱이 규칙대로인지를 경우 전부(인물 뒤집힘 × FixedSide × Upright × flip = 16)로 본다.
    /// 노드에 실제로 쓰이는지는 EmojiPlaybackTests(Unity)가 본다.
    /// </summary>
    public sealed class EmojiSideTests
    {
        private static EmojiDefinition Emoji(bool fixedSide)
            => new(
                "sided",
                new Vec2(100f, 50f),
                new[]
                {
                    new EmojiRigEntry("a", "s", Vec2.Zero, 0.5f, 10f),
                    new EmojiRigEntry("q", "s", Vec2.Zero, 0.5f, 10f, upright: true),
                },
                fixedSide: fixedSide);

        [Test]
        public void flip이_거짓이면_종전_규칙_그대로다()
        {
            // flip을 몰랐던 때의 식: 거울 = 뒤집힘 ∧ ¬FixedSide, 오프셋 −1 ⇔ 뒤집힘 ∧ FixedSide, 리그 −1 ⇔ 거울 ∧ Upright.
            foreach (bool fixedSide in new[] { false, true })
            foreach (bool flipped in new[] { false, true })
            {
                EmojiDefinition emoji = Emoji(fixedSide);
                bool mirrored = flipped && !fixedSide;

                Assert.That(EmojiSide.IsMirrored(emoji, flipped), Is.EqualTo(mirrored));
                Assert.That(EmojiSide.OffsetSign(emoji, flipped), Is.EqualTo(flipped && fixedSide ? -1f : 1f));
                Assert.That(EmojiSide.PlaceSign(emoji, emoji.Rigs[0], flipped), Is.EqualTo(1f));
                Assert.That(EmojiSide.PlaceSign(emoji, emoji.Rigs[1], flipped), Is.EqualTo(mirrored ? -1f : 1f));
            }
        }

        [Test]
        public void 화면에서_거울에_비치는가는_인물의_방향과_flip의_배타적_논리합이다()
        {
            foreach (bool fixedSide in new[] { false, true })
            foreach (bool flipped in new[] { false, true })
            foreach (bool flip in new[] { false, true })
            {
                EmojiDefinition emoji = Emoji(fixedSide);
                string where = $"fixedSide={fixedSide} 인물뒤집힘={flipped} flip={flip}";

                // 로케이터 × 오프셋 = 이모지 전체가 화면에서 받는 가로 부호.
                float locator = flipped ? -1f : 1f;
                float whole = locator * EmojiSide.OffsetSign(emoji, flipped, flip);

                bool expected = (flipped && !fixedSide) != flip;

                Assert.That(EmojiSide.IsMirrored(emoji, flipped, flip), Is.EqualTo(expected), where);
                Assert.That(whole, Is.EqualTo(expected ? -1f : 1f), $"{where} — 노드의 부호를 곱한 것이 거울과 같아야 한다");

                // 그림: 보통 리그는 이모지와 같이 뒤집히고, Upright 리그는 늘 바로 선다.
                float plain = whole * EmojiSide.PlaceSign(emoji, emoji.Rigs[0], flipped, flip);
                float upright = whole * EmojiSide.PlaceSign(emoji, emoji.Rigs[1], flipped, flip);

                Assert.That(plain, Is.EqualTo(whole), $"{where} — 보통 리그");
                Assert.That(upright, Is.EqualTo(1f), $"{where} — Upright 리그는 바로 선다");
            }
        }

        [Test]
        public void flip은_인물이_뒤집히지_않아도_반대쪽에_세운다()
        {
            // 작업공간에서 잰 실례의 대부분(뒤집히지 않은 인물의 왼쪽에 뜬 48개)이 이 경우다.
            EmojiDefinition emoji = Emoji(fixedSide: false);

            Assert.That(EmojiSide.OffsetSign(emoji, locatorFlipped: false, flip: true), Is.EqualTo(-1f));
            Assert.That(EmojiSide.IsMirrored(emoji, locatorFlipped: false, flip: true), Is.True);
        }

        [Test]
        public void 뒤집힌_인물에_flip이면_두_번_뒤집혀_제_쪽이다()
        {
            EmojiDefinition emoji = Emoji(fixedSide: false);

            Assert.That(EmojiSide.IsMirrored(emoji, locatorFlipped: true, flip: true), Is.False);
            Assert.That(EmojiSide.OffsetSign(emoji, locatorFlipped: true, flip: true), Is.EqualTo(-1f), "오프셋이 로케이터를 되돌린다");
        }

        [Test]
        public void 방향을_따르지_않는_이모지도_flip은_따른다()
        {
            EmojiDefinition emoji = Emoji(fixedSide: true);

            foreach (bool flipped in new[] { false, true })
            {
                float locator = flipped ? -1f : 1f;

                Assert.That(locator * EmojiSide.OffsetSign(emoji, flipped, flip: false), Is.EqualTo(1f), $"인물뒤집힘={flipped} — 만든 쪽");
                Assert.That(locator * EmojiSide.OffsetSign(emoji, flipped, flip: true), Is.EqualTo(-1f), $"인물뒤집힘={flipped} — 반대쪽");
            }
        }

        [TestCase(null, true, false)]
        [TestCase("", true, false)]
        [TestCase("flip", true, true)]
        [TestCase(" FLIP ", true, true)]
        [TestCase("left", false, false)]
        [TestCase("right", false, false)]
        public void 쪽_낱말은_생략과_flip뿐이다(string token, bool known, bool flip)
        {
            Assert.That(EmojiSideToken.TryParse(token, out bool parsed), Is.EqualTo(known));
            Assert.That(parsed, Is.EqualTo(flip));
        }
    }
}
