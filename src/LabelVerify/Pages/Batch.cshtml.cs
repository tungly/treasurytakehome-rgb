using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelVerify.Pages;

/// <summary>
/// Checks many labels at once. The browser reads the CSV through ParseCsv, then sends one label at a time to Check,
/// so a long batch never holds one request open and progress shows as each label finishes.
/// </summary>
public class BatchModel(LabelChecker checker) : PageModel
{
    const long MaxCsvBytes = 1024 * 1024;

    public void OnGet() { }

    public IActionResult OnPostParseCsv(IFormFile? csv)
    {
        if (csv is null || csv.Length == 0)
            return new JsonResult(new { rows = Array.Empty<BatchRow>(), errors = new[] { "Choose the CSV file first." } });
        if (csv.Length > MaxCsvBytes)
            return new JsonResult(new { rows = Array.Empty<BatchRow>(), errors = new[] { "The CSV file is larger than 1 MB." } });
        using var stream = csv.OpenReadStream();
        var (rows, errors) = BatchCsv.Parse(stream);
        return new JsonResult(new { rows, errors });
    }

    public async Task<IActionResult> OnPostCheckAsync(IFormFile? image, Application app, CancellationToken ct) =>
        new JsonResult(await checker.CheckAsync(image, app, ct));
}
