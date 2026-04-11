using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace DatasetPlatform.Api.Controllers;

/// <summary>
/// Provides read-only access to the immutable audit trail of all dataset operations.
///
/// <para>Route: <c>GET /api/audit</c></para>
///
/// <para>
/// Access rules (enforced by <c>DatasetService.GetAuditAsync</c>):
/// <list type="bullet">
///   <item>Global <c>DatasetAdmin</c> users may query all events or filter by dataset key.</item>
///   <item>Non-admin users must supply a <paramref name="datasetKey"/> and must hold read access to that dataset.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/audit")]
public sealed class AuditController(IDatasetService datasetService, IRequestUserContextAccessor userContextAccessor) : ControllerBase
{
    /// <summary>
    /// Returns audit events ordered by timestamp descending.
    /// </summary>
    /// <param name="datasetKey">
    /// Optional. When supplied, results are limited to events for that dataset.
    /// Non-admin users must always supply this parameter.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditEvent>>> GetAudit([FromQuery] string? datasetKey, [FromQuery] Guid? instanceId, CancellationToken cancellationToken)
    {
        try
        {
            var records = await datasetService.GetAuditAsync(userContextAccessor.GetCurrent(), cancellationToken, datasetKey, instanceId);
            return Ok(records);
        }
        catch (DatasetServiceException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
