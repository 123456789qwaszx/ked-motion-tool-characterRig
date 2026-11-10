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

    private readonly ListBox _emojiList = new() { MinHeight = 60 };
    private readonly TextBox _emojiName = new() { FontSize = 11 };
    private readonly ComboBox _emojiSkin = new() { FontSize = 11 };
    private readonly ComboBox _emojiEnter = new() { FontSize = 11 };
    private readonly ComboBox _emojiHold = new() { FontSize = 11 };
    private readonly ComboBox _emojiExit = new() { FontSize = 11 };
    private readonly TextBox _emojiHoldSeconds = new() { FontSize = 11, PlaceholderText = "끌 때까지 머문다" };
    private readonly TextBox _emojiOffsetX = new() { FontSize = 11 };
    private readonly TextBox _emojiOffsetY = new() { FontSize = 11 };
    private readonly TextBox _emojiLocator = new() { FontSize = 11, PlaceholderText = "(런타임 기본)" };
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
        _emojiFields.Children.Add(Labeled("등장", _emojiEnter));
        _emojiFields.Children.Add(Labeled("머묾(되풀이)", _emojiHold));
        _emojiFields.Children.Add(Labeled("퇴장", _emojiExit));
        _emojiFields.Children.Add(Labeled("머무는 시간", _emojiHoldSeconds));
        _emojiFields.Children.Add(Labeled("오프셋 X", _emojiOffsetX));
        _emojiFields.Children.Add(Labeled("오프셋 Y", _emojiOffsetY));
        _emojiFields.Children.Add(Labeled("붙는 자리", _emojiLocator));
        panel.Children.Add(_emojiFields);

        ToolTip.SetTip(_emojiName, "대본이 부르는 이름 — 빈칸 없는 글(한글도 된다)");
        ToolTip.SetTip(_emojiEnter, "뜰 때 한 번 도는 모션(튀어나옴). 유지(commit)면 그 자세에서 머묾이 시작한다");
        ToolTip.SetTip(_emojiHold, "머무는 동안 처음부터 되풀이하는 모션(까딱임·punch). 등장이 끝난 자세에서 시작하고 바퀴가 쌓이지 않는다");
        ToolTip.SetTip(_emojiExit, "사라질 때 한 번 도는 모션(흐려짐). 머묾이 멈춘 그 자세에서 시작한다");
        ToolTip.SetTip(_emojiHoldSeconds, "등장이 끝난 뒤 퇴장이 시작하기까지(초). 비우면 끌 때까지 머무는 이모지(상태) — 미리보기는 머묾을 얼마간만 보인다");
        ToolTip.SetTip(_emojiOffsetX, "붙는 자리에서의 오프셋(px) — Emoji_Anchor의 위치로 들어간다. 이모지마다 머리 옆·위·입 옆이 다르다");
        ToolTip.SetTip(_emojiLocator, "런타임의 기준점 이름(faceAura·face…) — 캐릭터마다 다른 머리 자리는 런타임이 푼다. 툴의 미리보기는 오프셋만 쓴다");

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
        Combo(_emojiEnter, (e, v) => e.Enter = v);
        Combo(_emojiHold, (e, v) => e.Hold = v);
        Combo(_emojiExit, (e, v) => e.Exit = v);
        OnCommit(_emojiHoldSeconds, text => WithEmoji(e => e.HoldSeconds = string.IsNullOrWhiteSpace(text) ? null : TryFloat(text, out float v) ? v : e.HoldSeconds));
        OnCommit(_emojiOffsetX, text => WithEmoji(e => e.OffsetX = TryFloat(text, out float v) ? v : e.OffsetX));
        OnCommit(_emojiOffsetY, text => WithEmoji(e => e.OffsetY = TryFloat(text, out float v) ? v : e.OffsetY));
        OnCommit(_emojiLocator, text => WithEmoji(e => e.Locator = text));
        return panel;
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

        RefreshAssets(); // 정의의 스킨으로 갈아 끼운 조각 그림을 오브젝트의 것으로 되돌린다
        RefreshEmojiInspector();
    }

    /// <summary>뷰포트가 이모지를 묶은 뒤 — 정의의 스킨 그림을 연기할 오브젝트에 싣는다(오브젝트가 입은 스킨 대신).</summary>
    private void LoadEmojiImages()
    {
        if (_viewport.ShownEmoji is not { } emoji || _viewport.EmojiActor is not { } actor)
        {
            return;
        }

        foreach ((string node, string? path) in _session.PartImagesOfSkin(emoji.Skin))
        {
            Bitmap? image = null;

            try
            {
                image = path is null ? null : new Bitmap(path);
            }
            catch (Exception exception)
            {
                _session.SetStatus($"이모지 {emoji.Name}의 조각 {node} 그림을 읽지 못했다: {exception.Message}");
            }

            _viewport.SetPartImage(actor.Id, node, image);
        }
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

        LoadEmojiImages();
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
        string? rig = selected is not null && RigParts.Of(_session.Rig, selected.Rig).Count > 0
            ? selected.Rig
            : _session.Rig?.RigKinds.FirstOrDefault(k => RigParts.Of(_session.Rig, k).Count > 0);

        if (rig is null)
        {
            throw new InvalidOperationException("조각이 있는 리그(이모지 리그)가 덤프에 없다 — 런타임이 이모지 리그를 덤프에 넣어야 한다.");
        }

        string name = "emoji";

        for (int n = 2; editor.Document.FindEmoji(name) is not null; n++)
        {
            name = $"emoji_{n}";
        }

        string? skin = selected is not null && selected.Rig == rig ? selected.Skin : null;

        editor.Batch(MotionChangeKind.Scene, () =>
        {
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
