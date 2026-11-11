using System.Text;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시(PLAN-emoji.md E7) — 런타임의 견본 셋을 툴 문서로 다시 지었다. 툴이 낸 <c>emojis.json</c>을 코어 로더로 읽으면
/// 런타임의 파일(<c>Fixtures/runtime-emojis.json</c>, 런타임 <c>007d4c16</c>의 <c>Assets/Dialogue/emojis.json</c>)과 프레임마다 같은 값이 나온다.
/// <c>examples/emoji</c>의 파일은 빌더(<see cref="EmojiExamples"/>)가 낸 바이트와 같다 — <c>MT_WRITE_EXAMPLES=1</c>이면 다시 쓴다.
/// </summary>
public sealed class EmojiExampleTests
{
    private const float Tolerance = 1e-4f;

    private static bool Rewrite => Environment.GetEnvironmentVariable("MT_WRITE_EXAMPLES") == "1";

    private static string Export(MotionDocument document)
    {
        Assert.True(EmojiExport.TryExport(document, null, out string json, out IReadOnlyList<EmojiProblem> problems), string.Join("\n", problems));
        return json;
    }

    [Fact]
    public void 예시는_막는_문제_없이_런타임의_견본_셋을_담는다()
    {
        MotionDocument document = EmojiExamples.Build().Document;

        Assert.Equal(new[] { "star", "question", "twinkle" }, document.Emojis.Select(e => e.Name));
        Assert.DoesNotContain(EmojiValidator.Validate(document, (Rig.EmojiRigFrame?)null), p => p.Blocking);
        Assert.True(document.FindEmoji("star")!.Stays);
        Assert.False(document.FindEmoji("question")!.Stays);
    }

    [Fact]
    public void 툴이_낸_파일은_런타임의_견본과_프레임마다_같다()
    {
        Dictionary<string, EmojiDefinition> runtime = EmojiDefinitionLoader.Build(EmojiExport.Read(Fixtures.TextOf("runtime-emojis.json")));
        Dictionary<string, EmojiDefinition> tool = EmojiDefinitionLoader.Build(EmojiExport.Read(Export(EmojiExamples.Build().Document)));

        Assert.Equal(runtime.Keys.OrderBy(k => k), tool.Keys.OrderBy(k => k));

        foreach ((string name, EmojiDefinition expected) in runtime)
        {
            EmojiDefinition actual = tool[name];
            Assert.Equal(expected.Offset.X, actual.Offset.X, Tolerance);
            Assert.Equal(expected.Offset.Y, actual.Offset.Y, Tolerance);
            Assert.Equal(expected.Fps, actual.Fps);
            Assert.Equal(expected.Stays, actual.Stays);

            Assert.Equal(expected.Rigs.Select(r => (r.Name, r.SpriteKey)), actual.Rigs.Select(r => (r.Name, r.SpriteKey)));

            for (int i = 0; i < expected.Rigs.Count; i++)
            {
                Assert.Equal(expected.Rigs[i].Place.X, actual.Rigs[i].Place.X, Tolerance);
                Assert.Equal(expected.Rigs[i].Place.Y, actual.Rigs[i].Place.Y, Tolerance);
                Assert.Equal(expected.Rigs[i].Scale, actual.Rigs[i].Scale, Tolerance);
                Assert.Equal(expected.Rigs[i].Rotation, actual.Rigs[i].Rotation, Tolerance);
            }

            Assert.Equal(expected.Clips.Select(c => (c.Name, c.Length)), actual.Clips.Select(c => (c.Name, c.Length)));
            Assert.Equal(expected.Main?.Name, actual.Main?.Name);
            Assert.Equal(expected.In?.Name, actual.In?.Name);
            Assert.Equal(expected.Hold?.Name, actual.Hold?.Name);
            Assert.Equal(expected.Out?.Name, actual.Out?.Name);

            string[] targets = expected.Rigs.Select(r => r.Name).Prepend("").ToArray();

            foreach (EmojiClip clip in expected.Clips)
            {
                EmojiClip other = actual.Clips.Single(c => c.Name == clip.Name);

                // 프레임 사이(¼ 프레임)도 잰다 — 기울기까지 같아야 곡선이 같다.
                for (float frame = 0f; frame <= clip.Length - 1 + 1e-6f; frame += 0.25f)
                {
                    foreach (string rig in targets)
                    {
                        EmojiNodePose want = EmojiClipMath.SampleNode(clip, rig, frame);
                        EmojiNodePose got = EmojiClipMath.SampleNode(other, rig, frame);
                        string where = $"{name}/{clip.Name} '{rig}' {frame}fr";

                        Assert.True(Near(want.Position.X, got.Position.X) && Near(want.Position.Y, got.Position.Y), $"{where} 자리 {want.Position} ≠ {got.Position}");
                        Assert.True(Near(want.Scale.X, got.Scale.X) && Near(want.Scale.Y, got.Scale.Y), $"{where} 배율 {want.Scale} ≠ {got.Scale}");
                        Assert.True(Near(want.Rotation, got.Rotation), $"{where} 회전 {want.Rotation} ≠ {got.Rotation}");
                        Assert.True(Near(want.Alpha, got.Alpha), $"{where} 불투명도 {want.Alpha} ≠ {got.Alpha}");
                        Assert.Equal(want.SpriteKey, got.SpriteKey);
                    }
                }
            }
        }
    }

    [Fact]
    public void 예시_파일은_빌더가_낸_것과_같다()
    {
        MotionDocument document = EmojiExamples.Build().Document;
        string directory = Path.Combine(RepoRoot(), "examples", "emoji");
        var files = new (string Name, string Text)[]
        {
            (EmojiExamples.DocumentFile, MotionDocumentJson.Write(document)),
            (EmojiExamples.ExportFile, Export(document)),
        };

        foreach ((string file, string text) in files)
        {
            string path = Path.Combine(directory, file);

            if (Rewrite)
            {
                File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
            }

            Assert.True(File.Exists(path), $"{path}가 없다 — MT_WRITE_EXAMPLES=1로 쓴다");
            Assert.Equal(text, File.ReadAllText(path));
        }

        // 저작본을 다시 읽어도 같은 파일이 나온다(그림은 문서 기준 경로 — art/ 밑에 있다).
        MotionDocument reread = MotionDocumentJson.Read(File.ReadAllText(Path.Combine(directory, EmojiExamples.DocumentFile)));
        Assert.Equal(Export(document), Export(reread));

        foreach (string image in document.Emojis.SelectMany(e => e.Rigs).SelectMany(r => r.Images).Distinct())
        {
            Assert.True(File.Exists(Path.Combine(directory, image)), $"그림 {image}가 없다");
        }
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= Tolerance;

    private static string RepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "KedMotionTool.sln")))
            {
                return d.FullName;
            }
        }

        throw new DirectoryNotFoundException("KedMotionTool.sln을 찾지 못했다.");
    }
}
