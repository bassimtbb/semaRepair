using System.Text.RegularExpressions;

namespace SemaRepair.Api.Utils;

/// <summary>
/// Detects OBD-II DTC fault codes in free text.
///
/// DTC format: one letter (P, C, B, U) followed by exactly 4 digits.
/// Examples: P2279, C1110, U0155, B1234
///
/// The four letter prefixes indicate the system:
///   P = Powertrain (engine, transmission)
///   C = Chassis (ABS, suspension)
///   B = Body (airbags, comfort)
///   U = Network (CAN bus communication)
/// </summary>
public static class DtcDetector
{
    // Word boundary ensures we do not match partial strings
    // e.g. "P22790" should not match as "P2279"
    private static readonly Regex DtcPattern = new(
        @"\b([PCBU][0-9]{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extracts the first DTC code found in the message.
    /// Returns null if no DTC code is present.
    /// </summary>
    /// <param name="message">The mechanic's message text.</param>
    public static string? Extract(string message)
    {
        var match = DtcPattern.Match(message);
        return match.Success
            ? match.Value.ToUpperInvariant()
            : null;
    }

    /// <summary>
    /// Returns all DTC codes found in the message, deduplicated.
    /// </summary>
    public static IReadOnlyList<string> ExtractAll(string message) =>
        DtcPattern.Matches(message)
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
}
