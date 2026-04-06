namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Defines the structure, metadata, and access control rules for a dataset.
/// A schema must exist before any instances can be created for that dataset.
///
/// <para>
/// Schemas are persisted as JSON files at <c>schemas/{key}.json</c> and are loaded
/// on startup or on first access. Schema changes take effect immediately.
/// </para>
///
/// <para>
/// Creating or modifying a schema requires the <c>DatasetAdmin</c> role or membership
/// in the schema's own <see cref="DatasetPermissions.DatasetAdminRoles"/> set.
/// </para>
/// </summary>
public sealed class DatasetSchema
{
    /// <summary>
    /// Unique, URL-safe identifier for this dataset (e.g. "market-rates", "fund-nav").
    /// Normalised to lowercase by the service. Used as the key in all API routes.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Human-readable display name shown in the dataset catalogue (e.g. "Market Reference Rates").</summary>
    public required string Name { get; init; }

    /// <summary>Optional description explaining the purpose and contents of the dataset.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Fields that appear in the instance header (summary / identity section).
    /// Header fields are stored in every index file and are used for filtering without
    /// loading the full detail payload. At least one field marked <see cref="SchemaField.IsKey"/>
    /// is recommended to enforce uniqueness.
    /// </summary>
    public IReadOnlyList<SchemaField> HeaderFields { get; init; } = [];

    /// <summary>
    /// Fields that appear in each row of the instance's tabular data section.
    /// Detail data is stored separately from the header index and is only loaded
    /// when full instance data is requested.
    /// </summary>
    public IReadOnlyList<SchemaField> DetailFields { get; init; } = [];

    /// <summary>Role-based access control configuration for this dataset.</summary>
    public DatasetPermissions Permissions { get; init; } = new();

    /// <summary>UTC timestamp of when this schema was first persisted.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of the last schema update. Set automatically by the service on every upsert.</summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
