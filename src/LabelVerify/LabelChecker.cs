using System.ClientModel;
using System.Diagnostics;

namespace LabelVerify;

/// <summary>The outcome of checking one label. Busy means Azure OpenAI hit its rate limit and the check can be retried.</summary>
public record CheckOutcome(List<FieldResult>? Results, string? Error, bool Busy, double Seconds)
{
    public Verdict? Overall => Results is null ? null : LabelCheck.Overall(Results);
}

/// <summary>Validates an uploaded label image, reads it, and compares it to the application. Shared by both pages.</summary>
public class LabelChecker(LabelReader reader, ILogger<LabelChecker> log)
{
    const long MaxBytes = 10 * 1024 * 1024;
    static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public async Task<CheckOutcome> CheckAsync(IFormFile? file, Application app, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Failed("Choose a label image first.");
        if (file.Length > MaxBytes)
            return Failed("That image is larger than 10 MB. Use a smaller photo.");
        if (!ImageTypes.Contains(file.ContentType))
            return Failed("That file type is not supported. Save the image as JPG or PNG and try again.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var timer = Stopwatch.StartNew();
        try
        {
            var label = await reader.ReadAsync(buffer.ToArray(), file.ContentType, ct);
            var results = LabelCheck.Compare(app, label);
            log.LogInformation("Checked {File} in {Seconds:0.00}s", file.FileName, timer.Elapsed.TotalSeconds);
            return new(results, null, false, timer.Elapsed.TotalSeconds);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            log.LogWarning("Rate limited while reading {File}", file.FileName);
            return new(null, "The label reader is busy. Wait a minute and try again.", true, timer.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Reading label {File} failed", file.FileName);
            return new(null, "The label could not be read. Try again, or check this label by eye.", false, timer.Elapsed.TotalSeconds);
        }
    }

    static CheckOutcome Failed(string error) => new(null, error, false, 0);
}
