using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Validation;

/// <summary>
/// 문서의 이모지 → <c>emojis.json</c>(<c>emojis/2</c>, 런타임 <c>docs/emoji-format.md</c>). 막는 문제가 0건일 때만 나간다(<c>docs/PLAN-emoji.md</c> E4).
///
/// - **구간 자르기** — 구간마다 따로 선 클립이다(구간의 첫 프레임이 클립의 프레임 0). 구간의 양 끝에 키가 없으면 그 프레임의 값과 기울기로
///   키를 세운다 — 잘라도 모양이 같다. 머묾을 잇지 않은 이모지는 타임라인 전체가 <see cref="EmojiAsset.MainClip"/> 하나다.
/// - **초 → 프레임** — 키의 시각은 × fps, 기울기는 ÷ fps(<c>emojis/2</c>의 기울기는 프레임당이다).
/// - **채널** — 자리 X/Y → <c>x · y</c>, 배율 → <c>scaleX · scaleY</c>, 회전 Z → <c>rotation</c>, 불투명도 → <c>alpha</c>,
///   그림(몇 번째) → <c>sprite</c>(그 그림의 스프라이트 키).
/// - **같은 규칙** — 낸 글자를 코어 로더(<see cref="EmojiDefinitionLoader"/> · <see cref="EmojiRules"/>)로 다시 읽어 런타임이 받는지 본다.
///   "툴에서는 되는데 재생에서 사라지는" 이모지를 내지 않는다.
/// </summary>
public static class EmojiExport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new() { IncludeFields = true };

    /// <param name="frame">이모지 리그의 틀. null이면 내장 덤프의 것.</param>
    public static bool TryExport(MotionDocument document, EmojiRigFrame? frame, out string json, out IReadOnlyList<EmojiProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(document);
        frame ??= EmojiRigFrame.Builtin;
        json = "";

        List<EmojiProblem> blocking = EmojiValidator.Validate(document, frame).Where(p => p.Blocking).ToList();

        if (blocking.Count > 0)
        {
            problems = blocking;
            return false;
        }

        string text = ToJson(document, frame).ToJsonString(Options) + "\n";

        // 런타임과 같은 로더로 다시 읽는다 — 거르는 이모지가 있으면 내지 않는다.
        var refused = new List<EmojiProblem>();
        Dictionary<string, EmojiDefinition> loaded = EmojiDefinitionLoader.Build(
            Read(text),
            (name, why) => refused.Add(new EmojiProblem(name ?? "", $"런타임이 읽지 못한다 — {why}")));

        foreach (EmojiAsset emoji in document.Emojis.Where(e => !loaded.ContainsKey(e.Name) && refused.All(r => r.EmojiName != e.Name)))
        {
            refused.Add(new EmojiProblem(emoji.Name, "런타임이 읽지 못한다"));
        }

        if (refused.Count > 0)
        {
            problems = refused;
            return false;
        }

        problems = Array.Empty<EmojiProblem>();
        json = text;
        return true;
    }

    /// <summary>낸 글자를 코어의 DTO로 — 런타임 로더가 받는 모양 그대로.</summary>
    public static EmojisFileDto Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<EmojisFileDto>(json, ReadOptions) ?? throw new JsonException("빈 emojis.json이다.");
    }

    /// <summary>검증 없이 바꾼다 — 미리보기 · 테스트용. 파일로 내보내는 길은 <see cref="TryExport"/>다.</summary>
    public static Dictionary<string, EmojiDefinition> ToDefinitions(MotionDocument document, EmojiRigFrame? frame = null, Action<string, string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return EmojiDefinitionLoader.Build(Read(ToJson(document, frame ?? EmojiRigFrame.Builtin).ToJsonString(Options)), warn);
    }

    /// <summary>문서 → <c>emojis/2</c>의 글자(검증 없이).</summary>
    public static JsonObject ToJson(MotionDocument document, EmojiRigFrame frame)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frame);

        return new JsonObject
        {
            ["schema"] = EmojiDefinitionLoader.Schema,
            ["fps"] = EmojiAsset.Fps,
            ["emojis"] = new JsonArray(document.Emojis.Select(e => (JsonNode)EmojiJson(e, frame)).ToArray()),
        };
    }

    /// <summary>이모지가 내보내는 클립들 — (클립 이름, 시작 프레임, 끝 프레임). 머묾이 없으면 타임라인 전체(main)와 구간들, 있으면 구간들.</summary>
    public static IReadOnlyList<(string Name, int Start, int End)> ClipsOf(EmojiAsset emoji)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        var clips = new List<(string Name, int Start, int End)>();

        if (!emoji.Stays)
        {
            clips.Add((EmojiAsset.MainClip, 0, emoji.LastFrame));
        }

        clips.AddRange(emoji.Spans.Select(s => (s.Name, s.Start, s.End)));
        return clips;
    }

    private static JsonObject EmojiJson(EmojiAsset emoji, EmojiRigFrame frame)
    {
        var json = new JsonObject
        {
            ["name"] = emoji.Name,
            ["offset"] = Xy(emoji.OffsetX, emoji.OffsetY),
            ["rigs"] = new JsonArray(emoji.Rigs.Select(r => (JsonNode)RigJson(r)).ToArray()),
        };

        // 좌우의 예외 — 거짓이면 적지 않는다(런타임이 없으면 거짓으로 읽는다).
        if (emoji.FixedSide)
        {
            json["fixedSide"] = true;
        }

        json["clips"] = new JsonArray(ClipsOf(emoji).Select(c => (JsonNode)ClipJson(emoji, frame, c.Name, c.Start, c.End)).ToArray());

        if (emoji.Stays)
        {
            Put(json, "in", emoji.In);
            Put(json, "hold", emoji.Hold);
            Put(json, "out", emoji.Out);
        }
        else
        {
            json["main"] = EmojiAsset.MainClip;
        }

        return json;
    }

    private static JsonObject RigJson(EmojiRigAsset rig)
    {
        var json = new JsonObject
        {
            ["name"] = rig.Name,
            ["sprite"] = rig.DefaultSprite,
            ["place"] = Xy(rig.PlaceX, rig.PlaceY),
        };

        if (rig.Scale != 1f)
        {
            json["scale"] = Round(rig.Scale);
        }

        if (rig.Rotation != 0f)
        {
            json["rotation"] = Round(rig.Rotation);
        }

        if (rig.Upright)
        {
            json["upright"] = true;
        }

        return json;
    }

    private static JsonObject ClipJson(EmojiAsset emoji, EmojiRigFrame frame, string name, int start, int end)
    {
        var tracks = new JsonArray();

        foreach (ChannelAsset channel in emoji.Timeline.Channels)
        {
            if (!frame.TryTarget(channel.Node, out string rigName))
            {
                continue; // 검증이 막는다
            }

            EmojiRigAsset? rig = emoji.FindRig(rigName);

            foreach ((Axis axis, string token) in TokensOf(channel.Kind))
            {
                if (channel.Get(axis) is not { Keys.Count: > 0 } curve)
                {
                    continue;
                }

                JsonArray keys = channel.Kind == ChannelKind.Image
                    ? SpriteKeys(curve.Keys, rig, start, end)
                    : NumberKeys(curve.Keys, start, end);

                tracks.Add(new JsonObject { ["rig"] = rigName, ["channel"] = token, ["keys"] = keys });
            }
        }

        return new JsonObject { ["name"] = name, ["length"] = end - start + 1, ["tracks"] = tracks };
    }

    private static IEnumerable<(Axis Axis, string Token)> TokensOf(ChannelKind kind) => kind switch
    {
        ChannelKind.Position => new[] { (Axis.X, "x"), (Axis.Y, "y") },
        ChannelKind.Scale => new[] { (Axis.X, "scaleX"), (Axis.Y, "scaleY") },
        ChannelKind.Euler => new[] { (Axis.Z, "rotation") },
        ChannelKind.Alpha => new[] { (Axis.X, "alpha") },
        ChannelKind.Image => new[] { (Axis.X, "sprite") },
        _ => Array.Empty<(Axis, string)>(),
    };

    /// <summary>
    /// 수치 키를 구간 [start, end]로 자른다. 안의 키는 그대로(시각만 당긴다), 양 끝에 키가 없으면 그 프레임의 값과 기울기로 세운다.
    /// 키가 아닌 자리라 곡선은 매끄럽다 — 양쪽 기울기가 같다. 그 자리가 계단 안이면 세운 시작 키도 계단이다.
    /// </summary>
    internal static JsonArray NumberKeys(IReadOnlyList<KeyAsset> keys, int start, int end)
    {
        CurveKey[] core = TangentMath.ToCurveKeys(keys);
        var cut = new List<(float F, float V, float In, float Out, bool Step)>();
        const float tolerance = 1e-3f;

        float FrameOf(KeyAsset k) => k.T * EmojiAsset.Fps;

        if (!keys.Any(k => Math.Abs(FrameOf(k) - start) <= tolerance))
        {
            (float value, float slope, bool step) = At(core, keys, start);
            cut.Add((start, value, slope, slope, step));
        }

        foreach (KeyAsset k in keys)
        {
            float f = FrameOf(k);

            if (f >= start - tolerance && f <= end + tolerance)
            {
                cut.Add((Math.Clamp(f, start, end), k.V, k.InTangent / EmojiAsset.Fps, k.OutTangent / EmojiAsset.Fps, k.Tangent == TangentMode.Step));
            }
        }

        if (end > start && !keys.Any(k => Math.Abs(FrameOf(k) - end) <= tolerance))
        {
            (float value, float slope, _) = At(core, keys, end);
            cut.Add((end, value, slope, slope, false));
        }

        // 곡선은 첫 키 앞과 마지막 키 뒤에서 끝값으로 평평하다. 세운 끝 키가 그 바깥이면, 그쪽을 향한 진짜 키의 기울기를 0으로 —
        // 남겨 두면 세운 키까지 Hermite가 휘어 모양이 달라진다.
        if (keys.Count > 0)
        {
            float first = FrameOf(keys[0]);
            float last = FrameOf(keys[^1]);

            for (int c = 0; c < cut.Count; c++)
            {
                (float f, float v, float i, float o, bool step) = cut[c];

                if (start < first - tolerance && Math.Abs(f - first) <= tolerance)
                {
                    cut[c] = (f, v, 0f, o, step);
                }

                if (end > last + tolerance && Math.Abs(f - last) <= tolerance)
                {
                    cut[c] = (f, v, cut[c].In, 0f, step);
                }
            }
        }

        var json = new JsonArray();

        foreach ((float f, float v, float i, float o, bool step) in cut.OrderBy(c => c.F))
        {
            var key = new JsonObject { ["f"] = Round(f - start), ["v"] = Round(v) };

            if (Round(i) != 0d)
            {
                key["i"] = Round(i);
            }

            if (step)
            {
                key["step"] = true;
            }
            else if (Round(o) != 0d)
            {
                key["o"] = Round(o);
            }

            json.Add(key);
        }

        return json;
    }

    /// <summary>그림 키를 구간으로 자른다 — 시작에 키가 없으면 그때 보이는 그림으로 세운다(첫 키 앞은 기본 그림이라 그 경우는 세우지 않는다).</summary>
    internal static JsonArray SpriteKeys(IReadOnlyList<KeyAsset> keys, EmojiRigAsset? rig, int start, int end)
    {
        var json = new JsonArray();
        const float tolerance = 1e-3f;
        KeyAsset? before = keys.LastOrDefault(k => k.T * EmojiAsset.Fps < start - tolerance);
        bool keyAtStart = keys.Any(k => Math.Abs(k.T * EmojiAsset.Fps - start) <= tolerance);

        if (!keyAtStart && before is not null)
        {
            json.Add(new JsonObject { ["f"] = 0, ["s"] = SpriteOf(rig, before.V) });
        }

        foreach (KeyAsset k in keys)
        {
            float f = k.T * EmojiAsset.Fps;

            if (f >= start - tolerance && f <= end + tolerance)
            {
                json.Add(new JsonObject { ["f"] = Round(Math.Clamp(f, start, end) - start), ["s"] = SpriteOf(rig, k.V) });
            }
        }

        return json;
    }

    private static string? SpriteOf(EmojiRigAsset? rig, float index)
    {
        int i = (int)Math.Round(index);
        return rig is not null && i >= 0 && i < rig.Images.Count ? EmojiRigAsset.SpriteKeyOf(rig.Images[i]) : null;
    }

    /// <summary>
    /// 프레임 f의 값과 기울기(프레임당). 계단 구간 안이면 그 계단의 값, 기울기 0, 계단이다.
    /// 기울기는 코어 <see cref="CurveFunctions.Evaluate"/>의 Hermite 식을 미분한 값이다 — 수치 미분의 오차를 키에 싣지 않는다.
    /// </summary>
    private static (float Value, float Slope, bool Step) At(CurveKey[] core, IReadOnlyList<KeyAsset> keys, int frame)
    {
        float t = EmojiAsset.SecondsOf(frame);
        float value = CurveFunctions.Evaluate(core, t);
        int next = Array.FindIndex(core, k => k.Time > t);

        if (next <= 0)
        {
            return (value, 0f, false); // 첫 키 앞 · 마지막 키 뒤는 끝값으로 평평하다
        }

        CurveKey k0 = core[next - 1];
        CurveKey k1 = core[next];
        float dt = k1.Time - k0.Time;

        if (float.IsInfinity(k0.OutTangent) || float.IsInfinity(k1.InTangent))
        {
            return (k0.Value, 0f, keys[next - 1].Tangent == TangentMode.Step);
        }

        if (dt <= 0f)
        {
            return (value, 0f, false);
        }

        float u = (t - k0.Time) / dt;
        float u2 = u * u;
        float d00 = 6f * u2 - 6f * u;
        float d10 = 3f * u2 - 4f * u + 1f;
        float d01 = -6f * u2 + 6f * u;
        float d11 = 3f * u2 - 2f * u;
        float slopePerSecond = (d00 * k0.Value + d10 * k0.OutTangent * dt + d01 * k1.Value + d11 * k1.InTangent * dt) / dt;

        return (value, slopePerSecond / EmojiAsset.Fps, false);
    }

    private static JsonObject Xy(float x, float y) => new() { ["x"] = Round(x), ["y"] = Round(y) };

    private static void Put(JsonObject json, string key, string? value)
    {
        if (value is not null)
        {
            json[key] = value;
        }
    }

    /// <summary>소수 넷째 자리까지 — 0.30000001을 파일에 남기지 않는다.</summary>
    private static double Round(float value) => Math.Round(value, 4);
}
