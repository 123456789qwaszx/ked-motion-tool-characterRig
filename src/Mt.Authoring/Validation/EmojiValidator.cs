using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mt.Authoring.Model;

namespace Mt.Authoring.Validation;

/// <summary>이모지 정의의 문제 하나. <see cref="Blocking"/>이 false면 알림이다.</summary>
public sealed record EmojiProblem(string EmojiName, string Message, bool Blocking = true)
{
    public override string ToString() => $"이모지 '{EmojiName}': {Message}";
}

/// <summary>
/// 이모지 정의의 검증(<c>PLAN-parts.md</c> §8). 막는 것: 키·참조(스킨·모션)의 어긋남, 리그가 안 맞는 스킨·모션. 알리는 것: 그림 없음, 없는 자리 노드.
/// </summary>
public static class EmojiValidator
{
    public static IReadOnlyList<EmojiProblem> Validate(MotionDocument document, IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<EmojiProblem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiAsset emoji in document.Emojis)
        {
            void Block(string message) => problems.Add(new EmojiProblem(emoji.Name, message));
            void Notice(string message) => problems.Add(new EmojiProblem(emoji.Name, message, Blocking: false));

            // 붙는 자리 — 코어가 아는 기준점 이름이어야 미리보기가 푼다. 런타임이 이름을 더 알 수 있어 막지는 않는다.
            if (emoji.Locator is not null && !Ked.Presentation.Core.FocusPresetName.TryNormalizeToken(emoji.Locator, out _))
            {
                Notice($"붙는 자리 '{emoji.Locator}'를 모른다 — 미리보기는 오프셋만 쓴다({string.Join("·", Preview.ScenePreview.FocusPresets)})");
            }

            if (string.IsNullOrEmpty(emoji.Name) || emoji.Name.Any(char.IsWhiteSpace))
            {
                Block("키는 빈칸 없는 글이어야 한다");
            }
            else if (!seen.Add(emoji.Name))
            {
                Block("같은 키가 둘이다");
            }

            ICollection<string>? nodes = null;

            if (rigNodeIds is not null && !rigNodeIds.TryGetValue(emoji.Rig ?? "", out nodes))
            {
                Block($"리그 덤프에 rigKind '{emoji.Rig}'가 없다. 있는 것: [{string.Join(", ", rigNodeIds.Keys)}]");
            }

            if (emoji.Skin is null)
            {
                Notice("그림(스킨)이 없다 — 아무것도 안 보인다");
            }
            else if (document.FindSkin(emoji.Skin) is not { } skin)
            {
                Block($"스킨 '{emoji.Skin}'이 없다");
            }
            else if (!string.Equals(skin.Rig, emoji.Rig, StringComparison.Ordinal))
            {
                Block($"스킨 '{skin.Name}'은 리그 '{skin.Rig}'의 것이다(이모지는 '{emoji.Rig}')");
            }
            else if (skin.Parts.Count == 0)
            {
                Notice($"스킨 '{skin.Name}'에 그림을 준 조각이 없다");
            }

            if (emoji.SkinFlipped is not null)
            {
                if (document.FindSkin(emoji.SkinFlipped) is not { } flipped)
                {
                    Block($"반전 판 스킨 '{emoji.SkinFlipped}'이 없다");
                }
                else if (!string.Equals(flipped.Rig, emoji.Rig, StringComparison.Ordinal))
                {
                    Block($"반전 판 스킨 '{flipped.Name}'은 리그 '{flipped.Rig}'의 것이다(이모지는 '{emoji.Rig}')");
                }
            }

            foreach ((string label, string? name) in new[] { ("등장", emoji.Enter), ("머묾", emoji.Hold), ("퇴장", emoji.Exit) })
            {
                if (name is null)
                {
                    continue;
                }

                if (document.Find(name) is not { } motion)
                {
                    Block($"{label} 모션 '{name}'이 없다");
                }
                else if (!ClipValidator.Wears(emoji.Rig ?? "", motion, rigNodeIds))
                {
                    Block($"{label} 모션 '{name}'의 리그 '{motion.Rig}'가 이모지의 리그 '{emoji.Rig}'에 안 맞는다");
                }
            }

            if (emoji.HoldSeconds is { } hold && (!float.IsFinite(hold) || hold < 0f))
            {
                Block($"머무는 시간은 0 이상의 수다 ({hold})");
            }

            if (nodes is not null && !nodes.Contains(emoji.AnchorNode))
            {
                Notice($"자리 노드 '{emoji.AnchorNode}'가 리그에 없다 — 오프셋이 루트에 들어간다");
            }
        }

        return problems;
    }
}

/// <summary>
/// 이모지 정의의 산출물 — <c>emojis.json</c>(<c>emojis/1-draft</c>, 런타임과 합의 전의 초안: <c>PLAN-parts.md</c> §8).
/// 스킨은 이름이 아니라 **풀어서**(조각 → 그림) 싣는다 — 런타임은 툴의 스킨 목록을 모른다. 모션은 이름으로(<c>motions.json</c>에 있다).
/// </summary>
public static class EmojisJson
{
    public const string Schema = "emojis/1-draft";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>막는 문제가 0건일 때만 나간다(이모지의 것과, 이모지가 쓰는 모션의 것).</summary>
    public static bool TryExport(
        MotionDocument document,
        IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds,
        out string json,
        out IReadOnlyList<string> problems,
        IReadOnlyDictionary<string, ICollection<string>>? alphaNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var used = new HashSet<string>(document.Emojis.SelectMany(e => new[] { e.Enter, e.Hold, e.Exit }).OfType<string>(), StringComparer.Ordinal);
        var blocking = EmojiValidator.Validate(document, rigNodeIds).Where(p => p.Blocking).Select(p => p.ToString()).ToList();
        blocking.AddRange(MotionValidator.Validate(document, rigNodeIds, null, alphaNodeIds).Where(p => p.Blocking && used.Contains(p.MotionName)).Select(p => p.ToString()));
        problems = blocking;

        if (blocking.Count > 0)
        {
            json = "";
            return false;
        }

        json = Write(document);
        return true;
    }

    /// <summary>
    /// 스킨을 풀어서 — 조각 → 그림. 한 장이고 제자리면 글 하나(<c>"art/a.png"</c>), 넘길 장이나 놓임이 있으면 객체:
    /// <c>{ "image", "frames": [...], "frameSeconds", "loop", "x", "y", "scale", "rotation" }</c>(기본값인 것은 안 적는다).
    /// </summary>
    private static JsonObject PartsOf(SkinAsset? skin)
    {
        var parts = new JsonObject();

        if (skin is null)
        {
            return parts;
        }

        foreach ((string node, string image) in skin.Parts)
        {
            SkinPartSetup setup = skin.SetupOf(node);

            if (setup.IsDefault)
            {
                parts[node] = image;
                continue;
            }

            var part = new JsonObject { ["image"] = image };

            if (setup.Frames.Count > 0)
            {
                part["frames"] = new JsonArray(setup.Frames.Select(f => (JsonNode)f).ToArray());
                part["frameSeconds"] = setup.FrameSeconds;
                part["loop"] = setup.Loop;
            }

            if (setup.IsPlaced)
            {
                part["x"] = setup.X;
                part["y"] = setup.Y;
                part["scale"] = setup.Scale;
                part["rotation"] = setup.Rotation;
            }

            parts[node] = part;
        }

        return parts;
    }

    public static string Write(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var emojis = new JsonArray();

        foreach (EmojiAsset e in document.Emojis.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var entry = new JsonObject { ["name"] = e.Name, ["rig"] = e.Rig, ["parts"] = PartsOf(document.FindSkin(e.Skin)) };

            // 반전 판 — 캐릭터가 좌우 반전일 때 이 그림을, 뒤집지 않고.
            if (document.FindSkin(e.SkinFlipped) is { } flipped)
            {
                entry["partsFlipped"] = PartsOf(flipped);
            }

            if (e.Enter is not null)
            {
                entry["enter"] = e.Enter;
            }

            if (e.Hold is not null)
            {
                entry["hold"] = e.Hold;
            }

            if (e.Exit is not null)
            {
                entry["exit"] = e.Exit;
            }

            // 머무는 시간이 없으면 끄라고 할 때까지 머문다 — "stay": true. 있으면 그 초만큼 머물고 스스로 사라진다.
            if (e.HoldSeconds is { } seconds)
            {
                entry["holdSeconds"] = seconds;
            }
            else
            {
                entry["stay"] = true;
            }

            entry["anchorNode"] = e.AnchorNode;
            entry["offset"] = new JsonArray(e.OffsetX, e.OffsetY);

            if (e.Locator is not null)
            {
                entry["locator"] = e.Locator;
            }

            emojis.Add(entry);
        }

        return new JsonObject { ["schema"] = Schema, ["emojis"] = emojis }.ToJsonString(Options) + "\n";
    }
}
