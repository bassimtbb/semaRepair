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
///   SearchByFaultCode — full-text + ILIKE search for fault code documents
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
        _db       = db;
        _docSearch = docSearch;
        _logger   = logger;
    }

    /// <summary>
    /// Finds car configurations matching the given criteria.
    /// Uses structured SQL filtering — NOT semantic search.
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
            query = query.Where(c =>
                EF.Functions.ILike(c.MarcaMacchina, $"%{brand}%"));

        if (!string.IsNullOrWhiteSpace(model))
            query = query.Where(c =>
                EF.Functions.ILike(c.ModelloMacchina, $"%{model}%"));

        if (yearFrom.HasValue)
            // Car must not have ended before yearFrom
            query = query.Where(c =>
                c.AnnoFine == null || c.AnnoFine >= yearFrom.Value);

        if (yearTo.HasValue)
            // Car must not have started after yearTo
            query = query.Where(c =>
                c.AnnoInizio == null || c.AnnoInizio <= yearTo.Value);

        if (!string.IsNullOrWhiteSpace(fuel))
            query = query.Where(c =>
                EF.Functions.ILike(c.AlimentazioneMacchina ?? "", $"%{fuel}%"));

        if (!string.IsNullOrWhiteSpace(engineCode))
            query = query.Where(c =>
                c.CodiceMotoreMacchina == engineCode);

        if (kw.HasValue)
            query = query.Where(c => c.Kw == kw.Value);

        // Group by engine code to avoid showing duplicate engine variants.
        // Take the earliest year variant as representative.
        var cars = await query
            .GroupBy(c => new { c.CodiceMotoreMacchina, c.MarcaMacchina, c.ModelloMacchina })
            .Select(g => g.OrderBy(c => c.AnnoInizio).First())
            .Take(6)
            .ToListAsync(ct);

        if (!cars.Any())
            return "NESSUN_VEICOLO_TROVATO: Nessun veicolo corrisponde ai criteri forniti. " +
                   "Chiedi al meccanico di specificare marca, modello o anno.";

        var sb = new StringBuilder();
        sb.AppendLine($"Trovati {cars.Count} veicoli:");
        sb.AppendLine();

        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            sb.AppendLine($"Opzione {i + 1}:");
            sb.AppendLine($"  idMacchina: {c.IdMacchina}");
            sb.AppendLine($"  marca: {c.MarcaMacchina}");
            sb.AppendLine($"  modello: {c.ModelloMacchina}");
            sb.AppendLine($"  motorizzazione: {c.MotorizzazioneMacchina}");
            sb.AppendLine($"  codiceMotore: {c.CodiceMotoreMacchina}");
            sb.AppendLine($"  alimentazione: {c.AlimentazioneMacchina}");
            sb.AppendLine($"  anni: {c.AnnoInizio}–{c.AnnoFine}");
            sb.AppendLine($"  potenza: {c.Kw} kW / {c.Cavalli} CV");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Searches repair documents by fault code (codice guasto).
    /// Uses full-text search first, falls back to ILIKE.
    /// </summary>
    public async Task<string> SearchByFaultCodeAsync(
        string faultCode,
        string? engineCode = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SearchByFaultCode: code={Code} engine={Engine}",
            faultCode, engineCode);

        var docs = await _docSearch.SearchByDtcAsync(faultCode, engineCode, ct);

        if (!docs.Any())
            return $"NESSUN_DOCUMENTO: Nessun caso documentato trovato per il " +
                   $"codice {faultCode}" +
                   (engineCode != null ? $" sul motore {engineCode}" : "") + ".";

        return FormatDocuments(docs, faultCode, showCars: true);
    }

    /// <summary>
    /// Searches repair documents by symptom description using vector similarity.
    /// </summary>
    public async Task<string> SearchBySymptomAsync(
        string symptom,
        string? engineCode = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SearchBySymptom: symptom={Symptom} engine={Engine}",
            symptom, engineCode);

        // topK=1 always — topK=3 returns too many loosely related documents
        var docs = await _docSearch.SearchBySymptomAsync(symptom, engineCode, topK: 1, ct: ct);

        if (!docs.Any())
        {
            var wordCount = symptom.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            if (wordCount <= 4)
                return "SINTOMO_VAGO: Descrivi meglio il problema.";

            return "NESSUN_DOCUMENTO: Nessun caso documentato trovato.";
        }

        // If no car is confirmed, return only the car list.
        // The mechanic must select their car before seeing the procedure.
        if (engineCode is null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SELEZIONA_VEICOLO: Ho trovato casi documentati " +
                          "per questo problema. Il meccanico deve prima " +
                          "selezionare il suo veicolo.");
            sb.AppendLine();
            sb.AppendLine("Veicoli con casi documentati per questo problema:");
            sb.AppendLine();

            foreach (var doc in docs.Take(1))
            {
                sb.AppendLine($"Documento: {doc.SiglaDocumento} — {doc.TitoloDocumento}");
                sb.AppendLine("Veicoli applicabili:");

                var uniqueCars = doc.Cars
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
                        $"{car.Kw}kw/{car.Cavalli}cv");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Car is confirmed — omit cars list so Gemini doesn't confuse it
        // with a selection prompt and incorrectly returns symptom_cars phase
        return FormatDocuments(docs, null, showCars: false);
    }

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

            // Only show applicable cars when explicitly requested.
            // When the car is already confirmed, omitting this section prevents
            // Gemini from confusing the car list with a selection prompt.
            if (showCars && doc.Cars.Any())
            {
                sb.AppendLine("Veicoli applicabili:");
                foreach (var car in doc.Cars
                    .GroupBy(c => c.CodiceMotore)
                    .Select(g => g.First())
                    .Take(5))
                {
                    sb.AppendLine(
                        $"  - {car.Marca} {car.Modello} {car.Motorizzazione} " +
                        $"({car.CodiceMotore}) {car.AnnoInizio}–{car.AnnoFine}");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
