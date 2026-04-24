using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Ai;

public sealed class OpenAiCleanupAdvisor : IAiCleanupAdvisor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public OpenAiCleanupAdvisor()
        : this(new HttpClient())
    {
    }

    public OpenAiCleanupAdvisor(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<AiCleanupRecommendation>> RecommendAsync(
        AiCleanupAdvisorRequest request,
        AiCleanupAdvisorOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException("OpenAI API key is required for AI cleanup recommendations.");
        }

        var candidates = request.CleanupFindings
            .OrderBy(f => f.Safety)
            .ThenByDescending(f => f.SizeBytes)
            .Take(Math.Max(1, options.MaxCandidateCount))
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        message.Content = new StringContent(
            JsonSerializer.Serialize(BuildPayload(request, candidates, options), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AI request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {TrimForStatus(responseBody)}");
        }

        var generated = ParseGeneratedRecommendations(responseBody)
            .Take(Math.Max(1, options.MaxRecommendations))
            .ToList();

        return AiCleanupRecommendationSafetyGate.Apply(candidates, generated, options.Model);
    }

    private static object BuildPayload(
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

        return new
        {
            model = options.Model,
            instructions = """
                You are the Disk Space Inspector AI Cleanup Advisor. Rank and explain storage cleanup candidates.
                You must only recommend paths present in cleanupCandidates. Do not invent files, folders, apps, or sizes.
                Never recommend direct deletion for candidates marked Blocked or UseSystemCleanup.
                Prefer these verbs: Clear cache, Empty recycle bin, Review downloads, Remove duplicate copy, Run Windows cleanup, Uninstall app, Archive or move, Leave alone.
                For Review items, explain what the user should verify before removing anything.
                Return compact JSON only.
                """,
            input = JsonSerializer.Serialize(input, JsonOptions),
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "disk_space_inspector_cleanup_recommendations",
                    strict = true,
                    schema = RecommendationSchema
                }
            }
        };
    }

    private static IReadOnlyList<GeneratedAiCleanupRecommendation> ParseGeneratedRecommendations(string responseJson)
    {
        var outputText = ExtractOutputText(responseJson);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return [];
        }

        outputText = StripCodeFence(outputText);
        var envelope = JsonSerializer.Deserialize<GeneratedEnvelope>(outputText, JsonOptions);
        return envelope?.Recommendations ?? [];
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    contentItem.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static string StripCodeFence(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstLineEnd = value.IndexOf('\n');
        var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || lastFence <= firstLineEnd)
        {
            return value;
        }

        return value[(firstLineEnd + 1)..lastFence].Trim();
    }

    private static string TrimForStatus(string responseBody)
    {
        responseBody = responseBody.ReplaceLineEndings(" ").Trim();
        return responseBody.Length <= 500 ? responseBody : responseBody[..500] + "...";
    }

    private sealed class GeneratedEnvelope
    {
        public IReadOnlyList<GeneratedAiCleanupRecommendation> Recommendations { get; init; } = [];
    }

    private static readonly object RecommendationSchema = new
    {
        type = "object",
        additionalProperties = false,
        properties = new
        {
            recommendations = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new
                    {
                        path = new { type = "string" },
                        recommendation = new
                        {
                            type = "string",
                            @enum = new[]
                            {
                                "Clear cache",
                                "Empty recycle bin",
                                "Review downloads",
                                "Remove duplicate copy",
                                "Run Windows cleanup",
                                "Uninstall app",
                                "Archive or move",
                                "Leave alone"
                            }
                        },
                        risk = new
                        {
                            type = "string",
                            @enum = new[] { "Safe", "Review", "UseSystemCleanup", "Blocked" }
                        },
                        confidence = new { type = "number" },
                        reasoning = new { type = "string" },
                        evidence = new { type = "string" }
                    },
                    required = new[] { "path", "recommendation", "risk", "confidence", "reasoning", "evidence" }
                }
            }
        },
        required = new[] { "recommendations" }
    };
}
