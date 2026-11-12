using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Mt.App.Views;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Avalonia.VisualTree;

namespace Mt.App;

// 전역 단축키와 그것이 부르는 시간 · 키 명령(이전·다음 키, 프레임 넘기기, 현재 시각의 키 복사·붙여넣기·지우기, 인비트윈) · 되돌리기 · 매니퓰레이터 고르기.
public partial class MainWindow
{
    // ── 전역 단축키 (마야 타임 슬라이더 관습) ───────────────────────────
    //
    // 1 / 2 이전·다음 키 · Alt+1 / Alt+2 이전·다음 프레임 · ` 첫 프레임 · Alt+` 처음부터 재생 · Alt+Q 재생 토글
    // Shift+W / E / F 현재 시각의 키 복사·붙여넣기·지우기 · Shift+1 / 2 인비트윈 빼기·넣기
    // 창 어디에 포커스가 있든 듣는다(터널). 글 입력 칸에서는 안 듣는다.

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Handled)
        {
            return;
        }

        KeyModifiers mods = e.KeyModifiers;
        bool none = mods == KeyModifiers.None;
        bool alt = mods == KeyModifiers.Alt;
        bool shift = mods == KeyModifiers.Shift;

        switch (e.PhysicalKey)
        {
            case PhysicalKey.Digit1 when none:
                PreviousKey();
                break;
            case PhysicalKey.Digit2 when none:
                NextKey();
                break;
            case PhysicalKey.Digit1 when alt:
                StepFrame(-1);
                break;
            case PhysicalKey.Digit2 when alt:
                StepFrame(+1);
                break;
            case PhysicalKey.Q when alt:
                _viewport.TogglePlay();
                break;
            case PhysicalKey.F1 when none:
                OpenShortcuts();
                break;
            case PhysicalKey.Backquote when alt:
                _viewport.PlayFromStart();
                break;
            case PhysicalKey.Backquote when none:
                _viewport.Stop();
                break;
            case PhysicalKey.W when shift:
                Guard("키 복사", CopyKeysAtCurrentTime);
                break;
            case PhysicalKey.E when shift:
                Guard("키 붙여넣기", PasteKeysAtCurrentTime);
                break;
            case PhysicalKey.F when shift:
                Guard("키 지우기", ClearKeysAtCurrentTime);
                break;
            case PhysicalKey.Digit1 when shift:
                Guard("인비트윈 빼기", RemoveInbetween);
                break;
            case PhysicalKey.Digit2 when shift:
                Guard("인비트윈 넣기", AddInbetween);
                break;
            // 파일 — 저장 · 다른 이름으로 · 열기 · 새로(툴바의 단추와 같은 길).
            case PhysicalKey.S when mods == KeyModifiers.Control:
                OnSaveClick(this, new RoutedEventArgs());
                break;
            case PhysicalKey.S when mods == (KeyModifiers.Control | KeyModifiers.Shift):
                OnSaveAsClick(this, new RoutedEventArgs());
                break;
            case PhysicalKey.O when mods == KeyModifiers.Control:
                OnOpenClick(this, new RoutedEventArgs());
                break;
            case PhysicalKey.N when mods == KeyModifiers.Control:
                NewDocument();
                break;
            // 되돌리기·다시하기 — 창 어디에 포커스가 있든 한 경로(툴바 버튼도 같다). 글 입력 칸은 위에서 빠졌으니 거기서는 글자 되돌리기다.
            case PhysicalKey.Z when mods == (KeyModifiers.Control | KeyModifiers.Shift):
            case PhysicalKey.Y when mods == KeyModifiers.Control:
                RedoCommand();
                break;
            case PhysicalKey.Z when mods == KeyModifiers.Control:
                UndoCommand();
                break;
            // 키 복사·잘라내기·붙여넣기 — 창 어디에 포커스가 있든(그래프·도프 시트의 선택 키). 붙이는 커브는 그래프가 정한다(고른 커브 › 보이는 커브 › 원래 커브).
            case PhysicalKey.C when mods == KeyModifiers.Control:
                _graph.CopySelection();
                break;
            case PhysicalKey.X when mods == KeyModifiers.Control:
                _graph.CutSelection();
                break;
            case PhysicalKey.V when mods == KeyModifiers.Control:
                _graph.Paste();
                break;
            // 매니퓰레이터(마야): Q 선택 · W 이동 · E 회전 · R 배율 — 창 어디에 포커스가 있든. 그래프 안에서는 그래프 도구(Q/W/R)가 먼저다.
            case PhysicalKey.Q when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Select);
                break;
            // S = Set Key(마야): 키 없는 포즈가 있으면 그것을, 없으면 고른 컨트롤러의 지금 값을 현재 시각에.
            case PhysicalKey.S when none:
                SetKeySelected();
                break;
            case PhysicalKey.W when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Move);
                break;
            case PhysicalKey.E when none:
                SetManipulator(ManipulatorMode.Rotate);
                break;
            case PhysicalKey.R when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Scale);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private bool InsideGraph(object? source)
        => source is Visual v && (ReferenceEquals(v, _graph) || v.FindAncestorOfType<GraphEditorView>() is not null);

    /// <summary>되돌리기 — Ctrl+Z와 툴바의 한 경로. 뷰포트가 포즈를 든 채 스크럽 중이면 그것부터 놓는다.</summary>
    private void UndoCommand()
    {
        _viewport.EndHoldScrub();
        Guard("되돌리기", _session.Editor.Undo);
    }

    private void RedoCommand()
    {
        _viewport.EndHoldScrub();
        Guard("다시하기", _session.Editor.Redo);
    }

    /// <summary>테스트 손잡이 — 하이어아키 트리(포커스를 두고 단축키를 보내는 데).</summary>
    internal Control Hierarchy => _hierarchy;

    /// <summary>테스트 손잡이 — 그래프 툴바의 값 칸.</summary>
    internal TextBox KeyValueBox => _keyValue;

    /// <summary>테스트 손잡이 — 하단 바의 범위 시작 칸(항상 보인다 — 글 입력 중 단축키 경계 검사).</summary>
    internal TextBox RangeStartBox => _rangeStart;

    private void SetManipulator(ManipulatorMode mode)
    {
        _viewport.Manipulator = mode;
        RetargetManipulator(mode);
        _viewport.InvalidateVisual();
    }

    /// <summary>
    /// W/E/R = 표의 기본 손잡이(몸·숙임·크기 — <c>manipulator</c> 표시). 지금 노드의 컨트롤러가 그 종류를 받으면(자리 X·Y를 고른 채 W)
    /// 그 노드 그대로, 다른 종류면 기본 손잡이의 노드로 옮긴다. 표에 없는 노드(리그 노드 보기의 리거)와 Q는 건드리지 않는다.
    /// </summary>
    private void RetargetManipulator(ManipulatorMode mode)
    {
        if (mode == ManipulatorMode.Select || _session.ControllersOf(_viewport.SelectedObject?.Rig) is not { } table)
        {
            return;
        }

        ChannelKind kind = mode switch
        {
            ManipulatorMode.Rotate => ChannelKind.Euler,
            ManipulatorMode.Scale => ChannelKind.Scale,
            _ => ChannelKind.Position,
        };
        Controller? current = _viewport.SelectedNode is { } node ? table.OfNode(node) : null;

        if ((current is null && _viewport.SelectedNode is not null) || current?.Kind == kind || table.DefaultFor(kind) is not { } fallback)
        {
            return;
        }

        _followSelection = false;
        _viewport.SelectedNode = ControllerNodeFor(_graph.Motion, fallback);
        RefreshChannelBox();
        RefreshChannels();
    }

    /// <summary>테스트 손잡이.</summary>
    internal ManipulatorMode Manipulator => _viewport.Manipulator;

    /// <summary>한 프레임(24fps) — 뷰포트 ←/→와 같은 규약.</summary>
    internal static float FrameSeconds => (float)(1.0 / TimeSnap.FramesPerSecond);

    /// <summary>재생 표시 콤보의 항목 — 항목마다 툴팁. 차례 = 연속(0) · 프레임 맞춤(1) = ToolSettings.PlayWholeFrames.</summary>
    private static ComboBoxItem PlayStepItem(string text, string tip)
    {
        var item = new ComboBoxItem { Content = text, FontSize = 11 };
        ToolTip.SetTip(item, tip);
        return item;
    }

    internal void PreviousKey() => _viewport.JumpToPreviousKey();

    internal void NextKey() => _viewport.JumpToNextKey();

    internal void StepFrame(int frames)
    {
        _viewport.Pause();
        _viewport.ScrubTo(_viewport.Time + frames * (double)FrameSeconds);
    }

    /// <summary>마야 timeSliderCopyKey — 현재 시각의 키 전부(모든 커브)를 클립보드에.</summary>
    internal void CopyKeysAtCurrentTime()
    {
        if (_graph.Motion is not { } motion)
        {
            return;
        }

        List<KeyRef> keys = TimeSliderOps.KeysAt(motion, _viewport.Time);
        KeyClipboard? clipboard = KeyClipboard.Copy(motion, keys);

        if (clipboard is null || clipboard.Count == 0)
        {
            _session.SetStatus("현재 시각에 키가 없다.");
            return;
        }

        _clipboard.Current = clipboard;
        _session.SetStatus($"키 {clipboard.Count}개 복사 ({_viewport.Time:0.###}s).");
    }

    /// <summary>마야 timeSliderPasteKey — 클립보드를 현재 시각에(같은 시각은 덮어씀, 없는 채널은 만든다).</summary>
    internal void PasteKeysAtCurrentTime()
    {
        if (_graph.MotionName is not { } name)
        {
            return;
        }

        if (_clipboard.Current is not { } clipboard || clipboard.Count == 0)
        {
            _session.SetStatus("클립보드에 키가 없다.");
            return;
        }

        float at = (float)_viewport.Time;
        Func<float, float>? snap = _settings.SnapEnabled ? t => (float)TimeSnap.Snap(t, _settings.SnapStep) : null;
        List<KeyRef> pasted = _session.Editor.PasteKeys(name, clipboard, snap?.Invoke(at) ?? at, snap);
        _selection.Set(pasted);
    }

    /// <summary>마야 timeSliderClearKey — 현재 시각의 키를 모든 커브에서 지운다.</summary>
    internal void ClearKeysAtCurrentTime()
    {
        if (_graph.MotionName is { } name && !_session.Editor.RemoveKeysAt(name, _viewport.Time))
        {
            _session.SetStatus("현재 시각에 키가 없다.");
        }
    }

    /// <summary>마야 removeInbetween — 현재 시각 뒤의 키를 한 프레임 당긴다.</summary>
    internal void RemoveInbetween()
    {
        if (_graph.MotionName is { } name && !_session.Editor.ShiftKeysAfter(name, (float)_viewport.Time, -FrameSeconds))
        {
            _session.SetStatus("현재 시각 뒤에 키가 없다.");
        }
    }

    /// <summary>마야 addInbetween — 현재 시각 뒤의 키를 한 프레임 민다.</summary>
    internal void AddInbetween()
    {
        if (_graph.MotionName is { } name && !_session.Editor.ShiftKeysAfter(name, (float)_viewport.Time, FrameSeconds))
        {
            _session.SetStatus("현재 시각 뒤에 키가 없다.");
        }
    }
}
