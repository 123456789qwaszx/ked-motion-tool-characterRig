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
public static class MtCli
{
    public const string Usage = """
        mt — ked-motion-tool 명령줄 (결과는 stdout의 JSON, 종료 코드 0 됨 · 1 막는 문제 · 2 쓰는 법)

          mt info     <문서>                                   모션·클립·스킨·씬의 목록
          mt validate <문서> [--rig <덤프>]                    문제 목록(모션·클립). 막는 문제가 있으면 종료 코드 1
          mt export   <문서> <motions.json> [--rig <덤프>]     motions.json 내보내기(검증을 지나야 나간다)
          mt pose     <문서> --motion <이름> --time <초> [--object <id>] [--mode relative|absolute] [--rig <덤프>]
          mt pose     <문서> --clip <이름> --time <초> [--rig <덤프>]
                                                               그 시각의 포즈 — 모션·클립이 건드리는 노드의 값, 불투명도, 방금 울린 이벤트
          mt bake     <문서> --clip <이름> --object <id> --name <새 모션> [--out <문서>] [--rig <덤프>]
                                                               클립에서 그 캐릭터에 얹힌 블록을 모션 하나로 구워 문서에 더한다

          mt emojis   <문서> <emojis.json> [--rig <덤프>]      이모지 정의 내보내기(emojis/1-draft — 스킨은 풀어서, 모션은 이름으로)

          mt rigs     <문서> <rigs.json>                       문서가 정의한 리그 내보내기(rig-schemas.json과 같은 모양)

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
                "pose" => Pose(options, stdout),
                "bake" => Bake(options, stdout),
                "emojis" => Emojis(options, stdout),
                "rigs" => Rigs(options, stdout),
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
                ["channels"] = new JsonArray(m.Channels.Select(c => (JsonNode)$"{c.Node} {ChannelKindLabel.Of(c.Kind)}").ToArray()),
                ["events"] = m.Events.Count,
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
            ["emojis"] = new JsonArray(document.Emojis.Select(e => (JsonNode)new JsonObject
            {
                ["name"] = e.Name, ["rig"] = e.Rig, ["skin"] = e.Skin, ["enter"] = e.Enter, ["hold"] = e.Hold, ["exit"] = e.Exit, ["holdSeconds"] = e.HoldSeconds,
            }).ToArray()),
            ["skins"] = new JsonArray(document.Skins.Select(s => (JsonNode)new JsonObject { ["name"] = s.Name, ["rig"] = s.Rig, ["parts"] = s.Parts.Count }).ToArray()),
            ["scenes"] = new JsonArray(document.Scenes.Select(s => (JsonNode)new JsonObject
            {
                ["name"] = s.Name,
                ["objects"] = new JsonArray(s.Objects.Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["rig"] = o.Rig, ["attachTo"] = o.AttachTo }).ToArray()),
            }).ToArray()),
        });
        return 0;
    }

    private static int Validate(Options options, TextWriter stdout)
    {
        MotionDocument document = Load(options, out RigSchemaFile rig);
        (JsonArray problems, bool blocking) = Problems(document, rig);
        Write(stdout, new JsonObject { ["ok"] = !blocking, ["problems"] = problems });
        return blocking ? 1 : 0;
    }

    private static int Export(Options options, TextWriter stdout)
    {
        string target = options.Positional(1, "내보낼 motions.json 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);

        if (!MotionExport.TryExport(document, rig.NodeIds, out string json, out IReadOnlyList<MotionProblem> problems, null, rig.AlphaNodeIds))
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
            ["schema"] = json.Contains(AuthoringFormat.ExportSchemaV3, StringComparison.Ordinal) ? AuthoringFormat.ExportSchemaV3 : AuthoringFormat.ExportSchema,
            ["motions"] = new JsonArray(MotionExport.ToClips(document).Select(c => (JsonNode)c.Name).ToArray()),
        });
        return 0;
    }

    private static int Emojis(Options options, TextWriter stdout)
    {
        string target = options.Positional(1, "내보낼 emojis.json 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);

        if (!EmojisJson.TryExport(document, CameraRig.WithCamera(rig.NodeIds), out string json, out IReadOnlyList<string> problems, rig.AlphaNodeIds))
        {
            Write(stdout, new JsonObject { ["ok"] = false, ["problems"] = new JsonArray(problems.Select(p => (JsonNode)p).ToArray()) });
            return 1;
        }

        File.WriteAllBytes(target, System.Text.Encoding.UTF8.GetBytes(json));
        Write(stdout, new JsonObject
        {
            ["ok"] = true,
            ["path"] = Path.GetFullPath(target),
            ["schema"] = EmojisJson.Schema,
            ["emojis"] = new JsonArray(document.Emojis.Select(e => (JsonNode)e.Name).OrderBy(n => n.GetValue<string>(), StringComparer.Ordinal).ToArray()),
        });
        return 0;
    }

    private static int Rigs(Options options, TextWriter stdout)
    {
        string target = options.Positional(1, "내보낼 rigs.json 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);

        if (document.Rigs.Count == 0)
        {
            throw new InvalidOperationException("문서가 정의한 리그가 없다.");
        }

        File.WriteAllBytes(target, System.Text.Encoding.UTF8.GetBytes(RigAssets.WriteSchemas(document, rig.Dto.capturedUnderParentSize.ToVec2())));
        Write(stdout, new JsonObject
        {
            ["ok"] = true,
            ["path"] = Path.GetFullPath(target),
            ["rigs"] = new JsonArray(document.Rigs.Select(r => (JsonNode)new JsonObject
            {
                ["kind"] = r.Kind,
                ["nodes"] = r.Nodes.Count,
                ["parts"] = r.Nodes.Count(n => n.Image),
            }).ToArray()),
        });
        return 0;
    }

    private static int Pose(Options options, TextWriter stdout)
    {
        MotionDocument document = Load(options, out RigSchemaFile rig);
        double time = options.Number("time");
        var alphas = new Dictionary<string, float>(StringComparer.Ordinal);
        var result = new JsonObject { ["ok"] = true, ["time"] = time };

        if (options.Value("clip") is { } clipName)
        {
            ClipAsset clip = document.FindClip(clipName) ?? throw new KeyNotFoundException($"클립 '{clipName}'이 없다.");
            SceneAsset scene = ClipPreview.SceneOf(document, clip) ?? throw new InvalidOperationException($"클립 '{clipName}'의 씬이 없다.");
            ClipStage stage = ClipStage.Build(document, clip, s => BuildTree(rig, s))!;
            RectNodeTree pose = ClipPreview.Pose(stage.TreeAt(time), document, clip, time, null, stage.CameraAt(time), null, alphas);
            CameraSettings camera = ClipPreview.CameraAt(document, clip, stage.CameraAt(time), time) ?? stage.CameraAt(time);

            // 클립이 건드리는 노드만 — 배역마다, 그 배역의 블록이 쓰는 (노드, 종류).
            var objects = new JsonObject();

            foreach (ClipTrack track in clip.Tracks.Where(t => !t.IsCamera))
            {
                if (ClipPreview.ObjectOf(document, clip, track) is not { } actor)
                {
                    continue;
                }

                var nodes = objects[actor.Id] as JsonObject ?? new JsonObject();
                objects[actor.Id] = nodes;

                foreach (ChannelAsset channel in track.Blocks.Where(b => b.IsMotion).SelectMany(b => document.Find(b.Motion)?.Channels ?? Enumerable.Empty<ChannelAsset>()))
                {
                    AddNodeValue(nodes, pose, alphas, actor, channel);
                }

                nodes["opacity"] = stage.OpacityOf(actor.Id, time);
            }

            result["clip"] = clip.Name;
            result["duration"] = ClipTiming.DurationOf(document, clip);
            result["objects"] = objects;
            result["camera"] = new JsonObject { ["zoom"] = camera.Zoom, ["panX"] = camera.PanX, ["panY"] = camera.PanY };
            result["events"] = new JsonArray(ClipPreview.EventsBetween(document, clip, time - EventWindow, time)
                .Select(e => (JsonNode)new JsonObject { ["time"] = e.Time, ["object"] = e.ObjectId, ["name"] = e.Name, ["value"] = e.Value }).ToArray());
            _ = scene;
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

            if (CameraRig.IsCamera(motion))
            {
                CameraSettings camera = CameraRig.Pose(scene.Camera, motion, time, mode);
                result["camera"] = new JsonObject { ["zoom"] = camera.Zoom, ["panX"] = camera.PanX, ["panY"] = camera.PanY };
            }
            else
            {
                SceneObject actor = options.Value("object") is { } id
                    ? scene.Find(id) ?? throw new KeyNotFoundException($"오브젝트 '{id}'가 없다.")
                    : scene.Objects.FirstOrDefault(o => o.Rig == motion.Rig) ?? throw new InvalidOperationException($"씬에 리그 '{motion.Rig}'의 오브젝트가 없다 — --object로 고른다.");
                RectNodeTree pose = MotionPreview.Pose(BuildTree(rig, scene), motion, time, mode, null, actor.Prefix, alphas);
                var nodes = new JsonObject();

                foreach (ChannelAsset channel in motion.Channels)
                {
                    AddNodeValue(nodes, pose, alphas, actor, channel);
                }

                result["object"] = actor.Id;
                result["nodes"] = nodes;
            }

            result["events"] = new JsonArray(ClipPreview.EventsBetween(motion, time - EventWindow, time)
                .Select(e => (JsonNode)new JsonObject { ["time"] = e.T, ["name"] = e.Name, ["value"] = e.Value }).ToArray());
        }
        else
        {
            throw new UsageException("pose에는 --motion 또는 --clip이 있어야 한다");
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
            ["channels"] = new JsonArray(baked.Channels.Select(c => (JsonNode)$"{c.Node} {ChannelKindLabel.Of(c.Kind)}").ToArray()),
            ["keys"] = baked.Channels.Sum(c => (c.X?.Keys.Count ?? 0) + (c.Y?.Keys.Count ?? 0) + (c.Z?.Keys.Count ?? 0)),
            ["events"] = baked.Events.Count,
        });
        return 0;
    }

    // ── helper ────────────────────────────────────────────────────────

    /// <summary>pose가 "방금 울린 이벤트"로 치는 구간(초) — 앱의 뷰포트와 같다.</summary>
    private const double EventWindow = 0.4;

    private static (JsonArray Problems, bool Blocking) Problems(MotionDocument document, RigSchemaFile rig)
    {
        var problems = new JsonArray();
        bool blocking = false;
        IReadOnlyDictionary<string, ICollection<string>>? nodeIds = CameraRig.WithCamera(rig.NodeIds);

        foreach (MotionProblem p in MotionValidator.Validate(document, nodeIds, null, rig.AlphaNodeIds))
        {
            problems.Add(new JsonObject { ["kind"] = "motion", ["name"] = p.MotionName, ["message"] = p.Message, ["blocking"] = p.Blocking });
            blocking |= p.Blocking;
        }

        foreach (EmojiProblem p in EmojiValidator.Validate(document, nodeIds))
        {
            problems.Add(new JsonObject { ["kind"] = "emoji", ["name"] = p.EmojiName, ["message"] = p.Message, ["blocking"] = p.Blocking });
            blocking |= p.Blocking;
        }

        foreach (ClipProblem p in ClipValidator.Validate(document, nodeIds))
        {
            problems.Add(new JsonObject { ["kind"] = "clip", ["name"] = p.ClipName, ["message"] = p.Message, ["blocking"] = p.Blocking });
            blocking |= p.Blocking;
        }

        // 문서가 정의한 리그 — 스키마 계약의 알림(구조는 유니티가 정한다).
        foreach (SchemaViolation v in SchemaContract.DefinedRigs(document))
        {
            problems.Add(new JsonObject { ["kind"] = "rig", ["name"] = v.Subject, ["message"] = v.Message, ["blocking"] = v.Blocking });
            blocking |= v.Blocking;
        }

        // 문서를 지을 때와 스키마가 달라진 것 — 알림(무엇이 달라졌고 무엇이 그것을 쓰는가).
        if (document.BuiltOn is { } builtOn)
        {
            // rig은 덤프에 문서가 정의한 리그를 더한 것이다 — 지문은 덤프의 리그만 적으니 그것은 빼고 견준다.
            SchemaPrint now = SchemaDiff.PrintOf(rig);
            now.Rigs.RemoveAll(r => document.FindRig(r.Kind) is not null);

            foreach (SchemaViolation v in SchemaDiff.Compare(builtOn, now).Notices(document))
            {
                problems.Add(new JsonObject { ["kind"] = "schema", ["name"] = v.Subject, ["message"] = v.ToString(), ["blocking"] = v.Blocking });
            }
        }

        return (problems, blocking);
    }

    /// <summary>앱의 뷰포트와 같은 조립 — 깊이·focus·무대 뼈대(내장본).</summary>
    private static RectNodeTree BuildTree(RigSchemaFile rig, SceneAsset scene)
        => ScenePreview.Build(rig, scene, null, null, BuiltinTuning.Depth, BuiltinTuning.Focus, BuiltinTuning.StageRig);

    private static void AddNodeValue(JsonObject nodes, RectNodeTree pose, Dictionary<string, float> alphas, SceneObject actor, ChannelAsset channel)
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
            case MotionChannelKind.Position:
                entry["position"] = new JsonArray(Round(state.AnchoredPosition.X), Round(state.AnchoredPosition.Y));
                break;
            case MotionChannelKind.Scale:
                entry["scale"] = new JsonArray(Round(state.LocalScale.X), Round(state.LocalScale.Y));
                break;
            case MotionChannelKind.Euler:
                entry["euler"] = new JsonArray(Round(state.LocalEulerAngles.X), Round(state.LocalEulerAngles.Y), Round(state.LocalEulerAngles.Z));
                break;
            case MotionChannelKind.Alpha:
                entry["alpha"] = Round(alphas.TryGetValue(key, out float alpha) ? alpha : 1f);
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
        rig = RigAssets.Compose(rig, document); // 문서가 정의한 리그(이모지 등)도 한 벌로

        // 손으로(또는 AI가) 쓴 문서는 탄젠트 값을 모른다 — 키의 탄젠트 종류(auto·flat·linear…)대로 값을 세운다. 앱이 편집할 때마다 하는 그 계산이고,
        // 앱이 저장한 문서에는 아무것도 안 바꾼다(이미 그 값이다). free는 적힌 값 그대로 둔다.
        foreach (ComponentCurve curve in document.Motions.SelectMany(m => m.Channels).SelectMany(c => new[] { c.X, c.Y, c.Z }).OfType<ComponentCurve>())
        {
            Mt.Authoring.Graph.TangentMath.Recompute(curve.Keys);
        }

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
