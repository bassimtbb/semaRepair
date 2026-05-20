using Microsoft.EntityFrameworkCore;
using Pgvector;
using SemaRepair.Api.Data;
using SemaRepair.Api.Services.Interfaces;

namespace SemaRepair.Api.Startup;

/// <summary>
/// Background service that runs once on backend startup.
/// Generates vector embeddings for any rows that have embed_text
/// but no embedding yet.
///
/// This makes the backend self-completing:
/// - First startup: generates 108 document embeddings + 157 car embeddings
/// - Subsequent startups: finds nothing to do and exits immediately
///
/// Two tables are processed:
///   repair_documents  — 108 rows, embed_text = title + keywords + identificazione
///   car_embeddings    — 157 rows, embed_text = brand + model + engine + fuel + power + years
///
/// Batch size: 20 rows per Gemini API call (API limit)
/// Delay between batches: 500ms (free-tier rate limit protection)
/// </summary>
public sealed class EmbeddingStartupService : BackgroundService
{
    // Number of rows per Gemini batchEmbedContents call
    private const int BatchSize = 20;

    // Delay between batches to stay within Gemini free-tier rate limits
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(500);

    private readonly IServiceProvider _services;
    private readonly ILogger<EmbeddingStartupService> _logger;

    public EmbeddingStartupService(
        IServiceProvider services,
        ILogger<EmbeddingStartupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Use a scope because AppDbContext is scoped, not singleton
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var embedder = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

        _logger.LogInformation("EmbeddingStartupService starting.");

        await EmbedRepairDocumentsAsync(db, embedder, stoppingToken);
        await EmbedCarConfigurationsAsync(db, embedder, stoppingToken);

        _logger.LogInformation("EmbeddingStartupService complete.");
    }

    /// <summary>
    /// Generates embeddings for repair_documents rows where embedding IS NULL.
    /// Skips rows where embed_text is also NULL (nothing to embed).
    /// </summary>
    private async Task EmbedRepairDocumentsAsync(
        AppDbContext db,
        IEmbeddingService embedder,
        CancellationToken ct)
    {
        var pending = await db.RepairDocuments
            .Where(r => r.Embedding == null && r.EmbedText != null)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            _logger.LogInformation(
                "repair_documents: all embeddings already generated. Skipping.");
            return;
        }

        _logger.LogInformation(
            "repair_documents: generating embeddings for {Count} documents.", pending.Count);

        int embedded = 0;
        int errors = 0;

        for (int i = 0; i < pending.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = pending.Skip(i).Take(BatchSize).ToList();

            foreach (var doc in batch)
            {
                try
                {
                    var floats = await embedder.EmbedAsync(doc.EmbedText!, ct);
                    doc.Embedding = new Vector(floats);
                    embedded++;
                }
                catch (Exception ex)
                {
                    // Log and continue — do not let one failure block the rest
                    _logger.LogWarning(
                        "Failed to embed repair document {Sigla}: {Error}",
                        doc.SiglaDocumento, ex.Message);
                    errors++;
                }
            }

            // Save each batch to avoid losing progress on interruption
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "repair_documents: {Embedded}/{Total} embedded, {Errors} errors.",
                embedded, pending.Count, errors);

            // Respect Gemini free-tier rate limit between batches
            if (i + BatchSize < pending.Count)
                await Task.Delay(BatchDelay, ct);
        }

        _logger.LogInformation(
            "repair_documents: complete. {Embedded} embedded, {Errors} errors.",
            embedded, errors);
    }

    /// <summary>
    /// Generates embeddings for car_embeddings rows where embedding IS NULL.
    /// Skips rows where embed_text is also NULL.
    /// </summary>
    private async Task EmbedCarConfigurationsAsync(
        AppDbContext db,
        IEmbeddingService embedder,
        CancellationToken ct)
    {
        var pending = await db.CarEmbeddings
            .Where(c => c.Embedding == null && c.EmbedText != null)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            _logger.LogInformation(
                "car_embeddings: all embeddings already generated. Skipping.");
            return;
        }

        _logger.LogInformation(
            "car_embeddings: generating embeddings for {Count} car configurations.",
            pending.Count);

        int embedded = 0;
        int errors = 0;

        for (int i = 0; i < pending.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = pending.Skip(i).Take(BatchSize).ToList();

            foreach (var car in batch)
            {
                try
                {
                    var floats = await embedder.EmbedAsync(car.EmbedText!, ct);
                    car.Embedding = new Vector(floats);
                    embedded++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Failed to embed car {IdMacchina}: {Error}",
                        car.IdMacchina, ex.Message);
                    errors++;
                }
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "car_embeddings: {Embedded}/{Total} embedded, {Errors} errors.",
                embedded, pending.Count, errors);

            if (i + BatchSize < pending.Count)
                await Task.Delay(BatchDelay, ct);
        }

        _logger.LogInformation(
            "car_embeddings: complete. {Embedded} embedded, {Errors} errors.",
            embedded, errors);
    }
}
