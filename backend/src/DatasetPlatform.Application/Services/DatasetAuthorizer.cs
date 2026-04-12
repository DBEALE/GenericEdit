using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Application.Services;

/// <summary>
/// Stateless helper that answers permission questions about a dataset schema and a user.
/// All methods are pure — they only inspect the schema permissions and the user context;
/// they never throw and have no side effects.
///
/// <para>
/// Authorization hierarchy (any higher level also grants all lower-level access):
/// <list type="number">
///   <item>Global <see cref="DatasetAdminRole"/> role — full access to everything</item>
///   <item>Schema-level DatasetAdminRoles — full access to this specific schema</item>
///   <item>SignoffRoles — can promote instances to Official; also implies read access</item>
///   <item>WriteRoles — can create/update/delete Draft and PendingApproval instances; also implies read access</item>
///   <item>ReadRoles — can view instances, headers, and audit logs for this schema</item>
/// </list>
/// </para>
/// </summary>
public static class DatasetAuthorizer
{
    /// <summary>
    /// The global super-admin role name. A user holding this role has unrestricted access
    /// to all datasets and all operations, regardless of individual schema permissions.
    /// </summary>
    public const string DatasetAdminRole = "DatasetAdmin";

    /// <summary>
    /// Role that grants permission to create, update, and delete catalogues.
    /// <see cref="DatasetAdminRole"/> transitively grants the same rights.
    /// </summary>
    public const string CatalogueAdminRole = "CatalogueAdmin";

    /// <summary>Returns <c>true</c> if the user may create, update, or delete catalogues.</summary>
    public static bool CanManageCatalogues(UserContext user)
        => user.HasRole(DatasetAdminRole) || user.HasRole(CatalogueAdminRole);

    /// <summary>
    /// Returns <c>true</c> if the user's ID or any of their roles appears in <paramref name="principals"/>.
    /// This allows permissions to be granted to both individual users and to groups/roles.
    /// </summary>
    private static bool MatchesPrincipal(HashSet<string> principals, UserContext user)
    {
        return principals.Contains(user.UserId)
            || user.Roles.Any(principals.Contains);
    }

    /// <summary>
    /// Returns <c>true</c> if the user may create, update, or delete this schema definition.
    /// Requires the global DatasetAdmin role or membership in the schema's DatasetAdminRoles.
    /// </summary>
    public static bool CanMaintainSchema(DatasetSchema schema, UserContext user)
    {
        return user.HasRole(DatasetAdminRole)
            || MatchesPrincipal(schema.Permissions.DatasetAdminRoles, user);
    }

    /// <summary>
    /// Returns <c>true</c> if the user may read instances, headers, or audit logs for this schema.
    /// Any user with Write, Signoff, or DatasetAdmin access also implicitly passes this check.
    /// </summary>
    public static bool CanRead(DatasetSchema schema, UserContext user)
    {
        return user.HasRole(DatasetAdminRole)
            || MatchesPrincipal(schema.Permissions.DatasetAdminRoles, user)
            || MatchesPrincipal(schema.Permissions.ReadRoles, user)
            || MatchesPrincipal(schema.Permissions.WriteRoles, user)
            || MatchesPrincipal(schema.Permissions.SignoffRoles, user);
    }

    /// <summary>
    /// Returns <c>true</c> if the user may create, update, or delete Draft/PendingApproval instances.
    /// Does <b>not</b> grant the ability to set an instance to <see cref="DatasetState.Official"/>
    /// — that requires <see cref="CanSignoff"/>.
    /// </summary>
    public static bool CanWrite(DatasetSchema schema, UserContext user)
    {
        return user.HasRole(DatasetAdminRole)
            || MatchesPrincipal(schema.Permissions.WriteRoles, user);
    }

    /// <summary>
    /// Returns <c>true</c> if the user may promote an instance to the <see cref="DatasetState.Official"/> state
    /// via the signoff endpoint.
    /// </summary>
    public static bool CanSignoff(DatasetSchema schema, UserContext user)
    {
        return user.HasRole(DatasetAdminRole)
            || MatchesPrincipal(schema.Permissions.SignoffRoles, user);
    }
}
