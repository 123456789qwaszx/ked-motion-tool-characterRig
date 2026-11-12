using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mt.Authoring;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App;

// 파일 — 열기 · 저장 · 다른 이름으로 · 리그 덤프 싣기 · motions.json 가져오기 · 내보내기와 파일 고르는 창.
public partial class MainWindow
{
    internal void OpenDocument(string path)
    {
        _session.Open(path);
        _settings.Touch(path);
        RefreshRecentMenu();
        SaveSettings();
    }

    // ── 파일 ──────────────────────────────────────────────────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("저작 원본 열기", new FilePickerFileType("모션 저작 원본") { Patterns = new[] { "*" + AuthoringFormat.AuthoringExtension, "*.json" } });

            if (path is not null)
            {
                OpenDocument(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"열기 실패: {exception.Message}");
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.DocumentPath is null)
            {
                await SaveAsAsync();
                return;
            }

            _session.Save();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsAsync();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private async Task SaveAsAsync()
    {
        string? path = await PickSaveAsync("저작 원본 저장", "motions" + AuthoringFormat.AuthoringExtension,
            new FilePickerFileType("모션 저작 원본") { Patterns = new[] { "*" + AuthoringFormat.AuthoringExtension } });

        if (path is not null)
        {
            _session.Save(path);
            _settings.Touch(path);
            RefreshRecentMenu();
            SaveSettings();
        }
    }

    private async void OnLoadRigClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("리그 덤프 (rig-schemas.json)", new FilePickerFileType("리그 덤프") { Patterns = new[] { "rig-schemas.json", "*.json" } });

            if (path is not null)
            {
                _session.LoadRig(path);
                RefreshChannelBox();
                RefreshProblems();
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"리그 덤프 실패: {exception.Message}");
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("motions.json 가져오기", new FilePickerFileType("motions.json") { Patterns = new[] { "motions.json", "*.json" } });

            if (path is not null)
            {
                _session.ImportMotions(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"가져오기 실패: {exception.Message}");
        }
    }

    /// <summary>툴바 범위 콤보 → 모션 거르기. 오브젝트가 없으면 전체.</summary>
    internal (Func<MotionAsset, bool>? Filter, string? Label) ExportFilter()
    {
        SceneObject? o = _viewport.SelectedObject;

        if (o is null || _session.Rig is not { } rig)
        {
            return (null, null);
        }

        return _exportScope.SelectedIndex switch
        {
            1 => (m => m.Rig == o.Rig, $"rigKind {o.Rig}"),
            2 => (m => ScenePreview.CanWear(rig, o, m), $"{o.Id}가 입는 모션"),
            _ => (null, null),
        };
    }

    internal int ExportScopeIndex
    {
        get => _exportScope.SelectedIndex;
        set => _exportScope.SelectedIndex = value;
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("motions.json 내보내기", "motions.json", new FilePickerFileType("motions.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                (Func<MotionAsset, bool>? filter, string? label) = ExportFilter();
                _session.Export(path, filter, label);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"내보내기 실패: {exception.Message}");
        }
    }

    private async Task<string?> PickOpenAsync(string title, FilePickerFileType type)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanOpen)
        {
            _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없다.");
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { type },
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSaveAsync(string title, string suggested, FilePickerFileType type)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanSave)
        {
            _session.SetStatus("이 환경에서는 저장 창을 열 수 없다.");
            return null;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            FileTypeChoices = new[] { type },
        });

        return file?.Path.LocalPath;
    }
}
