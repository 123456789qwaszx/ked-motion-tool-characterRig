using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 하네스 부품 둘: 원문 추출기와 상태 비교기.
    ///
    /// 원문 표본은 실제 에피소드 모양 그대로다 — Setup 노드 + Story 노드,
    /// 준비 커맨드(노드 끝) / 줄의 명령(<<at>>) / 대사 / 꼬리 커맨드, 주석, BOM, 태그 없음.
    /// </summary>
    public sealed class EquivalenceHarnessCoreTests
    {
        // 실제 Story_StellaEvent01_01.yarn의 구조를 축소한 것.
        private const string SampleYarn =
            "﻿title: Setup_Sample\n" +
            "---\n" +
            "\n" +
            "<<bg_spawn bg1 fl5>>\n" +
            "<<slot c1>>\n" +
            "\n" +
            "=== \n" +
            "\n" +
            "title: Story_Sample\n" +
            "---\n" +
            "\n" +
            "<<at start beat Setup_Sample>>\n" +
            "<<at start cast c1 bandi a 3>>\n" +
            "<<at start actor @4 c1>>\n" +
            "//<<at start mirror @4>>\n" +
            "<<at start right @4 2u 24fr>>\n" +
            "\n" +
            "첫 번째 대사다.\n" +
            "\n" +
            "<<at start left @4 3u>>\n" +
            "<<at start fade_in @4>>\n" +
            "\n" +
            "두 번째 대사다.\n" +
            "\n" +
            "<<shot_reset>>\n" +
            "===\n";

        private static List<YarnNodeGroups> ExtractSample(List<string> warnings = null)
            => YarnCommandTextExtractor.ExtractNodes(SampleYarn, "sample.yarn", warnings);

        // ── 추출기: 노드 분리 ────────────────────────────────────────

        [Test]
        public void 한_파일의_Setup과_Story_노드를_나눈다()
        {
            List<YarnNodeGroups> nodes = ExtractSample();

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[0].NodeName, Is.EqualTo("Setup_Sample"));
            Assert.That(nodes[1].NodeName, Is.EqualTo("Story_Sample"));
        }

        [Test]
        public void BOM이_노드_이름을_더럽히지_않는다()
        {
            // 파일 선두 BOM은 공백이 아니라 Trim에 안 걸린다.
            Assert.That(ExtractSample()[0].NodeName, Is.EqualTo("Setup_Sample"));
        }

        [Test]
        public void 대사가_없는_노드는_꼬리_그룹으로만_남는다()
        {
            YarnNodeGroups setup = ExtractSample()[0];

            Assert.That(setup.Groups.Count, Is.EqualTo(1));
            Assert.That(setup.Groups[0].LineText, Is.Null, "꼬리 그룹은 대사가 없다");
            Assert.That(setup.Groups[0].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "bg_spawn", "slot" }));
        }

        // ── 추출기: 라인 경계 ────────────────────────────────────────

        [Test]
        public void 커맨드는_다음_대사까지의_그룹에_모인다()
        {
            YarnNodeGroups story = ExtractSample()[1];

            // 대사 2개 + 꼬리 1개.
            Assert.That(story.Groups.Count, Is.EqualTo(3));

            Assert.That(story.Groups[0].LineText, Is.EqualTo("첫 번째 대사다."));
            Assert.That(story.Groups[0].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "beat", "cast", "actor", "right" }));

            Assert.That(story.Groups[1].LineText, Is.EqualTo("두 번째 대사다."));
            Assert.That(story.Groups[1].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "left", "fade_in" }));

            Assert.That(story.Groups[2].LineText, Is.Null);
            Assert.That(story.Groups[2].Commands.Select(c => c.Name), Is.EqualTo(new[] { "shot_reset" }));
        }

        [Test]
        public void at_없는_명령은_대사_줄_앞이면_오류이고_접지_않는다()
        {
            // 재생과 같다 — 실행하지 않는다(docs/PLAN_line-anchor.md §9 N5).
            const string yarn =
                "title: Unanchored\n" +
                "---\n" +
                "<<at start fade_in c1>>\n" +
                "<<face c1 02>>\n" +
                "<<step>>\n" +
                "대사다.\n" +
                "===\n";

            List<string> warnings = new();
            YarnLineGroup line = YarnCommandTextExtractor.Extract(yarn, "unanchored.yarn", warnings).Single();

            Assert.That(line.Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_in" }));
            Assert.That(line.Anchors.Select(a => a.Name), Is.EqualTo(new[] { "start", "end" }));

            // 걷은 <<step>>도 at 없는 명령의 하나로 읽힌다 — 런타임에 등록이 없다.
            Assert.That(warnings.Count, Is.EqualTo(2), string.Join("\n", warnings));
            Assert.That(warnings[0], Does.StartWith("unanchored.yarn:4 <<face c1 02>>").And.Contain("at 없는 명령"));
            Assert.That(warnings[1], Does.StartWith("unanchored.yarn:5 <<step>>"));
        }

        [Test]
        public void 노드_끝의_at_없는_명령은_꼬리_그룹이다()
        {
            // 붙을 줄이 없이 남은 명령 — 노드 끝의 암묵 스텝 하나가 쓴 순서로 실행한다(A6).
            const string yarn =
                "title: Tail_Sample\n" +
                "---\n" +
                "대사다.\n" +
                "<<fade_out c2>>\n" +
                "<<pause 0.5s>>\n" +
                "<<bg_fade_out bg 12fr>>\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "tail.yarn", warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[1].LineText, Is.Null, "노드 끝의 명령은 꼬리 그룹이다");
            Assert.That(groups[1].Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_out", "pause", "bg_fade_out" }));
        }

        [Test]
        public void 바로_실행하는_명령과_Yarn의_문은_at_없이_쓰고_접지_않는다()
        {
            const string yarn =
                "title: Immediate\n" +
                "---\n" +
                "<<surface_layout wide>>\n" +
                "<<set $a to 1>>\n" +
                "<<at start face c1 02>>\n" +
                "대사다.\n" +
                "<<surface_reset>>\n" +
                "<<wait 1>>\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "immediate.yarn", warnings);

            Assert.That(warnings, Is.Empty, string.Join("\n", warnings));
            Assert.That(groups.Count, Is.EqualTo(1), "무대 명령이 없는 노드 끝은 꼬리가 아니다");
            Assert.That(groups[0].Commands.Select(c => c.Name), Is.EqualTo(new[] { "face" }));
        }

        [Test]
        public void 표지가_아닌_대괄호는_글로_남는다()
        {
            List<BeatMarker> markers = new();

            string text = YarnCommandTextExtractor.StripLineMarkers(
                "[속삭임] 이건 [b]굵게[/b], [beat=a/]여기[beatbox=1/], [wave]물결[/wave][beat=b/].", markers);

            Assert.That(text, Is.EqualTo("[속삭임] 이건 [b]굵게[/b], 여기[beatbox=1/], [wave]물결[/wave]."));
            Assert.That(markers.Select(m => m.Name), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(markers[0].Position, Is.EqualTo("[속삭임] 이건 [b]굵게[/b], ".Length), "표지를 뺀 글의 자리");
        }

        [TestCase("[beat=a/]", "a")]
        [TestCase("[beat=\"b2\"/]", "b2")]
        [TestCase("[ beat = c / ]", "c")]
        public void 표지_이름은_따옴표가_있어도_없어도_읽는다(string marker, string name)
        {
            List<BeatMarker> markers = new();

            string text = YarnCommandTextExtractor.StripLineMarkers($"앞 {marker}뒤", markers);

            Assert.That(text, Is.EqualTo("앞 뒤"));
            Assert.That(markers.Single().Name, Is.EqualTo(name));
            Assert.That(markers.Single().Position, Is.EqualTo(2));
        }

        // ── 추출기: <<at>> · [beat] (docs/PLAN_line-beat.md R1 · R3) ───────────

        [Test]
        public void at_명령은_머리_명령_뒤에_표지_순서로_접히고_end가_마지막이다()
        {
            // <<at>>가 머리 명령 사이 · 앞에 쓰여도 머리 뒤에 접힌다 — 재생에서 머리는 줄이 들어올 때, 표지는 그 글자에서다.
            const string yarn =
                "title: At_Sample\n" +
                "---\n" +
                "<<at end emoji s2 sweat>>\n" +
                "<<at b face s2 06>>\n" +
                "<<at start face s2 05>>\n" +
                "<<at a face_swap s2 03 0.3s>>\n" +
                "<<at a motion s2 a008_s2>>\n" +
                "<<at start fade_in s2>>\n" +
                "세이나: 아, 음…… [beat=a/]이건 [beat=b/]비밀인데. #line:ln_at\n" +
                "<<at start fade_out s2>>\n" +
                "다음 대사다.\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "at.yarn", warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].LineText, Is.EqualTo("세이나: 아, 음…… 이건 비밀인데."));
            Assert.That(groups[0].LineId, Is.EqualTo("line:ln_at"));
            Assert.That(groups[0].Commands.Select(c => c.Name),
                Is.EqualTo(new[] { "face", "fade_in", "face_swap", "motion", "face", "emoji" }));
            Assert.That(groups[0].Commands[2].Args, Is.EqualTo(new[] { "s2", "03", "0.3s" }));
            Assert.That(groups[0].Commands[2].Source, Is.EqualTo("at.yarn:6"), "at 명령의 출처는 <<at>>를 쓴 행");

            Assert.That(groups[1].Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_out" }), "at은 다음 줄로 새지 않는다");
        }

        [Test]
        public void 선택지_앞의_at은_고른_갈래의_첫_줄의_것이다()
        {
            // 재생과 같다 — 쌓인 <<at>>은 다음에 오는 대사 줄이 가져간다.
            const string yarn =
                "title: Stray\n" +
                "---\n" +
                "<<at a face s1 02>>\n" +
                "<<at start fade_in s1>>\n" +
                "-> 고른다\n" +
                "다음 [beat=a/]대사다.\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "stray.yarn", warnings);

            Assert.That(warnings.Single(), Does.Contain("옵션"));
            Assert.That(groups.Single().Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_in", "face" }));
        }

        [Test]
        public void 노드_끝의_at은_경고하고_꼬리_그룹에_남는다()
        {
            const string yarn =
                "title: Tail\n" +
                "---\n" +
                "대사다.\n" +
                "<<fade_out s1>>\n" +
                "<<at a face s1 02>>\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "tail.yarn", warnings);

            Assert.That(warnings.Any(w => w.Contains("노드 끝")), Is.True);
            Assert.That(groups.Last().LineText, Is.Null);
            Assert.That(groups.Last().Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_out", "face" }));
        }

        [Test]
        public void 표지가_없는_at은_경고하고_줄_끝에서_접힌다()
        {
            const string yarn =
                "title: Missing\n" +
                "---\n" +
                "<<at b face s1 02>>\n" +
                "<<at a face s1 03>>\n" +
                "하린: [beat=a/]여기.\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "missing.yarn", warnings);

            Assert.That(warnings.Single(), Does.Contain("[beat=b/]가 없다").And.Contain("missing.yarn:5"));
            Assert.That(groups[0].Commands.Select(c => c.Args[1]), Is.EqualTo(new[] { "03", "02" }));
        }

        // ── 추출기: 줄의 앵커 (docs/PLAN_line-anchor.md A3–A8 · AR1 · AR10) ─────

        [Test]
        public void 새_표기의_줄은_앵커로_나뉘고_그_차례로_접힌다()
        {
            const string yarn =
                "title: Anchor_Sample\n" +
                "---\n" +
                "<<at end emoji s2 sweat>>\n" +
                "<<at in fade_out s1 0.5s>>\n" +
                "<<at a face_swap s2 03 0.3s>>\n" +
                "<<at in pause 0.5s>>\n" +
                "<<gap in 1s>>\n" +
                "<<at start face s2 05>>\n" +
                "<<gap a 12fr>>\n" +
                "세이나: [beat=in/]아, 음…… [beat=a/]이건 비밀인데. #line:ln_anchor\n" +
                "===\n";

            List<string> warnings = new();
            YarnNodeGroups node = YarnCommandTextExtractor.ExtractNodes(yarn, "anchor.yarn", warnings).Single();
            YarnLineGroup line = node.Groups.Single();

            Assert.That(warnings, Is.Empty, "앞 앵커의 pause는 된다");

            Assert.That(line.LineText, Is.EqualTo("세이나: 아, 음…… 이건 비밀인데."));
            Assert.That(line.Body, Is.EqualTo("아, 음…… 이건 비밀인데."));
            Assert.That(line.Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_out", "pause", "face", "face_swap", "emoji" }));

            // 물음 1 — 이 줄은 몇 스텝인가. 물음 2 — 스텝마다 어떤 명령과 어떤 대사인가.
            Assert.That(line.Anchors.Select(a => a.Name), Is.EqualTo(new[] { "in", "start", "a", "end" }));
            Assert.That(line.Anchors.Select(a => a.Kind), Is.EqualTo(new[]
            {
                LineAnchorKind.Pre, LineAnchorKind.Start, LineAnchorKind.Text, LineAnchorKind.End,
            }));
            Assert.That(line.Anchors[0].Gap, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(line.Anchors[2].Gap, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(line.Anchors[0].Commands.Select(c => c.Command.Name), Is.EqualTo(new[] { "fade_out", "pause" }));
            Assert.That(
                Enumerable.Range(0, line.Anchors.Count).Select(i => LineBeatSchedule.Segment(line.Body, line.Anchors, i)),
                Is.EqualTo(new[] { "", "아, 음…… ", "이건 비밀인데.", "" }));
        }

        [Test]
        public void 첫_글자_앞의_표지는_화자가_있어도_없어도_앞_앵커다()
        {
            const string yarn =
                "title: Pre\n" +
                "---\n" +
                "<<at in fade_in s1>>\n" +
                "<<at a face s1 02>>\n" +
                "하린: [beat=in/]여[beat=a/]기.\n" +
                "<<at in fade_out s1>>\n" +
                "<<at z face s1 03>>\n" +
                "[beat=in/]화자 없는 글[beat=z/]\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "pre.yarn", warnings);

            Assert.That(warnings, Is.Empty);

            Assert.That(groups[0].Anchors.Select(a => a.Kind), Is.EqualTo(new[]
            {
                LineAnchorKind.Pre, LineAnchorKind.Start, LineAnchorKind.Text, LineAnchorKind.End,
            }));
            Assert.That(groups[0].Anchors[2].Position, Is.EqualTo(1), "화자를 뺀 글의 자리");
            Assert.That(groups[0].Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_in", "face" }));

            Assert.That(groups[1].Body, Is.EqualTo("화자 없는 글"));
            Assert.That(groups[1].Anchors.Select(a => a.Name), Is.EqualTo(new[] { "in", "start", "z", "end" }));
            Assert.That(groups[1].Anchors[2].Position, Is.EqualTo("화자 없는 글".Length), "글 끝의 표지는 글 표지다");
        }

        [Test]
        public void gap_다음이_대사_줄이_아니면_경고하고_버린다()
        {
            const string yarn =
                "title: StrayGap\n" +
                "---\n" +
                "대사다.\n" +
                "<<fade_out s1>>\n" +
                "<<gap start 1s>>\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "straygap.yarn", warnings);

            Assert.That(warnings.Single(), Does.Contain("<<gap>>").And.Contain("노드 끝"));
            Assert.That(groups.Last().Commands.Select(c => c.Name), Is.EqualTo(new[] { "fade_out" }));
            Assert.That(groups.Last().Anchors, Is.Empty, "꼬리 그룹에는 앵커가 없다");
        }

        [Test]
        public void 꼴이_틀린_gap과_앵커가_없는_gap은_경고한다()
        {
            const string yarn =
                "title: BadGap\n" +
                "---\n" +
                "<<gap start>>\n" +
                "<<gap x 1s>>\n" +
                "하린: 여기.\n" +
                "===\n";

            List<string> warnings = new();
            YarnLineGroup line = YarnCommandTextExtractor.Extract(yarn, "badgap.yarn", warnings).Single();

            Assert.That(warnings.Count, Is.EqualTo(2), string.Join("\n", warnings));
            Assert.That(warnings[0], Does.StartWith("badgap.yarn:3 ").And.Contain("<<gap 이름 길이>>"));
            Assert.That(warnings[1], Does.StartWith("badgap.yarn:5 ").And.Contain("[beat=x/]가 없다"));
            Assert.That(line.Anchors.All(a => a.Gap == 0f), Is.True);
        }

        [TestCase("세이나: 아, 음", 5)]
        [TestCase("세이나:아", 4)]
        [TestCase("아, 음……", 0)]
        [TestCase("09:15|운월", 3)]
        [TestCase("값은 3\\: 2다", 0)]
        [TestCase("[b]굵게[/b]: 글", 0)]
        [TestCase(": 글", 0)]
        [TestCase("", 0)]
        public void 화자_이름의_길이(string plain, int length)
        {
            Assert.That(YarnCommandTextExtractor.CharacterPrefixLength(plain), Is.EqualTo(length));
        }

        // ── 추출기: 기다리는 명령은 어느 앵커에서나 (docs/PLAN_line-anchor.md A7 — line-beat D3 · R8을 바꿨다) ─────

        [Test]
        public void 기다리는_명령은_어느_앵커에서나_쓰고_경고하지_않는다()
        {
            const string yarn =
                "title: Waits\n" +
                "---\n" +
                "<<at in fade_out s1>>\n" +
                "<<at in pause 12fr>>\n" +
                "<<at in screen_cover black 0.5s>>\n" +
                "<<at start screen_cover_clear 0.5s>>\n" +
                "<<at start pause 0.3s>>\n" +
                "<<at start face s1 02>>\n" +
                "<<at a pause 1s>>\n" +
                "<<at a face s1 03>>\n" +
                "<<at end screen_cover white 0.2s>>\n" +
                "하린: [beat=in/]여[beat=a/]기.\n" +
                "===\n";

            List<string> warnings = new();
            YarnLineGroup line = YarnCommandTextExtractor.Extract(yarn, "waits.yarn", warnings).Single();

            Assert.That(warnings, Is.Empty, string.Join("\n", warnings));

            // 폴드는 그대로 접는다 — pause는 접을 것이 없다.
            Assert.That(line.Commands.Select(c => c.Name), Is.EqualTo(new[]
            {
                "fade_out", "pause", "screen_cover", "screen_cover_clear", "pause", "face", "pause", "face", "screen_cover",
            }));
            Assert.That(line.Anchors.Select(a => a.Name), Is.EqualTo(new[] { "in", "start", "a", "end" }));
        }

        [TestCase("pause", true)]
        [TestCase("screen_cover", true)]
        [TestCase("screen_cover_clear", false)]
        [TestCase("24fr", false)]
        [TestCase("face", false)]
        public void 기다리는_명령의_이름(string name, bool waiting)
        {
            Assert.That(YarnCommandTextExtractor.IsWaitingCommand(name), Is.EqualTo(waiting));
        }

        [Test]
        public void 명령이_없는_at은_경고하고_버린다()
        {
            const string yarn =
                "title: Bare\n" +
                "---\n" +
                "<<at a>>\n" +
                "하린: [beat=a/]여기.\n" +
                "===\n";

            List<string> warnings = new();
            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(yarn, "bare.yarn", warnings);

            Assert.That(warnings.Any(w => w.Contains("bare.yarn:3") && w.Contains("<<at 표지 명령")), Is.True, string.Join("\n", warnings));
            Assert.That(groups[0].Commands, Is.Empty);
        }

        [Test]
        public void 주석_처리된_커맨드는_빠진다()
        {
            YarnNodeGroups story = ExtractSample()[1];

            Assert.That(story.Groups[0].Commands.Any(c => c.Name == "mirror"), Is.False);
        }

        [Test]
        public void 출처에_파일명과_행_번호가_실린다()
        {
            // Unhandled 목록을 사람이 열어 보고 고칠 수 있어야 한다.
            StageCommand cast = ExtractSample()[1].Groups[0].Commands[1];

            Assert.That(cast.Name, Is.EqualTo("cast"));
            Assert.That(cast.Source, Does.StartWith("sample.yarn:"));
        }

        [Test]
        public void 인자가_순서대로_실린다()
        {
            StageCommand move = ExtractSample()[1].Groups[0].Commands[3];

            Assert.That(move.Name, Is.EqualTo("right"));
            Assert.That(move.Args, Is.EqualTo(new[] { "@4", "2u", "24fr" }));
        }

        // ── 추출기: 라인 ID ──────────────────────────────────────────

        [Test]
        public void 이_프로젝트_원문에는_라인_ID가_없다()
        {
            // 실측: 8개 yarn 파일 전부 #line 태그 0건.
            // 하네스는 ID 매칭이 아니라 순서 커서로 가야 한다.
            foreach (YarnLineGroup group in ExtractSample().SelectMany(n => n.Groups))
                Assert.That(group.LineId, Is.Null);
        }

        [Test]
        public void 라인_ID는_접두사를_포함한_전체다()
        {
            // Yarn 런타임의 라인 ID가 "line:316f20c1" 형태다 —
            // 접두사를 벗겨 저장하면 하네스의 시간표 조회가 전 라인에서 빗나간다.
            const string tagged =
                "title: N\n---\n대사입니다. #line:316f20c1\n===\n";

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(tagged, "t.yarn");

            Assert.That(groups[0].LineId, Is.EqualTo("line:316f20c1"));
            Assert.That(groups[0].LineText, Is.EqualTo("대사입니다."), "태그는 대사에서 빠진다");
        }

        // ── 추출기: 커맨드 파싱 ──────────────────────────────────────

        [Test]
        public void 따옴표_인자는_한_덩어리다()
        {
            Assert.That(
                YarnCommandTextExtractor.TryParseCommand("<<say c1 \"안녕 하세요\" 2s>>", "s:1",
                    out StageCommand command),
                Is.True);

            Assert.That(command.Args, Is.EqualTo(new[] { "c1", "안녕 하세요", "2s" }));
        }

        [Test]
        public void 커맨드가_아닌_줄은_거부한다()
        {
            Assert.That(YarnCommandTextExtractor.TryParseCommand("대사입니다", "s:1", out _), Is.False);
            Assert.That(YarnCommandTextExtractor.TryParseCommand("<<>>", "s:1", out _), Is.False);
            Assert.That(YarnCommandTextExtractor.TryParseCommand("<<미완성", "s:1", out _), Is.False);
        }

        [Test]
        public void 인자_없는_커맨드도_읽는다()
        {
            // 실제 원문에 <<36fr>> 같은 것이 있다 — 리듀서가 Unhandled로 처리할 몫이다.
            Assert.That(
                YarnCommandTextExtractor.TryParseCommand("<<36fr>>", "s:1", out StageCommand command),
                Is.True);

            Assert.That(command.Name, Is.EqualTo("36fr"));
            Assert.That(command.Args, Is.Empty);
        }

        // ── 추출기: 선형성 경고 ──────────────────────────────────────

        [Test]
        public void 분기와_옵션은_경고로_소리를_낸다()
        {
            const string branching =
                "title: N\n---\n" +
                "<<if $flag>>\n" +
                "<<jump Other>>\n" +
                "-> 선택지\n" +
                "대사.\n" +
                "===\n";

            List<string> warnings = new();
            YarnCommandTextExtractor.Extract(branching, "b.yarn", warnings);

            // 이 파일은 이 추출기로 접으면 안 된다는 신호다.
            Assert.That(warnings.Count(w => w.Contains("분기/제어")), Is.EqualTo(2));
            Assert.That(warnings.Count(w => w.Contains("옵션")), Is.EqualTo(1));
        }

        [Test]
        public void set은_경고하지_않고_접지_않는다()
        {
            // 무대 상태와 무관하고 분기하지도 않는다 — at 없이 쓰는 것이 맞다.
            List<string> warnings = new();

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(
                "title: N\n---\n<<set $a to 1>>\n대사.\n===\n", "s.yarn", warnings);

            Assert.That(warnings, Is.Empty);
            Assert.That(groups[0].Commands, Is.Empty);
        }

        // ── 비교기 ───────────────────────────────────────────────────

        private static StageState StateWith(Vec2 position, float alpha = 1f)
        {
            StageState state = new(RectSpace.Centered(1920f, 1080f));
            state.Nodes.Add("c1/Track", null, RectNodeState.StretchFull.WithAnchoredPosition(position));
            state.SetAlpha("c1/Track", alpha);
            return state;
        }

        [Test]
        public void 같은_상태는_등가다()
        {
            StageStateComparer.Result result =
                StageStateComparer.Compare(StateWith(new Vec2(10f, 20f)), StateWith(new Vec2(10f, 20f)));

            Assert.That(result.IsEquivalent, Is.True, result.ToString());
            Assert.That(result.ComparedNodes, Is.EqualTo(1));
        }

        [Test]
        public void 위치_ε_경계에서_갈린다()
        {
            // 0.05px는 잡음, 0.2px는 신호. ε = 0.1px가 그 사이에 있다.
            Assert.That(
                StageStateComparer.Compare(
                    StateWith(new Vec2(10f, 20f)), StateWith(new Vec2(10.05f, 20f))).IsEquivalent,
                Is.True, "0.05px는 등가");

            Assert.That(
                StageStateComparer.Compare(
                    StateWith(new Vec2(10f, 20f)), StateWith(new Vec2(10.2f, 20f))).IsEquivalent,
                Is.False, "0.2px는 불일치");
        }

        [Test]
        public void alpha도_비교한다()
        {
            StageStateComparer.Result result = StageStateComparer.Compare(
                StateWith(Vec2.Zero, alpha: 1f), StateWith(Vec2.Zero, alpha: 0f));

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches[0], Does.Contain("alpha"));
        }

        [Test]
        public void 각도는_360도_순환으로_본다()
        {
            StageState folded = new(RectSpace.Centered(1920f, 1080f));
            folded.Nodes.Add("n", null, RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 0f)));

            StageState captured = new(RectSpace.Centered(1920f, 1080f));
            captured.Nodes.Add("n", null, RectNodeState.StretchFull.WithLocalEuler(new Vec3(0f, 0f, 360f)));

            Assert.That(StageStateComparer.Compare(folded, captured).IsEquivalent, Is.True,
                "유니티가 오일러를 [0,360)으로 정규화해 돌려주기도 한다");
        }

        [Test]
        public void 캡처에만_있는_노드는_불일치다()
        {
            // 실제 무대에 있는데 접지 못한 노드 — 폴드에 빠진 축이다.
            StageState folded = new(RectSpace.Centered(1920f, 1080f));

            StageStateComparer.Result result = StageStateComparer.Compare(folded, StateWith(Vec2.Zero));

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches[0], Does.Contain("캡처에는 있는데"));
        }

        [Test]
        public void 접힘_전용_노드는_개수로_보고한다()
        {
            // 캡처가 안 덮는 축이다. 침묵하지 않되 불일치로 세지도 않는다.
            StageState captured = new(RectSpace.Centered(1920f, 1080f));

            StageStateComparer.Result result = StageStateComparer.Compare(StateWith(Vec2.Zero), captured);

            Assert.That(result.IsEquivalent, Is.True);
            Assert.That(result.FoldOnlyNodes, Is.EqualTo(1));
            Assert.That(result.ComparedNodes, Is.EqualTo(0));
        }

        [Test]
        public void 샷_축도_비교한다()
        {
            StageState folded = new(RectSpace.Centered(1920f, 1080f))
            {
                Shot = new ShotIntentState(3f, new Vec2(80f, 0f), Vec2.Zero),
            };

            StageState captured = new(RectSpace.Centered(1920f, 1080f))
            {
                Shot = new ShotIntentState(3f, new Vec2(90f, 0f), Vec2.Zero),
            };

            StageStateComparer.Result result = StageStateComparer.Compare(folded, captured);

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches[0], Does.Contain("shot.panInRigSpace"));
        }

        // ── 하네스 축소판 왕복 ───────────────────────────────────────

        [Test]
        public void 원문을_두_벌_접으면_등가로_판정된다()
        {
            // 하네스의 축소판: 원문 추출 → 두 벌 폴드 → 비교기 등가 판정.
            // 파이프라인이 이어져 있는지 보는 것이 목적이다.
            StageReducerTuning tuning = HarnessTuning();

            List<YarnLineGroup> groups = YarnCommandTextExtractor.Extract(SampleYarn, "sample.yarn");
            List<StageCommand> commands = groups.SelectMany(g => g.Commands).ToList();

            StageState a = StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);
            StageState b = StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);

            StageStateComparer.Result result = StageStateComparer.Compare(a, b);

            Assert.That(result.IsEquivalent, Is.True, result.ToString());
            Assert.That(result.ComparedNodes, Is.GreaterThan(0), "슬롯이 실제로 세워져야 유의미하다");
        }

        [Test]
        public void 접는_도중_실패해도_판정은_계속된다()
        {
            // 원문에는 아직 못 접는 커맨드가 섞여 있다(beat…).
            // 그것들이 Unhandled로 남을 뿐, 접힌 상태 자체는 성립해야 한다.
            StageReducerTuning tuning = HarnessTuning();

            List<StageCommand> commands = YarnCommandTextExtractor
                .Extract(SampleYarn, "sample.yarn")
                .SelectMany(g => g.Commands)
                .ToList();

            StageState state = StageReducer.ApplyAll(
                StageReducer.CreateInitialState(tuning), commands, tuning);

            Assert.That(state.HasSlot("c1"), Is.True, "slot은 접혔다");
            Assert.That(state.Unhandled.Count, Is.GreaterThan(0), "못 접은 것은 기록으로 남았다");
            Assert.That(state.Unhandled.Any(u => u.Command.Name == "bg_spawn"), Is.True);
        }

        private static StageReducerTuning HarnessTuning()
        {
            RigSchemaRigDto rig = new()
            {
                rigKind = "character",
                nodes = new List<RigSchemaNodeDto>
                {
                    NodeDto("__root", ""),
                    NodeDto("CharSlot_Track", "__root"),
                    NodeDto("CharSlot_Move_X", "CharSlot_Track"),
                    NodeDto("CharSlot_Move_Y", "CharSlot_Move_X"),
                    NodeDto("CharacterPortraitSprite_Root", "CharSlot_Move_Y"),
                },
            };

            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = new Float2Dto { x = 1920f, y = 1080f },
                    rigs = new List<RigSchemaRigDto> { rig },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                RoleAnchors = new RoleAnchorTuningBodyDto(),
            };
        }

        // ── 비교기: 무대 뷰(샷이 무대에 쓴 것) ─────────────────────────

        private static StageViewSnapshot ViewSolvedFrom(in ShotIntentState intent, string stageKey, float[] depths)
        {
            StageViewSnapshot view = new();
            CameraPose camera = ShotLayoutMath.SolveCamera(in intent);
            view.Cameras.Add(new StageViewSnapshot.CameraEntry(stageKey, camera, Vec2.Zero, 0f));

            foreach (float d in depths)
            {
                LayerResponse r = ShotLayoutMath.SolveLayer(in intent, d, LayerProfileCurve.Default);
                view.Layers.Add(new StageViewSnapshot.LayerEntry(
                    stageKey, d, ShotLayoutMath.ProjectToRigSpace(Vec2.Zero, in camera, in r), r.Scale * camera.Scale, camera.Roll));
            }

            return view;
        }

        [Test]
        public void 무대_뷰는_접힌_샷_의도를_코어_식으로_푼_값과_비교한다()
        {
            ShotIntentState intent = new(6f, new Vec2(250f, -30f), new Vec2(100f, 0f));
            StageViewSnapshot view = ViewSolvedFrom(in intent, "stage00", StageLayerLabels.Depths);

            StageStateComparer.Result result = new();
            StageStateComparer.CompareStageView(in intent, view, LayerProfileCurve.Default, result);

            Assert.That(result.IsEquivalent, Is.True, string.Join("\n", result.Mismatches));
            Assert.That(result.ComparedLayers, Is.EqualTo(5));
        }

        [Test]
        public void 무대_뷰가_다른_의도로_풀렸으면_층마다_불일치로_남는다()
        {
            ShotIntentState folded = new(6f, new Vec2(250f, 0f), Vec2.Zero);
            ShotIntentState live = new(6f, new Vec2(200f, 0f), Vec2.Zero);   // 팬 50 차이

            StageStateComparer.Result result = new();
            StageStateComparer.CompareStageView(
                in folded, ViewSolvedFrom(in live, "stage00", new[] { StageLayerLabels.Far, StageLayerLabels.Close }),
                LayerProfileCurve.Default, result);

            Assert.That(result.IsEquivalent, Is.False);
            Assert.That(result.Mismatches.Any(m => m.StartsWith("stage:stage00.cameraPan")), Is.True);
            Assert.That(result.Mismatches.Any(m => m.Contains("layer:1") && m.Contains("center")), Is.True, "Close 층 중심");
        }

        [Test]
        public void 무대_뷰는_카메라의_몸과_헤드까지_본다()
        {
            // 트랙 · 달리 · 롤이 든 샷 — 카메라 노드 다섯과 층의 중심 · 배율 · 기울기가 접힌 샷과 맞아야 한다.
            ShotIntentState folded = new(3f, new Vec2(80f, -20f), new Vec2(100f, 0f), new Vec2(240f, 60f), 4f, 12f);

            StageStateComparer.Result same = new();
            StageStateComparer.CompareStageView(
                in folded, ViewSolvedFrom(in folded, "stage00", StageLayerLabels.Depths), LayerProfileCurve.Default, same);

            Assert.That(same.IsEquivalent, Is.True, string.Join("\n", same.Mismatches));

            // 롤만 다른 화면 — 카메라 노드와 층의 기울기가 함께 어긋난다.
            ShotIntentState level = new(
                folded.Zoom, folded.PanInRigSpace, folded.FocusPointInRigSpace, folded.Track, folded.Dolly, 0f);

            StageStateComparer.Result tilted = new();
            StageStateComparer.CompareStageView(
                in folded, ViewSolvedFrom(in level, "stage00", new[] { StageLayerLabels.Mid }), LayerProfileCurve.Default, tilted);

            Assert.That(tilted.Mismatches.Any(m => m.StartsWith("stage:stage00.cameraRoll")), Is.True, string.Join("\n", tilted.Mismatches));
            Assert.That(tilted.Mismatches.Any(m => m.StartsWith("stage:stage00/layer:0.rotation")), Is.True, string.Join("\n", tilted.Mismatches));
        }

        [Test]
        public void 한_바퀴_넘겨_읽힌_기울기는_같은_기울기다()
        {
            // 유니티는 −10도를 350으로 읽어 준다. 같은 화면이다.
            ShotIntentState folded = new(0f, Vec2.Zero, Vec2.Zero, Vec2.Zero, 0f, -10f);

            StageViewSnapshot view = new();
            view.Cameras.Add(new StageViewSnapshot.CameraEntry(
                "stage00", new CameraPose(Vec2.Zero, 1f, 350f, 1f, Vec2.Zero), Vec2.Zero, 0f));

            StageStateComparer.Result result = new();
            StageStateComparer.CompareStageView(in folded, view, LayerProfileCurve.Default, result);

            Assert.That(result.IsEquivalent, Is.True, string.Join("\n", result.Mismatches));
        }

        [Test]
        public void 정착한_프레임에_흔들림이_남아_있으면_불일치다()
        {
            // 흔들림은 접히지 않는다 — 끝나면 0이어야 한다. 남아 있으면 연기가 구도를 더럽힌 것이다.
            ShotIntentState folded = ShotIntentState.Default;

            StageViewSnapshot view = new();
            view.Cameras.Add(new StageViewSnapshot.CameraEntry(
                "stage00", ShotLayoutMath.SolveCamera(in folded), new Vec2(6f, 0f), 0f));

            StageStateComparer.Result result = new();
            StageStateComparer.CompareStageView(in folded, view, LayerProfileCurve.Default, result);

            Assert.That(result.Mismatches.Count, Is.EqualTo(1), string.Join("\n", result.Mismatches));
            Assert.That(result.Mismatches[0], Does.StartWith("stage:stage00.cameraShake"));
        }

        [Test]
        public void 덮는_무대의_기대값은_중립이다()
        {
            // 샷은 본 무대만 움직인다(StageRoles). 덮는 무대가 샷을 따랐으면 불일치다.
            ShotIntentState folded = new(6f, new Vec2(250f, -30f), new Vec2(100f, 0f));

            StageStateComparer.Result fixedOverlay = new();
            StageStateComparer.CompareStageView(
                in folded, ViewSolvedFrom(ShotIntentState.Default, "stage01", StageLayerLabels.Depths),
                LayerProfileCurve.Default, fixedOverlay);

            Assert.That(fixedOverlay.IsEquivalent, Is.True, string.Join("\n", fixedOverlay.Mismatches));

            StageStateComparer.Result followingOverlay = new();
            StageStateComparer.CompareStageView(
                in folded, ViewSolvedFrom(in folded, "stage01", StageLayerLabels.Depths),
                LayerProfileCurve.Default, followingOverlay);

            Assert.That(followingOverlay.IsEquivalent, Is.False);
            Assert.That(followingOverlay.Mismatches.Any(m => m.StartsWith("stage:stage01.cameraZoom")), Is.True);
        }

        private static RigSchemaNodeDto NodeDto(string id, string parent) => new()
        {
            id = id,
            parent = parent,
            anchoredPosition = new Float2Dto { x = 0f, y = 0f },
            anchorMin = new Float2Dto { x = 0f, y = 0f },
            anchorMax = new Float2Dto { x = 1f, y = 1f },
            pivot = new Float2Dto { x = 0.5f, y = 0.5f },
            sizeDelta = new Float2Dto { x = 0f, y = 0f },
            localScale = new Float3Dto { x = 1f, y = 1f, z = 1f },
            localEulerAngles = new Float3Dto { x = 0f, y = 0f, z = 0f },
            measuredRectSize = new Float2Dto { x = 0f, y = 0f },
        };
    }
}
