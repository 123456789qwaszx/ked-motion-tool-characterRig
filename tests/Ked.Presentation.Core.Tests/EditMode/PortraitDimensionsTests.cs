using System.Collections.Generic;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 초상 치수 조회의 골든 — 런타임 PortraitResolver의 규약을 그대로 고정한다.
    /// 여기가 갈라지면 폴드가 다른 스프라이트를 고르고, 그건 곧 크기 불일치다.
    /// </summary>
    public sealed class PortraitDimensionsTests
    {
        private const float Eps = 1e-4f;

        private static PortraitDimensionsFileDto Db(params PortraitDimensionDto[] entries)
            => new() { entries = new List<PortraitDimensionDto>(entries) };

        private static PortraitDimensionDto E(
            string character, string variant, string emotion, float width, float height = 1000f)
            => new() { character = character, variant = variant, emotion = emotion, width = width, height = height };

        [Test]
        public void 크기는_가로_세로_픽셀_그대로다()
        {
            // 초상은 원본 픽셀 크기 그대로 그린다(2026-11-09) — 종횡비가 아니라 크기를 낸다.
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 800f, 1600f));

            Assert.That(db.TryGetSize("tyrant", "a", "01", out Vec2 size, out _), Is.True);
            Assert.That(size.X, Is.EqualTo(800f).Within(Eps));
            Assert.That(size.Y, Is.EqualTo(1600f).Within(Eps));
        }

        [Test]
        public void 캐릭터_키는_대소문자를_가리지_않는다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 500f));

            Assert.That(db.TryGetSize("Tyrant", "a", "01", out _, out _), Is.True);
            Assert.That(db.TryGetSize(" TYRANT ", "a", "01", out _, out _), Is.True);
        }

        [Test]
        public void 변형은_문자열_전체가_키다()
        {
            // 초상 에셋이 <캐릭터>/<변형>/<표정>.png 폴더 규약을 쓰면서 변형은 폴더 이름 그 자체가 됐다.
            // 마지막 글자만 보던 종전 규칙이라면 school과 casual이 같은 키('l')로 뭉갠다.
            PortraitDimensionsFileDto db = Db(
                E("tyrant", "school", "01", 700f),
                E("tyrant", "casual", "01", 400f));

            Assert.That(db.TryGetSize("tyrant", "school", "01", out Vec2 school, out _), Is.True);
            Assert.That(school.X, Is.EqualTo(700f).Within(Eps));

            Assert.That(db.TryGetSize("tyrant", "casual", "01", out Vec2 casual, out _), Is.True);
            Assert.That(casual.X, Is.EqualTo(400f).Within(Eps),
                "마지막 글자가 같아도 다른 변형이다");

            Assert.That(db.TryGetSize("tyrant", " School ", "01", out _, out _), Is.True,
                "캐릭터 키와 마찬가지로 트림·소문자화는 한다");
        }

        [Test]
        public void 접두사가_붙은_옛_변형_키는_더는_같은_키가_아니다()
        {
            // 파일 이름에 캐릭터가 눌어붙던 시절의 'body_b'는 이제 'b'와 다른 키다.
            // 폴백 (캐릭터, "a", "01")도 없으므로 그대로 실패한다 — 짐작으로 잇지 않는다.
            PortraitDimensionsFileDto db = Db(E("tyrant", "body_b", "01", 700f));

            Assert.That(db.TryGetSize("tyrant", "b", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("변형='b'"));
        }

        [Test]
        public void 표정은_두_자리로_접힌다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "02", 640f));

            Assert.That(db.TryGetSize("tyrant", "a", "2", out _, out _), Is.True);
            Assert.That(db.TryGetSize("tyrant", "a", "02", out _, out _), Is.True);
        }

        [Test]
        public void 빈_인자는_기본_변형과_기본_표정이다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetSize("tyrant", "", "", out Vec2 size, out _), Is.True);
            Assert.That(size.X, Is.EqualTo(600f).Within(Eps));
        }

        [Test]
        public void 못_찾으면_기본_변형_01로_한_번_물러선다()
        {
            PortraitDimensionsFileDto db = Db(
                E("tyrant", "a", "01", 600f),
                E("tyrant", "b", "03", 900f));

            // (b, 09)는 없다 → (a, 01)로.
            Assert.That(db.TryGetSize("tyrant", "b", "09", out Vec2 size, out _), Is.True);
            Assert.That(size.X, Is.EqualTo(600f).Within(Eps));
        }

        [Test]
        public void 규격_밖_표정도_폴백을_탄다()
        {
            // "abc"는 두 자리 코드로 접히지 않아 빈 코드가 된다 — 런타임도 그 키로 조회하고
            // 실패한 뒤 폴백으로 물러선다. 조용히 기본값으로 잇지 않는다.
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetSize("tyrant", "a", "abc", out Vec2 size, out _), Is.True);
            Assert.That(size.X, Is.EqualTo(600f).Within(Eps));
        }

        [Test]
        public void 폴백도_없으면_이유와_함께_실패한다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetSize("amber", "a", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("amber"));
        }

        [Test]
        public void 캐릭터_키가_없으면_실패한다()
        {
            PortraitDimensionsFileDto db = Db(E("tyrant", "a", "01", 600f));

            Assert.That(db.TryGetSize("", "a", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("캐릭터 키"));
        }

        [Test]
        public void 중복_키는_먼저_들어온_것이_이긴다()
        {
            // PortraitResolver가 TryAdd로 중복을 버리는 방향과 같다.
            PortraitDimensionsFileDto db = Db(
                E("tyrant", "a", "01", 600f),
                E("tyrant", "a", "01", 900f));

            Assert.That(db.TryGetSize("tyrant", "a", "01", out Vec2 size, out _), Is.True);
            Assert.That(size.X, Is.EqualTo(600f).Within(Eps));
        }

        [Test]
        public void 가로나_세로가_0이면_크기를_내지_않는다()
        {
            PortraitDimensionsFileDto tall = Db(E("tyrant", "a", "01", 600f, 0f));
            PortraitDimensionsFileDto wide = Db(E("tyrant", "a", "01", 0f, 1000f));

            Assert.That(tall.TryGetSize("tyrant", "a", "01", out _, out string reason), Is.False);
            Assert.That(reason, Does.Contain("유효하지 않다"));
            Assert.That(wide.TryGetSize("tyrant", "a", "01", out _, out _), Is.False);
        }
    }
}
