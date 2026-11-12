using System.Text.Json.Nodes;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>
/// 만드는 명령(mt new · add-motion · add-emoji · key · unkey · object · add-clip · block · span · picture · remove · controllers · presets) — 파일을 손으로 쓰지 않고
/// 문서를 지어, 검증을 지나고 내보내지는 데까지.
/// </summary>
public class MtCliCreateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"create-{Guid.NewGuid():N}");
    private readonly string _document;

    public MtCliCreateTests()
    {
        Directory.CreateDirectory(_directory);
        _document = Path.Combine(_directory, "a.motion-authoring.json");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static (int Code, JsonNode Json) Run(params string[] args)
    {
        var stdout = new StringWriter();
        int code = MtCli.Run(args, stdout, new StringWriter());
        return (code, JsonNode.Parse(stdout.ToString())!);
    }

    private MotionDocument Read() => MotionDocumentJson.Read(File.ReadAllText(_document));

    [Fact]
    public void 명령만으로_모션을_지어_내보낸다()
    {
        Assert.Equal(0, Run("new", _document).Code);
        Assert.Equal(1, Run("new", _document).Code); // 있는 문서는 덮어쓰지 않는다

        // 프리셋으로 하나, 빈 것에 키를 놓아 하나.
        Assert.Equal("bow", Run("add-motion", _document, "--preset", "bow").Json["name"]!.GetValue<string>());
        Assert.Equal(0, Run("add-motion", _document, "--name", "wave", "--duration", "0.5").Code);

        (int code, JsonNode keyed) = Run("key", _document, "--motion", "wave", "--controller", "body", "--axis", "x", "--keys", "0:0,0.25:30,0.5:0");
        Assert.Equal(0, code);
        Assert.Equal("CharacterPortrait_Track_Move", keyed["node"]!.GetValue<string>());
        Assert.Equal(3, keyed["keys"]!.AsArray().Count);
        Assert.False(keyed["blocking"]!.GetValue<bool>());

        // 같은 시각에 다시 놓으면 바뀐다(더해지지 않는다). 탄젠트의 기본은 flat.
        Run("key", _document, "--motion", "wave", "--controller", "body", "--axis", "x", "--time", "0.25", "--value", "40");
        ComponentCurve x = Read().Find("wave")!.Channels[0].X!;
        Assert.Equal(new[] { 0f, 40f, 0f }, x.Keys.Select(k => k.V));
        Assert.All(x.Keys, k => Assert.Equal(TangentMode.Flat, k.Tangent));

        // 노드와 종류를 직접 적어도 된다.
        Assert.Equal(0, Run("key", _document, "--motion", "wave", "--node", "CharacterPortrait_SwayPivot", "--kind", "euler", "--axis", "z", "--keys", "0:0,0.5:0").Code);

        Assert.True(Run("validate", _document).Json["ok"]!.GetValue<bool>());
        Assert.Equal(0, Run("export", _document, Path.Combine(_directory, "motions.json")).Code);

        Assert.Equal(0, Run("remove", _document, "--motion", "bow").Code);
        Assert.Equal(new[] { "wave" }, Read().Motions.Select(m => m.Name));
    }

    [Fact]
    public void 명령만으로_머무는_이모지를_지어_내보낸다()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "art"));
        File.WriteAllBytes(Path.Combine(_directory, "art", "star.png"), new byte[] { 0 });
        Run("new", _document);

        (int code, JsonNode made) = Run("add-emoji", _document, "--name", "spark", "--images", "art/star.png,art/star.png", "--life", "stay", "--in", "pop_in", "--hold", "pulse", "--out", "float_out");
        Assert.Equal(0, code);
        Assert.Equal(new[] { "star", "star_2" }, made["pictures"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Empty(made["problems"]!.AsArray());

        EmojiAsset emoji = Read().FindEmoji("spark")!;
        Assert.True(emoji.Stays);
        Assert.NotEmpty(emoji.Timeline.Channels); // 고른 움직임의 키가 들어 있다

        // 그림 하나에만, 프레임으로.
        (code, JsonNode keyed) = Run("key", _document, "--emoji", "spark", "--picture", "star_2", "--kind", "euler", "--axis", "z", "--keys", "8:0,16:360", "--tangent", "linear");
        Assert.Equal(0, code);
        Assert.Equal("star_2/EmojiRig_Image", keyed["node"]!.GetValue<string>());
        Assert.Equal(16, keyed["keys"]![1]!["frame"]!.GetValue<int>());

        // 그림을 적지 않으면 이모지 전체.
        Assert.Equal("Emoji_Offset", Run("key", _document, "--emoji", "spark", "--kind", "alpha", "--axis", "x", "--frame", "0", "--value", "1").Json["node"]!.GetValue<string>());

        Assert.True(Run("validate", _document).Json["ok"]!.GetValue<bool>());
        Assert.Equal(0, Run("emojis", _document, Path.Combine(_directory, "emojis.json")).Code);
    }

    [Fact]
    public void 화면이_거절하는_것은_여기서도_거절되고_문서는_그대로다()
    {
        Run("new", _document);
        Run("add-motion", _document, "--name", "wave");
        string before = File.ReadAllText(_document);

        Assert.Equal(1, Run("add-motion", _document, "--name", "Bad Name").Code);
        Assert.Equal(1, Run("add-motion", _document, "--name", "wave").Code);
        Assert.Equal(1, Run("add-motion", _document, "--preset", "nope").Code);
        Assert.Equal(1, Run("key", _document, "--motion", "wave", "--controller", "nope", "--axis", "x", "--time", "0", "--value", "0").Code);
        Assert.Equal(1, Run("key", _document, "--motion", "wave", "--controller", "bow", "--axis", "x", "--time", "0", "--value", "0").Code); // 발 기준 회전에는 x가 없다
        Assert.Equal(1, Run("key", _document, "--emoji", "nope", "--kind", "scale", "--axis", "x", "--frame", "0", "--value", "1").Code);
        Assert.Equal(before, File.ReadAllText(_document));
    }

    [Fact]
    public void 쓰는_법이_틀리면_종료_코드_2다()
    {
        Run("new", _document);
        Run("add-motion", _document, "--name", "wave");

        Assert.Equal(2, MtCli.Run(new[] { "key", _document, "--motion", "wave", "--controller", "body", "--axis", "x" }, new StringWriter(), new StringWriter())); // 값이 없다
        Assert.Equal(2, MtCli.Run(new[] { "key", _document, "--motion", "wave", "--controller", "body", "--axis", "x", "--keys", "0=1" }, new StringWriter(), new StringWriter()));
        Assert.Equal(2, MtCli.Run(new[] { "add-motion", _document, "--name", "b", "--end", "loop" }, new StringWriter(), new StringWriter()));
        Assert.Equal(2, MtCli.Run(new[] { "add-emoji", _document, "--name", "e", "--in", "pulse" }, new StringWriter(), new StringWriter())); // 머묾의 움직임을 등장에
        Assert.Equal(2, MtCli.Run(new[] { "remove", _document }, new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void 손잡이와_프리셋을_물어_볼_수_있다()
    {
        (int code, JsonNode controllers) = Run("controllers");
        Assert.Equal(0, code);
        JsonNode body = controllers["controllers"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == "body")!;
        Assert.Equal("CharacterPortrait_Track_Move", body["node"]!.GetValue<string>());
        Assert.Equal("position", body["kind"]!.GetValue<string>());
        Assert.Equal("relative", body["play"]!.GetValue<string>());

        (code, JsonNode presets) = Run("presets");
        Assert.Equal(0, code);
        Assert.Contains(presets["motions"]!.AsArray(), p => p!["id"]!.GetValue<string>() == "nod");
        Assert.Equal(new[] { "pop_in", "fade_in", "pulse", "shake", "float_out", "shrink_out" }, presets["emojiMoves"]!.AsArray().Select(m => m!["id"]!.GetValue<string>()));
    }

    [Fact]
    public void 명령만으로_두_캐릭터의_클립을_짓고_키_하나를_지운다()
    {
        Run("new", _document);
        Run("add-motion", _document, "--preset", "nod");
        Run("add-motion", _document, "--preset", "bow");

        // 캐릭터 — 없으면 세우고, 있으면 적은 값만 바꾼다.
        Assert.Equal("object", Run("object", _document, "--id", "s2", "--x", "300").Json["made"]!.GetValue<string>());
        JsonNode moved = Run("object", _document, "--id", "s2", "--y", "-20", "--scale", "1.1").Json;
        Assert.Equal("object-changed", moved["made"]!.GetValue<string>());
        Assert.Equal(300f, moved["x"]!.GetValue<float>());
        Assert.Equal(-20f, moved["y"]!.GetValue<float>());
        Assert.Equal(1.1f, moved["scaleY"]!.GetValue<float>());

        // 클립 — 배역과 트랙은 블록을 놓을 때 선다.
        Assert.Equal(0, Run("add-clip", _document, "--name", "greet").Code);
        JsonNode first = Run("block", _document, "--clip", "greet", "--motion", "nod", "--at", "0", "--object", "s1").Json;
        Assert.Equal(0, first["track"]!.GetValue<int>());
        JsonNode second = Run("block", _document, "--clip", "greet", "--motion", "bow", "--at", "0.4", "--object", "s2").Json;
        Assert.Equal(1, second["track"]!.GetValue<int>());
        Assert.Equal("s2", second["role"]!.GetValue<string>());
        Assert.False(second["blocking"]!.GetValue<bool>());

        ClipAsset clip = Assert.Single(Read().Clips);
        Assert.Equal(new[] { "s1", "s2" }, clip.Roles.Select(r => r.Object));
        Assert.Equal(new[] { 1, 1 }, clip.Tracks.Select(t => t.Blocks.Count));

        // 같은 캐릭터에 한 줄 더.
        Assert.Equal(2, Run("block", _document, "--clip", "greet", "--motion", "bow", "--at", "1", "--object", "s1", "--track", "new").Json["track"]!.GetValue<int>());
        Assert.Equal(0, Run("remove", _document, "--clip", "greet", "--track", "2", "--block", "0").Code);
        Assert.Equal(0, Run("remove", _document, "--clip", "greet", "--track", "2").Code);
        Assert.Equal(2, Read().Clips[0].Tracks.Count);
        Assert.Equal(0, Run("pose", _document, "--clip", "greet", "--time", "0.5").Code);

        // 키 하나 지우기 — 없는 시각이면 있는 것을 말한다.
        Run("add-motion", _document, "--name", "wave", "--duration", "0.5");
        Run("key", _document, "--motion", "wave", "--controller", "body", "--axis", "x", "--keys", "0:0,0.25:30,0.5:0");
        (int missing, JsonNode why) = Run("unkey", _document, "--motion", "wave", "--controller", "body", "--axis", "x", "--time", "0.3");
        Assert.Equal(1, missing);
        Assert.Contains("0 · 0.25 · 0.5", why["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(2, Run("unkey", _document, "--motion", "wave", "--controller", "body", "--axis", "x", "--time", "0.25").Json["left"]!.GetValue<int>());

        Assert.Equal(0, Run("remove", _document, "--object", "s2").Code);
        Assert.Null(Read().ActiveSceneAsset!.Find("s2"));
    }

    [Fact]
    public void 명령으로_이모지의_구간을_옮기고_그림의_기본_자리를_놓는다()
    {
        Run("new", _document);
        Run("add-emoji", _document, "--name", "spark", "--images", "a.png,b.png", "--life", "stay", "--in", "pop_in", "--hold", "pulse", "--out", "float_out");
        EmojiAsset before = Read().FindEmoji("spark")!;
        EmojiSpan hold = before.FindSpan(before.Hold)!;

        JsonNode moved = Run("span", _document, "--emoji", "spark", "--name", hold.Name, "--start", hold.Start.ToString(), "--end", (hold.End + 6).ToString()).Json;
        Assert.Equal("span-moved", moved["made"]!.GetValue<string>());
        Assert.Equal("hold", moved["linked"]!.GetValue<string>());
        Assert.Equal(hold.End + 6, Read().FindEmoji("spark")!.FindSpan(hold.Name)!.End);

        Assert.Equal("span", Run("span", _document, "--emoji", "spark", "--name", "extra", "--start", "0", "--end", "3").Json["made"]!.GetValue<string>());
        Assert.Equal(2, MtCli.Run(new[] { "span", _document, "--emoji", "spark", "--name", "extra", "--start", "0.5", "--end", "3" }, new StringWriter(), new StringWriter())); // 프레임은 정수

        // 적은 값만 바뀐다.
        JsonNode placed = Run("picture", _document, "--emoji", "spark", "--picture", "b", "--x", "40", "--scale", "0.5").Json;
        Assert.Equal(40f, placed["x"]!.GetValue<float>());
        Assert.Equal(0f, placed["y"]!.GetValue<float>());
        EmojiRigAsset b = Read().FindEmoji("spark")!.FindRig("b")!;
        Assert.Equal((40f, 0f, 0.5f, 0f), (b.PlaceX, b.PlaceY, b.Scale, b.Rotation));

        // 이모지의 키 하나 지우기 — 프레임으로.
        Run("key", _document, "--emoji", "spark", "--picture", "b", "--kind", "alpha", "--axis", "x", "--keys", "0:1,6:0.5,12:1");
        Assert.Equal(2, Run("unkey", _document, "--emoji", "spark", "--picture", "b", "--kind", "alpha", "--axis", "x", "--frame", "6").Json["left"]!.GetValue<int>());
    }
}
