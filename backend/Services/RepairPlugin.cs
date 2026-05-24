using Microsoft.EntityFrameworkCore;
using SemaRepair.Api.Data;
using SemaRepair.Api.Services.Interfaces;
using SemaRepair.Api.Utils;
using System.Text;

namespace SemaRepair.Api.Services;

/// <summary>
/// Contains all tools available to the Gemini LLM.
/// Each method is one tool the LLM can call.
///
/// Tools:
///   FindCar           — structured SQL search for car configurations
///   SearchByFaultCode — full-text search for fault code documents
///   SearchBySymptom   — semantic vector search for symptom documents
/// </summary>
public sealed class RepairPlugin
{
    private readonly AppDbContext _db;
    private readonly IDocumentSearchService _docSearch;
    private readonly ILogger<RepairPlugin> _logger;

    public RepairPlugin(
        AppDbContext db,
        IDocumentSearchService docSearch,
        ILogger<RepairPlugin> logger)
    {
        _db        = db;
        _docSearch = docSearch;
        _logger    = logger;
    }

    /// <summary>
    /// Finds car configurations matching the given criteria.
    /// Returns only cars that have at least one repair document,
    /// including the best document's sigla and title per car.
    /// </summary>
    public async Task<string> FindCarAsync(
        string? brand      = null,
        string? model      = null,
        int?    yearFrom   = null,
        int?    yearTo     = null,
        string? fuel       = null,
        string? engineCode = null,
        int?    kw         = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "FindCar: brand={Brand} model={Model} year={YearFrom}-{YearTo} " +
            "fuel={Fuel} engine={Engine} kw={Kw}",
            brand, model, yearFrom, yearTo, fuel, engineCode, kw);

        var query = _db.CarEmbeddings.AsQueryable();

        if (!string.IsNullOrWhiteSpace(brand))
            query = query.Where(c => EF.Functions.ILike(c.MarcaMacchina, $"%{brand}%"));

        if (!string.IsNullOrWhiteSpace(model))
            query = query.Where(c => EF.Functions.ILike(c.ModelloMacchina, $"%{model}%"));

        if (yearFrom.HasValue)
            query = query.Where(c => c.AnnoFine == null || c.AnnoFine >= yearFrom.Value);

        if (yearTo.HasValue)
            query = query.Where(c => c.AnnoInizio == null || c.AnnoInizio <= yearTo.Value);

        if (!string.IsNullOrWhiteSpace(fuel))
            query = query.Where(c => EF.Functions.ILike(c.AlimentazioneMacchina ?? "", $"%{fuel}%"));

        if (!string.IsNullOrWhiteSpace(engineCode))
            query = query.Where(c => c.CodiceMotoreMacchina == engineCode);

        if (kw.HasValue)
            query = query.Where(c => c.Kw == kw.Value);

        var cars = await query
            .GroupBy(c => new { c.CodiceMotoreMacchina, c.MarcaMacchina, c.ModelloMacchina })
            .Select(g => g.OrderBy(c => c.AnnoInizio).First())
            .Take(8)
            .ToListAsync(ct);

        if (!cars.Any())
            return "NESSUN_VEICOLO_TROVATO: Nessun veicolo corrisponde ai criteri forniti. " +
                   "Chiedi al meccanico di specificare marca, modello o anno.";

        // For each car, find its best repair document (highest grade).
        // Only cars with at least one document are included in the result.
        var results = new List<(string IdMacchina, string Marca, string Modello,
            string Motorizzazione, string CodiceMotore, string Alimentazione,
            int? AnnoInizio, int? AnnoFine, int? Kw, int? Cavalli,
            string Sigla, string Titolo)>();

        foreach (var car in cars)
        {
            var doc = await _docSearch.GetByCarIdAsync(car.IdMacchina, ct);
            if (doc is null) continue;
            results.Add((
                car.IdMacchina,
                car.MarcaMacchina,
                car.ModelloMacchina,
                car.MotorizzazioneMacchina ?? string.Empty,
                car.CodiceMotoreMacchina,
                car.AlimentazioneMacchina ?? string.Empty,
                car.AnnoInizio,
                car.AnnoFine,
                car.Kw,
                car.Cavalli,
                doc.SiglaDocumento,
                doc.TitoloDocumento
            ));
        }

        if (!results.Any())
            return "NESSUN_DOCUMENTO: Nessun documento di riparazione trovato per " +
                   "i veicoli che corrispondono ai criteri. " +
                   "Verifica i parametri del veicolo.";

        var sb = new StringBuilder();
        sb.AppendLine($"Trovati {results.Count} veicoli con documentazione:");
        sb.AppendLine();

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"Opzione {i + 1}:");
            sb.AppendLine($"  idMacchina: {r.IdMacchina}");
            sb.AppendLine($"  marca: {r.Marca}");
            sb.AppendLine($"  modello: {r.Modello}");
            sb.AppendLine($"  motorizzazione: {r.Motorizzazione}");
            sb.AppendLine($"  codiceMotore: {r.CodiceMotore}");
            sb.AppendLine($"  alimentazione: {r.Alimentazione}");
            sb.AppendLine($"  anni: {r.AnnoInizio}–{r.AnnoFine}");
            sb.AppendLine($"  potenza: {r.Kw} kW / {r.Cavalli} CV");
            sb.AppendLine($"  SiglaDocumento: {r.Sigla}");
            sb.AppendLine($"  TitoloDocumento: {r.Titolo}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Searches repair documents by fault code (codice guasto).
    /// </summary>
    public async Task<string> SearchByFaultCodeAsync(
        string faultCode,
        string? engineCode = null,
        string? brand = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SearchByFaultCode: code={Code} engine={Engine} brand={Brand}",
            faultCode, engineCode, brand);

        var docs = await _docSearch.SearchByDtcAsync(faultCode, engineCode, brand, ct);

        if (!docs.Any())
            return $"NESSUN_DOCUMENTO: Nessun caso documentato trovato per il " +
                   $"codice {faultCode}" +
                   (engineCode != null ? $" sul motore {engineCode}" : "") +
                   (brand      != null ? $" per la marca/modello {brand}" : "") + ".";

        // When a brand/model filter is active, strip cars that don't match from
        // the display list so Gemini only shows vehicles relevant to the filter.
        IReadOnlyList<RepairDocumentResult> displayDocs = docs;
        if (brand is not null)
        {
            displayDocs = docs
                .Select(d => d with
                {
                    Cars = d.Cars
                        .Where(c => MatchesBrandFilter(c, brand))
                        .ToList()
                })
                .Where(d => d.Cars.Any())
                .ToList();

            if (!displayDocs.Any())
                return $"NESSUN_DOCUMENTO: Nessun caso documentato trovato per il " +
                       $"codice {faultCode} per la marca/modello {brand}.";
        }

        return FormatDocuments(displayDocs, faultCode, showCars: true);
    }

    /// <summary>
    /// Searches repair documents by symptom description using vector similarity.
    /// </summary>
    public async Task<string> SearchBySymptomAsync(
        string symptom,
        string? engineCode = null,
        string? brand = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SearchBySymptom: symptom={Symptom} engine={Engine} brand={Brand}",
            symptom, engineCode, brand);

        var docs = await _docSearch.SearchBySymptomAsync(symptom, engineCode, brand, topK: 1, ct: ct);

        if (!docs.Any())
        {
            var wordCount = symptom.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            if (wordCount <= 3)
                return "SINTOMO_VAGO: La descrizione è troppo breve. Chiedi più dettagli.";

            return "NESSUN_DOCUMENTO: Nessun caso documentato trovato" +
                   (brand != null ? $" per la marca/modello {brand}" : "") + ".";
        }

        // No confirmed car — return a flat car list so the mechanic can select theirs.
        // Also use car-list path when brand is provided (B1b refinement): the mechanic
        // must still click to confirm their specific variant even with a brand filter.
        if (engineCode is null || brand is not null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SELEZIONA_VEICOLO: Ho trovato casi documentati per questo problema.");
            sb.AppendLine("Veicoli con casi documentati:");
            sb.AppendLine();

            foreach (var doc in docs.Take(1))
            {
                // When brand filter is active, only show cars matching that brand/model
                var filteredCars = brand is not null
                    ? doc.Cars.Where(c => MatchesBrandFilter(c, brand))
                    : doc.Cars;

                var uniqueCars = filteredCars
                    .GroupBy(c => new { c.Marca, c.Modello, c.CodiceMotore, c.Alimentazione })
                    .Select(g => g.OrderBy(c => c.AnnoInizio).First())
                    .Take(6)
                    .ToList();

                foreach (var car in uniqueCars)
                {
                    sb.AppendLine(
                        $"  idMacchina: {car.IdMacchina} | " +
                        $"{car.Marca} {car.Modello} {car.Motorizzazione} " +
                        $"({car.CodiceMotore}) | {car.Alimentazione} | " +
                        $"{car.AnnoInizio}–{car.AnnoFine} | " +
                        $"{car.Kw}kw/{car.Cavalli}cv | " +
                        $"SiglaDocumento: {doc.SiglaDocumento} | " +
                        $"TitoloDocumento: {doc.TitoloDocumento}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Car is confirmed — return document details directly.
        return FormatDocuments(docs, null, showCars: false);
    }

    /// <summary>
    /// Returns true when a car's brand or model matches every token in the
    /// brand filter string (e.g. "FIAT Ducato" → "FIAT" ∩ "Ducato").
    /// </summary>
    private static bool MatchesBrandFilter(CarInfo car, string brand) =>
        brand.Split(' ', StringSplitOptions.RemoveEmptyEntries)
             .All(t => car.Marca.Contains(t,   StringComparison.OrdinalIgnoreCase) ||
                       car.Modello.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string FormatDocuments(
        IReadOnlyList<RepairDocumentResult> docs,
        string? faultCode,
        bool showCars = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Trovati {docs.Count} casi documentati" +
            (faultCode != null ? $" per il codice {faultCode}" : "") + ":");
        sb.AppendLine();

        foreach (var doc in docs.Take(3))
        {
            var extracted = DocumentExtractor.Extract(doc);

            sb.AppendLine($"=== {doc.SiglaDocumento} ===");
            sb.AppendLine($"Titolo: {doc.TitoloDocumento}");
            sb.AppendLine($"Stelle: {doc.GradoAttendibilita}");
            sb.AppendLine($"Impianto: {extracted.Impianto}");
            sb.AppendLine($"Dispositivo: {extracted.Dispositivo}");
            sb.AppendLine($"Causa: {extracted.Causa}");
            sb.AppendLine($"Codici guasto: {string.Join(", ", extracted.Dtc)}");
            sb.AppendLine($"Intervento: {extracted.Intervento}");
            sb.AppendLine($"Nota: {extracted.Nota ?? "nessuna"}");
            sb.AppendLine();

            if (showCars && doc.Cars.Any())
            {
                sb.AppendLine("Veicoli applicabili:");
                foreach (var car in doc.Cars
                    .GroupBy(c => c.CodiceMotore)
                    .Select(g => g.First())
                    .Take(6))
                {
                    sb.AppendLine(
                        $"  idMacchina: {car.IdMacchina} | " +
                        $"{car.Marca} {car.Modello} {car.Motorizzazione} " +
                        $"({car.CodiceMotore}) | {car.Alimentazione} | " +
                        $"{car.AnnoInizio}–{car.AnnoFine} | " +
                        $"{car.Kw}kw/{car.Cavalli}cv | " +
                        $"SiglaDocumento: {doc.SiglaDocumento} | " +
                        $"TitoloDocumento: {doc.TitoloDocumento}");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
