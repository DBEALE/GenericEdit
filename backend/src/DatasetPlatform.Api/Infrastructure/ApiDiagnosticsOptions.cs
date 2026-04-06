namespace DatasetPlatform.Api.Infrastructure;

public sealed class ApiDiagnosticsOptions
{
    public const string SectionName = "ApiDebug";
    public const string FullVerbosity = "Full";
    public const string CompactVerbosity = "Compact";

    public bool Enabled { get; init; }

    // Relative paths are resolved from the API project content root.
    public string LogFilePath { get; init; } = "apiDiag.log";

    // Full logs request/response payload bodies. Compact omits payload bodies and logs only sizes.
    public string Verbosity { get; init; } = FullVerbosity;

    // When enabled, logs extracted internalInfo.searchEfficiency stats as a compact line.
    public bool LogSearchEfficiencyStats { get; init; }

    public bool IsCompactVerbosity()
    {
        return string.Equals(Verbosity, CompactVerbosity, StringComparison.OrdinalIgnoreCase);
    }
}
