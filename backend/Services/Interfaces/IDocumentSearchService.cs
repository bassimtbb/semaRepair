namespace SemaRepair.Api.Services.Interfaces;

/// <summary>
/// Searches the repair_documents knowledge base.
///
/// Two search strategies:
///   Full-text  — exact DTC code match via PostgreSQL tsvector (fast, precise)
///   Semantic   — symptom similarity via pgvector cosine distance (flexible)
///
/// Both support optional filtering by CodiceMotoreMacchina to restrict
/// results to documents that apply to the confirmed car engine.
/// </summary>
public interface IDocumentSearchService
{
    /// <summary>
    /// Searches by DTC fault code using PostgreSQL full-text search.
    /// Best for queries like "P2279", "C1110", "ho il codice P0403".
    /// Returns all documents where parole_chiave contains the exact code.
    /// </summary>
    /// <param name="dtcCode">OBD-II fault code, e.g. "P2279".</param>
    /// <param name="codiceMotore">
    /// Optional engine code filter. If provided, only returns documents
    /// that apply to this specific engine configuration.
    /// </param>
    Task<IReadOnlyList<RepairDocumentResult>> SearchByDtcAsync(
        string dtcCode,
        string? codiceMotore = null,
        string? brand = null,
        CancellationToken ct = default);

    /// <summary>
    /// Searches by symptom description using vector similarity.
    /// Best for queries like "spia motore accesa scarse prestazioni".
    /// Embeds the query and finds semantically similar documents.
    /// </summary>
    /// <param name="symptom">Free-text symptom description in Italian.</param>
    /// <param name="codiceMotore">
    /// Optional engine code filter. Always provide this when the car
    /// is confirmed — it dramatically improves result relevance.
    /// </param>
    /// <param name="brand">Optional brand/model filter (e.g. "FORD Fiesta").</param>
    /// <param name="motorizzazione">Optional motorizzazione partial match (e.g. "1.0 EcoBoost 12v").</param>
    /// <param name="fuel">Optional fuel type filter (e.g. "Diesel", "Benzina").</param>
    /// <param name="kw">Optional exact kW filter.</param>
    /// <param name="yearFrom">Optional minimum production year.</param>
    /// <param name="yearTo">Optional maximum production year.</param>
    /// <param name="topK">Number of documents to return. Default: 3.</param>
    Task<IReadOnlyList<RepairDocumentResult>> SearchBySymptomAsync(
        string symptom,
        string? codiceMotore = null,
        string? brand = null,
        string? motorizzazione = null,
        string? fuel = null,
        int? kw = null,
        int? yearFrom = null,
        int? yearTo = null,
        int topK = 3,
        CancellationToken ct = default);

    /// <summary>
    /// Searches repair documents by symptom with NO car filter.
    /// Used when the mechanic describes a problem without specifying a car.
    /// Returns documents ranked by semantic similarity.
    /// The caller uses the Cars property of each result to present
    /// which vehicles have this problem documented.
    /// </summary>
    Task<IReadOnlyList<RepairDocumentResult>> SearchBySymptomNoCarAsync(
        string symptom,
        int topK = 1,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches a single repair document by its unique sigla identifier.
    /// Returns null if not found.
    /// </summary>
    Task<RepairDocumentResult?> GetBySiglaAsync(
        string siglaDocumento,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches the highest-grade repair document associated with a given car.
    /// Used as a fallback when siglaDocumento was corrupted by the LLM.
    /// Returns null if no document is associated with this car.
    /// </summary>
    Task<RepairDocumentResult?> GetByCarIdAsync(
        string idMacchina,
        CancellationToken ct = default);

    /// <summary>
    /// Finds an alternative repair document for the same symptom,
    /// excluding the already-shown document (excludeSigla).
    ///
    /// Strategy:
    ///   1. Semantic search with engineCode filter, excluding excludeSigla → return if found.
    ///   2. If nothing found for this engine: broader semantic search (no engine filter),
    ///      still excluding excludeSigla → return up to 3 titles as suggestions.
    /// </summary>
    Task<AlternativeDocumentResult> FindAlternativeAsync(
        string symptom,
        string? engineCode,
        string excludeSigla,
        CancellationToken ct = default);
}
