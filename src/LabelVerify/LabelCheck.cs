using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LabelVerify;

/// <summary>Ordered from best to worst, so the highest value is the worst result.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Verdict>))]
public enum Verdict { Match, NeedsReview, Mismatch }

/// <summary>The result of checking one field. Expected is the application value; Found is what the label says.</summary>
public record FieldResult(string Field, Verdict Verdict, string? Expected, string? Found, string Note = "");

/// <summary>What the applicant typed into the application. CountryOfOrigin is blank for domestic products.</summary>
public record Application(
    string? BrandName,
    string? ClassType,
    string? AlcoholContent,
    string? NetContents,
    string? Bottler,
    string? CountryOfOrigin);

/// <summary>What was read off the label image. A null field means it was not found on the label.</summary>
/// <param name="WarningHeaderBold">Whether "GOVERNMENT WARNING:" looks bold. Null means the reader could not tell.</param>
/// <param name="WarningBodyBold">Whether the rest of the warning looks bold. Null means the reader could not tell.</param>
/// <param name="WarningFullyReadable">False when glare, blur or cropping hides part of the warning. Null means no warning.</param>
/// <param name="WarningTooSmall">Whether the warning looks tiny or hard to find next to the other text. Null means the reader could not tell.</param>
public record ExtractedLabel(
    string? BrandName,
    string? ClassType,
    string? AlcoholContent,
    string? NetContents,
    string? Bottler,
    string? CountryOfOrigin,
    string? GovernmentWarning,
    bool? WarningHeaderBold,
    bool? WarningBodyBold,
    bool? WarningFullyReadable,
    bool? WarningTooSmall);

/// <summary>Compares a label against its application, one field at a time.</summary>
public static partial class LabelCheck
{
    /// <summary>Required wording from 27 CFR 16.21. 27 CFR 16.22 adds the capitals and bold rules.</summary>
    public const string GovernmentWarning =
        "GOVERNMENT WARNING: (1) According to the Surgeon General, women should not drink alcoholic beverages " +
        "during pregnancy because of the risk of birth defects. (2) Consumption of alcoholic beverages impairs " +
        "your ability to drive a car or operate machinery, and may cause health problems.";

    /// <summary>The worst result wins: any Mismatch makes the label a Mismatch, then any Needs review.</summary>
    public static Verdict Overall(IEnumerable<FieldResult> results) =>
        results.Select(r => r.Verdict).DefaultIfEmpty(Verdict.Match).Max();

    public static List<FieldResult> Compare(Application app, ExtractedLabel label)
    {
        var results = new List<FieldResult>
        {
            CompareText("Brand name", app.BrandName, label.BrandName),
            CompareText("Class / type", app.ClassType, label.ClassType),
            CompareAlcohol(app.AlcoholContent, label.AlcoholContent),
            CompareNetContents(app.NetContents, label.NetContents),
            CompareText("Bottler name and address", app.Bottler, label.Bottler),
        };
        if (!string.IsNullOrWhiteSpace(app.CountryOfOrigin) || !string.IsNullOrWhiteSpace(label.CountryOfOrigin))
            results.Add(CompareText("Country of origin", app.CountryOfOrigin, label.CountryOfOrigin));
        results.Add(CheckWarningText(label.GovernmentWarning, label.WarningFullyReadable));
        results.Add(CheckWarningBold(label.WarningHeaderBold, label.WarningBodyBold));
        results.Add(CheckWarningSize(label.WarningTooSmall));
        return results;
    }

    /// <summary>
    /// Ignores case, punctuation and spacing, so "STONE'S THROW" matches "Stone's Throw".
    /// A near miss (a typo, or one value inside the other) goes to a person instead of failing.
    /// </summary>
    public static FieldResult CompareText(string field, string? expected, string? found)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return new(field, Verdict.NeedsReview, expected, found, "No value in the application.");
        if (string.IsNullOrWhiteSpace(found))
            return new(field, Verdict.NeedsReview, expected, found, "Not found on the label. Check by eye.");

        string a = Normalize(expected), b = Normalize(found);
        if (a == b)
        {
            string note = expected.Trim() == found.Trim() ? "" : "Same words; only case or punctuation differ.";
            return new(field, Verdict.Match, expected, found, note);
        }
        if (a.Contains(b) || b.Contains(a))
            return new(field, Verdict.NeedsReview, expected, found, "One value is part of the other.");
        if (Levenshtein(a, b) <= Math.Max(2, a.Length / 5))
            return new(field, Verdict.NeedsReview, expected, found, "Very close. Possible typo.");
        return new(field, Verdict.Mismatch, expected, found);
    }

    /// <summary>Compares the ABV numbers, and checks that any proof on the label is twice the ABV.</summary>
    public static FieldResult CompareAlcohol(string? expected, string? found)
    {
        const string field = "Alcohol content";
        if (string.IsNullOrWhiteSpace(found))
            return new(field, Verdict.NeedsReview, expected, found, "Not found on the label. Check by eye.");

        decimal? want = ParseAbv(expected) ?? ParseBareNumber(expected), got = ParseAbv(found);
        if (want is null || got is null)
            return new(field, Verdict.NeedsReview, expected, found, "Could not read a percentage. Check by eye.");
        if (want != got)
            return new(field, Verdict.Mismatch, expected, found, $"Application says {want}%, label says {got}%.");

        decimal? proof = ParseNumberBefore(found, ProofPattern());
        if (proof is not null && proof != got * 2)
            return new(field, Verdict.Mismatch, expected, found, $"{proof} proof should be {got * 2} proof for {got}% ABV.");
        return new(field, Verdict.Match, expected, found);
    }

    /// <summary>Converts both amounts to mL first, so "750 mL" matches "75 cL" and "12 fl oz" matches "355 mL".</summary>
    public static FieldResult CompareNetContents(string? expected, string? found)
    {
        const string field = "Net contents";
        if (string.IsNullOrWhiteSpace(found))
            return new(field, Verdict.NeedsReview, expected, found, "Not found on the label. Check by eye.");

        decimal? want = ParseMilliliters(expected), got = ParseMilliliters(found);
        if (want is null || got is null)
            return new(field, Verdict.NeedsReview, expected, found, "Could not read an amount and unit. Check by eye.");
        // Labels round fl oz conversions (12 fl oz = 354.9 mL is printed as 355 mL), so allow 0.5%.
        if (Math.Abs(want.Value - got.Value) <= Math.Max(1m, want.Value * 0.005m))
            return new(field, Verdict.Match, expected, found);
        return new(field, Verdict.Mismatch, expected, found, $"Application is {want:0.#} mL, label is {got:0.#} mL.");
    }

    /// <summary>
    /// The wording must be exact, capitals included. Only line breaks and extra spaces are forgiven.
    /// If part of the warning is hard to read in the photo, any result goes to a person, because the reader may have guessed words.
    /// </summary>
    public static FieldResult CheckWarningText(string? found, bool? fullyReadable = true)
    {
        var result = CheckWarningWording(found);
        if (fullyReadable != false)
            return result;
        string note = "Part of the warning is hard to read in this photo, so the wording may be guessed. Check by eye.";
        return result with { Verdict = Verdict.NeedsReview, Note = result.Note.Length > 0 ? $"{note} {result.Note}" : note };
    }

    /// <summary>Flags a warning that looks tiny or hard to find. A photo has no scale, so this is a judgment, not a measurement.</summary>
    public static FieldResult CheckWarningSize(bool? tooSmall)
    {
        const string field = "Government warning type size";
        const string rule = "Easy to find, not tiny";
        return tooSmall switch
        {
            false => new(field, Verdict.Match, rule, "Not noticeably small"),
            true => new(field, Verdict.NeedsReview, rule, "Looks tiny or hard to find",
                "Check the type size by eye against the minimum sizes in 27 CFR 16.22."),
            null => new(field, Verdict.NeedsReview, rule, null, "Could not tell from the image. Check by eye."),
        };
    }

    static FieldResult CheckWarningWording(string? found)
    {
        const string field = "Government warning wording";
        const string required = "Required wording (27 CFR 16.21)";
        if (string.IsNullOrWhiteSpace(found))
            return new(field, Verdict.Mismatch, required, found, "Warning statement not found on the label.");

        string got = Spaces().Replace(found.Trim(), " ");
        if (got == GovernmentWarning)
            return new(field, Verdict.Match, required, "Same as required wording");
        if (string.Equals(got, GovernmentWarning, StringComparison.OrdinalIgnoreCase))
            return new(field, Verdict.Mismatch, required, found, "Capital letters differ from the required text.");
        return new(field, Verdict.Mismatch, required, found, FirstDifference(GovernmentWarning, got));
    }

    /// <summary>
    /// "GOVERNMENT WARNING:" must be bold and the rest must not be (27 CFR 16.22).
    /// Bold is a judgment call from the image, so a problem goes to a person instead of failing.
    /// </summary>
    public static FieldResult CheckWarningBold(bool? headerBold, bool? bodyBold)
    {
        const string field = "Government warning bold type";
        const string rule = "Header bold, rest not bold";
        if (headerBold is null || bodyBold is null)
            return new(field, Verdict.NeedsReview, rule, null, "Could not tell from the image. Check by eye.");
        if (headerBold == false)
            return new(field, Verdict.NeedsReview, rule, "Header not bold", "\"GOVERNMENT WARNING:\" does not look bold. Check by eye.");
        if (bodyBold == true)
            return new(field, Verdict.NeedsReview, rule, "Rest is bold", "The rest of the warning looks bold. Check by eye.");
        return new(field, Verdict.Match, rule, "Header bold, rest not bold");
    }

    static string Normalize(string s) =>
        Spaces().Replace(NonAlphanumeric().Replace(s.ToUpperInvariant().Replace("'", "").Replace("’", ""), " "), " ").Trim();

    static decimal? ParseAbv(string? s) => s is null ? null : ParseNumberBefore(s, PercentPattern());

    static decimal? ParseBareNumber(string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;

    static decimal? ParseNumberBefore(string s, Regex pattern)
    {
        var m = pattern.Match(s);
        return m.Success ? decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    static decimal? ParseMilliliters(string? s)
    {
        if (s is null) return null;
        var m = AmountPattern().Match(s);
        if (!m.Success) return null;
        decimal amount = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        string unit = m.Groups[2].Value.ToLowerInvariant();
        decimal perUnit =
            unit.StartsWith("m") ? 1m :
            unit.StartsWith("c") ? 10m :
            unit.StartsWith("l") ? 1000m :
            29.5735m; // fluid ounces
        return amount * perUnit;
    }

    static string FirstDifference(string expected, string found)
    {
        string[] want = expected.Split(' '), got = found.Split(' ');
        for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
        {
            string w = i < want.Length ? want[i] : "(nothing)", g = i < got.Length ? got[i] : "(nothing)";
            if (w != g) return $"Wording differs at word {i + 1}: expected \"{w}\", found \"{g}\".";
        }
        return "Wording differs from the required text.";
    }

    static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^A-Z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*proof", RegexOptions.IgnoreCase)]
    private static partial Regex ProofPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(ml|milliliters?|millilitres?|cl|centiliters?|centilitres?|l|liters?|litres?|fl\.?\s*oz\.?|fluid\s+ounces?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();
}
