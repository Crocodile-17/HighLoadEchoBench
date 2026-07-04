using System.Globalization;

namespace EchoBench.LoadClient;

/// <summary>Параметры нагрузки из аргументов CLI.</summary>
public sealed record LoadOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 9000;
    public int Connections { get; init; } = 16;
    public int DurationSeconds { get; init; } = 5;
    public int WarmupSeconds { get; init; } = 1;
    public int PayloadBytes { get; init; } = 64;

    /// <summary>
    /// Целевой суммарный RPS по всем соединениям. 0 — чистый closed-loop «на максимум»
    /// (без пейсинга и без коррекции coordinated omission). &gt;0 — воркеры выдерживают
    /// заданный темп, а латентности пишутся с ожидаемым интервалом → честный хвост.
    /// См. <see cref="LatencyRecorder"/>.
    /// </summary>
    public int TargetRps { get; init; }

    public static LoadOptions Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = args[i + 1];
                i++;
            }
        }

        var o = new LoadOptions();
        if (map.TryGetValue("host", out var host)) o = o with { Host = host };
        if (map.TryGetValue("port", out var port)) o = o with { Port = ParseInt(port) };
        if (map.TryGetValue("conns", out var conns)) o = o with { Connections = ParseInt(conns) };
        if (map.TryGetValue("duration", out var dur)) o = o with { DurationSeconds = ParseInt(dur) };
        if (map.TryGetValue("warmup", out var warm)) o = o with { WarmupSeconds = ParseInt(warm) };
        if (map.TryGetValue("payload", out var pl)) o = o with { PayloadBytes = ParseInt(pl) };
        // Целевой RPS: --rps или --target-rps.
        if (map.TryGetValue("rps", out var rps)) o = o with { TargetRps = ParseInt(rps) };
        if (map.TryGetValue("target-rps", out var trps)) o = o with { TargetRps = ParseInt(trps) };
        return o;
    }

    private static int ParseInt(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
