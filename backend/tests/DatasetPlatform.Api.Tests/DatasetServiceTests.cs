using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Dtos;
using DatasetPlatform.Application.Services;
using DatasetPlatform.Domain.Models;

namespace DatasetPlatform.Api.Tests;

public sealed class DatasetServiceTests
{
    [Fact]
    public async Task CreateInstance_ShouldRejectOfficialState()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var request = new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Official",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        };

        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() => service.CreateInstanceAsync(request, writer, CancellationToken.None));
        Assert.Contains("Official state can only be set", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signoff_ShouldUpdateLoadedInstanceToOfficial()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var approver = CreateUser("approver");
        var signed = await service.SignoffInstanceAsync(new SignoffDatasetRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id
        }, approver, CancellationToken.None);

        Assert.Equal("Official", signed.State);
        Assert.Equal(created.Id, signed.Id);
        Assert.Equal(1, signed.Version);
        Assert.Equal("approver", signed.LastModifiedBy);

        var all = await service.GetInstancesAsync("FX_RATES", writer, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal("Official", all[0].State);
        Assert.Equal(created.Id, all[0].Id);
    }

    [Fact]
    public async Task Signoff_ShouldRejectWhenApproverModifiedInstanceSinceLastApprovedVersion()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(
            CreateSchema(
                "FX_RATES",
                read: ["viewer", "writer", "approver"],
                write: ["writer", "approver"],
                signoff: ["approver"]),
            admin,
            CancellationToken.None);

        var writer = CreateUser("writer", "writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var approver = CreateUser("approver", "approver", "writer");
        await service.SignoffInstanceAsync(new SignoffDatasetRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id
        }, approver, CancellationToken.None);

        await service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id,
            AsOfDate = created.AsOfDate,
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.31" }]
        }, approver, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.SignoffInstanceAsync(new SignoffDatasetRequest
            {
                DatasetKey = "FX_RATES",
                InstanceId = created.Id
            }, approver, CancellationToken.None));

        Assert.Contains("Approval is not permitted", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 change", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approver", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signoff_ShouldAllowWhenApproverDidNotModifySinceLastApprovedVersion()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(
            CreateSchema(
                "FX_RATES",
                read: ["viewer", "writer", "approver"],
                write: ["writer"],
                signoff: ["approver"]),
            admin,
            CancellationToken.None);

        var writer = CreateUser("writer", "writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var approver = CreateUser("approver", "approver");
        var firstSignoff = await service.SignoffInstanceAsync(new SignoffDatasetRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id
        }, approver, CancellationToken.None);

        await service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id,
            AsOfDate = created.AsOfDate,
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.31" }]
        }, writer, CancellationToken.None);

        var secondSignoff = await service.SignoffInstanceAsync(new SignoffDatasetRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id
        }, approver, CancellationToken.None);

        Assert.Equal("Official", firstSignoff.State);
        Assert.Equal("Official", secondSignoff.State);
        Assert.Equal("approver", secondSignoff.LastModifiedBy);
    }

    [Fact]
    public async Task CreateInstance_ShouldValidateRequiredFields()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var request = new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?>(),
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        };

        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() => service.CreateInstanceAsync(request, writer, CancellationToken.None));
        Assert.Contains("required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAccessibleSchemas_ShouldOnlyReturnAuthorizedDatasets()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(CreateSchema("FX_RATES", read: ["viewer"]), admin, CancellationToken.None);
        await service.UpsertSchemaAsync(CreateSchema("IR_CURVE", read: ["other-user"]), admin, CancellationToken.None);

        var viewer = CreateUser("viewer");
        var results = await service.GetAccessibleSchemasAsync(viewer, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("FX_RATES", results[0].Key);
    }

    [Fact]
    public async Task GetAccessibleSchemas_ShouldAllowRoleBasedReadPermission()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(CreateSchema("FX_RATES", read: ["DatasetReaderRole"]), admin, CancellationToken.None);
        await service.UpsertSchemaAsync(CreateSchema("IR_CURVE", read: ["DifferentRole"]), admin, CancellationToken.None);

        var roleReader = CreateUser("user-1", "DatasetReaderRole");
        var results = await service.GetAccessibleSchemasAsync(roleReader, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("FX_RATES", results[0].Key);
    }

    [Fact]
    public async Task GetAudit_WithReadRoleForDataset_ShouldReturnDatasetAudit()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(CreateSchema("FX_RATES", read: ["viewer"], write: ["writer"], signoff: ["approver"]), admin, CancellationToken.None);

        var writer = CreateUser("writer", "writer");
        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var viewer = CreateUser("viewer-user", "viewer");
        var audit = await service.GetAuditAsync(viewer, CancellationToken.None, "FX_RATES");

        Assert.NotEmpty(audit);
        Assert.Contains(audit, x => x.Action == "INSTANCE_CREATE");
    }

    [Fact]
    public async Task DatasetScopedAdminRole_ShouldAllowSchemaUpdateForThatDatasetOnly()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(
            WithDatasetAdminRoles(CreateSchema("FX_RATES", read: ["viewer"], write: ["writer"], signoff: ["approver"]), ["ScopedSchemaAdmin"]),
            admin,
            CancellationToken.None);

        var scopedAdmin = CreateUser("user-1", "ScopedSchemaAdmin");
        var updateSchema = WithDatasetAdminRoles(CreateSchema("FX_RATES", read: ["viewer"], write: ["writer"], signoff: ["approver"]), ["ScopedSchemaAdmin"]);
        updateSchema = new DatasetSchema
        {
            Key = updateSchema.Key,
            Name = updateSchema.Name,
            Description = "Updated by scoped admin",
            HeaderFields = updateSchema.HeaderFields,
            DetailFields = updateSchema.DetailFields,
            Permissions = updateSchema.Permissions,
            CreatedAtUtc = updateSchema.CreatedAtUtc,
            UpdatedAtUtc = updateSchema.UpdatedAtUtc
        };
        var updated = await service.UpsertSchemaAsync(
            updateSchema,
            scopedAdmin,
            CancellationToken.None);

        Assert.Equal("FX_RATES", updated.Key);
        Assert.Equal("Updated by scoped admin", updated.Description);

        var createEx = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.UpsertSchemaAsync(CreateSchema("NEW_DATASET"), scopedAdmin, CancellationToken.None));
        Assert.Contains("not authorized to create", createEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DatasetScopedAdminRole_ShouldNotAllowSchemaUpdateForOtherDatasets()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(
            WithDatasetAdminRoles(CreateSchema("FX_RATES"), ["ScopedSchemaAdmin"]),
            admin,
            CancellationToken.None);
        await service.UpsertSchemaAsync(
            WithDatasetAdminRoles(CreateSchema("IR_CURVE"), ["DifferentAdmin"]),
            admin,
            CancellationToken.None);

        var scopedAdmin = CreateUser("user-1", "ScopedSchemaAdmin");
        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.UpsertSchemaAsync(CreateSchema("IR_CURVE"), scopedAdmin, CancellationToken.None));

        Assert.Contains("not authorized to maintain this dataset schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RoleBasedWriteAndSignoffPermissions_ShouldEnforceExpectedBoundaries()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(
            CreateSchema(
                "FX_RATES",
                read: ["DatasetReaderRole"],
                write: ["DatasetWriterRole"],
                signoff: ["DatasetSignoffRole"]),
            admin,
            CancellationToken.None);

        var writer = CreateUser("writer-user", "DatasetWriterRole");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var writerVisibleRows = await service.GetInstancesAsync("FX_RATES", writer, CancellationToken.None);
        Assert.Single(writerVisibleRows);
        Assert.Equal(created.Id, writerVisibleRows[0].Id);

        var unauthorizedSignoff = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.SignoffInstanceAsync(new SignoffDatasetRequest
            {
                DatasetKey = "FX_RATES",
                InstanceId = created.Id
            }, writer, CancellationToken.None));
        Assert.Contains("not authorized to sign off", unauthorizedSignoff.Message, StringComparison.OrdinalIgnoreCase);

        var signoffUser = CreateUser("approver-user", "DatasetSignoffRole");
        var signed = await service.SignoffInstanceAsync(new SignoffDatasetRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id
        }, signoffUser, CancellationToken.None);

        Assert.Equal("Official", signed.State);
        Assert.Equal("approver-user", signed.LastModifiedBy);
    }

    [Fact]
    public async Task UpdateInstance_ShouldReplaceLoadedInstance()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var updated = await service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id,
            AsOfDate = new DateOnly(2026, 5, 1),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "NYC" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.11" }]
        }, writer, CancellationToken.None);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.Version + 1, updated.Version);
        Assert.Equal(new DateOnly(2026, 5, 1), updated.AsOfDate);

        var all = await service.GetInstancesAsync("FX_RATES", writer, CancellationToken.None);
        Assert.Single(all);
        Assert.Equal("NYC", all[0].Header["book"]?.ToString());
        Assert.Equal(new DateOnly(2026, 5, 1), all[0].AsOfDate);
        Assert.Equal(created.Version + 1, all[0].Version);
    }

    [Fact]
    public async Task UpdateInstance_AuditShouldIncludeChangedRows()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows =
            [
                new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" },
                new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.11" }
            ]
        }, writer, CancellationToken.None);

        await service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id,
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "NYC" },
            Rows =
            [
                new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.31" }
            ]
        }, writer, CancellationToken.None);

        var audit = await service.GetAuditAsync(admin, CancellationToken.None);
        var updateEvent = audit.Last(x => x.Action == "INSTANCE_UPDATE");

        Assert.Equal(2, updateEvent.RowChanges.Count);
        var updatedUsd = updateEvent.RowChanges.Single(x => x.Operation == "updated" && x.KeyFields["currency"] == "USD");
        var removedEur = updateEvent.RowChanges.Single(x => x.Operation == "removed" && x.KeyFields["currency"] == "EUR");

        Assert.Equal("1.24", updatedUsd.SourceValues!["rate"]);
        Assert.Equal("1.31", updatedUsd.TargetValues!["rate"]);
        Assert.Equal("1.11", removedEur.SourceValues!["rate"]);
        Assert.Null(removedEur.TargetValues);
    }

    [Fact]
    public async Task CreateInstance_AuditShouldIncludeAllSavedRows()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows =
            [
                new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" },
                new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.11" }
            ]
        }, writer, CancellationToken.None);

        var audit = await service.GetAuditAsync(admin, CancellationToken.None);
        var createEvent = audit.Last(x => x.Action == "INSTANCE_CREATE");

        Assert.Equal(2, createEvent.RowChanges.Count);
        Assert.Contains(createEvent.RowChanges, x => x.Operation == "added" && x.KeyFields["currency"] == "USD");
        Assert.Contains(createEvent.RowChanges, x => x.Operation == "added" && x.KeyFields["currency"] == "EUR");
    }

    [Fact]
    public async Task UpdateInstance_AuditShouldNoteRowsNotAuditedWhenNoKeyFieldsDefined()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(withKeyFields: false), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        await service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id,
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "NYC" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.11" }]
        }, writer, CancellationToken.None);

        var audit = await service.GetAuditAsync(admin, CancellationToken.None);
        var createEvent = audit.Last(x => x.Action == "INSTANCE_CREATE");
        var updateEvent = audit.Last(x => x.Action == "INSTANCE_UPDATE");

        Assert.Empty(createEvent.RowChanges);
        Assert.Empty(updateEvent.RowChanges);
    }

    [Fact]
    public async Task UpdateInstance_AuditShouldIncludeStateChange_WhenOnlyStateChanges()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        await service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            InstanceId = created.Id,
            AsOfDate = created.AsOfDate,
            State = "Scenario Testing",
            Header = new Dictionary<string, object?>(created.Header, StringComparer.OrdinalIgnoreCase),
            Rows = created.Rows.Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)).ToList()
        }, writer, CancellationToken.None);

        var audit = await service.GetAuditAsync(admin, CancellationToken.None);
        var updateEvent = audit.Last(x => x.Action == "INSTANCE_UPDATE");

        Assert.Empty(updateEvent.RowChanges);
    }

    [Fact]
    public async Task CreateInstance_WithResetVersion_ShouldRejectDuplicateHeader()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var first = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var duplicate = new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            ResetVersion = true,
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        };

        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.CreateInstanceAsync(duplicate, writer, CancellationToken.None));

        Assert.Equal(1, first.Version);
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateInstance_ShouldRejectDuplicateHeaderForSameAsOfDateAndState()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var duplicateRequest = new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.11" }]
        };

        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.CreateInstanceAsync(duplicateRequest, writer, CancellationToken.None));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateInstance_ShouldRejectWhenBecomingDuplicateHeader()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        var second = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "PendingApproval",
            Header = new Dictionary<string, object?> { ["book"] = "NYC" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.11" }]
        }, writer, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.UpdateInstanceAsync(new UpdateDatasetInstanceRequest
            {
                DatasetKey = "FX_RATES",
                InstanceId = second.Id,
                AsOfDate = new DateOnly(2026, 4, 3),
                State = "Draft",
                Header = new Dictionary<string, object?> { ["book"] = "LON" },
                Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.12" }]
            }, writer, CancellationToken.None));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInstances_WithMinAndMaxAsOfDate_ShouldReturnOnlyRangeMatches()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");

        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 1),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.10" }]
        }, writer, CancellationToken.None);

        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 2),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "NYC" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.11" }]
        }, writer, CancellationToken.None);

        await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "TKY" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.12" }]
        }, writer, CancellationToken.None);

        var results = await service.GetInstancesAsync(
            "FX_RATES",
            writer,
            CancellationToken.None,
            minAsOfDate: new DateOnly(2026, 4, 2),
            maxAsOfDate: new DateOnly(2026, 4, 3));

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, x => x.AsOfDate == new DateOnly(2026, 4, 1));
        Assert.Contains(results, x => x.AsOfDate == new DateOnly(2026, 4, 2));
        Assert.Contains(results, x => x.AsOfDate == new DateOnly(2026, 4, 3));
    }

    [Fact]
    public async Task CreateInstance_ShouldRejectDuplicateDetailRowsByConfiguredKeyFields()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);
        await service.UpsertSchemaAsync(CreateSchema(), admin, CancellationToken.None);

        var writer = CreateUser("writer");
        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.CreateInstanceAsync(new CreateDatasetInstanceRequest
            {
                DatasetKey = "FX_RATES",
                AsOfDate = new DateOnly(2026, 4, 3),
                State = "Draft",
                Header = new Dictionary<string, object?> { ["book"] = "LON" },
                Rows =
                [
                    new Dictionary<string, object?> { ["currency"] = "USD", ["rate"] = "1.24" },
                    new Dictionary<string, object?> { ["currency"] = "usd", ["rate"] = "1.30" }
                ]
            }, writer, CancellationToken.None));

        Assert.Contains("unique by key fields", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateInstance_WithLookupField_ShouldAllowValuesFromLatestOfficialLookupDataset()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(CreateLookupSourceSchema(), admin, CancellationToken.None);
        await service.UpsertSchemaAsync(CreateSchemaWithLookupField(), admin, CancellationToken.None);

        await repository.SaveInstanceAsync(new DatasetInstance
        {
            Id = Guid.NewGuid(),
            DatasetKey = "FX_LOOKUP",
            AsOfDate = new DateOnly(2026, 4, 2),
            State = "Official",
            Version = 1,
            Header = new Dictionary<string, object?> { ["book"] = "LOOKUP" },
            Rows =
            [
                new Dictionary<string, object?> { ["code"] = "USD", ["description"] = "US Dollar" },
                new Dictionary<string, object?> { ["code"] = "EUR", ["description"] = "Euro" }
            ],
            CreatedBy = "admin",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedBy = "admin",
            LastModifiedAtUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        var writer = CreateUser("writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.24" }]
        }, writer, CancellationToken.None);

        Assert.Equal("EUR", created.Rows[0]["currency"]?.ToString());
    }

    [Fact]
    public async Task CreateInstance_WithLookupField_ShouldAllowWhenWriterHasNoReadRoleOnLookupDataset()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(CreateLookupSourceSchema(read: ["LookupReader"]), admin, CancellationToken.None);
        await service.UpsertSchemaAsync(CreateSchemaWithLookupField(), admin, CancellationToken.None);

        await repository.SaveInstanceAsync(new DatasetInstance
        {
            Id = Guid.NewGuid(),
            DatasetKey = "FX_LOOKUP",
            AsOfDate = new DateOnly(2026, 4, 2),
            State = "Official",
            Version = 1,
            Header = new Dictionary<string, object?> { ["book"] = "LOOKUP" },
            Rows =
            [
                new Dictionary<string, object?> { ["code"] = "USD", ["description"] = "US Dollar" },
                new Dictionary<string, object?> { ["code"] = "EUR", ["description"] = "Euro" }
            ],
            CreatedBy = "admin",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedBy = "admin",
            LastModifiedAtUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        var writerWithoutLookupRead = CreateUser("writer", "writer");
        var created = await service.CreateInstanceAsync(new CreateDatasetInstanceRequest
        {
            DatasetKey = "FX_RATES",
            AsOfDate = new DateOnly(2026, 4, 3),
            State = "Draft",
            Header = new Dictionary<string, object?> { ["book"] = "LON" },
            Rows = [new Dictionary<string, object?> { ["currency"] = "EUR", ["rate"] = "1.24" }]
        }, writerWithoutLookupRead, CancellationToken.None);

        Assert.Equal("EUR", created.Rows[0]["currency"]?.ToString());
    }

    [Fact]
    public async Task CreateInstance_WithLookupField_ShouldRejectValuesNotInLookupDataset()
    {
        var repository = new InMemoryRepository();
        var service = new DatasetService(repository, new LookupValueCache());
        var admin = CreateUser("admin", DatasetAuthorizer.DatasetAdminRole);

        await service.UpsertSchemaAsync(CreateLookupSourceSchema(), admin, CancellationToken.None);
        await service.UpsertSchemaAsync(CreateSchemaWithLookupField(), admin, CancellationToken.None);

        await repository.SaveInstanceAsync(new DatasetInstance
        {
            Id = Guid.NewGuid(),
            DatasetKey = "FX_LOOKUP",
            AsOfDate = new DateOnly(2026, 4, 2),
            State = "Official",
            Version = 1,
            Header = new Dictionary<string, object?> { ["book"] = "LOOKUP" },
            Rows =
            [
                new Dictionary<string, object?> { ["code"] = "USD", ["description"] = "US Dollar" },
                new Dictionary<string, object?> { ["code"] = "EUR", ["description"] = "Euro" }
            ],
            CreatedBy = "admin",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastModifiedBy = "admin",
            LastModifiedAtUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        var writer = CreateUser("writer");
        var ex = await Assert.ThrowsAsync<DatasetServiceException>(() =>
            service.CreateInstanceAsync(new CreateDatasetInstanceRequest
            {
                DatasetKey = "FX_RATES",
                AsOfDate = new DateOnly(2026, 4, 3),
                State = "Draft",
                Header = new Dictionary<string, object?> { ["book"] = "LON" },
                Rows = [new Dictionary<string, object?> { ["currency"] = "GBP", ["rate"] = "1.24" }]
            }, writer, CancellationToken.None));

        Assert.Contains("lookup dataset", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must be one of", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static UserContext CreateUser(string userId, params string[] roles)
    {
        return new UserContext
        {
            UserId = userId,
            Roles = roles.ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static DatasetSchema CreateSchema(
        string key = "FX_RATES",
        IReadOnlyList<string>? read = null,
        bool withKeyFields = true,
        IReadOnlyList<string>? write = null,
        IReadOnlyList<string>? signoff = null)
    {
        return new DatasetSchema
        {
            Key = key,
            Name = "FX Rates",
            HeaderFields =
            [
                new SchemaField
                {
                    Name = "book",
                    Label = "Book",
                    Type = FieldType.String,
                    Required = true
                }
            ],
            DetailFields =
            [
                new SchemaField
                {
                    Name = "currency",
                    Label = "Currency",
                    Type = FieldType.Select,
                    IsKey = withKeyFields,
                    Required = true,
                    AllowedValues = ["USD", "EUR"]
                },
                new SchemaField
                {
                    Name = "rate",
                    Label = "Rate",
                    Type = FieldType.Number,
                    Required = true,
                    MinValue = 0
                }
            ],
            Permissions = new DatasetPermissions
            {
                ReadRoles = new HashSet<string>(read ?? ["viewer", "writer", "approver"], StringComparer.OrdinalIgnoreCase),
                WriteRoles = new HashSet<string>(write ?? ["writer"], StringComparer.OrdinalIgnoreCase),
                SignoffRoles = new HashSet<string>(signoff ?? ["approver"], StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private static DatasetSchema CreateSchemaWithLookupField(string key = "FX_RATES")
    {
        return new DatasetSchema
        {
            Key = key,
            Name = "FX Rates",
            HeaderFields =
            [
                new SchemaField
                {
                    Name = "book",
                    Label = "Book",
                    Type = FieldType.String,
                    Required = true
                }
            ],
            DetailFields =
            [
                new SchemaField
                {
                    Name = "currency",
                    Label = "Currency",
                    Type = FieldType.Lookup,
                    LookupDatasetKey = "FX_LOOKUP",
                    IsKey = true,
                    Required = true
                },
                new SchemaField
                {
                    Name = "rate",
                    Label = "Rate",
                    Type = FieldType.Number,
                    Required = true,
                    MinValue = 0
                }
            ],
            Permissions = new DatasetPermissions
            {
                ReadRoles = new HashSet<string>(["viewer", "writer", "approver"], StringComparer.OrdinalIgnoreCase),
                WriteRoles = new HashSet<string>(["writer"], StringComparer.OrdinalIgnoreCase),
                SignoffRoles = new HashSet<string>(["approver"], StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private static DatasetSchema CreateLookupSourceSchema(string key = "FX_LOOKUP", IReadOnlyList<string>? read = null)
    {
        return new DatasetSchema
        {
            Key = key,
            Name = "FX Lookup",
            HeaderFields =
            [
                new SchemaField
                {
                    Name = "book",
                    Label = "Book",
                    Type = FieldType.String,
                    Required = true
                }
            ],
            DetailFields =
            [
                new SchemaField
                {
                    Name = "code",
                    Label = "Code",
                    Type = FieldType.String,
                    Required = true
                },
                new SchemaField
                {
                    Name = "description",
                    Label = "Description",
                    Type = FieldType.String,
                    Required = false
                }
            ],
            Permissions = new DatasetPermissions
            {
                ReadRoles = new HashSet<string>(read ?? ["viewer", "writer", "approver"], StringComparer.OrdinalIgnoreCase),
                WriteRoles = new HashSet<string>(["writer"], StringComparer.OrdinalIgnoreCase),
                SignoffRoles = new HashSet<string>(["approver"], StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private static DatasetSchema WithDatasetAdminRoles(DatasetSchema schema, IReadOnlyList<string> roles)
    {
        return new DatasetSchema
        {
            Key = schema.Key,
            Name = schema.Name,
            Description = schema.Description,
            HeaderFields = schema.HeaderFields,
            DetailFields = schema.DetailFields,
            Permissions = new DatasetPermissions
            {
                ReadRoles = new HashSet<string>(schema.Permissions.ReadRoles, StringComparer.OrdinalIgnoreCase),
                WriteRoles = new HashSet<string>(schema.Permissions.WriteRoles, StringComparer.OrdinalIgnoreCase),
                SignoffRoles = new HashSet<string>(schema.Permissions.SignoffRoles, StringComparer.OrdinalIgnoreCase),
                DatasetAdminRoles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase)
            },
            CreatedAtUtc = schema.CreatedAtUtc,
            UpdatedAtUtc = schema.UpdatedAtUtc
        };
    }

    private sealed class InMemoryRepository : IDataRepository
    {
        private readonly List<DatasetSchema> _schemas = [];
        private readonly List<DatasetInstance> _instances = [];
        private readonly List<AuditEvent> _audit = [];

        public Task<IReadOnlyList<DatasetSchema>> GetSchemasAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DatasetSchema>>(_schemas.ToList());

        public Task<DatasetSchema?> GetSchemaAsync(string datasetKey, CancellationToken cancellationToken)
            => Task.FromResult(_schemas.FirstOrDefault(x => string.Equals(x.Key, datasetKey, StringComparison.OrdinalIgnoreCase)));

        public Task UpsertSchemaAsync(DatasetSchema schema, CancellationToken cancellationToken)
        {
            _schemas.RemoveAll(x => string.Equals(x.Key, schema.Key, StringComparison.OrdinalIgnoreCase));
            _schemas.Add(schema);
            return Task.CompletedTask;
        }

        public Task DeleteSchemaAsync(string datasetKey, CancellationToken cancellationToken)
        {
            _schemas.RemoveAll(x => string.Equals(x.Key, datasetKey, StringComparison.OrdinalIgnoreCase));
            _instances.RemoveAll(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DatasetInstance>> GetInstancesAsync(
            string datasetKey,
            CancellationToken cancellationToken,
            DateOnly? minAsOfDate = null,
            DateOnly? maxAsOfDate = null,
            string? state = null,
            IReadOnlyDictionary<string, string>? headerCriteria = null,
            bool includeDetails = true)
        {
            var query = _instances
                .Where(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase));

            if (minAsOfDate.HasValue)
            {
                query = query.Where(x => x.AsOfDate >= minAsOfDate.Value);
            }

            if (maxAsOfDate.HasValue)
            {
                query = query.Where(x => x.AsOfDate <= maxAsOfDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                var normalizedState = CanonicalToken(state);
                query = query.Where(x => string.Equals(CanonicalToken(x.State.ToString()), normalizedState, StringComparison.OrdinalIgnoreCase));
            }

            if (headerCriteria is not null)
            {
                foreach (var criterion in headerCriteria.Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value)))
                {
                    query = query.Where(instance =>
                    {
                        instance.Header.TryGetValue(criterion.Key, out var rawValue);
                        var actual = rawValue?.ToString() ?? string.Empty;
                        return actual.Contains(criterion.Value, StringComparison.OrdinalIgnoreCase);
                    });
                }
            }

            return Task.FromResult<IReadOnlyList<DatasetInstance>>(query.ToList());
        }

        public async Task<DatasetReadTrace> GetInstancesWithTraceAsync(
            string datasetKey,
            CancellationToken cancellationToken,
            DateOnly? minAsOfDate = null,
            DateOnly? maxAsOfDate = null,
            string? state = null,
            IReadOnlyDictionary<string, string>? headerCriteria = null,
            bool includeDetails = true)
        {
            var instances = await GetInstancesAsync(datasetKey, cancellationToken, minAsOfDate, maxAsOfDate, state, headerCriteria);
            return new DatasetReadTrace
            {
                Instances = instances,
                LoadedFilenames = [],
                DetailFilesRead = instances.Count,
                DetailFilesTotal = instances.Count,
                UsedFilteredSearchPath = minAsOfDate.HasValue || maxAsOfDate.HasValue || !string.IsNullOrWhiteSpace(state) || (headerCriteria?.Count > 0)
            };
        }

        private static string CanonicalToken(string value)
        {
            return new string(value
                .Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-')
                .ToArray())
                .ToUpperInvariant();
        }

        public Task<DatasetInstance?> GetInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
            => Task.FromResult(_instances.FirstOrDefault(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase) && x.Id == instanceId));

        public Task SaveInstanceAsync(DatasetInstance instance, CancellationToken cancellationToken)
        {
            _instances.Add(instance);
            return Task.CompletedTask;
        }

        public Task<bool> ReplaceInstanceAsync(DatasetInstance instance, CancellationToken cancellationToken, DatasetInstance? existing = null)
        {
            var index = _instances.FindIndex(x =>
                string.Equals(x.DatasetKey, instance.DatasetKey, StringComparison.OrdinalIgnoreCase) &&
                x.Id == instance.Id);

            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _instances[index] = instance;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteInstanceAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
        {
            var removed = _instances.RemoveAll(x =>
                string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase) &&
                x.Id == instanceId);

            return Task.FromResult(removed > 0);
        }

        public Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(string? datasetKey, CancellationToken cancellationToken)
        {
            var filtered = string.IsNullOrWhiteSpace(datasetKey)
                ? _audit
                : _audit.Where(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase)).ToList();

            return Task.FromResult<IReadOnlyList<AuditEvent>>(filtered.ToList());
        }

        public Task<(Guid Id, int Version)?> GetLatestInstanceVersionAsync(string datasetKey, string state, CancellationToken cancellationToken)
        {
            var result = _instances
                .Where(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(CanonicalToken(x.State.ToString()), CanonicalToken(state), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.AsOfDate)
                .ThenByDescending(x => x.Version)
                .Select(x => ((Guid Id, int Version)?)(x.Id, x.Version))
                .FirstOrDefault();
            return Task.FromResult(result);
        }

        public Task<DatasetInstance?> GetLatestInstanceAsync(string datasetKey, string state, CancellationToken cancellationToken)
        {
            var result = _instances
                .Where(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(CanonicalToken(x.State.ToString()), CanonicalToken(state), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.AsOfDate)
                .ThenByDescending(x => x.Version)
                .FirstOrDefault();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AuditEvent>> GetInstanceAuditHistoryAsync(string datasetKey, Guid instanceId, CancellationToken cancellationToken)
        {
            var events = _audit
                .Where(x => string.Equals(x.DatasetKey, datasetKey, StringComparison.OrdinalIgnoreCase)
                         && x.DatasetInstanceId == instanceId)
                .OrderByDescending(x => x.OccurredAtUtc)
                .ToList();

            // Mirror the stop-at-first-signoff behaviour of BlobDataRepository.
            var result = new List<AuditEvent>();
            foreach (var e in events)
            {
                result.Add(e);
                if (string.Equals(e.Action, "INSTANCE_SIGNOFF", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            return Task.FromResult<IReadOnlyList<AuditEvent>>(result);
        }

        public Task AddAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            _audit.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}

