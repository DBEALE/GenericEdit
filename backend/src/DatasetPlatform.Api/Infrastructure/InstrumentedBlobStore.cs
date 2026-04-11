using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// Decorator that wraps any <see cref="IBlobStore"/> implementation and appends a structured
/// line to <c>BlobStore.log</c> for every call, recording the implementing class name,
/// method, path/pattern argument, result, and elapsed time.
/// An optional artificial delay can be injected via <see cref="StorageOptions.SimulatedLatencyMs"/>
/// to surface inefficient access patterns during development.
/// </summary>
public sealed class InstrumentedBlobStore(IBlobStore inner, IWebHostEnvironment environment, IOptions<StorageOptions> options) : IBlobStore
{
    private static readonly SemaphoreSlim FileWriteLock = new(1, 1);
    private readonly string _implName = inner.GetType().Name;
    private readonly string _logPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "BlobStore.log"));
    private readonly int _simulatedLatencyMs = options.Value.SimulatedLatencyMs;

    public async Task<Stream?> GetBlobAsync(string path, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetBlobAsync(path, cancellationToken);
        await PadToMinLatencyAsync(sw, cancellationToken);
        await AppendAsync($"GetBlob      path={path} found={result is not null} {sw.ElapsedMilliseconds}ms", cancellationToken);
        return result;
    }

    public async Task PutBlobAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await inner.PutBlobAsync(path, content, cancellationToken);
        await PadToMinLatencyAsync(sw, cancellationToken);
        await AppendAsync($"PutBlob      path={path} {sw.ElapsedMilliseconds}ms", cancellationToken);
    }

    public async Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.BlobExistsAsync(path, cancellationToken);
        await PadToMinLatencyAsync(sw, cancellationToken);
        await AppendAsync($"BlobExists   path={path} exists={result} {sw.ElapsedMilliseconds}ms", cancellationToken);
        return result;
    }

    public async Task DeleteBlobAsync(string path, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await inner.DeleteBlobAsync(path, cancellationToken);
        await PadToMinLatencyAsync(sw, cancellationToken);
        await AppendAsync($"DeleteBlob   path={path} {sw.ElapsedMilliseconds}ms", cancellationToken);
    }

    public async Task<IReadOnlyList<string>> QueryBlobsAsync(string wildcardPattern, CancellationToken cancellationToken)
    {
        var entries = await QueryBlobsWithMetadataAsync(wildcardPattern, cancellationToken);
        return entries.Select(e => e.Key).ToList();
    }

    public async Task<IReadOnlyList<BlobEntry>> QueryBlobsWithMetadataAsync(string wildcardPattern, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.QueryBlobsWithMetadataAsync(wildcardPattern, cancellationToken);
        await PadToMinLatencyAsync(sw, cancellationToken);
        await AppendAsync($"QueryBlobs   pattern={wildcardPattern} results={result.Count} {sw.ElapsedMilliseconds}ms", cancellationToken);
        return result;
    }

    private Task PadToMinLatencyAsync(Stopwatch sw, CancellationToken cancellationToken)
    {
        if (_simulatedLatencyMs <= 0) return Task.CompletedTask;
        var remaining = _simulatedLatencyMs - (int)sw.ElapsedMilliseconds;
        return remaining > 0 ? Task.Delay(remaining, cancellationToken) : Task.CompletedTask;
    }

    private async Task AppendAsync(string details, CancellationToken cancellationToken)
    {
        var line = $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff zzz}] {_implName}.{details}";
        await FileWriteLock.WaitAsync(cancellationToken);
        try
        {
            var existing = File.Exists(_logPath)
                ? await File.ReadAllLinesAsync(_logPath, cancellationToken)
                : [];
            var retained = existing.Append(line).TakeLast(1000).ToArray();
            await File.WriteAllLinesAsync(_logPath, retained, cancellationToken);
        }
        finally
        {
            FileWriteLock.Release();
        }
    }
}
