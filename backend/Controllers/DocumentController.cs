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
