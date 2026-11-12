using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Schema;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Cli;

/// <summary>
/// <c>mt</c> — 모션 툴의 명령줄 입구. 창 없이 저작 원본(<c>*.motion-authoring.json</c>)을 읽어 검증·내보내기·포즈 조회·굽기를 한다.
///
/// 설계 원칙: **결과는 표준 출력에 JSON 하나**(기계가 읽는다), 사람에게 하는 말은 표준 오류, 종료 코드는 0 = 됨 · 1 = 막는 문제가 있다 · 2 = 쓰는 법이 틀렸다.
/// 화면과 같은 함수를 지난다(<see cref="MotionValidator"/> · <see cref="MotionExport"/> · <see cref="ClipPreview"/> · <see cref="ClipBake"/>) —
/// 여기서 통과한 것은 앱에서도 통과하고, 여기서 읽은 포즈는 뷰포트가 보이는 포즈다.
/// </summary>
public static partial class MtCli
{
    public const string Usage = """
        mt — ked-motion-tool-characterRig 명령줄 (결과는 stdout의 JSON, 종료 코드 0 됨 · 1 막는 문제 · 2 쓰는 법)

          mt info     <문서>                                   모션·클립·씬·이모지의 목록
          mt validate <문서> [--rig <덤프>]                    문제 목록(모션·클립·이모지). 막는 문제가 있으면 종료 코드 1
          mt export   <문서> <motions.json> [--rig <덤프>]     motions.json 내보내기(검증을 지나야 나간다)
          mt emojis   <문서> <emojis.json> [--rig <덤프>]      emojis.json(emojis/2) 내보내기 — 구간마다 따로 선 클립, 런타임 로더로 다시 읽어 본다
          mt pose     <문서> --motion <이름> --time <초> [--object <id>] [--mode relative|absolute] [--rig <덤프>]
          mt pose     <문서> --clip <이름> --time <초> [--rig <덤프>]
          mt pose     <문서> --emoji <이름> --time <초> [--off <초>] [--view life|timeline] [--facing right|left] [--rig <덤프>]
                                                               그 시각의 포즈 — 모션·클립이 건드리는 노드의 값,
                                                               이모지는 수명(등장 → 머묾 ⟲ → --off에 내리면 퇴장) 또는 타임라인의 그 프레임
          mt bake     <문서> --clip <이름> --object <id> --name <새 모션> [--out <문서>] [--rig <덤프>]
                                                               클립에서 그 캐릭터에 얹힌 블록을 모션 하나로 구워 문서에 더한다

        만들기 — 문서를 그 자리에서 고쳐 쓰고, 한 일과 쓴 뒤의 문제 목록을 낸다(화면과 같은 편집 명령을 지난다)

          mt new         <문서>                                캐릭터 s1 하나가 선 빈 문서
          mt controllers [--rig-kind character]                키를 놓을 수 있는 손잡이 — id · 노드 · 종류 · 성분 · 단위
          mt presets                                           모션 프리셋과 이모지의 움직임 목록
          mt add-motion  <문서> --name <이름> [--duration <초>] [--end restore|commit]
          mt add-motion  <문서> --preset <id> [--name <이름>]  프리셋으로(끄덕 nod · 숙임 bow …)
          mt add-emoji   <문서> --name <이름> [--images <a.png,b.png>] [--life once|stay] [--in <움직임>] [--hold <움직임>] [--out <움직임>]
                                                               그림 한 장이 그림 하나. 경로는 문서 기준. 움직임은 mt presets의 emojiMoves
          mt key         <문서> --motion <이름> --controller <id> --axis x|y|z (--time <초> --value <값> | --keys "초:값,초:값") [--tangent flat]
          mt key         <문서> --motion <이름> --node <노드> --kind position|scale|euler --axis …
          mt key         <문서> --emoji <이름> [--picture <그림>] --kind position|scale|euler|alpha|image --axis x|y|z
                                (--frame <프레임> --value <값> | --keys "프레임:값,프레임:값") [--tangent flat]
                                                               같은 시각에 키가 있으면 바꾼다. --picture가 없으면 이모지 전체. 탄젠트의 기본은 flat
          mt unkey       <문서> (key와 같은 자리) (--time <초> | --frame <프레임>)
                                                               그 시각의 키 하나를 지운다
          mt object      <문서> --id <id> [--scene <씬>] [--rig-kind character] [--x <px>] [--y <px>] [--rotation <도>] [--scale <배>] [--scale-x …] [--scale-y …]
                                                               씬의 캐릭터 — 없으면 세우고, 있으면 적은 값만 바꾼다
          mt add-clip    <문서> --name <이름> [--scene <씬>] [--duration <초>]
          mt block       <문서> --clip <이름> --motion <이름> --at <초> --object <id> [--role <배역>] [--mode relative|absolute] [--track <번호>|new]
                                                               클립에 모션 블록 하나. 배역(기본은 캐릭터의 id)과 그 트랙이 없으면 세운다
          mt span        <문서> --emoji <이름> --name <구간> --start <프레임> --end <프레임>
                                                               이모지의 구간 — 없으면 더하고, 있으면 옮긴다(끝 포함)
          mt picture     <문서> --emoji <이름> --picture <그림> [--x <px>] [--y <px>] [--scale <배>] [--rotation <도>]
                                                               그림의 기본 자리(키가 아니다) — 적은 값만 바꾼다
          mt remove      <문서> --motion <이름> | --emoji <이름> | --clip <이름> [--track <번호> [--block <번호>]] | --object <id> [--scene <씬>]

          <문서>  *.motion-authoring.json        <덤프>  rig-schemas.json (없으면 문서가 적어 둔 것, 그것도 없으면 내장본)
        """;

    private static readonly JsonSerializerOptions Output = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            stdout.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Options.Parse(args.Skip(1).ToArray());

            return args[0] switch
            {
                "info" => Info(options, stdout),
                "validate" => Validate(options, stdout),
                "export" => Export(options, stdout),
                "emojis" => Emojis(options, stdout),
                "pose" => Pose(options, stdout),
                "bake" => Bake(options, stdout),
                "new" => New(options, stdout),
                "controllers" => Controllers(options, stdout),
                "presets" => Presets(stdout),
                "add-motion" => AddMotion(options, stdout),
                "add-emoji" => AddEmoji(options, stdout),
                "key" => Key(options, stdout),
                "unkey" => Unkey(options, stdout),
                "object" => SceneObjectCommand(options, stdout),
                "add-clip" => AddClip(options, stdout),
                "block" => Block(options, stdout),
                "span" => Span(options, stdout),
                "picture" => Picture(options, stdout),
                "remove" => Remove(options, stdout),
                _ => throw new UsageException($"모르는 명령 '{args[0]}'"),
            };
        }
        catch (UsageException e)
        {
            stderr.WriteLine($"mt: {e.Message}");
            stderr.WriteLine(Usage);
            return 2;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            // 없는 파일·깨진 JSON·없는 이름 — 기계도 읽을 수 있게 JSON으로도 남긴다.
            stderr.WriteLine($"mt: {e.Message}");
            Write(stdout, new JsonObject { ["ok"] = false, ["error"] = e.Message });
            return 1;
        }
    }

    // ── 명령 ──────────────────────────────────────────────────────────

    private static int Info(Options options, TextWriter stdout)
    {
        MotionDocument document = Load(options, out _);
        var motions = new JsonArray();

        foreach (MotionAsset m in document.Motions)
        {
            motions.Add(new JsonObject
            {
                ["name"] = m.Name,
                ["rig"] = m.Rig,
                ["duration"] = m.Duration,
                ["end"] = m.End == MotionEnd.Restore ? "restore" : "commit",
                ["channels"] = new JsonArray(m.Channels.Select(c => (JsonNode)$"{c.Node} {ChannelKindLabel.File(c.Kind)}").ToArray()),
            });
        }

        var clips = new JsonArray();

        foreach (ClipAsset c in document.Clips)
        {
            clips.Add(new JsonObject
            {
                ["name"] = c.Name,
                ["duration"] = ClipTiming.DurationOf(document, c),
                ["roles"] = new JsonArray(c.Roles.Select(r => (JsonNode)new JsonObject { ["name"] = r.Name, ["rig"] = r.Rig, ["object"] = r.Object }).ToArray()),
                ["blocks"] = c.Tracks.Sum(t => t.Blocks.Count),
            });
        }

        Write(stdout, new JsonObject
        {
            ["ok"] = true,
            ["schema"] = document.Schema,
            ["motions"] = motions,
            ["clips"] = clips,
            ["scenes"] = new JsonArray(document.Scenes.Select(s => (JsonNode)new JsonObject
            {
                ["name"] = s.Name,
                ["objects"] = new JsonArray(s.Objects.Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["rig"] = o.Rig }).ToArray()),
            }).ToArray()),
            ["emojis"] = new JsonArray(document.Emojis.Select(e => (JsonNode)new JsonObject
            {
                ["name"] = e.Name,
                ["rigs"] = new JsonArray(e.Rigs.Select(r => (JsonNode)r.Name).ToArray()),
                ["frames"] = e.LastFrame + 1,
                ["clips"] = new JsonArray(EmojiExport.ClipsOf(e).Select(c => (JsonNode)$"{c.Name} {c.Start}~{c.End}").ToArray()),
                ["stays"] = e.Stays,
            }).ToArray()),
        });
        return 0;
    }

    private static int Validate(Options options, TextWriter stdout)
    {
        MotionDocument document = Load(options, out RigSchemaFile rig);
        (JsonArray problems, bool blocking) = Problems(document, rig);
        string path = options.Positional(0, "문서 경로");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

        // 모르는 키 — 읽을 때 말없이 버려진 것. 오타면 뜻한 값이 빠진 채 내보내진다.
        foreach (string key in MotionDocumentJson.UnknownKeys(File.ReadAllText(path)))
        {
            problems.Add(new JsonObject { ["kind"] = "document", ["name"] = key, ["message"] = $"모르는 키 '{key}' — 읽지 않았다(오타인가?)", ["blocking"] = false });
        }

        foreach (EmojiProblem p in EmojiValidator.MissingImages(document, directory))
        {
            problems.Add(new JsonObject { ["kind"] = "emoji", ["name"] = p.EmojiName, ["message"] = p.Message, ["blocking"] = p.Blocking });
        }

        Write(stdout, new JsonObject { ["ok"] = !blocking, ["problems"] = problems });
        return blocking ? 1 : 0;
    }

    private static int Export(Options options, TextWriter stdout)
    {
        string target = options.Positional(1, "내보낼 motions.json 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);

        if (!MotionExport.TryExport(document, rig.NodeIds, out string json, out IReadOnlyList<MotionProblem> problems))
        {
            Write(stdout, new JsonObject
            {
                ["ok"] = false,
                ["problems"] = new JsonArray(problems.Select(p => (JsonNode)new JsonObject { ["motion"] = p.MotionName, ["message"] = p.Message, ["blocking"] = p.Blocking }).ToArray()),
            });
            return 1;
        }

        File.WriteAllBytes(target, System.Text.Encoding.UTF8.GetBytes(json));
        Write(stdout, new JsonObject
        {
            ["ok"] = true,
            ["path"] = Path.GetFullPath(target),
            ["schema"] = AuthoringFormat.ExportSchema,
            ["motions"] = new JsonArray(MotionExport.ToClips(document).Select(c => (JsonNode)c.Name).ToArray()),
        });
        return 0;
    }

    private static int Emojis(Options options, TextWriter stdout)
    {
        string target = options.Positional(1, "내보낼 emojis.json 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);

        if (!EmojiRigFrame.TryRead(rig, out EmojiRigFrame frame, out string why))
        {
            throw new InvalidOperationException(why);
        }

        if (!EmojiExport.TryExport(document, frame, out string json, out IReadOnlyList<EmojiProblem> problems))
        {
            Write(stdout, new JsonObject
            {
                ["ok"] = false,
                ["problems"] = new JsonArray(problems.Select(p => (JsonNode)new JsonObject { ["emoji"] = p.EmojiName, ["message"] = p.Message, ["blocking"] = p.Blocking }).ToArray()),
            });
            return 1;
        }

        File.WriteAllBytes(target, System.Text.Encoding.UTF8.GetBytes(json));
        Write(stdout, new JsonObject
        {
            ["ok"] = true,
            ["path"] = Path.GetFullPath(target),
            ["schema"] = EmojiDefinitionLoader.Schema,
            ["emojis"] = new JsonArray(document.Emojis.Select(e => (JsonNode)e.Name).ToArray()),
        });
        return 0;
    }

    private static int Pose(Options options, TextWriter stdout)
    {
        MotionDocument document = Load(options, out RigSchemaFile rig);
        double time = options.Number("time");
        var result = new JsonObject { ["ok"] = true, ["time"] = time };

        if (options.Value("clip") is { } clipName)
        {
            ClipAsset clip = document.FindClip(clipName) ?? throw new KeyNotFoundException($"클립 '{clipName}'이 없다.");
            SceneAsset scene = ClipPreview.SceneOf(document, clip) ?? throw new InvalidOperationException($"클립 '{clipName}'의 씬이 없다.");
            RectNodeTree pose = ClipPreview.Pose(BuildTree(rig, scene), document, clip, time);

            // 클립이 건드리는 노드만 — 배역마다, 그 배역의 블록이 쓰는 (노드, 종류).
            var objects = new JsonObject();

            foreach (ClipTrack track in clip.Tracks)
            {
                if (ClipPreview.ObjectOf(document, clip, track) is not { } actor)
                {
                    continue;
                }

                var nodes = objects[actor.Id] as JsonObject ?? new JsonObject();
                objects[actor.Id] = nodes;

                foreach (ChannelAsset channel in track.Blocks.SelectMany(b => document.Find(b.Motion)?.Channels ?? Enumerable.Empty<ChannelAsset>()))
                {
                    AddNodeValue(nodes, pose, actor, channel);
                }
            }

            result["clip"] = clip.Name;
            result["duration"] = ClipTiming.DurationOf(document, clip);
            result["objects"] = objects;
        }
        else if (options.Value("motion") is { } motionName)
        {
            MotionAsset motion = document.Find(motionName) ?? throw new KeyNotFoundException($"모션 '{motionName}'이 없다.");
            SceneAsset scene = document.ActiveSceneAsset ?? throw new InvalidOperationException("씬이 없다.");
            MotionChannelMode mode = options.Value("mode") is { } token
                ? MotionModeToken.TryParse(token, out MotionChannelMode parsed) ? parsed : throw new UsageException($"--mode는 relative|absolute ('{token}')")
                : motion.PreviewMode;
            result["motion"] = motion.Name;
            result["duration"] = motion.Duration;
            result["mode"] = MotionModeToken.Of(mode);

            SceneObject actor = options.Value("object") is { } id
                ? scene.Find(id) ?? throw new KeyNotFoundException($"오브젝트 '{id}'가 없다.")
                : scene.Objects.FirstOrDefault(o => o.Rig == motion.Rig) ?? throw new InvalidOperationException($"씬에 리그 '{motion.Rig}'의 오브젝트가 없다 — --object로 고른다.");
            RectNodeTree pose = MotionPreview.Pose(BuildTree(rig, scene), motion, time, mode, null, actor.Prefix);
            var nodes = new JsonObject();

            foreach (ChannelAsset channel in motion.Channels)
            {
                AddNodeValue(nodes, pose, actor, channel);
            }

            result["object"] = actor.Id;
            result["nodes"] = nodes;
        }
        else if (options.Value("emoji") is { } emojiName)
        {
            EmojiPose(document, rig, emojiName, time, options, result);
        }
        else
        {
            throw new UsageException("pose에는 --motion · --clip · --emoji 중 하나가 있어야 한다");
        }

        Write(stdout, result);
        return 0;
    }

    private static int Bake(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        string clipName = options.Required("clip");
        string objectId = options.Required("object");
        string name = options.Required("name");
        ClipAsset clip = document.FindClip(clipName) ?? throw new KeyNotFoundException($"클립 '{clipName}'이 없다.");
        SceneAsset scene = ClipPreview.SceneOf(document, clip) ?? throw new InvalidOperationException($"클립 '{clipName}'의 씬이 없다.");

        var editor = new MotionEditor(document);
        MotionAsset baked = ClipBake.Bake(editor, BuildTree(rig, scene), clip.Name, objectId, name);
        string target = options.Value("out") ?? path;
        File.WriteAllText(target, MotionDocumentJson.Write(editor.Document));

        Write(stdout, new JsonObject
        {
            ["ok"] = true,
            ["path"] = Path.GetFullPath(target),
            ["motion"] = baked.Name,
            ["duration"] = baked.Duration,
            ["end"] = baked.End == MotionEnd.Restore ? "restore" : "commit",
            ["channels"] = new JsonArray(baked.Channels.Select(c => (JsonNode)$"{c.Node} {ChannelKindLabel.File(c.Kind)}").ToArray()),
            ["keys"] = baked.Channels.Sum(c => (c.X?.Keys.Count ?? 0) + (c.Y?.Keys.Count ?? 0) + (c.Z?.Keys.Count ?? 0)),
        });
        return 0;
    }

    // ── helper ────────────────────────────────────────────────────────

    private static (JsonArray Problems, bool Blocking) Problems(MotionDocument document, RigSchemaFile rig)
    {
        var problems = new JsonArray();
        bool blocking = false;
        IReadOnlyDictionary<string, ICollection<string>>? nodeIds = rig.NodeIds;

        foreach (MotionProblem p in MotionValidator.Validate(document, nodeIds))
        {
            problems.Add(new JsonObject { ["kind"] = "motion", ["name"] = p.MotionName, ["message"] = p.Message, ["blocking"] = p.Blocking });
            blocking |= p.Blocking;
        }

        foreach (ClipProblem p in ClipValidator.Validate(document, nodeIds))
        {
            problems.Add(new JsonObject { ["kind"] = "clip", ["name"] = p.ClipName, ["message"] = p.Message, ["blocking"] = p.Blocking });
            blocking |= p.Blocking;
        }

        foreach (EmojiProblem p in EmojiValidator.Validate(document, rig))
        {
            problems.Add(new JsonObject { ["kind"] = "emoji", ["name"] = p.EmojiName, ["message"] = p.Message, ["blocking"] = p.Blocking });
            blocking |= p.Blocking;
        }

        // 문서를 지을 때와 스키마가 달라진 것 — 알림(무엇이 달라졌고 무엇이 그것을 쓰는가).
        if (document.BuiltOn is { } builtOn)
        {
            foreach (SchemaViolation v in SchemaDiff.Compare(builtOn, SchemaDiff.PrintOf(rig)).Notices(document))
            {
                problems.Add(new JsonObject { ["kind"] = "schema", ["name"] = v.Subject, ["message"] = v.ToString(), ["blocking"] = v.Blocking });
            }
        }

        return (problems, blocking);
    }

    /// <summary>이모지의 그 시각 — 수명(life, 기본)이면 코어 재생의 단계 · 클립 · 프레임, 타임라인(timeline)이면 그 프레임. 값은 미리보기와 같은 길(EmojiPreview).</summary>
    private static void EmojiPose(MotionDocument document, RigSchemaFile rig, string emojiName, double time, Options options, JsonObject result)
    {
        EmojiAsset emoji = document.FindEmoji(emojiName) ?? throw new KeyNotFoundException($"이모지 '{emojiName}'이 없다.");

        if (!EmojiRigFrame.TryRead(rig, out EmojiRigFrame frame, out string why))
        {
            throw new InvalidOperationException(why);
        }

        string view = options.Value("view") ?? "life";
        EmojiClip? clip;
        float clipFrame;
        result["emoji"] = emoji.Name;
        result["view"] = view;

        if (view == "timeline")
        {
            clip = EmojiPreview.Timeline(emoji, frame) ?? throw new InvalidOperationException($"이모지 '{emoji.Name}'의 타임라인을 런타임이 읽지 못한다 — mt validate로 문제를 본다.");
            clipFrame = Math.Clamp((float)time * EmojiAsset.Fps, 0f, clip.Length - 1);
            result["frame"] = clipFrame;
        }
        else if (view == "life")
        {
            EmojiDefinition definition = EmojiPreview.Definition(emoji, frame, out string? reason)
                ?? throw new InvalidOperationException($"이모지 '{emoji.Name}'을 런타임이 읽지 못한다 — {reason}");
            double? off = options.Value("off") is null ? null : options.Number("off");
            EmojiMoment moment = EmojiPreview.Moment(definition, time, off);
            clip = moment.Clip;
            clipFrame = moment.Frame;
            result["phase"] = moment.Phase.ToString().ToLowerInvariant();
            result["clip"] = moment.Clip?.Name;
            result["frame"] = moment.Frame;
        }
        else
        {
            throw new UsageException($"--view는 life|timeline ('{view}')");
        }

        // 좌우 — 왼쪽을 보는 인물 밑이면 로케이터를 뒤집고 예외의 부호를 코어 EmojiSide로 곱한다(앱의 "왼쪽을 보는 인물로 보기"와 같다).
        string facing = options.Value("facing") ?? "right";

        if (facing is not ("right" or "left"))
        {
            throw new UsageException($"--facing은 right|left ('{facing}')");
        }

        result["facing"] = facing;
        EmojiDefinition? sides = EmojiPreview.Definition(emoji, frame, out _);
        EmojiPose pose = EmojiPreview.Pose(emoji, frame, clip, clipFrame, null, sides, facing == "left");
        RectNodeState whole = pose.Tree.GetState(frame.OffsetNode);
        result["offset"] = new JsonObject
        {
            ["position"] = new JsonArray(Round(whole.AnchoredPosition.X), Round(whole.AnchoredPosition.Y)),
            ["scale"] = new JsonArray(Round(whole.LocalScale.X), Round(whole.LocalScale.Y)),
            ["rotation"] = Round(whole.LocalEulerAngles.Z),
            ["alpha"] = Round(pose.Alpha),
        };
        result["rigs"] = new JsonArray(pose.Rigs.Select(r =>
        {
            RectNodeState image = pose.Tree.GetState(r.ImageNode);
            Vec2 at = pose.Tree.TransformPoint(r.ImageNode, Vec3.Zero).XY;
            return (JsonNode)new JsonObject
            {
                ["name"] = r.Rig,
                ["at"] = new JsonArray(Round(at.X), Round(at.Y)),
                ["position"] = new JsonArray(Round(image.AnchoredPosition.X), Round(image.AnchoredPosition.Y)),
                ["scale"] = new JsonArray(Round(image.LocalScale.X), Round(image.LocalScale.Y)),
                ["rotation"] = Round(image.LocalEulerAngles.Z),
                ["alpha"] = Round(r.Alpha),
                ["sprite"] = r.Sprite,
            };
        }).ToArray());
    }

    /// <summary>앱의 뷰포트와 같은 조립 — 깊이·focus·무대 뼈대(내장본).</summary>
    private static RectNodeTree BuildTree(RigSchemaFile rig, SceneAsset scene)
        => ScenePreview.Build(rig, scene, null, null, BuiltinTuning.Depth, BuiltinTuning.Focus, BuiltinTuning.StageRig);

    private static void AddNodeValue(JsonObject nodes, RectNodeTree pose, SceneObject actor, ChannelAsset channel)
    {
        string key = actor.KeyOf(channel.Node);

        if (!pose.Contains(key))
        {
            return;
        }

        var entry = nodes[channel.Node] as JsonObject ?? new JsonObject();
        nodes[channel.Node] = entry;
        RectNodeState state = pose.GetState(key);

        switch (channel.Kind)
        {
            case ChannelKind.Position:
                entry["position"] = new JsonArray(Round(state.AnchoredPosition.X), Round(state.AnchoredPosition.Y));
                break;
            case ChannelKind.Scale:
                entry["scale"] = new JsonArray(Round(state.LocalScale.X), Round(state.LocalScale.Y));
                break;
            case ChannelKind.Euler:
                entry["euler"] = new JsonArray(Round(state.LocalEulerAngles.X), Round(state.LocalEulerAngles.Y), Round(state.LocalEulerAngles.Z));
                break;
        }
    }

    /// <summary>읽는 쪽이 0.30000001을 보지 않게 — 소수 넷째 자리까지.</summary>
    private static double Round(float value) => Math.Round(value, 4);

    private static MotionDocument Load(Options options, out RigSchemaFile rig)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = MotionDocumentJson.Read(File.ReadAllText(path));

        // 덤프: --rig, 없으면 문서가 적어 둔 것(문서 기준 상대 경로, 내장본 표시는 빼고), 그것도 없으면 내장본.
        string? dump = options.Value("rig");

        if (dump is null && document.RigSchemas?.Path is { Length: > 0 } recorded && !recorded.StartsWith("builtin:", StringComparison.Ordinal))
        {
            string resolved = Path.IsPathRooted(recorded) ? recorded : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", recorded);
            dump = File.Exists(resolved) ? resolved : null;
        }

        rig = dump is null ? BuiltinTuning.RigSchemas : RigSchemaFile.Load(dump);

        return document;
    }

    private static void Write(TextWriter stdout, JsonNode node) => stdout.WriteLine(node.ToJsonString(Output));

    private sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }

    /// <summary>위치 인자와 <c>--이름 값</c> 옵션.</summary>
    private sealed class Options
    {
        private readonly List<string> _positional = new();
        private readonly Dictionary<string, string> _named = new(StringComparer.Ordinal);

        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    options._positional.Add(args[i]);
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new UsageException($"{args[i]}에 값이 없다");
                }

                options._named[args[i][2..]] = args[++i];
            }

            return options;
        }

        public string Positional(int index, string what)
            => index < _positional.Count ? _positional[index] : throw new UsageException($"{what}가 없다");

        public string? Value(string name) => _named.GetValueOrDefault(name);

        public string Required(string name) => Value(name) ?? throw new UsageException($"--{name}이 있어야 한다");

        public double Number(string name)
            => double.TryParse(Required(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : throw new UsageException($"--{name}은 수여야 한다 ('{Value(name)}')");
    }
}
