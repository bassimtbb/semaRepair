using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Scalar.AspNetCore;
using SemaRepair.Api.Data;
using SemaRepair.Api.Services;
using SemaRepair.Api.Services.Interfaces;
using SemaRepair.Api.Startup;

// Prevent unhandled background exceptions from killing the process.
// ASP.NET Core recovers per-request, but AppDomain crashes are fatal.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    Console.Error.WriteLine($"[FATAL] Unhandled exception: {ex?.Message}");
    Console.Error.WriteLine(ex?.StackTrace);
};

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
builder.Services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>();

// Named HttpClient for GeminiChatService (still used by TranscriptionController).
builder.Services.AddHttpClient<IChatService, GeminiChatService>();

// ── Startup background service ────────────────────────────────────────────────
// Runs once on startup to generate any missing embeddings.
builder.Services.AddHostedService<EmbeddingStartupService>();

// ── Search services ───────────────────────────────────────────────────────────
builder.Services.AddScoped<ICarSearchService, CarSearchService>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();

// ── Repair orchestration ──────────────────────────────────────────────────────
// RepairPlugin executes tool calls against the database and search services.
builder.Services.AddScoped<RepairPlugin>();

// RepairOrchestrator coordinates Gemini function calling.
// AddHttpClient<T> handles both DI registration and typed HttpClient injection.
builder.Services.AddHttpClient<RepairOrchestrator>();

// ── CORS ──────────────────────────────────────────────────────────────────────
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
