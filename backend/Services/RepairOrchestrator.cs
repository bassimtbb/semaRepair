using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SemaRepair.Api.Dtos;

namespace SemaRepair.Api.Services;

/// <summary>
/// Orchestrates the repair assistant conversation using Gemini function calling.
///
/// Flow:
///   1. Send user message to Gemini with tool definitions (non-streaming)
///   2. If Gemini calls a tool → execute it via RepairPlugin
///   3. Send tool result back to Gemini
///   4. Gemini streams the final answer via SSE
///
/// First request uses :generateContent (non-streaming) so tool calls are
/// easy to parse as plain JSON. Final answer uses :streamGenerateContent
/// so the frontend receives progressive chunks.
/// </summary>
public sealed class RepairOrchestrator
{
    private readonly RepairPlugin _plugin;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<RepairOrchestrator> _logger;

    private const string GenerateUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/" +
        "gemini-2.5-flash:generateContent";

    private const string StreamUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/" +
        "gemini-2.5-flash:streamGenerateContent";

    private const string SystemPrompt = """
        Sei l'assistente tecnico di SemaRepair.
        Aiuti i meccanici italiani a identificare guasti e trovare
        procedure di riparazione per veicoli.

        ════════════════════════════════════════
        COME USARE GLI STRUMENTI
        ════════════════════════════════════════

        ════════════════════════════════════════
        REGOLA DI PRIORITÀ — LEGGI PRIMA
        ════════════════════════════════════════

        Se il messaggio contiene il nome di una marca di veicolo
        (Fiat, Ford, Citroen, Citroën, Peugeot, Iveco) o un modello
        (Ducato, Fiesta, Focus, Jumper, Boxer, Daily, Jumpy, Berlingo):

        → Chiama SEMPRE FindCar per primo.
        → NON chiamare SearchBySymptom o SearchByFaultCode prima di
          aver identificato e confermato il veicolo.

        Esempio CORRETTO:
          Messaggio: "ho una Citroen diesel con mancato avviamento"
          → FindCar(brand="Citroen", fuel="Diesel")
          → Presenta le opzioni al meccanico
          → Meccanico conferma il veicolo
          → POI chiama SearchBySymptom con engineCode confermato

        Esempio SBAGLIATO:
          Messaggio: "ho una Citroen diesel con mancato avviamento"
          → SearchBySymptom(symptom="mancato avviamento")  ← VIETATO
          Il meccanico non ha ancora confermato il suo veicolo!

        ECCEZIONE: Se il veicolo è già confermato (engineCode disponibile),
        puoi chiamare SearchBySymptom o SearchByFaultCode direttamente.

        ════════════════════════════════════════

        Hai 3 strumenti disponibili. Chiamali subito senza chiedere
        conferma — non chiedere mai "vuoi che cerchi?" oppure "posso
        procedere?". Agisci direttamente.

        1. FindCar
           Quando usarlo: il meccanico descrive il suo veicolo.
           Estrai dalla frase: marca, modello, anno, alimentazione,
           codice motore, kw — passa solo i parametri menzionati.
           Esempi:
             "ho un Fiat Ducato diesel del 2001"
             → FindCar(brand="Fiat", model="Ducato",
                       fuel="Diesel", yearFrom=2001, yearTo=2001)

             "Ford Fiesta 1.5 TDCi 2017"
             → FindCar(brand="Ford", model="Fiesta",
                       fuel="Diesel", yearFrom=2017, yearTo=2017)

             "motore 8140.43S"
             → FindCar(engineCode="8140.43S")

        2. SearchByFaultCode
           Quando usarlo: il messaggio contiene un codice guasto
           (formato: lettera P/C/B/U seguita da 4 cifre).
           Se il veicolo è già confermato passa anche engineCode.
           Esempi:
             "P2279"
             → SearchByFaultCode(faultCode="P2279")

             "ho il codice P1671 sul mio Ducato F1AE0481C"
             → SearchByFaultCode(faultCode="P1671",
                                  engineCode="F1AE0481C")

        3. SearchBySymptom
           Quando usarlo: il meccanico descrive un problema senza
           codice guasto. Se il veicolo è confermato passa engineCode.
           Esempi:
             "spia motore accesa scarse prestazioni"
             → SearchBySymptom(symptom="spia motore accesa scarse prestazioni")

             "ventola del radiatore sempre al massimo"
             → SearchBySymptom(
                 symptom="ventola del radiatore sempre al massimo",
                 engineCode="F1AE0481C")

        ════════════════════════════════════════
        REGOLE PER LA RISPOSTA
        ════════════════════════════════════════

        Rispondi SEMPRE con JSON valido. Niente testo fuori dal JSON.

        SCHEMA — identificazione veicolo:
        Usa questo schema quando FindCar restituisce veicoli e il
        meccanico non ha ancora confermato il suo veicolo:
        {
          "phase": "identification",
          "message": "frase introduttiva in italiano",
          "carMatches": [
            {
              "idMacchina": "FI0370",
              "marca": "FIAT",
              "modello": "Ducato",
              "motorizzazione": "2.8 JTD 8v",
              "codiceMotore": "8140.43S",
              "alimentazione": "Diesel",
              "annoInizio": 2000,
              "annoFine": 2002,
              "kw": 94,
              "cavalli": 128
            }
          ],
          "confirmed": false,
          "confirmedCar": null
        }

        Quando il meccanico conferma un veicolo (sì/ok/primo/1/ecc.):
        {
          "phase": "identification",
          "message": "Veicolo confermato. Descrivi il problema.",
          "carMatches": [...],
          "confirmed": true,
          "confirmedCar": { ...veicolo scelto... }
        }

        SCHEMA — casi di riparazione:
        Usa questo schema quando SearchByFaultCode o SearchBySymptom
        restituisce documenti.
        IMPORTANTE: Se il risultato dello strumento inizia con
        "Trovati N casi documentati" (non con SELEZIONA_VEICOLO),
        rispondi SEMPRE con phase="chat", MAI con phase="symptom_cars".
        Il veicolo è già confermato — mostra direttamente i casi.
        {
          "phase": "chat",
          "found": true,
          "message": "frase introduttiva in italiano",
          "cases": [
            {
              "sigla": "GUP97569",
              "titolo": "titolo esatto del documento",
              "stelle": 1,
              "impianto": "valore esatto dall'Impianto",
              "dispositivo": "valore esatto dal Dispositivo",
              "causa": "valore esatto dalla Causa",
              "dtc": ["P2279", "P1102"],
              "procedura": "testo esatto dall'Intervento",
              "nota": "testo esatto dalla Nota o null"
            }
          ]
        }

        Quando nessun documento è trovato o rilevante:
        {
          "phase": "chat",
          "found": false,
          "message": "Non ho casi documentati per questo problema su questo veicolo.",
          "cases": []
        }

        SCHEMA — veicoli con codice guasto documentato:
        Usa questo schema quando SearchByFaultCode restituisce documenti
        MA il veicolo non è ancora confermato:
        {
          "phase": "dtc_cars",
          "dtcCode": "P2279",
          "message": "frase che spiega il codice e i veicoli trovati",
          "cars": [
            {
              "idMacchina": "...",
              "marca": "FORD",
              "modello": "Fiesta",
              "motorizzazione": "1.5 TDCi 8v",
              "codiceMotore": "XUJN",
              "alimentazione": "Diesel",
              "annoInizio": 2017,
              "annoFine": 2020,
              "kw": 63,
              "cavalli": 85,
              "siglaDocumento": "GUP97569",
              "titoloDocumento": "titolo del documento"
            }
          ],
          "selectedCar": null
        }

        SCHEMA — chiedi informazioni sul veicolo:
        Usa questo schema quando non hai abbastanza informazioni
        per identificare il veicolo:
        {
          "phase": "ask_car",
          "codeDetected": "P1671",
          "message": "domanda in italiano per ottenere marca/modello/anno",
          "confirmed": false,
          "carMatches": [],
          "confirmedCar": null
        }

        ════════════════════════════════════════
        REGOLE CRITICHE
        ════════════════════════════════════════

        1. Copia i valori dai documenti ESATTAMENTE — non parafrasare.
           Dispositivo, Causa, Intervento, Nota devono essere identici
           ai valori restituiti dallo strumento.

        2. I valori numerici (stelle, annoInizio, annoFine, kw, cavalli)
           devono essere numeri JSON, non stringhe.

        3. Il campo dtc deve contenere solo i codici trovati nel testo
           (formato P/C/B/U + 4 cifre). Array vuoto [] se nessuno.

        4. Massimo 3 casi nel campo cases, ordinati per stelle discendente.

        5. Se il risultato dello strumento inizia con NESSUN_VEICOLO_TROVATO
           o NESSUN_DOCUMENTO, rispondi con il schema appropriato
           (found=false oppure chiedi più informazioni).

        6. Lingua: sempre italiano professionale.
           Tono: tecnico ma chiaro, adatto a un meccanico.

        7. Se il messaggio non riguarda la riparazione di veicoli
           (es. saluti, domande generiche), rispondi con:
           {
             "phase": "chat",
             "found": false,
             "message": "Sono l'assistente tecnico SemaRepair. Posso aiutarti a trovare cause e procedure di riparazione per il tuo veicolo. Descrivi il problema o inserisci un codice guasto.",
             "cases": []
           }
           Non chiamare nessuno strumento per messaggi non tecnici.

        8. Se il risultato di SearchBySymptom inizia con SINTOMO_VAGO,
           chiedi al meccanico di descrivere il problema con più dettagli.
           Usa questo schema:
           {
             "phase": "chat",
             "found": false,
             "message": "domanda in italiano che chiede dettagli specifici sul sintomo",
             "cases": []
           }

        9. Se il risultato dello strumento inizia con SELEZIONA_VEICOLO:
           Il meccanico deve prima selezionare il suo veicolo.
           Rispondi con lo schema symptom_cars:
           {
             "phase": "symptom_cars",
             "message": "Ho trovato casi documentati per questo problema. Per mostrarti la procedura corretta ho bisogno di sapere il tuo veicolo. Seleziona quello corretto:",
             "documents": [
               {
                 "siglaDocumento": "GUP97468",
                 "titoloDocumento": "titolo esatto",
                 "cars": [
                   {
                     "idMacchina": "FI0370",
                     "marca": "FIAT",
                     "modello": "Ducato",
                     "motorizzazione": "2.8 JTD 8v",
                     "codiceMotore": "8140.43S",
                     "alimentazione": "Diesel",
                     "annoInizio": 2000,
                     "annoFine": 2002,
                     "kw": 94,
                     "cavalli": 128
                   }
                 ]
               }
             ],
             "selectedCar": null
           }
           Copia i veicoli ESATTAMENTE come forniti dallo strumento.
           Non inventare veicoli.

        10. Se il meccanico dice che il suo veicolo non è nella lista
            (frasi come "non ce la macchina", "non c'è la mia macchina",
            "il mio veicolo non c'è", "non trovo il mio veicolo",
            "non è nella lista", "la mia macchina non è in lista"):
            Cerca il codice DTC nella conversazione precedente (campo dtcCode).
            Rispondi con lo schema ask_car SENZA chiamare nessuno strumento:
            {
              "phase": "ask_car",
              "codeDetected": "<codice DTC dalla conversazione, es. P1320>",
              "message": "Mi dispiace che il tuo veicolo non sia in lista. Per trovare la procedura corretta dimmi marca, modello e anno del tuo veicolo.",
              "confirmed": false,
              "carMatches": [],
              "confirmedCar": null
            }
        """;

    public RepairOrchestrator(
        RepairPlugin plugin,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<RepairOrchestrator> logger)
    {
        _plugin     = plugin;
        _httpClient = httpClient;
        _apiKey     = configuration["GEMINI_API_KEY"]
            ?? throw new InvalidOperationException("GEMINI_API_KEY not configured.");
        _logger     = logger;
    }

    /// <summary>
    /// Public entry point. Wraps StreamInternalAsync with a top-level error
    /// guard so any unhandled exception becomes a graceful JSON error response
    /// instead of crashing the host.
    ///
    /// C# does not allow yield return inside a try-catch, so the pattern is:
    ///   outer try-finally (yield allowed) contains inner try-catch (no yield).
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        string? confirmedEngineCode,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? errorMessage = null;

        var enumerator = StreamInternalAsync(history, userMessage, confirmedEngineCode, ct)
            .GetAsyncEnumerator(ct);

        try  // try-finally only → yield return allowed inside
        {
            while (true)
            {
                bool   hasNext = false;
                string? current = null;

                try  // try-catch → no yield inside
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext) current = enumerator.Current;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "RepairOrchestrator error: {Error}", ex.Message);
                    errorMessage = JsonSerializer.Serialize(new
                    {
                        phase   = "chat",
                        found   = false,
                        message = "Si è verificato un errore. Riprova.",
                        cases   = Array.Empty<object>()
                    });
                    break;
                }

                if (!hasNext) break;
                if (current is not null) yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (errorMessage is not null)
            yield return errorMessage;
    }

    // ── Core logic ────────────────────────────────────────────────────────────

    private async IAsyncEnumerable<string> StreamInternalAsync(
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        string? confirmedEngineCode,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generateUrl = $"{GenerateUrl}?key={_apiKey}";
        var streamUrl   = $"{StreamUrl}?key={_apiKey}&alt=sse";

        // When a car is already confirmed, tell Gemini explicitly so it skips
        // FindCar and calls SearchBySymptom/SearchByFaultCode with engineCode directly.
        // This prevents Gemini from repeating the car-selection flow for the same symptom.
        var effectiveMessage = confirmedEngineCode is not null
            ? $"[Veicolo già confermato — codice motore: {confirmedEngineCode}] {userMessage}"
            : userMessage;

        var contents = BuildContents(history, effectiveMessage);
        var tools    = BuildToolDefinitions();

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents,
            tools,
            generationConfig = new
            {
                // responseMimeType is intentionally omitted here.
                // Gemini rejects "application/json" when tools are present.
                thinkingConfig  = new { thinkingBudget = 0 },
                maxOutputTokens = 8192,
                temperature     = 0.1
            }
        };

        _logger.LogInformation(
            "Sending first Gemini request for: {Message}", userMessage);

        // First request: non-streaming so we can inspect the response for tool calls
        var response     = await _httpClient.PostAsJsonAsync(generateUrl, requestBody, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation(
            "First Gemini response status: {Status}", response.StatusCode);
        _logger.LogInformation(
            "First Gemini response body (first 500 chars): {Body}",
            responseText[..Math.Min(500, responseText.Length)]);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Gemini first call failed: {Status} {Body}",
                response.StatusCode,
                responseText[..Math.Min(500, responseText.Length)]);
            yield return JsonSerializer.Serialize(new
            {
                phase   = "chat",
                found   = false,
                message = "Errore nella comunicazione con il servizio AI. Riprova.",
                cases   = Array.Empty<object>()
            });
            yield break;
        }

        var toolCall = ExtractToolCall(responseText);
        _logger.LogInformation(
            "Tool call detected: {ToolCall}", toolCall?.Name ?? "NONE");

        if (toolCall is null)
        {
            // No tool call — Gemini answered directly (e.g. a greeting or off-topic message).
            // ExtractTextChunks ensures the output is always valid JSON in the chat schema.
            foreach (var chunk in ExtractTextChunks(responseText))
                yield return chunk;
            yield break;
        }

        _logger.LogInformation(
            "Gemini called tool: {Tool} with args: {Args}",
            toolCall.Name, toolCall.Args);

        var toolResult = await ExecuteToolAsync(
            toolCall.Name, toolCall.Args, confirmedEngineCode, ct);

        _logger.LogInformation(
            "Tool result for {Tool}: {Result}",
            toolCall.Name, toolResult[..Math.Min(200, toolResult.Length)]);

        // Second request: streaming the final answer after tool execution
        var contents2 = BuildContentsWithToolResult(history, userMessage, toolCall, toolResult);

        var requestBody2 = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = contents2,
            generationConfig = new
            {
                responseMimeType = "application/json",
                thinkingConfig   = new { thinkingBudget = 0 },
                maxOutputTokens  = 8192,
                temperature      = 0.1
            }
        };

        var finalRequest = new HttpRequestMessage(HttpMethod.Post, streamUrl)
        {
            Content = JsonContent.Create(requestBody2)
        };

        var finalResponse = await _httpClient.SendAsync(
            finalRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        using var stream = await finalResponse.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            JsonElement doc;
            try { doc = JsonSerializer.Deserialize<JsonElement>(data); }
            catch { continue; }

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

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<object> BuildContents(
        IReadOnlyList<ConversationTurn> history,
        string userMessage)
    {
        var contents = history
            .Select(h => (object)new
            {
                role  = h.Role == "assistant" ? "model" : h.Role,
                parts = new[] { new { text = h.Content } }
            })
            .ToList();

        contents.Add(new
        {
            role  = "user",
            parts = new[] { new { text = userMessage } }
        });

        return contents;
    }

    private static List<object> BuildContentsWithToolResult(
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        ToolCall toolCall,
        string toolResult)
    {
        var contents = BuildContents(history, userMessage);

        // Add the model's function call turn
        contents.Add(new
        {
            role  = "model",
            parts = new[]
            {
                new
                {
                    functionCall = new
                    {
                        name = toolCall.Name,
                        args = JsonSerializer.Deserialize<JsonElement>(toolCall.Args)
                    }
                }
            }
        });

        // Add the tool result turn — Gemini treats tool responses as user content
        contents.Add(new
        {
            role  = "user",
            parts = new[]
            {
                new
                {
                    functionResponse = new
                    {
                        name     = toolCall.Name,
                        response = new { content = toolResult }
                    }
                }
            }
        });

        return contents;
    }

    private static object[] BuildToolDefinitions() =>
    [
        new
        {
            functionDeclarations = new object[]
            {
                new
                {
                    name        = "FindCar",
                    description = "Cerca veicoli nel database usando filtri strutturati. " +
                                  "Usa questo strumento quando il meccanico descrive il suo veicolo " +
                                  "(marca, modello, anno, alimentazione, codice motore, kw).",
                    parameters  = new
                    {
                        type       = "object",
                        properties = new
                        {
                            brand = new
                            {
                                type        = "string",
                                description = "Marca del veicolo (es. FIAT, FORD, CITROEN)"
                            },
                            model = new
                            {
                                type        = "string",
                                description = "Modello del veicolo (es. Ducato, Fiesta)"
                            },
                            yearFrom = new
                            {
                                type        = "integer",
                                description = "Anno minimo di immatricolazione"
                            },
                            yearTo = new
                            {
                                type        = "integer",
                                description = "Anno massimo di immatricolazione"
                            },
                            fuel = new
                            {
                                type        = "string",
                                description = "Alimentazione (Diesel, Benzina, Gas)"
                            },
                            engineCode = new
                            {
                                type        = "string",
                                description = "Codice motore esatto (es. F1AE0481C, XUJN)"
                            },
                            kw = new
                            {
                                type        = "integer",
                                description = "Potenza in kW"
                            }
                        }
                    }
                },
                new
                {
                    name        = "SearchByFaultCode",
                    description = "Cerca casi di riparazione documentati per un codice guasto " +
                                  "(formato: lettera P/C/B/U seguita da 4 cifre, es. P2279). " +
                                  "Usa questo strumento quando il meccanico menziona un codice guasto.",
                    parameters  = new
                    {
                        type       = "object",
                        properties = new
                        {
                            faultCode = new
                            {
                                type        = "string",
                                description = "Codice guasto OBD-II (es. P2279, C1110)"
                            },
                            engineCode = new
                            {
                                type        = "string",
                                description = "Codice motore per filtrare i risultati " +
                                             "(opzionale, solo se il veicolo è confermato)"
                            }
                        },
                        required = new[] { "faultCode" }
                    }
                },
                new
                {
                    name        = "SearchBySymptom",
                    description = "Cerca casi di riparazione per descrizione del sintomo " +
                                  "usando similarità semantica. " +
                                  "Usa questo strumento quando il meccanico descrive un problema " +
                                  "senza codice guasto.",
                    parameters  = new
                    {
                        type       = "object",
                        properties = new
                        {
                            symptom = new
                            {
                                type        = "string",
                                description = "Descrizione del problema o sintomo in italiano"
                            },
                            engineCode = new
                            {
                                type        = "string",
                                description = "Codice motore per filtrare i risultati " +
                                             "(opzionale, solo se il veicolo è confermato)"
                            }
                        },
                        required = new[] { "symptom" }
                    }
                }
            }
        }
    ];

    private record ToolCall(string Name, string Args);

    private static ToolCall? ExtractToolCall(string responseText)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (!doc.TryGetProperty("candidates", out var candidates)) return null;
            if (candidates.GetArrayLength() == 0) return null;

            var content = candidates[0].GetProperty("content");
            var parts   = content.GetProperty("parts");

            for (int i = 0; i < parts.GetArrayLength(); i++)
            {
                var part = parts[i];
                if (!part.TryGetProperty("functionCall", out var fc)) continue;

                return new ToolCall(
                    fc.GetProperty("name").GetString()!,
                    fc.GetProperty("args").GetRawText());
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Extracts the text from a non-streaming generateContent response and
    /// ensures it is always valid JSON in the chat schema.
    ///
    /// If Gemini returned valid JSON with a 'phase' field (rule 7 compliant),
    /// yield it as-is. Otherwise wrap the plain text in the chat schema so
    /// the frontend always receives a renderable response.
    /// </summary>
    private static List<string> ExtractTextChunks(string responseText)
    {
        var chunks = new List<string>();
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(responseText);
            if (!doc.TryGetProperty("candidates", out var candidates)) return chunks;
            if (candidates.GetArrayLength() == 0) return chunks;

            var content = candidates[0].GetProperty("content");
            var parts   = content.GetProperty("parts");

            var fullText = new StringBuilder();
            for (int i = 0; i < parts.GetArrayLength(); i++)
            {
                var part = parts[i];
                if (!part.TryGetProperty("text", out var textEl)) continue;
                var text = textEl.GetString();
                if (!string.IsNullOrEmpty(text))
                    fullText.Append(text);
            }

            if (fullText.Length == 0) return chunks;

            var raw = fullText.ToString().Trim();

            // Strip markdown fences (```json ... ``` or ``` ... ```)
            if (raw.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                raw = raw["```json".Length..].Trim();
            else if (raw.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                raw = raw["```".Length..].Trim();
            if (raw.EndsWith("```"))
                raw = raw[..^3].Trim();
            // Strip bare 'json' word prefix (e.g. Gemini outputs "json\n{...}")
            if (raw.StartsWith("json", StringComparison.OrdinalIgnoreCase)
                && raw.Length > 4
                && (raw[4] == '\n' || raw[4] == ' ' || raw[4] == '{'))
                raw = raw[4..].Trim();

            // If Gemini already returned JSON with a 'phase' field, use it directly
            try
            {
                var check = JsonSerializer.Deserialize<JsonElement>(raw);
                if (check.TryGetProperty("phase", out _))
                {
                    chunks.Add(raw);
                    return chunks;
                }
            }
            catch { }

            // Plain text or unrecognised schema — wrap in the chat envelope
            chunks.Add(JsonSerializer.Serialize(new
            {
                phase   = "chat",
                found   = false,
                message = raw,
                cases   = Array.Empty<object>()
            }));
        }
        catch { }

        return chunks;
    }

    private async Task<string> ExecuteToolAsync(
        string toolName,
        string argsJson,
        string? confirmedEngineCode,
        CancellationToken ct)
    {
        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argsJson);

            return toolName switch
            {
                "FindCar" => await _plugin.FindCarAsync(
                    brand:      GetString(args, "brand"),
                    model:      GetString(args, "model"),
                    yearFrom:   GetInt(args, "yearFrom"),
                    yearTo:     GetInt(args, "yearTo"),
                    fuel:       GetString(args, "fuel"),
                    engineCode: GetString(args, "engineCode"),
                    kw:         GetInt(args, "kw"),
                    ct:         ct),

                "SearchByFaultCode" => await _plugin.SearchByFaultCodeAsync(
                    faultCode:  GetString(args, "faultCode")!,
                    engineCode: GetString(args, "engineCode") ?? confirmedEngineCode,
                    ct:         ct),

                "SearchBySymptom" => await _plugin.SearchBySymptomAsync(
                    symptom:    GetString(args, "symptom")!,
                    engineCode: GetString(args, "engineCode") ?? confirmedEngineCode,
                    ct:         ct),

                _ => $"Strumento sconosciuto: {toolName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Tool execution error: {Error}", ex.Message);
            return $"Errore durante l'esecuzione dello strumento: {ex.Message}";
        }
    }

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;
}
