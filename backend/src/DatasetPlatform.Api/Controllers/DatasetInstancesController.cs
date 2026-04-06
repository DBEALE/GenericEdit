using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Dtos;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DatasetPlatform.Api.Controllers;

/// <summary>
/// Full CRUD controller for dataset instances, plus header listing and the signoff workflow.
///
/// <para>Base route: <c>/api/datasets/{datasetKey}/instances</c></para>
///
/// <para>
/// All write operations (POST, PUT, DELETE, signoff) require the user to hold write or signoff
/// access on the dataset schema. Read operations require at least read access.
/// </para>
///
/// <para>HTTP status code mapping for service errors:</para>
/// <list type="bullet">
///   <item>Validation / business rule failures → <c>400 Bad Request</c></item>
///   <item>Schema or instance not found → <c>404 Not Found</c></item>
///   <item>Concurrent modification (version mismatch) → <c>409 Conflict</c></item>
/// </list>
///
/// <para>
/// The <c>headerCriteria</c> query parameter is a JSON object of string-to-string pairs used
/// for substring filtering on header fields, e.g. <c>{"region":"EMEA","currency":"USD"}</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/datasets/{datasetKey}/instances")]
public sealed class DatasetInstancesController(IDatasetService datasetService, IRequestUserContextAccessor userContextAccessor) : ControllerBase
{
    [HttpGet("~/api/datasets/{datasetKey}/headers")]
    public async Task<ActionResult> GetHeaders(
        string datasetKey,
        [FromQuery] DateOnly? minAsOfDate,
        [FromQuery] DateOnly? maxAsOfDate,
        [FromQuery] string? state,
        [FromQuery] string? headerCriteria,
        [FromQuery] bool includeInternalInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsedHeaderCriteria = ParseHeaderCriteria(headerCriteria);
            var user = userContextAccessor.GetCurrent();
            if (includeInternalInfo)
            {
                var response = await datasetService.GetHeadersWithInternalInfoAsync(
                    datasetKey,
                    user,
                    cancellationToken,
                    minAsOfDate,
                    maxAsOfDate,
                    state,
                    parsedHeaderCriteria);
                return Ok(response);
            }

            var headers = await datasetService.GetHeadersAsync(
                datasetKey,
                user,
                cancellationToken,
                minAsOfDate,
                maxAsOfDate,
                state,
                parsedHeaderCriteria);
            return Ok(headers);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
        catch (JsonException)
        {
            return BadRequest("headerCriteria must be valid JSON object of string-to-string values.");
        }
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        string datasetKey,
        [FromQuery] DateOnly? minAsOfDate,
        [FromQuery] DateOnly? maxAsOfDate,
        [FromQuery] string? state,
        [FromQuery] string? headerCriteria,
        [FromQuery] bool includeInternalInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsedHeaderCriteria = ParseHeaderCriteria(headerCriteria);
            var user = userContextAccessor.GetCurrent();
            if (includeInternalInfo)
            {
                var response = await datasetService.GetInstancesWithInternalInfoAsync(
                    datasetKey,
                    user,
                    cancellationToken,
                    minAsOfDate,
                    maxAsOfDate,
                    state,
                    parsedHeaderCriteria);
                return Ok(response);
            }

            var instances = await datasetService.GetInstancesAsync(
                datasetKey,
                user,
                cancellationToken,
                minAsOfDate,
                maxAsOfDate,
                state,
                parsedHeaderCriteria);
            return Ok(instances);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
        catch (JsonException)
        {
            return BadRequest("headerCriteria must be valid JSON object of string-to-string values.");
        }
    }

    [HttpGet("{instanceId:guid}")]
    public async Task<ActionResult<DatasetInstance?>> GetById(
        string datasetKey,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var instance = await datasetService.GetInstanceAsync(datasetKey, instanceId, userContextAccessor.GetCurrent(), cancellationToken);
            if (instance is null)
            {
                return NotFound();
            }

            return Ok(instance);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
    }

    [HttpGet("latest")]
    public async Task<ActionResult<DatasetInstance?>> GetLatest(
        string datasetKey,
        [FromQuery] DateOnly asOfDate,
        [FromQuery] string state,
        [FromQuery] string? headerCriteria,
        [FromQuery] bool includeInternalInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsedHeaderCriteria = ParseHeaderCriteria(headerCriteria);
            if (includeInternalInfo)
            {
                var response = await datasetService.GetLatestInstanceWithInternalInfoAsync(
                    datasetKey,
                    asOfDate,
                    state,
                    parsedHeaderCriteria,
                    userContextAccessor.GetCurrent(),
                    cancellationToken);
                return Ok(response);
            }

            var instance = await datasetService.GetLatestInstanceAsync(datasetKey, asOfDate, state, parsedHeaderCriteria, userContextAccessor.GetCurrent(), cancellationToken);
            return Ok(instance);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
        catch (JsonException)
        {
            return BadRequest("headerCriteria must be valid JSON object of string-to-string values.");
        }
    }

    [HttpPost]
    public async Task<ActionResult<DatasetInstance>> Create(
        string datasetKey,
        [FromBody] CreateDatasetInstanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(datasetKey, request.DatasetKey, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Route key and payload key must match.");
        }

        try
        {
            var created = await datasetService.CreateInstanceAsync(request, userContextAccessor.GetCurrent(), cancellationToken);
            return Ok(created);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
    }

    [HttpPut("{instanceId:guid}")]
    public async Task<ActionResult<DatasetInstance>> Update(
        string datasetKey,
        Guid instanceId,
        [FromBody] UpdateDatasetInstanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(datasetKey, request.DatasetKey, StringComparison.OrdinalIgnoreCase) || instanceId != request.InstanceId)
        {
            return BadRequest("Route identifiers and payload identifiers must match.");
        }

        try
        {
            var updated = await datasetService.UpdateInstanceAsync(request, userContextAccessor.GetCurrent(), cancellationToken);
            return Ok(updated);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
    }

    [HttpDelete("{instanceId:guid}")]
    public async Task<IActionResult> Delete(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await datasetService.DeleteInstanceAsync(datasetKey, instanceId, userContextAccessor.GetCurrent(), cancellationToken);
            return NoContent();
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
    }

    /// <summary>
    /// Promotes an instance to <see cref="DatasetState.Official"/> in place.
    /// Requires signoff permission on the dataset.
    /// </summary>
    /// <param name="expectedVersion">
    /// Optional. The <c>version</c> value last observed by the caller.
    /// When provided, returns 409 Conflict if the instance was concurrently modified.
    /// </param>
    [HttpPost("{instanceId:guid}/signoff")]
    public async Task<ActionResult<DatasetInstance>> Signoff(
        string datasetKey,
        Guid instanceId,
        [FromQuery] int? expectedVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new SignoffDatasetRequest
            {
                DatasetKey = datasetKey,
                InstanceId = instanceId,
                ExpectedVersion = expectedVersion
            };

            var signed = await datasetService.SignoffInstanceAsync(request, userContextAccessor.GetCurrent(), cancellationToken);
            return Ok(signed);
        }
        catch (DatasetServiceException ex)
        {
            return MapServiceError(ex);
        }
    }

    private static Dictionary<string, string>? ParseHeaderCriteria(string? headerCriteria)
    {
        if (string.IsNullOrWhiteSpace(headerCriteria))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(headerCriteria);
    }

    /// <summary>
    /// Maps a <see cref="DatasetServiceException"/> to the appropriate HTTP status code.
    /// <list type="bullet">
    ///   <item><see cref="DatasetServiceErrorCode.NotFound"/> → 404</item>
    ///   <item><see cref="DatasetServiceErrorCode.Conflict"/> → 409</item>
    ///   <item>All others → 400</item>
    /// </list>
    /// </summary>
    private ActionResult MapServiceError(DatasetServiceException ex) => ex.ErrorCode switch
    {
        DatasetServiceErrorCode.NotFound => NotFound(ex.Message),
        DatasetServiceErrorCode.Conflict => Conflict(ex.Message),
        _ => BadRequest(ex.Message)
    };
}
