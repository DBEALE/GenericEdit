using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DatasetPlatform.Api.Infrastructure;

public sealed class ApiDiagnosticsMiddleware(
    RequestDelegate next,
    IOptions<ApiDiagnosticsOptions> options,
    IWebHostEnvironment environment)
{
    private static readonly SemaphoreSlim FileWriteLock = new(1, 1);
    private readonly RequestDelegate _next = next;
    private readonly ApiDiagnosticsOptions _options = options.Value;
    private readonly IWebHostEnvironment _environment = environment;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !IsApiRequest(context.Request.Path))
        {
            await _next(context);
            return;
        }

        ApiRequestIoStats.BeginRequest();

        var capturePayloadBodies = !_options.IsCompactVerbosity();
        var captureResponseBody = capturePayloadBodies;
        var requestBody = capturePayloadBodies
            ? await ReadRequestBodyAsync(context.Request, context.RequestAborted)
            : string.Empty;
        var requestBodyBytes = capturePayloadBodies
            ? Encoding.UTF8.GetByteCount(requestBody)
            : (int?)context.Request.ContentLength ?? 0;
        var startedAt = DateTimeOffset.UtcNow;
        var originalResponseBody = context.Response.Body;
        await using var bufferedResponseBody = new MemoryStream();
        context.Response.Body = bufferedResponseBody;

        Exception? pipelineException = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            pipelineException = ex;
        }

        string responseBody;
        var responseBodyBytes = (int)bufferedResponseBody.Length;
        bufferedResponseBody.Position = 0;
        using (var reader = new StreamReader(bufferedResponseBody, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            var fullResponseBody = await reader.ReadToEndAsync(context.RequestAborted);
            responseBodyBytes = Encoding.UTF8.GetByteCount(fullResponseBody);
            if (capturePayloadBodies || context.Response.StatusCode >= 400 || pipelineException is not null)
            {
                responseBody = fullResponseBody;
                captureResponseBody = true;
            }
            else
            {
                responseBody = string.Empty;
            }
        }

        var searchEfficiency = _options.LogSearchEfficiencyStats
            ? await BuildSearchEfficiencyLogAsync(context, bufferedResponseBody, context.RequestAborted)
            : null;
        var ioStats = _options.LogSearchEfficiencyStats
            ? ApiRequestIoStats.EndRequest()
            : new IoStatsSnapshot();

        bufferedResponseBody.Position = 0;
        await bufferedResponseBody.CopyToAsync(originalResponseBody, context.RequestAborted);
        context.Response.Body = originalResponseBody;

        await WriteLogEntryAsync(
            context,
            startedAt,
            requestBody,
            requestBodyBytes,
            responseBody,
            responseBodyBytes,
            capturePayloadBodies,
            captureResponseBody,
            searchEfficiency,
            ioStats,
            pipelineException,
            context.RequestAborted);

        if (pipelineException is not null)
        {
            throw pipelineException;
        }
    }

    private static bool IsApiRequest(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is 0)
        {
            return string.Empty;
        }

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;
        return body;
    }

    private async Task WriteLogEntryAsync(
        HttpContext context,
        DateTimeOffset startedAt,
        string requestBody,
        int requestBodyBytes,
        string responseBody,
        int responseBodyBytes,
        bool capturePayloadBodies,
        bool captureResponseBody,
        string? searchEfficiency,
        IoStatsSnapshot ioStats,
        Exception? pipelineException,
        CancellationToken cancellationToken)
    {
        var logPath = ResolveLogPath();
        var logDirectory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var completedAt = DateTimeOffset.UtcNow;
        var elapsedMs = (completedAt - startedAt).TotalMilliseconds;
        var query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;
        var requestBodyToken = capturePayloadBodies
            ? (string.IsNullOrWhiteSpace(requestBody) ? "<empty>" : requestBody)
            : $"<omitted; bytes={requestBodyBytes}>";
        var responseBodyToken = captureResponseBody
            ? (string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : responseBody)
            : $"<omitted; bytes={responseBodyBytes}>";
        var exceptionToken = pipelineException is null
            ? string.Empty
            : $"Exception: {pipelineException.GetType().Name} - {pipelineException.Message}{Environment.NewLine}";
        var searchEfficiencyTable = string.IsNullOrWhiteSpace(searchEfficiency)
            ? string.Empty
            : FormatMetricsTable(searchEfficiency);
        var storageIoLine =
            $"StorageIoStats: detailFilesRead={ioStats.DetailFilesRead}; headerFilesRead={ioStats.HeaderFilesRead}; otherFilesRead={ioStats.OtherFilesRead}; detailFilesWritten={ioStats.DetailFilesWritten}; headerFilesWritten={ioStats.HeaderFilesWritten}; otherFilesWritten={ioStats.OtherFilesWritten}; detailFilesDeleted={ioStats.DetailFilesDeleted}; headerFilesDeleted={ioStats.HeaderFilesDeleted}; otherFilesDeleted={ioStats.OtherFilesDeleted}; blobQueries={ioStats.BlobQueries}";
        var storageIoTable = FormatMetricsTable(storageIoLine);

        var entry =
            $"[{completedAt:yyyy-MM-dd HH:mm:ss.fff zzz}] {context.Request.Method} {context.Request.Path}{query}{Environment.NewLine}" +
            $"Status: {context.Response.StatusCode}; DurationMs: {elapsedMs:F2}{Environment.NewLine}" +
            $"PayloadVerbosity: {(capturePayloadBodies ? ApiDiagnosticsOptions.FullVerbosity : ApiDiagnosticsOptions.CompactVerbosity)}{Environment.NewLine}" +
            searchEfficiencyTable +
            storageIoTable +
            $"RequestBody:{Environment.NewLine}{requestBodyToken}{Environment.NewLine}" +
            $"ResponseBody:{Environment.NewLine}{responseBodyToken}{Environment.NewLine}" +
            exceptionToken +
            $"{new string('-', 80)}{Environment.NewLine}";

        await FileWriteLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(logPath, entry, cancellationToken);
        }
        finally
        {
            FileWriteLock.Release();
        }
    }

    private string ResolveLogPath()
    {
        var configured = string.IsNullOrWhiteSpace(_options.LogFilePath)
            ? "apiDiag.log"
            : _options.LogFilePath.Trim();

        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));
    }

    private static async Task<string?> BuildSearchEfficiencyLogAsync(
        HttpContext context,
        MemoryStream bufferedResponseBody,
        CancellationToken cancellationToken)
    {
        if (context.Response.StatusCode < 200 || context.Response.StatusCode >= 300)
        {
            return $"SearchEfficiency: unavailable; reason=non-success-status-{context.Response.StatusCode}";
        }

        var contentType = context.Response.ContentType ?? string.Empty;
        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return "SearchEfficiency: unavailable; reason=non-json-response";
        }

        try
        {
            bufferedResponseBody.Position = 0;
            using var document = await JsonDocument.ParseAsync(bufferedResponseBody, cancellationToken: cancellationToken);
            bufferedResponseBody.Position = 0;

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                var reason = context.Request.Query.ContainsKey("includeInternalInfo")
                    ? "searchEfficiency-not-present"
                    : "includeInternalInfo-not-requested";
                return $"SearchEfficiency: unavailable; reason={reason}";
            }

            if (!document.RootElement.TryGetProperty("internalInfo", out var internalInfo)
                || !internalInfo.TryGetProperty("searchEfficiency", out var efficiency))
            {
                var reason = context.Request.Query.ContainsKey("includeInternalInfo")
                    ? "searchEfficiency-not-present"
                    : "includeInternalInfo-not-requested";
                return $"SearchEfficiency: unavailable; reason={reason}";
            }

            return
                $"SearchEfficiency: headerFilesRead={ReadInt(efficiency, "headerFilesRead")}; " +
                $"headerFilesTotal={ReadInt(efficiency, "headerFilesTotal")}; " +
                $"detailFilesRead={ReadInt(efficiency, "detailFilesRead")}; " +
                $"detailFilesTotal={ReadInt(efficiency, "detailFilesTotal")}; " +
                $"candidateHeaderFilesConsidered={ReadInt(efficiency, "candidateHeaderFilesConsidered")}; " +
                $"matchedInstanceFileCount={ReadInt(efficiency, "matchedInstanceFileCount")}; " +
                $"headerFilesRebuilt={ReadInt(efficiency, "headerFilesRebuilt")}; " +
                $"usedFilteredSearchPath={ReadBool(efficiency, "usedFilteredSearchPath").ToString().ToLowerInvariant()}";
        }
        catch (JsonException)
        {
            bufferedResponseBody.Position = 0;
            return "SearchEfficiency: unavailable; reason=response-not-valid-json";
        }
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True;
    }

    private static string FormatMetricsTable(string line)
    {
        var separator = line.IndexOf(':');
        if (separator < 0)
        {
            return string.Empty;
        }

        var title = line[..separator].Trim();
        var payload = line[(separator + 1)..].Trim();
        var parts = payload
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(x => x.Length == 2 && !string.IsNullOrWhiteSpace(x[0]))
            .Select(x => new KeyValuePair<string, string>(x[0].Trim(), x[1].Trim()))
            .ToList();

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        if (title.Equals("SearchEfficiency", StringComparison.Ordinal))
        {
            return FormatSearchEfficiencyTable(parts);
        }

        return FormatCompactMetricsTable(title, parts);
    }

    private static string FormatCompactMetricsTable(string title, IReadOnlyCollection<KeyValuePair<string, string>> parts)
    {

        const int pairsPerRow = 3;
        var fieldWidth = parts.Max(x => x.Key.Length);
        var valueWidth = parts.Max(x => x.Value.Length);
        var pairWidth = fieldWidth + 1 + valueWidth;

        var builder = new StringBuilder();
        builder.Append(title).Append("Table:").Append(Environment.NewLine);

        for (var i = 0; i < parts.Count; i += pairsPerRow)
        {
            var rowParts = parts.Skip(i).Take(pairsPerRow).ToList();
            for (var col = 0; col < rowParts.Count; col++)
            {
                var part = rowParts[col];
                var cell = $"{part.Key.PadRight(fieldWidth)} {part.Value.PadLeft(valueWidth)}";
                builder.Append(cell.PadRight(pairWidth));
                if (col < rowParts.Count - 1)
                {
                    builder.Append("  |  ");
                }
            }

            builder.Append(Environment.NewLine);
        }

        return builder.ToString();
    }

    private static string FormatSearchEfficiencyTable(IReadOnlyCollection<KeyValuePair<string, string>> parts)
    {
        var byKey = parts.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var orderedKeys = new[]
        {
            "headerFilesRead",
            "detailFilesRead",
            "candidateHeaderFilesConsidered",
            "headerFilesTotal",
            "detailFilesTotal",
            "matchedInstanceFileCount",
            "headerFilesRebuilt",
            "usedFilteredSearchPath"
        };

        var presentOrderedKeys = orderedKeys.Where(byKey.ContainsKey).ToList();
        if (presentOrderedKeys.Count == 0)
        {
            return FormatCompactMetricsTable("SearchEfficiency", parts);
        }

        var remainder = parts.Where(x => !orderedKeys.Contains(x.Key, StringComparer.Ordinal)).ToList();
        presentOrderedKeys.AddRange(remainder.Select(x => x.Key));

        var rows = new List<(string? Col1, string? Col2, string? Col3)>();
        for (var i = 0; i < presentOrderedKeys.Count; i += 3)
        {
            rows.Add((
                i < presentOrderedKeys.Count ? presentOrderedKeys[i] : null,
                i + 1 < presentOrderedKeys.Count ? presentOrderedKeys[i + 1] : null,
                i + 2 < presentOrderedKeys.Count ? presentOrderedKeys[i + 2] : null));
        }

        string CellText(string? key)
        {
            if (string.IsNullOrWhiteSpace(key) || !byKey.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            return $"{key} {value}";
        }

        var col1Width = rows.Max(r => CellText(r.Col1).Length);
        var col2Width = rows.Max(r => CellText(r.Col2).Length);
        var col3Width = rows.Max(r => CellText(r.Col3).Length);

        var builder = new StringBuilder();
        builder.Append("SearchEfficiencyTable:").Append(Environment.NewLine);

        foreach (var row in rows)
        {
            var col1 = CellText(row.Col1);
            var col2 = CellText(row.Col2);
            var col3 = CellText(row.Col3);

            builder.Append(col1.PadRight(col1Width));
            if (col2Width > 0)
            {
                builder.Append("  |  ").Append(col2.PadRight(col2Width));
            }

            if (col3Width > 0)
            {
                builder.Append("  |  ").Append(col3.PadRight(col3Width));
            }

            builder.Append(Environment.NewLine);
        }

        return builder.ToString();
    }

}
