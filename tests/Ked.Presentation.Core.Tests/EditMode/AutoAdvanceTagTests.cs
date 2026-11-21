using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 줄 태그 #auto · #auto=길이(docs/PLAN_line-beat.md R9). 입력은 재생이 정규화한 태그다(소문자, ':'는 '=').
    /// </summary>
    public sealed class AutoAdvanceTagTests
    {
        [TestCase("auto", 0f)]
        [TestCase("auto=1.8s", 1.8f)]
        [TestCase("auto=54fr", 1.8f)]
        [TestCase("auto=0.27s", 0.27f)]
        [TestCase("auto=2", 2f)]        // 단위 없는 숫자는 초(DurationToken과 같다)
        public void auto_태그와_머무름을_읽는다(string tag, float hold)
        {
            Assert.IsTrue(AutoAdvanceTag.TryRead(tag, out float seconds, out string error));
            Assert.AreEqual(hold, seconds, 1e-4f);
            Assert.IsNull(error);
        }

        [TestCase("place")]
        [TestCase("autoplay")]
        [TestCase("auto2")]
        [TestCase("")]
        [TestCase(null)]
        public void auto가_아니면_false(string tag)
        {
            Assert.IsFalse(AutoAdvanceTag.TryRead(tag, out float seconds, out string error));
            Assert.AreEqual(0f, seconds);
            Assert.IsNull(error);
        }

        [Test]
        public void 길이를_읽지_못하면_머무름_없는_auto와_까닭()
        {
            Assert.IsTrue(AutoAdvanceTag.TryRead("auto=long", out float seconds, out string error), "자동 줄인 것은 그대로");
            Assert.AreEqual(0f, seconds);
            StringAssert.Contains("long", error);
        }
    }
}
