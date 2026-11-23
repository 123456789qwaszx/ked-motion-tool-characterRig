using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 타자기의 기준 속도 — 글자 하나가 보인 뒤 다음 글자까지의 시간을 셈한다.
    /// 재생의 타자기(Playback의 EllipsisBreathTypewriter)와 같은 식 · 같은 기본값이다. 다른 것은 둘이다:
    ///   흔들림(jitter)이 없다 — 시간표는 기준값이다.
    ///   글자를 TMP가 아니라 글에서 센다 — 리치 텍스트 태그가 있는 줄에서는 어긋난다.
    /// 값은 프로젝트의 것이다(예제 씬은 UnitsPerSecond 20). 툴이 그 프로젝트의 값을 넣는다.
    /// </summary>
    public sealed class TypewriterPacing
    {
        /// <summary>초당 글자 수.</summary>
        public float UnitsPerSecond = 30f;

        public float MultWhitespace = 0.35f;
        public float MultComma = 1.7f;
        public float MultColonSemicolon = 2.0f;
        public float MultPeriod = 3.2f;
        public float MultQuestionExclaim = 3.6f;
        public float MultEllipsisChar = 5.2f;

        /// <summary>"..." 세 글자를 한 덩어리로 칠 때의 배수(마침표의 배수보다 작으면 마침표의 것).</summary>
        public float MultTripleDotToken = 0.8f;

        /// <summary>줄 머리에서 천천히 시작하는 덩어리 수. 0이면 끈다.</summary>
        public int StartRampUnits = 10;

        /// <summary>줄 머리의 느려짐 배수(1이면 없다).</summary>
        public float StartSlowdownMultiplier = 1.25f;

        /// <summary>한 덩어리 뒤의 기다림의 상한(초).</summary>
        public float DelayCap = 0.35f;

        /// <summary>start가 열린 뒤 첫 글자가 쳐지기까지(초) — 대사창이 열리는 시간. 재생에서 재서 넣는다.</summary>
        public float StartDelay;

        public static TypewriterPacing Default => new();

        /// <summary>
        /// 글을 타자기의 덩어리로 나눈다 — 덩어리마다 "여기까지 보인다"의 글자 수. 보통은 한 글자, "..."는 셋이 한 덩어리다.
        /// </summary>
        public static List<int> RevealCounts(string body)
        {
            int count = body?.Length ?? 0;
            List<int> result = new(Math.Max(8, count));
            int i = 0;

            while (i < count)
            {
                int tokenLength = body[i] == '.' && i + 2 < count && body[i + 1] == '.' && body[i + 2] == '.' ? 3 : 1;
                result.Add(i + tokenLength);
                i += tokenLength;
            }

            return result;
        }

        /// <summary>unit번째 덩어리가 보인 뒤의 기다림(초).</summary>
        public double DelayAfter(string body, List<int> revealCounts, int unit)
        {
            if (UnitsPerSecond <= 0f)
                return 0.0;

            double delay = 1.0 / UnitsPerSecond;
            int target = revealCounts[unit];
            int previous = unit > 0 ? revealCounts[unit - 1] : 0;
            int tokenLength = Math.Max(1, Math.Min(8, target - previous));
            char last = target - 1 < body.Length ? body[target - 1] : '\0';

            double multiplier = Multiplier(last);

            if (tokenLength == 3 && last == '.')
                multiplier = Math.Max(multiplier, MultTripleDotToken);

            if (StartRampUnits > 0 && StartSlowdownMultiplier > 1f && unit < StartRampUnits)
            {
                float progress = StartRampUnits <= 1 ? 1f : unit / (float)(StartRampUnits - 1);
                multiplier *= StartSlowdownMultiplier + (1f - StartSlowdownMultiplier) * Math.Max(0f, Math.Min(1f, progress));
            }

            return Math.Max(0.0, Math.Min(delay * multiplier, Math.Max(0f, DelayCap)));
        }

        private double Multiplier(char c)
        {
            if (c == ' ' || c == '\t' || c == ' ')
                return MultWhitespace;

            if (c == ',' || c == '，')
                return MultComma;

            if (c == ':' || c == ';')
                return MultColonSemicolon;

            if (c == '.' || c == '。')
                return MultPeriod;

            if (c == '?' || c == '？' || c == '!' || c == '！')
                return MultQuestionExclaim;

            if (c == '…')
                return MultEllipsisChar;

            return 1.0;
        }
    }

    /// <summary>시간표의 스텝 하나 — 그 앵커가 열리는 때.</summary>
    public sealed class TimetableStep
    {
        public LineReadStep Step;

        /// <summary>줄이 도착한 때부터 이 앵커가 열리기까지(초).</summary>
        public float Open;

        /// <summary>
        /// 어림인가 — 글이 쳐지는 시간에 걸린 앵커(글 표지 · end)는 타자기 속도로 셈한 값이다.
        /// 글이 없는 구간(앞 앵커 · start)만 정확하다.
        /// </summary>
        public bool Estimated;

        public int OpenFrame => LineTimetable.Frame(Open);
    }

    /// <summary>시간표의 커맨드 하나 — 시작하는 때와 길이.</summary>
    public sealed class TimetableCommand
    {
        public LineReadCommand Command;
        public LineReadStep Step;

        /// <summary>줄이 도착한 때부터 이 커맨드가 시작하기까지(초) — 앵커가 열린 때 + 지연.</summary>
        public float Start;

        /// <summary>길이(초). 대본에 없고 lengthOf도 답하지 않으면 null — 모르는 길이다(모션 · 이모지의 클립).</summary>
        public float? Duration;

        public bool Estimated;

        public int StartFrame => LineTimetable.Frame(Start);

        /// <summary>끝나는 프레임. 길이를 모르면 시작 프레임.</summary>
        public int EndFrame => LineTimetable.Frame(Start + (Duration ?? 0f));
    }

    /// <summary>
    /// 줄 하나의 프레임 시간표 — **보통 재생의 기준 시간표**다(ked-story-tools docs/PLAN.md §4.6 · §6).
    ///
    /// 계산해서 보여 주는 면이다. 저장하지 않고, 런타임의 재생은 이것을 쓰지 않는다 — 재생은 순서와 최소 간격으로 돈다
    /// (docs/PLAN_line-anchor.md AR3). 기준 속도의 보통 재생에서만 참이고(서두르기 · 빨리 넘기기 · 번역문에서는 달라진다),
    /// 글이 없는 앞 앵커 구간에서만 정확하다.
    ///
    ///   앞 앵커     앞 앵커가 열린 때 + 그 길이(간격과 기다림의 합 가운데 큰 쪽)
    ///   start       앞 앵커가 모두 끝난 뒤. 타자기는 여기서 StartDelay 뒤에 시작한다
    ///   글 표지     타자기가 그 글자에 닿은 때. 앞 앵커의 길이가 차지 않았으면 그때까지 타자기가 선다
    ///   end         타자기가 끝난 때(앞 앵커의 길이가 찬 뒤)
    ///   넘김        end가 열린 때 + end의 길이
    /// </summary>
    public sealed class LineTimetable
    {
        public readonly List<TimetableStep> Steps = new();
        public readonly List<TimetableCommand> Commands = new();

        /// <summary>타자기가 첫 글자를 치는 때(초).</summary>
        public float TypingStart;

        /// <summary>타자기가 끝나는 때(초) — 앵커의 길이를 기다린 시간이 든다.</summary>
        public float TypingEnd;

        /// <summary>넘김을 받을 수 있는 때(초) — 줄의 길이.</summary>
        public float Ready;

        public int ReadyFrame => Frame(Ready);

        /// <summary>초 → 프레임(30fps — DurationToken.FramesPerSecond). 가장 가까운 프레임.</summary>
        public static int Frame(float seconds)
            => (int)Math.Round(seconds * DurationToken.FramesPerSecond, MidpointRounding.AwayFromZero);

        /// <summary>
        /// 줄 읽기의 결과에서 시간표를 낸다. 순수 함수다.
        /// lengthOf는 대본에 길이가 없는 커맨드(모션 · 이모지의 클립)의 길이를 답한다 — 모르면 null.
        /// </summary>
        public static LineTimetable Build(
            LineRead line,
            TypewriterPacing pacing = null,
            Func<LineReadCommand, float?> lengthOf = null)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            pacing ??= TypewriterPacing.Default;

            LineTimetable table = new();
            string body = line.Body ?? string.Empty;
            List<int> reveal = TypewriterPacing.RevealCounts(body);

            // 다음 앵커가 열릴 수 있는 가장 이른 때 — 앞 앵커가 열린 때 + 그 길이(AR3 ① · ②).
            double gate = 0.0;
            double clock = 0.0;
            int unit = 0;
            int visible = 0;
            bool typing = false;

            void Open(LineReadStep step, double at, bool estimated)
            {
                table.Steps.Add(new TimetableStep { Step = step, Open = (float)at, Estimated = estimated });

                foreach (LineReadCommand command in step.Commands)
                {
                    table.Commands.Add(new TimetableCommand
                    {
                        Command = command,
                        Step = step,
                        Start = (float)(at + command.Delay),
                        Duration = command.Duration ?? lengthOf?.Invoke(command),
                        Estimated = estimated,
                    });
                }

                gate = at + step.Length;
            }

            // 타자기를 position 글자 앞까지 돌린다 — 그 글자가 보이기 직전의 때를 돌려준다.
            double TypeTo(int position)
            {
                while (unit < reveal.Count && visible < position)
                {
                    // 덩어리 안의 글자("..."의 둘째 · 셋째)는 기다림 없이 이어 보인다 — 그 자리의 표지는 덩어리의 머리에서 열린다.
                    if (position < reveal[unit])
                        break;

                    visible = reveal[unit];
                    clock += pacing.DelayAfter(body, reveal, unit);
                    unit++;
                }

                return clock;
            }

            foreach (LineReadStep step in line.Steps)
            {
                switch (step.Kind)
                {
                    case LineAnchorKind.Pre:
                        Open(step, gate, estimated: false);
                        break;

                    case LineAnchorKind.Start:
                        Open(step, gate, estimated: false);
                        clock = table.Steps[table.Steps.Count - 1].Open + Math.Max(0f, pacing.StartDelay);
                        table.TypingStart = (float)clock;
                        typing = true;
                        break;

                    case LineAnchorKind.Text:
                    {
                        double reached = TypeTo(Math.Min(step.Position, body.Length));

                        // 앞 앵커의 길이가 차지 않았으면 타자기가 그 글자 앞에서 선다(I7).
                        if (gate > reached)
                            clock = gate;

                        Open(step, clock, estimated: true);
                        break;
                    }

                    case LineAnchorKind.End:
                    {
                        double finished = typing ? TypeTo(int.MaxValue) : gate;
                        table.TypingEnd = (float)finished;

                        Open(step, Math.Max(finished, gate), estimated: body.Length > 0);
                        break;
                    }
                }
            }

            table.Ready = (float)gate;
            return table;
        }
    }
}
