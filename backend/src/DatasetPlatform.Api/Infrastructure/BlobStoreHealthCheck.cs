using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// Verifies the configured blob store is reachable by issuing a lightweight existence
/// check against a fixed probe path. The path is not expected to exist — a clean
/// false is as good as true; what we care about is that no exception is thrown.
/// </summary>
public sealed class BlobStoreHealthCheck(IBlobStore blobStore) : IHealthCheck
{
    private const string ProbePath = ".healthcheck";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await blobStore.BlobExistsAsync(ProbePath, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Blob store is unreachable.", ex);
        }
    }
}
