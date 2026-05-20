using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SemaRepair.Api.Data;
using SemaRepair.Api.Services.Interfaces;

namespace SemaRepair.Api.Services;

/// <summary>
/// Finds matching car configurations using semantic vector similarity.
///
/// Flow:
///   1. Embed the mechanic's natural language car description
///   2. Search car_embeddings by cosine distance (ascending = most similar first)
///   3. Return the top K results as CarSearchResult records
/// </summary>
public sealed class CarSearchService : ICarSearchService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<CarSearchService> _logger;

    public CarSearchService(
        AppDbContext db,
        IEmbeddingService embedding,
        ILogger<CarSearchService> logger)
    {
        _db = db;
        _embedding = embedding;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CarSearchResult>> FindAsync(
        string query,
        int topK = 3,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Car search query: {Query}", query);

        // Embed the mechanic's description of their car
        var floats = await _embedding.EmbedAsync(query, ct);
        var queryVector = new Pgvector.Vector(floats);

        // Order by cosine distance ascending (closest = most similar)
        var results = await _db.CarEmbeddings
            .Where(c => c.Embedding != null)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .Select(c => new CarSearchResult(
                c.IdMacchina,
                c.MarcaMacchina,
                c.ModelloMacchina,
                c.MotorizzazioneMacchina ?? string.Empty,
                c.CodiceMotoreMacchina,
                c.AlimentazioneMacchina ?? string.Empty,
                c.AnnoInizio,
                c.AnnoFine,
                c.Kw,
                c.Cavalli))
            .ToListAsync(ct);

        _logger.LogDebug("Car search returned {Count} results.", results.Count);
        return results;
    }
}
