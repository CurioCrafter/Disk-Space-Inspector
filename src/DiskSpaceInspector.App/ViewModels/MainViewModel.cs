using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Cleanup;
using DiskSpaceInspector.Core.Layout;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Reporting;
using DiskSpaceInspector.Core.Scanning;
using DiskSpaceInspector.Core.Services;
using DiskSpaceInspector.Core.State;
using DiskSpaceInspector.Core.Windows;
using DiskSpaceInspector.Storage;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int OverviewWorkspaceIndex = 0;
    private const int FilesWorkspaceIndex = 1;
    private const int VisualizeWorkspaceIndex = 2;
    private const int VisualLabWorkspaceIndex = 3;
    private const int CleanupWorkspaceIndex = 4;
    private const int ChangesWorkspaceIndex = 5;
    private const int InsightsWorkspaceIndex = 6;
    private const int TutorialsWorkspaceIndex = 7;
    private const int SettingsWorkspaceIndex = 8;

    private readonly IDriveDiscoveryService _driveDiscovery;
    private readonly IFileSystemScanner _scanner;
    private readonly ICleanupClassifier _classifier;
    private readonly ITreemapLayoutService _treemapLayout;
    private readonly ISunburstLayoutService _sunburstLayout;
    private readonly IStorageBreakdownService _breakdownService;
    private readonly IStorageAnalyticsService _analyticsService;
    private readonly IScanStore _scanStore;
    private readonly IReportExportService _reportExportService;
    private readonly IFirstRunStateStore _firstRunStateStore;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ICleanupReviewQueueService _cleanupReviewQueueService;
    private readonly CleanupPlanBuilder _cleanupPlanBuilder = new();
    private readonly string _databasePath;
    private readonly string _reportsDirectory;

    private ScanResult? _currentScan;
    private IReadOnlyList<StorageRelationship> _currentRelationships = [];
    private Dictionary<long, FileSystemNode> _nodes = [];
    private Dictionary<long, List<FileSystemNode>> _childrenByParent = [];
    private Dictionary<long, CleanupFinding> _findingsByNode = [];
    private CancellationTokenSource? _scanCancellation;
    private ManualResetEventSlim? _pauseGate;
    private VolumeViewModel? _selectedVolume;
    private NodeRowViewModel? _selectedRow;
    private CleanupFindingViewModel? _selectedFinding;
    private CleanupReviewItemViewModel? _selectedCleanupReviewItem;
    private AppSettings _settings;
    private CleanupReviewQueue _cleanupReviewQueue = new();
    private long? _currentNodeId;
    private string _statusText = "Select a drive to scan.";
    private string _scanDetail = "";
    private bool _isScanning;
    private bool _isPaused;
    private string _searchText = "";
    private double _minimumSizeMegabytes;
    private string _selectedName = "";
    private string _selectedPath = "";
    private string _selectedSize = "";
    private string _selectedCounts = "";
    private string _selectedSafety = "";
    private string _breadcrumb = "";
    private string _plannedCleanupSize = "0 B";
    private string _warningSummary = "No scan loaded.";
    private double _scanProgressValue;
    private string _scanProgressText = "Idle";
    private string _scanThroughputText = "";
    private string _scanEtaText = "";
    private string _scanGapText = "";
    private string _scanQueueText = "";
    private string _accountedBytesText = "0 B";
    private string _usedBytesText = "0 B";
    private string _localAdvisorStatusText = "Local cleanup advisor uses scanner evidence only.";
    private string _visualLabSummary = "Run a scan or open demo mode to generate visual analytics.";
    private string _reportExportStatus = "Diagnostics exports are local and path-redacted by default.";
    private string _cleanupReviewSummary = "Stage safe or review-only findings here before taking action outside the app.";
    private string _whatChangedSummary = "Run a scan twice to compare what changed.";
    private int _selectedWorkspaceIndex;
    private bool _isFirstRunWelcomeVisible = true;

    public MainViewModel()
        : this(
            new WindowsDriveDiscoveryService(),
            new FileSystemScanner(new WindowsRelationshipResolver()),
            new CleanupClassifier(),
            new SquarifiedTreemapLayoutService(),
            new SunburstLayoutService(),
            new StorageBreakdownService(),
            new StorageAnalyticsService(),
            new SqliteScanStore(DefaultDatabasePath()),
            new ReportExportService(),
            new JsonFirstRunStateStore(DefaultFirstRunStatePath()),
            new JsonAppSettingsStore(DefaultAppSettingsPath()),
            new CleanupReviewQueueService(),
            DefaultDatabasePath(),
            DefaultReportsDirectory(),
            IsDemoMode(),
            IsFirstRunPreviewMode())
    {
    }

    public MainViewModel(
        IDriveDiscoveryService driveDiscovery,
        IFileSystemScanner scanner,
        ICleanupClassifier classifier,
        ITreemapLayoutService treemapLayout,
        ISunburstLayoutService sunburstLayout,
        IStorageBreakdownService breakdownService,
        IStorageAnalyticsService analyticsService,
        IScanStore scanStore,
        IReportExportService? reportExportService = null,
        IFirstRunStateStore? firstRunStateStore = null,
        IAppSettingsStore? appSettingsStore = null,
        ICleanupReviewQueueService? cleanupReviewQueueService = null,
        string? databasePath = null,
        string? reportsDirectory = null,
        bool demoMode = false,
        bool firstRunPreviewMode = false)
    {
        _driveDiscovery = driveDiscovery;
        _scanner = scanner;
        _classifier = classifier;
        _treemapLayout = treemapLayout;
        _sunburstLayout = sunburstLayout;
        _breakdownService = breakdownService;
        _analyticsService = analyticsService;
        _scanStore = scanStore;
        _reportExportService = reportExportService ?? new ReportExportService();
        _firstRunStateStore = firstRunStateStore ?? new JsonFirstRunStateStore(DefaultFirstRunStatePath());
        _appSettingsStore = appSettingsStore ?? new JsonAppSettingsStore(DefaultAppSettingsPath());
        _cleanupReviewQueueService = cleanupReviewQueueService ?? new CleanupReviewQueueService();
        _databasePath = databasePath ?? DefaultDatabasePath();
        _reportsDirectory = reportsDirectory ?? DefaultReportsDirectory();
        _settings = LoadSettings();
        _minimumSizeMegabytes = _settings.Charts.MinimumNodeSizeMegabytes;

        RefreshDrivesCommand = new AsyncRelayCommand(() => RefreshDrivesAsync(force: true), () => !IsScanning);
        ScanSelectedCommand = new AsyncRelayCommand(ScanSelectedAsync, () => SelectedVolume?.Model.IsReady == true && !IsScanning);
        ScanAllCommand = new AsyncRelayCommand(ScanAllAsync, () => Volumes.Any(v => v.Model.IsReady) && !IsScanning);
        CancelScanCommand = new RelayCommand(_ => _scanCancellation?.Cancel(), _ => IsScanning);
        PauseScanCommand = new RelayCommand(_ => PauseScan(), _ => IsScanning && !IsPaused);
        ResumeScanCommand = new RelayCommand(_ => ResumeScan(), _ => IsScanning && IsPaused);
        LoadLatestCommand = new AsyncRelayCommand(LoadLatestAsync, () => !IsScanning);
        TreemapTileSelectedCommand = new RelayCommand(tile =>
        {
            if (tile is TreemapTileViewModel { NodeId: { } nodeId })
            {
                NavigateToNode(nodeId);
            }
        });
        SunburstSegmentSelectedCommand = new RelayCommand(segment =>
        {
            if (segment is SunburstSegmentViewModel { NodeId: { } nodeId })
            {
                NavigateToNode(nodeId);
            }
        });
        DrillIntoSelectionCommand = new RelayCommand(_ =>
        {
            if (SelectedRow is not null && SelectedRow.Node.Kind != FileSystemNodeKind.File)
            {
                NavigateToNode(SelectedRow.Id);
            }
        }, _ => SelectedRow is not null);
        OpenSelectedCommand = new RelayCommand(_ => OpenSelectedPath(), _ => !string.IsNullOrWhiteSpace(SelectedPath));
        StageFindingCommand = new RelayCommand(_ => StageSelectedFinding(), _ => SelectedFinding is not null);
        VisualChartSelectedCommand = new RelayCommand(HandleVisualChartSelection);
        LoadDemoCommand = new RelayCommand(_ => LoadDemoData());
        ShowCleanupSafetyCommand = new RelayCommand(_ => ShowCleanupSafetyGuide());
        OpenPrivacyCenterCommand = new RelayCommand(_ => SelectedWorkspaceIndex = SettingsWorkspaceIndex);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync, () => _currentScan is not null && !IsScanning);
        RunConsumerToolCommand = new RelayCommand(RunConsumerTool);
        RemoveStagedCleanupCommand = new RelayCommand(_ => RemoveSelectedCleanupReviewItem(), _ => SelectedCleanupReviewItem is not null);
        ClearCleanupQueueCommand = new RelayCommand(_ => ClearCleanupReviewQueue(), _ => CleanupReviewItems.Count > 0);
        ExportCleanupReviewCommand = new AsyncRelayCommand(ExportCleanupReviewAsync, () => CleanupReviewItems.Count > 0);
        OpenStorageSettingsCommand = new RelayCommand(_ => OpenShellTarget("ms-settings:storagesense"));
        OpenInstalledAppsCommand = new RelayCommand(_ => OpenShellTarget("ms-settings:appsfeatures"));
        OpenDiskCleanupCommand = new RelayCommand(_ => OpenShellTarget("cleanmgr.exe"));
        OpenDataFolderCommand = new RelayCommand(_ =>
        {
            Directory.CreateDirectory(DefaultAppDataDirectory());
            OpenShellTarget(DefaultAppDataDirectory());
        });
        ResetWelcomeCommand = new RelayCommand(_ => ResetWelcomeState());
        ClearLocalDatabaseCommand = new RelayCommand(_ => ClearLocalDatabase(), _ => !IsScanning);

        BuildPrivacyFacts();
        _ = MarkAppOpenedAsync();

        if (firstRunPreviewMode)
        {
            IsFirstRunWelcomeVisible = true;
            StatusText = "Welcome preview.";
        }
        else if (demoMode || _settings.Launch.OpenDemoWorkspaceOnStartup)
        {
            LoadDemoData();
        }
        else
        {
            IsFirstRunWelcomeVisible = _settings.Launch.ShowWelcomeWhenNoScan;
            _ = RefreshDrivesAsync(force: false);
            if (_settings.Launch.LoadLatestScanOnStartup)
            {
                _ = LoadLatestAsync();
            }
        }

        SelectedWorkspaceIndex = InitialWorkspaceIndex();
    }

    public ObservableCollection<VolumeViewModel> Volumes { get; } = [];

    public ObservableCollection<NodeTreeItemViewModel> TreeNodes { get; } = [];

    public ObservableCollection<NodeRowViewModel> NodeRows { get; } = [];

    public ObservableCollection<TreemapTileViewModel> TreemapTiles { get; } = [];

    public ObservableCollection<SunburstSegmentViewModel> SunburstSegments { get; } = [];

    public ObservableCollection<NodeRowViewModel> TopSpaceConsumers { get; } = [];

    public ObservableCollection<StorageBreakdownItemViewModel> TypeBreakdown { get; } = [];

    public ObservableCollection<AgeHistogramBucketViewModel> AgeHistogram { get; } = [];

    public ObservableCollection<StorageBreakdownItemViewModel> CleanupPotential { get; } = [];

    public ObservableCollection<CleanupFindingViewModel> CleanupFindings { get; } = [];

    public ObservableCollection<CleanupReviewItemViewModel> CleanupReviewItems { get; } = [];

    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public ObservableCollection<StorageRelationshipViewModel> EvidenceRelationships { get; } = [];

    public ObservableCollection<ChangeRecordViewModel> Changes { get; } = [];

    public ObservableCollection<InsightFindingViewModel> Insights { get; } = [];

    public ObservableCollection<ChartDefinitionViewModel> VisualLabCharts { get; } = [];

    public ObservableCollection<ChartDefinitionViewModel> VisualLabAdvancedCharts { get; } = [];

    public ObservableCollection<TutorialStepViewModel> TutorialSteps { get; } = [];

    public ObservableCollection<QuickFindingViewModel> FiveMinuteFindings { get; } = [];

    public ObservableCollection<ConsumerStorageToolViewModel> ConsumerTools { get; } = [];

    public ObservableCollection<PrivacyFactViewModel> PrivacyFacts { get; } = [];

    public AsyncRelayCommand RefreshDrivesCommand { get; }

    public AsyncRelayCommand ScanSelectedCommand { get; }

    public AsyncRelayCommand ScanAllCommand { get; }

    public RelayCommand CancelScanCommand { get; }

    public RelayCommand PauseScanCommand { get; }

    public RelayCommand ResumeScanCommand { get; }

    public AsyncRelayCommand LoadLatestCommand { get; }

    public RelayCommand TreemapTileSelectedCommand { get; }

    public RelayCommand SunburstSegmentSelectedCommand { get; }

    public RelayCommand DrillIntoSelectionCommand { get; }

    public RelayCommand OpenSelectedCommand { get; }

    public RelayCommand StageFindingCommand { get; }

    public RelayCommand VisualChartSelectedCommand { get; }

    public RelayCommand LoadDemoCommand { get; }

    public RelayCommand ShowCleanupSafetyCommand { get; }

    public RelayCommand OpenPrivacyCenterCommand { get; }

    public AsyncRelayCommand ExportDiagnosticsCommand { get; }

    public RelayCommand RunConsumerToolCommand { get; }

    public RelayCommand RemoveStagedCleanupCommand { get; }

    public RelayCommand ClearCleanupQueueCommand { get; }

    public AsyncRelayCommand ExportCleanupReviewCommand { get; }

    public RelayCommand OpenStorageSettingsCommand { get; }

    public RelayCommand OpenInstalledAppsCommand { get; }

    public RelayCommand OpenDiskCleanupCommand { get; }

    public RelayCommand OpenDataFolderCommand { get; }

    public RelayCommand ResetWelcomeCommand { get; }

    public RelayCommand ClearLocalDatabaseCommand { get; }

    public VolumeViewModel? SelectedVolume
    {
        get => _selectedVolume;
        set
        {
            if (SetProperty(ref _selectedVolume, value))
            {
                ScanSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public NodeRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                if (value is not null)
                {
                    SelectNodeDetails(value.Node);
                }

                DrillIntoSelectionCommand.RaiseCanExecuteChanged();
                OpenSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CleanupFindingViewModel? SelectedFinding
    {
        get => _selectedFinding;
        set
        {
            if (SetProperty(ref _selectedFinding, value))
            {
                StageFindingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CleanupReviewItemViewModel? SelectedCleanupReviewItem
    {
        get => _selectedCleanupReviewItem;
        set
        {
            if (SetProperty(ref _selectedCleanupReviewItem, value))
            {
                RemoveStagedCleanupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RefreshDrivesCommand.RaiseCanExecuteChanged();
                ScanSelectedCommand.RaiseCanExecuteChanged();
                ScanAllCommand.RaiseCanExecuteChanged();
                CancelScanCommand.RaiseCanExecuteChanged();
                PauseScanCommand.RaiseCanExecuteChanged();
                ResumeScanCommand.RaiseCanExecuteChanged();
                LoadLatestCommand.RaiseCanExecuteChanged();
                ExportDiagnosticsCommand.RaiseCanExecuteChanged();
                ClearLocalDatabaseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                PauseScanCommand.RaiseCanExecuteChanged();
                ResumeScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ScanDetail
    {
        get => _scanDetail;
        private set => SetProperty(ref _scanDetail, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshExplorerRows();
            }
        }
    }

    public double MinimumSizeMegabytes
    {
        get => _minimumSizeMegabytes;
        set
        {
            if (SetProperty(ref _minimumSizeMegabytes, value))
            {
                _settings.Charts.MinimumNodeSizeMegabytes = Math.Max(0, value);
                PersistSettings();
                RefreshExplorerRows();
            }
        }
    }

    public string SelectedName
    {
        get => _selectedName;
        private set => SetProperty(ref _selectedName, value);
    }

    public string SelectedPath
    {
        get => _selectedPath;
        private set => SetProperty(ref _selectedPath, value);
    }

    public string SelectedSize
    {
        get => _selectedSize;
        private set => SetProperty(ref _selectedSize, value);
    }

    public string SelectedCounts
    {
        get => _selectedCounts;
        private set => SetProperty(ref _selectedCounts, value);
    }

    public string SelectedSafety
    {
        get => _selectedSafety;
        private set => SetProperty(ref _selectedSafety, value);
    }

    public string Breadcrumb
    {
        get => _breadcrumb;
        private set => SetProperty(ref _breadcrumb, value);
    }

    public string PlannedCleanupSize
    {
        get => _plannedCleanupSize;
        private set => SetProperty(ref _plannedCleanupSize, value);
    }

    public string WarningSummary
    {
        get => _warningSummary;
        private set => SetProperty(ref _warningSummary, value);
    }

    public double ScanProgressValue
    {
        get => _scanProgressValue;
        private set => SetProperty(ref _scanProgressValue, value);
    }

    public string ScanProgressText
    {
        get => _scanProgressText;
        private set => SetProperty(ref _scanProgressText, value);
    }

    public string ScanThroughputText
    {
        get => _scanThroughputText;
        private set => SetProperty(ref _scanThroughputText, value);
    }

    public string ScanEtaText
    {
        get => _scanEtaText;
        private set => SetProperty(ref _scanEtaText, value);
    }

    public string ScanGapText
    {
        get => _scanGapText;
        private set => SetProperty(ref _scanGapText, value);
    }

    public string ScanQueueText
    {
        get => _scanQueueText;
        private set => SetProperty(ref _scanQueueText, value);
    }

    public string AccountedBytesText
    {
        get => _accountedBytesText;
        private set => SetProperty(ref _accountedBytesText, value);
    }

    public string UsedBytesText
    {
        get => _usedBytesText;
        private set => SetProperty(ref _usedBytesText, value);
    }

    public string LocalAdvisorStatusText
    {
        get => _localAdvisorStatusText;
        private set => SetProperty(ref _localAdvisorStatusText, value);
    }

    public string VisualLabSummary
    {
        get => _visualLabSummary;
        private set => SetProperty(ref _visualLabSummary, value);
    }

    public string ReportExportStatus
    {
        get => _reportExportStatus;
        private set => SetProperty(ref _reportExportStatus, value);
    }

    public string CleanupReviewSummary
    {
        get => _cleanupReviewSummary;
        private set => SetProperty(ref _cleanupReviewSummary, value);
    }

    public string WhatChangedSummary
    {
        get => _whatChangedSummary;
        private set => SetProperty(ref _whatChangedSummary, value);
    }

    public string DatabasePath => RedactLocalDisplayPath(_databasePath);

    public string ReportsDirectory => RedactLocalDisplayPath(_reportsDirectory);

    public string AppVersion => typeof(FileSystemNode).Assembly
        .GetName()
        .Version?
        .ToString(3) ?? "1.1.0";

    public string OwnerName => "Andrew Rainsberger";

    public string UseRiskNotice => "Use at your own risk. This app scans local storage and explains cleanup candidates, but you are responsible for reviewing paths and backing up important files before acting.";

    public string TelemetryStatus => $"{PrivacyAndSafetyFacts.TelemetryMode} telemetry. No background usage or crash reporting is sent.";

    public string ExternalIntegrationPolicy => PrivacyAndSafetyFacts.ExternalIntegrationPolicy;

    public string BlockedDirectCleanupPaths => string.Join("; ", PrivacyAndSafetyFacts.BlockedDirectCleanupPaths);

    public IReadOnlyList<string> ChartDensityOptions { get; } = ["Compact", "Comfortable", "Spacious"];

    public bool LoadLatestScanOnStartup
    {
        get => _settings.Launch.LoadLatestScanOnStartup;
        set
        {
            if (_settings.Launch.LoadLatestScanOnStartup != value)
            {
                _settings.Launch.LoadLatestScanOnStartup = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool ShowWelcomeWhenNoScan
    {
        get => _settings.Launch.ShowWelcomeWhenNoScan;
        set
        {
            if (_settings.Launch.ShowWelcomeWhenNoScan != value)
            {
                _settings.Launch.ShowWelcomeWhenNoScan = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool OpenDemoWorkspaceOnStartup
    {
        get => _settings.Launch.OpenDemoWorkspaceOnStartup;
        set
        {
            if (_settings.Launch.OpenDemoWorkspaceOnStartup != value)
            {
                _settings.Launch.OpenDemoWorkspaceOnStartup = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool IncludeRemovableDrives
    {
        get => _settings.Scan.IncludeRemovableDrives;
        set
        {
            if (_settings.Scan.IncludeRemovableDrives != value)
            {
                _settings.Scan.IncludeRemovableDrives = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool RecordPermissionGaps
    {
        get => _settings.Scan.RecordPermissionGaps;
        set
        {
            if (_settings.Scan.RecordPermissionGaps != value)
            {
                _settings.Scan.RecordPermissionGaps = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool DoNotFollowDirectoryLinks
    {
        get => _settings.Scan.DoNotFollowDirectoryLinks;
        set
        {
            if (_settings.Scan.DoNotFollowDirectoryLinks != value)
            {
                _settings.Scan.DoNotFollowDirectoryLinks = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool RedactUserProfileInReports
    {
        get => _settings.Privacy.RedactUserProfileInReports;
        set
        {
            if (_settings.Privacy.RedactUserProfileInReports != value)
            {
                _settings.Privacy.RedactUserProfileInReports = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool RetainScanHistory
    {
        get => _settings.Data.RetainScanHistory;
        set
        {
            if (_settings.Data.RetainScanHistory != value)
            {
                _settings.Data.RetainScanHistory = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int MaxSnapshotsToKeep
    {
        get => _settings.Data.MaxSnapshotsToKeep;
        set
        {
            var normalized = Math.Clamp(value, 1, 100);
            if (_settings.Data.MaxSnapshotsToKeep != normalized)
            {
                _settings.Data.MaxSnapshotsToKeep = normalized;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int MaxBestInsightCards
    {
        get => _settings.Charts.MaxBestInsightCards;
        set
        {
            var normalized = Math.Clamp(value, 6, 28);
            if (_settings.Charts.MaxBestInsightCards != normalized)
            {
                _settings.Charts.MaxBestInsightCards = normalized;
                OnPropertyChanged();
                PersistSettings();
                if (_currentScan is not null)
                {
                    RefreshVisualLab(_currentScan, _findingsByNode.Values.ToList(), _currentRelationships, Changes.Select(c => c.Model).ToList());
                }
            }
        }
    }

    public string ChartDensity
    {
        get => _settings.Charts.Density;
        set
        {
            if (!string.Equals(_settings.Charts.Density, value, StringComparison.Ordinal))
            {
                _settings.Charts.Density = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int SelectedWorkspaceIndex
    {
        get => _selectedWorkspaceIndex;
        set => SetProperty(ref _selectedWorkspaceIndex, value);
    }

    public bool IsFirstRunWelcomeVisible
    {
        get => _isFirstRunWelcomeVisible;
        private set => SetProperty(ref _isFirstRunWelcomeVisible, value);
    }

    public void NavigateToNode(long nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        _currentNodeId = nodeId;
        SelectNodeDetails(node);
        RefreshExplorerRows();
        RefreshTreemap();
        RefreshSunburst();
        RefreshConnections();
    }

    private async Task RefreshDrivesAsync(bool force)
    {
        StatusText = "Discovering drives.";
        IReadOnlyList<VolumeInfo> volumes;
        try
        {
            volumes = await Task.Run(() => _driveDiscovery.GetVolumes().ToList()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Drive discovery failed: {ex.Message}";
            return;
        }

        if (!force && _currentScan is not null)
        {
            return;
        }

        Volumes.Clear();
        foreach (var volume in volumes)
        {
            Volumes.Add(new VolumeViewModel(volume));
        }

        SelectedVolume = Volumes.FirstOrDefault(v => v.Model.IsReady);
        StatusText = Volumes.Count == 0 ? "No filesystem drives were discovered." : "Drives refreshed.";
        ScanAllCommand.RaiseCanExecuteChanged();
    }

    private void LoadDemoData()
    {
        IsFirstRunWelcomeVisible = false;
        SelectedWorkspaceIndex = OverviewWorkspaceIndex;
        var demo = DemoDataFactory.Create();

        Volumes.Clear();
        foreach (var volume in demo.Volumes)
        {
            Volumes.Add(new VolumeViewModel(volume));
        }

        SelectedVolume = Volumes.FirstOrDefault();
        LoadScan(demo.Scan, demo.CleanupFindings, demo.Relationships, demo.Changes, demo.Insights);

        LocalAdvisorStatusText = "Demo mode: local cleanup advisor is populated from deterministic sample findings.";
        StatusText = "Demo scan loaded with realistic sample storage data.";
        ScanProgressValue = 0.923;
        ScanProgressText = "92.3% accounted";
        AccountedBytesText = ByteFormatter.Format(demo.Scan.Session.TotalPhysicalBytes);
        UsedBytesText = ByteFormatter.Format(demo.Scan.Volume.TotalBytes - demo.Scan.Volume.FreeBytes);
        ScanThroughputText = "18,420 files/s, 4,810 dirs/s";
        ScanEtaText = "Completed demo scan";
        ScanGapText = $"{demo.Scan.Issues.Count:n0} scan gaps";
        ScanQueueText = "0 queued";
        ScanDetail = $"{demo.Scan.Session.FilesScanned:n0} files, {demo.Scan.Session.DirectoriesScanned:n0} folders, {ByteFormatter.Format(demo.Scan.Session.TotalPhysicalBytes)} physical usage";
        _ = MarkDemoLoadedAsync();
    }

    private static bool IsDemoMode()
    {
        return Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFirstRunPreviewMode()
    {
        return Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--first-run", StringComparison.OrdinalIgnoreCase));
    }

    private static int InitialWorkspaceIndex()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (!arg.StartsWith("--view=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return arg["--view=".Length..].Trim().ToLowerInvariant() switch
            {
                "overview" => OverviewWorkspaceIndex,
                "files" or "explore" => FilesWorkspaceIndex,
                "visualize" or "visualizer" or "map" => VisualizeWorkspaceIndex,
                "visual-lab" or "lab" or "charts" => VisualLabWorkspaceIndex,
                "cleanup" => CleanupWorkspaceIndex,
                "changes" or "what-changed" => ChangesWorkspaceIndex,
                "insights" or "explanations" => InsightsWorkspaceIndex,
                "tutorials" or "guide" or "help" => TutorialsWorkspaceIndex,
                "settings" or "privacy" or "safety" => SettingsWorkspaceIndex,
                _ => OverviewWorkspaceIndex
            };
        }

        return OverviewWorkspaceIndex;
    }

    private static string DefaultAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Disk Space Inspector");
    }

    private static string DefaultDatabasePath()
    {
        return Path.Combine(DefaultAppDataDirectory(), "disk-space-inspector.db");
    }

    private static string DefaultFirstRunStatePath()
    {
        return Path.Combine(DefaultAppDataDirectory(), "first-run-state.json");
    }

    private static string DefaultAppSettingsPath()
    {
        return Path.Combine(DefaultAppDataDirectory(), "app-settings.json");
    }

    private static string DefaultReportsDirectory()
    {
        return Path.Combine(DefaultAppDataDirectory(), "Reports");
    }

    private AppSettings LoadSettings()
    {
        try
        {
            return _appSettingsStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void PersistSettings()
    {
        _ = PersistSettingsAsync();
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _appSettingsStore.SaveAsync(_settings).ConfigureAwait(false);
        }
        catch
        {
            // Settings persistence should never block scanning or review workflows.
        }
    }

    private static string RedactLocalDisplayPath(string path)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData) && path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
        {
            return "%LOCALAPPDATA%" + path[localAppData.Length..];
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "%USERPROFILE%" + path[userProfile.Length..];
        }

        return path;
    }

    private void BuildPrivacyFacts()
    {
        PrivacyFacts.Clear();
        PrivacyFacts.Add(new PrivacyFactViewModel(
            "Telemetry",
            "Off",
            "No background analytics, usage tracking, or crash upload service is wired into this app."));
        PrivacyFacts.Add(new PrivacyFactViewModel(
            "Local database",
            RedactLocalDisplayPath(_databasePath),
            "Scan snapshots stay in your local app data folder unless you export a report."));
        PrivacyFacts.Add(new PrivacyFactViewModel(
            "External integrations",
            "None",
            PrivacyAndSafetyFacts.ExternalIntegrationPolicy));
        PrivacyFacts.Add(new PrivacyFactViewModel(
            "Cleanup execution",
            "Advisory only",
            "This version stages recommendations for review and does not directly delete files."));
        PrivacyFacts.Add(new PrivacyFactViewModel(
            "Ownership",
            "Andrew Rainsberger",
            "Disk Space Inspector is source-available software owned by Andrew Rainsberger."));
        PrivacyFacts.Add(new PrivacyFactViewModel(
            "Use at your own risk",
            "Review before acting",
            "The app explains disk usage and cleanup candidates, but you are responsible for backups and any action taken outside the app."));
    }

    private async Task MarkAppOpenedAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var state = await _firstRunStateStore.LoadAsync().ConfigureAwait(false);
            state.HasOpenedApp = true;
            state.FirstOpenedAtUtc ??= now;
            state.LastOpenedAtUtc = now;
            await _firstRunStateStore.SaveAsync(state).ConfigureAwait(false);
        }
        catch
        {
            // First-run state should never block the main app experience.
        }
    }

    private async Task MarkDemoLoadedAsync()
    {
        try
        {
            var state = await _firstRunStateStore.LoadAsync().ConfigureAwait(false);
            state.HasLoadedDemo = true;
            state.LastDemoLoadedAtUtc = DateTimeOffset.UtcNow;
            await _firstRunStateStore.SaveAsync(state).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task MarkScanLoadedAsync()
    {
        try
        {
            var state = await _firstRunStateStore.LoadAsync().ConfigureAwait(false);
            state.HasLoadedScan = true;
            state.LastScanLoadedAtUtc = DateTimeOffset.UtcNow;
            await _firstRunStateStore.SaveAsync(state).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task ScanSelectedAsync()
    {
        if (SelectedVolume is null)
        {
            return;
        }

        await RunScanSetAsync([SelectedVolume.Model]).ConfigureAwait(true);
    }

    private async Task ScanAllAsync()
    {
        var volumes = Volumes
            .Where(v => v.Model.IsReady)
            .Where(v => (v.Model.DriveType == "Fixed" && _settings.Scan.IncludeFixedDrives) ||
                        (v.Model.DriveType == "Removable" && _settings.Scan.IncludeRemovableDrives))
            .Select(v => v.Model)
            .ToList();

        await RunScanSetAsync(volumes).ConfigureAwait(true);
    }

    private async Task RunScanSetAsync(IReadOnlyList<VolumeInfo> volumes)
    {
        if (volumes.Count == 0)
        {
            return;
        }

        IsFirstRunWelcomeVisible = false;
        IsScanning = true;
        IsPaused = false;
        _scanCancellation = new CancellationTokenSource();
        _pauseGate = new ManualResetEventSlim(initialState: true);
        var token = _scanCancellation.Token;
        var completedScans = new List<ScanCompleted>();
        var liveRefreshAt = DateTimeOffset.MinValue;

        try
        {
            for (var volumeIndex = 0; volumeIndex < volumes.Count; volumeIndex++)
            {
                var volume = volumes[volumeIndex];
                token.ThrowIfCancellationRequested();
                StatusText = $"Scanning {volume.RootPath}";
                var progress = new Progress<ScanProgress>(p =>
                {
                    UpdateProgress(p);
                    if (!string.IsNullOrWhiteSpace(p.CurrentPath))
                    {
                        StatusText = $"Scanning {p.CurrentPath}";
                    }
                });

                var beganScan = false;
                var completed = await _scanner.StartScanAsync(
                    new ScanRequest(volume)
                    {
                        PauseGate = _pauseGate,
                        PipelineOptions = ScanPipelineOptions.FastFirstScan
                    },
                    async (batch, batchToken) =>
                    {
                        if (!beganScan)
                        {
                            beganScan = true;
                            await _scanStore.BeginScanAsync(new ScanSession
                            {
                                Id = batch.ScanId,
                                RootPath = volume.RootPath,
                                StartedAtUtc = DateTimeOffset.UtcNow,
                                Status = ScanStatus.Running
                            }, volume, batchToken).ConfigureAwait(false);
                        }

                        var findings = Classify(batch.Nodes);
                        var insights = BuildInsights(batch.ScanId, batch.Nodes, findings, batch.Relationships);
                        await _scanStore.SaveScanBatchAsync(batch, findings, insights, batchToken).ConfigureAwait(false);

                        if (DateTimeOffset.UtcNow - liveRefreshAt > TimeSpan.FromSeconds(1))
                        {
                            liveRefreshAt = DateTimeOffset.UtcNow;
                            await RefreshLiveFromStoreAsync(batch.ScanId).ConfigureAwait(false);
                        }
                    },
                    progress,
                    token).ConfigureAwait(true);

                completed.Session.Status = token.IsCancellationRequested ? ScanStatus.Cancelled : completed.Session.Status;
                await _scanStore.CompleteScanAsync(completed, CancellationToken.None).ConfigureAwait(true);
                completedScans.Add(completed);
                ScanProgressText = $"{volumeIndex + 1}/{volumes.Count} volumes scanned";
            }

            await LoadLatestAsync().ConfigureAwait(true);
            StatusText = $"Scan complete. {completedScans.Sum(s => s.Session.FilesScanned):n0} files inspected.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled. Saved batches remain available.";
            await LoadLatestAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed.";
            ScanDetail = ex.Message;
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            _pauseGate?.Dispose();
            _pauseGate = null;
            IsPaused = false;
            IsScanning = false;
        }
    }

    private void PauseScan()
    {
        _pauseGate?.Reset();
        IsPaused = true;
        StatusText = "Scan paused.";
    }

    private void ResumeScan()
    {
        _pauseGate?.Set();
        IsPaused = false;
        StatusText = "Scan resumed.";
    }

    private async Task RefreshLiveFromStoreAsync(Guid scanId)
    {
        var result = await _scanStore.LoadLatestScanAsync(CancellationToken.None).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        var findings = await _scanStore.LoadCleanupFindingsAsync(result.Session.Id, CancellationToken.None).ConfigureAwait(false);
        var changes = await _scanStore.LoadChangeRecordsAsync(result.Session.Id, CancellationToken.None).ConfigureAwait(false);
        var relationships = await _scanStore.LoadRelationshipsAsync(result.Session.Id, CancellationToken.None).ConfigureAwait(false);
        var insights = await _scanStore.LoadInsightsAsync(result.Session.Id, CancellationToken.None).ConfigureAwait(false);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LoadScan(result, findings, relationships, changes, insights);
        });
    }

    private async Task LoadLatestAsync()
    {
        StatusText = "Loading latest persisted scan.";
        var result = await _scanStore.LoadLatestScanAsync().ConfigureAwait(true);
        if (result is null)
        {
            StatusText = "No saved scans found.";
            IsFirstRunWelcomeVisible = _settings.Launch.ShowWelcomeWhenNoScan;
            return;
        }

        var findings = await _scanStore.LoadCleanupFindingsAsync(result.Session.Id).ConfigureAwait(true);
        var changes = await _scanStore.LoadChangeRecordsAsync(result.Session.Id).ConfigureAwait(true);
        var relationships = await _scanStore.LoadRelationshipsAsync(result.Session.Id).ConfigureAwait(true);
        var insights = await _scanStore.LoadInsightsAsync(result.Session.Id).ConfigureAwait(true);
        LoadScan(result, findings, relationships, changes, insights);
        StatusText = $"Loaded scan from {result.Session.StartedAtUtc.LocalDateTime:g}.";
    }

    private void LoadScan(
        ScanResult result,
        IEnumerable<CleanupFinding> findings,
        IEnumerable<StorageRelationship>? relationships = null,
        IEnumerable<ChangeRecord>? changes = null,
        IEnumerable<InsightFinding>? insights = null)
    {
        var findingList = findings.ToList();
        var relationshipList = relationships?.ToList() ?? [];
        var changeList = changes?.ToList() ?? [];
        var insightList = insights?.ToList() ?? [];

        _currentScan = result;
        _currentRelationships = relationshipList;
        IsFirstRunWelcomeVisible = false;
        _nodes = result.Nodes.ToDictionary(n => n.Id);
        _childrenByParent = result.Nodes
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(SizeOf).ThenBy(n => n.Name).ToList());
        _findingsByNode = findingList
            .GroupBy(f => f.NodeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Confidence).First());

        CleanupFindings.Clear();
        foreach (var finding in _findingsByNode.Values.OrderBy(f => f.Safety).ThenByDescending(f => f.SizeBytes))
        {
            CleanupFindings.Add(new CleanupFindingViewModel(finding));
        }

        _cleanupReviewQueue = new CleanupReviewQueue();
        RefreshCleanupReviewQueue();

        Changes.Clear();
        var changeSummary = ChangeSummaryService.Summarize(changeList, result.Nodes.Count);
        WhatChangedSummary = changeSummary.Message;
        IReadOnlyList<ChangeRecord> displayChanges = changeSummary.IsBaseline
            ? []
            : changeList.OrderByDescending(c => Math.Abs(c.DeltaBytes)).Take(2000).ToList();
        foreach (var change in displayChanges)
        {
            Changes.Add(new ChangeRecordViewModel(change));
        }

        Insights.Clear();
        foreach (var insight in insightList.OrderByDescending(i => i.SizeBytes).ThenByDescending(i => i.Confidence).Take(2000))
        {
            Insights.Add(new InsightFindingViewModel(insight));
        }

        LocalAdvisorStatusText = _findingsByNode.Count == 0
            ? "Local cleanup advisor is waiting for scan evidence."
            : $"{_findingsByNode.Count:n0} evidence-backed cleanup findings loaded.";
        ExportDiagnosticsCommand.RaiseCanExecuteChanged();

        EvidenceRelationships.Clear();
        foreach (var relationship in _currentRelationships.OrderByDescending(r => r.Evidence.Confidence).Take(2000))
        {
            EvidenceRelationships.Add(new StorageRelationshipViewModel(relationship));
        }

        RefreshOverviewCollections();
        RefreshConsumerTools(result, findingList, relationshipList, changeList);
        RefreshFiveMinuteFindings(result, findingList, relationshipList, changeList);
        RefreshVisualLab(result, findingList, relationshipList, changeList);

        TreeNodes.Clear();
        var root = result.Nodes.FirstOrDefault(n => n.ParentId is null) ?? result.Nodes.FirstOrDefault();
        if (root is not null)
        {
            TreeNodes.Add(BuildTree(root, 0));
            NavigateToNode(root.Id);
        }

        var issueSummary = result.Issues.Count == 0
            ? "No scan issues recorded."
            : $"{result.Issues.Count:n0} scan gaps recorded. Permission-denied and inaccessible paths are tracked instead of hidden.";
        WarningSummary = issueSummary;
        ScanDetail = $"{result.Session.FilesScanned:n0} files, {result.Session.DirectoriesScanned:n0} folders, {ByteFormatter.Format(result.Session.TotalPhysicalBytes)} physical usage";
        _ = MarkScanLoadedAsync();
    }

    private List<CleanupFinding> Classify(IEnumerable<FileSystemNode> nodes)
    {
        return nodes
            .Select(_classifier.Classify)
            .Where(f => f is not null)
            .Cast<CleanupFinding>()
            .OrderByDescending(f => f.SizeBytes)
            .ToList();
    }

    private static List<InsightFinding> BuildInsights(
        Guid scanId,
        IEnumerable<FileSystemNode> nodes,
        IEnumerable<CleanupFinding> findings,
        IEnumerable<StorageRelationship> relationships)
    {
        var insights = new List<InsightFinding>();

        foreach (var finding in findings)
        {
            insights.Add(new InsightFinding
            {
                ScanId = scanId,
                NodeId = finding.NodeId,
                StableId = "",
                Path = finding.Path,
                Tool = finding.Category switch
                {
                    "Temporary files" or "Application cache" or "Recycle Bin" => "Cleanup Advisor",
                    "Developer artifact" => "Developer Bloat Finder",
                    "Windows cleanup" or "System managed storage" => "System Storage Advisor",
                    "Large file review" or "Downloads review" => "Large File Triage",
                    _ => "Storage Advisor"
                },
                Title = finding.Category,
                Description = finding.Explanation,
                Safety = finding.Safety,
                RecommendedAction = finding.RecommendedAction,
                SizeBytes = finding.SizeBytes,
                Confidence = finding.Confidence,
                Evidence = finding.MatchedRule
            });
        }

        foreach (var relationship in relationships)
        {
            insights.Add(new InsightFinding
            {
                ScanId = scanId,
                NodeId = relationship.SourceNodeId,
                StableId = "",
                Path = relationship.SourcePath,
                Tool = relationship.Kind switch
                {
                    FileSystemEdgeKind.AppOwnership or FileSystemEdgeKind.CacheOwnership => "App Ownership View",
                    FileSystemEdgeKind.PackageArtifact => "Developer Bloat Finder",
                    FileSystemEdgeKind.JunctionTarget or FileSystemEdgeKind.SymlinkTarget or FileSystemEdgeKind.HardlinkSibling => "Storage Graph",
                    _ => "Storage Graph"
                },
                Title = $"{relationship.Label} {relationship.Owner}".Trim(),
                Description = string.IsNullOrWhiteSpace(relationship.TargetPath)
                    ? relationship.Evidence.Detail
                    : $"{relationship.SourcePath} {relationship.Label} {relationship.TargetPath}",
                Safety = CleanupSafety.Review,
                RecommendedAction = CleanupActionKind.LeaveAlone,
                SizeBytes = 0,
                Confidence = relationship.Evidence.Confidence,
                Evidence = $"{relationship.Evidence.Source}: {relationship.Evidence.Detail}"
            });
        }

        foreach (var node in nodes)
        {
            var size = Math.Max(node.TotalPhysicalLength, node.PhysicalLength);
            if (node.Kind == FileSystemNodeKind.File && size >= 1024L * 1024 * 1024)
            {
                insights.Add(new InsightFinding
                {
                    ScanId = scanId,
                    NodeId = node.Id,
                    StableId = node.StableId,
                    Path = node.FullPath,
                    Tool = "Large File Triage",
                    Title = "Very large file",
                    Description = "Review whether this large file is media, an installer, a backup, a dump, or a virtual disk before moving or deleting it.",
                    Safety = CleanupSafety.Review,
                    RecommendedAction = CleanupActionKind.ArchiveOrMove,
                    SizeBytes = size,
                    Confidence = 0.66,
                    Evidence = $"Size >= 1 GB, type {node.Category}"
                });
            }
        }

        return insights;
    }

    private void UpdateProgress(ScanProgress progress)
    {
        ScanProgressValue = progress.ProgressFraction;
        ScanProgressText = progress.UsedBytes > 0
            ? $"{progress.ProgressFraction:P1} accounted"
            : "Scanning";
        AccountedBytesText = ByteFormatter.Format(progress.BytesSeen);
        UsedBytesText = progress.UsedBytes > 0 ? ByteFormatter.Format(progress.UsedBytes) : "Unknown";
        ScanThroughputText = $"{progress.FilesPerSecond:n0} files/s, {progress.DirectoriesPerSecond:n0} dirs/s";
        ScanEtaText = progress.EstimatedRemaining is { } eta && progress.UsedBytes > 0
            ? $"ETA {eta:g}"
            : "ETA unknown";
        ScanGapText = $"{progress.InaccessibleCount:n0} scan gaps";
        ScanQueueText = $"{progress.QueueDepth:n0} queued";
        ScanDetail = $"{progress.FilesScanned:n0} files, {progress.DirectoriesScanned:n0} folders, {ByteFormatter.Format(progress.BytesSeen)} of {UsedBytesText}";
    }

    private NodeTreeItemViewModel BuildTree(FileSystemNode node, int depth)
    {
        var item = new NodeTreeItemViewModel(node);
        if (depth >= 3 || !_childrenByParent.TryGetValue(node.Id, out var children))
        {
            return item;
        }

        foreach (var child in children
            .Where(c => c.Kind != FileSystemNodeKind.File)
            .Take(depth == 0 ? 80 : 50))
        {
            item.Children.Add(BuildTree(child, depth + 1));
        }

        return item;
    }

    private void RefreshExplorerRows()
    {
        NodeRows.Clear();
        if (_currentScan is null || _currentNodeId is null)
        {
            return;
        }

        var minBytes = (long)(MinimumSizeMegabytes * 1024 * 1024);
        IEnumerable<FileSystemNode> nodes;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            nodes = _nodes.Values.Where(n =>
                n.FullPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (n.Extension?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        else
        {
            nodes = _childrenByParent.TryGetValue(_currentNodeId.Value, out var children) ? children : [];
        }

        var parentSize = _nodes.TryGetValue(_currentNodeId.Value, out var parent) ? SizeOf(parent) : 0;
        foreach (var node in nodes
            .Where(n => SizeOf(n) >= minBytes)
            .OrderByDescending(SizeOf)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Take(2000))
        {
            _findingsByNode.TryGetValue(node.Id, out var finding);
            NodeRows.Add(new NodeRowViewModel(node, parentSize, finding));
        }
    }

    private void RefreshTreemap()
    {
        TreemapTiles.Clear();
        if (_currentNodeId is null || !_nodes.TryGetValue(_currentNodeId.Value, out var parent))
        {
            return;
        }

        var children = _childrenByParent.TryGetValue(parent.Id, out var childNodes) ? childNodes : [];
        foreach (var rect in _treemapLayout.Layout(parent, children, new TreemapBounds(0, 0, 1200, 520)))
        {
            TreemapTiles.Add(new TreemapTileViewModel(rect));
        }
    }

    private void RefreshSunburst()
    {
        SunburstSegments.Clear();
        if (_currentNodeId is null || !_nodes.TryGetValue(_currentNodeId.Value, out var parent))
        {
            return;
        }

        foreach (var segment in _sunburstLayout.Layout(parent, _childrenByParent, maxDepth: 4, maxSegments: 320))
        {
            SunburstSegments.Add(new SunburstSegmentViewModel(segment));
        }
    }

    private void RefreshOverviewCollections()
    {
        TopSpaceConsumers.Clear();
        TypeBreakdown.Clear();
        AgeHistogram.Clear();
        CleanupPotential.Clear();
        FiveMinuteFindings.Clear();

        if (_currentScan is null)
        {
            return;
        }

        var rootSize = _currentScan.Nodes
            .Where(n => n.ParentId is null)
            .Select(SizeOf)
            .DefaultIfEmpty(_currentScan.Session.TotalPhysicalBytes)
            .Max();

        foreach (var node in _currentScan.Nodes
            .Where(n => n.ParentId is not null)
            .OrderByDescending(SizeOf)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Take(16))
        {
            _findingsByNode.TryGetValue(node.Id, out var finding);
            TopSpaceConsumers.Add(new NodeRowViewModel(node, rootSize, finding));
        }

        foreach (var item in _breakdownService.BuildTypeBreakdown(_currentScan.Nodes))
        {
            TypeBreakdown.Add(new StorageBreakdownItemViewModel(item));
        }

        foreach (var bucket in _breakdownService.BuildAgeHistogram(_currentScan.Nodes))
        {
            AgeHistogram.Add(new AgeHistogramBucketViewModel(bucket));
        }

        foreach (var item in _breakdownService.BuildCleanupPotential(_findingsByNode.Values))
        {
            CleanupPotential.Add(new StorageBreakdownItemViewModel(item));
        }
    }

    private void RefreshConsumerTools(
        ScanResult result,
        IReadOnlyList<CleanupFinding> findings,
        IReadOnlyList<StorageRelationship> relationships,
        IReadOnlyList<ChangeRecord> changes)
    {
        ConsumerTools.Clear();

        var topFolder = result.Nodes
            .Where(n => n.ParentId is not null && n.Kind != FileSystemNodeKind.File)
            .OrderByDescending(SizeOf)
            .FirstOrDefault();
        if (topFolder is not null)
        {
            ConsumerTools.Add(new ConsumerStorageToolViewModel(
                "Find biggest folders",
                ByteFormatter.Format(SizeOf(topFolder)),
                $"{topFolder.Name} is the largest folder in the loaded scan.",
                "Show in Files",
                topFolder.Name,
                FilesWorkspaceIndex));
        }

        var safeBytes = findings.Where(f => f.Safety == CleanupSafety.Safe).Sum(f => f.SizeBytes);
        ConsumerTools.Add(new ConsumerStorageToolViewModel(
            "Review safest cleanup",
            ByteFormatter.Format(safeBytes),
            "Only low-risk cache and temporary findings are counted here.",
            "Open Cleanup",
            "cache",
            CleanupWorkspaceIndex));

        var oldDownloads = findings
            .Where(f => f.Category.Contains("Downloads", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.Contains(@"\Downloads", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.SizeBytes)
            .FirstOrDefault();
        ConsumerTools.Add(new ConsumerStorageToolViewModel(
            "Check downloads",
            oldDownloads is null ? "None flagged" : ByteFormatter.Format(oldDownloads.SizeBytes),
            oldDownloads is null ? "No large old downloads in this scan." : $"{oldDownloads.DisplayName} needs a human decision.",
            "Search downloads",
            "Downloads",
            FilesWorkspaceIndex));

        var developerBytes = findings
            .Where(f => f.Category.Contains("Developer", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.Contains("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        f.Path.Contains(@"\.venv", StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.SizeBytes);
        ConsumerTools.Add(new ConsumerStorageToolViewModel(
            "Developer bloat",
            ByteFormatter.Format(developerBytes),
            "Generated dependencies and caches can be high-yield but may need rebuild time.",
            "Filter dev paths",
            "node_modules",
            FilesWorkspaceIndex));

        var systemBytes = findings
            .Where(f => f.Safety == CleanupSafety.UseSystemCleanup)
            .Sum(f => f.SizeBytes);
        ConsumerTools.Add(new ConsumerStorageToolViewModel(
            "System cleanup routes",
            ByteFormatter.Format(systemBytes),
            "These items should use Windows or app cleanup tools, not direct deletion.",
            "Open Cleanup",
            "Windows",
            CleanupWorkspaceIndex));

        var growth = changes
            .Where(c => c.DeltaBytes > 0)
            .OrderByDescending(c => c.DeltaBytes)
            .FirstOrDefault();
        ConsumerTools.Add(new ConsumerStorageToolViewModel(
            "What grew recently",
            growth is null ? "No growth" : ByteFormatter.Format(growth.DeltaBytes),
            growth is null ? "Run another scan later to compare." : Path.GetFileName(growth.Path.TrimEnd('\\')),
            "Open What Changed",
            "",
            ChangesWorkspaceIndex));

        var owner = relationships
            .Where(r => !string.IsNullOrWhiteSpace(r.Owner))
            .GroupBy(r => r.Owner)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        ConsumerTools.Add(new ConsumerStorageToolViewModel(
            "App-owned storage",
            owner?.Key ?? "Unknown",
            owner is null ? "No app ownership evidence yet." : $"{owner.Count():n0} evidence links found.",
            "Open Explanations",
            owner?.Key ?? "",
            InsightsWorkspaceIndex));
    }

    private void RefreshFiveMinuteFindings(
        ScanResult result,
        IReadOnlyList<CleanupFinding> findings,
        IReadOnlyList<StorageRelationship> relationships,
        IReadOnlyList<ChangeRecord> changes)
    {
        FiveMinuteFindings.Clear();

        var safeBytes = findings
            .Where(f => f.Safety == CleanupSafety.Safe)
            .Sum(f => f.SizeBytes);
        var reviewBytes = findings
            .Where(f => f.Safety == CleanupSafety.Review)
            .Sum(f => f.SizeBytes);
        FiveMinuteFindings.Add(new QuickFindingViewModel(
            "Safest reclaimable",
            ByteFormatter.Format(safeBytes),
            $"{ByteFormatter.Format(reviewBytes)} more is review-only.",
            "Safe"));

        var topOwner = relationships
            .Where(r => !string.IsNullOrWhiteSpace(r.Owner))
            .GroupBy(r => r.Owner)
            .Select(g => new
            {
                Owner = g.Key,
                Size = g
                    .Select(r => _nodes.TryGetValue(r.SourceNodeId, out var node) ? SizeOf(node) : 0)
                    .Sum()
            })
            .OrderByDescending(g => g.Size)
            .FirstOrDefault();
        FiveMinuteFindings.Add(new QuickFindingViewModel(
            "Top owner signal",
            topOwner is null ? "Unknown" : topOwner.Owner,
            topOwner is null ? "Run enrichment or load demo data for ownership evidence." : $"{ByteFormatter.Format(topOwner.Size)} tied to evidence.",
            "Owner"));

        var growth = changes
            .Where(c => c.DeltaBytes > 0)
            .OrderByDescending(c => c.DeltaBytes)
            .FirstOrDefault();
        FiveMinuteFindings.Add(new QuickFindingViewModel(
            "Biggest growth",
            growth is null ? "No growth" : ByteFormatter.Format(growth.DeltaBytes),
            growth is null ? "No positive deltas in this snapshot." : $"{Path.GetFileName(growth.Path.TrimEnd('\\'))}: {growth.Reason}",
            "Growth"));

        FiveMinuteFindings.Add(new QuickFindingViewModel(
            "Scan gaps",
            result.Issues.Count.ToString("n0"),
            result.Issues.Count == 0 ? "No inaccessible paths recorded." : "Permission gaps are tracked instead of hidden.",
            result.Issues.Count == 0 ? "OK" : "Warning"));

        var localActionableCount = findings.Count(f => f.Safety is CleanupSafety.Safe or CleanupSafety.Review);
        FiveMinuteFindings.Add(new QuickFindingViewModel(
            "Local advisor",
            $"{localActionableCount:n0} candidates",
            localActionableCount == 0 ? "Run a scan to populate evidence-backed cleanup lanes." : "Recommendations are generated locally from scan evidence and safety rules.",
            "Local"));

        LocalAdvisorStatusText = localActionableCount == 0
            ? "Local cleanup advisor is waiting for scan evidence."
            : $"{localActionableCount:n0} local cleanup candidates found. Blocked/system items stay guarded.";
    }

    private void ShowCleanupSafetyGuide()
    {
        SelectedWorkspaceIndex = TutorialsWorkspaceIndex;
        StatusText = "Open the cleanup safety guide before staging recommendations.";
    }

    private void RunConsumerTool(object? parameter)
    {
        if (parameter is not ConsumerStorageToolViewModel tool)
        {
            return;
        }

        SearchText = tool.SearchQuery;
        SelectedWorkspaceIndex = tool.WorkspaceIndex;
        StatusText = $"{tool.Title}: {tool.Detail}";
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (_currentScan is null)
        {
            ReportExportStatus = "Load a scan or demo workspace before exporting diagnostics.";
            return;
        }

        try
        {
            var outputDirectory = Path.Combine(_reportsDirectory, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
            var bundle = await _reportExportService.ExportAsync(
                _currentScan,
                _findingsByNode.Values,
                _currentRelationships,
                Changes.Select(c => c.Model),
                Insights.Select(i => i.Model),
                new ReportExportOptions
                {
                    OutputDirectory = outputDirectory,
                    PathPrivacyMode = _settings.Privacy.RedactUserProfileInReports
                        ? PathPrivacyMode.RedactedUserProfile
                        : PathPrivacyMode.Raw,
                    IncludeRelationships = _settings.Privacy.IncludeRelationshipsInReports,
                    IncludeInsights = _settings.Privacy.IncludeInsightsInReports
                }).ConfigureAwait(true);

            ReportExportStatus = $"Exported {bundle.FileCount:n0} files to {RedactLocalDisplayPath(bundle.DirectoryPath)}. Redacted {bundle.RedactedPathCount:n0} path segments.";
            StatusText = "Diagnostics report exported locally.";
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{bundle.DirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportExportStatus = $"Diagnostics export failed: {ex.Message}";
        }
    }

    private void RefreshVisualLab(
        ScanResult result,
        IReadOnlyList<CleanupFinding> findings,
        IReadOnlyList<StorageRelationship> relationships,
        IReadOnlyList<ChangeRecord> changes)
    {
        VisualLabCharts.Clear();
        VisualLabAdvancedCharts.Clear();
        TutorialSteps.Clear();

        var snapshot = _analyticsService.BuildSnapshot(
            result,
            findings,
            relationships,
            changes,
            Volumes.Select(v => v.Model).ToList());

        foreach (var chart in snapshot.Charts.Where(c => !c.IsAdvanced).Take(_settings.Charts.MaxBestInsightCards))
        {
            VisualLabCharts.Add(new ChartDefinitionViewModel(chart));
        }

        foreach (var chart in snapshot.Charts.Where(c => c.IsAdvanced).Take(_settings.Charts.MaxAdvancedCards))
        {
            VisualLabAdvancedCharts.Add(new ChartDefinitionViewModel(chart));
        }

        var number = 1;
        foreach (var step in snapshot.Tutorials)
        {
            TutorialSteps.Add(new TutorialStepViewModel(step, number++));
        }

        VisualLabSummary = snapshot.Summary;
    }

    private void HandleVisualChartSelection(object? payload)
    {
        switch (payload)
        {
            case ChartPoint { NodeId: { } nodeId }:
                NavigateToNode(nodeId);
                StatusText = "Chart selection synced to the inspector.";
                break;
            case RelationshipFlow { NodeId: { } nodeId }:
                NavigateToNode(nodeId);
                StatusText = "Relationship flow synced to the inspector.";
                break;
            case ChartPoint point when !string.IsNullOrWhiteSpace(point.Path):
                StatusText = $"Chart selected {point.Path}.";
                break;
            case HeatmapCell cell:
                StatusText = $"Chart selected {cell.Label}: {cell.Detail}.";
                break;
            case RelationshipFlow flow:
                StatusText = $"Chart selected {flow.Source} -> {flow.Target}.";
                break;
        }
    }

    private void RefreshConnections()
    {
        Connections.Clear();
        EvidenceRelationships.Clear();
        if (_currentScan is null || _currentNodeId is null)
        {
            return;
        }

        foreach (var edge in _currentScan.Edges
            .Where(e => e.SourceNodeId == _currentNodeId || e.TargetNodeId == _currentNodeId)
            .Take(300))
        {
            Connections.Add(new ConnectionViewModel(edge, _nodes));
        }

        foreach (var relationship in _currentRelationships
            .Where(r => r.SourceNodeId == _currentNodeId || string.Equals(r.TargetPath, SelectedPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Evidence.Confidence)
            .Take(300))
        {
            EvidenceRelationships.Add(new StorageRelationshipViewModel(relationship));
        }
    }

    private void SelectNodeDetails(FileSystemNode node)
    {
        SelectedName = node.Name;
        SelectedPath = node.FullPath;
        SelectedSize = ByteFormatter.Format(SizeOf(node));
        SelectedCounts = node.Kind == FileSystemNodeKind.File
            ? "1 file"
            : $"{node.FileCount:n0} files / {node.FolderCount:n0} folders";
        SelectedSafety = _findingsByNode.TryGetValue(node.Id, out var finding)
            ? $"{finding.Safety}: {finding.Explanation}"
            : node.IsReparsePoint
                ? $"Linked location: {node.ReparseTarget}"
                : "No cleanup finding for this item.";
        Breadcrumb = BuildBreadcrumb(node);
        OpenSelectedCommand.RaiseCanExecuteChanged();
    }

    private string BuildBreadcrumb(FileSystemNode node)
    {
        var stack = new Stack<string>();
        var current = node;
        while (true)
        {
            stack.Push(current.Name);
            if (current.ParentId is not { } parentId || !_nodes.TryGetValue(parentId, out current!))
            {
                break;
            }
        }

        return string.Join(" > ", stack);
    }

    private void OpenSelectedPath()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath))
        {
            return;
        }

        var args = File.Exists(SelectedPath)
            ? $"/select,\"{SelectedPath}\""
            : $"\"{SelectedPath}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    private void StageSelectedFinding()
    {
        if (SelectedFinding is null)
        {
            return;
        }

        var result = _cleanupReviewQueueService.TryStage(_cleanupReviewQueue, SelectedFinding.Model);
        _cleanupReviewQueue = result.Queue;
        RefreshCleanupReviewQueue();

        var plan = _cleanupPlanBuilder.Build(_cleanupReviewQueue.Items.Select(ToFinding));
        PlannedCleanupSize = ByteFormatter.Format(plan.EstimatedReclaimableBytes);
        StatusText = result.Message;
    }

    private void RemoveSelectedCleanupReviewItem()
    {
        if (SelectedCleanupReviewItem is null)
        {
            return;
        }

        _cleanupReviewQueue = _cleanupReviewQueueService.Remove(_cleanupReviewQueue, SelectedCleanupReviewItem.Model.FindingId);
        SelectedCleanupReviewItem = null;
        RefreshCleanupReviewQueue();
        StatusText = "Removed the item from the cleanup review queue.";
    }

    private void ClearCleanupReviewQueue()
    {
        _cleanupReviewQueue = _cleanupReviewQueueService.Clear(_cleanupReviewQueue);
        SelectedCleanupReviewItem = null;
        RefreshCleanupReviewQueue();
        StatusText = "Cleanup review queue cleared.";
    }

    private async Task ExportCleanupReviewAsync()
    {
        if (_cleanupReviewQueue.Items.Count == 0)
        {
            StatusText = "Stage at least one cleanup item before exporting a review checklist.";
            return;
        }

        try
        {
            var outputDirectory = Path.Combine(_reportsDirectory, "cleanup-review-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
            var export = await _cleanupReviewQueueService.ExportAsync(
                _cleanupReviewQueue,
                outputDirectory,
                _settings.Privacy.RedactUserProfileInReports ? PathPrivacyMode.RedactedUserProfile : PathPrivacyMode.Raw)
                .ConfigureAwait(true);
            StatusText = $"Cleanup review exported locally. Redacted {export.RedactedPathCount:n0} path segments.";
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{export.DirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Cleanup review export failed: {ex.Message}";
        }
    }

    private void RefreshCleanupReviewQueue()
    {
        CleanupReviewItems.Clear();
        foreach (var item in _cleanupReviewQueue.Items
                     .OrderBy(item => item.Safety)
                     .ThenByDescending(item => item.SizeBytes))
        {
            CleanupReviewItems.Add(new CleanupReviewItemViewModel(item));
        }

        PlannedCleanupSize = ByteFormatter.Format(_cleanupReviewQueue.TotalBytes);
        CleanupReviewSummary = CleanupReviewItems.Count == 0
            ? "Nothing is staged. Select a Safe or Review finding and add it to this queue before taking action outside the app."
            : $"{CleanupReviewItems.Count:n0} item(s), {_cleanupReviewQueue.TotalFileCount:n0} file(s), {ByteFormatter.Format(_cleanupReviewQueue.TotalBytes)} staged for manual review.";
        ClearCleanupQueueCommand.RaiseCanExecuteChanged();
        ExportCleanupReviewCommand.RaiseCanExecuteChanged();
    }

    private static CleanupFinding ToFinding(CleanupReviewItem item)
    {
        return new CleanupFinding
        {
            Id = item.FindingId,
            NodeId = item.NodeId,
            Path = item.Path,
            DisplayName = item.DisplayName,
            Category = item.Category,
            Safety = item.Safety,
            RecommendedAction = item.RecommendedAction,
            SizeBytes = item.SizeBytes,
            FileCount = item.FileCount,
            Confidence = item.Confidence,
            Explanation = item.Explanation
        };
    }

    private void OpenShellTarget(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open {target}: {ex.Message}";
        }
    }

    private void ResetWelcomeState()
    {
        IsFirstRunWelcomeVisible = true;
        StatusText = "First-run welcome is visible again.";
    }

    private void ClearLocalDatabase()
    {
        if (IsScanning)
        {
            StatusText = "Stop the active scan before clearing local scan data.";
            return;
        }

        var choice = MessageBox.Show(
            "Clear the local scan database? This removes saved Disk Space Inspector snapshots on this PC only. It does not touch scanned files.",
            "Clear local scan data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            _currentScan = null;
            _nodes.Clear();
            _childrenByParent.Clear();
            _findingsByNode.Clear();
            CleanupFindings.Clear();
            CleanupReviewItems.Clear();
            TopSpaceConsumers.Clear();
            NodeRows.Clear();
            TreeNodes.Clear();
            TreemapTiles.Clear();
            SunburstSegments.Clear();
            Changes.Clear();
            Insights.Clear();
            ConsumerTools.Clear();
            StatusText = "Local scan database cleared. Run a new scan or open demo mode.";
            IsFirstRunWelcomeVisible = _settings.Launch.ShowWelcomeWhenNoScan;
        }
        catch (Exception ex)
        {
            StatusText = $"Could not clear local scan database: {ex.Message}";
        }
    }

    private static ScanResult MergeResults(IReadOnlyList<ScanResult> results)
    {
        if (results.Count == 1)
        {
            return results[0];
        }

        var root = new FileSystemNode
        {
            Id = 1,
            Name = "All scanned drives",
            FullPath = "disk-space-inspector://all-drives",
            Kind = FileSystemNodeKind.Drive,
            Category = "Drive"
        };

        var nodes = new List<FileSystemNode> { root };
        var edges = new List<FileSystemEdge>();
        var issues = new List<ScanIssue>();
        var nodeOffset = 1L;
        var edgeOffset = 0L;
        var issueOffset = 0L;

        foreach (var result in results)
        {
            var map = new Dictionary<long, long>();
            foreach (var node in result.Nodes)
            {
                var newId = node.Id + nodeOffset;
                map[node.Id] = newId;
                nodes.Add(new FileSystemNode
                {
                    Id = newId,
                    ParentId = node.ParentId is null ? root.Id : node.ParentId.Value + nodeOffset,
                    Name = node.Name,
                    FullPath = node.FullPath,
                    Kind = node.Kind,
                    Extension = node.Extension,
                    Length = node.Length,
                    AllocatedLength = node.AllocatedLength,
                    PhysicalLength = node.PhysicalLength,
                    TotalLength = node.TotalLength,
                    TotalPhysicalLength = node.TotalPhysicalLength,
                    FileCount = node.FileCount,
                    FolderCount = node.FolderCount,
                    Depth = node.Depth + 1,
                    LastModifiedUtc = node.LastModifiedUtc,
                    Attributes = node.Attributes,
                    IsReparsePoint = node.IsReparsePoint,
                    ReparseTarget = node.ReparseTarget,
                    VolumeSerial = node.VolumeSerial,
                    FileId = node.FileId,
                    HardLinkCount = node.HardLinkCount,
                    IsHardLinkDuplicate = node.IsHardLinkDuplicate,
                    IsInaccessiblePlaceholder = node.IsInaccessiblePlaceholder,
                    Category = node.Category
                });
            }

            edges.AddRange(result.Edges.Select(edge => new FileSystemEdge
            {
                Id = edge.Id + edgeOffset,
                SourceNodeId = map.GetValueOrDefault(edge.SourceNodeId, edge.SourceNodeId + nodeOffset),
                TargetNodeId = edge.TargetNodeId is { } targetId && map.TryGetValue(targetId, out var mappedTarget)
                    ? mappedTarget
                    : edge.TargetNodeId,
                TargetPath = edge.TargetPath,
                Kind = edge.Kind,
                Label = edge.Label,
                Evidence = edge.Evidence
            }));

            issues.AddRange(result.Issues.Select(issue => new ScanIssue
            {
                Id = issue.Id + issueOffset,
                NodeId = issue.NodeId is { } nodeId && map.TryGetValue(nodeId, out var mappedNodeId) ? mappedNodeId : issue.NodeId,
                Path = issue.Path,
                Operation = issue.Operation,
                Message = issue.Message,
                NativeErrorCode = issue.NativeErrorCode,
                ElevationMayHelp = issue.ElevationMayHelp,
                OccurredAtUtc = issue.OccurredAtUtc
            }));

            nodeOffset = nodes.Max(n => n.Id);
            edgeOffset = edges.Count == 0 ? edgeOffset : edges.Max(e => e.Id);
            issueOffset = issues.Count == 0 ? issueOffset : issues.Max(i => i.Id);
        }

        root.TotalLength = results.Sum(r => r.Session.TotalLogicalBytes);
        root.TotalPhysicalLength = results.Sum(r => r.Session.TotalPhysicalBytes);
        root.FileCount = results.Sum(r => (int)Math.Min(int.MaxValue, r.Session.FilesScanned));
        root.FolderCount = results.Sum(r => (int)Math.Min(int.MaxValue, r.Session.DirectoriesScanned));

        return new ScanResult
        {
            Session = new ScanSession
            {
                RootPath = root.FullPath,
                Status = results.All(r => r.Session.Status == ScanStatus.Completed) ? ScanStatus.Completed : ScanStatus.Cancelled,
                StartedAtUtc = results.Min(r => r.Session.StartedAtUtc),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TotalLogicalBytes = root.TotalLength,
                TotalPhysicalBytes = root.TotalPhysicalLength,
                FilesScanned = results.Sum(r => r.Session.FilesScanned),
                DirectoriesScanned = results.Sum(r => r.Session.DirectoriesScanned),
                IssueCount = issues.Count
            },
            Volume = new VolumeInfo
            {
                Name = "All drives",
                RootPath = root.FullPath,
                DriveType = "Aggregate",
                IsReady = true,
                TotalBytes = results.Sum(r => r.Volume.TotalBytes),
                FreeBytes = results.Sum(r => r.Volume.FreeBytes)
            },
            Nodes = nodes,
            Edges = edges,
            Issues = issues
        };

    }

    private static long SizeOf(FileSystemNode node)
    {
        return Math.Max(node.TotalPhysicalLength, node.PhysicalLength);
    }
}
