using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace DatasetPlatform.Api.Controllers;

/// <summary>
/// Manages dataset schema definitions — the structural blueprints that describe
/// what fields an instance must contain and who is allowed to access it.
///
/// <para>Routes: <c>GET/PUT/DELETE /api/schemas/{datasetKey}</c></para>
///
/// <para>Authorization is delegated to <c>DatasetService</c>, which checks the
/// user's roles against the schema's <c>DatasetPermissions</c>.</para>
/// </summary>
[ApiController]
[Route("api/schemas")]
public sealed class SchemasController(IDatasetService datasetService, IRequestUserContextAccessor userContextAccessor) : ControllerBase
{
    /// <summary>
    /// Returns all schemas the current user is permitted to read.
    /// DatasetAdmin users see all schemas; other users see only schemas where they hold
    /// at least one of: ReadRoles, WriteRoles, SignoffRoles, or DatasetAdminRoles.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DatasetSchema>>> GetSchemas(CancellationToken cancellationToken)
    {
        var user = userContextAccessor.GetCurrent();
        var schemas = await datasetService.GetAccessibleSchemasAsync(user, cancellationToken);
        return Ok(schemas);
    }

    /// <summary>
    /// Creates or fully replaces the schema for <paramref name="datasetKey"/>.
    /// The route key and the <c>Key</c> field in the request body must match.
    /// Creating a new schema requires the global <c>DatasetAdmin</c> role.
    /// Updating an existing schema requires <c>DatasetAdmin</c> or schema-level admin access.
    /// </summary>
    /// <returns>The saved schema with the normalised key and updated timestamp.</returns>
    [HttpPut("{datasetKey}")]
    public async Task<ActionResult<DatasetSchema>> UpsertSchema(string datasetKey, [FromBody] DatasetSchema schema, CancellationToken cancellationToken)
    {
        if (!string.Equals(datasetKey, schema.Key, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Route key and payload key must match.");
        }

        try
        {
            var saved = await datasetService.UpsertSchemaAsync(schema, userContextAccessor.GetCurrent(), cancellationToken);
            return Ok(saved);
        }
        catch (DatasetServiceException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Permanently deletes the schema AND all instances for <paramref name="datasetKey"/>.
    /// This operation is irreversible. Requires schema-level DatasetAdmin access.
    /// </summary>
    /// <returns>204 No Content on success.</returns>
    [HttpDelete("{datasetKey}")]
    public async Task<ActionResult> DeleteSchema(string datasetKey, CancellationToken cancellationToken)
    {
        try
        {
            await datasetService.DeleteSchemaAsync(datasetKey, userContextAccessor.GetCurrent(), cancellationToken);
            return NoContent();
        }
        catch (DatasetServiceException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
