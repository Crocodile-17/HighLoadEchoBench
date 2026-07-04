using EchoBench.LoadClient;
using Xunit;

namespace EchoBench.Tests;

/// <summary>
/// Самое ошибаемое место измерения — коррекция coordinated omission и слияние гистограмм.
/// Тесты фиксируют два инварианта: (1) без ожидаемого интервала перцентили = «как записали»;
/// (2) с ожидаемым интервалом одиночный затык до-вписывает проглоченные запросы и раздувает
/// хвост; (3) Merge суммирует счётчики воркеров.
/// </summary>
public sealed class LatencyRecorderTests
{
    [Fact]
    public void Without_expected_interval_percentile_matches_recorded_values()
    {
        var rec = new LatencyRecorder(); // коррекция выключена
        for (int v = 1; v <= 1000; v++)
            rec.Record(v);

        Assert.Equal(1000, rec.Count);
        // 3 значащих цифры HdrHistogram → допускаем ~0.5% уход.
        Assert.InRange(rec.PercentileMicros(0.50), 495, 505);
        Assert.InRange(rec.PercentileMicros(0.99), 985, 995);
    }

    [Fact]
    public void Expected_interval_backfills_a_long_stall_and_inflates_the_tail()
    {
        const long interval = 10; // мкс между запросами одного соединения

        // Одинаковые данные: сотня «нормальных» ответов по 10 мкс + один затык на 1 c.
        var raw = new LatencyRecorder(expectedIntervalMicros: 0);
        var corrected = new LatencyRecorder(expectedIntervalMicros: interval);
        for (int i = 0; i < 100; i++)
        {
            raw.Record(interval);
            corrected.Record(interval);
        }
        raw.Record(1_000_000);
        corrected.Record(1_000_000);

        // Без коррекции один выброс из ~101 теряется в хвосте → p99 крошечный.
        Assert.True(raw.PercentileMicros(0.99) < 1_000,
            $"raw p99={raw.PercentileMicros(0.99)} должен оставаться малым");

        // С коррекцией затык до-вписывает проглоченные запросы (10,20,…,1e6 мкс),
        // и тот же p99 взлетает на порядки.
        Assert.True(corrected.PercentileMicros(0.99) > raw.PercentileMicros(0.99) * 100,
            $"corrected p99={corrected.PercentileMicros(0.99)} должен быть кратно больше raw");

        // Пик в обоих случаях — сам затык (~1 c), коррекция его не теряет.
        Assert.InRange(corrected.PercentileMicros(1.0), 900_000, 1_100_000);
    }

    [Fact]
    public void Merge_sums_counts_of_worker_recorders()
    {
        var a = new LatencyRecorder();
        var b = new LatencyRecorder();
        for (int i = 0; i < 500; i++) { a.Record(100); b.Record(300); }

        var merged = LatencyRecorder.Merge(new[] { a, b });

        Assert.Equal(1000, merged.Count);
        Assert.InRange(merged.PercentileMicros(0.50), 95, 305);
        Assert.InRange(merged.PercentileMicros(0.99), 295, 305);
    }
}
