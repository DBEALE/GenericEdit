using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Dtos;
using DatasetPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Globalization;

namespace DatasetPlatform.Application.Services;

public sealed class DatasetService(IDataRepository repository, ILogger<DatasetService>? logger = null) : IDatasetService
{
    private readonly ILogger<DatasetService> logger = logger ?? NullLogger<DatasetService>.Instance;
    private const string OfficialState = "Official";
    private const string InstanceCreateAction = "INSTANCE_CREATE";
    private const string InstanceUpdateAction = "INSTANCE_UPDATE";
    private const string InstanceSignoffAction = "INSTANCE_SIGNOFF";

    public async Task<IReadOnlyList<DatasetSchema>> GetAccessibleSchemasAsync(UserContext user, CancellationToken cancellationToken)
    {
        var schemas = await repository.GetSchemasAsync(cancellationToken);
        return schemas.Where(schema => DatasetAuthorizer.CanRead(schema, user)).ToList();
    }

    public async Task<DatasetSchema> UpsertSchemaAsync(DatasetSchema schema, UserContext user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schema.Key))
        {
            throw new DatasetServiceException("Schema key is required.");
        }

        var normalizedKey = NormalizeDatasetKey(schema.Key);
        var existingSchema = await repository.GetSchemaAsync(normalizedKey, cancellationToken);

        if (existingSchema is null)
        {
            if (!user.HasRole(DatasetAuthorizer.DatasetAdminRole))
            {
                throw new DatasetServiceException("User is not authorized to create dataset schemas.");
            }
        }
        else if (!DatasetAuthorizer.CanMaintainSchema(existingSchema, user))
        {
            throw new DatasetServiceException("User is not authorized to maintain this dataset schema.");
        }

        EnsureNoDuplicateFieldNames(schema.HeaderFields, "header");
        EnsureNoDuplicateFieldNames(schema.DetailFields, "detail");
        EnsureKeyFieldsAreValid(schema);
        EnsureFieldConfigurationsAreValid(schema);

        var normalizedSchema = new DatasetSchema
        {
            Key = normalizedKey,
            Name = schema.Name,
            Description = schema.Description,
            HeaderFields = schema.HeaderFields,
            DetailFields = schema.DetailFields,
            Permissions = schema.Permissions,
            CreatedAtUtc = schema.CreatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await repository.UpsertSchemaAsync(normalizedSchema, cancellationToken);
        await AddAuditAsync(user, "SCHEMA_UPSERT", normalizedSchema.Key, null, cancellationToken);
        return normalizedSchema;
    }

    public async Task DeleteSchemaAsync(string datasetKey, UserContext user, CancellationToken cancellationToken)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanMaintainSchema(schema, user), "User is not authorized to maintain this dataset schema.");

        await repository.DeleteSchemaAsync(key, cancellationToken);
        await AddAuditAsync(user, "SCHEMA_DELETE", key, null, cancellationToken);
    }

    public async Task<IReadOnlyList<DatasetInstance>> GetInstancesAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        ValidateAsOfDateRange(minAsOfDate, maxAsOfDate);

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        var instances = await repository.GetInstancesAsync(
            key,
            cancellationToken,
            minAsOfDate,
            maxAsOfDate,
            normalizedState,
            normalizedHeaderCriteria,
            includeDetails: true);

        var filtered = ApplyInstanceFilters(instances, minAsOfDate, maxAsOfDate, normalizedState, normalizedHeaderCriteria);

        var filteredList = filtered
            .OrderByDescending(x => x.AsOfDate)
            .ThenByDescending(x => x.Version)
            .ToList();

        return filteredList;
    }

    public async Task<DatasetInstancesQueryResponse> GetInstancesWithInternalInfoAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        ValidateAsOfDateRange(minAsOfDate, maxAsOfDate);

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        var traced = await repository.GetInstancesWithTraceAsync(
            key,
            cancellationToken,
            minAsOfDate,
            maxAsOfDate,
            normalizedState,
            normalizedHeaderCriteria,
            includeDetails: true);

        var filtered = ApplyInstanceFilters(traced.Instances, minAsOfDate, maxAsOfDate, normalizedState, normalizedHeaderCriteria);

        var filteredList = filtered
            .OrderByDescending(x => x.AsOfDate)
            .ThenByDescending(x => x.Version)
            .ToList();

        return new DatasetInstancesQueryResponse
        {
            Items = filteredList,
            InternalInfo = new DatasetInternalInfo
            {
                LoadedFilenames = traced.LoadedFilenames
                    .Select(x => new DatasetLoadedFileInfo
                    {
                        FileName = x.FileName,
                        Reason = x.Reason
                    })
                    .ToList(),
                SearchEfficiency = new DatasetSearchEfficiencyStats
                {
                    HeaderFilesRead = traced.HeaderFilesRead,
                    HeaderFilesTotal = traced.HeaderFilesTotal,
                    DetailFilesRead = traced.DetailFilesRead,
                    DetailFilesTotal = traced.DetailFilesTotal,
                    CandidateHeaderFilesConsidered = traced.CandidateHeaderFilesConsidered,
                    MatchedInstanceFileCount = traced.MatchedInstanceFileCount,
                    HeaderFilesRebuilt = traced.HeaderFilesRebuilt,
                    UsedFilteredSearchPath = traced.UsedFilteredSearchPath
                }
            }
        };
    }

    public async Task<IReadOnlyList<DatasetHeaderSummary>> GetHeadersAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        ValidateAsOfDateRange(minAsOfDate, maxAsOfDate);

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        var instances = await repository.GetInstancesAsync(
            key,
            cancellationToken,
            minAsOfDate,
            maxAsOfDate,
            normalizedState,
            normalizedHeaderCriteria,
            includeDetails: false);

        var filtered = ApplyInstanceFilters(instances, minAsOfDate, maxAsOfDate, normalizedState, normalizedHeaderCriteria);

        return filtered
            .OrderByDescending(x => x.AsOfDate)
            .ThenByDescending(x => x.Version)
            .Select(ToHeaderSummary)
            .ToList();
    }

    public async Task<DatasetHeadersQueryResponse> GetHeadersWithInternalInfoAsync(
        string datasetKey,
        UserContext user,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        ValidateAsOfDateRange(minAsOfDate, maxAsOfDate);

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        var traced = await repository.GetInstancesWithTraceAsync(
            key,
            cancellationToken,
            minAsOfDate,
            maxAsOfDate,
            normalizedState,
            normalizedHeaderCriteria,
            includeDetails: false);

        var filtered = ApplyInstanceFilters(traced.Instances, minAsOfDate, maxAsOfDate, normalizedState, normalizedHeaderCriteria)
            .OrderByDescending(x => x.AsOfDate)
            .ThenByDescending(x => x.Version)
            .ToList();

        return new DatasetHeadersQueryResponse
        {
            Items = filtered
                .Select(ToHeaderSummary)
                .ToList(),
            InternalInfo = new DatasetInternalInfo
            {
                LoadedFilenames = traced.LoadedFilenames
                    .Select(x => new DatasetLoadedFileInfo
                    {
                        FileName = x.FileName,
                        Reason = x.Reason
                    })
                    .ToList(),
                SearchEfficiency = new DatasetSearchEfficiencyStats
                {
                    HeaderFilesRead = traced.HeaderFilesRead,
                    HeaderFilesTotal = traced.HeaderFilesTotal,
                    DetailFilesRead = traced.DetailFilesRead,
                    DetailFilesTotal = traced.DetailFilesTotal,
                    CandidateHeaderFilesConsidered = traced.CandidateHeaderFilesConsidered,
                    MatchedInstanceFileCount = traced.MatchedInstanceFileCount,
                    HeaderFilesRebuilt = traced.HeaderFilesRebuilt,
                    UsedFilteredSearchPath = traced.UsedFilteredSearchPath
                }
            }
        };
    }

    public async Task<DatasetInstance?> GetInstanceAsync(string datasetKey, Guid instanceId, UserContext user, CancellationToken cancellationToken)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        return await repository.GetInstanceAsync(key, instanceId, cancellationToken);
    }

    private static IEnumerable<DatasetInstance> ApplyInstanceFilters(
        IReadOnlyList<DatasetInstance> instances,
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? state,
        IReadOnlyDictionary<string, string>? headerCriteria)
    {

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        return instances.Where(instance =>
            (!minAsOfDate.HasValue || instance.AsOfDate >= minAsOfDate.Value) &&
            (!maxAsOfDate.HasValue || instance.AsOfDate <= maxAsOfDate.Value) &&
            (normalizedState is null || StateMatches(instance.State, normalizedState)) &&
            HeaderMatchesCriteria(instance.Header, normalizedHeaderCriteria));
    }

    private static DatasetHeaderSummary ToHeaderSummary(DatasetInstance instance)
    {
        return new DatasetHeaderSummary
        {
            Id = instance.Id,
            DatasetKey = instance.DatasetKey,
            AsOfDate = instance.AsOfDate,
            State = instance.State,
            Version = instance.Version,
            Header = CloneMap(instance.Header),
            CreatedBy = instance.CreatedBy,
            CreatedAtUtc = instance.CreatedAtUtc,
            LastModifiedBy = instance.LastModifiedBy,
            LastModifiedAtUtc = instance.LastModifiedAtUtc
        };
    }

    private static void ValidateAsOfDateRange(DateOnly? minAsOfDate, DateOnly? maxAsOfDate)
    {
        if (minAsOfDate.HasValue && maxAsOfDate.HasValue && minAsOfDate.Value > maxAsOfDate.Value)
        {
            throw new DatasetServiceException("minAsOfDate cannot be greater than maxAsOfDate.");
        }
    }

    public async Task<DatasetInstance?> GetLatestInstanceAsync(
        string datasetKey,
        DateOnly asOfDate,
        string state,
        IReadOnlyDictionary<string, string>? headerCriteria,
        UserContext user,
        CancellationToken cancellationToken)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        var traced = await repository.GetInstancesWithTraceAsync(
            key,
            cancellationToken,
            null,
            asOfDate,
            normalizedState,
            normalizedHeaderCriteria,
            includeDetails: true);

        var upToDateMatches = ApplyInstanceFilters(traced.Instances, null, asOfDate, normalizedState, normalizedHeaderCriteria)
            .Where(x => x.AsOfDate <= asOfDate)
            .ToList();

        if (upToDateMatches.Count == 0)
        {
            return null;
        }

        var latestDate = upToDateMatches.Max(x => x.AsOfDate);
        var latestDateMatches = upToDateMatches
            .Where(x => x.AsOfDate == latestDate)
            .ToList();

        if (latestDateMatches.Count > 1)
        {
            throw new DatasetServiceException(
                $"Latest instance criteria matched {latestDateMatches.Count} instances on {latestDate:yyyy-MM-dd}. Provide more specific header criteria.");
        }

        return latestDateMatches[0];
    }

    public async Task<DatasetLatestInstanceQueryResponse> GetLatestInstanceWithInternalInfoAsync(
        string datasetKey,
        DateOnly asOfDate,
        string state,
        IReadOnlyDictionary<string, string>? headerCriteria,
        UserContext user,
        CancellationToken cancellationToken)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to read this dataset.");

        var normalizedState = NormalizeStateFilter(state);
        var normalizedHeaderCriteria = NormalizeHeaderCriteria(headerCriteria);

        var traced = await repository.GetInstancesWithTraceAsync(
            key,
            cancellationToken,
            null,
            asOfDate,
            normalizedState,
            normalizedHeaderCriteria,
            includeDetails: true);

        var upToDateMatches = ApplyInstanceFilters(traced.Instances, null, asOfDate, normalizedState, normalizedHeaderCriteria)
            .Where(x => x.AsOfDate <= asOfDate)
            .ToList();

        DatasetInstance? latestItem = null;
        if (upToDateMatches.Count > 0)
        {
            var latestDate = upToDateMatches.Max(x => x.AsOfDate);
            var latestDateMatches = upToDateMatches
                .Where(x => x.AsOfDate == latestDate)
                .ToList();

            if (latestDateMatches.Count > 1)
            {
                throw new DatasetServiceException(
                    $"Latest instance criteria matched {latestDateMatches.Count} instances on {latestDate:yyyy-MM-dd}. Provide more specific header criteria.");
            }

            latestItem = latestDateMatches[0];
        }

        var internalInfo = new DatasetInternalInfo
        {
            LoadedFilenames = traced.LoadedFilenames
                .Select(x => new DatasetLoadedFileInfo
                {
                    FileName = x.FileName,
                    Reason = x.Reason
                })
                .ToList(),
            SearchEfficiency = new DatasetSearchEfficiencyStats
            {
                HeaderFilesRead = traced.HeaderFilesRead,
                HeaderFilesTotal = traced.HeaderFilesTotal,
                DetailFilesRead = traced.DetailFilesRead,
                DetailFilesTotal = traced.DetailFilesTotal,
                CandidateHeaderFilesConsidered = traced.CandidateHeaderFilesConsidered,
                MatchedInstanceFileCount = latestItem is null ? 0 : 1,
                HeaderFilesRebuilt = traced.HeaderFilesRebuilt,
                UsedFilteredSearchPath = traced.UsedFilteredSearchPath
            }
        };

        return new DatasetLatestInstanceQueryResponse
        {
            Item = latestItem,
            InternalInfo = internalInfo
        };
    }

    public async Task<DatasetInstance> CreateInstanceAsync(CreateDatasetInstanceRequest request, UserContext user, CancellationToken cancellationToken)
    {
        string? key = null;
        try
        {
            key = NormalizeDatasetKey(request.DatasetKey);
            var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
            EnsureAuthorized(DatasetAuthorizer.CanWrite(schema, user), "User is not authorized to write this dataset.");

            if (IsOfficialState(request.State))
            {
                throw new DatasetServiceException("Official state can only be set during signoff.");
            }

            await ValidateBySchemaAsync(schema, request.Header, request.Rows, cancellationToken);

            // Only load headers for the target (asOfDate, state) — no detail rows needed for
            // uniqueness and version checks. The single-day span triggers the fast prefix path.
            var instances = await repository.GetInstancesAsync(
                key, cancellationToken,
                minAsOfDate: request.AsOfDate,
                maxAsOfDate: request.AsOfDate,
                state: request.State,
                includeDetails: false);
            EnsureUniqueHeader(instances, request.AsOfDate, request.State, request.Header, null);

            var nextVersion = request.ResetVersion
                ? 1
                : instances
                    .Where(x => x.AsOfDate == request.AsOfDate && x.State == request.State)
                    .Select(x => x.Version)
                    .DefaultIfEmpty(0)
                    .Max() + 1;

            var instance = new DatasetInstance
            {
                Id = Guid.NewGuid(),
                DatasetKey = key,
                AsOfDate = request.AsOfDate,
                State = request.State,
                Version = nextVersion,
                Header = CloneMap(request.Header),
                Rows = request.Rows.Select(CloneMap).ToList(),
                CreatedBy = user.UserId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastModifiedBy = user.UserId,
                LastModifiedAtUtc = DateTimeOffset.UtcNow
            };

            await repository.SaveInstanceAsync(instance, cancellationToken);
            var createRowChanges = BuildCreateAuditRowChanges(instance, schema);
            await AddAuditAsync(user, "INSTANCE_CREATE", key, instance.Id, cancellationToken, createRowChanges);
            return instance;
        }
        catch (DatasetServiceException ex)
        {
            logger.LogWarning(ex,
                "CreateInstance validation/authorization failed. datasetKey={DatasetKey}; userId={UserId}; asOfDate={AsOfDate}; state={State}; rowCount={RowCount}; headerKeys=[{HeaderKeys}]",
                key ?? request.DatasetKey,
                user.UserId,
                request.AsOfDate,
                request.State,
                request.Rows.Count,
                string.Join(",", request.Header.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CreateInstance unexpected failure. datasetKey={DatasetKey}; userId={UserId}; asOfDate={AsOfDate}; state={State}; rowCount={RowCount}",
                key ?? request.DatasetKey,
                user.UserId,
                request.AsOfDate,
                request.State,
                request.Rows.Count);
            throw;
        }
    }

    public async Task<DatasetInstance> UpdateInstanceAsync(UpdateDatasetInstanceRequest request, UserContext user, CancellationToken cancellationToken)
    {
        string? key = null;
        try
        {
            key = NormalizeDatasetKey(request.DatasetKey);
            var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
            EnsureAuthorized(DatasetAuthorizer.CanWrite(schema, user), "User is not authorized to write this dataset.");

            if (IsOfficialState(request.State))
            {
                throw new DatasetServiceException("Official state can only be set during signoff.");
            }

            var existing = await repository.GetInstanceAsync(key, request.InstanceId, cancellationToken)
                ?? throw new DatasetServiceException("Dataset instance was not found.", DatasetServiceErrorCode.NotFound);

            // Optimistic concurrency check: reject if the caller's expected version differs from
            // the stored version, which indicates a concurrent modification occurred.
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != existing.Version)
            {
                throw new DatasetServiceException(
                    $"Conflict: instance was modified by another user (expected version {request.ExpectedVersion.Value}, current version {existing.Version}). Refresh and retry.",
                    DatasetServiceErrorCode.Conflict);
            }

            await ValidateBySchemaAsync(schema, request.Header, request.Rows, cancellationToken);

            var identityChanged =
                request.AsOfDate != existing.AsOfDate ||
                request.State != existing.State ||
                !HeadersEqual(existing.Header, request.Header);

            if (identityChanged)
            {
                // Only load headers for the target (asOfDate, state) — no detail rows needed.
                var instances = await repository.GetInstancesAsync(
                    key, cancellationToken,
                    minAsOfDate: request.AsOfDate,
                    maxAsOfDate: request.AsOfDate,
                    state: request.State,
                    includeDetails: false);
                EnsureUniqueHeader(instances, request.AsOfDate, request.State, request.Header, existing.Id);
            }

            var updated = new DatasetInstance
            {
                Id = existing.Id,
                DatasetKey = key,
                AsOfDate = request.AsOfDate,
                State = request.State,
                Version = existing.Version + 1,
                Header = CloneMap(request.Header),
                Rows = request.Rows.Select(CloneMap).ToList(),
                CreatedBy = existing.CreatedBy ?? existing.LastModifiedBy,
                CreatedAtUtc = existing.CreatedAtUtc ?? existing.LastModifiedAtUtc,
                LastModifiedBy = user.UserId,
                LastModifiedAtUtc = DateTimeOffset.UtcNow
            };

            await repository.ReplaceInstanceAsync(updated, cancellationToken);
            var updateRowChanges = BuildUpdateAuditRowChanges(existing, updated, schema);
            await AddAuditAsync(user, "INSTANCE_UPDATE", key, updated.Id, cancellationToken, updateRowChanges);
            return updated;
        }
        catch (DatasetServiceException ex)
        {
            logger.LogWarning(ex,
                "UpdateInstance validation/authorization failed. datasetKey={DatasetKey}; instanceId={InstanceId}; userId={UserId}; asOfDate={AsOfDate}; state={State}; expectedVersion={ExpectedVersion}; rowCount={RowCount}; headerKeys=[{HeaderKeys}]",
                key ?? request.DatasetKey,
                request.InstanceId,
                user.UserId,
                request.AsOfDate,
                request.State,
                request.ExpectedVersion,
                request.Rows.Count,
                string.Join(",", request.Header.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "UpdateInstance unexpected failure. datasetKey={DatasetKey}; instanceId={InstanceId}; userId={UserId}; asOfDate={AsOfDate}; state={State}; expectedVersion={ExpectedVersion}; rowCount={RowCount}",
                key ?? request.DatasetKey,
                request.InstanceId,
                user.UserId,
                request.AsOfDate,
                request.State,
                request.ExpectedVersion,
                request.Rows.Count);
            throw;
        }
    }

    public async Task DeleteInstanceAsync(string datasetKey, Guid instanceId, UserContext user, CancellationToken cancellationToken)
    {
        var key = NormalizeDatasetKey(datasetKey);
        var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
        EnsureAuthorized(DatasetAuthorizer.CanWrite(schema, user), "User is not authorized to delete dataset headers.");

        var deleted = await repository.DeleteInstanceAsync(key, instanceId, cancellationToken);
        if (!deleted)
        {
            throw new DatasetServiceException("Dataset instance was not found.", DatasetServiceErrorCode.NotFound);
        }

        await AddAuditAsync(user, "INSTANCE_DELETE", key, instanceId, cancellationToken);
    }

    public async Task<DatasetInstance> SignoffInstanceAsync(SignoffDatasetRequest request, UserContext user, CancellationToken cancellationToken)
    {
        string? key = null;
        try
        {
            key = NormalizeDatasetKey(request.DatasetKey);
            var schema = await GetSchemaOrThrowAsync(key, cancellationToken);
            EnsureAuthorized(DatasetAuthorizer.CanSignoff(schema, user), "User is not authorized to sign off this dataset.");

            var existing = await repository.GetInstanceAsync(key, request.InstanceId, cancellationToken)
                ?? throw new DatasetServiceException("Dataset instance was not found.", DatasetServiceErrorCode.NotFound);

            // Optimistic concurrency check: reject if the caller's expected version differs from
            // the stored version, which indicates a concurrent modification occurred since they fetched it.
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != existing.Version)
            {
                throw new DatasetServiceException(
                    $"Conflict: instance was modified by another user (expected version {request.ExpectedVersion.Value}, current version {existing.Version}). Refresh and retry.",
                    DatasetServiceErrorCode.Conflict);
            }

            await EnsureApproverDidNotModifyInstanceSinceLastSignoffAsync(key, existing.Id, user.UserId, cancellationToken);

            // Only load headers for the target (asOfDate, Official) — no detail rows needed.
            var instances = await repository.GetInstancesAsync(
                key, cancellationToken,
                minAsOfDate: existing.AsOfDate,
                maxAsOfDate: existing.AsOfDate,
                state: OfficialState,
                includeDetails: false);
            EnsureUniqueHeader(instances, existing.AsOfDate, OfficialState, existing.Header, existing.Id);

            var official = new DatasetInstance
            {
                Id = existing.Id,
                DatasetKey = key,
                AsOfDate = existing.AsOfDate,
                State = OfficialState,
                Version = existing.Version,
                Header = CloneMap(existing.Header),
                Rows = existing.Rows.Select(CloneMap).ToList(),
                CreatedBy = existing.CreatedBy ?? existing.LastModifiedBy,
                CreatedAtUtc = existing.CreatedAtUtc ?? existing.LastModifiedAtUtc,
                LastModifiedBy = user.UserId,
                LastModifiedAtUtc = DateTimeOffset.UtcNow
            };

            await repository.ReplaceInstanceAsync(official, cancellationToken);
            await AddAuditAsync(user, InstanceSignoffAction, key, official.Id, cancellationToken);
            return official;
        }
        catch (DatasetServiceException ex)
        {
            logger.LogWarning(ex,
                "Signoff validation/authorization failed. datasetKey={DatasetKey}; instanceId={InstanceId}; userId={UserId}; expectedVersion={ExpectedVersion}",
                key ?? request.DatasetKey,
                request.InstanceId,
                user.UserId,
                request.ExpectedVersion);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Signoff unexpected failure. datasetKey={DatasetKey}; instanceId={InstanceId}; userId={UserId}; expectedVersion={ExpectedVersion}",
                key ?? request.DatasetKey,
                request.InstanceId,
                user.UserId,
                request.ExpectedVersion);
            throw;
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> GetAuditAsync(UserContext user, CancellationToken cancellationToken, string? datasetKey = null)
    {
        var normalizedDatasetKey = string.IsNullOrWhiteSpace(datasetKey)
            ? null
            : NormalizeDatasetKey(datasetKey);

        if (user.HasRole(DatasetAuthorizer.DatasetAdminRole))
        {
            return await repository.GetAuditEventsAsync(normalizedDatasetKey, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(normalizedDatasetKey))
        {
            var schema = await GetSchemaOrThrowAsync(normalizedDatasetKey, cancellationToken);
            EnsureAuthorized(DatasetAuthorizer.CanRead(schema, user), "User is not authorized to view audit logs for this dataset.");
            return await repository.GetAuditEventsAsync(normalizedDatasetKey, cancellationToken);
        }

        var readableKeys = (await repository.GetSchemasAsync(cancellationToken))
            .Where(schema => DatasetAuthorizer.CanRead(schema, user))
            .Select(schema => NormalizeDatasetKey(schema.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allAudit = await repository.GetAuditEventsAsync(null, cancellationToken);
        return allAudit
            .Where(entry => !string.IsNullOrWhiteSpace(entry.DatasetKey)
                && readableKeys.Contains(NormalizeDatasetKey(entry.DatasetKey)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetLookupPermissibleValuesAsync(string lookupDatasetKey, CancellationToken cancellationToken)
    {
        var normalizedLookupKey = NormalizeDatasetKey(lookupDatasetKey);
        return await ResolveLookupPermissibleValuesAsync(normalizedLookupKey, cancellationToken);
    }

    private async Task AddAuditAsync(
        UserContext user,
        string action,
        string datasetKey,
        Guid? instanceId,
        CancellationToken cancellationToken,
        IReadOnlyList<AuditRowChange>? rowChanges = null)
    {
        var normalizedUser = string.IsNullOrWhiteSpace(user.UserId) ? "unknown" : user.UserId.Trim();
        await repository.AddAuditEventAsync(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            UserId = normalizedUser,
            Action = action,
            DatasetKey = datasetKey,
            DatasetInstanceId = instanceId,
            RowChanges = rowChanges ?? new List<AuditRowChange>()
        }, cancellationToken);
    }

    private async Task EnsureApproverDidNotModifyInstanceSinceLastSignoffAsync(
        string datasetKey,
        Guid instanceId,
        string approverUserId,
        CancellationToken cancellationToken)
    {
        var auditEvents = await repository.GetAuditEventsAsync(datasetKey, cancellationToken);
        var instanceAudit = auditEvents
            .Where(x => x.DatasetInstanceId == instanceId)
            .ToList();

        var lastSignoffAtUtc = instanceAudit
            .Where(x => string.Equals(x.Action, InstanceSignoffAction, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => (DateTimeOffset?)x.OccurredAtUtc)
            .FirstOrDefault();

        var contributorsSinceLastSignoff = instanceAudit
            .Where(x =>
                (string.Equals(x.Action, InstanceCreateAction, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Action, InstanceUpdateAction, StringComparison.OrdinalIgnoreCase))
                && (!lastSignoffAtUtc.HasValue || x.OccurredAtUtc > lastSignoffAtUtc.Value))
            .Select(x => x.UserId?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var approverChangeCount = instanceAudit.Count(x =>
            (string.Equals(x.Action, InstanceCreateAction, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Action, InstanceUpdateAction, StringComparison.OrdinalIgnoreCase))
            && (!lastSignoffAtUtc.HasValue || x.OccurredAtUtc > lastSignoffAtUtc.Value)
            && string.Equals(x.UserId?.Trim(), approverUserId.Trim(), StringComparison.OrdinalIgnoreCase));

        var normalizedApprover = approverUserId.Trim();
        if (!contributorsSinceLastSignoff.Contains(normalizedApprover, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var changeWord = approverChangeCount == 1 ? "change" : "changes";

        throw new DatasetServiceException(
            $"Approval is not permitted for '{normalizedApprover}' because {approverChangeCount} {changeWord} were made by this user "
            + "since the last approved version. "
            + $"Contributors since last approval: {string.Join(", ", contributorsSinceLastSignoff)}.");
    }

    private static string BuildCreateAuditDetails(DatasetInstance instance, DatasetSchema schema)
    {
        var headerText = $"Header: {FormatRowForAudit(instance.Header)}. ";
        var keyFields = GetDetailKeyFields(schema);
        if (keyFields.Count == 0)
        {
            return $"Created version {instance.Version} for {instance.AsOfDate} / {instance.State}. {headerText}Detail rows saved: {instance.Rows.Count}. Row data was not audited because no key fields were defined.";
        }

        var rowDetails = instance.Rows
            .Select((row) =>
            {
                var rowKey = BuildRowKey(row, keyFields);
                return $"row [{rowKey}] added {FormatRowForAudit(row)}";
            })
            .ToList();

        var rowsText = rowDetails.Count == 0
            ? "no detail rows"
            : string.Join(" | ", rowDetails);

        return $"Created version {instance.Version} for {instance.AsOfDate} / {instance.State}. {headerText}Detail rows ({instance.Rows.Count}): {rowsText}.";
    }

    private static string BuildUpdateAuditDetails(DatasetInstance existing, DatasetInstance updated, DatasetSchema schema)
    {
        var headerChanged = !RowsEqual(existing.Header, updated.Header);
        var lifecycleChangeSummary = BuildLifecycleDiffSummary(existing, updated);
        var headerChangeSummary = headerChanged
            ? BuildHeaderDiffSummary(existing.Header, updated.Header)
            : string.Empty;
        var lifecycleText = string.IsNullOrWhiteSpace(lifecycleChangeSummary)
            ? string.Empty
            : $"Lifecycle changes: {lifecycleChangeSummary}. ";
        var keyFields = GetDetailKeyFields(schema);
        if (keyFields.Count == 0)
        {
            var headerFallbackText = headerChanged
                ? $"Header: before={FormatRowForAudit(existing.Header)}; after={FormatRowForAudit(updated.Header)}. Header changes: {headerChangeSummary}. "
                : string.Empty;
            return $"Replaced instance {existing.Id} in place. {lifecycleText}{headerFallbackText}Row data was not audited because no key fields were defined.";
        }

        var changeLines = GetChangedRowAuditLines(existing.Rows, updated.Rows, keyFields);
        var changesText = changeLines.Count == 0
            ? "no detail row changes"
            : string.Join(" | ", changeLines);

        var headerText = headerChanged
            ? $"Header: before={FormatRowForAudit(existing.Header)}; after={FormatRowForAudit(updated.Header)}. Header changes: {headerChangeSummary}. "
            : string.Empty;

        return $"Updated instance {existing.Id}. {lifecycleText}{headerText}Changes ({changeLines.Count}): {changesText}.";
    }

    private static string BuildLifecycleDiffSummary(DatasetInstance before, DatasetInstance after)
    {
        var changes = new List<string>();

        if (before.AsOfDate != after.AsOfDate)
        {
            changes.Add($"asOfDate {before.AsOfDate:yyyy-MM-dd} -> {after.AsOfDate:yyyy-MM-dd}");
        }

        if (!string.Equals(before.State, after.State, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"state {before.State} -> {after.State}");
        }

        if (changes.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", changes);
    }

    private static string BuildHeaderDiffSummary(IDictionary<string, object?> beforeHeader, IDictionary<string, object?> afterHeader)
    {
        var allFields = beforeHeader.Keys
            .Concat(afterHeader.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changes = new List<string>();
        foreach (var field in allFields)
        {
            beforeHeader.TryGetValue(field, out var beforeValue);
            afterHeader.TryGetValue(field, out var afterValue);
            var beforeText = FormatValueForAudit(beforeValue);
            var afterText = FormatValueForAudit(afterValue);
            if (!string.Equals(beforeText, afterText, StringComparison.Ordinal))
            {
                changes.Add($"{field} {beforeText} -> {afterText}");
            }
        }

        if (changes.Count == 0)
        {
            return "no header value changes";
        }

        return string.Join(", ", changes);
    }

    private static List<string> GetChangedRowAuditLines(
        IReadOnlyList<IDictionary<string, object?>> beforeRows,
        IReadOnlyList<IDictionary<string, object?>> afterRows,
        IReadOnlyList<string> keyFields)
    {
        var beforeByKey = beforeRows.ToDictionary(
            row => BuildRowKey(row, keyFields),
            row => row,
            StringComparer.OrdinalIgnoreCase);

        var afterByKey = afterRows.ToDictionary(
            row => BuildRowKey(row, keyFields),
            row => row,
            StringComparer.OrdinalIgnoreCase);

        var allKeys = beforeByKey.Keys
            .Concat(afterByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>();

        foreach (var rowKey in allKeys)
        {
            var hasBefore = beforeByKey.TryGetValue(rowKey, out var beforeRow);
            var hasAfter = afterByKey.TryGetValue(rowKey, out var afterRow);

            if (!hasBefore && hasAfter)
            {
                lines.Add($"added row [{rowKey}] {FormatNonKeyValuesForAudit(afterRow!, keyFields)}");
                continue;
            }

            if (hasBefore && !hasAfter)
            {
                lines.Add($"removed row [{rowKey}] {FormatNonKeyValuesForAudit(beforeRow!, keyFields)}");
                continue;
            }

            if (hasBefore && hasAfter && !RowsEqual(beforeRow!, afterRow!))
            {
                lines.Add($"updated row [{rowKey}] ({BuildFieldDiffSummary(beforeRow!, afterRow!)})");
            }
        }

        return lines;
    }

    private static List<AuditRowChange> BuildCreateAuditRowChanges(DatasetInstance instance, DatasetSchema schema)
    {
        var keyFields = GetDetailKeyFields(schema);
        if (keyFields.Count == 0)
        {
            return new List<AuditRowChange>();
        }

        return instance.Rows.Select(row => new AuditRowChange
        {
            Operation = "added",
            KeyFields = BuildAuditValueMap(row, keyFields),
            TargetValues = BuildNonKeyAuditValueMap(row, keyFields)
        }).ToList();
    }

    private static List<AuditRowChange> BuildUpdateAuditRowChanges(DatasetInstance existing, DatasetInstance updated, DatasetSchema schema)
    {
        var keyFields = GetDetailKeyFields(schema);
        if (keyFields.Count == 0)
        {
            return new List<AuditRowChange>();
        }

        var beforeByKey = existing.Rows.ToDictionary(
            row => BuildRowKey(row, keyFields),
            row => row,
            StringComparer.OrdinalIgnoreCase);

        var afterByKey = updated.Rows.ToDictionary(
            row => BuildRowKey(row, keyFields),
            row => row,
            StringComparer.OrdinalIgnoreCase);

        var allKeys = beforeByKey.Keys
            .Concat(afterByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rowChanges = new List<AuditRowChange>();

        foreach (var rowKey in allKeys)
        {
            var hasBefore = beforeByKey.TryGetValue(rowKey, out var beforeRow);
            var hasAfter = afterByKey.TryGetValue(rowKey, out var afterRow);

            if (!hasBefore && hasAfter)
            {
                rowChanges.Add(new AuditRowChange
                {
                    Operation = "added",
                    KeyFields = BuildAuditValueMap(afterRow!, keyFields),
                    TargetValues = BuildNonKeyAuditValueMap(afterRow!, keyFields)
                });
                continue;
            }

            if (hasBefore && !hasAfter)
            {
                rowChanges.Add(new AuditRowChange
                {
                    Operation = "removed",
                    KeyFields = BuildAuditValueMap(beforeRow!, keyFields),
                    SourceValues = BuildNonKeyAuditValueMap(beforeRow!, keyFields)
                });
                continue;
            }

            if (hasBefore && hasAfter && !RowsEqual(beforeRow!, afterRow!))
            {
                rowChanges.Add(new AuditRowChange
                {
                    Operation = "updated",
                    KeyFields = BuildAuditValueMap(afterRow!, keyFields),
                    SourceValues = BuildNonKeyAuditValueMap(beforeRow!, keyFields),
                    TargetValues = BuildNonKeyAuditValueMap(afterRow!, keyFields)
                });
            }
        }

        return rowChanges;
    }

    private static IReadOnlyList<string> GetDetailKeyFields(DatasetSchema schema)
    {
        return schema.DetailFields
            .Where(x => x.IsKey)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildRowKey(IDictionary<string, object?> row, IReadOnlyList<string> keyFields)
    {
        var parts = keyFields.Select((keyField) =>
        {
            row.TryGetValue(keyField, out var value);
            return $"{keyField}={FormatValueForAudit(value)}";
        });

        return string.Join(", ", parts);
    }

    private static IDictionary<string, string> BuildAuditValueMap(IDictionary<string, object?> row, IReadOnlyList<string> fields)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            row.TryGetValue(field, out var value);
            map[field] = FormatValueForAudit(value);
        }

        return map;
    }

    private static IDictionary<string, string> BuildNonKeyAuditValueMap(IDictionary<string, object?> row, IReadOnlyList<string> keyFields)
    {
        return row
            .Where(x => !keyFields.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => FormatValueForAudit(x.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool RowsEqual(IDictionary<string, object?> left, IDictionary<string, object?> right)
    {
        var leftKeys = left.Keys.Select(x => x.Trim()).Where(x => x.Length > 0);
        var rightKeys = right.Keys.Select(x => x.Trim()).Where(x => x.Length > 0);
        var allKeys = leftKeys
            .Concat(rightKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in allKeys)
        {
            left.TryGetValue(key, out var leftValue);
            right.TryGetValue(key, out var rightValue);
            if (!string.Equals(FormatValueForAudit(leftValue), FormatValueForAudit(rightValue), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatRowForAudit(IDictionary<string, object?> row)
    {
        var ordered = row
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={FormatValueForAudit(x.Value)}");
        return "{" + string.Join(", ", ordered) + "}";
    }

    private static string BuildFieldDiffSummary(IDictionary<string, object?> beforeRow, IDictionary<string, object?> afterRow)
    {
        var allFields = beforeRow.Keys
            .Concat(afterRow.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changes = new List<string>();
        foreach (var field in allFields)
        {
            beforeRow.TryGetValue(field, out var beforeValue);
            afterRow.TryGetValue(field, out var afterValue);
            var beforeText = FormatValueForAudit(beforeValue);
            var afterText = FormatValueForAudit(afterValue);
            if (!string.Equals(beforeText, afterText, StringComparison.Ordinal))
            {
                changes.Add($"{field} {beforeText} -> {afterText}");
            }
        }

        if (changes.Count == 0)
        {
            return "no value changes";
        }

        return string.Join(", ", changes);
    }

    private static string FormatNonKeyValuesForAudit(IDictionary<string, object?> row, IReadOnlyList<string> keyFields)
    {
        var nonKeyValues = row
            .Where(x => !keyFields.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        if (nonKeyValues.Count == 0)
        {
            return "{no non-key fields}";
        }

        return FormatRowForAudit(nonKeyValues);
    }

    private static string FormatValueForAudit(object? value)
    {
        return value switch
        {
            null => "null",
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private async Task<DatasetSchema> GetSchemaOrThrowAsync(string datasetKey, CancellationToken cancellationToken)
    {
        return await repository.GetSchemaAsync(datasetKey, cancellationToken)
            ?? throw new DatasetServiceException($"Schema '{datasetKey}' was not found.", DatasetServiceErrorCode.NotFound);
    }

    private static void EnsureAuthorized(bool allowed, string message)
    {
        if (!allowed)
        {
            throw new DatasetServiceException(message);
        }
    }

    private static string NormalizeDatasetKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DatasetServiceException("Dataset key is required.");
        }

        return key.Trim().ToUpperInvariant();
    }

    private static Dictionary<string, object?> CloneMap(IDictionary<string, object?> source)
    {
        return source.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureNoDuplicateFieldNames(IReadOnlyList<SchemaField> fields, string groupName)
    {
        var duplicates = fields
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new DatasetServiceException($"Duplicate {groupName} field names: {string.Join(", ", duplicates)}");
        }
    }

    private static void EnsureKeyFieldsAreValid(DatasetSchema schema)
    {
        var keyFields = schema.DetailFields
            .Where(x => x.IsKey)
            .Select(x => x.Name)
            .ToList();

        var duplicateKeyFields = keyFields
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicateKeyFields.Count > 0)
        {
            throw new DatasetServiceException($"Duplicate key fields: {string.Join(", ", duplicateKeyFields)}");
        }

        if (keyFields.Count == 0)
        {
            return;
        }
    }

    private static void EnsureFieldConfigurationsAreValid(DatasetSchema schema)
    {
        EnsureFieldConfigurationsAreValid(schema.HeaderFields, "header");
        EnsureFieldConfigurationsAreValid(schema.DetailFields, "detail");
    }

    private static void EnsureFieldConfigurationsAreValid(IReadOnlyList<SchemaField> fields, string groupName)
    {
        foreach (var field in fields)
        {
            if (field.Type == FieldType.Select && field.AllowedValues.Count == 0)
            {
                throw new DatasetServiceException($"{groupName} field '{field.Name}' is Select but has no allowed values configured.");
            }

            if (field.Type == FieldType.Lookup && string.IsNullOrWhiteSpace(field.LookupDatasetKey))
            {
                throw new DatasetServiceException($"{groupName} field '{field.Name}' is Lookup but has no lookup dataset key configured.");
            }
        }
    }

    private static void EnsureUniqueHeader(
        IReadOnlyList<DatasetInstance> instances,
        DateOnly asOfDate,
        string state,
        IDictionary<string, object?> header,
        Guid? excludeId)
    {
        var duplicateExists = instances.Any(instance =>
            instance.AsOfDate == asOfDate &&
            StateMatches(instance.State, CanonicalToken(state)) &&
            (!excludeId.HasValue || instance.Id != excludeId.Value) &&
            HeadersEqual(instance.Header, header));

        if (duplicateExists)
        {
            throw new DatasetServiceException("A header with the same AsOfDate, State, and header values already exists.");
        }
    }

    private static bool HeadersEqual(IDictionary<string, object?> left, IDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var rightValue))
            {
                return false;
            }

            if (!HeaderValuesEqual(entry.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HeaderValuesEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is JsonElement leftJson && right is JsonElement rightJson)
        {
            return string.Equals(leftJson.GetRawText(), rightJson.GetRawText(), StringComparison.Ordinal);
        }

        if (left is JsonElement leftElement)
        {
            return string.Equals(leftElement.GetRawText(), JsonSerializer.Serialize(right), StringComparison.Ordinal);
        }

        if (right is JsonElement rightElement)
        {
            return string.Equals(JsonSerializer.Serialize(left), rightElement.GetRawText(), StringComparison.Ordinal);
        }

        return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> NormalizeHeaderCriteria(IReadOnlyDictionary<string, string>? headerCriteria)
    {
        if (headerCriteria is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return headerCriteria
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeStateFilter(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return CanonicalToken(state);
    }

    private static bool StateMatches(string state, string normalizedStateFilter)
    {
        return string.Equals(CanonicalToken(state), normalizedStateFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOfficialState(string? state)
    {
        return string.Equals(CanonicalToken(state ?? string.Empty), CanonicalToken(OfficialState), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HeaderMatchesCriteria(IDictionary<string, object?> header, IReadOnlyDictionary<string, string> criteria)
    {
        if (criteria.Count == 0)
        {
            return true;
        }

        foreach (var criterion in criteria)
        {
            header.TryGetValue(criterion.Key, out var rawValue);
            var actual = rawValue?.ToString() ?? string.Empty;
            if (!actual.Contains(criterion.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string CanonicalToken(string value)
    {
        return new string(value
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-')
            .ToArray())
            .ToUpperInvariant();
    }

    private async Task ValidateBySchemaAsync(
        DatasetSchema schema,
        IDictionary<string, object?> header,
        IReadOnlyList<IDictionary<string, object?>> rows,
        CancellationToken cancellationToken)
    {
        var lookupValueCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        await ValidateMapAsync("Header", schema.HeaderFields, header, lookupValueCache, cancellationToken);

        for (var i = 0; i < rows.Count; i++)
        {
            await ValidateMapAsync($"Detail row {i + 1}", schema.DetailFields, rows[i], lookupValueCache, cancellationToken);
        }

        ValidateUniqueKeyFields(schema, rows);
    }

    private static void ValidateUniqueKeyFields(DatasetSchema schema, IReadOnlyList<IDictionary<string, object?>> rows)
    {
        var keyFields = schema.DetailFields
            .Where(x => x.IsKey)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keyFields.Count == 0 || rows.Count < 2)
        {
            return;
        }

        var fieldsByName = schema.DetailFields.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var keyParts = new List<string>(keyFields.Count);
            foreach (var keyFieldName in keyFields)
            {
                row.TryGetValue(keyFieldName, out var raw);
                var field = fieldsByName[keyFieldName];
                keyParts.Add(NormalizeKeyFieldValue(field, raw));
            }

            var key = string.Join("\u001F", keyParts);
            if (seen.TryGetValue(key, out _))
            {
                var duplicateKeyValuesText = string.Join(", ", keyFields.Select((keyFieldName) =>
                {
                    row.TryGetValue(keyFieldName, out var rawValue);
                    return $"{keyFieldName}={FormatValueForAudit(rawValue)}";
                }));

                throw new DatasetServiceException(
                    $"Detail rows must be unique by key fields [{string.Join(", ", keyFields)}]. Duplicate key values: [{duplicateKeyValuesText}].");
            }

            seen[key] = i;
        }
    }

    private static string NormalizeKeyFieldValue(SchemaField field, object? value)
    {
        var text = value?.ToString()?.Trim() ?? string.Empty;

        return field.Type switch
        {
            FieldType.Number when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                => d.ToString("G29", CultureInfo.InvariantCulture),
            FieldType.Date when DateOnly.TryParse(text, out var date)
                => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FieldType.Boolean when bool.TryParse(text, out var b)
                => b ? "TRUE" : "FALSE",
            _ => text.ToUpperInvariant()
        };
    }

    private async Task ValidateMapAsync(
        string scope,
        IReadOnlyList<SchemaField> fields,
        IDictionary<string, object?> values,
        Dictionary<string, IReadOnlyList<string>> lookupValueCache,
        CancellationToken cancellationToken)
    {
        foreach (var field in fields)
        {
            values.TryGetValue(field.Name, out var rawValue);
            var hasValue = rawValue is not null && !string.IsNullOrWhiteSpace(rawValue.ToString());

            if (field.Required && !hasValue)
            {
                throw new DatasetServiceException($"{scope}: field '{field.Name}' is required.");
            }

            if (!hasValue)
            {
                continue;
            }

            var text = rawValue!.ToString()!;

            if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value)
            {
                throw new DatasetServiceException($"{scope}: field '{field.Name}' exceeds max length {field.MaxLength.Value}.");
            }

            switch (field.Type)
            {
                case FieldType.Number:
                    if (!decimal.TryParse(text, out var numberValue))
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be numeric.");
                    }

                    if (field.MinValue.HasValue && numberValue < field.MinValue.Value)
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be >= {field.MinValue.Value}.");
                    }

                    if (field.MaxValue.HasValue && numberValue > field.MaxValue.Value)
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be <= {field.MaxValue.Value}.");
                    }

                    break;
                case FieldType.Date:
                    if (!DateOnly.TryParse(text, out _))
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be a valid date.");
                    }

                    break;
                case FieldType.Boolean:
                    if (!bool.TryParse(text, out _))
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be true or false.");
                    }

                    break;
                case FieldType.Select:
                    if (field.AllowedValues.Count == 0)
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' has no allowed values configured.");
                    }

                    var match = field.AllowedValues.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));
                    if (!match)
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be one of [{string.Join(", ", field.AllowedValues)}].");
                    }

                    break;
                case FieldType.Lookup:
                    if (string.IsNullOrWhiteSpace(field.LookupDatasetKey))
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' has no lookup dataset key configured.");
                    }

                    var lookupDatasetKey = NormalizeDatasetKey(field.LookupDatasetKey);
                    if (!lookupValueCache.TryGetValue(lookupDatasetKey, out var permissibleValues))
                    {
                        permissibleValues = await ResolveLookupPermissibleValuesAsync(lookupDatasetKey, cancellationToken);
                        lookupValueCache[lookupDatasetKey] = permissibleValues;
                    }

                    var lookupMatch = permissibleValues.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));
                    if (!lookupMatch)
                    {
                        throw new DatasetServiceException($"{scope}: field '{field.Name}' must be one of [{string.Join(", ", permissibleValues)}] from lookup dataset '{lookupDatasetKey}'.");
                    }

                    break;
            }
        }
    }

    private async Task<IReadOnlyList<string>> ResolveLookupPermissibleValuesAsync(string lookupDatasetKey, CancellationToken cancellationToken)
    {
        var lookupSchema = await repository.GetSchemaAsync(lookupDatasetKey, cancellationToken)
            ?? throw new DatasetServiceException($"Lookup dataset schema '{lookupDatasetKey}' was not found.", DatasetServiceErrorCode.NotFound);

        if (lookupSchema.DetailFields.Count == 0)
        {
            throw new DatasetServiceException($"Lookup dataset '{lookupDatasetKey}' has no detail fields.");
        }

        var lookupSourceFieldName = lookupSchema.DetailFields[0].Name;

        // Walk backwards in 400-day windows to find the most recent Official instance.
        // Each window uses the fast per-day prefix path (≤400 day span) rather than a full
        // scan, so we avoid reading thousands of header files across all historical dates.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DatasetInstance? latestOfficial = null;
        for (var windowEnd = today; windowEnd > today.AddYears(-20) && latestOfficial is null; windowEnd = windowEnd.AddDays(-401))
        {
            var windowStart = windowEnd.AddDays(-400);
            var candidates = await repository.GetInstancesAsync(
                lookupDatasetKey, cancellationToken,
                minAsOfDate: windowStart,
                maxAsOfDate: windowEnd,
                state: OfficialState,
                includeDetails: true);

            latestOfficial = candidates
                .OrderByDescending(x => x.AsOfDate)
                .ThenByDescending(x => x.Version)
                .FirstOrDefault();
        }

        if (latestOfficial is null)
        {
            throw new DatasetServiceException($"Lookup dataset '{lookupDatasetKey}' has no official instance.");
        }

        var values = latestOfficial.Rows
            .Select(row =>
            {
                row.TryGetValue(lookupSourceFieldName, out var rawValue);
                return rawValue?.ToString()?.Trim() ?? string.Empty;
            })
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (values.Count == 0)
        {
            throw new DatasetServiceException($"Lookup dataset '{lookupDatasetKey}' has no values in first detail field '{lookupSourceFieldName}' for latest official instance.");
        }

        return values;
    }
}
