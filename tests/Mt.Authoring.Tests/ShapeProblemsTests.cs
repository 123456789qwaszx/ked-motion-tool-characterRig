using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 모양 규칙의 문제를 전부 말한다 — 코어의 검사는 처음 걸린 하나에서 멈추지만 파일로 만드는 사람은 한 번에 다 봐야 한다.
/// 규칙은 코어의 것 그대로여야 한다: 코어가 됐다고 하면 문제가 없고, 코어가 말한 첫 문제는 목록의 맨 앞이다.
/// </summary>
public class ShapeProblemsTests
{
    private static MotionDocument Read(string motions) => MotionDocumentJson.Read("{ \"schema\": \"motion-authoring/2\", \"motions\": [" + motions + "] }");

    private const string Broken = """
        { "name": "Bad Name", "rig": "character", "duration": 1, "end": "restore", "channels": [
          { "node": "CharacterPortrait_Track_Move", "kind": "position",
            "x": { "keys": [ { "t": 0.1, "v": 0, "tangent": "flat" }, { "t": 1, "v": 0, "tangent": "flat" } ] },
            "y": { "keys": [ { "t": 0, "v": 0, "tangent": "flat" }, { "t": 0.8, "v": 0, "tangent": "flat" } ] } },
          { "node": "CharacterPortrait_SwayPivot", "kind": "euler",
            "z": { "keys": [ { "t": 0, "v": 0, "tangent": "flat" } ] } } ] }
        """;

    [Fact]
    public void 걸린_것을_한_번에_다_말한다()
    {
        List<string> messages = MotionValidator.Validate(Read(Broken)).Where(p => p.Blocking).Select(p => p.Message).ToList();

        Assert.Equal(4, messages.Count);
        Assert.Contains(messages, m => m.Contains("이름은 [a-z0-9_]+", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("채널 0: X: 첫 키 시각이 0이 아니다", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("채널 0: Y: 마지막 키 시각이 길이와 다르다", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("채널 1: Z: 키가 2개 이상이어야 한다", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "name": "ok", "rig": "character", "duration": 1, "end": "commit", "channels": [ { "node": "N", "kind": "scale", "x": { "keys": [ { "t": 0, "v": 1 }, { "t": 1, "v": 2 } ] } } ] }""")]
    [InlineData("""{ "name": "ok", "rig": "character", "duration": 0, "end": "commit", "channels": [] }""")]
    [InlineData("""{ "name": "NO", "rig": "character", "duration": -1, "end": "commit", "channels": [] }""")]
    [InlineData("""{ "name": "ok", "rig": "character", "duration": 1, "end": "commit", "channels": [ { "node": "CharSlot_Track_Focus", "kind": "position", "x": { "keys": [ { "t": 0, "v": 1 }, { "t": 1, "v": 2 } ] }, "y": { "keys": [ { "t": 0, "v": 1 } ] } } ] }""")]
    [InlineData("""{ "name": "ok", "rig": "character", "duration": 1, "end": "commit", "channels": [ { "node": "N", "kind": "position", "x": { "keys": [ { "t": 0, "v": 1 }, { "t": 1, "v": 2 } ] } }, { "node": "N", "kind": "position", "y": { "keys": [ { "t": 0, "v": 1 }, { "t": 0.5, "v": 1 }, { "t": 0.5, "v": 2 }, { "t": 1, "v": 2 } ] } } ] }""")]
    [InlineData("""{ "name": "ok", "rig": "character", "duration": 1, "end": "commit", "channels": [ { "node": "N", "kind": "euler" } ] }""")]
    [InlineData(Broken)]
    public void 코어의_판정과_어긋나지_않는다(string motion)
    {
        MotionClip clip = MotionAssetCodec.ToClip(Read(motion).Motions[0], skipEmojiOnly: true);
        List<string> all = MotionValidator.ShapeProblems(clip).ToList();

        if (MotionRules.TryValidate(clip, null, out string first))
        {
            Assert.Empty(all);
        }
        else
        {
            Assert.Equal(first, all[0]);
            Assert.Equal(all.Count, all.Distinct().Count());
        }
    }

    [Fact]
    public void 모르는_키는_자리와_함께_알린다()
    {
        const string json = """
            { "schema": "motion-authoring/2", // 주석은 읽어 준다
              "motions": [ { "name": "m", "rig": "character", "duration": 0.5, "end": "restore", "ednn": "commit",
                "channels": [ { "node": "N", "kind": "position",
                  "y": { "keyz": [], "keys": [ { "t": 0, "v": 0, "tangent": "flat", "tangnt": "x" }, { "T": 0.5, "v": 0 } ] } } ] } ],
              "emojis": [ { "name": "e", "rigs": [ { "name": "a", "images": [ "x.png" ], "plaseX": 5, "placex": 5 } ],
                "timeline": { "duration": 1, "channels": [] }, "spans": [ { "name": "hold", "start": 0, "end": 3, "loop": true } ], "hold": "hold" } ],
              "scenes": [ { "name": "scene", "objects": [ { "id": "s1", "rig": "character", "plase": "left" } ] } ], }
            """;

        Assert.Equal(
            new[] { "motions[0].ednn", "motions[0].channels[0].y.keyz", "motions[0].channels[0].y.keys[0].tangnt", "emojis[0].rigs[0].plaseX", "emojis[0].spans[0].loop", "scenes[0].objects[0].plase" },
            MotionDocumentJson.UnknownKeys(json));

        // 대소문자는 가리지 않는다(읽기가 그렇다). 툴이 쓴 문서에는 모르는 키가 없다.
        Assert.Empty(MotionDocumentJson.UnknownKeys(MotionDocumentJson.Write(MotionDocumentJson.Read(json))));
        Assert.Empty(MotionDocumentJson.UnknownKeys("{ broken"));
    }
}
