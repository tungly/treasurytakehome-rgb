using LabelVerify;

namespace LabelVerify.Tests;

public class LabelCheckTests
{
    [Theory]
    [InlineData("Stone's Throw", "STONE'S THROW", Verdict.Match)]
    [InlineData("OLD TOM DISTILLERY", "OLD TOM DISTILLERY", Verdict.Match)]
    [InlineData("Old Tom Distillery", "OLD TOM DISTILERY", Verdict.NeedsReview)]
    [InlineData("Old Tom Distillery", "Old Tom", Verdict.NeedsReview)]
    [InlineData("Old Tom Distillery", "Blue Ridge Spirits", Verdict.Mismatch)]
    [InlineData("Old Tom Distillery", null, Verdict.NeedsReview)]
    public void Text_fields_forgive_case_and_punctuation(string expected, string? found, Verdict verdict) =>
        Assert.Equal(verdict, LabelCheck.CompareText("Brand name", expected, found).Verdict);

    [Theory]
    [InlineData("45% Alc./Vol. (90 Proof)", "45% Alc./Vol. (90 Proof)", Verdict.Match)]
    [InlineData("45", "Alc. 45% by Vol.", Verdict.Match)]
    [InlineData("40%", "45% Alc./Vol.", Verdict.Mismatch)]
    [InlineData("45%", "45% Alc./Vol. (80 Proof)", Verdict.Mismatch)]
    [InlineData("45%", "ninety proof", Verdict.NeedsReview)]
    public void Alcohol_content_compares_numbers_and_proof(string expected, string found, Verdict verdict) =>
        Assert.Equal(verdict, LabelCheck.CompareAlcohol(expected, found).Verdict);

    [Theory]
    [InlineData("750 mL", "750 ML", Verdict.Match)]
    [InlineData("750 mL", "75 cL", Verdict.Match)]
    [InlineData("1 L", "1000 ml", Verdict.Match)]
    [InlineData("1.75 Liters", "1750 mL", Verdict.Match)]
    [InlineData("12 fl oz", "355 mL", Verdict.Match)]
    [InlineData("750 mL", "700 mL", Verdict.Mismatch)]
    [InlineData("750 mL", "one bottle", Verdict.NeedsReview)]
    public void Net_contents_compares_in_milliliters(string expected, string found, Verdict verdict) =>
        Assert.Equal(verdict, LabelCheck.CompareNetContents(expected, found).Verdict);

    [Fact]
    public void Warning_matches_exact_text_across_line_breaks()
    {
        string wrapped = LabelCheck.GovernmentWarning.Replace(" (2)", "\n(2)").Replace("Surgeon ", "Surgeon\n  ");
        Assert.Equal(Verdict.Match, LabelCheck.CheckWarningText(wrapped).Verdict);
    }

    [Fact]
    public void Warning_in_title_case_is_rejected()
    {
        var result = LabelCheck.CheckWarningText(LabelCheck.GovernmentWarning.Replace("GOVERNMENT WARNING:", "Government Warning:"));
        Assert.Equal(Verdict.Mismatch, result.Verdict);
        Assert.Contains("Capital", result.Note);
    }

    [Fact]
    public void Warning_with_changed_wording_names_the_first_difference()
    {
        var result = LabelCheck.CheckWarningText(LabelCheck.GovernmentWarning.Replace("birth defects", "health issues"));
        Assert.Equal(Verdict.Mismatch, result.Verdict);
        Assert.Contains("\"birth\"", result.Note);
    }

    [Fact]
    public void Missing_warning_is_rejected() =>
        Assert.Equal(Verdict.Mismatch, LabelCheck.CheckWarningText(null).Verdict);

    [Fact]
    public void Exact_warning_that_is_partly_unreadable_goes_to_review()
    {
        var result = LabelCheck.CheckWarningText(LabelCheck.GovernmentWarning, fullyReadable: false);
        Assert.Equal(Verdict.NeedsReview, result.Verdict);
        Assert.Contains("hard to read", result.Note);
    }

    [Fact]
    public void Unreadable_warning_keeps_the_wording_difference_in_the_note()
    {
        var result = LabelCheck.CheckWarningText(LabelCheck.GovernmentWarning.Replace("birth defects", "[unreadable]"), fullyReadable: false);
        Assert.Equal(Verdict.NeedsReview, result.Verdict);
        Assert.Contains("\"birth\"", result.Note);
    }

    [Theory]
    [InlineData(false, Verdict.Match)]
    [InlineData(true, Verdict.NeedsReview)]
    [InlineData(null, Verdict.NeedsReview)]
    public void Tiny_or_unclear_warning_size_goes_to_review(bool? tooSmall, Verdict verdict) =>
        Assert.Equal(verdict, LabelCheck.CheckWarningSize(tooSmall).Verdict);

    [Theory]
    [InlineData(true, false, Verdict.Match)]
    [InlineData(false, false, Verdict.NeedsReview)]
    [InlineData(true, true, Verdict.NeedsReview)]
    [InlineData(null, false, Verdict.NeedsReview)]
    public void Warning_header_bold_and_body_not_bold(bool? header, bool? body, Verdict verdict) =>
        Assert.Equal(verdict, LabelCheck.CheckWarningBold(header, body).Verdict);

    [Fact]
    public void No_warning_found_skips_the_bold_and_size_rows()
    {
        var label = new ExtractedLabel("A", "B", "45%", "750 mL", null, null, null, null, null, null, null);
        var results = LabelCheck.Compare(new Application("A", "B", "45%", "750 mL", "C", null), label);
        Assert.Equal("Government warning wording", results[^1].Field);
        Assert.Equal(Verdict.Mismatch, results[^1].Verdict);
    }

    [Fact]
    public void Summary_counts_problems_and_items_to_review()
    {
        FieldResult R(Verdict v) => new("f", v, null, null);
        Assert.Equal("Does not match: 1 problem found, 4 items to review",
            LabelCheck.Summary([R(Verdict.Mismatch), R(Verdict.NeedsReview), R(Verdict.NeedsReview), R(Verdict.NeedsReview), R(Verdict.NeedsReview)]));
        Assert.Equal("Does not match: 2 problems found", LabelCheck.Summary([R(Verdict.Mismatch), R(Verdict.Mismatch), R(Verdict.Match)]));
        Assert.Equal("Needs your review: 1 item to review", LabelCheck.Summary([R(Verdict.NeedsReview), R(Verdict.Match)]));
        Assert.Equal("Everything matches", LabelCheck.Summary([R(Verdict.Match)]));
    }

    [Fact]
    public void Domestic_product_skips_country_of_origin()
    {
        var app = new Application("OLD TOM DISTILLERY", "Kentucky Straight Bourbon Whiskey", "45%", "750 mL",
            "Old Tom Distillery, Louisville, KY", null);
        var label = new ExtractedLabel("OLD TOM DISTILLERY", "Kentucky Straight Bourbon Whiskey",
            "45% Alc./Vol. (90 Proof)", "750 mL", "Old Tom Distillery, Louisville, KY", null,
            LabelCheck.GovernmentWarning, true, false, true, false);

        var results = LabelCheck.Compare(app, label);

        Assert.DoesNotContain(results, r => r.Field == "Country of origin");
        Assert.All(results, r => Assert.Equal(Verdict.Match, r.Verdict));
    }
}
