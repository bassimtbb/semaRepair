using System.Net.Http.Json;
using System.Text.Json;
using SemaRepair.Api.Services.Interfaces;

namespace SemaRepair.Api.Services;

/// <summary>
/// Generates text embeddings using Google Gemini text-embedding-001.
///
/// Model: gemini-embedding-001
/// Output dimensions: 768 (compatible with pgvector HNSW index limit of 2000)
/// API version: v1beta (required for gemini-embedding-001)
/// </summary>
public sealed class GeminiEmbeddingService : IEmbeddingService
{
    // Gemini embedding endpoint — v1beta required for gemini-embedding-001
    private const string EmbeddingUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent";

    // Must match the vector(768) column type in PostgreSQL
    private const int OutputDimensions = 768;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    public GeminiEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"]
            ?? throw new InvalidOperationException(
                "GEMINI_API_KEY is not configured. " +
                "Add it to .env and verify docker-compose.yml passes it to the backend.");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var url = $"{EmbeddingUrl}?key={_apiKey}";

        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new { parts = new[] { new { text } } },
            // outputDimensionality keeps vectors within pgvector HNSW 2000-dim limit
            outputDimensionality = OutputDimensions
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Gemini embedding failed. Status: {Status}. Body: {Body}",
                response.StatusCode, error[..Math.Min(300, error.Length)]);
            throw new HttpRequestException(
                $"Gemini embedding API returned {response.StatusCode}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: ct);

        // Response shape: { "embedding": { "values": [0.1, 0.2, ...] } }
        return json
            .GetProperty("embedding")
            .GetProperty("values")
            .EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();
    }
}
