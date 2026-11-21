using System.Collections.Generic;
using Ked.Presentation.Core;
using NUnit.Framework;

// 모션 검증 규칙 — 내보내기·로더·저작 도구가 같은 함수를 쓴다. 어긋난 모션은 로그 + 무시(조용히 빠뜨리지 않는다).
// 규칙은 모양만 본다 — 값의 뜻(relative/absolute)은 커맨드가 정하므로(motions/2) 중립값 규칙이 없다.
public class MotionRulesTests
{
    private static CurveKey[] Keys(params (float t, float v)[] keys)
    {
        CurveKey[] result = new CurveKey[keys.Length];

        for (int i = 0; i < keys.Length; i++)
            result[i] = new CurveKey(keys[i].t, keys[i].v, 0f, 0f);

        return result;
    }

    private static MotionChannel Channel(string node = "CharacterPortrait_Track_Move", MotionChannelKind kind = MotionChannelKind.Position,
        CurveKey[] x = null, CurveKey[] y = null, CurveKey[] z = null)
        => new() { Node = node, Kind = kind, X = x, Y = y, Z = z };

    private static MotionClip Clip(float duration = 0.6f, params MotionChannel[] channels)
        => new() { Name = "nod", Rig = "character", Duration = duration, End = MotionEnd.Commit, Channels = channels };

    private static MotionClip Restore(float duration = 0.6f, params MotionChannel[] channels)
        => new() { Name = "nod", Rig = "character", Duration = duration, End = MotionEnd.Restore, Channels = channels };

    private static MotionChannel Nod => Channel(y: Keys((0f, 0f), (0.3f, -18f), (0.6f, 0f)));

    private static readonly HashSet<string> Rig = new() { "CharacterPortrait_Track_Move", "CharacterPortrait_SwayPivot", "CharSlot_Track", "CharFocus_Track", "CharFocus_Rotation", "CharFocus_Scale" };

    [Test]
    public void 유효한_클립은_통과한다_스키마가_있어도_없어도()
    {
        MotionClip clip = Clip(0.6f, Nod, Channel("CharacterPortrait_SwayPivot", MotionChannelKind.Euler, z: Keys((0f, 3f), (0.6f, 0f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, null, out string reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(clip, Rig, out reason), reason);
    }

    [TestCase("Nod", "이름")]
    [TestCase("nod-1", "이름")]
    [TestCase("", "이름")]
    public void 이름_규칙(string name, string expect)
    {
        MotionClip clip = Clip(0.6f, Nod);
        clip.Name = name;

        Assert.IsFalse(MotionRules.TryValidate(clip, null, out string reason));
        StringAssert.Contains(expect, reason);
    }

    [Test]
    public void 길이는_0보다_커야_하고_채널은_없어도_된다()
    {
        Assert.IsFalse(MotionRules.TryValidate(Clip(0f, Nod), null, out string reason));
        StringAssert.Contains("길이", reason);

        // 채널이 없는 모션은 아무것도 하지 않는 모션이다 — 저작 도구가 첫 키에서 채널을 만드므로 그 전에도 유효해야 한다.
        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f), null, out reason), reason);
        MotionClip nullChannels = Clip(0.6f);
        nullChannels.Channels = null;
        Assert.IsTrue(MotionRules.TryValidate(nullChannels, null, out reason), reason);
    }

    [Test]
    public void 포커스_계층은_키잉할_수_없다()
    {
        // 포커스 계층(대화할 때만 서는 자리)은 focus_* 커맨드가 놓고 focus_reset이 되돌린다 — 클립이 키를 주지 않는다.
        foreach (string node in new[] { "CharFocus_Track", "CharFocus_Rotation", "CharFocus_Scale" })
        {
            MotionClip clip = Clip(0.6f, Channel(node, x: Keys((0f, 0f), (0.6f, 10f))));

            Assert.IsFalse(MotionRules.TryValidate(clip, Rig, out string reason), node);
            StringAssert.Contains("포커스 계층", reason);
        }
    }

    [Test]
    public void 스키마에_없는_노드는_거부한다_스키마를_줬을_때만()
    {
        MotionClip clip = Clip(0.6f, Channel("NoSuchNode", x: Keys((0f, 0f), (0.6f, 10f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, null, out _));
        Assert.IsFalse(MotionRules.TryValidate(clip, Rig, out string reason));
        StringAssert.Contains("스키마", reason);
    }

    [Test]
    public void Position_Scale에는_Z가_없고_성분은_하나는_있어야_한다()
    {
        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(z: Keys((0f, 0f), (0.6f, 1f)))), null, out string reason));
        StringAssert.Contains("Z", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel()), null, out reason));
        StringAssert.Contains("성분", reason);
    }

    [Test]
    public void 성분_키_규칙_둘_이상_0에서_시작_길이에서_끝_오름차순()
    {
        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f)))), null, out string reason));
        StringAssert.Contains("2개", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0.1f, 0f), (0.6f, 0f)))), null, out reason));
        StringAssert.Contains("첫 키", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f), (0.5f, 0f)))), null, out reason));
        StringAssert.Contains("길이", reason);

        Assert.IsFalse(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f), (0.4f, 1f), (0.4f, 2f), (0.6f, 0f)))), null, out reason));
        StringAssert.Contains("오름차순", reason);
    }

    [Test]
    public void 첫_키와_마지막_키의_값은_규칙이_보지_않는다_뜻은_커맨드가_정한다()
    {
        // 0프레임 140 → 길이에서 360: absolute면 140→360, relative면 지금 값+140 → +360. 둘 다 유효한 모션이다.
        CurveKey[] from140 = Keys((0f, 140f), (0.6f, 360f));

        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f, Channel(y: from140)), null, out string reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Channel(y: from140)), null, out reason), reason);

        // 배율도 같다 — 0에서 시작하는 배율도 모양으로는 유효하다(absolute로 쓰면 뜻이 있다).
        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f, Channel(kind: MotionChannelKind.Scale, x: Keys((0f, 0f), (0.6f, 1f)))), null, out reason), reason);
    }

    [Test]
    public void restore와_commit은_모양_규칙이_같다()
    {
        // 돌아오는 흔들림 — 통과.
        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Nod), null, out string reason), reason);

        // 마지막 키가 남는 키라도 restore로 선언할 수 있다 — 재생이 끝에서 시작값을 되쓴다(튐은 작가의 선택, 툴이 경고).
        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Channel(y: Keys((0f, 0f), (0.6f, -18f)))), null, out reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(Clip(0.6f, Channel(y: Keys((0f, 0f), (0.6f, -18f)))), null, out reason), reason);

        Assert.IsTrue(MotionRules.TryValidate(Restore(0.6f, Channel(kind: MotionChannelKind.Scale, x: Keys((0f, 1f), (0.3f, 1.2f), (0.6f, 1f)))), null, out reason), reason);
    }

    [Test]
    public void end는_restore_또는_commit이어야_한다()
    {
        MotionClip clip = Clip(0.6f, Nod);
        clip.End = (MotionEnd)7;

        Assert.IsFalse(MotionRules.TryValidate(clip, null, out string reason));
        StringAssert.Contains("end", reason);
    }

    [Test]
    public void 같은_노드_같은_종류의_채널은_둘일_수_없다()
    {
        MotionClip clip = Clip(0.6f, Nod, Channel(x: Keys((0f, 0f), (0.6f, 4f))));

        Assert.IsFalse(MotionRules.TryValidate(clip, null, out string reason));
        StringAssert.Contains("둘", reason);

        // 종류가 다르면 같은 노드라도 된다 — 위치와 회전을 한 노드에 같이 건다.
        clip.Channels[1] = Channel(kind: MotionChannelKind.Euler, z: Keys((0f, 0f), (0.6f, 4f)));
        Assert.IsTrue(MotionRules.TryValidate(clip, null, out reason), reason);
    }

    [Test]
    public void 허용_오차_안의_잡음은_통과한다()
    {
        MotionClip clip = Clip(0.6f, Channel(y: Keys((0.00001f, 0.00001f), (0.59999f, 3f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, null, out string reason), reason);
    }

    // ── 카메라 리그 — 화면 흔들림 ─────────────────────────────────────

    private static MotionClip Shake(MotionEnd end, params MotionChannel[] channels)
        => new() { Name = "shake_hit", Rig = MotionRules.CameraRig, Duration = 0.6f, End = end, Channels = channels };

    private static MotionChannel ShakeX(string node)
        => Channel(node, x: Keys((0f, 0f), (0.3f, 12f), (0.6f, 0f)));

    private static readonly HashSet<string> CameraRigNodes = new()
    {
        "Camera_Root", "Camera_Shake", "Camera_Pan", "Camera_Zoom", "Camera_Roll", "Camera_Dolly", "Camera_Track",
    };

    [Test]
    public void 흔들림은_Camera_Shake의_위치_회전_배율에_키를_준다()
    {
        MotionClip clip = Shake(
            MotionEnd.Restore,
            ShakeX(MotionRules.CameraShakeNode),
            Channel(MotionRules.CameraShakeNode, MotionChannelKind.Euler, z: Keys((0f, 0f), (0.3f, 0.5f), (0.6f, 0f))));

        Assert.IsTrue(MotionRules.TryValidate(clip, CameraRigNodes, out string reason), reason);
        Assert.IsTrue(MotionRules.TryValidate(clip, null, out reason), reason);
    }

    [TestCase("Camera_Pan")]
    [TestCase("Camera_Zoom")]
    [TestCase("Camera_Roll")]
    [TestCase("Camera_Dolly")]
    [TestCase("Camera_Track")]
    [TestCase("Camera_Root")]
    public void 흔들림_노드가_아닌_카메라_노드는_키잉할_수_없다(string node)
    {
        // 샷의 축 노드는 저작값이 아니라 샷 식의 해다 — 키를 주면 샷이 다음에 쓰는 값과 싸운다.
        MotionClip clip = Shake(MotionEnd.Restore, ShakeX(node));

        Assert.IsFalse(MotionRules.TryValidate(clip, CameraRigNodes, out string reason));
        StringAssert.Contains("Camera_Shake 하나다", reason);

        // 스키마를 주지 않아도 걸린다 — 리그 이름만으로 정해지는 규칙이다.
        Assert.IsFalse(MotionRules.TryValidate(clip, null, out _));
    }

    [Test]
    public void 흔들림은_restore여야_한다()
    {
        // commit이면 흔들림 노드에 값이 남는다. 폴드는 흔들림을 접지 않으므로 화면이 정지 프레임과 어긋난다.
        MotionClip clip = Shake(MotionEnd.Commit, ShakeX(MotionRules.CameraShakeNode));

        Assert.IsFalse(MotionRules.TryValidate(clip, CameraRigNodes, out string reason));
        StringAssert.Contains("restore여야 한다", reason);
    }

    [Test]
    public void 캐릭터_리그의_모션은_카메라의_규칙을_받지_않는다()
    {
        // 같은 이름의 노드를 가리켜도 리그가 다르면 다른 규칙이다 — commit도 된다.
        MotionClip clip = Clip(0.6f, Nod);

        Assert.IsTrue(MotionRules.TryValidate(clip, Rig, out string reason), reason);
    }
}
