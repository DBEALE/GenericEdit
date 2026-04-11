namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Defines the role-based access control (RBAC) rules for a <see cref="DatasetSchema"/>.
/// Each property holds a set of role names (or user IDs) that are granted the corresponding capability.
///
/// <para>
/// Matching is case-insensitive and supports both direct user IDs and role names.
/// A user is authorised if their <see cref="UserContext.UserId"/> OR any of their
/// <see cref="UserContext.Roles"/> appears in the relevant set.
/// </para>
///
/// <para>Capability hierarchy (highest to lowest): DatasetAdmin ⊃ Signoff ⊃ Write ⊃ Read.</para>
/// </summary>
public sealed class DatasetPermissions
{
    /// <summary>
    /// Roles/users allowed to read instances and headers.
    /// Any user with Write, Signoff, or DatasetAdmin access also implicitly has Read access.
    /// </summary>
    public HashSet<string> ReadRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Roles/users allowed to create, update, and delete Draft or PendingApproval instances.</summary>
    public HashSet<string> WriteRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Roles/users allowed to promote an instance to the <see cref="DatasetState.Official"/> state via the signoff endpoint.</summary>
    public HashSet<string> SignoffRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Roles/users allowed to create, modify, or delete this dataset's schema definition.</summary>
    public HashSet<string> DatasetAdminRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

}
