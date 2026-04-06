using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// <see cref="IBlobStore"/> implementation that reads and writes blobs as files on the local filesystem.
/// Used in development and single-node deployments.
///
/// <para>
/// All paths are resolved relative to <see cref="StorageOptions.BasePath"/> (defaults to <c>./data/</c>).
/// Directory structure mirrors the logical blob path, e.g.:
/// <c>data/schemas/market-rates.json</c>, <c>data/instances/market-rates/abc123.json</c>.
/// </para>
///
/// <para>
/// <b>Concurrency note:</b> This store is not safe for concurrent writes from multiple processes
/// or multiple server instances. The <c>BlobDataRepository</c> wraps all calls in a
/// <c>SemaphoreSlim</c> to serialise access within a single process.
/// </para>
/// </summary>
public sealed class FileSystemBlobStore(IOptions<StorageOptions> options) : IBlobStore
{
    private readonly string _basePath = Path.GetFullPath(options.Value.BasePath);

    public async Task<Stream?> GetBlobAsync(string path, CancellationToken cancellationToken)
    {
        var absolute = ResolvePath(path);
        if (!File.Exists(absolute))
        {
            return null;
        }

        var stream = new MemoryStream();
        await using var source = File.OpenRead(absolute);
        await source.CopyToAsync(stream, cancellationToken);
        stream.Position = 0;
        return stream;
    }

    public async Task PutBlobAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        var absolute = ResolvePath(path);
        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        content.Position = 0;
        await using var destination = File.Create(absolute);
        await content.CopyToAsync(destination, cancellationToken);
    }

    public Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken)
    {
        var absolute = ResolvePath(path);
        return Task.FromResult(File.Exists(absolute));
    }

    public Task DeleteBlobAsync(string path, CancellationToken cancellationToken)
    {
        var absolute = ResolvePath(path);
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> QueryBlobsAsync(string wildcardPattern, CancellationToken cancellationToken)
    {
        var absolutePattern = ResolvePath(wildcardPattern);
        var searchRoot = GetSearchRoot(absolutePattern);
        if (!Directory.Exists(searchRoot))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var regex = WildcardToRegex(absolutePattern.Replace('/', '\\'));
        var files = Directory
            .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Where(path => regex.IsMatch(path.Replace('/', '\\')))
            .Select(path => Path.GetRelativePath(_basePath, path)
                .Replace('\\', '/'))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(_basePath, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string GetSearchRoot(string wildcardPath)
    {
        var wildcardIndex = wildcardPath.IndexOfAny(['*', '?']);
        if (wildcardIndex < 0)
        {
            var directory = Path.GetDirectoryName(wildcardPath);
            return string.IsNullOrWhiteSpace(directory) ? wildcardPath : directory;
        }

        var prefix = wildcardPath[..wildcardIndex];
        var lastSlash = prefix.LastIndexOfAny(['\\', '/']);
        if (lastSlash < 0)
        {
            return Directory.GetCurrentDirectory();
        }

        return prefix[..lastSlash];
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
