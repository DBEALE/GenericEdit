using System.Text.Json;
using System.Globalization;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Domain.Models;
using Microsoft.Extensions.Options;

namespace DatasetPlatform.Api.Infrastructure;

public class BlobDataRepository(IBlobStore blobStore, IOptions<StorageOptions> options) : IDataRepository
{
    private const string HeaderFileSuffix = DatasetHeaderPartitioning.HeaderFileSuffix;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly IBlobStore _blobStore = blobStore;
    private readonly string _storageProvider = options.Value.Provider;
    private readonly S3StorageOptions _s3 = options.Value.S3;
    private bool _legacyAuditMigrated;

    public async Task<IReadOnlyList<DatasetSchema>> GetSchemasAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var schemaKeys = await ListKeysByPrefixAsync(BuildKey("schemas/"), cancellationToken);
            var schemas = new List<DatasetSchema>();
            foreach (var key in schemaKeys.Where(k => k.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var schema = await ReadJsonAsync<DatasetSchema?>(key, null, cancellationToken);
                if (schema is not null && !string.IsNullOrWhiteSpace(schema.Key))
                {
                    schemas.Add(schema);
                }
            }

            return schemas.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<DatasetSchema?> GetSchemaAsync(string datasetKey, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ReadJsonAsync<DatasetSchema?>(GetSchemaKey(datasetKey), null, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpsertSchemaAsync(DatasetSchema schema, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await WriteJsonAsync(GetSchemaKey(schema.Key), schema, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteSchemaAsync(string datasetKey, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var normalizedKey = EncodeFileKey(datasetKey);
            await DeleteIfExistsAsync(GetSchemaKey(datasetKey), cancellationToken);
            await DeleteIfExistsAsync(BuildKey($"instances/{normalizedKey}.json"), cancellationToken);

            var instancePrefix = BuildKey($"instances/{normalizedKey}/");
            var instanceKeys = await ListKeysByPrefixAsync(instancePrefix, cancellationToken);
            foreach (var key in instanceKeys)
            {
                await DeleteIfExistsAsync(key, cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<DatasetInstance>> GetInstancesAsync(
        string datasetKey,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null,
        bool includeDetails = true)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var trace = await ReadInstancesForDatasetWithTraceAsync(datasetKey, minAsOfDate, maxAsOfDate, state, headerCriteria, includeDetails, cancellationToken);
            return trace.Instances;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<DatasetReadTrace> GetInstancesWithTraceAsync(
        string datasetKey,
        CancellationToken cancellationToken,
        DateOnly? minAsOfDate = null,
        DateOnly? maxAsOfDate = null,
        string? state = null,
        IReadOnlyDictionary<string, string>? headerCriteria = null,
        bool includeDetails = true)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ReadInstancesForDatasetWithTraceAsync(datasetKey, minAsOfDate, maxAsOfDate, state, headerCriteria, includeDetails, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<DatasetInstance?> GetInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ReadJsonAsync<DatasetInstance?>(GetInstanceKey(datasetKey, instanceId), null, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveInstanceAsync(DatasetInstance instance, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await SaveInstanceUnlockedAsync(instance, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> ReplaceInstanceAsync(DatasetInstance instance, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var key = GetInstanceKey(instance.DatasetKey, instance.Id);
            if (!await ObjectExistsAsync(key, cancellationToken))
            {
                return false;
            }

            await SaveInstanceUnlockedAsync(instance, cancellationToken);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var key = GetInstanceKey(datasetKey, instanceId);
            if (!await ObjectExistsAsync(key, cancellationToken))
            {
                return false;
            }

            await DeleteIfExistsAsync(key, cancellationToken);
            await DeleteAllHeaderIndexesForInstanceAsync(datasetKey, instanceId, cancellationToken);
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(string? datasetKey, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLegacyAuditMigratedAsync(cancellationToken);
            var audit = new List<AuditEvent>();
            var auditPrefix = string.IsNullOrWhiteSpace(datasetKey)
                ? GetAuditRootPrefix()
                : GetAuditDatasetPrefix(datasetKey);
            var auditKeys = await ListKeysByPrefixAsync(auditPrefix, cancellationToken);

            foreach (var key in auditKeys.Where(k => k.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var entry = await ReadJsonAsync<AuditEvent?>(key, null, cancellationToken);
                if (entry is not null)
                {
                    audit.Add(entry);
                }
            }

            return audit
                .OrderByDescending(x => x.OccurredAtUtc)
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLegacyAuditMigratedAsync(cancellationToken);
            await WriteJsonAsync(GetAuditEventKey(auditEvent), auditEvent, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureLegacyAuditMigratedAsync(CancellationToken cancellationToken)
    {
        if (_legacyAuditMigrated)
        {
            return;
        }

        var legacyAudit = await ReadJsonAsync(GetLegacyAuditKey(), new List<AuditEvent>(), cancellationToken);
        foreach (var auditEvent in legacyAudit)
        {
            var targetKey = GetAuditEventKey(auditEvent);
            if (!await ObjectExistsAsync(targetKey, cancellationToken))
            {
                await WriteJsonAsync(targetKey, auditEvent, cancellationToken);
            }
        }

        _legacyAuditMigrated = true;
    }

    private async Task<DatasetReadTrace> ReadInstancesForDatasetWithTraceAsync(
        string datasetKey,
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? state,
        IReadOnlyDictionary<string, string>? headerCriteria,
        bool includeDetails,
        CancellationToken cancellationToken)
    {
        var normalizedDataset = EncodeFileKey(datasetKey);
        var instancePrefix = BuildKey($"instances/{normalizedDataset}/");
        var headersPrefix = BuildKey($"instances/{normalizedDataset}/headers/");

        var headerKeys = (await ListKeysByPrefixAsync(headersPrefix, cancellationToken))
            .Where(k => k.EndsWith(HeaderFileSuffix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var detailKeys = new List<string>();
        Dictionary<Guid, string>? fullKeysById = null;

        async Task<Dictionary<Guid, string>> EnsureDetailKeysByIdAsync()
        {
            if (fullKeysById is not null)
            {
                return fullKeysById;
            }

            var allKeys = await ListKeysByPrefixAsync(instancePrefix, cancellationToken);
            detailKeys = allKeys
                .Where(IsDetailInstanceKey)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            fullKeysById = detailKeys
                .Select(key => new { Key = key, Name = Path.GetFileNameWithoutExtension(key) })
                .Where(x => Guid.TryParseExact(x.Name, "N", out _))
                .ToDictionary(x => Guid.ParseExact(x.Name, "N"), x => x.Key);
            return fullKeysById;
        }

        int GetDetailFilesTotal() => fullKeysById?.Count ?? 0;

        var normalizedState = NormalizeStateFilter(state);
        var normalizedCriteria = DatasetHeaderPartitioning.NormalizeHeaderCriteria(headerCriteria);
        var hasFilters = minAsOfDate.HasValue || maxAsOfDate.HasValue || normalizedState is not null || normalizedCriteria.Count > 0;

        if (!hasFilters)
        {
            if (!includeDetails)
            {
                return await ReadHeaderOnlyWithoutFiltersAsync(
                    headerKeys,
                    normalizedCriteria,
                    EnsureDetailKeysByIdAsync,
                    GetDetailFilesTotal,
                    cancellationToken);
            }

            var loadedFullKeysById = await EnsureDetailKeysByIdAsync();

            var items = new List<DatasetInstance>();
            var loaded = new List<DatasetLoadedFileTrace>();
            foreach (var detailKey in detailKeys)
            {
                var instance = await ReadJsonAsync<DatasetInstance?>(detailKey, null, cancellationToken);
                loaded.Add(new DatasetLoadedFileTrace
                {
                    FileName = ToDatasetTracePath(detailKey),
                    Reason = "Full scan because no filters were provided."
                });

                if (instance is not null)
                {
                    items.Add(instance);
                }
            }

            return new DatasetReadTrace
            {
                Instances = items,
                LoadedFilenames = loaded,
                HeaderFilesRead = 0,
                HeaderFilesTotal = headerKeys.Count,
                DetailFilesRead = detailKeys.Count,
                DetailFilesTotal = loadedFullKeysById.Count,
                CandidateHeaderFilesConsidered = 0,
                MatchedInstanceFileCount = items.Count,
                HeaderFilesRebuilt = 0,
                UsedFilteredSearchPath = false
            };
        }

        var loadedFiles = new List<DatasetLoadedFileTrace>();
        var matchedIds = new List<Guid>();
        var matchedHeaders = new Dictionary<Guid, DatasetInstanceHeaderIndex>();
        var headerFilesRead = 0;
        var headerFilesRebuilt = 0;
        var detailFilesRead = 0;
        var cachedInstances = new Dictionary<string, DatasetInstance>(StringComparer.Ordinal);

        var candidateHeaderKeys = await GetCandidateHeaderKeysAsync(
            normalizedDataset,
            headerKeys,
            minAsOfDate,
            maxAsOfDate,
            normalizedState,
            cancellationToken);

        foreach (var headerKey in candidateHeaderKeys)
        {
            if (!TryParseHeaderKey(headerKey, out _, out _, out var instanceId))
            {
                continue;
            }

            var header = await ReadJsonAsync<DatasetInstanceHeaderIndex?>(headerKey, null, cancellationToken);
            var isMatch = header is not null && HeaderIndexMatches(header, minAsOfDate, maxAsOfDate, normalizedState, normalizedCriteria);
            headerFilesRead += 1;
            loadedFiles.Add(new DatasetLoadedFileTrace
            {
                FileName = ToDatasetTracePath(headerKey),
                Reason = $"Header candidate evaluated against filters. lookedFor=[{FormatTraceSearchCriteria(minAsOfDate, maxAsOfDate, normalizedState, normalizedCriteria)}]; found=[{FormatHeaderIndexForTrace(header)}]; matched={isMatch.ToString().ToLowerInvariant()}."
            });

            if (isMatch)
            {
                if (!includeDetails && RequiresSummaryBackfill(header!))
                {
                    var loadedFullKeysById = await EnsureDetailKeysByIdAsync();
                    if (loadedFullKeysById.TryGetValue(instanceId, out var detailKey))
                    {
                        var instance = await ReadJsonAsync<DatasetInstance?>(detailKey, null, cancellationToken);
                        detailFilesRead += 1;
                        loadedFiles.Add(new DatasetLoadedFileTrace
                        {
                            FileName = ToDatasetTracePath(detailKey),
                            Reason = "Header index missing summary fields; read instance file to rebuild header index metadata."
                        });

                        if (instance is not null)
                        {
                            cachedInstances[detailKey] = instance;
                            var refreshedHeader = CreateHeaderIndex(instance);
                            await WriteJsonAsync(GetHeaderKey(instance.DatasetKey, refreshedHeader.State, refreshedHeader.AsOfDate, refreshedHeader.Id), refreshedHeader, cancellationToken);
                            headerFilesRebuilt += 1;
                            header = refreshedHeader;
                        }
                    }
                }

                matchedIds.Add(instanceId);
                matchedHeaders[instanceId] = header!;
            }
        }

        var loadedFullKeysByIdForDetails = includeDetails
            ? await EnsureDetailKeysByIdAsync()
            : fullKeysById;

        var indexedIds = headerKeys
            .Select(k => Path.GetFileName(k))
            .Where(name => name.EndsWith(HeaderFileSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[..^HeaderFileSuffix.Length])
            .Where(idText => Guid.TryParseExact(idText, "N", out _))
            .Select(idText => Guid.ParseExact(idText, "N"))
            .ToHashSet();

        if (includeDetails)
        {
            foreach (var entry in loadedFullKeysByIdForDetails!)
            {
                if (indexedIds.Contains(entry.Key))
                {
                    continue;
                }

                var instance = await ReadJsonAsync<DatasetInstance?>(entry.Value, null, cancellationToken);
                detailFilesRead += 1;
                loadedFiles.Add(new DatasetLoadedFileTrace
                {
                    FileName = ToDatasetTracePath(entry.Value),
                    Reason = "Header index missing; read instance file to rebuild header index."
                });

                if (instance is null)
                {
                    continue;
                }

                cachedInstances[entry.Value] = instance;
                var header = CreateHeaderIndex(instance);
                await WriteJsonAsync(GetHeaderKey(instance.DatasetKey, header.State, header.AsOfDate, header.Id), header, cancellationToken);
                headerFilesRebuilt += 1;

                if (HeaderIndexMatches(header, minAsOfDate, maxAsOfDate, normalizedState, normalizedCriteria))
                {
                    matchedIds.Add(entry.Key);
                    matchedHeaders[entry.Key] = header;
                }
            }
        }

        var resultInstances = new List<DatasetInstance>();
        foreach (var instanceId in matchedIds.Distinct())
        {
            if (!includeDetails)
            {
                if (matchedHeaders.TryGetValue(instanceId, out var headerOnly))
                {
                    resultInstances.Add(ToHeaderOnlyInstance(headerOnly));
                }

                continue;
            }

            if (loadedFullKeysByIdForDetails is null || !loadedFullKeysByIdForDetails.TryGetValue(instanceId, out var detailKey))
            {
                continue;
            }

            if (cachedInstances.TryGetValue(detailKey, out var cached))
            {
                resultInstances.Add(cached);
                continue;
            }

            var instance = await ReadJsonAsync<DatasetInstance?>(detailKey, null, cancellationToken);
            detailFilesRead += 1;
            if (instance is null)
            {
                loadedFiles.Add(new DatasetLoadedFileTrace
                {
                    FileName = ToDatasetTracePath(detailKey),
                    Reason = $"Instance detail read attempted for matched header id. lookedFor=[{FormatTraceSearchCriteria(minAsOfDate, maxAsOfDate, normalizedState, normalizedCriteria)}]; found=[<detail payload missing>]; matched=false."
                });
                continue;
            }

            loadedFiles.Add(new DatasetLoadedFileTrace
            {
                FileName = ToDatasetTracePath(detailKey),
                Reason = $"Instance matched header filters; read full payload for response. lookedFor=[{FormatTraceSearchCriteria(minAsOfDate, maxAsOfDate, normalizedState, normalizedCriteria)}]; found=[{FormatInstanceHeaderForTrace(instance.Header)}]; matched=true."
            });

            if (instance is not null)
            {
                if (matchedHeaders.TryGetValue(instanceId, out var matchedHeader)
                    && DatasetHeaderPartitioning.HasHeaderHashMismatch(matchedHeader.HeaderHash, instance.Header))
                {
                    var refreshedHeader = CreateHeaderIndex(instance);
                    await WriteJsonAsync(GetHeaderKey(instance.DatasetKey, refreshedHeader.State, refreshedHeader.AsOfDate, refreshedHeader.Id), refreshedHeader, cancellationToken);
                    headerFilesRebuilt += 1;
                    loadedFiles.Add(new DatasetLoadedFileTrace
                    {
                        FileName = ToDatasetTracePath(detailKey),
                        Reason = "Header hash mismatch detected; rebuilt header index from detail payload."
                    });

                    if (!HeaderIndexMatches(refreshedHeader, minAsOfDate, maxAsOfDate, normalizedState, normalizedCriteria))
                    {
                        continue;
                    }
                }

                resultInstances.Add(instance);
            }
        }

        return new DatasetReadTrace
        {
            Instances = resultInstances,
            LoadedFilenames = loadedFiles,
            HeaderFilesRead = headerFilesRead,
            HeaderFilesTotal = headerKeys.Count,
            DetailFilesRead = detailFilesRead,
            DetailFilesTotal = GetDetailFilesTotal(),
            CandidateHeaderFilesConsidered = candidateHeaderKeys.Count,
            MatchedInstanceFileCount = resultInstances.Count,
            HeaderFilesRebuilt = headerFilesRebuilt,
            UsedFilteredSearchPath = true
        };
    }

    private async Task<DatasetReadTrace> ReadHeaderOnlyWithoutFiltersAsync(
        List<string> headerKeys,
        IReadOnlyDictionary<string, string> normalizedCriteria,
        Func<Task<Dictionary<Guid, string>>> ensureDetailKeysByIdAsync,
        Func<int> getDetailFilesTotal,
        CancellationToken cancellationToken)
    {
        var resultInstances = new List<DatasetInstance>();
        var loaded = new List<DatasetLoadedFileTrace>();
        var detailFilesRead = 0;
        var headerFilesRebuilt = 0;

        foreach (var headerKey in headerKeys)
        {
            var header = await ReadJsonAsync<DatasetInstanceHeaderIndex?>(headerKey, null, cancellationToken);
            if (header is null)
            {
                continue;
            }

            if (RequiresSummaryBackfill(header)
                && TryParseHeaderKey(headerKey, out _, out _, out var instanceId)
                && (await ensureDetailKeysByIdAsync()).TryGetValue(instanceId, out var detailKey))
            {
                var instance = await ReadJsonAsync<DatasetInstance?>(detailKey, null, cancellationToken);
                detailFilesRead += 1;
                loaded.Add(new DatasetLoadedFileTrace
                {
                    FileName = ToDatasetTracePath(detailKey),
                    Reason = "Header index missing summary fields; read instance file to rebuild header index metadata."
                });

                if (instance is not null)
                {
                    var refreshedHeader = CreateHeaderIndex(instance);
                    await WriteJsonAsync(GetHeaderKey(instance.DatasetKey, refreshedHeader.State, refreshedHeader.AsOfDate, refreshedHeader.Id), refreshedHeader, cancellationToken);
                    headerFilesRebuilt += 1;
                    header = refreshedHeader;
                }
            }

            var isMatch = HeaderIndexMatches(header, null, null, null, normalizedCriteria);
            loaded.Add(new DatasetLoadedFileTrace
            {
                FileName = ToDatasetTracePath(headerKey),
                Reason = $"Header candidate evaluated against filters. lookedFor=[{FormatTraceSearchCriteria(null, null, null, normalizedCriteria)}]; found=[{FormatHeaderIndexForTrace(header)}]; matched={isMatch.ToString().ToLowerInvariant()}."
            });

            if (isMatch)
            {
                resultInstances.Add(ToHeaderOnlyInstance(header));
            }
        }

        return new DatasetReadTrace
        {
            Instances = resultInstances,
            LoadedFilenames = loaded,
            HeaderFilesRead = headerKeys.Count,
            HeaderFilesTotal = headerKeys.Count,
            DetailFilesRead = detailFilesRead,
            DetailFilesTotal = getDetailFilesTotal(),
            CandidateHeaderFilesConsidered = headerKeys.Count,
            MatchedInstanceFileCount = resultInstances.Count,
            HeaderFilesRebuilt = headerFilesRebuilt,
            UsedFilteredSearchPath = false
        };
    }

    private async Task<List<string>> GetCandidateHeaderKeysAsync(
        string normalizedDataset,
        List<string> allHeaderKeys,
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? normalizedState,
        CancellationToken cancellationToken)
    {
        // For bounded ranges, list only matching state/date partition prefixes.
        if (minAsOfDate.HasValue && maxAsOfDate.HasValue)
        {
            var spanDays = maxAsOfDate.Value.DayNumber - minAsOfDate.Value.DayNumber;
            if (spanDays >= 0 && spanDays <= 400)
            {
                return await ListCandidateHeaderKeysByRangePrefixesAsync(
                    normalizedDataset,
                    allHeaderKeys,
                    minAsOfDate.Value,
                    maxAsOfDate.Value,
                    normalizedState,
                    cancellationToken);
            }
        }

        return GetCandidateHeaderKeysByFiltering(allHeaderKeys, minAsOfDate, maxAsOfDate, normalizedState);
    }

    private async Task<List<string>> ListCandidateHeaderKeysByRangePrefixesAsync(
        string normalizedDataset,
        List<string> allHeaderKeys,
        DateOnly minAsOfDate,
        DateOnly maxAsOfDate,
        string? normalizedState,
        CancellationToken cancellationToken)
    {
        if (normalizedState is null)
        {
            return GetCandidateHeaderKeysByFiltering(allHeaderKeys, minAsOfDate, maxAsOfDate, normalizedState);
        }

        var stateTokens = new List<string> { normalizedState };

        var keys = new List<string>();
        for (var day = minAsOfDate.DayNumber; day <= maxAsOfDate.DayNumber; day++)
        {
            var date = DateOnly.FromDayNumber(day);
            var dateToken = DatasetHeaderPartitioning.DatePartitionToken(date);

            foreach (var stateToken in stateTokens)
            {
                var prefix = BuildKey($"instances/{normalizedDataset}/{DatasetHeaderPartitioning.HeadersFolderName}/{stateToken}/{dateToken}/");
                var matchingKeys = await ListKeysByPrefixAsync(prefix, cancellationToken);
                keys.AddRange(matchingKeys.Where(k => k.EndsWith(HeaderFileSuffix, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return keys
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private List<string> GetCandidateHeaderKeysByFiltering(List<string> allHeaderKeys, DateOnly? minAsOfDate, DateOnly? maxAsOfDate, string? normalizedState)
    {
        return allHeaderKeys
            .Where(key =>
            {
                if (!TryParseHeaderKey(key, out var stateToken, out var date, out _))
                {
                    return false;
                }

                return DatasetHeaderPartitioning.IsCandidatePartition(date, stateToken, minAsOfDate, maxAsOfDate, normalizedState);
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task DeleteAllHeaderIndexesForInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
    {
        var keys = await GetHeaderIndexKeysForInstanceAsync(datasetKey, instanceId, cancellationToken);
        foreach (var key in keys)
        {
            await DeleteIfExistsAsync(key, cancellationToken);
        }
    }

    private async Task SaveInstanceUnlockedAsync(DatasetInstance instance, CancellationToken cancellationToken)
    {
        var header = CreateHeaderIndex(instance);
        var targetHeaderKey = GetHeaderKey(instance.DatasetKey, header.State, header.AsOfDate, instance.Id);
        var existingHeaderKeys = await GetHeaderIndexKeysForInstanceAsync(instance.DatasetKey, instance.Id, cancellationToken);

        foreach (var existingKey in existingHeaderKeys.Where(k => !string.Equals(k, targetHeaderKey, StringComparison.OrdinalIgnoreCase)))
        {
            await DeleteIfExistsAsync(existingKey, cancellationToken);
        }

        await WriteJsonAsync(GetInstanceKey(instance.DatasetKey, instance.Id), instance, cancellationToken);

        if (existingHeaderKeys.Any(k => string.Equals(k, targetHeaderKey, StringComparison.OrdinalIgnoreCase)))
        {
            var existingHeader = await ReadJsonAsync<DatasetInstanceHeaderIndex?>(targetHeaderKey, null, cancellationToken);
            if (existingHeader is not null && HeaderIndexEquals(existingHeader, header))
            {
                return;
            }
        }

        await WriteJsonAsync(targetHeaderKey, header, cancellationToken);
    }

    private async Task<List<string>> GetHeaderIndexKeysForInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
    {
        var prefix = BuildKey($"instances/{EncodeFileKey(datasetKey)}/headers/");
        var keys = await ListKeysByPrefixAsync(prefix, cancellationToken);
        var suffix = $"/{instanceId:N}{HeaderFileSuffix}";

        var result = keys
            .Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var legacyKey = BuildKey($"instances/{EncodeFileKey(datasetKey)}/{instanceId:N}{HeaderFileSuffix}");
        if (!result.Contains(legacyKey, StringComparer.OrdinalIgnoreCase) && await ObjectExistsAsync(legacyKey, cancellationToken))
        {
            result.Add(legacyKey);
        }

        return result;
    }

    private static DatasetInstanceHeaderIndex CreateHeaderIndex(DatasetInstance instance)
    {
        var normalizedHeader = DatasetHeaderPartitioning.ToIndexHeader(instance.Header);
        return new DatasetInstanceHeaderIndex
        {
            Id = instance.Id,
            DatasetKey = instance.DatasetKey,
            AsOfDate = instance.AsOfDate,
            State = instance.State,
            Header = normalizedHeader,
            HeaderHash = DatasetHeaderPartitioning.ComputeHeaderHash(normalizedHeader),
            Version = instance.Version,
            CreatedBy = instance.CreatedBy,
            CreatedAtUtc = instance.CreatedAtUtc,
            LastModifiedBy = instance.LastModifiedBy,
            LastModifiedAtUtc = instance.LastModifiedAtUtc
        };
    }

    private static bool HeaderIndexEquals(DatasetInstanceHeaderIndex left, DatasetInstanceHeaderIndex right)
    {
        if (left.Id != right.Id
            || !string.Equals(left.DatasetKey, right.DatasetKey, StringComparison.Ordinal)
            || left.AsOfDate != right.AsOfDate
            || left.State != right.State
            || !string.Equals(left.HeaderHash, right.HeaderHash, StringComparison.Ordinal)
            || left.Version != right.Version
            || !string.Equals(left.CreatedBy, right.CreatedBy, StringComparison.Ordinal)
            || left.CreatedAtUtc != right.CreatedAtUtc
            || !string.Equals(left.LastModifiedBy, right.LastModifiedBy, StringComparison.Ordinal)
            || left.LastModifiedAtUtc != right.LastModifiedAtUtc)
        {
            return false;
        }

        if (left.Header.Count != right.Header.Count)
        {
            return false;
        }

        foreach (var pair in left.Header)
        {
            if (!right.Header.TryGetValue(pair.Key, out var rightValue)
                || !string.Equals(pair.Value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static DatasetInstance ToHeaderOnlyInstance(DatasetInstanceHeaderIndex header)
    {
        return new DatasetInstance
        {
            Id = header.Id,
            DatasetKey = header.DatasetKey,
            AsOfDate = header.AsOfDate,
            State = header.State,
            Version = header.Version,
            Header = header.Header.ToDictionary(
                x => x.Key,
                x => (object?)x.Value,
                StringComparer.OrdinalIgnoreCase),
            Rows = [],
            CreatedBy = header.CreatedBy,
            CreatedAtUtc = header.CreatedAtUtc,
            LastModifiedBy = string.IsNullOrWhiteSpace(header.LastModifiedBy) ? "unknown" : header.LastModifiedBy,
            LastModifiedAtUtc = header.LastModifiedAtUtc == default ? DateTimeOffset.UnixEpoch : header.LastModifiedAtUtc
        };
    }

    private static bool RequiresSummaryBackfill(DatasetInstanceHeaderIndex header)
    {
        return header.Version <= 0
            || string.IsNullOrWhiteSpace(header.LastModifiedBy)
            || header.LastModifiedAtUtc == default;
    }

    private static bool HeaderIndexMatches(
        DatasetInstanceHeaderIndex headerIndex,
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? normalizedState,
        IReadOnlyDictionary<string, string> normalizedHeaderCriteria)
    {
        return DatasetHeaderPartitioning.HeaderIndexMatches(
            headerIndex.AsOfDate,
            headerIndex.State,
            headerIndex.Header,
            minAsOfDate,
            maxAsOfDate,
            normalizedState,
            normalizedHeaderCriteria);
    }

    private static string? NormalizeStateFilter(string? state)
    {
        return DatasetHeaderPartitioning.NormalizeStateFilter(state);
    }

    private static string FormatTraceSearchCriteria(
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? normalizedState,
        IReadOnlyDictionary<string, string> normalizedHeaderCriteria)
    {
        var minAsOfToken = minAsOfDate.HasValue ? minAsOfDate.Value.ToString("yyyy-MM-dd") : "<any>";
        var maxAsOfToken = maxAsOfDate.HasValue ? maxAsOfDate.Value.ToString("yyyy-MM-dd") : "<any>";
        var stateToken = string.IsNullOrWhiteSpace(normalizedState) ? "<any>" : normalizedState;
        var criteriaToken = normalizedHeaderCriteria.Count == 0
            ? "<none>"
            : string.Join(", ", normalizedHeaderCriteria
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.Key}='{x.Value}'"));

        return $"minAsOfDate={minAsOfToken}; maxAsOfDate={maxAsOfToken}; state={stateToken}; headerCriteria={criteriaToken}";
    }

    private static string FormatHeaderIndexForTrace(DatasetInstanceHeaderIndex? headerIndex)
    {
        if (headerIndex is null)
        {
            return "<header index missing or unreadable>";
        }

        var stateToken = DatasetHeaderPartitioning.NormalizeStateToken(headerIndex.State);
        var headerToken = headerIndex.Header.Count == 0
            ? "<none>"
            : string.Join(", ", headerIndex.Header
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{x.Key}='{x.Value}'"));

        return $"asOfDate={headerIndex.AsOfDate:yyyy-MM-dd}; state={stateToken}; header={headerToken}";
    }

    private static string FormatInstanceHeaderForTrace(IDictionary<string, object?> header)
    {
        if (header.Count == 0)
        {
            return "<none>";
        }

        return string.Join(", ", header
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}='{x.Value?.ToString() ?? string.Empty}'"));
    }

    private string GetSchemaKey(string datasetKey)
        => BuildKey($"schemas/{EncodeFileKey(datasetKey)}.json");

    private string GetLegacyAuditKey()
        => BuildKey("audit-events.json");

    private string GetAuditRootPrefix()
        => BuildKey("audit/");

    private string GetAuditDatasetPrefix(string datasetKey)
        => BuildKey($"audit/{EncodeFileKey(datasetKey)}/");

    private string GetAuditEventKey(AuditEvent auditEvent)
    {
        var datasetFolder = string.IsNullOrWhiteSpace(auditEvent.DatasetKey)
            ? "UNKNOWN"
            : EncodeFileKey(auditEvent.DatasetKey);
        var actionToken = string.IsNullOrWhiteSpace(auditEvent.Action)
            ? "UNKNOWN_ACTION"
            : EncodeFileKey(auditEvent.Action);
        var timestamp = auditEvent.OccurredAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture);
        return BuildKey($"audit/{datasetFolder}/{timestamp}_{actionToken}_{auditEvent.Id:N}.json");
    }

    private string GetInstanceKey(string datasetKey, Guid instanceId)
        => BuildKey($"instances/{EncodeFileKey(datasetKey)}/{instanceId:N}.json");

    private string GetHeaderKey(string datasetKey, string state, DateOnly asOfDate, Guid instanceId)
        => BuildKey($"instances/{EncodeFileKey(datasetKey)}/{DatasetHeaderPartitioning.HeadersFolderName}/{DatasetHeaderPartitioning.NormalizeStateToken(state)}/{DatasetHeaderPartitioning.DatePartitionToken(asOfDate)}/{instanceId:N}{HeaderFileSuffix}");

    private string BuildKey(string relative)
    {
        var prefix = string.Equals(_storageProvider, StorageProviders.S3, StringComparison.OrdinalIgnoreCase)
            ? (_s3.Prefix ?? string.Empty).Trim('/')
            : string.Empty;
        var suffix = relative.Trim('/');
        return string.IsNullOrWhiteSpace(prefix)
            ? suffix
            : $"{prefix}/{suffix}";
    }

    private async Task<List<string>> ListKeysByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        ApiRequestIoStats.IncrementQuery();
        var keys = await _blobStore.QueryBlobsAsync($"{prefix}*", cancellationToken);
        return keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
    }

    private async Task<bool> ObjectExistsAsync(string key, CancellationToken cancellationToken)
    {
        return await _blobStore.BlobExistsAsync(key, cancellationToken);
    }

    private async Task<T> ReadJsonAsync<T>(string key, T defaultValue, CancellationToken cancellationToken)
    {
        var blob = await _blobStore.GetBlobAsync(key, cancellationToken);
        if (blob is null)
        {
            return defaultValue;
        }

        ApiRequestIoStats.IncrementRead(key);

        await using var stream = blob;
        var model = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return model ?? defaultValue;
    }

    private async Task WriteJsonAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        ApiRequestIoStats.IncrementWrite(key);
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        stream.Position = 0;
        await _blobStore.PutBlobAsync(key, stream, cancellationToken);
    }

    private async Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
    {
        ApiRequestIoStats.IncrementDelete(key);
        await _blobStore.DeleteBlobAsync(key, cancellationToken);
    }

    private bool TryParseHeaderKey(string key, out string stateToken, out DateOnly date, out Guid instanceId)
    {
        stateToken = string.Empty;
        date = default;
        instanceId = default;

        var normalizedPrefix = BuildKey("instances/");
        if (!key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(HeaderFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = key.Split('/');
        var headersIndex = Array.FindIndex(parts, p => string.Equals(p, "headers", StringComparison.OrdinalIgnoreCase));
        if (headersIndex < 0 || headersIndex + 3 >= parts.Length)
        {
            return false;
        }

        stateToken = parts[headersIndex + 1];
        var dateText = parts[headersIndex + 2];
        var fileName = parts[headersIndex + 3];

        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", out date))
        {
            return false;
        }

        var idText = fileName[..^HeaderFileSuffix.Length];
        return Guid.TryParseExact(idText, "N", out instanceId);
    }

    private bool IsDetailInstanceKey(string key)
    {
        if (!key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || key.EndsWith(HeaderFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (key.Contains("/headers/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(key);
        return Guid.TryParseExact(fileName, "N", out _);
    }

    private static string EncodeFileKey(string datasetKey)
    {
        return Uri.EscapeDataString(datasetKey.Trim().ToUpperInvariant());
    }

    private static string ToDatasetRelativePath(string datasetPrefix, string fullKey)
    {
        if (fullKey.StartsWith(datasetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return fullKey[datasetPrefix.Length..];
        }

        return fullKey;
    }

    private static string ToDatasetTracePath(string fullKey)
    {
        var normalized = fullKey.Replace('\\', '/');
        var marker = "/instances/";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        string pathFromInstances;

        if (index >= 0)
        {
            pathFromInstances = normalized[(index + 1)..];
        }
        else if (normalized.StartsWith("instances/", StringComparison.OrdinalIgnoreCase))
        {
            pathFromInstances = normalized;
        }
        else
        {
            pathFromInstances = normalized;
        }

        return "\\" + pathFromInstances.Replace('/', '\\');
    }

    private sealed record DatasetInstanceHeaderIndex
    {
        public Guid Id { get; init; }
        public string DatasetKey { get; init; } = string.Empty;
        public DateOnly AsOfDate { get; init; }
        public string State { get; init; } = string.Empty;
        public Dictionary<string, string> Header { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string? HeaderHash { get; init; }
        public int Version { get; init; }
        public string? CreatedBy { get; init; }
        public DateTimeOffset? CreatedAtUtc { get; init; }
        public string LastModifiedBy { get; init; } = string.Empty;
        public DateTimeOffset LastModifiedAtUtc { get; init; }
    }

    private sealed record ParsedHeaderKey
    {
        public string Key { get; init; } = string.Empty;
        public string StateToken { get; init; } = string.Empty;
        public DateOnly Date { get; init; }
        public Guid InstanceId { get; init; }
    }
}
