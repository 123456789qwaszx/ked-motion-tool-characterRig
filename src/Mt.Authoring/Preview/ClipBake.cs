using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 굽기 — 클립에서 한 출연자(씬 오브젝트)에게 얹힌 블록 전부를 **모션 하나**로 만든다.
///
/// 여러 배역·여러 트랙으로 한 캐릭터에 모션을 겹쳐 합성한 것은 미리보기에서는 순서대로 얹혀 보이지만,
/// 런타임에서 한 노드에 두 모션이 겹칠 때의 동작은 정해져 있지 않다. 구운 모션은 그 합성 결과를 키로 가진 보통 모션이라
/// <c>motion</c> 커맨드 하나로 같은 움직임이 난다.
///
/// 방법: <see cref="ClipPreview.Pose"/>(보이는 것과 같은 함수)를 프레임(1/24초)마다, 그리고 얹힌 블록의 키 시각마다 불러 그 오브젝트의 노드 값을 읽고,
/// 시작 상태에 대한 변화(위치·회전은 차, 배율은 비)로 적는다 — 그래서 구운 모션은 **상대값(relative)** 모션이다.
/// 직선 위의 키는 덜어 낸다(오차 <see cref="Tolerance"/> 안). 키는 linear 탄젠트라 프레임 사이도 표본을 잇는 직선이다.
/// 끝에 남는 것이 있으면 commit, 없으면 restore.
///
/// 한계: 프레임 사이의 순간 변화(restore 블록이 끝나며 돌아가는 것)는 한 프레임짜리 경사가 된다.
/// </summary>
public static class ClipBake
{
    /// <summary>키를 덜어 낼 때의 허용 오차(px · 도 · 배율).</summary>
    public const float Tolerance = 1e-3f;

    /// <summary>
    /// 굽는다 — 되돌리기 한 칸. 새 모션을 돌려준다. 그 오브젝트에 얹힌(끄지 않은) 블록이 없거나 아무것도 안 움직이면 예외.
    /// </summary>
    /// <param name="template">클립의 씬으로 세운 트리(<see cref="ScenePreview.Build"/>).</param>
    /// <param name="isMuted">끈 트랙은 굽지 않는다 — 보이는 것을 굽는다.</param>
    public static MotionAsset Bake(
        MotionEditor editor,
        RectNodeTree template,
        string clipName,
        string objectId,
        string newName,
        Func<ClipTrack, bool>? isMuted = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(objectId);
        MotionDocument document = editor.Document;
        ClipAsset clip = document.FindClip(clipName) ?? throw new KeyNotFoundException($"클립 '{clipName}'이 없다.");
        SceneObject actor = ClipPreview.SceneOf(document, clip)?.Find(objectId)
            ?? throw new KeyNotFoundException($"클립 '{clipName}'의 씬에 오브젝트 '{objectId}'가 없다.");

        // 이 출연자의 노드를 건드리는 채널들 — 표본을 뜰 자리.
        var targets = new List<(string Node, ChannelKind Kind)>();
        var keyTimes = new List<float>(); // 얹힌 블록의 키 시각(클립 시각) — 프레임 사이의 정점을 놓치지 않게 표본에 더한다

        foreach (ClipTrack track in clip.Tracks)
        {
            if (isMuted?.Invoke(track) == true || !ReferenceEquals(ClipPreview.ObjectOf(document, clip, track), actor))
            {
                continue;
            }

            foreach (ClipBlock block in track.Blocks)
            {
                if (block.IsMuted || document.Find(block.Motion) is not { } motion)
                {
                    continue;
                }

                keyTimes.Add(block.At);
                keyTimes.Add(block.At + motion.Duration * block.Cycles);

                foreach (ChannelAsset channel in motion.Channels)
                {
                    // 불투명도 · 그림은 리그 트리에 자리가 없다 — 미리보기(MotionPreview)도 얹지 않는다.
                    if (ChannelKinds.IsEmojiOnly(channel.Kind) || !template.Contains(actor.KeyOf(channel.Node)))
                    {
                        continue;
                    }

                    if (!targets.Contains((channel.Node, channel.Kind)))
                    {
                        targets.Add((channel.Node, channel.Kind));
                    }

                    foreach (Axis axis in ChannelKinds.Axes(channel.Kind))
                    {
                        for (int cycle = 0; cycle < block.Cycles; cycle++)
                        {
                            float from = block.At + cycle * motion.Duration; // 바퀴마다 키 시각이 다시 온다
                            keyTimes.AddRange(channel.Get(axis)?.Keys.Select(k => from + k.T) ?? Enumerable.Empty<float>());
                        }
                    }
                }
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException($"오브젝트 '{objectId}'에 얹힌 블록이 없다 — 구울 것이 없다.");
        }

        float duration = ClipTiming.DurationOf(document, clip);

        if (duration <= 0f)
        {
            throw new InvalidOperationException("클립 길이가 0이다 — 구울 것이 없다.");
        }

        // 표본 시각: 프레임마다 + 얹힌 블록의 키 시각 + 길이 자리(마지막 키는 길이에 있어야 한다 — 코어 규칙).
        var all = new List<float>();
        double frame = PlaybackClock.Frame;

        for (int k = 0; k * frame < duration - 1e-6; k++)
        {
            all.Add((float)(k * frame));
        }

        all.AddRange(keyTimes.Where(t => t > 0f && t < duration));
        all.Sort();
        var times = new List<float>();

        foreach (float t in all)
        {
            // 거의 같은 시각은 하나로 — 키 둘이 한 자리에 서지 않게.
            if (times.Count == 0 || t - times[^1] > 1e-4f)
            {
                times.Add(t);
            }
        }

        if (duration - times[^1] <= 1e-4f)
        {
            times.RemoveAt(times.Count - 1);
        }

        times.Add(duration);

        // (채널, 축)마다 시작 상태에 대한 변화의 표본.
        var samples = new Dictionary<(int Target, Axis Axis), float[]>();

        for (int i = 0; i < times.Count; i++)
        {
            RectNodeTree pose = ClipPreview.Pose(template, document, clip, times[i], isMuted);

            for (int c = 0; c < targets.Count; c++)
            {
                string key = actor.KeyOf(targets[c].Node);
                Vec3 start = ValueOf(targets[c].Kind, template.GetState(key));
                Vec3 now = ValueOf(targets[c].Kind, pose.GetState(key));

                foreach (Axis axis in ChannelKinds.Axes(targets[c].Kind))
                {
                    if (!samples.TryGetValue((c, axis), out float[]? values))
                    {
                        samples[(c, axis)] = values = new float[times.Count];
                    }

                    values[i] = Delta(targets[c].Kind, Component(start, axis), Component(now, axis));
                }
            }
        }

        // 끝난 뒤에 남는 것 — 길이를 지난 포즈로 본다(restore 블록은 돌아간 뒤다).
        RectNodeTree after = ClipPreview.Pose(template, document, clip, duration + 1.0, isMuted);
        bool leaves = false;
        var kept = new List<(int Target, Axis Axis, float[] Values)>();

        for (int c = 0; c < targets.Count; c++)
        {
            float neutral = ChannelKinds.Neutral(targets[c].Kind);
            string key = actor.KeyOf(targets[c].Node);

            foreach (Axis axis in ChannelKinds.Axes(targets[c].Kind))
            {
                float[] values = samples[(c, axis)];

                // 내내 중립값인 성분은 싣지 않는다 — 건드리지 않은 것이다.
                if (values.All(v => Math.Abs(v - neutral) <= Tolerance))
                {
                    continue;
                }

                kept.Add((c, axis, values));
                float end = Delta(targets[c].Kind, Component(ValueOf(targets[c].Kind, template.GetState(key)), axis), Component(ValueOf(targets[c].Kind, after.GetState(key)), axis));
                leaves |= Math.Abs(end - neutral) > Tolerance;

                // commit이면 마지막 키가 남는 값이어야 한다 — 길이 자리의 표본을 끝난 뒤의 값으로.
                values[^1] = end;
            }
        }

        if (kept.Count == 0)
        {
            throw new InvalidOperationException($"오브젝트 '{objectId}'가 이 클립에서 움직이지 않는다 — 구울 것이 없다.");
        }

        MotionAsset? baked = null;

        editor.Batch(MotionChangeKind.Structure, () =>
        {
            baked = editor.AddMotion(newName, actor.Rig, duration, leaves ? MotionEnd.Commit : MotionEnd.Restore, MotionChannelMode.Relative);

            foreach ((int target, Axis axis, float[] values) in kept)
            {
                (string node, ChannelKind kind) = targets[target];

                if (baked.FindChannel(node, kind) is null)
                {
                    editor.AddChannel(newName, node, kind);
                }

                editor.SetKeys(newName, node, kind, axis, Reduce(times, values).Select(i => new KeyAsset { T = times[i], V = values[i], Tangent = TangentMode.Linear }));
            }
        });

        return editor.Document.Find(newName)!;
    }

    /// <summary>직선 위의 표본을 덜어 낸 번호들(Ramer–Douglas–Peucker, 값 오차 <see cref="Tolerance"/> 안). 처음과 끝은 남는다.</summary>
    public static List<int> Reduce(IReadOnlyList<float> times, IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);
        var keep = new SortedSet<int> { 0, times.Count - 1 };
        var stack = new Stack<(int From, int To)>();
        stack.Push((0, times.Count - 1));

        while (stack.Count > 0)
        {
            (int from, int to) = stack.Pop();
            int worst = -1;
            float worstError = Tolerance;

            for (int i = from + 1; i < to; i++)
            {
                float u = (times[i] - times[from]) / (times[to] - times[from]);
                float error = Math.Abs(values[i] - (values[from] + (values[to] - values[from]) * u));

                if (error > worstError)
                {
                    worstError = error;
                    worst = i;
                }
            }

            if (worst >= 0)
            {
                keep.Add(worst);
                stack.Push((from, worst));
                stack.Push((worst, to));
            }
        }

        return keep.ToList();
    }

    private static Vec3 ValueOf(ChannelKind kind, in RectNodeState state) => kind switch
    {
        ChannelKind.Position => new Vec3(state.AnchoredPosition.X, state.AnchoredPosition.Y, 0f),
        ChannelKind.Scale => state.LocalScale,
        ChannelKind.Euler => state.LocalEulerAngles,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "리그 트리에 없는 채널 종류"),
    };

    private static float Component(Vec3 value, Axis axis) => axis switch
    {
        Axis.X => value.X,
        Axis.Y => value.Y,
        _ => value.Z,
    };

    /// <summary>relative의 뜻대로 — 위치·회전은 차, 배율은 비(시작 배율이 0이면 비를 못 낸다: 1로 둔다).</summary>
    private static float Delta(ChannelKind kind, float start, float now)
        => kind == ChannelKind.Scale ? (Math.Abs(start) < 1e-9f ? 1f : now / start) : now - start;
}
