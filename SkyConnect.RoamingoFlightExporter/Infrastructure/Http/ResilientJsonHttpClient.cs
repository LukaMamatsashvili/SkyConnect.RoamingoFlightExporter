using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyConnect.RoamingoFlightExporter.Configuration;

namespace SkyConnect.RoamingoFlightExporter.Infrastructure.Http;

public sealed class ResilientJsonHttpClient(
    HttpClient httpClient,
    IOptions<ExporterOptions> options,
    ILogger<ResilientJsonHttpClient> logger) : IJsonHttpClient
{
    private readonly Queue<DateTimeOffset> _requestStartTimes = new();
    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);
    private readonly ExporterOptions _options = options.Value;

    public async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await WaitForRateLimitSlotAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            try
            {
                using var response = await SendWithTimeoutAsync(request, timeoutCts, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(timeoutCts.Token)
                        .WaitAsync(GetRequestTimeout(), cancellationToken);

                    return await JsonDocument.ParseAsync(
                        stream,
                        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip },
                        timeoutCts.Token)
                        .WaitAsync(GetRequestTimeout(), cancellationToken);
                }

                var body = await response.Content
                    .ReadAsStringAsync(timeoutCts.Token)
                    .WaitAsync(GetRequestTimeout(), cancellationToken);
                if (IsQuotaExceeded(response.StatusCode, body))
                {
                    throw new FlightLabsQuotaExceededException($"FlightLabs quota is exhausted. Body: {body}");
                }

                if (IsNoData(response.StatusCode, body))
                {
                    throw new FlightLabsNoDataException($"FlightLabs has no data for this request. Body: {body}");
                }

                if (!ShouldRetry(response.StatusCode, body) || attempt >= _options.FlightLabs.MaxRetryAttempts)
                {
                    throw new InvalidOperationException($"FlightLabs returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
                }

                await DelayBeforeRetryAsync(response, attempt, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < _options.FlightLabs.MaxRetryAttempts)
            {
                await DelayBeforeRetryAsync(null, attempt, cancellationToken);
            }
            catch (TimeoutException) when (attempt < _options.FlightLabs.MaxRetryAttempts)
            {
                await DelayBeforeRetryAsync(null, attempt, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt >= _options.FlightLabs.MaxRetryAttempts)
            {
                throw new TimeoutException($"FlightLabs request timed out after {_options.RequestTimeoutSeconds} seconds and {_options.FlightLabs.MaxRetryAttempts} attempts.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _options.FlightLabs.MaxRetryAttempts)
            {
                await DelayBeforeRetryAsync(null, attempt, cancellationToken);
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request,
        CancellationTokenSource timeoutCts,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .WaitAsync(GetRequestTimeout(), cancellationToken);
        }
        catch (TimeoutException)
        {
            timeoutCts.Cancel();
            throw;
        }
    }

    private TimeSpan GetRequestTimeout()
        => TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

    private async Task WaitForRateLimitSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan? delay = null;
            await _rateLimitLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var window = TimeSpan.FromSeconds(_options.RateLimitWindowSeconds);

                while (_requestStartTimes.Count > 0 && now - _requestStartTimes.Peek() >= window)
                {
                    _requestStartTimes.Dequeue();
                }

                if (_requestStartTimes.Count < _options.RequestsPerRateLimitWindow)
                {
                    _requestStartTimes.Enqueue(now);
                    return;
                }

                delay = window - (now - _requestStartTimes.Peek()) + TimeSpan.FromMilliseconds(100);
            }
            finally
            {
                _rateLimitLock.Release();
            }

            await Task.Delay(delay.Value, cancellationToken);
        }
    }

    private async Task DelayBeforeRetryAsync(HttpResponseMessage? response, int attempt, CancellationToken cancellationToken)
    {
        var delay = GetRetryDelay(response, attempt);
        logger.LogWarning("Transient FlightLabs request issue. Retrying in {DelaySeconds:n1}s, attempt {Attempt}/{MaxAttempts}.",
            delay.TotalSeconds, attempt, _options.FlightLabs.MaxRetryAttempts);
        await Task.Delay(delay, cancellationToken);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, string body)
    {
        if (IsQuotaExceeded(statusCode, body))
        {
            return false;
        }

        return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    }

    private static bool IsQuotaExceeded(HttpStatusCode statusCode, string body)
        => statusCode == HttpStatusCode.TooManyRequests
           && !body.Contains("Rate Limit Exceeded", StringComparison.OrdinalIgnoreCase)
           && !body.Contains("10 reqs/10 sec", StringComparison.OrdinalIgnoreCase)
           && (body.Contains("trial API calls have exceeded", StringComparison.OrdinalIgnoreCase)
               || body.Contains("more than 20 requests per month", StringComparison.OrdinalIgnoreCase)
               || body.Contains("monthly", StringComparison.OrdinalIgnoreCase)
               || body.Contains("End Trial", StringComparison.OrdinalIgnoreCase));

    private static bool IsNoData(HttpStatusCode statusCode, string body)
        => statusCode == HttpStatusCode.NotFound
           && body.Contains("No data was found", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response?.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        var jitterMs = Random.Shared.Next(100, 750);
        var seconds = Math.Min(30, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMs);
    }
}
