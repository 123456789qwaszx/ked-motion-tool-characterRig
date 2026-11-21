using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ked.Presentation.Core
{
    /// <summary>대사 줄의 글 안 자리 표지 [beat=a/] 하나 — 글 순서대로 온다. 명령은 줄 앞의 &lt;&lt;at a 명령&gt;&gt;에 있다.</summary>
    public readonly struct BeatMarker
    {
        public readonly string Name;

        /// <summary>글자 자리 — 이 글자가 드러나기 직전에 실행한다. 글 끝이면 글 길이.</summary>
        public readonly int Position;

        public BeatMarker(string name, int position)
        {
            Name = name ?? string.Empty;
            Position = position;
        }
    }

    /// <summary>&lt;&lt;at 표지 명령 …&gt;&gt; 하나 — 다음 대사 줄의 그 표지 자리에서 실행할 명령.</summary>
    public readonly struct AtCommand
    {
        public readonly string Beat;
        public readonly StageCommand Command;

        public AtCommand(string beat, in StageCommand command)
        {
            Beat = beat ?? string.Empty;
            Command = command;
        }

        public override string ToString() => $"<<at {Beat} {Command.Name} {string.Join(" ", Command.Args)}>>";
    }

    /// <summary>줄의 실행 목록 한 칸 — 이 자리(글자)에 닿으면 실행한다.</summary>
    public readonly struct ScheduledCommand
    {
        /// <summary>글자 자리. 타자기 끝(end · 자리를 잃은 것)은 LineBeatSchedule.LineEnd.</summary>
        public readonly int Position;

        /// <summary>대본에 쓴 &lt;&lt;at&gt;&gt; — 명령과 로그용 글.</summary>
        public readonly AtCommand At;

        public ScheduledCommand(int position, in AtCommand at)
        {
            Position = position;
            At = at;
        }
    }

    /// <summary>앵커의 갈래 — 값의 차례가 줄 안에서 열리는 차례다(docs/PLAN_line-anchor.md AR1).</summary>
    public enum LineAnchorKind
    {
        /// <summary>앞 앵커 — 첫 글자 앞의 표지. 대사창이 열리기 전에 열린다.</summary>
        Pre = 0,

        /// <summary>표지 없이 항상 있다 — 대사창이 열리며.</summary>
        Start = 1,

        /// <summary>글 안 · 글 끝의 표지 — 타자기가 그 글자에 닿을 때.</summary>
        Text = 2,

        /// <summary>표지 없이 항상 있다 — 타자기 끝.</summary>
        End = 3,
    }

    /// <summary>&lt;&lt;gap 이름 길이&gt;&gt; 하나 — 그 앵커가 열린 뒤 다음 앵커가 열리기까지의 최소 시간(AR4). 다음 대사 줄의 것이다.</summary>
    public readonly struct GapCommand
    {
        public readonly string Anchor;
        public readonly float Seconds;

        /// <summary>어디에 썼나(파일:행) — 문제를 적을 때 쓴다.</summary>
        public readonly string Source;

        public GapCommand(string anchor, float seconds, string source = null)
        {
            Anchor = anchor ?? string.Empty;
            Seconds = seconds;
            Source = source;
        }

        public override string ToString() => $"<<gap {Anchor} {Seconds.ToString(CultureInfo.InvariantCulture)}s>>";
    }

    /// <summary>
    /// 줄의 앵커 하나 — 여기서 시작되는 뭉치(대사 조각 + 명령)가 스텝이다(docs/PLAN_line-anchor.md A1 · A2).
    /// 대사 조각은 글을 쪼개 들지 않는다 — 자리만 들고, 조각은 LineBeatSchedule.Segment가 계산한다.
    /// </summary>
    public sealed class LineAnchor
    {
        /// <summary>표지 이름 · start · end. 옛 표기의 무대 스텝을 읽은 것은 LineBeatSchedule.LegacyStepName.</summary>
        public readonly string Name;

        public readonly LineAnchorKind Kind;

        /// <summary>화자 이름을 뺀 글의 글자 자리. 앞 앵커 · start는 0, end는 LineBeatSchedule.LineEnd.</summary>
        public readonly int Position;

        /// <summary>옛 표기의 무대 스텝(&lt;&lt;step&gt;&gt;)을 읽은 앞 앵커다 — 이행이 끝나면 없다(§9 N5).</summary>
        public readonly bool Legacy;

        /// <summary>이 앵커가 열린 뒤 다음 앵커가 열리기까지의 최소 시간(초). 적지 않았으면 0.</summary>
        public float Gap;

        /// <summary>이 앵커의 명령 — &lt;&lt;at&gt;&gt;를 쓴 순서(AR2).</summary>
        public readonly List<AtCommand> Commands = new();

        public LineAnchor(string name, LineAnchorKind kind, int position, bool legacy = false)
        {
            Name = name ?? string.Empty;
            Kind = kind;
            Position = position;
            Legacy = legacy;
        }

        public override string ToString() => $"{Kind} {Name}({Commands.Count})";
    }

    /// <summary>
    /// 대사 줄 하나의 실행 목록을 짓는다 — 표지와 &lt;&lt;at&gt;&gt;를 이어 실행 순서로 늘어놓는다(docs/PLAN_line-beat.md §4).
    /// **재생(InlineCommandTrack)과 폴드(YarnCommandTextExtractor)가 같이 부른다** — 둘의 순서가 지어진 대로 같다.
    ///
    /// 순서(R2): 글자 자리 → 같은 자리면 글의 표지 순서 → 같은 표지면 &lt;&lt;at&gt;&gt;를 쓴 순서. 맨 끝에 end.
    /// 줄 머리 명령(at 없는 것)은 여기에 들지 않는다 — 그 앞에 따로 실행된다.
    ///
    /// 실패를 로그로 흘리지 않는다 — 문제는 problems에 적고, 명령은 사라지지 않게 둔다:
    ///   표지가 없는 at(R5 · 번역문이 빠뜨림)   줄 끝(end 자리)에서
    ///   at이 없는 표지(R6)                     무시
    ///   같은 표지 둘(R6)                       앞의 것
    ///   꼴이 틀린 표지 이름(R7)                무시 — 그 이름의 at은 줄 끝에서
    /// </summary>
    public static class LineBeatSchedule
    {
        /// <summary>글 안 자리 표지의 이름 — [beat=a/].</summary>
        public const string MarkerName = "beat";

        /// <summary>&lt;&lt;at&gt;&gt; 명령의 이름 — 대본 명령이 아니라 다음 줄로 넘기는 표시다.</summary>
        public const string AtCommandName = "at";

        /// <summary>예약된 자리 — 타자기 끝. 글에 표지를 두지 않는다.</summary>
        public const string EndBeat = "end";

        /// <summary>예약된 자리 — 대사창이 열리는 때(지금의 줄 머리). 글에 표지를 두지 않는다(docs/PLAN_line-anchor.md A4).</summary>
        public const string StartBeat = "start";

        /// <summary>&lt;&lt;gap&gt;&gt; 명령의 이름 — 대본 명령이 아니라 다음 줄의 앵커에 간격을 적는 표시다.</summary>
        public const string GapCommandName = "gap";

        /// <summary>타자기 끝의 자리. 글자 자리 어느 것보다 뒤다.</summary>
        public const int LineEnd = int.MaxValue;

        /// <summary>
        /// 옛 표기의 무대 스텝(&lt;&lt;step&gt;&gt;)을 읽은 앞 앵커의 이름 — 줄 앞의 몇 번째 스텝인가(0부터).
        /// 표지 이름이 될 수 없는 꼴이라 글의 표지와 겹치지 않는다.
        /// </summary>
        public static string LegacyStepName(int index) => "~" + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>표지 이름의 꼴(R7 · AR6) — 영문 소문자 · 숫자, 하나 이상. start · end는 예약이라 글에 둘 수 없다.</summary>
        public static bool IsValidBeatName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == EndBeat || name == StartBeat)
                return false;

            foreach (char c in name)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9'))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// &lt;&lt;at 표지 명령 …&gt;&gt;의 토큰(이름 뒤의 것)을 읽는다 — 첫 토큰이 표지, 그 뒤가 명령 한 줄.
        /// 명령이 없으면 false와 까닭.
        /// </summary>
        public static bool TryReadAt(IReadOnlyList<string> args, string source, out AtCommand at, out string error)
        {
            at = default;
            error = null;

            if (args == null || args.Count < 2 || string.IsNullOrEmpty(args[0]) || string.IsNullOrEmpty(args[1]))
            {
                error = "<<at 표지 명령 …>>의 꼴이 아니다 — 표지와 명령이 있어야 한다";
                return false;
            }

            string[] commandArgs = new string[args.Count - 2];

            for (int i = 2; i < args.Count; i++)
                commandArgs[i - 2] = args[i];

            at = new AtCommand(args[0], new StageCommand(args[1], commandArgs, source));
            return true;
        }

        /// <summary>
        /// &lt;&lt;gap 이름 길이&gt;&gt;의 토큰(이름 뒤의 것)을 읽는다 — 앵커 이름과 길이(1.5s · 12fr). 꼴이 아니면 false와 까닭.
        /// </summary>
        public static bool TryReadGap(IReadOnlyList<string> args, string source, out GapCommand gap, out string error)
        {
            gap = default;
            error = null;

            if (args == null || args.Count != 2 || string.IsNullOrEmpty(args[0]) ||
                !DurationToken.TryParseSeconds(args[1], out float seconds))
            {
                error = "<<gap 이름 길이>>의 꼴이 아니다 — 앵커 이름과 길이(1.5s · 12fr)가 있어야 한다";
                return false;
            }

            gap = new GapCommand(args[0], seconds, source);
            return true;
        }

        /// <summary>
        /// 줄의 앵커를 열리는 차례로 짓는다(docs/PLAN_line-anchor.md §5 · AR1) — **재생 · 접힘 · 도구가 같이 부른다.**
        ///
        ///   앞 앵커(옛 무대 스텝 → 글의 표지 순서) → start → 글 표지(글자 자리 → 같은 자리면 글의 표지 순서) → end
        ///
        /// markers는 글 순서이고 자리는 화자 이름을 뺀 글의 것이다 — 자리 0(첫 글자 앞)의 표지가 앞 앵커다(A5).
        /// ats · gaps는 쓴 순서. start · end는 명령이 없어도 언제나 있다(A4).
        /// legacySteps는 옛 표기로 줄 앞에 닫힌 무대 스텝의 수 — 그 명령은 ats에 LegacyStepName으로 실려 온다.
        ///
        /// 실패를 로그로 흘리지 않는다 — 문제는 problems에 적고, 명령은 사라지지 않게 둔다:
        ///   표지가 없는 at(AR9 · 번역문이 빠뜨림)   end에서
        ///   표지가 없는 gap · 같은 이름의 gap 둘     버린다 · 앞의 것
        ///   at도 gap도 없는 표지                     무시(앵커가 서지 않는다)
        ///   같은 표지 둘 · 꼴이 틀린 표지 이름       앞의 것 · 무시
        /// </summary>
        public static List<LineAnchor> BuildAnchors(
            IReadOnlyList<BeatMarker> markers,
            IReadOnlyList<AtCommand> ats,
            IReadOnlyList<GapCommand> gaps = null,
            List<string> problems = null,
            int legacySteps = 0)
        {
            Dictionary<string, LineAnchor> byName = new(StringComparer.Ordinal);
            List<LineAnchor> pre = new();
            List<LineAnchor> text = new();
            Dictionary<LineAnchor, int> textOrder = new();

            for (int k = 0; k < legacySteps; k++)
            {
                LineAnchor step = new(LegacyStepName(k), LineAnchorKind.Pre, 0, legacy: true);
                pre.Add(step);
                byName.Add(step.Name, step);
            }

            for (int k = 0; k < (markers?.Count ?? 0); k++)
            {
                BeatMarker marker = markers[k];

                if (!IsValidBeatName(marker.Name))
                {
                    problems?.Add(marker.Name == EndBeat
                        ? "[beat=end/] — end는 타자기 끝의 예약 이름이라 글에 두지 않는다. 무시한다"
                        : marker.Name == StartBeat
                            ? "[beat=start/] — start는 대사창이 열리는 때의 예약 이름이라 글에 두지 않는다. 무시한다"
                            : $"[beat={marker.Name}/] — 표지 이름은 영문 소문자 · 숫자다. 무시한다");
                    continue;
                }

                if (byName.ContainsKey(marker.Name))
                {
                    problems?.Add($"[beat={marker.Name}/]가 한 줄에 둘이다 — 앞의 것을 쓴다");
                    continue;
                }

                LineAnchor anchor;

                if (marker.Position <= 0)
                {
                    anchor = new LineAnchor(marker.Name, LineAnchorKind.Pre, 0);
                    pre.Add(anchor);
                }
                else
                {
                    anchor = new LineAnchor(marker.Name, LineAnchorKind.Text, marker.Position);
                    text.Add(anchor);
                    textOrder.Add(anchor, k);
                }

                byName.Add(marker.Name, anchor);
            }

            // 열쇠가 모두 달라(글의 표지 순서가 가른다) 순서가 하나로 정해진다.
            text.Sort((a, b) =>
            {
                int c = a.Position.CompareTo(b.Position);
                return c != 0 ? c : textOrder[a].CompareTo(textOrder[b]);
            });

            LineAnchor start = new(StartBeat, LineAnchorKind.Start, 0);
            LineAnchor end = new(EndBeat, LineAnchorKind.End, LineEnd);
            byName.Add(StartBeat, start);
            byName.Add(EndBeat, end);

            for (int w = 0; w < (ats?.Count ?? 0); w++)
            {
                AtCommand at = ats[w];

                if (!byName.TryGetValue(at.Beat, out LineAnchor anchor))
                {
                    problems?.Add($"{at} — 줄에 [beat={at.Beat}/]가 없다. 줄 끝에서 실행한다");
                    anchor = end;
                }

                anchor.Commands.Add(at);
            }

            HashSet<LineAnchor> gapped = new();

            for (int g = 0; g < (gaps?.Count ?? 0); g++)
            {
                GapCommand gap = gaps[g];

                if (!byName.TryGetValue(gap.Anchor, out LineAnchor anchor))
                {
                    problems?.Add($"{gap} — 줄에 [beat={gap.Anchor}/]가 없다. 간격을 버린다");
                    continue;
                }

                if (!gapped.Add(anchor))
                {
                    problems?.Add($"<<gap {gap.Anchor} …>>가 한 줄에 둘이다 — 앞의 것을 쓴다");
                    continue;
                }

                anchor.Gap = gap.Seconds;
            }

            List<LineAnchor> anchors = new(pre.Count + text.Count + 2);

            void AddUsed(List<LineAnchor> candidates)
            {
                foreach (LineAnchor anchor in candidates)
                {
                    if (anchor.Commands.Count > 0 || gapped.Contains(anchor))
                    {
                        anchors.Add(anchor);
                        continue;
                    }

                    // 빈 무대 스텝은 스텝이 아니다(지금 그대로). 표지는 적어 둔다.
                    if (!anchor.Legacy)
                        problems?.Add($"[beat={anchor.Name}/]에 <<at {anchor.Name} …>>가 없다 — 무시한다");
                }
            }

            AddUsed(pre);
            anchors.Add(start);
            AddUsed(text);
            anchors.Add(end);

            return anchors;
        }

        /// <summary>앵커의 명령을 실행 순서로 편다 — 앵커의 차례, 그 안에서는 쓴 순서(AR1 · AR2).</summary>
        public static List<AtCommand> Flatten(IReadOnlyList<LineAnchor> anchors)
        {
            List<AtCommand> commands = new();

            for (int i = 0; i < (anchors?.Count ?? 0); i++)
                commands.AddRange(anchors[i].Commands);

            return commands;
        }

        /// <summary>
        /// 그 앵커(스텝)에서 나오는 대사 조각 — body는 화자 이름과 표지를 뺀 그 언어의 글이다.
        /// 앞 앵커 · end에는 글이 없다. start는 첫 글 표지 앞까지, 글 표지는 다음 글 표지 앞까지다.
        /// </summary>
        public static string Segment(string body, IReadOnlyList<LineAnchor> anchors, int index)
        {
            if (string.IsNullOrEmpty(body) || anchors == null || index < 0 || index >= anchors.Count)
                return string.Empty;

            LineAnchor anchor = anchors[index];

            if (anchor.Kind != LineAnchorKind.Start && anchor.Kind != LineAnchorKind.Text)
                return string.Empty;

            int from = anchor.Kind == LineAnchorKind.Start ? 0 : Math.Min(anchor.Position, body.Length);
            int to = body.Length;

            for (int next = index + 1; next < anchors.Count; next++)
            {
                if (anchors[next].Kind != LineAnchorKind.Text)
                    continue;

                to = Math.Min(anchors[next].Position, body.Length);
                break;
            }

            return from < to ? body.Substring(from, to - from) : string.Empty;
        }

        /// <summary>
        /// markers는 글 순서, ats는 쓴 순서. 결과는 실행 순서다.
        /// 재생(InlineCommandTrack)이 앵커(BuildAnchors)로 옮겨 가면 걷는다 — docs/PLAN_line-anchor.md N2.
        /// </summary>
        public static List<ScheduledCommand> Build(
            IReadOnlyList<BeatMarker> markers,
            IReadOnlyList<AtCommand> ats,
            List<string> problems = null)
        {
            // 표지 이름 → (글자 자리, 글의 표지 순서). 같은 표지가 둘이면 앞의 것.
            Dictionary<string, (int Position, int TextOrder)> beats = new(StringComparer.Ordinal);
            HashSet<string> used = new(StringComparer.Ordinal);

            for (int k = 0; k < (markers?.Count ?? 0); k++)
            {
                BeatMarker marker = markers[k];

                if (!IsValidBeatName(marker.Name))
                {
                    problems?.Add(marker.Name == EndBeat
                        ? "[beat=end/] — end는 타자기 끝의 예약 이름이라 글에 두지 않는다. 무시한다"
                        : $"[beat={marker.Name}/] — 표지 이름은 영문 소문자 · 숫자다. 무시한다");
                    continue;
                }

                if (beats.ContainsKey(marker.Name))
                {
                    problems?.Add($"[beat={marker.Name}/]가 한 줄에 둘이다 — 앞의 것을 쓴다");
                    continue;
                }

                beats.Add(marker.Name, (marker.Position, k));
            }

            List<Keyed> keyed = new();

            for (int w = 0; w < (ats?.Count ?? 0); w++)
            {
                AtCommand at = ats[w];

                if (at.Beat == EndBeat)
                {
                    keyed.Add(new Keyed(LineEnd, int.MaxValue, w, new ScheduledCommand(LineEnd, at)));
                    continue;
                }

                if (beats.TryGetValue(at.Beat, out var beat))
                {
                    used.Add(at.Beat);
                    keyed.Add(new Keyed(beat.Position, beat.TextOrder, w, new ScheduledCommand(beat.Position, at)));
                    continue;
                }

                problems?.Add($"{at} — 줄에 [beat={at.Beat}/]가 없다. 줄 끝에서 실행한다");
                keyed.Add(new Keyed(LineEnd, int.MaxValue, w, new ScheduledCommand(LineEnd, at)));
            }

            foreach (string name in beats.Keys)
            {
                if (!used.Contains(name))
                    problems?.Add($"[beat={name}/]에 <<at {name} …>>가 없다 — 무시한다");
            }

            // 열쇠가 모두 달라(쓴 순서가 가른다) 순서가 하나로 정해진다.
            keyed.Sort((a, b) =>
            {
                int c = a.Position.CompareTo(b.Position);

                if (c != 0)
                    return c;

                c = a.TextOrder.CompareTo(b.TextOrder);
                return c != 0 ? c : a.Written.CompareTo(b.Written);
            });

            List<ScheduledCommand> schedule = new(keyed.Count);

            foreach (Keyed item in keyed)
                schedule.Add(item.Command);

            return schedule;
        }

        private readonly struct Keyed
        {
            public readonly int Position;
            public readonly int TextOrder;
            public readonly int Written;
            public readonly ScheduledCommand Command;

            public Keyed(int position, int textOrder, int written, in ScheduledCommand command)
            {
                Position = position;
                TextOrder = textOrder;
                Written = written;
                Command = command;
            }
        }
    }
}
