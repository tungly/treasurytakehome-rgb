using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelVerify.Pages;

/// <summary>Checks one label (up to 4 images, such as front and back) against the application fields the agent types in.</summary>
public class IndexModel(LabelChecker checker) : PageModel
{
    [BindProperty] public List<IFormFile> LabelImages { get; set; } = [];
    [BindProperty] public Application App { get; set; } = new(null, null, null, null, null, null);

    public CheckOutcome? Outcome { get; private set; }
    public List<string> ImageDataUrls { get; } = [];

    public async Task OnPostAsync(CancellationToken ct)
    {
        Outcome = await checker.CheckAsync(LabelImages, App, ct);
        if (Outcome.Results is null) return;
        foreach (var image in LabelImages)
        {
            using var buffer = new MemoryStream();
            await image.CopyToAsync(buffer, ct);
            ImageDataUrls.Add($"data:{image.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}");
        }
    }
}
