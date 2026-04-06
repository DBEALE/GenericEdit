namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// Low-level binary storage abstraction. Hides the difference between local filesystem
/// and cloud object storage (S3) from the repository layer.
///
/// <para>
/// Paths are relative to a configured root (e.g. <c>./data/</c> for the filesystem adapter,
/// or a prefix within an S3 bucket). Callers should never pass absolute paths.
/// </para>
///
/// <para>Implementations: <see cref="FileSystemBlobStore"/>, <c>S3BlobStore</c>.</para>
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Opens the blob at <paramref name="path"/> and returns its content as a stream,
    /// or <c>null</c> if the blob does not exist.
    /// </summary>
    Task<Stream?> GetBlobAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, creating parent directories
    /// (or intermediate "folders" in S3) as needed. Overwrites any existing blob.
    /// </summary>
    Task PutBlobAsync(string path, Stream content, CancellationToken cancellationToken);

    /// <summary>Returns <c>true</c> if a blob exists at <paramref name="path"/>.</summary>
    Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>Deletes the blob at <paramref name="path"/>. No-ops if the blob does not exist.</summary>
    Task DeleteBlobAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all blob paths that match <paramref name="wildcardPattern"/>.
    /// Supports <c>*</c> (any characters within a path segment) and path-level wildcards.
    /// Returns relative paths from the storage root, using forward slashes.
    /// </summary>
    Task<IReadOnlyList<string>> QueryBlobsAsync(string wildcardPattern, CancellationToken cancellationToken);
}
