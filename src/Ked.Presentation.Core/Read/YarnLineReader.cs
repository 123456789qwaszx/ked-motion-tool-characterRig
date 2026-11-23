using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 줄 읽기 — yarn 원문에서 대사 줄마다 스텝의 목록을 읽는다(ked-story-tools docs/PLAN.md §4.2 · K2).
    ///
    ///   스텝마다    앵커 이름 · 갈래 · 대사 조각 · 간격
    ///   커맨드마다  이름 · 인자(명세 표의 칸으로) · 종류 · 앵커가 열린 뒤의 지연 · 원문에서의 줄
    ///   줄마다      그 줄의 커맨드 묶음이 원문의 어디서 어디까지인가
    ///
    /// 앵커의 차례는 재생 · 접힘과 같은 함수(LineBeatSchedule.BuildAnchors)가 정한다.
    ///
    /// **추출기(YarnCommandTextExtractor)와 다른 점** — 추출기는 등가성 하네스 전용이라 "재생이 만나는 순서 그대로"의
    /// 커맨드 열이 필요하고, 그래서 분기 · 선택지가 있는 파일을 믿지 못한다. 줄 읽기는 재생 순서가 필요 없다 —
    /// 대사 줄 **바로 위**의 커맨드 묶음만 읽는다. 그래서 분기가 있는 파일에서도 돈다:
    ///   분기 · 선택지(if · else · jump · -&gt; …)는 묶음을 끊는다. 그 위에 남은 at · gap은 어느 줄의 것인지
    ///   원문으로 알 수 없으므로 파일의 문제로 적는다(원문 검사의 오류 — docs/PLAN_line-anchor.md AR10).
    ///
    /// 줄의 명령은 모두 &lt;&lt;at&gt;&gt;이다. 대사 줄 앞의 at 없는 명령은 원문 검사의 오류다 — 재생처럼 실행하지 않으므로
    /// 앵커에 넣지 않고 LineRead.Unanchored에 싣는다. 바로 실행하는 명령(명세 표의 Immediate)과 Yarn의 문은 at 없이 쓰는
    /// 것이 맞다 — 앵커 밖의 것으로 LineRead.Immediate에 싣는다. 노드 끝에 줄 없이 남은 명령(암묵 스텝)은 줄이 아니라 읽지 않는다.
    /// </summary>
    public static class YarnLineReader
    {
        /// <summary>
        /// &lt;&lt;pause&gt;&gt;에 길이를 적지 않았을 때의 길이(초). 파서 표에는 생략값이 없고 핸들러가 이 값으로 간다 —
        /// 무대 파서와 같아야 한다(EditMode 시험이 맞춰 본다).
        /// </summary>
        public const float DefaultPauseSeconds = 0.18f;

        private const string LineIdPrefix = "line:";

        /// <summary>"abc" → "line:abc". 이미 접두사가 있으면 그대로.</summary>
        public static string NormalizeLineId(string lineId)
        {
            string trimmed = (lineId ?? string.Empty).Trim().TrimStart('#');
            return trimmed.StartsWith(LineIdPrefix, StringComparison.Ordinal) ? trimmed : LineIdPrefix + trimmed;
        }

        /// <summary>
        /// yarn 원문과 LineId를 받아 그 줄을 돌려준다. 없으면 null.
        /// 한 파일에서 여러 줄을 읽으려면 ReadFile을 한 번 부르고 TryGet으로 찾는다.
        /// </summary>
        public static LineRead Read(string yarnText, string lineId, string sourceName = null, StageCommandSpecTable specs = null)
            => ReadFile(yarnText, sourceName, specs).TryGet(lineId, out LineRead line) ? line : null;

        /// <summary>파일의 대사 줄을 모두 읽는다. specs를 주지 않으면 StageCommandSpecTable.Default.</summary>
        public static YarnFileRead ReadFile(string yarnText, string sourceName = null, StageCommandSpecTable specs = null)
        {
            if (yarnText == null)
                throw new ArgumentNullException(nameof(yarnText));

            specs ??= StageCommandSpecTable.Default;
            sourceName ??= "yarn";

            YarnFileRead file = new();
            List<Pending> pending = new();
            List<BeatMarker> markers = new();
            List<string> problems = new();

            string nodeName = null;
            string pendingTitle = null;
            bool inHeader = true;

            // 묶음의 첫 줄 — 0이면 아직 묶음이 없다.
            int blockFirstLine = 0;
            int blockStart = 0;

            void Break(string where, int lineNumber, bool nodeEnd = false)
            {
                int strayAts = 0;
                int strayGaps = 0;
                int strayBare = 0;

                foreach (Pending item in pending)
                {
                    if (item.Role == Role.At)
                        strayAts++;
                    else if (item.Role == Role.Gap)
                        strayGaps++;
                    else if (item.Role == Role.Bare)
                        strayBare++;
                }

                // at 없는 명령이 노드 끝에 줄 없이 남는 것은 암묵 스텝이다 — 문제가 아니다(줄이 없어 읽을 주소도 없다).
                if (strayBare > 0 && !nodeEnd)
                    file.Problems.Add($"{sourceName}:{lineNumber} at 없는 명령 {strayBare}개 다음이 대사 줄이 아니라 {where}이다 — 어느 줄의 것인지 알 수 없다");

                if (strayAts > 0)
                    file.Problems.Add($"{sourceName}:{lineNumber} <<at>> {strayAts}개 다음이 대사 줄이 아니라 {where}이다 — 어느 줄의 것인지 알 수 없다");

                if (strayGaps > 0)
                    file.Problems.Add($"{sourceName}:{lineNumber} <<gap>> {strayGaps}개 다음이 대사 줄이 아니라 {where}이다 — 붙을 앵커가 없다");

                pending.Clear();
                blockFirstLine = 0;
            }

            int offset = 0;
            int lineNumber = 0;

            while (offset <= yarnText.Length)
            {
                int lineStart = offset;
                int newline = yarnText.IndexOf('\n', offset);
                int lineEnd = newline < 0 ? yarnText.Length : newline;
                offset = newline < 0 ? yarnText.Length + 1 : newline + 1;
                lineNumber++;

                string raw = yarnText.Substring(lineStart, lineEnd - lineStart);
                string line = YarnCommandTextExtractor.StripComment(raw).Trim().TrimStart('﻿');

                if (line.Length == 0)
                    continue;

                if (inHeader)
                {
                    if (line.StartsWith("title:", StringComparison.Ordinal))
                        pendingTitle = line.Substring("title:".Length).Trim();

                    if (line == "---")
                    {
                        nodeName = pendingTitle;
                        pendingTitle = null;
                        inHeader = false;
                    }

                    continue;
                }

                if (line == "===")
                {
                    Break("노드 끝", lineNumber, nodeEnd: true);
                    inHeader = true;
                    nodeName = null;
                    continue;
                }

                string source = $"{sourceName}:{lineNumber}";

                if (line.StartsWith("<<", StringComparison.Ordinal))
                {
                    if (!YarnCommandTextExtractor.TryParseCommand(line, source, out StageCommand command))
                    {
                        file.Problems.Add($"{source} 커맨드를 읽지 못했다: {line}");
                        continue;
                    }

                    if (YarnCommandTextExtractor.IsYarnControl(command.Name))
                    {
                        Break("분기", lineNumber);
                        continue;
                    }

                    if (blockFirstLine == 0)
                    {
                        blockFirstLine = lineNumber;
                        blockStart = lineStart;
                    }

                    pending.Add(Classify(command, lineNumber, specs, file.Problems, line));
                    continue;
                }

                if (line.StartsWith("->", StringComparison.Ordinal))
                {
                    Break("선택지", lineNumber);
                    continue;
                }

                // 대사 줄 — 바로 위의 묶음이 이 줄의 것이다.
                LineRead read = new()
                {
                    NodeName = nodeName,
                    Line = lineNumber,
                    Indent = raw.Substring(0, raw.Length - raw.TrimStart().Length),
                    Block = blockFirstLine > 0
                        ? new SourceSpan(blockFirstLine, lineNumber - 1, blockStart, lineStart - blockStart)
                        : new SourceSpan(lineNumber, lineNumber - 1, lineStart, 0),
                };

                markers.Clear();
                problems.Clear();

                string text = YarnCommandTextExtractor.StripTags(line, out string lineId);
                string plain = YarnCommandTextExtractor.StripLineMarkers(text, markers);
                int bodyStart = YarnCommandTextExtractor.CharacterPrefixLength(plain);

                read.LineId = lineId;
                read.Body = plain.Substring(bodyStart);
                read.Speaker = bodyStart > 0 ? plain.Substring(0, bodyStart).TrimEnd().TrimEnd(':').TrimEnd() : string.Empty;

                for (int m = 0; m < markers.Count; m++)
                    markers[m] = new BeatMarker(markers[m].Name, Math.Max(0, markers[m].Position - bodyStart));

                BuildSteps(read, pending, markers, specs, problems);

                foreach (string problem in problems)
                    read.Problems.Add(problem);

                file.Add(read, source);

                pending.Clear();
                blockFirstLine = 0;
            }

            if (!inHeader)
                Break("파일 끝", lineNumber, nodeEnd: true);

            return file;
        }

        // ── 묶음의 한 줄 ────────────────────────────────────────────────────────

        private enum Role
        {
            /// <summary>&lt;&lt;at 이름 명령 …&gt;&gt;</summary>
            At,

            /// <summary>&lt;&lt;gap 이름 길이&gt;&gt;</summary>
            Gap,

            /// <summary>at 없이 쓴 무대 커맨드 — 대사 줄 앞이면 오류(실행하지 않는다), 노드 끝이면 암묵 스텝.</summary>
            Bare,

            /// <summary>바로 실행하는 명령 — 앵커 밖.</summary>
            Immediate,

            /// <summary>읽지 못한 at · gap — 문제로만 남는다.</summary>
            Broken,
        }

        private sealed class Pending
        {
            public Role Role;
            public int Line;
            public AtCommand At;
            public GapCommand Gap;
            public StageCommand Command;
        }

        private static Pending Classify(
            in StageCommand command, int lineNumber, StageCommandSpecTable specs, List<string> fileProblems, string line)
        {
            if (command.Name == LineBeatSchedule.AtCommandName)
            {
                if (LineBeatSchedule.TryReadAt(command.Args, command.Source, out AtCommand at, out string error))
                    return new Pending { Role = Role.At, Line = lineNumber, At = at };

                fileProblems.Add($"{command.Source} {error}: {line}");
                return new Pending { Role = Role.Broken, Line = lineNumber };
            }

            if (command.Name == LineBeatSchedule.GapCommandName)
            {
                if (LineBeatSchedule.TryReadGap(command.Args, command.Source, out GapCommand gap, out string error))
                    return new Pending { Role = Role.Gap, Line = lineNumber, Gap = gap };

                fileProblems.Add($"{command.Source} {error}: {line}");
                return new Pending { Role = Role.Broken, Line = lineNumber };
            }

            bool immediate = specs.IsImmediate(command.Name) || YarnCommandTextExtractor.IsYarnStatement(command.Name);

            return new Pending
            {
                Role = immediate ? Role.Immediate : Role.Bare,
                Line = lineNumber,
                Command = command,
            };
        }

        private static void BuildSteps(
            LineRead read, List<Pending> pending, List<BeatMarker> markers, StageCommandSpecTable specs, List<string> problems)
        {
            // 같은 StageCommand가 둘일 수 있으므로 줄 번호는 차례로 잇는다: 앵커마다 쓴 순서가 지켜진다.
            List<AtCommand> ordered = new(pending.Count);
            Dictionary<string, Queue<Pending>> origin = new(StringComparer.Ordinal);
            List<GapCommand> gaps = new();
            Dictionary<string, int> gapLines = new(StringComparer.Ordinal);

            foreach (Pending item in pending)
            {
                switch (item.Role)
                {
                    case Role.At:
                        ordered.Add(item.At);

                        if (!origin.TryGetValue(item.At.Beat, out Queue<Pending> queue))
                            origin.Add(item.At.Beat, queue = new Queue<Pending>());

                        queue.Enqueue(item);
                        break;

                    case Role.Gap:
                        gaps.Add(item.Gap);

                        if (!gapLines.ContainsKey(item.Gap.Anchor))
                            gapLines.Add(item.Gap.Anchor, item.Line);

                        break;

                    case Role.Bare:
                        problems.Add(YarnCommandTextExtractor.UnanchoredProblem(item.Command));
                        read.Unanchored.Add(ReadCommand(item.Command, item.Line, null, specs, problems));
                        break;

                    case Role.Immediate:
                        read.Immediate.Add(ReadCommand(item.Command, item.Line, null, specs, problems));
                        break;
                }
            }

            List<LineAnchor> anchors = LineBeatSchedule.BuildAnchors(markers, ordered, gaps, problems);

            // 표지가 없는 at은 end에서 실행된다(AR9) — 원래 이름의 줄에서 찾는다.
            Dictionary<string, int> kindCounts = new(StringComparer.Ordinal);

            for (int i = 0; i < anchors.Count; i++)
            {
                LineAnchor anchor = anchors[i];

                LineReadStep step = new()
                {
                    Name = anchor.Name,
                    Kind = anchor.Kind,
                    Position = anchor.Position,
                    Text = LineBeatSchedule.Segment(read.Body, anchors, i),
                    Gap = anchor.Gap,
                    GapLine = gapLines.TryGetValue(anchor.Name, out int gapLine) ? gapLine : 0,
                };

                float running = 0f;
                kindCounts.Clear();

                foreach (AtCommand at in anchor.Commands)
                {
                    Pending item = origin[at.Beat].Dequeue();

                    LineReadCommand command = ReadCommand(at.Command, item.Line, step.Name, specs, problems);
                    command.Delay = running;

                    if (command.Spec != null)
                    {
                        string kindKey = CommandKinds.Key(command.Spec.Kind);
                        kindCounts.TryGetValue(kindKey, out int seen);
                        command.KindIndex = seen;
                        kindCounts[kindKey] = seen + 1;

                        if (command.Spec.Immediate)
                            problems.Add($"<<at {at.Beat} {command.Name} …>> — 바로 실행하는 명령에는 at을 붙이지 않는다(파서가 모르는 명령이 된다)");
                    }

                    if (command.Waits)
                    {
                        command.Hold = command.Duration ?? 0f;
                        running += command.Hold;
                    }

                    step.Commands.Add(command);
                }

                step.Settle = running;
                read.Steps.Add(step);
            }
        }

        private static LineReadCommand ReadCommand(
            in StageCommand source, int lineNumber, string anchor, StageCommandSpecTable specs, List<string> problems)
        {
            LineReadCommand command = new()
            {
                Name = source.Name,
                Args = source.Args,
                Anchor = anchor,
                Line = lineNumber,
            };

            if (!specs.TryGet(source.Name, out StageCommandSpec spec))
            {
                if (!YarnCommandTextExtractor.IsYarnStatement(source.Name))
                    problems.Add($"<<{source.Name}>> — 명세 표에 없는 명령이다. 종류와 인자 칸을 모른다");

                return command;
            }

            command.Spec = spec;
            command.Bound = spec.Bind(source.Args, problems);
            command.Waits = spec.Waits;

            int durationIndex = spec.DurationArgIndex;

            if (durationIndex >= 0)
            {
                BoundArg duration = command.Bound[durationIndex];
                string token = duration.Value;

                if (!string.IsNullOrEmpty(token))
                {
                    if (DurationToken.TryParseSeconds(token, out float seconds))
                        command.Duration = seconds;
                    else
                        problems.Add($"<<{source.Name}>> — 길이 '{token}'을 읽지 못했다");
                }
            }

            // pause는 표에 생략값이 없다 — 핸들러가 제 값으로 간다.
            if (command.Duration == null && source.Name == YarnCommandTextExtractor.PauseCommand)
                command.Duration = DefaultPauseSeconds;

            return command;
        }
    }
}
