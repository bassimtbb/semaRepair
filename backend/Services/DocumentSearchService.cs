using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SemaRepair.Api.Data;
using SemaRepair.Api.Data.Entities;
using SemaRepair.Api.Services.Interfaces;

namespace SemaRepair.Api.Services;

/// <summary>
/// Searches repair documents using full-text or semantic search.
/// Always loads the related RepairDocumentCars via Include() so that
/// callers receive the complete picture of which cars each document applies to.
/// </summary>
public sealed class DocumentSearchService : IDocumentSearchService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<DocumentSearchService> _logger;

    public DocumentSearchService(
        AppDbContext db,
        IEmbeddingService embedding,
        ILogger<DocumentSearchService> logger)
    {
        _db = db;
        _embedding = embedding;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepairDocumentResult>> SearchByDtcAsync(
        string dtcCode,
        string? codiceMotore = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "DTC search: code={Code}, engine={Engine}", dtcCode, codiceMotore);

        // Normalize the DTC code — always uppercase, no spaces
        var normalizedCode = dtcCode.Trim().ToUpperInvariant();

        // Use PostgreSQL full-text search on the pre-built search_vector column.
        // to_tsquery('simple', ...) matches the code exactly without stemming.
        var query = _db.RepairDocuments
            .Where(rd => rd.SearchVector!
                .Matches(EF.Functions.ToTsQuery("simple", normalizedCode)));

        // If a car is confirmed, restrict to documents that apply to that engine
        if (codiceMotore is not null)
        {
            query = query.Where(rd =>
                rd.Cars.Any(c => c.CodiceMotoreMacchina == codiceMotore));
        }

        var documents = await query
            .Include(rd => rd.Cars)
            .OrderBy(rd => rd.SiglaDocumento)
            .ToListAsync(ct);

        _logger.LogDebug(
            "DTC search returned {Count} documents.", documents.Count);

        return documents.Select(ToResult).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepairDocumentResult>> SearchBySymptomAsync(
        string symptom,
        string? codiceMotore = null,
        int topK = 3,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Semantic search: symptom={Symptom}, engine={Engine}",
            symptom, codiceMotore);

        // Embed the symptom description
        var floats = await _embedding.EmbedAsync(symptom, ct);
        var queryVector = new Pgvector.Vector(floats);

        var query = _db.RepairDocuments
            .Where(rd => rd.Embedding != null);

        // Filter to confirmed car's engine before ranking by similarity.
        // This is critical — without it we get results from unrelated cars.
        if (codiceMotore is not null)
        {
            query = query.Where(rd =>
                rd.Cars.Any(c => c.CodiceMotoreMacchina == codiceMotore));
        }

        var documents = await query
            .OrderBy(rd => rd.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .Include(rd => rd.Cars)
            .ToListAsync(ct);

        _logger.LogDebug(
            "Semantic search returned {Count} documents.", documents.Count);

        if (documents.Count == 0)
        {
            _logger.LogInformation(
                "Semantic search returned 0 results for engine={Engine}. Falling back to keyword search.",
                codiceMotore);
            return await SearchByKeywordAsync(symptom, codiceMotore, topK, ct);
        }

        return documents.Select(ToResult).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RepairDocumentResult>> SearchBySymptomNoCarAsync(
        string symptom,
        int topK = 1,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Symptom-only search (no car filter): {Symptom}", symptom);

        var floats = await _embedding.EmbedAsync(symptom, ct);
        var queryVector = new Pgvector.Vector(floats);

        // Search all documents without any car filter
        var documents = await _db.RepairDocuments
            .Where(rd => rd.Embedding != null)
            .OrderBy(rd => rd.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .Include(rd => rd.Cars)
            .ToListAsync(ct);

        // Keyword fallback if semantic search returns nothing
        if (documents.Count == 0)
        {
            _logger.LogInformation(
                "Symptom-only semantic search returned 0 results. " +
                "Falling back to keyword search.");
            return await SearchByKeywordAsync(symptom, null, topK, ct);
        }

        _logger.LogDebug(
            "Symptom-only search returned {Count} documents.", documents.Count);

        return documents.Select(ToResult).ToList();
    }

    /// <summary>
    /// Keyword fallback: searches titolo_documento and parole_chiave using
    /// the pre-built search_vector GIN index with an OR tsquery.
    /// Used when semantic search returns no results for the confirmed engine.
    /// </summary>
    private async Task<IReadOnlyList<RepairDocumentResult>> SearchByKeywordAsync(
        string query,
        string? codiceMotore = null,
        int topK = 3,
        CancellationToken ct = default)
    {
        // Keep words that are 3+ chars and contain no tsquery operator characters
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && w.All(c => c != '&' && c != '|' && c != '!' && c != '(' && c != ')' && c != ':'))
            .ToList();

        if (words.Count == 0)
            return [];

        // Build an OR tsquery: word1 | word2 | word3 …
        // Uses the existing GIN index on search_vector for efficiency.
        var tsQueryString = string.Join(" | ", words);

        var dbQuery = _db.RepairDocuments
            .Where(rd => rd.SearchVector != null);

        if (codiceMotore is not null)
            dbQuery = dbQuery.Where(rd =>
                rd.Cars.Any(c => c.CodiceMotoreMacchina == codiceMotore));

        var results = await dbQuery
            .Where(rd => rd.SearchVector!.Matches(
                EF.Functions.ToTsQuery("simple", tsQueryString)))
            .Include(rd => rd.Cars)
            .Take(topK)
            .ToListAsync(ct);

        _logger.LogInformation(
            "Keyword fallback returned {Count} documents for engine={Engine}.",
            results.Count, codiceMotore);

        return results.Select(ToResult).ToList();
    }

    /// <summary>
    /// Maps a RepairDocumentEntity to a RepairDocumentResult record.
    /// </summary>
    private static RepairDocumentResult ToResult(RepairDocumentEntity rd) =>
        new(
            rd.SiglaDocumento,
            rd.TitoloDocumento   ?? string.Empty,
            rd.ParoleChiave      ?? string.Empty,
            rd.GradoAttendibilita,
            rd.Identificazione   ?? string.Empty,
            rd.Procedura         ?? string.Empty,
            rd.Cars.Select(c => new CarInfo(
                c.IdMacchina,
                c.MarcaMacchina,
                c.ModelloMacchina,
                c.MotorizzazioneMacchina ?? string.Empty,
                c.CodiceMotoreMacchina,
                c.AlimentazioneMacchina  ?? string.Empty,
                c.AnnoInizioMacchina,
                c.AnnoFineMacchina,
                c.KwMacchina,
                c.CavalliMacchina
            )).ToList()
        );
}
