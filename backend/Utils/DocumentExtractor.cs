using System.Text.RegularExpressions;
using SemaRepair.Api.Services;

namespace SemaRepair.Api.Utils;

/// <summary>
/// Extracts structured fields from the raw identificazione and procedura
/// chapter text. This runs in the backend so Gemini never needs to
/// paraphrase — we give it exact values to include in the JSON.
///
/// The identificazione chapter has this structure:
///   Impianto: {value}
///   Dispositivo: {value}
///   Anomalia: {value}
///   Errori rilevati dall'autodiagnosi: {codes}
///   Causa: {value}
///
/// The procedura chapter has this structure:
///   Intervento: {value}
///   Procedura: {value}
///   Nota: {value}
/// </summary>
public static class DocumentExtractor
{
    // Known field labels used as stop-word boundaries during extraction
    private static readonly string[] StopWords =
    [
        "Impianto:", "Dispositivo:", "Anomalia:",
        "Errori rilevati", "Causa:", "Intervento:",
        "Procedura:", "Nota:"
    ];

    // Source-format placeholders that mean "no value"
    private static readonly string[] EmptyPlaceholders = ["- -", "--", "- ", "-"];

    /// <summary>
    /// Extracts a named field value from a text block.
    /// Searches for "{fieldName}: {value}" and returns the value.
    /// Returns null if the field is not found or the value is an empty placeholder.
    /// </summary>
    public static string? ExtractField(string text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var prefix = fieldName.ToLowerInvariant() + ":";

        // First pass: line-by-line (handles the typical multi-line format)
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.ToLowerInvariant().StartsWith(prefix))
            {
                var raw = trimmed[prefix.Length..].Trim();
                // Truncate at any stop-word appearing inline on the same line
                var cut = raw.Length;
                foreach (var stop in StopWords)
                {
                    var si = raw.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                    if (si > 0 && si < cut) cut = si;
                }
                var value = raw[..cut].Trim();
                var pipeIdx = value.IndexOf('|');
                if (pipeIdx > 0)
                    value = value[..pipeIdx].Trim();
                if (!string.IsNullOrEmpty(value) && !IsEmptyPlaceholder(value))
                    return value;
            }
        }

        // Fallback: locate inline occurrence and read until the next known field
        var idx = text.ToLowerInvariant().IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;

        var rest = text[(idx + prefix.Length)..].TrimStart();

        var end = rest.Length;
        foreach (var stop in StopWords)
        {
            var stopIdx = rest.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
            if (stopIdx > 0 && stopIdx < end)
                end = stopIdx;
        }

        var result = rest[..end].Trim();
        var pipeSep = result.IndexOf('|');
        if (pipeSep > 0)
            result = result[..pipeSep].Trim();
        if (string.IsNullOrEmpty(result) || IsEmptyPlaceholder(result)) return null;
        return result;
    }

    private static bool IsEmptyPlaceholder(string value) =>
        EmptyPlaceholders.Any(p => string.Equals(value, p, StringComparison.Ordinal));

    /// <summary>
    /// Extracts DTC codes from the text.
    /// Looks for patterns like P2279, C1110, B1234, U0155.
    /// </summary>
    public static List<string> ExtractDtcCodes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = Regex.Matches(
            text,
            @"\b([PCBU][0-9]{4})\b",
            RegexOptions.IgnoreCase);

        return matches
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Extracts all structured fields from a repair document.
    /// Returns a PreExtractedCase with exact text from the source.
    /// </summary>
    public static PreExtractedCase Extract(RepairDocumentResult doc)
    {
        var id = doc.Identificazione ?? string.Empty;
        var proc = doc.Procedura ?? string.Empty;
        var combined = id + " " + proc;

        return new PreExtractedCase(
            SiglaDocumento:     doc.SiglaDocumento,
            TitoloDocumento:    doc.TitoloDocumento,
            GradoAttendibilita: doc.GradoAttendibilita,
            Impianto:           ExtractField(id, "Impianto")    ?? string.Empty,
            Dispositivo:        ExtractField(id, "Dispositivo") ?? string.Empty,
            Anomalia:           ExtractField(id, "Anomalia")    ?? doc.TitoloDocumento,
            Dtc:                ExtractDtcCodes(combined),
            Causa:              ExtractField(id, "Causa")       ?? string.Empty,
            Intervento:         ExtractField(proc, "Intervento") ?? string.Empty,
            Nota:               ExtractField(proc, "Nota")
        );
    }
}

/// <summary>
/// Pre-extracted repair case fields — exact text from source documents.
/// Passed to Gemini so it includes them verbatim in the JSON response.
/// </summary>
public sealed record PreExtractedCase(
    string SiglaDocumento,
    string TitoloDocumento,
    int GradoAttendibilita,
    string Impianto,
    string Dispositivo,
    string Anomalia,
    List<string> Dtc,
    string Causa,
    string Intervento,
    string? Nota
);
