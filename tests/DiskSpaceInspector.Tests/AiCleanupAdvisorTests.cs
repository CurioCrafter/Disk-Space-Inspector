using System.Net;
using DiskSpaceInspector.Core.Ai;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class AiCleanupAdvisorTests
{
    [TestMethod]
    public void CodexAuthStatusParser_ParsesExpectedStates()
    {
        Assert.AreEqual(
            CodexAuthKind.ChatGpt,
            CodexAuthStatusParser.Parse(0, "Logged in using ChatGPT", "").Kind);
        Assert.AreEqual(
            CodexAuthKind.ApiKey,
            CodexAuthStatusParser.Parse(0, "Logged in using API key", "").Kind);
        Assert.AreEqual(
            CodexAuthKind.NotLoggedIn,
            CodexAuthStatusParser.Parse(1, "Not logged in", "").Kind);
        Assert.AreEqual(
            CodexAuthKind.CodexNotInstalled,
            CodexAuthStatusParser.NotInstalled("missing").Kind);
    }

    [TestMethod]
    public async Task CodexAuthService_ReturnsNotInstalledWhenCliIsMissing()
    {
        var service = new CodexAuthService(new FakeProcessRunner
        {
            RunException = new FileNotFoundException("codex was not found")
        });

        var status = await service.GetStatusAsync();

        Assert.AreEqual(CodexAuthKind.CodexNotInstalled, status.Kind);
        StringAssert.Contains(status.DisplayText, "npm i -g @openai/codex");
    }

    [TestMethod]
    public async Task CodexCliCleanupAdvisor_UsesCodexExecSchemaAndPreservesSafetyGuardrails()
    {
        var temp = Finding(
            path: @"C:\Users\andre\AppData\Local\Temp\cache.bin",
            safety: CleanupSafety.Safe,
            action: CleanupActionKind.ClearCache,
            sizeBytes: 100);
        var blocked = Finding(
            path: @"C:\Windows\System32\drivers\etc\hosts",
            safety: CleanupSafety.Blocked,
            action: CleanupActionKind.LeaveAlone,
            sizeBytes: 50);
        var runner = new FakeProcessRunner
        {
            Result = new ProcessRunResult
            {
                ExitCode = 0,
                StandardOutput = """
                    {
                      "recommendations": [
                        {
                          "path": "C:\\Users\\andre\\AppData\\Local\\Temp\\cache.bin",
                          "recommendation": "Clear cache",
                          "risk": "Safe",
                          "confidence": 0.9,
                          "reasoning": "Temp cache can be regenerated.",
                          "evidence": "temp-path"
                        },
                        {
                          "path": "C:\\Windows\\System32\\drivers\\etc\\hosts",
                          "recommendation": "Clear cache",
                          "risk": "Safe",
                          "confidence": 0.99,
                          "reasoning": "The model tried to downgrade risk.",
                          "evidence": "bad"
                        },
                        {
                          "path": "C:\\invented.bin",
                          "recommendation": "Clear cache",
                          "risk": "Safe",
                          "confidence": 1,
                          "reasoning": "invented",
                          "evidence": "none"
                        }
                      ]
                    }
                    """
            }
        };
        var advisor = new CodexCliCleanupAdvisor(runner);

        var recommendations = await advisor.RecommendAsync(
            new AiCleanupAdvisorRequest
            {
                ScanId = Guid.NewGuid(),
                CleanupFindings = [temp, blocked]
            },
            new AiCleanupAdvisorOptions
            {
                Model = "Codex",
                MaxCandidateCount = 80,
                MaxRecommendations = 25
            });

        Assert.AreEqual("codex", runner.LastRequest?.FileName);
        CollectionAssert.Contains(runner.LastRequest?.Arguments.ToList(), "exec");
        CollectionAssert.Contains(runner.LastRequest?.Arguments.ToList(), "--ephemeral");
        CollectionAssert.Contains(runner.LastRequest?.Arguments.ToList(), "--skip-git-repo-check");
        CollectionAssert.Contains(runner.LastRequest?.Arguments.ToList(), "read-only");
        CollectionAssert.Contains(runner.LastRequest?.Arguments.ToList(), "--output-schema");
        CollectionAssert.Contains(runner.LastRequest?.Arguments.ToList(), "-");
        StringAssert.Contains(runner.LastRequest?.StandardInput ?? "", "Do not run commands");

        Assert.AreEqual(2, recommendations.Count);
        Assert.IsTrue(recommendations.Single(r => r.Path == temp.Path).CanStage);

        var blockedRecommendation = recommendations.Single(r => r.Path == blocked.Path);
        Assert.AreEqual(CleanupSafety.Blocked, blockedRecommendation.Safety);
        Assert.AreEqual(CleanupActionKind.LeaveAlone, blockedRecommendation.RecommendedAction);
        Assert.IsFalse(blockedRecommendation.CanStage);
    }

    [TestMethod]
    public async Task OpenAiCleanupAdvisor_ParsesResponsesOutputAndPreservesSafetyGuardrails()
    {
        var temp = Finding(
            path: @"C:\Users\andre\AppData\Local\Temp\cache.bin",
            safety: CleanupSafety.Safe,
            action: CleanupActionKind.ClearCache,
            sizeBytes: 100);
        var blocked = Finding(
            path: @"C:\Windows\System32\drivers\etc\hosts",
            safety: CleanupSafety.Blocked,
            action: CleanupActionKind.LeaveAlone,
            sizeBytes: 50);

        using var httpClient = new HttpClient(new StubHandler("""
            {
              "output": [
                {
                  "content": [
                    {
                      "type": "output_text",
                      "text": "{\"recommendations\":[{\"path\":\"C:\\\\Users\\\\andre\\\\AppData\\\\Local\\\\Temp\\\\cache.bin\",\"recommendation\":\"Clear cache\",\"risk\":\"Safe\",\"confidence\":0.9,\"reasoning\":\"Temp cache can be regenerated.\",\"evidence\":\"temp-path\"},{\"path\":\"C:\\\\Windows\\\\System32\\\\drivers\\\\etc\\\\hosts\",\"recommendation\":\"Clear cache\",\"risk\":\"Safe\",\"confidence\":0.99,\"reasoning\":\"The model tried to downgrade risk.\",\"evidence\":\"bad\"},{\"path\":\"C:\\\\made-up.bin\",\"recommendation\":\"Clear cache\",\"risk\":\"Safe\",\"confidence\":1,\"reasoning\":\"invented\",\"evidence\":\"none\"}]}"
                    }
                  ]
                }
              ]
            }
            """));
        var advisor = new OpenAiCleanupAdvisor(httpClient);

        var recommendations = await advisor.RecommendAsync(
            new AiCleanupAdvisorRequest
            {
                ScanId = Guid.NewGuid(),
                CleanupFindings = [temp, blocked]
            },
            new AiCleanupAdvisorOptions
            {
                ApiKey = "test-key",
                Model = "test-model",
                Endpoint = "https://example.test/v1/responses"
            });

        Assert.AreEqual(2, recommendations.Count);

        var tempRecommendation = recommendations.Single(r => r.Path == temp.Path);
        Assert.AreEqual(CleanupSafety.Safe, tempRecommendation.Safety);
        Assert.AreEqual(CleanupActionKind.ClearCache, tempRecommendation.RecommendedAction);
        Assert.IsTrue(tempRecommendation.CanStage);

        var blockedRecommendation = recommendations.Single(r => r.Path == blocked.Path);
        Assert.AreEqual(CleanupSafety.Blocked, blockedRecommendation.Safety);
        Assert.AreEqual(CleanupActionKind.LeaveAlone, blockedRecommendation.RecommendedAction);
        Assert.IsFalse(blockedRecommendation.CanStage);
        StringAssert.Contains(blockedRecommendation.Guardrail, "Blocked by Disk Space Inspector");
    }

    [TestMethod]
    public void SafetyGate_UsesMoreConservativeRiskThanModel()
    {
        var review = Finding(
            path: @"C:\Users\andre\Downloads\old.iso",
            safety: CleanupSafety.Review,
            action: CleanupActionKind.ReviewDownloads,
            sizeBytes: 1024);

        var recommendations = AiCleanupRecommendationSafetyGate.Apply(
            [review],
            [
                new GeneratedAiCleanupRecommendation
                {
                    Path = review.Path,
                    Recommendation = "Clear cache",
                    Risk = "Safe",
                    Confidence = 1,
                    Reasoning = "The model said this was safe.",
                    Evidence = "download"
                }
            ],
            "test-model");

        Assert.AreEqual(1, recommendations.Count);
        Assert.AreEqual(CleanupSafety.Review, recommendations[0].Safety);
        Assert.AreEqual(CleanupActionKind.ReviewDownloads, recommendations[0].RecommendedAction);
    }

    private static CleanupFinding Finding(string path, CleanupSafety safety, CleanupActionKind action, long sizeBytes)
    {
        return new CleanupFinding
        {
            NodeId = Random.Shared.NextInt64(1, 1000),
            Path = path,
            DisplayName = Path.GetFileName(path),
            Category = "Test",
            Safety = safety,
            RecommendedAction = action,
            SizeBytes = sizeBytes,
            FileCount = 1,
            Confidence = 0.8,
            Explanation = "Fixture finding.",
            MatchedRule = "fixture",
            Evidence =
            {
                ["path"] = path,
                ["matchedRule"] = "fixture"
            }
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("test-key", request.Headers.Authorization?.Parameter);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public ProcessRunRequest? LastRequest { get; private set; }

        public ProcessRunResult Result { get; init; } = new();

        public Exception? RunException { get; init; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (RunException is not null)
            {
                throw RunException;
            }

            return Task.FromResult(Result);
        }

        public Task StartDetachedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
