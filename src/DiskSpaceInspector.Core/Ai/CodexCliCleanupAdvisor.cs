using System.Text.Json;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Ai;

public sealed class CodexCliCleanupAdvisor : IAiCleanupAdvisor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IProcessRunner _processRunner;

    public CodexCliCleanupAdvisor()
        : this(new ProcessRunner())
    {
    }

    public CodexCliCleanupAdvisor(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<AiCleanupRecommendation>> RecommendAsync(
        AiCleanupAdvisorRequest request,
        AiCleanupAdvisorOptions options,
        CancellationToken cancellationToken = default)
    {
        var candidates = request.CleanupFindings
            .OrderBy(f => f.Safety)
            .ThenByDescending(f => f.SizeBytes)
            .Take(Math.Max(1, options.MaxCandidateCount))
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var schemaPath = Path.Combine(Path.GetTempPath(), $"DiskSpaceInspector-codex-schema-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(schemaPath, RecommendationSchemaJson, cancellationToken).ConfigureAwait(false);

        try
        {
            var prompt = BuildPrompt(request, candidates, options);
            var result = await _processRunner.RunAsync(new ProcessRunRequest
            {
                FileName = "codex",
                Arguments =
                [
                    "exec",
                    "--ephemeral",
                    "--skip-git-repo-check",
                    "--sandbox",
                    "read-only",
                    "--output-schema",
                    schemaPath,
                    "-"
                ],
                StandardInput = prompt,
                WorkingDirectory = Environment.CurrentDirectory,
                Timeout = TimeSpan.FromMinutes(3)
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Codex AI request failed: {TrimForStatus(result.StandardError + "\n" + result.StandardOutput)}");
            }

            var generated = ParseGeneratedRecommendations(result.StandardOutput)
                .Take(Math.Max(1, options.MaxRecommendations))
                .ToList();

            return AiCleanupRecommendationSafetyGate.Apply(candidates, generated, "Codex");
        }
        finally
        {
            TryDelete(schemaPath);
        }
    }

    private static string BuildPrompt(
        AiCleanupAdvisorRequest request,
        IReadOnlyList<CleanupFinding> candidates,
        AiCleanupAdvisorOptions options)
    {
        var input = new
        {
            scanId = request.ScanId,
            maxRecommendations = options.MaxRecommendations,
            cleanupCandidates = candidates.Select(f => new
            {
                id = f.Id,
                f.Path,
                f.DisplayName,
                f.Category,
                safety = f.Safety.ToString(),
                recommendedAction = f.RecommendedAction.ToString(),
                f.SizeBytes,
                f.FileCount,
                lastModifiedUtc = f.LastModifiedUtc?.ToString("O"),
                f.Confidence,
                f.Explanation,
                f.MatchedRule,
                f.AppOrSource,
                f.Evidence
            }),
            relatedInsights = request.Insights
                .OrderByDescending(i => i.SizeBytes)
                .Take(40)
                .Select(i => new
                {
                    i.Path,
                    i.Tool,
                    i.Title,
                    safety = i.Safety.ToString(),
                    action = i.RecommendedAction.ToString(),
                    i.SizeBytes,
                    i.Confidence,
                    i.Evidence
                }),
            relationships = request.Relationships
                .OrderByDescending(r => r.Evidence.Confidence)
                .Take(40)
                .Select(r => new
                {
                    r.SourcePath,
                    r.TargetPath,
                    kind = r.Kind.ToString(),
                    r.Label,
                    r.Owner,
                    evidenceSource = r.Evidence.Source,
                    evidenceDetail = r.Evidence.Detail,
                    confidence = r.Evidence.Confidence
                })
        };

        return $$"""
            You are the Disk Space Inspector Codex AI cleanup advisor.
            Rank and explain Windows storage cleanup candidates.
            Return JSON only and conform exactly to the provided output schema.

            Hard rules:
            - Use only paths present in cleanupCandidates.
            - Do not invent files, folders, apps, sizes, or scan evidence.
            - Never recommend direct deletion for candidates marked Blocked or UseSystemCleanup.
            - Prefer these verbs: Clear cache, Empty recycle bin, Review downloads, Remove duplicate copy, Run Windows cleanup, Uninstall app, Archive or move, Leave alone.
            - For Review items, explain what the user should verify first.
            - This task is advisory only. Do not run commands or inspect the local filesystem.

            Disk Space Inspector cleanup context:
            {{JsonSerializer.Serialize(input, JsonOptions)}}
            """;
    }

    private static IReadOnlyList<GeneratedAiCleanupRecommendation> ParseGeneratedRecommendations(string output)
    {
        output = StripToJson(output);
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var envelope = JsonSerializer.Deserialize<GeneratedEnvelope>(output, JsonOptions);
        return envelope?.Recommendations ?? [];
    }

    private static string StripToJson(string output)
    {
        output = output.Trim();
        if (output.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = output.IndexOf('\n');
            var lastFence = output.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                output = output[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        var firstBrace = output.IndexOf('{');
        var lastBrace = output.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace > firstBrace
            ? output[firstBrace..(lastBrace + 1)]
            : output;
    }

    private static string TrimForStatus(string responseBody)
    {
        responseBody = responseBody.ReplaceLineEndings(" ").Trim();
        return responseBody.Length <= 500 ? responseBody : responseBody[..500] + "...";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary schema cleanup is best effort.
        }
    }

    private sealed class GeneratedEnvelope
    {
        public IReadOnlyList<GeneratedAiCleanupRecommendation> Recommendations { get; init; } = [];
    }

    private const string RecommendationSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "recommendations": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "path": { "type": "string" },
                  "recommendation": {
                    "type": "string",
                    "enum": [
                      "Clear cache",
                      "Empty recycle bin",
                      "Review downloads",
                      "Remove duplicate copy",
                      "Run Windows cleanup",
                      "Uninstall app",
                      "Archive or move",
                      "Leave alone"
                    ]
                  },
                  "risk": {
                    "type": "string",
                    "enum": [ "Safe", "Review", "UseSystemCleanup", "Blocked" ]
                  },
                  "confidence": { "type": "number" },
                  "reasoning": { "type": "string" },
                  "evidence": { "type": "string" }
                },
                "required": [ "path", "recommendation", "risk", "confidence", "reasoning", "evidence" ]
              }
            }
          },
          "required": [ "recommendations" ]
        }
        """;
}
