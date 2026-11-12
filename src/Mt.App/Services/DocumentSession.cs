using Mt.Authoring;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;
using Mt.Authoring.Schema;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;
using Ked.Presentation.Core;

namespace Mt.App.Services;

/// <summary>
/// 열린 문서 하나의 살림 — 경로·편집기·리그 덤프·저장 여부. 화면은 이것을 통해서만 파일에 닿는다.
/// 파일 선택 창은 화면의 일이고, 여기는 경로를 받아 읽고 쓴다(헤드리스 테스트가 그대로 쓴다).
/// </summary>
public sealed class DocumentSession
{
    private long _savedRevision;
    private bool _autoSaveMine;

    public DocumentSession()
    {
        Editor = new MotionEditor(new MotionDocument());
        Editor.Changed += OnEditorChanged;
        UseBuiltinTuning();
        _assetFingerprint = AssetFingerprintOf(Editor.Document);
    }

    // ── 문서 ↔ 실은 자산 ──────────────────────────────────────────────
    //
    // 문서가 적은 자산 참조(리그 덤프 경로·기준 해상도 경로·오브젝트별 초상)는 되돌리기·다시하기로도 바뀐다. 그때 실은 리그·깊이·focus·컨트롤러 표·
    // 무대 크기와 뷰포트의 초상 비트맵이 문서와 어긋나지 않도록, 참조 지문을 두고 달라진 때만 다시 싣고 AssetsChanged를 올린다.
    // 키 편집(Keys·Metadata)은 보지도 않고, 참조가 같으면 파일을 읽지 않는다.

    private string _assetFingerprint = "";
    private bool _syncingAssets;

    /// <summary>테스트 손잡이 — 문서 참조를 따라 자산을 다시 실은 횟수.</summary>
    internal int AssetSyncCount { get; private set; }

    private static string AssetFingerprintOf(MotionDocument document)
        => "rig=" + (document.RigSchemas?.Path ?? "") + "\nbase=" + (document.BaseResolution?.Path ?? "") + "\n"
           + string.Join("\n", document.Scenes.SelectMany(scene => scene.Objects).Select(o => $"portrait {o.Id}={o.Portrait ?? ""}"))
           + "\nemoji images=" + string.Join("|", document.Emojis.SelectMany(e => e.Rigs).SelectMany(r => r.Images).Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal));

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        if (_syncingAssets || e.Kind is MotionChangeKind.Keys or MotionChangeKind.Metadata)
        {
            return;
        }

        SyncAssetsToDocument();
    }

    private void SyncAssetsToDocument()
    {
        string fingerprint = AssetFingerprintOf(Editor.Document);

        if (fingerprint == _assetFingerprint)
        {
            return;
        }

        string[] before = _assetFingerprint.Split('\n');
        string[] after = fingerprint.Split('\n');
        bool rigChanged = before.Length == 0 || before[0] != after[0];
        bool baseChanged = before.Length < 2 || before[1] != after[1];
        _assetFingerprint = fingerprint;

        if (rigChanged)
        {
            UseBuiltinTuning();      // 참조가 사라졌으면 내장으로
            TryLoadRigFromDocument(); // 있으면 그 덤프와 옆의 깊이·focus·컨트롤러 표
        }

        if (rigChanged || baseChanged)
        {
            if (!rigChanged)
            {
                StageSize = BaseResolutionFile.Default;
            }

            TryLoadAssetsFromDocument();
        }

        AssetSyncCount++;
        AssetsChanged?.Invoke(this, EventArgs.Empty); // 초상은 뷰포트가 문서 참조대로 다시 싣는다
    }

    /// <summary>내장 기본값으로 — 덤프 파일이 없는 문서의 시작 상태.</summary>
    private void UseBuiltinTuning()
    {
        Controllers = BuiltinTuning.Controllers;
        Rig = BuiltinTuning.RigSchemas;
        RigPath = null;
        Depth = BuiltinTuning.Depth;
        Focus = BuiltinTuning.Focus;
        EmojiLocators = EmojiLocatorFile.Empty;
        StageSize = BuiltinTuning.BaseResolution;
    }

    public MotionEditor Editor { get; private set; }

    public string? DocumentPath { get; private set; }

    /// <summary>리그 덤프 — 파일을 싣지 않았으면 내장 기본값(<see cref="BuiltinTuning"/>). null이 아니다.</summary>
    public RigSchemaFile? Rig
    {
        get => _dumpRig;
        private set => _dumpRig = value;
    }

    private RigSchemaFile? _dumpRig;

    /// <summary>실은 덤프 파일의 경로. null이면 내장 리그다.</summary>
    public string? RigPath { get; private set; }

    public bool IsBuiltinRig => RigPath is null;

    /// <summary>덤프 옆 presets/depth.json. 없으면 레이어는 그리는 순서만 정한다.</summary>
    public DepthTuningFile? Depth { get; private set; }

    /// <summary>덤프 옆 presets/focus.json — 배역별 focus 오프셋. 없으면 base 오프셋 없이(0).</summary>
    public FocusTuningFile? Focus { get; private set; }

    /// <summary>덤프 옆 presets/emoji-locator.json — 캐릭터별 머리의 자리(이모지의 로케이터). 보기에만 쓴다. 없으면 빈 표(기본 자리).</summary>
    public EmojiLocatorFile EmojiLocators { get; private set; } = EmojiLocatorFile.Empty;

    /// <summary>컨트롤러 표 — 덤프 옆 <c>presets/controllers.json</c>, 없으면 내장본. 리그에 없는 노드를 가리키면 상태 줄에 알린다.</summary>
    public ControllersFile Controllers { get; private set; } = BuiltinTuning.Controllers;

    /// <summary>선택 리그의 컨트롤러 표. 표에 없는 리그는 null — 노드로 다룬다.</summary>
    public ControllerRig? ControllersOf(string? rigKind) => Controllers.Rig(rigKind);

    public string Status { get; private set; } = "";

    /// <summary>미리보기 초상 PNG의 절대 경로. 문서에는 문서 기준 상대 경로로 적힌다.</summary>
    /// <summary>오브젝트의 초상 파일(절대 경로). 문서에 없거나 파일이 없으면 null.</summary>
    public string? PortraitPathOf(SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(o);

        if (string.IsNullOrEmpty(o.Portrait))
        {
            return null;
        }

        string resolved = ResolveFromDocument(o.Portrait);
        return File.Exists(resolved) ? resolved : null;
    }

    /// <summary>기준 해상도(무대 크기). base-resolution.json이 없으면 1920×1080.</summary>
    public Vec2 StageSize { get; private set; } = BaseResolutionFile.Default;

    public bool IsDirty => Editor.Revision != _savedRevision;

    /// <summary>문서·편집기가 통째로 갈렸다(새로/열기). 화면은 편집기를 다시 묶는다.</summary>
    public event EventHandler? DocumentReplaced;

    public event EventHandler? StatusChanged;

    /// <summary>리그 덤프·초상·기준 해상도가 바뀌었다. 뷰포트가 다시 싣는다.</summary>
    public event EventHandler? AssetsChanged;

    /// <summary>검증에 쓰는 rigKind → 노드 표 — 덤프의 것.</summary>
    public IReadOnlyDictionary<string, ICollection<string>>? RigNodeIds => Rig?.NodeIds;

    public IReadOnlyList<MotionProblem> Problems => MotionValidator.Validate(Editor.Document, RigNodeIds, Controllers);

    /// <summary>이모지 리그의 틀 — 실은 덤프의 것, 덤프에 없으면 null(검증이 알린다).</summary>
    public EmojiRigFrame? EmojiFrame => EmojiRigFrame.TryRead(Rig ?? BuiltinTuning.RigSchemas, out EmojiRigFrame frame, out _) ? frame : null;

    /// <summary>이모지의 문제 — 저장된 문서면 그림 파일이 그 경로에 없는 것도 알린다.</summary>
    public IReadOnlyList<EmojiProblem> EmojiProblems
    {
        get
        {
            IReadOnlyList<EmojiProblem> problems = EmojiValidator.Validate(Editor.Document, Rig ?? BuiltinTuning.RigSchemas);

            return DocumentPath is null
                ? problems
                : problems.Concat(EmojiValidator.MissingImages(Editor.Document, Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".")).ToList();
        }
    }

    /// <summary>문서 기준 경로 → 절대 경로(그림 파일을 읽을 때). 파일이 없으면 null.</summary>
    public string? ExistingFileOf(string documentPath)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        string resolved = ResolveFromDocument(documentPath);
        return File.Exists(resolved) ? resolved : null;
    }

    /// <summary>절대 경로 → 문서 기준 경로(문서가 아직 저장되지 않았으면 절대 경로). 문서에 적는 글자다.</summary>
    public string DocumentRelative(string path) => RelativeToDocument(path);

    /// <summary>
    /// <c>emojis.json</c>(<c>emojis/2</c>)을 쓴다 — 막는 문제가 0건이고 런타임 로더가 다시 읽을 때만. 정규 바이트(UTF-8 BOM 없음 · LF).
    /// </summary>
    public bool ExportEmojis(string path)
    {
        if (EmojiFrame is not { } frame)
        {
            SetStatus("이모지 내보내기 막힘 — 리그 덤프에 이모지 리그(emoji)가 없다.");
            return false;
        }

        if (!EmojiExport.TryExport(Editor.Document, frame, out string json, out IReadOnlyList<EmojiProblem> problems))
        {
            SetStatus($"이모지 내보내기 막힘 — 문제 {problems.Count}건: {string.Join(" ", problems.Take(3))}");
            return false;
        }

        File.WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(json));
        SetStatus($"내보냈다: {path} — 이모지 {Editor.Document.Emojis.Count}개");
        return true;
    }

    public void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 스키마가 달라진 것(<c>docs/PLAN-characterRig.md</c> §3의 2) — 문서를 지을 때의 스키마와 지금 실린 덤프를 견준다.
    /// 문서에 지문이 없거나 덤프가 없으면 달라진 것이 없다.
    /// </summary>
    public SchemaDiff SchemaChange
        => Editor.Document.BuiltOn is { } builtOn && _dumpRig is not null ? SchemaDiff.Compare(builtOn, SchemaDiff.PrintOf(_dumpRig)) : SchemaDiff.None;

    /// <summary>이름이 바뀐 것으로 보이는 노드를 따라간다 — 문서가 옛 이름으로 가리키던 곳을 새 이름으로(한 칸). 바꾼 곳의 수.</summary>
    public int FollowSchemaRenames()
    {
        int changed = Editor.FollowSchemaRenames(SchemaChange);
        SetStatus(changed == 0 ? "이름 바꾸기를 따라간다: 바꿀 곳이 없다." : $"이름이 바뀐 노드를 따라갔다: {changed}곳.");
        return changed;
    }

    /// <summary>지금 실린 스키마를 문서의 스키마로 적는다 — 달라진 것을 다 보았다는 뜻이다(한 칸).</summary>
    public void AdoptSchema()
    {
        if (_dumpRig is null)
        {
            return;
        }

        Editor.SetBuiltOn(SchemaDiff.PrintOf(_dumpRig));
        SetStatus("지금의 스키마를 문서에 적었다.");
    }

    private void RecordSchemaIfMissing()
    {
        if (Editor.Document.BuiltOn is null && _dumpRig is not null)
        {
            Editor.SetBuiltOn(SchemaDiff.PrintOf(_dumpRig));
        }
    }

    public void New()
    {
        Replace(new MotionDocument(), null);
        SetStatus("새 문서.");
    }

    /// <summary>
    /// 연 파일에 있던 모르는 키(오타일 수 있다 — 읽지 않았다). 그 파일을 연 동안만 든다: 저장하면 파일에서 사라지므로 비운다.
    /// </summary>
    public IReadOnlyList<string> UnknownKeys { get; private set; } = Array.Empty<string>();

    public void Open(string path)
    {
        Load(path, path);
        SchemaDiff change = SchemaChange;
        SetStatus(change.IsEmpty
            ? $"열었다: {path}"
            : $"열었다: {path} — 문서를 지을 때와 스키마가 다르다(문제 줄에 무엇이 달라졌는지 나온다).");
    }

    /// <summary><paramref name="file"/>을 읽어 <paramref name="documentPath"/>의 문서로 든다(자동 저장본을 되살릴 때 둘이 다르다).</summary>
    private void Load(string file, string documentPath)
    {
        string json = File.ReadAllText(file);
        MotionDocument document = MotionDocumentJson.Read(json);
        Replace(document, documentPath);
        UnknownKeys = MotionDocumentJson.UnknownKeys(json);
        TryLoadRigFromDocument();
        TryLoadAssetsFromDocument();
        _assetFingerprint = AssetFingerprintOf(Editor.Document);
        AssetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 연 문서보다 새로운 자동 저장본 — 지난번에 저장하지 못하고 끝났다는 뜻이다. 없거나 더 오래됐으면 null.
    /// </summary>
    public string? NewerAutoSave()
    {
        if (DocumentPath is null || !File.Exists(DocumentPath))
        {
            return null;
        }

        string path = AutoSavePathFor(DocumentPath);
        return File.Exists(path) && File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(DocumentPath) ? path : null;
    }

    /// <summary>자동 저장본을 되살린다 — 문서의 자리는 그대로고, 저장하지 않은 것이 있는 상태가 된다. 저장하면 자동 저장본은 지운다.</summary>
    public void RecoverAutoSave()
    {
        string documentPath = DocumentPath ?? throw new InvalidOperationException("되살릴 문서가 없다.");
        string path = AutoSavePathFor(documentPath);
        Load(path, documentPath);
        _savedRevision = -1;
        _autoSaveMine = true;
        SetStatus($"자동 저장본을 되살렸다: {Path.GetFileName(path)} — 저장해야 문서에 남는다.");
    }

    /// <summary>자동 저장본을 버린다(파일을 지운다) — 사람이 그러라고 했을 때만.</summary>
    public void DiscardAutoSave()
    {
        if (DocumentPath is null)
        {
            return;
        }

        string path = AutoSavePathFor(DocumentPath);
        File.Delete(path);
        _autoSaveMine = false;
        SetStatus($"자동 저장본을 지웠다: {Path.GetFileName(path)}");
    }

    /// <summary>자동 저장 — 문서 옆 <c>*.autosave.motion-authoring.json</c>. 저장 여부(IsDirty)는 건드리지 않는다. 경로가 없으면 안 한다.
    /// 저장하면 지우고, 저장하지 못하고 끝났으면 남아서 다음에 열 때 되살릴지 묻는다(<see cref="NewerAutoSave"/>).</summary>
    public string? AutoSave()
    {
        if (DocumentPath is null || !IsDirty)
        {
            return null;
        }

        string path = AutoSavePathFor(DocumentPath);
        File.WriteAllText(path, MotionDocumentJson.Write(Editor.Document));
        _autoSaveMine = true;
        SetStatus($"자동 저장: {Path.GetFileName(path)}");
        return path;
    }

    public static string AutoSavePathFor(string documentPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
        string name = Path.GetFileName(documentPath);
        string stem = name.EndsWith(AuthoringFormat.AuthoringExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^AuthoringFormat.AuthoringExtension.Length]
            : Path.GetFileNameWithoutExtension(name);
        return Path.Combine(directory, stem + ".autosave" + AuthoringFormat.AuthoringExtension);
    }

    public void Save(string? path = null)
    {
        path ??= DocumentPath ?? throw new InvalidOperationException("저장할 경로가 없다 — 다른 이름으로 저장을 쓴다.");
        RecordSchemaIfMissing();
        File.WriteAllText(path, MotionDocumentJson.Write(Editor.Document));

        // 이 세션이 쓴(또는 되살린) 자동 저장본은 저장하면 할 일을 다했다. 전부터 있던 것은 건드리지 않는다.
        if (_autoSaveMine && DocumentPath is not null)
        {
            File.Delete(AutoSavePathFor(DocumentPath));
            _autoSaveMine = false;
        }

        DocumentPath = path;
        _savedRevision = Editor.Revision;
        UnknownKeys = Array.Empty<string>();
        SetStatus($"저장했다: {path}");
    }

    /// <summary>리그 덤프를 싣고 문서에 경로(문서 기준 상대)와 해시를 적는다.</summary>
    public void LoadRig(string path)
    {
        RigSchemaFile rig = RigSchemaFile.Load(path);
        _syncingAssets = true; // 여기서 직접 싣는다 — 문서 참조 변화에 다시 반응하지 않는다

        try
        {
            LoadRigCore(path, rig);
        }
        finally
        {
            _syncingAssets = false;
            _assetFingerprint = AssetFingerprintOf(Editor.Document);
        }
    }

    private void LoadRigCore(string path, RigSchemaFile rig)
    {
        Rig = rig;
        RigPath = path;
        string? baseResolution = BaseResolutionFile.TryFindBeside(path);

        // 덤프 참조와 기준 해상도 참조는 한 조작 — 되돌리기 한 칸.
        Editor.Batch(MotionChangeKind.Document, () =>
        {
            Editor.SetRigSchemas(RelativeToDocument(path), rig.Sha256);
            RecordSchemaIfMissing(); // 이미 적힌 지문은 두고 — 다른 덤프를 실었으면 달라진 것이 보여야 한다

            if (baseResolution is not null)
            {
                Editor.SetBaseResolution(RelativeToDocument(baseResolution));
            }
        });

        // 덤프 옆의 base-resolution.json·presets/depth.json을 같이 싣는다 — ExportedTuning 폴더를 통째로 복사하는 관례. 옆에 없으면 내장 기본값.
        string? depthPath = DepthTuningFile.TryFindBeside(path);
        Depth = BuiltinTuning.Depth;

        if (depthPath is not null)
        {
            try
            {
                Depth = DepthTuningFile.Load(depthPath);
            }
            catch (Exception exception)
            {
                SetStatus($"depth.json을 읽지 못했다: {exception.Message} — 깊이는 그리는 순서만 정한다.");
            }
        }

        string? focusPath = FocusTuningFile.TryFindBeside(path);
        Focus = BuiltinTuning.Focus;

        if (focusPath is not null)
        {
            try
            {
                Focus = FocusTuningFile.Load(focusPath);
            }
            catch (Exception exception)
            {
                SetStatus($"focus.json을 읽지 못했다: {exception.Message} — 배역 오프셋 없이.");
            }
        }

        if (baseResolution is not null)
        {
            StageSize = BaseResolutionFile.Load(baseResolution);
        }

        LoadEmojiLocatorsBeside(path);

        string? controllersPath = LoadControllersBeside(path);

        AssetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus($"리그 덤프: {string.Join(", ", rig.RigKinds)} ({Path.GetFileName(path)})"
                  + (baseResolution is null ? " — base-resolution.json이 옆에 없어 1920×1080으로 둔다" : $" · 무대 {StageSize.X:0}×{StageSize.Y:0}")
                  + (depthPath is null ? " · depth.json 없음(내장 깊이 커브)" : " · 깊이 커브")
                  + (focusPath is null ? " · focus 없음(내장 표)" : " · focus 표")
                  + (controllersPath is null ? " · 컨트롤러 내장 표" : " · 컨트롤러 표"));
    }

    /// <summary>덤프 옆 이모지 로케이터 표를 싣는다(없거나 못 읽으면 빈 표 — 기본 자리). 보기에만 쓰는 표라 못 읽어도 일을 막지 않는다.</summary>
    private void LoadEmojiLocatorsBeside(string rigSchemasPath)
    {
        EmojiLocators = EmojiLocatorFile.Empty;

        if (EmojiLocatorFile.TryFindBeside(rigSchemasPath) is not { } locatorPath)
        {
            return;
        }

        try
        {
            EmojiLocators = EmojiLocatorFile.Load(locatorPath);
        }
        catch (Exception exception)
        {
            SetStatus($"emoji-locator.json을 읽지 못했다: {exception.Message} — 이모지는 기본 머리 자리에 보인다.");
        }
    }

    /// <summary>덤프 옆 컨트롤러 표를 싣는다(없으면 내장본). 리그에 없는 노드를 가리키면 상태 줄에 알리고 그대로 쓴다. 찾은 경로를 돌려준다.</summary>
    private string? LoadControllersBeside(string rigSchemasPath)
    {
        string? controllersPath = ControllersFile.TryFindBeside(rigSchemasPath);
        Controllers = BuiltinTuning.Controllers;

        if (controllersPath is not null)
        {
            try
            {
                Controllers = ControllersFile.Load(controllersPath);
            }
            catch (Exception exception)
            {
                SetStatus($"controllers.json을 읽지 못했다: {exception.Message} — 내장 컨트롤러 표로 둔다.");
                controllersPath = null;
            }
        }

        if (Rig is { } rig)
        {
            foreach (string rigKind in Controllers.RigKinds)
            {
                if (rig.NodeIds.TryGetValue(rigKind, out ICollection<string>? ids) && Controllers.TryGetRig(rigKind, out ControllerRig table))
                {
                    IReadOnlyList<string> problems = table.Validate(ids);

                    if (problems.Count > 0)
                    {
                        SetStatus($"컨트롤러 표가 리그와 어긋난다: {string.Join(" · ", problems)}");
                    }
                }
            }
        }

        return controllersPath;
    }

    /// <summary>오브젝트의 미리보기 초상. 문서에 문서 기준 상대 경로로 적힌다 — 산출물과는 무관하다.</summary>
    public void SetPortrait(string objectId, string? path)
    {
        if (path is not null && !File.Exists(path))
        {
            throw new FileNotFoundException("초상 파일이 없다.", path);
        }

        _syncingAssets = true;

        try
        {
            Editor.SetObjectPortrait(null, objectId, path is null ? null : RelativeToDocument(path));
        }
        finally
        {
            _syncingAssets = false;
            _assetFingerprint = AssetFingerprintOf(Editor.Document);
        }

        AssetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(path is null ? $"{objectId}: 초상을 뺐다." : $"{objectId}: 초상 {Path.GetFileName(path)}");
    }

    /// <summary><c>motions/1</c>을 읽어 문서로 올린다. 이름이 겹치면 바꿔 넣는다.</summary>
    public IReadOnlyList<string> ImportMotions(string path)
    {
        if (!MotionsJson.TryRead(File.ReadAllText(path), out List<MotionClip> clips, out List<string> problems))
        {
            SetStatus($"가져오기 실패: {string.Join(" ", problems)}");
            return Array.Empty<string>();
        }

        IReadOnlyList<string> imported = Editor.ImportClips(clips, replace: true);
        SetStatus(problems.Count == 0
            ? $"가져왔다: {string.Join(", ", imported)}"
            : $"가져왔다: {string.Join(", ", imported)} — 건너뜀 {problems.Count}건: {string.Join(" ", problems)}");
        return imported;
    }

    /// <summary>문제가 0건일 때만 쓴다. 정규 바이트(UTF-8 BOM 없음·LF)로 — Unity 내보내기와 같은 바이트다.</summary>
    /// <param name="filter">내보낼 모션(null이면 전부). <paramref name="scopeLabel"/>은 상태 줄 글자.</param>
    public bool Export(string path, Func<MotionAsset, bool>? filter = null, string? scopeLabel = null)
    {
        if (!MotionExport.TryExport(Editor.Document, RigNodeIds, out string json, out IReadOnlyList<MotionProblem> problems, filter))
        {
            SetStatus($"내보내기 막힘 — 문제 {problems.Count}건을 먼저 고친다.");
            return false;
        }

        File.WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(json));
        int count = Editor.Document.Motions.Count(m => filter is null || filter(m));
        SetStatus($"내보냈다: {path} — 모션 {count}개{(scopeLabel is null ? "" : $" ({scopeLabel})")}");
        return true;
    }

    private void Replace(MotionDocument document, string? path)
    {
        UnknownKeys = Array.Empty<string>();
        _autoSaveMine = false;
        Editor.Changed -= OnEditorChanged;
        Editor = new MotionEditor(document);
        Editor.Changed += OnEditorChanged;
        DocumentPath = path;
        _savedRevision = Editor.Revision;
        UseBuiltinTuning();
        _assetFingerprint = AssetFingerprintOf(Editor.Document);
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>문서가 적어 둔 초상·기준 해상도를 찾아 싣는다. 없는 파일은 상태 줄에 남기고 넘어간다.</summary>
    private void TryLoadAssetsFromDocument()
    {
        MotionDocument document = Editor.Document;

        if (document.BaseResolution is { Path: { Length: > 0 } basePath })
        {
            string resolved = ResolveFromDocument(basePath);

            if (File.Exists(resolved))
            {
                StageSize = BaseResolutionFile.Load(resolved);
            }
            else
            {
                SetStatus($"기준 해상도 파일을 찾지 못했다: {resolved} — 1920×1080으로 둔다.");
            }
        }

        foreach (SceneAsset scene in document.Scenes)
        {
            foreach (SceneObject o in scene.Objects)
            {
                if (!string.IsNullOrEmpty(o.Portrait) && !File.Exists(ResolveFromDocument(o.Portrait)))
                {
                    SetStatus($"{o.Id}의 초상 파일을 찾지 못했다: {ResolveFromDocument(o.Portrait)} — 자리표시로 보인다.");
                }
            }
        }
    }

    /// <summary>문서가 적어 둔 리그 덤프를 찾아 싣는다. 해시가 다르면 알린다 — 조용히 맞는 척하지 않는다.</summary>
    private void TryLoadRigFromDocument()
    {
        RigSchemasReference? reference = Editor.Document.RigSchemas;

        if (reference is null || string.IsNullOrEmpty(reference.Path))
        {
            return;
        }

        string resolved = ResolveFromDocument(reference.Path);

        if (!File.Exists(resolved))
        {
            SetStatus($"리그 덤프를 찾지 못했다: {resolved} — 내장 리그({BuiltinTuning.Source})로 연다.");
            return;
        }

        RigSchemaFile rig = RigSchemaFile.Load(resolved);
        Rig = rig;
        RigPath = resolved;

        try
        {
            Depth = DepthTuningFile.TryFindBeside(resolved) is { } depthPath ? DepthTuningFile.Load(depthPath) : BuiltinTuning.Depth;
            Focus = FocusTuningFile.TryFindBeside(resolved) is { } focusPath ? FocusTuningFile.Load(focusPath) : BuiltinTuning.Focus;
            LoadControllersBeside(resolved);
        }
        catch (Exception exception)
        {
            SetStatus($"덤프 옆 프리셋을 읽지 못했다: {exception.Message} — 내장 프리셋으로 둔다.");
            Depth = BuiltinTuning.Depth;
            Focus = BuiltinTuning.Focus;
        }

        LoadEmojiLocatorsBeside(resolved);

        if (!string.IsNullOrEmpty(reference.Sha256) && !string.Equals(reference.Sha256, rig.Sha256, StringComparison.Ordinal))
        {
            IReadOnlyList<MotionProblem> problems = Problems;
            SetStatus($"리그가 바뀌었다 ({Path.GetFileName(resolved)}) — 모든 채널의 노드를 다시 검증했다: 문제 {problems.Count}건.");
        }
    }

    private string RelativeToDocument(string path)
    {
        string full = Path.GetFullPath(path);

        if (DocumentPath is null)
        {
            return full.Replace('\\', '/');
        }

        string documentDirectory = Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".";
        return Path.GetRelativePath(documentDirectory, full).Replace('\\', '/');
    }

    private string ResolveFromDocument(string path)
    {
        if (Path.IsPathRooted(path) || DocumentPath is null)
        {
            return Path.GetFullPath(path);
        }

        string documentDirectory = Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".";
        return Path.GetFullPath(Path.Combine(documentDirectory, path));
    }
}
