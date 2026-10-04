using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelVerify.Pages;

/// <summary>Checks one label image against the application fields the agent types in.</summary>
public class IndexModel(LabelChecker checker) : PageModel
{
    [BindProperty] public IFormFile? LabelImage { get; set; }
    [BindProperty] public Application App { get; set; } = new(null, null, null, null, null, null);

    public CheckOutcome? Outcome { get; private set; }
    public string? ImageDataUrl { get; private set; }

    public async Task OnPostAsync(CancellationToken ct)
    {
        Outcome = await checker.CheckAsync(LabelImage, App, ct);
        if (Outcome.Results is not null && LabelImage is not null)
        {
            using var buffer = new MemoryStream();
            await LabelImage.CopyToAsync(buffer, ct);
            ImageDataUrl = $"data:{LabelImage.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
        }
    }
}
