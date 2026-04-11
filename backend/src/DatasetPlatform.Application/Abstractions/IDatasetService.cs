using DatasetPlatform.Application.Dtos;
using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Application.Abstractions;

/// <summary>
/// Core business logic contract for the Dataset Platform.
/// Controllers depend on this interface; <c>DatasetService</c> is the production implementation.
///
/// <para>
/// Every method that accesses data takes a <see cref="UserContext"/> so the service can
/// enforce authorization before touching the repository. Authorization failures throw
/// <c>DatasetServiceException</c>, which controllers map to HTTP 400 responses.
/// </para>
/// </summary>
public interface IDatasetService
{
    // ─── Schema management ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns all schemas the <paramref name="user"/> is permitted to read.
    /// DatasetAdmin users see all schemas; others see only schemas where they have at least ReadRoles membership.
    /// </summary>
    Task<IReadOnlyList<DatasetSchema>> GetAccessibleSchemasAsync(UserContext user, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a schema. The key is normalised to lowercase.
    /// Requires DatasetAdmin role for new schemas, or schema-level DatasetAdminRoles for updates.
    /// </summary>
    Task<DatasetSchema> UpsertSchemaAsync(DatasetSchema schema, UserContext user, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the schema AND all instances for the dataset. Irreversible.
    /// Requires schema-level DatasetAdmin access.
    /// </summary>
    Task DeleteSchemaAsync(string datasetKey, UserContext user, CancellationToken cancellationToken);

    // ─── Instance queries ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all instances for a dataset, sorted by asOfDate and version descending.
    /// Supports optional filtering by date range, state, and header field values.
    /// </summary>
    Task<IReadOnlyList<DatasetInstance>> GetInstancesAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null);

    /// <summary>
    /// Same as <see cref="GetInstancesAsync"/> but wraps the result with storage diagnostic info
    /// (files read, search efficiency). Called when the client passes <c>includeInternalInfo=true</c>.
    /// </summary>
    Task<DatasetInstancesQueryResponse> GetInstancesWithInternalInfoAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null);

    /// <summary>
    /// Like <see cref="GetInstancesAsync"/> but returns lightweight header summaries only
    /// (no detail rows). Faster for list/search views where row data is not needed.
    /// </summary>
    Task<IReadOnlyList<DatasetHeaderSummary>> GetHeadersAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null);

    /// <summary>Same as <see cref="GetHeadersAsync"/> but includes storage diagnostic info.</summary>
    Task<DatasetHeadersQueryResponse> GetHeadersWithInternalInfoAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null);

    /// <summary>Returns a single instance by GUID, or <c>null</c> if not found.</summary>
    Task<DatasetInstance?> GetInstanceAsync(string datasetKey, Guid instanceId, UserContext user, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recent instance whose <c>AsOfDate</c> is on or before <paramref name="asOfDate"/>,
    /// for the given state and optional header criteria. Returns <c>null</c> if no match is found.
    /// Throws if the criteria match more than one instance on the same date.
    /// </summary>
    Task<DatasetInstance?> GetLatestInstanceAsync(string datasetKey, DateOnly asOfDate, string state, IReadOnlyDictionary<string, string>? headerCriteria, UserContext user, CancellationToken cancellationToken);

    /// <summary>Same as <see cref="GetLatestInstanceAsync"/> but includes storage diagnostic info.</summary>
    Task<DatasetLatestInstanceQueryResponse> GetLatestInstanceWithInternalInfoAsync(string datasetKey, DateOnly asOfDate, string state, IReadOnlyDictionary<string, string>? headerCriteria, UserContext user, CancellationToken cancellationToken);

    // ─── Instance mutations ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new instance. State must be Draft or PendingApproval (Official requires signoff).
    /// Validates the data against the schema and enforces header uniqueness.
    /// </summary>
    Task<DatasetInstance> CreateInstanceAsync(CreateDatasetInstanceRequest request, UserContext user, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing instance's data. Increments the version number.
    /// Validates new data against schema and re-checks uniqueness if identity fields changed.
    /// </summary>
    Task<DatasetInstance> UpdateInstanceAsync(UpdateDatasetInstanceRequest request, UserContext user, CancellationToken cancellationToken);

    /// <summary>Deletes the instance. Throws if not found.</summary>
    Task DeleteInstanceAsync(string datasetKey, Guid instanceId, UserContext user, CancellationToken cancellationToken);

    /// <summary>
    /// Promotes an existing instance to <see cref="DatasetState.Official"/> in place.
    /// Requires the Signoff permission. Validates that no other Official instance already
    /// exists for the same (asOfDate, header key fields).
    /// </summary>
    Task<DatasetInstance> SignoffInstanceAsync(SignoffDatasetRequest request, UserContext user, CancellationToken cancellationToken);

    // ─── Audit & lookups ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns audit events for datasets the user can read.
    /// DatasetAdmin users can request all events or filter by dataset key.
    /// Non-admin users must specify a dataset key and must have read access to that dataset.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetAuditAsync(UserContext user, CancellationToken cancellationToken, string? datasetKey = null, Guid? instanceId = null);

    /// <summary>
    /// Returns the set of permissible values for a Lookup field that references <paramref name="lookupDatasetKey"/>.
    /// Used by the UI to populate dropdown options at runtime.
    /// </summary>
    Task<IReadOnlyList<string>> GetLookupPermissibleValuesAsync(string lookupDatasetKey, CancellationToken cancellationToken);
}
