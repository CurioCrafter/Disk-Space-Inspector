using System.Collections.Concurrent;
using System.IO;
using System.IO.Enumeration;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;
using DiskSpaceInspector.Core.Windows;

namespace DiskSpaceInspector.Core.Scanning;

public sealed class FileSystemScanner : IFileSystemScanner
{
    private readonly IRelationshipResolver _relationshipResolver;
    private readonly IStorageRelationshipResolver? _storageRelationshipResolver;
    private long _nextNodeId;
    private long _nextEdgeId;
    private long _nextIssueId;

    public FileSystemScanner(IRelationshipResolver relationshipResolver)
    {
        _relationshipResolver = relationshipResolver;
        _storageRelationshipResolver = relationshipResolver as IStorageRelationshipResolver;
    }

    public async Task<ScanCompleted> StartScanAsync(
        ScanRequest request,
        Func<ScanBatch, CancellationToken, Task> onBatchAsync,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<FileSystemNode>();
        var edges = new List<FileSystemEdge>();
        var issues = new List<ScanIssue>();
        var relationships = new List<StorageRelationship>();

        var completed = await ScanInternalAsync(
            request,
            async (batch, token) =>
            {
                nodes.AddRange(batch.Nodes);
                edges.AddRange(batch.Edges);
                issues.AddRange(batch.Issues);
                relationships.AddRange(batch.Relationships);
                await onBatchAsync(batch, token).ConfigureAwait(false);
            },
            progress,
            cancellationToken).ConfigureAwait(false);

        return completed;
    }

    public async Task<ScanResult> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var batches = new List<ScanBatch>();
        var completed = await ScanInternalAsync(
            request,
            (batch, _) =>
            {
                batches.Add(batch);
                return Task.CompletedTask;
            },
            progress,
            cancellationToken).ConfigureAwait(false);

        var nodes = batches
            .SelectMany(b => b.Nodes)
            .GroupBy(n => n.Id)
            .Select(g => g.Last())
            .OrderBy(n => n.Id)
            .ToList();
        var edges = batches
            .SelectMany(b => b.Edges)
            .GroupBy(e => e.Id)
            .Select(g => g.Last())
            .OrderBy(e => e.Id)
            .ToList();
        var issues = batches
            .SelectMany(b => b.Issues)
            .GroupBy(i => i.Id)
            .Select(g => g.Last())
            .OrderBy(i => i.Id)
            .ToList();

        return new ScanResult
        {
            Session = completed.Session,
            Volume = completed.Volume,
            Nodes = nodes,
            Edges = edges,
            Issues = issues
        };
    }

    private async Task<ScanCompleted> ScanInternalAsync(
        ScanRequest request,
        Func<ScanBatch, CancellationToken, Task> onBatchAsync,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        _nextNodeId = 0;
        _nextEdgeId = 0;
        _nextIssueId = 0;

        var session = new ScanSession
        {
            RootPath = request.Volume.RootPath,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = ScanStatus.Running
        };

        var startedAt = DateTimeOffset.UtcNow;
        var nodes = new ConcurrentDictionary<long, FileSystemNode>();
        var edges = new ConcurrentBag<FileSystemEdge>();
        var issues = new ConcurrentBag<ScanIssue>();
        var relationships = new ConcurrentBag<StorageRelationship>();
        var pendingNodes = new ConcurrentQueue<FileSystemNode>();
        var pendingEdges = new ConcurrentQueue<FileSystemEdge>();
        var pendingIssues = new ConcurrentQueue<ScanIssue>();
        var pendingRelationships = new ConcurrentQueue<StorageRelationship>();
        var hardlinkFirstNode = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var parentIdentityByNode = new ConcurrentDictionary<long, NodeIdentity>();

        var rootPath = EnsureTrailingSeparator(request.Volume.RootPath);
        var rootIdentity = PathIdentity.Create(rootPath, request.Volume.VolumeSerial, null, null, null);
        var root = new FileSystemNode
        {
            Id = NextNodeId(),
            StableId = rootIdentity.StableId,
            PathHash = rootIdentity.PathHash,
            ParentId = null,
            Name = request.Volume.DisplayName,
            FullPath = rootPath,
            Kind = FileSystemNodeKind.Drive,
            Depth = 0,
            Category = "Drive",
            Attributes = FileAttributes.Directory
        };
        nodes[root.Id] = root;
        parentIdentityByNode[root.Id] = rootIdentity;
        pendingNodes.Enqueue(root);

        var queue = new ConcurrentQueue<DirectoryWork>();
        using var signal = new SemaphoreSlim(0);
        var remaining = 0;
        var completed = 0;
        long filesScanned = 0;
        long directoriesScanned = 0;
        long bytesSeen = 0;
        long lastProgressTicks = 0;
        long lastFlushTicks = 0;
        var batchNumber = 0;
        var usedBytes = Math.Max(0, request.Volume.TotalBytes - request.Volume.FreeBytes);
        if (usedBytes == 0)
        {
            usedBytes = request.Volume.TotalBytes;
        }

        Enqueue(new DirectoryWork(root.Id, rootPath, 0));

        var workerCount = Math.Clamp(request.MaxConcurrency, 1, 16);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(WorkerAsync, cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
            session.Status = cancellationToken.IsCancellationRequested ? ScanStatus.Cancelled : ScanStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            session.Status = ScanStatus.Cancelled;
        }
        catch (Exception ex)
        {
            session.Status = ScanStatus.Failed;
            issues.Add(NewIssue(rootPath, "scan", ex, null, false));
        }
        finally
        {
            session.CompletedAtUtc = DateTimeOffset.UtcNow;
            await FlushBatchAsync(force: true).ConfigureAwait(false);
        }

        var nodeList = nodes.Values.OrderBy(n => n.Id).ToList();
        Aggregate(nodeList);
        await FlushAggregateBatchAsync(nodeList).ConfigureAwait(false);

        root.TotalLength = nodeList.Single(n => n.Id == root.Id).TotalLength;
        root.TotalPhysicalLength = nodeList.Single(n => n.Id == root.Id).TotalPhysicalLength;
        session.TotalLogicalBytes = root.TotalLength;
        session.TotalPhysicalBytes = root.TotalPhysicalLength;
        session.FilesScanned = filesScanned;
        session.DirectoriesScanned = directoriesScanned;
        session.IssueCount = issues.Count;

        return new ScanCompleted
        {
            Session = session,
            Volume = request.Volume,
            FinalMetrics = CreateMetrics(rootPath, Volatile.Read(ref bytesSeen), Volatile.Read(ref filesScanned), Volatile.Read(ref directoriesScanned), issues.Count, queue.Count, batchNumber, startedAt),
            BatchCount = batchNumber
        };

        async Task WorkerAsync()
        {
            while (true)
            {
                request.PauseGate?.Wait(cancellationToken);
                await signal.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (Volatile.Read(ref completed) == 1 && queue.IsEmpty)
                {
                    return;
                }

                if (!queue.TryDequeue(out var work))
                {
                    continue;
                }

                try
                {
                    ProcessDirectory(work);
                }
                finally
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        Volatile.Write(ref completed, 1);
                        for (var i = 0; i < workerCount; i++)
                        {
                            signal.Release();
                        }
                    }
                }
            }
        }

        void ProcessDirectory(DirectoryWork work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.PauseGate?.Wait(cancellationToken);

            Interlocked.Increment(ref directoriesScanned);
            ReportProgressIfNeeded(work.Path, force: false);

            IEnumerable<FastFileSystemEntry> entries;
            try
            {
                entries = EnumerateEntries(work.Path, request);
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                AddIssue(NewIssue(work.Path, "enumerate", ex, work.ParentId, true));
                return;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                request.PauseGate?.Wait(cancellationToken);

                try
                {
                    var attributes = entry.Attributes;
                    var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                    var isReparse = attributes.HasFlag(FileAttributes.ReparsePoint);
                    var nodeId = NextNodeId();
                    var name = entry.Name;
                    var path = entry.FullName;
                    var kind = isDirectory
                        ? isReparse ? FileSystemNodeKind.ReparsePoint : FileSystemNodeKind.Directory
                        : FileSystemNodeKind.File;
                    var length = 0L;
                    var allocated = 0L;
                    var physical = 0L;

                    if (!isDirectory)
                    {
                        length = entry.Length;
                        allocated = request.PipelineOptions.PerformanceMode == ScanPerformanceMode.FastFirstScan
                            ? length
                            : WindowsFileMetadata.GetAllocatedSize(path, length);
                        physical = allocated;
                    }

                    var identity = request.PipelineOptions.PerformanceMode == ScanPerformanceMode.FastFirstScan
                        ? null
                        : WindowsFileMetadata.TryGetIdentity(path, isDirectory);
                    parentIdentityByNode.TryGetValue(work.ParentId, out var parentIdentity);
                    var nodeIdentity = PathIdentity.Create(
                        path,
                        identity?.VolumeSerial ?? request.Volume.VolumeSerial,
                        identity?.FileId,
                        parentIdentity?.StableId,
                        parentIdentity?.PathHash);
                    if (!isDirectory && identity is not null)
                    {
                        if (identity.HardLinkCount > 1)
                        {
                            var key = $"{identity.VolumeSerial}:{identity.FileId}";
                            if (hardlinkFirstNode.TryGetValue(key, out var firstNodeId))
                            {
                                physical = 0;
                                var hardlinkEdge = new FileSystemEdge
                                {
                                    Id = NextEdgeId(),
                                    SourceNodeId = nodeId,
                                    TargetNodeId = firstNodeId,
                                    Kind = FileSystemEdgeKind.HardlinkSibling,
                                    Label = "hardlink sibling",
                                    Evidence = key
                                };
                                edges.Add(hardlinkEdge);
                                pendingEdges.Enqueue(hardlinkEdge);
                            }
                            else
                            {
                                hardlinkFirstNode[key] = nodeId;
                            }
                        }
                    }

                    var node = new FileSystemNode
                    {
                        Id = nodeId,
                        StableId = nodeIdentity.StableId,
                        PathHash = nodeIdentity.PathHash,
                        ParentStableId = nodeIdentity.ParentStableId,
                        ParentPathHash = nodeIdentity.ParentPathHash,
                        ParentId = work.ParentId,
                        Name = name,
                        FullPath = path,
                        Kind = kind,
                        Extension = isDirectory ? null : Path.GetExtension(name),
                        Length = length,
                        AllocatedLength = allocated,
                        PhysicalLength = physical,
                        TotalLength = isDirectory ? 0 : length,
                        TotalPhysicalLength = isDirectory ? 0 : physical,
                        FileCount = isDirectory ? 0 : 1,
                        FolderCount = 0,
                        Depth = work.Depth + 1,
                        LastModifiedUtc = entry.LastModifiedUtc,
                        Attributes = attributes,
                        IsReparsePoint = isReparse,
                        ReparseTarget = entry.LinkTarget,
                        VolumeSerial = identity?.VolumeSerial,
                        FileId = identity?.FileId,
                        HardLinkCount = identity?.HardLinkCount ?? 0,
                        IsHardLinkDuplicate = physical == 0 && identity?.HardLinkCount > 1,
                        Category = Categorize(name, isDirectory, isReparse)
                    };

                    nodes[nodeId] = node;
                    parentIdentityByNode[nodeId] = nodeIdentity;
                    pendingNodes.Enqueue(node);
                    Interlocked.Add(ref bytesSeen, physical);
                    if (!isDirectory)
                    {
                        Interlocked.Increment(ref filesScanned);
                    }

                    if (request.PipelineOptions.ResolveRelationshipsDuringScan)
                    {
                        foreach (var edge in _relationshipResolver.Resolve(node))
                        {
                            var storedEdge = new FileSystemEdge
                            {
                                Id = NextEdgeId(),
                                SourceNodeId = edge.SourceNodeId,
                                TargetNodeId = edge.TargetNodeId,
                                TargetPath = edge.TargetPath,
                                Kind = edge.Kind,
                                Label = edge.Label,
                                Evidence = edge.Evidence
                            };
                            edges.Add(storedEdge);
                            pendingEdges.Enqueue(storedEdge);
                        }
                    }

                    if (request.PipelineOptions.ResolveStorageOwnershipDuringScan && _storageRelationshipResolver is not null)
                    {
                        foreach (var relationship in _storageRelationshipResolver.ResolveRelationships(node, session.Id))
                        {
                            relationships.Add(relationship);
                            pendingRelationships.Enqueue(relationship);
                        }
                    }

                    if (isDirectory && !isReparse)
                    {
                        Enqueue(new DirectoryWork(nodeId, path, work.Depth + 1));
                    }

                    FlushBatchIfNeeded();
                    ReportProgressIfNeeded(path, force: false);
                }
                catch (Exception ex) when (IsExpectedFileSystemException(ex))
                {
                    AddIssue(NewIssue(entry.FullName, "inspect", ex, work.ParentId, true));
                }
            }
        }

        void Enqueue(DirectoryWork work)
        {
            Interlocked.Increment(ref remaining);
            queue.Enqueue(work);
            signal.Release();
        }

        void AddIssue(ScanIssue issue)
        {
            issues.Add(issue);
            pendingIssues.Enqueue(issue);
        }

        void FlushBatchIfNeeded()
        {
            var now = Environment.TickCount64;
            if (pendingNodes.Count >= 1000 || pendingIssues.Count > 0 && now - Interlocked.Read(ref lastFlushTicks) > 250 || now - Interlocked.Read(ref lastFlushTicks) > 500)
            {
                lock (pendingNodes)
                {
                    if (pendingNodes.IsEmpty && pendingEdges.IsEmpty && pendingIssues.IsEmpty && pendingRelationships.IsEmpty)
                    {
                        return;
                    }

                    Interlocked.Exchange(ref lastFlushTicks, now);
                    FlushBatchAsync(force: false).GetAwaiter().GetResult();
                }
            }
        }

        async Task FlushBatchAsync(bool force)
        {
            var batchNodes = Drain(pendingNodes);
            var batchEdges = Drain(pendingEdges);
            var batchIssues = Drain(pendingIssues);
            var batchRelationships = Drain(pendingRelationships);

            if (!force && batchNodes.Count == 0 && batchEdges.Count == 0 && batchIssues.Count == 0 && batchRelationships.Count == 0)
            {
                return;
            }

            var batch = new ScanBatch
            {
                ScanId = session.Id,
                BatchNumber = Interlocked.Increment(ref batchNumber),
                Nodes = batchNodes,
                Edges = batchEdges,
                Issues = batchIssues,
                Relationships = batchRelationships,
                Metrics = CreateMetrics(
                    batchNodes.LastOrDefault()?.FullPath ?? rootPath,
                    Volatile.Read(ref bytesSeen),
                    Volatile.Read(ref filesScanned),
                    Volatile.Read(ref directoriesScanned),
                    issues.Count,
                    queue.Count,
                    batchNumber,
                    startedAt)
            };

            await onBatchAsync(batch, force ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            ReportProgress(batch.Metrics, batch.Metrics.CurrentPath, "Scanning");
        }

        async Task FlushAggregateBatchAsync(IReadOnlyList<FileSystemNode> allNodes)
        {
            var directories = allNodes
                .Where(n => n.Kind is FileSystemNodeKind.Drive or FileSystemNodeKind.Directory or FileSystemNodeKind.ReparsePoint)
                .ToList();

            if (directories.Count == 0)
            {
                return;
            }

            var batch = new ScanBatch
            {
                ScanId = session.Id,
                BatchNumber = Interlocked.Increment(ref batchNumber),
                Nodes = directories,
                Metrics = CreateMetrics(
                    rootPath,
                    Volatile.Read(ref bytesSeen),
                    Volatile.Read(ref filesScanned),
                    Volatile.Read(ref directoriesScanned),
                    issues.Count,
                    queue.Count,
                    batchNumber,
                    startedAt)
            };

            await onBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        ScanMetrics CreateMetrics(
            string currentPath,
            long accountedBytes,
            long currentFilesScanned,
            long currentDirectoriesScanned,
            int inaccessibleCount,
            int queueDepth,
            int currentBatchNumber,
            DateTimeOffset scanStartedAt)
        {
            var elapsed = DateTimeOffset.UtcNow - scanStartedAt;
            var seconds = Math.Max(0.001, elapsed.TotalSeconds);
            TimeSpan? eta = null;
            if (usedBytes > 0 && accountedBytes > 0)
            {
                var remainingBytes = Math.Max(0, usedBytes - accountedBytes);
                var bytesPerSecond = accountedBytes / seconds;
                eta = bytesPerSecond > 0 ? TimeSpan.FromSeconds(remainingBytes / bytesPerSecond) : null;
            }

            return new ScanMetrics
            {
                ScanId = session.Id,
                VolumeRootPath = request.Volume.RootPath,
                CurrentPath = currentPath,
                UsedBytes = usedBytes,
                AccountedBytes = accountedBytes,
                FilesScanned = currentFilesScanned,
                DirectoriesScanned = currentDirectoriesScanned,
                InaccessibleCount = inaccessibleCount,
                QueueDepth = queueDepth,
                BatchNumber = currentBatchNumber,
                Elapsed = elapsed,
                EstimatedRemaining = eta,
                FilesPerSecond = currentFilesScanned / seconds,
                DirectoriesPerSecond = currentDirectoriesScanned / seconds
            };
        }

        void ReportProgressIfNeeded(string currentPath, bool force)
        {
            var now = Environment.TickCount64;
            if (!force && now - Interlocked.Read(ref lastProgressTicks) < 250)
            {
                return;
            }

            Interlocked.Exchange(ref lastProgressTicks, now);
            var metrics = CreateMetrics(
                currentPath,
                Volatile.Read(ref bytesSeen),
                Volatile.Read(ref filesScanned),
                Volatile.Read(ref directoriesScanned),
                issues.Count,
                queue.Count,
                batchNumber,
                startedAt);
            ReportProgress(metrics, currentPath, "Scanning");
        }

        void ReportProgress(ScanMetrics metrics, string currentPath, string message)
        {
            progress?.Report(new ScanProgress
            {
                ScanId = session.Id,
                CurrentPath = currentPath,
                FilesScanned = metrics.FilesScanned,
                DirectoriesScanned = metrics.DirectoriesScanned,
                BytesSeen = metrics.AccountedBytes,
                UsedBytes = metrics.UsedBytes,
                ProgressFraction = metrics.ProgressFraction,
                FilesPerSecond = metrics.FilesPerSecond,
                DirectoriesPerSecond = metrics.DirectoriesPerSecond,
                Elapsed = metrics.Elapsed,
                EstimatedRemaining = metrics.EstimatedRemaining,
                InaccessibleCount = metrics.InaccessibleCount,
                QueueDepth = metrics.QueueDepth,
                BatchNumber = metrics.BatchNumber,
                VolumesCompleted = metrics.VolumesCompleted,
                VolumeCount = metrics.VolumeCount,
                Issues = metrics.InaccessibleCount,
                Message = message
            });
        }
    }

    private long NextNodeId() => Interlocked.Increment(ref _nextNodeId);

    private long NextEdgeId() => Interlocked.Increment(ref _nextEdgeId);

    private long NextIssueId() => Interlocked.Increment(ref _nextIssueId);

    private ScanIssue NewIssue(string path, string operation, Exception ex, long? nodeId, bool elevationMayHelp)
    {
        return new ScanIssue
        {
            Id = NextIssueId(),
            NodeId = nodeId,
            Path = path,
            Operation = operation,
            Message = ex.Message,
            ElevationMayHelp = elevationMayHelp
        };
    }

    private static void Aggregate(IReadOnlyList<FileSystemNode> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        foreach (var node in nodes.OrderByDescending(n => n.Depth))
        {
            if (node.Kind == FileSystemNodeKind.File)
            {
                node.TotalLength = node.Length;
                node.TotalPhysicalLength = node.PhysicalLength;
                node.FileCount = 1;
            }

            if (node.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent))
            {
                continue;
            }

            parent.TotalLength += node.TotalLength;
            parent.TotalPhysicalLength += node.TotalPhysicalLength;
            parent.FileCount += node.FileCount;
            parent.FolderCount += node.Kind is FileSystemNodeKind.Directory or FileSystemNodeKind.ReparsePoint
                ? node.FolderCount + 1
                : node.FolderCount;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    private static bool IsExpectedFileSystemException(Exception ex)
    {
        return ex is UnauthorizedAccessException or IOException or PathTooLongException or System.Security.SecurityException;
    }

    private static IEnumerable<FastFileSystemEntry> EnumerateEntries(string path, ScanRequest request)
    {
        if (request.PipelineOptions.PerformanceMode == ScanPerformanceMode.FastFirstScan &&
            request.PipelineOptions.UseFastEnumeration)
        {
            var options = new EnumerationOptions
            {
                AttributesToSkip = request.IncludeHiddenAndSystemEntries ? 0 : FileAttributes.Hidden | FileAttributes.System,
                IgnoreInaccessible = false,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };

            return new FileSystemEnumerable<FastFileSystemEntry>(
                path,
                (ref FileSystemEntry entry) => new FastFileSystemEntry(
                    entry.FileName.ToString(),
                    entry.ToFullPath(),
                    entry.Attributes,
                    entry.IsDirectory ? 0 : entry.Length,
                    entry.LastWriteTimeUtc,
                    null),
                options);
        }

        return EnumerateLegacyEntries(path, request);
    }

    private static IEnumerable<FastFileSystemEntry> EnumerateLegacyEntries(string path, ScanRequest request)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = request.IncludeHiddenAndSystemEntries ? 0 : FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };

        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            yield return new FastFileSystemEntry(
                entry.Name,
                entry.FullName,
                entry.Attributes,
                entry is FileInfo file ? TryGetFileLength(file) : 0,
                TryGetLastWriteTime(entry),
                TryGetLinkTarget(entry));
        }
    }

    private static long TryGetFileLength(FileInfo entry)
    {
        try
        {
            return entry.Length;
        }
        catch
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetLastWriteTime(FileSystemInfo entry)
    {
        try
        {
            return entry.LastWriteTimeUtc;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetLinkTarget(FileSystemInfo entry)
    {
        try
        {
            return entry.LinkTarget;
        }
        catch
        {
            return null;
        }
    }

    private static string Categorize(string name, bool isDirectory, bool isReparse)
    {
        if (isReparse) return "Link";
        if (isDirectory) return "Folder";

        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mov" or ".mkv" or ".avi" => "Video",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "Image",
            ".zip" or ".7z" or ".rar" or ".iso" => "Archive",
            ".exe" or ".msi" or ".msix" => "Installer",
            ".dll" or ".sys" => "System",
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".xaml" => "Code",
            ".log" or ".tmp" => "Temporary",
            _ => string.IsNullOrWhiteSpace(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant()
        };
    }

    private static List<T> Drain<T>(ConcurrentQueue<T> queue)
    {
        var items = new List<T>();
        while (queue.TryDequeue(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    private readonly record struct DirectoryWork(long ParentId, string Path, int Depth);

    private readonly record struct FastFileSystemEntry(
        string Name,
        string FullName,
        FileAttributes Attributes,
        long Length,
        DateTimeOffset? LastModifiedUtc,
        string? LinkTarget);
}
