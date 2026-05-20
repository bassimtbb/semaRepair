namespace SemaRepair.Api.Dtos;

/// <summary>
/// The car the mechanic has confirmed as their vehicle.
/// Sent in every request after confirmation so the backend
/// can filter search results to this specific engine.
/// </summary>
public sealed record ConfirmedCarDto(
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
