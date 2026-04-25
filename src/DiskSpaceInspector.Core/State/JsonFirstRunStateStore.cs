using System.Text.Json;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.State;

public sealed class JsonFirstRunStateStore : IFirstRunStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public JsonFirstRunStateStore(string path)
    {
        _path = path;
    }

    public async Task<FirstRunState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new FirstRunState();
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<FirstRunState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? new FirstRunState();
    }

    public async Task SaveAsync(FirstRunState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
