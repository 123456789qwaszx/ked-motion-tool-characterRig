using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mt.App;

// 문서의 얼굴 — 창 제목(문서의 이름 · 고쳤다는 표시)과, 저장하지 않은 것을 버리기 전에 묻는 일(닫기 · 새로 · 열기).
public partial class MainWindow
{
    private const string AppTitle = "ked-motion-tool-characterRig";

    private Window? _unsavedWindow;
    private Action? _afterUnsaved;
    private bool _closeConfirmed;

    /// <summary>
    /// 저장하지 않은 것을 버리기 전에 묻는가. 사람이 여는 창은 묻는다. 테스트가 여는 창은 기본으로 묻지 않는다 —
    /// 고친 채로 닫는 테스트가 대부분이라서다(묻는 것 자체는 켜고 따로 본다).
    /// </summary>
    internal bool AsksBeforeDiscard { get; set; }

    /// <summary>창 제목 — "문서 이름 • — 앱 이름". •는 저장한 뒤 고친 것이 있다는 표시.</summary>
    private void RefreshTitle()
    {
        string name = _session.DocumentPath is { } path ? Path.GetFileName(path) : "새 문서";
        Title = $"{name}{(_session.IsDirty ? " •" : "")} — {AppTitle}";
    }

    /// <summary>
    /// 고친 것이 없으면 바로 하고, 있으면 먼저 묻는다(저장하고 · 저장하지 않고 · 그만두기).
    /// 따로 뜨는 작은 창이다 — 본 창을 막지 않으니, 답하기 전에 다른 일을 하면 물음은 거둔다.
    /// </summary>
    private void ConfirmUnsaved(string what, Action proceed)
    {
        if (!AsksBeforeDiscard || !_session.IsDirty)
        {
            proceed();
            return;
        }

        CloseUnsavedPrompt();
        _afterUnsaved = proceed;

        var save = new Button { Content = "저장하고 계속", MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        var discard = new Button { Content = "저장하지 않고 계속", MinWidth = 130, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "그만두기", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        save.Click += (_, _) => AnswerUnsaved(UnsavedAnswer.Save);
        discard.Click += (_, _) => AnswerUnsaved(UnsavedAnswer.Discard);
        cancel.Click += (_, _) => AnswerUnsaved(UnsavedAnswer.Cancel);

        string name = _session.DocumentPath is { } path ? Path.GetFileName(path) : "새 문서";
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = $"'{name}'에 저장하지 않은 것이 있다.", FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{what} — 저장할까?", Opacity = 0.85, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, discard, cancel } });

        var window = new Window
        {
            Title = "저장하지 않은 것이 있다",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_unsavedWindow, window))
            {
                _unsavedWindow = null;
                _afterUnsaved = null;
            }
        };
        _unsavedWindow = window;
        window.Show(this);
    }

    private enum UnsavedAnswer
    {
        Save,
        Discard,
        Cancel,
    }

    private async void AnswerUnsaved(UnsavedAnswer answer)
    {
        Action? proceed = _afterUnsaved;
        CloseUnsavedPrompt();

        try
        {
            if (answer == UnsavedAnswer.Cancel || proceed is null)
            {
                _session.SetStatus("그만두었다 — 문서는 그대로다.");
                return;
            }

            if (answer == UnsavedAnswer.Save)
            {
                if (_session.DocumentPath is null)
                {
                    await SaveAsAsync();
                }
                else
                {
                    _session.Save();
                }

                // 저장할 곳을 고르다 그만뒀으면 하던 일도 그만둔다.
                if (_session.IsDirty)
                {
                    _session.SetStatus("저장하지 않았다 — 하던 일을 그만두었다.");
                    return;
                }
            }

            proceed();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private void CloseUnsavedPrompt()
    {
        Window? window = _unsavedWindow;
        _unsavedWindow = null;
        _afterUnsaved = null;
        window?.Close();
    }

    /// <summary>창을 닫을 때 — 고친 것이 있으면 닫기를 멈추고 묻는다. 답이 "계속"이면 다시 닫는다.</summary>
    private bool HoldCloseForUnsaved()
    {
        if (_closeConfirmed || !AsksBeforeDiscard || !_session.IsDirty)
        {
            return false;
        }

        ConfirmUnsaved("창을 닫는다", () =>
        {
            _closeConfirmed = true;
            Close();
        });
        return true;
    }

    private void NewDocument() => ConfirmUnsaved("새 문서를 연다", () => Guard("새 문서", _session.New));

    private void OnNewClick(object? sender, RoutedEventArgs e) => NewDocument();

    // ── 테스트 손잡이 ──────────────────────────────────────────────────

    internal bool UnsavedPromptOpen => _unsavedWindow is not null;

    internal void NewDocumentFromForm() => NewDocument();

    internal void OpenDocumentAskingFromForm(string path) => ConfirmUnsaved("다른 문서를 연다", () => Guard("열기", () => OpenDocument(path)));

    /// <summary>물음에 답한다 — "save" · "discard" · "cancel".</summary>
    internal void AnswerUnsavedFromForm(string answer) => AnswerUnsaved(answer switch
    {
        "save" => UnsavedAnswer.Save,
        "discard" => UnsavedAnswer.Discard,
        _ => UnsavedAnswer.Cancel,
    });
}
