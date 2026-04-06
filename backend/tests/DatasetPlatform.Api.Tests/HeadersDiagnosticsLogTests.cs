using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Dtos;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DatasetPlatform.Api.Tests;

public sealed class HeadersDiagnosticsLogTests
{
    [Fact]
    public async Task HeadersQuery_WhenIncludeInternalInfoNotRequested_ShouldStillWriteUnavailableStatsLine()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dataset-platform-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dataPath = Path.Combine(tempRoot, "data");
        var logPath = Path.Combine(tempRoot, "apiDiag.log");

        await using var factory = new HeadersApiFactory(
            dataPath,
            logPath,
            verbosity: ApiDiagnosticsOptions.CompactVerbosity,
            logSearchEfficiencyStats: true);
        using var client = factory.CreateClient();

        try
        {
            await SeedDatasetAsync(factory.Services, CancellationToken.None);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/datasets/FX_RATES/headers?minAsOfDate=2026-03-05&maxAsOfDate=2026-04-05");
            request.Headers.Add("x-user-id", "viewer");
            request.Headers.Add("x-user-roles", "DatasetReaderRole");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var logText = await ReadFileWithRetryAsync(logPath, maxAttempts: 20, delayMs: 50);
            Assert.Contains("SearchEfficiencyTable:", logText, StringComparison.Ordinal);
            Assert.Matches("(?s)SearchEfficiencyTable:.*reason\\s+includeInternalInfo-not-requested", logText);
            Assert.Contains("StorageIoStatsTable:", logText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HeadersQuery_WhenLogSearchEfficiencyStatsEnabled_ShouldWriteStatsLine()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dataset-platform-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dataPath = Path.Combine(tempRoot, "data");
        var logPath = Path.Combine(tempRoot, "apiDiag.log");

        await using var factory = new HeadersApiFactory(
            dataPath,
            logPath,
            verbosity: ApiDiagnosticsOptions.CompactVerbosity,
            logSearchEfficiencyStats: true);
        using var client = factory.CreateClient();

        try
        {
            await SeedDatasetAsync(factory.Services, CancellationToken.None);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/datasets/FX_RATES/headers?minAsOfDate=2026-03-05&maxAsOfDate=2026-04-05&includeInternalInfo=true");
            request.Headers.Add("x-user-id", "viewer");
            request.Headers.Add("x-user-roles", "DatasetReaderRole");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var logText = await ReadFileWithRetryAsync(logPath, maxAttempts: 20, delayMs: 50);
            Assert.Contains("SearchEfficiencyTable:", logText, StringComparison.Ordinal);
            Assert.Matches("(?s)SearchEfficiencyTable:.*headerFilesRead", logText);
            Assert.Matches("(?s)SearchEfficiencyTable:.*detailFilesRead\\s+0", logText);
            Assert.Contains("StorageIoStatsTable:", logText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HeadersQuery_CompactDiagnostics_ShouldOmitPayloadBodies()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dataset-platform-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dataPath = Path.Combine(tempRoot, "data");
        var logPath = Path.Combine(tempRoot, "apiDiag.log");

        await using var factory = new HeadersApiFactory(dataPath, logPath, ApiDiagnosticsOptions.CompactVerbosity);
        using var client = factory.CreateClient();

        try
        {
            await SeedDatasetAsync(factory.Services, CancellationToken.None);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/datasets/FX_RATES/headers?minAsOfDate=2026-03-05&maxAsOfDate=2026-04-05&includeInternalInfo=true");
            request.Headers.Add("x-user-id", "viewer");
            request.Headers.Add("x-user-roles", "DatasetReaderRole");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var logText = await ReadFileWithRetryAsync(logPath, maxAttempts: 20, delayMs: 50);
            Assert.Contains("PayloadVerbosity: Compact", logText, StringComparison.Ordinal);
            Assert.Contains("<omitted; bytes=", logText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"items\"", logText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HeadersQuery_InternalInfoLog_ShouldReportZeroDetailReads()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dataset-platform-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dataPath = Path.Combine(tempRoot, "data");
        var logPath = Path.Combine(tempRoot, "apiDiag.log");

        await using var factory = new HeadersApiFactory(dataPath, logPath);
        using var client = factory.CreateClient();

        try
        {
            await SeedDatasetAsync(factory.Services, CancellationToken.None);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/datasets/FX_RATES/headers?minAsOfDate=2026-03-05&maxAsOfDate=2026-04-05&includeInternalInfo=true");
            request.Headers.Add("x-user-id", "viewer");
            request.Headers.Add("x-user-roles", "DatasetReaderRole");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var logText = await ReadFileWithRetryAsync(logPath, maxAttempts: 20, delayMs: 50);
            Assert.Contains("GET /api/datasets/FX_RATES/headers", logText, StringComparison.Ordinal);
            Assert.Contains("\"detailFilesRead\":0", logText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task SeedDatasetAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var datasetService = scope.ServiceProvider.GetRequiredService<IDatasetService>();

        var admin = new UserContext
        {
            UserId = "admin",
            Roles = [DatasetAuthorizer.DatasetAdminRole]
        };

        var schema = new DatasetSchema
        {
            Key = "FX_RATES",
            Name = "FX Rates",
            Description = "Integration test schema",
            HeaderFields =
            [
                new SchemaField { Name = "region", Label = "Region", Type = FieldType.String, Required = true },
                new SchemaField { Name = "entity", Label = "Entity", Type = FieldType.String, Required = true }
            ],
            DetailFields =
            [
                new SchemaField { Name = "currency", Label = "Currency", Type = FieldType.String, Required = true },
                new SchemaField { Name = "rate", Label = "Rate", Type = FieldType.Number, Required = true }
            ],
            Permissions = new DatasetPermissions
            {
                ReadRoles = ["DatasetReaderRole"],
                WriteRoles = ["DatasetWriterRole"],
                SignoffRoles = ["DatasetSignoffRole"]
            }
        };

        await datasetService.UpsertSchemaAsync(schema, admin, cancellationToken);

        var writer = new UserContext
        {
            UserId = "writer",
            Roles = ["DatasetWriterRole"]
        };

        await datasetService.CreateInstanceAsync(
            new CreateDatasetInstanceRequest
            {
                DatasetKey = "FX_RATES",
                AsOfDate = new DateOnly(2026, 4, 4),
                State = "Draft",
                Header = new Dictionary<string, object?>
                {
                    ["region"] = "Europe",
                    ["entity"] = "HBEU"
                },
                Rows =
                [
                    new Dictionary<string, object?>
                    {
                        ["currency"] = "USD",
                        ["rate"] = 1.11m
                    }
                ]
            },
            writer,
            cancellationToken);
    }

    [Fact]
    public async Task GetInstanceById_ShouldLogStorageIoDetailRead()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dataset-platform-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dataPath = Path.Combine(tempRoot, "data");
        var logPath = Path.Combine(tempRoot, "apiDiag.log");

        await using var factory = new HeadersApiFactory(
            dataPath,
            logPath,
            verbosity: ApiDiagnosticsOptions.CompactVerbosity,
            logSearchEfficiencyStats: true);
        using var client = factory.CreateClient();

        try
        {
            await SeedDatasetAsync(factory.Services, CancellationToken.None);

            using var listRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/datasets/FX_RATES/headers?minAsOfDate=2026-03-05&maxAsOfDate=2026-04-05");
            listRequest.Headers.Add("x-user-id", "viewer");
            listRequest.Headers.Add("x-user-roles", "DatasetReaderRole");

            var listResponse = await client.SendAsync(listRequest);
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var listRaw = await listResponse.Content.ReadAsStringAsync();
            using var listDoc = System.Text.Json.JsonDocument.Parse(listRaw);
            var items = listDoc.RootElement;
            Assert.Equal(System.Text.Json.JsonValueKind.Array, items.ValueKind);
            Assert.Equal(1, items.GetArrayLength());
            var first = items[0];
            var instanceId = first.GetProperty("id").GetGuid();

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/datasets/FX_RATES/instances/{instanceId}");
            request.Headers.Add("x-user-id", "viewer");
            request.Headers.Add("x-user-roles", "DatasetReaderRole");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var logText = await ReadFileWithRetryAsync(logPath, maxAttempts: 20, delayMs: 50);
            Assert.Contains("GET /api/datasets/FX_RATES/instances/", logText, StringComparison.Ordinal);
            Assert.Contains("StorageIoStatsTable:", logText, StringComparison.Ordinal);
            Assert.Matches("(?s)StorageIoStatsTable:.*detailFilesRead\\s+1", logText);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task<string> ReadFileWithRetryAsync(string path, int maxAttempts, int delayMs)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path, Encoding.UTF8);
            }

            await Task.Delay(delayMs);
        }

        throw new Xunit.Sdk.XunitException($"Expected diagnostics log file was not created: {path}");
    }

    private sealed class HeadersApiFactory(
        string storagePath,
        string logPath,
        string verbosity = ApiDiagnosticsOptions.FullVerbosity,
        bool logSearchEfficiencyStats = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:Provider"] = "LocalFile",
                    ["Storage:BasePath"] = storagePath,
                    ["ApiDebug:Enabled"] = "true",
                    ["ApiDebug:LogFilePath"] = logPath,
                    ["ApiDebug:Verbosity"] = verbosity,
                    ["ApiDebug:LogSearchEfficiencyStats"] = logSearchEfficiencyStats.ToString().ToLowerInvariant()
                });
            });
        }
    }
}

