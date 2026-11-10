using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 이벤트 키 명령(PLAN-parts.md §2.5) — 각각 되돌리기 한 칸. 시각 오름차순은 편집기가 지킨다. 이름 규칙·길이 밖은 검증(코어 MotionRules)이 알린다.
public sealed partial class MotionEditor
{
    /// <summary>이벤트 키를 놓고 번호를 돌려준다. 시각은 0 아래로 못 간다.</summary>
    public int AddMotionEvent(string motionName, float time, string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        MotionAsset motion = Require(motionName);
        var added = new MotionEventAsset { T = ClampAt(time), Name = name.Trim(), Value = string.IsNullOrEmpty(value) ? null : value };
        Mutate(MotionChangeKind.Metadata, () => InsertEvent(motion, added));
        return motion.Events.IndexOf(added);
    }

    /// <summary>이벤트 키의 시각·이름·값을 바꾼다. 시각이 바뀌면 자리도 옮겨지고 새 번호를 돌려준다.</summary>
    public int UpdateMotionEvent(string motionName, int index, float time, string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        MotionAsset motion = Require(motionName);
        MotionEventAsset old = RequireEvent(motion, index);
        var next = new MotionEventAsset { T = ClampAt(time), Name = name.Trim(), Value = string.IsNullOrEmpty(value) ? null : value };

        Mutate(MotionChangeKind.Metadata, () =>
        {
            motion.Events.Remove(old);
            InsertEvent(motion, next);
        });
        return motion.Events.IndexOf(next);
    }

    public void RemoveMotionEvent(string motionName, int index)
    {
        MotionAsset motion = Require(motionName);
        MotionEventAsset target = RequireEvent(motion, index);
        Mutate(MotionChangeKind.Metadata, () => motion.Events.Remove(target));
    }

    private static void InsertEvent(MotionAsset motion, MotionEventAsset added)
    {
        int at = motion.Events.Count;

        for (int i = 0; i < motion.Events.Count; i++)
        {
            if (motion.Events[i].T > added.T)
            {
                at = i;
                break;
            }
        }

        motion.Events.Insert(at, added);
    }

    private static MotionEventAsset RequireEvent(MotionAsset motion, int index)
    {
        if (index < 0 || index >= motion.Events.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"모션 '{motion.Name}'의 이벤트가 {motion.Events.Count}개다.");
        }

        return motion.Events[index];
    }
}
