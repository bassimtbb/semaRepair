using System.Text;
using SemaRepair.Api.Dtos;
using SemaRepair.Api.Services;
using SemaRepair.Api.Utils;

namespace SemaRepair.Api.Prompts;

/// <summary>
/// All LLM system prompts in one place.
///
/// Centralizing prompts here means:
///   - Easy to review and improve without touching service code
///   - Clear JSON schemas documented alongside the prompts
///   - No magic strings scattered across the codebase
///
/// Every prompt enforces JSON-only output.
/// The frontend renders the JSON — the LLM must not add prose outside it.
/// </summary>
public static class PromptTemplates
{
    // Shared JSON-only rule prepended to every prompt
    private const string JsonOnlyRule =
        "Rispondi SOLO con JSON valido. " +
        "Non aggiungere testo, markdown, o spiegazioni fuori dal JSON.";

    /// <summary>
    /// Prompt for the car identification phase.
    /// Used when the mechanic has not yet confirmed their vehicle.
    ///
    /// Response schema:
    /// {
    ///   "phase": "identification",
    ///   "message": "string in Italian — what to show the mechanic",
    ///   "carMatches": [
    ///     {
    ///       "idMacchina": "FO2983",
    ///       "marca": "FORD",
    ///       "modello": "Fiesta",
    ///       "motorizzazione": "1.5 TDCi 8v",
    ///       "codiceMotore": "XUJN",
    ///       "alimentazione": "Diesel",
    ///       "annoInizio": 2017,
    ///       "annoFine": 2020,
    ///       "kw": 63,
    ///       "cavalli": 85
    ///     }
    ///   ],
    ///   "confirmed": false,
    ///   "confirmedCar": null
    /// }
    ///
    /// When the mechanic confirms (message contains: sì/si/ok/esatto/confermo/1/2/3/primo/secondo/terzo):
    ///   confirmed = true
    ///   confirmedCar = the selected car object (same shape as carMatches item)
    ///   message = "Veicolo confermato: {Marca} {Modello}. Descrivi il problema."
    /// </summary>
    public static string CarIdentification(
        IReadOnlyList<CarSearchResult> matches,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(JsonOnlyRule);
        sb.AppendLine();
        sb.AppendLine("Sei l'assistente tecnico SemaRepair.");
        sb.AppendLine("Fase: identificazione veicolo.");
        sb.AppendLine();

        if (history is not null && history.Any())
        {
            sb.AppendLine("CONTESTO CONVERSAZIONE PRECEDENTE:");
            foreach (var turn in history.TakeLast(6))
            {
                var preview = turn.Content.Length > 100
                    ? turn.Content[..100] : turn.Content;
                sb.AppendLine($"  {turn.Role}: {preview}");
            }
            sb.AppendLine();
            sb.AppendLine("Usa questo contesto per capire le informazioni");
            sb.AppendLine("già fornite dal meccanico sul suo veicolo.");
            sb.AppendLine();
        }

        sb.AppendLine($"Veicoli trovati dal sistema (top {matches.Count} per similarità):");

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            sb.AppendLine(
                $"  Opzione {i + 1}: {m.Marca} {m.Modello} {m.Motorizzazione} " +
                $"| {m.Alimentazione} | {m.Kw}kw/{m.Cavalli}cv " +
                $"| {m.AnnoInizio}–{m.AnnoFine} | Codice: {m.CodiceMotore} " +
                $"| idMacchina: {m.IdMacchina}");
        }

        sb.AppendLine();
        sb.AppendLine("REGOLA CRITICA: il campo carMatches deve contenere");
        sb.AppendLine("ESATTAMENTE le seguenti opzioni, tutte e " + matches.Count + ":");
        sb.AppendLine();

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            sb.AppendLine($"  Opzione {i + 1} — includi QUESTO oggetto nel array carMatches:");
            sb.AppendLine($"  {{");
            sb.AppendLine($"    \"idMacchina\": \"{m.IdMacchina}\",");
            sb.AppendLine($"    \"marca\": \"{m.Marca}\",");
            sb.AppendLine($"    \"modello\": \"{m.Modello}\",");
            sb.AppendLine($"    \"motorizzazione\": \"{m.Motorizzazione}\",");
            sb.AppendLine($"    \"codiceMotore\": \"{m.CodiceMotore}\",");
            sb.AppendLine($"    \"alimentazione\": \"{m.Alimentazione}\",");
            sb.AppendLine($"    \"annoInizio\": {m.AnnoInizio?.ToString() ?? "null"},");
            sb.AppendLine($"    \"annoFine\": {m.AnnoFine?.ToString() ?? "null"},");
            sb.AppendLine($"    \"kw\": {m.Kw?.ToString() ?? "null"},");
            sb.AppendLine($"    \"cavalli\": {m.Cavalli?.ToString() ?? "null"}");
            sb.AppendLine($"  }}");
        }

        sb.AppendLine();
        sb.AppendLine("Non generare carMatches vuoto. Non inventare dati.");
        sb.AppendLine("Copia gli oggetti esatti qui sopra nel campo carMatches.");
        sb.AppendLine();
        sb.AppendLine("Il JSON di risposta deve avere ESATTAMENTE questi campi radice:");
        sb.AppendLine("  \"phase\" (stringa, valore esatto: \"identification\"),");
        sb.AppendLine("  \"message\" (stringa in italiano),");
        sb.AppendLine("  \"carMatches\" (array con gli oggetti qui sopra),");
        sb.AppendLine("  \"confirmed\" (boolean),");
        sb.AppendLine("  \"confirmedCar\" (oggetto o null)");
        sb.AppendLine("NON usare altri nomi di campo (es. non 'stage', non 'fase').");
        sb.AppendLine();
        sb.AppendLine("Regole per confirmed:");
        sb.AppendLine("1. Se il messaggio NON contiene conferma:");
        sb.AppendLine("   confirmed=false, confirmedCar=null");
        sb.AppendLine("   Chiedi al meccanico quale opzione corrisponde al suo veicolo");
        sb.AppendLine("2. Se il messaggio contiene conferma");
        sb.AppendLine("   (sì/si/ok/esatto/confermo/primo/secondo/terzo/1/2/3):");
        sb.AppendLine("   confirmed=true");
        sb.AppendLine("   confirmedCar = l'opzione scelta (stesso oggetto da carMatches)");
        sb.AppendLine("3. message: massimo 2 frasi, italiano professionale");

        return sb.ToString();
    }

    /// <summary>
    /// Prompt for the DTC code search phase.
    /// Used when a DTC code is detected and we show which cars have it documented.
    ///
    /// Response schema:
    /// {
    ///   "phase": "dtc_cars",
    ///   "dtcCode": "P2279",
    ///   "message": "string in Italian",
    ///   "cars": [
    ///     {
    ///       "idMacchina": "FO2983",
    ///       "marca": "FORD",
    ///       "modello": "Fiesta",
    ///       "motorizzazione": "1.5 TDCi 8v",
    ///       "codiceMotore": "XUJN",
    ///       "alimentazione": "Diesel",
    ///       "annoInizio": 2017,
    ///       "annoFine": 2020,
    ///       "kw": 63,
    ///       "cavalli": 85,
    ///       "siglaDocumento": "GUP97569",
    ///       "titoloDocumento": "Sporadicamente..."
    ///     }
    ///   ],
    ///   "selectedCar": null
    /// }
    ///
    /// When the mechanic selects a car:
    ///   selectedCar = the chosen car object
    ///   message = "Veicolo selezionato. Ecco i casi per il codice {dtcCode}."
    /// </summary>
    public static string DtcCarSelection(
        string dtcCode,
        IReadOnlyList<RepairDocumentResult> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine(JsonOnlyRule);
        sb.AppendLine();
        sb.AppendLine("Sei l'assistente tecnico SemaRepair.");
        sb.AppendLine($"Il meccanico ha fornito il codice guasto: {dtcCode}");
        sb.AppendLine();
        sb.AppendLine("Documenti trovati per questo codice:");

        foreach (var doc in documents)
        {
            sb.AppendLine($"  Sigla: {doc.SiglaDocumento}");
            sb.AppendLine($"  Titolo: {doc.TitoloDocumento}");
            sb.AppendLine($"  Veicoli:");
            foreach (var car in doc.Cars)
            {
                sb.AppendLine(
                    $"    - {car.Marca} {car.Modello} {car.Motorizzazione} " +
                    $"| {car.Alimentazione} | {car.Kw}kw/{car.Cavalli}cv " +
                    $"| {car.AnnoInizio}–{car.AnnoFine} " +
                    $"| Codice: {car.CodiceMotore} | idMacchina: {car.IdMacchina}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("REGOLA CRITICA: il campo cars deve contenere");
        sb.AppendLine("ESATTAMENTE questi veicoli — non lasciarlo vuoto:");
        sb.AppendLine();

        var uniqueCars = documents
            .SelectMany(d => d.Cars.Select(c => new { Car = c, d.SiglaDocumento, d.TitoloDocumento }))
            .GroupBy(x => x.Car.IdMacchina)
            .Select(g => g.First())
            .ToList();

        foreach (var item in uniqueCars)
        {
            var c = item.Car;
            sb.AppendLine($"  {{");
            sb.AppendLine($"    \"idMacchina\": \"{c.IdMacchina}\",");
            sb.AppendLine($"    \"marca\": \"{c.Marca}\",");
            sb.AppendLine($"    \"modello\": \"{c.Modello}\",");
            sb.AppendLine($"    \"motorizzazione\": \"{c.Motorizzazione}\",");
            sb.AppendLine($"    \"codiceMotore\": \"{c.CodiceMotore}\",");
            sb.AppendLine($"    \"alimentazione\": \"{c.Alimentazione}\",");
            sb.AppendLine($"    \"annoInizio\": {c.AnnoInizio?.ToString() ?? "null"},");
            sb.AppendLine($"    \"annoFine\": {c.AnnoFine?.ToString() ?? "null"},");
            sb.AppendLine($"    \"kw\": {c.Kw?.ToString() ?? "null"},");
            sb.AppendLine($"    \"cavalli\": {c.Cavalli?.ToString() ?? "null"},");
            sb.AppendLine($"    \"siglaDocumento\": \"{item.SiglaDocumento}\",");
            sb.AppendLine($"    \"titoloDocumento\": \"{item.TitoloDocumento}\"");
            sb.AppendLine($"  }}");
        }

        sb.AppendLine();
        sb.AppendLine("Il JSON di risposta deve avere ESATTAMENTE questi campi radice:");
        sb.AppendLine("  \"phase\" (stringa, valore esatto: \"dtc_cars\"),");
        sb.AppendLine("  \"dtcCode\" (stringa, es. \"P0101\"),");
        sb.AppendLine("  \"message\" (stringa in italiano),");
        sb.AppendLine("  \"cars\" (array con gli oggetti qui sopra),");
        sb.AppendLine("  \"selectedCar\" (oggetto o null)");
        sb.AppendLine("NON usare altri nomi di campo.");
        sb.AppendLine("selectedCar=null finché il meccanico non sceglie.");
        sb.AppendLine("message: spiega il codice guasto in italiano, max 2 frasi.");

        return sb.ToString();
    }

    /// <summary>
    /// Prompt used when a fault code is detected but no car is mentioned.
    /// Instead of showing all cars with this code, the bot asks for
    /// the vehicle details to narrow down the search.
    ///
    /// Response schema:
    /// {
    ///   "phase": "ask_car",
    ///   "codeDetected": "P1671",
    ///   "message": "Ho rilevato il codice guasto P1671... Di che marca si tratta?",
    ///   "confirmed": false,
    ///   "carMatches": [],
    ///   "confirmedCar": null
    /// }
    /// </summary>
    public static string AskForCarWithCode(string dtcCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine(JsonOnlyRule);
        sb.AppendLine();
        sb.AppendLine("Sei l'assistente tecnico SemaRepair.");
        sb.AppendLine($"Il meccanico ha fornito il codice guasto: {dtcCode}");
        sb.AppendLine();
        sb.AppendLine("Non è stato specificato il veicolo.");
        sb.AppendLine("Devi chiedere le informazioni sul veicolo prima di cercare.");
        sb.AppendLine();
        sb.AppendLine("Rispondi con questo schema JSON:");
        sb.AppendLine("{");
        sb.AppendLine("  \"phase\": \"ask_car\",");
        sb.AppendLine($"  \"codeDetected\": \"{dtcCode}\",");
        sb.AppendLine("  \"message\": \"[tua domanda in italiano]\",");
        sb.AppendLine("  \"confirmed\": false,");
        sb.AppendLine("  \"carMatches\": [],");
        sb.AppendLine("  \"confirmedCar\": null");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Regole per il messaggio:");
        sb.AppendLine($"1. Menziona il codice {dtcCode} e spiega brevemente");
        sb.AppendLine("   cosa indica (max 1 frase)");
        sb.AppendLine("2. Chiedi la marca del veicolo");
        sb.AppendLine("3. Chiedi il modello");
        sb.AppendLine("4. Chiedi l'anno o il tipo di motore se utile");
        sb.AppendLine("5. Tono professionale, max 3 frasi totali");
        sb.AppendLine("6. Non mostrare procedure di riparazione ancora");
        sb.AppendLine("7. Non elencare veicoli ancora");

        return sb.ToString();
    }

    /// <summary>
    /// Prompt for when the mechanic describes a symptom with no car.
    /// We found matching documents — now present which cars have this problem.
    ///
    /// Response schema:
    /// {
    ///   "phase": "symptom_cars",
    ///   "message": "Questo problema è documentato per i seguenti veicoli...",
    ///   "documents": [
    ///     {
    ///       "siglaDocumento": "GUP97468",
    ///       "titoloDocumento": "Funzionamento continuo...",
    ///       "cars": [ { "idMacchina": "FI0398", "marca": "FIAT", ... } ]
    ///     }
    ///   ],
    ///   "selectedCar": null
    /// }
    /// </summary>
    public static string SymptomCarSelection(
        IReadOnlyList<RepairDocumentResult> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine(JsonOnlyRule);
        sb.AppendLine();
        sb.AppendLine("Sei l'assistente tecnico SemaRepair.");
        sb.AppendLine("Il meccanico ha descritto un problema senza specificare il veicolo.");
        sb.AppendLine("Abbiamo trovato casi documentati per questo problema.");
        sb.AppendLine();
        sb.AppendLine("DOCUMENTI TROVATI E VEICOLI ASSOCIATI:");
        sb.AppendLine("Copia ESATTAMENTE questi oggetti nel campo documents.");
        sb.AppendLine();

        foreach (var doc in documents)
        {
            sb.AppendLine($"Documento: {doc.SiglaDocumento}");
            sb.AppendLine($"Titolo: {doc.TitoloDocumento}");
            sb.AppendLine($"Veicoli che hanno questo problema documentato:");

            // Group by brand + model + engine + fuel to avoid showing
            // duplicate entries that differ only by year range or chassis ID
            var uniqueCars = doc.Cars
                .GroupBy(c => new {
                    c.Marca,
                    c.Modello,
                    c.CodiceMotore,
                    c.Alimentazione
                })
                .Select(g => g.OrderBy(c => c.AnnoInizio).First())
                .OrderBy(c => c.Marca)
                .ThenBy(c => c.Modello)
                .ThenBy(c => c.Alimentazione)
                .ToList();

            // Cap at 8 cars maximum to keep the list manageable
            uniqueCars = uniqueCars.Take(8).ToList();

            foreach (var car in uniqueCars)
            {
                sb.AppendLine($"  {{");
                sb.AppendLine($"    \"idMacchina\": \"{car.IdMacchina}\",");
                sb.AppendLine($"    \"marca\": \"{car.Marca}\",");
                sb.AppendLine($"    \"modello\": \"{car.Modello}\",");
                sb.AppendLine($"    \"motorizzazione\": \"{car.Motorizzazione}\",");
                sb.AppendLine($"    \"codiceMotore\": \"{car.CodiceMotore}\",");
                sb.AppendLine($"    \"alimentazione\": \"{car.Alimentazione}\",");
                sb.AppendLine($"    \"annoInizio\": {car.AnnoInizio?.ToString() ?? "null"},");
                sb.AppendLine($"    \"annoFine\": {car.AnnoFine?.ToString() ?? "null"},");
                sb.AppendLine($"    \"kw\": {car.Kw?.ToString() ?? "null"},");
                sb.AppendLine($"    \"cavalli\": {car.Cavalli?.ToString() ?? "null"}");
                sb.AppendLine($"  }}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("REGOLE:");
        sb.AppendLine("1. phase deve essere \"symptom_cars\"");
        sb.AppendLine("2. message: spiega che il problema è documentato");
        sb.AppendLine("   per questi veicoli. Massimo 2 frasi in italiano.");
        sb.AppendLine("3. documents: array con i documenti trovati,");
        sb.AppendLine("   ciascuno con siglaDocumento, titoloDocumento, cars[]");
        sb.AppendLine("4. selectedCar: null finché il meccanico non sceglie");
        sb.AppendLine("5. Se il messaggio contiene una selezione");
        sb.AppendLine("   (primo/1/secondo/2 ecc.): selectedCar = il veicolo scelto");
        sb.AppendLine("6. Se il messaggio dice che la macchina non è presente");
        sb.AppendLine("   (non è presente / non c'è / non trovo / altro):");
        sb.AppendLine("   selectedCar = null");
        sb.AppendLine("   message = \"Abbiamo solo questi veicoli documentati");
        sb.AppendLine("   per questo problema. Se il tuo veicolo non è in lista,");
        sb.AppendLine("   descrivi marca, modello e tipo di motore per cercare");
        sb.AppendLine("   casi simili su veicoli analoghi.\"");
        sb.AppendLine("7. Tutti i valori numerici devono essere numeri JSON");
        sb.AppendLine("8. Non inventare veicoli — usa SOLO quelli elencati sopra");

        return sb.ToString();
    }

    /// <summary>
    /// Prompt for the repair answer phase.
    /// Receives pre-extracted fields so Gemini copies exact values — no paraphrasing.
    /// </summary>
    public static string RepairAnswer(
        CarInfo car,
        IReadOnlyList<PreExtractedCase> cases,
        string? dtcCode = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(JsonOnlyRule);
        sb.AppendLine();
        sb.AppendLine("Sei un assistente tecnico per officine meccaniche.");
        sb.AppendLine();
        sb.AppendLine("VEICOLO CONFERMATO:");
        sb.AppendLine(
            $"  {car.Marca} {car.Modello} {car.Motorizzazione} " +
            $"| {car.Alimentazione} | {car.Kw}kw/{car.Cavalli}cv " +
            $"| Codice motore: {car.CodiceMotore}");

        if (dtcCode is not null)
            sb.AppendLine($"  Codice guasto: {dtcCode}");

        sb.AppendLine();

        if (cases.Count == 0)
        {
            sb.AppendLine("Nessun documento trovato per questo veicolo e problema.");
            sb.AppendLine();
            sb.AppendLine("Rispondi con questo JSON esatto:");
            sb.AppendLine("{");
            sb.AppendLine("  \"phase\": \"chat\",");
            sb.AppendLine("  \"found\": false,");
            sb.AppendLine("  \"message\": \"Non ho casi documentati per questo problema " +
                          "su questo veicolo. Ti consiglio di collegare il diagnostico " +
                          "per ottenere codici guasto specifici.\",");
            sb.AppendLine("  \"cases\": []");
            sb.AppendLine("}");
            return sb.ToString();
        }

        sb.AppendLine("CASI DI RIPARAZIONE TROVATI:");
        sb.AppendLine("I valori qui sotto sono ESATTI — copiali nel JSON senza modifiche.");
        sb.AppendLine();

        for (int i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var dtcJson = "[" + string.Join(", ", c.Dtc.Select(d => $"\"{d}\"")) + "]";

            sb.AppendLine($"CASO {i + 1}:");
            sb.AppendLine($"  sigla: \"{c.SiglaDocumento}\"");
            sb.AppendLine($"  titolo: \"{c.TitoloDocumento?.Replace("\"", "\\\"")}\"");
            sb.AppendLine($"  stelle: {c.GradoAttendibilita}");
            sb.AppendLine($"  impianto: \"{c.Impianto.Replace("\"", "\\\"")}\"");
            sb.AppendLine($"  dispositivo: \"{c.Dispositivo.Replace("\"", "\\\"")}\"");
            sb.AppendLine($"  causa: \"{c.Causa.Replace("\"", "\\\"")}\"");
            sb.AppendLine($"  dtc: {dtcJson}");
            sb.AppendLine($"  procedura: \"{c.Intervento.Replace("\"", "\\\"")}\"");
            sb.AppendLine($"  nota: " +
                (c.Nota != null ? $"\"{c.Nota.Replace("\"", "\\\"")}\"" : "null"));
            sb.AppendLine();
        }

        sb.AppendLine("ISTRUZIONI:");
        sb.AppendLine("1. Valuta se i casi trovati sono rilevanti per il problema descritto.");
        sb.AppendLine("2. Se rilevanti: found=true, includi i casi nel campo cases.");
        sb.AppendLine("   COPIA I VALORI ESATTAMENTE come indicati sopra.");
        sb.AppendLine("   NON parafrasare. NON modificare. NON inventare.");
        sb.AppendLine("3. Se NON rilevanti: found=false, cases=[].");
        sb.AppendLine("4. message: una frase introduttiva breve in italiano.");
        sb.AppendLine("5. Massimo 3 casi, ordinati per stelle discendente.");
        sb.AppendLine();
        sb.AppendLine("Schema JSON da rispettare:");
        sb.AppendLine("{");
        sb.AppendLine("  \"phase\": \"chat\",");
        sb.AppendLine("  \"found\": true,");
        sb.AppendLine("  \"message\": \"...\",");
        sb.AppendLine("  \"cases\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"sigla\": \"GUP97569\",");
        sb.AppendLine("      \"titolo\": \"...\",");
        sb.AppendLine("      \"stelle\": 1,");
        sb.AppendLine("      \"impianto\": \"...\",");
        sb.AppendLine("      \"dispositivo\": \"...\",");
        sb.AppendLine("      \"causa\": \"...\",");
        sb.AppendLine("      \"dtc\": [\"P2279\"],");
        sb.AppendLine("      \"procedura\": \"...\",");
        sb.AppendLine("      \"nota\": null");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
