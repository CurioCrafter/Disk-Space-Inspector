namespace DiskSpaceInspector.Core.Models;

public sealed class AiCleanupAdvisorOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gpt-4.1-mini";

    public string Endpoint { get; init; } = "https://api.openai.com/v1/responses";

    public int MaxCandidateCount { get; init; } = 80;

    public int MaxRecommendations { get; init; } = 25;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
