using System.Text.Json.Serialization;

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

    // ─── Legacy aliases ─────────────────────────────────────────────────────────
    // Older persisted schema JSON used "readUsers", "writeUsers", etc.
    // These write-only properties transparently migrate those values into the
    // current role-based properties on deserialization. They are never serialized.

    /// <inheritdoc cref="ReadRoles"/>
    [JsonPropertyName("readUsers")]
    public HashSet<string>? LegacyReadUsers
    {
        set => ReadRoles = Normalize(value);
    }

    /// <inheritdoc cref="WriteRoles"/>
    [JsonPropertyName("writeUsers")]
    public HashSet<string>? LegacyWriteUsers
    {
        set => WriteRoles = Normalize(value);
    }

    /// <inheritdoc cref="SignoffRoles"/>
    [JsonPropertyName("signoffUsers")]
    public HashSet<string>? LegacySignoffUsers
    {
        set => SignoffRoles = Normalize(value);
    }

    /// <inheritdoc cref="DatasetAdminRoles"/>
    [JsonPropertyName("datasetAdminUsers")]
    public HashSet<string>? LegacyDatasetAdminUsers
    {
        set => DatasetAdminRoles = Normalize(value);
    }

    /// <summary>Returns a new case-insensitive <see cref="HashSet{T}"/> copied from <paramref name="source"/>, or an empty set if null.</summary>
    private static HashSet<string> Normalize(HashSet<string>? source)
    {
        return source is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
    }
}
