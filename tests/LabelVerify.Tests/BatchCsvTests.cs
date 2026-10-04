using System.Text;
using LabelVerify;

namespace LabelVerify.Tests;

public class BatchCsvTests
{
    const string Header = "file,brand_name,class_type,alcohol_content,net_contents,bottler,country_of_origin";

    static (List<BatchRow> Rows, List<string> Errors) Parse(string text, bool bom = false)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bom) bytes = [.. Encoding.UTF8.GetPreamble(), .. bytes];
        return BatchCsv.Parse(new MemoryStream(bytes));
    }

    [Fact]
    public void Reads_quoted_fields_with_commas()
    {
        var (rows, errors) = Parse($"{Header}\nold-tom.png,OLD TOM,Bourbon,45%,750 mL,\"Old Tom Distillery, Louisville, KY\",\n");
        Assert.Empty(errors);
        var row = Assert.Single(rows);
        Assert.Equal("old-tom.png", row.File);
        Assert.Equal("Old Tom Distillery, Louisville, KY", row.App.Bottler);
        Assert.Equal("", row.App.CountryOfOrigin);
    }

    [Fact]
    public void Handles_excel_byte_order_mark_and_any_column_order_or_case()
    {
        var (rows, errors) = Parse("Brand_Name,FILE,class_type,alcohol_content,net_contents,bottler,country_of_origin\nGLEN,glen.png,Scotch,43%,700 mL,Arden,Scotland\n", bom: true);
        Assert.Empty(errors);
        Assert.Equal("glen.png", Assert.Single(rows).File);
        Assert.Equal("GLEN", rows[0].App.BrandName);
    }

    [Fact]
    public void Reports_missing_columns() =>
        Assert.Contains("country_of_origin", Assert.Single(Parse("file,brand_name,class_type,alcohol_content,net_contents,bottler\n").Errors));

    [Fact]
    public void Reports_duplicate_and_blank_file_names_but_skips_empty_lines()
    {
        var (rows, errors) = Parse($"{Header}\na.png,A,,,,,\n\nA.PNG,A,,,,,\n,B,,,,,\n");
        Assert.Single(rows);
        Assert.Equal(2, errors.Count);
        Assert.Contains("more than once", errors[0]);
        Assert.Contains("blank", errors[1]);
    }

    [Fact]
    public void Header_only_is_an_error() =>
        Assert.Contains("no label rows", Assert.Single(Parse(Header + "\n").Errors));
}
