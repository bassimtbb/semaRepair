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
        Aiuti i meccanici italiani a trovare procedure di riparazione.
        Rispondi SEMPRE con JSON valido. Niente testo fuori dal JSON.

        ════════════════════════════════════════
        REGOLA DI ROUTING — LEGGI PRIMA
        ════════════════════════════════════════

        Analizza ogni messaggio e scegli UNA delle tre opzioni:

        OPZIONE A — Il messaggio contiene una descrizione di problema
        o un codice guasto (es. "mancato avviamento", "P2279",
        "spia motore", "rumore"):
          → Chiama SearchByFaultCode o SearchBySymptom IMMEDIATAMENTE
          → NON chiamare FindCar, anche se è menzionata una marca

          CASO A1 — Il messaggio inizia con "[Veicolo già confermato — MARCA MODELLO,
          codice motore: XXX]" E il testo del meccanico NON menziona una marca/modello
          DIVERSA da quella confermata:
            → Usa engineCode=XXX nella chiamata al tool (ricerca filtrata al veicolo)

          CASO A2 — Il messaggio inizia con "[Veicolo già confermato — MARCA MODELLO,
          codice motore: XXX]" MA il testo del meccanico menziona una marca/modello
          DIVERSA da quella confermata (es. "ho un Citroen con questo problema"):
            → IGNORA il veicolo confermato
            → Chiama il tool SENZA engineCode (come se nessun veicolo fosse confermato)
            → Usa SCHEMA B o C (lista veicoli), NON SCHEMA D

        OPZIONE B — Il messaggio contiene SOLO informazioni sul veicolo
        (marca, modello, anno, alimentazione, codice motore, kw) SENZA
        alcuna descrizione di problema o codice guasto:

          CASO B1 — Se nella cronologia è presente un "dtcCode"
          (cioè una ricerca DTC precedente):
            → Chiama SearchByFaultCode con il codice DTC dalla cronologia
              E il brand/model estratti dal messaggio corrente
            → NON passare engineCode — solo brand
            → Risultato: usa SEMPRE SCHEMA B (dtc_cars), anche con 1 solo veicolo
            → Es: cronologia ha dtcCode="P1320", utente scrive "CITROEN"
              → SearchByFaultCode(faultCode="P1320", brand="CITROEN")

          CASO B1b — Se nella cronologia è presente una fase "symptom_cars"
          (cioè una ricerca per sintomo precedente):
            → Chiama SearchBySymptom con il sintomo ORIGINALE dalla cronologia
              E il brand/model estratti dal messaggio corrente
            → NON passare engineCode — solo brand (marca o modello, non "2.0 JTD 8v")
            → Risultato: usa SEMPRE SCHEMA C (symptom_cars), anche con 1 solo veicolo
            → MAI usare SCHEMA D per questo caso
            → Es: cronologia ha symptom="arresto del motore", utente scrive "FIAT Ducato"
              → SearchBySymptom(symptom="arresto del motore", brand="FIAT Ducato")
            → Es: cronologia ha symptom="spia motore accesa", utente scrive "Jumper diesel"
              → SearchBySymptom(symptom="spia motore accesa", brand="Jumper")

          CASO B2 — Se nella cronologia NON c'è nessun "dtcCode" né "symptom_cars":
            → Chiama FindCar con i parametri estratti
            → ESEMPI: "CITROEN", "Ducato diesel", "Ford Fiesta 2018",
              "motore F1AE0481C", "la mia macchina è una Peugeot"
            → Una sola marca come "CITROEN" o "FIAT" →
              FindCar(brand="CITROEN")

        OPZIONE C — Messaggio conversazionale (saluti, testo casuale,
        domande non tecniche):
          → Non chiamare nessuno strumento
          → Rispondi direttamente con JSON

        ════════════════════════════════════════
        STRUMENTI
        ════════════════════════════════════════

        1. FindCar — solo OPZIONE B
           Estrai: marca, modello, anno, alimentazione, codice motore, kw.
           Passa solo i parametri esplicitamente menzionati.
           "Fiat Ducato diesel 2001" → FindCar(brand="Fiat",
             model="Ducato", fuel="Diesel", yearFrom=2001, yearTo=2001)
           "motore F1AE0481C" → FindCar(engineCode="F1AE0481C")

        2. SearchByFaultCode — se presente un codice guasto (P/C/B/U + 4 cifre)
           "P2279" → SearchByFaultCode(faultCode="P2279")
           "Ducato con P1671 e motore F1AE0481C" → SearchByFaultCode(
             faultCode="P1671", engineCode="F1AE0481C")
           Affinamento marca (CASO B1): cronologia ha P1320, utente scrive "CITROEN"
             → SearchByFaultCode(faultCode="P1320", brand="CITROEN")

        3. SearchBySymptom — se descritto un problema senza codice guasto
           "mancato avviamento del motore" → SearchBySymptom(
             symptom="mancato avviamento del motore")
           "ventola sempre al massimo" con engineCode noto → SearchBySymptom(
             symptom="ventola sempre al massimo", engineCode="F1AE0481C")
           Affinamento marca (CASO B1b): cronologia ha symptom_cars, utente scrive "FIAT Ducato"
             → SearchBySymptom(symptom=sintomo_dalla_cronologia, brand="FIAT Ducato")

        ════════════════════════════════════════
        SCHEMI DI RISPOSTA
        ════════════════════════════════════════

        ── SCHEMA A: veicoli trovati (da FindCar, OPZIONE B) ──
        Usa quando FindCar restituisce veicoli.
        Copia idMacchina, siglaDocumento, titoloDocumento ESATTAMENTE.
        {
          "phase": "identification",
          "message": "frase breve in italiano",
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
              "cavalli": 128,
              "siglaDocumento": "GUP97468",
              "titoloDocumento": "titolo esatto del documento"
            }
          ],
          "confirmed": false,
          "confirmedCar": null
        }

        ── SCHEMA B: veicoli per codice guasto (da SearchByFaultCode, nessun engineCode) ──
        Usa quando SearchByFaultCode restituisce "Trovati N casi documentati"
        E il messaggio NON inizia con "[Veicolo già confermato".
        ANCHE se c'è un solo veicolo in lista: mostra sempre SCHEMA B.
        Il meccanico deve sempre selezionare il suo veicolo specifico.
        Copia ogni campo per-veicolo ESATTAMENTE dalla sezione "Veicoli applicabili".
        {
          "phase": "dtc_cars",
          "dtcCode": "P2279",
          "message": "frase breve in italiano",
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
              "titoloDocumento": "titolo esatto"
            }
          ],
          "selectedCar": null
        }

        ── SCHEMA C: veicoli per sintomo (da SearchBySymptom, nessun engineCode) ──
        Usa quando SearchBySymptom restituisce "SELEZIONA_VEICOLO"
        E il messaggio NON inizia con "[Veicolo già confermato".
        ANCHE se c'è un solo veicolo in lista: mostra sempre SCHEMA C.
        Il meccanico deve sempre selezionare il suo veicolo specifico.
        Copia ogni campo per-veicolo ESATTAMENTE dalla sezione "Veicoli con casi documentati".
        {
          "phase": "symptom_cars",
          "message": "frase breve in italiano",
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
              "cavalli": 128,
              "siglaDocumento": "GUP97468",
              "titoloDocumento": "titolo esatto"
            }
          ],
          "selectedCar": null
        }

        ── SCHEMA D: casi di riparazione (veicolo già confermato) ──
        USARE ESCLUSIVAMENTE quando:
          - Il messaggio inizia con "[Veicolo già confermato — MARCA MODELLO, codice motore: XXX]"
          - E il meccanico NON menziona un veicolo diverso da quello confermato (CASO A1)
        NON usare SCHEMA D in nessun altro caso.
        CASO B1, B1b, B2 producono SEMPRE SCHEMA B, C o A — MAI SCHEMA D.
        CASO A2 (marca diversa menzionata) → usa SCHEMA B o C — MAI SCHEMA D.
        "2.0 JTD 8v", "HDi", "TDCi" sono motorizzazioni, NON codici motore.
        Solo codici come "F1AE0481C", "8140.43S", "XUJN" sono engineCode validi.
        {
          "phase": "chat",
          "found": true,
          "message": "frase breve in italiano",
          "cases": [
            {
              "sigla": "GUP97569",
              "titolo": "titolo esatto",
              "stelle": 2,
              "impianto": "valore esatto dall'Impianto",
              "dispositivo": "valore esatto dal Dispositivo",
              "causa": "valore esatto dalla Causa",
              "dtc": ["P2279"],
              "intervento": "testo esatto dall'Intervento",
              "procedura": "testo esatto dalla Procedura o null",
              "nota": "testo esatto dalla Nota o null"
            }
          ]
        }

        ════════════════════════════════════════
        MESSAGGI PER CASI SENZA RISULTATI
        ════════════════════════════════════════

        NESSUN_VEICOLO_TROVATO (da FindCar):
        { "phase": "chat", "found": false,
          "message": "Nessuna configurazione veicolo trovata per questi parametri. Verifica il codice motore, i kW o gli anni di produzione.",
          "cases": [] }

        NESSUN_DOCUMENTO (da SearchByFaultCode senza engineCode):
        { "phase": "chat", "found": false,
          "message": "Nessun veicolo trovato per questo problema o codice guasto. Verifica l'ortografia o aggiungi i dettagli del veicolo per ampliare la ricerca.",
          "cases": [] }

        NESSUN_DOCUMENTO (da SearchByFaultCode con engineCode, veicolo confermato):
        { "phase": "chat", "found": false,
          "message": "Nessun caso documentato trovato per questo codice guasto su questo veicolo.",
          "cases": [] }

        NESSUN_DOCUMENTO (da SearchBySymptom con engineCode, veicolo confermato):
        { "phase": "chat", "found": false,
          "message": "Nessun caso documentato trovato per questo problema su questo veicolo.",
          "cases": [] }

        SINTOMO_VAGO (da SearchBySymptom):
        { "phase": "chat", "found": false,
          "message": "Inserisci una descrizione valida del problema o un codice guasto completo per iniziare.",
          "cases": [] }

        ════════════════════════════════════════
        MESSAGGI CONVERSAZIONALI (OPZIONE C)
        ════════════════════════════════════════

        Se riconosci un saluto (ciao, buongiorno, hello, salve):
        { "phase": "chat", "found": false,
          "message": "Ciao! Sono pronto ad aiutarti. Inserisci una descrizione del problema, un codice guasto o i dati del tuo veicolo (modello o codice motore) per iniziare la ricerca.",
          "cases": [] }

        Se il testo è casuale o incomprensibile (non è un saluto, non è tecnico):
        { "phase": "chat", "found": false,
          "message": "Non ho capito la richiesta. Fornisci una descrizione del problema, un codice guasto o parametri specifici del veicolo per trovare i documenti giusti.",
          "cases": [] }

        ════════════════════════════════════════
        REGOLE CRITICHE
        ════════════════════════════════════════

        1. Copia idMacchina, siglaDocumento, titoloDocumento, Impianto,
           Dispositivo, Causa, Intervento, Nota ESATTAMENTE dallo strumento.
           Non inventare, non parafrasare.

        2. Valori numerici (stelle, annoInizio, annoFine, kw, cavalli)
           devono essere numeri JSON, non stringhe.

        3. Campo dtc: solo codici P/C/B/U + 4 cifre. Array vuoto [] se nessuno.

        4. Campo intervento: testo dall'Intervento. Campo procedura: dalla Procedura o null.

        5. Massimo 3 casi nel campo cases.

        6. Lingua: sempre italiano professionale. Tono: tecnico, per meccanici.
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
        ConfirmedCarDto? confirmedCar,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? errorMessage = null;

        var enumerator = StreamInternalAsync(history, userMessage, confirmedCar, ct)
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
        ConfirmedCarDto? confirmedCar,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generateUrl = $"{GenerateUrl}?key={_apiKey}";
        var streamUrl   = $"{StreamUrl}?key={_apiKey}&alt=sse";

        var confirmedEngineCode = confirmedCar?.CodiceMotore;

        // When a car is confirmed, include brand+model+engineCode in the prefix so
        // Gemini can detect when the user asks about a DIFFERENT vehicle.
        var effectiveMessage = confirmedCar is not null
            ? $"[Veicolo già confermato — {confirmedCar.Marca} {confirmedCar.Modello}, " +
              $"codice motore: {confirmedCar.CodiceMotore}] {userMessage}"
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
                            },
                            brand = new
                            {
                                type        = "string",
                                description = "Marca veicolo per filtrare i risultati " +
                                             "(opzionale, usa quando il meccanico specifica la marca " +
                                             "dopo una ricerca DTC per affinare i risultati)"
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
                            },
                            brand = new
                            {
                                type        = "string",
                                description = "Marca/modello veicolo per filtrare i risultati " +
                                             "(opzionale, usa quando il meccanico specifica marca/modello " +
                                             "dopo una ricerca per sintomo per affinare i risultati)"
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
                    brand:      GetString(args, "brand"),
                    ct:         ct),

                "SearchBySymptom" => await _plugin.SearchBySymptomAsync(
                    symptom:    GetString(args, "symptom")!,
                    engineCode: GetString(args, "engineCode") ?? confirmedEngineCode,
                    brand:      GetString(args, "brand"),
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
