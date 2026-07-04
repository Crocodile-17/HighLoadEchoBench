using System.Globalization;
using EchoBench.Orchestrator;

// Оркестратор: прогон полной матрицы {модель × режим GC × пулинг}. Для каждой ячейки —
// отдельный процесс Host с env GC, внешний сбор dotnet-counters, прогон LoadClient, строка
// в results.csv. См. ARCHITECTURE §7 «Orchestrator», §13 шаги 6–7.

var cliArgs = ParseArgs(args);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// --- Пути и инструменты -----------------------------------------------------
string repoRoot = FindRepoRoot();
string config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
string dotnet = ResolveDotnet();

string hostDll = ResolveBuiltDll(repoRoot, "EchoBench.Host", config);
string loadDll = ResolveBuiltDll(repoRoot, "EchoBench.LoadClient", config);
string countersExe = CountersCollector.ResolveExe();

string outPath = cliArgs.Out ?? Path.Combine(repoRoot, "bench", "results", "results.csv");
string countersDir = Path.Combine(repoRoot, "bench", "results", "counters");
Directory.CreateDirectory(countersDir);

// --- Матрица ----------------------------------------------------------------
var matrixOptions = new MatrixOptions
{
    BasePort = cliArgs.BasePort,
    ConnsSweep = cliArgs.ConnsSweep,
    DurationSeconds = cliArgs.Duration,
    WarmupSeconds = cliArgs.Warmup,
    PayloadBytes = cliArgs.Payload,
};

var specs = ExperimentMatrix.Build(matrixOptions);
if (cliArgs.MaxCells is int max && max < specs.Count)
    specs = specs.Take(max).ToList();

// CPU-изоляция (§10): сервер и нагрузчик на непересекающихся ядрах, иначе клиент ворует
// CPU у сервера и искажает RPS/латентность. По умолчанию делим ядра пополам; выключается
// флагом --no-pin или при отсутствии taskset / нехватке ядер.
var (serverPrefix, clientPrefix, pinNote) = ResolveCpuPinning(cliArgs);

Console.WriteLine($"== EchoBench Orchestrator ==");
Console.WriteLine($"repo:      {repoRoot}");
Console.WriteLine($"dotnet:    {dotnet}");
Console.WriteLine($"host dll:  {hostDll}");
Console.WriteLine($"load dll:  {loadDll}");
Console.WriteLine($"counters:  {countersExe}");
Console.WriteLine($"results:   {outPath}");
Console.WriteLine($"cells:     {specs.Count}  (conns-sweep=[{string.Join(',', matrixOptions.ConnsSweep)}], warmup={matrixOptions.WarmupSeconds}s, dur={matrixOptions.DurationSeconds}s, payload={matrixOptions.PayloadBytes}B)");
Console.WriteLine($"cpu pin:   {pinNote}");
Console.WriteLine();

// --- Компоненты -------------------------------------------------------------
var launcher = new ServerProcessLauncher(dotnet, hostDll, serverPrefix);
var collectorFactory = new CountersCollector(countersExe);
var loadRunner = new LoadClientRunner(dotnet, loadDll, clientPrefix);
var writer = new ResultWriter(outPath);

var readyTimeout = TimeSpan.FromSeconds(30);
var gracefulStop = TimeSpan.FromSeconds(5);
var drainPause = TimeSpan.FromMilliseconds(cliArgs.PauseMs);
const int counterSlackSeconds = 2;

int ok = 0, failed = 0;

for (int i = 0; i < specs.Count; i++)
{
    if (cts.IsCancellationRequested)
        break;

    var spec = specs[i];
    Console.WriteLine($"[{i + 1}/{specs.Count}] {spec.Label}  (gcServer={(spec.GcServer ? 1 : 0)}, gcConcurrent={(spec.GcConcurrent ? 1 : 0)}, pooled={(spec.Pooled ? 1 : 0)})");

    ServerProcessHandle? host = null;
    RunningProcess? collector = null;
    try
    {
        host = await launcher.LaunchAsync(spec, readyTimeout, cts.Token);
        Console.WriteLine($"    host pid={host.Pid} READY");

        int collectSeconds = spec.WarmupSeconds + spec.DurationSeconds + counterSlackSeconds;
        string collectorBase = Path.Combine(countersDir, spec.Label);
        collector = collectorFactory.Start(host.Pid, collectorBase, collectSeconds);

        var load = await loadRunner.RunAsync(spec, cts.Token);
        Console.WriteLine($"    load: rps={load.Rps:F0}  p50={load.P50:F1}us  p99={load.P99:F1}us  reqs={load.Requests}");

        // Дождаться, пока collector сам остановится по --duration и сбросит CSV.
        await collector.WaitForExitAsync(TimeSpan.FromSeconds(collectSeconds + 20), cts.Token);

        var counters = CountersParser.ParseFile(CountersCollector.CsvPath(collectorBase));
        Console.WriteLine($"    gc:   gen0={counters.Gen0} gen1={counters.Gen1} gen2={counters.Gen2}  pause={counters.GcPauseMs:F1}ms  ws={counters.WsMb:F1}MiB  alloc={counters.AllocMbps:F2}MiB/s  threads={counters.Threads}");

        writer.Append(spec, load, counters);
        ok++;
        Console.WriteLine($"    -> written to results.csv");
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        Console.WriteLine("    cancelled.");
        collector?.Dispose();
        if (host is not null) await SafeStop(host, gracefulStop, drainPause);
        break;
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"    FAILED: {ex.Message}");
    }
    finally
    {
        collector?.Dispose();
        if (host is not null)
            await SafeStop(host, gracefulStop, drainPause);
    }

    Console.WriteLine();
}

Console.WriteLine($"== matrix done: {ok} ok, {failed} failed -> {outPath} ==");

// --- Графики (шаг 8) --------------------------------------------------------
// Tidy-.dat из results.csv + gnuplot по каждому .gp. Не валим весь прогон, если графики
// не построились (нет gnuplot / пустой CSV) — данные уже записаны.
if (ok > 0 && !cliArgs.NoPlots && !cts.IsCancellationRequested)
{
    try
    {
        var plots = new PlotRunner(repoRoot);
        await plots.RunAsync(outPath, cts.Token);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"графики не построены: {ex.Message}");
    }
}

return ok > 0 ? 0 : 1;

// --- Локальные хелперы ------------------------------------------------------

static async Task SafeStop(ServerProcessHandle host, TimeSpan grace, TimeSpan drain)
{
    try { await host.StopAsync(grace, drain, CancellationToken.None); }
    catch { /* teardown best-effort: главное — не оставить осиротевший процесс */ }
    finally { host.Dispose(); }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "HighLoadEchoBench.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Не найден корень репозитория (HighLoadEchoBench.slnx) вверх от " + AppContext.BaseDirectory);
}

static string ResolveBuiltDll(string repoRoot, string project, string config)
{
    string Path1(string cfg) => Path.Combine(repoRoot, "src", project, "bin", cfg, "net10.0", project + ".dll");

    var primary = Path1(config);
    if (File.Exists(primary)) return primary;

    var other = Path1(config == "Release" ? "Debug" : "Release");
    if (File.Exists(other)) return other;

    throw new FileNotFoundException(
        $"Не найдена сборка {project}.dll. Сначала собери решение: `dotnet build -c Release`. Искал:\n  {primary}\n  {other}");
}

static string ResolveDotnet()
{
    var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    if (!string.IsNullOrEmpty(root))
    {
        var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (File.Exists(candidate)) return candidate;
    }
    return "dotnet"; // расчёт на PATH
}

static CliArgs ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var key = args[i][2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            map[key] = args[i + 1];
            i++;
        }
        else map[key] = "true";
    }

    int Int(string key, int dflt) =>
        map.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : dflt;

    bool Flag(string key) =>
        map.TryGetValue(key, out var v) && (v == "true" || v == "1");

    // Свип conns: --conns-sweep "64,128,..." (приоритет) либо одиночный --conns (back-compat).
    int[] connsSweep;
    if (map.TryGetValue("conns-sweep", out var cs))
        connsSweep = cs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    else if (map.TryGetValue("conns", out var c1))
        connsSweep = new[] { int.Parse(c1, CultureInfo.InvariantCulture) };
    else
        connsSweep = new[] { 64, 128, 256, 512, 1024 };

    return new CliArgs
    {
        MaxCells = map.TryGetValue("max-cells", out var mc) ? int.Parse(mc, CultureInfo.InvariantCulture) : null,
        BasePort = Int("base-port", 18000),
        ConnsSweep = connsSweep,
        Duration = Int("dur", 4),
        Warmup = Int("warmup", 1),
        Payload = Int("payload", 128),
        PauseMs = Int("pause-ms", 1500),
        Out = map.TryGetValue("out", out var o) ? o : null,
        ServerCores = map.TryGetValue("server-cores", out var sc) ? sc : null,
        ClientCores = map.TryGetValue("client-cores", out var cc) ? cc : null,
        NoPin = Flag("no-pin"),
        NoPlots = Flag("no-plots"),
    };
}

// Делит ядра между сервером и нагрузчиком. Явные --server-cores/--client-cores имеют приоритет;
// иначе ядра делятся пополам. Возвращает префиксы команды (taskset) и человекочитаемую заметку.
static (IReadOnlyList<string> server, IReadOnlyList<string> client, string note) ResolveCpuPinning(CliArgs args)
{
    var empty = Array.Empty<string>();

    if (args.NoPin)
        return (empty, empty, "выключено (--no-pin) — сервер и клиент делят CPU, цифры могут быть искажены (§10)");

    bool hasTaskset = FindOnPath("taskset") is not null;
    if (!hasTaskset)
        return (empty, empty, "taskset не найден — сервер и клиент делят CPU, цифры могут быть искажены (§10)");

    string serverCores, clientCores;
    if (args.ServerCores is not null && args.ClientCores is not null)
    {
        serverCores = args.ServerCores;
        clientCores = args.ClientCores;
    }
    else
    {
        int n = Environment.ProcessorCount;
        if (n < 4)
            return (empty, empty, $"всего {n} ядер — пиннинг пропущен, сервер и клиент делят CPU (§10)");
        int half = n / 2;
        serverCores = $"0-{half - 1}";
        clientCores = $"{half}-{n - 1}";
    }

    string[] Prefix(string cores) => new[] { "taskset", "-c", cores };
    return (Prefix(serverCores), Prefix(clientCores),
        $"server=taskset -c {serverCores}, client=taskset -c {clientCores}");
}

static string? FindOnPath(string exe)
{
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        var candidate = Path.Combine(dir, exe);
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

internal sealed record CliArgs
{
    public int? MaxCells { get; init; }
    public int BasePort { get; init; }
    public required IReadOnlyList<int> ConnsSweep { get; init; }
    public int Duration { get; init; }
    public int Warmup { get; init; }
    public int Payload { get; init; }
    public int PauseMs { get; init; }
    public string? Out { get; init; }
    public string? ServerCores { get; init; }
    public string? ClientCores { get; init; }
    public bool NoPin { get; init; }
    public bool NoPlots { get; init; }
}
