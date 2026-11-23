using System.Collections.Generic;
using System.Linq;
using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 프레임 시간표(ked-story-tools docs/PLAN.md §4.6 · §6) — 줄 읽기의 결과에서 보통 재생의 기준 시간표를 낸다.
    /// </summary>
    public sealed class LineTimetableTests
    {
        private const float Tolerance = 1e-4f;

        // 셈이 쉬운 타자기 — 글자마다 0.1초, 숨 · 머리의 느려짐 없음.
        private static TypewriterPacing Flat() => new()
        {
            UnitsPerSecond = 10f,
            MultWhitespace = 1f,
            MultComma = 1f,
            MultColonSemicolon = 1f,
            MultPeriod = 1f,
            MultQuestionExclaim = 1f,
            MultEllipsisChar = 1f,
            MultTripleDotToken = 1f,
            StartRampUnits = 0,
            DelayCap = 10f,
        };

        private static LineRead Read(params string[] body)
            => YarnLineReader.Read("title: Test\n---\n" + string.Join("\n", body) + "\n===\n", "t", "test.yarn");

        private static TimetableStep Step(LineTimetable table, string name) => table.Steps.Single(s => s.Step.Name == name);

        private static TimetableCommand Command(LineTimetable table, string name) => table.Commands.Single(c => c.Command.Name == name);

        [Test]
        public void 앞_앵커와_글_표지와_end의_때()
        {
            // 글: "아, 음…… " 7자 + "이건 비밀인데." 8자.
            LineRead line = Read(
                "<<at in fade_out s1 0.5s>>",
                "<<at in pause 0.5s>>",
                "<<at in fade s2 0.72 0.5s>>",
                "<<gap in 1s>>",
                "<<at start face s2 05>>",
                "<<at a face_swap s2 03 0.3s>>",
                "<<gap a 12fr>>",
                "<<at end emoji s2 sweat>>",
                "세이나: [beat=in/]아, 음…… [beat=a/]이건 비밀인데. #line:t");

            LineTimetable table = LineTimetable.Build(line, Flat());

            Assert.That(table.Steps.Select(s => s.Step.Name), Is.EqualTo(new[] { "in", "start", "a", "end" }));
            Assert.That(Step(table, "in").Open, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(Step(table, "start").Open, Is.EqualTo(1f).Within(Tolerance), "in의 간격 1초");
            Assert.That(Step(table, "a").Open, Is.EqualTo(1.7f).Within(Tolerance), "일곱 글자 뒤");
            Assert.That(Step(table, "end").Open, Is.EqualTo(2.5f).Within(Tolerance), "a의 간격(0.4초)보다 글이 길다");
            Assert.That(table.TypingStart, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(table.TypingEnd, Is.EqualTo(2.5f).Within(Tolerance));
            Assert.That(table.Ready, Is.EqualTo(2.5f).Within(Tolerance));

            Assert.That(table.Steps.Select(s => s.OpenFrame), Is.EqualTo(new[] { 0, 30, 51, 75 }));
            Assert.That(table.Steps.Select(s => s.Estimated), Is.EqualTo(new[] { false, false, true, true }),
                "글이 없는 구간만 정확하다");

            // 커맨드 — 앵커가 열린 때 + 지연, 길이는 대본의 것.
            TimetableCommand fade = Command(table, "fade");
            Assert.That(fade.Start, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(fade.Duration, Is.EqualTo(0.5f));
            Assert.That(fade.StartFrame, Is.EqualTo(15));
            Assert.That(fade.EndFrame, Is.EqualTo(30));
            Assert.That(Command(table, "face_swap").StartFrame, Is.EqualTo(51));
            Assert.That(Command(table, "face_swap").EndFrame, Is.EqualTo(60));
            Assert.That(Command(table, "emoji").Duration, Is.Null, "모르는 길이");
            Assert.That(Command(table, "emoji").EndFrame, Is.EqualTo(Command(table, "emoji").StartFrame));
        }

        [Test]
        public void 간격이_차지_않으면_타자기가_표지_앞에서_선다()
        {
            // "여기서 " 4자 뒤의 a — start의 간격 2초가 찰 때까지 선다. 그 뒤의 글은 그만큼 밀린다.
            LineRead line = Read(
                "<<at start face s1 3>>",
                "<<gap start 2s>>",
                "<<at a face_swap s1 05 13fr>>",
                "<<gap a 1s>>",
                "<<at b motion s1 nod>>",
                "하린: 여기서 [beat=a/]둘셋 [beat=b/]넷. #line:t");

            LineTimetable table = LineTimetable.Build(line, Flat());

            Assert.That(Step(table, "a").Open, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(Step(table, "b").Open, Is.EqualTo(3f).Within(Tolerance), "글은 0.3초면 닿지만 a의 간격이 1초다");
            Assert.That(Step(table, "end").Open, Is.EqualTo(3.2f).Within(Tolerance));
        }

        [Test]
        public void 기다리는_명령이_다음_앵커를_붙잡는다()
        {
            LineRead line = Read(
                "<<at in screen_cover black 1s>>",
                "<<at in screen_cover_clear 0.5s>>",
                "<<at start face s1 3>>",
                "<<at end emoji s1 star>>",
                "<<gap end 1s>>",
                "하린: [beat=in/]둘. #line:t");

            LineTimetable table = LineTimetable.Build(line, Flat());

            Assert.That(Command(table, "screen_cover_clear").Start, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(Step(table, "start").Open, Is.EqualTo(1f).Within(Tolerance), "간격을 적지 않아도 기다림만큼");
            Assert.That(Step(table, "end").Open, Is.EqualTo(1.2f).Within(Tolerance));
            Assert.That(table.Ready, Is.EqualTo(2.2f).Within(Tolerance), "end의 간격 — 넘김을 받기 전의 최소 시간");
        }

        [Test]
        public void 연출이_없는_줄은_글의_길이다()
        {
            LineTimetable table = LineTimetable.Build(Read("하린: 다섯글자. #line:t"), Flat());

            Assert.That(table.Steps.Select(s => s.Step.Name), Is.EqualTo(new[] { "start", "end" }));
            Assert.That(table.Commands, Is.Empty);
            Assert.That(table.Ready, Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void 대본에_없는_길이는_물어서_채운다()
        {
            LineRead line = Read(
                "<<at start motion s1 nod>>",
                "<<at start face s1 3>>",
                "하린: 끄덕. #line:t");

            LineTimetable table = LineTimetable.Build(
                line, Flat(), command => command.Name == "motion" && command.Args[1] == "nod" ? 0.8f : (float?)null);

            Assert.That(Command(table, "motion").Duration, Is.EqualTo(0.8f));
            Assert.That(Command(table, "motion").EndFrame, Is.EqualTo(24));
            Assert.That(Command(table, "face").Duration, Is.Null);
        }

        [Test]
        public void 대사창이_열리는_시간만큼_타자기가_늦는다()
        {
            TypewriterPacing pacing = Flat();
            pacing.StartDelay = 0.3f;

            LineTimetable table = LineTimetable.Build(Read("<<at a face s1 3>>", "하린: 하나[beat=a/]둘. #line:t"), pacing);

            Assert.That(Step(table, "start").Open, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(table.TypingStart, Is.EqualTo(0.3f).Within(Tolerance));
            Assert.That(Step(table, "a").Open, Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void 타자기의_덩어리()
        {
            // 보통은 한 글자, "..."는 셋이 한 덩어리다(재생의 타자기와 같다).
            Assert.That(TypewriterPacing.RevealCounts("가...나"), Is.EqualTo(new[] { 1, 4, 5 }));
            Assert.That(TypewriterPacing.RevealCounts("가.."), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(TypewriterPacing.RevealCounts(""), Is.Empty);

            // 덩어리 안의 표지는 덩어리의 머리에서 열린다 — 셋이 기다림 없이 이어 보인다.
            LineRead line = Read("<<at a face s1 3>>", "하린: 가..[beat=a/].나 #line:t");
            LineTimetable table = LineTimetable.Build(line, Flat());

            Assert.That(Step(table, "a").Open, Is.EqualTo(0.1f).Within(Tolerance));
        }

        [Test]
        public void 타자기의_기준_속도는_재생의_식과_같다()
        {
            // EllipsisBreathTypewriter의 기본값: 초당 30자 · 머리 10덩어리는 1.25배에서 1배로 · 상한 0.35초.
            TypewriterPacing pacing = TypewriterPacing.Default;
            string body = "가, 나.";
            List<int> reveal = TypewriterPacing.RevealCounts(body);

            double per = 1.0 / 30.0;
            double Ramp(int unit) => 1.25 + (1.0 - 1.25) * (unit / 9.0);

            Assert.That(pacing.DelayAfter(body, reveal, 0), Is.EqualTo(per * 1.0 * Ramp(0)).Within(1e-6));
            Assert.That(pacing.DelayAfter(body, reveal, 1), Is.EqualTo(per * 1.7 * Ramp(1)).Within(1e-6), "쉼표");
            Assert.That(pacing.DelayAfter(body, reveal, 2), Is.EqualTo(per * 0.35 * Ramp(2)).Within(1e-6), "공백");
            Assert.That(pacing.DelayAfter(body, reveal, 4), Is.EqualTo(per * 3.2 * Ramp(4)).Within(1e-6), "마침표");

            // 상한.
            pacing.UnitsPerSecond = 5f;
            Assert.That(pacing.DelayAfter(body, reveal, 4), Is.EqualTo(0.35).Within(1e-6));

            // 머리의 느려짐이 끝난 뒤.
            string longBody = "가나다라마바사아자차카타";
            Assert.That(TypewriterPacing.Default.DelayAfter(longBody, TypewriterPacing.RevealCounts(longBody), 10), Is.EqualTo(per).Within(1e-6));
        }
    }
}
