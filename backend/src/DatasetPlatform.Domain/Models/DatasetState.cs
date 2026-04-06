namespace DatasetPlatform.Domain.Models;

/// <summary>
/// Represents the lifecycle state of a dataset instance.
/// Instances follow a one-way promotion path: Draft → PendingApproval → Official.
/// Only the Signoff endpoint can promote an instance to <see cref="Official"/>;
/// it cannot be set directly via create or update.
/// </summary>
public enum DatasetState
{
    /// <summary>Work-in-progress entry. Can be created and edited freely by users with write access.</summary>
    Draft = 1,

    /// <summary>
    /// Submitted for approval. Indicates the data is ready for review but has not yet been signed off.
    /// Can be created and edited by users with write access.
    /// </summary>
    PendingApproval = 2,

    /// <summary>
    /// Approved, authoritative version. Can only be set via the Signoff endpoint by users with signoff access.
    /// Once official, only one instance per (dataset, asOfDate, headerCriteria) combination is permitted.
    /// </summary>
    Official = 3
}
