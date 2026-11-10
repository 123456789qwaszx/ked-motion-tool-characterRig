using System.Globalization;
using Avalonia.Controls;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App;

// 카메라 모션의 셸 — 카메라(zoom·pan)를 키와 커브로 움직이는 모션(CameraRig).
// 만드는 버튼(왼쪽 "카메라 모션 추가")과, 오른쪽 "모션" 탭의 카메라 값 칸(지금 시각의 값 — 넣으면 그 시각에 키).
// 커브는 그래프 에디터가 캐릭터 모션과 똑같이 다룬다.
public partial class MainWindow
{
    private readonly StackPanel _cameraPanel = new() { Spacing = 4, IsVisible = false };
    private readonly TextBox _cameraZoom = new() { FontSize = 11 };
    private readonly TextBox _cameraPanX = new() { FontSize = 11 };
    private readonly TextBox _cameraPanY = new() { FontSize = 11 };

    /// <summary>테스트 손잡이 — "카메라 모션 추가".</summary>
    internal string AddCameraMotionFromForm() => AddCameraMotion();

    /// <summary>테스트 손잡이 — 모션 탭의 카메라 값 칸(zoom · pan x · pan y)과 보이는가.</summary>
    internal (TextBox Zoom, TextBox PanX, TextBox PanY, bool Visible) CameraBoxes => (_cameraZoom, _cameraPanX, _cameraPanY, _cameraPanel.IsVisible);

    private Control BuildCameraPanel()
    {
        _cameraPanel.Children.Add(Header("카메라 (지금 시각)"));
        _cameraPanel.Children.Add(Labeled("zoom", _cameraZoom));
        _cameraPanel.Children.Add(Labeled("pan X", _cameraPanX));
        _cameraPanel.Children.Add(Labeled("pan Y", _cameraPanY));
        const string tip = "지금 시각의 카메라 값. 값을 넣고 Enter = 그 시각에 키(되돌리기 한 번) — 커브는 아래 그래프에서 다듬는다. zoom 1당 배율 0.05, pan은 px. 재생 기준이 상대값이면 씬 카메라와의 차가 키 값이 된다";
        ToolTip.SetTip(_cameraZoom, tip);
        ToolTip.SetTip(_cameraPanX, tip);
        ToolTip.SetTip(_cameraPanY, tip);

        OnCommit(_cameraZoom, text => CommitCamera(CameraRig.ZoomNode, Axis.X, text));
        OnCommit(_cameraPanX, text => CommitCamera(CameraRig.PanNode, Axis.X, text));
        OnCommit(_cameraPanY, text => CommitCamera(CameraRig.PanNode, Axis.Y, text));
        return _cameraPanel;
    }

    private void CommitCamera(string node, Axis axis, string text)
    {
        if (_refreshing || !TryFloat(text, out float value))
        {
            return;
        }

        Guard("카메라 키", () => _viewport.SetCameraValue(node, axis, value));
        RefreshCameraPanel();
    }

    /// <summary>카메라 모션을 고른 동안만 보이고, 시각·문서가 바뀔 때마다 그 시각의 값을 다시 쓴다(글을 치는 중인 칸은 건드리지 않는다).</summary>
    private void RefreshCameraPanel()
    {
        RefreshAlphaPanel(); // 같은 때(시각·문서·선택이 바뀔 때)에 같이 다시 읽는다
        _refreshDepth++;

        try
        {
            CameraSettings? camera = _viewport.CameraValue();
            _cameraPanel.IsVisible = camera is not null;

            if (camera is null)
            {
                return;
            }

            Write(_cameraZoom, camera.Zoom);
            Write(_cameraPanX, camera.PanX);
            Write(_cameraPanY, camera.PanY);
        }
        finally
        {
            _refreshDepth--;
        }

        static void Write(TextBox box, float value)
        {
            if (!box.IsFocused)
            {
                box.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// 카메라 모션을 만든다 — 이름 camera, camera_2…, 길이 1초, 유지(commit)·절대값(키 = 카메라 값 그대로).
    /// zoom·pan x·pan y에 씬의 지금 카메라 값으로 처음·끝 키를 둔다(커브가 바로 선다) — 전부 되돌리기 한 번.
    /// </summary>
    private string AddCameraMotion()
    {
        MotionEditor editor = _session.Editor;
        string name = MotionNames.Unique(editor.Document, CameraRig.RigKind);
        CameraSettings camera = _viewport.Scene?.Camera ?? new CameraSettings();
        const float duration = 1f;

        editor.Batch(MotionChangeKind.Structure, () =>
        {
            editor.AddMotion(name, CameraRig.RigKind, duration, MotionEnd.Commit, MotionChannelMode.Absolute);
            editor.AddChannel(name, CameraRig.ZoomNode, MotionChannelKind.Position);
            editor.SetKeys(name, CameraRig.ZoomNode, MotionChannelKind.Position, Axis.X, Flat(camera.Zoom));
            editor.AddChannel(name, CameraRig.PanNode, MotionChannelKind.Position);
            editor.SetKeys(name, CameraRig.PanNode, MotionChannelKind.Position, Axis.X, Flat(camera.PanX));
            editor.SetKeys(name, CameraRig.PanNode, MotionChannelKind.Position, Axis.Y, Flat(camera.PanY));
        });

        _motionList.SelectedItem = name;
        return name;

        KeyAsset[] Flat(float value) => new[]
        {
            new KeyAsset { T = 0f, V = value, Tangent = _viewport.DefaultTangent },
            new KeyAsset { T = duration, V = value, Tangent = _viewport.DefaultTangent },
        };
    }
}

// 불투명도 값 칸(PLAN-parts.md P4) — "모션" 탭, 불투명도를 받을 수 있는 노드를 골랐을 때만. 값을 넣으면 그 시각에 알파 채널의 키.
public partial class MainWindow
{
    private readonly StackPanel _alphaPanel = new() { Spacing = 4, IsVisible = false };
    private readonly TextBox _alphaBox = new() { FontSize = 11 };
    private readonly TextBlock _alphaNode = new() { FontSize = 10, Opacity = 0.7 };

    /// <summary>테스트 손잡이 — 불투명도 칸과 보이는가.</summary>
    internal (TextBox Box, bool Visible) AlphaBox => (_alphaBox, _alphaPanel.IsVisible);

    private Control BuildAlphaPanel()
    {
        _alphaPanel.Children.Add(Header("불투명도 (지금 시각)"));
        _alphaPanel.Children.Add(_alphaNode);
        _alphaPanel.Children.Add(Labeled("alpha", _alphaBox));
        ToolTip.SetTip(_alphaBox, "고른 노드의 불투명도(0~1) — 값을 넣고 Enter = 그 시각에 알파 채널의 키(되돌리기 한 번). 커브는 그래프에서. 부모 노드의 불투명도와 곱해져 그려진다. 덤프에 CanvasGroup이 있는 노드만 받는다. 알파 채널이 든 모션은 motions/3으로 나간다");
        OnCommit(_alphaBox, text =>
        {
            if (!_refreshing && _viewport.SelectedNode is { } node && TryFloat(text, out float value))
            {
                Guard("불투명도 키", () => _viewport.SetAlphaValue(node, value));
                RefreshAlphaPanel();
            }
        });
        return _alphaPanel;
    }

    private void RefreshAlphaPanel()
    {
        _refreshDepth++;

        try
        {
            float? alpha = _viewport.AlphaValue(_viewport.SelectedNode);
            _alphaPanel.IsVisible = alpha is not null;

            if (alpha is null)
            {
                return;
            }

            _alphaNode.Text = _viewport.SelectedNode;

            if (!_alphaBox.IsFocused)
            {
                _alphaBox.Text = alpha.Value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }
}
