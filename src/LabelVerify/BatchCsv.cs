using Microsoft.VisualBasic.FileIO;

namespace LabelVerify;

/// <summary>One CSV row: the image files of one label (such as front and back) and its application fields.</summary>
public record BatchRow(IReadOnlyList<string> Files, Application App);

/// <summary>
/// Reads the batch CSV. Each row names the image files of one label and gives its application fields.
/// A label with several images lists them in the file column separated by "|", such as "front.jpg|back.jpg".
/// </summary>
public static class BatchCsv
{
    public static readonly string[] Columns =
        ["file", "brand_name", "class_type", "alcohol_content", "net_contents", "bottler", "country_of_origin"];

    /// <summary>Returns the rows, or a list of problems if the CSV cannot be used as is.</summary>
    public static (List<BatchRow> Rows, List<string> Errors) Parse(Stream csv)
    {
        var rows = new List<BatchRow>();
        var errors = new List<string>();
        using var parser = new TextFieldParser(new StreamReader(csv)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");

        string[]? header = parser.ReadFields();
        if (header is null)
            return (rows, ["The CSV file is empty."]);
        var index = header.Select((name, i) => (name: name.Trim().ToLowerInvariant(), i))
            .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First().i);
        var missing = Columns.Where(c => !index.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            return (rows, [$"The CSV is missing these columns: {string.Join(", ", missing)}."]);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parser.EndOfData)
        {
            long line = parser.LineNumber;
            string[]? f = parser.ReadFields();
            if (f is null || f.All(string.IsNullOrWhiteSpace)) continue;
            string Get(string column) => index[column] < f.Length ? f[index[column]] : "";

            var files = Get("file").Split('|').Select(name => Path.GetFileName(name.Trim())).Where(name => name.Length > 0).ToList();
            var repeated = files.Where(name => !seen.Add(name)).ToList();
            if (files.Count == 0)
                errors.Add($"Line {line}: the file column is blank.");
            else if (files.Count > LabelChecker.MaxImages)
                errors.Add($"Line {line}: list up to {LabelChecker.MaxImages} images for one label.");
            else if (repeated.Count > 0)
                errors.Add($"Line {line}: {string.Join(", ", repeated)} is listed more than once.");
            else
                rows.Add(new(files, new Application(Get("brand_name"), Get("class_type"), Get("alcohol_content"),
                    Get("net_contents"), Get("bottler"), Get("country_of_origin"))));
        }
        if (rows.Count == 0 && errors.Count == 0)
            errors.Add("The CSV has a header but no label rows.");
        return (rows, errors);
    }
}
