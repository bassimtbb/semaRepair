using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Scalar.AspNetCore;
using SemaRepair.Api.Data;
using SemaRepair.Api.Dtos;
using SemaRepair.Api.Prompts;
using SemaRepair.Api.Services;
using SemaRepair.Api.Services.Interfaces;
using SemaRepair.Api.Startup;
using SemaRepair.Api.Utils;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// OpenAPI documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title   = "SemaRepair API",
        Version = "v1",
    });
});

// ── Database ──────────────────────────────────────────────────────────────────
// Connects to the PostgreSQL database created by the dataseeder.
// UseVector() enables pgvector cosine similarity search.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.UseVector()
    )
);

// ── HTTP clients ──────────────────────────────────────────────────────────────
// Untyped registration ensures IHttpClientFactory is available for injection
// (used by TranscriptionController).
builder.Services.AddHttpClient();

// Named HttpClient for GeminiEmbeddingService.
// Using AddHttpClient<T> creates a typed client with proper lifecycle management.
builder.Services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>();

// Named HttpClient for GeminiChatService
builder.Services.AddHttpClient<IChatService, GeminiChatService>();

// ── Startup background service ────────────────────────────────────────────────
// Runs once on startup to generate any missing embeddings.
// Idempotent — skips tables that already have all embeddings.
builder.Services.AddHostedService<EmbeddingStartupService>();

// ── Search services ───────────────────────────────────────────────────────────
// Scoped — new instance per HTTP request, shares the DbContext lifetime.
builder.Services.AddScoped<ICarSearchService, CarSearchService>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();

// ── CORS ──────────────────────────────────────────────────────────────────────
// Allow the React frontend to call the API from the browser.
// In production this should be restricted to the actual domain.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

// Must be before MapControllers
app.UseCors();

// Scalar UI available at /scalar/v1
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "openapi/{documentName}.json";
    });
    app.MapScalarApiReference(options =>
    {
        options.Title = "SemaRepair API";
    });
}

app.MapControllers();
app.Run();
