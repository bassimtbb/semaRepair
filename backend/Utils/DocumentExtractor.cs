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
    ///
    /// When identificazione is NULL (some documents lack it), falls back to
    /// parole_chiave to derive Impianto and Dispositivo from keywords.
    /// </summary>
    public static PreExtractedCase Extract(RepairDocumentResult doc)
    {
        var id       = doc.Identificazione ?? string.Empty;
        var proc     = doc.Procedura       ?? string.Empty;
        var combined = id + " " + proc;

        var impianto    = ExtractField(id, "Impianto");
        var dispositivo = ExtractField(id, "Dispositivo");
        var causa       = ExtractField(id, "Causa");

        // When identificazione is empty, derive Impianto and Dispositivo from
        // the pipe-separated parole_chiave, e.g. "INIEZIONE | DIESEL | EGR | P2279 |"
        if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(doc.ParoleChiave))
        {
            var (kwImpianto, kwDispositivo) = ExtractFromKeywords(doc.ParoleChiave);
            impianto    ??= kwImpianto;
            dispositivo ??= kwDispositivo;
        }

        return new PreExtractedCase(
            SiglaDocumento:     doc.SiglaDocumento,
            TitoloDocumento:    doc.TitoloDocumento,
            GradoAttendibilita: doc.GradoAttendibilita,
            Impianto:           impianto    ?? string.Empty,
            Dispositivo:        dispositivo ?? string.Empty,
            Anomalia:           ExtractField(id, "Anomalia") ?? doc.TitoloDocumento,
            Dtc:                ExtractDtcCodes(combined),
            Causa:              causa       ?? string.Empty,
            Intervento:         ExtractField(proc, "Intervento") ?? string.Empty,
            ProceduraDetail:    ExtractField(proc, "Procedura"),
            Nota:               ExtractField(proc, "Nota")
        );
    }

    /// <summary>
    /// Derives Impianto and Dispositivo from pipe-separated keywords when
    /// the identificazione chapter is absent. DTC codes and fuel types are
    /// filtered out; short all-uppercase words are kept as acronyms (EGR, DPF).
    /// </summary>
    private static (string? Impianto, string? Dispositivo) ExtractFromKeywords(
        string paroleChiave)
    {
        static string FormatKeyword(string s) =>
            // Keep short all-uppercase words as acronyms (EGR, DPF, ABS…)
            s.Length <= 5 && s == s.ToUpperInvariant()
                ? s
                : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

        var fuelWords = new HashSet<string>(
            ["DIESEL", "BENZINA", "GAS", "GPL", "METANO"],
            StringComparer.OrdinalIgnoreCase);

        var components = paroleChiave
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => k.Length > 0
                     && !Regex.IsMatch(k, @"^[PCBU]\d{4}$")   // not a DTC code
                     && !fuelWords.Contains(k))                  // not a fuel type
            .ToList();

        var impianto    = components.Count > 0 ? FormatKeyword(components[0]) : null;
        var dispositivo = components.Count > 1
            ? string.Join(", ", components.Skip(1).Select(FormatKeyword))
            : null;

        return (impianto, dispositivo);
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
    string? ProceduraDetail,
    string? Nota
);
