namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Represents the identity of the user making the current API request.
/// Populated from HTTP request headers by <c>RequestUserContextAccessor</c>:
/// <list type="bullet">
///   <item><c>x-user-id</c> → <see cref="UserId"/> (defaults to "anonymous" if absent)</item>
///   <item><c>x-user-roles</c> → <see cref="Roles"/> (comma-separated, e.g. "DatasetAdmin,Analyst")</item>
/// </list>
/// <para>
/// <b>Security note:</b> These headers are currently trusted without cryptographic verification.
/// Before production use, integrate an authentication middleware (OIDC/Azure AD) that populates
/// or validates these values from a verified token.
/// </para>
/// </summary>
public sealed class UserContext
{
    /// <summary>Identifier of the requesting user. Falls back to "anonymous" when the header is missing.</summary>
    public required string UserId { get; init; }

    /// <summary>Set of role names the user holds. Case-insensitive. Used for permission checks in <c>DatasetAuthorizer</c>.</summary>
    public HashSet<string> Roles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns <c>true</c> if the user holds the specified role (case-insensitive).</summary>
    public bool HasRole(string role) => Roles.Contains(role);
}
