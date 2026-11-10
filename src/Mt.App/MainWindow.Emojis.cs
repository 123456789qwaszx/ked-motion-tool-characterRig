using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Validation;

namespace Mt.App;

// 이모지 정의의 셸(PLAN-parts.md §8) — 오른쪽 "이모지" 탭. 목록에서 고르면 그 이모지가 시간의 주인이 된다:
// 뷰포트·타임 슬라이더가 수명 한 번(등장 → 머묾 → 퇴장)을 재생하고, 모션·클립 선택은 풀린다. 값은 아래 칸에서 고친다(각각 되돌리기 한 칸).
public partial class MainWindow
{
    private const string NoneChoice = "(없음)";

    private readonly ListBox _emojiList = new() { MinHeight = 60, MaxHeight = 150, FontSize = 11 }; // 정의가 많아도 칸들이 화면 밖으로 밀리지 않게
    private readonly TextBox _emojiName = new() { FontSize = 11 };
    private readonly ComboBox _emojiSkin = new() { FontSize = 11 };
    private readonly ComboBox _emojiSkinFlipped = new() { FontSize = 11 };
    private readonly ComboBox _emojiEnter = new() { FontSize = 11 };
    private readonly ComboBox _emojiHold = new() { FontSize = 11 };
    private readonly ComboBox _emojiExit = new() { FontSize = 11 };
    private readonly TextBox _emojiHoldSeconds = new() { FontSize = 11, PlaceholderText = "끌 때까지 머문다" };
    private readonly TextBox _emojiOffsetX = new() { FontSize = 11 };
    private readonly TextBox _emojiOffsetY = new() { FontSize = 11 };
    private readonly TextBox _emojiLocator = new() { FontSize = 11, PlaceholderText = "(붙는 노드)" };
    private readonly StackPanel _emojiFields = new() { Spacing = 4 };

    internal string? SelectedEmoji => _emojiList.SelectedItem as string;

    /// <summary>테스트 손잡이 — 이모지 목록에서 고른다(null이면 푼다).</summary>
    internal void SelectEmoji(string? name) => _emojiList.SelectedItem = name;

    /// <summary>테스트 손잡이 — "이모지 추가".</summary>
    internal string AddEmojiFromForm() => AddEmoji();

    /// <summary>테스트 손잡이 — 정의의 값을 인스펙터의 길로 고친다.</summary>
    internal void UpdateSelectedEmoji(Action<EmojiAsset> change) => WithEmoji(change);

    /// <summary>테스트 손잡이 — 칸들의 지금 글.</summary>
    internal (string Name, string? Skin, string? Enter, string? Hold, string? Exit, string HoldSeconds, bool Enabled) EmojiFormState
        => (_emojiName.Text ?? "", Choice(_emojiSkin), Choice(_emojiEnter), Choice(_emojiHold), Choice(_emojiExit), _emojiHoldSeconds.Text ?? "", _emojiFields.IsEnabled);

    private static string? Choice(ComboBox combo) => combo.SelectedItem as string is { } s && s != NoneChoice ? s : null;

    private EmojiAsset? CurrentEmoji => SelectedEmoji is { } name ? _session.Editor.Document.FindEmoji(name) : null;

    private Control BuildEmojiTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        _emojiList.SelectionChanged += (_, _) => OnEmojiSelected();
        ToolTip.SetTip(_emojiList, "이모지 정의 — 그림(스킨) + 등장·머묾·퇴장 모션 + 머무는 시간 + 자리. 고르면 뷰포트가 수명 한 번을 재생한다(모션·클립 선택은 풀린다)");
        panel.Children.Add(_emojiList);

        Button add = SmallButton("이모지 추가", (_, _) => Guard("이모지 추가", () => AddEmoji()));
        ToolTip.SetTip(add, "고른 오브젝트의 리그(조각이 있는 리그 — 이모지 리그)로 새 정의를 만든다. 그 오브젝트가 입은 스킨이 있으면 그것이 그림이 된다");
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { add, SmallButton("이모지 삭제", (_, _) => Guard("이모지 삭제", RemoveEmoji)), SmallButton("emojis.json…", OnExportEmojisClick) },
        });

        _emojiFields.Children.Add(Labeled("키", _emojiName));
        _emojiFields.Children.Add(Labeled("그림(스킨)", _emojiSkin));
        _emojiFields.Children.Add(Labeled("반전 판", _emojiSkinFlipped));
        Button newSkin = SmallButton("새 스킨", (_, _) => Guard("새 스킨", () => NewEmojiSkin(flipped: false)));
        Button newFlipped = SmallButton("새 반전 판", (_, _) => Guard("새 반전 판", () => NewEmojiSkin(flipped: true)));
        ToolTip.SetTip(newSkin, "이 이모지의 이름으로 빈 스킨을 만들어 그림으로 삼는다 — 되돌리기 한 번. 아래 조각 칸에서 그림을 준다");
        ToolTip.SetTip(newFlipped, "이 이모지의 반전 판 스킨(이름_반전)을 만든다 — 되돌리기 한 번");
        _emojiFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { newSkin, newFlipped } });
        _emojiFields.Children.Add(_emojiSkinParts);
        ToolTip.SetTip(_emojiSkinFlipped, "붙은 캐릭터가 좌우 반전일 때 입는 스킨(말풍선의 꼬리가 반대쪽인 판) — 자리와 움직임은 뒤집히고 이 그림은 뒤집지 않고 그린다. 없으면 같은 그림이 뒤집혀 보인다");
        _emojiFields.Children.Add(Labeled("등장", _emojiEnter));
        _emojiFields.Children.Add(Labeled("머묾(되풀이)", _emojiHold));
        _emojiFields.Children.Add(Labeled("퇴장", _emojiExit));
        _emojiFields.Children.Add(Labeled("머무는 시간", _emojiHoldSeconds));
        _emojiFields.Children.Add(Labeled("오프셋 X", _emojiOffsetX));
        _emojiFields.Children.Add(Labeled("오프셋 Y", _emojiOffsetY));
        _emojiFields.Children.Add(Labeled("붙는 자리", _emojiLocator));
        panel.Children.Add(_emojiFields);

        // 리그 — 툴이 정의한 리그면 조각의 수와 크기를 여기서 바꾼다(덤프의 리그는 런타임이 원본이라 못 바꾼다).
        _rigFields.Children.Add(Header("리그"));
        _rigFields.Children.Add(_rigSummary);
        _rigFields.Children.Add(Labeled("조각 수", _rigPartCount));
        _rigFields.Children.Add(Labeled("조각 크기", _rigPartSize));
        _rigFields.Children.Add(SmallButton("rigs.json…", OnExportRigsClick));
        BuildRigEditor(_rigFields);
        panel.Children.Add(_rigFields);
        ToolTip.SetTip(_rigPartCount, "이 리그의 조각(그림 노드) 수 — 1번부터 다시 세운다. 가장 조각이 많은 이모지에 맞춘다(안 쓰는 조각은 스킨이 그림을 안 주면 안 그려진다)");
        ToolTip.SetTip(_rigPartSize, "조각 사각형의 한 변(px) — 그림이 이 안에 종횡비로 맞춰진다");
        OnCommit(_rigPartCount, _ => CommitRigParts());
        OnCommit(_rigPartSize, _ => CommitRigParts());

        ToolTip.SetTip(_emojiName, "대본이 부르는 이름 — 빈칸 없는 글(한글도 된다)");
        ToolTip.SetTip(_emojiEnter, "뜰 때 한 번 도는 모션(튀어나옴). 유지(commit)면 그 자세에서 머묾이 시작한다");
        ToolTip.SetTip(_emojiHold, "머무는 동안 처음부터 되풀이하는 모션(까딱임·punch). 등장이 끝난 자세에서 시작하고 바퀴가 쌓이지 않는다. 끝에서 처음으로 튀는 머묾(흘러내렸다가 다시 위에서)은 끝 규칙을 '유지'로 둔다 — '복원' 모션은 중립값에서 끝나야 한다");
        ToolTip.SetTip(_emojiExit, "사라질 때 한 번 도는 모션(흐려짐). 머묾이 멈춘 그 자세에서 시작한다");
        ToolTip.SetTip(_emojiHoldSeconds, "등장이 끝난 뒤 퇴장이 시작하기까지(초). 비우면 끌 때까지 머무는 이모지(상태) — 미리보기는 머묾을 얼마간만 보인다");
        ToolTip.SetTip(_emojiOffsetX, "붙는 자리에서의 오프셋(px) — Emoji_Anchor의 위치로 들어간다. 이모지마다 머리 옆·위·입 옆이 다르다");
        ToolTip.SetTip(_emojiLocator, "기준점 이름(faceAura·face·bust·body·feet·handLeft·handRight) — 붙은 캐릭터의 그 점에서부터 오프셋을 잰다. 비우면 붙는 노드에서부터");

        OnCommit(_emojiName, text =>
        {
            if (!_refreshing && CurrentEmoji is { } emoji && text.Trim() != emoji.Name)
            {
                Guard("이모지 키", () =>
                {
                    _session.Editor.RenameEmoji(emoji.Name, text);
                    _emojiList.SelectedItem = text.Trim();
                });
            }
        });

        void Combo(ComboBox combo, Action<EmojiAsset, string?> set)
            => combo.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && combo.SelectedIndex >= 0)
                {
                    WithEmoji(e => set(e, Choice(combo)));
                }
            };

        Combo(_emojiSkin, (e, v) => e.Skin = v);
        Combo(_emojiSkinFlipped, (e, v) => e.SkinFlipped = v);
        Combo(_emojiEnter, (e, v) => e.Enter = v);
        Combo(_emojiHold, (e, v) => e.Hold = v);
        Combo(_emojiExit, (e, v) => e.Exit = v);
        OnCommit(_emojiHoldSeconds, text => WithEmoji(e => e.HoldSeconds = string.IsNullOrWhiteSpace(text) ? null : TryFloat(text, out float v) ? v : e.HoldSeconds));
        OnCommit(_emojiOffsetX, text => WithEmoji(e => e.OffsetX = TryFloat(text, out float v) ? v : e.OffsetX));
        OnCommit(_emojiOffsetY, text => WithEmoji(e => e.OffsetY = TryFloat(text, out float v) ? v : e.OffsetY));
        OnCommit(_emojiLocator, text => WithEmoji(e => e.Locator = text));
        return panel;
    }

    // 이 이모지의 스킨을 그 자리에서 고친다 — "씬" 탭의 스킨 칸(오브젝트가 입은 스킨)과 같은 줄을 쓴다.
    private readonly StackPanel _emojiSkinParts = new() { Spacing = 3 };
    private readonly Dictionary<string, SkinSetupBoxSet> _emojiSetupBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkinSetupBoxSet> _emojiFlippedSetupBoxes = new(StringComparer.Ordinal);

    /// <summary>테스트 손잡이 — "이모지" 탭의 조각 그림 칸(반전 판이면 flipped).</summary>
    internal TextBox? EmojiSkinPartBox(string node, bool flipped = false)
        => _emojiSkinParts.Children.OfType<Panel>().SelectMany(p => p.Children.OfType<TextBox>()).FirstOrDefault(b => Equals(b.Tag, node) && Equals(b.Name == "flipped", flipped));

    internal SkinSetupBoxSet? EmojiSkinSetupBoxes(string node, bool flipped = false)
        => (flipped ? _emojiFlippedSetupBoxes : _emojiSetupBoxes).TryGetValue(node, out SkinSetupBoxSet? boxes) ? boxes : null;

    internal string? NewEmojiSkinFromForm(bool flipped) => NewEmojiSkin(flipped);

    /// <summary>이 이모지의 이름으로 새 스킨을 만들어 그림(또는 반전 판)으로 삼는다 — 한 칸.</summary>
    private string? NewEmojiSkin(bool flipped)
    {
        if (CurrentEmoji is not { } emoji)
        {
            return null;
        }

        MotionDocument document = _session.Editor.Document;
        string stem = flipped ? emoji.Name + "_반전" : emoji.Name;
        string name = stem;

        for (int i = 2; document.FindSkin(name) is not null; i++)
        {
            name = $"{stem}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.Batch(MotionChangeKind.Scene, () =>
        {
            _session.Editor.AddSkin(name, emoji.Rig);
            EmojiAsset value = emoji.Clone();

            if (flipped)
            {
                value.SkinFlipped = name;
            }
            else
            {
                value.Skin = name;
            }

            _session.Editor.UpdateEmoji(emoji.Name, value);
        });
        return name;
    }

    private void RefreshEmojiSkinParts(EmojiAsset? emoji)
    {
        _emojiSkinParts.Children.Clear();
        _emojiSetupBoxes.Clear();
        _emojiFlippedSetupBoxes.Clear();

        if (emoji is null)
        {
            return;
        }

        IReadOnlyList<string> parts = RigParts.Of(_session.Rig, emoji.Rig);

        void Rows(string? skinName, string title, Dictionary<string, SkinSetupBoxSet> boxes, bool flipped)
        {
            if (_session.Editor.Document.FindSkin(skinName) is not { } skin || skin.Rig != emoji.Rig)
            {
                return;
            }

            _emojiSkinParts.Children.Add(new TextBlock { Text = $"{title} — {skin.Name}", FontSize = 10, Opacity = 0.75, Margin = new Avalonia.Thickness(0, 4, 0, 0) });

            foreach (string node in parts)
            {
                Control row = SkinPartRow(skin, node, boxes);

                if (flipped && row is Panel panel)
                {
                    foreach (TextBox box in panel.Children.OfType<TextBox>())
                    {
                        box.Name = "flipped";
                    }
                }

                _emojiSkinParts.Children.Add(row);
            }
        }

        Rows(emoji.Skin, "조각 그림", _emojiSetupBoxes, flipped: false);
        Rows(emoji.SkinFlipped, "반전 판의 조각 그림", _emojiFlippedSetupBoxes, flipped: true);
    }

    private readonly StackPanel _rigFields = new() { Spacing = 4 };
    private readonly TextBlock _rigSummary = new() { FontSize = 10, Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _rigPartCount = new() { FontSize = 11 };
    private readonly TextBox _rigPartSize = new() { FontSize = 11 };

    /// <summary>테스트 손잡이 — 리그 칸의 글(조각 수 · 조각 크기)과 고칠 수 있는가.</summary>
    internal (TextBox Count, TextBox Size, bool Editable) RigPartBoxes => (_rigPartCount, _rigPartSize, _rigPartCount.IsEnabled);

    /// <summary>지금 다루는 리그 — 고른 이모지의 것, 없으면 고른 오브젝트의 것.</summary>
    private string? RigInFocus => _rigPicked is { } picked && _session.Editor.Document.FindRig(picked) is not null ? picked : CurrentEmoji?.Rig ?? _viewport.SelectedObject?.Rig;

    private void CommitRigParts()
    {
        if (_refreshing || RigInFocus is not { } kind || _session.Editor.Document.FindRig(kind) is null)
        {
            return;
        }

        if (int.TryParse((_rigPartCount.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && TryFloat(_rigPartSize.Text ?? "", out float size))
        {
            Guard("리그 조각", () => _session.Editor.SetRigParts(kind, count, size));
        }
    }

    private void RefreshRigFields()
    {
        _refreshDepth++;

        try
        {
            string? kind = RigInFocus;
            RigAsset? rig = _session.Editor.Document.FindRig(kind);
            _rigPartCount.IsEnabled = rig is not null;
            _rigPartSize.IsEnabled = rig is not null;
            _rigSummary.Text = kind is null ? "(이모지나 오브젝트를 고른다)"
                : rig is not null ? $"{kind} — 툴에서 정의한 리그(노드 {rig.Nodes.Count}개). rigs.json으로 내보내 런타임이 세운다"
                : $"{kind} — 덤프의 리그(런타임이 원본 — 여기서 못 고친다)";

            if (!_rigPartCount.IsFocused)
            {
                _rigPartCount.Text = rig is null ? "" : rig.Nodes.Count(n => n.Image).ToString(CultureInfo.InvariantCulture);
            }

            if (!_rigPartSize.IsFocused)
            {
                _rigPartSize.Text = rig?.Nodes.FirstOrDefault(n => n.Image) is { } part ? part.Width.ToString("0.###", CultureInfo.InvariantCulture) : "";
            }

            RefreshRigEditor();
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private async void OnExportRigsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("rigs.json 내보내기", "rigs.json", new FilePickerFileType("rigs.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                _session.ExportRigs(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"리그 내보내기 실패: {exception.Message}");
        }
    }

    /// <summary>고른 정의를 고친다 — 모든 칸이 지나는 한 길(되돌리기 한 칸, 같은 값이면 흔적이 없다).</summary>
    private void WithEmoji(Action<EmojiAsset> change)
    {
        if (_refreshing || CurrentEmoji is not { } emoji)
        {
            return;
        }

        EmojiAsset next = emoji.Clone();
        change(next);
        Guard("이모지", () => _session.Editor.UpdateEmoji(emoji.Name, next));
    }

    private void OnEmojiSelected()
    {
        if (_refreshing)
        {
            return;
        }

        if (SelectedEmoji is not null)
        {
            // 시간의 주인은 하나 — 모션과 클립을 푼다. 모션 선택을 풀면 그 알림이 다시 묶는다(BindMotion → 이모지).
            DeselectClip();

            if (SelectedMotion is not null)
            {
                _motionList.SelectedItem = null;
                RefreshEmojiInspector();
                return;
            }
        }

        BindMotion(SelectedMotion);
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshEmojiInspector();
    }

    /// <summary>모션이나 클립을 골랐다 — 이모지 선택을 푼다(다시 묶는 일은 고른 쪽이 한다).</summary>
    private void DeselectEmoji()
    {
        if (SelectedEmoji is null)
        {
            return;
        }

        _refreshDepth++;

        try
        {
            _emojiList.SelectedItem = null;
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshEmojiInspector();
    }

    private void RefreshEmojis()
    {
        _refreshDepth++;

        try
        {
            string? selected = SelectedEmoji ?? _viewport.EmojiName;
            List<string> names = _session.Editor.Document.Emojis.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            _emojiList.ItemsSource = names;
            _emojiList.SelectedIndex = selected is null || SelectedMotion is not null || SelectedClip is not null ? -1 : names.IndexOf(selected);
        }
        finally
        {
            _refreshDepth--;
        }

        // 고른 이모지가 사라졌으면(삭제·되돌리기) 뷰포트를 풀어 준다.
        if (_viewport.EmojiName != (SelectedMotion is null && SelectedClip is null ? SelectedEmoji : null))
        {
            BindMotion(SelectedMotion);
        }

        RefreshEmojiInspector();
    }

    private void RefreshEmojiInspector()
    {
        _refreshDepth++;

        try
        {
            EmojiAsset? emoji = CurrentEmoji;
            _emojiFields.IsEnabled = emoji is not null;
            MotionDocument document = _session.Editor.Document;

            void Fill(ComboBox combo, IEnumerable<string> names, string? current)
            {
                var choices = new List<string> { NoneChoice };
                choices.AddRange(names);

                // 문서에 없는 이름이 적혀 있으면 그대로 보인다 — 조용히 지우지 않는다(검증이 알린다).
                if (current is not null && !choices.Contains(current))
                {
                    choices.Add(current);
                }

                combo.ItemsSource = choices;
                combo.SelectedIndex = current is null ? 0 : choices.IndexOf(current);
            }

            string rig = emoji?.Rig ?? "";
            List<string> motions = document.Motions.Where(m => m.Rig == rig).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Fill(_emojiSkin, document.Skins.Where(s => s.Rig == rig).Select(s => s.Name), emoji?.Skin);
            Fill(_emojiSkinFlipped, document.Skins.Where(s => s.Rig == rig).Select(s => s.Name), emoji?.SkinFlipped);
            RefreshEmojiSkinParts(emoji);
            Fill(_emojiEnter, motions, emoji?.Enter);
            Fill(_emojiHold, motions, emoji?.Hold);
            Fill(_emojiExit, motions, emoji?.Exit);

            void Write(TextBox box, string text)
            {
                if (!box.IsFocused)
                {
                    box.Text = text;
                }
            }

            Write(_emojiName, emoji?.Name ?? "");
            Write(_emojiHoldSeconds, emoji?.HoldSeconds is { } hold ? hold.ToString("0.###", CultureInfo.InvariantCulture) : "");
            Write(_emojiOffsetX, emoji is null ? "" : emoji.OffsetX.ToString("0.###", CultureInfo.InvariantCulture));
            Write(_emojiOffsetY, emoji is null ? "" : emoji.OffsetY.ToString("0.###", CultureInfo.InvariantCulture));
            Write(_emojiLocator, emoji?.Locator ?? "");
        }
        finally
        {
            _refreshDepth--;
            RefreshRigFields();
        }
    }

    private Control EmojiProblemRow(EmojiProblem problem) => new TextBlock
    {
        Text = (problem.Blocking ? "⚠ " : "△ ") + problem,
        FontSize = 11,
        Foreground = problem.Blocking ? Avalonia.Media.Brushes.OrangeRed : Avalonia.Media.Brushes.Goldenrod,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        MaxWidth = 700,
    };

    // ── 명령 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 새 정의 — 키 emoji, emoji_2…, 리그는 고른 오브젝트의 것(조각이 있는 리그여야 한다; 아니면 덤프에서 조각이 있는 첫 리그).
    /// 고른 오브젝트가 스킨을 입고 있으면 그것이 그림이 된다. 만든 것을 고른다 — 되돌리기 한 번.
    /// </summary>
    private string AddEmoji()
    {
        MotionEditor editor = _session.Editor;
        SceneObject? selected = _viewport.SelectedObject;
        // 리그: 고른 오브젝트의 것(조각이 있으면), 아니면 조각이 있는 첫 리그, 그것도 없으면 이모지 리그를 툴이 정의한다.
        string rig = selected is not null && RigParts.Of(_session.Rig, selected.Rig).Count > 0
            ? selected.Rig
            : _session.Rig?.RigKinds.FirstOrDefault(k => RigParts.Of(_session.Rig, k).Count > 0) ?? RigTemplates.EmojiKind;

        string name = "emoji";

        for (int n = 2; editor.Document.FindEmoji(name) is not null; n++)
        {
            name = $"emoji_{n}";
        }

        string? skin = selected is not null && selected.Rig == rig ? selected.Skin : null;

        editor.Batch(MotionChangeKind.Document, () =>
        {
            EnsureRigDefined(rig);
            editor.AddEmoji(name, rig);

            if (skin is not null)
            {
                editor.UpdateEmoji(name, new EmojiAsset { Skin = skin });
            }
        });

        _emojiList.SelectedItem = name;
        return name;
    }

    private void RemoveEmoji()
    {
        if (SelectedEmoji is { } name)
        {
            _session.Editor.RemoveEmoji(name);
        }
    }

    private async void OnExportEmojisClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("emojis.json 내보내기", "emojis.json", new FilePickerFileType("emojis.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                _session.ExportEmojis(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"이모지 내보내기 실패: {exception.Message}");
        }
    }
}
