using System.Text.Json;
using System.Text.Json.Serialization;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Settings;

/// <summary>
/// 작업 모드 — 화면이 그 일에 쓰는 것만 보인다(docs/history/PLAN-ux.md U1).
/// </summary>
public enum WorkMode
{
    /// <summary>캐릭터 모션 · 클립 · 씬.</summary>
    Motion,

    /// <summary>이모지.</summary>
    Emoji,
}

/// <summary>
/// 툴 설정 — 문서가 아니라 이 컴퓨터의 것(마야 preferences). JSON 한 파일, 없으면 기본값.
/// 읽기 실패는 조용히 기본값으로 떨어지되 <see cref="LoadProblem"/>에 남긴다.
/// </summary>
public sealed class ToolSettings
{
    public const string FileName = "settings.json";
    public const double MinPanelWidth = 160;
    public const int MaxRecentFiles = 8;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public bool SnapEnabled { get; set; } = true;

    /// <summary>초. 기본 1/60.</summary>
    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    public TangentMode DefaultTangent { get; set; } = TangentMode.Auto;

    public bool AutoKey { get; set; }

    /// <summary>1프레임 단위 재생(마야 Real-time 24fps 표시). 꺼지면 연속.</summary>
    public bool PlayWholeFrames { get; set; }

    /// <summary>하이어아키에 컨트롤러(자리·깊이·연기) 대신 덤프의 리그 노드 26단을 보인다. 리거용 — 기본은 끔.</summary>
    public bool ShowRigNodes { get; set; }

    /// <summary>이모지의 그래프를 종류별 칸(자리 · 크기 · 회전 · 투명도 · 그림)에 나눠 그린다 — 칸마다 제 세로 눈금. 기본은 켜짐.</summary>
    public bool StackEmojiGraph { get; set; } = true;

    /// <summary>트윈머신 창 — 닫을 때 열려 있었는가(다음에 열 때 다시 띄운다) · 화면의 자리 · 오버슈트.</summary>
    public bool TweenWindowOpen { get; set; }

    public int? TweenWindowX { get; set; }

    public int? TweenWindowY { get; set; }

    public bool TweenOvershoot { get; set; }

    /// <summary>마지막 작업 모드.</summary>
    public WorkMode WorkMode { get; set; }

    /// <summary>캐릭터 모션 모드에서 오른쪽 인스펙터의 마지막 탭(0 채널 박스 · 1 씬 · 2 모션 · 3 클립). 이모지 모드의 탭은 하나라 남기지 않는다.</summary>
    public int InspectorTab { get; set; }

    /// <summary>채널 박스에서 접어 둔 묶음 id(placement·depth·acting).</summary>
    public List<string> CollapsedChannelBoxGroups { get; set; } = new();

    /// <summary>하이어아키에 무대 뼈대(Shot/Pan/Zoom/Mask/DepthSystem/Framing…)를 전부 보인다. 기본은 Scene › 무대 › 층만.</summary>
    public bool ShowStageSkeleton { get; set; }

    /// <summary>창 배치 — 왼쪽(씬·모션) 패널 폭(px). 스플리터로 끌면 저장된다.</summary>
    public double LeftPanelWidth { get; set; } = 280;

    /// <summary>창 배치 — 오른쪽(인스펙터·채널 박스) 패널 폭(px).</summary>
    public double RightPanelWidth { get; set; } = 280;

    /// <summary>창 배치 — 뷰포트 : 그래프/도프 시트 높이 비율(별 단위).</summary>
    public double ViewportRowStar { get; set; } = 2;

    public double GraphRowStar { get; set; } = 3;

    /// <summary>타임라인(그래프 위)의 높이 px — 사이의 스플리터를 끌면 남는다. 0이면 트랙 수에 맞춘다.</summary>
    public double TimelineHeight { get; set; }

    /// <summary>시간 표시 — 마야처럼 프레임이 기본.</summary>
    public Graph.TimeDisplay TimeDisplay { get; set; } = Graph.TimeDisplay.Frames;

    /// <summary>분. 0이면 끈다.</summary>
    public int AutoSaveMinutes { get; set; } = 5;

    public List<string> RecentFiles { get; set; } = new();

    [JsonIgnore]
    public string? LoadProblem { get; private set; }

    public void Touch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, full);

        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }

    /// <summary>이 툴의 설정 자리 — 형제 툴(extend · screenTransition)과 폴더를 나눈다.</summary>
    public static string DefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ked-motion-tool-characterRig", FileName);

    /// <summary>갈라지기 전의 자리 — 형제 툴이 지금도 쓴다. 이 툴은 제 설정이 아직 없을 때 한 번 읽어 올 뿐, 쓰지 않는다.</summary>
    public static string SharedLegacyPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ked-motion-tool", FileName);

    /// <param name="fallback">제 파일이 없을 때 대신 읽을 파일(옛 자리). 저장은 언제나 <paramref name="path"/>에 한다.</param>
    public static ToolSettings Load(string path, string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            if (fallback is null || !File.Exists(fallback))
            {
                return new ToolSettings();
            }

            path = fallback;
        }

        try
        {
            ToolSettings settings = JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path), Options) ?? new ToolSettings();
            settings.RecentFiles ??= new List<string>();
            settings.CollapsedChannelBoxGroups ??= new List<string>();
            settings.InspectorTab = Math.Clamp(settings.InspectorTab, 0, 3);
            settings.SnapStep = settings.SnapStep > 0 ? settings.SnapStep : TimeSnap.DefaultStep;
            settings.AutoSaveMinutes = Math.Clamp(settings.AutoSaveMinutes, 0, 120);
            settings.LeftPanelWidth = Math.Clamp(settings.LeftPanelWidth, MinPanelWidth, 800);
            settings.RightPanelWidth = Math.Clamp(settings.RightPanelWidth, MinPanelWidth, 800);
            settings.ViewportRowStar = settings.ViewportRowStar > 0 ? settings.ViewportRowStar : 2;
            settings.GraphRowStar = settings.GraphRowStar > 0 ? settings.GraphRowStar : 3;
            return settings;
        }
        catch (JsonException exception)
        {
            return new ToolSettings { LoadProblem = $"설정을 읽지 못해 기본값으로 둔다: {exception.Message}" };
        }
    }

    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options) + "\n");
    }
}
