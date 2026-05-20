using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SemaRepair.Api.Dtos;
using SemaRepair.Api.Services.Interfaces;

namespace SemaRepair.Api.Services;

/// <summary>
/// Streams responses from Google Gemini 2.5 Flash.
///
/// Key configuration decisions:
///   - responseMimeType: "application/json" forces structured JSON output
///   - thinkingBudget: 0 disables internal reasoning tokens that would
///     consume the output token budget and cause truncation
///   - maxOutputTokens: 8192 gives enough space for 3 repair cases
///   - temperature: 0.1 keeps the output factual and consistent
///   - Model: gemini-2.5-flash — fast and cost-effective for this use case
/// </summary>
public sealed class GeminiChatService : IChatService
{
    // SSE streaming endpoint — alt=sse returns server-sent events
    private const string StreamUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:streamGenerateContent";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiChatService> _logger;

    public GeminiChatService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiChatService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"]
            ?? throw new InvalidOperationException(
                "GEMINI_API_KEY is not configured.");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"{StreamUrl}?key={_apiKey}&alt=sse";

        // Build the contents array from history + current message.
        // Gemini uses "model" instead of "assistant" for the AI role.
        var contents = history
            .Select(h => new
            {
                role  = h.Role == "assistant" ? "model" : h.Role,
                parts = new[] { new { text = h.Content } }
            })
            .Cast<object>()
            .Append(new
            {
                role  = "user",
                parts = new[] { new { text = userMessage } }
            })
            .ToList();

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents,
            generationConfig = new
            {
                // Force JSON output — LLM cannot add markdown or prose
                responseMimeType = "application/json",
                // Disable thinking tokens — they consume output budget
                // and cause JSON to be truncated in gemini-2.5-flash
                thinkingConfig = new { thinkingBudget = 0 },
                maxOutputTokens = 8192,
                temperature     = 0.1
            }
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody)
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError("Gemini chat request failed: {Error}", ex.Message);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Gemini chat API error {Status}: {Body}",
                response.StatusCode,
                error[..Math.Min(300, error.Length)]);
            throw new HttpRequestException(
                $"Gemini API returned {response.StatusCode}");
        }

        // Read the SSE stream line by line
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            // SSE lines with data start with "data: "
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]") break;

            // Parse the SSE chunk and extract the text delta
            JsonElement doc;
            try
            {
                doc = JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (JsonException)
            {
                // Malformed chunk — skip and continue
                continue;
            }

            // Navigate: candidates[0].content.parts[0].text
            if (!doc.TryGetProperty("candidates", out var candidates)) continue;
            if (candidates.GetArrayLength() == 0) continue;
            if (!candidates[0].TryGetProperty("content", out var content)) continue;
            if (!content.TryGetProperty("parts", out var parts)) continue;
            if (parts.GetArrayLength() == 0) continue;
            if (!parts[0].TryGetProperty("text", out var textEl)) continue;

            var chunk = textEl.GetString();
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }
}
