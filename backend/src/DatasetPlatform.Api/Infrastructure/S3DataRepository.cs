using Microsoft.Extensions.Options;

namespace DatasetPlatform.Api.Infrastructure;

// Compatibility alias so existing references to S3DataRepository remain valid.
public sealed class S3DataRepository(IBlobStore blobStore, IOptions<StorageOptions> options)
    : BlobDataRepository(blobStore, options)
{
}