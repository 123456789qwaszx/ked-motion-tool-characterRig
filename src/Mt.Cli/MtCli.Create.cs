using System.Globalization;
using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Presets;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;

namespace Mt.Cli;

/// <summary>
/// 만드는 명령 — 파일을 손으로 쓰지 않고 문서를 짓는다(new · add-motion · add-emoji · key · remove — 나머지는 <c>MtCli.Build.cs</c>)와
/// 무엇으로 지을 수 있는지 묻는 명령(controllers · presets).
/// 화면과 같은 편집 명령(<see cref="MotionEditor"/>)을 지나므로 화면에서 만든 것과 같은 문서가 나오고, 화면이 거절하는 것은 여기서도 거절된다.
/// 고친 문서는 그 자리에 다시 쓴다 — 쓴 뒤의 문제 목록을 같이 낸다.
/// </summary>
public static partial class MtCli
{
    private static int New(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"'{path}'가 이미 있다 — 덮어쓰지 않는다.");
        }

        var document = new MotionDocument();
        document.EnsureDefaultScene();
        return Save(path, document, BuiltinTuning.RigSchemas, stdout, new JsonObject { ["made"] = "document" });
    }

    private static int Controllers(Options options, TextWriter stdout)
    {
        string kind = options.Value("rig-kind") ?? SceneObject.DefaultRig;
        ControllerRig table = BuiltinTuning.Controllers.Rig(kind) ?? throw new KeyNotFoundException($"리그 '{kind}'의 컨트롤러 표가 없다.");
        var list = new JsonArray();

        foreach (Controller c in table.Controllers)
        {
            list.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["group"] = c.Group.Label,
                ["play"] = c.Group.Mode == MotionChannelMode.Absolute ? "absolute" : "relative",
                ["label"] = c.Label,
                ["node"] = c.Node,
                ["kind"] = KindToken(c.Kind),
                ["axes"] = new JsonArray(c.Axes.Select(a => (JsonNode)a.ToString().ToLowerInvariant()).ToArray()),
                ["unit"] = c.Unit,
                ["note"] = c.Note,
            });
        }

        Write(stdout, new JsonObject { ["rig"] = kind, ["controllers"] = list });
        return 0;
    }

    private static int Presets(TextWriter stdout)
    {
        Write(stdout, new JsonObject
        {
            ["motions"] = new JsonArray(MotionPresets.All.Select(p => (JsonNode)new JsonObject
            {
                ["id"] = p.Id,
                ["label"] = p.Label,
                ["note"] = p.Note,
                ["duration"] = p.Duration,
                ["end"] = p.End == MotionEnd.Restore ? "restore" : "commit",
            }).ToArray()),
            ["emojiMoves"] = new JsonArray(EmojiMoves.All.Select(m => (JsonNode)new JsonObject
            {
                ["id"] = MoveToken(m),
                ["label"] = EmojiMoves.Label(m),
                ["note"] = EmojiMoves.Describe(m),
                ["for"] = EmojiMoves.Role(m) switch { EmojiLifeRole.In => "in", EmojiLifeRole.Hold => "hold", _ => "out" },
            }).ToArray()),
        });
        return 0;
    }

    private static int AddMotion(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        string rigKind = options.Value("rig-kind") ?? SceneObject.DefaultRig;
        string name;

        if (options.Value("preset") is { } presetId)
        {
            MotionPreset preset = MotionPresets.Find(presetId) ?? throw new KeyNotFoundException($"프리셋 '{presetId}'가 없다 — mt presets로 본다.");
            ControllerRig table = BuiltinTuning.Controllers.Rig(rigKind) ?? throw new KeyNotFoundException($"리그 '{rigKind}'의 컨트롤러 표가 없다.");
            name = MotionPresets.Add(editor, preset, rigKind, table);

            if (options.Value("name") is { } wanted && wanted != name)
            {
                editor.RenameMotion(name, wanted);
                name = wanted;
            }
        }
        else
        {
            name = options.Required("name");
            float duration = options.Value("duration") is null ? 1f : (float)options.Number("duration");
            MotionEnd end = options.Value("end") switch
            {
                null or "restore" => MotionEnd.Restore,
                "commit" => MotionEnd.Commit,
                var other => throw new UsageException($"--end는 restore 또는 commit이다 ('{other}')"),
            };
            editor.AddMotion(name, rigKind, duration, end);
        }

        return Save(path, editor.Document, rig, stdout, new JsonObject { ["made"] = "motion", ["name"] = name });
    }

    private static int AddEmoji(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        string name = options.Required("name");
        string[] images = (options.Value("images") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool stays = options.Value("life") switch
        {
            null or "once" => false,
            "stay" => true,
            var other => throw new UsageException($"--life는 once 또는 stay다 ('{other}')"),
        };
        EmojiMove? @in = Move(options, "in", EmojiLifeRole.In);
        EmojiMove? hold = Move(options, "hold", EmojiLifeRole.Hold);
        EmojiMove? @out = Move(options, "out", EmojiLifeRole.Out);

        if (!EmojiRigFrame.TryRead(rig, out EmojiRigFrame frame, out string why))
        {
            throw new InvalidOperationException(why);
        }

        EmojiAsset emoji = editor.StartEmoji(new EmojiStart(name, images, stays, @in, hold, @out), frame);

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = "emoji",
            ["name"] = emoji.Name,
            ["pictures"] = new JsonArray(emoji.Rigs.Select(r => (JsonNode)r.Name).ToArray()),
            ["frames"] = emoji.LastFrame + 1,
            ["spans"] = new JsonArray(emoji.Spans.Select(s => (JsonNode)$"{s.Name} {s.Start}~{s.End}").ToArray()),
        });
    }

    /// <summary>
    /// 키를 놓는다 — 모션: <c>--motion</c>과 (<c>--controller</c> 또는 <c>--node</c> + <c>--kind</c>), 시각은 초.
    /// 이모지: <c>--emoji</c>와 <c>--kind</c>(대상은 <c>--picture</c>, 없으면 이모지 전체), 시각은 프레임.
    /// 한 개는 <c>--time</c>/<c>--frame</c> + <c>--value</c>, 여럿은 <c>--keys "시각:값,시각:값"</c>.
    /// </summary>
    private static int Key(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        TangentMode tangent = options.Value("tangent") is { } t
            ? Enum.TryParse(t, ignoreCase: true, out TangentMode parsed) && parsed != TangentMode.Free
                ? parsed
                : throw new UsageException($"--tangent는 auto · flat · linear · spline · plateau · step이다 ('{t}')")
            : TangentMode.Flat;
        (string motionName, string node, ChannelKind kind, Axis axis, bool frames) = Target(options, document, rig);
        List<(float Time, float Value)> keys = ParseKeys(options, frames);

        if (ChannelKinds.IsAlwaysStepped(kind))
        {
            tangent = TangentMode.Step;
        }

        editor.Batch(MotionChangeKind.Keys, () =>
        {
            if (document.Find(motionName)!.FindChannel(node, kind) is null)
            {
                editor.AddChannel(motionName, node, kind);
            }

            foreach ((float time, float value) in keys)
            {
                editor.AddKey(motionName, node, kind, axis, new KeyAsset { T = time, V = value, Tangent = tangent });
            }
        });

        ComponentCurve curve = editor.Document.Find(motionName)!.FindChannel(node, kind)!.Get(axis)!;

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = "keys",
            ["node"] = node,
            ["kind"] = KindToken(kind),
            ["axis"] = axis.ToString().ToLowerInvariant(),
            ["keys"] = new JsonArray(curve.Keys.Select(k => (JsonNode)new JsonObject
            {
                [frames ? "frame" : "t"] = frames ? EmojiAsset.FrameOf(k.T) : k.T,
                ["v"] = k.V,
                ["tangent"] = k.Tangent.ToString().ToLowerInvariant(),
            }).ToArray()),
        });
    }

    private static int Remove(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        var removed = new JsonObject { ["made"] = "removed" };

        if (options.Value("motion") is { } motion)
        {
            editor.RemoveMotion(motion);
            removed["motion"] = motion;
        }
        else if (options.Value("emoji") is { } emoji)
        {
            editor.RemoveEmoji(emoji);
            removed["emoji"] = emoji;
        }
        else if (options.Value("clip") is { } clip)
        {
            removed["clip"] = clip;

            if (options.Value("track") is null)
            {
                editor.RemoveClip(clip);
            }
            else if (options.Value("block") is null)
            {
                int track = (int)options.Number("track");
                editor.RemoveClipTrack(clip, track);
                removed["track"] = track;
            }
            else
            {
                int track = (int)options.Number("track"), block = (int)options.Number("block");
                editor.RemoveClipBlock(clip, track, block);
                removed["track"] = track;
                removed["block"] = block;
            }
        }
        else if (options.Value("object") is { } objectId)
        {
            editor.RemoveSceneObject(options.Value("scene"), objectId);
            removed["object"] = objectId;
        }
        else
        {
            throw new UsageException("remove에는 --motion · --emoji · --clip · --object 중 하나가 있어야 한다");
        }

        return Save(path, editor.Document, rig, stdout, removed);
    }

    // ── helper ────────────────────────────────────────────────────────

    /// <summary>
    /// 키가 놓이는 곳 — 모션은 손잡이(<c>--controller</c>)나 노드 · 종류로, 이모지는 그림(<c>--picture</c>, 없으면 전체)과 종류로.
    /// 이모지면 시각을 프레임으로 센다.
    /// </summary>
    private static (string Motion, string Node, ChannelKind Kind, Axis Axis, bool Frames) Target(Options options, MotionDocument document, RigSchemaFile rig)
    {
        string motionName, node;
        ChannelKind kind;
        bool frames;
        IReadOnlyList<Axis>? allowed = null; // 손잡이로 골랐으면 그 손잡이의 성분만

        if (options.Value("emoji") is { } emojiName)
        {
            EmojiAsset emoji = document.FindEmoji(emojiName) ?? throw new KeyNotFoundException($"이모지 '{emojiName}'이 없다.");

            if (!EmojiRigFrame.TryRead(rig, out EmojiRigFrame frame, out string why))
            {
                throw new InvalidOperationException(why);
            }

            string picture = options.Value("picture") ?? "";

            if (picture.Length > 0 && emoji.FindRig(picture) is null)
            {
                throw new KeyNotFoundException($"이모지 '{emojiName}'에 그림 '{picture}'이 없다 ({string.Join(" · ", emoji.Rigs.Select(r => r.Name))}).");
            }

            motionName = EmojiAsset.TimelineNameOf(emoji.Name);
            node = frame.TargetNode(picture);
            kind = ParseKind(options.Required("kind"));
            frames = true;
        }
        else
        {
            motionName = options.Required("motion");
            MotionAsset motion = document.Find(motionName) ?? throw new KeyNotFoundException($"모션 '{motionName}'이 없다.");
            frames = false;

            if (options.Value("controller") is { } controllerId)
            {
                ControllerRig table = BuiltinTuning.Controllers.Rig(motion.Rig) ?? throw new KeyNotFoundException($"리그 '{motion.Rig}'의 컨트롤러 표가 없다.");
                Controller c = table.Find(controllerId)
                    ?? throw new KeyNotFoundException($"컨트롤러 '{controllerId}'가 없다 ({string.Join(" · ", table.Controllers.Select(x => x.Id))}).");
                node = c.Node;
                kind = c.Kind;
                allowed = c.Axes;
            }
            else
            {
                node = options.Required("node");
                kind = ParseKind(options.Required("kind"));
            }
        }

        Axis axis = Enum.TryParse(options.Required("axis"), ignoreCase: true, out Axis a) ? a : throw new UsageException($"--axis는 x · y · z다 ('{options.Value("axis")}')");

        if (frames && kind == ChannelKind.Euler)
        {
            allowed = new[] { Axis.Z }; // 이모지의 회전은 z뿐이다
        }

        allowed ??= ChannelKinds.Axes(kind);

        if (!allowed.Contains(axis))
        {
            throw new InvalidOperationException($"{KindToken(kind)}의 성분은 {string.Join(" · ", allowed.Select(x => x.ToString().ToLowerInvariant()))}다 ({axis.ToString().ToLowerInvariant()}는 없다).");
        }

        return (motionName, node, kind, axis, frames);
    }

    /// <summary>문서를 그 자리에 쓰고, 한 일과 쓴 뒤의 문제를 낸다. 만드는 도중의 문서는 문제가 있기 마련이라 종료 코드는 0이다.</summary>
    private static int Save(string path, MotionDocument document, RigSchemaFile rig, TextWriter stdout, JsonObject done)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, MotionDocumentJson.Write(document));
        (JsonArray problems, bool blocking) = Problems(document, rig);
        done["ok"] = true;
        done["path"] = Path.GetFullPath(path);
        done["blocking"] = blocking;
        done["problems"] = problems;
        Write(stdout, done);
        return 0;
    }

    private static List<(float Time, float Value)> ParseKeys(Options options, bool frames)
    {
        string unit = frames ? "frame" : "time";
        var keys = new List<(float, float)>();

        float At(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double at))
            {
                throw new UsageException($"시각이 수가 아니다 ('{text}')");
            }

            return frames ? EmojiAsset.SecondsOf((float)at) : (float)at;
        }

        if (options.Value("keys") is { } list)
        {
            foreach (string pair in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = pair.Split(':');

                if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw new UsageException($"--keys는 \"시각:값,시각:값\"이다 ('{pair}')");
                }

                keys.Add((At(parts[0]), (float)value));
            }
        }
        else
        {
            keys.Add((At(options.Required(unit)), (float)options.Number("value")));
        }

        return keys.Count > 0 ? keys : throw new UsageException("놓을 키가 없다");
    }

    private static ChannelKind ParseKind(string text) => text.ToLowerInvariant() switch
    {
        "position" => ChannelKind.Position,
        "scale" => ChannelKind.Scale,
        "euler" or "rotate" => ChannelKind.Euler,
        "alpha" => ChannelKind.Alpha,
        "image" => ChannelKind.Image,
        _ => throw new UsageException($"--kind는 position · scale · euler · alpha · image다 ('{text}')"),
    };

    /// <summary>저작본에 적는 낱말 — 회전은 <c>euler</c>다(화면은 rotate라고 적는다).</summary>
    private static string KindToken(ChannelKind kind) => kind == ChannelKind.Euler ? "euler" : kind.ToString().ToLowerInvariant();

    private static string MoveToken(EmojiMove move) => move switch
    {
        EmojiMove.PopIn => "pop_in",
        EmojiMove.FadeIn => "fade_in",
        EmojiMove.Pulse => "pulse",
        EmojiMove.Shake => "shake",
        EmojiMove.FloatOut => "float_out",
        EmojiMove.ShrinkOut => "shrink_out",
        _ => move.ToString().ToLowerInvariant(),
    };

    private static EmojiMove? Move(Options options, string name, EmojiLifeRole role)
    {
        if (options.Value(name) is not { } token || token == "none")
        {
            return null;
        }

        foreach (EmojiMove move in EmojiMoves.All)
        {
            if (MoveToken(move) == token)
            {
                return EmojiMoves.Role(move) == role
                    ? move
                    : throw new UsageException($"'{token}'은 --{name}의 움직임이 아니다 — mt presets로 본다.");
            }
        }

        throw new UsageException($"모르는 움직임 '{token}' — mt presets로 본다.");
    }
}
