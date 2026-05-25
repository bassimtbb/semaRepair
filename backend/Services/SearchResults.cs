namespace SemaRepair.Api.Services;

/// <summary>
/// Minimal car information attached to a search result.
/// </summary>
public sealed record CarInfo(
    string IdMacchina,
    string Marca,
    string Modello,
    string Motorizzazione,
    string CodiceMotore,
    string Alimentazione,
    int? AnnoInizio,
    int? AnnoFine,
    int? Kw,
    int? Cavalli
);

/// <summary>
/// A car configuration returned by the car similarity search.
/// Includes a similarity score (0.0 to 1.0) for ranking.
/// </summary>
public sealed record CarSearchResult(
    string IdMacchina,
    string Marca,
    string Modello,
    string Motorizzazione,
    string CodiceMotore,
    string Alimentazione,
    int? AnnoInizio,
    int? AnnoFine,
    int? Kw,
    int? Cavalli
);

/// <summary>
/// A complete repair document returned by the document search.
/// Contains all chapter content and the list of cars it applies to.
/// </summary>
public sealed record RepairDocumentResult(
    string SiglaDocumento,
    string TitoloDocumento,
    string ParoleChiave,
    int GradoAttendibilita,
    string Identificazione,
    string Procedura,
    IReadOnlyList<CarInfo> Cars
);

/// <summary>
/// A lightweight document title suggestion, returned when no alternative
/// document exists for the confirmed car and a broader search is used instead.
/// </summary>
public sealed record DocumentSuggestion(string Sigla, string Titolo);

/// <summary>
/// Result of the "find alternative document" search.
/// When Found is true, Document is the next most relevant document.
/// When Found is false, Suggestions lists related documents from a broader search.
/// </summary>
public sealed record AlternativeDocumentResult(
    bool Found,
    RepairDocumentResult? Document,
    IReadOnlyList<DocumentSuggestion> Suggestions
);
