namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Immutable record of a single operation performed on a dataset or its schema.
/// Audit events are appended-only and are never modified after creation.
///
/// <para>
/// Events are stored per-dataset under <c>audit/{datasetKey}/{timestamp}_{action}_{id}.json</c>.
/// Common action values: <c>SCHEMA_UPSERT</c>, <c>SCHEMA_DELETE</c>,
/// <c>INSTANCE_CREATE</c>, <c>INSTANCE_UPDATE</c>, <c>INSTANCE_DELETE</c>, <c>INSTANCE_SIGNOFF</c>.
/// </para>
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Unique identifier for this audit event.</summary>
    public required Guid Id { get; init; }

    /// <summary>UTC timestamp of when the operation occurred.</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>ID of the user who performed the operation (from the <c>x-user-id</c> request header).</summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Short uppercase token describing the type of operation.
    /// Examples: <c>INSTANCE_CREATE</c>, <c>INSTANCE_SIGNOFF</c>, <c>SCHEMA_DELETE</c>.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>Key of the dataset that was affected.</summary>
    public required string DatasetKey { get; init; }

    /// <summary>ID of the specific instance affected, if applicable. Null for schema-level operations.</summary>
    public Guid? DatasetInstanceId { get; init; }

    /// <summary>Human-readable description of what changed (e.g. field-level diff for updates).</summary>
    public string Details { get; init; } = string.Empty;
}
