using DatasetPlatform.Domain.Models;
using System.Security.Cryptography;
using System.Text;

namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// Static helpers for the header-index partitioning strategy used by <c>BlobDataRepository</c>.
///
/// <para>
/// <b>What is partitioning?</b>
/// Rather than storing all header summaries in a single file, the repository splits them
/// into per-(state, asOfDate) files called "header partitions". For example:
/// <c>instances/market-rates/headers/OFFICIAL/2024-12-01.header.json</c>
/// contains only the header fields of all Official instances whose asOfDate is 2024-12-01.
/// </para>
///
/// <para>
/// This means a query with a date range or state filter only needs to read the relevant
/// partition files rather than scanning every instance file. The trade-off is that header
/// index files must be kept in sync with instance writes, and can occasionally be stale
/// (they are rebuilt on mismatch).
/// </para>
///
/// <para>
/// <b>Hash integrity:</b> Each header entry in an index file stores a SHA-256 hash of the
/// header values. When a partition is read, the hash is recomputed and compared; if they
/// differ, the index is rebuilt from the underlying instance files.
/// </para>
/// </summary>
internal static class DatasetHeaderPartitioning
{
    /// <summary>File extension for header partition files (e.g. <c>2024-12-01.header.json</c>).</summary>
    public const string HeaderFileSuffix = ".header.json";

    /// <summary>Sub-folder name within the dataset's instance directory that holds partition files.</summary>
    public const string HeadersFolderName = "headers";

    // ─── State token normalisation ────────────────────────────────────────────

    /// <summary>
    /// Converts a state label into a canonical uppercase string token
    /// suitable for use in file paths (strips whitespace, underscores, and hyphens).
    /// Example: <c>PendingApproval → "PENDINGAPPROVAL"</c>.
    /// </summary>
    public static string NormalizeStateToken(string state)
    {
        return CanonicalToken(state);
    }

    /// <summary>
    /// Normalises a state filter string. Returns <c>null</c> (meaning "no filter") when
    /// the input is blank; otherwise returns the canonical uppercase token.
    /// </summary>
    public static string? NormalizeStateFilter(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return CanonicalToken(state);
    }

    /// <summary>Returns the date formatted as a partition file name token: <c>yyyy-MM-dd</c>.</summary>
    public static string DatePartitionToken(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    // ─── Partition candidacy ──────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the partition identified by <paramref name="partitionDate"/> and
    /// <paramref name="partitionStateToken"/> could contain instances that match the given filters.
    /// Used to skip reading partition files that cannot possibly contribute results.
    /// </summary>
    public static bool IsCandidatePartition(
        DateOnly partitionDate,
        string partitionStateToken,
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? normalizedState)
    {
        if (normalizedState is not null && !string.Equals(partitionStateToken, normalizedState, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (minAsOfDate.HasValue && partitionDate < minAsOfDate.Value)
        {
            return false;
        }

        if (maxAsOfDate.HasValue && partitionDate > maxAsOfDate.Value)
        {
            return false;
        }

        return true;
    }

    // ─── Latest-instance planning ─────────────────────────────────────────────

    /// <summary>
    /// Builds a prioritised search plan for a "get latest instance" query.
    /// Returns one plan per matching state, each listing candidate dates in descending order.
    /// The caller walks dates from newest to oldest and stops as soon as a match is found.
    /// </summary>
    /// <param name="availablePartitions">All known partitions for the dataset.</param>
    /// <param name="asOfDate">Upper bound — only partitions on or before this date are considered.</param>
    /// <param name="normalizedState">State filter, or <c>null</c> to consider all states.</param>
    public static IReadOnlyList<LatestUpStatePlan> BuildLatestUpStatePlans(
        IEnumerable<HeaderPartition> availablePartitions,
        DateOnly asOfDate,
        string? normalizedState)
    {
        var filtered = availablePartitions
            .Where(x => x.Date <= asOfDate)
            .Where(x => normalizedState is null || string.Equals(x.StateToken, normalizedState, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var states = normalizedState is not null
            ? [normalizedState]
            : filtered
                .Select(x => x.StateToken)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var plans = new List<LatestUpStatePlan>(states.Count);
        foreach (var stateToken in states)
        {
            var dates = filtered
                .Where(x => string.Equals(x.StateToken, stateToken, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Date)
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            if (dates.Count == 0)
            {
                continue;
            }

            plans.Add(new LatestUpStatePlan
            {
                StateToken = stateToken,
                DatesDescending = dates
            });
        }

        return plans;
    }

    // ─── Header filtering ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if every criterion in <paramref name="criteria"/> is satisfied by the
    /// given <paramref name="header"/>. Matching is case-insensitive substring search — a criterion
    /// value of "EUR" matches a header value of "EUR/USD".
    /// </summary>
    public static bool HeaderMatchesCriteria(IReadOnlyDictionary<string, string> header, IReadOnlyDictionary<string, string> criteria)
    {
        foreach (var criterion in criteria)
        {
            header.TryGetValue(criterion.Key, out var rawValue);
            var actual = rawValue ?? string.Empty;
            if (!actual.Contains(criterion.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // ─── Header index helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Converts an instance header (object values) to a string-only dictionary
    /// suitable for writing to a header index file.
    /// </summary>
    public static Dictionary<string, string> ToIndexHeader(IDictionary<string, object?> instanceHeader)
    {
        return instanceHeader.ToDictionary(
            x => x.Key,
            x => x.Value?.ToString() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hash of the header key-value pairs.
    /// Keys are sorted and normalised (trimmed, uppercased) before hashing so that
    /// the same logical header always produces the same hash regardless of insertion order.
    /// Used by the index integrity check to detect stale partitions.
    /// </summary>
    public static string ComputeHeaderHash(IReadOnlyDictionary<string, string> header)
    {
        var builder = new StringBuilder();
        foreach (var pair in header.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key.Trim().ToUpperInvariant());
            builder.Append('\0');
            builder.Append(pair.Value ?? string.Empty);
            builder.Append('\0');
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Returns <c>true</c> if the stored hash does not match a freshly computed hash of
    /// <paramref name="instanceHeader"/>, indicating the header index entry is stale.
    /// Returns <c>false</c> (no mismatch) if <paramref name="storedHash"/> is null or empty,
    /// treating missing hashes as valid (backwards compatibility with older index files).
    /// </summary>
    public static bool HasHeaderHashMismatch(string? storedHash, IDictionary<string, object?> instanceHeader)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var normalized = ToIndexHeader(instanceHeader);
        var computed = ComputeHeaderHash(normalized);
        return !string.Equals(storedHash, computed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips blank entries from <paramref name="headerCriteria"/> and returns a
    /// case-insensitive dictionary suitable for filtering. Returns an empty dictionary
    /// if the input is null.
    /// </summary>
    public static Dictionary<string, string> NormalizeHeaderCriteria(IReadOnlyDictionary<string, string>? headerCriteria)
    {
        if (headerCriteria is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return headerCriteria
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Combines all filter conditions to determine whether a header index entry is
    /// a candidate for inclusion in query results. All conditions must pass.
    /// </summary>
    public static bool HeaderIndexMatches(
        DateOnly headerAsOfDate,
        string headerState,
        IReadOnlyDictionary<string, string> header,
        DateOnly? minAsOfDate,
        DateOnly? maxAsOfDate,
        string? normalizedState,
        IReadOnlyDictionary<string, string> normalizedHeaderCriteria)
    {
        if (minAsOfDate.HasValue && headerAsOfDate < minAsOfDate.Value)
        {
            return false;
        }

        if (maxAsOfDate.HasValue && headerAsOfDate > maxAsOfDate.Value)
        {
            return false;
        }

        if (normalizedState is not null)
        {
            var actualState = NormalizeStateToken(headerState);
            if (!string.Equals(actualState, normalizedState, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return HeaderMatchesCriteria(header, normalizedHeaderCriteria);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Produces a canonical comparison token by stripping whitespace, underscores, and hyphens
    /// then uppercasing. Used to make state comparisons resilient to minor formatting differences
    /// (e.g. "Pending_Approval", "pending-approval", and "PendingApproval" all map to "PENDINGAPPROVAL").
    /// </summary>
    private static string CanonicalToken(string value)
    {
        return string.Concat(value.Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-'))
            .ToUpperInvariant();
    }

    // ─── Supporting records ───────────────────────────────────────────────────

    /// <summary>Identifies a single header partition by its state token and date.</summary>
    public sealed record HeaderPartition
    {
        /// <summary>Canonical state token (e.g. <c>"OFFICIAL"</c>).</summary>
        public string StateToken { get; init; } = string.Empty;

        /// <summary>The asOfDate this partition covers.</summary>
        public DateOnly Date { get; init; }
    }

    /// <summary>
    /// A search plan for one state during a "get latest instance" query.
    /// Dates are sorted newest-first so the caller can stop as soon as a match is found.
    /// </summary>
    public sealed record LatestUpStatePlan
    {
        /// <summary>Canonical state token for this plan.</summary>
        public string StateToken { get; init; } = string.Empty;

        /// <summary>Candidate dates to check, ordered newest to oldest.</summary>
        public IReadOnlyList<DateOnly> DatesDescending { get; init; } = [];
    }
}
