using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Application.Abstractions;

/// <summary>
/// Storage abstraction used by <c>DatasetService</c> to persist and retrieve schemas, instances, and audit events.
/// Implementations include <c>BlobDataRepository</c> (local filesystem or S3).
/// Swap implementations in <c>Program.cs</c> without touching business logic.
///
/// <para>
/// All methods accept a <see cref="CancellationToken"/> for cooperative cancellation.
/// Implementations are expected to be thread-safe; the current blob-based implementation
/// uses an internal semaphore to serialise writes.
/// </para>
/// </summary>
public interface IDataRepository
{
    // ─── Schema operations ───────────────────────────────────────────────────────

    /// <summary>Returns all persisted schemas, ordered by key ascending.</summary>
    Task<IReadOnlyList<DatasetSchema>> GetSchemasAsync(CancellationToken cancellationToken);

    /// <summary>Returns the schema for <paramref name="datasetKey"/>, or <c>null</c> if not found.</summary>
    Task<DatasetSchema?> GetSchemaAsync(string datasetKey, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the schema for the given dataset key.</summary>
    Task UpsertSchemaAsync(DatasetSchema schema, CancellationToken cancellationToken);

    /// <summary>Deletes the schema and all associated instance files for <paramref name="datasetKey"/>.</summary>
    Task DeleteSchemaAsync(string datasetKey, CancellationToken cancellationToken);

    // ─── Instance operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns all instances for a dataset, optionally filtered by date range, state, and header criteria.
    /// When <paramref name="includeDetails"/> is <c>false</c>, row data is omitted for better performance.
    /// </summary>
    Task<IReadOnlyList<DatasetInstance>> GetInstancesAsync(
        string datasetKey,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null,
        bool includeDetails = true);

    /// <summary>
    /// Same as <see cref="GetInstancesAsync"/> but also returns diagnostic trace information
    /// (files read, search efficiency stats). Used when the caller requests <c>includeInternalInfo=true</c>.
    /// </summary>
    Task<DatasetReadTrace> GetInstancesWithTraceAsync(
        string datasetKey,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null,
        bool includeDetails = true);

    /// <summary>Returns a single instance by its GUID, or <c>null</c> if not found.</summary>
    Task<DatasetInstance?> GetInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recent instance for <paramref name="datasetKey"/> in the given <paramref name="state"/>
    /// (highest <see cref="DatasetInstance.AsOfDate"/>, then highest <see cref="DatasetInstance.Version"/>),
    /// or <c>null</c> if none exists. Always includes full detail rows.
    /// </summary>
    Task<DatasetInstance?> GetLatestInstanceAsync(string datasetKey, string state, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the ID and version of the most recent instance for <paramref name="datasetKey"/> in the given <paramref name="state"/>,
    /// or <c>null</c> if none exists. Reads only the winning header file — suitable for cache-staleness checks.
    /// </summary>
    Task<(Guid Id, int Version)?> GetLatestInstanceVersionAsync(string datasetKey, string state, CancellationToken cancellationToken);

    /// <summary>Unconditionally writes (creates or overwrites) the given instance.</summary>
    Task SaveInstanceAsync(DatasetInstance instance, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing instance. Returns <c>false</c> (and makes no changes) if
    /// no instance with the same ID already exists — preventing unintentional creates.
    /// When <paramref name="existing"/> is supplied the implementation may skip the existence
    /// check and use the known previous state/date to compute the old header path directly,
    /// avoiding redundant blob queries.
    /// </summary>
    Task<bool> ReplaceInstanceAsync(DatasetInstance instance, CancellationToken cancellationToken, DatasetInstance? existing = null);

    /// <summary>
    /// Deletes the instance with the given ID. Returns <c>false</c> if the instance was not found.
    /// Also removes associated header index entries.
    /// </summary>
    Task<bool> DeleteInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken);

    // ─── Catalogue operations ────────────────────────────────────────────────────

    /// <summary>Returns all catalogues, ordered by key ascending.</summary>
    Task<IReadOnlyList<Catalogue>> GetCataloguesAsync(CancellationToken cancellationToken);

    /// <summary>Returns the catalogue for <paramref name="catalogueKey"/>, or <c>null</c> if not found.</summary>
    Task<Catalogue?> GetCatalogueAsync(string catalogueKey, CancellationToken cancellationToken);

    /// <summary>Creates or replaces a catalogue.</summary>
    Task UpsertCatalogueAsync(Catalogue catalogue, CancellationToken cancellationToken);

    /// <summary>Deletes a catalogue. Schemas that reference it retain their key; they become uncatalogued.</summary>
    Task DeleteCatalogueAsync(string catalogueKey, CancellationToken cancellationToken);

    // ─── Audit operations ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all audit events, optionally filtered to a specific dataset key.
    /// Results are ordered by <see cref="AuditEvent.OccurredAtUtc"/> descending.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(
        string? datasetKey,
        CancellationToken cancellationToken,
        DateOnly? minOccurredDate = null,
        DateOnly? maxOccurredDate = null);

    /// <summary>
    /// Returns audit events for a specific instance, newest first, stopping after the most recent signoff.
    /// Only reads files in the instance's own audit subfolder — far cheaper than loading the full dataset audit.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetInstanceAuditHistoryAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken);

    /// <summary>Appends a single audit event to the store. Events are immutable once written.</summary>
    Task AddAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Result returned by <see cref="IDataRepository.GetInstancesWithTraceAsync"/> containing
/// both the matched instances and diagnostic counters for analysing search performance.
/// </summary>
public sealed record DatasetReadTrace
{
    /// <summary>The instances that matched all applied filters.</summary>
    public IReadOnlyList<DatasetInstance> Instances { get; init; } = [];

    /// <summary>Each file that was opened during the query, with a human-readable reason.</summary>
    public IReadOnlyList<DatasetLoadedFileTrace> LoadedFilenames { get; init; } = [];

    /// <summary>Number of header index files that were actually read.</summary>
    public int HeaderFilesRead { get; init; }

    /// <summary>Total number of header index files available for this dataset.</summary>
    public int HeaderFilesTotal { get; init; }

    /// <summary>Number of full detail files that were read to satisfy the query.</summary>
    public int DetailFilesRead { get; init; }

    /// <summary>Total number of detail files available for this dataset.</summary>
    public int DetailFilesTotal { get; init; }

    /// <summary>Number of header partitions considered before applying date/state filters.</summary>
    public int CandidateHeaderFilesConsidered { get; init; }

    /// <summary>Number of instances that survived all filter stages and are included in <see cref="Instances"/>.</summary>
    public int MatchedInstanceFileCount { get; init; }

    /// <summary>Number of stale header index files that were rebuilt during this query.</summary>
    public int HeaderFilesRebuilt { get; init; }

    /// <summary><c>true</c> if date/state partitioning was used to skip files; <c>false</c> for a full scan.</summary>
    public bool UsedFilteredSearchPath { get; init; }
}

/// <summary>Describes a single file opened during a traced dataset query.</summary>
public sealed record DatasetLoadedFileTrace
{
    /// <summary>Relative path of the file within the storage root.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Human-readable explanation of why this file was loaded (e.g. "Matched header filter").</summary>
    public string Reason { get; init; } = string.Empty;
}
