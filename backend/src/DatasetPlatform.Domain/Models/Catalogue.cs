namespace DatasetPlatform.Domain.Models;

/// <summary>
/// A named grouping of dataset schemas that allows users to browse related datasets together.
/// Catalogues are managed by users with the <c>CatalogueAdmin</c> role.
/// </summary>
public sealed class Catalogue
{
    /// <summary>
    /// Unique, URL-safe identifier for this catalogue (e.g. "rates", "reference-data").
    /// Normalised to lowercase by the service.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Human-readable display name (e.g. "FX Rates", "Reference Data").</summary>
    public required string Name { get; init; }

    /// <summary>Optional description of what datasets belong in this catalogue.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>UTC timestamp of when this catalogue was first created.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional header field template. When non-empty, every dataset schema assigned to this
    /// catalogue uses exactly these header fields. The schema stores no header fields of its own
    /// — the service injects the template at read time, so updating the template here propagates
    /// instantly to all datasets without requiring any schema re-saves.
    /// </summary>
    public IReadOnlyList<SchemaField> HeaderFields { get; init; } = [];
}
