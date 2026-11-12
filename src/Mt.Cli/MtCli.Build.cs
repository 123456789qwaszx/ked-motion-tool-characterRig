using System.Globalization;
using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Cli;

/// <summary>
/// 만드는 명령의 나머지 — 키 하나 지우기(unkey), 씬의 캐릭터(object), 클립과 블록(add-clip · block),
/// 이모지의 구간(span)과 그림의 기본 자리(picture). 적지 않은 값은 지금 값 그대로 둔다.
/// </summary>
public static partial class MtCli
{
    private static int Unkey(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        (string motionName, string node, ChannelKind kind, Axis axis, bool frames) = Target(options, document, rig);
        float time = frames ? EmojiAsset.SecondsOf((float)options.Number("frame")) : (float)options.Number("time");
        IReadOnlyList<KeyAsset> keys = document.Find(motionName)!.FindChannel(node, kind)?.Get(axis)?.Keys ?? (IReadOnlyList<KeyAsset>)Array.Empty<KeyAsset>();
        int index = -1;

        for (int i = 0; i < keys.Count; i++)
        {
            if (Math.Abs(keys[i].T - time) <= 1e-4f)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            string at = frames ? $"{options.Value("frame")}프레임" : $"{options.Value("time")}초";
            string have = string.Join(" · ", keys.Select(k => frames
                ? EmojiAsset.FrameOf(k.T).ToString(CultureInfo.InvariantCulture)
                : k.T.ToString("0.####", CultureInfo.InvariantCulture)));
            throw new KeyNotFoundException($"{node} {KindToken(kind)} {axis.ToString().ToLowerInvariant()}의 {at}에 키가 없다 (있는 것: {(have.Length == 0 ? "없음" : have)}).");
        }

        editor.RemoveKeys(motionName, new[] { new KeyRef(new CurveRef(node, kind, axis), index) });

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = "unkeyed",
            ["node"] = node,
            ["kind"] = KindToken(kind),
            ["axis"] = axis.ToString().ToLowerInvariant(),
            ["left"] = editor.Document.Find(motionName)!.FindChannel(node, kind)?.Get(axis)?.Keys.Count ?? 0,
        });
    }

    /// <summary>씬의 캐릭터 — 없으면 세우고, 있으면 적은 값만 바꾼다.</summary>
    private static int SceneObjectCommand(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        string id = options.Required("id");
        string? sceneName = options.Value("scene");
        document.EnsureDefaultScene();
        SceneAsset scene = (sceneName is null ? document.ActiveSceneAsset : document.FindScene(sceneName)) ?? throw new KeyNotFoundException($"씬 '{sceneName}'이 없다.");
        bool made = scene.Find(id) is null;

        editor.Batch(MotionChangeKind.Scene, () =>
        {
            if (made)
            {
                editor.AddSceneObject(scene.Name, id, options.Value("rig-kind") ?? SceneObject.DefaultRig);
            }

            SceneObject o = scene.Find(id)!;
            editor.SetObjectPlacement(
                scene.Name,
                id,
                Float(options, "x", o.PositionX),
                Float(options, "y", o.PositionY),
                Float(options, "rotation", o.RotationZ),
                Float(options, "scale-x", Float(options, "scale", o.ScaleX)),
                Float(options, "scale-y", Float(options, "scale", o.ScaleY)));
        });

        SceneObject done = scene.Find(id)!;

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = made ? "object" : "object-changed",
            ["scene"] = scene.Name,
            ["id"] = done.Id,
            ["rigKind"] = done.Rig,
            ["x"] = done.PositionX,
            ["y"] = done.PositionY,
            ["rotation"] = done.RotationZ,
            ["scaleX"] = done.ScaleX,
            ["scaleY"] = done.ScaleY,
        });
    }

    private static int AddClip(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        ClipAsset clip = editor.AddClip(options.Required("name"), options.Value("scene"));

        if (options.Value("duration") is not null)
        {
            editor.SetClipDuration(clip.Name, (float)options.Number("duration"));
        }

        return Save(path, editor.Document, rig, stdout, new JsonObject { ["made"] = "clip", ["name"] = clip.Name, ["scene"] = clip.Scene });
    }

    /// <summary>
    /// 클립에 모션 블록 하나 — 배역(<c>--object</c>의 캐릭터가 맡는다)이 없으면 세우고, 그 배역의 트랙이 없으면 더한다.
    /// 같은 노드를 쓰는 블록이 같은 때에 겹치면 검증이 말한다 — 그때는 <c>--track new</c>로 줄을 나누거나 <c>--at</c>을 옮긴다.
    /// </summary>
    private static int Block(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        string clipName = options.Required("clip");
        ClipAsset clip = document.Clips.FirstOrDefault(c => string.Equals(c.Name, clipName, StringComparison.Ordinal)) ?? throw new KeyNotFoundException($"클립 '{clipName}'이 없다.");
        string motionName = options.Required("motion");
        MotionAsset motion = document.Find(motionName) ?? throw new KeyNotFoundException($"모션 '{motionName}'이 없다.");
        string? objectId = options.Value("object");
        string role = options.Value("role") ?? objectId ?? (clip.Roles.Count == 1 ? clip.Roles[0].Name : throw new UsageException("block에는 --object(맡을 캐릭터)나 --role이 있어야 한다"));
        MotionChannelMode? mode = options.Value("mode") is { } m
            ? Enum.TryParse(m, ignoreCase: true, out MotionChannelMode parsed) ? parsed : throw new UsageException($"--mode는 relative · absolute다 ('{m}')")
            : null;
        int track = -1, index = -1;

        editor.Batch(MotionChangeKind.Clip, () =>
        {
            if (clip.FindRole(role) is null)
            {
                editor.AddClipRole(clip.Name, role, motion.Rig, objectId ?? role);
            }
            else if (objectId is not null)
            {
                editor.BindClipRole(clip.Name, role, objectId);
            }

            string? wanted = options.Value("track");

            if (wanted is null)
            {
                track = clip.Tracks.FindIndex(t => string.Equals(t.Role, role, StringComparison.Ordinal));
            }
            else if (wanted != "new")
            {
                track = int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : throw new UsageException($"--track은 번호나 new다 ('{wanted}')");
            }

            if (track < 0)
            {
                track = editor.AddClipTrack(clip.Name, role);
            }

            index = editor.AddClipBlock(clip.Name, track, motion.Name, (float)options.Number("at"), mode);
        });

        ClipBlock block = clip.Tracks[track].Blocks[index];

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = "block",
            ["clip"] = clip.Name,
            ["role"] = role,
            ["track"] = track,
            ["block"] = index,
            ["motion"] = block.Motion,
            ["at"] = block.At,
            ["mode"] = block.Mode.ToString().ToLowerInvariant(),
        });
    }

    /// <summary>이모지의 구간 — 없으면 더하고, 있으면 옮긴다(프레임, 끝 포함).</summary>
    private static int Span(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        string emojiName = options.Required("emoji");
        EmojiAsset emoji = document.FindEmoji(emojiName) ?? throw new KeyNotFoundException($"이모지 '{emojiName}'이 없다.");
        string name = options.Required("name");
        int start = Frame(options, "start"), end = Frame(options, "end");
        bool made = emoji.FindSpan(name) is null;

        if (made)
        {
            editor.AddEmojiSpan(emoji.Name, name, start, end);
        }
        else
        {
            editor.SetEmojiSpan(emoji.Name, name, start, end);
        }

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = made ? "span" : "span-moved",
            ["emoji"] = emoji.Name,
            ["name"] = name,
            ["start"] = start,
            ["end"] = end,
            ["linked"] = string.Equals(emoji.In, name, StringComparison.Ordinal) ? "in"
                : string.Equals(emoji.Hold, name, StringComparison.Ordinal) ? "hold"
                : string.Equals(emoji.Out, name, StringComparison.Ordinal) ? "out"
                : null,
        });
    }

    /// <summary>이모지의 그림의 기본 자리 · 배율 · 회전(키가 아니다) — 적은 값만 바꾼다.</summary>
    private static int Picture(Options options, TextWriter stdout)
    {
        string path = options.Positional(0, "문서 경로");
        MotionDocument document = Load(options, out RigSchemaFile rig);
        var editor = new MotionEditor(document);
        string emojiName = options.Required("emoji");
        EmojiAsset emoji = document.FindEmoji(emojiName) ?? throw new KeyNotFoundException($"이모지 '{emojiName}'이 없다.");
        string pictureName = options.Required("picture");
        EmojiRigAsset picture = emoji.FindRig(pictureName)
            ?? throw new KeyNotFoundException($"이모지 '{emojiName}'에 그림 '{pictureName}'이 없다 ({string.Join(" · ", emoji.Rigs.Select(r => r.Name))}).");

        editor.SetEmojiRigPlace(
            emoji.Name,
            picture.Name,
            Float(options, "x", picture.PlaceX),
            Float(options, "y", picture.PlaceY),
            Float(options, "scale", picture.Scale),
            Float(options, "rotation", picture.Rotation));

        return Save(path, editor.Document, rig, stdout, new JsonObject
        {
            ["made"] = "picture-placed",
            ["emoji"] = emoji.Name,
            ["picture"] = picture.Name,
            ["x"] = picture.PlaceX,
            ["y"] = picture.PlaceY,
            ["scale"] = picture.Scale,
            ["rotation"] = picture.Rotation,
        });
    }

    private static float Float(Options options, string name, float otherwise)
        => options.Value(name) is null ? otherwise : (float)options.Number(name);

    private static int Frame(Options options, string name)
    {
        double value = options.Number(name);
        return Math.Abs(value - Math.Round(value)) < 1e-6 ? (int)Math.Round(value) : throw new UsageException($"--{name}은 프레임(정수)이다 ('{options.Value(name)}')");
    }
}
