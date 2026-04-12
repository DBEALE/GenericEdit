using DatasetPlatform.Api.Controllers;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Dtos;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace DatasetPlatform.Api.Tests;

public sealed class LookupControllerTests
{
    [Fact]
    public async Task GetValues_ShouldReturnOk_WhenLookupValuesExist()
    {
        var service = new FakeDatasetService
        {
            LookupValues = ["USD", "EUR"]
        };
        var controller = new LookupController(service);

        var result = await controller.GetValues("FX_LOOKUP", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);
        Assert.Equal(["USD", "EUR"], values);
    }

    [Fact]
    public async Task GetValues_ShouldForwardDatasetKey_ToService()
    {
        var service = new FakeDatasetService
        {
            LookupValues = ["USD"]
        };
        var controller = new LookupController(service);

        await controller.GetValues("FX_LOOKUP", CancellationToken.None);

        Assert.Equal("FX_LOOKUP", service.LastLookupDatasetKey);
    }

    [Fact]
    public async Task GetValues_ShouldReturnBadRequest_WhenServiceThrows()
    {
        var service = new FakeDatasetService
        {
            ThrowOnLookup = new DatasetServiceException("Lookup dataset schema 'FX_LOOKUP' was not found.")
        };
        var controller = new LookupController(service);

        var result = await controller.GetValues("FX_LOOKUP", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var message = Assert.IsType<string>(badRequest.Value);
        Assert.Contains("not found", message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDatasetService : IDatasetService
    {
        public IReadOnlyList<string> LookupValues { get; set; } = [];
        public Exception? ThrowOnLookup { get; set; }
        public string? LastLookupDatasetKey { get; private set; }

        public Task<IReadOnlyList<string>> GetLookupPermissibleValuesAsync(string lookupDatasetKey, CancellationToken cancellationToken)
        {
            LastLookupDatasetKey = lookupDatasetKey;
            if (ThrowOnLookup is not null)
            {
                throw ThrowOnLookup;
            }

            return Task.FromResult(LookupValues);
        }

        public Task<IReadOnlyList<DatasetSchema>> GetAccessibleSchemasAsync(UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<DatasetSchema> UpsertSchemaAsync(DatasetSchema schema, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DeleteSchemaAsync(string datasetKey, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DatasetInstance>> GetInstancesAsync(string datasetKey, UserContext user, CancellationToken cancellationToken, DateOnly? minAsOfDate = null, DateOnly? maxAsOfDate = null, string? state = null, IReadOnlyDictionary<string, string>? headerCriteria = null)
            => throw new NotImplementedException();

        public Task<DatasetInstancesQueryResponse> GetInstancesWithInternalInfoAsync(string datasetKey, UserContext user, CancellationToken cancellationToken, DateOnly? minAsOfDate = null, DateOnly? maxAsOfDate = null, string? state = null, IReadOnlyDictionary<string, string>? headerCriteria = null)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DatasetHeaderSummary>> GetHeadersAsync(string datasetKey, UserContext user, CancellationToken cancellationToken, DateOnly? minAsOfDate = null, DateOnly? maxAsOfDate = null, string? state = null, IReadOnlyDictionary<string, string>? headerCriteria = null)
            => throw new NotImplementedException();

        public Task<DatasetHeadersQueryResponse> GetHeadersWithInternalInfoAsync(string datasetKey, UserContext user, CancellationToken cancellationToken, DateOnly? minAsOfDate = null, DateOnly? maxAsOfDate = null, string? state = null, IReadOnlyDictionary<string, string>? headerCriteria = null)
            => throw new NotImplementedException();

        public Task<DatasetInstance?> GetInstanceAsync(string datasetKey, Guid instanceId, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<DatasetInstance?> GetLatestInstanceAsync(string datasetKey, DateOnly asOfDate, string state, IReadOnlyDictionary<string, string>? headerCriteria, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<DatasetLatestInstanceQueryResponse> GetLatestInstanceWithInternalInfoAsync(string datasetKey, DateOnly asOfDate, string state, IReadOnlyDictionary<string, string>? headerCriteria, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<DatasetInstance> CreateInstanceAsync(CreateDatasetInstanceRequest request, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<DatasetInstance> UpdateInstanceAsync(UpdateDatasetInstanceRequest request, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DeleteInstanceAsync(string datasetKey, Guid instanceId, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<DatasetInstance> SignoffInstanceAsync(SignoffDatasetRequest request, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Catalogue>> GetCataloguesAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<Catalogue> UpsertCatalogueAsync(Catalogue catalogue, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DeleteCatalogueAsync(string catalogueKey, UserContext user, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<AuditEvent>> GetAuditAsync(
            UserContext user,
            CancellationToken cancellationToken,
            string? datasetKey = null,
            Guid? instanceId = null,
            DateOnly? minOccurredDate = null,
            DateOnly? maxOccurredDate = null)
            => throw new NotImplementedException();
    }
}
