namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Represents a single versioned snapshot of data for a dataset on a specific date.
/// Each instance holds a header (key-value metadata describing the record) and
/// zero or more detail rows (tabular data).
///
/// <para>
/// Instances are uniquely identified by their <see cref="Id"/> (GUID), but are also
/// logically keyed by (<see cref="DatasetKey"/>, <see cref="AsOfDate"/>, <see cref="State"/>,
/// key-field values in <see cref="Header"/>). Two instances with the same logical key
/// are not permitted in the same state at the same time.
/// </para>
///
/// <para>
/// <see cref="Version"/> increments on each update. The first instance created for a
/// (dataset, asOfDate, state) combination starts at version 1 unless <c>ResetVersion</c>
/// is specified in the create request.
/// </para>
/// </summary>
public sealed class DatasetInstance
{
    /// <summary>Globally unique identifier for this instance. Assigned on creation and never changes.</summary>
    public required Guid Id { get; init; }

    /// <summary>The dataset this instance belongs to. Normalised to lowercase.</summary>
    public required string DatasetKey { get; init; }

    /// <summary>The business date this snapshot is "as of". Used for temporal filtering and latest-instance lookups.</summary>
    public required DateOnly AsOfDate { get; init; }

    /// <summary>Current lifecycle state label. Determines who can read, modify, or promote this instance.</summary>
    public required string State { get; init; }

    /// <summary>
    /// Monotonically increasing edit counter. Starts at 1 on creation, increments by 1 on each update.
    /// Useful for detecting whether an instance has changed since last fetched.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Key-value pairs that describe the header/summary of this instance (e.g. region, fund, currency).
    /// Fields are defined by <see cref="DatasetSchema.HeaderFields"/>. Dictionary is case-insensitive.
    /// </summary>
    public IDictionary<string, object?> Header { get; init; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tabular detail rows for this instance. Each row is a key-value dictionary matching
    /// <see cref="DatasetSchema.DetailFields"/>. May be empty for header-only datasets.
    /// </summary>
    public IReadOnlyList<IDictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>User ID of the person who first created this instance. May be null for records migrated from legacy systems.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>UTC timestamp of when this instance was first created. May be null for legacy records.</summary>
    public DateTimeOffset? CreatedAtUtc { get; init; }

    /// <summary>User ID of the person who last modified (or signed off) this instance.</summary>
    public required string LastModifiedBy { get; init; }

    /// <summary>UTC timestamp of the most recent modification or signoff.</summary>
    public required DateTimeOffset LastModifiedAtUtc { get; init; }
}
