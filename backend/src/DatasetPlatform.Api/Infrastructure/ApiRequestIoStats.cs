using System.Threading;

namespace DatasetPlatform.Api.Infrastructure;

public static class ApiRequestIoStats
{
    private static readonly AsyncLocal<MutableIoStats?> Current = new();

    private enum FileKind
    {
        Detail,
        Header,
        Other
    }

    public static void BeginRequest()
    {
        Current.Value = new MutableIoStats();
    }

    public static IoStatsSnapshot EndRequest()
    {
        var snapshot = Current.Value?.ToSnapshot() ?? new IoStatsSnapshot();
        Current.Value = null;
        return snapshot;
    }

    public static void IncrementRead(string key)
    {
        var current = Current.Value;
        if (current is null)
        {
            return;
        }

        switch (ClassifyKey(key))
        {
            case FileKind.Header:
                current.HeaderFilesRead += 1;
                return;
            case FileKind.Detail:
                current.DetailFilesRead += 1;
                return;
            default:
                current.OtherFilesRead += 1;
                return;
        }
    }

    public static void IncrementWrite(string key)
    {
        var current = Current.Value;
        if (current is null)
        {
            return;
        }

        switch (ClassifyKey(key))
        {
            case FileKind.Header:
                current.HeaderFilesWritten += 1;
                return;
            case FileKind.Detail:
                current.DetailFilesWritten += 1;
                return;
            default:
                current.OtherFilesWritten += 1;
                return;
        }
    }

    public static void IncrementDelete(string key)
    {
        var current = Current.Value;
        if (current is null)
        {
            return;
        }

        switch (ClassifyKey(key))
        {
            case FileKind.Header:
                current.HeaderFilesDeleted += 1;
                return;
            case FileKind.Detail:
                current.DetailFilesDeleted += 1;
                return;
            default:
                current.OtherFilesDeleted += 1;
                return;
        }
    }

    public static void IncrementQuery()
    {
        var current = Current.Value;
        if (current is null)
        {
            return;
        }

        current.BlobQueries += 1;
    }

    private static FileKind ClassifyKey(string key)
    {
        if (key.EndsWith(DatasetHeaderPartitioning.HeaderFileSuffix, StringComparison.OrdinalIgnoreCase)
            || key.Contains($"/{DatasetHeaderPartitioning.HeadersFolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            return FileKind.Header;
        }

        if (key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !key.Contains($"/{DatasetHeaderPartitioning.HeadersFolderName}/", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(Path.GetFileNameWithoutExtension(key), "N", out _))
        {
            return FileKind.Detail;
        }

        return FileKind.Other;
    }

    private sealed class MutableIoStats
    {
        public int DetailFilesRead { get; set; }
        public int HeaderFilesRead { get; set; }
        public int OtherFilesRead { get; set; }
        public int DetailFilesWritten { get; set; }
        public int HeaderFilesWritten { get; set; }
        public int OtherFilesWritten { get; set; }
        public int DetailFilesDeleted { get; set; }
        public int HeaderFilesDeleted { get; set; }
        public int OtherFilesDeleted { get; set; }
        public int BlobQueries { get; set; }

        public IoStatsSnapshot ToSnapshot()
        {
            return new IoStatsSnapshot
            {
                DetailFilesRead = DetailFilesRead,
                HeaderFilesRead = HeaderFilesRead,
                OtherFilesRead = OtherFilesRead,
                DetailFilesWritten = DetailFilesWritten,
                HeaderFilesWritten = HeaderFilesWritten,
                OtherFilesWritten = OtherFilesWritten,
                DetailFilesDeleted = DetailFilesDeleted,
                HeaderFilesDeleted = HeaderFilesDeleted,
                OtherFilesDeleted = OtherFilesDeleted,
                BlobQueries = BlobQueries
            };
        }
    }
}

public sealed class IoStatsSnapshot
{
    public int DetailFilesRead { get; init; }
    public int HeaderFilesRead { get; init; }
    public int OtherFilesRead { get; init; }
    public int DetailFilesWritten { get; init; }
    public int HeaderFilesWritten { get; init; }
    public int OtherFilesWritten { get; init; }
    public int DetailFilesDeleted { get; init; }
    public int HeaderFilesDeleted { get; init; }
    public int OtherFilesDeleted { get; init; }
    public int BlobQueries { get; init; }
}
