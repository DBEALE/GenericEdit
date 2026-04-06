namespace DatasetPlatform.Application.Services;

/// <summary>
/// Categorises the type of failure so controllers can map it to the correct HTTP status code.
/// </summary>
public enum DatasetServiceErrorCode
{
    /// <summary>General validation or business-rule violation. Maps to HTTP 400.</summary>
    ValidationError,

    /// <summary>
    /// The requested resource (schema or instance) does not exist. Maps to HTTP 404.
    /// </summary>
    NotFound,

    /// <summary>
    /// The caller supplied an <c>ExpectedVersion</c> that does not match the current version,
    /// indicating a concurrent modification. Maps to HTTP 409.
    /// </summary>
    Conflict,
}

/// <summary>
/// Thrown by <c>DatasetService</c> when a business rule is violated or a resource is not found.
/// Controllers catch this and map <see cref="ErrorCode"/> to the appropriate HTTP status code.
/// </summary>
public sealed class DatasetServiceException : Exception
{
    /// <summary>Initialises the exception with a validation error code (HTTP 400).</summary>
    public DatasetServiceException(string message)
        : this(message, DatasetServiceErrorCode.ValidationError) { }

    /// <summary>Initialises the exception with an explicit error code.</summary>
    public DatasetServiceException(string message, DatasetServiceErrorCode errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Indicates the category of failure. Used by controllers to choose the HTTP status code.</summary>
    public DatasetServiceErrorCode ErrorCode { get; }
}
