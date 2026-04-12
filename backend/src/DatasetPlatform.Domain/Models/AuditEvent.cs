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

    /// <summary>
    /// Instance as-of date at the moment the audit event was recorded.
    /// Populated for instance-level actions.
    /// </summary>
    public string? AsOfDate { get; init; }

    /// <summary>
    /// Instance state at the moment the audit event was recorded.
    /// Populated for instance-level actions.
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    /// Snapshot of header field values for the affected instance at the time of the audit event.
    /// Populated for instance-level create/update/delete/signoff actions when header data is available.
    /// </summary>
    public IReadOnlyDictionary<string, string> InstanceHeader { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Structured row-level change records for instance create/update actions.
    /// <para>
    /// Each entry includes key fields and optional source/target value maps.
    /// Added rows include key fields + target values only.
    /// Removed rows include key fields + source values only.
    /// Updated rows include key fields + both source and target values.
    /// </para>
    /// </summary>
    public IReadOnlyList<AuditRowChange> RowChanges { get; init; } = new List<AuditRowChange>();
}

public sealed class AuditRowChange
{
    public string Operation { get; init; } = "updated";
    public IDictionary<string, string> KeyFields { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, string>? SourceValues { get; init; }
    public IDictionary<string, string>? TargetValues { get; init; }
}
