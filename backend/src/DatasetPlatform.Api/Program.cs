// ─────────────────────────────────────────────────────────────────────────────
// Program.cs — ASP.NET Core application entry point
//
// Responsibilities:
//   1. Configure dependency injection (DI) — select storage provider, register services.
//   2. Build the middleware pipeline — CORS, HTTPS redirect, diagnostics logging, routing.
//   3. Start the web host.
//
// Storage provider is selected at startup based on appsettings.json "Storage:Provider":
//   - "LocalFile" (default) → FileSystemBlobStore  → reads/writes ./data/ on disk
//   - "S3"                  → S3BlobStore           → reads/writes AWS S3 bucket
//
// Both providers share the same BlobDataRepository and DatasetService; only the
// blob store implementation changes, making it easy to add new backends later.
// ─────────────────────────────────────────────────────────────────────────────
using DatasetPlatform.Api.Infrastructure;
using DatasetPlatform.Application.Abstractions;
using DatasetPlatform.Application.Services;
using Amazon;
using Amazon.S3;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<ApiDiagnosticsOptions>(builder.Configuration.GetSection(ApiDiagnosticsOptions.SectionName));
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
if (string.Equals(storageOptions.Provider, StorageProviders.S3, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAmazonS3>(_ =>
    {
        var s3 = storageOptions.S3;
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Region),
            ForcePathStyle = s3.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            config.ServiceURL = s3.ServiceUrl;
            config.ForcePathStyle = true;
        }

        return new AmazonS3Client(config);
    });
    builder.Services.AddSingleton<IBlobStore, S3BlobStore>();
    builder.Services.AddSingleton<IDataRepository, BlobDataRepository>();
}
else
{
    builder.Services.AddSingleton<IBlobStore, FileSystemBlobStore>();
    builder.Services.AddSingleton<IDataRepository, BlobDataRepository>();
}
builder.Services.AddScoped<IDatasetService, DatasetService>();
builder.Services.AddScoped<IRequestUserContextAccessor, RequestUserContextAccessor>();
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseHttpsRedirection();
app.UseMiddleware<ApiDiagnosticsMiddleware>();
app.MapControllers();

app.Run();

public partial class Program;
