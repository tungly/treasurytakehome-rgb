using System.ClientModel;
using System.Diagnostics;

namespace LabelVerify;

/// <summary>The outcome of checking one label. Busy means Azure OpenAI hit its rate limit and the check can be retried.</summary>
public record CheckOutcome(List<FieldResult>? Results, string? Error, bool Busy, double Seconds)
{
    public Verdict? Overall => Results is null ? null : LabelCheck.Overall(Results);
}

/// <summary>Validates the uploaded images of one label, reads them, and compares them to the application. Shared by both pages.</summary>
public class LabelChecker(LabelReader reader, ILogger<LabelChecker> log)
{
    const long MaxBytes = 10 * 1024 * 1024;
    public const int MaxImages = 4;
    static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public async Task<CheckOutcome> CheckAsync(IReadOnlyList<IFormFile> files, Application app, CancellationToken ct)
    {
        files = files.Where(f => f.Length > 0).ToList();
        if (files.Count == 0)
            return Failed("Choose a label image first.");
        if (files.Count > MaxImages)
            return Failed($"Choose up to {MaxImages} images for one label, such as front, back, and neck.");
        foreach (var file in files)
        {
            if (file.Length > MaxBytes)
                return Failed($"{file.FileName} is larger than 10 MB. Use a smaller photo.");
            if (!ImageTypes.Contains(file.ContentType))
                return Failed($"{file.FileName} is not a supported file type. Save it as JPG or PNG and try again.");
        }

        var images = new List<LabelImage>();
        foreach (var file in files)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            images.Add(new(buffer.ToArray(), file.ContentType));
        }
        string names = string.Join(", ", files.Select(f => f.FileName));

        var timer = Stopwatch.StartNew();
        try
        {
            var label = await reader.ReadAsync(images, ct);
            var results = LabelCheck.Compare(app, label);
            log.LogInformation("Checked {Files} in {Seconds:0.00}s", names, timer.Elapsed.TotalSeconds);
            return new(results, null, false, timer.Elapsed.TotalSeconds);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            log.LogWarning("Rate limited while reading {Files}", names);
            return new(null, "The label reader is busy. Wait a minute and try again.", true, timer.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Reading label {Files} failed", names);
            return new(null, "The label could not be read. Try again, or check this label by eye.", false, timer.Elapsed.TotalSeconds);
        }
    }

    static CheckOutcome Failed(string error) => new(null, error, false, 0);
}
