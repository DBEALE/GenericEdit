using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Application.Dtos;

/// <summary>
/// Request body for POST /api/datasets/{datasetKey}/instances.
/// Creates a new dataset instance in Draft or PendingApproval state.
/// </summary>
public sealed class CreateDatasetInstanceRequest
{
    /// <summary>Must match the dataset key in the URL route.</summary>
    public required string DatasetKey { get; init; }

    /// <summary>The business date this snapshot applies to.</summary>
    public required DateOnly AsOfDate { get; init; }

    /// <summary>Initial lifecycle state label. Official state still requires signoff.</summary>
    public required string State { get; init; }

    /// <summary>
    /// When <c>true</c>, forces the new instance's version to 1, ignoring any existing
    /// versions for the same (dataset, asOfDate, state). Useful when starting a fresh cycle.
    /// </summary>
    public bool ResetVersion { get; init; }

    /// <summary>Header key-value pairs. Keys must match the schema's HeaderFields names.</summary>
    public IDictionary<string, object?> Header { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Detail rows. Each dictionary's keys must match the schema's DetailFields names.</summary>
    public IReadOnlyList<IDictionary<string, object?>> Rows { get; init; } = [];
}

/// <summary>
/// Request body for PUT /api/datasets/{datasetKey}/instances/{instanceId}.
/// Replaces the data in an existing Draft or PendingApproval instance.
/// </summary>
public sealed class UpdateDatasetInstanceRequest
{
    /// <summary>Must match the dataset key in the URL route.</summary>
    public required string DatasetKey { get; init; }

    /// <summary>Must match the instance ID in the URL route.</summary>
    public required Guid InstanceId { get; init; }

    /// <summary>New business date for the snapshot. Changing this may trigger a uniqueness re-check.</summary>
    public required DateOnly AsOfDate { get; init; }

    /// <summary>New state label. Use the signoff endpoint to transition to Official.</summary>
    public required string State { get; init; }

    /// <summary>
    /// The <see cref="DatasetInstance.Version"/> value the caller last observed for this instance.
    /// When provided, the update will fail with HTTP 409 if the stored version has changed
    /// since the caller fetched it, preventing silent overwrites from concurrent edits.
    /// Omit (leave <c>null</c>) to skip the check and always overwrite (old behaviour).
    /// </summary>
    public int? ExpectedVersion { get; init; }

    /// <summary>Replacement header values. Changing key-field values triggers a uniqueness re-check.</summary>
    public IDictionary<string, object?> Header { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Replacement detail rows.</summary>
    public IReadOnlyList<IDictionary<string, object?>> Rows { get; init; } = [];
}

/// <summary>
/// Request for POST /api/datasets/{datasetKey}/instances/{instanceId}/signoff.
/// Promotes an existing instance to <see cref="DatasetState.Official"/> in place.
/// </summary>
public sealed class SignoffDatasetRequest
{
    /// <summary>Dataset the target instance belongs to.</summary>
    public required string DatasetKey { get; init; }

    /// <summary>ID of the instance to promote to Official state.</summary>
    public required Guid InstanceId { get; init; }

    /// <summary>
    /// The <see cref="DatasetInstance.Version"/> value the caller last observed.
    /// When provided, the signoff will fail with HTTP 409 if the instance has been
    /// modified since the caller fetched it.
    /// Omit to skip the check (old behaviour).
    /// </summary>
    public int? ExpectedVersion { get; init; }
}
