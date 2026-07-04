using System.Globalization;

namespace EchoBench.LoadClient;

/// <summary>Итог прогона нагрузки: пропускная способность и перцентили латентности.</summary>
public sealed record LoadResult
{
    public required int Connections { get; init; }
    public required int PayloadBytes { get; init; }

    /// <summary>Целевой суммарный RPS прогона (0 — closed-loop без CO-коррекции).</summary>
    public required int TargetRps { get; init; }

    public required long TotalRequests { get; init; }
    public required double DurationSeconds { get; init; }
    public required double Rps { get; init; }
    public required double P50Micros { get; init; }
    public required double P95Micros { get; init; }
    public required double P99Micros { get; init; }
    public required double P999Micros { get; init; }

    /// <summary>Заголовок CSV — оркестратор склеивает эти колонки со счётчиками сервера.</summary>
    public static string CsvHeader =>
        "conns,payload_bytes,target_rps,requests,dur_s,rps,p50_us,p95_us,p99_us,p999_us";

    public string ToCsvLine() => string.Join(',', new[]
    {
        Connections.ToString(CultureInfo.InvariantCulture),
        PayloadBytes.ToString(CultureInfo.InvariantCulture),
        TargetRps.ToString(CultureInfo.InvariantCulture),
        TotalRequests.ToString(CultureInfo.InvariantCulture),
        DurationSeconds.ToString("F2", CultureInfo.InvariantCulture),
        Rps.ToString("F0", CultureInfo.InvariantCulture),
        P50Micros.ToString("F1", CultureInfo.InvariantCulture),
        P95Micros.ToString("F1", CultureInfo.InvariantCulture),
        P99Micros.ToString("F1", CultureInfo.InvariantCulture),
        P999Micros.ToString("F1", CultureInfo.InvariantCulture),
    });

    public override string ToString() =>
        $"conns={Connections}  payload={PayloadBytes}B  " +
        $"target_rps={(TargetRps > 0 ? TargetRps.ToString(CultureInfo.InvariantCulture) : "max")}  " +
        $"requests={TotalRequests}  dur={DurationSeconds:F1}s  rps={Rps:F0}  " +
        $"p50={P50Micros:F1}us  p95={P95Micros:F1}us  p99={P99Micros:F1}us  p99.9={P999Micros:F1}us";
}
