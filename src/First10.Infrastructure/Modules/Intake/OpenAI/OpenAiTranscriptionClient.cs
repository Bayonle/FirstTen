using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using First10.Modules.Intake.Triage;
using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.Intake.OpenAI;

public sealed class OpenAiTranscriptionClient(
    HttpClient httpClient,
    IConfiguration configuration) : IReporterAudioTranscriber
{
    public const string Model = "gpt-4o-transcribe";
    private const int MaximumBytes = 25 * 1024 * 1024;

    public async Task<ReporterTranscript> TranscribeAsync(
        ReadOnlyMemory<byte> safeAudio,
        string contentType,
        string evidenceReference,
        DateTimeOffset sourceOccurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (safeAudio.IsEmpty || safeAudio.Length > MaximumBytes)
        {
            throw new OpenAiProviderException("transcription_audio_size_rejected");
        }

        var extension = ContentTypeToExtension(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadApiKey());
        using var form = new MultipartFormDataContent();
        var file = new ReadOnlyMemoryContent(safeAudio);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", $"safe-audio.{extension}");
        form.Add(new StringContent(Model), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("true"), "include[]=logprobs");
        request.Content = form;

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new OpenAiProviderException($"transcription_http_{(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync(
                OpenAiJsonContext.Default.TranscriptionWireResponse,
                cancellationToken) ?? throw new OpenAiProviderException("transcription_empty_response");
            if (string.IsNullOrWhiteSpace(payload.Text))
            {
                throw new OpenAiProviderException("transcription_empty_text");
            }

            var confidence = CalculateMeanConfidence(payload.Logprobs);
            return new ReporterTranscript(
                payload.Text.Trim(),
                evidenceReference,
                sourceOccurredAtUtc,
                confidence,
                ToQuality(confidence),
                Model,
                payload.Usage?.InputTokens ?? 0,
                payload.Usage?.OutputTokens ?? 0);
        }
        catch (OpenAiProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpenAiProviderException("transcription_transport_failure", exception);
        }
    }

    private string ReadApiKey() => configuration["OpenAI:ApiKey"]
        ?? throw new OpenAiProviderException("openai_not_configured");

    private static string ContentTypeToExtension(string contentType) => contentType switch
    {
        "audio/mpeg" => "mp3",
        "audio/mp4" => "mp4",
        "audio/mpga" => "mpga",
        "audio/x-m4a" => "m4a",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/webm" => "webm",
        _ => throw new OpenAiProviderException("transcription_content_type_rejected")
    };

    private static double? CalculateMeanConfidence(IReadOnlyList<TranscriptionLogprob>? logprobs)
    {
        if (logprobs is null || logprobs.Count == 0)
        {
            return null;
        }

        return logprobs.Average(x => Math.Clamp(Math.Exp(x.Logprob), 0, 1));
    }

    private static TranscriptionQuality ToQuality(double? confidence) => confidence switch
    {
        >= 0.85 => TranscriptionQuality.High,
        >= 0.60 => TranscriptionQuality.Medium,
        not null => TranscriptionQuality.Low,
        _ => TranscriptionQuality.Unknown
    };
}

public sealed record TranscriptionWireResponse(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("logprobs")] IReadOnlyList<TranscriptionLogprob>? Logprobs,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

public sealed record OpenAiUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public sealed record TranscriptionLogprob(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("logprob")] double Logprob);

[JsonSerializable(typeof(TranscriptionWireResponse))]
internal sealed partial class OpenAiJsonContext : JsonSerializerContext;
