using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary><c>mt</c> 명령줄 — 창 없이 검증·내보내기·포즈·굽기. 결과는 stdout의 JSON, 종료 코드 0·1·2.</summary>
public class MtCliTests
{
    private static (int Code, JsonNode? Json, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = MtCli.Run(args, stdout, stderr);
        string text = stdout.ToString();
        JsonNode? json = null;

        try
        {
            json = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            // 쓰는 법 안내처럼 JSON이 아닌 출력
        }

        return (code, json, text, stderr.ToString());
    }

    /// <summary>nod·bow + 두 캐릭터 + 클립(s1에 bow@0 · lean 없이 nod@0.5는 s2). 임시 폴더에 저장한다.</summary>
    private static string WriteDocument(Action<MotionEditor>? more = null)
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.AddSceneObject(null, "s2", "character");
        editor.AddClip("greet");
        editor.AddClipRole("greet", "a", "character", "s1");
        editor.AddClipRole("greet", "b", "character", "s2");
        editor.AddClipTrack("greet", "a");
        editor.AddClipTrack("greet", "b");
        editor.AddClipBlock("greet", 0, "bow", 0f);
        editor.AddClipBlock("greet", 1, "nod", 0.5f);
        more?.Invoke(editor);

        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sample.motion-authoring.json");
        File.WriteAllText(path, MotionDocumentJson.Write(editor.Document));
        return path;
    }

    [Fact]
    public void 쓰는_법이_틀리면_2이고_안내가_나온다()
    {
        Assert.Equal(2, Run().Code);
        Assert.Contains("mt validate", Run().Out, StringComparison.Ordinal);
        Assert.Equal(0, Run("--help").Code);
        (int code, _, _, string err) = Run("frobnicate", "x");
        Assert.Equal(2, code);
        Assert.Contains("모르는 명령", err, StringComparison.Ordinal);
        Assert.Equal(2, Run("validate").Code);                                  // 문서가 없다
        Assert.Equal(2, Run("pose", WriteDocument(), "--time", "0.1").Code);    // --motion도 --clip도 없다
        Assert.Equal(2, Run("pose", WriteDocument(), "--motion", "nod", "--time", "soon").Code);
        Assert.Equal(2, Run("pose", WriteDocument(), "--motion").Code);         // 값이 없다

        // 없는 파일·없는 이름은 1 — JSON으로도 남는다.
        (code, JsonNode? json, _, _) = Run("info", "/no/such/file.json");
        Assert.Equal(1, code);
        Assert.False(json!["ok"]!.GetValue<bool>());
        Assert.Equal(1, Run("pose", WriteDocument(), "--motion", "nope", "--time", "0").Code);
    }

    [Fact]
    public void info는_문서의_목록을_낸다()
    {
        (int code, JsonNode? json, _, _) = Run("info", WriteDocument());
        Assert.Equal(0, code);
        Assert.Equal(new[] { "nod", "bow" }, json!["motions"]!.AsArray().Select(m => m!["name"]!.GetValue<string>()));
        // 채널의 종류는 파일의 말로(euler) — 화면의 말(rotate)로 내면 그대로 파일에 적게 된다.
        Assert.DoesNotContain("rotate", json["motions"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("greet", json["clips"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(1.1, json["clips"]![0]!["duration"]!.GetValue<double>(), 3);
        Assert.Equal(new[] { "s1", "s2" }, json["scenes"]![0]!["objects"]!.AsArray().Select(o => o!["id"]!.GetValue<string>()));
    }

    [Fact]
    public void validate는_막는_문제가_있으면_1이다()
    {
        (int code, JsonNode? json, _, _) = Run("validate", WriteDocument());
        Assert.Equal(0, code);
        Assert.True(json!["ok"]!.GetValue<bool>());
        Assert.Empty(json["problems"]!.AsArray());

        // 모션의 길이를 줄이면 키가 길이 밖 — 모션의 문제이자 그 모션을 쓰는 클립의 문제다.
        (code, json, _, _) = Run("validate", WriteDocument(e => e.SetDuration("nod", 0.3f)));
        Assert.Equal(1, code);
        Assert.False(json!["ok"]!.GetValue<bool>());
        Assert.Contains(json["problems"]!.AsArray(), p => p!["kind"]!.GetValue<string>() == "motion" && p["name"]!.GetValue<string>() == "nod" && p["blocking"]!.GetValue<bool>());
        Assert.Contains(json["problems"]!.AsArray(), p => p!["kind"]!.GetValue<string>() == "clip" && p["name"]!.GetValue<string>() == "greet");

        // 알림뿐이면 0이다(빈 클립).
        (code, json, _, _) = Run("validate", WriteDocument(e => e.AddClip("empty")));
        Assert.Equal(0, code);
        Assert.Single(json!["problems"]!.AsArray());
    }

    [Fact]
    public void validate는_문서를_지을_때와_달라진_스키마를_알린다()
    {
        // 지을 때는 있던 노드가 지금의 덤프(내장본)에는 없다 — 알림이라 종료 코드는 그대로 0이다.
        SchemaPrint builtOn = Mt.Authoring.Schema.SchemaDiff.PrintOf(Mt.Authoring.Rig.BuiltinTuning.RigSchemas);
        builtOn.Rigs.Single(r => r.Kind == "character").Nodes.Add("Old_Node < __root");

        (int code, JsonNode? json, _, _) = Run("validate", WriteDocument(e => e.SetBuiltOn(builtOn)));

        Assert.Equal(0, code);
        JsonNode schema = Assert.Single(json!["problems"]!.AsArray(), p => p!["kind"]!.GetValue<string>() == "schema")!;
        Assert.False(schema["blocking"]!.GetValue<bool>());
        Assert.Contains("사라진 노드 [Old_Node]", schema["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void export는_앱과_같은_바이트를_쓰고_검증을_못_지나면_안_쓴다()
    {
        string document = WriteDocument();
        string target = Path.Combine(Path.GetDirectoryName(document)!, "motions.json");
        (int code, JsonNode? json, _, _) = Run("export", document, target);
        Assert.Equal(0, code);
        Assert.Equal("motions/2", json!["schema"]!.GetValue<string>());
        Assert.True(MotionsJson.TryRead(File.ReadAllText(target), out List<MotionClip> clips, out _));
        Assert.Equal(2, clips.Count);

        // 견본과 바이트까지 같다.
        string plain = Path.Combine(Path.GetDirectoryName(document)!, "plain.motion-authoring.json");
        File.WriteAllText(plain, MotionDocumentJson.Write(Fixtures.NodBowDocument()));
        string plainOut = Path.Combine(Path.GetDirectoryName(document)!, "plain.json");
        Assert.Equal(0, Run("export", plain, plainOut).Code);
        Assert.Equal(Fixtures.BytesOf("motions.nod-bow.json"), File.ReadAllBytes(plainOut));

        // 막는 문제가 있으면 파일을 안 쓴다.
        string broken = WriteDocument(e => e.SetDuration("nod", 0.3f));
        string brokenOut = Path.Combine(Path.GetDirectoryName(broken)!, "motions.json");
        Assert.Equal(1, Run("export", broken, brokenOut).Code);
        Assert.False(File.Exists(brokenOut));
    }

    [Fact]
    public void pose는_그_시각의_값을_낸다()
    {
        string document = WriteDocument();

        // 모션: nod의 0.3초 — 가장 깊은 자리(-18).
        (int code, JsonNode? json, _, _) = Run("pose", document, "--motion", "nod", "--time", "0.3");
        Assert.Equal(0, code);
        Assert.Equal("s1", json!["object"]!.GetValue<string>());
        Assert.Equal(-18.0, json["nodes"]!["CharacterPortrait_Track_Move_Y"]!["position"]![1]!.GetValue<double>(), 3);

        // 재생 기준을 바꿔 읽는다: bow는 절대값 모션 — 끝에서 z = 8.
        (_, json, _, _) = Run("pose", document, "--motion", "bow", "--time", "9", "--mode", "relative", "--object", "s2");
        Assert.Equal("s2", json!["object"]!.GetValue<string>());
        Assert.Equal(8.0, json["nodes"]!["CharacterPortrait_SwayPivot"]!["euler"]![2]!.GetValue<double>(), 3);
        Assert.Equal(1, Run("pose", document, "--motion", "bow", "--time", "0", "--object", "nobody").Code);

        // 클립: 0.8초 — s1의 bow는 끝나 8이 남았고, s2는 nod의 0.3초다.
        (code, json, _, _) = Run("pose", document, "--clip", "greet", "--time", "0.8");
        Assert.Equal(0, code);
        Assert.Equal(8.0, json!["objects"]!["s1"]!["CharacterPortrait_SwayPivot"]!["euler"]![2]!.GetValue<double>(), 3);
        Assert.Equal(-18.0, json["objects"]!["s2"]!["CharacterPortrait_Track_Move_Y"]!["position"]![1]!.GetValue<double>(), 3);
    }

    [Fact]
    public void bake는_클립의_한_캐릭터를_모션_하나로_구워_문서에_더한다()
    {
        string document = WriteDocument();
        string target = Path.Combine(Path.GetDirectoryName(document)!, "baked.motion-authoring.json");
        (int code, JsonNode? json, _, _) = Run("bake", document, "--clip", "greet", "--object", "s2", "--name", "greet_s2", "--out", target);
        Assert.Equal(0, code);
        Assert.Equal("greet_s2", json!["motion"]!.GetValue<string>());
        Assert.Equal("restore", json["end"]!.GetValue<string>());

        // 원본은 그대로, --out에 구운 모션이 든 문서가 선다 — 다시 읽어 pose로 확인한다.
        Assert.Null(MotionDocumentJson.Read(File.ReadAllText(document)).Find("greet_s2"));
        Assert.NotNull(MotionDocumentJson.Read(File.ReadAllText(target)).Find("greet_s2"));
        (_, json, _, _) = Run("pose", target, "--motion", "greet_s2", "--time", "0.8", "--object", "s2");
        Assert.Equal(-18.0, json!["nodes"]!["CharacterPortrait_Track_Move_Y"]!["position"]![1]!.GetValue<double>(), 2);
        Assert.Equal(0, Run("validate", target).Code);

        // 이름이 겹치거나 구울 것이 없으면 1 — 문서는 안 바뀐다.
        string before = File.ReadAllText(target);
        Assert.Equal(1, Run("bake", target, "--clip", "greet", "--object", "s2", "--name", "nod").Code);
        Assert.Equal(2, Run("bake", target, "--clip", "greet", "--object", "s2").Code);
        Assert.Equal(before, File.ReadAllText(target));
    }

    [Fact]
    public void 손으로_쓴_문서의_탄젠트는_종류대로_세워_읽는다()
    {
        // AI가 쓰는 꼴: 키의 값과 탄젠트 종류만 — 탄젠트 값은 없다.
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "hand.motion-authoring.json");
        File.WriteAllText(path, """
            { "schema": "motion-authoring/2", "motions": [ { "name": "slide", "rig": "character", "duration": 1, "end": "commit",
              "channels": [ { "node": "CharSlot_Track_X", "kind": "position",
                "x": { "keys": [ { "t": 0, "v": 0, "tangent": "linear" }, { "t": 1, "v": 100, "tangent": "linear" } ] } } ] } ] }
            """);

        Assert.Equal(0, Run("validate", path).Code);
        (int code, JsonNode? json, _, _) = Run("pose", path, "--motion", "slide", "--time", "0.25");
        Assert.Equal(0, code);
        Assert.Equal(25.0, json!["nodes"]!["CharSlot_Track_X"]!["position"]![0]!.GetValue<double>(), 2); // linear — 탄젠트 0이었다면 15.6쯤이 나온다

        // 내보낸 파일에도 그 탄젠트 값이 실린다.
        string target = Path.Combine(directory, "motions.json");
        Assert.Equal(0, Run("export", path, target).Code);
        Assert.True(MotionsJson.TryRead(File.ReadAllText(target), out List<MotionClip> clips, out _));
        Assert.Equal(100f, clips[0].Channels[0].X[0].OutTangent, 3);
    }
}
