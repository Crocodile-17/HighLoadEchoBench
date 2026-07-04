using System.Globalization;

namespace EchoBench.Orchestrator;

/// <summary>
/// Шаг 8: results.csv → графики. gnuplot слабо пивотит плоский CSV, поэтому стратегия —
/// СНАЧАЛА развернуть данные в несколько «tidy» .dat (уже отфильтрованных и разложенных по
/// колонкам-сериям), ПОТОМ позвать gnuplot на простых .gp, читающих чистый .dat.
///
/// Все .gp запускаются с рабочим каталогом = корень репозитория; пути внутри них — от корня
/// (та же конвенция, что для standalone-запуска `gnuplot bench/plots/rps.gp` из корня).
/// Зависимостей на Host/LoadClient нет — PlotRunner живёт в Orchestrator (ссылается только
/// на Abstractions, как и весь проект).
/// </summary>
public sealed class PlotRunner
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly string _repoRoot;
    private readonly string _gnuplot;
    private readonly string _datDir;     // куда писать .dat (bench/results)
    private readonly string _imgDir;     // куда gnuplot пишет PNG (bench/results/img)

    public PlotRunner(string repoRoot, string? gnuplotExe = null)
    {
        _repoRoot = repoRoot;
        _gnuplot = gnuplotExe ?? ResolveGnuplot();
        _datDir = Path.Combine(repoRoot, "bench", "results");
        _imgDir = Path.Combine(_datDir, "img");
    }

    public async Task RunAsync(string resultsCsvPath, CancellationToken ct)
    {
        Console.WriteLine("== графики ==");
        if (!File.Exists(resultsCsvPath))
            throw new FileNotFoundException($"results.csv не найден: {resultsCsvPath}");

        var rows = ReadCsv(resultsCsvPath);
        if (rows.Count == 0)
            throw new InvalidOperationException("results.csv пуст — нечего строить.");

        Directory.CreateDirectory(_imgDir);

        // Развернуть плоский CSV в 4 tidy .dat.
        int refConns = rows.Select(r => r.Int("conns")).Max();   // сравнение на максимальной нагрузке
        WriteMemoryDat(rows);                 // флагман: ws_mb от conns, серия на (модель×GC)
        WriteRpsDat(rows, refConns);          // RPS: X=режим GC, кластеры=модели
        WriteLatencyDat(rows, refConns);      // перцентили по моделям
        WriteAllocDat(rows, refConns);        // alloc_mbps: пул против без пула
        Console.WriteLine($"   tidy .dat записаны в {Rel(_datDir)} (refConns={refConns})");

        // gnuplot по каждому .gp.
        foreach (var gp in new[] { "memory.gp", "rps.gp", "latency.gp", "alloc.gp" })
            await RunGnuplotAsync(gp, ct);

        Console.WriteLine($"   PNG: {Rel(_imgDir)}/{{memory,rps,latency,alloc}}.png");
    }

    // --- Развёртка .dat ---------------------------------------------------------

    // Порядок серий флагмана — ДОЛЖЕН совпадать с шапкой колонок в memory.gp.
    private static readonly (string model, int gcS, int gcC, string label)[] MemSeries =
    {
        ("ThreadPerConnection", 0, 0, "TPC WS/blk"),
        ("ThreadPerConnection", 0, 1, "TPC WS/bg"),
        ("ThreadPerConnection", 1, 0, "TPC Srv/blk"),
        ("ThreadPerConnection", 1, 1, "TPC Srv/bg"),
        ("Async",               0, 0, "Async WS/blk"),
        ("Async",               0, 1, "Async WS/bg"),
        ("Async",               1, 0, "Async Srv/blk"),
        ("Async",               1, 1, "Async Srv/bg"),
    };

    private static readonly (int gcS, int gcC, string label)[] GcModes =
    {
        (0, 0, "WS/blk"), (0, 1, "WS/bg"), (1, 0, "Srv/blk"), (1, 1, "Srv/bg"),
    };

    /// <summary>Флагман: X=conns, по колонке ws_mb на каждую (модель×режим GC); пулинг фиксирован (pooled=1).</summary>
    private void WriteMemoryDat(IReadOnlyList<Row> rows)
    {
        var index = rows.ToLookup(r => (r.Str("model"), r.Int("gc_server"), r.Int("gc_concurrent"), r.Int("pooled"), r.Int("conns")));
        var connsVals = rows.Select(r => r.Int("conns")).Distinct().OrderBy(c => c).ToArray();

        using var w = new StreamWriter(Path.Combine(_datDir, "plot_memory.dat"));
        w.WriteLine("# Флагман: working set (MB) от числа соединений. Пулинг фиксирован pooled=1.");
        w.Write("# conns");
        foreach (var s in MemSeries) w.Write($"  \"{s.label}\"");
        w.WriteLine();
        foreach (var conns in connsVals)
        {
            w.Write(conns.ToString(Inv));
            foreach (var s in MemSeries)
            {
                var row = index[(s.model, s.gcS, s.gcC, 1, conns)].FirstOrDefault();
                w.Write("  " + (row is null ? "NaN" : row.Raw("ws_mb")));
            }
            w.WriteLine();
        }
    }

    /// <summary>RPS на refConns: строка=режим GC (xtic), колонки=модели (кластеры). Пулинг pooled=1.</summary>
    private void WriteRpsDat(IReadOnlyList<Row> rows, int refConns)
    {
        using var w = new StreamWriter(Path.Combine(_datDir, "plot_rps.dat"));
        w.WriteLine($"# RPS по режимам GC (conns={refConns}, pooled=1). col1=режим, col2=TPC, col3=Async.");
        w.WriteLine("# gc_mode  TPC  Async");
        foreach (var (gcS, gcC, label) in GcModes)
        {
            double tpc = Cell(rows, "ThreadPerConnection", gcS, gcC, 1, refConns, "rps");
            double asy = Cell(rows, "Async", gcS, gcC, 1, refConns, "rps");
            w.WriteLine($"\"{label}\"  {tpc.ToString("F0", Inv)}  {asy.ToString("F0", Inv)}");
        }
    }

    /// <summary>Перцентили на refConns при репрезентативном GC (Server+background, pooled=1): X=перцентиль, кластеры=модели.</summary>
    private void WriteLatencyDat(IReadOnlyList<Row> rows, int refConns)
    {
        const int gcS = 1, gcC = 1, pooled = 1;   // «продакшн-похожий» режим
        var pcts = new[] { ("p50", "p50_us"), ("p95", "p95_us"), ("p99", "p99_us"), ("p999", "p999_us") };

        using var w = new StreamWriter(Path.Combine(_datDir, "plot_latency.dat"));
        w.WriteLine($"# Латентность (µs) по перцентилям (conns={refConns}, GC=Server+bg, pooled=1). col2=TPC, col3=Async.");
        w.WriteLine("# pct  TPC  Async");
        foreach (var (label, col) in pcts)
        {
            double tpc = Cell(rows, "ThreadPerConnection", gcS, gcC, pooled, refConns, col);
            double asy = Cell(rows, "Async", gcS, gcC, pooled, refConns, col);
            w.WriteLine($"\"{label}\"  {tpc.ToString("F1", Inv)}  {asy.ToString("F1", Inv)}");
        }
    }

    /// <summary>Allocation rate на refConns при репрезентативном GC (Server+background): X=модель, кластеры=без пула/пул.</summary>
    private void WriteAllocDat(IReadOnlyList<Row> rows, int refConns)
    {
        const int gcS = 1, gcC = 1;
        using var w = new StreamWriter(Path.Combine(_datDir, "plot_alloc.dat"));
        w.WriteLine($"# Allocation rate (MB/s) пул против без пула (conns={refConns}, GC=Server+bg). col2=new[], col3=ArrayPool.");
        w.WriteLine("# model  nopool  pooled");
        foreach (var (model, label) in new[] { ("ThreadPerConnection", "TPC"), ("Async", "Async") })
        {
            double nopool = Cell(rows, model, gcS, gcC, 0, refConns, "alloc_mbps");
            double pooled = Cell(rows, model, gcS, gcC, 1, refConns, "alloc_mbps");
            w.WriteLine($"\"{label}\"  {nopool.ToString("F3", Inv)}  {pooled.ToString("F3", Inv)}");
        }
    }

    private static double Cell(IReadOnlyList<Row> rows, string model, int gcS, int gcC, int pooled, int conns, string col)
    {
        var row = rows.FirstOrDefault(r =>
            r.Str("model") == model && r.Int("gc_server") == gcS && r.Int("gc_concurrent") == gcC &&
            r.Int("pooled") == pooled && r.Int("conns") == conns);
        if (row is null)
        {
            Console.Error.WriteLine($"   ! нет данных: {model} gcS={gcS} gcC={gcC} pooled={pooled} conns={conns} -> {col}=NaN");
            return double.NaN;
        }
        return row.Num(col);
    }

    // --- Вызов gnuplot ----------------------------------------------------------

    private async Task RunGnuplotAsync(string script, CancellationToken ct)
    {
        string scriptPath = Path.Combine("bench", "plots", script);   // относительно CWD=repoRoot
        Console.WriteLine($"   gnuplot {scriptPath}  (cwd={Rel(_repoRoot)})");

        var result = await ProcessRunner.RunAsync(
            new ProcessSpec
            {
                FileName = _gnuplot,
                Arguments = new[] { scriptPath },
                WorkingDirectory = _repoRoot,
            },
            TimeSpan.FromSeconds(60),
            ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.StdErr))
            foreach (var line in result.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Console.Error.WriteLine($"     gnuplot[{script}]: {line}");

        if (result.TimedOut || result.ExitCode != 0)
            throw new InvalidOperationException($"gnuplot завершился с кодом {result.ExitCode} на {script} (timeout={result.TimedOut}).");
    }

    private static string ResolveGnuplot()
    {
        var env = Environment.GetEnvironmentVariable("GNUPLOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "gnuplot");
            if (File.Exists(candidate)) return candidate;
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "gnuplot");
        if (File.Exists(local)) return local;

        throw new FileNotFoundException(
            "gnuplot не найден (PATH, $GNUPLOT, ~/.local/bin). Поставь: sudo apt-get install gnuplot, либо см. scripts/setup.sh.");
    }

    // --- Разбор CSV -------------------------------------------------------------

    private static IReadOnlyList<Row> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return Array.Empty<Row>();

        var header = lines[0].Split(',');
        var colIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++) colIndex[header[i].Trim()] = i;

        var rows = new List<Row>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(new Row(lines[i].Split(','), colIndex));
        }
        return rows;
    }

    private string Rel(string abs) => Path.GetRelativePath(_repoRoot, abs).Replace('\\', '/');

    /// <summary>Строка results.csv с доступом по имени колонки — устойчиво к перестановке колонок.</summary>
    private sealed class Row
    {
        private readonly string[] _values;
        private readonly IReadOnlyDictionary<string, int> _cols;
        public Row(string[] values, IReadOnlyDictionary<string, int> cols) { _values = values; _cols = cols; }

        public string Raw(string col) => _cols.TryGetValue(col, out var i) && i < _values.Length ? _values[i].Trim() : "";
        public string Str(string col) => Raw(col);
        public int Int(string col) => int.Parse(Raw(col), CultureInfo.InvariantCulture);
        public double Num(string col) => double.Parse(Raw(col), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
