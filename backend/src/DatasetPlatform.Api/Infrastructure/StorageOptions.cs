namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// Strongly-typed configuration for the storage provider.
/// Bound from the <c>"Storage"</c> section of <c>appsettings.json</c>.
///
/// <example>
/// <code>
/// "Storage": {
///   "Provider": "LocalFile",   // or "S3"
///   "BasePath": "../../../data",
///   "S3": { ... }
/// }
/// </code>
/// </example>
/// </summary>
public sealed class StorageOptions
{
    /// <summary>The config section key used to bind this class.</summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Storage backend to use. Valid values: <see cref="StorageProviders.LocalFile"/> (default) or <see cref="StorageProviders.S3"/>.
    /// </summary>
    public string Provider { get; init; } = StorageProviders.LocalFile;

    /// <summary>
    /// Root directory for the LocalFile provider. Relative paths are resolved from the working directory.
    /// Ignored when <see cref="Provider"/> is S3.
    /// </summary>
    public string BasePath { get; init; } = "./data";

    /// <summary>S3-specific configuration. Only used when <see cref="Provider"/> is <see cref="StorageProviders.S3"/>.</summary>
    public S3StorageOptions S3 { get; init; } = new();
}

/// <summary>Named constants for the supported storage providers.</summary>
public static class StorageProviders
{
    /// <summary>Local filesystem storage — suitable for development and single-node deployments.</summary>
    public const string LocalFile = "LocalFile";

    /// <summary>AWS S3 object storage — suitable for cloud deployments and multi-node setups.</summary>
    public const string S3 = "S3";
}

/// <summary>
/// AWS S3-specific configuration. Bound from the <c>"Storage:S3"</c> sub-section.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>Name of the S3 bucket that stores all blobs.</summary>
    public string BucketName { get; init; } = string.Empty;

    /// <summary>AWS region where the bucket is hosted (e.g. <c>"eu-west-1"</c>).</summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>
    /// Key prefix prepended to all blob paths within the bucket
    /// (e.g. <c>"dataset-platform"</c> → blobs stored at <c>dataset-platform/schemas/...</c>).
    /// </summary>
    public string Prefix { get; init; } = "dataset-platform";

    /// <summary>
    /// Optional custom S3-compatible endpoint URL (e.g. for LocalStack or MinIO in development).
    /// When set, <c>ForcePathStyle</c> is automatically enabled.
    /// </summary>
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Forces path-style URLs (<c>https://host/bucket/key</c>) instead of virtual-hosted style.
    /// Required for S3-compatible stores like MinIO. Automatically set when <see cref="ServiceUrl"/> is configured.
    /// </summary>
    public bool ForcePathStyle { get; init; }
}
