using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;

var target = args.FirstOrDefault() ?? "http://localhost:5080";
var requestCount = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 500;
var concurrency = args.Length > 2 && int.TryParse(args[2], out var workers) ? workers : 25;
var webhookSecret = Environment.GetEnvironmentVariable("FIRST10_LOAD_TELEGRAM_SECRET")
    ?? throw new InvalidOperationException("FIRST10_LOAD_TELEGRAM_SECRET is required.");
using var client = new HttpClient { BaseAddress = new Uri(target), Timeout = TimeSpan.FromSeconds(5) };
var latencies = new ConcurrentBag<double>();
var failures = 0;
await Parallel.ForEachAsync(
    Enumerable.Range(0, requestCount),
    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
    async (sequence, cancellationToken) =>
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/telegram")
            {
                Content = JsonContent.Create(new
                {
                    update_id = 9_000_000 + sequence,
                    message = new
                    {
                        message_id = 9_000_000 + sequence,
                        date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        chat = new { id = -9_000_000 - sequence },
                        voice = new
                        {
                            file_id = $"load-voice-{sequence}",
                            duration = 8,
                            mime_type = "audio/ogg"
                        }
                    }
                })
            };
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", webhookSecret);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref failures);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Interlocked.Increment(ref failures);
        }
        finally
        {
            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    });

var ordered = latencies.Order().ToArray();
var p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
Console.WriteLine($"signed_telegram_intake requests={requestCount} concurrency={concurrency} failures={failures} p95_ack_ms={p95:F1}");
return failures == 0 && p95 < 2_000 ? 0 : 1;
