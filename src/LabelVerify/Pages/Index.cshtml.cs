using System.ClientModel;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelVerify.Pages;

/// <summary>Checks one label image against the application fields the agent types in.</summary>
public class IndexModel(LabelReader reader, ILogger<IndexModel> log) : PageModel
{
    const long MaxBytes = 10 * 1024 * 1024;
    static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    [BindProperty] public IFormFile? LabelImage { get; set; }
    [BindProperty] public Application App { get; set; } = new(null, null, null, null, null, null);

    public List<FieldResult>? Results { get; private set; }
    public string? Error { get; private set; }
    public double Seconds { get; private set; }
    public string? ImageDataUrl { get; private set; }

    public async Task OnPostAsync(CancellationToken ct)
    {
        if (LabelImage is null || LabelImage.Length == 0)
        {
            Error = "Choose a label image first.";
            return;
        }
        if (LabelImage.Length > MaxBytes)
        {
            Error = "That image is larger than 10 MB. Use a smaller photo.";
            return;
        }
        if (!ImageTypes.Contains(LabelImage.ContentType))
        {
            Error = "That file type is not supported. Save the image as JPG or PNG and try again.";
            return;
        }

        using var buffer = new MemoryStream();
        await LabelImage.CopyToAsync(buffer, ct);
        byte[] image = buffer.ToArray();
        ImageDataUrl = $"data:{LabelImage.ContentType};base64,{Convert.ToBase64String(image)}";

        var timer = Stopwatch.StartNew();
        try
        {
            var label = await reader.ReadAsync(image, LabelImage.ContentType, ct);
            Results = LabelCheck.Compare(App, label);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            Error = "The label reader is busy. Wait a minute and try again.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Reading label {File} failed", LabelImage.FileName);
            Error = "The label could not be read. Try again, or check this label by eye.";
        }
        Seconds = timer.Elapsed.TotalSeconds;
        log.LogInformation("Checked {File} in {Seconds:0.00}s", LabelImage.FileName, Seconds);
    }
}
