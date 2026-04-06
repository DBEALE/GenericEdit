using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatasetPlatform.Api.Controllers;

/// <summary>
/// Provides runtime lookup values for <see cref="Domain.Models.FieldType.Lookup"/> fields.
/// The UI calls this endpoint to populate dropdown options when editing an instance whose
/// schema references another dataset as a lookup source.
///
/// <para>Route: <c>GET /api/lookups/{datasetKey}/values</c></para>
///
/// <para>No user-level authorization is applied here — lookup values are considered non-sensitive
/// catalogue data. If your datasets contain sensitive values, add authorization checks in
/// <c>DatasetService.GetLookupPermissibleValuesAsync</c>.</para>
/// </summary>
[ApiController]
[Route("api/lookups")]
public sealed class LookupController(IDatasetService datasetService) : ControllerBase
{
    /// <summary>
    /// Returns the set of permissible string values for the dataset identified by <paramref name="datasetKey"/>.
    /// Values are derived from the Official instances of the lookup dataset.
    /// </summary>
    [HttpGet("{datasetKey}/values")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetValues(string datasetKey, CancellationToken cancellationToken)
    {
        try
        {
            var values = await datasetService.GetLookupPermissibleValuesAsync(datasetKey, cancellationToken);
            return Ok(values);
        }
        catch (DatasetServiceException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
