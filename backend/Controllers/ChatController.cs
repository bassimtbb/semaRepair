using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SemaRepair.Api.Dtos;
using SemaRepair.Api.Prompts;
using SemaRepair.Api.Services;
using SemaRepair.Api.Services.Interfaces;
using SemaRepair.Api.Utils;

namespace SemaRepair.Api.Controllers;

/// <summary>
/// Handles the streaming chat endpoint.
///
/// This controller has zero business logic.
/// It only:
///   1. Reads the request
///   2. Detects the DTC code (if any)
///   3. Calls the appropriate service based on what we have
///   4. Builds the system prompt
///   5. Streams the LLM response back as SSE
///
/// Routing logic (in order of priority):
///   DTC detected + car is null   → DTC car selection
///   DTC detected + car confirmed → Repair answer filtered by DTC
///   No DTC      + car is null    → Car identification
///   No DTC      + car confirmed  → Repair answer by symptom
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ChatController : ControllerBase
{
    private readonly ICarSearchService       _carSearch;
    private readonly IDocumentSearchService  _docSearch;
    private readonly IChatService            _chat;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        ICarSearchService carSearch,
        IDocumentSearchService docSearch,
        IChatService chat,
        ILogger<ChatController> logger)
    {
        _carSearch = carSearch;
        _docSearch = docSearch;
        _chat      = chat;
        _logger    = logger;
    }

    /// <summary>
    /// Streams a chat response as Server-Sent Events (SSE).
    ///
    /// The response is a stream of SSE lines:
    ///   data: {"text":"...chunk..."}\n\n
    ///   data: {"text":"...chunk..."}\n\n
    ///   data: [DONE]\n\n
    ///
    /// The frontend accumulates the "text" values into a complete JSON string,
    /// then parses it when [DONE] is received.
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamAsync(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        // Set SSE response headers
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            // Detect DTC code in the mechanic's message
            var dtcCode = DtcDetector.Extract(request.Message);

            // Build the system prompt based on what we have
            string systemPrompt = await BuildSystemPromptAsync(
                request, dtcCode, ct);

            // Stream the LLM response chunk by chunk
            await foreach (var chunk in _chat.StreamAsync(
                systemPrompt, request.History, request.Message, ct))
            {
                // Each chunk is a partial JSON text fragment
                await Response.WriteAsync(
                    $"data: {{\"text\":{JsonSerializer.Serialize(chunk)}}}\n\n",
                    ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal, not an error
            _logger.LogDebug("Client disconnected during streaming.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Chat stream error: {Error}", ex.Message);

            // Send error event before closing
            await Response.WriteAsync(
                $"data: {{\"error\":{JsonSerializer.Serialize(ex.Message)}}}\n\n",
                ct);
        }
        finally
        {
            // Always send [DONE] so the frontend knows the stream has ended
            await Response.WriteAsync("data: [DONE]\n\n", ct);
        }
    }

    /// <summary>
    /// Selects the appropriate service calls and builds the system prompt.
    /// This is the only routing logic in the controller.
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(
        ChatRequest request,
        string? dtcCode,
        CancellationToken ct)
    {
        // ── Case 1: Fault code detected, no car confirmed yet ────────────────
        // Ask for vehicle details before searching — do not show procedure yet.
        if (dtcCode is not null && request.Car is null)
        {
            _logger.LogInformation(
                "Fault code {Code} detected with no car. Asking for vehicle info.",
                dtcCode);
            return PromptTemplates.AskForCarWithCode(dtcCode);
        }

        // ── Case 2: DTC code detected, car is confirmed ───────────────────────
        // Search repair documents filtered by this specific engine + DTC code.
        if (dtcCode is not null && request.Car is not null)
        {
            _logger.LogInformation(
                "Repair by DTC: code={Code}, engine={Engine}",
                dtcCode, request.Car.CodiceMotore);

            var docs = await _docSearch.SearchByDtcAsync(
                dtcCode, request.Car.CodiceMotore, ct);
            var car = ToCarInfo(request.Car);
            var dtcCases = docs.Select(DocumentExtractor.Extract).ToList();
            return PromptTemplates.RepairAnswer(car, dtcCases, dtcCode);
        }

        // ── Case 3: No DTC, no confirmed car ─────────────────────────────────
        // If the message (or history) mentions a car brand or model → identify.
        // If the message is a pure symptom → search documents first.
        if (request.Car is null)
        {
            // Combine all user messages so short follow-ups ("94 kw", "2000-2002")
            // are searched in context with earlier answers ("fiat Ducato").
            var combinedQuery = BuildCarQuery(request.Message, request.History);

            // Detect car mention in the current message OR the combined history
            bool hasCar = CarMentionDetector.ContainsCarMention(request.Message)
                || CarMentionDetector.ContainsCarMention(combinedQuery);

            if (hasCar)
            {
                _logger.LogInformation(
                    "Car mention detected. Running car identification.");

                var matches = await _carSearch.FindAsync(
                    combinedQuery, topK: 5, ct: ct);

                // Narrow to the detected brand when one is identifiable
                var detectedBrand = DetectBrand(combinedQuery);
                if (detectedBrand is not null)
                {
                    var filtered = matches
                        .Where(m => m.Marca.ToLowerInvariant().Contains(detectedBrand))
                        .ToList();
                    if (filtered.Count > 0)
                        matches = filtered;
                }

                return PromptTemplates.CarIdentification(matches, request.History);
            }

            // Pure symptom — search documents first
            var symptomDocs = await _docSearch.SearchBySymptomNoCarAsync(
                request.Message, topK: 1, ct: ct);

            if (symptomDocs.Count > 0)
            {
                _logger.LogInformation(
                    "Symptom search found {Count} documents. " +
                    "Presenting associated cars.", symptomDocs.Count);
                return PromptTemplates.SymptomCarSelection(symptomDocs);
            }

            // No relevant documents found — fall back to car identification
            _logger.LogInformation(
                "No symptom documents found. Falling back to car identification.");
            var carMatches = await _carSearch.FindAsync(
                combinedQuery, topK: 5, ct: ct);
            return PromptTemplates.CarIdentification(carMatches, request.History);
        }

        // ── Case 4: No DTC, car confirmed ────────────────────────────────────
        // Semantic search for repair documents matching the symptom description.
        _logger.LogInformation(
            "Repair by symptom: engine={Engine}, symptom={Symptom}",
            request.Car.CodiceMotore, request.Message);

        var repairDocs = await _docSearch.SearchBySymptomAsync(
            request.Message, request.Car.CodiceMotore, ct: ct);
        var confirmedCar = ToCarInfo(request.Car);
        var symptomCases = repairDocs.Select(DocumentExtractor.Extract).ToList();
        return PromptTemplates.RepairAnswer(confirmedCar, symptomCases);
    }

    /// <summary>Maps ConfirmedCarDto to CarInfo for use in prompt building.</summary>
    private static CarInfo ToCarInfo(ConfirmedCarDto dto) =>
        new(dto.IdMacchina, dto.Marca, dto.Modello, dto.Motorizzazione,
            dto.CodiceMotore, dto.Alimentazione, dto.AnnoInizio,
            dto.AnnoFine, dto.Kw, dto.Cavalli);

    /// <summary>
    /// Combines all user messages from conversation history with the current
    /// message into a single search query. Short follow-up answers
    /// ("94 kw", "2000-2002") are enriched with earlier brand/model context.
    /// </summary>
    private static string BuildCarQuery(
        string currentMessage,
        IReadOnlyList<ConversationTurn> history)
    {
        var userMessages = history
            .Where(h => h.Role == "user")
            .Select(h => h.Content)
            .ToList();

        userMessages.Add(currentMessage);

        var combined = string.Join(" ", userMessages);

        // Stay focused: if the combined text is too long take only the last 3 turns
        if (combined.Length > 300)
            combined = string.Join(" ", userMessages.TakeLast(3));

        return combined.Trim();
    }

    /// <summary>
    /// Returns the first known brand found in the text, or null.
    /// Used to narrow search results to the brand the mechanic mentioned.
    /// </summary>
    private static string? DetectBrand(string text)
    {
        var lower = text.ToLowerInvariant();
        string[] knownBrands = ["fiat", "ford", "citroen", "citroën", "peugeot", "iveco"];
        return knownBrands.FirstOrDefault(b => lower.Contains(b));
    }
}
