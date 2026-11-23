using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 커맨드의 종류 — 연출가가 화면에서 무엇을 고치는가(ked-story-tools docs/PLAN.md §4.1).
    /// 팩토리의 갈래(누가 실행하는가)와 1:1이 아니라 명세 표가 커맨드마다 하나씩 적는다.
    /// 기준은 리그다 — 카메라 리그는 카메라, 캐릭터 리그의 포커스 · 슬롯 계층은 자리와 구도,
    /// 초상 계층의 연기 축은 연기, 스테이지 리그와 화면 전체는 화면. 초상의 얼굴(표정)과 인물 곁의 감정 아이콘(이모지)은
    /// 연기에서 떼어 제 종류로 둔다 — 연출가가 따로 고친다(ked-story-tools docs/PLAN.md D13).
    /// </summary>
    public enum CommandKind
    {
        /// <summary>카메라 — 카메라 리그의 축과 흔들림.</summary>
        Camera,

        /// <summary>자리와 구도 — 누가 어디에 얼마나 크게 어느 쪽을 보고 서 있는가.</summary>
        Placement,

        /// <summary>표정 — 초상의 얼굴을 바꾼다(face · face_swap).</summary>
        Expression,

        /// <summary>연기 — 포즈 · 모션 · 초상 계층의 연기 축.</summary>
        Acting,

        /// <summary>이모지 — 인물 곁의 감정 아이콘을 세우고 내린다(emoji · emoji_off).</summary>
        Emoji,

        /// <summary>화면 — 스테이지 리그(마스크 · 블러 · 어둡게)와 화면 전체(덮개 · 섬광 · 컷인 · 팝업 · 배경).</summary>
        Screen,

        /// <summary>세우기 — 장면을 차린다(슬롯 · 캐스팅 · 배경 · 대사창).</summary>
        Setup,

        /// <summary>소리 — BGM · 효과음 · 보이스.</summary>
        Sound,

        /// <summary>시간 — 같은 앵커의 뒤 커맨드를 늦춘다(pause).</summary>
        Time,
    }

    /// <summary>종류의 이름표 — 내보내는 JSON의 키와 화면에 보이는 낱말.</summary>
    public static class CommandKinds
    {
        public static readonly CommandKind[] All =
        {
            CommandKind.Camera, CommandKind.Placement, CommandKind.Expression, CommandKind.Acting, CommandKind.Emoji,
            CommandKind.Screen, CommandKind.Setup, CommandKind.Sound, CommandKind.Time,
        };

        public static string Key(CommandKind kind)
        {
            switch (kind)
            {
                case CommandKind.Camera: return "camera";
                case CommandKind.Placement: return "placement";
                case CommandKind.Expression: return "expression";
                case CommandKind.Acting: return "acting";
                case CommandKind.Emoji: return "emoji";
                case CommandKind.Screen: return "screen";
                case CommandKind.Setup: return "setup";
                case CommandKind.Sound: return "sound";
                case CommandKind.Time: return "time";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static string Label(CommandKind kind)
        {
            switch (kind)
            {
                case CommandKind.Camera: return "카메라";
                case CommandKind.Placement: return "자리와 구도";
                case CommandKind.Expression: return "표정";
                case CommandKind.Acting: return "연기";
                case CommandKind.Emoji: return "이모지";
                case CommandKind.Screen: return "화면";
                case CommandKind.Setup: return "세우기";
                case CommandKind.Sound: return "소리";
                case CommandKind.Time: return "시간";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>연출가의 종류인가 — 종류 필터에 오르는 것(카메라 · 자리와 구도 · 표정 · 연기 · 이모지 · 화면).</summary>
        public static bool IsDirectorKind(CommandKind kind)
            => kind == CommandKind.Camera || kind == CommandKind.Placement || kind == CommandKind.Expression ||
               kind == CommandKind.Acting || kind == CommandKind.Emoji || kind == CommandKind.Screen;

        public static bool TryParse(string key, out CommandKind kind)
        {
            foreach (CommandKind candidate in All)
            {
                if (Key(candidate) == key)
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = default;
            return false;
        }
    }

    /// <summary>인자의 갈래 — 툴이 그 칸을 어떻게 보여 주고 고치는가.</summary>
    public enum ArgKind
    {
        /// <summary>슬롯 키(s1 · bg_main · portrait) · 별칭.</summary>
        Slot,

        /// <summary>거리 — 단위 토큰(1.5u · -2u).</summary>
        Distance,

        /// <summary>각도 — 도.</summary>
        Angle,

        /// <summary>배율 — 1이 본래 크기.</summary>
        Scale,

        /// <summary>길이 — 시간 토큰(12fr · 0.4s).</summary>
        Duration,

        /// <summary>이징 이름. 비우면 그 커맨드의 기본 이징.</summary>
        Ease,

        /// <summary>이름 — 목록에서 고른다(표정 · 모션 · 이모지 · 프리셋 · 무대 · 층 …). 어느 목록인지는 Catalog.</summary>
        Name,

        /// <summary>토큰 — 정해진 낱말 가운데 하나(keep_y · relative|absolute). 낱말은 Choices.</summary>
        Token,

        /// <summary>수 — 단위 없는 값(세기 · 불투명도).</summary>
        Number,

        /// <summary>깊이 레벨 — 수(14) · 라벨(front) · x배율(x1.04) 가운데 하나(SizeToken).</summary>
        Level,
    }

    public static class ArgKinds
    {
        public static readonly ArgKind[] All =
        {
            ArgKind.Slot, ArgKind.Distance, ArgKind.Angle, ArgKind.Scale, ArgKind.Duration,
            ArgKind.Ease, ArgKind.Name, ArgKind.Token, ArgKind.Number, ArgKind.Level,
        };

        public static string Key(ArgKind kind)
        {
            switch (kind)
            {
                case ArgKind.Slot: return "slot";
                case ArgKind.Distance: return "distance";
                case ArgKind.Angle: return "angle";
                case ArgKind.Scale: return "scale";
                case ArgKind.Duration: return "duration";
                case ArgKind.Ease: return "ease";
                case ArgKind.Name: return "name";
                case ArgKind.Token: return "token";
                case ArgKind.Number: return "number";
                case ArgKind.Level: return "level";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static bool TryParse(string key, out ArgKind kind)
        {
            foreach (ArgKind candidate in All)
            {
                if (Key(candidate) == key)
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = default;
            return false;
        }
    }

    /// <summary>커맨드의 인자 칸 하나 — 이름 · 갈래 · 생략값.</summary>
    public sealed class CommandArgSpec
    {
        /// <summary>칸의 키(영문) — 그 커맨드 안에서 유일하다.</summary>
        public readonly string Name;

        /// <summary>화면에 보이는 이름.</summary>
        public readonly string Label;

        public readonly ArgKind Kind;

        /// <summary>
        /// 파서 표(StageCommandParser.Table)의 생략값 — 글자 그대로. null이면 표에 생략값이 없다.
        /// 빈 문자열은 "비워 둔다"가 생략값이라는 뜻이다(이징 — 그 커맨드의 기본 이징으로 간다).
        /// </summary>
        public readonly string Default;

        /// <summary>적지 않아도 되는가. 표에 생략값이 있으면 언제나 참이고, 없어도 핸들러가 제 값으로 가면 참이다(Note에 적는다).</summary>
        public readonly bool Optional;

        /// <summary>갈래가 이름일 때 — 어느 목록에서 고르는가(face · motion · emoji · stage · layer …).</summary>
        public readonly string Catalog;

        /// <summary>갈래가 토큰일 때 — 고를 수 있는 낱말.</summary>
        public readonly IReadOnlyList<string> Choices;

        /// <summary>
        /// 낱말이 맞을 때만 자리를 차지하는 칸이다 — 그 낱말이 아니면 이 칸은 없는 것이고 뒤 칸이 당겨진다
        /// (size의 keep_y: 셋째 칸이 keep_y가 아니면 길이다).
        /// </summary>
        public readonly bool Flag;

        /// <summary>덧붙일 말 — 생략했을 때 가는 값, 단위의 예외.</summary>
        public readonly string Note;

        public CommandArgSpec(
            string name,
            string label,
            ArgKind kind,
            string defaultToken = null,
            bool optional = false,
            string catalog = null,
            IReadOnlyList<string> choices = null,
            bool flag = false,
            string note = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("인자 칸에는 이름이 있어야 한다", nameof(name));

            Name = name;
            Label = string.IsNullOrEmpty(label) ? name : label;
            Kind = kind;
            Default = defaultToken;
            Optional = optional || defaultToken != null || flag;
            Catalog = catalog;
            Choices = choices ?? Array.Empty<string>();
            Flag = flag;
            Note = note;
        }

        public override string ToString() => Default != null ? $"{Name}={Default}" : Optional ? $"[{Name}]" : Name;
    }

    /// <summary>대본에 쓴 인자를 명세 표의 칸에 맞춘 것 하나.</summary>
    public readonly struct BoundArg
    {
        public readonly CommandArgSpec Spec;

        /// <summary>대본에 쓴 글. 적지 않았으면 null.</summary>
        public readonly string Token;

        /// <summary>대본의 몇 번째 인자인가(0부터). 적지 않았으면 -1.</summary>
        public readonly int Index;

        public BoundArg(CommandArgSpec spec, string token, int index)
        {
            Spec = spec;
            Token = token;
            Index = index;
        }

        /// <summary>적었는가.</summary>
        public bool Written => Token != null;

        /// <summary>실제로 가는 글 — 쓴 것, 없으면 표의 생략값(그것도 없으면 null).</summary>
        public string Value => Token ?? Spec.Default;

        public override string ToString() => $"{Spec.Name}:{Value ?? "∅"}";
    }

    /// <summary>
    /// 커맨드 하나의 명세 — 종류 하나와 인자 칸의 목록(ked-story-tools docs/PLAN.md §4.1 · K1).
    /// 뜻(파싱 · 실행)은 무대 파서의 것이다 — 이 표는 그 인자가 무엇인지를 데이터로 적을 뿐이고,
    /// 파서 표와 이름 · 인자 수 · 생략값이 어긋나면 EditMode 시험이 실패한다.
    /// </summary>
    public sealed class StageCommandSpec
    {
        public readonly string Name;
        public readonly CommandKind Kind;
        public readonly IReadOnlyList<CommandArgSpec> Args;

        /// <summary>한 줄 설명.</summary>
        public readonly string Summary;

        /// <summary>
        /// 기다리는 명령인가 — 끝날 때까지 그 앵커의 뒤 명령을 붙잡는다(docs/PLAN_line-anchor.md A7 · AR2).
        /// YarnCommandTextExtractor.IsWaitingCommand와 같아야 한다(시험이 맞춰 본다).
        /// </summary>
        public readonly bool Waits;

        /// <summary>
        /// 바로 실행하는 명령인가 — 브리지가 쌓지 않고 Yarn이 만난 때 실행한다. 그래서 **&lt;&lt;at&gt;&gt;을 붙이지 않는다**
        /// (붙이면 무대 파서로 넘어가 모르는 명령이 된다). 무대 파서의 표에는 없다.
        /// </summary>
        public readonly bool Immediate;

        public StageCommandSpec(
            string name,
            CommandKind kind,
            string summary,
            IReadOnlyList<CommandArgSpec> args = null,
            bool waits = false,
            bool immediate = false)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("커맨드에는 이름이 있어야 한다", nameof(name));

            Name = name;
            Kind = kind;
            Summary = summary ?? string.Empty;
            Args = args ?? Array.Empty<CommandArgSpec>();
            Waits = waits;
            Immediate = immediate;

            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (CommandArgSpec arg in Args)
            {
                if (!seen.Add(arg.Name))
                    throw new ArgumentException($"<<{name}>>의 인자 칸 '{arg.Name}'이 둘이다");
            }
        }

        /// <summary>길이 칸의 자리(Args에서). 없으면 -1 — 길이가 대본에 없는 커맨드다(모션 · 이모지 · 즉시).</summary>
        public int DurationArgIndex
        {
            get
            {
                for (int i = 0; i < Args.Count; i++)
                {
                    if (Args[i].Kind == ArgKind.Duration)
                        return i;
                }

                return -1;
            }
        }

        /// <summary>
        /// 대본에 쓴 인자를 칸에 맞춘다 — 칸마다 하나, 칸의 차례대로. 빈 글은 적지 않은 것이다(StageCommand.Arg와 같다).
        /// 낱말 칸(Flag)은 그 낱말일 때만 인자 하나를 쓴다. 칸보다 많은 인자는 problems에 적는다.
        /// </summary>
        public List<BoundArg> Bind(IReadOnlyList<string> args, List<string> problems = null)
        {
            List<BoundArg> bound = new(Args.Count);
            int count = args?.Count ?? 0;
            int at = 0;

            foreach (CommandArgSpec spec in Args)
            {
                string token = at < count && !string.IsNullOrEmpty(args[at]) ? args[at] : null;

                if (spec.Flag)
                {
                    if (token != null && IsChoice(spec, token))
                    {
                        bound.Add(new BoundArg(spec, token, at));
                        at++;
                    }
                    else
                    {
                        bound.Add(new BoundArg(spec, null, -1));
                    }

                    continue;
                }

                bound.Add(new BoundArg(spec, token, token != null ? at : -1));
                at++;
            }

            if (at < count)
                problems?.Add($"<<{Name}>>의 인자가 {count}개다 — 칸은 {Args.Count}개다");

            return bound;
        }

        private static bool IsChoice(CommandArgSpec spec, string token)
        {
            string trimmed = token.Trim();

            foreach (string choice in spec.Choices)
            {
                if (string.Equals(choice, trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public override string ToString() => $"{Name}({string.Join(" ", Args)}) — {CommandKinds.Label(Kind)}";
    }
}
