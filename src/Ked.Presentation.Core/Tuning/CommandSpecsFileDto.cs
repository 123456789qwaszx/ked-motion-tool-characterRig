using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    // ─────────────────────────────────────────────────────────────────
    // command-specs.json의 전송 타입 — schema "command-specs/1". 명세 표(StageCommandSpecTable)를 툴에 건넨다.
    //
    //   {
    //     "schema": "command-specs/1",
    //     "kinds":    [ { "key": "camera", "label": "카메라", "director": true }, … ],
    //     "argKinds": [ "slot", "distance", … ],
    //     "lineStructure": [ "at", "gap" ],     커맨드가 아니라 줄의 구조 — 표 밖이다
    //     "commands": [
    //       { "name": "place", "kind": "placement", "summary": "…", "waits": false, "immediate": false, "fork": false,
    //         "args": [ { "name": "slot", "label": "슬롯", "kind": "slot", "hasDefault": false, "defaultValue": "",
    //                     "optional": false, "flag": false, "catalog": "", "choices": [], "note": "" }, … ] }
    //     ]
    //   }
    //
    // 필드 이름이 JSON 키와 1:1이다. 코어는 JSON 파서를 갖지 않는다 — 역직렬화는 호스트가 한다
    // (유니티: JsonUtility, 툴: System.Text.Json + IncludeFields).
    // JsonUtility는 null 문자열을 ""로 낸다 — 생략값이 "없다"와 "비워 둔다"는 hasDefault로 가른다.
    //
    // immediate가 참인 것은 <<at>>을 붙이지 않는 명령이다(브리지가 쌓지 않고 바로 실행한다).
    // fork가 참인 것은 포크가 덧붙인 어휘다(StageCommandSpecTable.Fork.cs).
    // ─────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class CommandSpecsFileDto
    {
        public string schema;
        public List<CommandKindDto> kinds;
        public List<string> argKinds;
        public List<string> lineStructure;
        public List<CommandSpecDto> commands;
    }

    [Serializable]
    public sealed class CommandKindDto
    {
        public string key;
        public string label;

        /// <summary>연출가의 넷인가 — 종류 필터에 오른다.</summary>
        public bool director;
    }

    [Serializable]
    public sealed class CommandSpecDto
    {
        public string name;
        public string kind;
        public string summary;
        public bool waits;
        public bool immediate;
        public bool fork;
        public List<CommandArgDto> args;
    }

    [Serializable]
    public sealed class CommandArgDto
    {
        public string name;
        public string label;
        public string kind;
        public bool hasDefault;
        public string defaultValue;
        public bool optional;
        public bool flag;
        public string catalog;
        public List<string> choices;
        public string note;
    }

    /// <summary>명세 표 ↔ command-specs.json의 전송 타입.</summary>
    public static class CommandSpecsFile
    {
        public const string Schema = "command-specs/1";

        /// <summary>내보내는 파일의 이름(ExportedTuning 아래).</summary>
        public const string FileName = "command-specs.json";

        public static CommandSpecsFileDto ToDto(StageCommandSpecTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            CommandSpecsFileDto dto = new()
            {
                schema = Schema,
                kinds = new List<CommandKindDto>(),
                argKinds = new List<string>(),
                lineStructure = new List<string> { LineBeatSchedule.AtCommandName, LineBeatSchedule.GapCommandName },
                commands = new List<CommandSpecDto>(),
            };

            foreach (CommandKind kind in CommandKinds.All)
            {
                dto.kinds.Add(new CommandKindDto
                {
                    key = CommandKinds.Key(kind),
                    label = CommandKinds.Label(kind),
                    director = CommandKinds.IsDirectorKind(kind),
                });
            }

            foreach (ArgKind kind in ArgKinds.All)
                dto.argKinds.Add(ArgKinds.Key(kind));

            foreach (StageCommandSpec spec in table.All)
            {
                CommandSpecDto command = new()
                {
                    name = spec.Name,
                    kind = CommandKinds.Key(spec.Kind),
                    summary = spec.Summary,
                    waits = spec.Waits,
                    immediate = spec.Immediate,
                    fork = !StageCommandSpecTable.Core.Contains(spec.Name),
                    args = new List<CommandArgDto>(),
                };

                foreach (CommandArgSpec arg in spec.Args)
                {
                    command.args.Add(new CommandArgDto
                    {
                        name = arg.Name,
                        label = arg.Label,
                        kind = ArgKinds.Key(arg.Kind),
                        hasDefault = arg.Default != null,
                        defaultValue = arg.Default ?? string.Empty,
                        optional = arg.Optional,
                        flag = arg.Flag,
                        catalog = arg.Catalog ?? string.Empty,
                        choices = new List<string>(arg.Choices),
                        note = arg.Note ?? string.Empty,
                    });
                }

                dto.commands.Add(command);
            }

            return dto;
        }

        /// <summary>
        /// 읽은 파일을 명세 표로 — 툴이 포크가 낸 표를 읽을 때 쓴다.
        /// 모르는 종류 · 갈래 · 겹친 이름은 problems에 적고 그 커맨드를 건너뛴다(조용히 다른 종류로 읽지 않는다).
        /// </summary>
        public static StageCommandSpecTable FromDto(CommandSpecsFileDto dto, List<string> problems = null)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (dto.schema != Schema)
                problems?.Add($"schema가 '{dto.schema}'다 — '{Schema}'를 읽는다");

            List<StageCommandSpec> specs = new();
            HashSet<string> names = new(StringComparer.Ordinal);

            foreach (CommandSpecDto command in dto.commands ?? new List<CommandSpecDto>())
            {
                if (command == null || string.IsNullOrEmpty(command.name))
                {
                    problems?.Add("이름 없는 커맨드를 건너뛴다");
                    continue;
                }

                if (!CommandKinds.TryParse(command.kind, out CommandKind kind))
                {
                    problems?.Add($"<<{command.name}>> — 모르는 종류 '{command.kind}'. 건너뛴다");
                    continue;
                }

                if (!names.Add(command.name))
                {
                    problems?.Add($"<<{command.name}>>이 둘이다 — 앞의 것을 쓴다");
                    continue;
                }

                List<CommandArgSpec> args = new();
                bool broken = false;

                foreach (CommandArgDto arg in command.args ?? new List<CommandArgDto>())
                {
                    if (arg == null || !ArgKinds.TryParse(arg.kind, out ArgKind argKind))
                    {
                        problems?.Add($"<<{command.name}>> — 인자 '{arg?.name}'의 갈래 '{arg?.kind}'를 모른다. 커맨드를 건너뛴다");
                        broken = true;
                        break;
                    }

                    args.Add(new CommandArgSpec(
                        arg.name,
                        arg.label,
                        argKind,
                        arg.hasDefault ? arg.defaultValue ?? string.Empty : null,
                        arg.optional,
                        string.IsNullOrEmpty(arg.catalog) ? null : arg.catalog,
                        arg.choices != null ? arg.choices.ToArray() : null,
                        arg.flag,
                        string.IsNullOrEmpty(arg.note) ? null : arg.note));
                }

                if (broken)
                {
                    names.Remove(command.name);
                    continue;
                }

                specs.Add(new StageCommandSpec(command.name, kind, command.summary, args, command.waits, command.immediate));
            }

            return new StageCommandSpecTable(specs);
        }
    }
}
