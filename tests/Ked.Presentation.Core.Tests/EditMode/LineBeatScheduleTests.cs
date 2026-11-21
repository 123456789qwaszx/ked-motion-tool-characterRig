using System.Collections.Generic;
using System.Linq;
using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 대사 줄의 실행 목록(docs/PLAN_line-beat.md R2 · R5 · R6 · R7)과 앵커(docs/PLAN_line-anchor.md AR1 · AR4 · AR6 · AR7 · AR9)
    /// — 재생과 폴드가 같이 부르는 함수다.
    /// 명령의 둘째 인자를 이름표로 써서 순서를 본다.
    /// </summary>
    public sealed class LineBeatScheduleTests
    {
        private static StageCommand Cmd(string tag) => new("face", new[] { "s1", tag });

        private static AtCommand At(string beat, string tag) => new(beat, Cmd(tag));

        private static string[] Order(List<ScheduledCommand> schedule) => schedule.Select(c => c.At.Command.Args[1]).ToArray();

        [Test]
        public void 글자_자리_다음_표지_순서_다음_쓴_순서이고_end가_마지막이다()
        {
            // 글: "가[beat=b/][beat=a/]나[beat=c/]다" — b와 a는 같은 자리(1), c는 2.
            BeatMarker[] markers =
            {
                new BeatMarker("b", 1),
                new BeatMarker("a", 1),
                new BeatMarker("c", 2),
            };

            AtCommand[] ats =
            {
                At("end", "e1"),
                At("c", "c1"),
                At("a", "a1"),
                At("b", "b1"),
                At("a", "a2"),
                At("end", "e2"),
            };

            List<string> problems = new();
            List<ScheduledCommand> schedule = LineBeatSchedule.Build(markers, ats, problems);

            Assert.That(problems, Is.Empty);
            Assert.That(Order(schedule), Is.EqualTo(new[] { "b1", "a1", "a2", "c1", "e1", "e2" }));
            Assert.That(schedule.Select(c => c.Position), Is.EqualTo(new[] { 1, 1, 1, 2, LineBeatSchedule.LineEnd, LineBeatSchedule.LineEnd }));
            Assert.That(schedule[1].At.ToString(), Is.EqualTo("<<at a face s1 a1>>"));
        }

        [Test]
        public void 표지가_없는_at은_줄_끝에서_쓴_순서로_실행하고_적는다()
        {
            // 번역문이 표지를 빠뜨린 줄(R5) — 연출이 사라지지 않는다.
            List<string> problems = new();
            List<ScheduledCommand> schedule = LineBeatSchedule.Build(
                new[] { new BeatMarker("a", 3) },
                new[] { At("end", "e1"), At("b", "b1"), At("a", "a1") },
                problems);

            Assert.That(Order(schedule), Is.EqualTo(new[] { "a1", "e1", "b1" }));
            Assert.That(schedule[2].Position, Is.EqualTo(LineBeatSchedule.LineEnd));
            Assert.That(problems.Single(), Does.Contain("[beat=b/]가 없다"));
        }

        [Test]
        public void at이_없는_표지는_무시하고_적는다()
        {
            List<string> problems = new();
            List<ScheduledCommand> schedule = LineBeatSchedule.Build(
                new[] { new BeatMarker("a", 0), new BeatMarker("b", 2) },
                new[] { At("a", "a1") },
                problems);

            Assert.That(Order(schedule), Is.EqualTo(new[] { "a1" }));
            Assert.That(problems.Single(), Does.Contain("[beat=b/]에 <<at b …>>가 없다"));
        }

        [Test]
        public void 같은_표지가_둘이면_앞의_것을_쓰고_적는다()
        {
            List<string> problems = new();
            List<ScheduledCommand> schedule = LineBeatSchedule.Build(
                new[] { new BeatMarker("a", 1), new BeatMarker("a", 4) },
                new[] { At("a", "a1") },
                problems);

            Assert.That(schedule.Single().Position, Is.EqualTo(1));
            Assert.That(problems.Single(), Does.Contain("둘이다"));
        }

        [TestCase("end")]
        [TestCase("A")]
        [TestCase("a-b")]
        [TestCase("")]
        public void 꼴이_틀린_표지는_무시하고_그_이름의_at은_줄_끝이다(string name)
        {
            List<string> problems = new();
            List<ScheduledCommand> schedule = LineBeatSchedule.Build(
                new[] { new BeatMarker(name, 1) },
                new[] { At(name, "x") },
                problems);

            Assert.That(schedule.Single().Position, Is.EqualTo(LineBeatSchedule.LineEnd));

            // end는 예약이라 at end는 본디 줄 끝이다 — 표지만 문제다.
            Assert.That(problems.Count, Is.EqualTo(name == "end" ? 1 : 2), string.Join("\n", problems));
        }

        [TestCase("a", true)]
        [TestCase("b2", true)]
        [TestCase("12", true)]
        [TestCase("end", false)]
        [TestCase("start", false)]
        [TestCase("Ab", false)]
        [TestCase("a_b", false)]
        [TestCase(null, false)]
        public void 표지_이름의_꼴(string name, bool valid)
        {
            Assert.That(LineBeatSchedule.IsValidBeatName(name), Is.EqualTo(valid));
        }

        [Test]
        public void at의_토큰을_표지와_명령으로_읽는다()
        {
            Assert.That(LineBeatSchedule.TryReadAt(new[] { "a", "face_swap", "s2", "03", "0.3s" }, "f:3", out AtCommand at, out string error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(at.Beat, Is.EqualTo("a"));
            Assert.That(at.Command.Name, Is.EqualTo("face_swap"));
            Assert.That(at.Command.Args, Is.EqualTo(new[] { "s2", "03", "0.3s" }));
            Assert.That(at.Command.Source, Is.EqualTo("f:3"));

            Assert.That(LineBeatSchedule.TryReadAt(new[] { "a" }, "f:4", out _, out error), Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void 빈_줄은_빈_목록이다()
        {
            Assert.That(LineBeatSchedule.Build(null, null), Is.Empty);
            Assert.That(LineBeatSchedule.Build(new BeatMarker[0], new AtCommand[0]), Is.Empty);
        }

        // ── 앵커(docs/PLAN_line-anchor.md AR1 · AR4 · AR6 · AR7 · AR9) ─────────

        private static string[] Names(List<LineAnchor> anchors) => anchors.Select(a => a.Name).ToArray();

        private static string[] Tags(LineAnchor anchor) => anchor.Commands.Select(c => c.Command.Args[1]).ToArray();

        [Test]
        public void 앵커는_앞_앵커_start_글_표지_end의_차례이고_그_안은_쓴_순서다()
        {
            // 글: "[beat=in/][beat=up/]가[beat=b/][beat=a/]나[beat=c/]" — in · up은 첫 글자 앞(자리 0), b · a는 1, c는 글 끝(2).
            BeatMarker[] markers =
            {
                new BeatMarker("in", 0),
                new BeatMarker("up", 0),
                new BeatMarker("b", 1),
                new BeatMarker("a", 1),
                new BeatMarker("c", 2),
            };

            AtCommand[] ats =
            {
                At("end", "e1"),
                At("c", "c1"),
                At("start", "s1"),
                At("up", "u1"),
                At("a", "a1"),
                At("in", "i1"),
                At("b", "b1"),
                At("start", "s2"),
                At("in", "i2"),
            };

            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(markers, ats, null, problems);

            Assert.That(problems, Is.Empty);
            Assert.That(Names(anchors), Is.EqualTo(new[] { "in", "up", "start", "b", "a", "c", "end" }));
            Assert.That(anchors.Select(a => a.Kind), Is.EqualTo(new[]
            {
                LineAnchorKind.Pre, LineAnchorKind.Pre, LineAnchorKind.Start,
                LineAnchorKind.Text, LineAnchorKind.Text, LineAnchorKind.Text, LineAnchorKind.End,
            }));
            Assert.That(anchors.Select(a => a.Position), Is.EqualTo(new[] { 0, 0, 0, 1, 1, 2, LineBeatSchedule.LineEnd }));
            Assert.That(Tags(anchors[0]), Is.EqualTo(new[] { "i1", "i2" }));
            Assert.That(Tags(anchors[2]), Is.EqualTo(new[] { "s1", "s2" }));
            Assert.That(LineBeatSchedule.Flatten(anchors).Select(c => c.Command.Args[1]),
                Is.EqualTo(new[] { "i1", "i2", "u1", "s1", "s2", "b1", "a1", "c1", "e1" }));
        }

        [Test]
        public void 빈_줄에도_start와_end는_있다()
        {
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(null, null);

            Assert.That(Names(anchors), Is.EqualTo(new[] { "start", "end" }));
            Assert.That(anchors.All(a => a.Commands.Count == 0 && a.Gap == 0f && !a.Legacy), Is.True);
        }

        [Test]
        public void 간격은_그_이름의_앵커에_붙는다()
        {
            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("in", 0), new BeatMarker("a", 2) },
                new[] { At("in", "i1"), At("a", "a1") },
                new[] { new GapCommand("in", 1f), new GapCommand("a", 0.4f), new GapCommand("end", 0.5f), new GapCommand("start", 0.2f) },
                problems);

            Assert.That(problems, Is.Empty);
            Assert.That(Names(anchors), Is.EqualTo(new[] { "in", "start", "a", "end" }));
            Assert.That(anchors.Select(a => a.Gap), Is.EqualTo(new[] { 1f, 0.2f, 0.4f, 0.5f }));
        }

        [Test]
        public void 명령이_없어도_간격이_있는_표지는_앵커로_선다()
        {
            // 글을 치다 그 자리에서 숨만 고르는 표지 — 명령은 없고 간격만 있다.
            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("a", 2), new BeatMarker("b", 4) },
                null,
                new[] { new GapCommand("a", 0.4f) },
                problems);

            Assert.That(Names(anchors), Is.EqualTo(new[] { "start", "a", "end" }));
            Assert.That(problems.Single(), Does.Contain("[beat=b/]에 <<at b …>>가 없다"));
        }

        [Test]
        public void 앵커가_없는_간격과_같은_이름의_간격_둘은_적고_버린다()
        {
            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("a", 2) },
                new[] { At("a", "a1") },
                new[] { new GapCommand("a", 0.4f), new GapCommand("x", 1f), new GapCommand("a", 9f) },
                problems);

            Assert.That(anchors.Single(a => a.Name == "a").Gap, Is.EqualTo(0.4f), "앞의 것을 쓴다");
            Assert.That(problems.Count, Is.EqualTo(2), string.Join("\n", problems));
            Assert.That(problems[0], Does.Contain("[beat=x/]가 없다").And.Contain("간격을 버린다"));
            Assert.That(problems[1], Does.Contain("<<gap a").And.Contain("둘이다"));
        }

        [Test]
        public void 표지가_없는_at은_end에_붙고_적는다()
        {
            // 번역문이 표지를 빠뜨린 줄(AR9) — 앞 앵커였던 것도 줄 끝으로 간다. 연출이 사라지지 않는다.
            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("a", 3) },
                new[] { At("end", "e1"), At("in", "i1"), At("a", "a1") },
                null,
                problems);

            Assert.That(Names(anchors), Is.EqualTo(new[] { "start", "a", "end" }));
            Assert.That(Tags(anchors[2]), Is.EqualTo(new[] { "e1", "i1" }));
            Assert.That(problems.Single(), Does.Contain("[beat=in/]가 없다"));
        }

        [Test]
        public void start는_글에_둘_수_없는_예약_이름이다()
        {
            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("start", 2) },
                new[] { At("start", "s1") },
                null,
                problems);

            Assert.That(Names(anchors), Is.EqualTo(new[] { "start", "end" }));
            Assert.That(Tags(anchors[0]), Is.EqualTo(new[] { "s1" }), "at start는 본디 start의 것이다 — 표지만 문제다");
            Assert.That(problems.Single(), Does.Contain("[beat=start/]").And.Contain("예약"));
        }

        [Test]
        public void 옛_무대_스텝은_표지보다_앞선_앞_앵커이고_빈_것은_서지_않는다()
        {
            // 옛 표기: 줄 앞에 <<step>>이 셋 — 가운데 것은 명령이 없다(빈 스텝은 스텝이 아니다).
            List<string> problems = new();
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("in", 0) },
                new[]
                {
                    At(LineBeatSchedule.LegacyStepName(0), "p1"),
                    At(LineBeatSchedule.LegacyStepName(2), "p3"),
                    At("in", "i1"),
                    At("start", "s1"),
                    At(LineBeatSchedule.LegacyStepName(0), "p2"),
                },
                null,
                problems,
                legacySteps: 3);

            Assert.That(problems, Is.Empty);
            Assert.That(Names(anchors), Is.EqualTo(new[] { "~0", "~2", "in", "start", "end" }));
            Assert.That(anchors.Select(a => a.Legacy), Is.EqualTo(new[] { true, true, false, false, false }));
            Assert.That(anchors.Take(3).All(a => a.Kind == LineAnchorKind.Pre), Is.True);
            Assert.That(Tags(anchors[0]), Is.EqualTo(new[] { "p1", "p2" }));
        }

        [Test]
        public void 스텝마다의_대사_조각()
        {
            // 글: "[beat=in/]가[beat=a/]나다[beat=b/]라" — in은 앞 앵커, a는 1, b는 3.
            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(
                new[] { new BeatMarker("in", 0), new BeatMarker("a", 1), new BeatMarker("b", 3) },
                new[] { At("in", "i1"), At("a", "a1"), At("b", "b1") });

            string[] segments = Enumerable.Range(0, anchors.Count)
                .Select(i => LineBeatSchedule.Segment("가나다라", anchors, i))
                .ToArray();

            Assert.That(Names(anchors), Is.EqualTo(new[] { "in", "start", "a", "b", "end" }));
            Assert.That(segments, Is.EqualTo(new[] { "", "가", "나다", "라", "" }));

            // 번역문이 더 짧다 — 자리가 글 밖이면 조각은 비고, 남은 글은 앞 스텝의 것이다.
            Assert.That(LineBeatSchedule.Segment("가나", anchors, 2), Is.EqualTo("나"));
            Assert.That(LineBeatSchedule.Segment("가나", anchors, 3), Is.EqualTo(""));
            Assert.That(LineBeatSchedule.Segment(null, anchors, 1), Is.EqualTo(""));
        }

        [Test]
        public void gap의_토큰을_앵커_이름과_길이로_읽는다()
        {
            Assert.That(LineBeatSchedule.TryReadGap(new[] { "a", "12fr" }, "f:3", out GapCommand gap, out string error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(gap.Anchor, Is.EqualTo("a"));
            Assert.That(gap.Seconds, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(gap.Source, Is.EqualTo("f:3"));

            Assert.That(LineBeatSchedule.TryReadGap(new[] { "end", "1.5s" }, "f:4", out gap, out error), Is.True);
            Assert.That(gap.Seconds, Is.EqualTo(1.5f).Within(1e-6f));

            Assert.That(LineBeatSchedule.TryReadGap(new[] { "a" }, "f:5", out _, out error), Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(LineBeatSchedule.TryReadGap(new[] { "a", "soon" }, "f:6", out _, out error), Is.False);
            Assert.That(LineBeatSchedule.TryReadGap(new[] { "a", "1s", "2s" }, "f:7", out _, out error), Is.False);
        }
    }
}
