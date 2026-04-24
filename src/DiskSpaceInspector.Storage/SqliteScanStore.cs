using System.Data;
using System.Text.Json;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Scanning;
using DiskSpaceInspector.Core.Services;
using Microsoft.Data.Sqlite;

namespace DiskSpaceInspector.Storage;

public sealed class SqliteScanStore : IScanStore
{
    private readonly string _databasePath;

    public SqliteScanStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = OpenConnection();
        await ExecuteAsync(connection, Schema, cancellationToken).ConfigureAwait(false);
    }

    public async Task BeginScanAsync(
        ScanSession session,
        VolumeInfo volume,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;

        await InsertVolumeAsync(connection, transaction, volume, cancellationToken).ConfigureAwait(false);
        await InsertScanAsync(connection, transaction, session, volume, cancellationToken).ConfigureAwait(false);
        await InsertCheckpointAsync(connection, transaction, new ScanCheckpoint
        {
            ScanId = session.Id,
            VolumeRootPath = volume.RootPath,
            Status = ScanStatus.Running
        }, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveScanBatchAsync(
        ScanBatch batch,
        IEnumerable<CleanupFinding> findings,
        IEnumerable<InsightFinding> insights,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;

        await InsertBatchAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
        foreach (var node in batch.Nodes)
        {
            await InsertScanNodeAsync(connection, transaction, batch.ScanId, node, cancellationToken).ConfigureAwait(false);
            await UpsertCurrentNodeAndChangeAsync(connection, transaction, batch.ScanId, batch.Metrics.VolumeRootPath, node, cancellationToken).ConfigureAwait(false);
        }

        foreach (var edge in batch.Edges)
        {
            await InsertEdgeAsync(connection, transaction, batch.ScanId, edge, cancellationToken).ConfigureAwait(false);
        }

        foreach (var relationship in batch.Relationships)
        {
            await InsertRelationshipAsync(connection, transaction, relationship, cancellationToken).ConfigureAwait(false);
        }

        foreach (var issue in batch.Issues)
        {
            await InsertIssueAsync(connection, transaction, batch.ScanId, issue, cancellationToken).ConfigureAwait(false);
        }

        foreach (var finding in findings)
        {
            await InsertFindingAsync(connection, transaction, batch.ScanId, finding, cancellationToken).ConfigureAwait(false);
        }

        foreach (var insight in insights)
        {
            await InsertInsightAsync(connection, transaction, insight, cancellationToken).ConfigureAwait(false);
        }

        await InsertCheckpointAsync(connection, transaction, new ScanCheckpoint
        {
            ScanId = batch.ScanId,
            VolumeRootPath = batch.Metrics.VolumeRootPath,
            LastNodeId = batch.Nodes.Count == 0 ? 0 : batch.Nodes.Max(n => n.Id),
            FilesScanned = batch.Metrics.FilesScanned,
            DirectoriesScanned = batch.Metrics.DirectoriesScanned,
            AccountedBytes = batch.Metrics.AccountedBytes,
            QueueDepth = batch.Metrics.QueueDepth,
            Status = ScanStatus.Running
        }, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteScanAsync(
        ScanCompleted completed,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)dbTransaction;

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE scans
            SET completed_at_utc = $completed_at_utc,
                status = $status,
                total_logical_bytes = $total_logical_bytes,
                total_physical_bytes = $total_physical_bytes,
                files_scanned = $files_scanned,
                directories_scanned = $directories_scanned,
                issue_count = $issue_count
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$id", completed.Session.Id.ToString());
        update.Parameters.AddWithValue("$completed_at_utc", (object?)completed.Session.CompletedAtUtc?.ToString("O") ?? DBNull.Value);
        update.Parameters.AddWithValue("$status", completed.Session.Status.ToString());
        update.Parameters.AddWithValue("$total_logical_bytes", completed.Session.TotalLogicalBytes);
        update.Parameters.AddWithValue("$total_physical_bytes", completed.Session.TotalPhysicalBytes);
        update.Parameters.AddWithValue("$files_scanned", completed.Session.FilesScanned);
        update.Parameters.AddWithValue("$directories_scanned", completed.Session.DirectoriesScanned);
        update.Parameters.AddWithValue("$issue_count", completed.Session.IssueCount);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var deletedInsert = connection.CreateCommand();
        deletedInsert.Transaction = transaction;
        deletedInsert.CommandText = """
            INSERT INTO change_records
                (id, scan_id, stable_id, path, previous_path, kind, previous_size_bytes, current_size_bytes, detected_at_utc, reason)
            SELECT lower(hex(randomblob(16))), $scan_id, stable_id, full_path, full_path, 'Deleted',
                   total_physical_length, 0, $detected_at_utc, 'not seen in latest scan'
            FROM current_nodes
            WHERE volume_root_path = $volume_root_path
              AND last_seen_scan_id <> $scan_id
              AND is_deleted = 0;
            """;
        deletedInsert.Parameters.AddWithValue("$scan_id", completed.Session.Id.ToString());
        deletedInsert.Parameters.AddWithValue("$volume_root_path", completed.Volume.RootPath);
        deletedInsert.Parameters.AddWithValue("$detected_at_utc", DateTimeOffset.UtcNow.ToString("O"));
        await deletedInsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var markDeleted = connection.CreateCommand();
        markDeleted.Transaction = transaction;
        markDeleted.CommandText = """
            UPDATE current_nodes
            SET is_deleted = 1
            WHERE volume_root_path = $volume_root_path
              AND last_seen_scan_id <> $scan_id;
            """;
        markDeleted.Parameters.AddWithValue("$scan_id", completed.Session.Id.ToString());
        markDeleted.Parameters.AddWithValue("$volume_root_path", completed.Volume.RootPath);
        await markDeleted.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await InsertCheckpointAsync(connection, transaction, new ScanCheckpoint
        {
            ScanId = completed.Session.Id,
            VolumeRootPath = completed.Volume.RootPath,
            FilesScanned = completed.Session.FilesScanned,
            DirectoriesScanned = completed.Session.DirectoriesScanned,
            AccountedBytes = completed.Session.TotalPhysicalBytes,
            Status = completed.Session.Status
        }, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveScanAsync(
        ScanResult result,
        IEnumerable<CleanupFinding> findings,
        CancellationToken cancellationToken = default)
    {
        await BeginScanAsync(result.Session, result.Volume, cancellationToken).ConfigureAwait(false);
        await SaveScanBatchAsync(new ScanBatch
        {
            ScanId = result.Session.Id,
            BatchNumber = 1,
            Nodes = result.Nodes,
            Edges = result.Edges,
            Issues = result.Issues,
            Metrics = new ScanMetrics
            {
                ScanId = result.Session.Id,
                VolumeRootPath = result.Volume.RootPath,
                AccountedBytes = result.Session.TotalPhysicalBytes,
                UsedBytes = Math.Max(0, result.Volume.TotalBytes - result.Volume.FreeBytes),
                FilesScanned = result.Session.FilesScanned,
                DirectoriesScanned = result.Session.DirectoriesScanned,
                InaccessibleCount = result.Session.IssueCount
            }
        }, findings, [], cancellationToken).ConfigureAwait(false);
        await CompleteScanAsync(new ScanCompleted
        {
            Session = result.Session,
            Volume = result.Volume,
            FinalMetrics = new ScanMetrics
            {
                ScanId = result.Session.Id,
                VolumeRootPath = result.Volume.RootPath,
                AccountedBytes = result.Session.TotalPhysicalBytes,
                UsedBytes = Math.Max(0, result.Volume.TotalBytes - result.Volume.FreeBytes)
            },
            BatchCount = 1
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ScanResult?> LoadCurrentScanAsync(CancellationToken cancellationToken = default)
    {
        return LoadLatestScanAsync(cancellationToken);
    }

    public async Task<ScanResult?> LoadLatestScanAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();

        var scanCommand = connection.CreateCommand();
        scanCommand.CommandText = """
            SELECT id, root_path, started_at_utc, completed_at_utc, status, total_logical_bytes,
                   total_physical_bytes, files_scanned, directories_scanned, issue_count, volume_root_path
            FROM scans
            ORDER BY started_at_utc DESC
            LIMIT 1;
            """;

        await using var reader = await scanCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var scanId = Guid.Parse(reader.GetString(0));
        var volumeRoot = reader.GetString(10);
        var session = new ScanSession
        {
            Id = scanId,
            RootPath = reader.GetString(1),
            StartedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
            CompletedAtUtc = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            Status = Enum.Parse<ScanStatus>(reader.GetString(4)),
            TotalLogicalBytes = reader.GetInt64(5),
            TotalPhysicalBytes = reader.GetInt64(6),
            FilesScanned = reader.GetInt64(7),
            DirectoriesScanned = reader.GetInt64(8),
            IssueCount = reader.GetInt32(9)
        };
        await reader.DisposeAsync().ConfigureAwait(false);

        var volume = await LoadVolumeAsync(connection, volumeRoot, cancellationToken).ConfigureAwait(false) ?? new VolumeInfo
        {
            Name = volumeRoot.TrimEnd('\\'),
            RootPath = volumeRoot,
            IsReady = true
        };

        return new ScanResult
        {
            Session = session,
            Volume = volume,
            Nodes = await LoadNodesAsync(connection, scanId, cancellationToken).ConfigureAwait(false),
            Edges = await LoadEdgesAsync(connection, scanId, cancellationToken).ConfigureAwait(false),
            Issues = await LoadIssuesAsync(connection, scanId, cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<IReadOnlyList<CleanupFinding>> LoadCleanupFindingsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, node_id, path, display_name, category, safety, recommended_action, size_bytes,
                   file_count, last_modified_utc, confidence, explanation, matched_rule, app_or_source, evidence_json
            FROM cleanup_findings
            WHERE scan_id = $scan_id
            ORDER BY size_bytes DESC;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var findings = new List<CleanupFinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            findings.Add(ReadFinding(reader));
        }

        return findings;
    }

    public async Task<IReadOnlyList<ChangeRecord>> LoadChangeRecordsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scan_id, stable_id, path, previous_path, kind, previous_size_bytes,
                   current_size_bytes, detected_at_utc, reason
            FROM change_records
            WHERE scan_id = $scan_id
            ORDER BY abs(current_size_bytes - previous_size_bytes) DESC, detected_at_utc DESC;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var records = new List<ChangeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(new ChangeRecord
            {
                Id = Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                ScanId = Guid.Parse(reader.GetString(1)),
                StableId = reader.GetString(2),
                Path = reader.GetString(3),
                PreviousPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                Kind = Enum.Parse<ChangeKind>(reader.GetString(5)),
                PreviousSizeBytes = reader.GetInt64(6),
                CurrentSizeBytes = reader.GetInt64(7),
                DetectedAtUtc = DateTimeOffset.Parse(reader.GetString(8)),
                Reason = reader.GetString(9)
            });
        }

        return records;
    }

    public async Task<IReadOnlyList<StorageRelationship>> LoadRelationshipsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scan_id, source_node_id, source_path, target_path, kind, label, owner,
                   evidence_source, evidence_detail, confidence
            FROM relationship_evidence
            WHERE scan_id = $scan_id
            ORDER BY confidence DESC;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var relationships = new List<StorageRelationship>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            relationships.Add(new StorageRelationship
            {
                Id = Guid.TryParse(reader.GetString(0), out var id) ? id : Guid.NewGuid(),
                ScanId = Guid.Parse(reader.GetString(1)),
                SourceNodeId = reader.GetInt64(2),
                SourcePath = reader.GetString(3),
                TargetPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                Kind = Enum.Parse<FileSystemEdgeKind>(reader.GetString(5)),
                Label = reader.GetString(6),
                Owner = reader.GetString(7),
                Evidence = new RelationshipEvidence
                {
                    Source = reader.GetString(8),
                    Detail = reader.GetString(9),
                    Confidence = reader.GetDouble(10)
                }
            });
        }

        return relationships;
    }

    public async Task<IReadOnlyList<InsightFinding>> LoadInsightsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scan_id, node_id, stable_id, path, tool, title, description, safety,
                   recommended_action, size_bytes, confidence, evidence
            FROM insight_findings
            WHERE scan_id = $scan_id
            ORDER BY size_bytes DESC, confidence DESC;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var insights = new List<InsightFinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            insights.Add(new InsightFinding
            {
                Id = Guid.Parse(reader.GetString(0)),
                ScanId = Guid.Parse(reader.GetString(1)),
                NodeId = reader.GetInt64(2),
                StableId = reader.GetString(3),
                Path = reader.GetString(4),
                Tool = reader.GetString(5),
                Title = reader.GetString(6),
                Description = reader.GetString(7),
                Safety = Enum.Parse<CleanupSafety>(reader.GetString(8)),
                RecommendedAction = Enum.Parse<CleanupActionKind>(reader.GetString(9)),
                SizeBytes = reader.GetInt64(10),
                Confidence = reader.GetDouble(11),
                Evidence = reader.GetString(12)
            });
        }

        return insights;
    }

    public async Task<DriveDashboard?> LoadDriveDashboardAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var latest = await LoadLatestScanHeaderAsync(connection, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return null;
        }

        var topNodes = await LoadNodesBySqlAsync(
            connection,
            $"""
             SELECT {NodeColumns}
             FROM scan_nodes
             WHERE scan_id = $scan_id
               AND parent_id IS NOT NULL
             ORDER BY total_physical_length DESC, name COLLATE NOCASE
             LIMIT 16;
             """,
            command => command.Parameters.AddWithValue("$scan_id", latest.Value.ScanId.ToString()),
            cancellationToken).ConfigureAwait(false);

        var cleanup = new List<StorageBreakdownItem>();
        var cleanupCommand = connection.CreateCommand();
        cleanupCommand.CommandText = """
            SELECT safety, sum(size_bytes), count(*)
            FROM cleanup_findings
            WHERE scan_id = $scan_id
            GROUP BY safety
            ORDER BY sum(size_bytes) DESC;
            """;
        cleanupCommand.Parameters.AddWithValue("$scan_id", latest.Value.ScanId.ToString());
        await using (var reader = await cleanupCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cleanup.Add(new StorageBreakdownItem
                {
                    Key = reader.GetString(0),
                    Label = reader.GetString(0),
                    SizeBytes = reader.GetInt64(1),
                    Count = reader.GetInt32(2),
                    ColorKey = reader.GetString(0)
                });
            }
        }

        var cleanupTotal = cleanup.Sum(i => i.SizeBytes);
        cleanup = cleanup.Select(i => new StorageBreakdownItem
        {
            Key = i.Key,
            Label = i.Label,
            SizeBytes = i.SizeBytes,
            Count = i.Count,
            Fraction = cleanupTotal > 0 ? i.SizeBytes / (double)cleanupTotal : 0,
            ColorKey = i.ColorKey
        }).ToList();

        return new DriveDashboard
        {
            ScanId = latest.Value.ScanId,
            RootPath = latest.Value.RootPath,
            Status = latest.Value.Status,
            StartedAtUtc = latest.Value.StartedAtUtc,
            CompletedAtUtc = latest.Value.CompletedAtUtc,
            TotalPhysicalBytes = latest.Value.TotalPhysicalBytes,
            FilesScanned = latest.Value.FilesScanned,
            DirectoriesScanned = latest.Value.DirectoriesScanned,
            IssueCount = latest.Value.IssueCount,
            ReclaimableBytes = cleanup
                .Where(i => i.Key is nameof(CleanupSafety.Safe) or nameof(CleanupSafety.Review))
                .Sum(i => i.SizeBytes),
            TopSpaceConsumers = topNodes,
            CleanupBySafety = cleanup
        };
    }

    public async Task<IReadOnlyList<FileSystemNode>> LoadChildrenAsync(
        string? parentStableId,
        NodeQuerySort sort = NodeQuerySort.SizeDescending,
        int skip = 0,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var scanId = await GetLatestScanIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (scanId is null)
        {
            return [];
        }

        var parentPredicate = string.IsNullOrWhiteSpace(parentStableId)
            ? "parent_id IS NULL"
            : "parent_stable_id = $parent_stable_id";
        return await LoadNodesBySqlAsync(
            connection,
            $"""
             SELECT {NodeColumns}
             FROM scan_nodes
             WHERE scan_id = $scan_id
               AND {parentPredicate}
             ORDER BY {SortClause(sort)}
             LIMIT $take OFFSET $skip;
             """,
            command =>
            {
                command.Parameters.AddWithValue("$scan_id", scanId.Value.ToString());
                command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 2000));
                command.Parameters.AddWithValue("$skip", Math.Max(0, skip));
                if (!string.IsNullOrWhiteSpace(parentStableId))
                {
                    command.Parameters.AddWithValue("$parent_stable_id", parentStableId);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileSystemNode>> SearchNodesAsync(
        string query,
        int skip = 0,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var scanId = await GetLatestScanIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (scanId is null)
        {
            return [];
        }

        return await LoadNodesBySqlAsync(
            connection,
            $"""
             SELECT {NodeColumns}
             FROM scan_nodes
             WHERE scan_id = $scan_id
               AND (name LIKE $query OR full_path LIKE $query OR extension LIKE $query)
             ORDER BY total_physical_length DESC, name COLLATE NOCASE
             LIMIT $take OFFSET $skip;
             """,
            command =>
            {
                command.Parameters.AddWithValue("$scan_id", scanId.Value.ToString());
                command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
                command.Parameters.AddWithValue("$take", Math.Clamp(take, 1, 2000));
                command.Parameters.AddWithValue("$skip", Math.Max(0, skip));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileSystemNode>> LoadTreemapDataAsync(
        string? parentStableId,
        int maxTiles = 240,
        CancellationToken cancellationToken = default)
    {
        return await LoadChildrenAsync(
            parentStableId,
            NodeQuerySort.SizeDescending,
            0,
            Math.Clamp(maxTiles, 1, 2000),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileSystemNode>> LoadSunburstDataAsync(
        string? parentStableId,
        int depth = 4,
        int maxNodes = 500,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var scanId = await GetLatestScanIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (scanId is null)
        {
            return [];
        }

        var subtreePredicate = string.IsNullOrWhiteSpace(parentStableId)
            ? "1 = 1"
            : "full_path LIKE (SELECT full_path || '%' FROM scan_nodes WHERE scan_id = $scan_id AND stable_id = $parent_stable_id LIMIT 1)";
        return await LoadNodesBySqlAsync(
            connection,
            $"""
             SELECT {NodeColumns}
             FROM scan_nodes
             WHERE scan_id = $scan_id
               AND {subtreePredicate}
             ORDER BY depth, total_physical_length DESC, name COLLATE NOCASE
             LIMIT $take;
             """,
            command =>
            {
                command.Parameters.AddWithValue("$scan_id", scanId.Value.ToString());
                command.Parameters.AddWithValue("$depth", Math.Clamp(depth, 1, 8));
                command.Parameters.AddWithValue("$take", Math.Clamp(maxNodes, 1, 5000));
                if (!string.IsNullOrWhiteSpace(parentStableId))
                {
                    command.Parameters.AddWithValue("$parent_stable_id", parentStableId);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StorageBreakdownItem>> LoadTypeBreakdownAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var scanId = await GetLatestScanIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (scanId is null)
        {
            return [];
        }

        var items = new List<StorageBreakdownItem>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT category, sum(total_physical_length), count(*)
            FROM scan_nodes
            WHERE scan_id = $scan_id
              AND kind = 'File'
              AND total_physical_length > 0
            GROUP BY category
            ORDER BY sum(total_physical_length) DESC
            LIMIT 12;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.Value.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new StorageBreakdownItem
            {
                Key = reader.GetString(0),
                Label = reader.GetString(0),
                SizeBytes = reader.GetInt64(1),
                Count = reader.GetInt32(2),
                ColorKey = reader.GetString(0)
            });
        }

        var total = items.Sum(i => i.SizeBytes);
        return items.Select(i => new StorageBreakdownItem
        {
            Key = i.Key,
            Label = i.Label,
            SizeBytes = i.SizeBytes,
            Count = i.Count,
            Fraction = total > 0 ? i.SizeBytes / (double)total : 0,
            ColorKey = i.ColorKey
        }).ToList();
    }

    public async Task<IReadOnlyList<AgeHistogramBucket>> LoadAgeHistogramAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        var scanId = await GetLatestScanIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (scanId is null)
        {
            return [];
        }

        var buckets = new List<AgeHistogramBucket>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                CASE
                    WHEN last_modified_utc IS NULL THEN 'Unknown'
                    WHEN julianday('now') - julianday(last_modified_utc) <= 30 THEN 'Last 30 days'
                    WHEN julianday('now') - julianday(last_modified_utc) <= 90 THEN '30-90 days'
                    WHEN julianday('now') - julianday(last_modified_utc) <= 365 THEN '90 days-1 year'
                    WHEN julianday('now') - julianday(last_modified_utc) <= 1095 THEN '1-3 years'
                    ELSE '3+ years'
                END AS bucket,
                sum(total_physical_length),
                count(*)
            FROM scan_nodes
            WHERE scan_id = $scan_id
              AND kind = 'File'
              AND total_physical_length > 0
            GROUP BY bucket;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.Value.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            buckets.Add(new AgeHistogramBucket
            {
                Label = reader.GetString(0),
                SizeBytes = reader.GetInt64(1),
                Count = reader.GetInt32(2)
            });
        }

        var total = buckets.Sum(i => i.SizeBytes);
        return buckets.Select(i => new AgeHistogramBucket
        {
            Label = i.Label,
            SizeBytes = i.SizeBytes,
            Count = i.Count,
            Fraction = total > 0 ? i.SizeBytes / (double)total : 0
        }).ToList();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Guid?> GetLatestScanIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM scans ORDER BY started_at_utc DESC LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string id && Guid.TryParse(id, out var scanId) ? scanId : null;
    }

    private static async Task<LatestScanHeader?> LoadLatestScanHeaderAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, root_path, started_at_utc, completed_at_utc, status, total_physical_bytes,
                   files_scanned, directories_scanned, issue_count
            FROM scans
            ORDER BY started_at_utc DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new LatestScanHeader(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            Enum.Parse<ScanStatus>(reader.GetString(4)),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt32(8));
    }

    private static async Task<IReadOnlyList<FileSystemNode>> LoadNodesBySqlAsync(
        SqliteConnection connection,
        string commandText,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        configure(command);

        var nodes = new List<FileSystemNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            nodes.Add(ReadNode(reader));
        }

        return nodes;
    }

    private static string SortClause(NodeQuerySort sort)
    {
        return sort switch
        {
            NodeQuerySort.NameAscending => "name COLLATE NOCASE, total_physical_length DESC",
            NodeQuerySort.ModifiedDescending => "last_modified_utc DESC, total_physical_length DESC",
            NodeQuerySort.KindThenSize => "kind, total_physical_length DESC, name COLLATE NOCASE",
            _ => "total_physical_length DESC, name COLLATE NOCASE"
        };
    }

    private static async Task InsertVolumeAsync(SqliteConnection connection, IDbTransaction transaction, VolumeInfo volume, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO volumes
                (root_path, name, file_system, label, volume_serial, drive_type, is_ready, total_bytes, free_bytes)
            VALUES
                ($root_path, $name, $file_system, $label, $volume_serial, $drive_type, $is_ready, $total_bytes, $free_bytes);
            """;
        command.Parameters.AddWithValue("$root_path", volume.RootPath);
        command.Parameters.AddWithValue("$name", volume.Name);
        command.Parameters.AddWithValue("$file_system", (object?)volume.FileSystem ?? DBNull.Value);
        command.Parameters.AddWithValue("$label", (object?)volume.Label ?? DBNull.Value);
        command.Parameters.AddWithValue("$volume_serial", (object?)volume.VolumeSerial ?? DBNull.Value);
        command.Parameters.AddWithValue("$drive_type", volume.DriveType);
        command.Parameters.AddWithValue("$is_ready", volume.IsReady ? 1 : 0);
        command.Parameters.AddWithValue("$total_bytes", volume.TotalBytes);
        command.Parameters.AddWithValue("$free_bytes", volume.FreeBytes);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertScanAsync(SqliteConnection connection, IDbTransaction transaction, ScanSession session, VolumeInfo volume, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO scans
                (id, volume_root_path, root_path, started_at_utc, completed_at_utc, status,
                 total_logical_bytes, total_physical_bytes, files_scanned, directories_scanned, issue_count)
            VALUES
                ($id, $volume_root_path, $root_path, $started_at_utc, $completed_at_utc, $status,
                 $total_logical_bytes, $total_physical_bytes, $files_scanned, $directories_scanned, $issue_count);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$volume_root_path", volume.RootPath);
        command.Parameters.AddWithValue("$root_path", session.RootPath);
        command.Parameters.AddWithValue("$started_at_utc", session.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$completed_at_utc", (object?)session.CompletedAtUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$total_logical_bytes", session.TotalLogicalBytes);
        command.Parameters.AddWithValue("$total_physical_bytes", session.TotalPhysicalBytes);
        command.Parameters.AddWithValue("$files_scanned", session.FilesScanned);
        command.Parameters.AddWithValue("$directories_scanned", session.DirectoriesScanned);
        command.Parameters.AddWithValue("$issue_count", session.IssueCount);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertBatchAsync(SqliteConnection connection, IDbTransaction transaction, ScanBatch batch, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO scan_batches
                (scan_id, batch_number, captured_at_utc, node_count, edge_count, issue_count, accounted_bytes, queue_depth)
            VALUES
                ($scan_id, $batch_number, $captured_at_utc, $node_count, $edge_count, $issue_count, $accounted_bytes, $queue_depth);
            """;
        command.Parameters.AddWithValue("$scan_id", batch.ScanId.ToString());
        command.Parameters.AddWithValue("$batch_number", batch.BatchNumber);
        command.Parameters.AddWithValue("$captured_at_utc", batch.CapturedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$node_count", batch.Nodes.Count);
        command.Parameters.AddWithValue("$edge_count", batch.Edges.Count + batch.Relationships.Count);
        command.Parameters.AddWithValue("$issue_count", batch.Issues.Count);
        command.Parameters.AddWithValue("$accounted_bytes", batch.Metrics.AccountedBytes);
        command.Parameters.AddWithValue("$queue_depth", batch.Metrics.QueueDepth);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertScanNodeAsync(SqliteConnection connection, IDbTransaction transaction, Guid scanId, FileSystemNode node, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = NodeInsertSql("scan_nodes", includeVolumeRoot: false);
        AddNodeParameters(command, scanId, null, node);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertCurrentNodeAndChangeAsync(
        SqliteConnection connection,
        IDbTransaction transaction,
        Guid scanId,
        string volumeRootPath,
        FileSystemNode node,
        CancellationToken cancellationToken)
    {
        var stableId = string.IsNullOrWhiteSpace(node.StableId)
            ? $"path:{PathIdentity.HashPath(node.FullPath)}"
            : node.StableId;
        var previous = await LoadCurrentSignatureAsync(connection, transaction, stableId, cancellationToken).ConfigureAwait(false);
        var currentSize = Math.Max(node.TotalPhysicalLength, node.PhysicalLength);
        if (previous is null)
        {
            await InsertChangeAsync(connection, transaction, new ChangeRecord
            {
                ScanId = scanId,
                StableId = stableId,
                Path = node.FullPath,
                Kind = ChangeKind.Added,
                CurrentSizeBytes = currentSize,
                Reason = "first time seen"
            }, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(previous.LastSeenScanId, scanId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var kind = ChangeKind.Unchanged;
            var reason = "";
            if (!previous.FullPath.Equals(node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                kind = ChangeKind.Moved;
                reason = "stable file identity appeared at a different path";
            }
            else if (previous.Length != node.Length ||
                     previous.AllocatedLength != node.AllocatedLength ||
                     previous.LastModifiedUtc != node.LastModifiedUtc ||
                     !previous.Attributes.Equals(node.Attributes.ToString(), StringComparison.Ordinal))
            {
                kind = ChangeKind.Modified;
                reason = "size, timestamp, or attributes changed";
            }

            if (kind != ChangeKind.Unchanged)
            {
                await InsertChangeAsync(connection, transaction, new ChangeRecord
                {
                    ScanId = scanId,
                    StableId = stableId,
                    Path = node.FullPath,
                    PreviousPath = previous.FullPath,
                    Kind = kind,
                    PreviousSizeBytes = previous.AllocatedLength,
                    CurrentSizeBytes = currentSize,
                    Reason = reason
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = CurrentNodeUpsertSql;
        AddNodeParameters(command, scanId, volumeRootPath, node);
        command.Parameters.AddWithValue("$is_deleted", 0);
        command.Parameters.AddWithValue("$last_seen_scan_id", scanId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NodeSignature?> LoadCurrentSignatureAsync(SqliteConnection connection, IDbTransaction transaction, string stableId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT stable_id, last_seen_scan_id, path_hash, full_path, length, allocated_length, last_modified_utc, attributes, reparse_target, usn
            FROM current_nodes
            WHERE stable_id = $stable_id
              AND is_deleted = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$stable_id", stableId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new NodeSignature
        {
            StableId = reader.GetString(0),
            LastSeenScanId = reader.GetString(1),
            PathHash = reader.GetString(2),
            FullPath = reader.GetString(3),
            Length = reader.GetInt64(4),
            AllocatedLength = reader.GetInt64(5),
            LastModifiedUtc = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            Attributes = reader.GetString(7),
            ReparseTarget = reader.IsDBNull(8) ? null : reader.GetString(8),
            Usn = reader.IsDBNull(9) ? null : reader.GetInt64(9)
        };
    }

    private static async Task InsertChangeAsync(SqliteConnection connection, IDbTransaction transaction, ChangeRecord change, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO change_records
                (id, scan_id, stable_id, path, previous_path, kind, previous_size_bytes, current_size_bytes, detected_at_utc, reason)
            VALUES
                ($id, $scan_id, $stable_id, $path, $previous_path, $kind, $previous_size_bytes, $current_size_bytes, $detected_at_utc, $reason);
            """;
        command.Parameters.AddWithValue("$id", change.Id.ToString());
        command.Parameters.AddWithValue("$scan_id", change.ScanId.ToString());
        command.Parameters.AddWithValue("$stable_id", change.StableId);
        command.Parameters.AddWithValue("$path", change.Path);
        command.Parameters.AddWithValue("$previous_path", (object?)change.PreviousPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", change.Kind.ToString());
        command.Parameters.AddWithValue("$previous_size_bytes", change.PreviousSizeBytes);
        command.Parameters.AddWithValue("$current_size_bytes", change.CurrentSizeBytes);
        command.Parameters.AddWithValue("$detected_at_utc", change.DetectedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$reason", change.Reason);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertEdgeAsync(SqliteConnection connection, IDbTransaction transaction, Guid scanId, FileSystemEdge edge, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO edges
                (scan_id, id, source_node_id, target_node_id, target_path, kind, label, evidence)
            VALUES
                ($scan_id, $id, $source_node_id, $target_node_id, $target_path, $kind, $label, $evidence);
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());
        command.Parameters.AddWithValue("$id", edge.Id);
        command.Parameters.AddWithValue("$source_node_id", edge.SourceNodeId);
        command.Parameters.AddWithValue("$target_node_id", (object?)edge.TargetNodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$target_path", (object?)edge.TargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", edge.Kind.ToString());
        command.Parameters.AddWithValue("$label", edge.Label);
        command.Parameters.AddWithValue("$evidence", edge.Evidence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRelationshipAsync(SqliteConnection connection, IDbTransaction transaction, StorageRelationship relationship, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO relationship_evidence
                (id, scan_id, source_node_id, source_path, target_path, kind, label, owner, evidence_source, evidence_detail, confidence)
            VALUES
                ($id, $scan_id, $source_node_id, $source_path, $target_path, $kind, $label, $owner, $evidence_source, $evidence_detail, $confidence);
            """;
        command.Parameters.AddWithValue("$id", relationship.Id.ToString());
        command.Parameters.AddWithValue("$scan_id", relationship.ScanId.ToString());
        command.Parameters.AddWithValue("$source_node_id", relationship.SourceNodeId);
        command.Parameters.AddWithValue("$source_path", relationship.SourcePath);
        command.Parameters.AddWithValue("$target_path", (object?)relationship.TargetPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", relationship.Kind.ToString());
        command.Parameters.AddWithValue("$label", relationship.Label);
        command.Parameters.AddWithValue("$owner", relationship.Owner);
        command.Parameters.AddWithValue("$evidence_source", relationship.Evidence.Source);
        command.Parameters.AddWithValue("$evidence_detail", relationship.Evidence.Detail);
        command.Parameters.AddWithValue("$confidence", relationship.Evidence.Confidence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertIssueAsync(SqliteConnection connection, IDbTransaction transaction, Guid scanId, ScanIssue issue, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO scan_errors
                (scan_id, id, node_id, path, operation, message, native_error_code, elevation_may_help, occurred_at_utc)
            VALUES
                ($scan_id, $id, $node_id, $path, $operation, $message, $native_error_code, $elevation_may_help, $occurred_at_utc);
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());
        command.Parameters.AddWithValue("$id", issue.Id);
        command.Parameters.AddWithValue("$node_id", (object?)issue.NodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$path", issue.Path);
        command.Parameters.AddWithValue("$operation", issue.Operation);
        command.Parameters.AddWithValue("$message", issue.Message);
        command.Parameters.AddWithValue("$native_error_code", (object?)issue.NativeErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$elevation_may_help", issue.ElevationMayHelp ? 1 : 0);
        command.Parameters.AddWithValue("$occurred_at_utc", issue.OccurredAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertFindingAsync(SqliteConnection connection, IDbTransaction transaction, Guid scanId, CleanupFinding finding, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO cleanup_findings
                (scan_id, id, node_id, path, display_name, category, safety, recommended_action, size_bytes,
                 file_count, last_modified_utc, confidence, explanation, matched_rule, app_or_source, evidence_json)
            VALUES
                ($scan_id, $id, $node_id, $path, $display_name, $category, $safety, $recommended_action, $size_bytes,
                 $file_count, $last_modified_utc, $confidence, $explanation, $matched_rule, $app_or_source, $evidence_json);
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());
        command.Parameters.AddWithValue("$id", finding.Id.ToString());
        command.Parameters.AddWithValue("$node_id", finding.NodeId);
        command.Parameters.AddWithValue("$path", finding.Path);
        command.Parameters.AddWithValue("$display_name", finding.DisplayName);
        command.Parameters.AddWithValue("$category", finding.Category);
        command.Parameters.AddWithValue("$safety", finding.Safety.ToString());
        command.Parameters.AddWithValue("$recommended_action", finding.RecommendedAction.ToString());
        command.Parameters.AddWithValue("$size_bytes", finding.SizeBytes);
        command.Parameters.AddWithValue("$file_count", finding.FileCount);
        command.Parameters.AddWithValue("$last_modified_utc", (object?)finding.LastModifiedUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidence", finding.Confidence);
        command.Parameters.AddWithValue("$explanation", finding.Explanation);
        command.Parameters.AddWithValue("$matched_rule", finding.MatchedRule);
        command.Parameters.AddWithValue("$app_or_source", (object?)finding.AppOrSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$evidence_json", JsonSerializer.Serialize(finding.Evidence));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertInsightAsync(SqliteConnection connection, IDbTransaction transaction, InsightFinding insight, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO insight_findings
                (id, scan_id, node_id, stable_id, path, tool, title, description, safety,
                 recommended_action, size_bytes, confidence, evidence)
            VALUES
                ($id, $scan_id, $node_id, $stable_id, $path, $tool, $title, $description, $safety,
                 $recommended_action, $size_bytes, $confidence, $evidence);
            """;
        command.Parameters.AddWithValue("$id", insight.Id.ToString());
        command.Parameters.AddWithValue("$scan_id", insight.ScanId.ToString());
        command.Parameters.AddWithValue("$node_id", insight.NodeId);
        command.Parameters.AddWithValue("$stable_id", insight.StableId);
        command.Parameters.AddWithValue("$path", insight.Path);
        command.Parameters.AddWithValue("$tool", insight.Tool);
        command.Parameters.AddWithValue("$title", insight.Title);
        command.Parameters.AddWithValue("$description", insight.Description);
        command.Parameters.AddWithValue("$safety", insight.Safety.ToString());
        command.Parameters.AddWithValue("$recommended_action", insight.RecommendedAction.ToString());
        command.Parameters.AddWithValue("$size_bytes", insight.SizeBytes);
        command.Parameters.AddWithValue("$confidence", insight.Confidence);
        command.Parameters.AddWithValue("$evidence", insight.Evidence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCheckpointAsync(SqliteConnection connection, IDbTransaction transaction, ScanCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO scan_checkpoints
                (scan_id, volume_root_path, updated_at_utc, last_node_id, files_scanned,
                 directories_scanned, accounted_bytes, queue_depth, status)
            VALUES
                ($scan_id, $volume_root_path, $updated_at_utc, $last_node_id, $files_scanned,
                 $directories_scanned, $accounted_bytes, $queue_depth, $status);
            """;
        command.Parameters.AddWithValue("$scan_id", checkpoint.ScanId.ToString());
        command.Parameters.AddWithValue("$volume_root_path", checkpoint.VolumeRootPath);
        command.Parameters.AddWithValue("$updated_at_utc", checkpoint.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$last_node_id", checkpoint.LastNodeId);
        command.Parameters.AddWithValue("$files_scanned", checkpoint.FilesScanned);
        command.Parameters.AddWithValue("$directories_scanned", checkpoint.DirectoriesScanned);
        command.Parameters.AddWithValue("$accounted_bytes", checkpoint.AccountedBytes);
        command.Parameters.AddWithValue("$queue_depth", checkpoint.QueueDepth);
        command.Parameters.AddWithValue("$status", checkpoint.Status.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddNodeParameters(SqliteCommand command, Guid scanId, string? volumeRootPath, FileSystemNode node)
    {
        var stableId = string.IsNullOrWhiteSpace(node.StableId)
            ? $"path:{PathIdentity.HashPath(node.FullPath)}"
            : node.StableId;
        var pathHash = string.IsNullOrWhiteSpace(node.PathHash)
            ? PathIdentity.HashPath(node.FullPath)
            : node.PathHash;

        command.Parameters.AddWithValue("$scan_id", scanId.ToString());
        command.Parameters.AddWithValue("$volume_root_path", (object?)volumeRootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", node.Id);
        command.Parameters.AddWithValue("$stable_id", stableId);
        command.Parameters.AddWithValue("$path_hash", pathHash);
        command.Parameters.AddWithValue("$parent_stable_id", (object?)node.ParentStableId ?? DBNull.Value);
        command.Parameters.AddWithValue("$parent_path_hash", (object?)node.ParentPathHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$parent_id", (object?)node.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", node.Name);
        command.Parameters.AddWithValue("$full_path", node.FullPath);
        command.Parameters.AddWithValue("$kind", node.Kind.ToString());
        command.Parameters.AddWithValue("$extension", (object?)node.Extension ?? DBNull.Value);
        command.Parameters.AddWithValue("$length", node.Length);
        command.Parameters.AddWithValue("$allocated_length", node.AllocatedLength);
        command.Parameters.AddWithValue("$physical_length", node.PhysicalLength);
        command.Parameters.AddWithValue("$total_length", node.TotalLength);
        command.Parameters.AddWithValue("$total_physical_length", node.TotalPhysicalLength);
        command.Parameters.AddWithValue("$file_count", node.FileCount);
        command.Parameters.AddWithValue("$folder_count", node.FolderCount);
        command.Parameters.AddWithValue("$depth", node.Depth);
        command.Parameters.AddWithValue("$last_modified_utc", (object?)node.LastModifiedUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$attributes", node.Attributes.ToString());
        command.Parameters.AddWithValue("$is_reparse_point", node.IsReparsePoint ? 1 : 0);
        command.Parameters.AddWithValue("$reparse_target", (object?)node.ReparseTarget ?? DBNull.Value);
        command.Parameters.AddWithValue("$reparse_tag", (object?)node.ReparseTag ?? DBNull.Value);
        command.Parameters.AddWithValue("$volume_serial", (object?)node.VolumeSerial ?? DBNull.Value);
        command.Parameters.AddWithValue("$file_id", (object?)node.FileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$usn", (object?)node.Usn ?? DBNull.Value);
        command.Parameters.AddWithValue("$hardlink_count", node.HardLinkCount);
        command.Parameters.AddWithValue("$is_hardlink_duplicate", node.IsHardLinkDuplicate ? 1 : 0);
        command.Parameters.AddWithValue("$category", node.Category);
    }

    private static string NodeInsertSql(string tableName, bool includeVolumeRoot)
    {
        var columns = includeVolumeRoot ? "volume_root_path, " : "";
        var values = includeVolumeRoot ? "$volume_root_path, " : "";
        return $"""
            INSERT OR REPLACE INTO {tableName}
                (scan_id, {columns}id, stable_id, path_hash, parent_stable_id, parent_path_hash,
                 parent_id, name, full_path, kind, extension, length, allocated_length, physical_length,
                 total_length, total_physical_length, file_count, folder_count, depth, last_modified_utc,
                 attributes, is_reparse_point, reparse_target, reparse_tag, volume_serial, file_id,
                 usn, hardlink_count, is_hardlink_duplicate, category)
            VALUES
                ($scan_id, {values}$id, $stable_id, $path_hash, $parent_stable_id, $parent_path_hash,
                 $parent_id, $name, $full_path, $kind, $extension, $length, $allocated_length, $physical_length,
                 $total_length, $total_physical_length, $file_count, $folder_count, $depth, $last_modified_utc,
                 $attributes, $is_reparse_point, $reparse_target, $reparse_tag, $volume_serial, $file_id,
                 $usn, $hardlink_count, $is_hardlink_duplicate, $category);
            """;
    }

    private const string CurrentNodeUpsertSql = """
        INSERT INTO current_nodes
            (stable_id, scan_id, volume_root_path, id, path_hash, parent_stable_id, parent_path_hash,
             parent_id, name, full_path, kind, extension, length, allocated_length, physical_length,
             total_length, total_physical_length, file_count, folder_count, depth, last_modified_utc,
             attributes, is_reparse_point, reparse_target, reparse_tag, volume_serial, file_id,
             usn, hardlink_count, is_hardlink_duplicate, category, last_seen_scan_id, is_deleted)
        VALUES
            ($stable_id, $scan_id, $volume_root_path, $id, $path_hash, $parent_stable_id, $parent_path_hash,
             $parent_id, $name, $full_path, $kind, $extension, $length, $allocated_length, $physical_length,
             $total_length, $total_physical_length, $file_count, $folder_count, $depth, $last_modified_utc,
             $attributes, $is_reparse_point, $reparse_target, $reparse_tag, $volume_serial, $file_id,
             $usn, $hardlink_count, $is_hardlink_duplicate, $category, $last_seen_scan_id, $is_deleted)
        ON CONFLICT(stable_id) DO UPDATE SET
            scan_id = excluded.scan_id,
            volume_root_path = excluded.volume_root_path,
            id = excluded.id,
            path_hash = excluded.path_hash,
            parent_stable_id = excluded.parent_stable_id,
            parent_path_hash = excluded.parent_path_hash,
            parent_id = excluded.parent_id,
            name = excluded.name,
            full_path = excluded.full_path,
            kind = excluded.kind,
            extension = excluded.extension,
            length = excluded.length,
            allocated_length = excluded.allocated_length,
            physical_length = excluded.physical_length,
            total_length = excluded.total_length,
            total_physical_length = excluded.total_physical_length,
            file_count = excluded.file_count,
            folder_count = excluded.folder_count,
            depth = excluded.depth,
            last_modified_utc = excluded.last_modified_utc,
            attributes = excluded.attributes,
            is_reparse_point = excluded.is_reparse_point,
            reparse_target = excluded.reparse_target,
            reparse_tag = excluded.reparse_tag,
            volume_serial = excluded.volume_serial,
            file_id = excluded.file_id,
            usn = excluded.usn,
            hardlink_count = excluded.hardlink_count,
            is_hardlink_duplicate = excluded.is_hardlink_duplicate,
            category = excluded.category,
            last_seen_scan_id = excluded.last_seen_scan_id,
            is_deleted = excluded.is_deleted;
        """;

    private static async Task<VolumeInfo?> LoadVolumeAsync(SqliteConnection connection, string rootPath, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT root_path, name, file_system, label, volume_serial, drive_type, is_ready, total_bytes, free_bytes
            FROM volumes
            WHERE root_path = $root_path;
            """;
        command.Parameters.AddWithValue("$root_path", rootPath);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new VolumeInfo
        {
            RootPath = reader.GetString(0),
            Name = reader.GetString(1),
            FileSystem = reader.IsDBNull(2) ? null : reader.GetString(2),
            Label = reader.IsDBNull(3) ? null : reader.GetString(3),
            VolumeSerial = reader.IsDBNull(4) ? null : reader.GetString(4),
            DriveType = reader.GetString(5),
            IsReady = reader.GetInt32(6) == 1,
            TotalBytes = reader.GetInt64(7),
            FreeBytes = reader.GetInt64(8)
        };
    }

    private const string NodeColumns = """
        id, stable_id, path_hash, parent_stable_id, parent_path_hash, parent_id, name, full_path,
        kind, extension, length, allocated_length, physical_length, total_length, total_physical_length,
        file_count, folder_count, depth, last_modified_utc, attributes, is_reparse_point, reparse_target,
        reparse_tag, volume_serial, file_id, usn, hardlink_count, is_hardlink_duplicate, category
        """;

    private readonly record struct LatestScanHeader(
        Guid ScanId,
        string RootPath,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        ScanStatus Status,
        long TotalPhysicalBytes,
        long FilesScanned,
        long DirectoriesScanned,
        int IssueCount);

    private static async Task<List<FileSystemNode>> LoadNodesAsync(SqliteConnection connection, Guid scanId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, stable_id, path_hash, parent_stable_id, parent_path_hash, parent_id, name, full_path,
                   kind, extension, length, allocated_length, physical_length, total_length, total_physical_length,
                   file_count, folder_count, depth, last_modified_utc, attributes, is_reparse_point, reparse_target,
                   reparse_tag, volume_serial, file_id, usn, hardlink_count, is_hardlink_duplicate, category
            FROM scan_nodes
            WHERE scan_id = $scan_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var nodes = new List<FileSystemNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            nodes.Add(ReadNode(reader));
        }

        return nodes;
    }

    private static FileSystemNode ReadNode(SqliteDataReader reader)
    {
        return new FileSystemNode
        {
            Id = reader.GetInt64(0),
            StableId = reader.GetString(1),
            PathHash = reader.GetString(2),
            ParentStableId = reader.IsDBNull(3) ? null : reader.GetString(3),
            ParentPathHash = reader.IsDBNull(4) ? null : reader.GetString(4),
            ParentId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            Name = reader.GetString(6),
            FullPath = reader.GetString(7),
            Kind = Enum.Parse<FileSystemNodeKind>(reader.GetString(8)),
            Extension = reader.IsDBNull(9) ? null : reader.GetString(9),
            Length = reader.GetInt64(10),
            AllocatedLength = reader.GetInt64(11),
            PhysicalLength = reader.GetInt64(12),
            TotalLength = reader.GetInt64(13),
            TotalPhysicalLength = reader.GetInt64(14),
            FileCount = reader.GetInt32(15),
            FolderCount = reader.GetInt32(16),
            Depth = reader.GetInt32(17),
            LastModifiedUtc = reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
            Attributes = Enum.TryParse<FileAttributes>(reader.GetString(19), out var attributes) ? attributes : 0,
            IsReparsePoint = reader.GetInt32(20) == 1,
            ReparseTarget = reader.IsDBNull(21) ? null : reader.GetString(21),
            ReparseTag = reader.IsDBNull(22) ? null : reader.GetString(22),
            VolumeSerial = reader.IsDBNull(23) ? null : reader.GetString(23),
            FileId = reader.IsDBNull(24) ? null : reader.GetString(24),
            Usn = reader.IsDBNull(25) ? null : reader.GetInt64(25),
            HardLinkCount = reader.GetInt32(26),
            IsHardLinkDuplicate = reader.GetInt32(27) == 1,
            Category = reader.GetString(28)
        };
    }

    private static async Task<List<FileSystemEdge>> LoadEdgesAsync(SqliteConnection connection, Guid scanId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_node_id, target_node_id, target_path, kind, label, evidence
            FROM edges
            WHERE scan_id = $scan_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var edges = new List<FileSystemEdge>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            edges.Add(new FileSystemEdge
            {
                Id = reader.GetInt64(0),
                SourceNodeId = reader.GetInt64(1),
                TargetNodeId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                TargetPath = reader.IsDBNull(3) ? null : reader.GetString(3),
                Kind = Enum.Parse<FileSystemEdgeKind>(reader.GetString(4)),
                Label = reader.GetString(5),
                Evidence = reader.GetString(6)
            });
        }

        return edges;
    }

    private static async Task<List<ScanIssue>> LoadIssuesAsync(SqliteConnection connection, Guid scanId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, node_id, path, operation, message, native_error_code, elevation_may_help, occurred_at_utc
            FROM scan_errors
            WHERE scan_id = $scan_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$scan_id", scanId.ToString());

        var issues = new List<ScanIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            issues.Add(new ScanIssue
            {
                Id = reader.GetInt64(0),
                NodeId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Path = reader.GetString(2),
                Operation = reader.GetString(3),
                Message = reader.GetString(4),
                NativeErrorCode = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                ElevationMayHelp = reader.GetInt32(6) == 1,
                OccurredAtUtc = DateTimeOffset.Parse(reader.GetString(7))
            });
        }

        return issues;
    }

    private static CleanupFinding ReadFinding(SqliteDataReader reader)
    {
        var evidence = reader.IsDBNull(14)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(14)) ?? [];

        return new CleanupFinding
        {
            Id = Guid.Parse(reader.GetString(0)),
            NodeId = reader.GetInt64(1),
            Path = reader.GetString(2),
            DisplayName = reader.GetString(3),
            Category = reader.GetString(4),
            Safety = Enum.Parse<CleanupSafety>(reader.GetString(5)),
            RecommendedAction = Enum.Parse<CleanupActionKind>(reader.GetString(6)),
            SizeBytes = reader.GetInt64(7),
            FileCount = reader.GetInt32(8),
            LastModifiedUtc = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            Confidence = reader.GetDouble(10),
            Explanation = reader.GetString(11),
            MatchedRule = reader.GetString(12),
            AppOrSource = reader.IsDBNull(13) ? null : reader.GetString(13),
            Evidence = evidence
        };
    }

    private const string Schema = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;

        CREATE TABLE IF NOT EXISTS volumes (
            root_path TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            file_system TEXT NULL,
            label TEXT NULL,
            volume_serial TEXT NULL,
            drive_type TEXT NOT NULL,
            is_ready INTEGER NOT NULL,
            total_bytes INTEGER NOT NULL,
            free_bytes INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS scans (
            id TEXT PRIMARY KEY,
            volume_root_path TEXT NOT NULL,
            root_path TEXT NOT NULL,
            started_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            status TEXT NOT NULL,
            total_logical_bytes INTEGER NOT NULL,
            total_physical_bytes INTEGER NOT NULL,
            files_scanned INTEGER NOT NULL,
            directories_scanned INTEGER NOT NULL,
            issue_count INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS scan_batches (
            scan_id TEXT NOT NULL,
            batch_number INTEGER NOT NULL,
            captured_at_utc TEXT NOT NULL,
            node_count INTEGER NOT NULL,
            edge_count INTEGER NOT NULL,
            issue_count INTEGER NOT NULL,
            accounted_bytes INTEGER NOT NULL,
            queue_depth INTEGER NOT NULL,
            PRIMARY KEY(scan_id, batch_number)
        );

        CREATE TABLE IF NOT EXISTS scan_checkpoints (
            scan_id TEXT NOT NULL,
            volume_root_path TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            last_node_id INTEGER NOT NULL,
            files_scanned INTEGER NOT NULL,
            directories_scanned INTEGER NOT NULL,
            accounted_bytes INTEGER NOT NULL,
            queue_depth INTEGER NOT NULL,
            status TEXT NOT NULL,
            PRIMARY KEY(scan_id, volume_root_path)
        );

        CREATE TABLE IF NOT EXISTS scan_nodes (
            scan_id TEXT NOT NULL,
            id INTEGER NOT NULL,
            stable_id TEXT NOT NULL,
            path_hash TEXT NOT NULL,
            parent_stable_id TEXT NULL,
            parent_path_hash TEXT NULL,
            parent_id INTEGER NULL,
            name TEXT NOT NULL,
            full_path TEXT NOT NULL,
            kind TEXT NOT NULL,
            extension TEXT NULL,
            length INTEGER NOT NULL,
            allocated_length INTEGER NOT NULL,
            physical_length INTEGER NOT NULL,
            total_length INTEGER NOT NULL,
            total_physical_length INTEGER NOT NULL,
            file_count INTEGER NOT NULL,
            folder_count INTEGER NOT NULL,
            depth INTEGER NOT NULL,
            last_modified_utc TEXT NULL,
            attributes TEXT NOT NULL,
            is_reparse_point INTEGER NOT NULL,
            reparse_target TEXT NULL,
            reparse_tag TEXT NULL,
            volume_serial TEXT NULL,
            file_id TEXT NULL,
            usn INTEGER NULL,
            hardlink_count INTEGER NOT NULL,
            is_hardlink_duplicate INTEGER NOT NULL,
            category TEXT NOT NULL,
            PRIMARY KEY(scan_id, id)
        );

        CREATE TABLE IF NOT EXISTS current_nodes (
            stable_id TEXT PRIMARY KEY,
            scan_id TEXT NOT NULL,
            volume_root_path TEXT NOT NULL,
            id INTEGER NOT NULL,
            path_hash TEXT NOT NULL,
            parent_stable_id TEXT NULL,
            parent_path_hash TEXT NULL,
            parent_id INTEGER NULL,
            name TEXT NOT NULL,
            full_path TEXT NOT NULL,
            kind TEXT NOT NULL,
            extension TEXT NULL,
            length INTEGER NOT NULL,
            allocated_length INTEGER NOT NULL,
            physical_length INTEGER NOT NULL,
            total_length INTEGER NOT NULL,
            total_physical_length INTEGER NOT NULL,
            file_count INTEGER NOT NULL,
            folder_count INTEGER NOT NULL,
            depth INTEGER NOT NULL,
            last_modified_utc TEXT NULL,
            attributes TEXT NOT NULL,
            is_reparse_point INTEGER NOT NULL,
            reparse_target TEXT NULL,
            reparse_tag TEXT NULL,
            volume_serial TEXT NULL,
            file_id TEXT NULL,
            usn INTEGER NULL,
            hardlink_count INTEGER NOT NULL,
            is_hardlink_duplicate INTEGER NOT NULL,
            category TEXT NOT NULL,
            last_seen_scan_id TEXT NOT NULL,
            is_deleted INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS node_aggregates (
            scan_id TEXT NOT NULL,
            stable_id TEXT NOT NULL,
            total_length INTEGER NOT NULL,
            total_physical_length INTEGER NOT NULL,
            file_count INTEGER NOT NULL,
            folder_count INTEGER NOT NULL,
            PRIMARY KEY(scan_id, stable_id)
        );

        CREATE TABLE IF NOT EXISTS change_records (
            id TEXT PRIMARY KEY,
            scan_id TEXT NOT NULL,
            stable_id TEXT NOT NULL,
            path TEXT NOT NULL,
            previous_path TEXT NULL,
            kind TEXT NOT NULL,
            previous_size_bytes INTEGER NOT NULL,
            current_size_bytes INTEGER NOT NULL,
            detected_at_utc TEXT NOT NULL,
            reason TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS edges (
            scan_id TEXT NOT NULL,
            id INTEGER NOT NULL,
            source_node_id INTEGER NOT NULL,
            target_node_id INTEGER NULL,
            target_path TEXT NULL,
            kind TEXT NOT NULL,
            label TEXT NOT NULL,
            evidence TEXT NOT NULL,
            PRIMARY KEY(scan_id, id)
        );

        CREATE TABLE IF NOT EXISTS relationship_evidence (
            id TEXT PRIMARY KEY,
            scan_id TEXT NOT NULL,
            source_node_id INTEGER NOT NULL,
            source_path TEXT NOT NULL,
            target_path TEXT NULL,
            kind TEXT NOT NULL,
            label TEXT NOT NULL,
            owner TEXT NOT NULL,
            evidence_source TEXT NOT NULL,
            evidence_detail TEXT NOT NULL,
            confidence REAL NOT NULL
        );

        CREATE TABLE IF NOT EXISTS scan_errors (
            scan_id TEXT NOT NULL,
            id INTEGER NOT NULL,
            node_id INTEGER NULL,
            path TEXT NOT NULL,
            operation TEXT NOT NULL,
            message TEXT NOT NULL,
            native_error_code INTEGER NULL,
            elevation_may_help INTEGER NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            PRIMARY KEY(scan_id, id)
        );

        CREATE TABLE IF NOT EXISTS cleanup_findings (
            scan_id TEXT NOT NULL,
            id TEXT NOT NULL,
            node_id INTEGER NOT NULL,
            path TEXT NOT NULL,
            display_name TEXT NOT NULL,
            category TEXT NOT NULL,
            safety TEXT NOT NULL,
            recommended_action TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            file_count INTEGER NOT NULL,
            last_modified_utc TEXT NULL,
            confidence REAL NOT NULL,
            explanation TEXT NOT NULL,
            matched_rule TEXT NOT NULL,
            app_or_source TEXT NULL,
            evidence_json TEXT NOT NULL,
            PRIMARY KEY(scan_id, id)
        );

        CREATE TABLE IF NOT EXISTS insight_findings (
            id TEXT PRIMARY KEY,
            scan_id TEXT NOT NULL,
            node_id INTEGER NOT NULL,
            stable_id TEXT NOT NULL,
            path TEXT NOT NULL,
            tool TEXT NOT NULL,
            title TEXT NOT NULL,
            description TEXT NOT NULL,
            safety TEXT NOT NULL,
            recommended_action TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            confidence REAL NOT NULL,
            evidence TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS hash_groups (
            scan_id TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            partial_hash TEXT NULL,
            full_hash TEXT NULL,
            node_count INTEGER NOT NULL,
            reclaimable_bytes INTEGER NOT NULL,
            PRIMARY KEY(scan_id, size_bytes, partial_hash, full_hash)
        );

        CREATE TABLE IF NOT EXISTS cleanup_actions (
            id TEXT PRIMARY KEY,
            scan_id TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            status TEXT NOT NULL,
            planned_bytes INTEGER NOT NULL,
            evidence_json TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_scan_nodes_scan_parent ON scan_nodes(scan_id, parent_id);
        CREATE INDEX IF NOT EXISTS ix_scan_nodes_scan_size ON scan_nodes(scan_id, total_physical_length DESC);
        CREATE INDEX IF NOT EXISTS ix_current_nodes_path_hash ON current_nodes(path_hash);
        CREATE INDEX IF NOT EXISTS ix_current_nodes_volume_deleted ON current_nodes(volume_root_path, is_deleted);
        CREATE INDEX IF NOT EXISTS ix_change_records_scan ON change_records(scan_id, kind);
        CREATE INDEX IF NOT EXISTS ix_relationship_scan ON relationship_evidence(scan_id, source_node_id);
        CREATE INDEX IF NOT EXISTS ix_insights_scan ON insight_findings(scan_id, size_bytes DESC);
        """;
}
