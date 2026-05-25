using Microsoft.AspNetCore.Mvc;
using SemaRepair.Api.Services;
using SemaRepair.Api.Services.Interfaces;
using SemaRepair.Api.Utils;

namespace SemaRepair.Api.Controllers;

/// <summary>
/// Fetches a single repair document by its sigla identifier.
/// Used by the frontend to display a document immediately after the mechanic
/// selects a car from the symptom or DTC search results — no Gemini call needed.
/// </summary>
[ApiController]
[Route("api/documents")]
public sealed class DocumentController : ControllerBase
{
    private readonly IDocumentSearchService _docs;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        IDocumentSearchService docs,
        ILogger<DocumentController> logger)
    {
        _docs   = docs;
        _logger = logger;
    }

    [HttpGet("{sigla}")]
    public async Task<IActionResult> GetAsync(string sigla, CancellationToken ct)
    {
        _logger.LogInformation("Document fetch: {Sigla}", sigla);

        var doc = await _docs.GetBySiglaAsync(sigla, ct);

        if (doc is null)
            return NotFound(new { error = $"Documento {sigla} non trovato." });

        return Ok(BuildResponse(doc));
    }

    /// <summary>
    /// Fallback endpoint used when the LLM corrupts the sigla.
    /// Finds the highest-grade document associated with the given car ID.
    /// </summary>
    [HttpGet("by-car/{idMacchina}")]
    public async Task<IActionResult> GetByCarAsync(string idMacchina, CancellationToken ct)
    {
        _logger.LogInformation("Document fetch by car: {IdMacchina}", idMacchina);

        var doc = await _docs.GetByCarIdAsync(idMacchina, ct);

        if (doc is null)
            return NotFound(new { error = $"Nessun documento trovato per il veicolo {idMacchina}." });

        return Ok(BuildResponse(doc));
    }

    /// <summary>
    /// Returns an alternative repair document for the same symptom,
    /// excluding the document the mechanic already viewed.
    /// When no alternative exists for the confirmed engine, returns related
    /// document title suggestions from a broader semantic search.
    /// </summary>
    [HttpGet("alternative")]
    public async Task<IActionResult> GetAlternativeAsync(
        [FromQuery] string symptom,
        [FromQuery] string? engineCode,
        [FromQuery] string excludeSigla,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symptom) || string.IsNullOrWhiteSpace(excludeSigla))
            return BadRequest(new { error = "symptom and excludeSigla are required." });

        _logger.LogInformation(
            "Alternative doc: symptom={Symptom}, engine={Engine}, exclude={Exclude}",
            symptom, engineCode, excludeSigla);

        var result = await _docs.FindAlternativeAsync(symptom, engineCode, excludeSigla, ct);

        if (result.Found && result.Document is not null)
            return Ok(new { found = true, document = BuildResponse(result.Document) });

        return Ok(new
        {
            found = false,
            message = "Non esistono altri documenti relativi a questo problema per il veicolo selezionato.",
            relatedSuggestions = result.Suggestions
                .Select(s => new { sigla = s.Sigla, titolo = s.Titolo })
                .ToList()
        });
    }

    private static object BuildResponse(RepairDocumentResult doc)
    {
        var extracted = DocumentExtractor.Extract(doc);
        return new
        {
            sigla       = doc.SiglaDocumento,
            titolo      = doc.TitoloDocumento,
            stelle      = doc.GradoAttendibilita,
            impianto    = extracted.Impianto,
            dispositivo = extracted.Dispositivo,
            anomalia    = extracted.Anomalia,
            causa       = extracted.Causa,
            dtc         = extracted.Dtc,
            intervento  = extracted.Intervento,
            procedura   = extracted.ProceduraDetail,
            nota        = extracted.Nota,
            engineCodes = doc.Cars
                .Select(c => c.CodiceMotore)
                .Distinct()
                .ToList()
        };
    }
}
