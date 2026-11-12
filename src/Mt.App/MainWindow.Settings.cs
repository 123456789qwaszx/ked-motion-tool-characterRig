using Avalonia.Controls;
using Avalonia.Media;
using Mt.Authoring.Graph;
using Mt.Authoring.Settings;

namespace Mt.App;

// 설정 — 파일에서 읽은 값을 화면에 입히고(창 배치 포함) 다시 적는다. 설정 창 · 단축키 창(F1) · 최근 문서 메뉴.
public partial class MainWindow
{
    /// <summary>설정 → 화면·뷰. 설정 파일에서 읽은 값을 한 번에 입힌다.</summary>
    /// <summary>창 배치(패널 폭·뷰포트/그래프 비율)를 설정에서 입힌다. 스플리터가 바꾼 값은 저장할 때 읽는다.</summary>
    private void ApplyLayout()
    {
        Shell.ColumnDefinitions[0].Width = new GridLength(_settings.LeftPanelWidth);
        Shell.ColumnDefinitions[4].Width = new GridLength(_settings.RightPanelWidth);
        Shell.ColumnDefinitions[0].MinWidth = ToolSettings.MinPanelWidth;
        Shell.ColumnDefinitions[4].MinWidth = ToolSettings.MinPanelWidth;
        Shell.ColumnDefinitions[2].MinWidth = 600; // 트랜스포트·레인지 줄이 겹치지 않는 폭

        if (_centerGrid is { } center)
        {
            center.RowDefinitions[0].Height = new GridLength(_settings.ViewportRowStar, GridUnitType.Star);
            center.RowDefinitions[2].Height = new GridLength(_settings.GraphRowStar, GridUnitType.Star);
        }
    }

    private void CaptureLayout()
    {
        _settings.LeftPanelWidth = Shell.ColumnDefinitions[0].Width.Value;
        _settings.RightPanelWidth = Shell.ColumnDefinitions[4].Width.Value;

        if (_centerGrid is { } center && center.RowDefinitions[0].Height.IsStar && center.RowDefinitions[2].Height.IsStar)
        {
            _settings.ViewportRowStar = center.RowDefinitions[0].Height.Value;
            _settings.GraphRowStar = center.RowDefinitions[2].Height.Value;
        }
    }

    private void ApplySettings()
    {
        _refreshDepth++;

        try
        {
            _snap.IsChecked = _settings.SnapEnabled;
            int snapIndex = Array.FindIndex(SnapSteps, v => Math.Abs(v - _settings.SnapStep) < 1e-9);
            _snapStepCombo.SelectedIndex = snapIndex < 0 ? 0 : snapIndex;
            int tangentIndex = Array.IndexOf(NewKeyTangentChoices, _settings.DefaultTangent);
            _newKeyTangent.SelectedIndex = tangentIndex < 0 ? 0 : tangentIndex;
            _autoKeyToggle.IsChecked = _settings.AutoKey;
            _autoKeyToggle.Foreground = _settings.AutoKey ? AutoKeyOnBrush : null;
            _showStageSkeleton.IsChecked = _settings.ShowStageSkeleton;
            _collapsedGroups.Clear();
            _collapsedGroups.UnionWith(_settings.CollapsedChannelBoxGroups);
            ApplyWorkMode(_settings.WorkMode);
            int autoSaveIndex = Array.IndexOf(AutoSaveMinutes, _settings.AutoSaveMinutes);
            _autoSaveCombo.SelectedIndex = autoSaveIndex < 0 ? 2 : autoSaveIndex;
            _timeDisplayCombo.SelectedIndex = (int)_settings.TimeDisplay;
            _playStep.SelectedIndex = _settings.PlayWholeFrames ? 1 : 0;
            _showRigNodes.IsChecked = _settings.ShowRigNodes;
        }
        finally
        {
            _refreshDepth--;
        }

        PushSettingsToViews();
        RefreshRecentMenu();
    }

    private void PushSettingsToViews()
    {
        _graph.SnapEnabled = _settings.SnapEnabled;
        _dope.SnapEnabled = _settings.SnapEnabled;
        _viewport.SnapEnabled = _settings.SnapEnabled;
        _graph.SnapStep = _settings.SnapStep;
        _dope.SnapStep = _settings.SnapStep;
        _viewport.SnapStep = _settings.SnapStep;
        _viewport.DefaultTangent = _settings.DefaultTangent;
        _viewport.AutoKey = _settings.AutoKey;
        _viewport.PlayWholeFrames = _settings.PlayWholeFrames;
        _graph.TimeDisplay = _settings.TimeDisplay;
        _dope.TimeDisplay = _settings.TimeDisplay;
        _timeSlider.SnapEnabled = _settings.SnapEnabled;
        _timeSlider.SnapStep = _settings.SnapStep;
        _timeSlider.TimeDisplay = _settings.TimeDisplay;
        _timeline.SnapEnabled = _settings.SnapEnabled;
        _timeline.SnapStep = _settings.SnapStep;
        _timeline.TimeDisplay = _settings.TimeDisplay;
        _timeline.InvalidateVisual();
        _rangeSlider.TimeDisplay = _settings.TimeDisplay;
        _timeSlider.InvalidateVisual();
        _rangeSlider.InvalidateVisual();
        _graph.InvalidateVisual();
        _dope.InvalidateVisual();
        RefreshTimeTexts();
        _autoSaveTimer.Stop();

        if (_settings.AutoSaveMinutes > 0)
        {
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(_settings.AutoSaveMinutes);
            _autoSaveTimer.Start();
        }

        _viewport.InvalidateVisual();
    }

    private void SaveSettings()
    {
        try
        {
            CaptureLayout();
            _settings.Save(_settingsPath);
        }
        catch (Exception exception)
        {
            _session.SetStatus($"설정을 저장하지 못했다: {exception.Message}");
        }
    }

    private void RefreshRecentMenu()
    {
        var flyout = new MenuFlyout();

        foreach (string path in _settings.RecentFiles)
        {
            var item = new MenuItem { Header = path, FontSize = 11 };
            string captured = path;
            item.Click += (_, _) => Guard("최근 파일", () => OpenDocument(captured));
            flyout.Items.Add(item);
        }

        if (_settings.RecentFiles.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "(없음)", IsEnabled = false, FontSize = 11 });
        }

        _recentButton.Flyout = flyout;
    }

    /// <summary>설정 창의 컨트롤들은 미리 묶어 두고(창을 열 때마다 같은 인스턴스), 바꾸는 즉시 적용한다.</summary>
    private void WireSettingsControls()
    {
        _showRigNodes.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            _settings.ShowRigNodes = _showRigNodes.IsChecked == true;
            RebuildHierarchy();
            PushSettingsToViews();
        };
        ToolTip.SetTip(_showRigNodes, "하이어아키의 오브젝트 밑에 컨트롤러(자리·깊이·연기) 대신 덤프의 리그 노드 26단을 보인다 — 리거용. 표가 없는 리그는 늘 노드다");
        _showStageSkeleton.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            _settings.ShowStageSkeleton = _showStageSkeleton.IsChecked == true;
            RebuildHierarchy();
        };
        ToolTip.SetTip(_showStageSkeleton, "하이어아키에 PresentataionUIRoot 뼈대 전체를 보인다. 끄면 Scene › 무대 › 층 › 오브젝트만");
        _snap.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            _settings.SnapEnabled = _snap.IsChecked == true;
            PushSettingsToViews();
        };
        _snapStepCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _snapStepCombo.SelectedIndex >= 0)
            {
                _settings.SnapStep = SnapSteps[_snapStepCombo.SelectedIndex];
                PushSettingsToViews();
            }
        };
        _autoSaveCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _autoSaveCombo.SelectedIndex >= 0)
            {
                _settings.AutoSaveMinutes = AutoSaveMinutes[_autoSaveCombo.SelectedIndex];
                PushSettingsToViews();
            }
        };
        _newKeyTangent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _settings.DefaultTangent = DefaultTangent;
                PushSettingsToViews();
            }
        };
        OnCommit(_currentTime, text => { if (TimeText.TryParse(text, Display, out double v)) _graph.CurrentTime = v; });
        _timeDisplayCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _timeDisplayCombo.SelectedIndex >= 0)
            {
                _settings.TimeDisplay = (TimeDisplay)_timeDisplayCombo.SelectedIndex;
                PushSettingsToViews();
            }
        };
    }

    /// <summary>설정 창(마야 Preferences) — 툴바 "설정…". 바꾸는 즉시 적용, 닫을 때 저장. 컨트롤은 같은 인스턴스를 붙였다 뗀다.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8 };
        _settingsRows.Clear();

        void Row(string label, Control control)
        {
            var row = (Panel)Labeled(label, control);
            _settingsRows.Add(row);
            panel.Children.Add(row);
        }

        panel.Children.Add(Header("하이어아키"));
        panel.Children.Add(_showRigNodes);
        panel.Children.Add(_showStageSkeleton);
        panel.Children.Add(Header("키"));
        ToolTip.SetTip(_snapStepCombo, "키 배치·이동 및 타임라인 탐색 시, 시간을 지정한 간격의 배수에 맞춥니다.");
        Row("시간 스냅 간격", _snapStepCombo);
        Row("새 키 탄젠트", _newKeyTangent);
        panel.Children.Add(Header("표시 · 저장"));
        Row("시간 표시", _timeDisplayCombo);
        Row("자동 저장", _autoSaveCombo);
        panel.Children.Add(new TextBlock { Text = "바꾸는 즉시 적용되고, 창을 닫을 때 설정 파일에 남는다.", FontSize = 10, Opacity = 0.6, Margin = new Avalonia.Thickness(0, 6, 0, 0) });

        _settingsWindow = new Window { Title = "설정", Width = 400, Height = 380, CanResize = false, Content = panel };
        _settingsWindow.Closed += (_, _) =>
        {
            foreach (Panel row in _settingsRows)
            {
                row.Children.Clear();
            }

            panel.Children.Clear();
            _settingsWindow = null;
            SaveSettings();
        };
        _settingsWindow.Show();
    }

    /// <summary>단축키 창 — 툴바 "단축키" · F1. 읽기만.</summary>
    private void OpenShortcuts()
    {
        if (_shortcutsWindow is not null)
        {
            _shortcutsWindow.Activate();
            return;
        }

        var text = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = ShortcutsText };
        _shortcutsWindow = new Window
        {
            Title = "단축키",
            Width = 560,
            Height = 620,
            Content = new ScrollViewer { Content = text, Padding = new Avalonia.Thickness(14) },
        };
        _shortcutsWindow.Closed += (_, _) => _shortcutsWindow = null;
        _shortcutsWindow.Show();
    }

    internal const string ShortcutsText =
        "전역 (창 어디서나 · 글 입력 칸 제외)\n" +
        "  1 / 2  이전·다음 키        Alt+1 / Alt+2  이전·다음 프레임\n" +
        "  `  첫 프레임               Alt+`  처음부터 재생        Alt+Q  재생/일시정지\n" +
        "  S  키 놓기 — 키 없는 포즈가 있으면 그것을, 없으면 고른 컨트롤러의 지금 값을 현재 시각에\n" +
        "  Esc  키 없는 포즈 버리기\n" +
        "  Q / W / E / R  뷰포트 선택·이동·회전·배율 (W 몸 · E 숙임 · R 크기 — 컨트롤러 표의 기본 손잡이)\n" +
        "  Shift+W / E / F  현재 시각의 키 복사·붙여넣기·지우기      Shift+1 / 2  인비트윈 빼기·넣기\n" +
        "  Ctrl+C / X / V · Del  키 복사·잘라내기·붙여넣기·삭제      Ctrl+Z / Y  되돌리기·다시 하기\n" +
        "  F1  이 창\n\n" +
        "뷰포트\n" +
        "  휠  커서 축으로 확대·축소      MMB 드래그  보기 이동      F  보기 맞춤(이모지를 보면 이모지에)\n" +
        "  LMB  오브젝트 고르기 · 기즈모 끌기(Auto Key면 놓는 순간 키)      Home / End  처음·끝      ← / →  한 프레임\n\n" +
        "그래프 에디터 (마야)\n" +
        "  LMB  키 선택 · 빈 자리 드래그 박스 선택(키가 들면 키만, 커브만 지나면 커브) · Ctrl+드래그 라소\n" +
        "  MMB 드래그  선택 키 이동(핸들을 골랐으면 핸들 회전)      MMB 클릭  고른 커브에 키 끼우기\n" +
        "  RMB 드래그  선택 키 둘레면 한 축만 이동, 빈 자리면 팬      Alt+MMB  팬      Alt+RMB  줌 드래그      휠  줌\n" +
        "  더블클릭  커브 위에 키      I  현재 시각에 키      A / F  전체 · 선택 맞춤\n" +
        "  Q / W / R  선택·이동·스케일 툴(그래프 안)      Ctrl+1…6  탄젠트 auto·flat·linear·free·spline·plateau\n" +
        "  Ctrl+B / Ctrl+Shift+B  버퍼 커브 찍기·바꾸기      툴바 step  계단 키(이모지의 타임라인만 — 그림 바꾸기는 늘 계단)\n\n" +
        "도프 시트\n" +
        "  블록 드래그  시간 이동      Alt+드래그  복제      범위 띠 양끝  시간 스케일      더블클릭  키      노드 행 클릭  접기\n\n" +
        "타임라인 (연출 클립 — 왼쪽 '클립'에서 고른다)\n" +
        "  LMB  블록 선택(아래 그래프가 그 모션을 묶는다 — 클립 안에서 바로 고친다) · 빈 곳/눈금자 스크럽\n" +
        "  블록 드래그(LMB·MMB)  이동(가로 시각 · 세로 트랙)      더블클릭  그 모션만 열기(클립을 떠난다)\n" +
        "  우클릭  모션 놓기 · 상대값/절대값 · 배역 끄기 · 새 모션으로 굽기 · 트랙 추가/삭제      Ctrl+D  이어서 한 번 더      Del  삭제\n" +
        "  줄 머리 🔊  그 배역 끄기/켜기(미리보기에서만 빠진다)      M  고른 블록 하나만 끄기/켜기(파일에 남는다)\n" +
        "  블록을 고르고 그 블록 안의 시각이면 뷰포트 끌기·채널 박스·S가 그 블록의 모션에 키를 놓는다      아래 경계를 끌면 타임라인 높이\n\n" +
        "이모지 (툴바의 '이모지' 모드 — 그래프 · 도프 시트 · 뷰포트가 그 타임라인을 본다)\n" +
        "  '이모지 추가'  새 이모지의 첫 화면 — 그림(PNG 끌어다 놓기) · 어떻게 뜨는가 · 움직임 → 만들기(한 칸) · '빈 이모지로 시작'\n" +
        "  툴바 '트윈머신'  따로 뜨는 창 — 앞 키 ↔ 뒤 키 사이의 값을 지금 프레임에(슬라이더: 끄는 동안 보이고 놓으면 한 칸 · 단추 · 오버슈트)\n" +
        "  오른쪽 '키 놓기'  자리 · 크기 · 회전 · 투명도 · 그림 — 고른 그림들(없으면 이모지 전체)의 지금 값을 지금 프레임에      그래프 '종류별 칸'  종류마다 제 눈금\n" +
        "  그림을 고르면 그래프는 그 그림의 것만 · 왼쪽 목록에서 이모지를 누르면 대상이 이모지 전체(머리에서의 자리를 끈다)\n" +
        "  Q / W / E / R  선택 · 이동 · 회전 · 크기(뷰포트 오른쪽 위의 띠와 같다)      그림 누르기  고르기 — 빈 곳은 맞지 않는다 · 겹친 곳을 다시 누르면 밑의 것 · 빈 데를 누르면 전부 풀린다(이모지 전체는 왼쪽 목록에서 이모지를 누른다)\n" +
        "  Shift/Ctrl+누르기  더하기 · 빼기      빈 데서 끌기  상자로 고르기      여럿을 고르면 함께 끌리고 S가 전부를 키한다(한 칸)\n" +
        "  여럿 + 회전 E · 크기 R  고른 것들의 가운데 점을 축으로 무리째 돌고 벌어진다(자리도 옮겨 간다 — Auto Key면 자리의 키도)\n" +
        "  매니퓰레이터 끌기  Auto Key 꺼짐 = 기본값(그림의 자리 · 크기 · 회전, 이모지의 머리에서의 자리 — 키가 아니다) · 켜짐 = 지금 프레임에 키      S  지금 값을 키(도구의 종류만, 선택 도구면 셋 다)\n" +
        "  F  이모지에 맞춤      도구 띠 '인물' · '◀ 왼쪽 보기'(뒤집힌 인물에게 뜨는 모습 — 보기만, 켠 동안 끌기 없음) · '인물과 함께'\n" +
        "  수명의 띠(뷰포트 밑)  끝 끌기 = 시작 · 끝 · 몸통 끌기 = 통째로(놓을 때 한 칸) · 오른쪽 단추 = 풀기 · 지우기 · '+ 등장/머묾/퇴장' · '게임처럼 재생' · '움직임 가져오기'(여섯 움직임의 키를 고른 그림의 그 자리에 — 한 칸)\n" +
        "  그래프 목록 '키 없음' ＋  지금 값에 키(이모지 전체 · 그림마다 자리 · 크기 · 회전 · 투명도 · 그림)\n\n" +
        "타임 슬라이더\n" +
        "  클릭·드래그  스크럽      Shift+드래그  프레임 범위 선택      범위 안 MMB  범위의 키 이동      범위 밖 MMB  포즈 들고 스크럽(S로 키)\n" +
        "  우클릭  복사·붙여넣기·삭제·스냅 · 재생 범위 · 재생 모드 메뉴\n\n" +
        "하이어아키\n" +
        "  클릭  오브젝트·컨트롤러 고르기      우클릭  이름·복제·삭제·숨기기·레이어·만들기 메뉴\n" +
        "  F2  이름 바꾸기      Ctrl+D  복제      Del  삭제      Ctrl+Shift+N  캐릭터 리그 만들기";

    /// <summary>테스트 손잡이.</summary>
    internal int InspectorTabCount => _inspectorTabs.ItemCount;

    internal int InspectorTab
    {
        get => _inspectorTabs.SelectedIndex;
        set => _inspectorTabs.SelectedIndex = value;
    }

    internal Window? SettingsWindow => _settingsWindow;

    internal Window? ShortcutsWindow => _shortcutsWindow;

    internal void OpenSettingsFromMenu() => OpenSettings();

    internal void OpenShortcutsFromMenu() => OpenShortcuts();
}
