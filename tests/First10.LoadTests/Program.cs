using System.Collections.Concurrent;
using System.Diagnostics;

var target = args.FirstOrDefault() ?? "http://localhost:5080";
var requestCount = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 500;
var concurrency = args.Length > 2 && int.TryParse(args[2], out var workers) ? workers : 25;
using var client = new HttpClient { BaseAddress = new Uri(target), Timeout = TimeSpan.FromSeconds(5) };
var latencies = new ConcurrentBag<double>();
var failures = 0;
await Parallel.ForEachAsync(
    Enumerable.Range(0, requestCount),
    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
    async (_, cancellationToken) =>
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync("/alive", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref failures);
            }
        }
        catch (HttpRequestException)
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
Console.WriteLine($"requests={requestCount} concurrency={concurrency} failures={failures} p95_ms={p95:F1}");
return failures == 0 && p95 < 2_000 ? 0 : 1;
