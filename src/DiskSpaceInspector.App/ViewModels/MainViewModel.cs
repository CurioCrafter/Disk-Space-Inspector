using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Ai;
using DiskSpaceInspector.Core.Cleanup;
using DiskSpaceInspector.Core.Layout;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Scanning;
using DiskSpaceInspector.Core.Services;
using DiskSpaceInspector.Core.Windows;
using DiskSpaceInspector.Storage;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IDriveDiscoveryService _driveDiscovery;
    private readonly IFileSystemScanner _scanner;
    private readonly ICleanupClassifier _classifier;
    private readonly ITreemapLayoutService _treemapLayout;
    private readonly ISunburstLayoutService _sunburstLayout;
    private readonly IStorageBreakdownService _breakdownService;
    private readonly IStorageAnalyticsService _analyticsService;
    private readonly IScanStore _scanStore;
    private readonly IAiCleanupAdvisor _aiAdvisor;
    private readonly ICodexAuthService _codexAuthService;
    private readonly CleanupPlanBuilder _cleanupPlanBuilder = new();

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
    private CodexAuthStatus _codexAuthStatus = new();
    private string _aiStatusText = "Codex AI uses your Codex ChatGPT login. Click Check Codex status or Login with Codex.";
    private string _aiRecommendationSummary = "No AI recommendations yet.";
    private string _visualLabSummary = "Run a scan or open demo mode to generate visual analytics.";
    private bool _isAiBusy;
    private AiCleanupRecommendationViewModel? _selectedAiRecommendation;

    public MainViewModel()
        : this(
            new WindowsDriveDiscoveryService(),
            new FileSystemScanner(new WindowsRelationshipResolver()),
            new CleanupClassifier(),
            new SquarifiedTreemapLayoutService(),
            new SunburstLayoutService(),
            new StorageBreakdownService(),
            new StorageAnalyticsService(),
            new SqliteScanStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Disk Space Inspector",
                "disk-space-inspector.db")),
            new CodexCliCleanupAdvisor(),
            new CodexAuthService(),
            IsDemoMode())
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
        IAiCleanupAdvisor? aiAdvisor = null,
        ICodexAuthService? codexAuthService = null,
        bool demoMode = false)
    {
        _driveDiscovery = driveDiscovery;
        _scanner = scanner;
        _classifier = classifier;
        _treemapLayout = treemapLayout;
        _sunburstLayout = sunburstLayout;
        _breakdownService = breakdownService;
        _analyticsService = analyticsService;
        _scanStore = scanStore;
        _aiAdvisor = aiAdvisor ?? new CodexCliCleanupAdvisor();
        _codexAuthService = codexAuthService ?? new CodexAuthService();

        RefreshDrivesCommand = new RelayCommand(_ => RefreshDrives());
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
        LoginWithCodexCommand = new AsyncRelayCommand(LoginWithCodexAsync, () => !IsAiBusy);
        CheckCodexStatusCommand = new AsyncRelayCommand(CheckCodexStatusAsync, () => !IsAiBusy);
        AskAiCleanupAdvisorCommand = new AsyncRelayCommand(AskAiCleanupAdvisorAsync, CanAskAiCleanupAdvisor);
        StageAiRecommendationCommand = new RelayCommand(_ => StageSelectedAiRecommendation(), _ => SelectedAiRecommendation is not null);
        VisualChartSelectedCommand = new RelayCommand(HandleVisualChartSelection);

        if (demoMode)
        {
            LoadDemoData();
        }
        else
        {
            RefreshDrives();
        }
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

    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public ObservableCollection<StorageRelationshipViewModel> EvidenceRelationships { get; } = [];

    public ObservableCollection<ChangeRecordViewModel> Changes { get; } = [];

    public ObservableCollection<InsightFindingViewModel> Insights { get; } = [];

    public ObservableCollection<AiCleanupRecommendationViewModel> AiRecommendations { get; } = [];

    public ObservableCollection<ChartDefinitionViewModel> VisualLabCharts { get; } = [];

    public ObservableCollection<ChartDefinitionViewModel> VisualLabAdvancedCharts { get; } = [];

    public ObservableCollection<TutorialStepViewModel> TutorialSteps { get; } = [];

    public RelayCommand RefreshDrivesCommand { get; }

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

    public AsyncRelayCommand LoginWithCodexCommand { get; }

    public AsyncRelayCommand CheckCodexStatusCommand { get; }

    public AsyncRelayCommand AskAiCleanupAdvisorCommand { get; }

    public RelayCommand StageAiRecommendationCommand { get; }

    public RelayCommand VisualChartSelectedCommand { get; }

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

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ScanSelectedCommand.RaiseCanExecuteChanged();
                ScanAllCommand.RaiseCanExecuteChanged();
                CancelScanCommand.RaiseCanExecuteChanged();
                PauseScanCommand.RaiseCanExecuteChanged();
                ResumeScanCommand.RaiseCanExecuteChanged();
                LoadLatestCommand.RaiseCanExecuteChanged();
                AskAiCleanupAdvisorCommand.RaiseCanExecuteChanged();
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

    public string AiStatusText
    {
        get => _aiStatusText;
        private set => SetProperty(ref _aiStatusText, value);
    }

    public string AiRecommendationSummary
    {
        get => _aiRecommendationSummary;
        private set => SetProperty(ref _aiRecommendationSummary, value);
    }

    public string VisualLabSummary
    {
        get => _visualLabSummary;
        private set => SetProperty(ref _visualLabSummary, value);
    }

    public bool IsAiBusy
    {
        get => _isAiBusy;
        private set
        {
            if (SetProperty(ref _isAiBusy, value))
            {
                LoginWithCodexCommand.RaiseCanExecuteChanged();
                CheckCodexStatusCommand.RaiseCanExecuteChanged();
                AskAiCleanupAdvisorCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AiCleanupRecommendationViewModel? SelectedAiRecommendation
    {
        get => _selectedAiRecommendation;
        set
        {
            if (SetProperty(ref _selectedAiRecommendation, value))
            {
                StageAiRecommendationCommand.RaiseCanExecuteChanged();
            }
        }
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

    private void RefreshDrives()
    {
        Volumes.Clear();
        foreach (var volume in _driveDiscovery.GetVolumes())
        {
            Volumes.Add(new VolumeViewModel(volume));
        }

        SelectedVolume = Volumes.FirstOrDefault(v => v.Model.IsReady);
        StatusText = Volumes.Count == 0 ? "No filesystem drives were discovered." : "Drives refreshed.";
        ScanAllCommand.RaiseCanExecuteChanged();
    }

    private void LoadDemoData()
    {
        var demo = DemoDataFactory.Create();

        Volumes.Clear();
        foreach (var volume in demo.Volumes)
        {
            Volumes.Add(new VolumeViewModel(volume));
        }

        SelectedVolume = Volumes.FirstOrDefault();
        LoadScan(demo.Scan, demo.CleanupFindings, demo.Relationships, demo.Changes, demo.Insights);

        AiRecommendations.Clear();
        foreach (var recommendation in demo.AiRecommendations)
        {
            AiRecommendations.Add(new AiCleanupRecommendationViewModel(recommendation));
        }

        SelectedAiRecommendation = AiRecommendations.FirstOrDefault();
        AiRecommendationSummary = $"{AiRecommendations.Count:n0} demo AI recommendations. Safety remains constrained by app guardrails.";
        AiStatusText = "Demo mode: Codex recommendations are seeded sample data.";
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
    }

    private static bool IsDemoMode()
    {
        return Environment.GetCommandLineArgs()
            .Any(arg => string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase));
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
            .Where(v => v.Model.DriveType is "Fixed" or "Removable")
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

        Changes.Clear();
        foreach (var change in changeList.OrderByDescending(c => Math.Abs(c.DeltaBytes)).Take(2000))
        {
            Changes.Add(new ChangeRecordViewModel(change));
        }

        Insights.Clear();
        foreach (var insight in insightList.OrderByDescending(i => i.SizeBytes).ThenByDescending(i => i.Confidence).Take(2000))
        {
            Insights.Add(new InsightFindingViewModel(insight));
        }

        AiRecommendations.Clear();
        SelectedAiRecommendation = null;
        AiRecommendationSummary = "No AI recommendations yet.";
        AskAiCleanupAdvisorCommand.RaiseCanExecuteChanged();

        EvidenceRelationships.Clear();
        foreach (var relationship in _currentRelationships.OrderByDescending(r => r.Evidence.Confidence).Take(2000))
        {
            EvidenceRelationships.Add(new StorageRelationshipViewModel(relationship));
        }

        RefreshOverviewCollections();
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

        foreach (var chart in snapshot.Charts.Where(c => !c.IsAdvanced).Take(16))
        {
            VisualLabCharts.Add(new ChartDefinitionViewModel(chart));
        }

        foreach (var chart in snapshot.Charts.Where(c => c.IsAdvanced).Take(32))
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
                StatusText = "Visual Lab selection synced to the inspector.";
                break;
            case RelationshipFlow { NodeId: { } nodeId }:
                NavigateToNode(nodeId);
                StatusText = "Relationship flow synced to the inspector.";
                break;
            case ChartPoint point when !string.IsNullOrWhiteSpace(point.Path):
                StatusText = $"Visual Lab selected {point.Path}.";
                break;
            case HeatmapCell cell:
                StatusText = $"Visual Lab selected {cell.Label}: {cell.Detail}.";
                break;
            case RelationshipFlow flow:
                StatusText = $"Visual Lab selected {flow.Source} -> {flow.Target}.";
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

        var plan = _cleanupPlanBuilder.Build([SelectedFinding.Model]);
        PlannedCleanupSize = ByteFormatter.Format(plan.EstimatedReclaimableBytes);
        StatusText = plan.Findings.Count == 0
            ? "This item is not eligible for direct cleanup. Use the recommended app or system route."
            : $"Staged {SelectedFinding.DisplayName} for review. No files are deleted from this version.";
    }

    private async Task LoginWithCodexAsync()
    {
        IsAiBusy = true;
        AiStatusText = "Opening Codex login. Complete the ChatGPT sign-in flow in the external window.";
        try
        {
            await _codexAuthService.StartLoginAsync().ConfigureAwait(true);
            await PollCodexStatusAfterLoginAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AiStatusText = $"Could not start Codex login: {ex.Message}";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    private async Task PollCodexStatusAfterLoginAsync()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            var status = await _codexAuthService.GetStatusAsync().ConfigureAwait(true);
            ApplyCodexStatus(status);
            if (status.Kind == CodexAuthKind.ChatGpt)
            {
                return;
            }
        }

        AiStatusText = "Codex login window opened. Click Check Codex status after finishing login.";
    }

    private async Task CheckCodexStatusAsync()
    {
        IsAiBusy = true;
        AiStatusText = "Checking Codex login status.";
        try
        {
            ApplyCodexStatus(await _codexAuthService.GetStatusAsync().ConfigureAwait(true));
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    private bool CanAskAiCleanupAdvisor()
    {
        return !IsScanning &&
               !IsAiBusy &&
               _currentScan is not null &&
               _findingsByNode.Count > 0 &&
               _codexAuthStatus.CanUseChatGpt;
    }

    private async Task AskAiCleanupAdvisorAsync()
    {
        if (_currentScan is null)
        {
            AiStatusText = "Load or run a scan before asking AI for cleanup recommendations.";
            return;
        }

        var codexStatus = await _codexAuthService.GetStatusAsync().ConfigureAwait(true);
        ApplyCodexStatus(codexStatus);
        if (!codexStatus.CanUseChatGpt)
        {
            AiStatusText = $"{codexStatus.DisplayText} Use Login with Codex, then try Ask Codex AI again.";
            return;
        }

        var findings = _findingsByNode.Values
            .OrderBy(f => f.Safety)
            .ThenByDescending(f => f.SizeBytes)
            .Take(120)
            .ToList();

        if (findings.Count == 0)
        {
            AiStatusText = "No cleanup findings are available for AI to rank.";
            return;
        }

        IsAiBusy = true;
        AiStatusText = "Asking AI to rank cleanup candidates.";
        AiRecommendationSummary = "AI analysis running.";

        try
        {
            var request = new AiCleanupAdvisorRequest
            {
                ScanId = _currentScan.Session.Id,
                CleanupFindings = findings,
                Insights = Insights.Select(i => i.Model).ToList(),
                Relationships = _currentRelationships
            };
            var options = new AiCleanupAdvisorOptions
            {
                Model = "Codex",
                MaxCandidateCount = 80,
                MaxRecommendations = 25
            };

            var recommendations = await _aiAdvisor.RecommendAsync(request, options).ConfigureAwait(true);
            AiRecommendations.Clear();
            foreach (var recommendation in recommendations)
            {
                AiRecommendations.Add(new AiCleanupRecommendationViewModel(recommendation));
            }

            AiRecommendationSummary = recommendations.Count == 0
                ? "AI did not return any recommendations from the provided candidates."
                : $"{recommendations.Count:n0} AI recommendations. {recommendations.Count(r => r.CanStage):n0} can be staged for review.";
            AiStatusText = "AI recommendations loaded. Safety is still constrained by Disk Space Inspector rules.";
        }
        catch (Exception ex)
        {
            AiStatusText = $"AI request failed: {ex.Message}";
            AiRecommendationSummary = "AI recommendations unavailable.";
        }
        finally
        {
            IsAiBusy = false;
        }
    }

    private void ApplyCodexStatus(CodexAuthStatus status)
    {
        _codexAuthStatus = status;
        AiStatusText = status.Detail.Length > 0
            ? $"{status.DisplayText} {status.Detail}"
            : status.DisplayText;
        AskAiCleanupAdvisorCommand.RaiseCanExecuteChanged();
    }

    private void StageSelectedAiRecommendation()
    {
        if (SelectedAiRecommendation is null)
        {
            return;
        }

        var recommendation = SelectedAiRecommendation.Model;
        var finding = _findingsByNode.Values.FirstOrDefault(f => f.Id == recommendation.SourceFindingId);
        if (finding is null)
        {
            StatusText = "The AI recommendation no longer matches a loaded cleanup finding.";
            return;
        }

        var plan = _cleanupPlanBuilder.Build([finding]);
        PlannedCleanupSize = ByteFormatter.Format(plan.EstimatedReclaimableBytes);
        StatusText = recommendation.CanStage && plan.Findings.Count > 0
            ? $"Staged AI recommendation for {recommendation.DisplayName}. No files are deleted from this version."
            : "AI recommendation is advisory only for this item. Use the listed system/app route or leave it alone.";
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
