using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시 문서(<c>examples/emoji</c>)가 살아 있는지 — 문제 없이 서고, 저장된 파일이 지은 것과 같고, 그림이 다 있다.
/// 예시를 고쳤으면 <c>MT_WRITE_EXAMPLES=1 dotnet test --filter EmojiExampleTests</c>로 파일을 다시 쓴다.
/// </summary>
public class EmojiExampleTests
{
    private static readonly RigSchemaFile Dump = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    private static string ExampleDirectory()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);

        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KedMotionTool.sln")))
        {
            at = at.Parent;
        }

        return Path.Combine(at?.FullName ?? throw new InvalidOperationException("저장소 뿌리를 못 찾았다"), "examples", "emoji");
    }

    [Fact]
    public void 예시_문서는_문제_없이_서고_저장된_파일과_같다()
    {
        MotionEditor editor = EmojiExamples.Build();
        MotionDocument document = editor.Document;
        RigSchemaFile rig = RigAssets.Compose(Dump, document);

        Assert.True(!MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds).Any(), string.Join("\n", MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds).Select(p => $"{p.MotionName}: {p.Message}")));
        Assert.Empty(EmojiValidator.Validate(document, rig.NodeIds).Select(p => $"{p.EmojiName}: {p.Message}"));
        Assert.Empty(ClipValidator.Validate(document, rig.NodeIds).Select(p => p.Message));
        Assert.Equal(24, document.Emojis.Count);

        string directory = ExampleDirectory();
        string path = Path.Combine(directory, "emoji-examples.motion-authoring.json");
        string json = MotionDocumentJson.Write(document);

        if (Environment.GetEnvironmentVariable("MT_WRITE_EXAMPLES") == "1")
        {
            File.WriteAllText(path, json);
        }

        Assert.Equal(json, File.ReadAllText(path));

        // 스킨이 가리키는 그림이 다 있다.
        Assert.All(document.Skins.SelectMany(s => s.Parts.Values).Distinct(), image => Assert.True(File.Exists(Path.Combine(directory, image)), image));
    }

    [Fact]
    public void 예시_클립에서_이벤트가_이모지를_띄우고_끈다()
    {
        MotionEditor editor = EmojiExamples.Build();
        MotionDocument document = editor.Document;
        ClipAsset clip = document.FindClip("emoji_demo")!;
        string? Playing(double t)
            => EmojiEvents.Resolve(document, document.ActiveSceneAsset!, ClipPreview.EventsBetween(document, clip, double.NegativeInfinity, t), t).SingleOrDefault().Emoji?.Name;

        Assert.Null(Playing(0.2));
        Assert.Equal("빨간경고막대기", Playing(0.6));
        Assert.Null(Playing(1.9));              // 잠깐 뜨고 사라졌다
        Assert.Equal("행복한벚꽃", Playing(3.5)); // 머문다
        Assert.Equal("행복한벚꽃", Playing(4.6)); // 끄라는 말이 왔다 — 퇴장 중
        Assert.Null(Playing(5.0));
        Assert.Equal("말줄임점풍선_우", Playing(8.0));
        Assert.Null(Playing(9.5));
    }
}
