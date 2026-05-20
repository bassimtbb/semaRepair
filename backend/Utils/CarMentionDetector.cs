namespace SemaRepair.Api.Utils;

/// <summary>
/// Detects whether a message contains a car brand or model mention.
/// Used to decide whether to run car identification or symptom search.
/// If the mechanic mentions a brand we run identification first.
/// If no brand is mentioned we search symptoms directly.
/// </summary>
public static class CarMentionDetector
{
    // Italian car brands present in the database
    private static readonly string[] KnownBrands =
    [
        "fiat", "ford", "citroen", "citroën", "peugeot", "iveco"
    ];

    // Common car model keywords
    private static readonly string[] KnownModels =
    [
        "ducato", "fiesta", "focus", "ecosport", "kuga",
        "transit", "bravo", "punto", "stilo", "grande punto",
        "berlingo", "jumper", "boxer", "daily"
    ];

    /// <summary>
    /// Returns true if the message contains a car brand or model name.
    /// Case-insensitive search.
    /// </summary>
    public static bool ContainsCarMention(string message)
    {
        var lower = message.ToLowerInvariant();

        foreach (var brand in KnownBrands)
            if (lower.Contains(brand)) return true;

        foreach (var model in KnownModels)
            if (lower.Contains(model)) return true;

        return false;
    }
}
