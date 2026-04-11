using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DatasetPlatform.Application.Services;

/// <summary>
/// Singleton in-memory cache for lookup dataset permissible values.
/// Each entry stores the values alongside the instance ID that produced them.
/// Invalidation is implicit: when the latest official instance changes, its ID changes,
/// causing a cache miss and a reload — no explicit invalidation needed.
/// </summary>
public sealed class LookupValueCache
{
    private readonly ConcurrentDictionary<string, (Guid InstanceId, int Version, IReadOnlyList<string> Values)> _entries
        = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string datasetKey, Guid currentInstanceId, int currentVersion, [NotNullWhen(true)] out IReadOnlyList<string>? values)
    {
        if (_entries.TryGetValue(datasetKey, out var entry)
            && entry.InstanceId == currentInstanceId
            && entry.Version == currentVersion)
        {
            values = entry.Values;
            return true;
        }
        values = null;
        return false;
    }

    public void Set(string datasetKey, Guid instanceId, int version, IReadOnlyList<string> values)
        => _entries[datasetKey] = (instanceId, version, values);
}
