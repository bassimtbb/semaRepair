namespace SemaRepair.Api.Services.Interfaces;

/// <summary>
/// Generates vector embeddings for text using an external AI model.
/// Abstracted as an interface so it can be mocked in tests.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Embeds the given text and returns a 768-dimension float array.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Array of 768 floats representing the semantic vector.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
