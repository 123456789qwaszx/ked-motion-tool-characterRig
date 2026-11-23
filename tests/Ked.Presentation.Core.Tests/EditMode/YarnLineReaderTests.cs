using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 줄 읽기(ked-story-tools docs/PLAN.md §4.2 · K2) — yarn 원문과 LineId에서 스텝의 목록을 읽는다.
    /// </summary>
    public sealed class YarnLineReaderTests
    {
        private static string Node(params string[] body)
            => "title: Test\n---\n" + string.Join("\n", body) + "\n===\n";

        // 계획서 §3의 줄 그대로.
        private static readonly string[] SecretLine =
        {
            "<<at in fade_out s1 0.5s>>",
            "<<at in pause 0.5s>>",
            "<<at in fade s2 0.72 0.5s>>",
            "<<gap in 1s>>",
            "<<at start face s2 05>>",
            "<<at a face_swap s2 03 0.3s>>",
            "<<gap a 12fr>>",
            "<<at end emoji s2 sweat>>",
            "세이나: [beat=in/]아, 음…… [beat=a/]이건 비밀인데. #line:s1_secret",
        };

        private static LineRead Read(string yarn, string lineId) => YarnLineReader.Read(yarn, lineId, "test.yarn");

        private static string[] Names(LineReadStep step) => step.Commands.Select(c => c.Name).ToArray();

        [Test]
        public void 줄을_스텝으로_읽는다()
        {
            LineRead line = Read(Node(SecretLine), "line:s1_secret");

            Assert.That(line, Is.Not.Null);
            Assert.That(line.Problems, Is.Empty);
            Assert.That(line.LineId, Is.EqualTo("line:s1_secret"));
            Assert.That(line.NodeName, Is.EqualTo("Test"));
            Assert.That(line.Speaker, Is.EqualTo("세이나"));
            Assert.That(line.Body, Is.EqualTo("아, 음…… 이건 비밀인데."));

            Assert.That(line.Steps.Select(s => s.Name), Is.EqualTo(new[] { "in", "start", "a", "end" }));
            Assert.That(line.Steps.Select(s => s.Kind), Is.EqualTo(new[]
            {
                LineAnchorKind.Pre, LineAnchorKind.Start, LineAnchorKind.Text, LineAnchorKind.End,
            }));
            Assert.That(line.Steps.Select(s => s.Text), Is.EqualTo(new[] { "", "아, 음…… ", "이건 비밀인데.", "" }));
            Assert.That(line.Steps.Select(s => s.Gap), Is.EqualTo(new[] { 1f, 0f, 0.4f, 0f }));
            Assert.That(line.Steps.Select(s => s.HasGap), Is.EqualTo(new[] { true, false, true, false }));

            Assert.That(Names(line.Step("in")), Is.EqualTo(new[] { "fade_out", "pause", "fade" }));
            Assert.That(Names(line.Step("start")), Is.EqualTo(new[] { "face" }));
            Assert.That(Names(line.Step("a")), Is.EqualTo(new[] { "face_swap" }));
            Assert.That(Names(line.Step("end")), Is.EqualTo(new[] { "emoji" }));
        }

        [Test]
        public void 커맨드마다_종류와_칸과_줄을_싣는다()
        {
            LineRead line = Read(Node(SecretLine), "s1_secret");
            LineReadCommand fade = line.Step("in").Commands[2];

            Assert.That(fade.Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(fade.Anchor, Is.EqualTo("in"));
            Assert.That(fade.Line, Is.EqualTo(5), "머리 두 줄(title · ---) 뒤의 셋째 줄");
            Assert.That(fade.Bound.Select(b => b.Spec.Name), Is.EqualTo(new[] { "slot", "alpha", "duration" }));
            Assert.That(fade.Bound.Select(b => b.Value), Is.EqualTo(new[] { "s2", "0.72", "0.5s" }));
            Assert.That(fade.Duration, Is.EqualTo(0.5f));

            // 주소의 마지막 칸 — 그 앵커의 같은 종류 안에서 몇 번째인가.
            Assert.That(line.Step("in").Commands.Select(c => c.KindIndex), Is.EqualTo(new[] { 0, 0, 1 }));
            Assert.That(line.Step("in").Commands.Select(c => c.Kind), Is.EqualTo(new CommandKind?[]
            {
                CommandKind.Placement, CommandKind.Time, CommandKind.Placement,
            }));

            Assert.That(line.Step("a").Commands[0].Kind, Is.EqualTo(CommandKind.Expression), "face_swap은 표정(D13)");
            Assert.That(line.Step("end").Commands[0].Kind, Is.EqualTo(CommandKind.Emoji));
            Assert.That(line.Step("end").Commands[0].Duration, Is.Null, "이모지의 길이는 대본에 없다");
            Assert.That(line.Step("a").GapLine, Is.EqualTo(9));
        }

        [Test]
        public void pause는_뒤_커맨드의_지연으로_남는다()
        {
            LineRead line = Read(Node(SecretLine), "s1_secret");
            LineReadStep step = line.Step("in");

            Assert.That(step.Commands.Select(c => c.Delay), Is.EqualTo(new[] { 0f, 0f, 0.5f }));
            Assert.That(step.Commands[1].Waits, Is.True);
            Assert.That(step.Commands[1].Hold, Is.EqualTo(0.5f));
            Assert.That(step.Settle, Is.EqualTo(0.5f));

            // 종류로 걸러 pause가 빠져도 그 커맨드가 언제 시작하는지는 남는다.
            LineReadCommand[] placement = step.Commands.Where(c => c.Kind == CommandKind.Placement).ToArray();
            Assert.That(placement.Select(c => c.Name), Is.EqualTo(new[] { "fade_out", "fade" }));
            Assert.That(placement.Select(c => c.Delay), Is.EqualTo(new[] { 0f, 0.5f }));
        }

        [Test]
        public void 기다리는_커맨드는_뒤를_붙잡는다()
        {
            LineRead line = Read(Node(
                "<<at in screen_cover black 1s>>",
                "<<at in bg_sprite bg_main night>>",
                "<<at in pause>>",
                "<<at in screen_cover_clear>>",
                "하린: [beat=in/]밤이 됐어. #line:night"), "night");

            LineReadStep step = line.Step("in");

            Assert.That(step.Commands.Select(c => c.Delay), Is.EqualTo(new[] { 0f, 1f, 1f, 1f + YarnLineReader.DefaultPauseSeconds }));
            Assert.That(step.Commands.Select(c => c.Waits), Is.EqualTo(new[] { true, false, true, false }));
            Assert.That(step.Commands[2].Duration, Is.EqualTo(YarnLineReader.DefaultPauseSeconds), "길이를 적지 않은 pause");
            Assert.That(step.Commands[3].Duration, Is.EqualTo(0.5f), "표의 생략값");
            Assert.That(step.Settle, Is.EqualTo(1f + YarnLineReader.DefaultPauseSeconds));
        }

        [Test]
        public void 앵커의_길이는_간격과_기다림의_합_가운데_큰_쪽이다()
        {
            // 간격은 앵커가 열린 때부터 잰다 — 기다림보다 짧은 간격은 길이를 늘리지 않는다.
            LineRead line = Read(Node(
                "<<at in pause 2s>>",
                "<<at in face s1 3>>",
                "<<gap in 1s>>",
                "<<at a pause 0.5s>>",
                "<<at a face s1 5>>",
                "<<gap a 3s>>",
                "하린: [beat=in/]하나 [beat=a/]둘 #line:len"), "len");

            Assert.That(line.Step("in").Length, Is.EqualTo(2f));
            Assert.That(line.Step("a").Length, Is.EqualTo(3f));
            Assert.That(line.Step("start").Length, Is.EqualTo(0f));
        }

        [Test]
        public void 묶음의_자리를_돌려준다()
        {
            string[] body =
            {
                "하린: 앞 줄. #line:before",
                "",
                "// 주석은 묶음 앞에 있으면 묶음이 아니다",
                "<<at start face s1 3>>",
                "",
                "// 묶음 안의 주석",
                "<<at a motion s1 nod>>",
                "하린: 하나 [beat=a/]둘 #line:target",
                "하린: 뒤 줄. #line:after",
            };

            foreach (string newline in new[] { "\n", "\r\n" })
            {
                string yarn = Node(body).Replace("\n", newline);
                LineRead line = Read(yarn, "target");

                Assert.That(line.Block.FirstLine, Is.EqualTo(6));
                Assert.That(line.Block.LastLine, Is.EqualTo(9));
                Assert.That(line.Line, Is.EqualTo(10));

                string block = yarn.Substring(line.Block.Start, line.Block.Length);
                Assert.That(block, Is.EqualTo(string.Join(newline, body.Skip(3).Take(4)) + newline));

                // 그 묶음만 다시 쓰면 다른 곳은 글자 하나 바뀌지 않는다.
                string rewritten = yarn.Remove(line.Block.Start, line.Block.Length).Insert(line.Block.Start, "<<at start face s1 5>>" + newline);
                LineRead again = Read(rewritten, "target");

                Assert.That(again.Step("start").Commands[0].Args[1], Is.EqualTo("5"));
                Assert.That(again.Step("a"), Is.Null);
                Assert.That(Read(rewritten, "before").Body, Is.EqualTo("앞 줄."));
                Assert.That(Read(rewritten, "after").Body, Is.EqualTo("뒤 줄."));
            }
        }

        [Test]
        public void 묶음이_없으면_빈_범위이고_대사_줄의_머리를_가리킨다()
        {
            string yarn = Node("하린: 앞 줄. #line:before", "    하린: 연출 없는 줄. #line:plain");
            LineRead line = Read(yarn, "plain");

            Assert.That(line.Block.IsEmpty, Is.True);
            Assert.That(line.Block.FirstLine, Is.EqualTo(line.Line));
            Assert.That(yarn.Substring(line.Block.Start).StartsWith("    하린: 연출 없는 줄."), Is.True);
            Assert.That(line.Indent, Is.EqualTo("    "));
            Assert.That(line.Steps.Select(s => s.Name), Is.EqualTo(new[] { "start", "end" }), "start · end는 커맨드가 없어도 있다");
        }

        [Test]
        public void LineId로_찾는다()
        {
            YarnFileRead file = YarnLineReader.ReadFile(Node(
                "하린: 하나. #line:one",
                "하린: 태그 없는 줄.",
                "하린: 둘. #auto=1s #line:two",
                "하린: 겹친 줄. #line:one"), "test.yarn");

            Assert.That(file.Lines, Has.Count.EqualTo(4));
            Assert.That(file.TryGet("line:two", out LineRead two), Is.True);
            Assert.That(two.Body, Is.EqualTo("둘."));
            Assert.That(file.TryGet("two", out _), Is.True, "접두사 없이도 찾는다");
            Assert.That(file.TryGet("three", out _), Is.False);
            Assert.That(file.TryGet(null, out _), Is.False);
            Assert.That(file.Lines[1].LineId, Is.Null);

            Assert.That(file.TryGet("one", out LineRead one), Is.True);
            Assert.That(one.Body, Is.EqualTo("하나."), "겹치면 앞의 것");
            Assert.That(file.Problems, Has.Count.EqualTo(1));

            Assert.That(YarnLineReader.Read(Node("하린: 하나. #line:one"), "없는_줄"), Is.Null);
        }

        [Test]
        public void 분기와_선택지가_있는_파일에서도_읽는다()
        {
            string yarn = Node(
                "<<at start face s1 3>>",
                "하린: 어디로 갈까? #line:ask",
                "-> 교실로 #line:opt_class",
                "    <<at start motion s1 nod>>",
                "    하린: 교실이 좋겠어. #line:class",
                "    <<jump Classroom>>",
                "-> 옥상으로 <<if $key>> #line:opt_roof",
                "    <<at in fade_out s1 15fr>>",
                "    <<gap in 1s>>",
                "    하린: [beat=in/]옥상으로 가자. #line:roof",
                "<<if $late>>",
                "    <<at start face s1 5>>",
                "    하린: 늦었네. #line:late",
                "<<else>>",
                "    하린: 아직 괜찮아. #line:early",
                "<<endif>>",
                "<<set $visited to true>>",
                "<<at end emoji s1 star>>",
                "하린: 끝. #line:done");

            YarnFileRead file = YarnLineReader.ReadFile(yarn, "test.yarn");

            Assert.That(file.Problems, Is.Empty);
            Assert.That(file.Lines.Select(l => l.LineId), Is.EqualTo(new[]
            {
                "line:ask", "line:class", "line:roof", "line:late", "line:early", "line:done",
            }), "선택지의 글은 대사 줄이 아니다");

            Assert.That(file.TryGet("class", out LineRead inOption), Is.True);
            Assert.That(Names(inOption.Step("start")), Is.EqualTo(new[] { "motion" }));
            Assert.That(inOption.Indent, Is.EqualTo("    "));

            Assert.That(file.TryGet("roof", out LineRead roof), Is.True);
            Assert.That(roof.Steps.Select(s => s.Name), Is.EqualTo(new[] { "in", "start", "end" }));
            Assert.That(roof.Step("in").Gap, Is.EqualTo(1f));

            Assert.That(file.TryGet("late", out LineRead late), Is.True);
            Assert.That(Names(late.Step("start")), Is.EqualTo(new[] { "face" }));

            Assert.That(file.TryGet("early", out LineRead early), Is.True);
            Assert.That(early.Block.IsEmpty, Is.True);

            // Yarn의 문(set)은 묶음을 끊지 않는다 — 앵커 밖의 것으로 싣는다.
            Assert.That(file.TryGet("done", out LineRead done), Is.True);
            Assert.That(Names(done.Step("end")), Is.EqualTo(new[] { "emoji" }));
            Assert.That(done.Immediate.Select(c => c.Name), Is.EqualTo(new[] { "set" }));
            Assert.That(done.Problems, Is.Empty);
            Assert.That(done.Block.FirstLine, Is.EqualTo(done.Line - 2));
        }

        [Test]
        public void 분기_앞에_남은_at은_파일의_문제다()
        {
            YarnFileRead file = YarnLineReader.ReadFile(Node(
                "<<at start face s1 3>>",
                "<<gap start 1s>>",
                "<<if $late>>",
                "    하린: 늦었네. #line:late",
                "<<endif>>",
                "<<at start face s1 5>>",
                "-> 선택지 #line:opt",
                "    하린: 골랐어. #line:picked",
                "<<at end emoji s1 star>>"), "test.yarn");

            Assert.That(file.TryGet("late", out LineRead late), Is.True);
            Assert.That(late.Block.IsEmpty, Is.True, "분기 앞의 at은 이 줄의 것이라고 읽지 않는다");
            Assert.That(late.Step("start").Commands, Is.Empty);

            // 분기 앞의 at · gap, 선택지 앞의 at, 노드 끝의 at.
            Assert.That(file.Problems, Has.Count.EqualTo(4));
            Assert.That(file.Problems[0], Does.Contain("test.yarn:5"));
            Assert.That(file.Problems[0], Does.Contain("분기"));
        }

        [Test]
        public void at_없는_명령은_앵커에_넣지_않고_오류로_적는다()
        {
            // 재생은 실행하지 않는다(docs/PLAN_line-anchor.md §9 N5) — 줄 읽기도 앵커에 넣지 않는다.
            LineRead line = Read(Node(
                "<<fade_out s1 15fr>>",
                "<<step>>",
                "<<at start motion s2 nod>>",
                "시구레: 자리를 바꿨어요. #line:swap"), "swap");

            Assert.That(line.Steps.Select(s => s.Name), Is.EqualTo(new[] { "start", "end" }));
            Assert.That(Names(line.Step("start")), Is.EqualTo(new[] { "motion" }));

            Assert.That(line.Unanchored.Select(c => c.Name), Is.EqualTo(new[] { "fade_out", "step" }));
            Assert.That(line.Unanchored.Select(c => c.Line), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(line.Unanchored[0].Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(line.Unanchored[0].Anchor, Is.Null);
            Assert.That(line.Unanchored[1].Kind, Is.Null, "걷은 <<step>>은 명세 표에 없다");

            // at 없는 명령 둘 + 표에 없는 명령 하나(step).
            Assert.That(line.Problems.Count(p => p.Contains("at 없는 명령")), Is.EqualTo(2), string.Join("\n", line.Problems));
            Assert.That(line.Problems, Has.Count.EqualTo(3));
            Assert.That(line.Block.FirstLine, Is.EqualTo(3), "묶음의 범위에는 든다");
        }

        [Test]
        public void 바로_실행하는_명령은_앵커에_넣지_않는다()
        {
            LineRead line = Read(Node(
                "<<surface_layout wide>>",
                "<<at start face s1 3>>",
                "하린: 대사창이 넓어졌어. #line:wide"), "wide");

            // 브리지가 쌓지 않고 Yarn이 만난 때 실행한다 — start의 명령이 아니고, at 없는 명령의 오류도 아니다.
            Assert.That(Names(line.Step("start")), Is.EqualTo(new[] { "face" }));
            Assert.That(line.Immediate.Select(c => c.Name), Is.EqualTo(new[] { "surface_layout" }));
            Assert.That(line.Immediate[0].Anchor, Is.Null);
            Assert.That(line.Immediate[0].Kind, Is.EqualTo(CommandKind.Setup));
            Assert.That(line.Immediate[0].Line, Is.EqualTo(3));
            Assert.That(line.Unanchored, Is.Empty);
            Assert.That(line.Problems, Is.Empty);
            Assert.That(line.Block.FirstLine, Is.EqualTo(3), "묶음의 범위에는 든다");
        }

        [Test]
        public void 바로_실행하는_명령에_at을_붙이면_문제다()
        {
            LineRead line = Read(Node(
                "<<at start surface_reset>>",
                "하린: 되돌렸어. #line:reset"), "reset");

            Assert.That(line.Problems, Has.Count.EqualTo(1));
            Assert.That(line.Problems[0], Does.Contain("at을 붙이지 않는다"));
        }

        [Test]
        public void 표에_없는_명령은_종류_없이_싣고_적는다()
        {
            string yarn = Node(
                "<<at start cutin_panel stage01 wide>>",
                "<<messenger_open>>",
                "하린: 포크의 어휘. #line:fork");

            LineRead plain = Read(yarn, "fork");

            // 공개 표는 포크의 어휘를 모른다 — at 없는 messenger_open은 오류이고, 둘 다 표에 없다.
            Assert.That(plain.Step("start").Commands.Select(c => c.Name), Is.EqualTo(new[] { "cutin_panel" }));
            Assert.That(plain.Step("start").Commands[0].Kind, Is.Null);
            Assert.That(plain.Step("start").Commands[0].KindIndex, Is.EqualTo(-1));
            Assert.That(plain.Unanchored.Select(c => c.Name), Is.EqualTo(new[] { "messenger_open" }));
            Assert.That(plain.Problems, Has.Count.EqualTo(3), string.Join("\n", plain.Problems));

            // 포크가 제 어휘를 덧붙인 표로 읽으면 가려진다.
            StageCommandSpecTable fork = StageCommandSpecTable.Core.With(new[]
            {
                new StageCommandSpec("cutin_panel", CommandKind.Screen, "", new[]
                {
                    new CommandArgSpec("stage", "무대", ArgKind.Name, "stage01", catalog: "stage"),
                    new CommandArgSpec("panel", "판", ArgKind.Name, catalog: "cutin_panel"),
                }),
                new StageCommandSpec("messenger_open", CommandKind.Setup, "", immediate: true),
            });

            LineRead read = YarnLineReader.Read(yarn, "fork", "test.yarn", fork);

            Assert.That(read.Problems, Is.Empty);
            Assert.That(read.Step("start").Commands.Select(c => c.Name), Is.EqualTo(new[] { "cutin_panel" }));
            Assert.That(read.Step("start").Commands[0].Kind, Is.EqualTo(CommandKind.Screen));
            Assert.That(read.Immediate.Select(c => c.Name), Is.EqualTo(new[] { "messenger_open" }));
            Assert.That(read.Unanchored, Is.Empty);
        }

        [Test]
        public void 짝이_없는_at은_end에서_읽고_적는다()
        {
            LineRead line = Read(Node(
                "<<at b motion s1 nod>>",
                "<<at end emoji s1 star>>",
                "<<gap c 1s>>",
                "하린: 표지가 없는 줄. #line:orphan"), "orphan");

            // 재생이 하는 대로다(AR9) — 연출이 사라지지 않게 end에서 실행한다.
            Assert.That(Names(line.Step("end")), Is.EqualTo(new[] { "motion", "emoji" }));
            Assert.That(line.Step("end").Commands.Select(c => c.Line), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(line.Problems, Has.Count.EqualTo(2));
        }

        [Test]
        public void 예제_대본을_추출기와_같은_차례로_읽는다()
        {
            // 선형인 대본에서는 추출기(접힘)와 줄 읽기가 같은 커맨드 열을 낸다.
            string dir = Path.Combine("Assets", "Dialogue", "ChapterProgression");

            if (!Directory.Exists(dir))
                Assert.Ignore("예제 대본이 없는 저장소다(분리본)");

            int lines = 0;

            foreach (string path in Directory.GetFiles(dir, "*.yarn").OrderBy(p => p))
            {
                string yarn = File.ReadAllText(path);
                List<string> warnings = new();
                List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, path, warnings)
                    .Where(g => g.LineText != null).ToList();

                YarnFileRead file = YarnLineReader.ReadFile(yarn, path);

                // 분기 · 선택지가 있는 파일도 터지지 않는다. 차례의 대조는 선형인 파일에서만 한다.
                if (warnings.Any(w => w.Contains("선형 파일 전용")) || yarn.Contains("<<detour"))
                    continue;

                Assert.That(file.Lines.Count, Is.EqualTo(groups.Count), path);

                for (int i = 0; i < groups.Count; i++)
                {
                    string[] folded = groups[i].Commands.Select(c => c.ToString()).ToArray();
                    string[] read = file.Lines[i].Steps
                        .SelectMany(s => s.Commands)
                        .Select(c => new StageCommand(c.Name, c.Args, $"{path}:{c.Line}").ToString()).ToArray();

                    Assert.That(read, Is.EqualTo(folded), $"{path}:{file.Lines[i].Line}");
                    Assert.That(file.Lines[i].Body, Is.EqualTo(groups[i].Body));
                    Assert.That(file.Lines[i].LineId, Is.EqualTo(groups[i].LineId));
                    lines++;
                }
            }

            Assert.That(lines, Is.GreaterThan(0));
        }
    }
}
