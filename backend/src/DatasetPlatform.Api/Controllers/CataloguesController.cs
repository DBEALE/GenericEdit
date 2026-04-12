using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace DatasetPlatform.Api.Controllers;

/// <summary>
/// Manages catalogues — named groupings of dataset schemas.
///
/// <para>Routes: <c>GET /api/catalogues</c>, <c>PUT /api/catalogues/{catalogueKey}</c>, <c>DELETE /api/catalogues/{catalogueKey}</c></para>
///
/// <para>
/// Reading catalogues requires no special role — catalogue names are not sensitive metadata.
/// Creating or deleting a catalogue requires the <c>CatalogueAdmin</c> or <c>DatasetAdmin</c> role.
/// </para>
/// </summary>
[ApiController]
[Route("api/catalogues")]
public sealed class CataloguesController(IDatasetService datasetService, IRequestUserContextAccessor userContextAccessor) : ControllerBase
{
    /// <summary>Returns all catalogues, ordered by key.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Catalogue>>> GetCatalogues(CancellationToken cancellationToken)
    {
        var catalogues = await datasetService.GetCataloguesAsync(cancellationToken);
        return Ok(catalogues);
    }

    /// <summary>
    /// Creates or fully replaces the catalogue for <paramref name="catalogueKey"/>.
    /// The route key and the <c>Key</c> field in the request body must match.
    /// Requires <c>CatalogueAdmin</c> or <c>DatasetAdmin</c> role.
    /// </summary>
    [HttpPut("{catalogueKey}")]
    public async Task<ActionResult<Catalogue>> UpsertCatalogue(string catalogueKey, [FromBody] Catalogue catalogue, CancellationToken cancellationToken)
    {
        if (!string.Equals(catalogueKey, catalogue.Key, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Route key and payload key must match.");

        try
        {
            var saved = await datasetService.UpsertCatalogueAsync(catalogue, userContextAccessor.GetCurrent(), cancellationToken);
            return Ok(saved);
        }
        catch (DatasetServiceException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Deletes the catalogue. Schemas that reference it are not affected.
    /// Requires <c>CatalogueAdmin</c> or <c>DatasetAdmin</c> role.
    /// </summary>
    [HttpDelete("{catalogueKey}")]
    public async Task<ActionResult> DeleteCatalogue(string catalogueKey, CancellationToken cancellationToken)
    {
        try
        {
            await datasetService.DeleteCatalogueAsync(catalogueKey, userContextAccessor.GetCurrent(), cancellationToken);
            return NoContent();
        }
        catch (DatasetServiceException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
