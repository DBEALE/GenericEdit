using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Domain.Models;
using Microsoft.Extensions.Options;

namespace DatasetPlatform.Api.Tests;

public sealed class BlobDataRepositoryTests
{
    [Fact]
    public async Task GetInstancesWithTrace_HeaderOnly_ShouldAvoidDetailReads()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "dataset-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var storageOptions = Options.Create(new StorageOptions
            {
                BasePath = tempPath
            });
            var repository = new BlobDataRepository(new FileSystemBlobStore(storageOptions), storageOptions);

            var first = CreateInstance(
                datasetKey: "FX_RATES",
                asOfDate: new DateOnly(2022, 4, 3),
                state: "Draft",
                version: 1,
                header: new Dictionary<string, object?>
                {
                    ["region"] = "Europe",
                    ["entity"] = "HBEU"
                });

            var second = CreateInstance(
                datasetKey: "FX_RATES",
                asOfDate: new DateOnly(2022, 4, 4),
                state: "Official",
                version: 2,
                header: new Dictionary<string, object?>
                {
                    ["region"] = "Europe",
                    ["entity"] = "HBEU"
                });

            await repository.SaveInstanceAsync(first, CancellationToken.None);
            await repository.SaveInstanceAsync(second, CancellationToken.None);

            var trace = await repository.GetInstancesWithTraceAsync(
                datasetKey: "FX_RATES",
                cancellationToken: CancellationToken.None,
                minAsOfDate: new DateOnly(2022, 4, 3),
                maxAsOfDate: new DateOnly(2022, 4, 4),
                includeDetails: false);

            Assert.Equal(2, trace.Instances.Count);
            Assert.Equal(0, trace.DetailFilesRead);
            Assert.Equal(2, trace.HeaderFilesRead);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetInstancesWithTrace_LatestUpTo_ShouldNotReportMissingHeaderIndex_WhenAllHeadersExist()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "dataset-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var storageOptions = Options.Create(new StorageOptions
            {
                BasePath = tempPath
            });
            var repository = new BlobDataRepository(new FileSystemBlobStore(storageOptions), storageOptions);

            var older = CreateInstance(
                datasetKey: "FX_RATES",
                asOfDate: new DateOnly(2022, 4, 4),
                state: "Draft",
                version: 1,
                header: new Dictionary<string, object?>
                {
                    ["region"] = "Europe",
                    ["entity"] = "HBFR"
                });

            var latest = CreateInstance(
                datasetKey: "FX_RATES",
                asOfDate: new DateOnly(2022, 4, 18),
                state: "Draft",
                version: 1,
                header: new Dictionary<string, object?>
                {
                    ["region"] = "Europe",
                    ["entity"] = "HBEU"
                });

            await repository.SaveInstanceAsync(older, CancellationToken.None);
            await repository.SaveInstanceAsync(latest, CancellationToken.None);

            var headerCriteria = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["region"] = "Europe",
                ["entity"] = "HBEU"
            };

            var trace = await repository.GetInstancesWithTraceAsync(
                datasetKey: "FX_RATES",
                cancellationToken: CancellationToken.None,
                minAsOfDate: new DateOnly(2022, 4, 18),
                maxAsOfDate: new DateOnly(2022, 4, 18),
                state: "Draft",
                headerCriteria: headerCriteria);

            var only = Assert.Single(trace.Instances);
            Assert.Equal(latest.Id, only.Id);
            Assert.Equal(0, trace.HeaderFilesRebuilt);

            Assert.DoesNotContain(
                trace.LoadedFilenames,
                x => string.Equals(
                    x.Reason,
                    "Header index missing; read instance file to rebuild header index.",
                    StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    private static DatasetInstance CreateInstance(
        string datasetKey,
        DateOnly asOfDate,
        string state,
        int version,
        IDictionary<string, object?> header)
    {
        var now = DateTimeOffset.UtcNow;
        return new DatasetInstance
        {
            Id = Guid.NewGuid(),
            DatasetKey = datasetKey,
            AsOfDate = asOfDate,
            State = state,
            Version = version,
            Header = new Dictionary<string, object?>(header, StringComparer.OrdinalIgnoreCase),
            Rows =
            [
                new Dictionary<string, object?>
                {
                    ["sourceCurrency"] = "EUR",
                    ["targetCurrency"] = "USD",
                    ["rate"] = "1.10"
                }
            ],
            CreatedBy = "test",
            CreatedAtUtc = now,
            LastModifiedBy = "test",
            LastModifiedAtUtc = now
        };
    }
}

