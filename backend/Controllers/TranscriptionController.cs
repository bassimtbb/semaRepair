using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace SemaRepair.Api.Controllers;

/// <summary>
/// Handles audio transcription using Gemini AI.
/// The frontend sends raw audio as base64, this endpoint
/// returns the Italian transcript.
/// The API key stays on the server — never exposed to the browser.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class TranscriptionController : ControllerBase
{
    private const string GeminiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<TranscriptionController> _logger;

    public TranscriptionController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TranscriptionController> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = configuration["GEMINI_API_KEY"]
            ?? throw new InvalidOperationException("GEMINI_API_KEY not configured.");
        _logger = logger;
    }

    /// <summary>
    /// Transcribes audio to Italian text using Gemini.
    /// Accepts audio as base64 encoded string.
    /// </summary>
    [HttpPost("transcribe")]
    public async Task<IActionResult> TranscribeAsync(
        [FromBody] TranscribeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AudioBase64))
            return BadRequest(new { error = "Audio data is required." });

        try
        {
            var url = $"{GeminiUrl}?key={_apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = request.MimeType ?? "audio/webm",
                                    data = request.AudioBase64
                                }
                            },
                            new
                            {
                                text = "Trascrivi esattamente in italiano " +
                                       "quello che viene detto in questo audio. " +
                                       "Restituisci SOLO il testo trascritto, " +
                                       "senza spiegazioni o commenti. " +
                                       "Mantieni la terminologia tecnica automobilistica."
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0,
                    maxOutputTokens = 500
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Gemini transcription failed: {Status} {Body}",
                    response.StatusCode,
                    errorBody[..Math.Min(200, errorBody.Length)]);
                return StatusCode(500, new { error = "Transcription failed." });
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: ct);

            var transcript = json
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim() ?? string.Empty;

            _logger.LogInformation(
                "Transcription successful: {Transcript}", transcript);

            return Ok(new { transcript });
        }
        catch (Exception ex)
        {
            _logger.LogError("Transcription error: {Error}", ex.Message);
            return StatusCode(500, new { error = "Transcription failed." });
        }
    }
}

/// <summary>Request body for POST /api/transcription/transcribe</summary>
public sealed record TranscribeRequest(
    /// <summary>Base64-encoded audio data</summary>
    string AudioBase64,
    /// <summary>MIME type of the audio (e.g. audio/webm, audio/mp4)</summary>
    string? MimeType
);
