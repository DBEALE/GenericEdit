using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Application.Dtos;

/// <summary>
/// Diagnostic information returned alongside query results when <c>includeInternalInfo=true</c>
/// is passed in the request. Useful for debugging storage access patterns and performance.
/// </summary>
public sealed class DatasetInternalInfo
{
    /// <summary>Files that were opened during the query, each with a reason explaining why.</summary>
    public IReadOnlyList<DatasetLoadedFileInfo> LoadedFilenames { get; init; } = [];

    /// <summary>Counters summarising how efficiently the query read the underlying storage.</summary>
    public DatasetSearchEfficiencyStats? SearchEfficiency { get; init; }
}

/// <summary>A single file access record within a traced dataset query.</summary>
public sealed class DatasetLoadedFileInfo
{
    /// <summary>Relative path of the file within the storage root.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Human-readable reason this file was accessed (e.g. "Matched date partition filter").</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Storage I/O counters for a single dataset query. A high ratio of files read to files total
/// indicates the query could not use partitioned indexes and performed a full scan.
/// Returned as part of <see cref="DatasetInternalInfo"/>.
/// </summary>
public sealed class DatasetSearchEfficiencyStats
{
    /// <summary>Number of header index files actually opened.</summary>
    public int HeaderFilesRead { get; init; }

    /// <summary>Total number of header index files available for this dataset.</summary>
    public int HeaderFilesTotal { get; init; }

    /// <summary>Number of full detail files opened (only when <c>includeDetails=true</c>).</summary>
    public int DetailFilesRead { get; init; }

    /// <summary>Total number of detail files available for this dataset.</summary>
    public int DetailFilesTotal { get; init; }

    /// <summary>Number of header partitions evaluated before date/state filtering was applied.</summary>
    public int CandidateHeaderFilesConsidered { get; init; }

    /// <summary>Number of instances returned after all filters were applied.</summary>
    public int MatchedInstanceFileCount { get; init; }

    /// <summary>Number of stale header index files that were rebuilt during this query.</summary>
    public int HeaderFilesRebuilt { get; init; }

    /// <summary><c>true</c> when the query used partition-aware filtering; <c>false</c> for a full scan.</summary>
    public bool UsedFilteredSearchPath { get; init; }
}

/// <summary>
/// Response envelope for GET /api/datasets/{datasetKey}/instances.
/// Contains the matched instances and optional internal diagnostic info.
/// </summary>
public sealed class DatasetInstancesQueryResponse
{
    /// <summary>Matched dataset instances, sorted by asOfDate and version descending.</summary>
    public IReadOnlyList<DatasetInstance> Items { get; init; } = [];

    /// <summary>Storage diagnostic info. Only present when <c>includeInternalInfo=true</c> was requested.</summary>
    public DatasetInternalInfo? InternalInfo { get; init; }
}

/// <summary>
/// Lightweight summary of a dataset instance containing header fields but no detail rows.
/// Returned by GET /api/datasets/{datasetKey}/headers for faster list views.
/// </summary>
public sealed class DatasetHeaderSummary
{
    /// <summary>Unique identifier of the underlying instance.</summary>
    public Guid Id { get; init; }

    /// <summary>Dataset this instance belongs to.</summary>
    public string DatasetKey { get; init; } = string.Empty;

    /// <summary>Business date the snapshot applies to.</summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>Current lifecycle state label.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Edit counter — increments on each update.</summary>
    public int Version { get; init; }

    /// <summary>Header key-value pairs (no detail rows).</summary>
    public Dictionary<string, object?> Header { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>User who originally created this instance.</summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>UTC timestamp of creation.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>User who last modified or signed off this instance.</summary>
    public string LastModifiedBy { get; init; } = string.Empty;

    /// <summary>UTC timestamp of the most recent modification.</summary>
    public DateTimeOffset LastModifiedAtUtc { get; init; }
}

/// <summary>Response envelope for GET /api/datasets/{datasetKey}/headers.</summary>
public sealed class DatasetHeadersQueryResponse
{
    /// <summary>Matched header summaries, sorted by asOfDate and version descending.</summary>
    public IReadOnlyList<DatasetHeaderSummary> Items { get; init; } = [];

    /// <summary>Storage diagnostic info. Only present when <c>includeInternalInfo=true</c> was requested.</summary>
    public DatasetInternalInfo? InternalInfo { get; init; }
}

/// <summary>Response envelope for GET /api/datasets/{datasetKey}/instances/latest.</summary>
public sealed class DatasetLatestInstanceQueryResponse
{
    /// <summary>The most recent matching instance, or <c>null</c> if none was found.</summary>
    public DatasetInstance? Item { get; init; }

    /// <summary>Storage diagnostic info. Only present when <c>includeInternalInfo=true</c> was requested.</summary>
    public DatasetInternalInfo? InternalInfo { get; init; }
}
