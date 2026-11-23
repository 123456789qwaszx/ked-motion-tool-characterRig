using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 원문에서의 자리 — 줄 번호(1부터, 양 끝 포함)와 글자 자리. 읽을 때마다 푸는 위치다 —
    /// 주소가 아니다(고치면 밀린다. 주소는 LineId / 앵커 / 종류 / 순번이다).
    /// </summary>
    public readonly struct SourceSpan
    {
        public readonly int FirstLine;

        /// <summary>마지막 줄(포함). 빈 범위면 FirstLine - 1.</summary>
        public readonly int LastLine;

        /// <summary>원문(받은 글 그대로)에서 FirstLine이 시작하는 글자 자리.</summary>
        public readonly int Start;

        /// <summary>글자 수 — 줄바꿈까지 든다. Start + Length가 다음 줄의 머리다.</summary>
        public readonly int Length;

        public SourceSpan(int firstLine, int lastLine, int start, int length)
        {
            FirstLine = firstLine;
            LastLine = lastLine;
            Start = start;
            Length = length;
        }

        public bool IsEmpty => Length == 0;

        public override string ToString() => IsEmpty ? $"{FirstLine}(빈 범위)" : $"{FirstLine}–{LastLine}";
    }

    /// <summary>줄의 커맨드 하나 — 명세 표의 칸 · 종류 · 그 앵커가 열린 뒤의 지연 · 원문에서의 줄.</summary>
    public sealed class LineReadCommand
    {
        public string Name;

        /// <summary>대본에 쓴 인자 그대로.</summary>
        public IReadOnlyList<string> Args = Array.Empty<string>();

        /// <summary>명세 표의 항목. 표에 없는 이름이면 null — 종류도 칸도 모른다.</summary>
        public StageCommandSpec Spec;

        /// <summary>인자를 명세 표의 칸에 맞춘 것. 표에 없으면 비어 있다.</summary>
        public List<BoundArg> Bound = new();

        /// <summary>이 커맨드가 속한 앵커의 이름. 앵커 밖(바로 실행하는 명령 · at 없는 명령)이면 null.</summary>
        public string Anchor;

        /// <summary>
        /// 앵커가 열린 뒤 이 커맨드가 시작하기까지(초) — 같은 앵커에서 앞선 기다리는 명령(pause · screen_cover)의 합.
        /// 종류로 걸러 보아도 언제 시작하는지가 남는다.
        /// </summary>
        public float Delay;

        /// <summary>대본에서 읽은 길이(초) — 길이 칸에 쓴 값이나 표의 생략값. 대본에 없으면 null(모션 · 이모지의 클립, 즉시).</summary>
        public float? Duration;

        /// <summary>기다리는 명령인가 — 끝날 때까지 그 앵커의 뒤 커맨드를 붙잡는다.</summary>
        public bool Waits;

        /// <summary>뒤 커맨드를 붙잡는 시간(초). 기다리는 명령이 아니면 0.</summary>
        public float Hold;

        /// <summary>원문에서의 줄 번호(1부터).</summary>
        public int Line;

        /// <summary>그 앵커의 같은 종류 안에서 몇 번째인가(0부터) — 주소의 마지막 칸. 표에 없으면 -1.</summary>
        public int KindIndex = -1;

        public CommandKind? Kind => Spec?.Kind;

        public override string ToString()
            => $"+{Delay.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}s {Name} {string.Join(" ", Args)}".TrimEnd();
    }

    /// <summary>줄의 스텝 하나 — 앵커 하나에서 시작되는 뭉치(대사 조각 + 커맨드, docs/PLAN_line-anchor.md A1).</summary>
    public sealed class LineReadStep
    {
        /// <summary>앵커의 이름 — 표지 이름 · start · end.</summary>
        public string Name;

        public LineAnchorKind Kind;

        /// <summary>화자 이름을 뺀 글의 글자 자리. 앞 앵커 · start는 0, end는 LineBeatSchedule.LineEnd.</summary>
        public int Position;

        /// <summary>이 스텝에서 나오는 대사 조각. 앞 앵커 · end에는 글이 없다.</summary>
        public string Text = string.Empty;

        /// <summary>간격(초) — 이 앵커가 열린 뒤 다음 앵커가 열리기까지의 최소 시간. 적지 않았으면 0.</summary>
        public float Gap;

        /// <summary>&lt;&lt;gap&gt;&gt;을 쓴 줄 번호. 적지 않았으면 0.</summary>
        public int GapLine;

        /// <summary>이 앵커의 커맨드가 모두 시작되기까지(초) — 기다리는 명령의 합(AR3 ①).</summary>
        public float Settle;

        public readonly List<LineReadCommand> Commands = new();

        public bool HasGap => GapLine > 0;

        /// <summary>
        /// 앵커의 길이(초) — 다음 앵커가 열릴 수 있기까지의 최소 시간. 간격은 앵커가 열린 때부터 재므로
        /// 간격과 기다림의 합 가운데 큰 쪽이다(AR3 ① · ②). 글이 있는 스텝은 글이 쳐지는 동안 더 늘어난다.
        /// </summary>
        public float Length => Math.Max(Gap, Settle);

        public override string ToString() => $"{Kind} {Name}({Commands.Count})";
    }

    /// <summary>
    /// 줄 하나를 읽은 것 — 스텝의 목록(ked-story-tools docs/PLAN.md §4.2 · K2).
    /// 주소는 LineId / 스텝의 이름 / 종류 / KindIndex다.
    /// </summary>
    public sealed class LineRead
    {
        /// <summary>#line 태그 값 — 접두사를 포함한 전체("line:s1_secret"). 태그가 없으면 null.</summary>
        public string LineId;

        public string NodeName;

        /// <summary>화자 이름. 없으면 빈 글.</summary>
        public string Speaker = string.Empty;

        /// <summary>화자 이름과 표지를 뺀 글 — 스텝의 글자 자리가 이 글의 자리다.</summary>
        public string Body = string.Empty;

        /// <summary>대사 줄의 줄 번호(1부터).</summary>
        public int Line;

        /// <summary>대사 줄의 들여쓰기 — 묶음을 다시 쓸 때 맞춘다.</summary>
        public string Indent = string.Empty;

        /// <summary>
        /// 이 줄의 커맨드 묶음(&lt;&lt;at&gt;&gt; · &lt;&lt;gap&gt;&gt; · 앵커 밖의 것)이 원문의 어디서 어디까지인가 — 대사 줄은 들지 않는다.
        /// 툴은 이 범위만 다시 쓴다. 묶음이 없으면 빈 범위이고 Start는 대사 줄의 머리다(끼워 넣을 자리).
        /// 묶음 사이의 빈 줄 · 주석 줄은 범위에 든다.
        /// </summary>
        public SourceSpan Block;

        /// <summary>스텝 — 열리는 차례. start · end는 커맨드가 없어도 있다.</summary>
        public readonly List<LineReadStep> Steps = new();

        /// <summary>
        /// 바로 실행하는 명령 — 앵커에 속하지 않는다. Yarn이 만난 때(이 줄이 도착하기 전) 실행된다
        /// (명세 표의 Immediate — surface_layout 같은 것과, Yarn의 set · declare · call · wait).
        /// </summary>
        public readonly List<LineReadCommand> Immediate = new();

        /// <summary>
        /// at 없이 쓴 무대 명령 — 원문 검사의 오류다. 재생은 실행하지 않는다(docs/PLAN_line-anchor.md §9 N5).
        /// 묶음의 범위(Block)에는 든다 — 원문의 그 줄은 누가 지우기 전까지 남는다(실행되지 않을 뿐이다).
        /// </summary>
        public readonly List<LineReadCommand> Unanchored = new();

        /// <summary>이 줄에서 찾은 문제 — 짝이 없는 at · gap, at 없는 명령, 표에 없는 명령, at을 붙인 즉시 명령 ….</summary>
        public readonly List<string> Problems = new();

        /// <summary>이름으로 스텝을 찾는다. 없으면 null.</summary>
        public LineReadStep Step(string name)
        {
            foreach (LineReadStep step in Steps)
            {
                if (step.Name == name)
                    return step;
            }

            return null;
        }
    }

    /// <summary>yarn 파일 하나를 읽은 것 — 대사 줄마다 하나. 분기 · 선택지가 있어도 된다.</summary>
    public sealed class YarnFileRead
    {
        /// <summary>대사 줄 — 파일에 쓴 차례(재생 순서가 아니다).</summary>
        public readonly List<LineRead> Lines = new();

        /// <summary>줄에 속하지 않는 문제 — 붙을 줄이 없는 at · gap, 겹친 LineId, 읽지 못한 커맨드.</summary>
        public readonly List<string> Problems = new();

        private readonly Dictionary<string, LineRead> _byId = new(StringComparer.Ordinal);

        /// <summary>LineId로 찾는다 — "line:abc"도 "abc"도 받는다.</summary>
        public bool TryGet(string lineId, out LineRead line)
        {
            line = null;

            if (string.IsNullOrEmpty(lineId))
                return false;

            return _byId.TryGetValue(YarnLineReader.NormalizeLineId(lineId), out line);
        }

        internal void Add(LineRead line, string source)
        {
            Lines.Add(line);

            if (line.LineId == null)
                return;

            if (_byId.ContainsKey(line.LineId))
            {
                Problems.Add($"{source} #{line.LineId}가 이 파일에 둘이다 — 앞의 것으로 찾는다");
                return;
            }

            _byId.Add(line.LineId, line);
        }
    }
}
