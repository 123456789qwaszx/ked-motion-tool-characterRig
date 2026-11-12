using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Editing;

// 이모지 명령(docs/PLAN-emoji.md E3). 각각 되돌리기 한 칸이고, 같은 값이면 흔적이 없다(Mutate가 지킨다).
// 구조(이름이 있는가 · 겹치지 않는가)는 여기서 지키고, 규칙(구간이 타임라인 안인가 · 그림이 있는가 · 잇기가 맞는가)은 검증(EmojiValidator)이 알린다.
// 타임라인의 키는 모션의 명령(AddChannel · SetKeys · KeyPose …)이 그대로 맡는다 — 이름은 EmojiAsset.TimelineNameOf.
public sealed partial class MotionEditor
{
    /// <summary>새 이모지의 타임라인 길이(초) — 24프레임.</summary>
    public const float DefaultEmojiSeconds = 1f;

    // ── 이모지 ────────────────────────────────────────────────────────

    /// <summary>빈 이모지를 더한다 — 리그 없이, 길이 <paramref name="seconds"/>의 빈 타임라인과 함께.</summary>
    public EmojiAsset AddEmoji(string name, float seconds = DefaultEmojiSeconds)
    {
        ArgumentNullException.ThrowIfNull(name);
        RequireNewEmojiName(name);

        var emoji = new EmojiAsset
        {
            Name = name,
            Timeline = new MotionAsset
            {
                Name = EmojiAsset.TimelineNameOf(name),
                Rig = EmojiRigFrame.RigKind,
                Duration = seconds,
                End = MotionEnd.Restore,
                PreviewMode = MotionChannelMode.Relative,
            },
        };

        Mutate(MotionChangeKind.Emoji, () => Document.Emojis.Add(emoji));
        return emoji;
    }

    /// <summary>
    /// 새 이모지를 한 칸에 짓는다(첫 화면 — <see cref="EmojiStart"/>): 그림 한 장에 그림(리그) 하나, 수명, 움직임의 키.
    ///
    /// - 그림의 이름은 파일 이름에서 온다(규칙에 맞게 고치고, 겹치면 번호). 그림이 없으면 빈 그림 <c>body</c> 하나.
    /// - 머무는 이모지: 머묾 구간을 놓고 잇는다. 등장 · 퇴장의 움직임을 골랐으면 그 구간도 놓는다(타임라인을 셋으로 — 고르지 않은 쪽은 머묾이 차지한다).
    /// - 한 번 재생하는 이모지: 구간 없이, 등장의 움직임은 앞 1/3에 · 퇴장의 움직임은 뒤 1/3에. 머묾의 움직임은 가운데에.
    /// - 움직임의 대상은 이모지 전체다 — 그림이 여럿이어도 함께 움직인다. 넣은 것은 그냥 키다.
    /// </summary>
    public EmojiAsset StartEmoji(EmojiStart start, EmojiRigFrame? frame = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        RequireNewEmojiName(start.Name);
        EmojiAsset? made = null;

        Batch(MotionChangeKind.Emoji, () =>
        {
            EmojiAsset emoji = AddEmoji(start.Name);
            made = emoji;

            foreach (string image in start.Images)
            {
                string stem = MotionNames.Sanitize(Path.GetFileNameWithoutExtension(image));
                string rig = stem;

                for (int i = 2; emoji.FindRig(rig) is not null; i++)
                {
                    rig = $"{stem}_{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }

                AddEmojiRig(emoji.Name, rig, new[] { image });
            }

            if (emoji.Rigs.Count == 0)
            {
                AddEmojiRig(emoji.Name, "body");
            }

            int last = emoji.LastFrame;
            int third = Math.Max(1, (last + 1) / 3);
            bool leadIn = start.In is not null;
            bool leadOut = start.Out is not null;
            (int Start, int End) @in = (0, third - 1);
            (int Start, int End) @out = (last - third + 1, last);
            (int Start, int End) hold = (leadIn || !start.Stays ? third : 0, leadOut || !start.Stays ? last - third : last);

            if (start.Stays)
            {
                string? inName = null;
                string? outName = null;

                if (leadIn)
                {
                    inName = AddEmojiSpan(emoji.Name, EmojiLife.SpanStem(EmojiLifeRole.In), @in.Start, @in.End).Name;
                }

                string holdName = AddEmojiSpan(emoji.Name, EmojiLife.SpanStem(EmojiLifeRole.Hold), hold.Start, hold.End).Name;

                if (leadOut)
                {
                    outName = AddEmojiSpan(emoji.Name, EmojiLife.SpanStem(EmojiLifeRole.Out), @out.Start, @out.End).Name;
                }

                SetEmojiLinks(emoji.Name, inName, holdName, outName);
            }

            string[] whole = { "" };

            if (start.In is { } inMove)
            {
                LayEmojiMove(emoji.Name, whole, inMove, @in.Start, @in.End, frame);
            }

            if (start.Hold is { } holdMove)
            {
                LayEmojiMove(emoji.Name, whole, holdMove, hold.Start, hold.End, frame);
            }

            if (start.Out is { } outMove)
            {
                LayEmojiMove(emoji.Name, whole, outMove, @out.Start, @out.End, frame);
            }
        });

        return made!;
    }

    /// <summary>이모지를 지운다 — 타임라인도 함께.</summary>
    public void RemoveEmoji(string name)
    {
        EmojiAsset emoji = RequireEmoji(name);
        Mutate(MotionChangeKind.Emoji, () => Document.Emojis.Remove(emoji));
    }

    /// <summary>이름을 바꾼다 — 타임라인의 이름이 따라온다. 한 번 내보낸 이름은 대본이 가리키므로 바꾸지 않는 것이 규약이다(막지는 않는다).</summary>
    public void RenameEmoji(string name, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        EmojiAsset emoji = RequireEmoji(name);

        if (string.Equals(name, newName, StringComparison.Ordinal))
        {
            return;
        }

        RequireNewEmojiName(newName);

        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.Name = newName;
            emoji.Timeline.Name = EmojiAsset.TimelineNameOf(newName);
        });
    }

    /// <summary>이모지를 통째로 복제한다(리그 · 키 · 구간 · 잇기) — 재사용은 가져와서 고치는 것이다(docs/emoji-rig.md §2).</summary>
    public EmojiAsset DuplicateEmoji(string name, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        EmojiAsset source = RequireEmoji(name);
        RequireNewEmojiName(newName);

        var holder = new MotionDocument();
        holder.Emojis.Add(source);
        EmojiAsset copy = Serialization.MotionDocumentJson.Read(Serialization.MotionDocumentJson.Write(holder)).Emojis[0];
        copy.Name = newName;
        copy.Timeline.Name = EmojiAsset.TimelineNameOf(newName);

        Mutate(MotionChangeKind.Emoji, () => Document.Emojis.Insert(Document.Emojis.IndexOf(source) + 1, copy));
        return copy;
    }

    /// <summary>이모지별 오프셋(<c>Emoji_Offset</c>) — 로케이터 기준 px.</summary>
    public void SetEmojiOffset(string name, float x, float y)
    {
        EmojiAsset emoji = RequireEmoji(name);
        RequireFinite(x, y);
        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.OffsetX = x;
            emoji.OffsetY = y;
        });
    }

    /// <summary>
    /// 이모지가 인물의 방향을 따르지 않는가(<c>fixedSide</c>) — 참이면 인물이 뒤집혀도 만든 쪽 그대로 선다. 같은 값이면 기록하지 않는다.
    /// </summary>
    public void SetEmojiFixedSide(string name, bool fixedSide)
    {
        EmojiAsset emoji = RequireEmoji(name);

        if (emoji.FixedSide == fixedSide)
        {
            return;
        }

        Mutate(MotionChangeKind.Emoji, () => emoji.FixedSide = fixedSide);
    }

    // ── 리그 ──────────────────────────────────────────────────────────

    /// <summary>리그를 더한다(맨 위에 그려진다). 이름은 이모지 안에서 하나뿐이고 이름 규칙(<c>[a-z0-9_]+</c>)을 따른다.</summary>
    public EmojiRigAsset AddEmojiRig(string emojiName, string rigName, IEnumerable<string>? images = null)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        RequireNewRigName(emoji, rigName);

        var rig = new EmojiRigAsset { Name = rigName, Images = images?.ToList() ?? new List<string>() };
        Mutate(MotionChangeKind.Emoji, () => emoji.Rigs.Add(rig));
        return rig;
    }

    /// <summary>리그를 지운다 — 그 리그를 가리키는 타임라인 채널도 같은 칸에서 사라진다(없는 리그를 가리키며 남지 않게).</summary>
    public void RemoveEmojiRig(string emojiName, string rigName)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);
        string node = EmojiRigFrame.RigNode(rigName, "");

        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.Rigs.Remove(rig);
            emoji.Timeline.Channels.RemoveAll(c => c.Node.StartsWith(node, StringComparison.Ordinal));
        });
    }

    /// <summary>리그의 이름을 바꾼다 — 그 리그를 가리키는 타임라인 채널이 따라온다.</summary>
    public void RenameEmojiRig(string emojiName, string rigName, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);

        if (string.Equals(rigName, newName, StringComparison.Ordinal))
        {
            return;
        }

        RequireNewRigName(emoji, newName);
        string oldHead = EmojiRigFrame.RigNode(rigName, "");
        string newHead = EmojiRigFrame.RigNode(newName, "");

        Mutate(MotionChangeKind.Emoji, () =>
        {
            rig.Name = newName;

            foreach (ChannelAsset channel in emoji.Timeline.Channels.Where(c => c.Node.StartsWith(oldHead, StringComparison.Ordinal)))
            {
                channel.Node = newHead + channel.Node[oldHead.Length..];
            }
        });
    }

    /// <summary>리그의 순서(그려지는 순서 — 뒤가 위)를 바꾼다. 자리는 0 … 개수-1 안으로 조인다.</summary>
    public void MoveEmojiRig(string emojiName, string rigName, int index)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);
        int target = Math.Clamp(index, 0, emoji.Rigs.Count - 1);

        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.Rigs.Remove(rig);
            emoji.Rigs.Insert(target, rig);
        });
    }

    /// <summary>리그의 기본 자리 · 배율 · 회전(<c>EmojiRig_Place</c>) — 키가 아니다. 배율 0은 쓸 수 없다.</summary>
    public void SetEmojiRigPlace(string emojiName, string rigName, float x, float y, float scale, float rotation)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);
        RequireFinite(x, y, scale, rotation);

        if (scale == 0f)
        {
            throw new InvalidOperationException("기본 배율은 0일 수 없다 — 그림이 사라진다.");
        }

        Mutate(MotionChangeKind.Emoji, () =>
        {
            rig.PlaceX = x;
            rig.PlaceY = y;
            rig.Scale = scale;
            rig.Rotation = rotation;
        });
    }

    /// <summary>
    /// 리그가 뒤집히면 안 되는 그림인가(<c>upright</c>) — 참이면 이모지가 거울에 비쳐도 그림과 키는 만든 그대로 보인다. 같은 값이면 기록하지 않는다.
    /// </summary>
    public void SetEmojiRigUpright(string emojiName, string rigName, bool upright)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);

        if (rig.Upright == upright)
        {
            return;
        }

        Mutate(MotionChangeKind.Emoji, () => rig.Upright = upright);
    }

    /// <summary>리그의 그림들(문서 기준 경로) — 첫 장이 기본 스프라이트. 그림 바꾸기 키는 몇 번째인가로 가리키므로 순서를 바꾸면 키가 다른 장을 가리킨다.</summary>
    public void SetEmojiRigImages(string emojiName, string rigName, IEnumerable<string> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);
        List<string> list = images.ToList();

        if (list.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("빈 그림 경로는 둘 수 없다.");
        }

        Mutate(MotionChangeKind.Emoji, () => rig.Images = list);
    }

    /// <summary>
    /// 그림 넘기기를 한 번에 깐다 — <paramref name="startFrame"/>부터 <paramref name="everyFrames"/>프레임마다 다음 장(0 → 1 → … → 처음으로)을
    /// <paramref name="count"/>번. 그 범위에 있던 그림 키는 바뀐다. 그림 채널이 없으면 만든다. 타임라인 밖은 깔지 않는다. 한 칸이다.
    /// </summary>
    /// <param name="frame">이모지 리그의 틀(그림 노드의 이름). null이면 내장 덤프의 것.</param>
    /// <returns>깐 키의 수.</returns>
    public int LayEmojiImageFlips(string emojiName, string rigName, int startFrame, int everyFrames, int count, EmojiRigFrame? frame = null)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiRigAsset rig = RequireRig(emoji, rigName);

        if (rig.Images.Count == 0)
        {
            throw new InvalidOperationException($"그림 '{rigName}'에 그림 파일이 없다 — 넘길 장이 없다.");
        }

        if (startFrame < 0 || everyFrames < 1 || count < 1)
        {
            throw new InvalidOperationException($"시작 ≥ 0 · 간격 ≥ 1 · 횟수 ≥ 1이어야 한다 ({startFrame} · {everyFrames} · {count})");
        }

        List<int> frames = Enumerable.Range(0, count).Select(i => startFrame + i * everyFrames).Where(f => f <= emoji.LastFrame).ToList();

        if (frames.Count == 0)
        {
            return 0;
        }

        string node = (frame ?? EmojiRigFrame.Builtin).ImageNodeOf(rigName);
        float from = EmojiAsset.SecondsOf(frames[0]) - MotionRules.Tolerance;
        float to = EmojiAsset.SecondsOf(frames[^1]) + MotionRules.Tolerance;

        Mutate(MotionChangeKind.Keys, () =>
        {
            ChannelAsset channel = emoji.Timeline.FindChannel(node, ChannelKind.Image) ?? AddTo(emoji.Timeline, new ChannelAsset { Node = node, Kind = ChannelKind.Image });
            List<KeyAsset> keys = channel.X?.Keys.Where(k => k.T < from || k.T > to).ToList() ?? new List<KeyAsset>();

            for (int i = 0; i < frames.Count; i++)
            {
                keys.Add(new KeyAsset { T = EmojiAsset.SecondsOf(frames[i]), V = i % rig.Images.Count, Tangent = TangentMode.Step });
            }

            channel.X = new ComponentCurve { Keys = keys.OrderBy(k => k.T).ToList() };
            TangentMath.Recompute(channel.X.Keys);
        });

        return frames.Count;

        static ChannelAsset AddTo(MotionAsset timeline, ChannelAsset channel)
        {
            timeline.Channels.Add(channel);
            return channel;
        }
    }

    /// <summary>
    /// 움직임을 가져와 넣는다 — <paramref name="startFrame"/>…<paramref name="endFrame"/>(끝 포함)에 그 움직임의 키를 깐다. 한 칸이다.
    /// 대상은 그림들(리그 이름)이고 빈 이름은 이모지 전체다. 그 범위에 있던 같은 성분의 키는 바뀌고, 범위 밖의 키와 다른 성분은 그대로다.
    /// 채널이 없으면 만든다. 넣은 것은 그냥 키다 — 프리셋을 가리키지 않는다.
    /// </summary>
    /// <param name="frame">이모지 리그의 틀(키를 받는 노드의 이름). null이면 내장 덤프의 것.</param>
    /// <returns>깐 키의 수.</returns>
    public int LayEmojiMove(string emojiName, IReadOnlyList<string> targets, EmojiMove move, int startFrame, int endFrame, EmojiRigFrame? frame = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        EmojiAsset emoji = RequireEmoji(emojiName);
        RequireSpanRange(startFrame, endFrame);

        if (endFrame > emoji.LastFrame)
        {
            throw new InvalidOperationException($"움직임을 넣을 곳({startFrame}~{endFrame}fr)이 타임라인(0~{emoji.LastFrame}fr) 밖이다.");
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("움직임을 넣을 대상이 없다.");
        }

        foreach (string target in targets.Where(t => t.Length > 0))
        {
            RequireRig(emoji, target);
        }

        EmojiRigFrame shape = frame ?? EmojiRigFrame.Builtin;
        float from = EmojiAsset.SecondsOf(startFrame) - MotionRules.Tolerance;
        float to = EmojiAsset.SecondsOf(endFrame) + MotionRules.Tolerance;
        int laid = 0;

        Mutate(MotionChangeKind.Keys, () =>
        {
            foreach (string target in targets.Distinct(StringComparer.Ordinal))
            {
                string node = shape.TargetNode(target);

                foreach (EmojiMoveCurve curve in EmojiMoves.Curves(move))
                {
                    ChannelAsset? channel = emoji.Timeline.FindChannel(node, curve.Kind);

                    if (channel is null)
                    {
                        channel = new ChannelAsset { Node = node, Kind = curve.Kind };
                        emoji.Timeline.Channels.Add(channel);
                    }

                    IReadOnlyList<(int Frame, float Value, TangentMode Tangent)> placed = EmojiMoves.Place(curve, startFrame, endFrame);

                    foreach (Axis axis in curve.Axes)
                    {
                        List<KeyAsset> keys = channel.Get(axis)?.Keys.Where(k => k.T < from || k.T > to).ToList() ?? new List<KeyAsset>();
                        keys.AddRange(placed.Select(p => new KeyAsset { T = EmojiAsset.SecondsOf(p.Frame), V = p.Value, Tangent = p.Tangent }));
                        var component = new ComponentCurve { Keys = keys.OrderBy(k => k.T).ToList() };
                        TangentMath.Recompute(component.Keys);
                        channel.Set(axis, component);
                        laid += placed.Count;
                    }
                }
            }
        });

        return laid;
    }

    // ── 구간과 잇기 ───────────────────────────────────────────────────

    /// <summary>이름 붙은 구간을 더한다(프레임, 끝 포함). 겹쳐도 된다. 타임라인 밖이면 검증이 알린다.</summary>
    public EmojiSpan AddEmojiSpan(string emojiName, string spanName, int start, int end)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        RequireNewSpanName(emoji, spanName);
        RequireSpanRange(start, end);

        var span = new EmojiSpan { Name = spanName, Start = start, End = end };
        Mutate(MotionChangeKind.Emoji, () => emoji.Spans.Add(span));
        return span;
    }

    /// <summary>
    /// 수명의 자리(등장 · 머묾 · 퇴장)에 새 구간을 놓고 잇는다 — 한 칸. 프레임은 <see cref="EmojiLife.Suggest"/>가 정한다.
    /// 그 자리에 이미 이은 구간이 있으면 막는다.
    /// </summary>
    public EmojiSpan AddEmojiLifeSpan(string emojiName, EmojiLifeRole role)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);

        if (EmojiLife.LinkOf(emoji, role) is { } linked)
        {
            throw new InvalidOperationException($"이모지 '{emoji.Name}'의 {EmojiLife.Label(role)}에는 이미 구간 '{linked}'을 이었다.");
        }

        (int start, int end) = EmojiLife.Suggest(emoji, role);
        var span = new EmojiSpan { Name = EmojiLife.FreeSpanName(emoji, role), Start = start, End = end };

        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.Spans.Add(span);

            switch (role)
            {
                case EmojiLifeRole.In:
                    emoji.In = span.Name;
                    break;
                case EmojiLifeRole.Hold:
                    emoji.Hold = span.Name;
                    break;
                default:
                    emoji.Out = span.Name;
                    break;
            }
        });
        return span;
    }

    public void SetEmojiSpan(string emojiName, string spanName, int start, int end)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiSpan span = RequireSpan(emoji, spanName);
        RequireSpanRange(start, end);

        Mutate(MotionChangeKind.Emoji, () =>
        {
            span.Start = start;
            span.End = end;
        });
    }

    /// <summary>구간의 이름을 바꾼다 — 등장 · 머묾 · 퇴장의 잇기가 따라온다.</summary>
    public void RenameEmojiSpan(string emojiName, string spanName, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiSpan span = RequireSpan(emoji, spanName);

        if (string.Equals(spanName, newName, StringComparison.Ordinal))
        {
            return;
        }

        RequireNewSpanName(emoji, newName);

        Mutate(MotionChangeKind.Emoji, () =>
        {
            span.Name = newName;
            emoji.In = Follow(emoji.In);
            emoji.Hold = Follow(emoji.Hold);
            emoji.Out = Follow(emoji.Out);
        });

        string? Follow(string? link) => string.Equals(link, spanName, StringComparison.Ordinal) ? newName : link;
    }

    /// <summary>구간을 지운다 — 그 구간을 이은 자리는 같은 칸에서 비워진다.</summary>
    public void RemoveEmojiSpan(string emojiName, string spanName)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);
        EmojiSpan span = RequireSpan(emoji, spanName);

        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.Spans.Remove(span);
            emoji.In = Drop(emoji.In);
            emoji.Hold = Drop(emoji.Hold);
            emoji.Out = Drop(emoji.Out);
        });

        string? Drop(string? link) => string.Equals(link, spanName, StringComparison.Ordinal) ? null : link;
    }

    /// <summary>
    /// 구간을 등장 · 머묾 · 퇴장에 잇는다(null = 잇지 않는다). 셋을 한 칸으로 정한다.
    /// 머묾을 이으면 머무는 이모지, 잇지 않으면 타임라인 전체를 한 번 재생하고 사라지는 이모지다.
    /// </summary>
    public void SetEmojiLinks(string emojiName, string? @in, string? hold, string? @out)
    {
        EmojiAsset emoji = RequireEmoji(emojiName);

        foreach (string? link in new[] { @in, hold, @out })
        {
            if (link is not null)
            {
                RequireSpan(emoji, link);
            }
        }

        Mutate(MotionChangeKind.Emoji, () =>
        {
            emoji.In = @in;
            emoji.Hold = hold;
            emoji.Out = @out;
        });
    }

    // ── 지키기 ────────────────────────────────────────────────────────

    private EmojiAsset RequireEmoji(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Document.FindEmoji(name) ?? throw new KeyNotFoundException($"이모지 '{name}'이 없다.");
    }

    private static EmojiRigAsset RequireRig(EmojiAsset emoji, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return emoji.FindRig(name) ?? throw new KeyNotFoundException($"이모지 '{emoji.Name}'에 그림 '{name}'이 없다.");
    }

    private static EmojiSpan RequireSpan(EmojiAsset emoji, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return emoji.FindSpan(name) ?? throw new KeyNotFoundException($"이모지 '{emoji.Name}'에 구간 '{name}'이 없다.");
    }

    private void RequireNewEmojiName(string name)
    {
        if (!MotionNames.IsValid(name))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(name));
        }

        if (Document.FindEmoji(name) is not null)
        {
            throw new InvalidOperationException($"이모지 '{name}'이 이미 있다 — 이름은 하나뿐이어야 한다.");
        }
    }

    private static void RequireNewRigName(EmojiAsset emoji, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!MotionNames.IsValid(name))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(name));
        }

        if (emoji.FindRig(name) is not null)
        {
            throw new InvalidOperationException($"이모지 '{emoji.Name}'에 그림 '{name}'이 이미 있다 — 키가 그림을 이름으로 가리킨다.");
        }
    }

    private static void RequireNewSpanName(EmojiAsset emoji, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!MotionNames.IsValid(name))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(name));
        }

        if (string.Equals(name, EmojiAsset.MainClip, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{EmojiAsset.MainClip}'은 타임라인 전체의 클립 이름이라 구간에 쓰지 않는다.");
        }

        if (emoji.FindSpan(name) is not null)
        {
            throw new InvalidOperationException($"이모지 '{emoji.Name}'에 구간 '{name}'이 이미 있다.");
        }
    }

    private static void RequireSpanRange(int start, int end)
    {
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"구간은 0 ≤ 시작 ≤ 끝이어야 한다 ({start}~{end})");
        }
    }

    private static void RequireFinite(params float[] values)
    {
        if (values.Any(v => !float.IsFinite(v)))
        {
            throw new InvalidOperationException("수가 아닌 값은 쓸 수 없다.");
        }
    }

    /// <summary>내보내는 모션만 — 이모지의 타임라인은 이모지와 함께 이름이 바뀌고 지워진다.</summary>
    private MotionAsset RequireExported(string name)
    {
        if (EmojiAsset.IsTimelineName(name, out string emoji))
        {
            throw new InvalidOperationException($"'{name}'은 이모지 '{emoji}'의 타임라인이다 — 이모지와 함께 이름을 바꾸고 지운다.");
        }

        return Require(name);
    }
}
