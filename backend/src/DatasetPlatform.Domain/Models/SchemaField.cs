namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Describes a single field within a dataset schema (either a header field or a detail/row field).
/// Each field defines its name, display label, data type, and optional validation constraints.
/// </summary>
public sealed class SchemaField
{
    /// <summary>
    /// Machine-readable identifier for this field. Used as the key in instance Header/Row dictionaries.
    /// Must be unique within its section (header or detail). Case-insensitive when matching data.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Human-readable display label shown in the UI for this field.</summary>
    public required string Label { get; init; }

    /// <summary>The data type of this field. Controls validation and UI rendering.</summary>
    public required FieldType Type { get; init; }

    /// <summary>
    /// When <c>true</c>, this field is part of the natural key for uniqueness checks.
    /// Two instances with the same (datasetKey, asOfDate, state) and matching key field values
    /// are considered duplicates and will be rejected.
    /// </summary>
    public bool IsKey { get; init; }

    /// <summary>When <c>true</c>, a non-null, non-empty value must be supplied for this field.</summary>
    public bool Required { get; init; }

    /// <summary>
    /// Maximum number of characters allowed. Only applies when <see cref="Type"/> is <see cref="FieldType.String"/>.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Minimum numeric value allowed (inclusive). Only applies when <see cref="Type"/> is <see cref="FieldType.Number"/>.
    /// </summary>
    public decimal? MinValue { get; init; }

    /// <summary>
    /// Maximum numeric value allowed (inclusive). Only applies when <see cref="Type"/> is <see cref="FieldType.Number"/>.
    /// </summary>
    public decimal? MaxValue { get; init; }

    /// <summary>
    /// Allowed values for <see cref="FieldType.Select"/> fields.
    /// The submitted value must exactly match one of these entries (case-insensitive).
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>
    /// Key of another dataset whose values supply the lookup list.
    /// Only used when <see cref="Type"/> is <see cref="FieldType.Lookup"/>.
    /// Permissible values are retrieved via GET /api/lookups/{lookupDatasetKey}/values.
    /// </summary>
    public string? LookupDatasetKey { get; init; }
}
