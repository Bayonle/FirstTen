using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length != 2 || args[0] is not ("validate" or "import"))
{
    Console.Error.WriteLine("Usage: First10.GuidanceAssets <validate|import> <manifest.json>");
    return 2;
}

var path = Path.GetFullPath(args[1]);
var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(path))
    ?? throw new InvalidDataException("Manifest is empty.");
var errors = new List<string>();
if (manifest.Locales is null
    || manifest.Locales.Select(x => x.Language).Order().SequenceEqual(
        Manifest.RequiredLanguages.Order()) is false)
{
    errors.Add("Exactly English, NigerianPidgin, and Yoruba locales are required.");
}

foreach (var locale in manifest.Locales ?? [])
{
    var textHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(locale.ExactText ?? "")));
    if (!string.Equals(textHash, locale.TextSha256, StringComparison.OrdinalIgnoreCase))
    {
        errors.Add($"{locale.Language}: text checksum mismatch.");
    }

    var audioPath = Path.Combine(Path.GetDirectoryName(path)!, locale.VoiceAsset ?? "");
    if (manifest.Enabled && !File.Exists(audioPath))
    {
        errors.Add($"{locale.Language}: enabled manifest requires a pinned voice file.");
    }
    else if (File.Exists(audioPath))
    {
        var audioHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(audioPath)));
        if (!string.Equals(audioHash, locale.VoiceSha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{locale.Language}: voice checksum mismatch.");
        }
    }
}

if (args[0] == "import" && manifest.Enabled)
{
    errors.Add("This non-production tool imports drafts only; clinical approval is required in the application.");
}

if (errors.Count > 0)
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

Console.WriteLine(args[0] == "import"
    ? $"Draft '{manifest.TemplateKey}' validated for operator import; it remains disabled."
    : $"Manifest '{manifest.TemplateKey}' is structurally valid and disabled={(!manifest.Enabled).ToString().ToLowerInvariant()}.");
return 0;

internal sealed record Manifest(
    [property: JsonPropertyName("templateKey")] string TemplateKey,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("locales")] Locale[]? Locales)
{
    public static readonly string[] RequiredLanguages = ["English", "NigerianPidgin", "Yoruba"];
}

internal sealed record Locale(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("exactText")] string? ExactText,
    [property: JsonPropertyName("textSha256")] string TextSha256,
    [property: JsonPropertyName("voiceAsset")] string? VoiceAsset,
    [property: JsonPropertyName("voiceSha256")] string VoiceSha256);
