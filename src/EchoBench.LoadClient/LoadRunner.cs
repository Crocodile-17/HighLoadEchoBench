using System.Diagnostics;

namespace EchoBench.LoadClient;

/// <summary>
/// Поднимает N воркеров, выдерживает прогрев и окно измерения, сливает гистограммы
/// и считает RPS + перцентили.
/// </summary>
public sealed class LoadRunner
{
    private readonly LoadOptions _options;

    public LoadRunner(LoadOptions options) => _options = options;

    public async Task<LoadResult> RunAsync(CancellationToken ct)
    {
        int conns = Math.Max(1, _options.Connections);

        // Целевой RPS делится поровну между соединениями. Из доли на соединение получаем
        // и ожидаемый интервал для CO-коррекции (мкс), и шаг пейсинга (Stopwatch-тики).
        long expectedIntervalMicros = 0;
        long intervalTicks = 0;
        if (_options.TargetRps > 0)
        {
            double perConnRps = (double)_options.TargetRps / conns;
            expectedIntervalMicros = (long)Math.Round(1_000_000.0 / perConnRps);
            intervalTicks = (long)Math.Round(Stopwatch.Frequency / perConnRps);
        }

        var workers = new ConnectionWorker[conns];
        for (int i = 0; i < conns; i++)
            workers[i] = new ConnectionWorker(_options, expectedIntervalMicros, intervalTicks);

        long measureFrom = Stopwatch.GetTimestamp()
                           + (long)(_options.WarmupSeconds * (double)Stopwatch.Frequency);
        int totalSeconds = _options.WarmupSeconds + _options.DurationSeconds;

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(totalSeconds));

        var tasks = new Task[conns];
        for (int i = 0; i < conns; i++)
            tasks[i] = workers[i].RunAsync(measureFrom, runCts.Token);

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение по таймеру окна измерения.
        }

        long requests = workers.Sum(w => w.Requests);
        var merged = LatencyRecorder.Merge(workers.Select(w => w.Latency));
        double durationS = _options.DurationSeconds;

        return new LoadResult
        {
            Connections = conns,
            PayloadBytes = _options.PayloadBytes,
            TargetRps = _options.TargetRps,
            TotalRequests = requests,
            DurationSeconds = durationS,
            Rps = durationS > 0 ? requests / durationS : 0,
            P50Micros = merged.PercentileMicros(0.50),
            P95Micros = merged.PercentileMicros(0.95),
            P99Micros = merged.PercentileMicros(0.99),
            P999Micros = merged.PercentileMicros(0.999),
        };
    }
}
