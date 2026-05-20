namespace SemaRepair.Api.Services.Interfaces;

/// <summary>
/// Finds car configurations that match a natural language description.
/// Used during the car identification phase of the conversation.
/// </summary>
public interface ICarSearchService
{
    /// <summary>
    /// Embeds the query and returns the top K most similar car configurations.
    /// Uses cosine similarity on the car_embeddings table.
    /// </summary>
    /// <param name="query">
    /// Natural language description of the car.
    /// Example: "ho un Fiat Ducato diesel del 2004"
    /// </param>
    /// <param name="topK">Number of results to return. Default: 3.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CarSearchResult>> FindAsync(
        string query,
        int topK = 3,
        CancellationToken ct = default);
}
