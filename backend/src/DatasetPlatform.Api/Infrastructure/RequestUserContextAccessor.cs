using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Api.Infrastructure;

/// <summary>
/// Provides the <see cref="UserContext"/> for the currently executing HTTP request.
/// Controllers call this to obtain the caller's identity before invoking service methods.
/// </summary>
public interface IRequestUserContextAccessor
{
    /// <summary>Builds and returns the <see cref="UserContext"/> for the current HTTP request.</summary>
    UserContext GetCurrent();
}

/// <summary>
/// Extracts user identity from incoming HTTP request headers.
///
/// <para>Headers consumed:</para>
/// <list type="bullet">
///   <item><c>x-user-id</c> — the caller's user identifier (falls back to "anonymous" if absent).</item>
///   <item><c>x-user-roles</c> — comma-separated list of roles, e.g. <c>"DatasetAdmin,Analyst"</c>.</item>
/// </list>
///
/// <para>
/// <b>Security warning:</b> These headers are currently accepted as-is with no cryptographic
/// verification. Any client can send arbitrary values. Before production use, replace this
/// implementation with one that validates a signed JWT from an OIDC provider (e.g. Azure AD / Entra ID).
/// </para>
/// </summary>
public sealed class RequestUserContextAccessor(IHttpContextAccessor httpContextAccessor) : IRequestUserContextAccessor
{
    /// <inheritdoc/>
    public UserContext GetCurrent()
    {
        var context = httpContextAccessor.HttpContext;

        // Read user ID; default to "anonymous" so downstream code always has a non-null value.
        var userId = context?.Request.Headers["x-user-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = "anonymous";
        }

        // Parse comma-separated roles into a case-insensitive set.
        var rolesHeader = context?.Request.Headers["x-user-roles"].FirstOrDefault() ?? string.Empty;
        var roles = rolesHeader
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new UserContext
        {
            UserId = userId,
            Roles = roles
        };
    }
}
