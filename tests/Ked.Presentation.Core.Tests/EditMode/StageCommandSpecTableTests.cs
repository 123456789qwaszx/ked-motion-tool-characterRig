using System;
using System.Collections.Generic;
using System.Linq;
using Ked.Presentation.Core;
using NUnit.Framework;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 명세 표(ked-story-tools docs/PLAN.md §4.1 · K1) — 표 자체의 모양. 파서 표와의 대조는 StageCommandSpecTableParserTests.
    /// </summary>
    public sealed class StageCommandSpecTableTests
    {
        private static StageCommandSpecTable Table => StageCommandSpecTable.Default;

        private static StageCommandSpec Spec(string name)
        {
            Assert.That(Table.TryGet(name, out StageCommandSpec spec), Is.True, $"<<{name}>>이 표에 없다");
            return spec;
        }

        [Test]
        public void 줄의_구조는_표_밖이다()
        {
            Assert.That(Table.Contains(LineBeatSchedule.AtCommandName), Is.False);
            Assert.That(Table.Contains(LineBeatSchedule.GapCommandName), Is.False);
            Assert.That(Table.Contains("step"), Is.False, "걷은 옛 표기다");
        }

        [Test]
        public void 바로_실행하는_명령을_가린다()
        {
            // 브리지가 쌓지 않고 바로 실행한다 — <<at>>을 붙이지 않는다(ked-story-tools docs/handoff/runtime.md 6절).
            string[] immediate = StageCommandSpecTable.Core.All.Where(s => s.Immediate).Select(s => s.Name).ToArray();

            Assert.That(immediate, Is.EquivalentTo(new[] { "surface_layout", "surface_reset" }));
            Assert.That(Table.IsImmediate("surface_layout"), Is.True);
            Assert.That(Table.IsImmediate("place"), Is.False);
            Assert.That(Table.IsImmediate("없는_이름"), Is.False);
            Assert.That(Table.IsImmediate(null), Is.False);
        }

        [Test]
        public void 기다리는_명령은_추출기와_같다()
        {
            foreach (StageCommandSpec spec in Table.All)
                Assert.That(spec.Waits, Is.EqualTo(YarnCommandTextExtractor.IsWaitingCommand(spec.Name)), spec.Name);
        }

        [Test]
        public void 정해진_종류_배정()
        {
            // 소유자가 정한 것(D3)과 리그에서 벗어나게 둔 것만 고정한다 — 나머지는 표가 정본이다.
            Assert.That(Spec("char_visual_dim").Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(Spec("char_visual").Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(Spec("fade_in").Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(Spec("mirror").Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(Spec("size").Kind, Is.EqualTo(CommandKind.Placement));
            Assert.That(Spec("shot_shake").Kind, Is.EqualTo(CommandKind.Camera));
            Assert.That(Spec("motion").Kind, Is.EqualTo(CommandKind.Acting));

            // 초상의 얼굴과 감정 아이콘은 연기에서 떼어 제 종류로 둔다(D13).
            Assert.That(Table.OfKind(CommandKind.Expression).Select(s => s.Name), Is.EquivalentTo(new[] { "face", "face_swap" }));
            Assert.That(Table.OfKind(CommandKind.Emoji).Select(s => s.Name), Is.EquivalentTo(new[] { "emoji", "emoji_off" }));
            Assert.That(CommandKinds.All.Where(CommandKinds.IsDirectorKind), Is.EqualTo(new[]
            {
                CommandKind.Camera, CommandKind.Placement, CommandKind.Expression, CommandKind.Acting, CommandKind.Emoji, CommandKind.Screen,
            }));
            Assert.That(Spec("pause").Kind, Is.EqualTo(CommandKind.Time));

            // 시간은 pause 하나다 — 필터에 오르지 않고 뒤 커맨드의 지연으로 보인다.
            Assert.That(Table.OfKind(CommandKind.Time).Select(s => s.Name), Is.EquivalentTo(new[] { "pause" }));
        }

        [Test]
        public void 종류의_이름표는_제_키로_되읽힌다()
        {
            foreach (CommandKind kind in CommandKinds.All)
            {
                Assert.That(CommandKinds.TryParse(CommandKinds.Key(kind), out CommandKind back), Is.True);
                Assert.That(back, Is.EqualTo(kind));
                Assert.That(CommandKinds.Label(kind), Is.Not.Empty);
            }

            foreach (ArgKind kind in ArgKinds.All)
            {
                Assert.That(ArgKinds.TryParse(ArgKinds.Key(kind), out ArgKind back), Is.True);
                Assert.That(back, Is.EqualTo(kind));
            }

            Assert.That(CommandKinds.TryParse("없는_종류", out _), Is.False);
        }

        [Test]
        public void 생략값은_제_갈래로_읽힌다()
        {
            foreach (StageCommandSpec spec in Table.All)
            {
                foreach (CommandArgSpec arg in spec.Args)
                {
                    string where = $"<<{spec.Name}>> {arg.Name}";

                    if (arg.Default != null)
                        Assert.That(arg.Optional, Is.True, where);

                    if (arg.Kind == ArgKind.Name)
                        Assert.That(arg.Catalog, Is.Not.Null.And.Not.Empty, $"{where} — 이름 칸은 어느 목록인지 적는다");

                    if (arg.Kind == ArgKind.Token)
                        Assert.That(arg.Choices, Is.Not.Empty, $"{where} — 토큰 칸은 낱말을 적는다");

                    if (string.IsNullOrEmpty(arg.Default))
                        continue;

                    switch (arg.Kind)
                    {
                        case ArgKind.Duration:
                            Assert.That(DurationToken.TryParseSeconds(arg.Default, out _), Is.True, where);
                            break;

                        case ArgKind.Distance:
                            Assert.That(UnitToken.TryParseSignedPixels(arg.Default, UnitToken.DefaultReferenceStageWidth, out _), Is.True, where);
                            break;

                        case ArgKind.Angle:
                        case ArgKind.Scale:
                        case ArgKind.Number:
                            Assert.That(NumberToken.TryParseFloat(arg.Default, out _), Is.True, where);
                            break;

                        case ArgKind.Token:
                            Assert.That(arg.Choices, Does.Contain(arg.Default), where);
                            break;
                    }
                }
            }
        }

        [Test]
        public void 슬롯의_움직임_넷은_자리와_구도이고_거리를_꼭_적는다()
        {
            // docs/PLAN_move-axes.md §4 — 슬롯 · 거리 · 길이(0.4s) · 이징.
            foreach (string name in new[] { "left", "right", "up", "down" })
            {
                StageCommandSpec spec = Spec(name);

                Assert.That(spec.Kind, Is.EqualTo(CommandKind.Placement), name);
                Assert.That(spec.Args.Select(a => a.Name), Is.EqualTo(new[] { "slot", "distance", "duration", "ease" }), name);
                Assert.That(spec.Args[1].Kind, Is.EqualTo(ArgKind.Distance), name);
                Assert.That(spec.Args[1].Default, Is.Null, name + " — 거리에는 생략값이 없다");
                Assert.That(spec.Args[2].Default, Is.EqualTo("0.4s"), name);
                Assert.That(spec.DurationArgIndex, Is.EqualTo(2), name);
            }
        }

        [Test]
        public void 길이_칸의_자리()
        {
            Assert.That(Spec("place").DurationArgIndex, Is.EqualTo(3));
            Assert.That(Spec("size").DurationArgIndex, Is.EqualTo(3));
            Assert.That(Spec("pause").DurationArgIndex, Is.EqualTo(0));
            Assert.That(Spec("motion").DurationArgIndex, Is.EqualTo(-1), "모션의 길이는 대본에 없다");
            Assert.That(Spec("face").DurationArgIndex, Is.EqualTo(-1));
        }

        [Test]
        public void 인자를_칸에_맞춘다()
        {
            List<string> problems = new();
            List<BoundArg> bound = Spec("place").Bind(new[] { "s1", "120u", "", "0.4s" }, problems);

            Assert.That(problems, Is.Empty);
            Assert.That(bound.Select(b => b.Spec.Name), Is.EqualTo(new[] { "slot", "x", "y", "duration", "ease" }));
            Assert.That(bound.Select(b => b.Token), Is.EqualTo(new[] { "s1", "120u", null, "0.4s", null }));
            Assert.That(bound.Select(b => b.Value), Is.EqualTo(new[] { "s1", "120u", "0u", "0.4s", "" }));
            Assert.That(bound.Select(b => b.Index), Is.EqualTo(new[] { 0, 1, -1, 3, -1 }));
            Assert.That(bound[2].Written, Is.False, "빈 글은 적지 않은 것이다");
        }

        [Test]
        public void 낱말_칸은_그_낱말일_때만_자리를_차지한다()
        {
            StageCommandSpec size = Spec("size");

            List<BoundArg> kept = size.Bind(new[] { "c1", "14", "keep_y", "12fr", "OutCubic" });
            Assert.That(kept.Select(b => b.Token), Is.EqualTo(new[] { "c1", "14", "keep_y", "12fr", "OutCubic" }));

            // 셋째 칸이 keep_y가 아니면 길이다(SizeToken).
            List<BoundArg> plain = size.Bind(new[] { "c1", "close", "12fr" });
            Assert.That(plain.Select(b => b.Spec.Name), Is.EqualTo(new[] { "slot", "level", "keep_y", "duration", "ease" }));
            Assert.That(plain.Select(b => b.Token), Is.EqualTo(new[] { "c1", "close", null, "12fr", null }));
            Assert.That(plain[3].Index, Is.EqualTo(2));
            Assert.That(plain[3].Value, Is.EqualTo("12fr"));

            List<BoundArg> bare = size.Bind(new[] { "c1", "x1.04" });
            Assert.That(bare[3].Value, Is.EqualTo("13fr"));
        }

        [Test]
        public void 칸보다_많은_인자는_적어_둔다()
        {
            List<string> problems = new();
            Spec("fade_in").Bind(new[] { "s1", "12fr", "남는_것" }, problems);

            Assert.That(problems, Has.Count.EqualTo(1));
        }

        [Test]
        public void 포크는_제_어휘를_덧붙인다()
        {
            StageCommandSpec panel = new(
                "cutin_panel", CommandKind.Screen, "포크의 컷인 판",
                new[] { new CommandArgSpec("stage", "무대", ArgKind.Name, "stage01", catalog: "stage") });
            StageCommandSpec messenger = new("messenger_open", CommandKind.Setup, "포크의 메신저 구간", immediate: true);

            StageCommandSpecTable fork = StageCommandSpecTable.Core.With(new[] { panel, messenger });

            Assert.That(fork.All.Count, Is.EqualTo(StageCommandSpecTable.Core.All.Count + 2));
            Assert.That(fork.IsImmediate("messenger_open"), Is.True);
            Assert.That(StageCommandSpecTable.Core.Contains("cutin_panel"), Is.False, "본래 표는 바뀌지 않는다");

            CommandSpecsFileDto dto = CommandSpecsFile.ToDto(fork);
            Assert.That(dto.commands.Where(c => c.fork).Select(c => c.name), Is.EqualTo(new[] { "cutin_panel", "messenger_open" }));

            // 같은 이름을 조용히 덮지 않는다.
            Assert.Throws<ArgumentException>(() => StageCommandSpecTable.Core.With(new[] { new StageCommandSpec("place", CommandKind.Screen, "") }));
        }

        [Test]
        public void 내보낸_표를_되읽으면_같다()
        {
            CommandSpecsFileDto dto = CommandSpecsFile.ToDto(Table);

            Assert.That(dto.schema, Is.EqualTo(CommandSpecsFile.Schema));
            Assert.That(dto.lineStructure, Is.EqualTo(new[] { "at", "gap" }));
            Assert.That(dto.kinds.Count, Is.EqualTo(CommandKinds.All.Length));

            List<string> problems = new();
            StageCommandSpecTable back = CommandSpecsFile.FromDto(dto, problems);

            Assert.That(problems, Is.Empty);
            Assert.That(back.All.Count, Is.EqualTo(Table.All.Count));

            for (int i = 0; i < Table.All.Count; i++)
            {
                StageCommandSpec a = Table.All[i];
                StageCommandSpec b = back.All[i];

                Assert.That(b.Name, Is.EqualTo(a.Name));
                Assert.That(b.Kind, Is.EqualTo(a.Kind), a.Name);
                Assert.That(b.Summary, Is.EqualTo(a.Summary), a.Name);
                Assert.That(b.Waits, Is.EqualTo(a.Waits), a.Name);
                Assert.That(b.Immediate, Is.EqualTo(a.Immediate), a.Name);
                Assert.That(b.Args.Count, Is.EqualTo(a.Args.Count), a.Name);

                for (int k = 0; k < a.Args.Count; k++)
                {
                    string where = $"<<{a.Name}>> {a.Args[k].Name}";

                    Assert.That(b.Args[k].Name, Is.EqualTo(a.Args[k].Name), where);
                    Assert.That(b.Args[k].Label, Is.EqualTo(a.Args[k].Label), where);
                    Assert.That(b.Args[k].Kind, Is.EqualTo(a.Args[k].Kind), where);
                    Assert.That(b.Args[k].Default, Is.EqualTo(a.Args[k].Default), where + " — 없다(null)와 비워 둔다(\"\")가 갈려야 한다");
                    Assert.That(b.Args[k].Optional, Is.EqualTo(a.Args[k].Optional), where);
                    Assert.That(b.Args[k].Flag, Is.EqualTo(a.Args[k].Flag), where);
                    Assert.That(b.Args[k].Catalog, Is.EqualTo(a.Args[k].Catalog), where);
                    Assert.That(b.Args[k].Choices, Is.EqualTo(a.Args[k].Choices), where);
                    Assert.That(b.Args[k].Note, Is.EqualTo(a.Args[k].Note), where);
                }
            }
        }

        [Test]
        public void 되읽을_때_모르는_종류는_건너뛰고_적는다()
        {
            CommandSpecsFileDto dto = new()
            {
                schema = CommandSpecsFile.Schema,
                commands = new List<CommandSpecDto>
                {
                    new() { name = "ok", kind = "camera" },
                    new() { name = "bad", kind = "없는_종류" },
                    new() { name = "ok", kind = "screen" },
                    new() { name = "bad_arg", kind = "camera", args = new List<CommandArgDto> { new() { name = "a", kind = "없는_갈래" } } },
                },
            };

            List<string> problems = new();
            StageCommandSpecTable table = CommandSpecsFile.FromDto(dto, problems);

            Assert.That(table.All.Select(s => s.Name), Is.EqualTo(new[] { "ok" }));
            Assert.That(table.All[0].Kind, Is.EqualTo(CommandKind.Camera), "겹치면 앞의 것");
            Assert.That(problems, Has.Count.EqualTo(3));
        }
    }
}
