using System;
using System.Collections.Generic;
using System.Text;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 대사 라인 경계 하나. Commands는 "직전 대사 이후 ~ 이 대사까지" 사이의 커맨드들이다.
    /// 파일 끝의 대사 없는 꼬리 커맨드들은 LineText가 null인 마지막 항목으로 남는다.
    /// </summary>
    public sealed class YarnLineGroup
    {
        /// <summary>대사 원문(태그 · 자리 표지 제외). 꼬리 그룹이면 null.</summary>
        public string LineText;

        /// <summary>
        /// #line 태그 값. **접두사를 포함한 전체**다 — Yarn 런타임의 라인 ID가
        /// "line:316f20c1" 형태이므로 접두사를 벗기면 조회가 빗나간다.
        /// 태그가 없으면 null (이 프로젝트의 원문이 그렇다 — 하네스는 순서 커서로 간다).
        /// </summary>
        public string LineId;

        /// <summary>
        /// 이 줄에서 실행되는 커맨드 — 앵커의 차례(앞 앵커 → start → 글 표지 → end), 그 안에서는 쓴 순서.
        /// 순서는 재생과 같은 LineBeatSchedule.BuildAnchors가 정한다(docs/PLAN_line-anchor.md AR1 · AR2).
        /// 줄이 끝났을 때의 무대가 이 열을 접은 것과 같다.
        /// </summary>
        public readonly List<StageCommand> Commands = new();

        /// <summary>
        /// 이 줄의 앵커 — 열리는 차례. 앵커 하나가 스텝 하나다(docs/PLAN_line-anchor.md A1 · A2).
        /// start · end는 명령이 없어도 있다. 꼬리 그룹(줄이 없다)이면 비어 있다.
        /// </summary>
        public readonly List<LineAnchor> Anchors = new();

        /// <summary>
        /// 화자 이름과 표지를 뺀 글 — 앵커의 글자 자리가 이 글의 자리다(재생의 TextWithoutCharacterName과 같다).
        /// 스텝마다의 대사 조각은 LineBeatSchedule.Segment(Body, Anchors, i). 꼬리 그룹이면 null.
        /// </summary>
        public string Body;
    }

    /// <summary>노드 하나의 라인 그룹들. 한 파일에 여러 노드가 있다(Setup + Story).</summary>
    public sealed class YarnNodeGroups
    {
        public string NodeName;
        public readonly List<YarnLineGroup> Groups = new();

        /// <summary>옛 표기 — 이 노드의 &lt;&lt;step&gt;&gt; 수. 앞 앵커의 경계로 읽었다(docs/PLAN_line-anchor.md §9 — N5에서 오류).</summary>
        public int LegacySteps;

        /// <summary>옛 표기 — 이 노드의 at 없는 명령 수. start의 것으로 읽었다(§10 Q1 — N5에서 오류).</summary>
        public int BareCommands;
    }

    /// <summary>
    /// yarn "원문"에서 커맨드 열을 라인 경계 단위로 뽑는다 — 등가성 하네스 전용.
    ///
    /// **원문을 접는 것이 가장 강한 대조다.** 브리지가 이미 파싱한 커맨드를 받아 접으면
    /// "파싱은 맞다고 치고" 비교하는 셈이다. 원문에서 직접 뽑으면 브리지 파싱과
    /// 코어 파싱이 다를 때 여기서 드러난다.
    ///
    /// 용도 한정: 이것은 Yarn 문법 해석기가 아니다. 대표 에피소드처럼 선형인 파일에서
    /// "재생이 만나는 순서 그대로"의 커맨드 열을 얻는 것이 목적이다. 분기(if/jump)·옵션(->)은
    /// 다루지 않으며, 만나면 커맨드가 아니라 경고 목록에 남긴다 — 그런 파일은 이 추출기로
    /// 접으면 안 된다는 신호다.
    /// </summary>
    public static class YarnCommandTextExtractor
    {
        /// <summary>
        /// 무대 스텝(옛 표기) — 여기까지의 at 없는 커맨드가 다음 대사 줄의 앞 앵커 하나다. 무대 커맨드가 아니다.
        /// 그룹을 가르지 않는다: 앞의 커맨드는 다음 대사의 그룹에 그대로 든다(docs/PLAN_line-anchor.md A6 · §9).
        /// </summary>
        public const string StageStepCommand = "step";

        /// <summary>기다리는 명령 — 시간만 쓴다. 무대 상태는 접을 것이 없다(StageReducer).</summary>
        public const string PauseCommand = "pause";

        /// <summary>
        /// 기다리는 명령인가 — pause · screen_cover(덮기). 그 앵커의 뒤 명령을 붙잡고, 다음 앵커는 그것이 끝난 뒤에 열린다
        /// (docs/PLAN_line-anchor.md A7 · AR3). 재생에서는 무대 파서가 이 둘의 스펙에 wait를 세운다 — 두 목록이 같아야 한다
        /// (EditMode 시험이 맞춰 본다). screen_cover_clear는 기다리지 않는다.
        /// </summary>
        public static bool IsWaitingCommand(string name) => name == PauseCommand || name == "screen_cover";

        /// <summary>파일 전체를 노드 구분 없이 하나의 흐름으로 뽑는다(단일 노드 파일용).</summary>
        public static List<YarnLineGroup> Extract(
            string yarnText, string sourceName, List<string> warnings = null)
        {
            List<YarnLineGroup> flattened = new();

            foreach (YarnNodeGroups node in ExtractNodes(yarnText, sourceName, warnings))
                flattened.AddRange(node.Groups);

            return flattened;
        }

        /// <summary>노드별로 뽑는다. 한 파일에 Setup 노드와 Story 노드가 함께 있다.</summary>
        public static List<YarnNodeGroups> ExtractNodes(
            string yarnText, string sourceName, List<string> warnings = null)
        {
            if (yarnText == null)
                throw new ArgumentNullException(nameof(yarnText));

            List<YarnNodeGroups> nodes = new();
            YarnNodeGroups node = null;
            string pendingTitle = null;
            bool inHeader = true;

            // 다음 대사 줄의 것 — 쓴 순서. 그 줄에 닿으면 앵커로 나뉜다(LineBeatSchedule.BuildAnchors).
            // at 없는 명령(옛 표기)은 start의 것으로 싣고 pendingBare에 적는다(§10 Q1).
            List<AtCommand> pending = new();
            List<bool> pendingBare = new();
            List<GapCommand> pendingGaps = new();
            List<BeatMarker> markers = new();
            List<string> problems = new();

            // 옛 표기의 무대 스텝 — 이 줄 앞에 닫힌 것의 수와, 아직 닫히지 않은 것이 시작하는 자리.
            int legacySteps = 0;
            int stepStart = 0;

            string[] lines = yarnText.Replace("\r\n", "\n").Split('\n');

            void ResetPending()
            {
                pending.Clear();
                pendingBare.Clear();
                pendingGaps.Clear();
                legacySteps = 0;
                stepStart = 0;
            }

            // AR10 — <<at>> · <<gap>> 다음이 대사 줄이 아니다. 재생은 경고하고 at을 그 자리의 명령 뒤에 붙인다 — 접기도 그렇게.
            // 간격은 붙을 앵커가 없어 버린다.
            void SettleStray(string where, string source)
            {
                List<AtCommand> stray = new();

                for (int k = 0; k < pending.Count; )
                {
                    if (pendingBare[k])
                    {
                        k++;
                        continue;
                    }

                    stray.Add(pending[k]);
                    pending.RemoveAt(k);
                    pendingBare.RemoveAt(k);

                    if (k < stepStart)
                        stepStart--;
                }

                if (stray.Count > 0)
                {
                    warnings?.Add($"{source} <<at>> {stray.Count}개 다음이 대사 줄이 아니라 {where}이다 — 붙을 줄이 없어 그 스텝의 머리 뒤에 둔다.");

                    foreach (AtCommand at in stray)
                    {
                        pending.Add(new AtCommand(LineBeatSchedule.StartBeat, at.Command));
                        pendingBare.Add(true);
                    }
                }

                if (pendingGaps.Count > 0)
                {
                    warnings?.Add($"{source} <<gap>> {pendingGaps.Count}개 다음이 대사 줄이 아니라 {where}이다 — 붙을 앵커가 없어 버린다.");
                    pendingGaps.Clear();
                }
            }

            void FlushNode()
            {
                if (node == null)
                    return;

                // 꼬리 커맨드(대사 없이 노드가 끝나는 경우)도 버리지 않는다 — 노드 끝의 암묵 스텝이 쓴 순서로 실행한다.
                if (pending.Count > 0)
                {
                    YarnLineGroup tail = new();

                    foreach (AtCommand at in pending)
                        tail.Commands.Add(at.Command);

                    node.Groups.Add(tail);
                }

                nodes.Add(node);
                node = null;
                ResetPending();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();

                // 파일 선두 BOM은 공백이 아니라 Trim에 안 걸린다.
                line = line.TrimStart('﻿');

                if (line.Length == 0)
                    continue;

                // 노드 헤더: title: ~ --- 사이. === 는 노드 끝.
                if (inHeader)
                {
                    if (line.StartsWith("title:", StringComparison.Ordinal))
                        pendingTitle = line.Substring("title:".Length).Trim();

                    if (line == "---")
                    {
                        node = new YarnNodeGroups { NodeName = pendingTitle };
                        pendingTitle = null;
                        inHeader = false;
                    }

                    continue;
                }

                if (line == "===")
                {
                    SettleStray("노드 끝", $"{sourceName}:{i + 1}");
                    FlushNode();
                    inHeader = true;
                    continue;
                }

                if (line.StartsWith("<<", StringComparison.Ordinal))
                {
                    if (TryParseCommand(line, $"{sourceName}:{i + 1}", out StageCommand command))
                    {
                        if (command.Name == StageStepCommand)
                        {
                            // 옛 표기 — 여기까지의 at 없는 명령이 앞 앵커 하나다. 빈 스텝은 스텝이 아니다(지금 그대로).
                            string stepName = LineBeatSchedule.LegacyStepName(legacySteps);
                            bool any = false;

                            for (int k = stepStart; k < pending.Count; k++)
                            {
                                if (!pendingBare[k])
                                    continue;

                                pending[k] = new AtCommand(stepName, pending[k].Command);
                                any = true;
                            }

                            if (any)
                                legacySteps++;

                            stepStart = pending.Count;
                            node.LegacySteps++;
                            continue;
                        }

                        if (command.Name == LineBeatSchedule.AtCommandName)
                        {
                            if (LineBeatSchedule.TryReadAt(command.Args, command.Source, out AtCommand at, out string error))
                            {
                                pending.Add(at);
                                pendingBare.Add(false);
                            }
                            else
                            {
                                warnings?.Add($"{command.Source} {error}: {line}");
                            }

                            continue;
                        }

                        if (command.Name == LineBeatSchedule.GapCommandName)
                        {
                            if (LineBeatSchedule.TryReadGap(command.Args, command.Source, out GapCommand gap, out string error))
                                pendingGaps.Add(gap);
                            else
                                warnings?.Add($"{command.Source} {error}: {line}");

                            continue;
                        }

                        if (IsFlowControl(command.Name))
                        {
                            warnings?.Add(
                                $"{sourceName}:{i + 1} 분기/제어 커맨드 '{command.Name}' — " +
                                "이 추출기는 선형 파일 전용이다. 결과를 믿지 말 것.");
                        }

                        pending.Add(new AtCommand(LineBeatSchedule.StartBeat, command));
                        pendingBare.Add(true);
                        node.BareCommands++;
                    }
                    else
                    {
                        warnings?.Add($"{sourceName}:{i + 1} 커맨드를 읽지 못했다: {line}");
                    }

                    continue;
                }

                if (line.StartsWith("->", StringComparison.Ordinal))
                {
                    SettleStray("선택지", $"{sourceName}:{i + 1}");
                    warnings?.Add($"{sourceName}:{i + 1} 옵션(->)은 다루지 않는다 — 선형 파일 전용.");
                    continue;
                }

                // 대사 라인 = 경계. 쌓인 것이 이 줄의 앵커로 나뉜다 — 재생과 같은 함수가 정한다.
                string text = StripTags(line, out string lineId);
                string lineSource = $"{sourceName}:{i + 1}";

                markers.Clear();
                problems.Clear();

                YarnLineGroup group = new();
                group.LineText = StripLineMarkers(text, markers);
                group.LineId = lineId;

                // 표지의 자리를 화자 이름을 뺀 글의 자리로 옮긴다 — 재생과 같은 자리다. 자리 0이 앞 앵커다(A5).
                int bodyStart = CharacterPrefixLength(group.LineText);
                group.Body = group.LineText.Substring(bodyStart);

                for (int m = 0; m < markers.Count; m++)
                    markers[m] = new BeatMarker(markers[m].Name, Math.Max(0, markers[m].Position - bodyStart));

                // 재생의 차례에 맞춘다 — at 없는 명령(옛 표기)은 미리 풀려 머리 배치로 실행되고, start의 <<at>>은 그 바로 뒤다.
                // 그래서 한 줄에 둘을 섞어 쓰면 at 없는 것이 먼저다. 다른 앵커의 차례는 바뀌지 않는다.
                List<AtCommand> ordered = new(pending.Count);

                for (int k = 0; k < pending.Count; k++)
                {
                    if (pendingBare[k] && pending[k].Beat == LineBeatSchedule.StartBeat)
                        ordered.Add(pending[k]);
                }

                for (int k = 0; k < pending.Count; k++)
                {
                    if (!pendingBare[k] || pending[k].Beat != LineBeatSchedule.StartBeat)
                        ordered.Add(pending[k]);
                }

                foreach (LineAnchor anchor in LineBeatSchedule.BuildAnchors(markers, ordered, pendingGaps, problems, legacySteps))
                {
                    group.Anchors.Add(anchor);

                    // 기다리는 명령(pause · screen_cover)은 어느 앵커에서나 쓴다(docs/PLAN_line-anchor.md A7) — 접을 것은 같다.
                    foreach (AtCommand at in anchor.Commands)
                        group.Commands.Add(at.Command);
                }

                foreach (string problem in problems)
                    warnings?.Add($"{lineSource} {problem}");

                node?.Groups.Add(group);
                ResetPending();
            }

            if (node != null)
                SettleStray("파일 끝", $"{sourceName}:{lines.Length}");

            FlushNode();

            return nodes;
        }

        /// <summary>
        /// 화자 이름("세이나: ")의 길이 — 줄 머리의 첫 ':'까지와 뒤따르는 공백. 화자가 없으면 0.
        /// Yarn이 줄 머리의 "이름:"을 화자로 떼는 것을 따른다 — 재생의 표지 자리는 화자를 뺀 글의 자리다.
        /// \: 는 화자 표시가 아니고, ':' 앞에 마크업이 오면 화자가 없는 것으로 본다. plain은 표지를 뺀 글이다.
        /// </summary>
        public static int CharacterPrefixLength(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return 0;

            for (int i = 0; i < plain.Length; i++)
            {
                char c = plain[i];

                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '[')
                    return 0;

                if (c != ':')
                    continue;

                if (i == 0)
                    return 0;

                int end = i + 1;

                while (end < plain.Length && char.IsWhiteSpace(plain[end]))
                    end++;

                return end;
            }

            return 0;
        }

        /// <summary>"&lt;&lt;cmd a "b c" d&gt;&gt;" → StageCommand. 따옴표 인자를 존중한다.</summary>
        public static bool TryParseCommand(string line, string source, out StageCommand command)
        {
            command = default;

            string trimmed = line.Trim();

            if (!trimmed.StartsWith("<<", StringComparison.Ordinal) ||
                !trimmed.EndsWith(">>", StringComparison.Ordinal) ||
                trimmed.Length < 4)
            {
                return false;
            }

            string body = trimmed.Substring(2, trimmed.Length - 4).Trim();

            if (body.Length == 0)
                return false;

            List<string> tokens = SplitRespectingQuotes(body);

            if (tokens.Count == 0)
                return false;

            string name = tokens[0];
            tokens.RemoveAt(0);

            command = new StageCommand(name, tokens.ToArray(), source);
            return true;
        }

        /// <summary>
        /// 대사 글에서 자리 표지 [beat=a/]를 떼어 글 순서대로 markers 뒤에 붙이고, 표지를 뺀 글을 돌려준다.
        /// 이름은 따옴표가 있어도 없어도 된다. 표지의 자리는 표지를 뺀 글의 글자 자리다.
        /// 재생은 Yarn의 마크업 파서로 읽는다(Playback의 InlineCommandTrack) — 코어는 Yarn을 모르므로 이 표지 하나만 읽는다.
        /// Yarn처럼 따옴표 안의 역슬래시는 벗기고, 공백 뒤(또는 줄 머리)의 표지는 뒤따르는 공백 하나를 같이 먹는다.
        /// </summary>
        public static string StripLineMarkers(string text, List<BeatMarker> markers)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0)
                return text;

            StringBuilder plain = new(text.Length);
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] != '[' || !TryReadBeatMarker(text, i, out int end, out string name))
                {
                    plain.Append(text[i++]);
                    continue;
                }

                markers.Add(new BeatMarker(name, plain.Length));

                bool afterWhitespace = plain.Length == 0 || char.IsWhiteSpace(plain[plain.Length - 1]);
                i = end;

                if (afterWhitespace && i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
            }

            return plain.ToString();
        }

        // [ beat = 이름 / ] — start는 '['의 자리. 읽었으면 end는 ']' 다음 자리.
        private static bool TryReadBeatMarker(string text, int start, out int end, out string name)
        {
            end = start;
            name = null;

            int i = SkipWhitespace(text, start + 1);

            if (string.CompareOrdinal(text, i, LineBeatSchedule.MarkerName, 0, LineBeatSchedule.MarkerName.Length) != 0)
                return false;

            i = SkipWhitespace(text, i + LineBeatSchedule.MarkerName.Length);

            if (i >= text.Length || text[i] != '=')
                return false;

            i = SkipWhitespace(text, i + 1);

            StringBuilder read = new();

            if (i < text.Length && text[i] == '"')
            {
                for (i++; i < text.Length && text[i] != '"'; i++)
                {
                    // 역슬래시는 다음 글자를 그대로 싣는다(\" → ").
                    if (text[i] == '\\' && i + 1 < text.Length)
                        i++;

                    read.Append(text[i]);
                }

                if (i >= text.Length)
                    return false;

                i++;
            }
            else
            {
                for (; i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '/' && text[i] != ']'; i++)
                    read.Append(text[i]);
            }

            i = SkipWhitespace(text, i);

            if (i >= text.Length || text[i] != '/')
                return false;

            i = SkipWhitespace(text, i + 1);

            if (i >= text.Length || text[i] != ']')
                return false;

            end = i + 1;
            name = read.ToString();
            return true;
        }

        private static int SkipWhitespace(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            return index;
        }

        private static bool IsFlowControl(string name)
        {
            switch (name)
            {
                // 선형성을 깨는 것만 경고한다. set/declare는 무대 상태와 무관하고
                // 분기하지도 않으므로 그냥 흘러가 리듀서의 Unhandled로 남는다.
                case "if":
                case "elseif":
                case "else":
                case "endif":
                case "jump":
                case "stop":
                    return true;

                default:
                    return false;
            }
        }

        private static List<string> SplitRespectingQuotes(string body)
        {
            List<string> tokens = new();
            StringBuilder sb = new();
            bool inQuotes = false;

            foreach (char c in body)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }

                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        private static string StripComment(string line)
        {
            int index = line.IndexOf("//", StringComparison.Ordinal);
            return index < 0 ? line : line.Substring(0, index);
        }

        private static string StripTags(string line, out string lineId)
        {
            lineId = null;

            int hash = line.IndexOf('#');

            if (hash < 0)
                return line.Trim();

            string tags = line.Substring(hash);
            string text = line.Substring(0, hash).Trim();

            foreach (string tag in tags.Split('#'))
            {
                string t = tag.Trim();

                // Yarn 런타임의 라인 ID는 "line:" 접두사를 포함한 전체다
                // (#line:abc 태그 → ID "line:abc"). 접두사를 벗기면 조회가 빗나간다.
                if (t.StartsWith("line:", StringComparison.Ordinal))
                    lineId = t;
            }

            return text;
        }
    }
}