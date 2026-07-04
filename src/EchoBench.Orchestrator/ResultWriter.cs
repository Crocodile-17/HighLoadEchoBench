using System.Globalization;
using System.Text;

namespace EchoBench.Orchestrator;

/// <summary>
/// Пишет results.csv — одна строка на ячейку матрицы. Колонки строго по ARCHITECTURE §9
/// (порядок и имена менять нельзя — на этот формат завязаны GNUplot-скрипты).
/// </summary>
public sealed class ResultWriter
{
    public const string Header =
        "model,gc_server,gc_concurrent,pooled,conns,dur_s," +
        "rps,p50_us,p95_us,p99_us,p999_us," +
        "gen0,gen1,gen2,gc_pause_ms,ws_mb,alloc_mbps,threads";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly string _path;

    public ResultWriter(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
            File.WriteAllText(_path, Header + "\n");
    }

    public void Append(RunSpec spec, LoadOutcome load, CountersSummary counters)
    {
        var fields = new[]
        {
            spec.Model.ToString(),
            spec.GcServer ? "1" : "0",
            spec.GcConcurrent ? "1" : "0",
            spec.Pooled ? "1" : "0",
            spec.Conns.ToString(Inv),
            load.DurationSeconds.ToString("F2", Inv),
            load.Rps.ToString("F0", Inv),
            load.P50.ToString("F1", Inv),
            load.P95.ToString("F1", Inv),
            load.P99.ToString("F1", Inv),
            load.P999.ToString("F1", Inv),
            counters.Gen0.ToString(Inv),
            counters.Gen1.ToString(Inv),
            counters.Gen2.ToString(Inv),
            counters.GcPauseMs.ToString("F2", Inv),
            counters.WsMb.ToString("F1", Inv),
            counters.AllocMbps.ToString("F3", Inv),
            counters.Threads.ToString(Inv),
        };

        File.AppendAllText(_path, string.Join(',', fields) + "\n", Encoding.UTF8);
    }
}
