using System.ComponentModel;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Ai;

public sealed class CodexAuthService : ICodexAuthService
{
    private readonly IProcessRunner _processRunner;

    public CodexAuthService()
        : this(new ProcessRunner())
    {
    }

    public CodexAuthService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<CodexAuthStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(new ProcessRunRequest
            {
                FileName = "codex",
                Arguments = ["login", "status"],
                Timeout = TimeSpan.FromSeconds(15)
            }, cancellationToken).ConfigureAwait(false);

            return CodexAuthStatusParser.Parse(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Exception ex) when (IsMissingCodex(ex))
        {
            return CodexAuthStatusParser.NotInstalled(ex.Message);
        }
        catch (Exception ex)
        {
            return CodexAuthStatusParser.Error(ex.Message);
        }
    }

    public Task StartLoginAsync(CancellationToken cancellationToken = default)
    {
        const string script = "$Host.UI.RawUI.WindowTitle = 'Disk Space Inspector Codex Login'; codex login; Write-Host ''; Write-Host 'Codex login finished. You can close this window.'; Read-Host 'Press Enter to close'";
        return _processRunner.StartDetachedAsync(
            "powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
            cancellationToken);
    }

    private static bool IsMissingCodex(Exception ex)
    {
        return ex is FileNotFoundException ||
               ex is Win32Exception { NativeErrorCode: 2 };
    }
}
