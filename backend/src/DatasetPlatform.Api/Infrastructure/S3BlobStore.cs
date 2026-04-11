using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace DatasetPlatform.Api.Infrastructure;

public sealed class S3BlobStore(IAmazonS3 s3Client, IOptions<StorageOptions> options) : IBlobStore
{
    private readonly IAmazonS3 _s3Client = s3Client;
    private readonly S3StorageOptions _s3 = options.Value.S3;

    public async Task<Stream?> GetBlobAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(_s3.BucketName, path, cancellationToken);
            var stream = new MemoryStream();
            using (response)
            {
                await response.ResponseStream.CopyToAsync(stream, cancellationToken);
            }

            stream.Position = 0;
            return stream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task PutBlobAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _s3.BucketName,
            Key = path,
            InputStream = content,
            AutoCloseStream = false
        }, cancellationToken);
    }

    public async Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(_s3.BucketName, path, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteBlobAsync(string path, CancellationToken cancellationToken)
    {
        await _s3Client.DeleteObjectAsync(_s3.BucketName, path, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> QueryBlobsAsync(string wildcardPattern, CancellationToken cancellationToken)
    {
        var entries = await QueryBlobsWithMetadataAsync(wildcardPattern, cancellationToken);
        return entries.Select(e => e.Key).ToList();
    }

    public async Task<IReadOnlyList<BlobEntry>> QueryBlobsWithMetadataAsync(string wildcardPattern, CancellationToken cancellationToken)
    {
        var wildcardIndex = wildcardPattern.IndexOfAny(['*', '?']);
        var prefix = wildcardIndex >= 0 ? wildcardPattern[..wildcardIndex] : wildcardPattern;
        var regex = WildcardToRegex(wildcardPattern);

        var entries = new List<BlobEntry>();
        string? continuation = null;

        do
        {
            var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _s3.BucketName,
                Prefix = prefix,
                ContinuationToken = continuation
            }, cancellationToken);

            entries.AddRange(response.S3Objects
                .Where(x => regex.IsMatch(x.Key))
                .Select(x => new BlobEntry(x.Key, x.LastModified.HasValue ? new DateTimeOffset(x.LastModified.Value, TimeSpan.Zero) : DateTimeOffset.MinValue)));
            continuation = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (!string.IsNullOrEmpty(continuation));

        return entries;
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return new Regex($"^{escaped}$", RegexOptions.CultureInvariant);
    }
}
